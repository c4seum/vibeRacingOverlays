using vibeRacingOverlays.Data.IRSdk;
using vibeRacingOverlays.Data.Model;
using vibeRacingOverlays.Data.Telemetry;

namespace vibeRacingOverlays.Data.Engine
{
    /// <summary>
    /// Turns raw 60 Hz telemetry into RaceSnapshots. Keeps per-car state that iRacing doesn't provide
    /// (pit stops, stints, tows, checkpoint timing for live gaps).
    /// </summary>
    public sealed class RaceEngine
    {
        const int Checkpoints = 100;     // timing points per lap for live gaps

        sealed class Tracker
        {
            public double LastProgress = double.NaN;
            public bool PrevOnPit;
            public int PrevSurface = -1;
            public double LastOnTrackTime = -1000;
            public int PitCount;
            public int LapAtPitExit = -1;
            public double PitEnterTime = -1;
            public double PitLaneTime;
            public bool Towing;
            public int LastKey = int.MinValue;
            public readonly Dictionary<int, double> Times = new Dictionary<int, double>();
        }

        public bool LivePositions = true;

        readonly Tracker[] trackers = new Tracker[TelemetryState.MaxCars];
        readonly FuelTracker fuel = new FuelTracker();
        SessionInfo session;
        int sessionNum = -1;
        string carTrackKey;
        long version;

        public SessionInfo Session { get { return session; } }

        public RaceEngine() { ResetTrackers(); }

        void ResetTrackers()
        {
            for (int i = 0; i < trackers.Length; i++) trackers[i] = new Tracker();
        }

        public void SetSession(SessionInfo s)
        {
            // a different event (new subsession or track) must not inherit pit stops, stints or timing
            if (session != null && (s.SubSessionId != session.SubSessionId || s.TrackName != session.TrackName))
            {
                ResetTrackers();
                sessionNum = -1;
            }
            session = s;
            var p = s.Player;
            string key = (p != null ? p.CarPath : "") + "@" + s.TrackName;
            if (key != carTrackKey) { carTrackKey = key; fuel.Reset(); }
        }

        /// <summary>Call for every telemetry frame (cheap: only tracking).</summary>
        public void Track(TelemetryState s)
        {
            if (session == null) return;
            if (s.SessionNum != sessionNum) { sessionNum = s.SessionNum; ResetTrackers(); }

            fuel.Update(s);
            double t = s.SessionTime;
            for (int i = 0; i < TelemetryState.MaxCars; i++)
            {
                if (!session.Drivers.ContainsKey(i)) continue;
                var tr = trackers[i];
                int surface = s.CarIdxTrackSurface[i];
                bool onPit = s.CarIdxOnPitRoad[i];
                float pct = s.CarIdxLapDistPct[i];

                if (surface == (int)TrackSurface.OnTrack || surface == (int)TrackSurface.OffTrack) tr.LastOnTrackTime = t;

                // tow / reset: back in the pit stall without driving down the pit lane
                if (surface == (int)TrackSurface.InPitStall && tr.PrevSurface != (int)TrackSurface.InPitStall
                    && tr.PrevSurface != (int)TrackSurface.ApproachingPits && !tr.PrevOnPit && t - tr.LastOnTrackTime < 120)
                    tr.Towing = true;
                if (!onPit && surface != (int)TrackSurface.InPitStall && surface != (int)TrackSurface.NotInWorld) tr.Towing = false;

                if (onPit && !tr.PrevOnPit) tr.PitEnterTime = t;
                if (onPit && tr.PitEnterTime >= 0) tr.PitLaneTime = t - tr.PitEnterTime;
                if (!onPit && tr.PrevOnPit)
                {
                    tr.PitCount++;
                    tr.LapAtPitExit = s.CarIdxLapCompleted[i];
                }
                tr.PrevOnPit = onPit;
                tr.PrevSurface = surface;

                if (pct < 0 || surface == (int)TrackSurface.NotInWorld) continue;

                double progress = CarProgress(s, i, tr);
                int key = (int)Math.Floor(progress * Checkpoints);
                if (tr.LastKey == int.MinValue || key < tr.LastKey || key - tr.LastKey > 10)
                {
                    // first sample, reset or teleport: restart timing for this car. Only real
                    // checkpoint crossings are recorded, otherwise every car would share the same start time.
                    tr.Times.Clear();
                }
                else
                {
                    for (int k = tr.LastKey + 1; k <= key; k++) tr.Times[k] = t;
                }
                tr.LastKey = key;
                if (tr.Times.Count > Checkpoints * 3)
                {
                    int min = key - Checkpoints * 2;
                    foreach (var k in tr.Times.Keys.Where(k => k < min).ToList()) tr.Times.Remove(k);
                }
            }
        }

