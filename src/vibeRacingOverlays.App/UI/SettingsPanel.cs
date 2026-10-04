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

        /// <summary>Copies all widget settings from another settings object; instance settings (name, position...) are kept.</summary>
        static void ApplyTo(WidgetSettings ws, WidgetSettings source)
        {
            foreach (var p in Editor.SettingsOf(ws)) p.SetValue(ws, p.GetValue(source));
            var t = ws as ITableSettings;
            if (t != null) { t.Columns = ((ITableSettings)source).Columns; t.MergeColumns(); }
            var norm = ws as INormalizable;
            if (norm != null) norm.Normalize();
        }

        /// <summary>Loads a preset into a widget (its name and position stay).</summary>
        public static void Load(WidgetSettings ws, WidgetPreset p)
        {
            ApplyTo(ws, AppSettings.CloneWidget(p.Settings));
            ws.PresetId = p.Id;
        }

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
                // when the widget shows and how often it redraws: with its name (user's choice, 2026-10-04)
                AddRows(widgetCard, ws, defaults, SettingProps(ws).Where(x => x.A.Group == "Widget"));

                // the cards of the session profile (Standings) and of the widget, in one order for every widget type:
                // rows, columns, header bar, text & size, colors
                var cards = new List<(int Rank, Action Draw)>();
                var profiles = ws as ISessionProfiles;
                if (profiles != null)
                {
                    object profile = profiles.Profile(Kind), defProfile = ((ISessionProfiles)defaults).Profile(Kind);
                    SessionCard(profiles, profile);
                    CollectCards(cards, profile, defProfile);
                }
                CollectCards(cards, ws, defaults);
                foreach (var c in cards.OrderBy(c => c.Rank)) c.Draw();   // stable: profile cards before widget cards of the same rank
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

            sealed class SettingProp { public PropertyInfo P; public SettingAttribute A; }

            static List<SettingProp> SettingProps(object t)
            {
                return t.GetType().GetProperties()
                    .Select(p => new SettingProp { P = p, A = p.GetCustomAttribute<SettingAttribute>() })
                    .Where(x => x.A != null)
                    .OrderBy(x => x.A.Order)
                    .ToList();
            }

            void AddRows(Panel card, object t, object d, IEnumerable<SettingProp> props)
            {
                foreach (var x in props)
                {
                    var p = x.P;
                    var reset = ResetButton(() => IsDefault(p, t, d), () => ResetProp(p, t, d), "Reset to default (" + Describe(p.GetValue(d)) + ")");
                    Ui.Row(card, x.A.Label, CreateEditor(p, x.A, t), x.A.Tooltip, reset);
                }
            }

            /// <summary>
            /// The cards of <paramref name="t"/> (the widget or its session profile): one per [Setting] group, and the column
            /// and header lists with the settings of their group ("Columns", "Header bar") on top of the list.
            /// </summary>
            void CollectCards(List<(int Rank, Action Draw)> cards, object t, object d)
            {
                var props = SettingProps(t);
                var table = t as ITableSettings;
                var header = t as IHeaderItems;
                foreach (var group in props.GroupBy(x => x.A.Group))
                {
                    string g = group.Key;
                    if (g == "Widget" || (g == "Columns" && table != null) || (g == "Header bar" && header != null)) continue;
                    var list = group.ToList();
                    cards.Add((GroupRank(g), () =>
                    {
                        var card = Ui.Card(host, Header(g, () => list.All(x => IsDefault(x.P, t, d)), () => { foreach (var x in list) ResetProp(x.P, t, d); }));
                        AddRows(card, t, d, list);
                    }));
                }
                if (table != null)
                {
                    var own = props.Where(x => x.A.Group == "Columns").ToList();
                    var defTable = (ITableSettings)d;
                    cards.Add((GroupRank("Columns"), () =>
                    {
                        var card = Ui.Card(host, Header("Columns", () => ListIsDefault(table.Columns, defTable.Columns) && own.All(x => IsDefault(x.P, t, d)),
                            () => { table.Columns = CopyList(defTable.Columns); foreach (var x in own) ResetProp(x.P, t, d); }));
                        AddRows(card, t, d, own);
                        AddHint(card, table.AvailableColumns.Any(c => c.Formats != null) ? "Order, on / off and format of the columns. Widths follow the content and font size; only the driver name has a width (px) of its own." : "Order and on / off of the columns. Widths follow the content and font size; only the driver name has a width (px) of its own.");
                        card.Children.Add(new ListEditor(table.Columns, table.AvailableColumns, true, Changed, this));   // rows add the card inset themselves
                    }));
                }
                if (header != null)
                {
                    var own = props.Where(x => x.A.Group == "Header bar").ToList();
                    var defHeader = (IHeaderItems)d;
                    cards.Add((GroupRank("Header bar"), () =>
                    {
                        var card = Ui.Card(host, Header("Header bar", () => ListIsDefault(header.Header, defHeader.Header) && own.All(x => IsDefault(x.P, t, d)),
                            () => { header.Header = CopyList(defHeader.Header); foreach (var x in own) ResetProp(x.P, t, d); }));
                        AddRows(card, t, d, own);
                        AddHint(card, "Order, on / off and format of the header bar items. Items after \"Push what follows to the right\" are right-aligned.");
                        card.Children.Add(new ListEditor(header.Header, header.AvailableHeader, false, Changed, this));
                    }));
                }
            }

            static void AddHint(Panel card, string text)
            {
                var hint = Ui.Caption(text);
                hint.TextWrapping = TextWrapping.Wrap;
                hint.Margin = new Thickness(14, 0, 10, 4);
                card.Children.Add(hint);
            }

            void ResetToDefaults()
            {
                foreach (var p in AllProps(ws)) ResetProp(p, ws, defaults);
                var t = ws as ITableSettings;
                if (t != null) t.Columns = CopyList(((ITableSettings)defaults).Columns);
            }

            // ------------------------------------------------------------ defaults / reset

            /// <summary>Settings of <paramref name="t"/> that a reset covers (the lists have their own reset; session profiles are compared deep).</summary>
            public static IEnumerable<PropertyInfo> SettingsOf(object t) { return AllProps(t); }

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
                    // values marked [Browsable(false)] are only read from older settings, never offered
                    var values = Enum.GetValues(t).Cast<object>().Where(v =>
                    {
                        var field = t.GetField(v.ToString());
                        var b = field != null ? (System.ComponentModel.BrowsableAttribute)Attribute.GetCustomAttribute(field, typeof(System.ComponentModel.BrowsableAttribute)) : null;
                        return b == null || b.Browsable;
                    }).ToList();
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
            /// <summary>What a column or header item shows, where its name doesn't say it all (tooltip in the lists).</summary>
            static readonly Dictionary<string, string> ItemTips = new Dictionary<string, string>
            {
                { "Class color bar (multiclass only)", "A bar in the colour of the car's class; only in multiclass sessions" },
                { "Position", "Position in the class (overall when everyone is in one class)" },
                { "Class position", "Position in the class" },
                { "Positions gained", "Places gained (green) or lost (red) since the start of the race" },
                { "Car brand", "Short name of the car brand, for example LAM or BMW" },
                { "License / SR", "License class and safety rating, in the license colour" },
                { "iRating change (est.)", "Estimated iRating gain or loss if the race ended now (green / red)" },
                { "Gap to leader", "Time behind the leader of the class" },
                { "Gap to fastest", "Difference to the fastest lap of the class" },
                { "Interval", "Time to the car one position ahead" },
                { "Gap to car ahead", "Difference to the lap time of the car one position ahead" },
                { "Pit status and flags", "PIT, TOW, OUT (out lap), number of stops, and the flags: black flag, furled black flag (warning), meatball (repair), DQ" },
                { "Laps in stint", "Laps since the car's last pit stop" },
                { "Laps completed", "Number of laps the car has completed" },
                { "Tire compound", "The tyres the car is on, when compounds differ in the field" },
                { "Relative time", "Seconds ahead (+) or behind (-) you on track" },
                { "Push what follows to the right", "Items below this one are aligned to the right of the header bar" },
                { "Widget name", "The display name of the widget" },
                { "Session", "Practice, qualify or race (as a letter or a word)" },
                { "Class (single class)", "The class or car, when everyone is in one class" },
                { "Laps", "Laps of the leader, and the total (or an estimate for timed races)" },
                { "Time", "Time remaining (and the length of the session)" },
                { "Strength of field", "Average iRating of the field (of your class)" },
                { "Cars", "Cars on track and the number of cars in the session" },
                { "Incidents and limits", "Your incidents, the next penalty and the disqualification limit of the event" },
                { "Clock", "The time of day: your PC's clock or the sim's" },
                { "Track temperature", "Temperature of the track surface" },
                { "Air temperature", "Temperature of the air" },
                { "Humidity", "Relative humidity of the air" },
            };

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
                        string tip;
                        if (ItemTips.TryGetValue(def.Label, out tip)) cb.ToolTip = tip;
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
            // the same order for every widget type (user's wish, 2026-10-04): what it shows, its lists, then how it looks
            switch (g)
            {
                case "Content": return 0;
                case "Rows": return 1;
                case "Rows (single class)": return 2;
                case "Rows (multiclass)": return 3;
                case "Estimates": return 4;
                case "Columns": return 10;
                case "Header bar": return 11;
                case "Text & size": return 20;
                case "Colors": return 21;
                default: return 30;
            }
        }
    }
}
