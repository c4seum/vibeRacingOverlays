using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.Widgets
{
    public enum NameStyle { Full, Short, LastName }

    public sealed class StandingsSettings : WidgetSettings, ITableSettings
    {
        public override string TypeName { get { return "Standings"; } }

        // Single class vs multiclass is detected from the session; both layouts are configured here.
        [Setting("Name style", Group = "Content", Order = 4)] public NameStyle NameStyle { get; set; } = NameStyle.Full;
        [Setting("Header bar", Group = "Content", Order = 5)] public bool ShowHeader { get; set; } = true;
        [Setting("Column titles row", Group = "Content", Order = 6)] public bool ShowColumnTitles { get; set; } = false;
        [Setting("Lap time decimals", Group = "Content", Min = 0, Max = 3, Order = 7)] public int LapDecimals { get; set; } = 1;

        [Setting("Total rows", Group = "Single class", Min = 3, Max = 40, Order = 1)] public int Rows { get; set; } = 16;
        [Setting("Leader rows (top block)", Group = "Single class", Min = 0, Max = 20, Order = 2)] public int TopRows { get; set; } = 5;

        [Setting("Rows for my class", Group = "Multiclass", Min = 1, Max = 40, Order = 1)] public int MyClassRows { get; set; } = 8;
        [Setting("Leader rows in my class", Group = "Multiclass", Min = 0, Max = 20, Order = 2,
            Tooltip = "The rest of your class block is a window around your position.")]
        public int MyClassTopRows { get; set; } = 3;
        [Setting("Rows per other class", Group = "Multiclass", Min = 0, Max = 40, Order = 3, Tooltip = "0 = only show my class.")]
        public int OtherClassRows { get; set; } = 3;
        [Setting("Class headers", Group = "Multiclass", Order = 4)] public bool ShowClassHeaders { get; set; } = true;
        [Setting("My class first", Group = "Multiclass", Order = 5, Tooltip = "Otherwise classes are ordered fastest first.")]
        public bool PlayerClassFirst { get; set; } = false;
        [Setting("Font size", Group = "Style", Min = 8, Max = 30, Step = 0.5, Order = 20)] public double FontSize { get; set; } = 14;
        [Setting("Row height", Group = "Style", Min = 12, Max = 50, Order = 21)] public int RowHeight { get; set; } = 22;
        [Setting("Header background", Group = "Style", IsColor = true, Order = 22)] public string HeaderColor { get; set; } = "#FF1C1C1C";
        [Setting("Row background", Group = "Style", IsColor = true, Order = 23)] public string RowColor { get; set; } = "#FF1E1E1E";
        [Setting("Alternate row", Group = "Style", IsColor = true, Order = 24)] public string RowAltColor { get; set; } = "#FF282828";
        [Setting("Player row", Group = "Style", IsColor = true, Order = 25)] public string PlayerColor { get; set; } = "#FF8E2A2A";
        [Setting("Text", Group = "Style", IsColor = true, Order = 26)] public string TextColor { get; set; } = "#FFFFFFFF";

        public List<ColumnConfig> Columns { get; set; }

        public static readonly ColumnDef[] Defs =
        {
            new ColumnDef("classbar", "Class color bar (multiclass only)", "", 4, true, Align.Left),
            new ColumnDef("pos", "Position", "P", 26, true, Align.Right),
            new ColumnDef("gain", "Positions gained", "+/-", 38, true, Align.Left),
            new ColumnDef("num", "Car number", "#", 34, true, Align.Center),
            new ColumnDef("name", "Driver name", "DRIVER", 190, true, Align.Left),
            new ColumnDef("brand", "Car brand", "CAR", 34, true, Align.Center),
            new ColumnDef("lic", "License / SR", "LIC", 42, true, Align.Center),
            new ColumnDef("ir", "iRating", "iR", 40, true, Align.Right),
            new ColumnDef("irdelta", "iRating change (est.)", "iR+/-", 46, true, Align.Right),
            new ColumnDef("gap", "Gap to leader", "GAP", 50, true, Align.Right),
            new ColumnDef("int", "Interval", "INT", 50, true, Align.Right),
            new ColumnDef("last", "Last lap", "LAST", 62, true, Align.Right),
            new ColumnDef("best", "Best lap", "BEST", 62, false, Align.Right),
            new ColumnDef("pit", "Pit status", "PIT", 42, true, Align.Center),
            new ColumnDef("stint", "Laps in stint", "STINT", 32, true, Align.Right),
        };

        public IReadOnlyList<ColumnDef> AvailableColumns { get { return Defs; } }
        public void MergeColumns() { Columns = TableColumns.Merge(Columns, Defs); }

        public StandingsSettings() { Title = "Standings"; MergeColumns(); }
    }

    public sealed class StandingsWidget : Widget
    {
        const float Pad = 6;
        const uint Dim = 0xFF9A9A9A, Green = 0xFF3CC850, Red = 0xFFE8433A, Purple = 0xFFA04BE0, Orange = 0xFFF08C1E;

        readonly StandingsSettings s;
        public StandingsWidget(StandingsSettings s) : base(s) { this.s = s; }

        public override void Draw(DisplayList dl, RaceSnapshot snap)
        {
            bool multi = snap.Classes.Count > 1;
            var cols = s.Columns.Where(c => c.Enabled && (multi || c.Key != "classbar")).ToList();
            float fs = (float)s.FontSize, rh = s.RowHeight;
            float width = Pad * 2 + cols.Sum(c => c.Width + 4);
            float y = 0;

            if (s.ShowHeader)
            {
                dl.Rect(0, 0, width, rh + 2, Bg(Argb.Parse(s.HeaderColor)), 4);
                DrawHeader(dl, snap, width, rh + 2, fs);
                y += rh + 6;
            }
            if (s.ShowColumnTitles)
            {
                float x = Pad;
                foreach (var c in cols)
                {
                    var def = StandingsSettings.Defs.First(d => d.Key == c.Key);
                    dl.Text(x, y, c.Width, rh, def.Header, fs * 0.75f, Dim, def.Align);
                    x += c.Width + 4;
                }
                y += rh;
            }

            if (multi)
            {
                var classes = snap.Classes.ToList();
                if (s.PlayerClassFirst && snap.PlayerClass != null && classes.Remove(snap.PlayerClass)) classes.Insert(0, snap.PlayerClass);
                bool first = true;
                foreach (var cs in classes)
                {
                    bool mine = cs == snap.PlayerClass;
                    int rows = mine ? s.MyClassRows : s.OtherClassRows;
                    if (rows <= 0 || cs.Cars.Count == 0) continue;
                    if (!first) y += 4;
                    first = false;
                    if (s.ShowClassHeaders) { DrawClassHeader(dl, cs, y, width, rh, fs); y += rh; }
                    // in my class: leaders + a window around my position
                    int top = mine ? Math.Min(s.MyClassTopRows, rows - 1) : rows;
                    y = DrawBlock(dl, snap, cs.Cars, rows, Math.Max(0, top), cols, y, width, rh, fs);
                }
            }
            else
            {
                var field = snap.PlayerClass != null ? snap.PlayerClass.Cars : snap.Cars;
                int total = Math.Max(1, s.Rows);
                y = DrawBlock(dl, snap, field, total, Math.Max(0, Math.Min(s.TopRows, total)), cols, y, width, rh, fs);
            }
            dl.Width = width;
            dl.Height = y;
        }

        /// <summary>
        /// Draws up to <paramref name="total"/> rows of <paramref name="field"/>: the first <paramref name="top"/> cars,
        /// then a window centered on the player. Returns the new y.
        /// </summary>
        float DrawBlock(DisplayList dl, RaceSnapshot snap, List<CarInfo> field, int total, int top, List<ColumnConfig> cols,
            float y, float width, float rh, float fs)
        {
            var rows = new List<CarInfo>();
            int splitAfter = -1;
            if (field.Count <= total) rows.AddRange(field);
            else
            {
                rows.AddRange(field.Take(top));
                int bottom = total - top;
                int pi = snap.Player != null ? field.IndexOf(snap.Player) : -1;
                int start = pi < 0 ? top : pi - bottom / 2;
                start = Math.Max(top, Math.Min(start, field.Count - bottom));
                if (start > top && top > 0) splitAfter = top - 1;
                rows.AddRange(field.Skip(start).Take(bottom));
            }

            uint rowBg = Bg(Argb.Parse(s.RowColor)), altBg = Bg(Argb.Parse(s.RowAltColor)), playerBg = Bg(Argb.Parse(s.PlayerColor));
            uint text = Argb.Parse(s.TextColor);
            int reserve = Math.Min(total, field.Count);
            for (int r = 0; r < reserve; r++)
            {
                if (r >= rows.Count) { y += rh; continue; }
                var car = rows[r];
                dl.Rect(0, y, width, rh, car.IsPlayer ? playerBg : (r % 2 == 1 ? altBg : rowBg));
                float x = Pad;
                foreach (var c in cols)
                {
                    DrawCell(dl, c.Key, car, snap, x, y, c.Width, rh, fs, text, car == field[0]);
                    x += c.Width + 4;
                }
                y += rh;
                if (r == splitAfter) y += 6;
            }
            if (splitAfter < 0 && field.Count > total && top > 0 && top < total) y += 6; // keep the height stable
            return y;
        }

        void DrawClassHeader(DisplayList dl, ClassStandings cs, float y, float width, float rh, float fs)
        {
            uint color = Argb.FromRgb(cs.Color);
            dl.Rect(0, y, width, rh, Bg(Argb.Parse(s.HeaderColor)));
            dl.Rect(0, y, 4, rh, color);
            dl.Text(Pad + 4, y, width * 0.5f, rh, cs.Name, fs * 0.9f, color);
            string info = "SOF " + cs.Sof + "   " + cs.Cars.Count(c => c.InWorld) + "/" + cs.Cars.Count;
            if (cs.BestLap > 0) info = "Best " + Fmt.Lap(cs.BestLap, s.LapDecimals) + "   " + info;
            dl.Text(width * 0.4f, y, width * 0.6f - Pad, rh, info, fs * 0.8f, Dim, Align.Right);
        }

        void DrawHeader(DisplayList dl, RaceSnapshot snap, float width, float h, float fs)
        {
            string session = string.IsNullOrEmpty(snap.SessionType) ? "" : snap.SessionType.Substring(0, 1).ToUpperInvariant();
            bool multi = snap.Classes.Count > 1;
            // single class: show the class (or the car for single-make series); multiclass: the class headers do that
            string cls = multi || snap.PlayerClass == null ? "" : snap.PlayerClass.Name;

            string laps = "";
            if (snap.TotalLaps > 0) laps = snap.LeaderLap + "/" + snap.TotalLaps;
            else if (snap.EstTotalLaps > 0) laps = snap.LeaderLap + "/≈" + Fmt.Num(snap.EstTotalLaps, "0.0");
            else if (snap.LeaderLap > 0) laps = "Lap " + snap.LeaderLap;

            string time = snap.TimeRemain >= 0 ? Fmt.Clock(snap.TimeRemain) : "";
            if (time.Length > 0 && snap.TimeTotal > 0) time += "/" + Fmt.Short(snap.TimeTotal);

            string left = string.Join("   ", new[] { session, cls, laps, time }.Where(t => !string.IsNullOrEmpty(t)));
            var cs = multi ? null : snap.PlayerClass;
            int sof = cs != null ? cs.Sof : Data.Engine.RatingMath.StrengthOfField(snap.Cars.Select(c => c.IRating));
            int count = cs != null ? cs.Cars.Count : snap.Cars.Count;
            int running = cs != null ? cs.Cars.Count(c => c.InWorld) : snap.Cars.Count(c => c.InWorld);
            string right = Fmt.Num(snap.TrackTemp, "0.0") + "°C   SOF " + sof + "   " + running + "/" + count;

            dl.Text(Pad, 0, width * 0.62f, h, left, fs, 0xFFFFFFFF);
            dl.Text(width * 0.5f, 0, width * 0.5f - Pad, h, right, fs, 0xFFFFFFFF, Align.Right);
        }

        void DrawCell(DisplayList dl, string key, CarInfo c, RaceSnapshot snap, float x, float y, float w, float h, float fs, uint text, bool leaderRow)
        {
            float small = fs * 0.85f;
            switch (key)
            {
                case "classbar":
                    dl.Rect(x, y + 2, w, h - 4, Argb.FromRgb(c.ClassColor));
                    break;
                case "pos":
                    dl.Text(x, y, w, h, c.ClassPos.ToString(), fs, text, Align.Right);
                    break;
                case "gain":
                    if (c.PositionsGained.HasValue)
                    {
                        int g = c.PositionsGained.Value;
                        if (g == 0) dl.Text(x, y, w, h, "-", fs, Dim, Align.Left);
                        else dl.Text(x, y, w, h, (g > 0 ? "▲" : "▼") + Math.Abs(g), fs, g > 0 ? Green : Red, Align.Left, true, TextFit.Shrink);
                    }
                    break;
                case "num":
                    dl.Text(x, y, w, h, c.Number, fs, text, Align.Center);
                    break;
                case "name":
                    string name = s.NameStyle == NameStyle.Short ? c.ShortName
                        : s.NameStyle == NameStyle.LastName ? (c.Name.Contains(' ') ? c.Name.Substring(c.Name.LastIndexOf(' ') + 1) : c.Name) : c.Name;
                    dl.Text(x, y, w, h, name, fs, c.InWorld || c.LapCompleted > 0 ? text : Dim, Align.Left);
                    break;
                case "brand":
                    dl.Text(x, y, w, h, c.Brand, small * 0.9f, 0xFFB0B0B0, Align.Center);
                    break;
                case "lic":
                    uint lc = Fmt.LicenseColor(c.LicLetter);
                    dl.Badge(x, y + 3, w, h - 6, Fmt.License(c.LicLetter, c.LicSR), small, lc, Argb.ContrastText(lc), 4);
                    break;
                case "ir":
                    dl.Text(x, y, w, h, Fmt.IRating(c.IRating), fs, text, Align.Right);
                    break;
                case "irdelta":
                    if (c.IRatingDelta.HasValue)
                    {
                        int d = (int)Math.Round(c.IRatingDelta.Value);
                        dl.Text(x, y, w, h, (d > 0 ? "▲" : d < 0 ? "▼" : "") + Math.Abs(d), fs, d > 0 ? Green : d < 0 ? Red : Dim, Align.Right);
                    }
                    break;
                case "gap":
                    if (leaderRow) { if (snap.IsRace) dl.Text(x, y, w, h, "GAP", small, Dim, Align.Right); }
                    else if (snap.IsRace && c.LapsDown > 0) dl.Text(x, y, w, h, "+" + c.LapsDown + "L", fs, text, Align.Right);
                    else if (c.GapToClassLeader.HasValue) dl.Text(x, y, w, h, snap.IsRace ? Fmt.Gap(c.GapToClassLeader.Value) : "+" + Fmt.Num(c.GapToClassLeader.Value, "0.000"), fs, text, Align.Right);
                    break;
                case "int":
                    if (leaderRow) { if (snap.IsRace) dl.Text(x, y, w, h, "INT", small, Dim, Align.Right); }
                    else if (snap.IsRace && c.IntervalLaps > 0) dl.Text(x, y, w, h, "+" + c.IntervalLaps + "L", fs, text, Align.Right);
                    else if (c.Interval.HasValue) dl.Text(x, y, w, h, snap.IsRace ? Fmt.Gap(c.Interval.Value) : "+" + Fmt.Num(c.Interval.Value, "0.000"), fs, text, Align.Right);
                    break;
                case "last":
                    if (c.LastLap > 0)
                    {
                        dl.Text(x, y, w, h, Fmt.Lap(c.LastLap, s.LapDecimals), fs, text, Align.Right);
                        if (c.LastIsClassBest) dl.Rect(x + 4, y + h - 3, w - 4, 2, Purple);
                        else if (c.LastIsPersonalBest) dl.Rect(x + 4, y + h - 3, w - 4, 2, Green);
                    }
                    break;
                case "best":
                    if (c.BestLap > 0) dl.Text(x, y, w, h, Fmt.Lap(c.BestLap, s.LapDecimals), fs, text, Align.Right);
                    break;
                case "pit":
                    if (c.Towing) dl.Badge(x, y + 3, w, h - 6, "TOW", small, Red, 0xFFFFFFFF);
                    else if (c.OnPitRoad || c.InPitStall) dl.Badge(x, y + 3, w, h - 6, "PIT", small, Orange, 0xFF000000);
                    else if (c.OutLap) dl.Text(x, y, w, h, "OUT", small, Orange, Align.Center);
                    else if (c.PitCount > 0) dl.Text(x, y, w, h, "P" + c.PitCount, small, Dim, Align.Center);
                    break;
                case "stint":
                    if (c.Lap > 0) dl.Text(x, y, w, h, c.StintLaps.ToString(), fs, text, Align.Right);
                    break;
            }
        }
    }
}
