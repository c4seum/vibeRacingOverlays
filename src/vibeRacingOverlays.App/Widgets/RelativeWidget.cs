using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.Widgets
{
    /// <summary>How cars on pit road are shown: grey text only (the look before 1.4), or the whole row faded.</summary>
    public enum PitRowStyle { DimText, DimRow }

    public sealed class RelativeSettings : WidgetSettings, ITableSettings, IHeaderItems, INormalizable
    {
        public override string TypeName { get { return "Relative"; } }

        [Setting("Cars ahead", Group = "Rows", Min = 0, Max = 10, Order = 1)] public int CarsAhead { get; set; } = 3;
        [Setting("Cars behind", Group = "Rows", Min = 0, Max = 10, Order = 2)] public int CarsBehind { get; set; } = 3;
        [Setting("Show header bar", Group = "Header bar", Order = 0)] public bool ShowHeader { get; set; } = true;

        [Setting("Font size", Group = "Text & size", Min = 8, Max = 30, Step = 0.5, Order = 3)] public double FontSize { get; set; } = 14;
        [Setting("Row height", Group = "Text & size", Min = 12, Max = 50, Order = 4)] public int RowHeight { get; set; } = 22;
        [Setting("Column spacing", Group = "Text & size", Min = 0, Max = 30, Order = 5, Tooltip = "Space between the columns (px). Column widths follow the content and the font size.")]
        public int ColumnSpacing { get; set; } = 8;
        [Setting("Header background", Group = "Colors", IsColor = true, Order = 2)] public string HeaderColor { get; set; } = "#FF1C1C1C";
        [Setting("Row background", Group = "Colors", IsColor = true, Order = 3)] public string RowColor { get; set; } = "#FF1E1E1E";
        [Setting("Alternate row", Group = "Colors", IsColor = true, Order = 4)] public string RowAltColor { get; set; } = "#FF282828";
        [Setting("Player row", Group = "Colors", IsColor = true, Order = 5)] public string PlayerColor { get; set; } = "#FF8E2A2A";
        [Setting("Car number (single class)", Group = "Colors", IsColor = true, Order = 9)] public string NumberColor { get; set; } = "#FF4A4F57";
        [Setting("Lapping you (ahead)", Group = "Colors", IsColor = true, Order = 7)] public string AheadLapColor { get; set; } = "#FFFF8A5B";
        [Setting("Lapped by you (behind)", Group = "Colors", IsColor = true, Order = 8)] public string BehindLapColor { get; set; } = "#FF6FA8FF";
        // DimText keeps the look of existing widgets; the Default preset uses DimRow (DEVELOPMENT.md, "Changing a widget")
        [Setting("Cars in the pits", Group = "Colors", Order = 10, Tooltip = "Cars on pit road: grey text only, or the whole row faded (car number, license and class color too). The PIT badge stays bright.")]
        public PitRowStyle PitRows { get; set; } = PitRowStyle.DimText;

        public List<ColumnConfig> Columns { get; set; }
        /// <summary>Header bar items (on/off, order, format).</summary>
        public List<ColumnConfig> Header { get; set; }

        public static readonly ColumnDef[] Defs =
        {
            new ColumnDef("classbar", "Class color bar (multiclass only)", "", 4, true, Align.Left).Fit(f => "", 1f, 4),
            new ColumnDef("pos", "Class position", "P", 26, true, Align.Right).Fit("88"),
            new ColumnDef("num", "Car number", "#", 34, true, Align.Center).Fit("888", 0.85f, 8),
            StandingsDefs.NameColumn(180),
            StandingsDefs.LicenseColumn(),
            StandingsDefs.RatingColumn(),
            new ColumnDef("pit", "Pit status and flags", "PIT", 38, true, Align.Center).Fit("TOW", 0.85f, 10),
            StandingsDefs.LastLapColumn(false, "1"),
            new ColumnDef("stint", "Laps in stint", "STINT", 32, false, Align.Right).Fit("88"),
            new ColumnDef("rel", "Relative time", "REL", 50, true, Align.Right).Fit("-88.8"),
        };

        public IReadOnlyList<ColumnDef> AvailableColumns { get { return Defs; } }
        public IReadOnlyList<ColumnDef> AvailableHeader { get { return HeaderDefs; } }
        public void MergeColumns() { Columns = TableColumns.Merge(Columns, Defs); Header = TableColumns.Merge(Header, HeaderDefs); }

        // same layout as before the header items: "RELATIVE" left, time remaining and incidents right
        public static readonly ColumnDef[] HeaderDefs =
        {
            HeaderBar.Item("title", true), HeaderBar.Item("laps", false),
            HeaderBar.Item("spacer", true),
            HeaderBar.Item("time", true, "remain"), HeaderBar.Item("tracktemp", false), HeaderBar.Item("airtemp", false), HeaderBar.Item("humidity", false),
            HeaderBar.Item("incidents", true), HeaderBar.Item("clock", false),
        };

        public RelativeSettings() { Title = "Relative"; Show = ShowWhen.InCar; MergeColumns(); }

        // before 1.2 the name style was a setting of its own; now it is the name column's format
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public NameStyle? NameStyle { get; set; }

        public void Normalize()
        {
            MergeColumns();
            if (NameStyle.HasValue)
            {
                var name = Columns.First(c => c.Key == "name");
                name.Format = NameStyle.Value == Widgets.NameStyle.Short ? "short" : NameStyle.Value == Widgets.NameStyle.LastName ? "last" : "full";
                NameStyle = null;
            }
        }
    }

    public sealed class RelativeWidget : Widget
    {
        const float Pad = 6, Corner = 6;
        const uint Dim = 0xFF9A9A9A, Orange = 0xFFF08C1E, Red = 0xFFE8433A;
        readonly RelativeSettings s;
        bool multi, race, fade;
        uint fadeTo;

        public RelativeWidget(RelativeSettings s) : base(s) { this.s = s; }

        public override void Draw(DisplayList dl, RaceSnapshot snap)
        {
            multi = snap.Classes.Count > 1;
            race = snap.IsRace;
            var cols = TableColumns.Resolve(s.Columns.Where(c => c.Enabled && (multi || c.Key != "classbar")), RelativeSettings.Defs, (float)s.FontSize, false);
            float fs = (float)s.FontSize, rh = s.RowHeight, sp = Math.Max(0, s.ColumnSpacing);
            float width = Pad * 2 + cols.Sum(c => c.Width) + sp * Math.Max(0, cols.Count - 1);
            float y = 0;

            if (s.ShowHeader)
            {
                // the header is part of the widget: no gap, one shape with rounded corners (RoundCorners below)
                dl.Rect(0, 0, width, rh + 2, Bg(Argb.Parse(s.HeaderColor)));
                HeaderBar.Draw(dl, snap, s.Header, "RELATIVE", width, rh + 2, fs, Pad, 16);
                y += rh + 2;
            }

            var list = snap.Relative;
            int pi = snap.Player != null ? list.IndexOf(snap.Player) : -1;
            var rows = new List<CarInfo>();
            for (int k = s.CarsAhead; k >= 1; k--) rows.Add(pi >= 0 && pi - k >= 0 ? list[pi - k] : null);
            rows.Add(pi >= 0 ? list[pi] : null);
            for (int k = 1; k <= s.CarsBehind; k++) rows.Add(pi >= 0 && pi + k < list.Count ? list[pi + k] : null);

            uint rowBg = Bg(Argb.Parse(s.RowColor)), altBg = Bg(Argb.Parse(s.RowAltColor)), playerBg = Bg(Argb.Parse(s.PlayerColor));
            uint aheadLap = Argb.Parse(s.AheadLapColor), behindLap = Argb.Parse(s.BehindLapColor);
            fadeTo = Argb.Parse(s.RowColor);

            for (int r = 0; r < rows.Count; r++)
            {
                var c = rows[r];
                dl.Rect(0, y, width, rh, c != null && c.IsPlayer ? playerBg : (r % 2 == 1 ? altBg : rowBg));
                if (c != null)
                {
                    uint text = c.IsPlayer || c.RelativeLap == 0 ? 0xFFFFFFFF : c.RelativeLap > 0 ? aheadLap : behindLap;
                    bool inPits = c.OnPitRoad && !c.IsPlayer;
                    if (inPits) text = Dim;
                    fade = inPits && s.PitRows == PitRowStyle.DimRow;
                    float x = Pad;
                    foreach (var col in cols)
                    {
                        DrawCell(dl, col, c, x, y, col.Width, rh, fs, text);
                        x += col.Width + sp;
                    }
                }
                y += rh;
            }
            dl.Width = width;
            dl.Height = y;
            dl.RoundCorners(Corner);
        }

        void DrawCell(DisplayList dl, ColumnConfig col, CarInfo c, float x, float y, float w, float h, float fs, uint text)
        {
            float small = fs * 0.85f;
            // a car in the pits isn't racing you: its colored parts fade towards the row, like its text
            uint F(uint color, double t = 0.6) { return fade ? Argb.Mix(color, fadeTo, t) : color; }
            switch (col.Key)
            {
                case "classbar": dl.Rect(x, y + 2, w, h - 4, F(Argb.FromRgb(c.ClassColor))); break;
                case "pos": if (c.ClassPos > 0) dl.Text(x, y, w, h, c.ClassPos.ToString(), fs, text, Align.Right); break;
                case "num":
                    // multiclass: badge in the iRacing class color; single class: neutral
                    uint cc = multi ? Argb.FromRgb(c.ClassColor) : Argb.Parse(s.NumberColor);
                    dl.Badge(x, y + 3, w, h - 6, c.Number, small, F(cc), F(Argb.ContrastText(cc), 0.45), 3);
                    break;
                case "name":
                    string name = StandingsDefs.DriverName(c, col.Format);
                    dl.Text(x, y, w, h, name, fs, text);
                    break;
                case "lic":
                    uint lc = Fmt.LicenseColor(c.LicLetter);
                    dl.Badge(x, y + 3, w, h - 6, Fmt.License(c.LicLetter, c.LicSR, col.Format), small, F(lc), F(Argb.ContrastText(lc), 0.45), 4);
                    break;
                case "ir": dl.Text(x, y, w, h, Fmt.Rating(c.IRating, col.Format), fs, text, Align.Right); break;
                case "pit":
                    DrawPitStatus(dl, c, x, y, w, h, small, false, race);
                    break;
                case "last": if (c.LastLap > 0) dl.Text(x, y, w, h, Fmt.Lap(c.LastLap, int.Parse(col.Format ?? "1")), fs, text, Align.Right); break;
                case "stint": if (c.Lap > 0) dl.Text(x, y, w, h, c.StintLaps.ToString(), fs, text, Align.Right); break;
                case "rel": if (!c.IsPlayer) dl.Text(x, y, w, h, Fmt.Signed(c.RelativeTime, 1), fs, text, Align.Right); break;
            }
        }
    }
}
