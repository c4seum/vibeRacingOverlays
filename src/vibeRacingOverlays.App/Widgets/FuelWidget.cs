using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.Widgets
{
    public enum ClockSource { RealTime, SimTime }

    public sealed class FuelSettings : WidgetSettings
    {
        public override string TypeName { get { return "Fuel calculator"; } }

        [Setting("Safety margin (L)", Group = "Content", Min = 0, Max = 5, Step = 0.1, Order = 1)] public double Margin { get; set; } = 0.5;
        [Setting("Custom per lap (0 = average)", Group = "Content", Min = 0, Max = 20, Step = 0.01, Order = 2)] public double CustomPerLap { get; set; } = 0;
        [Setting("Clock", Group = "Content", Order = 3)] public ClockSource Clock { get; set; } = ClockSource.RealTime;
        [Setting("Show 'Last' row", Group = "Content", Order = 4)] public bool ShowLast { get; set; } = true;
        [Setting("Show 'Custom' row", Group = "Content", Order = 5)] public bool ShowCustom { get; set; } = true;

        [Setting("Value font size", Group = "Style", Min = 12, Max = 40, Order = 20)] public double FontSize { get; set; } = 24;
        [Setting("Background", Group = "Style", IsColor = true, Order = 21)] public string BackgroundColor { get; set; } = "#FF2B2B2B";
        [Setting("Average color", Group = "Style", IsColor = true, Order = 22)] public string AvgColor { get; set; } = "#FF8FA8F0";
        [Setting("Last color", Group = "Style", IsColor = true, Order = 23)] public string LastColor { get; set; } = "#FFF0C36A";
        [Setting("Custom color", Group = "Style", IsColor = true, Order = 24)] public string CustomColor { get; set; } = "#FF7ED67E";

        public FuelSettings() { Title = "Fuel calculator"; RefreshHz = 4; Show = ShowWhen.InCar; }
    }

    public sealed class FuelWidget : Widget
    {
        const uint White = 0xFFFFFFFF, Red = 0xFFE8433A, Yellow = 0xFFF2C318;
        readonly FuelSettings s;

        public FuelWidget(FuelSettings s) : base(s) { this.s = s; }

        public override void Draw(DisplayList dl, RaceSnapshot snap)
        {
            var f = snap.Fuel;
            float big = (float)s.FontSize, label = big * 0.46f;
            float colW = big * 3.3f, pad = big * 0.42f;
            float lblH = label * 1.35f, valH = big * 1.2f, block = lblH + valH + big * 0.25f;
            int rows = 2 + (s.ShowLast ? 1 : 0) + (s.ShowCustom ? 1 : 0);
            float width = pad * 2 + colW * 4, height = pad + block * rows;

            dl.Rect(0, 0, width, height, Bg(Argb.Parse(s.BackgroundColor)), 6);
            float[] x = { pad, pad + colW, pad + colW * 2, pad + colW * 3 };
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

            double custom = s.CustomPerLap > 0 ? s.CustomPerLap : Math.Round(f.AvgPerLap, 2);
            DrawRow(dl, f, "Average", f.AvgPerLap, Argb.Parse(s.AvgColor), x, y, colW, lblH, valH, label, big, true);
            y += block;
            if (s.ShowLast) { DrawRow(dl, f, "Last", f.LastPerLap, Argb.Parse(s.LastColor), x, y, colW, lblH, valH, label, big, false); y += block; }
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
                double end = f.FuelAtEnd(perLap);
                dl.Text(x[3], vy, colW, valH, Fmt.Num(end, "0.00"), big, end < 0 ? Red : color);
            }
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