        static double CarProgress(TelemetryState s, int i, Tracker tr)
        {
            double p = s.CarIdxLap[i] - 1 + s.CarIdxLapDistPct[i];
            // lap counter and distance don't always flip in the same frame at the line
            if (!double.IsNaN(tr.LastProgress))
            {
                double d = p - tr.LastProgress;
                if (d < -0.5 && d > -1.5 && s.CarIdxLapDistPct[i] < 0.1) p += 1;
                else if (d > 0.5 && d < 1.5 && s.CarIdxLapDistPct[i] > 0.9) p -= 1;
            }
            tr.LastProgress = p;
            return p;
        }

        /// <summary>Builds an immutable snapshot for the overlays.</summary>
        public RaceSnapshot Build(TelemetryState s, string sourceName)
        {
            var snap = new RaceSnapshot { Version = ++version, Source = sourceName, Connected = true };
            if (session == null || s == null) return snap;

            var sess = session.Session(s.SessionNum);
            snap.SessionTime = s.SessionTime;
            snap.SessionType = sess != null ? sess.Type : "";
            snap.SessionName = sess != null ? sess.Name : "";
            snap.IsRace = sess != null && sess.IsRace;
            snap.State = s.SessionState;
            snap.Flags = s.SessionFlags;
            snap.TrackName = session.TrackDisplayName;
            snap.TrackLengthKm = session.TrackLengthKm;
            snap.TrackTemp = s.TrackTemp;
            snap.AirTemp = s.AirTemp;
            snap.TimeOfDay = s.SessionTimeOfDay;
            snap.Incidents = s.Incidents;
            snap.TimeRemain = s.SessionTimeRemain > 0 && s.SessionTimeRemain < 7 * 24 * 3600 ? s.SessionTimeRemain : -1;
            snap.TimeTotal = sess != null ? sess.TimeLimit : -1;
            snap.TotalLaps = sess != null ? sess.LapsLimit : -1;

            int playerIdx = s.PlayerCarIdx >= 0 ? s.PlayerCarIdx : session.DriverCarIdx;

            // ---- cars
            foreach (var d in session.Drivers.Values)
            {
                if (d.IsSpectator || d.IsPaceCar || d.CarIdx == session.PaceCarIdx) continue;
                int i = d.CarIdx;
                if (i < 0 || i >= TelemetryState.MaxCars) continue;
                var tr = trackers[i];
                var c = new CarInfo
                {
                    CarIdx = i, Driver = d, Name = d.UserName, ShortName = ShortName(d.UserName), Number = d.CarNumber,
                    ClassId = d.CarClassId, ClassName = d.CarClassShortName, ClassColor = d.CarClassColor,
                    CarName = d.CarScreenName, Brand = Brands.Short(d.CarScreenName), IsPlayer = i == playerIdx,
                    IRating = d.IRating, LicColor = d.LicColor,
                    Lap = s.CarIdxLap[i], LapCompleted = s.CarIdxLapCompleted[i], LapDistPct = s.CarIdxLapDistPct[i],
                    Surface = (TrackSurface)s.CarIdxTrackSurface[i],
                    OverallPos = s.CarIdxPosition[i], ClassPos = s.CarIdxClassPosition[i],
                    LastLap = s.CarIdxLastLapTime[i] > 0 ? s.CarIdxLastLapTime[i] : 0,
                    BestLap = s.CarIdxBestLapTime[i] > 0 ? s.CarIdxBestLapTime[i] : 0,
                    OnPitRoad = s.CarIdxOnPitRoad[i],
                    TireCompound = s.CarIdxTireCompound[i],
                    PitCount = tr.PitCount, Towing = tr.Towing, PitLaneTime = tr.PitLaneTime,
                };
                c.InWorld = c.Surface != TrackSurface.NotInWorld && c.LapDistPct >= 0;
                c.InPitStall = c.Surface == TrackSurface.InPitStall;
                c.Progress = double.IsNaN(tr.LastProgress) ? c.LapCompleted : tr.LastProgress;
                c.StintLaps = Math.Max(0, tr.LapAtPitExit >= 0 ? c.LapCompleted - tr.LapAtPitExit : c.LapCompleted);
                c.OutLap = tr.LapAtPitExit >= 0 && c.LapCompleted == tr.LapAtPitExit && !c.OnPitRoad;
                ParseLicense(d.LicString, c);
                snap.Cars.Add(c);
            }

            // ---- classes, ordered fastest class first
            foreach (var g in snap.Cars.GroupBy(c => c.ClassId))
            {
                var first = g.First().Driver;
                var cs = new ClassStandings
                {
                    ClassId = g.Key, Color = first.CarClassColor, EstLapTime = first.CarClassEstLapTime,
                    Cars = g.ToList(),
                };
                cs.Name = ClassNaming.Resolve(g.Key, g.Select(c => c.Driver));
                foreach (var c in cs.Cars) c.ClassName = cs.Name;
                cs.Sof = RatingMath.StrengthOfField(cs.Cars.Select(c => c.IRating));
                cs.BestLap = cs.Cars.Where(c => c.BestLap > 0).Select(c => c.BestLap).DefaultIfEmpty(0).Min();
                cs.EstLapTime = ReferenceLapTime(cs);
                OrderClass(cs, s, snap.IsRace);
                snap.Classes.Add(cs);
            }
            snap.Classes.Sort((a, b) => a.EstLapTime.CompareTo(b.EstLapTime));

            // grid positions -> positions gained
            var startPos = new Dictionary<int, int>();
            foreach (var r in session.QualifyResults) startPos[r.CarIdx] = r.ClassPosition;

            foreach (var cs in snap.Classes)
            {
                var leader = cs.Cars.Count > 0 ? cs.Cars[0] : null;
                double[] irDelta = snap.IsRace ? RatingMath.EstimateChanges(cs.Cars.Select(c => c.IRating).ToList()) : null;
                for (int k = 0; k < cs.Cars.Count; k++)
                {
                    var c = cs.Cars[k];
                    c.ClassPos = k + 1;
                    int sp;
                    if (startPos.TryGetValue(c.CarIdx, out sp) && sp > 0)
                    {
                        c.StartClassPos = sp;
                        if (snap.IsRace) c.PositionsGained = sp - c.ClassPos;
                    }
                    if (irDelta != null) c.IRatingDelta = irDelta[k];
                    c.LastIsClassBest = c.LastLap > 0 && cs.BestLap > 0 && Math.Abs(c.LastLap - cs.BestLap) < 0.0005;
                    c.LastIsPersonalBest = !c.LastIsClassBest && c.LastLap > 0 && c.BestLap > 0 && Math.Abs(c.LastLap - c.BestLap) < 0.0005;

                    if (k == 0) continue;
                    var ahead = cs.Cars[k - 1];
                    if (snap.IsRace)
                    {
                        c.LapsDown = Math.Max(0, (int)Math.Floor(leader.Progress - c.Progress));
                        c.GapToClassLeader = Gap(leader, c, s, cs.EstLapTime);
                        c.IntervalLaps = Math.Max(0, (int)Math.Floor(ahead.Progress - c.Progress));
                        c.Interval = Gap(ahead, c, s, cs.EstLapTime);
                    }
                    else
                    {
                        if (c.BestLap > 0 && leader.BestLap > 0) c.GapToClassLeader = c.BestLap - leader.BestLap;
                        if (c.BestLap > 0 && ahead.BestLap > 0) c.Interval = c.BestLap - ahead.BestLap;
                    }
                }
            }

            snap.Player = snap.Cars.FirstOrDefault(c => c.IsPlayer);
            snap.PlayerClass = snap.Player != null ? snap.Classes.FirstOrDefault(c => c.ClassId == snap.Player.ClassId) : snap.Classes.FirstOrDefault();

            // overall order for header info
            var overallLeader = snap.Cars.Where(c => c.InWorld || c.LapCompleted > 0).OrderByDescending(c => c.Progress).FirstOrDefault();
            var classLeader = snap.PlayerClass != null && snap.PlayerClass.Cars.Count > 0 ? snap.PlayerClass.Cars[0] : null;
            snap.LeaderLap = classLeader != null ? Math.Max(0, classLeader.Lap) : 0;
            if (snap.TotalLaps <= 0 && snap.TimeRemain > 0 && classLeader != null)
            {
                double lapTime = LapTimeEstimate(classLeader, snap.PlayerClass);
                if (lapTime > 0) snap.EstTotalLaps = classLeader.Progress + snap.TimeRemain / lapTime;
            }

            BuildRelative(snap, s);
            BuildFuel(snap, s, overallLeader);
            return snap;
        }

