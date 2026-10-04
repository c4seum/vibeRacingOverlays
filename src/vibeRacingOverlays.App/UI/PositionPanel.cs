using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Overlay;

namespace vibeRacingOverlays.App.UI
{
    /// <summary>
    /// Position of the selected widget (screen, anchor, offsets, lock) and the snapping settings, shown in the
    /// main window next to the widget settings and styled like them. Dragging a widget on screen updates the
    /// numbers; changing a number moves the widget.
    /// </summary>
    public sealed class PositionPanel : StackPanel
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const double LabelWidth = 170;

        // corner/edge/plus marks for the 3x3 anchor grid (16x16 box)
        static readonly string[] AnchorPaths =
        {
            "M3,14 L3,3 L14,3", "M2,3 L14,3 M8,3 L8,13", "M2,3 L13,3 L13,14",
            "M3,2 L3,14 M3,8 L13,8", "M8,2 L8,14 M2,8 L14,8", "M13,2 L13,14 M13,8 L3,8",
            "M3,2 L3,13 L14,13", "M2,13 L14,13 M8,13 L8,3", "M13,2 L13,13 L2,13",
        };
        static readonly string[] AnchorNames = { "top left", "top", "top right", "left", "centre", "right", "bottom left", "bottom", "bottom right" };

        static readonly AppSettings SnapDefaults = new AppSettings();

        readonly AppSettings settings;
        readonly OverlayManager overlays;
        readonly List<Action> refreshers = new List<Action>();   // re-read all controls from the settings
        WidgetSettings ws;
        bool updating;   // true while controls are filled from the settings (their change events must not write back)

        public PositionPanel(AppSettings settings, OverlayManager overlays)
        {
            this.settings = settings;
            this.overlays = overlays;
        }

        /// <summary>Shows the position of this widget (null = nothing selected).</summary>
        public void SetWidget(WidgetSettings widget)
        {
            ws = widget;
            Build();
        }

        /// <summary>Re-reads the values (after the widget was dragged, scaled or changed elsewhere).</summary>
        public void Refresh()
        {
            updating = true;
            try { foreach (var r in refreshers) r(); }
            finally { updating = false; }
        }

