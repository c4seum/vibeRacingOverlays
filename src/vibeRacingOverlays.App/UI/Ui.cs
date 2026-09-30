using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace vibeRacingOverlays.App.UI
{
    /// <summary>
    /// Shared building blocks of the editor UI (styles live in App.xaml), so every panel looks the same:
    /// cards per group, a header with a reset button, label + editor rows with thin dividers.
    /// </summary>
    public static class Ui
    {
        public const double LabelWidth = 190;

        /// <summary>
        /// Height of every row in a card, whatever it holds (check box, slider + box, segments, text box, color picker...),
        /// so blocks look even. Only editors marked tall (see <see cref="MarkTall"/>, e.g. the 3×3 anchor grid) may grow.
        /// Build settings rows with <see cref="Row"/> (or <see cref="ListRow"/>) and never give them their own height.
        /// </summary>
        public const double RowHeight = 34;

        /// <summary>
        /// Height of every input control in a row (text box, combo, button, segmented, color swatch; set in App.xaml
        /// and here), so each row has the same (RowHeight - ControlHeight) / 2 of air above and below.
        /// </summary>
        public const double ControlHeight = 26;

        /// <summary>Lets an editor that can't fit <see cref="RowHeight"/> make its row taller.</summary>
        public static T MarkTall<T>(T editor) where T : FrameworkElement { editor.Tag = "tall"; return editor; }

        static bool IsTall(UIElement e) { return e is UniformGrid || (e is FrameworkElement fe && fe.Tag as string == "tall"); }

        // Segoe Fluent Icons / Segoe MDL2 Assets code points
        public const string IconAdd = "", IconCopy = "", IconDelete = "", IconReset = "",
            IconUp = "", IconDown = "", IconLeft = "", IconRight = "", IconEdit = "",
            IconList = "", IconSort = "", IconFuel = "", IconWidget = "", IconSwap = "",
            IconSave = "";

        public static Style Style(string key) { return (Style)Application.Current.FindResource(key); }

        /// <summary>Space between the card edge and its content: rows fill the card width (inset list), dividers run edge to edge.</summary>
        public static readonly Thickness Inset = new Thickness(14, 0, 10, 0);

        /// <summary>A group of settings: rounded card (soft shadow in the light theme) holding a vertical stack.</summary>
        public static StackPanel Card(Panel host, UIElement header)
        {
            var stack = new StackPanel();
            if (header != null) stack.Children.Add(header);
            var card = new Border { Style = Style("CardStyle"), Child = stack };
            var shadow = new Border { Style = Style("CardShadowStyle") };
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.Children.Add(shadow);
            g.Children.Add(card);
            host.Children.Add(g);
            return stack;
        }

        /// <summary>Card title (small caps in the accent color) with an optional quiet reset button on the right.</summary>
        public static DockPanel Header(string text, Button reset)
        {
            var row = new DockPanel { Margin = new Thickness(14, 6, 6, 2), LastChildFill = true, MinHeight = 28 };
            if (reset != null) { DockPanel.SetDock(reset, Dock.Right); row.Children.Add(reset); }
            var tb = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            row.Children.Add(tb);
            return row;
        }

        /// <summary>"↺ Reset" in the card header.</summary>
        public static Button HeaderReset(string tooltip)
        {
            var b = new Button { Style = Style("GhostButton"), Padding = new Thickness(8, 1, 8, 1), MinHeight = 22, Margin = new Thickness(0), ToolTip = tooltip, FontSize = 11.5 };
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(new TextBlock { Text = IconReset, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) });
            p.Children.Add(new TextBlock { Text = "Reset", VerticalAlignment = VerticalAlignment.Center });
            b.Content = p;
            return b;
        }

        /// <summary>Small icon-only reset button at the end of a row.</summary>
        public static Button RowReset(string tooltip)
        {
            var b = IconButton(IconReset, tooltip);
            b.FontSize = 10.5;
            b.Width = 24; b.Height = 24;
            b.Margin = new Thickness(8, 0, 0, 0);
            return b;
        }

        public static Button IconButton(string glyph, string tooltip)
        {
            return new Button { Style = Style("IconButton"), Content = glyph, ToolTip = tooltip };
        }

        /// <summary>Label | editor | (reset) row of an inset list; rows after the first get a divider across the full card width.</summary>
        public static Grid Row(Panel card, string label, UIElement editor, string tooltip, UIElement reset, double labelWidth = LabelWidth)
        {
            bool first = !card.Children.OfType<Grid>().Any(x => x.Tag as string == "row");
            if (!first) card.Children.Add(Divider());
            bool tall = IsTall(editor);
            var g = new Grid { Tag = "row", Margin = Inset };
            // fixed height: rows never differ because of the control they hold
            if (tall) g.MinHeight = RowHeight; else g.Height = RowHeight;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            var l = new TextBlock { Text = label, ToolTip = tooltip, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center };
            if (tall) l.Margin = new Thickness(0, 11, 12, 0);
            Grid.SetColumn(l, 0);
            g.Children.Add(l);
            Grid.SetColumn(editor, 1);
            if (editor is FrameworkElement fe)
            {
                fe.VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center;
                fe.Margin = new Thickness(fe.Margin.Left, tall ? 6 : 0, fe.Margin.Right, tall ? 6 : 0);
            }
            g.Children.Add(editor);
            if (reset != null)
            {
                var r = (FrameworkElement)reset;
                Grid.SetColumn(reset, 2);
                r.HorizontalAlignment = HorizontalAlignment.Right;
                r.VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center;
                if (tall) r.Margin = new Thickness(r.Margin.Left, 7, r.Margin.Right, 0);
                g.Children.Add(reset);
            }
            card.Children.Add(g);
            return g;
        }

        /// <summary>
        /// A row with its own columns (e.g. the column list: check box | width | order | reset) that still follows the
        /// card rules: <see cref="RowHeight"/>, the card inset and a divider before every row but the first.
        /// </summary>
        public static Grid ListRow(Panel card, params GridLength[] columns)
        {
            bool first = !card.Children.OfType<Grid>().Any(x => x.Tag as string == "row");
            if (!first) card.Children.Add(Divider());
            var g = new Grid { Tag = "row", Margin = Inset, Height = RowHeight };
            foreach (var c in columns) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
            card.Children.Add(g);
            return g;
        }

        /// <summary>Thin line across the full width of a card.</summary>
        public static Border Divider()
        {
            var line = new Border { Height = 1 };
            line.SetResourceReference(Border.BackgroundProperty, "Divider");
            return line;
        }

        /// <summary>On/off setting (a check box).</summary>
        public static CheckBox Toggle(bool on)
        {
            return new CheckBox { IsChecked = on };
        }

        /// <summary>Segmented control for a few short choices. onPick gets the chosen value.</summary>
        public static Border Segmented(IEnumerable<object> values, object selected, Action<object> onPick)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            string group = "seg" + Guid.NewGuid().ToString("N");
            foreach (var v in values)
            {
                var value = v;
                var rb = new RadioButton { Style = Style("Segment"), Content = Label(value), GroupName = group, IsChecked = Equals(value, selected) };
                rb.Checked += (s, e) => onPick(value);
                panel.Children.Add(rb);
            }
            var b = new Border { Child = panel, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Height = ControlHeight };
            b.SetResourceReference(Border.BackgroundProperty, "Field");
            b.BorderBrush = Brushes.Transparent;   // filled, no outline (like all controls)
            return b;
        }

        public static TextBlock Caption(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            return t;
        }

        /// <summary>Enum value for display: "InCar" -> "In car", "NextToRefuel" -> "Next to refuel".</summary>
        public static string Label(object value)
        {
            string s = Convert.ToString(value);
            if (!(value is Enum) || string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1])) { sb.Append(' '); sb.Append(char.ToLowerInvariant(c)); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Wraps enum values so combo boxes show readable names but still hand back the value.</summary>
        public sealed class EnumItem
        {
            public object Value;
            public override string ToString() { return Label(Value); }
        }
    }
}