        void OrderClass(ClassStandings cs, TelemetryState s, bool isRace)
        {
            bool live = LivePositions && isRace && s.SessionState == SessionState.Racing;
            var grid = new Dictionary<int, int>();
            foreach (var r in session.QualifyResults) grid[r.CarIdx] = r.ClassPosition;

            cs.Cars.Sort((a, b) =>
            {
                if (live)
                {
                    bool ra = a.LapCompleted >= 0 && a.Lap > 0, rb = b.LapCompleted >= 0 && b.Lap > 0;
                    if (ra && rb && Math.Abs(a.Progress - b.Progress) > 1e-6) return b.Progress.CompareTo(a.Progress);
                    if (ra != rb) return ra ? -1 : 1;
                }
                int pa = Key(a, grid, isRace), pb = Key(b, grid, isRace);
                if (pa != pb) return pa.CompareTo(pb);
                if (!isRace && a.BestLap != b.BestLap)
                {
                    if (a.BestLap <= 0) return 1;
                    if (b.BestLap <= 0) return -1;
                    return a.BestLap.CompareTo(b.BestLap);
                }
                return a.CarIdx.CompareTo(b.CarIdx);
            });
        }

        static int Key(CarInfo c, Dictionary<int, int> grid, bool isRace)
        {
            if (c.ClassPos > 0) return c.ClassPos;
            int g;
            if (isRace && grid.TryGetValue(c.CarIdx, out g) && g > 0) return 500 + g;
            return 1000;
        }

