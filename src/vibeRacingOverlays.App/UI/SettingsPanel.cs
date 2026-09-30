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
        static readonly HashSet<string> InstanceProps = new HashSet<string> { "Id", "Title", "Enabled", "X", "Y" };

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

                var typeLabel = new TextBlock { Text = ws.TypeName.ToUpperInvariant(), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
                typeLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                host.Children.Add(typeLabel);

                var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var resetAll = new Button { Content = "↺  Reset widget", Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3),
                    ToolTip = "Reset all settings of this widget to the defaults (name and position are kept)" };
                resetAll.Click += (s, e) => ResetWidget();
                Track(resetAll, () => AllProps().All(IsDefault) && ColumnsAreDefault());
                DockPanel.SetDock(resetAll, Dock.Right);
                titleRow.Children.Add(resetAll);
                var title = new TextBox { Text = ws.Title, FontSize = 16, ToolTip = "Only changes the name shown in the app; it stays a " + ws.TypeName + " widget." };
                title.TextChanged += (s, e) => { ws.Title = string.IsNullOrWhiteSpace(title.Text) ? ws.TypeName : title.Text; titleChanged(); changed(); };
                titleRow.Children.Add(title);
                host.Children.Add(titleRow);
                if (App != null) host.Children.Add(PresetRow());

                var props = ws.GetType().GetProperties()
                    .Select(p => new { P = p, A = p.GetCustomAttribute<SettingAttribute>() })
                    .Where(x => x.A != null)
                    .OrderBy(x => GroupRank(x.A.Group)).ThenBy(x => x.A.Order)
                    .ToList();

                foreach (var group in props.GroupBy(x => x.A.Group))
                {
                    var groupProps = group.Select(x => x.P).ToList();
                    host.Children.Add(Header(group.Key, () => groupProps.All(IsDefault), () => { foreach (var p in groupProps) ResetProp(p); }));
                    foreach (var x in group)
                    {
                        var p = x.P;
                        var reset = ResetButton(() => IsDefault(p), () => ResetProp(p), "Reset to default (" + Describe(p.GetValue(defaults)) + ")");
                        host.Children.Add(Row(x.A.Label, CreateEditor(p, x.A), x.A.Tooltip, reset));
                    }
                }

                var table = ws as ITableSettings;
                if (table != null)
                {
                    host.Children.Add(Header("Columns", ColumnsAreDefault, () => table.Columns = FreshColumns()));
                    host.Children.Add(new ColumnsEditor(table, Changed, this));
                }
            }

            // ------------------------------------------------------------ presets

            IEnumerable<WidgetPreset> PresetsOfType()
            {
                return App.Presets.Where(p => p.TypeName == ws.TypeName).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
            }

            UIElement PresetRow()
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var label = new TextBlock { Text = "Preset", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
                label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                row.Children.Add(label);

                var list = PresetsOfType().ToList();
                var combo = new ComboBox
                {
                    Width = 220, ItemsSource = list, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = list.Count == 0 ? "No " + ws.TypeName + " presets yet: use Save" : "Saved " + ws.TypeName + " presets (available in every layout)",
                };
                combo.SelectedItem = list.FirstOrDefault(p => p.Id == ws.PresetId);
                row.Children.Add(combo);

                var load = new Button { Content = "Load", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), ToolTip = "Apply the selected preset to this widget (name and position are kept)" };
                var save = new Button { Content = "Save", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), ToolTip = "Save this widget's settings as a preset" };
                var del = new Button { Content = "Delete", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), ToolTip = "Delete the selected preset" };
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
                var b = new Button { Content = "↺", Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(8, 0, 0, 0), FontSize = 14, ToolTip = tooltip };
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

            UIElement Header(string text, Func<bool> isDefault, Action reset)
            {
                var row = new DockPanel { Margin = new Thickness(0, 16, 0, 6), LastChildFill = false };
                var tb = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "Accent");   // follows theme switches
                var b = new Button { Content = "↺  Reset", FontSize = 11, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(12, 0, 0, 0),
                    ToolTip = "Reset all " + text + " settings to the defaults" };
                b.Click += (s, e) => { reset(); changed(); Rebuild(); };
                Track(b, isDefault);
                row.Children.Add(tb);
                row.Children.Add(b);
                return row;
            }

            static UIElement Row(string label, UIElement editor, string tooltip, UIElement reset)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, ToolTip = tooltip });
                Grid.SetColumn(editor, 1);
                g.Children.Add(editor);
                Grid.SetColumn(reset, 2);
                g.Children.Add(reset);
                return g;
            }

            // ------------------------------------------------------------ editors

            UIElement CreateEditor(PropertyInfo p, SettingAttribute a)
            {
                var t = p.PropertyType;
                if (t == typeof(bool))
                {
                    var cb = new CheckBox { IsChecked = (bool)p.GetValue(ws) };
                    cb.Click += (s, e) => { p.SetValue(ws, cb.IsChecked == true); Changed(); };
                    return cb;
                }
                if (t.IsEnum)
                {
                    var combo = new ComboBox { ItemsSource = Enum.GetValues(t), SelectedItem = p.GetValue(ws), Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
                    combo.SelectionChanged += (s, e) => { if (combo.SelectedItem != null) { p.SetValue(ws, combo.SelectedItem); Changed(); } };
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
                var swatch = new Border { Width = 34, Height = 22, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
                var box = new TextBox { Width = 100, Text = (string)p.GetValue(ws) };
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
                panel.Children.Add(new TextBlock { Text = "#AARRGGBB", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
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

                        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var cb = new CheckBox { Content = def.Label, IsChecked = col.Enabled };
                        cb.Click += (s, e) => { col.Enabled = cb.IsChecked == true; changed(); };
                        g.Children.Add(cb);

                        var width = new TextBox { Text = col.Width.ToString(CultureInfo.InvariantCulture), Width = 56, ToolTip = "Width (px)" };
                        width.LostFocus += (s, e) =>
                        {
                            float w;
                            if (float.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out w) && w > 0) { col.Width = w; changed(); }
                            else width.Text = col.Width.ToString(CultureInfo.InvariantCulture);
                        };
                        Grid.SetColumn(width, 1);
                        g.Children.Add(width);

                        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                        var up = new Button { Content = "▲", Padding = new Thickness(6, 0, 6, 0), IsEnabled = i > 0 };
                        var down = new Button { Content = "▼", Padding = new Thickness(6, 0, 6, 0), IsEnabled = i < table.Columns.Count - 1 };
                        up.Click += (s, e) => Move(index, -1);
                        down.Click += (s, e) => Move(index, 1);
                        buttons.Children.Add(up);
                        buttons.Children.Add(down);
                        Grid.SetColumn(buttons, 2);
                        g.Children.Add(buttons);

                        var reset = owner.ResetButton(() => col.Enabled == def.DefaultOn && col.Width == def.Width,
                            () => { col.Enabled = def.DefaultOn; col.Width = def.Width; },
                            "Reset to default (" + (def.DefaultOn ? "on" : "off") + ", " + def.Width.ToString(CultureInfo.InvariantCulture) + " px)");
                        reset.Margin = new Thickness(2, 0, 0, 0);
                        Grid.SetColumn(reset, 3);
                        g.Children.Add(reset);

                        Children.Add(g);
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
