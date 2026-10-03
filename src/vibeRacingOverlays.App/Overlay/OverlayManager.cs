using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Widgets;
using vibeRacingOverlays.Data.Engine;

namespace vibeRacingOverlays.App.Overlay
{
    /// <summary>Owns the overlay windows: creation, visibility, edit mode, hotkeys and saving.</summary>
    public sealed class OverlayManager : IDisposable
    {
        const int HotkeyEdit = 1, HotkeyToggle = 2, HotkeyNextLayout = 3;

        readonly AppSettings settings;
        readonly TelemetryService telemetry;
        readonly Dictionary<string, OverlayWindow> windows = new Dictionary<string, OverlayWindow>();
        readonly DispatcherTimer timer;
        DateTime nextTopmost = DateTime.MinValue;
        DateTime saveAt = DateTime.MaxValue;
        HwndSource hotkeySource;

        public bool EditMode { get; private set; }
        public event Action StateChanged;
        /// <summary>A widget was moved, resized or changed (the position panel re-reads its values).</summary>
        public event Action<WidgetSettings> WidgetChanged;
        /// <summary>The set of widgets changed (added, removed or another layout activated).</summary>
        public event Action WidgetsChanged;
        /// <summary>A widget was clicked on screen in edit mode.</summary>
        public event Action<WidgetSettings> WidgetGrabbed;

        /// <summary>Screen rectangles of the other visible widgets (what a dragged widget can snap to).</summary>
        IEnumerable<Native.RECT> SnapTargets(OverlayWindow self)
        {
            foreach (var w in windows.Values)
            {
                Native.RECT r;
                if (w != self && w.IsVisible && Native.GetWindowRect(new WindowInteropHelper(w).Handle, out r)) yield return r;
            }
        }

        public OverlayManager(AppSettings settings, TelemetryService telemetry)
        {
            this.settings = settings;
            this.telemetry = telemetry;
            timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (s, e) => Tick();
        }

        public void Start()
        {
            Sync();
            timer.Start();
        }

        /// <summary>Creates/removes windows so they match the widget list.</summary>
        public void Sync()
        {
            var ids = new HashSet<string>(settings.Widgets.Select(w => w.Id));
            foreach (var id in windows.Keys.Where(k => !ids.Contains(k)).ToList())
            {
                windows[id].Close();
                windows.Remove(id);
            }
            foreach (var ws in settings.Widgets)
            {
                if (windows.ContainsKey(ws.Id)) continue;
                var win = new OverlayWindow(Widget.Create(ws), settings.Font);
                win.Moved += w => { ScheduleSave(); if (WidgetChanged != null) WidgetChanged(w.Widget.Settings); };
                win.Grabbed += w => { if (WidgetGrabbed != null) WidgetGrabbed(w.Widget.Settings); };
                win.Snapping = settings;
                win.SnapTargets = SnapTargets;
                win.SetEditMode(EditMode);
                windows[ws.Id] = win;
            }
            if (WidgetsChanged != null) WidgetsChanged();
        }

        /// <summary>The settings objects of these widgets were replaced (undo): their windows are made again.</summary>
        public void Reload(IEnumerable<string> ids)
        {
            foreach (var id in ids)
            {
                OverlayWindow w;
                if (windows.TryGetValue(id, out w)) { w.Close(); windows.Remove(id); }
            }
            Sync();
        }

        public void Invalidate(WidgetSettings ws)
        {
            OverlayWindow w;
            if (windows.TryGetValue(ws.Id, out w)) w.Invalidate();
            ScheduleSave();
            if (WidgetChanged != null) WidgetChanged(ws);
        }

        /// <summary>Moves a widget's window to its anchor, screen and offsets (after changing them in the position panel).</summary>
        public void ApplyPosition(WidgetSettings ws)
        {
            OverlayWindow w;
            if (windows.TryGetValue(ws.Id, out w)) w.ApplyPosition();
            ScheduleSave();
        }

        public void SetFont(string font)
        {
            settings.Font = font;
            Rendering.TextMeasure.SetFont(font);
            foreach (var w in windows.Values) w.SetFont(font);
            ScheduleSave();
        }

        public void SetEditMode(bool on)
        {
            EditMode = on;
            foreach (var w in windows.Values) w.SetEditMode(on);
            if (!on) SaveNow();
            if (StateChanged != null) StateChanged();
        }

        public void ToggleOverlays()
        {
            settings.OverlaysVisible = !settings.OverlaysVisible;
            ScheduleSave();
            if (StateChanged != null) StateChanged();
        }

        /// <summary>Activates another layout: its widgets replace the current windows.</summary>
        public void SwitchLayout(string layoutId)
        {
            if (!settings.Layouts.Any(l => l.Id == layoutId) || settings.ActiveLayoutId == layoutId) return;
            settings.ActiveLayoutId = layoutId;
            Sync();
            ScheduleSave();
            if (StateChanged != null) StateChanged();
        }