        /// <summary>Live gap between two cars using checkpoint crossing times; falls back to lap-fraction estimate.</summary>
        double? Gap(CarInfo ahead, CarInfo behind, TelemetryState s, double estLap)
        {
            var ta = trackers[ahead.CarIdx];
            var tb = trackers[behind.CarIdx];
            if (tb.LastKey != int.MinValue)
            {
                double tAhead, tBehind;
                if (ta.Times.TryGetValue(tb.LastKey, out tAhead) && tb.Times.TryGetValue(tb.LastKey, out tBehind))
                {
                    double g = tBehind - tAhead;
                    if (g >= 0) return g;
                }
                // same position one lap earlier (car is a lap down): use the gap within the lap
                int lapsDown = (int)Math.Floor(ahead.Progress - behind.Progress);
                if (lapsDown >= 1 && ta.Times.TryGetValue(tb.LastKey + lapsDown * Checkpoints, out tAhead) && tb.Times.TryGetValue(tb.LastKey, out tBehind))
                    return Math.Max(0, tBehind - tAhead);
            }
            if (s.CarIdxF2Time[behind.CarIdx] > 0 || s.CarIdxF2Time[ahead.CarIdx] > 0)
            {
                double f = s.CarIdxF2Time[behind.CarIdx] - s.CarIdxF2Time[ahead.CarIdx];
                if (f >= 0) return f;
            }
            if (estLap > 0) return Math.Max(0, (ahead.Progress - behind.Progress) % 1.0) * estLap;
            return null;
        }

        /// <summary>
        /// Typical lap time of a class: median of recent laps, else best lap, else iRacing's estimate.
        /// (iRacing reports CarClassEstLapTime = 1.0 in replays, so it can't be trusted on its own.)
        /// </summary>
        double ReferenceLapTime(ClassStandings cs)
        {
            var laps = cs.Cars.Where(c => c.LastLap > 0).Select(c => (double)c.LastLap).OrderBy(x => x).ToList();
            if (laps.Count > 0) return laps[laps.Count / 2];
            if (cs.BestLap > 0) return cs.BestLap;
            if (cs.EstLapTime > 10) return cs.EstLapTime;
            if (session.DriverCarEstLapTime > 10) return session.DriverCarEstLapTime;
            return session.TrackLengthKm > 0 ? session.TrackLengthKm * 25 : 90; // ~145 km/h average as a last resort
        }

        static double LapTimeEstimate(CarInfo c, ClassStandings cs)
        {
            if (c.LastLap > 0) return c.LastLap;
            if (c.BestLap > 0) return c.BestLap;
            return cs != null ? cs.EstLapTime : 0;
        }

