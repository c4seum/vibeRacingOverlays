using vibeRacingOverlays.Data.Telemetry;

namespace vibeRacingOverlays.Data.Engine
{
    /// <summary>Measures the player's fuel use per lap, skipping laps with pit visits, refuels or cautions.</summary>
    public sealed class FuelTracker
    {
        const int MaxHistory = 50;     // enough for the "last N laps" average of the fuel widget
        const int AverageLaps = 10;    // the plain average uses the last 10 valid laps

        readonly List<double> laps = new List<double>();
        int lastCompleted = int.MinValue;
        double fuelAtLapStart = -1;
        double prevFuel = -1;
        bool lapInvalid = true;

        public double Average { get { return laps.Count > 0 ? laps.Skip(Math.Max(0, laps.Count - AverageLaps)).Average() : 0; } }
        public double Last { get; private set; }
        public int Count { get { return laps.Count; } }
        /// <summary>Fuel used per valid lap, oldest first. A new array whenever a lap is added (snapshots can share it).</summary>
        public double[] Laps { get; private set; } = new double[0];

        // valid laps since the last pit visit / refuel / leaving the car (cautions skip a lap but keep the stint)
        double stintSum;
        int stintCount;
        public double StintAverage { get { return stintCount > 0 ? stintSum / stintCount : 0; } }
        public int StintCount { get { return stintCount; } }

        public void Reset()
        {
            laps.Clear();
            Laps = new double[0];
            stintSum = 0; stintCount = 0;
            lastCompleted = int.MinValue; fuelAtLapStart = -1; prevFuel = -1; lapInvalid = true; Last = 0;
        }

        public void Update(TelemetryState s)
        {
            double fuel = s.FuelLevel;
            bool refuel = prevFuel >= 0 && fuel > prevFuel + 0.05;
            if (refuel || s.OnPitRoad || !s.IsOnTrack)
            {
                lapInvalid = true;
                stintSum = 0; stintCount = 0;   // a new stint starts
            }
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
                    Laps = laps.ToArray();
                    Last = used;
                    stintSum += used; stintCount++;
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
