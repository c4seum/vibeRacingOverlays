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
        /// <summary>Keeps the user's order/visibility and adds columns introduced in newer versions.</summary>
        public static List<ColumnConfig> Merge(List<ColumnConfig> saved, IReadOnlyList<ColumnDef> defs)
        {
            var res = new List<ColumnConfig>();
            if (saved != null)
                foreach (var c in saved)
                {
                    var def = defs.FirstOrDefault(d => d.Key == c.Key);
                    if (def == null || res.Any(r => r.Key == c.Key)) continue;
                    if (c.Width <= 0) c.Width = def.Width;
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
