namespace vibeRacingOverlays.Data.Engine
{
    /// <summary>
    /// Community reverse-engineered iRacing iRating formulas (strength of field and estimated change).
    /// </summary>
    public static class RatingMath
    {
        static readonly double BR1 = 1600.0 / Math.Log(2.0);

        public static int StrengthOfField(IEnumerable<int> ratings)
        {
            double sum = 0; int n = 0;
            foreach (int ir in ratings)
            {
                if (ir <= 0) continue;
                sum += Math.Exp(-ir / BR1);
                n++;
            }
            return n == 0 ? 0 : (int)Math.Round(-BR1 * Math.Log(sum / n));
        }

        /// <summary>Estimated iRating change per driver, given ratings ordered by finishing position.</summary>
        public static double[] EstimateChanges(IList<int> ratingsInFinishOrder)
        {
            int n = ratingsInFinishOrder.Count;
            var res = new double[n];
            if (n < 2) return res;
            var ir = new double[n];
            for (int i = 0; i < n; i++) ir[i] = ratingsInFinishOrder[i] > 0 ? ratingsInFinishOrder[i] : 1350;
            for (int i = 0; i < n; i++)
            {
                double expected = -0.5;
                for (int j = 0; j < n; j++) expected += Chance(ir[i], ir[j]);
                int pos = i + 1;
                double fudge = (n / 2.0 - pos) / 100.0;
                res[i] = (n - pos - expected - fudge) * 200.0 / n;
            }
            return res;
        }

        static double Chance(double a, double b)
        {
            double ea = Math.Exp(-a / BR1), eb = Math.Exp(-b / BR1);
            return ((1 - ea) * eb) / ((1 - eb) * ea + (1 - ea) * eb);
        }
    }

    public static class Brands
    {
        static readonly string[][] Map =
        {
            new[] { "porsche", "POR" }, new[] { "bmw", "BMW" }, new[] { "mercedes", "MB" }, new[] { "amg", "MB" },
            new[] { "audi", "AUD" }, new[] { "ferrari", "FER" }, new[] { "lamborghini", "LAM" }, new[] { "mclaren", "McL" },
            new[] { "aston", "AM" }, new[] { "mustang", "FRD" }, new[] { "ford", "FRD" }, new[] { "chevrolet", "CHV" },
            new[] { "corvette", "CHV" }, new[] { "camaro", "CHV" }, new[] { "toyota", "TOY" }, new[] { "mazda", "MAZ" },
            new[] { "mx-5", "MAZ" }, new[] { "honda", "HON" }, new[] { "acura", "ACU" }, new[] { "lexus", "LEX" },
            new[] { "nissan", "NIS" }, new[] { "hyundai", "HYU" }, new[] { "kia", "KIA" }, new[] { "cadillac", "CAD" },
            new[] { "dallara", "DAL" }, new[] { "ligier", "LIG" }, new[] { "radical", "RAD" }, new[] { "renault", "REN" },
            new[] { "volkswagen", "VW" }, new[] { "subaru", "SUB" }, new[] { "mini", "MIN" }, new[] { "buick", "BUI" },
            new[] { "alpine", "ALP" }, new[] { "genesis", "GEN" }, new[] { "pontiac", "PON" }, new[] { "dodge", "DOD" },
            new[] { "lotus", "LOT" }, new[] { "williams", "WIL" }, new[] { "super formula", "SF" }, new[] { "skip barber", "SB" },
        };

        public static string Short(string carName)
        {
            if (string.IsNullOrEmpty(carName)) return "";
            string l = carName.ToLowerInvariant();
            foreach (var m in Map)
                if (l.Contains(m[0])) return m[1];
            return carName.Length <= 3 ? carName.ToUpperInvariant() : carName.Substring(0, 3).ToUpperInvariant();
        }
    }
}
