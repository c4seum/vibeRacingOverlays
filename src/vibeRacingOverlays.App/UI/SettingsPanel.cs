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
                defaults = (WidgetSettings)Activator.CreateInstance(ws.GetType());
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
                Track(resetAll, () => AllProps().All(IsDefault) && ColumnsAreDefault());
                var widgetCard = Ui.Card(host, Ui.Header(ws.TypeName + " widget", resetAll));
                var title = new TextBox { Text = ws.Title, FontWeight = FontWeights.SemiBold, ToolTip = "Only changes the name shown in the app; it stays a " + ws.TypeName + " widget." };
                title.TextChanged += (s, e) => { ws.Title = string.IsNullOrWhiteSpace(title.Text) ? ws.TypeName : title.Text; titleChanged(); changed(); };
                Ui.Row(widgetCard, "Display name", title, "Only changes the name shown in the app", null);
                if (App != null) Ui.Row(widgetCard, "Preset", PresetRow(), "Saved settings of this widget type, available in every layout", null);

                var props = ws.GetType().GetProperties()
                    .Select(p => new { P = p, A = p.GetCustomAttribute<SettingAttribute>() })
                    .Where(x => x.A != null)
                    .OrderBy(x => GroupRank(x.A.Group)).ThenBy(x => x.A.Order)
                    .ToList();

                foreach (var group in props.GroupBy(x => x.A.Group))
                {
                    var groupProps = group.Select(x => x.P).ToList();
                    var card = Ui.Card(host, Header(group.Key, () => groupProps.All(IsDefault), () => { foreach (var p in groupProps) ResetProp(p); }));
                    foreach (var x in group)
                    {
                        var p = x.P;
                        var reset = ResetButton(() => IsDefault(p), () => ResetProp(p), "Reset to default (" + Describe(p.GetValue(defaults)) + ")");
                        Ui.Row(card, x.A.Label, CreateEditor(p, x.A), x.A.Tooltip, reset);
                    }
                }

                var table = ws as ITableSettings;
                if (table != null)
                {
                    var card = Ui.Card(host, Header("Columns", ColumnsAreDefault, () => table.Columns = FreshColumns()));
                    var colHint = Ui.Caption("On / off, width in px and order of the columns.");
                    colHint.Margin = new Thickness(14, 0, 10, 4);
                    card.Children.Add(colHint);
                    card.Children.Add(new ColumnsEditor(table, Changed, this));   // rows add the card inset themselves
                }
            }

            // ------------------------------------------------------------ presets

            IEnumerable<WidgetPreset> PresetsOfType()
            {
                return App.Presets.Where(p => p.TypeName == ws.TypeName).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
            }

            /// <summary>Preset combo with Load / Save / Delete (the editor part of the "Preset" row).</summary>
            FrameworkElement PresetRow()
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };

                var list = PresetsOfType().ToList();
                var combo = new ComboBox
                {
                    Width = 170, ItemsSource = list, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = list.Count == 0 ? "No " + ws.TypeName + " presets yet: use Save" : "Saved " + ws.TypeName + " presets (available in every layout)",
                };
                combo.SelectedItem = list.FirstOrDefault(p => p.Id == ws.PresetId);
                row.Children.Add(combo);

                var load = new Button { Content = "Load", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Apply the selected preset to this widget (name and position are kept)" };
                var save = new Button { Content = "Save", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Save this widget's settings as a preset" };
                var del = Ui.IconButton(Ui.IconDelete, "Delete the selected preset");
                del.Margin = new Thickness(2, 0, 0, 0);
                Action updateButtons = () => { load.IsEnabled = del.IsEnabled = combo.SelectedItem != null; };
                combo.SelectionChanged += (s, e) => updateButtons();
                updateButtons();

                load.Click += (s, e) =>
                {
                    var p = combo.SelectedItem as WidgetPreset;
                    if (p == null) return;
                    ApplyFrom(AppSettings.CloneWidget(p.Settings));
                    ws.PresetId = p.Id;
                    changed();
                    Rebuild();
                };
                save.Click += (s, e) => SavePreset(combo.SelectedItem as WidgetPreset);
                del.Click += (s, e) => DeletePreset(combo.SelectedItem as WidgetPreset);

                row.Children.Add(load);
                row.Children.Add(save);
                row.Children.Add(del);
                return row;
            }

            void SavePreset(WidgetPreset selected)
            {
                var owner = Window.GetWindow(host);
                string suggestion = selected != null ? selected.Name : ws.TypeName + " preset " + (PresetsOfType().Count() + 1);
                string name = InputDialog.Ask(owner, "Save preset", "Save the settings of this " + ws.TypeName + " widget as preset:", suggestion);
                if (name == null) return;

                var existing = PresetsOfType().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null && MessageBox.Show(owner, "Overwrite preset '" + existing.Name + "'?", "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

                var snapshot = AppSettings.CloneWidget(ws);
                snapshot.PresetId = null;
                if (existing != null) existing.Settings = snapshot;
                else { existing = new WidgetPreset { Name = name, Settings = snapshot }; App.Presets.Add(existing); }
                ws.PresetId = existing.Id;
                changed();
                Rebuild();
            }

            void DeletePreset(WidgetPreset p)
            {
                if (p == null) return;
                var owner = Window.GetWindow(host);
                bool last = PresetsOfType().Count() == 1;
                string msg = "Delete preset '" + p.Name + "'?" + (last ? "\nThis is the last " + ws.TypeName + " preset: the widget will be reset to the defaults." : "");
                if (MessageBox.Show(owner, msg, "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

                App.Presets.Remove(p);
                // widgets in any layout that pointed at this preset no longer do
                foreach (var w in App.Layouts.SelectMany(l => l.Widgets)) if (w.PresetId == p.Id) w.PresetId = null;
                if (last) ResetToDefaults();
                changed();
                Rebuild();
            }

            /// <summary>Copies all widget settings from another settings object; instance settings (name, position...) are kept.</summary>
            void ApplyFrom(WidgetSettings source)
            {
                foreach (var p in AllProps()) p.SetValue(ws, p.GetValue(source));
                var t = ws as ITableSettings;
                if (t != null) { t.Columns = ((ITableSettings)source).Columns; t.MergeColumns(); }
            }

            void ResetToDefaults()
            {
                foreach (var p in AllProps()) ResetProp(p);
                var t = ws as ITableSettings;
                if (t != null) t.Columns = FreshColumns();
            }

            // ------------------------------------------------------------ defaults / reset

            IEnumerable<PropertyInfo> AllProps()
            {
                return ws.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite && !InstanceProps.Contains(p.Name)
                    && p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() == null && p.Name != "Columns");
            }

            bool IsDefault(PropertyInfo p)
            {
                object a = p.GetValue(ws), b = p.GetValue(defaults);
                if (a is string && b is string) return string.Equals(((string)a).Trim(), (string)b, StringComparison.OrdinalIgnoreCase);
                return Equals(a, b);
            }

            void ResetProp(PropertyInfo p) { p.SetValue(ws, p.GetValue(defaults)); }

            /// <summary>A new default column list owned only by this widget (never shared with another widget or layout).</summary>
            List<ColumnConfig> FreshColumns() { return ((ITableSettings)Activator.CreateInstance(ws.GetType())).Columns; }

            bool ColumnsAreDefault()
            {
                var t = ws as ITableSettings;
                if (t == null) return true;
                var d = ((ITableSettings)defaults).Columns;
                if (t.Columns.Count != d.Count) return false;
                for (int i = 0; i < d.Count; i++)
                    if (t.Columns[i].Key != d[i].Key || t.Columns[i].Enabled != d[i].Enabled || t.Columns[i].Width != d[i].Width) return false;
                return true;
            }

            void ResetWidget()
            {
                var owner = Window.GetWindow(host);
                if (MessageBox.Show(owner, "Reset all settings of '" + ws.Title + "' to the defaults?\nThe name and position are kept.",
                    "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                foreach (var p in AllProps()) ResetProp(p);
                var t = ws as ITableSettings;
                if (t != null) t.Columns = FreshColumns();
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

            UIElement CreateEditor(PropertyInfo p, SettingAttribute a)
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
                if (t == typeof(int) || t == typeof(double)) return NumberEditor(p, a);
                if (t == typeof(string) && a.IsColor) return ColorEditor(p);

                var tb = new TextBox { Text = Convert.ToString(p.GetValue(ws)), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
                tb.TextChanged += (s, e) => { p.SetValue(ws, tb.Text); Changed(); };
                return tb;
            }

            UIElement NumberEditor(PropertyInfo p, SettingAttribute a)
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

            UIElement ColorEditor(PropertyInfo p)
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

            /// <summary>Column list with on/off, width, ordering and a reset (on/off + width) per column.</summary>
            sealed class ColumnsEditor : StackPanel
            {
                readonly ITableSettings table;
                readonly Action changed;
                readonly Editor owner;

                public ColumnsEditor(ITableSettings table, Action changed, Editor owner)
                {
                    this.table = table;
                    this.changed = changed;
                    this.owner = owner;
                    Build();
                }

                void Build()
                {
                    Children.Clear();
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        var col = table.Columns[i];
                        var def = table.AvailableColumns.First(d => d.Key == col.Key);
                        int index = i;

                        // same height, inset and dividers as every other card row
                        var g = Ui.ListRow(this, new GridLength(Ui.LabelWidth + 60), new GridLength(70), GridLength.Auto, new GridLength(1, GridUnitType.Star));

                        var cb = new CheckBox { Content = def.Label, IsChecked = col.Enabled, VerticalAlignment = VerticalAlignment.Center };
                        cb.Click += (s, e) => { col.Enabled = cb.IsChecked == true; changed(); };
                        g.Children.Add(cb);

                        var width = new TextBox { Text = col.Width.ToString(CultureInfo.InvariantCulture), Width = 56, ToolTip = "Width (px)", VerticalAlignment = VerticalAlignment.Center };
                        width.LostFocus += (s, e) =>
                        {
                            float w;
                            if (float.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out w) && w > 0) { col.Width = w; changed(); }
                            else width.Text = col.Width.ToString(CultureInfo.InvariantCulture);
                        };
                        Grid.SetColumn(width, 1);
                        g.Children.Add(width);

                        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                        var buttonsMargin = new Thickness(6, 0, 0, 0);
                        var up = Ui.IconButton(Ui.IconUp, "Move up");
                        up.IsEnabled = i > 0;
                        var down = Ui.IconButton(Ui.IconDown, "Move down");
                        down.IsEnabled = i < table.Columns.Count - 1;
                        up.Margin = buttonsMargin;
                        up.Click += (s, e) => Move(index, -1);
                        down.Click += (s, e) => Move(index, 1);
                        buttons.Children.Add(up);
                        buttons.Children.Add(down);
                        Grid.SetColumn(buttons, 2);
                        g.Children.Add(buttons);

                        var reset = owner.ResetButton(() => col.Enabled == def.DefaultOn && col.Width == def.Width,
                            () => { col.Enabled = def.DefaultOn; col.Width = def.Width; },
                            "Reset to default (" + (def.DefaultOn ? "on" : "off") + ", " + def.Width.ToString(CultureInfo.InvariantCulture) + " px)");
                        reset.HorizontalAlignment = HorizontalAlignment.Right;
                        reset.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetColumn(reset, 3);
                        g.Children.Add(reset);

                    }
                }

                void Move(int index, int delta)
                {
                    int j = index + delta;
                    if (j < 0 || j >= table.Columns.Count) return;
                    var c = table.Columns[index];
                    table.Columns.RemoveAt(index);
                    table.Columns.Insert(j, c);
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
