using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Overlay;
using vibeRacingOverlays.App.UI;
using vibeRacingOverlays.App.Widgets;
using vibeRacingOverlays.Data.Engine;

namespace vibeRacingOverlays.App
{
    public partial class MainWindow : Window
    {
        readonly AppSettings settings;
        readonly TelemetryService telemetry;
        readonly OverlayManager overlays;
        readonly DispatcherTimer statusTimer;
        readonly PreviewData previewData;
        readonly PreviewPanel preview;
        readonly PositionPanel position;
        bool loading;

        public MainWindow(AppSettings settings, TelemetryService telemetry, OverlayManager overlays)
        {
            InitializeComponent();
            this.settings = settings;
            this.telemetry = telemetry;
            this.overlays = overlays;
            Title = BuildInfo.AppName + " " + BuildInfo.Version;
            AppTitle.Text = BuildInfo.AppName;
            if (BuildInfo.IsDev) AppTitle.Foreground = System.Windows.Media.Brushes.Orange;

            loading = true;
            SourceBox.ItemsSource = Enum.GetValues(typeof(SourceMode));
            SourceBox.SelectedItem = settings.Source;
            OverlaysToggle.IsChecked = settings.OverlaysVisible;
            ThemeBox.ItemsSource = Enum.GetValues(typeof(AppTheme));
            ThemeBox.SelectedItem = settings.Theme;
            loading = false;

            ThemeBox.SelectionChanged += (s, e) =>
            {
                if (loading || ThemeBox.SelectedItem == null) return;
                settings.Theme = (AppTheme)ThemeBox.SelectedItem;
                ThemeManager.Apply(settings.Theme);
                overlays.ScheduleSave();
            };

            SourceBox.SelectionChanged += (s, e) =>
            {
                if (loading || SourceBox.SelectedItem == null) return;
                settings.Source = (SourceMode)SourceBox.SelectedItem;
                telemetry.Mode = settings.Source;
                overlays.ScheduleSave();
            };
            // Checked/Unchecked instead of Click: also fires for UI Automation (the manager syncs IsChecked back)
            EditToggle.Checked += (s, e) => { if (!overlays.EditMode) overlays.SetEditMode(true); };
            EditToggle.Unchecked += (s, e) => { if (overlays.EditMode) overlays.SetEditMode(false); };
            OverlaysToggle.Click += (s, e) => overlays.ToggleOverlays();
            overlays.StateChanged += () =>
            {
                EditToggle.IsChecked = overlays.EditMode;
                OverlaysToggle.IsChecked = settings.OverlaysVisible;
                // the layout may have been switched with the hotkey
                if (shownLayoutId != settings.ActiveLayoutId) ShowActiveLayout();
            };

            // dragged, scaled or renamed: the position panel follows
            overlays.WidgetChanged += ws => { if (ws == Selected) position.Refresh(); };
            // clicking a widget on screen in edit layout mode selects it
            overlays.WidgetGrabbed += ws =>
            {
                foreach (ListBoxItem item in WidgetList.Items)
                    if (item.Tag == ws) WidgetList.SelectedItem = item;
            };

            LayoutBox.SelectionChanged += (s, e) =>
            {
                var l = LayoutBox.SelectedItem as LayoutConfig;
                if (loading || l == null) return;
                overlays.SwitchLayout(l.Id);
            };
            LayoutNewButton.Click += (s, e) => NewLayout();
            LayoutSaveAsButton.Click += (s, e) => SaveLayoutAs();
            LayoutRenameButton.Click += (s, e) => RenameLayout();
            LayoutDeleteButton.Click += (s, e) => DeleteLayout();

            AddButton.Click += (s, e) => ShowAddMenu();
            DuplicateButton.Click += (s, e) => Duplicate();
            RemoveButton.Click += (s, e) => Remove();
            WidgetList.SelectionChanged += (s, e) => ShowSelected();

            FooterText.Text = "Edit layout (" + settings.HotkeyEditMode + "): drag widgets to move them (Shift: no snapping), mouse wheel to resize.   "
                + "Show/hide all: " + settings.HotkeyToggleOverlays + ".   Next layout: " + settings.HotkeyNextLayout
                + ".   Layouts are saved automatically in " + AppSettings.Folder;

            SettingsPanel.App = settings;
            previewData = new PreviewData(telemetry);
            preview = new PreviewPanel(previewData, settings);
            PreviewHost.Content = preview;
            position = new PositionPanel(settings, overlays);
            PositionHost.Content = position;
            preview.LayoutToggleRequested += () =>
            {
                settings.PreviewSideBySide = !settings.PreviewSideBySide;
                ApplyEditorLayout();
                overlays.ScheduleSave();
            };
            ApplyEditorLayout();
            Closed += (s, e) => previewData.Dispose();

            ShowActiveLayout();

            statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            statusTimer.Tick += (s, e) => UpdateStatus();
            statusTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            overlays.RegisterHotkeys(new WindowInteropHelper(this).Handle);
        }