        void BuildRelative(RaceSnapshot snap, TelemetryState s)
        {
            var p = snap.Player;
            if (p == null || !p.InWorld) return;
            var classLaps = snap.Classes.ToDictionary(c => c.ClassId, c => c.EstLapTime);
            double playerEstLap = snap.PlayerClass != null ? snap.PlayerClass.EstLapTime : 90;
            if (playerEstLap <= 0) playerEstLap = 90;
            // CarIdxEstTime isn't filled in every situation (e.g. replays); then use track distance
            bool haveEst = snap.Cars.Any(c => c.InWorld && !c.IsPlayer && s.CarIdxEstTime[c.CarIdx] > 0);
            // EstTime is in each class's own scale (iRacing's CarClassEstLapTime); convert to the player's lap time
            Func<CarInfo, double> scaled = c =>
            {
                double iracingLap = c.Driver.CarClassEstLapTime > 10 ? c.Driver.CarClassEstLapTime : classLaps[c.ClassId];
                return s.CarIdxEstTime[c.CarIdx] * (playerEstLap / iracingLap);
            };
            double pEst = haveEst ? scaled(p) : 0;

            foreach (var c in snap.Cars)
            {
                if (!c.InWorld) continue;
                double relPct = c.LapDistPct - p.LapDistPct;
                if (relPct > 0.5) relPct -= 1; else if (relPct < -0.5) relPct += 1;

                double rel;
                if (haveEst)
                {
                    rel = scaled(c) - pEst;
                    while (rel > playerEstLap / 2) rel -= playerEstLap;
                    while (rel < -playerEstLap / 2) rel += playerEstLap;
                }
                else rel = relPct * playerEstLap;
                c.RelativeTime = rel;
                c.RelativeLap = snap.IsRace ? (int)Math.Round((c.Progress - p.Progress) - relPct) : 0;
                snap.Relative.Add(c);
            }
            snap.Relative.Sort((a, b) => b.RelativeTime.CompareTo(a.RelativeTime));
        }

        void BuildFuel(RaceSnapshot snap, TelemetryState s, CarInfo overallLeader)
        {
            var f = snap.Fuel;
            f.Level = s.FuelLevel;
            f.MaxFuel = (float)session.MaxFuel;
            f.AvgPerLap = fuel.Average;
            f.LastPerLap = fuel.Last;
            f.ValidLaps = fuel.Count;

            var p = snap.Player;
            if (p == null) return;
            double playerProgress = Math.Max(0, p.Progress);
            double playerLap = LapTimeEstimate(p, snap.PlayerClass);
            if (playerLap <= 0) return;

            if (!snap.IsRace || overallLeader == null)
            {
                if (snap.TotalLaps > 0) f.LapsToGo = Math.Max(0, snap.TotalLaps - playerProgress);
                else if (snap.TimeRemain > 0) f.LapsToGo = Math.Ceiling(playerProgress + snap.TimeRemain / playerLap) - playerProgress;
                return;
            }

            // the race ends when the overall leader finishes; the player finishes on the next line crossing
            var leaderClass = snap.Classes.FirstOrDefault(c => c.ClassId == overallLeader.ClassId);
            double leaderLap = LapTimeEstimate(overallLeader, leaderClass);
            double leaderProgress = Math.Max(0, overallLeader.Progress);
            double leaderToGo;
            if (snap.TotalLaps > 0) leaderToGo = Math.Max(0, snap.TotalLaps - leaderProgress);
            else if (snap.TimeRemain > 0 && leaderLap > 0) leaderToGo = Math.Ceiling(leaderProgress + snap.TimeRemain / leaderLap) - leaderProgress;
            else if (s.SessionState >= SessionState.Checkered) leaderToGo = 0;
            else return;

            double timeLeft = leaderToGo * leaderLap;
            f.LapsToGo = Math.Max(0, Math.Ceiling(playerProgress + timeLeft / playerLap - 1e-6) - playerProgress);
            if (s.SessionState >= SessionState.Checkered) f.LapsToGo = Math.Max(0, Math.Ceiling(playerProgress) - playerProgress);
        }

        static void ParseLicense(string lic, CarInfo c)
        {
            if (string.IsNullOrEmpty(lic)) return;
            lic = lic.Trim();
            c.LicLetter = char.ToUpperInvariant(lic[0]);
            double sr;
            if (double.TryParse(lic.Substring(1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sr))
                c.LicSR = sr;
        }

        /// <summary>"Jasper Groenewegen" -> "J. Groenewegen"</summary>
        static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int sp = name.IndexOf(' ');
            if (sp <= 0 || sp == name.Length - 1) return name;
            return name.Substring(0, 1) + ". " + name.Substring(sp + 1);
        }
    }
}
