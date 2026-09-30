using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.Widgets
{
    public sealed class ColumnDef
    {
        public string Key;
        public string Label;     // shown in the editor
        public string Header;    // shown in the overlay's column header row
        public float Width;
        public bool DefaultOn;
        public Align Align;
        /// <summary>Display formats the user can pick (key + example label, e.g. ("k1", "4.5k")); null = none.</summary>
        public (string Key, string Label)[] Formats;
        public string DefaultFormat;
        public ColumnDef(string key, string label, string header, float width, bool on, Align align)
        {
            Key = key; Label = label; Header = header; Width = width; DefaultOn = on; Align = align;
        }

        public ColumnDef WithFormats(string defaultFormat, params (string Key, string Label)[] formats)
        {
            Formats = formats;
            DefaultFormat = defaultFormat;
            return this;
        }

        public bool HasFormat(string key) { return Formats != null && Formats.Any(f => f.Key == key); }

        /// <summary>Only these columns have a width the user sets (the driver name); all others get a fixed width.</summary>
        public bool Resizable;
        /// <summary>Widest content per format ("|" separates alternatives, digits are equal-width); null = <see cref="Width"/> scaled with the font.</summary>
        public Func<string, string> Sample;
        /// <summary>Font size of the content relative to the widget's font size (badges and brands are smaller).</summary>
        public float SampleSize = 1f;
        /// <summary>Space inside the column around the text (badge padding).</summary>
        public float Extra;

        public ColumnDef UserWidth() { Resizable = true; return this; }

        public ColumnDef Fit(string sample, float size = 1f, float extra = 0) { return Fit(f => sample, size, extra); }

        public ColumnDef Fit(Func<string, string> sample, float size = 1f, float extra = 0)
        {
            Sample = sample; SampleSize = size; Extra = extra;
            return this;
        }

        /// <summary>
        /// Width of the column: the user's width for the name, otherwise the widest content of the chosen format
        /// at this font size (and the column title when the titles row is shown), so values always fit and line up.
        /// </summary>
        public float WidthFor(ColumnConfig c, float fontSize, bool titles)
        {
            if (Resizable) return c.Width > 0 ? c.Width : Width;
            float w;
            if (Sample != null)
            {
                w = 0;
                foreach (var s in Sample(c.Format ?? DefaultFormat).Split('|'))
                    w = Math.Max(w, Rendering.TextMeasure.Measure(s, fontSize * SampleSize, true));
                w += Extra;
            }
            else w = Width * fontSize / 14f;
            if (titles && !string.IsNullOrEmpty(Header)) w = Math.Max(w, Rendering.TextMeasure.Measure(Header, fontSize * 0.75f, true));
            return (float)Math.Ceiling(w);
        }
    }

    /// <summary>Settings that need fixing up after loading (migration of older files, new defaults).</summary>
    public interface INormalizable
    {
        void Normalize();
    }

    /// <summary>Practice / qualifying and race are configured separately (e.g. the standings).</summary>
    public enum SessionKind { PracticeQualify, Race }

    /// <summary>Widgets with a settings profile per session kind; the editor shows a P&amp;Q / Race switch.</summary>
    public interface ISessionProfiles
    {
        object Profile(SessionKind kind);
    }

    /// <summary>Settings with a list of header items (on/off, order, format), edited like the column list.</summary>
    public interface IHeaderItems
    {
        List<ColumnConfig> Header { get; set; }
        IReadOnlyList<ColumnDef> AvailableHeader { get; }
    }

    public interface ITableSettings
    {
        List<ColumnConfig> Columns { get; set; }
        IReadOnlyList<ColumnDef> AvailableColumns { get; }
        void MergeColumns();
    }

    public static class TableColumns
    {
        /// <summary>The visible columns with their drawn width (fixed from the content, or the user's width for the name).</summary>
        public static List<ColumnConfig> Resolve(IEnumerable<ColumnConfig> visible, IReadOnlyList<ColumnDef> defs, float fontSize, bool titles)
        {
            var res = new List<ColumnConfig>();
            foreach (var c in visible)
            {
                var def = defs.FirstOrDefault(d => d.Key == c.Key);
                if (def == null) continue;
                res.Add(new ColumnConfig { Key = c.Key, Enabled = true, Format = c.Format, Width = def.WidthFor(c, fontSize, titles) });
            }
            return res;
        }

        /// <summary>Keeps the user's order/visibility and adds columns introduced in newer versions.</summary>
        public static List<ColumnConfig> Merge(List<ColumnConfig> saved, IReadOnlyList<ColumnDef> defs)
        {
            var res = new List<ColumnConfig>();
            if (saved != null)
                foreach (var c in saved)
                {
                    var def = defs.FirstOrDefault(d => d.Key == c.Key);
                    if (def == null || res.Any(r => r.Key == c.Key)) continue;
                    if (c.Width <= 0 || !def.Resizable) c.Width = def.Width;   // only resizable columns keep a width of their own
                    if (!def.HasFormat(c.Format)) c.Format = def.DefaultFormat;
                    res.Add(c);
                }
            foreach (var d in defs)
                if (!res.Any(r => r.Key == d.Key)) res.Add(new ColumnConfig { Key = d.Key, Enabled = d.DefaultOn, Width = d.Width, Format = d.DefaultFormat });
            return res;
        }
    }

    public abstract class Widget
    {
        public WidgetSettings Settings { get; private set; }
        protected Widget(WidgetSettings s) { Settings = s; }

        /// <summary>Draws the overlay at scale 1. Sets list.Width/Height.</summary>
        public abstract void Draw(DisplayList list, RaceSnapshot snap);

        public virtual bool ShouldShow(RaceSnapshot snap)
        {
            if (snap == null || !snap.Connected) return false;
            switch (Settings.Show)
            {
                case ShowWhen.InCar: return snap.Player != null && snap.Player.InWorld;
                case ShowWhen.InRace: return snap.IsRace;
                default: return true;
            }
        }

        protected uint Bg(uint color) { return Argb.WithAlpha(color, Settings.BackgroundOpacity); }

        /// <summary>
        /// Pit status column: disqualified, towing, on pit road, then the flag iRacing shows the driver
        /// (black flag = penalty to serve, furled black flag = warning / slow down, meatball = repair),
        /// then the out lap (held for the whole first lap after the pit lane) and (with <paramref name="stops"/>) the number of stops.
        /// </summary>
        protected static void DrawPitStatus(DisplayList dl, CarInfo c, float x, float y, float w, float h, float small, bool stops)
        {
            const uint Red = 0xFFE8433A, Orange = 0xFFF08C1E, Yellow = 0xFFF2C318, Dim = 0xFF9A9A9A;
            var f = c.DriverFlags;
            float by = y + 3, bh = h - 6;
            if ((f & Data.Telemetry.SessionFlags.Disqualify) != 0) FlagBadge(dl, x, by, w, bh, "DQ", small, 0xFF000000, 0xFFFFFFFF);
            else if (c.Towing) dl.Badge(x, by, w, bh, "TOW", small, Red, 0xFFFFFFFF);
            else if (c.OnPitRoad || c.InPitStall) dl.Badge(x, by, w, bh, "PIT", small, Orange, 0xFF000000);
            else if ((f & Data.Telemetry.SessionFlags.Black) != 0) FlagBadge(dl, x, by, w, bh, "⚑", small, 0xFF000000, 0xFFFFFFFF);
            else if ((f & Data.Telemetry.SessionFlags.Furled) != 0) dl.Badge(x, by, w, bh, "⚑", small, Yellow, 0xFF000000);
            else if ((f & Data.Telemetry.SessionFlags.Repair) != 0) FlagBadge(dl, x, by, w, bh, "●", small, 0xFF000000, Orange);
            else if (c.OutLap) dl.Text(x, y, w, h, "OUT", small, Orange, Align.Center);
            else if (stops && c.PitCount > 0) dl.Text(x, y, w, h, "P" + c.PitCount, small, Dim, Align.Center);
        }

        /// <summary>Black flag badge with a thin white edge, so it stays visible on dark rows.</summary>
        static void FlagBadge(DisplayList dl, float x, float y, float w, float h, string text, float size, uint bg, uint fg)
        {
            dl.Rect(x, y, w, h, 0xFFFFFFFF, 3);
            dl.Rect(x + 1, y + 1, w - 2, h - 2, bg, 2);
            dl.Text(x, y, w, h, text, size, fg, Align.Center);
        }

        public static Widget Create(WidgetSettings s)
        {
            if (s is StandingsSettings) return new StandingsWidget((StandingsSettings)s);
            if (s is RelativeSettings) return new RelativeWidget((RelativeSettings)s);
            if (s is FuelSettings) return new FuelWidget((FuelSettings)s);
            throw new NotSupportedException(s.GetType().Name);
        }

        /// <summary>Widget types offered in the "+ Add" menu.</summary>
        public static readonly (string Name, Func<WidgetSettings> Make)[] Catalog =
        {
            ("Standings", () => new StandingsSettings()),
            ("Relative", () => new RelativeSettings()),
            ("Fuel calculator", () => new FuelSettings()),
        };
    }
}
