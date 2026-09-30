using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
        DateTime nextFrame = DateTime.MinValue;

        public Widget Widget { get; private set; }
        public event Action<OverlayWindow> Moved;

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

            MouseLeftButtonDown += (s, e) => { if (editMode) { DragMove(); SavePosition(); } };
            MouseWheel += OnWheel;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            ApplyExStyle();
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

        public void ReassertTopmost()
        {
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (!editMode) return;
            var st = Widget.Settings;
            st.Scale = Math.Round(Math.Max(0.5, Math.Min(3, st.Scale + (e.Delta > 0 ? 0.05 : -0.05))), 2);
            forceRedraw = true;
            Moved?.Invoke(this);
        }

        void SavePosition()
        {
            Widget.Settings.X = Math.Round(Left);
            Widget.Settings.Y = Math.Round(Top);
            Moved?.Invoke(this);
        }

        public void Invalidate() { forceRedraw = true; }

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
            if (Math.Abs(Width - w) > 0.5) Width = w;
            if (Math.Abs(Height - h) > 0.5) Height = h;

            renderer.SetDpi(VisualTreeHelper.GetDpi(this).PixelsPerDip);
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
            // translucent fill so empty overlays can still be grabbed, plus a border and a label
            dl.Ops.Insert(0, new DrawOp(OpKind.Rect, 0, 0, w, h, 0x302080FF));
            dl.Rect(0, 0, w, 2, 0xFF2F8CFF);
            dl.Rect(0, h - 2, w, 2, 0xFF2F8CFF);
            dl.Rect(0, 0, 2, h, 0xFF2F8CFF);
            dl.Rect(w - 2, 0, 2, h, 0xFF2F8CFF);
            string label = Widget.Settings.Title + "  " + (int)Math.Round(Widget.Settings.Scale * 100) + "%";
            dl.Rect(0, 0, Math.Min(w, 8 + label.Length * 7.5f), 18, 0xE02F8CFF);
            dl.Text(4, 0, w - 8, 18, label, 12, 0xFFFFFFFF);
        }
    }
}
