using System.Diagnostics;
using vibeRacingOverlays.Data.IRSdk;

namespace vibeRacingOverlays.Data.Telemetry
{
    /// <summary>
    /// Synthetic multi-class race (or practice session) so overlays can be designed without iRacing running.
    /// Produces the same TelemetryState/SessionInfo shapes as the live source.
    /// </summary>
    public sealed class DemoSource : ITelemetrySource
    {
        sealed class Car
        {
            public int Idx;
            public int ClassId;
            public double Pace;          // base lap time
            public double LapTime;       // current lap's time
            public double Progress;      // laps, fractional
            public double LapStart;      // session time the current lap started
            public float Last, Best;
            public int PitLap;
            public double PitUntil = -1;
            public bool Pitted;
            public double TowUntil = -1;
            public bool Garage;          // practice: never leaves the garage (no lap time, like many drivers in iRacing)
        }

        const double StartTime = 600;     // race already 10 minutes in
        const double RaceLength = 2700;   // 45 minute race
        const double TrackKm = 7.0;
        const float FuelPerLap = 3.1f;
        const float MaxFuel = 100f;

        static readonly string[] First = { "Jasper", "Juha-Pekka", "Joshua", "William", "Tyson", "Matt", "Denis", "Casey", "Ahmed", "Pablo", "Lars", "Sven", "Marco", "Kevin", "Tom", "Bas", "Nick", "Luca", "Felix", "Jonas", "Rik", "Pieter", "Anna", "Sophie", "Emma", "Noah" };
        static readonly string[] Last = { "de Vries", "Jansen", "Bakker", "Visser", "Smit", "Meijer", "Mulder", "Bos", "Vos", "Peters", "Hendriks", "Dekker", "Brouwer", "Koster", "Groen", "Schouten", "Huisman", "Kramer", "Verhoeven", "Post", "Kuipers", "Veenstra", "Prins", "Blom" };
        static readonly string[] Gt3Cars = { "Porsche 911 GT3 R (992)", "BMW M4 GT3", "Ferrari 296 GT3", "Mercedes-AMG GT3 2020", "Audi R8 LMS EVO II GT3", "Lamborghini Huracan GT3 EVO", "McLaren 720S GT3 EVO", "Ford Mustang GT3", "Aston Martin Vantage GT3 EVO", "Chevrolet Corvette Z06 GT3.R", "Acura NSX GT3 EVO 22" };
        static readonly string[] Gt4Cars = { "Porsche 718 Cayman GT4 Clubsport MR", "BMW M4 G82 GT4", "Mercedes-AMG GT4", "Aston Martin Vantage GT4", "McLaren 570S GT4", "Toyota GR Supra GT4" };
        static readonly string[] Lics = { "R", "D", "C", "B", "A", "P" };
        static readonly uint[] LicColors = { 0xd62d20, 0xf07d14, 0xf2c318, 0x3db04b, 0x1e6fe0, 0x000000 };

        readonly List<Car> cars = new List<Car>();
        readonly Random rnd = new Random(7);
        readonly Stopwatch clock = Stopwatch.StartNew();
        SessionInfo session;
        bool sessionSent;
        double simTime = 0;
        double lastWall;
        float fuel = 70f;
        int playerIdx;
        Car pitShadow;   // always in the pit lane right behind the player, so a car in the pits is always visible
        Car lapping, lapped;   // race: always a car a lap up just ahead and a lap down just behind the player (relative colours)
        TelemetryState lastState;

        readonly bool multiClass;
        readonly bool practice;

        /// <param name="multiClass">true: GT3 + GT4 field, false: GT3 only.</param>
        /// <param name="practice">true: a practice session (ranked by fastest lap via the session results, some cars without a time).</param>
        public DemoSource(bool multiClass = true, bool practice = false)
        {
            this.multiClass = multiClass;
            this.practice = practice;
            BuildField();
        }

        public string Name { get { return "Demo"; } }
        public bool Connected { get { return true; } }

        /// <summary>True once the fast-forward to mid-race is done.</summary>
        public bool WarmedUp { get { return simTime >= StartTime; } }

        public void WaitForData(int timeoutMs)
        {
            // while fast-forwarding, deliver frames as fast as the consumer can take them
            if (!WarmedUp) return;
            Thread.Sleep(Math.Min(timeoutMs, 16));
        }