        /// <summary>
        /// Stacked: preview on top, below it the settings with the position panel on their right.
        /// Side by side: settings left, splitter, preview right with the position panel below it.
        /// </summary>
        void ApplyEditorLayout()
        {
            bool side = settings.PreviewSideBySide;
            var cols = EditorGrid.ColumnDefinitions;
            var rows = EditorGrid.RowDefinitions;
            if (side)
            {
                // settings keep their natural width (splitter adjustable), the preview gets the rest
                cols[0].Width = new GridLength(680);
                cols[1].Width = GridLength.Auto;
                cols[2].Width = new GridLength(1, GridUnitType.Star);
                cols[2].MinWidth = 520;
                // the preview fills the space above the position panel
                rows[0].Height = new GridLength(1, GridUnitType.Star);
                rows[0].MinHeight = 180;
                rows[1].Height = GridLength.Auto;
                Place(SettingsScroll, 0, 0, 2, 1);
                Place(EditorSplitter, 0, 1, 2, 1);
                Place(PreviewHost, 0, 2, 1, 1);
                Place(PositionScroll, 1, 2, 1, 1);
                EditorSplitter.Visibility = Visibility.Visible;
            }
            else
            {
                cols[0].Width = new GridLength(1, GridUnitType.Star);
                cols[1].Width = new GridLength(0);
                cols[2].Width = GridLength.Auto;
                cols[2].MinWidth = 0;
                rows[0].Height = GridLength.Auto;
                rows[0].MinHeight = 0;
                rows[1].Height = new GridLength(1, GridUnitType.Star);
                Place(PreviewHost, 0, 0, 1, 3);
                Place(SettingsScroll, 1, 0, 1, 1);
                Place(PositionScroll, 1, 2, 1, 1);
                EditorSplitter.Visibility = Visibility.Collapsed;
            }
            // make room for settings + preview / position panel when the window is narrow
            double target = Math.Min(side ? 1600 : 1440, SystemParameters.WorkArea.Width - 40);
            if (WindowState == WindowState.Normal && Width < target)
            {
                Width = target;
                Left = Math.Max(SystemParameters.WorkArea.Left, Math.Min(Left, SystemParameters.WorkArea.Right - Width));
            }
            preview.SetSideBySide(side);
        }

        static void Place(UIElement e, int row, int col, int rowSpan, int colSpan)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            Grid.SetRowSpan(e, rowSpan);
            Grid.SetColumnSpan(e, colSpan);
        }

        void UpdateStatus()
        {
            var snap = telemetry.Latest;
            bool live = telemetry.IRacingConnected;
            string src = telemetry.ActiveSource;
            if (src == "iRacing" && live)
            {
                StatusDot.Fill = Brushes.LimeGreen;
                StatusText.Text = snap.Connected && snap.Source == "iRacing"
                    ? "iRacing connected - " + snap.SessionType + " @ " + snap.TrackName
                    : "iRacing connected - loading session...";
            }
            else if (src == "Demo")
            {
                StatusDot.Fill = Brushes.Orange;
                StatusText.Text = "Demo race (iRacing not running)";
            }
            else
            {
                StatusDot.Fill = Brushes.Gray;
                StatusText.Text = "Waiting for iRacing...";
            }
        }

        void RefreshList()
        {
            var selected = WidgetList.SelectedItem as ListBoxItem;
            string selectedId = selected != null ? ((WidgetSettings)selected.Tag).Id : null;
            WidgetList.Items.Clear();
            foreach (var ws in settings.Widgets)
            {
                var w = ws;
                // only the box toggles the overlay; clicking the name selects it (opens its settings)
                var cb = new CheckBox { IsChecked = w.Enabled, Margin = new Thickness(2, 4, 8, 4), ToolTip = "Show / hide this widget" };
                cb.Click += (s, e) => { w.Enabled = cb.IsChecked == true; overlays.Invalidate(w); };
                // display name (user editable) + the widget type, shown when the name differs from it
                var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                SetListName(name, w);
                var row = new DockPanel { Background = Brushes.Transparent };   // transparent = whole row is clickable
                row.Children.Add(cb);
                row.Children.Add(name);
                var item = new ListBoxItem { Content = row, Tag = w };
                WidgetList.Items.Add(item);
                if (w.Id == selectedId) WidgetList.SelectedItem = item;
            }
        }

        // ---------------------------------------------------------------- layouts

        string shownLayoutId;

        void RefreshLayouts()
        {
            loading = true;
            LayoutBox.ItemsSource = null;
            LayoutBox.ItemsSource = settings.Layouts;
            LayoutBox.SelectedItem = settings.ActiveLayout;
            LayoutDeleteButton.IsEnabled = settings.Layouts.Count > 1;
            loading = false;
        }

        /// <summary>Shows the widgets of the active layout (after switching, creating or deleting layouts).</summary>
        void ShowActiveLayout()
        {
            shownLayoutId = settings.ActiveLayoutId;
            RefreshLayouts();
            RefreshList();
            if (WidgetList.Items.Count > 0) WidgetList.SelectedIndex = 0;
            else { SettingsHost.Children.Clear(); preview.SetWidget(null); position.SetWidget(null); }
        }

