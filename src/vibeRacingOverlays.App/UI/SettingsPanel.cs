using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.App.Widgets;

namespace vibeRacingOverlays.App.UI
{
    /// <summary>
    /// Builds the settings editor for a widget from its [Setting] attributes, with reset-to-default
    /// buttons per setting, per group and for the whole widget. Defaults come from a freshly
    /// constructed settings object of the same widget type.
    /// </summary>
    public static class SettingsPanel
    {
        /// <summary>App settings (holds the widget presets); set by the main window.</summary>
        public static AppSettings App;

        /// <summary>Session kind being edited (P&amp;Q or race) for widgets with session profiles; the preview follows it.</summary>
        public static SessionKind Kind = SessionKind.Race;
        public static event Action KindChanged;

        public static void Build(Panel host, WidgetSettings ws, Action changed, Action titleChanged)
        {
            new Editor(host, ws, changed, titleChanged).Render();
        }

        /// <summary>Settings that describe the widget instance itself; a widget reset keeps them.</summary>
        static readonly HashSet<string> InstanceProps = new HashSet<string> { "Id", "Title", "Enabled", "X", "Y", "Screen", "Anchor", "OffsetX", "OffsetY", "Locked" };

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        sealed class Editor
        {
            readonly Panel host;
            readonly WidgetSettings ws;
            readonly WidgetSettings defaults;
            readonly Action changed;
            readonly Action titleChanged;
            // re-evaluates the enabled state of all reset buttons after every change
            readonly List<Action> refreshers = new List<Action>();

            public Editor(Panel host, WidgetSettings ws, Action changed, Action titleChanged)
            {
                this.host = host;
                this.ws = ws;
                this.changed = changed;
                this.titleChanged = titleChanged;
                // the type's Default preset (falls back to the built-in defaults); the reset buttons go back to it
                defaults = App != null ? App.DefaultsFor(ws.GetType()) : Core.Defaults.Preset(ws.GetType());
            }

            void Changed()
            {
                changed();
                foreach (var r in refreshers) r();
            }

            /// <summary>Redraws the panel (after a reset) without losing the scroll position.</summary>
            void Rebuild()
            {
                ScrollViewer sv = null;
                DependencyObject d = host;
                while (d != null && sv == null) { d = VisualTreeHelper.GetParent(d); sv = d as ScrollViewer; }
                double offset = sv != null ? sv.VerticalOffset : 0;
                Build(host, ws, changed, titleChanged);
                if (sv != null) host.Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(offset)), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            public void Render()
            {
                host.Children.Clear();

                // first card: the widget itself (type, display name, preset) with "Reset widget"
                var resetAll = Ui.HeaderReset("Reset all settings of this widget to the defaults (name and position are kept)");
                ((StackPanel)resetAll.Content).Children.OfType<TextBlock>().Last().Text = "Reset widget";
                resetAll.Click += (s, e) => ResetWidget();
                Track(resetAll, () => AllProps(ws).All(p => IsDefault(p, ws, defaults)) && ColumnsAreDefault());
                var widgetCard = Ui.Card(host, Ui.Header(ws.TypeName + " widget", resetAll));
                var title = new TextBox { Text = ws.Title, FontWeight = FontWeights.SemiBold, ToolTip = "Only changes the name shown in the app; it stays a " + ws.TypeName + " widget." };
                title.TextChanged += (s, e) => { ws.Title = string.IsNullOrWhiteSpace(title.Text) ? ws.TypeName : title.Text; titleChanged(); changed(); };
                Ui.Row(widgetCard, "Display name", title, "Only changes the name shown in the app", null);
                if (App != null) Ui.Row(widgetCard, "Preset", PresetRow(), "Saved settings of this widget type, available in every layout", null);

                var profiles = ws as ISessionProfiles;
                if (profiles != null)
                {
                    object profile = profiles.Profile(Kind), defProfile = ((ISessionProfiles)defaults).Profile(Kind);
                    SessionCard(profiles, profile);
                    RenderProps(profile, defProfile);
                    RenderLists(profile, defProfile);
                }
                RenderProps(ws, defaults);
                RenderLists(ws, defaults);
            }

            static string KindName(SessionKind k) { return k == SessionKind.Race ? "Race" : "Practice & qualifying"; }

            PropertyInfo ProfileProp(object profile) { return ws.GetType().GetProperties().First(p => p.GetIndexParameters().Length == 0 && ReferenceEquals(p.GetValue(ws), profile)); }

            /// <summary>P&amp;Q / Race switch: the cards below edit the content of that session kind; the style is shared.</summary>
            void SessionCard(ISessionProfiles profiles, object profile)
            {
                var prop = ProfileProp(profile);
                var reset = Ui.HeaderReset("Reset all " + KindName(Kind).ToLowerInvariant() + " settings (rows, columns, header) to the defaults");
                ((StackPanel)reset.Content).Children.OfType<TextBlock>().Last().Text = Kind == SessionKind.Race ? "Reset race" : "Reset P&Q";
                reset.Click += (s, e) => { ResetProp(prop, ws, defaults); changed(); Rebuild(); };
                Track(reset, () => IsDefault(prop, ws, defaults));
                var card = Ui.Card(host, Ui.Header("Session", reset));

                var kinds = new object[] { KindName(SessionKind.PracticeQualify), KindName(SessionKind.Race) };
                var seg = Ui.Segmented(kinds, KindName(Kind), v =>
                {
                    var k = (string)v == KindName(SessionKind.Race) ? SessionKind.Race : SessionKind.PracticeQualify;
                    if (k == Kind) return;
                    Kind = k;
                    if (KindChanged != null) KindChanged();
                    host.Dispatcher.BeginInvoke(new Action(Rebuild));   // not while the segment is still handling its click
                });
                Ui.Row(card, "Settings for", seg, "Rows, columns and header can differ per session type; the style (colors, size) is shared", null);

                var other = Kind == SessionKind.Race ? SessionKind.PracticeQualify : SessionKind.Race;
                var copy = new Button { Content = "Copy to " + KindName(other).ToLowerInvariant(), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Use these rows, columns and header also for " + KindName(other).ToLowerInvariant() };
                copy.Click += (s, e) =>
                {
                    var owner = Window.GetWindow(host);
                    if (MessageBox.Show(owner, "Replace the " + KindName(other).ToLowerInvariant() + " settings with these " + KindName(Kind).ToLowerInvariant() + " settings?",
                        "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    var target = ProfileProp(profiles.Profile(other));
                    var clone = AppSettings.CloneValue(profile, profile.GetType());
                    var kindProp = clone.GetType().GetProperty("Kind");
                    if (kindProp != null) kindProp.SetValue(clone, other);
                    target.SetValue(ws, clone);
                    var norm = ws as INormalizable;
                    if (norm != null) norm.Normalize();   // columns that only exist for the other session kind
                    changed();
                    Rebuild();
                };
                Ui.Row(card, "Copy", copy, null, null);
            }

            /// <summary>One card per [Setting] group of <paramref name="t"/> (the widget or its session profile).</summary>
            void RenderProps(object t, object d)
            {
                var props = t.GetType().GetProperties()
                    .Select(p => new { P = p, A = p.GetCustomAttribute<SettingAttribute>() })
                    .Where(x => x.A != null)
                    .OrderBy(x => GroupRank(x.A.Group)).ThenBy(x => x.A.Order)
                    .ToList();

                foreach (var group in props.GroupBy(x => x.A.Group))
                {
                    var groupProps = group.Select(x => x.P).ToList();
                    var card = Ui.Card(host, Header(group.Key, () => groupProps.All(p => IsDefault(p, t, d)), () => { foreach (var p in groupProps) ResetProp(p, t, d); }));
                    foreach (var x in group)
                    {
                        var p = x.P;
                        var reset = ResetButton(() => IsDefault(p, t, d), () => ResetProp(p, t, d), "Reset to default (" + Describe(p.GetValue(d)) + ")");
                        Ui.Row(card, x.A.Label, CreateEditor(p, x.A, t), x.A.Tooltip, reset);
                    }
                }
            }

            /// <summary>The column list and the header item list of <paramref name="t"/>, if it has them.</summary>
            void RenderLists(object t, object d)
            {
                var table = t as ITableSettings;
                if (table != null)
                {
                    var defTable = (ITableSettings)d;
                    var card = Ui.Card(host, Header("Columns", () => ListIsDefault(table.Columns, defTable.Columns), () => table.Columns = CopyList(defTable.Columns)));
                    AddHint(card, table.AvailableColumns.Any(c => c.Formats != null) ? "Order, on / off and format of the columns. Widths follow the content and font size; only the driver name has a width (px) of its own." : "Order and on / off of the columns. Widths follow the content and font size; only the driver name has a width (px) of its own.");
                    card.Children.Add(new ListEditor(table.Columns, table.AvailableColumns, true, Changed, this));   // rows add the card inset themselves
                }
                var header = t as IHeaderItems;
                if (header != null)
                {
                    var defHeader = (IHeaderItems)d;
                    var card = Ui.Card(host, Header("Header items", () => ListIsDefault(header.Header, defHeader.Header), () => header.Header = CopyList(defHeader.Header)));
                    AddHint(card, "Order, on / off and format of the header bar. Items after \"Push what follows to the right\" are right-aligned.");
                    card.Children.Add(new ListEditor(header.Header, header.AvailableHeader, false, Changed, this));
                }
            }

            static void AddHint(Panel card, string text)
            {
                var hint = Ui.Caption(text);
                hint.TextWrapping = TextWrapping.Wrap;
                hint.Margin = new Thickness(14, 0, 10, 4);
                card.Children.Add(hint);
            }

            // ------------------------------------------------------------ presets

            /// <summary>Presets of this widget type: Default first, then by name.</summary>
            List<WidgetPreset> PresetsOfType()
            {
                return App.Presets.Where(p => p.TypeName == ws.TypeName)
                    .OrderBy(p => p.IsDefault ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }

            /// <summary>The preset this widget follows (its last loaded / saved one; Default when it has none).</summary>
            WidgetPreset Current()
            {
                return App.Presets.FirstOrDefault(p => p.Id == ws.PresetId && p.TypeName == ws.TypeName) ?? App.DefaultPreset(ws.GetType());
            }

            bool Modified() { var c = Current(); return c != null && !AppSettings.SameSettings(ws, c.Settings); }

            sealed class PresetItem
            {
                public WidgetPreset Preset;
                public string Label;
                public override string ToString() { return Label; }
            }

            /// <summary>
            /// Preset combo (choosing one loads it) with Save / Save as / Rename / Delete. "*" = the widget differs
            /// from its preset. Default can't be renamed or deleted; its delete button restores the factory settings.
            /// </summary>
            FrameworkElement PresetRow()
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var combo = new ComboBox { Width = 132, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Saved " + ws.TypeName + " presets (every layout). Choosing one loads it. * = changed since loaded or saved" };
                var save = new Button { Content = "Save", Margin = new Thickness(6, 0, 0, 0) };
                var saveAs = new Button { Content = "Save as", Margin = new Thickness(4, 0, 0, 0), ToolTip = "Save this widget's settings as a new preset" };
                var rename = new Button { Content = "Rename", Margin = new Thickness(4, 0, 0, 0) };
                var del = Ui.IconButton(Ui.IconDelete, "");
                del.Margin = new Thickness(2, 0, 0, 0);
                bool updating = false;

                Action refresh = () =>
                {
                    updating = true;
                    var cur = Current();
                    bool mod = Modified();
                    var items = PresetsOfType().Select(p => new PresetItem { Preset = p, Label = p.Name + (p == cur && mod ? " *" : "") }).ToList();
                    combo.ItemsSource = items;
                    combo.SelectedItem = items.FirstOrDefault(i => i.Preset == cur);
                    bool isDefault = cur != null && cur.IsDefault;
                    save.IsEnabled = mod;
                    save.ToolTip = isDefault && Core.Defaults.SourceFile != null
                        ? "Store this widget's settings in the Default preset. DEV build: they also become the app's built-in defaults (defaults.json)"
                        : isDefault ? "Store this widget's settings in the Default preset: new " + ws.TypeName + " widgets and the reset buttons use them"
                        : "Store this widget's settings in preset '" + (cur != null ? cur.Name : "") + "'";
                    rename.IsEnabled = !isDefault;
                    rename.ToolTip = isDefault ? "The Default preset keeps its name" : "Rename this preset";
                    del.ToolTip = isDefault ? "Restore the factory settings of the Default preset" : "Delete this preset";
                    del.Content = isDefault ? Ui.IconReset : Ui.IconDelete;
                    updating = false;
                };
                refresh();
                refreshers.Add(refresh);

                combo.SelectionChanged += (s, e) =>
                {
                    var item = combo.SelectedItem as PresetItem;
                    if (updating || item == null || item.Preset == Current() && !Modified()) return;
                    var owner = Window.GetWindow(host);
                    if (Modified() && MessageBox.Show(owner, "Load preset '" + item.Preset.Name + "'? The changes to this widget since its last save are lost.",
                        "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { refresh(); return; }
                    ApplyFrom(AppSettings.CloneWidget(item.Preset.Settings));
                    ws.PresetId = item.Preset.Id;
                    changed();
                    Rebuild();
                };
                save.Click += (s, e) => SavePreset(Current());
                saveAs.Click += (s, e) => SavePresetAs();
                rename.Click += (s, e) => RenamePreset(Current());
                del.Click += (s, e) => { var c = Current(); if (c != null && c.IsDefault) FactoryReset(c); else DeletePreset(c); };

                row.Children.Add(combo);
                row.Children.Add(save);
                row.Children.Add(saveAs);
                row.Children.Add(rename);
                row.Children.Add(del);
                return row;
            }

            WidgetSettings Snapshot()
            {
                var snap = AppSettings.CloneWidget(ws);
                snap.PresetId = null;
                return snap;
            }

            void AfterPresetChange(WidgetPreset p)
            {
                // the Default preset is the app's default set: DEV builds write it into the built-in defaults
                if (p != null && p.IsDefault) Core.Defaults.Export(App);
                changed();
                Rebuild();   // new defaults for the reset buttons
            }

            void SavePreset(WidgetPreset p)
            {
                if (p == null) { SavePresetAs(); return; }
                p.Settings = Snapshot();
                ws.PresetId = p.Id;
                AfterPresetChange(p);
            }

            string UniquePresetName(string name, WidgetPreset except)
            {
                string n = name;
                for (int i = 2; PresetsOfType().Any(p => p != except && string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)); i++) n = name + " (" + i + ")";
                return n;
            }

            void SavePresetAs()
            {
                var owner = Window.GetWindow(host);
                string name = InputDialog.Ask(owner, "Save preset as", "Save the settings of this " + ws.TypeName + " widget as a new preset:",
                    UniquePresetName(ws.TypeName + " preset " + PresetsOfType().Count, null));
                if (name == null || string.IsNullOrWhiteSpace(name)) return;
                var p = new WidgetPreset { Name = UniquePresetName(name.Trim(), null), Settings = Snapshot() };
                if (string.Equals(p.Name, "Default", StringComparison.OrdinalIgnoreCase)) p.Name = UniquePresetName("Default copy", null);
                App.Presets.Add(p);
                ws.PresetId = p.Id;
                AfterPresetChange(p);
            }

            void RenamePreset(WidgetPreset p)
            {
                if (p == null || p.IsDefault) return;
                var owner = Window.GetWindow(host);
                string name = InputDialog.Ask(owner, "Rename preset", "New name for preset '" + p.Name + "':", p.Name);
                if (name == null || string.IsNullOrWhiteSpace(name) || name == p.Name) return;
                name = name.Trim();
                p.Name = string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ? UniquePresetName("Default copy", p) : UniquePresetName(name, p);
                changed();
                Rebuild();
            }

            void DeletePreset(WidgetPreset p)
            {
                if (p == null || p.IsDefault) return;
                var owner = Window.GetWindow(host);
                if (MessageBox.Show(owner, "Delete preset '" + p.Name + "'?\nWidgets that use it keep their settings and follow the Default preset from now on.",
                    "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                App.Presets.RemoveAll(x => x == p);
                var def = App.DefaultPreset(ws.GetType());
                foreach (var w in App.Layouts.SelectMany(l => l.Widgets.Concat(l.Saved ?? new List<WidgetSettings>())))
                    if (w.PresetId == p.Id) w.PresetId = def != null ? def.Id : null;
                changed();
                Rebuild();
            }

            /// <summary>Default preset back to the factory settings (DEV: the code defaults; releases: the built-in defaults), also for this widget.</summary>
            void FactoryReset(WidgetPreset p)
            {
                var owner = Window.GetWindow(host);
                if (MessageBox.Show(owner, "Restore the factory settings of the " + ws.TypeName + " Default preset, and load them into this widget?",
                    "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                var factory = BuildInfo.IsDev ? (WidgetSettings)Activator.CreateInstance(ws.GetType()) : Core.Defaults.Preset(ws.GetType());
                p.Settings = AppSettings.CloneWidget(factory);
                ApplyFrom(AppSettings.CloneWidget(p.Settings));
                ws.PresetId = p.Id;
                AfterPresetChange(p);
            }

            /// <summary>Copies all widget settings from another settings object; instance settings (name, position...) are kept.</summary>
            void ApplyFrom(WidgetSettings source)
            {
                foreach (var p in AllProps(ws)) p.SetValue(ws, p.GetValue(source));
                var t = ws as ITableSettings;
                if (t != null) { t.Columns = ((ITableSettings)source).Columns; t.MergeColumns(); }
                var norm = ws as INormalizable;
                if (norm != null) norm.Normalize();
            }

            void ResetToDefaults()
            {
                foreach (var p in AllProps(ws)) ResetProp(p, ws, defaults);
                var t = ws as ITableSettings;
                if (t != null) t.Columns = CopyList(((ITableSettings)defaults).Columns);
            }

            // ------------------------------------------------------------ defaults / reset

            /// <summary>Settings of <paramref name="t"/> that a reset covers (the lists have their own reset; session profiles are compared deep).</summary>
            static IEnumerable<PropertyInfo> AllProps(object t)
            {
                return t.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite && !InstanceProps.Contains(p.Name)
                    && p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() == null
                    && p.Name != "Columns" && p.Name != "Header" && p.Name != "Kind");
            }

            static bool IsSimple(Type t) { return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal); }

            static bool IsDefault(PropertyInfo p, object t, object d)
            {
                object a = p.GetValue(t), b = p.GetValue(d);
                if (a is string && b is string) return string.Equals(((string)a).Trim(), (string)b, StringComparison.OrdinalIgnoreCase);
                if (IsSimple(p.PropertyType)) return Equals(a, b);
                return AppSettings.ToJson(a, p.PropertyType) == AppSettings.ToJson(b, p.PropertyType);
            }

            /// <summary>Sets the default; complex values are copied so the widget never shares an object with the defaults.</summary>
            static void ResetProp(PropertyInfo p, object t, object d)
            {
                object v = p.GetValue(d);
                p.SetValue(t, IsSimple(p.PropertyType) ? v : AppSettings.CloneValue(v, p.PropertyType));
            }

            /// <summary>A copy of a default list, owned only by this widget (never shared with another widget or layout).</summary>
            static List<ColumnConfig> CopyList(List<ColumnConfig> list)
            {
                return list.Select(c => new ColumnConfig { Key = c.Key, Enabled = c.Enabled, Width = c.Width, Format = c.Format }).ToList();
            }

            static bool ListIsDefault(List<ColumnConfig> a, List<ColumnConfig> d)
            {
                if (a.Count != d.Count) return false;
                for (int i = 0; i < d.Count; i++)
                    if (a[i].Key != d[i].Key || a[i].Enabled != d[i].Enabled || a[i].Width != d[i].Width || a[i].Format != d[i].Format) return false;
                return true;
            }

            bool ColumnsAreDefault()
            {
                var t = ws as ITableSettings;
                return t == null || ListIsDefault(t.Columns, ((ITableSettings)defaults).Columns);
            }

            void ResetWidget()
            {
                var owner = Window.GetWindow(host);
                if (MessageBox.Show(owner, "Reset all settings of '" + ws.Title + "' to the defaults?\nThe name and position are kept.",
                    "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                ResetToDefaults();
                changed();
                Rebuild();
            }

            /// <summary>Small reset button, enabled only while the value differs from the default.</summary>
            public Button ResetButton(Func<bool> isDefault, Action reset, string tooltip)
            {
                var b = Ui.RowReset(tooltip);
                b.Click += (s, e) => { reset(); changed(); Rebuild(); };
                Track(b, isDefault);
                return b;
            }

            void Track(Button b, Func<bool> isDefault)
            {
                Action refresh = () => b.IsEnabled = !isDefault();
                refresh();
                refreshers.Add(refresh);
            }

            static string Describe(object v)
            {
                if (v is double) return ((double)v).ToString("0.###", Inv);
                if (v is bool) return (bool)v ? "on" : "off";
                return Convert.ToString(v, Inv);
            }

            // ------------------------------------------------------------ layout helpers

            /// <summary>Card header: group name plus "Reset" (enabled while any setting of the group differs from its default).</summary>
            UIElement Header(string text, Func<bool> isDefault, Action reset)
            {
                var b = Ui.HeaderReset("Reset all " + text.ToLowerInvariant() + " settings to the defaults");
                b.Click += (s, e) => { reset(); changed(); Rebuild(); };
                Track(b, isDefault);
                return Ui.Header(text, b);
            }

            // ------------------------------------------------------------ editors

            UIElement CreateEditor(PropertyInfo p, SettingAttribute a, object ws)
            {
                var t = p.PropertyType;
                if (t == typeof(bool))
                {
                    var sw = Ui.Toggle((bool)p.GetValue(ws));
                    // Checked/Unchecked instead of Click: also fires for keyboard and UI Automation
                    RoutedEventHandler onSwitch = (s, e) => { bool v = sw.IsChecked == true; if ((bool)p.GetValue(ws) != v) { p.SetValue(ws, v); Changed(); } };
                    sw.Checked += onSwitch;
                    sw.Unchecked += onSwitch;
                    return sw;
                }
                if (t.IsEnum)
                {
                    var values = Enum.GetValues(t).Cast<object>().ToList();
                    // a few short choices: segmented control; otherwise a drop-down
                    if (values.Count <= 3 && values.All(v => Ui.Label(v).Length <= 14))
                        return Ui.Segmented(values, p.GetValue(ws), v => { if (!Equals(p.GetValue(ws), v)) { p.SetValue(ws, v); Changed(); } });
                    var items = values.Select(v => new Ui.EnumItem { Value = v }).ToList();
                    var combo = new ComboBox { ItemsSource = items, SelectedItem = items.First(i => Equals(i.Value, p.GetValue(ws))), Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
                    combo.SelectionChanged += (s, e) => { var i = combo.SelectedItem as Ui.EnumItem; if (i != null) { p.SetValue(ws, i.Value); Changed(); } };
                    return combo;
                }
                if (t == typeof(int) || t == typeof(double)) return NumberEditor(p, a, ws);
                if (t == typeof(string) && a.IsColor) return ColorEditor(p, ws);

                var tb = new TextBox { Text = Convert.ToString(p.GetValue(ws)), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
                tb.TextChanged += (s, e) => { p.SetValue(ws, tb.Text); Changed(); };
                return tb;
            }

            UIElement NumberEditor(PropertyInfo p, SettingAttribute a, object ws)
            {
                bool isInt = p.PropertyType == typeof(int);
                double value = Convert.ToDouble(p.GetValue(ws), Inv);
                var panel = new DockPanel();
                var box = new TextBox { Width = 64, Text = Format(value, a.Step), Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(box, Dock.Right);
                var slider = new Slider
                {
                    Minimum = a.Min, Maximum = Math.Max(a.Max, value), Value = value, SmallChange = a.Step, LargeChange = a.Step * 5,
                    TickFrequency = a.Step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center,
                };
                bool updating = false;
                Action<double> apply = v =>
                {
                    if (isInt) p.SetValue(ws, (int)Math.Round(v)); else p.SetValue(ws, Math.Round(v, 4));
                    Changed();
                };
                slider.ValueChanged += (s, e) =>
                {
                    if (updating) return;
                    updating = true; box.Text = Format(slider.Value, a.Step); updating = false;
                    apply(slider.Value);
                };
                box.LostFocus += (s, e) =>
                {
                    double v;
                    if (!double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, Inv, out v)) { box.Text = Format(slider.Value, a.Step); return; }
                    updating = true;
                    if (v > slider.Maximum) slider.Maximum = v;
                    slider.Value = v; box.Text = Format(v, a.Step);
                    updating = false;
                    apply(v);
                };
                panel.Children.Add(box);
                panel.Children.Add(slider);
                return panel;
            }

            static string Format(double v, double step)
            {
                int decimals = step >= 1 ? 0 : step >= 0.1 ? 1 : step >= 0.01 ? 2 : 3;
                return v.ToString("F" + decimals, Inv);
            }

            UIElement ColorEditor(PropertyInfo p, object ws)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                var swatch = new Border { Width = 34, Height = Ui.ControlHeight, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Pick a color" };
                swatch.SetResourceReference(Border.BorderBrushProperty, "Border");
                var box = new TextBox { Width = 104, Text = (string)p.GetValue(ws), FontFamily = new FontFamily("Consolas") };
                Action refresh = () =>
                {
                    uint c = Argb.Parse(box.Text, 0);
                    swatch.Background = new SolidColorBrush(Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c));
                };
                refresh();
                box.TextChanged += (s, e) => { p.SetValue(ws, box.Text); refresh(); Changed(); };
                swatch.MouseLeftButtonUp += (s, e) =>
                {
                    uint c = Argb.Parse(box.Text, 0xFFFFFFFF);
                    using (var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb((int)(c | 0xFF000000)) })
                    {
                        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                        uint rgb = (uint)dlg.Color.ToArgb() & 0xFFFFFF;
                        box.Text = Argb.ToHex((c & 0xFF000000) | rgb);   // keep the alpha the user had
                    }
                };
                panel.Children.Add(swatch);
                panel.Children.Add(box);
                var hint = Ui.Caption("#AARRGGBB");
                hint.VerticalAlignment = VerticalAlignment.Center;
                hint.Margin = new Thickness(8, 0, 0, 0);
                panel.Children.Add(hint);
                return panel;
            }

            // ------------------------------------------------------------ columns

            /// <summary>
            /// Column (or header item) list with on/off, width, format, ordering and a reset per entry.
            /// The format choices show an example of the result ("1:35.764", "4.5k"...).
            /// </summary>
            sealed class ListEditor : StackPanel
            {
                readonly List<ColumnConfig> list;
                readonly IReadOnlyList<ColumnDef> defs;
                readonly bool widths;
                readonly Action changed;
                readonly Editor owner;

                public ListEditor(List<ColumnConfig> list, IReadOnlyList<ColumnDef> defs, bool widths, Action changed, Editor owner)
                {
                    this.list = list;
                    this.defs = defs;
                    this.widths = widths;
                    this.changed = changed;
                    this.owner = owner;
                    Build();
                }

                void Build()
                {
                    Children.Clear();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var col = list[i];
                        var def = defs.First(d => d.Key == col.Key);
                        int index = i;

                        // same height, inset and dividers as every other card row; order buttons first, then the check box
                        var g = Ui.ListRow(this, GridLength.Auto, new GridLength(Ui.LabelWidth + 30), new GridLength(64), new GridLength(150), new GridLength(1, GridUnitType.Star));

                        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                        var up = Ui.IconButton(Ui.IconUp, "Move up");
                        up.IsEnabled = i > 0;
                        var down = Ui.IconButton(Ui.IconDown, "Move down");
                        down.IsEnabled = i < list.Count - 1;
                        up.Click += (s, e) => Move(index, -1);
                        down.Click += (s, e) => Move(index, 1);
                        buttons.Children.Add(up);
                        buttons.Children.Add(down);
                        g.Children.Add(buttons);

                        var cb = new CheckBox { Content = def.Label, IsChecked = col.Enabled, VerticalAlignment = VerticalAlignment.Center };
                        cb.Click += (s, e) => { col.Enabled = cb.IsChecked == true; changed(); };
                        Grid.SetColumn(cb, 1);
                        g.Children.Add(cb);

                        // only the driver name has a width of its own; the other columns fit their content
                        bool hasWidth = widths && def.Resizable;
                        if (hasWidth)
                        {
                            var width = new TextBox { Text = col.Width.ToString(CultureInfo.InvariantCulture), Width = 56, ToolTip = "Width (px)", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                            width.LostFocus += (s, e) =>
                            {
                                float w;
                                if (float.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out w) && w > 0) { col.Width = w; changed(); }
                                else width.Text = col.Width.ToString(CultureInfo.InvariantCulture);
                            };
                            Grid.SetColumn(width, 2);
                            g.Children.Add(width);
                        }

                        if (def.Formats != null && def.Formats.Length > 1)
                        {
                            var combo = new ComboBox { ItemsSource = def.Formats.Select(f => f.Label).ToList(), Width = 142, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Format" };
                            combo.SelectedIndex = Math.Max(0, Array.FindIndex(def.Formats, f => f.Key == (col.Format ?? def.DefaultFormat)));
                            combo.SelectionChanged += (s, e) => { if (combo.SelectedIndex >= 0) { col.Format = def.Formats[combo.SelectedIndex].Key; changed(); } };
                            Grid.SetColumn(combo, 3);
                            g.Children.Add(combo);
                        }

                        bool hasFormat = def.Formats != null && def.Formats.Length > 1;
                        string defFormat = hasFormat ? def.Formats.First(f => f.Key == def.DefaultFormat).Label : null;
                        var reset = owner.ResetButton(
                            () => col.Enabled == def.DefaultOn && (!hasWidth || col.Width == def.Width) && (!hasFormat || col.Format == def.DefaultFormat),
                            () => { col.Enabled = def.DefaultOn; col.Width = def.Width; col.Format = def.DefaultFormat; },
                            "Reset to default (" + (def.DefaultOn ? "on" : "off") + (hasWidth ? ", " + def.Width.ToString(CultureInfo.InvariantCulture) + " px" : "")
                                + (defFormat != null ? ", " + defFormat : "") + ")");
                        reset.HorizontalAlignment = HorizontalAlignment.Right;
                        reset.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetColumn(reset, 4);
                        g.Children.Add(reset);
                    }
                }

                void Move(int index, int delta)
                {
                    int j = index + delta;
                    if (j < 0 || j >= list.Count) return;
                    var c = list[index];
                    list.RemoveAt(index);
                    list.Insert(j, c);
                    changed();
                    Build();
                }
            }
        }

        static int GroupRank(string g)
        {
            switch (g) { case "Content": return 0; case "Single class": return 1; case "Multiclass": return 2; case "Layout": return 3; case "Style": return 4; default: return 5; }
        }
    }
}
