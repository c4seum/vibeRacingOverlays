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

        /// <summary>Gap in seconds: 3.7 / 53.0 / 1:02.3; from a minute on like a lap time, with the same decimals.</summary>
        public static string Gap(double seconds, int decimals = 1)
        {
            double r = Math.Round(seconds, Math.Max(0, decimals));
            if (r < 60) return r.ToString(decimals <= 0 ? "0" : "0." + new string('0', decimals), Inv);
            return Lap(r, decimals);
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
        /// <summary>
        /// License in one of the standings formats: "L2" A3.48, "L1" A3.4, "L0" A3, "L" A, "SR2" 3.48, "SR1" 3.4, "SR0" 3.
        /// The SR is cut, not rounded (like iRacing shows it).
        /// </summary>
        public static string License(char letter, double sr, string format)
        {
            if (letter == ' ') return "";
            string f = format ?? "L1";
            int dec = f.EndsWith("2") ? 2 : f.EndsWith("1") ? 1 : 0;
            double cut = Math.Floor(sr * Math.Pow(10, dec)) / Math.Pow(10, dec);
            string num = cut.ToString(dec == 0 ? "0" : "0." + new string('0', dec), Inv);
            if (f == "L") return letter.ToString();
            if (f.StartsWith("SR")) return num;
            return letter + num;
        }

        /// <summary>iRating / SOF: "full" 4567, "k1" 4.5k, "k0" 4k (cut, not rounded).</summary>
        public static string Rating(int ir, string format)
        {
            if (ir <= 0) return "";
            switch (format)
            {
                case "full": return ir.ToString(Inv);
                case "k0": return (ir / 1000).ToString(Inv) + "k";
                default: return ir >= 1000 ? (Math.Floor(ir / 100.0) / 10).ToString("0.0", Inv) + "k" : ir.ToString(Inv);
            }
        }

        /// <summary>Temperature from °C: "C1" 32.4°C, "C0" 32°C, "F1" 90.3°F, "F0" 90°F, "CF" 32.4°C 90.3°F, "FC" 90.3°F 32.4°C.</summary>
        public static string Temperature(double celsius, string format)
        {
            double f = celsius * 9 / 5 + 32;
            string c1 = celsius.ToString("0.0", Inv) + "°C", f1 = f.ToString("0.0", Inv) + "°F";
            switch (format)
            {
                case "C0": return celsius.ToString("0", Inv) + "°C";
                case "F1": return f1;
                case "F0": return f.ToString("0", Inv) + "°F";
                case "CF": return c1 + " " + f1;
                case "FC": return f1 + " " + c1;
                default: return c1;
            }
        }

        /// <summary>Relative humidity (0..1) as a percentage: "1" 55.2%, "0" 55%.</summary>
        public static string Humidity(double fraction, string format)
        {
            return (fraction * 100).ToString(format == "0" ? "0" : "0.0", Inv) + "%";
        }

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
