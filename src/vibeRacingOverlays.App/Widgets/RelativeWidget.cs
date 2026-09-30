using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.Widgets
{
    public sealed class RelativeSettings : WidgetSettings, ITableSettings
    {
        public override string TypeName { get { return "Relative"; } }

        [Setting("Cars ahead", Group = "Content", Min = 0, Max = 10, Order = 1)] public int CarsAhead { get; set; } = 3;
        [Setting("Cars behind", Group = "Content", Min = 0, Max = 10, Order = 2)] public int CarsBehind { get; set; } = 3;
        [Setting("Name style", Group = "Content", Order = 3)] public NameStyle NameStyle { get; set; } = NameStyle.Full;
        [Setting("Header bar", Group = "Content", Order = 4)] public bool ShowHeader { get; set; } = true;

        [Setting("Font size", Group = "Style", Min = 8, Max = 30, Step = 0.5, Order = 20)] public double FontSize { get; set; } = 14;
        [Setting("Row height", Group = "Style", Min = 12, Max = 50, Order = 21)] public int RowHeight { get; set; } = 22;
        [Setting("Column spacing", Group = "Style", Min = 0, Max = 30, Order = 21, Tooltip = "Space between the columns (px). Column widths follow the content and the font size.")]
        public int ColumnSpacing { get; set; } = 8;
        [Setting("Row background", Group = "Style", IsColor = true, Order = 22)] public string RowColor { get; set; } = "#FF1E1E1E";
        [Setting("Alternate row", Group = "Style", IsColor = true, Order = 23)] public string RowAltColor { get; set; } = "#FF282828";
        [Setting("Player row", Group = "Style", IsColor = true, Order = 24)] public string PlayerColor { get; set; } = "#FF8E2A2A";
        [Setting("Car number (single class)", Group = "Style", IsColor = true, Order = 27)] public string NumberColor { get; set; } = "#FF4A4F57";
        [Setting("Lapping you (ahead)", Group = "Style", IsColor = true, Order = 25)] public string AheadLapColor { get; set; } = "#FFFF8A5B";
        [Setting("Lapped by you (behind)", Group = "Style", IsColor = true, Order = 26)] public string BehindLapColor { get; set; } = "#FF6FA8FF";

        public List<ColumnConfig> Columns { get; set; }

        public static readonly ColumnDef[] Defs =
        {
            new ColumnDef("classbar", "Class color bar (multiclass only)", "", 4, true, Align.Left).Fit(f => "", 1f, 4),
            new ColumnDef("pos", "Class position", "P", 26, true, Align.Right).Fit("88"),
            new ColumnDef("num", "Car number", "#", 34, true, Align.Center).Fit("888", 0.85f, 8),
            new ColumnDef("name", "Driver name", "DRIVER", 180, true, Align.Left).UserWidth(),
            new ColumnDef("lic", "License / SR", "LIC", 42, true, Align.Center).Fit("D4.9", 0.85f, 10),
            new ColumnDef("ir", "iRating", "iR", 40, true, Align.Right).Fit("8.8k"),
            new ColumnDef("pit", "Pit status and flags", "PIT", 38, true, Align.Center).Fit("TOW", 0.85f, 10),
            new ColumnDef("last", "Last lap", "LAST", 62, false, Align.Right).Fit("8:88.8"),
            new ColumnDef("stint", "Laps in stint", "STINT", 32, false, Align.Right).Fit("88"),
            new ColumnDef("rel", "Relative time", "REL", 50, true, Align.Right).Fit("-88.8"),
        };

        public IReadOnlyList<ColumnDef> AvailableColumns { get { return Defs; } }
        public void MergeColumns() { Columns = TableColumns.Merge(Columns, Defs); }

        public RelativeSettings() { Title = "Relative"; Show = ShowWhen.InCar; MergeColumns(); }
    }

    public sealed class RelativeWidget : Widget
    {
        const float Pad = 6;
        const uint Dim = 0xFF9A9A9A, Orange = 0xFFF08C1E, Red = 0xFFE8433A;
        readonly RelativeSettings s;
        bool multi;

        public RelativeWidget(RelativeSettings s) : base(s) { this.s = s; }

        public override void Draw(DisplayList dl, RaceSnapshot snap)
        {
            multi = snap.Classes.Count > 1;
            var cols = TableColumns.Resolve(s.Columns.Where(c => c.Enabled && (multi || c.Key != "classbar")), RelativeSettings.Defs, (float)s.FontSize, false);
            float fs = (float)s.FontSize, rh = s.RowHeight, sp = Math.Max(0, s.ColumnSpacing);
            float width = Pad * 2 + cols.Sum(c => c.Width) + sp * Math.Max(0, cols.Count - 1);
            float y = 0;

            if (s.ShowHeader)
            {
                dl.Rect(0, 0, width, rh + 2, Bg(0xFF1C1C1C), 4);
                string left = "RELATIVE";
                string right = (snap.TimeRemain >= 0 ? Fmt.Clock(snap.TimeRemain) + "   " : "") + snap.Incidents + "x";
                dl.Text(Pad, 0, width / 2, rh + 2, left, fs, 0xFFFFFFFF);
                dl.Text(width / 2, 0, width / 2 - Pad, rh + 2, right, fs, 0xFFFFFFFF, Align.Right);
                y += rh + 6;
            }

            var list = snap.Relative;
            int pi = snap.Player != null ? list.IndexOf(snap.Player) : -1;
            var rows = new List<CarInfo>();
            for (int k = s.CarsAhead; k >= 1; k--) rows.Add(pi >= 0 && pi - k >= 0 ? list[pi - k] : null);
            rows.Add(pi >= 0 ? list[pi] : null);
            for (int k = 1; k <= s.CarsBehind; k++) rows.Add(pi >= 0 && pi + k < list.Count ? list[pi + k] : null);

            uint rowBg = Bg(Argb.Parse(s.RowColor)), altBg = Bg(Argb.Parse(s.RowAltColor)), playerBg = Bg(Argb.Parse(s.PlayerColor));
            uint aheadLap = Argb.Parse(s.AheadLapColor), behindLap = Argb.Parse(s.BehindLapColor);

            for (int r = 0; r < rows.Count; r++)
            {
                var c = rows[r];
                dl.Rect(0, y, width, rh, c != null && c.IsPlayer ? playerBg : (r % 2 == 1 ? altBg : rowBg));
                if (c != null)
                {
                    uint text = c.IsPlayer || c.RelativeLap == 0 ? 0xFFFFFFFF : c.RelativeLap > 0 ? aheadLap : behindLap;
                    if (c.OnPitRoad && !c.IsPlayer) text = Dim;
                    float x = Pad;
                    foreach (var col in cols)
                    {
                        DrawCell(dl, col.Key, c, x, y, col.Width, rh, fs, text);
                        x += col.Width + sp;
                    }
                }
                y += rh;
            }
            dl.Width = width;
            dl.Height = y;
        }

        void DrawCell(DisplayList dl, string key, CarInfo c, float x, float y, float w, float h, float fs, uint text)
        {
            float small = fs * 0.85f;
            switch (key)
            {
                case "classbar": dl.Rect(x, y + 2, w, h - 4, Argb.FromRgb(c.ClassColor)); break;
                case "pos": if (c.ClassPos > 0) dl.Text(x, y, w, h, c.ClassPos.ToString(), fs, text, Align.Right); break;
                case "num":
                    // multiclass: badge in the iRacing class color; single class: neutral
                    uint cc = multi ? Argb.FromRgb(c.ClassColor) : Argb.Parse(s.NumberColor);
                    dl.Badge(x, y + 3, w, h - 6, c.Number, small, cc, Argb.ContrastText(cc), 3);
                    break;
                case "name":
                    string name = s.NameStyle == NameStyle.Short ? c.ShortName
                        : s.NameStyle == NameStyle.LastName ? (c.Name.Contains(' ') ? c.Name.Substring(c.Name.LastIndexOf(' ') + 1) : c.Name) : c.Name;
                    dl.Text(x, y, w, h, name, fs, text);
                    break;
                case "lic":
                    uint lc = Fmt.LicenseColor(c.LicLetter);
                    dl.Badge(x, y + 3, w, h - 6, Fmt.License(c.LicLetter, c.LicSR), small, lc, Argb.ContrastText(lc), 4);
                    break;
                case "ir": dl.Text(x, y, w, h, Fmt.IRating(c.IRating), fs, text, Align.Right); break;
                case "pit":
                    DrawPitStatus(dl, c, x, y, w, h, small, false);
                    break;
                case "last": if (c.LastLap > 0) dl.Text(x, y, w, h, Fmt.Lap(c.LastLap, 1), fs, text, Align.Right); break;
                case "stint": if (c.Lap > 0) dl.Text(x, y, w, h, c.StintLaps.ToString(), fs, text, Align.Right); break;
                case "rel": if (!c.IsPlayer) dl.Text(x, y, w, h, Fmt.Signed(c.RelativeTime, 1), fs, text, Align.Right); break;
            }
        }
    }
}
