using vibeRacingOverlays.Data.Telemetry;

namespace vibeRacingOverlays.Data.Engine
{
    /// <summary>
    /// Measures the player's fuel use per lap, skipping laps with pit visits, refuels or cautions.
    /// Lives as long as the car and track stay the same, also when iRacing closes and opens the sim between the
    /// sessions of an event (practice, qualifying and race are often separate subsessions), see TelemetryService.
    /// </summary>
    public sealed class FuelTracker
    {
        const int MaxHistory = 50;     // enough for the "last N laps" average of the fuel widget
        const int AverageLaps = 10;    // the plain average uses the last 10 valid laps

        readonly List<double> laps = new List<double>();
        int lastCompleted = int.MinValue;
        double fuelAtLapStart = -1;
        double prevFuel = -1;
        bool lapInvalid = true;
        string carTrack;

        // race start (user's wish, 2026-10-27): the average starts again when the player crosses the line at the
        // green, so the pace lap isn't in it and lap 1 is the first lap that counts
        bool race;
        SessionState prevState;
        bool armStart;        // the race went green: the next line crossing starts lap 1
        bool countFirstLap;   // the lap being measured is lap 1 of the race (it counts, unlike other first laps)
        double lineTime = -1, lineFuel = -1;   // the player's last line crossing (session time, fuel)

        public double Average { get { return laps.Count > 0 ? laps.Skip(Math.Max(0, laps.Count - AverageLaps)).Average() : 0; } }
        public double Last { get; private set; }
        public int Count { get { return laps.Count; } }
        /// <summary>Fuel used per valid lap, oldest first. A new array whenever a lap is added (snapshots can share it).</summary>
        public double[] Laps { get; private set; } = new double[0];

        /// <summary>
        /// Average of practice / qualifying (the last 10 valid laps with this car on this track) when the race session
        /// started, and how many laps that is: an estimate on the grid, until the race has a lap of its own.
        /// </summary>
        public double PreviousAverage { get; private set; }
        public int PreviousCount { get; private set; }

        // valid laps since the last pit visit / refuel / leaving the car (cautions skip a lap but keep the stint)
        double stintSum;
        int stintCount;
        public double StintAverage { get { return stintCount > 0 ? stintSum / stintCount : 0; } }
        public int StintCount { get { return stintCount; } }

        /// <summary>Another car or track: nothing measured so far applies.</summary>
        public void SetCarTrack(string key)
        {
            if (key == carTrack) return;
            carTrack = key;
            Clear();
            PreviousAverage = 0; PreviousCount = 0;
            lastCompleted = int.MinValue; fuelAtLapStart = -1; prevFuel = -1; lapInvalid = true;
            armStart = false; countFirstLap = false; lineTime = -1;
        }

        /// <summary>
        /// The session kind. Going into a race keeps the practice / qualifying laps as <see cref="PreviousAverage"/>
        /// and starts the race's own measurement empty.
        /// </summary>
        public void SetRace(bool isRace)
        {
            if (isRace == race) return;
            race = isRace;
            if (!isRace) return;
            if (laps.Count > 0)
            {
                PreviousAverage = Average;
                PreviousCount = Math.Min(laps.Count, AverageLaps);
            }
            Clear();
        }

        /// <summary>
        /// Starts the averages again (a hotkey, after a caution or an incident). Only what was counted goes: the lap
        /// being driven keeps its start, so it counts in full at the line instead of from the moment of the reset.
        /// </summary>
        public void Reset()
        {
            Clear();
            PreviousAverage = 0; PreviousCount = 0;
        }

        void Clear()
        {
            laps.Clear();
            Laps = new double[0];
            stintSum = 0; stintCount = 0;
            Last = 0;
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
            // before the green (grid, pace lap) nothing counts
            if (race && s.SessionState != SessionState.Racing) lapInvalid = true;
            prevFuel = fuel;

            if (race && s.SessionState == SessionState.Racing && prevState != SessionState.Racing && prevState != SessionState.Invalid)
            {
                // the green: the counter goes to 0. The player crosses the line right after it (or crossed it a moment ago)
                Clear();
                if (lineTime >= 0 && s.SessionTime - lineTime < 3) StartFirstLap(lineFuel);
                else armStart = true;
            }
            prevState = s.SessionState;

            if (s.LapCompleted == lastCompleted) return;
            lineTime = s.SessionTime; lineFuel = fuel;

            if (armStart)
            {
                armStart = false;
                lastCompleted = s.LapCompleted;
                StartFirstLap(fuel);
                return;
            }

            // lastCompleted >= 1: the start lap (from the grid / pit exit at session start) isn't a representative lap,
            // except lap 1 of the race, measured from the line at the green
            if ((lastCompleted >= 1 || countFirstLap) && s.LapCompleted == lastCompleted + 1 && !lapInvalid && fuelAtLapStart > 0)
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
            countFirstLap = false;
            lastCompleted = s.LapCompleted;
            fuelAtLapStart = fuel;
            lapInvalid = s.OnPitRoad || !s.IsOnTrack;
        }

        void StartFirstLap(double fuel)
        {
            fuelAtLapStart = fuel;
            countFirstLap = true;
            lapInvalid = false;
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