        public bool Poll(out TelemetryState state, out SessionInfo s)
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = now - lastWall;
            lastWall = now;
            if (dt > 0.5) dt = 0.5;
            // fast-forward to mid-race (with the engine watching) so there are laps, gaps, stints and fuel data
            if (!WarmedUp) dt = 0.1;
            if (simTime < RaceLength + 300) Step(dt);

            s = null;
            if (!sessionSent) { s = session; sessionSent = true; }
            state = BuildState();
            lastState = state;
            return true;
        }

        public void ResendSession() { sessionSent = false; }

        public void Dispose() { }

        void BuildField()
        {
            session = new SessionInfo
            {
                TrackName = "spa up", TrackDisplayName = "Circuit de Spa-Francorchamps (Demo)", TrackConfig = "Grand Prix",
                TrackLengthKm = TrackKm, NumCarClasses = multiClass ? 2 : 1, FuelMaxLtr = MaxFuel, MaxFuelPct = 1, DriverCarEstLapTime = 137.5,
                TrackSurfaceTemp = "32.40 C",
                // like most official races: penalty at 17x and every 8x after that, DQ at 25x (practice: unlimited)
                IncidentLimit = practice ? -1 : 25, IncidentWarningInitial = practice ? -1 : 17, IncidentWarningSubsequent = practice ? -1 : 8,
            };
            session.Sessions.Add(practice
                ? new SessionEntry { Num = 0, Type = "Practice", Name = "PRACTICE", LapsLimit = -1, TimeLimit = 3600 }
                : new SessionEntry { Num = 0, Type = "Race", Name = "RACE", LapsLimit = -1, TimeLimit = RaceLength });

            int idx = 0;
            var usedNames = new HashSet<string>();
            int fieldSize = multiClass ? 28 : 24;
            for (int c = 0; c < fieldSize; c++)
            {
                bool gt3 = !multiClass || c < 20;
                string name;
                do { name = First[rnd.Next(First.Length)] + " " + Last[rnd.Next(Last.Length)]; } while (!usedNames.Add(name));
                string carName = gt3 ? Gt3Cars[rnd.Next(Gt3Cars.Length)] : Gt4Cars[rnd.Next(Gt4Cars.Length)];
                int lic = rnd.Next(1, 6);
                int ir = gt3 ? rnd.Next(1400, 6500) : rnd.Next(900, 3500);
                var d = new DriverEntry
                {
                    CarIdx = idx, UserId = 100000 + idx, UserName = name, AbbrevName = name, Initials = name.Substring(0, 1),
                    CarNumber = (rnd.Next(2, 999)).ToString(), CarScreenName = carName, CarScreenNameShort = carName,
                    CarNumberRaw = 0,
                    CarPath = carName.ToLowerInvariant().Replace(" ", ""),
                    CarClassId = gt3 ? 2708 : 4088, CarClassShortName = gt3 ? "GT3" : "GT4",
                    CarClassColor = gt3 ? 0xffda59u : 0x33ceffu, CarClassEstLapTime = gt3 ? 137.5 : 150.0,
                    IRating = ir, LicString = Lics[lic] + " " + (1 + rnd.NextDouble() * 3.99).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    LicColor = LicColors[lic],
                };
                d.CarNumberRaw = int.Parse(d.CarNumber);
                session.Drivers[idx] = d;

                var car = new Car
                {
                    Idx = idx, ClassId = d.CarClassId,
                    Pace = (gt3 ? 137.5 : 150.0) + (6500 - ir) / 1500.0 + rnd.NextDouble() * 0.8,
                    Progress = -(c * 0.004), PitLap = 7 + rnd.Next(0, 6),
                };
                if (practice)
                {
                    // spread around the lap, a few never go out; one stop late in the session
                    car.Progress = -rnd.NextDouble();
                    car.Garage = c % 7 == 3 && c != 12;
                    car.PitLap = 3 + rnd.Next(0, 3);
                }
                car.LapTime = car.Pace;
                cars.Add(car);
                idx++;
            }
            playerIdx = 12;
            session.DriverCarIdx = playerIdx;
            session.Drivers[playerIdx].UserName = "You (Demo)";
            session.Drivers[playerIdx].AbbrevName = "You (Demo)";
            pitShadow = cars[13];
            pitShadow.Garage = false;
            if (!practice)
            {
                lapping = cars[2];
                lapped = cars[cars.Count - 2];
            }

            var grid = cars.OrderBy(c => -c.Progress).ToList();
            for (int i = 0; i < grid.Count; i++)
            {
                int classPos = grid.Take(i + 1).Count(g => g.ClassId == grid[i].ClassId);
                session.QualifyResults.Add(new ResultPosition { Position = i + 1, ClassPosition = classPos, CarIdx = grid[i].Idx });
            }

            // one car gets towed back to the pits halfway through, to show the TOW state
            if (!practice) cars[5].TowUntil = -2;
        }

