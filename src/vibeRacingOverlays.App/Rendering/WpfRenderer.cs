using System.Windows;
using System.Windows.Media;

namespace vibeRacingOverlays.App.Rendering
{
    /// <summary>Replays a DisplayList into a WPF DrawingContext, caching brushes and text layouts.</summary>
    public sealed class WpfRenderer
    {
        readonly Dictionary<uint, SolidColorBrush> brushes = new Dictionary<uint, SolidColorBrush>();
        readonly Dictionary<(string, float, bool, uint, float, TextFit, Align), Placed> texts = new Dictionary<(string, float, bool, uint, float, TextFit, Align), Placed>();
        readonly TabularText tabular;
        string fontFamily;

        /// <summary>A text ready to draw: either a tabular-digit drawing or (for trimmed names) a plain FormattedText.</summary>
        sealed class Placed
        {
            public Drawing Drawing;
            public FormattedText Trimmed;
            public double Width, Height;
        }

        public WpfRenderer(string font)
        {
            fontFamily = font;
            tabular = new TabularText(font);
        }

        public void SetFont(string font)
        {
            if (font == fontFamily) return;
            fontFamily = font;
            tabular.SetFont(font);
            texts.Clear();
        }

        double ppd = 1, scale = 1;

        public void SetDpi(double ppd) { SetDpi(ppd, scale); }

        /// <summary>Screen DPI and the scale the list is drawn at: together they say where the whole pixels are.</summary>
        public void SetDpi(double ppd, double scale)
        {
            if (ppd == this.ppd && scale == this.scale) return;
            this.ppd = ppd;
            this.scale = scale;
            tabular.SetDpi(ppd);
            tabular.SetGrid(ppd * scale);
            texts.Clear();
        }

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

        /// <summary>
        /// Lays out one text so it never leaves its box: shrink (numbers/badges, keeps the alignment)
        /// or trim with an ellipsis (names). Digits are tabular so columns line up. Alignment is applied in Render.
        /// </summary>
        Placed Text(in DrawOp op)
        {
            var key = (op.Text, op.FontSize, op.Bold, op.Color, op.W, op.Fit, op.Align);
            Placed p;
            if (texts.TryGetValue(key, out p)) return p;
            if (texts.Count > 4000) texts.Clear();

            var brush = Brush(op.Color);
            var l = tabular.Build(op.Text, op.FontSize, op.Bold, brush, op.Color);
            if (op.W > 0 && l.Width > op.W)
            {
                var fit = op.Fit == TextFit.Auto ? (op.Align == Align.Left ? TextFit.Ellipsis : TextFit.Shrink) : op.Fit;
                if (fit == TextFit.Shrink)
                {
                    double size = Math.Max(op.FontSize * 0.6, op.FontSize * op.W / l.Width * 0.98);
                    l = tabular.Build(op.Text, size, op.Bold, brush, op.Color);
                }
                if (l.Width > op.W)
                {
                    // still too wide (or a name): trim with an ellipsis
                    var ft = tabular.Plain(op.Text, op.FontSize, op.Bold, brush);
                    ft.MaxTextWidth = op.W;
                    ft.Trimming = TextTrimming.CharacterEllipsis;
                    p = new Placed { Trimmed = ft, Width = Math.Min(ft.WidthIncludingTrailingWhitespace, op.W), Height = ft.Height };
                    texts[key] = p;
                    return p;
                }
            }
            p = new Placed { Drawing = l.Drawing, Width = l.Width, Height = l.Height };
            texts[key] = p;
            return p;
        }

        static readonly Dictionary<string, Geometry> iconShapes = new Dictionary<string, Geometry>();

        /// <summary>An icon's strokes as a filled shape (round caps, Icons.Stroke units wide), made once per icon.</summary>
        static Geometry IconShape(string path)
        {
            lock (iconShapes)
            {
                Geometry g;
                if (iconShapes.TryGetValue(path, out g)) return g;
                if (path.StartsWith(Icons.FillPrefix)) g = Geometry.Parse(path.Substring(Icons.FillPrefix.Length)).Clone();   // a filled shape
                else
                {
                    var pen = new Pen(Brushes.Black, Icons.Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    g = Geometry.Parse(path).GetWidenedPathGeometry(pen);
                }
                g.Freeze();
                iconShapes[path] = g;
                return g;
            }
        }

        public void Render(DrawingContext dc, DisplayList list)
        {
            if (list.Font != null) SetFont(list.Font);
            // the widget as one shape with rounded corners (header, rows and gaps together)
            bool clip = list.ClipRadius > 0 && list.ClipCount > 0;
            if (clip) dc.PushClip(new RectangleGeometry(new Rect(0, 0, list.ClipWidth, list.ClipHeight), list.ClipRadius, list.ClipRadius));
            for (int i = 0; i < list.Ops.Count; i++)
            {
                if (clip && i == list.ClipCount) { dc.Pop(); clip = false; }
                var op = list.Ops[i];
                if (op.Kind == OpKind.Rect)
                {
                    var r = new Rect(op.X, op.Y, op.W, op.H);
                    if (op.Radius > 0) dc.DrawRoundedRectangle(Brush(op.Color), null, r, op.Radius, op.Radius);
                    else dc.DrawRectangle(Brush(op.Color), null, r);
                }
                else if (op.Kind == OpKind.Icon)
                {
                    // on whole pixels like text, scaled from the 24x24 box
                    dc.PushTransform(new TranslateTransform(tabular.Snap(op.X), tabular.Snap(op.Y)));
                    dc.PushTransform(new ScaleTransform(op.W / 24.0, op.H / 24.0));
                    dc.DrawGeometry(Brush(op.Color), null, IconShape(op.Text));
                    dc.Pop();
                    dc.Pop();
                }
                else
                {
                    var t = Text(op);
                    double w = op.W > 0 ? Math.Min(t.Width, op.W) : t.Width;
                    double x = op.Align == Align.Right ? op.X + op.W - w : op.Align == Align.Center ? op.X + (op.W - w) / 2 : op.X;
                    double y = op.Y + (op.H - t.Height) / 2;
                    // on whole pixels: text between pixels is smeared over two, so strokes look thinner and uneven
                    x = tabular.Snap(x);
                    y = tabular.Snap(y);
                    if (t.Trimmed != null) { dc.DrawText(t.Trimmed, new Point(x, y)); continue; }
                    dc.PushTransform(new TranslateTransform(x, y));
                    dc.DrawDrawing(t.Drawing);
                    dc.Pop();
                }
            }
            if (clip) dc.Pop();
        }
    }
}
