using System.Globalization;

namespace vibeRacingOverlays.App.Rendering
{
    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Lap time: 1:27.4 (decimals = 1) or 1:27.412 (decimals = 3).</summary>
        public static string Lap(double seconds, int decimals = 1)
        {
            if (seconds <= 0) return "";
            double r = Math.Round(seconds, decimals);
            int min = (int)(r / 60);
            double sec = r - min * 60;
            string f = decimals <= 0 ? "00" : "00." + new string('0', decimals);
            return min > 0 ? min.ToString(Inv) + ":" + sec.ToString(f, Inv) : sec.ToString(decimals <= 0 ? "0" : "0." + new string('0', decimals), Inv);
        }

        /// <summary>Gap in seconds: 3.7 / 53.0 / 1:02.3</summary>
        public static string Gap(double seconds, int decimals = 1)
        {
            if (seconds < 60) return seconds.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), Inv);
            int min = (int)(seconds / 60);
            return min.ToString(Inv) + ":" + (seconds - min * 60).ToString("00.0", Inv);
        }

        /// <summary>Signed relative time: +1.3 / -0.4</summary>
        public static string Signed(double seconds, int decimals = 1)
        {
            string f = "0." + new string('0', Math.Max(1, decimals));
            string abs = Math.Abs(seconds).ToString(f, Inv);
            if (abs.Trim('0', '.').Length == 0) return abs;   // no "-0.0"
            return (seconds >= 0 ? "+" : "-") + abs;
        }

        /// <summary>Duration: 11:06:40 / 6:40</summary>
        public static string Clock(double seconds)
        {
            if (seconds < 0) return "";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return ((int)t.TotalHours).ToString(Inv) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            return t.Minutes.ToString(Inv) + ":" + t.Seconds.ToString("00");
        }

        /// <summary>Compact duration: 22h13m / 45m</summary>
        public static string Short(double seconds)
        {
            if (seconds < 0) return "";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return ((int)t.TotalHours).ToString(Inv) + "h" + t.Minutes.ToString("00") + "m";
            return ((int)t.TotalMinutes).ToString(Inv) + "m";
        }

        public static string Num(double v, string format) { return v.ToString(format, Inv); }

        public static string IRating(int ir)
        {
            if (ir <= 0) return "";
            return ir >= 1000 ? (ir / 1000.0).ToString("0.0", Inv) + "k" : ir.ToString(Inv);
        }

        public static string License(char letter, double sr)
        {
            if (letter == ' ') return "";
            return letter + (Math.Floor(sr * 10) / 10).ToString("0.0", Inv);
        }

        /// <summary>iRacing license colors (the SDK reports Pro as black; we use purple like most overlays).</summary>
        public static uint LicenseColor(char letter)
        {
            switch (letter)
            {
                case 'R': return 0xFFD62D20;
                case 'D': return 0xFFF07D14;
                case 'C': return 0xFFF2C318;
                case 'B': return 0xFF3DB04B;
                case 'A': return 0xFF1E6FE0;
                case 'P': return 0xFF8A2BE2;
                default: return 0xFF555555;
            }
        }
    }
}
