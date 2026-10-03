using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace vibeRacingOverlays.App.Rendering
{
    /// <summary>
    /// Text layout with tabular digits: every digit gets the width of the widest digit, so lap times, gaps and
    /// numbers line up in a column whatever the font ("1:11.1" is as wide as "1:08.8"). Runs of other characters
    /// keep their normal shape and spacing. Used for drawing (WpfRenderer) and for measuring (widget layout).
    /// </summary>
    public sealed class TabularText
    {
        public sealed class Layout
        {
            public Drawing Drawing;
            public double Width;
            public double Height;
        }

        readonly Dictionary<(double, bool), double> digitWidths = new Dictionary<(double, bool), double>();
        readonly Dictionary<(string, double, bool, uint), Layout> cache = new Dictionary<(string, double, bool, uint), Layout>();
        Typeface regular, bold;
        double pixelsPerDip = 1;
        double grid = 1;   // device pixels per unit (DPI x widget scale): glyphs are placed on whole pixels

        /// <summary>Off only to compare (--font-samples): text at the exact, fractional positions.</summary>
        public static bool PixelSnap = true;
        /// <summary>How glyphs are laid out (Ideal: the font's own shapes and spacing; Display: GDI-like, fitted to the pixel grid).</summary>
        public static TextFormattingMode LayoutMode = TextFormattingMode.Ideal;
        string font;

        public TabularText(string font) { SetFont(font); }

        public void SetFont(string f)
        {
            if (f == font && regular != null) return;
            font = f;
            // bundled fonts and their semibold files: see FontCatalog
            regular = FontCatalog.Face(f, false);
            bold = FontCatalog.Face(f, true);
            digitWidths.Clear();
            cache.Clear();
        }

        public void SetDpi(double ppd) { if (ppd != pixelsPerDip) { pixelsPerDip = ppd; cache.Clear(); } }

        /// <summary>Device pixels per layout unit, so digits land on whole pixels (a glyph between pixels looks thinner or blurred).</summary>
        public void SetGrid(double g) { if (g > 0 && g != grid) { grid = g; cache.Clear(); } }

        public double Snap(double v) { return PixelSnap ? Math.Round(v * grid) / grid : v; }

        public Typeface Face(bool isBold) { return isBold ? bold : regular; }

        public FormattedText Plain(string text, double size, bool isBold, Brush brush)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face(isBold), size, brush, null, LayoutMode, pixelsPerDip)
            { MaxLineCount = 1, Trimming = TextTrimming.None };
        }

        double DigitWidth(double size, bool isBold)
        {
            double w;
            if (digitWidths.TryGetValue((size, isBold), out w)) return w;
            for (char d = '0'; d <= '9'; d++) w = Math.Max(w, Plain(d.ToString(), size, isBold, Brushes.White).WidthIncludingTrailingWhitespace);
            digitWidths[(size, isBold)] = w;
            return w;
        }

        /// <summary>Width of a text at this size (tabular digits), without building a drawing.</summary>
        public double Measure(string text, double size, bool isBold)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double dw = DigitWidth(size, isBold), x = 0;
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsDigit(text[i])) { x += dw; i++; continue; }
                int start = i;
                while (i < text.Length && !char.IsDigit(text[i])) i++;
                x += Plain(text.Substring(start, i - start), size, isBold, Brushes.White).WidthIncludingTrailingWhitespace;
            }
            return x;
        }

        /// <summary>Builds (and caches) the drawing of a text at the origin.</summary>
        public Layout Build(string text, double size, bool isBold, Brush brush, uint colorKey)
        {
            var key = (text, size, isBold, colorKey);
            Layout l;
            if (cache.TryGetValue(key, out l)) return l;
            if (cache.Count > 6000) cache.Clear();

            double dw = DigitWidth(size, isBold), x = 0, h = 0;
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                int i = 0;
                while (i < text.Length)
                {
                    if (char.IsDigit(text[i]))
                    {
                        var ft = Plain(text[i].ToString(), size, isBold, brush);
                        dc.DrawText(ft, new Point(Snap(x + (dw - ft.WidthIncludingTrailingWhitespace) / 2), 0));   // centred in its cell, on a whole pixel
                        h = Math.Max(h, ft.Height);
                        x += dw; i++;
                        continue;
                    }
                    int start = i;
                    while (i < text.Length && !char.IsDigit(text[i])) i++;
                    var run = Plain(text.Substring(start, i - start), size, isBold, brush);
                    dc.DrawText(run, new Point(Snap(x), 0));
                    h = Math.Max(h, run.Height);
                    x += run.WidthIncludingTrailingWhitespace;
                }
            }
            group.Freeze();
            l = new Layout { Drawing = group, Width = x, Height = h > 0 ? h : Plain("0", size, isBold, brush).Height };
            cache[key] = l;
            return l;
        }
    }

    /// <summary>Text measuring for widget layout (same font and digit rules as the renderer).</summary>
    public static class TextMeasure
    {
        // one layout per font: each widget has its own font (Style > Font)
        static readonly Dictionary<string, TabularText> texts = new Dictionary<string, TabularText>(StringComparer.OrdinalIgnoreCase);
        // widgets measure the same column samples and header items every frame
        static readonly Dictionary<(string, string, float, bool), float> cache = new Dictionary<(string, string, float, bool), float>();
        static string fallback = "Bahnschrift";
        [ThreadStatic] static string current;

        public static void SetFont(string font) { lock (cache) { fallback = font; } }

        sealed class Scope : IDisposable
        {
            readonly string previous;
            public Scope(string font) { previous = current; current = font; }
            public void Dispose() { current = previous; }
        }

        /// <summary>Measures in this font until disposed (a widget's Draw, see Widget.Paint).</summary>
        public static IDisposable Use(string font) { return new Scope(font); }

        public static float Measure(string s, float size, bool bold)
        {
            lock (cache)
            {
                string font = current ?? fallback;
                float w;
                if (cache.TryGetValue((font, s, size, bold), out w)) return w;
                if (cache.Count > 4000) cache.Clear();
                TabularText text;
                if (!texts.TryGetValue(font, out text)) texts[font] = text = new TabularText(font);
                w = (float)text.Measure(s, size, bold);
                cache[(font, s, size, bold)] = w;
                return w;
            }
        }
    }
}
