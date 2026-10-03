using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.App.Widgets;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.Overlay
{
    /// <summary>
    /// A transparent, topmost, click-through window that shows one widget.
    /// Redraws only when the widget's display list changed.
    /// </summary>
    public sealed class OverlayWindow : Window
    {
        sealed class Surface : FrameworkElement
        {
            public readonly DrawingVisual Visual = new DrawingVisual();
            public Surface() { AddVisualChild(Visual); }
            protected override int VisualChildrenCount { get { return 1; } }
            protected override Visual GetVisualChild(int index) { return Visual; }
        }

        readonly Surface surface = new Surface();
        readonly WpfRenderer renderer;
        DisplayList current = new DisplayList();
        DisplayList next = new DisplayList();
        IntPtr hwnd;
        bool editMode;
        bool forceRedraw = true;
        bool sized;      // drawn at least once, so the size (needed for anchoring) is known
        bool dragging;
        DateTime nextFrame = DateTime.MinValue;

        public Widget Widget { get; private set; }
        public event Action<OverlayWindow> Moved;
        /// <summary>Clicked in edit mode (the main window selects it).</summary>
        public event Action<OverlayWindow> Grabbed;
        /// <summary>Set by the manager: snap settings and the rectangles of the other visible widgets.</summary>
        public AppSettings Snapping;
        internal Func<OverlayWindow, IEnumerable<Native.RECT>> SnapTargets;

        public OverlayWindow(Widget widget, string font)
        {
            Widget = widget;
            renderer = new WpfRenderer(font);
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Manual;
            Title = Core.BuildInfo.AppName + " - " + widget.Settings.Title;
            Left = widget.Settings.X;
            Top = widget.Settings.Y;
            Width = 10; Height = 10;
            Content = surface;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            RenderOptions.SetEdgeMode(surface, EdgeMode.Unspecified);

            MouseLeftButtonDown += (s, e) =>
            {
                if (!editMode) return;
                if (Grabbed != null) Grabbed(this);
                if (Widget.Settings.Locked) return;
                dragging = true;
                try { DragMove(); } finally { dragging = false; }
                SavePosition();
            };
            MouseWheel += OnWheel;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            ApplyExStyle();
        }

        /// <summary>Snapping while dragging: Windows asks where to put the window (WM_MOVING); Shift = no snapping.</summary>
        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_MOVING && dragging && Snapping != null && Snapping.SnapEnabled && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                var r = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.RECT>(lParam);
                var screen = Placement.At(Placement.Monitors(), (r.Left + r.Right) / 2.0, (r.Top + r.Bottom) / 2.0).Bounds;
                var others = SnapTargets != null ? SnapTargets(this) : Enumerable.Empty<Native.RECT>();
                Placement.Snap(ref r, screen, others, Math.Max(1, Snapping.SnapDistance), Math.Max(0, Snapping.SnapMargin));
                System.Runtime.InteropServices.Marshal.StructureToPtr(r, lParam, false);
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        public void SetFont(string font) { renderer.SetFont(font); forceRedraw = true; }

        public void SetEditMode(bool on)
        {
            editMode = on;
            ApplyExStyle();
            forceRedraw = true;
        }

        void ApplyExStyle()
        {
            if (hwnd == IntPtr.Zero) return;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED;
            if (editMode) ex &= ~Native.WS_EX_TRANSPARENT; else ex |= Native.WS_EX_TRANSPARENT; // click-through when locked
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
        }

        /// <summary>Puts the widget on top of all always-on-top windows (only while iRacing is in front, see OverlayManager).</summary>
        public void BringToTop()
        {
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>
        /// A visible window of another app that lies over this widget (Zero = none). Only those are a reason to bring
        /// the widget back on top: lifting it every time shuffled overlapping widgets.
        /// </summary>
        static readonly string OwnName = IRacingWindow.ProcessName((uint)Environment.ProcessId);

        public IntPtr CoveringWindow()
        {
            Native.RECT me;
            if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out me)) return IntPtr.Zero;
            uint own = (uint)Environment.ProcessId;
            // only the windows above this one in the z-order (a few topmost windows at most)
            for (IntPtr h = Native.GetWindow(hwnd, Native.GW_HWNDPREV); h != IntPtr.Zero; h = Native.GetWindow(h, Native.GW_HWNDPREV))
            {
                uint pid;
                Native.RECT r;
                if (!Native.IsWindowVisible(h)) continue;
                Native.GetWindowThreadProcessId(h, out pid);
                // widgets of another copy of this app (DEV next to the release) aren't a reason: both would keep lifting
                if (pid == own || IRacingWindow.ProcessName(pid) == OwnName || !Native.GetWindowRect(h, out r)) continue;
                if (r.Left < me.Right && me.Left < r.Right && r.Top < me.Bottom && me.Top < r.Bottom) return h;
            }
            return IntPtr.Zero;
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (!editMode || Widget.Settings.Locked) return;
            var st = Widget.Settings;
            st.Scale = Math.Round(Math.Max(0.5, Math.Min(3, st.Scale + (e.Delta > 0 ? 0.05 : -0.05))), 2);
            forceRedraw = true;
            Moved?.Invoke(this);
        }

        /// <summary>After dragging: the widget keeps its anchor; screen and offsets follow the new position.</summary>
        void SavePosition()
        {
            Native.RECT r;
            if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out r)) return;
            var ws = Widget.Settings;
            var mon = Placement.At(Placement.Monitors(), (r.Left + r.Right) / 2.0, (r.Top + r.Bottom) / 2.0);
            ws.Screen = mon.Label;
            Placement.ToOffsets(ws, mon.Bounds, r.Left, r.Top, r.Width, r.Height);
            ws.X = r.Left;
            ws.Y = r.Top;
            Moved?.Invoke(this);
        }

        public void Invalidate() { forceRedraw = true; }

        /// <summary>Window size in screen pixels (Width/Height are device independent units).</summary>
        void PixelSize(out double w, out double h)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            w = Math.Round(Width * dpi.DpiScaleX);
            h = Math.Round(Height * dpi.DpiScaleY);
        }

        /// <summary>
        /// Puts the window where its anchor, screen and offsets say. Widgets without a screen yet (older
        /// settings, new or duplicated widgets) get one, plus offsets, from their X/Y.
        /// A saved screen that isn't there (e.g. a laptop without the other monitors) falls back to the main screen.
        /// </summary>
        public void ApplyPosition()
        {
            if (hwnd == IntPtr.Zero || !sized) return;
            var ws = Widget.Settings;
            var monitors = Placement.Monitors();
            double w, h, x, y;
            PixelSize(out w, out h);
            Placement.Monitor mon;
            if (ws.Screen == null)
            {
                mon = Placement.At(monitors, ws.X + w / 2, ws.Y + h / 2);
                ws.Screen = mon.Label;
                Placement.ToOffsets(ws, mon.Bounds, ws.X, ws.Y, w, h);
                Moved?.Invoke(this);
            }
            else mon = Placement.Find(monitors, ws.Screen) ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
            Placement.ToPosition(ws, mon.Bounds, w, h, out x, out y);
            ws.X = x;
            ws.Y = y;
            Native.SetWindowPos(hwnd, IntPtr.Zero, (int)x, (int)y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        /// <summary>Called by the manager's timer. Respects the widget's own refresh rate.</summary>
        public void Tick(RaceSnapshot snap, DateTime now)
        {
            if (!forceRedraw && now < nextFrame) return;
            nextFrame = now.AddSeconds(1.0 / Math.Max(1, Widget.Settings.RefreshHz));

            next.Clear();
            try { Widget.Draw(next, snap); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Widget draw: " + ex); }

            if (editMode) DrawEditFrame(next);

            if (!forceRedraw && next.SameAs(current)) return;
            forceRedraw = false;
            var tmp = current; current = next; next = tmp;

            double scale = Widget.Settings.Scale;
            double w = Math.Max(10, Math.Ceiling(current.Width * scale)), h = Math.Max(10, Math.Ceiling(current.Height * scale));
            bool resized = !sized || Math.Abs(Width - w) > 0.5 || Math.Abs(Height - h) > 0.5;
            if (Math.Abs(Width - w) > 0.5) Width = w;
            if (Math.Abs(Height - h) > 0.5) Height = h;
            sized = true;
            // anchored widgets grow away from their anchor (e.g. bottom right grows up and to the left)
            if (resized && !dragging) ApplyPosition();

            renderer.SetDpi(VisualTreeHelper.GetDpi(this).PixelsPerDip, Widget.Settings.Scale);
            using (var dc = surface.Visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                renderer.Render(dc, current);
                dc.Pop();
            }
        }

        void DrawEditFrame(DisplayList dl)
        {
            if (dl.Width < 40) { dl.Width = 260; dl.Height = Math.Max(dl.Height, 60); }
            float w = dl.Width, h = dl.Height;
            bool locked = Widget.Settings.Locked;
            uint border = locked ? 0xFF8A8F96u : 0xFF2F8CFFu;
            // translucent fill so empty overlays can still be grabbed, plus a border and a label (grey = locked)
            dl.Ops.Insert(0, new DrawOp(OpKind.Rect, 0, 0, w, h, locked ? 0x30808080u : 0x302080FFu));
            dl.Rect(0, 0, w, 2, border);
            dl.Rect(0, h - 2, w, 2, border);
            dl.Rect(0, 0, 2, h, border);
            dl.Rect(w - 2, 0, 2, h, border);
            string label = Widget.Settings.Title + "  " + (int)Math.Round(Widget.Settings.Scale * 100) + "%" + (locked ? "  LOCKED" : "");
            dl.Rect(0, 0, Math.Min(w, 8 + label.Length * 7.5f), 18, locked ? 0xE0707780u : 0xE02F8CFFu);
            dl.Text(4, 0, w - 8, 18, label, 12, 0xFFFFFFFF);
        }
    }
}
