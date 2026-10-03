namespace vibeRacingOverlays.App.Rendering
{
    public enum OpKind : byte { Rect, Text, Icon }
    public enum Align : byte { Left, Center, Right }

    /// <summary>
    /// What to do when a text is wider than its box. Auto: left-aligned text gets an ellipsis (names),
    /// right/center-aligned text is shrunk until it fits (numbers, badges), so alignment is kept.
    /// </summary>
    public enum TextFit : byte { Auto, Shrink, Ellipsis }

    /// <summary>One drawing primitive. Value equality lets us skip redraws when nothing changed.</summary>
    public readonly record struct DrawOp(
        OpKind Kind, float X, float Y, float W, float H, uint Color,
        float Radius = 0, string Text = null, float FontSize = 0, bool Bold = false, Align Align = Align.Left, TextFit Fit = TextFit.Auto);

    /// <summary>
    /// Widgets draw into this renderer-independent list. The overlay window only touches WPF
    /// (and triggers a layered-window update) when the list differs from the previous frame.
    /// </summary>
    public sealed class DisplayList
    {
        public readonly List<DrawOp> Ops = new List<DrawOp>(256);
        public float Width;
        public float Height;
        /// <summary>The widget's font (FontCatalog name); null = the renderer's own.</summary>
        public string Font;
        /// <summary>The widget is one shape with rounded corners: the first ClipCount ops are clipped to it (an edit frame added later isn't).</summary>
        public float ClipRadius, ClipWidth, ClipHeight;
        public int ClipCount;

        /// <summary>Rounds the corners of everything drawn so far (call at the end of Draw, after Width and Height are set).</summary>
        public void RoundCorners(float radius) { ClipRadius = radius; ClipCount = Ops.Count; ClipWidth = Width; ClipHeight = Height; }

        public void Clear() { Ops.Clear(); Width = Height = 0; Font = null; ClipRadius = 0; ClipCount = 0; ClipWidth = ClipHeight = 0; }

        /// <summary>Width a text will take when drawn (same font and tabular digits as the renderer).</summary>
        public float Measure(string text, float size, bool bold = true) { return TextMeasure.Measure(text, size, bold); }

        public void Rect(float x, float y, float w, float h, uint color, float radius = 0)
        {
            if ((color >> 24) == 0 || w <= 0 || h <= 0) return;
            Ops.Add(new DrawOp(OpKind.Rect, x, y, w, h, color, radius));
        }

        public void Text(float x, float y, float w, float h, string text, float size, uint color, Align align = Align.Left, bool bold = true, TextFit fit = TextFit.Auto)
        {
            if (string.IsNullOrEmpty(text) || (color >> 24) == 0) return;
            Ops.Add(new DrawOp(OpKind.Text, x, y, w, h, color, 0, text, size, bold, align, fit));
        }

        /// <summary>A line icon (path data in a 24x24 box, see <see cref="Icons"/>) drawn <paramref name="size"/> pixels square.</summary>
        public void Icon(float x, float y, float size, string path, uint color)
        {
            if (string.IsNullOrEmpty(path) || (color >> 24) == 0 || size <= 0) return;
            Ops.Add(new DrawOp(OpKind.Icon, x, y, size, size, color, 0, path));
        }

        /// <summary>Text on a rounded "badge" background.</summary>
        public void Badge(float x, float y, float w, float h, string text, float size, uint bg, uint fg, float radius = 3)
        {
            if (string.IsNullOrEmpty(text)) return;
            Rect(x, y, w, h, bg, radius);
            Text(x, y, w, h, text, size, fg, Align.Center);
        }

        public bool SameAs(DisplayList other)
        {
            if (other == null || other.Width != Width || other.Height != Height || other.Font != Font || other.ClipRadius != ClipRadius || other.ClipCount != ClipCount || other.ClipWidth != ClipWidth || other.ClipHeight != ClipHeight || other.Ops.Count != Ops.Count) return false;
            for (int i = 0; i < Ops.Count; i++)
                if (!Ops[i].Equals(other.Ops[i])) return false;
            return true;
        }
    }

    public static class Argb
    {
        public static uint Parse(string s, uint fallback = 0xFFFFFFFF)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim().TrimStart('#');
            uint v;
            if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v)) return fallback;
            if (s.Length == 6) v |= 0xFF000000;
            return v;
        }

        public static string ToHex(uint c) { return "#" + c.ToString("X8"); }

        public static uint FromRgb(uint rgb, byte alpha = 0xFF) { return ((uint)alpha << 24) | (rgb & 0xFFFFFF); }

        public static uint WithAlpha(uint c, double opacity)
        {
            uint a = (uint)Math.Round(((c >> 24) & 0xFF) * Math.Max(0, Math.Min(1, opacity)));
            return (a << 24) | (c & 0xFFFFFF);
        }

        /// <summary>Color a, moved a fraction t (0..1) towards color b (alpha of a is kept).</summary>
        public static uint Mix(uint a, uint b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            uint Ch(int shift) { double x = (a >> shift) & 0xFF, y = (b >> shift) & 0xFF; return (uint)Math.Round(x + (y - x) * t) << shift; }
            return (a & 0xFF000000) | Ch(16) | Ch(8) | Ch(0);
        }

        /// <summary>Black or white, whichever reads better on the given background.</summary>
        public static uint ContrastText(uint bg)
        {
            double r = (bg >> 16) & 0xFF, g = (bg >> 8) & 0xFF, b = bg & 0xFF;
            return (0.299 * r + 0.587 * g + 0.114 * b) > 150 ? 0xFF000000 : 0xFFFFFFFF;
        }
    }
}
