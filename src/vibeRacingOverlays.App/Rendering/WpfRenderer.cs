using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace vibeRacingOverlays.App.Rendering
{
    /// <summary>Replays a DisplayList into a WPF DrawingContext, caching brushes and text layouts.</summary>
    public sealed class WpfRenderer
    {
        readonly Dictionary<uint, SolidColorBrush> brushes = new Dictionary<uint, SolidColorBrush>();
        readonly Dictionary<(string, float, bool, uint, float, TextFit, Align), FormattedText> texts = new Dictionary<(string, float, bool, uint, float, TextFit, Align), FormattedText>();
        Typeface regular, bold;
        string fontFamily;
        double pixelsPerDip = 1;

        public WpfRenderer(string font) { SetFont(font); }

        public void SetFont(string font)
        {
            if (font == fontFamily) return;
            fontFamily = font;
            var family = new FontFamily(string.IsNullOrWhiteSpace(font) ? "Segoe UI" : font);
            regular = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            bold = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            texts.Clear();
        }

        public void SetDpi(double ppd) { if (ppd != pixelsPerDip) { pixelsPerDip = ppd; texts.Clear(); } }

        SolidColorBrush Brush(uint c)
        {
            SolidColorBrush b;
            if (!brushes.TryGetValue(c, out b))
            {
                b = new SolidColorBrush(Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c));
                b.Freeze();
                brushes[c] = b;
            }
            return b;
        }

        FormattedText Make(in DrawOp op, double size)
        {
            return new FormattedText(op.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, op.Bold ? bold : regular,
                size, Brush(op.Color), pixelsPerDip) { MaxLineCount = 1, Trimming = TextTrimming.None };
        }

        /// <summary>
        /// Lays out one text so it never leaves its box: shrink (numbers/badges, keeps the alignment)
        /// or trim with an ellipsis (names). Alignment itself is applied in Render.
        /// </summary>
        FormattedText Text(in DrawOp op)
        {
            var key = (op.Text, op.FontSize, op.Bold, op.Color, op.W, op.Fit, op.Align);
            FormattedText ft;
            if (texts.TryGetValue(key, out ft)) return ft;
            if (texts.Count > 4000) texts.Clear();

            ft = Make(op, op.FontSize);
            if (op.W > 0 && ft.WidthIncludingTrailingWhitespace > op.W)
            {
                var fit = op.Fit == TextFit.Auto ? (op.Align == Align.Left ? TextFit.Ellipsis : TextFit.Shrink) : op.Fit;
                if (fit == TextFit.Shrink)
                {
                    double size = Math.Max(op.FontSize * 0.6, op.FontSize * op.W / ft.WidthIncludingTrailingWhitespace * 0.98);
                    ft = Make(op, size);
                }
                if (ft.WidthIncludingTrailingWhitespace > op.W)
                {
                    ft.MaxTextWidth = op.W;
                    ft.Trimming = TextTrimming.CharacterEllipsis;
                }
            }
            texts[key] = ft;
            return ft;
        }
        public void Render(DrawingContext dc, DisplayList list)
        {
            foreach (var op in list.Ops)
            {
                if (op.Kind == OpKind.Rect)
                {
                    var r = new Rect(op.X, op.Y, op.W, op.H);
                    if (op.Radius > 0) dc.DrawRoundedRectangle(Brush(op.Color), null, r, op.Radius, op.Radius);
                    else dc.DrawRectangle(Brush(op.Color), null, r);
                }
                else
                {
                    var ft = Text(op);
                    double w = Math.Min(ft.WidthIncludingTrailingWhitespace, op.W > 0 ? op.W : double.MaxValue);
                    double x = op.Align == Align.Right ? op.X + op.W - w : op.Align == Align.Center ? op.X + (op.W - w) / 2 : op.X;
                    double y = op.Y + (op.H - ft.Height) / 2;
                    dc.DrawText(ft, new Point(x, y));
                }
            }
        }
    }
}
