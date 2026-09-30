using vibeRacingOverlays.App.Core;
using Forms = System.Windows.Forms;

namespace vibeRacingOverlays.App.Overlay
{
    /// <summary>
    /// Anchored positions and snapping, all in physical screen pixels (the app is per-monitor DPI aware).
    /// </summary>
    internal static class Placement
    {
        public sealed class Monitor
        {
            /// <summary>"Left", "Middle", "Right", "Main" or "Screen n"; stored in WidgetSettings.Screen, so it means the same screen on every PC.</summary>
            public string Label;
            public Native.RECT Bounds;
            public bool Primary;
            public override string ToString() { return Label + "  (" + Bounds.Width + "x" + Bounds.Height + ")"; }
        }

        /// <summary>All monitors from left to right.</summary>
        public static List<Monitor> Monitors()
        {
            var list = Forms.Screen.AllScreens.OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).Select(s => new Monitor
            {
                Primary = s.Primary,
                Bounds = new Native.RECT { Left = s.Bounds.Left, Top = s.Bounds.Top, Right = s.Bounds.Right, Bottom = s.Bounds.Bottom },
            }).ToList();
            string[] three = { "Left", "Middle", "Right" };
            for (int i = 0; i < list.Count; i++)
                list[i].Label = list.Count == 3 ? three[i] : list.Count == 1 ? "Main" : "Screen " + (i + 1);
            return list;
        }

        public static Monitor Find(List<Monitor> monitors, string name)
        {
            return monitors.FirstOrDefault(m => m.Label == name);
        }

        /// <summary>The monitor containing the point, else the nearest one.</summary>
        public static Monitor At(List<Monitor> monitors, double x, double y)
        {
            return monitors.OrderBy(m => Distance(m.Bounds, x, y)).First();
        }

        static double Distance(Native.RECT r, double x, double y)
        {
            double dx = Math.Max(0, Math.Max(r.Left - x, x - r.Right)), dy = Math.Max(0, Math.Max(r.Top - y, y - r.Bottom));
            return dx * dx + dy * dy;
        }

        static int Col(Anchor a) { return (int)a % 3; }   // 0 left, 1 centre, 2 right
        static int Row(Anchor a) { return (int)a / 3; }   // 0 top, 1 middle, 2 bottom

        /// <summary>Top-left pixel of a widget of the given size, from its anchor and offsets.</summary>
        public static void ToPosition(WidgetSettings ws, Native.RECT screen, double w, double h, out double x, out double y)
        {
            x = Axis(Col(ws.Anchor), screen.Left, screen.Right, w, ws.OffsetX);
            y = Axis(Row(ws.Anchor), screen.Top, screen.Bottom, h, ws.OffsetY);
        }

        static double Axis(int mode, double lo, double hi, double size, double offset)
        {
            if (mode == 0) return lo + offset;
            if (mode == 2) return hi - size - offset;
            return Math.Round(lo + (hi - lo - size) / 2) + offset;
        }

        /// <summary>The offsets that put a widget of the given size at (x, y), for its current anchor.</summary>
        public static void ToOffsets(WidgetSettings ws, Native.RECT screen, double x, double y, double w, double h)
        {
            ws.OffsetX = Math.Round(Inverse(Col(ws.Anchor), screen.Left, screen.Right, w, x));
            ws.OffsetY = Math.Round(Inverse(Row(ws.Anchor), screen.Top, screen.Bottom, h, y));
        }

        static double Inverse(int mode, double lo, double hi, double size, double pos)
        {
            if (mode == 0) return pos - lo;
            if (mode == 2) return hi - size - pos;
            return pos - Math.Round(lo + (hi - lo - size) / 2);
        }

        /// <summary>
        /// Moves the rectangle so its edges snap to the screen edges and to the edges of nearby widgets
        /// (next to them, keeping the margin, or aligned with them). X and Y snap independently, so corners snap too.
        /// </summary>
        public static void Snap(ref Native.RECT r, Native.RECT screen, IEnumerable<Native.RECT> others, int distance, int margin)
        {
            int bestX = distance + 1, bestY = distance + 1, dx = 0, dy = 0;
            Action<int, int> tryX = (from, to) => { int d = to - from; if (Math.Abs(d) < Math.Abs(bestX)) { bestX = d; dx = d; } };
            Action<int, int> tryY = (from, to) => { int d = to - from; if (Math.Abs(d) < Math.Abs(bestY)) { bestY = d; dy = d; } };

            tryX(r.Left, screen.Left + margin);
            tryX(r.Right, screen.Right - margin);
            tryY(r.Top, screen.Top + margin);
            tryY(r.Bottom, screen.Bottom - margin);

            foreach (var o in others)
            {
                // only widgets that are side by side (or close) in the other direction
                bool nearY = r.Top < o.Bottom + margin + distance && r.Bottom > o.Top - margin - distance;
                bool nearX = r.Left < o.Right + margin + distance && r.Right > o.Left - margin - distance;
                if (nearY)
                {
                    tryX(r.Left, o.Right + margin);    // to the right of it
                    tryX(r.Right, o.Left - margin);    // to the left of it
                    tryX(r.Left, o.Left);              // left edges aligned
                    tryX(r.Right, o.Right);            // right edges aligned
                }
                if (nearX)
                {
                    tryY(r.Top, o.Bottom + margin);    // below it
                    tryY(r.Bottom, o.Top - margin);    // above it
                    tryY(r.Top, o.Top);                // top edges aligned
                    tryY(r.Bottom, o.Bottom);          // bottom edges aligned
                }
            }

            if (Math.Abs(bestX) <= distance) { r.Left += dx; r.Right += dx; }
            if (Math.Abs(bestY) <= distance) { r.Top += dy; r.Bottom += dy; }
        }
    }
}