        public void NextLayout()
        {
            if (settings.Layouts.Count < 2) return;
            int i = settings.Layouts.IndexOf(settings.ActiveLayout);
            SwitchLayout(settings.Layouts[(i + 1) % settings.Layouts.Count].Id);
        }

        public void ScheduleSave() { saveAt = DateTime.UtcNow.AddSeconds(1); }

        public void SaveNow()
        {
            saveAt = DateTime.MaxValue;
            try { settings.Save(); } catch { }   // logged and shown in the status bar by Save itself
        }

        // Windows screens the widgets must never be lifted over: the Win+Shift+S screenshot screen (its frozen
        // image already shows the widgets, live widgets on top of it looked like duplicates), Start and search
        static readonly HashSet<string> SystemScreens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ScreenClippingHost", "SnippingTool", "ScreenSketch", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "ShellHost" };
        static readonly HashSet<string> Sims = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "iRacingSim64DX11", "iRacingSim64" };
        readonly Dictionary<uint, string> processNames = new Dictionary<uint, string>();

        /// <summary>
        /// Whether widgets may be put back on top now: not while a Windows screen like the screenshot tool is in front,
        /// and not over another always-on-top app you're using (that one should stay in front while you use it).
        /// Always while iRacing or this app is in front.
        /// </summary>
        bool MayGoOnTop()
        {
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return true;
            uint pid;
            Native.GetWindowThreadProcessId(fg, out pid);
            if (pid == (uint)Environment.ProcessId) return true;
            string name;
            if (!processNames.TryGetValue(pid, out name))
            {
                try { using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) name = p.ProcessName; }
                catch (Exception) { name = ""; }
                if (processNames.Count > 200) processNames.Clear();
                processNames[pid] = name;
            }
            if (Sims.Contains(name)) return true;
            if (SystemScreens.Contains(name)) return false;
            return (Native.GetWindowLong(fg, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0;
        }

        void Tick()
        {
            var snap = telemetry.Latest;
            var now = DateTime.UtcNow;
            bool reassert = now >= nextTopmost;
            if (reassert) { nextTopmost = now.AddSeconds(2); reassert = MayGoOnTop(); }

            foreach (var w in windows.Values)
            {
                var ws = w.Widget.Settings;
                bool show = settings.OverlaysVisible && ws.Enabled && (EditMode || w.Widget.ShouldShow(snap));
                if (show)
                {
                    if (!w.IsVisible) { w.Show(); w.ApplyPosition(); w.Invalidate(); }
                    w.Tick(snap, now);
                    if (reassert) w.ReassertTopmost();
                }
                else if (w.IsVisible) w.Hide();
            }

            if (now >= saveAt) SaveNow();
        }

        // ---------------------------------------------------------------- global hotkeys

        public void RegisterHotkeys(IntPtr hwnd)
        {
            hotkeySource = HwndSource.FromHwnd(hwnd);
            hotkeySource.AddHook(HotkeyHook);
            Register(hwnd, HotkeyEdit, settings.HotkeyEditMode);
            Register(hwnd, HotkeyToggle, settings.HotkeyToggleOverlays);
            Register(hwnd, HotkeyNextLayout, settings.HotkeyNextLayout);
        }

        static void Register(IntPtr hwnd, int id, string combo)
        {
            uint mods, vk;
            if (!ParseHotkey(combo, out mods, out vk)) return;
            Native.RegisterHotKey(hwnd, id, mods | Native.MOD_NOREPEAT, vk);
        }

        IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != Native.WM_HOTKEY) return IntPtr.Zero;
            int id = wParam.ToInt32();
            if (id == HotkeyEdit) { SetEditMode(!EditMode); handled = true; }
            else if (id == HotkeyToggle) { ToggleOverlays(); handled = true; }
            else if (id == HotkeyNextLayout) { NextLayout(); handled = true; }
            return IntPtr.Zero;
        }

        public static bool ParseHotkey(string combo, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(combo)) return false;
            foreach (var raw in combo.Split('+'))
            {
                string p = raw.Trim();
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "win": mods |= Native.MOD_WIN; break;
                    default:
                        Key key;
                        if (!Enum.TryParse(p, true, out key)) return false;
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                        break;
                }
            }
            return vk != 0;
        }

        public void Dispose()
        {
            timer.Stop();
            if (hotkeySource != null)
            {
                Native.UnregisterHotKey(hotkeySource.Handle, HotkeyEdit);
                Native.UnregisterHotKey(hotkeySource.Handle, HotkeyToggle);
                Native.UnregisterHotKey(hotkeySource.Handle, HotkeyNextLayout);
            }
            foreach (var w in windows.Values) w.Close();
            windows.Clear();
            SaveNow();
        }
    }
}