        void Build()
        {
            Children.Clear();
            refreshers.Clear();
            if (ws == null) return;

            var typeLabel = Ui.Caption("Position");
            typeLabel.Margin = new Thickness(0, 0, 0, 4);
            Children.Add(typeLabel);
            var title = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 6) };
            refreshers.Add(() => title.Text = ws.Title);
            Children.Add(title);
            var hint = Ui.Caption("Drag widgets on screen in Edit layout (hold Shift to drag without snapping), or set them here.");
            hint.Margin = new Thickness(0, 0, 0, 14);
            Children.Add(hint);

            // ---- anchor & position
            var card = Ui.Card(this, Header("Anchoring and positioning", () => ws.Anchor == Anchor.TopLeft && ws.OffsetX == 0 && ws.OffsetY == 0,
                () => { ws.Anchor = Anchor.TopLeft; ws.OffsetX = 0; ws.OffsetY = 0; }, "Back to the top left corner of the screen"));

            var screen = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            screen.SelectionChanged += (s, e) =>
            {
                var m = screen.SelectedItem as Placement.Monitor;
                if (updating || m == null || ws.Screen == m.Label) return;
                ws.Screen = m.Label;
                Moved();
            };
            refreshers.Add(() =>
            {
                var monitors = Placement.Monitors();
                screen.ItemsSource = monitors;
                screen.SelectedItem = Placement.Find(monitors, ws.Screen) ?? monitors.FirstOrDefault(m => m.Primary);
            });
            var screenRow = Ui.Row(card, "Screen", screen, "Screens are remembered as Left / Middle / Right, so the layout lands on the same screen on another PC", null, LabelWidth);

            var grid = new UniformGrid { Rows = 3, Columns = 3, HorizontalAlignment = HorizontalAlignment.Left };
            var anchors = new ToggleButton[9];
            for (int i = 0; i < 9; i++)
            {
                var a = (Anchor)i;
                var mark = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(AnchorPaths[i]), Width = 16, Height = 16, StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                };
                var b = new ToggleButton { Content = mark, Width = 34, Height = 34, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 4), ToolTip = "Anchor: " + AnchorNames[i] };
                // the mark takes the button's text colour (white on the selected, blue button)
                mark.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = b });
                System.Windows.Automation.AutomationProperties.SetName(b, "Anchor " + AnchorNames[i]);
                b.Checked += (s, e) =>
                {
                    if (updating) return;
                    // new anchor: the widget moves into that corner/edge of its screen
                    ws.Anchor = a; ws.OffsetX = 0; ws.OffsetY = 0;
                    Moved();
                };
                b.Unchecked += (s, e) => { if (!updating && ws.Anchor == a) { updating = true; b.IsChecked = true; updating = false; } };
                anchors[i] = b;
                grid.Children.Add(b);
            }
            refreshers.Add(() => { for (int i = 0; i < 9; i++) anchors[i].IsChecked = (int)ws.Anchor == i; });
            var anchorRow = Ui.Row(card, "Anchor", grid, "The corner, edge or centre of the screen the widget sticks to. It grows away from it when it gets bigger.", null, LabelWidth);

            var offXRow = Ui.Row(card, "X offset (px)", Stepper(() => ws.OffsetX, v => { ws.OffsetX = v; Moved(); }),
                "Distance from the anchor towards the middle of the screen (centre: + is right)",
                ResetButton(() => ws.OffsetX == 0, () => { ws.OffsetX = 0; Moved(); }, "Reset to 0"), LabelWidth);
            var offYRow = Ui.Row(card, "Y offset (px)", Stepper(() => ws.OffsetY, v => { ws.OffsetY = v; Moved(); }),
                "Distance from the anchor towards the middle of the screen (centre: + is down)",
                ResetButton(() => ws.OffsetY == 0, () => { ws.OffsetY = 0; Moved(); }, "Reset to 0"), LabelWidth);

            var lockBox = Ui.Toggle(false);
            lockBox.ToolTip = "A locked widget can't be dragged or resized by accident";
            RoutedEventHandler onLock = (s, e) => { bool v = lockBox.IsChecked == true; if (!updating && ws.Locked != v) { ws.Locked = v; overlays.Invalidate(ws); } };
            lockBox.Checked += onLock;
            lockBox.Unchecked += onLock;
            var lockLine = new DockPanel { LastChildFill = false };
            var lockAll = new Button { Content = "Lock all", Margin = new Thickness(16, 0, 6, 0), ToolTip = "Lock every widget of this layout" };
            var unlockAll = new Button { Content = "Unlock all", Margin = new Thickness(0), ToolTip = "Unlock every widget of this layout" };
            lockAll.Click += (s, e) => SetAllLocked(true);
            unlockAll.Click += (s, e) => SetAllLocked(false);
            lockLine.Children.Add(lockBox);
            lockLine.Children.Add(lockAll);
            lockLine.Children.Add(unlockAll);
            Ui.Row(card, "Lock position and size", lockLine, "A locked widget can't be dragged or resized in edit layout, so it doesn't move by accident", null, LabelWidth);

            // a locked widget can't be moved from here either
            refreshers.Add(() =>
            {
                lockBox.IsChecked = ws.Locked;
                screenRow.IsEnabled = anchorRow.IsEnabled = offXRow.IsEnabled = offYRow.IsEnabled = !ws.Locked;
            });

            // ---- snapping (app-wide)
            var snapCard = Ui.Card(this, Header("Snapping (all widgets)",
                () => settings.SnapEnabled == SnapDefaults.SnapEnabled && settings.SnapDistance == SnapDefaults.SnapDistance && settings.SnapMargin == SnapDefaults.SnapMargin,
                () => { settings.SnapEnabled = SnapDefaults.SnapEnabled; settings.SnapDistance = SnapDefaults.SnapDistance; settings.SnapMargin = SnapDefaults.SnapMargin; overlays.ScheduleSave(); },
                "Reset the snapping settings to the defaults"));
            var snap = Ui.Toggle(settings.SnapEnabled);
            RoutedEventHandler onSnap = (s, e) => { if (!updating) { settings.SnapEnabled = snap.IsChecked == true; overlays.ScheduleSave(); Refresh(); } };
            snap.Checked += onSnap;
            snap.Unchecked += onSnap;
            refreshers.Add(() => snap.IsChecked = settings.SnapEnabled);
            Ui.Row(snapCard, "Snap when dragging", snap, "Dragged widgets snap to the edges and corners of other widgets and of the screen",
                ResetButton(() => settings.SnapEnabled == SnapDefaults.SnapEnabled, () => { settings.SnapEnabled = SnapDefaults.SnapEnabled; overlays.ScheduleSave(); }, "Reset to default (on)"), LabelWidth);
            Ui.Row(snapCard, "Snap distance (px)", Number(1, 50, () => settings.SnapDistance, v => { settings.SnapDistance = (int)v; overlays.ScheduleSave(); }),
                "How close an edge must come before it snaps",
                ResetButton(() => settings.SnapDistance == SnapDefaults.SnapDistance, () => { settings.SnapDistance = SnapDefaults.SnapDistance; overlays.ScheduleSave(); }, "Reset to default (" + SnapDefaults.SnapDistance + ")"), LabelWidth);
            Ui.Row(snapCard, "Snap margin (px)", Number(0, 50, () => settings.SnapMargin, v => { settings.SnapMargin = (int)v; overlays.ScheduleSave(); }),
                "Space kept between snapped widgets and from the screen edge",
                ResetButton(() => settings.SnapMargin == SnapDefaults.SnapMargin, () => { settings.SnapMargin = SnapDefaults.SnapMargin; overlays.ScheduleSave(); }, "Reset to default (" + SnapDefaults.SnapMargin + ")"), LabelWidth);

            Refresh();
        }

        void Moved()
        {
            // a widget that was never shown has no screen yet: it gets the main screen
            if (ws.Screen == null) { var m = Placement.Monitors().FirstOrDefault(x => x.Primary); if (m != null) ws.Screen = m.Label; }
            overlays.ApplyPosition(ws);
            Refresh();
        }

        void SetAllLocked(bool locked)
        {
            foreach (var w in settings.Widgets) { w.Locked = locked; overlays.Invalidate(w); }
            Refresh();
        }

        // ---------------------------------------------------------------- building blocks (same look as SettingsPanel)

        UIElement Header(string text, Func<bool> isDefault, Action reset, string tooltip)
        {
            var b = Ui.HeaderReset(tooltip);
            b.Click += (s, e) => { reset(); Moved(); };
            refreshers.Add(() => b.IsEnabled = !isDefault());
            return Ui.Header(text, b);
        }

        /// <summary>Small reset button, enabled only while the value differs from the default.</summary>
        Button ResetButton(Func<bool> isDefault, Action reset, string tooltip)
        {
            var b = Ui.RowReset(tooltip);
            b.Click += (s, e) => { reset(); Refresh(); };
            refreshers.Add(() => b.IsEnabled = !isDefault());
            return b;
        }

        /// <summary>◀ [value] ▶: the buttons and Up/Down change it by 1 (Shift: 10); typing applies on Enter / leaving the field.</summary>
        FrameworkElement Stepper(Func<double> get, Action<double> set)
        {
            var box = new TextBox { Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            Action<int> step = dir => set(Math.Round(get()) + dir * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1));
            Action commit = () =>
            {
                double v;
                if (double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, Inv, out v) && Math.Round(v) != Math.Round(get())) set(Math.Round(v));
                else Refresh();
            };
            box.LostKeyboardFocus += (s, e) => commit();
            box.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { commit(); box.SelectAll(); e.Handled = true; }
                else if (e.Key == Key.Up || e.Key == Key.Down) { step(e.Key == Key.Up ? 1 : -1); box.SelectAll(); e.Handled = true; }
            };
            refreshers.Add(() => { if (!box.IsKeyboardFocused) box.Text = Math.Round(get()).ToString(Inv); });
            var minus = Ui.IconButton(Ui.IconLeft, "-1 (Shift: -10)");
            minus.Margin = new Thickness(0, 0, 4, 0);
            var plus = Ui.IconButton(Ui.IconRight, "+1 (Shift: +10)");
            plus.Margin = new Thickness(4, 0, 0, 0);
            minus.Click += (s, e) => step(-1);
            plus.Click += (s, e) => step(1);
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(minus);
            p.Children.Add(box);
            p.Children.Add(plus);
            return p;
        }

        /// <summary>Slider with a value box on the right, like the number settings of a widget.</summary>
        FrameworkElement Number(double min, double max, Func<double> get, Action<double> set)
        {
            var panel = new DockPanel();
            var box = new TextBox { Width = 64, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(box, Dock.Right);
            var slider = new Slider { Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 5, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (s, e) => { if (updating) return; set(Math.Round(slider.Value)); Refresh(); };
            box.LostKeyboardFocus += (s, e) =>
            {
                double v;
                if (double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, Inv, out v)) set(Math.Max(min, Math.Min(max, Math.Round(v))));
                Refresh();
            };
            box.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { Keyboard.ClearFocus(); e.Handled = true; } };
            refreshers.Add(() => { slider.Value = get(); if (!box.IsKeyboardFocused) box.Text = get().ToString(Inv); });
            panel.Children.Add(box);
            panel.Children.Add(slider);
            return panel;
        }
    }
}
