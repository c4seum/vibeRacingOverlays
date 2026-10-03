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

        public void PreviousLayout()
        {
            if (settings.Layouts.Count < 2) return;
            int i = settings.Layouts.IndexOf(settings.ActiveLayout);
            SwitchLayout(settings.Layouts[(i - 1 + settings.Layouts.Count) % settings.Layouts.Count].Id);
        }

        public void ScheduleSave() { saveAt = DateTime.UtcNow.AddSeconds(1); }

        public void SaveNow()
        {
            saveAt = DateTime.MaxValue;
            try { settings.Save(); } catch { }   // logged and shown in the status bar by Save itself
        }

        /// <summary>
        /// Widgets are only put back on top while iRacing is the window in front. Topmost windows stay above normal
        /// windows by themselves; re-asserting at other times lifted them over the taskbar, menus, tray flyouts and the
        /// Win+Shift+S screen (whose frozen image already shows the widgets: they looked doubled). Never while iRacing
        /// runs in exclusive full screen: nothing can be drawn over it there, and pushing windows over it can throw
        /// the game out of full screen.
        /// </summary>
        static bool MayGoOnTop()
        {
            return IRacingWindow.InFront() && !IRacingWindow.ExclusiveFullScreenNow();
        }

        /// <summary>--zorder-log &lt;file&gt;: every 2 seconds, what's in front and what the widgets do (to check iRacing's display modes).</summary>
        public static string ZOrderLog;

        void Tick()
        {
            var snap = telemetry.Latest;
            var now = DateTime.UtcNow;
            bool check = now >= nextTopmost, reassert = false;
            if (check) { nextTopmost = now.AddSeconds(2); reassert = MayGoOnTop(); }
            var log = check && ZOrderLog != null ? new System.Text.StringBuilder(ZOrderDiag.State(reassert)) : null;

            foreach (var w in windows.Values)
            {
                var ws = w.Widget.Settings;
                bool show = settings.OverlaysVisible && ws.Enabled && (EditMode || w.Widget.ShouldShow(snap));
                if (show)
                {
                    if (!w.IsVisible) { w.Show(); w.ApplyPosition(); w.Invalidate(); }
                    w.Tick(snap, now);
                    IntPtr cover = check ? w.CoveringWindow() : IntPtr.Zero;
                    if (reassert && cover != IntPtr.Zero) w.BringToTop();
                    if (log != null) log.Append(" | ").Append(ws.Title).Append(cover == IntPtr.Zero ? ": on top" : ": under " + ZOrderDiag.Describe(cover) + (reassert ? " -> lifted" : ""));
                }
                else if (w.IsVisible) w.Hide();
            }
            if (log != null) ZOrderDiag.Write(ZOrderLog, log.ToString());

            if (now >= saveAt) SaveNow();
        }

        // ---------------------------------------------------------------- hotkeys and wheel buttons

        readonly ControllerInput controller = new ControllerInput();
        readonly List<int> registered = new List<int>();
        const int HotkeyIdBase = 100;   // id = base + index in Hotkeys.Catalog

        /// <summary>Actions whose keys couldn't be registered (another program uses that combination).</summary>
        public readonly HashSet<HotkeyAction> KeysInUse = new HashSet<HotkeyAction>();

        /// <summary>A widget's settings were changed by a hotkey (shown / hidden, fuel custom per lap): the editor refreshes.</summary>
        public event Action<WidgetSettings> WidgetEdited;

        /// <summary>The Hotkeys window waits for a wheel button: it gets the next press instead of an action (UI thread).</summary>
        public Action<string, string, int> CaptureButton
        {
            get { return captureButton; }
            set { captureButton = value; UpdateController(); }
        }
        Action<string, string, int> captureButton;

        public void RegisterHotkeys(IntPtr hwnd)
        {
            hotkeySource = HwndSource.FromHwnd(hwnd);
            hotkeySource.AddHook(HotkeyHook);
            controller.Pressed += (device, name, button) =>
            {
                // on the input thread: only hand the press to the UI thread
                hotkeySource.Dispatcher.BeginInvoke(new Action(() => ButtonPressed(device, name, button)));
            };
            ReloadHotkeys();
        }

        bool paused;

        /// <summary>No actions while the Hotkeys window is open (pressing a combination to set it must not also run it).</summary>
        public void SuspendHotkeys()
        {
            paused = true;
            if (hotkeySource == null) return;
            foreach (int id in registered) Native.UnregisterHotKey(hotkeySource.Handle, id);
            registered.Clear();
        }

        /// <summary>(Re)registers every keyboard binding and starts the wheel input only when a button is bound.</summary>
        public void ReloadHotkeys()
        {
            if (hotkeySource == null) return;
            paused = false;
            foreach (int id in registered) Native.UnregisterHotKey(hotkeySource.Handle, id);
            registered.Clear();
            KeysInUse.Clear();
            for (int i = 0; i < Hotkeys.Catalog.Length; i++)
            {
                var b = Hotkeys.Of(settings, Hotkeys.Catalog[i].Action);
                uint mods, vk;
                if (b == null || !ParseHotkey(b.Keys, out mods, out vk)) continue;
                if (Native.RegisterHotKey(hotkeySource.Handle, HotkeyIdBase + i, mods | Native.MOD_NOREPEAT, vk)) registered.Add(HotkeyIdBase + i);
                else KeysInUse.Add(b.Action);
            }
            UpdateController();
        }

        // the wheel input runs only while a button is bound or the Hotkeys window waits for one (no cost otherwise)
        void UpdateController()
        {
            bool need = captureButton != null || (settings.Hotkeys != null && settings.Hotkeys.Any(b => b.Device != null));
            if (need) controller.Start(); else controller.Stop();
        }

        void ButtonPressed(string device, string name, int button)
        {
            if (captureButton != null) { captureButton(device, name, button); return; }
            if (paused) return;
            foreach (var b in settings.Hotkeys.Where(x => x.Device == device && x.Button == button)) Do(b.Action);
        }

        IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != Native.WM_HOTKEY) return IntPtr.Zero;
            int i = wParam.ToInt32() - HotkeyIdBase;
            if (i >= 0 && i < Hotkeys.Catalog.Length) { Do(Hotkeys.Catalog[i].Action); handled = true; }
            return IntPtr.Zero;
        }

        /// <summary>Runs an action (from a hotkey or a wheel button).</summary>
        public void Do(HotkeyAction a)
        {
            switch (a)
            {
                case HotkeyAction.EditLayout: SetEditMode(!EditMode); break;
                case HotkeyAction.ToggleWidgets: ToggleOverlays(); break;
                case HotkeyAction.NextLayout: NextLayout(); break;
                case HotkeyAction.PreviousLayout: PreviousLayout(); break;
                case HotkeyAction.Layout1: case HotkeyAction.Layout2: case HotkeyAction.Layout3: case HotkeyAction.Layout4:
                    int li = a - HotkeyAction.Layout1;
                    if (li < settings.Layouts.Count) SwitchLayout(settings.Layouts[li].Id);
                    break;
                case HotkeyAction.Widget1: case HotkeyAction.Widget2: case HotkeyAction.Widget3: case HotkeyAction.Widget4: case HotkeyAction.Widget5:
                    // widget n of the active layout, in the order of the widget list
                    int wi = a - HotkeyAction.Widget1;
                    if (wi < settings.Widgets.Count)
                    {
                        var ws = settings.Widgets[wi];
                        ws.Enabled = !ws.Enabled;
                        Invalidate(ws);
                        if (WidgetEdited != null) WidgetEdited(ws);
                    }
                    break;
                case HotkeyAction.FuelCustomUp: StepFuelCustom(0.05); break;
                case HotkeyAction.FuelCustomDown: StepFuelCustom(-0.05); break;
                case HotkeyAction.FuelResetAverage: telemetry.ResetFuelAverages(); break;
            }
        }

        /// <summary>
        /// Custom fuel per lap of the fuel calculators of the active layout, in steps (a fuel saving target during the
        /// race). From "0 = average" it starts at the current average.
        /// </summary>
        void StepFuelCustom(double step)
        {
            double avg = telemetry.Latest != null && telemetry.Latest.Fuel != null ? telemetry.Latest.Fuel.AvgPerLap : 0;
            foreach (var fs in settings.Widgets.OfType<FuelSettings>())
            {
                double from = fs.CustomPerLap > 0 ? fs.CustomPerLap : Math.Round(avg, 2);
                fs.CustomPerLap = Math.Max(0.01, Math.Round(from + step, 2));
                Invalidate(fs);
                if (WidgetEdited != null) WidgetEdited(fs);
            }
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
            if (hotkeySource != null) foreach (int id in registered) Native.UnregisterHotKey(hotkeySource.Handle, id);
            controller.Dispose();
            foreach (var w in windows.Values) w.Close();
            windows.Clear();
            SaveNow();
        }
    }
}