        string UniqueLayoutName(string name)
        {
            string n = name;
            for (int i = 2; settings.Layouts.Any(l => string.Equals(l.Name, n, StringComparison.OrdinalIgnoreCase)); i++) n = name + " (" + i + ")";
            return n;
        }

        void AddLayout(LayoutConfig l)
        {
            settings.Layouts.Add(l);
            overlays.SwitchLayout(l.Id);
            ShowActiveLayout();
        }

        void NewLayout()
        {
            string name = InputDialog.Ask(this, "New layout", "Name of the new layout:", UniqueLayoutName("Layout " + (settings.Layouts.Count + 1)));
            if (name == null) return;
            AddLayout(AppSettings.DefaultLayout(UniqueLayoutName(name)));
        }

        void SaveLayoutAs()
        {
            string name = InputDialog.Ask(this, "Save layout as", "Save the current layout (all widgets, positions and settings) as:",
                UniqueLayoutName(settings.ActiveLayout.Name + " copy"));
            if (name == null) return;
            AddLayout(settings.CloneLayout(settings.ActiveLayout, UniqueLayoutName(name)));
        }

        void RenameLayout()
        {
            var l = settings.ActiveLayout;
            string name = InputDialog.Ask(this, "Rename layout", "New name for layout '" + l.Name + "':", l.Name);
            if (name == null || name == l.Name) return;
            l.Name = settings.Layouts.Any(x => x != l && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ? UniqueLayoutName(name) : name;
            overlays.ScheduleSave();
            RefreshLayouts();
        }

        void DeleteLayout()
        {
            if (settings.Layouts.Count < 2) return;
            var l = settings.ActiveLayout;
            if (MessageBox.Show(this, "Delete layout '" + l.Name + "' and its " + l.Widgets.Count + " widgets?", "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            int i = settings.Layouts.IndexOf(l);
            var next = settings.Layouts[i == 0 ? 1 : i - 1];
            overlays.SwitchLayout(next.Id);
            settings.Layouts.Remove(l);
            overlays.ScheduleSave();
            ShowActiveLayout();
        }

        static void SetListName(TextBlock tb, WidgetSettings w)
        {
            tb.Inlines.Clear();
            tb.Inlines.Add(new System.Windows.Documents.Run(w.Title));
            if (!string.Equals(w.Title.Trim(), w.TypeName, StringComparison.OrdinalIgnoreCase))
            {
                var type = new System.Windows.Documents.Run("  " + w.TypeName) { FontSize = 11 };
                type.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
                tb.Inlines.Add(type);
            }
            tb.ToolTip = w.TypeName + " widget";
        }

        WidgetSettings Selected
        {
            get { var i = WidgetList.SelectedItem as ListBoxItem; return i != null ? (WidgetSettings)i.Tag : null; }
        }

        void ShowSelected()
        {
            var ws = Selected;
            if (preview != null) preview.SetWidget(ws);
            if (position != null) position.SetWidget(ws);
            if (ws == null) { SettingsHost.Children.Clear(); return; }
            SettingsPanel.Build(SettingsHost, ws, () => overlays.Invalidate(ws), () =>
            {
                foreach (ListBoxItem item in WidgetList.Items)
                    if (item.Tag == ws) SetListName((TextBlock)((DockPanel)item.Content).Children[1], ws);
            });
        }

        void ShowAddMenu()
        {
            var menu = new ContextMenu();
            foreach (var entry in Widget.Catalog)
            {
                var e = entry;
                var mi = new MenuItem { Header = e.Name };
                mi.Click += (s, a) =>
                {
                    var ws = e.Make();
                    ws.X = 200 + 30 * settings.Widgets.Count;
                    ws.Y = 200 + 30 * settings.Widgets.Count;
                    Add(ws);
                };
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = AddButton;
            menu.IsOpen = true;
        }

        void Add(WidgetSettings ws)
        {
            settings.Widgets.Add(ws);
            overlays.Sync();
            overlays.ScheduleSave();
            RefreshList();
            WidgetList.SelectedIndex = WidgetList.Items.Count - 1;
        }

        void Duplicate()
        {
            var ws = Selected;
            if (ws == null) return;
            var json = System.Text.Json.JsonSerializer.Serialize<WidgetSettings>(ws);
            var copy = System.Text.Json.JsonSerializer.Deserialize<WidgetSettings>(json);
            copy.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            copy.Title = ws.Title + " (copy)";
            copy.X += 30; copy.Y += 30;
            copy.Screen = null;   // placed from X/Y, so it doesn't land exactly on top of the original
            Add(copy);
        }

        void Remove()
        {
            var ws = Selected;
            if (ws == null) return;
            if (MessageBox.Show(this, "Remove widget '" + ws.Title + "'?", "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            settings.Widgets.Remove(ws);
            overlays.Sync();
            overlays.ScheduleSave();
            RefreshList();
            if (WidgetList.Items.Count > 0) WidgetList.SelectedIndex = 0; else { SettingsHost.Children.Clear(); preview.SetWidget(null); position.SetWidget(null); }
        }
    }
}
