using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace vibeRacingOverlays.App.Rendering
{
    /// <summary>Draws a widget's display list on a "track-like" backdrop (editor preview, PNG snapshots).</summary>
    public static class PreviewRenderer
    {
        static readonly Brush Backdrop = Frozen(new LinearGradientBrush(Color.FromRgb(0x4E, 0x5C, 0x52), Color.FromRgb(0x33, 0x3B, 0x36), 90));

        static Brush Frozen(Brush b) { b.Freeze(); return b; }

        public static void Draw(DrawingContext dc, WpfRenderer renderer, DisplayList dl, double scale, double pad, double width, double height)
        {
            dc.DrawRoundedRectangle(Backdrop, null, new Rect(0, 0, width, height), 6, 6);
            dc.PushTransform(new TranslateTransform(pad, pad));
            dc.PushTransform(new ScaleTransform(scale, scale));
            renderer.Render(dc, dl);
            dc.Pop();
            dc.Pop();
        }

        public static RenderTargetBitmap ToBitmap(DisplayList dl, string font, double pad)
        {
            int w = (int)Math.Ceiling(dl.Width + pad * 2), h = (int)Math.Ceiling(dl.Height + pad * 2);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) Draw(dc, new WpfRenderer(font), dl, 1, pad, w, h);
            var bmp = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            return bmp;
        }
    }
}
