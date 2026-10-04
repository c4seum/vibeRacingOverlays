using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.App.Widgets;
using vibeRacingOverlays.Data.Engine;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.UI
{
    public enum PreviewSource { Live, DemoSingleClass, DemoMulticlass }

    /// <summary>
    /// Data for the editor preview: the app's live snapshot, or one of two demo races that are only
    /// started while the preview needs them.
    /// </summary>
    public sealed class PreviewData : IDisposable
    {
        readonly TelemetryService main;
        TelemetryService single, multi, singlePractice, multiPractice;

        public PreviewData(TelemetryService main) { this.main = main; }

        public bool LiveAvailable { get { return main.IRacingConnected; } }

        public RaceSnapshot Get(PreviewSource src, bool practice = false)
        {
            switch (src)
            {
                case PreviewSource.DemoSingleClass: return (practice ? Demo(ref singlePractice, false, true) : Demo(ref single, false, false)).Latest;
                case PreviewSource.DemoMulticlass: return (practice ? Demo(ref multiPractice, true, true) : Demo(ref multi, true, false)).Latest;
                default: return main.Latest;
            }
        }

        static TelemetryService Demo(ref TelemetryService svc, bool multiClass, bool practice)
        {
            if (svc == null)
            {
                svc = new TelemetryService { Mode = SourceMode.Demo, DemoMultiClass = multiClass, DemoPractice = practice, SnapshotHz = 5 };
                svc.Start();
            }
            return svc;
        }

        public void Dispose()
        {
            if (single != null) single.Dispose();
            if (multi != null) multi.Dispose();
            if (singlePractice != null) singlePractice.Dispose();
            if (multiPractice != null) multiPractice.Dispose();
        }
    }

    /// <summary>Live preview of the selected overlay, shown above its settings.</summary>
    public sealed class PreviewPanel : Border
    {
        sealed class Surface : FrameworkElement
        {
            public readonly DrawingVisual Visual = new DrawingVisual();
            public Surface() { AddVisualChild(Visual); }
            protected override int VisualChildrenCount { get { return 1; } }
            protected override Visual GetVisualChild(int index) { return Visual; }
        }

        const double Pad = 10, MaxPreviewHeight = 360;
        static PreviewSource? chosen;   // remembered while switching between overlays

        readonly PreviewData data;
        readonly AppSettings settings;
        readonly ComboBox sourceBox = new ComboBox { Width = 170, ToolTip = "What the preview shows: live iRacing data, or a demo race in single class or multiclass (the widgets on screen follow the Data choice at the top)" };
        readonly TextBlock info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 12 };
        readonly Surface surface = InitSurface();

        // text exactly like the overlay windows: grayscale, no ClearType. ClearType (the window default) gave colour
        // fringes and made the preview look thinner and sharper-edged than the widget on screen
        static Surface InitSurface()
        {
            var s = new Surface();
            TextOptions.SetTextFormattingMode(s, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(s, TextRenderingMode.Grayscale);
            return s;
        }
        readonly TextBlock empty = new TextBlock { Text = "No data for this widget in the selected source.", Margin = new Thickness(0, 8, 0, 8) };
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly Button layoutButton = new Button { Style = Ui.Style("GhostButton"), Margin = new Thickness(0) };
        WpfRenderer renderer;
        WidgetSettings ws;
        bool sideBySide;
        StackPanel infoRow;
        DockPanel topPanel;

        /// <summary>Raised when the user clicks the stacked / side-by-side button.</summary>
        public event Action LayoutToggleRequested;

        public PreviewPanel(PreviewData data, AppSettings settings)
        {
            this.data = data;
            this.settings = settings;
            Padding = new Thickness(24, 10, 24, 12);
            BorderThickness = new Thickness(0, 0, 0, 1);
            SetResourceReference(BackgroundProperty, "Panel");
            SetResourceReference(BorderBrushProperty, "Divider");
            info.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted");

            sourceBox.ItemsSource = new[] { "Live (iRacing)", "Demo: single class", "Demo: multiclass" };
            if (!chosen.HasValue) chosen = data.LiveAvailable ? PreviewSource.Live : PreviewSource.DemoMulticlass;
            sourceBox.SelectedIndex = (int)chosen.Value;
            sourceBox.SelectionChanged += (s, e) => { chosen = (PreviewSource)sourceBox.SelectedIndex; Refresh(); };

            var title = new TextBlock { Text = "PREVIEW", FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            layoutButton.Click += (s, e) => { if (LayoutToggleRequested != null) LayoutToggleRequested(); };
            DockPanel.SetDock(layoutButton, Dock.Right);
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = false };
            top.Children.Add(layoutButton);
            top.Children.Add(title);
            top.Children.Add(sourceBox);
            infoRow = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(infoRow);
            infoRow.Children.Add(info);
            topPanel = top;
            SetSideBySide(false);

            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(surface);
            stack.Children.Add(empty);
            Child = stack;

            timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => timer.Start();
            Unloaded += (s, e) => timer.Stop();
            SizeChanged += (s, e) => Refresh();
        }

        /// <summary>Side by side: the preview uses the full height of its column.</summary>
        public void SetSideBySide(bool on)
        {
            sideBySide = on;
            layoutButton.Content = on ? "⇅  Stacked" : "⇆  Side by side";
            layoutButton.ToolTip = on ? "Show the preview above the settings" : "Show the preview next to the settings";
            BorderThickness = on ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 0, 1);
            VerticalAlignment = on ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            // narrow column: put the info text on its own line below the controls
            var stack = Child as StackPanel;
            if (stack != null && infoRow != null)
            {
                if (infoRow.Parent is Panel) ((Panel)infoRow.Parent).Children.Remove(infoRow);
                if (on) { stack.Children.Insert(1, infoRow); info.Margin = new Thickness(0, 0, 0, 8); }
                else { topPanel.Children.Add(infoRow); info.Margin = new Thickness(10, 0, 0, 0); }
            }
            Refresh();
        }

        public void SetWidget(WidgetSettings w)
        {
            ws = w;
            Visibility = w == null ? Visibility.Collapsed : Visibility.Visible;
            Refresh();
        }

        void Refresh()
        {
            if (ws == null || !IsVisible) return;
            var src = (PreviewSource)Math.Max(0, sourceBox.SelectedIndex);
            // widgets with session profiles: the demo session follows the P&Q / Race switch of the settings
            bool practice = ws is ISessionProfiles && SettingsPanel.Kind == SessionKind.PracticeQualify;
            var snap = data.Get(src, practice);

            if (renderer == null) renderer = new WpfRenderer(settings.Font);
            renderer.SetFont(settings.Font);
            renderer.SetDpi(VisualTreeHelper.GetDpi(this).PixelsPerDip);

            var dl = new DisplayList();
            bool hasData = snap != null && snap.Connected && (src != PreviewSource.Live || data.LiveAvailable);
            if (hasData)
            {
                try { Widget.Create(ws).Paint(dl, snap); } catch { dl.Clear(); }
            }

            string layout = !hasData ? "" : (snap.Classes.Count > 1 ? "Multiclass (" + snap.Classes.Count + " classes)" : "Single class") + (snap.IsRace ? ", race" : ", " + (snap.SessionType ?? "practice").ToLowerInvariant());
            if (src == PreviewSource.Live && !data.LiveAvailable) layout = "iRacing is not running: pick a demo source";

            if (!hasData || dl.Width <= 0 || dl.Height <= 0)
            {
                info.Text = layout;
                surface.Width = 0; surface.Height = 0;
                using (surface.Visual.RenderOpen()) { }
                empty.Visibility = Visibility.Visible;
                return;
            }
            empty.Visibility = Visibility.Collapsed;

            // show at the overlay's own scale, shrunk when it doesn't fit the panel: the height left over after the
            // header lines above the image (side by side the info text has a line of its own) and the image's own margin
            double used = Padding.Top + Padding.Bottom + Pad * 2;
            var stack = Child as StackPanel;
            if (stack != null)
                foreach (var c in stack.Children.OfType<FrameworkElement>())
                    if (c != surface && c.Visibility == Visibility.Visible) used += c.ActualHeight + c.Margin.Top + c.Margin.Bottom;
            double avail = Math.Max(100, ActualWidth - Padding.Left - Padding.Right - Pad * 2);
            double maxH = sideBySide ? Math.Max(60, ActualHeight - used) : MaxPreviewHeight;
            double scale = Math.Min(ws.Scale, Math.Min(avail / dl.Width, maxH / dl.Height));
            double w = dl.Width * scale + Pad * 2, h = dl.Height * scale + Pad * 2;
            info.Text = layout + "   " + (int)Math.Round(scale * 100) + "%";
            surface.Width = w; surface.Height = h;
            surface.HorizontalAlignment = HorizontalAlignment.Center;
            renderer.SetDpi(VisualTreeHelper.GetDpi(this).PixelsPerDip, scale);
            using (var dc = surface.Visual.RenderOpen()) PreviewRenderer.Draw(dc, renderer, dl, scale, Pad, w, h);
        }
    }
}