        void Step(double dt)
        {
            simTime += dt;
            foreach (var c in cars)
            {
                if (c.Garage) continue;
                if (c.TowUntil > 0)
                {
                    if (simTime < c.TowUntil) continue;
                    c.TowUntil = -1;
                    c.PitUntil = simTime + 25; // repairs
                }
                if (c.TowUntil == -2 && simTime > 1500 && c.Progress % 1 > 0.4 && c.Progress % 1 < 0.5)
                {
                    c.TowUntil = simTime + 40;
                    c.Progress = Math.Floor(c.Progress) + 0.02; // "teleported" to pit stall
                    continue;
                }

                double speed = 1.0 / c.LapTime;
                bool inPit = c.PitUntil > simTime;
                if (inPit) speed = 0; // standing in the pit box

                double before = c.Progress;
                c.Progress += speed * dt;
                double frac = c.Progress - Math.Floor(c.Progress);

                // pit entry near the end of the pit lap
                int lap = (int)Math.Floor(c.Progress);
                if (!c.Pitted && lap == c.PitLap && frac > 0.98 && c.PitUntil < 0)
                {
                    c.Pitted = true;
                    c.PitUntil = simTime + 28 + rnd.NextDouble() * 8;
                    if (c.Idx == playerIdx) fuel = Math.Min(MaxFuel, fuel + 45f);
                }

                if (Math.Floor(before) != Math.Floor(c.Progress) && before >= 0)
                {
                    float t = (float)(simTime - c.LapStart);
                    if (c.LapStart > 0 && t > 60)
                    {
                        c.Last = t;
                        if (c.Best == 0 || t < c.Best) c.Best = t;
                        if (practice) UpdateResults();
                    }
                    c.LapStart = simTime;
                    c.LapTime = c.Pace + (rnd.NextDouble() - 0.5) * 1.2;
                }
                if (before < 0 && c.Progress >= 0) c.LapStart = simTime;

                if (c.Idx == playerIdx && !inPit) fuel = Math.Max(0, fuel - (float)(speed * dt * (FuelPerLap + (rnd.NextDouble() - 0.5) * 0.1)));
            }

            // the pit shadow drives down the pit lane alongside the player: the relative and standings always show a car
            // in the pits (to see the "Cars in the pits" styles and the PIT badge without waiting for a stop)
            var me = cars[playerIdx];
            if (pitShadow != null && me.Progress > 0) { pitShadow.Progress = me.Progress - 0.004; pitShadow.PitUntil = -1; }
            // and the relative always shows its "lapping you" and "lapped by you" colours (race only: practice has no laps down)
            if (lapping != null && me.Progress > 0) { lapping.Progress = me.Progress + 1.003; lapping.PitUntil = -1; lapping.TowUntil = -1; }
            if (lapped != null && me.Progress > 1.1) { lapped.Progress = me.Progress - 1.002; lapped.PitUntil = -1; lapped.TowUntil = -1; }
        }

        /// <summary>Practice: the session results iRacing keeps (ranked by fastest lap, only cars with a time).</summary>
        void UpdateResults()
        {
            var ranked = cars.Where(c => c.Best > 0).OrderBy(c => c.Best).ToList();
            var results = new List<ResultPosition>();
            var classCount = new Dictionary<int, int>();
            for (int i = 0; i < ranked.Count; i++)
            {
                var c = ranked[i];
                int cp; classCount.TryGetValue(c.ClassId, out cp); classCount[c.ClassId] = ++cp;
                results.Add(new ResultPosition
                {
                    Position = i + 1, ClassPosition = cp, CarIdx = c.Idx, FastestTime = c.Best, LastTime = c.Last,
                    LapsComplete = (int)Math.Floor(Math.Max(0, c.Progress)),
                });
            }
            session.Sessions[0].Results = results;
        }

