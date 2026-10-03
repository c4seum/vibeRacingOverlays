using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.Widgets
{
    public enum ClockSource { RealTime, SimTime }

    /// <summary>Where the number of pit stops is shown.</summary>
    public enum StopsDisplay { Off, NextToRefuel, Column }

    /// <summary>Classic: big values in a free grid (the original look). Table: header bar, big fuel level and laps, then one row per estimate, like the Relative and Standings.</summary>
    public enum FuelLayout { Classic, Table }

    public sealed class FuelSettings : WidgetSettings
    {
        public override string TypeName { get { return "Fuel calculator"; } }

        // Classic keeps the look of existing widgets; the Default preset uses Table (DEVELOPMENT.md, "Changing a widget")
        [Setting("Layout", Group = "Content", Order = 0, Tooltip = "Table: in the style of the Relative and Standings (header bar, big fuel level and laps, a row per estimate). Classic: the original free layout.")]
        public FuelLayout Layout { get; set; } = FuelLayout.Classic;

        [Setting("Safety margin (L)", Group = "Content", Min = 0, Max = 5, Step = 0.1, Order = 1)] public double Margin { get; set; } = 0.5;
        [Setting("Custom per lap (0 = average)", Group = "Content", Min = 0, Max = 20, Step = 0.01, Order = 2)] public double CustomPerLap { get; set; } = 0;
        [Setting("Clock", Group = "Content", Order = 3)] public ClockSource Clock { get; set; } = ClockSource.RealTime;
        [Setting("Show 'Last' row", Group = "Content", Order = 4)] public bool ShowLast { get; set; } = true;
        [Setting("Show 'Custom' row", Group = "Content", Order = 5)] public bool ShowCustom { get; set; } = true;
        [Setting("Show 'Last N laps' row", Group = "Content", Order = 6, Tooltip = "Average fuel use of the last N valid laps (no pit visits, refuels or cautions)")]
        public bool ShowLastN { get; set; } = false;
        [Setting("Laps for 'Last N' average", Group = "Content", Min = 2, Max = 50, Order = 7)] public int LastNLaps { get; set; } = 5;
        [Setting("Show 'Stint' row", Group = "Content", Order = 8, Tooltip = "Average fuel use of the valid laps since your last pit stop")]
        public bool ShowStint { get; set; } = false;
        [Setting("Stops", Group = "Content", Order = 9, Tooltip = "Pit stops needed to finish (0 = you make it without refuelling): the refuel amount divided by the tank capacity, rounded up. "
            + "Next to refuel: small, above the refuel value. Column: a column of its own.")]
        public StopsDisplay Stops { get; set; } = StopsDisplay.NextToRefuel;

        [Setting("Value font size", Group = "Style", Min = 12, Max = 40, Order = 20)] public double FontSize { get; set; } = 24;
        [Setting("Background", Group = "Style", IsColor = true, Order = 21)] public string BackgroundColor { get; set; } = "#FF2B2B2B";
        [Setting("Average color", Group = "Style", IsColor = true, Order = 22)] public string AvgColor { get; set; } = "#FF8FA8F0";
        [Setting("Last color", Group = "Style", IsColor = true, Order = 23)] public string LastColor { get; set; } = "#FFF0C36A";
        [Setting("Custom color", Group = "Style", IsColor = true, Order = 24)] public string CustomColor { get; set; } = "#FF7ED67E";
        [Setting("Last N color", Group = "Style", IsColor = true, Order = 25)] public string LastNColor { get; set; } = "#FFD99AF0";
        [Setting("Stint color", Group = "Style", IsColor = true, Order = 26)] public string StintColor { get; set; } = "#FF6FD3D8";

        public FuelSettings() { Title = "Fuel calculator"; RefreshHz = 4; Show = ShowWhen.InCar; }
    }

    public sealed class FuelWidget : Widget
    {
        const uint White = 0xFFFFFFFF, Red = 0xFFE8433A, Yellow = 0xFFF2C318;
        readonly FuelSettings s;
        float stopsX, stopsW;   // the optional stops column (width 0 = no column)

        public FuelWidget(FuelSettings s) : base(s) { this.s = s; }

        public override void Draw(DisplayList dl, RaceSnapshot snap)
        {
            if (s.Layout == FuelLayout.Table) { DrawTable(dl, snap); return; }
            var f = snap.Fuel;
            float big = (float)s.FontSize, label = big * 0.46f;
            float pad = big * 0.42f;
            // columns are 3.3x the value size (the original look); wider fonts (Orbitron, mono) get the room their values and labels need
            float need = Math.Max(dl.Measure("88.88", big), new[] { "Laps in Race", "Laps Remain", "Fuel at End", "Clock 24h" }.Max(t => dl.Measure(t, label)));
            float colW = Math.Max(big * 3.3f, need + big * 0.5f);
            float lblH = label * 1.35f, valH = big * 1.2f, block = lblH + valH + big * 0.25f;
            int rows = 2 + (s.ShowLast ? 1 : 0) + (s.ShowLastN ? 1 : 0) + (s.ShowStint ? 1 : 0) + (s.ShowCustom ? 1 : 0);
            // optional stops column between "Refuel" and "Fuel at End" (narrower: it only holds a small number)
            stopsW = s.Stops == StopsDisplay.Column ? colW * 0.6f : 0;
            stopsX = pad + colW * 3;
            float width = pad * 2 + colW * 4 + stopsW, height = pad + block * rows;

            dl.Rect(0, 0, width, height, Bg(Argb.Parse(s.BackgroundColor)), 6);
            float[] x = { pad, pad + colW, pad + colW * 2, pad + colW * 3 + stopsW };
            float y = pad * 0.6f;

            // top row: level, laps to go, pit indicator, clock
            dl.Text(x[0], y, colW, lblH, "Fuel Level", label, White);
            dl.Text(x[0], y + lblH, colW, valH, Fmt.Num(f.Level, "0.00"), big, White);
            dl.Text(x[1], y, colW, lblH, "Laps in Race", label, White);
            dl.Text(x[1], y + lblH, colW, valH, f.LapsToGo >= 0 ? Fmt.Num(f.LapsToGo, "0.0") : "-", big, White);
            DrawPit(dl, f, x[2] + colW * 0.12f, y + lblH * 0.4f, colW * 0.62f, valH * 0.8f, label * 1.15f);
            dl.Text(x[3], y, colW, lblH, s.Clock == ClockSource.RealTime ? "Clock 24h" : "Sim time", label, White);
            string clock = s.Clock == ClockSource.RealTime ? DateTime.Now.ToString("HH:mm") : TimeSpan.FromSeconds(snap.TimeOfDay).ToString(@"hh\:mm");
            dl.Text(x[3], y + lblH, colW, valH, clock, big, White);
            y += block;

            // the per-lap rows use the values from the last line crossing (or pit exit): they change once a lap,
            // not with every drop of fuel; level, laps in race and the pit indicator above stay live
            f = f.AtLine ?? f;
            double custom = s.CustomPerLap > 0 ? s.CustomPerLap : Math.Round(f.AvgPerLap, 2);
            DrawRow(dl, f, "Average", f.AvgPerLap, Argb.Parse(s.AvgColor), x, y, colW, lblH, valH, label, big, true);
            y += block;
            if (s.ShowLast) { DrawRow(dl, f, "Last", f.LastPerLap, Argb.Parse(s.LastColor), x, y, colW, lblH, valH, label, big, false); y += block; }
            if (s.ShowLastN)
            {
                int n = Math.Max(2, s.LastNLaps), have = Math.Min(n, f.Laps.Length);
                // "Last 3/5 avg" while there aren't n valid laps yet
                string name = "Last " + (have > 0 && have < n ? have + "/" : "") + n + " avg";
                DrawRow(dl, f, name, f.AverageOfLast(n), Argb.Parse(s.LastNColor), x, y, colW, lblH, valH, label, big, false);
                y += block;
            }
            if (s.ShowStint)
            {
                // "Stint avg (4)": number of valid laps since the last pit stop
                string name = "Stint avg" + (f.StintValidLaps > 0 ? " (" + f.StintValidLaps + ")" : "");
                DrawRow(dl, f, name, f.StintAvgPerLap, Argb.Parse(s.StintColor), x, y, colW, lblH, valH, label, big, false);
                y += block;
            }
            if (s.ShowCustom) { DrawRow(dl, f, s.CustomPerLap > 0 ? "Custom" : "Custom (avg)", custom, Argb.Parse(s.CustomColor), x, y, colW, lblH, valH, label, big, false); y += block; }

            dl.Width = width;
            dl.Height = height;
        }

        void DrawRow(DisplayList dl, FuelInfo f, string name, double perLap, uint color, float[] x, float y, float colW, float lblH, float valH, float label, float big, bool headers)
        {
            dl.Text(x[0], y, colW, lblH, name, label, color);
            if (headers)
            {
                dl.Text(x[1], y, colW, lblH, "Laps Remain", label, color);
                dl.Text(x[2], y, colW, lblH, "Refuel", label, color);
                if (stopsW > 0) dl.Text(stopsX, y, stopsW, lblH, "Stops", label, color);
                dl.Text(x[3], y, colW, lblH, "Fuel at End", label, color);
            }
            float vy = y + lblH;
            if (perLap <= 0)
            {
                dl.Text(x[0], vy, colW, valH, "-", big, color);
                return;
            }
            dl.Text(x[0], vy, colW, valH, Fmt.Num(perLap, "0.00"), big, color);
            dl.Text(x[1], vy, colW, valH, Fmt.Num(f.LapsRemaining(perLap), "0.00"), big, color);
            if (f.LapsToGo >= 0)
            {
                dl.Text(x[2], vy, colW, valH, Fmt.Num(f.Refuel(perLap, s.Margin), "0.00"), big, color);
                int stops = f.StopsNeeded(perLap, s.Margin);
                // "0 stops" too; but not when the tank size is unknown and fuel is still needed (that would read as 0)
                bool known = f.MaxFuel > 0 || f.Refuel(perLap, s.Margin) <= 0;
                if (known && s.Stops == StopsDisplay.Column)
                    dl.Text(stopsX, vy, stopsW, valH, stops.ToString(), big, color);
                else if (known && s.Stops == StopsDisplay.NextToRefuel)
                {
                    // on the label line, right above the refuel value ("Refuel" header on the left of it in the first row)
                    float gap = label * 0.8f;
                    dl.Text(x[2], y, colW - gap, lblH, stops == 1 ? "1 stop" : stops + " stops", label, color, Align.Right);
                }
                double end = f.FuelAtEnd(perLap);
                dl.Text(x[3], vy, colW, valH, Fmt.Num(end, "0.00"), big, end < 0 ? Red : color);
            }
        }

        // ---------------------------------------------------------------- Table layout (like the Relative and Standings)

        const uint Dim = 0xFF9A9A9A, HeaderColor = 0xFF1C1C1C, RowColor = 0xFF1E1E1E, RowAltColor = 0xFF282828;

        /// <summary>The estimates shown, in order: name, fuel per lap, colour.</summary>
        List<(string Name, double PerLap, uint Color)> Estimates(FuelInfo f)
        {
            var list = new List<(string, double, uint)> { ("Average", f.AvgPerLap, Argb.Parse(s.AvgColor)) };
            if (s.ShowLast) list.Add(("Last", f.LastPerLap, Argb.Parse(s.LastColor)));
            if (s.ShowLastN)
            {
                int n = Math.Max(2, s.LastNLaps), have = Math.Min(n, f.Laps.Length);
                list.Add(("Last " + (have > 0 && have < n ? have + "/" : "") + n + " avg", f.AverageOfLast(n), Argb.Parse(s.LastNColor)));
            }
            if (s.ShowStint) list.Add(("Stint avg" + (f.StintValidLaps > 0 ? " (" + f.StintValidLaps + ")" : ""), f.StintAvgPerLap, Argb.Parse(s.StintColor)));
            if (s.ShowCustom) list.Add((s.CustomPerLap > 0 ? "Custom" : "Custom (avg)", s.CustomPerLap > 0 ? s.CustomPerLap : Math.Round(f.AvgPerLap, 2), Argb.Parse(s.CustomColor)));
            return list;
        }

        /// <summary>
        /// Header bar (title and clock), a band with the fuel level and laps in race big and the pit indicator, column
        /// titles, then a row per estimate with a colour bar. One rounded shape, like the Relative and Standings.
        /// "Value font size" scales it all: the rows are 0.58x (24 = the Relative's 14).
        /// </summary>
        void DrawTable(DisplayList dl, RaceSnapshot snap)
        {
            var live = snap.Fuel;
            var f = live.AtLine ?? live;   // estimates change once a lap (see Draw); level, laps and pit stay live
            float fs = (float)s.FontSize * 0.58f, small = fs * 0.75f;
            float rh = (float)Math.Round(fs * 1.8f), hh = rh + 2, sp = (float)Math.Round(fs * 0.7f), pad = 8;
            float heroFs = fs * 1.9f, heroLbl = fs * 0.7f;
            var rows = Estimates(f);
            bool stopsCol = s.Stops != StopsDisplay.Off;
            // measured widths get 2 px of slack: text is placed on whole pixels and must never be cut off by a hair
            Func<string, float, float> M = (t, size) => dl.Measure(t, size) + 2;

            // columns: colour bar | name | L/lap | laps | refuel | (stops) | at end
            float num = Math.Max(M("188.88", fs), M("REFUEL", small));
            var w = new List<float> { 4, rows.Max(r => M(r.Name, fs)), num, num, num };
            if (stopsCol) w.Add(Math.Max(M("8", fs), M("STOPS", small)));
            w.Add(Math.Max(M("-188.88", fs), M("AT END", small)));
            var x = new float[w.Count];
            for (int i = 1; i < w.Count; i++) x[i] = x[i - 1] + w[i - 1] + sp;
            float width = x[w.Count - 1] + w[w.Count - 1] + pad;

            string level = Fmt.Num(live.Level, "0.00"), laps = live.LapsToGo >= 0 ? Fmt.Num(live.LapsToGo, "0.0") : "-";
            float levelW = Math.Max(M("188.88", heroFs), M("FUEL LEVEL", heroLbl)), lapsW = Math.Max(M("188.8", heroFs), M("LAPS IN RACE", heroLbl));
            float pitW = dl.Measure("PIT", fs) + fs * 1.4f, pitH = rh * 0.85f;
            width = Math.Max(width, pad + levelW + fs * 1.5f + lapsW + fs * 1.5f + pitW + pad);

            // header bar: title, clock
            float y = 0;
            dl.Rect(0, y, width, hh, Bg(HeaderColor));
            dl.Text(pad, y, width / 2, hh, "FUEL", fs, White);
            string clock = s.Clock == ClockSource.RealTime ? DateTime.Now.ToString("HH:mm") : TimeSpan.FromSeconds(snap.TimeOfDay).ToString(@"hh\:mm");
            float clockW = M("88:88", fs);
            dl.Text(width - pad - clockW, y, clockW, hh, clock, fs, White, Align.Right);
            string clockLbl = s.Clock == ClockSource.RealTime ? "Clock" : "Sim time";
            dl.Text(width / 2, y, width / 2 - pad - clockW - fs * 0.6f, hh, clockLbl, small, Dim, Align.Right);
            y += hh;

            // big band: fuel level, laps in race, pit indicator
            float lblH = heroLbl * 1.5f, valH = heroFs * 1.15f, heroH = lblH + valH + 6;
            dl.Rect(0, y, width, heroH, Bg(RowColor));
            float hx = pad + 2;
            dl.Text(hx, y + 3, levelW, lblH, "FUEL LEVEL", heroLbl, Dim);
            dl.Text(hx, y + 3 + lblH, levelW, valH, level, heroFs, White);
            hx += levelW + fs * 1.5f;
            dl.Text(hx, y + 3, lapsW, lblH, "LAPS IN RACE", heroLbl, Dim);
            dl.Text(hx, y + 3 + lblH, lapsW, valH, laps, heroFs, White);
            DrawPit(dl, live, width - pad - pitW, y + (heroH - pitH) / 2, pitW, pitH, fs);
            y += heroH;

            // column titles
            dl.Rect(0, y, width, rh, Bg(HeaderColor));
            var titles = new List<string> { "", "", "L/LAP", "LAPS", "REFUEL" };
            if (stopsCol) titles.Add("STOPS");
            titles.Add("AT END");
            for (int i = 2; i < titles.Count; i++) dl.Text(x[i], y, w[i], rh, titles[i], small, Dim, Align.Right);
            y += rh;

            // a row per estimate
            for (int r = 0; r < rows.Count; r++)
            {
                var (name, perLap, color) = rows[r];
                dl.Rect(0, y, width, rh, Bg(r % 2 == 1 ? RowAltColor : RowColor));
                dl.Rect(x[0], y + 2, w[0], rh - 4, color);
                dl.Text(x[1], y, w[1], rh, name, fs, color);
                if (perLap > 0)
                {
                    int c = 2;
                    dl.Text(x[c], y, w[c], rh, Fmt.Num(perLap, "0.00"), fs, White, Align.Right); c++;
                    dl.Text(x[c], y, w[c], rh, Fmt.Num(f.LapsRemaining(perLap), "0.00"), fs, White, Align.Right); c++;
                    if (f.LapsToGo >= 0)
                    {
                        dl.Text(x[c], y, w[c], rh, Fmt.Num(f.Refuel(perLap, s.Margin), "0.00"), fs, White, Align.Right); c++;
                        if (stopsCol)
                        {
                            // not when the tank size is unknown and fuel is still needed (that would read as 0)
                            bool known = f.MaxFuel > 0 || f.Refuel(perLap, s.Margin) <= 0;
                            if (known) dl.Text(x[c], y, w[c], rh, f.StopsNeeded(perLap, s.Margin).ToString(), fs, White, Align.Right);
                            c++;
                        }
                        double end = f.FuelAtEnd(perLap);
                        dl.Text(x[c], y, w[c], rh, Fmt.Num(end, "0.00"), fs, end < 0 ? Red : White, Align.Right);
                    }
                }
                else dl.Text(x[2], y, w[2], rh, "-", fs, Dim, Align.Right);
                y += rh;
            }

            dl.Width = width;
            dl.Height = y;
            dl.RoundCorners(6);
        }

        void DrawPit(DisplayList dl, FuelInfo f, float x, float y, float w, float h, float size)
        {
            // off / stop needed / box within 2 laps / box now
            int state = 0;
            if (f.Valid && f.LapsToGo >= 0 && f.Level < f.Need(f.AvgPerLap) + s.Margin)
            {
                double left = f.LapsRemaining(f.AvgPerLap);
                state = left < 1 ? 3 : left < 2 ? 2 : 1;
            }
            switch (state)
            {
                case 3: dl.Badge(x, y, w, h, "PIT", size, Red, White); break;
                case 2: dl.Badge(x, y, w, h, "PIT", size, Yellow, 0xFF000000); break;
                case 1:
                    dl.Rect(x, y, w, h, 0x40F2C318, 3);
                    dl.Text(x, y, w, h, "PIT", size, Yellow, Align.Center);
                    break;
                default:
                    dl.Rect(x, y, w, h, 0x30FFFFFF, 3);
                    dl.Text(x, y, w, h, "PIT", size, 0xFF6A6A6A, Align.Center);
                    break;
            }
        }
    }
}
