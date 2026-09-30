using vibeRacingOverlays.Data.Telemetry;

namespace vibeRacingOverlays.Data.Engine
{
    /// <summary>Measures the player's fuel use per lap, skipping laps with pit visits, refuels or cautions.</summary>
    public sealed class FuelTracker
    {
        const int MaxHistory = 10;

        readonly List<double> laps = new List<double>();
        int lastCompleted = int.MinValue;
        double fuelAtLapStart = -1;
        double prevFuel = -1;
        bool lapInvalid = true;

        public double Average { get { return laps.Count > 0 ? laps.Average() : 0; } }
        public double Last { get; private set; }
        public int Count { get { return laps.Count; } }

        public void Reset()
        {
            laps.Clear();
            lastCompleted = int.MinValue; fuelAtLapStart = -1; prevFuel = -1; lapInvalid = true; Last = 0;
        }

        public void Update(TelemetryState s)
        {
            double fuel = s.FuelLevel;
            if (prevFuel >= 0 && fuel > prevFuel + 0.05) lapInvalid = true;           // refuel
            if (s.OnPitRoad || !s.IsOnTrack) lapInvalid = true;
            if ((s.SessionFlags & (SessionFlags.Caution | SessionFlags.CautionWaving)) != 0) lapInvalid = true;
            prevFuel = fuel;

            if (s.LapCompleted == lastCompleted) return;

            // lastCompleted >= 1: the start lap (from the grid / pit exit at session start) isn't a representative lap
            if (lastCompleted >= 1 && s.LapCompleted == lastCompleted + 1 && !lapInvalid && fuelAtLapStart > 0)
            {
                double used = fuelAtLapStart - fuel;
                if (used > 0.05 && !IsOutlier(used))
                {
                    laps.Add(used);
                    if (laps.Count > MaxHistory) laps.RemoveAt(0);
                    Last = used;
                }
            }
            lastCompleted = s.LapCompleted;
            fuelAtLapStart = fuel;
            lapInvalid = s.OnPitRoad || !s.IsOnTrack;
        }

        bool IsOutlier(double used)
        {
            if (laps.Count < 3) return false;
            var sorted = laps.OrderBy(x => x).ToList();
            double median = sorted[sorted.Count / 2];
            return used < median * 0.6 || used > median * 1.5;
        }
    }
}