        TelemetryState BuildState()
        {
            var s = new TelemetryState
            {
                SessionTime = simTime, SessionNum = 0,
                SessionState = simTime < RaceLength ? SessionState.Racing : SessionState.Checkered,
                SessionFlags = SessionFlags.Green,
                SessionTimeRemain = Math.Max(0, RaceLength - simTime), SessionLapsRemain = 32767,
                SessionTimeOfDay = 13 * 3600 + (float)simTime,
                PlayerCarIdx = playerIdx, IsOnTrack = true, FuelLevel = fuel, TrackTemp = 32.4f, AirTemp = 21.3f, Humidity = 0.55f,
                Incidents = (int)(simTime / 300),   // an incident now and then, for the header
            };

            var order = cars.OrderByDescending(c => c.Progress).ToList();
            var leader = order[0];
            var classCount = new Dictionary<int, int>();
            for (int i = 0; i < order.Count; i++)
            {
                var c = order[i];
                int ci = c.Idx;
                int cp;
                classCount.TryGetValue(c.ClassId, out cp);
                classCount[c.ClassId] = ++cp;

                bool inPit = c.PitUntil > simTime || c.TowUntil > simTime;
                double frac = c.Progress - Math.Floor(c.Progress);
                bool pitLane = inPit || (c.Pitted && c.PitUntil > 0 && simTime - c.PitUntil < 6) || (c == pitShadow && cars[playerIdx].Progress > 0);

                s.CarIdxLap[ci] = (int)Math.Floor(c.Progress) + 1;
                s.CarIdxLapCompleted[ci] = (int)Math.Floor(c.Progress);
                s.CarIdxLapDistPct[ci] = (float)(c.Progress < 0 ? 1 + c.Progress : frac);
                s.CarIdxPosition[ci] = i + 1;
                s.CarIdxClassPosition[ci] = cp;
                s.CarIdxOnPitRoad[ci] = pitLane;
                // a few drivers with a flag, so the pit column's symbols show up in the demo
                s.CarIdxSessionFlags[ci] = ci == 5 ? (int)SessionFlags.Furled : ci == 9 ? (int)SessionFlags.Black : ci == 14 ? (int)SessionFlags.Repair : 0;
                s.CarIdxTrackSurface[ci] = c.TowUntil > simTime ? (int)TrackSurface.NotInWorld
                    : inPit ? (int)TrackSurface.InPitStall : (pitLane ? (int)TrackSurface.ApproachingPits : (int)TrackSurface.OnTrack);
                s.CarIdxLastLapTime[ci] = c.Last;
                s.CarIdxBestLapTime[ci] = c.Best;
                s.CarIdxF2Time[ci] = (float)((leader.Progress - c.Progress) * leader.Pace);
                s.CarIdxEstTime[ci] = (float)(s.CarIdxLapDistPct[ci] * session.Drivers[ci].CarClassEstLapTime);
                if (c.Garage)
                {
                    // like iRacing: not in the world, no telemetry lap data
                    s.CarIdxTrackSurface[ci] = (int)TrackSurface.NotInWorld; s.CarIdxLapDistPct[ci] = -1;
                    s.CarIdxLap[ci] = -1; s.CarIdxLapCompleted[ci] = -1; s.CarIdxPosition[ci] = 0; s.CarIdxClassPosition[ci] = 0;
                    s.CarIdxLastLapTime[ci] = -1; s.CarIdxBestLapTime[ci] = -1;
                }
                if (ci == playerIdx)
                {
                    s.Lap = s.CarIdxLap[ci];
                    s.LapCompleted = s.CarIdxLapCompleted[ci];
                    s.LapDistPct = s.CarIdxLapDistPct[ci];
                    s.OnPitRoad = pitLane;
                    s.LapLastLapTime = c.Last;
                    s.LapBestLapTime = c.Best;
                    s.Speed = inPit ? 0 : 62f;
                }
            }
            // unused slots
            for (int i = cars.Count; i < TelemetryState.MaxCars; i++) { s.CarIdxTrackSurface[i] = -1; s.CarIdxLapDistPct[i] = -1; }
            return s;
        }
    }
}
