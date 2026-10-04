using System.Text.Json.Serialization;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.Widgets
{
    public enum NameStyle { Full, Short, LastName }

    /// <summary>
    /// What the standings show in one kind of session (P&amp;Q or race): rows, columns, header items and their formats.
    /// Style (colors, font, scale) is shared and lives in <see cref="StandingsSettings"/>.
    /// </summary>
    public sealed class StandingsProfile : ITableSettings, IHeaderItems
    {
        public SessionKind Kind { get; set; }

        [Setting("Show header bar", Group = "Header bar", Order = 0, Tooltip = "The bar above the rows with the title and the header items below")] public bool ShowHeader { get; set; } = true;
        [Setting("Column titles row", Group = "Columns", Order = 0, Tooltip = "A row with the column names above the drivers")] public bool ShowColumnTitles { get; set; } = false;

        [Setting("Total rows", Group = "Rows (single class)", Min = 3, Max = 40, Order = 1, Tooltip = "Number of drivers shown when everyone is in one class")] public int Rows { get; set; } = 16;
        [Setting("Leader rows (top block)", Group = "Rows (single class)", Min = 0, Max = 20, Order = 2, Tooltip = "How many of those rows show the leaders; the rest is a window around your position")] public int TopRows { get; set; } = 5;

        [Setting("Rows for my class", Group = "Rows (multiclass)", Min = 1, Max = 40, Order = 1, Tooltip = "Number of drivers of your own class shown in multiclass sessions")] public int MyClassRows { get; set; } = 8;
        [Setting("Leader rows in my class", Group = "Rows (multiclass)", Min = 0, Max = 20, Order = 2,
            Tooltip = "The rest of your class block is a window around your position.")]
        public int MyClassTopRows { get; set; } = 3;
        [Setting("Rows per other class", Group = "Rows (multiclass)", Min = 0, Max = 40, Order = 3, Tooltip = "0 = only show my class.")]
        public int OtherClassRows { get; set; } = 3;
        [Setting("Class headers", Group = "Rows (multiclass)", Order = 4, Tooltip = "A bar above each class with its name, best lap, strength of field and number of cars")] public bool ShowClassHeaders { get; set; } = true;
        [Setting("My class first", Group = "Rows (multiclass)", Order = 5, Tooltip = "Otherwise classes are ordered fastest first.")]
        public bool PlayerClassFirst { get; set; } = false;

        /// <summary>Driver row columns (on/off, width, order, format).</summary>
        public List<ColumnConfig> Columns { get; set; }
        /// <summary>Header bar items (on/off, order, format).</summary>
        public List<ColumnConfig> Header { get; set; }

        public StandingsProfile() : this(SessionKind.PracticeQualify) { }

        public StandingsProfile(SessionKind kind)
        {
            Kind = kind;
            MergeColumns();
        }

        [JsonIgnore] public IReadOnlyList<ColumnDef> AvailableColumns { get { return StandingsDefs.Columns(Kind); } }
        [JsonIgnore] public IReadOnlyList<ColumnDef> AvailableHeader { get { return StandingsDefs.Header(Kind); } }

        public void MergeColumns()
        {
            Columns = TableColumns.Merge(Columns, StandingsDefs.Columns(Kind));
            Header = TableColumns.Merge(Header, StandingsDefs.Header(Kind));
        }

        public ColumnConfig Column(string key) { return Columns.FirstOrDefault(c => c.Key == key); }
        public ColumnConfig HeaderItem(string key) { return Header.FirstOrDefault(c => c.Key == key); }
    }

    /// <summary>The columns and header items of the standings, with their formats and per-session defaults.</summary>
    public static class StandingsDefs
    {
        static readonly (string, string)[] Decimals = { ("3", "1.746"), ("2", "1.74"), ("1", "1.7") };
        static readonly (string, string)[] LapDecimals = { ("3", "1:35.764"), ("2", "1:35.76"), ("1", "1:35.7") };
        static readonly (string, string)[] RatingFormats = { ("full", "4567"), ("k1", "4.5k"), ("k0", "4k") };

        static string Digits(string format) { int d; return new string('8', int.TryParse(format, out d) ? d : 1); }

        // ---- columns the standings and the relative share: same formats and widths in both widgets

        public static ColumnDef NameColumn(float width)
        {
            return new ColumnDef("name", "Driver name", "DRIVER", width, true, Align.Left).UserWidth()
                .WithFormats("full", ("full", "Full name"), ("short", "J. Groenewegen"), ("last", "Last name"));
        }

        public static ColumnDef LicenseColumn()
        {
            return new ColumnDef("lic", "License / SR", "LIC", 42, true, Align.Center).Fit(LicenseSample, 0.85f, 10)
                .WithFormats("L1", ("L2", "A3.48"), ("L1", "A3.4"), ("L0", "A3"), ("L", "A"), ("SR2", "3.48"), ("SR1", "3.4"), ("SR0", "3"));
        }

        public static ColumnDef RatingColumn()
        {
            return new ColumnDef("ir", "iRating", "iR", 40, true, Align.Right).Fit(f => f == "full" ? "8888" : f == "k0" ? "88k" : "8.8k")
                .WithFormats("k1", RatingFormats);
        }

        public static ColumnDef LastLapColumn(bool on, string defaultFormat)
        {
            return new ColumnDef("last", "Last lap", "LAST", 62, on, Align.Right).Fit(f => "8:88." + Digits(f)).WithFormats(defaultFormat, LapDecimals);
        }

        /// <summary>Driver name in the name column's format: "full", "short" (J. Groenewegen) or "last".</summary>
        public static string DriverName(CarInfo c, string format)
        {
            if (format == "short") return c.ShortName;
            if (format == "last") return c.Name.Contains(' ') ? c.Name.Substring(c.Name.LastIndexOf(' ') + 1) : c.Name;
            return c.Name;
        }

        /// <summary>Widest license text per format (D is one of the widest letters).</summary>
        static string LicenseSample(string f)
        {
            switch (f) { case "L2": return "D4.99"; case "L0": return "D4"; case "L": return "D"; case "SR2": return "4.99"; case "SR1": return "4.9"; case "SR0": return "4"; default: return "D4.9"; }
        }

        /// <summary>
        /// Every column except the driver name has a fixed width: the widest content of its format (the samples),
        /// measured at the widget's font size. Badges get some padding around their text.
        /// </summary>
        public static ColumnDef[] Columns(SessionKind kind)
        {
            bool race = kind == SessionKind.Race;
            const float Small = 0.85f;
            return new[]
            {
                new ColumnDef("classbar", "Class color bar (multiclass only)", "", 4, true, Align.Left).Fit(f => "", 1f, 4),
                new ColumnDef("pos", "Position", "P", 26, true, Align.Right).Fit("88"),
                new ColumnDef("gain", "Positions gained", "+/-", 38, race, Align.Left).Fit("▲88"),
                new ColumnDef("num", "Car number", "#", 34, true, Align.Center).Fit("888"),
                NameColumn(190),
                new ColumnDef("brand", "Car brand", "CAR", 34, true, Align.Center).Fit("LAM", Small * 0.9f, 2),
                LicenseColumn(),
                RatingColumn(),
                new ColumnDef("irdelta", "iRating change (est.)", "iR+/-", 46, race, Align.Left).Fit("▲888"),
                // race gaps: 53.0, 1:02.3 or +3L; P&Q: +0.532
                new ColumnDef("gap", race ? "Gap to leader" : "Gap to fastest", "GAP", 50, true, Align.Right)
                    .Fit(f => race ? "88." + Digits(f) + "|8:88.8|+88L" : "+88." + Digits(f)).WithFormats(race ? "1" : "3", Decimals),
                new ColumnDef("int", race ? "Interval" : "Gap to car ahead", "INT", 50, race, Align.Right)
                    .Fit(f => race ? "88." + Digits(f) + "|8:88.8|+88L" : "+88." + Digits(f)).WithFormats(race ? "1" : "3", Decimals),
                new ColumnDef("laps", "Laps completed", "LAPS", 32, !race, Align.Right).Fit("888"),
                LastLapColumn(true, race ? "1" : "3"),
                new ColumnDef("best", "Best lap", "BEST", 62, !race, Align.Right).Fit(f => "8:88." + Digits(f)).WithFormats("3", LapDecimals),
                new ColumnDef("tire", "Tire compound", "TIRE", 22, false, Align.Center)
                    .WithFormats("differ", ("differ", "Only when mixed"), ("always", "Always")),
                new ColumnDef("pit", "Pit status and flags", "PIT", 42, true, Align.Center).Fit("TOW", Small, 10),
                new ColumnDef("stint", "Laps in stint", "STINT", 32, race, Align.Right).Fit("88"),
            };
        }

        public static ColumnDef[] Header(SessionKind kind)
        {
            bool race = kind == SessionKind.Race;
            return new[]
            {
                HeaderBar.Item("session", true), HeaderBar.Item("class", true), HeaderBar.Item("laps", race), HeaderBar.Item("time", true),
                HeaderBar.Item("spacer", true),
                HeaderBar.Item("tracktemp", true), HeaderBar.Item("airtemp", false), HeaderBar.Item("humidity", false),
                HeaderBar.Item("sof", true), HeaderBar.Item("cars", true), HeaderBar.Item("incidents", false), HeaderBar.Item("clock", false),
            };
        }
    }
    public sealed class StandingsSettings : WidgetSettings, ISessionProfiles, INormalizable
    {
        public override string TypeName { get { return "Standings"; } }

        public StandingsProfile PracticeQualify { get; set; } = new StandingsProfile(SessionKind.PracticeQualify);
        public StandingsProfile Race { get; set; } = new StandingsProfile(SessionKind.Race);

        [Setting("Font size", Group = "Text & size", Min = 8, Max = 30, Step = 0.5, Order = 3, Tooltip = "Size of the text in the rows; badges and the header follow it")] public double FontSize { get; set; } = 14;
        [Setting("Row height", Group = "Text & size", Min = 12, Max = 50, Order = 4, Tooltip = "Height of each row (px)")] public int RowHeight { get; set; } = 22;
        [Setting("Column spacing", Group = "Text & size", Min = 0, Max = 30, Order = 5, Tooltip = "Space between the columns (px). Column widths follow the content and the font size.")]
        public int ColumnSpacing { get; set; } = 8;
        [Setting("Header background", Group = "Colors", IsColor = true, Order = 2, Tooltip = "Colour of the header bar (and of the class headers and the gaps between blocks)")] public string HeaderColor { get; set; } = "#FF1C1C1C";
        [Setting("Row background", Group = "Colors", IsColor = true, Order = 3, Tooltip = "Colour of the rows")] public string RowColor { get; set; } = "#FF1E1E1E";
        [Setting("Alternate row", Group = "Colors", IsColor = true, Order = 4, Tooltip = "Colour of every second row, so long lists are easier to follow")] public string RowAltColor { get; set; } = "#FF282828";
        [Setting("Player row", Group = "Colors", IsColor = true, Order = 5, Tooltip = "Colour of your own row")] public string PlayerColor { get; set; } = "#FF8E2A2A";
        [Setting("Text", Group = "Colors", IsColor = true, Order = 6, Tooltip = "Colour of the text in the rows")] public string TextColor { get; set; } = "#FFFFFFFF";

        public StandingsSettings() { Title = "Standings"; }

        public object Profile(SessionKind kind) { return kind == SessionKind.Race ? Race : PracticeQualify; }
        public StandingsProfile For(bool isRace) { return isRace ? Race : PracticeQualify; }

        // ---- settings from before the P&Q / Race split (read once, then moved into both profiles)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NameStyle? NameStyle { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowHeader { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowColumnTitles { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? LapDecimals { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Rows { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? TopRows { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MyClassRows { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MyClassTopRows { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? OtherClassRows { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowClassHeaders { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? PlayerClassFirst { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ColumnConfig> Columns { get; set; }

        public void Normalize()
        {
            if (PracticeQualify == null) PracticeQualify = new StandingsProfile(SessionKind.PracticeQualify);
            if (Race == null) Race = new StandingsProfile(SessionKind.Race);
            PracticeQualify.Kind = SessionKind.PracticeQualify;
            Race.Kind = SessionKind.Race;
            foreach (var p in new[] { PracticeQualify, Race }) ApplyLegacy(p);
            NameStyle = null; ShowHeader = null; ShowColumnTitles = null; LapDecimals = null; Rows = null; TopRows = null;
            MyClassRows = null; MyClassTopRows = null; OtherClassRows = null; ShowClassHeaders = null; PlayerClassFirst = null; Columns = null;
            PracticeQualify.MergeColumns();
            Race.MergeColumns();
        }

        void ApplyLegacy(StandingsProfile p)
        {
            if (ShowHeader.HasValue) p.ShowHeader = ShowHeader.Value;
            if (ShowColumnTitles.HasValue) p.ShowColumnTitles = ShowColumnTitles.Value;
            if (Rows.HasValue) p.Rows = Rows.Value;
            if (TopRows.HasValue) p.TopRows = TopRows.Value;
            if (MyClassRows.HasValue) p.MyClassRows = MyClassRows.Value;
            if (MyClassTopRows.HasValue) p.MyClassTopRows = MyClassTopRows.Value;
            if (OtherClassRows.HasValue) p.OtherClassRows = OtherClassRows.Value;
            if (ShowClassHeaders.HasValue) p.ShowClassHeaders = ShowClassHeaders.Value;
            if (PlayerClassFirst.HasValue) p.PlayerClassFirst = PlayerClassFirst.Value;
            if (Columns != null)
            {
                // keep the user's order, visibility and widths; formats come from the old single settings
                p.Columns = Columns.Select(c => new ColumnConfig { Key = c.Key, Enabled = c.Enabled, Width = c.Width }).ToList();
                p.MergeColumns();
            }
            if (NameStyle.HasValue) p.Column("name").Format = NameStyle.Value == Widgets.NameStyle.Short ? "short" : NameStyle.Value == Widgets.NameStyle.LastName ? "last" : "full";
            if (LapDecimals.HasValue)
            {
                string d = Math.Max(1, Math.Min(3, LapDecimals.Value)).ToString();
                p.Column("last").Format = d;
                p.Column("best").Format = d;
            }
        }
    }

    public sealed class StandingsWidget : Widget
    {
        const float Pad = 6, HeaderGap = 16, Corner = 6;
        const uint Dim = 0xFF9A9A9A, Green = 0xFF3CC850, Red = 0xFFE8433A, Purple = 0xFFA04BE0, Orange = 0xFFF08C1E;

        readonly StandingsSettings s;
        StandingsProfile pr;          // profile of the session being drawn
        bool compoundsDiffer;

        public StandingsWidget(StandingsSettings s) : base(s) { this.s = s; }

        static int Dec(ColumnConfig c, int fallback)
        {
            int d;
            return c != null && int.TryParse(c.Format, out d) ? d : fallback;
        }

        public override void Draw(DisplayList dl, RaceSnapshot snap)
        {
            pr = s.For(snap.IsRace);
            bool multi = snap.Classes.Count > 1;
            compoundsDiffer = snap.Cars.Where(c => c.InWorld).Select(c => c.TireCompound).Distinct().Count() > 1;
            float fs = (float)s.FontSize, rh = s.RowHeight, sp = Math.Max(0, s.ColumnSpacing);
            var cols = TableColumns.Resolve(pr.Columns.Where(c => c.Enabled && (multi || c.Key != "classbar")), pr.AvailableColumns, fs, pr.ShowColumnTitles);
            float width = Pad * 2 + cols.Sum(c => c.Width) + sp * Math.Max(0, cols.Count - 1);
            float y = 0;

            if (pr.ShowHeader)
            {
                // the header is part of the widget: no gap, one shape with rounded corners (RoundCorners below)
                dl.Rect(0, 0, width, rh + 2, Bg(Argb.Parse(s.HeaderColor)));
                HeaderBar.Draw(dl, snap, pr.Header, "STANDINGS", width, rh + 2, fs, Pad, HeaderGap);
                y += rh + 2;
            }
            if (pr.ShowColumnTitles)
            {
                float x = Pad;
                dl.Rect(0, y, width, rh, Bg(Argb.Parse(s.HeaderColor)));
                foreach (var c in cols)
                {
                    var def = pr.AvailableColumns.First(d => d.Key == c.Key);
                    dl.Text(x, y, c.Width, rh, def.Header, fs * 0.75f, Dim, def.Align);
                    x += c.Width + sp;
                }
                y += rh;
            }

            if (multi)
            {
                var classes = snap.Classes.ToList();
                if (pr.PlayerClassFirst && snap.PlayerClass != null && classes.Remove(snap.PlayerClass)) classes.Insert(0, snap.PlayerClass);
                bool first = true;
                foreach (var cs in classes)
                {
                    bool mine = cs == snap.PlayerClass;
                    int rows = mine ? pr.MyClassRows : pr.OtherClassRows;
                    if (rows <= 0 || cs.Cars.Count == 0) continue;
                    if (!first) y = Gap(dl, y, 4, width);
                    first = false;
                    if (pr.ShowClassHeaders) { DrawClassHeader(dl, cs, y, width, rh, fs); y += rh; }
                    // in my class: leaders + a window around my position
                    int top = mine ? Math.Min(pr.MyClassTopRows, rows - 1) : rows;
                    y = DrawBlock(dl, snap, cs.Cars, rows, Math.Max(0, top), cols, y, width, rh, fs);
                }
            }
            else
            {
                var field = snap.PlayerClass != null ? snap.PlayerClass.Cars : snap.Cars;
                int total = Math.Max(1, pr.Rows);
                y = DrawBlock(dl, snap, field, total, Math.Max(0, Math.Min(pr.TopRows, total)), cols, y, width, rh, fs);
            }
            dl.Width = width;
            dl.Height = y;
            dl.RoundCorners(Corner);
        }

        /// <summary>Space between blocks, in the header colour: the widget stays one shape instead of loose pieces.</summary>
        float Gap(DisplayList dl, float y, float h, float width)
        {
            dl.Rect(0, y, width, h, Bg(Argb.Parse(s.HeaderColor)));
            return y + h;
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
                    DrawCell(dl, c, car, snap, x, y, c.Width, rh, fs, text, car == field[0]);
                    x += c.Width + Math.Max(0, s.ColumnSpacing);
                }
                y += rh;
                if (r == splitAfter) y = Gap(dl, y, 6, width);
            }
            if (splitAfter < 0 && field.Count > total && top > 0 && top < total) y = Gap(dl, y, 6, width); // keep the height stable
            return y;
        }

        void DrawClassHeader(DisplayList dl, ClassStandings cs, float y, float width, float rh, float fs)
        {
            uint color = Argb.FromRgb(cs.Color);
            dl.Rect(0, y, width, rh, Bg(Argb.Parse(s.HeaderColor)));
            dl.Rect(0, y, 4, rh, color);
            dl.Text(Pad + 4, y, width * 0.5f, rh, cs.Name, fs * 0.9f, color);
            var sof = pr.HeaderItem("sof");
            string info = "SOF " + Fmt.Rating(cs.Sof, sof != null ? sof.Format : "full") + "   " + cs.Cars.Count(c => c.InWorld) + "/" + cs.Cars.Count;
            if (cs.BestLap > 0) info = "Best " + Fmt.Lap(cs.BestLap, Dec(pr.Column("best"), 3)) + "   " + info;
            dl.Text(width * 0.4f, y, width * 0.6f - Pad, rh, info, fs * 0.8f, Dim, Align.Right);
        }

        void DrawCell(DisplayList dl, ColumnConfig col, CarInfo c, RaceSnapshot snap, float x, float y, float w, float h, float fs, uint text, bool leaderRow)
        {
            float small = fs * 0.85f;
            switch (col.Key)
            {
                case "classbar":
                    dl.Rect(x, y + 2, w, h - 4, Argb.FromRgb(c.ClassColor));
                    break;
                case "pos":
                    if (c.ClassPos > 0) dl.Text(x, y, w, h, c.ClassPos.ToString(), fs, text, Align.Right);
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
                    string name = StandingsDefs.DriverName(c, col.Format);
                    dl.Text(x, y, w, h, name, fs, c.InWorld || c.LapsComplete > 0 ? text : Dim, Align.Left);
                    break;
                case "brand":
                    dl.Text(x, y, w, h, c.Brand, small * 0.9f, 0xFFB0B0B0, Align.Center);
                    break;
                case "lic":
                    uint lc = Fmt.LicenseColor(c.LicLetter);
                    dl.Badge(x, y + 3, w, h - 6, Fmt.License(c.LicLetter, c.LicSR, col.Format), small, lc, Argb.ContrastText(lc), 4);
                    break;
                case "ir":
                    dl.Text(x, y, w, h, Fmt.Rating(c.IRating, col.Format), fs, text, Align.Right);
                    break;
                case "irdelta":
                    if (c.IRatingDelta.HasValue)
                    {
                        int d = (int)Math.Round(c.IRatingDelta.Value);
                        dl.Text(x, y, w, h, (d > 0 ? "▲" : d < 0 ? "▼" : "") + Math.Abs(d), fs, d > 0 ? Green : d < 0 ? Red : Dim, Align.Left);
                    }
                    break;
                case "gap":
                    if (leaderRow) dl.Text(x, y, w, h, "GAP", small, Dim, Align.Right);   // P1 of each class: column label instead of a value
                    else if (snap.IsRace && c.LapsDown > 0) dl.Text(x, y, w, h, "+" + c.LapsDown + "L", fs, text, Align.Right);
                    else if (c.GapToClassLeader.HasValue) dl.Text(x, y, w, h, GapText(c.GapToClassLeader.Value, Dec(col, 1), snap.IsRace), fs, text, Align.Right);
                    break;
                case "int":
                    if (leaderRow) dl.Text(x, y, w, h, "INT", small, Dim, Align.Right);
                    else if (snap.IsRace && c.IntervalLaps > 0) dl.Text(x, y, w, h, "+" + c.IntervalLaps + "L", fs, text, Align.Right);
                    else if (c.Interval.HasValue) dl.Text(x, y, w, h, GapText(c.Interval.Value, Dec(col, 1), snap.IsRace), fs, text, Align.Right);
                    break;
                case "laps":
                    if (c.LapsComplete > 0) dl.Text(x, y, w, h, c.LapsComplete.ToString(), fs, text, Align.Right);
                    break;
                case "last":
                    if (c.LastLap > 0)
                    {
                        dl.Text(x, y, w, h, Fmt.Lap(c.LastLap, Dec(col, 1)), fs, text, Align.Right);
                        if (c.LastIsClassBest) dl.Rect(x + 4, y + h - 3, w - 4, 2, Purple);
                        else if (c.LastIsPersonalBest) dl.Rect(x + 4, y + h - 3, w - 4, 2, Green);
                    }
                    break;
                case "best":
                    if (c.BestLap > 0) dl.Text(x, y, w, h, Fmt.Lap(c.BestLap, Dec(col, 3)), fs, text, Align.Right);
                    break;
                case "tire":
                    // iRacing reports a compound index per car; the names ("Hard", "Wet") only come for the player's car,
                    // so other car types in a multiclass field may show a best guess
                    if (c.TireCompound >= 0 && c.InWorld && (col.Format == "always" || compoundsDiffer))
                    {
                        string tireName;
                        if (!snap.TireNames.TryGetValue(c.TireCompound, out tireName) || string.IsNullOrEmpty(tireName)) tireName = c.TireCompound == 0 ? "Dry" : "Wet";
                        bool wet = tireName.StartsWith("W", StringComparison.OrdinalIgnoreCase);
                        float d = Math.Min(w, h - 6);
                        dl.Badge(x + (w - d) / 2, y + (h - d) / 2, d, d, tireName.Substring(0, 1).ToUpperInvariant(), small * 0.8f, wet ? 0xFF2F8CFF : 0xFF5A5F66, 0xFFFFFFFF, d / 2);
                    }
                    break;
                case "pit":
                    DrawPitStatus(dl, c, x, y, w, h, small, true, snap.IsRace);
                    break;
                case "stint":
                    if (c.Lap > 0) dl.Text(x, y, w, h, c.StintLaps.ToString(), fs, text, Align.Right);
                    break;
            }
        }

        /// <summary>Race: 53.0 / 1:02.3 with the chosen decimals; P&amp;Q: +0.532 (difference in lap time).</summary>
        static string GapText(double v, int decimals, bool race)
        {
            if (race) return Fmt.Gap(Math.Abs(v), decimals);
            return "+" + Math.Abs(v).ToString(decimals <= 0 ? "0" : "0." + new string('0', decimals), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
