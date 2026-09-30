using vibeRacingOverlays.Data.IRSdk;

namespace vibeRacingOverlays.Data.Engine
{
    /// <summary>
    /// Decides the display name of a car class. iRacing's session info often leaves CarClassShortName
    /// empty (e.g. in replays), and the real class names are only available through the authenticated
    /// iRacing Data API, so we fall back to recognizing the class from the cars in it.
    /// Priority: CarClassShortName > recognized category > single car name.
    /// </summary>
    public static class ClassNaming
    {
        // checked in order; first match wins (e.g. "Porsche 911 GT3 Cup" must become Cup, not GT3)
        static readonly (string[] Tokens, string Name)[] Rules =
        {
            (new[] { "lmdh", "gtp", "499p", "963", "arx-06", "arx06", "v-series.r", "vseriesr" }, "GTP"),
            (new[] { "lmp2", "p217" }, "LMP2"),
            (new[] { "lmp3", "p320" }, "LMP3"),
            (new[] { "lmp1", "919", "ts050", "r18" }, "LMP1"),
            (new[] { "gt3 cup", "gt3cup", " cup", "cup " }, "Cup"),
            (new[] { "gte", "gtlm", "rsr" }, "GTE"),
            (new[] { "gt3" }, "GT3"),
            (new[] { "gt4" }, "GT4"),
            (new[] { "tcr" }, "TCR"),
            (new[] { "mx-5", "mx5" }, "MX-5"),
            (new[] { "super formula", "superformula" }, "Super Formula"),
            (new[] { "formula 4", "fia f4", "f4 " }, "F4"),
            (new[] { "nascar", "cup series" }, "NASCAR"),
        };

        public static string Resolve(int classId, IEnumerable<DriverEntry> driversInClass)
        {
            var drivers = driversInClass.ToList();
            string official = drivers.Select(d => d.CarClassShortName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (!string.IsNullOrWhiteSpace(official)) return official.Trim();

            // recognize the category from the cars; all distinct cars must agree
            var cars = drivers.GroupBy(d => d.CarPath).Select(g => g.First()).ToList();
            var names = cars.Select(Recognize).Distinct().ToList();
            if (names.Count == 1 && names[0] != null) return names[0];

            // single-make class: the car is the class
            if (cars.Count == 1) return Short(cars[0]);

            // mixed and unrecognized: most common recognized name, else the first car
            var best = names.Where(n => n != null).GroupBy(n => n).OrderByDescending(g => g.Count()).FirstOrDefault();
            return best != null ? best.Key : (cars.Count > 0 ? Short(cars[0]) : "Class " + classId);
        }

        static string Recognize(DriverEntry d)
        {
            string s = " " + (d.CarScreenName + " " + d.CarPath).ToLowerInvariant() + " ";
            foreach (var r in Rules)
                foreach (var t in r.Tokens)
                    if (s.Contains(t)) return r.Name;
            return null;
        }

        static string Short(DriverEntry d)
        {
            return !string.IsNullOrWhiteSpace(d.CarScreenNameShort) ? d.CarScreenNameShort : d.CarScreenName;
        }
    }
}
