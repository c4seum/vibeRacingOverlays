using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
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
            LayoutRenameButton.Click += (s, e) => RenameLayout();
            LayoutDeleteButton.Click += (s, e) => DeleteLayout();
            LayoutPresetBox.SelectionChanged += (s, e) => { if (!loading) UpdatePresetButtons(); };
            PresetLoadButton.Click += (s, e) => LoadPreset();
            PresetSaveButton.Click += (s, e) => SavePreset();
            PresetSaveAsButton.Click += (s, e) => SavePresetAs();
            PresetRenameButton.Click += (s, e) => RenamePreset();
            PresetDeleteButton.Click += (s, e) => DeletePreset();
            LayoutExportButton.Click += (s, e) => ExportLayout();
            LayoutImportButton.Click += (s, e) => ImportLayout();
            // drop layout, preset or widget files on the window to import them
            DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += (s, e) => { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null) ImportFiles(files); };
            // preset files added or changed in the library folder (also while the app runs)
            if (Core.PresetLibrary.Current != null) Core.PresetLibrary.Current.Changed += () => { overlays.ScheduleSave(); ShowSelected(); };

            AddButton.Click += (s, e) => ShowAddMenu();
            DuplicateButton.Click += (s, e) => Duplicate();
            RemoveButton.Click += (s, e) => Remove();
            WidgetList.SelectionChanged += (s, e) => ShowSelected();

            FooterText.Text = "Edit layout (" + settings.HotkeyEditMode + "): drag widgets to move them (Shift: no snapping), mouse wheel to resize.   "
                + "Show/hide all: " + settings.HotkeyToggleOverlays + ".   Next layout: " + settings.HotkeyNextLayout
                + ".   Every change is kept automatically; layout presets are saved versions you load. Settings: " + AppSettings.Folder;

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
            statusTimer.Tick += (s, e) => { UpdateStatus(); UpdatePresetMark(); };
            statusTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ThemeManager.ApplyTitleBar(this);
            overlays.RegisterHotkeys(new WindowInteropHelper(this).Handle);
        }

        /// <summary>
        /// Stacked: preview on top, below it the settings with the position panel on their right.
        /// Side by side: settings left, splitter, preview right with the position panel below it.
        /// </summary>
        // fixed widths of the settings and position blocks (their rows don't stretch); set in MainWindow.xaml too
        // area = block + padding on both sides + the vertical scroll bar
        const double SidebarWidth = 250, SettingsArea = 620 + 2 * 20 + 10, PositionArea = 480 + 2 * 20 + 10, SplitterWidth = 4;
        const double WindowChrome = 16;   // resize borders of a normal window

        void ApplyEditorLayout()
        {
            bool side = settings.PreviewSideBySide;
            var cols = EditorGrid.ColumnDefinitions;
            var rows = EditorGrid.RowDefinitions;
            if (side)
            {
                // settings in a column of their own width (the splitter can only make it wider),
                // the right column holds the preview above the position panel, sharing the height
                cols[0].Width = new GridLength(SettingsArea);
                cols[0].MinWidth = SettingsArea;
                cols[1].Width = GridLength.Auto;
                cols[2].Width = new GridLength(1, GridUnitType.Star);
                cols[2].MinWidth = PositionArea;
                rows[0].Height = new GridLength(1, GridUnitType.Star);
                rows[0].MinHeight = 200;
                rows[1].Height = new GridLength(1, GridUnitType.Star);
                Place(SettingsScroll, 0, 0, 2, 1);
                Place(EditorSplitter, 0, 1, 2, 1);
                Place(PreviewHost, 0, 2, 1, 1);
                Place(PositionScroll, 1, 2, 1, 1);
                EditorSplitter.Visibility = Visibility.Visible;
            }
            else
            {
                // preview across the top; settings and position each get half of the rest, centred in it
                cols[0].Width = new GridLength(1, GridUnitType.Star);
                cols[0].MinWidth = SettingsArea;
                cols[1].Width = new GridLength(0);
                cols[2].Width = new GridLength(1, GridUnitType.Star);
                cols[2].MinWidth = PositionArea;
                rows[0].Height = GridLength.Auto;
                rows[0].MinHeight = 0;
                rows[1].Height = new GridLength(1, GridUnitType.Star);
                Place(PreviewHost, 0, 0, 1, 3);
                Place(SettingsScroll, 1, 0, 1, 1);
                Place(PositionScroll, 1, 2, 1, 1);
                EditorSplitter.Visibility = Visibility.Collapsed;
            }

            // the window can't get smaller than its content (limited to the screen, e.g. a laptop at 150% scaling)
            var work = SystemParameters.WorkArea;
            MinWidth = Math.Min(SidebarWidth + SettingsArea + PositionArea + (side ? SplitterWidth : 0) + WindowChrome, work.Width);
            MinHeight = Math.Min(side ? 640 : 760, work.Height);
            double target = Math.Min(Math.Max(MinWidth, side ? 1600 : 1480), work.Width - 40);
            if (WindowState == WindowState.Normal && Width < target)
            {
                Width = target;
                Left = Math.Max(work.Left, Math.Min(Left, work.Right - Width));
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
            // a failing save must never go unnoticed (layouts and presets would only live in memory)
            if (AppSettings.SaveProblem != null)
            {
                StatusDot.SetResourceReference(Shape.FillProperty, "Danger");
                StatusText.Text = "Settings can't be saved - see " + AppSettings.ErrorLog;
                StatusText.ToolTip = AppSettings.SaveProblem;
                return;
            }
            StatusText.ToolTip = null;
            if (src == "iRacing" && live)
            {
                StatusDot.SetResourceReference(Shape.FillProperty, "Success");
                StatusText.Text = snap.Connected && snap.Source == "iRacing"
                    ? "iRacing connected - " + snap.SessionType + " @ " + snap.TrackName
                    : "iRacing connected - loading session...";
            }
            else if (src == "Demo")
            {
                StatusDot.SetResourceReference(Shape.FillProperty, "Warning");
                StatusText.Text = "Demo race (iRacing not running)";
            }
            else
            {
                StatusDot.SetResourceReference(Shape.FillProperty, "Muted");
                StatusText.Text = "Waiting for iRacing...";
            }
        }

        static string TypeIcon(WidgetSettings w)
        {
            if (w is StandingsSettings) return Ui.IconList;
            if (w is RelativeSettings) return Ui.IconSort;
            if (w is FuelSettings) return Ui.IconFuel;
            return Ui.IconWidget;
        }

        void RefreshList()
        {
            var selected = WidgetList.SelectedItem as ListBoxItem;
            string selectedId = selected != null ? ((WidgetSettings)selected.Tag).Id : null;
            WidgetList.Items.Clear();
            foreach (var ws in settings.Widgets)
            {
                var w = ws;
                // only the switch shows / hides the widget; clicking the name selects it (opens its settings)
                var sw = Ui.Toggle(w.Enabled);
                sw.ToolTip = "Show / hide this widget";
                sw.Margin = new Thickness(8, 0, 0, 0);
                RoutedEventHandler onSwitch = (s, e) => { bool v = sw.IsChecked == true; if (w.Enabled != v) { w.Enabled = v; overlays.Invalidate(w); } };
                sw.Checked += onSwitch;
                sw.Unchecked += onSwitch;
                DockPanel.SetDock(sw, Dock.Right);
                var icon = new TextBlock { Text = TypeIcon(w), FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                icon.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                // display name (user editable) + the widget type, shown when the name differs from it
                var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                SetListName(name, w);
                var row = new DockPanel { Background = Brushes.Transparent, MinHeight = 26 };   // transparent = whole row is clickable
                row.Children.Add(sw);
                row.Children.Add(icon);
                row.Children.Add(name);
                var item = new ListBoxItem { Content = row, Tag = w };
                WidgetList.Items.Add(item);
                if (w.Id == selectedId) WidgetList.SelectedItem = item;
            }
        }

        // ---------------------------------------------------------------- layouts (always the current version)

        string shownLayoutId;

        void RefreshLayouts()
        {
            loading = true;
            LayoutBox.ItemsSource = null;
            LayoutBox.ItemsSource = settings.Layouts;
            LayoutBox.SelectedItem = settings.ActiveLayout;
            LayoutDeleteButton.IsEnabled = settings.Layouts.Count > 1;
            LayoutDeleteButton.ToolTip = settings.Layouts.Count > 1 ? "Delete this layout" : "The last layout can't be deleted";
            loading = false;
            RefreshPresets(null);
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

        void NewLayout()
        {
            string name = InputDialog.Ask(this, "New layout", "Name of the new, empty layout (load a preset into it, or add widgets):", UniqueLayoutName("Layout " + (settings.Layouts.Count + 1)));
            if (name == null) return;
            var l = new LayoutConfig { Name = UniqueLayoutName(name) };
            settings.Layouts.Add(l);
            overlays.SwitchLayout(l.Id);
            ShowActiveLayout();
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
            var l = settings.ActiveLayout;
            if (settings.Layouts.Count < 2) return;
            if (MessageBox.Show(this, "Delete layout '" + l.Name + "' and its " + l.Widgets.Count + " widgets?\n(Your layout presets stay.)", "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            int i = settings.Layouts.IndexOf(l);
            var next = settings.Layouts[i == 0 ? 1 : i - 1];
            overlays.SwitchLayout(next.Id);
            settings.Layouts.Remove(l);
            overlays.ScheduleSave();
            ShowActiveLayout();
        }

        // ---------------------------------------------------------------- layout presets (saved versions to load)

        LayoutPreset SelectedPreset { get { return LayoutPresetBox.SelectedItem as LayoutPreset; } }

        /// <summary>Fills the preset list; selects <paramref name="select"/>, else the one the active layout came from.</summary>
        void RefreshPresets(LayoutPreset select)
        {
            loading = true;
            var l = settings.ActiveLayout;
            var from = settings.LayoutPresetOf(l);
            bool differs = settings.DiffersFromPreset(l);
            var list = settings.LayoutPresets.OrderBy(p => p.IsBuiltIn ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var p in list) p.Label = p.Name + (p == from && differs ? " *" : "");
            LayoutPresetBox.ItemsSource = null;
            LayoutPresetBox.ItemsSource = list;
            LayoutPresetBox.SelectedItem = select ?? from ?? list.FirstOrDefault();
            presetMark = differs;
            loading = false;
            UpdatePresetButtons();
        }

        bool presetMark;

        /// <summary>Keeps the "*" up to date while you change the layout.</summary>
        void UpdatePresetMark()
        {
            if (LayoutPresetBox.IsDropDownOpen) return;
            if (settings.DiffersFromPreset(settings.ActiveLayout) != presetMark) RefreshPresets(SelectedPreset);
        }

        void UpdatePresetButtons()
        {
            var p = SelectedPreset;
            var l = settings.ActiveLayout;
            bool builtIn = p != null && p.IsBuiltIn;
            PresetLoadButton.IsEnabled = p != null;
            PresetLoadButton.ToolTip = p != null ? "Replace the widgets of layout '" + l.Name + "' with preset '" + p.Name + "'" : null;
            // Save writes the layout into the preset it came from (when that's the selected one); Get started stays as it is
            PresetSaveButton.IsEnabled = p != null && !builtIn;
            PresetSaveButton.ToolTip = builtIn ? "Get started stays as the app made it: use Save as" : p != null ? "Store layout '" + l.Name + "' in preset '" + p.Name + "'" : null;
            PresetRenameButton.IsEnabled = PresetDeleteButton.IsEnabled = p != null && !builtIn;
            PresetRenameButton.ToolTip = builtIn ? "Get started keeps its name" : "Rename this preset";
            PresetDeleteButton.ToolTip = builtIn ? "Get started can't be deleted" : "Delete this preset (your layouts stay)";
        }

        void LoadPreset()
        {
            var p = SelectedPreset;
            var l = settings.ActiveLayout;
            if (p == null) return;
            if (l.Widgets.Count > 0 && !(l.PresetId == p.Id && !settings.DiffersFromPreset(l))
                && MessageBox.Show(this, "Load preset '" + p.Name + "' into layout '" + l.Name + "'? Its current widgets are replaced.", "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            settings.LoadLayoutPreset(l, p);
            overlays.Sync();
            overlays.SaveNow();
            ShowActiveLayout();
        }

        void SavePreset()
        {
            var p = SelectedPreset;
            var l = settings.ActiveLayout;
            if (p == null || p.IsBuiltIn) return;
            if (l.PresetId != p.Id && MessageBox.Show(this, "Store layout '" + l.Name + "' in preset '" + p.Name + "'? The preset's widgets are replaced.", "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            p.Widgets = AppSettings.PresetWidgets(l);
            l.PresetId = p.Id;
            overlays.SaveNow();
            RefreshPresets(p);
        }

        void SavePresetAs()
        {
            var l = settings.ActiveLayout;
            string name = InputDialog.Ask(this, "Save layout preset as", "Store layout '" + l.Name + "' (all widgets, positions and settings) as a new preset:", settings.UniqueLayoutPresetName(l.Name, null));
            if (name == null) return;
            var p = new LayoutPreset { Name = settings.UniqueLayoutPresetName(name.Trim(), null), Widgets = AppSettings.PresetWidgets(l) };
            settings.LayoutPresets.Add(p);
            l.PresetId = p.Id;
            overlays.SaveNow();
            RefreshPresets(p);
        }

        void RenamePreset()
        {
            var p = SelectedPreset;
            if (p == null || p.IsBuiltIn) return;
            string name = InputDialog.Ask(this, "Rename layout preset", "New name for preset '" + p.Name + "':", p.Name);
            if (name == null || name.Trim() == p.Name) return;
            p.Name = settings.UniqueLayoutPresetName(name.Trim(), p);
            overlays.ScheduleSave();
            RefreshPresets(p);
        }

        void DeletePreset()
        {
            var p = SelectedPreset;
            if (p == null || p.IsBuiltIn) return;
            if (MessageBox.Show(this, "Delete layout preset '" + p.Name + "'? Your layouts stay as they are.", "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            settings.LayoutPresets.Remove(p);
            foreach (var l in settings.Layouts) if (l.PresetId == p.Id) l.PresetId = null;
            overlays.ScheduleSave();
            RefreshPresets(null);
        }
        // ---------------------------------------------------------------- export / import

        // exported layouts go next to the preset library (Documents\vibeRacingOverlays\layouts)
        static string ExportFolder { get { var d = System.IO.Path.Combine(Core.Exchange.LibraryFolder, "layouts"); System.IO.Directory.CreateDirectory(d); return d; } }

        string AskSavePath(string title, string filter, string fileName)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = fileName, InitialDirectory = ExportFolder, AddExtension = true };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        }

        string AskOpenPath(string title, string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, InitialDirectory = ExportFolder };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        }

        void ExportLayout()
        {
            var l = settings.ActiveLayout;
            string path = AskSavePath("Export layout", Core.Exchange.LayoutFilter, Core.Exchange.SafeName(l.Name) + ".vrolayout.json");
            if (path == null) return;
            try { Core.Exchange.ExportLayout(l, path); }
            catch (Exception ex) { MessageBox.Show(this, "The layout could not be exported:\n" + ex.Message, "vibeRacingOverlays", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        void ImportLayout()
        {
            string path = AskOpenPath("Import layout", Core.Exchange.LayoutFilter);
            if (path != null) ImportFiles(new[] { path });
        }

        /// <summary>
        /// Imports layout files (each as a new layout) and preset / widget files (each as a new preset, loaded into the
        /// selected widget when it's the same type). Used by Import and by dropping files on the window.
        /// </summary>
        public void ImportFiles(IEnumerable<string> paths)
        {
            var report = new List<string>();
            LayoutPreset lastLayout = null;
            WidgetPreset lastPreset = null;
            foreach (var path in paths)
            {
                string name = System.IO.Path.GetFileName(path);
                try
                {
                    string kind = Core.Exchange.Kind(path);
                    if (kind == "layout")
                    {
                        int skipped;
                        var l = Core.Exchange.ImportLayout(path, settings, out skipped);
                        var lp = new LayoutPreset { Name = settings.UniqueLayoutPresetName(l.Name, null), Widgets = l.Widgets };
                        settings.LayoutPresets.Add(lp);
                        lastLayout = lp;
                        report.Add("Layout preset '" + lp.Name + "' (" + lp.Widgets.Count + " widgets" + (skipped > 0 ? ", " + skipped + " of an unknown type skipped" : "") + "): select it and press Load");
                    }
                    else if (kind == "preset")
                    {
                        var p = Core.Exchange.ReadPreset(path);
                        if (p == null) { report.Add(name + ": a widget type this version doesn't know"); continue; }
                        p = settings.AddImportedPreset(p);
                        lastPreset = p;
                        report.Add(p.TypeName + " preset '" + p.Name + "'");
                    }
                    else report.Add(name + ": not a vibeRacingOverlays layout, preset or widget file");
                }
                catch (Exception ex) { report.Add(name + ": " + ex.Message); }
            }
            if (lastPreset != null)
            {
                if (Core.PresetLibrary.Current != null) Core.PresetLibrary.Current.Mirror();
                // load it into the selected widget when that's the same type
                var ws = Selected;
                if (ws != null && ws.GetType() == lastPreset.Settings.GetType() && lastLayout == null)
                    SettingsPanel.Load(ws, lastPreset);
            }
            if (lastLayout != null) RefreshPresets(lastLayout);
            ShowSelected();
            overlays.SaveNow();
            if (report.Count > 0)
                MessageBox.Show(this, "Imported:\n- " + string.Join("\n- ", report), "vibeRacingOverlays", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    if (item.Tag == ws) SetListName((TextBlock)((DockPanel)item.Content).Children[2], ws);
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
                    var ws = settings.NewWidget(e.Make().GetType());   // Default preset, centre of the main screen
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
