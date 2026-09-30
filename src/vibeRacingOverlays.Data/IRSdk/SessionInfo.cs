namespace vibeRacingOverlays.Data.IRSdk
{
    public sealed class DriverEntry
    {
        public int CarIdx;
        public int UserId;
        public string UserName = "";
        public string AbbrevName = "";
        public string Initials = "";
        public string TeamName = "";
        public string CarNumber = "";
        public string CarScreenName = "";
        public string CarScreenNameShort = "";
        public string CarPath = "";
        public int CarClassId;
        public string CarClassShortName = "";
        public uint CarClassColor;       // 0xRRGGBB
        public double CarClassEstLapTime;
        public int IRating;
        public string LicString = "";    // "A 4.99"
        public uint LicColor;            // 0xRRGGBB
        public bool IsSpectator;
        public bool IsPaceCar;
        public string ClubName = "";
    }

    public sealed class ResultPosition
    {
        public int Position;       // 1-based
        public int ClassPosition;  // 1-based
        public int CarIdx;
        public int Lap;
        public double FastestTime;
        public double LastTime;
        public int LapsComplete;
        public int Incidents;
        public string ReasonOut = "";
    }

    public sealed class SessionEntry
    {
        public int Num;
        public string Type = "";     // Practice, Qualify, Race, ...
        public string Name = "";
        public int LapsLimit = -1;   // -1 = unlimited
        public double TimeLimit = -1; // seconds, -1 = unlimited
        public List<ResultPosition> Results = new List<ResultPosition>();

        public bool IsRace { get { return Type.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0; } }
    }

    public sealed class SessionInfo
    {
        public string TrackName = "";
        public string TrackDisplayName = "";
        public string TrackConfig = "";
        public double TrackLengthKm;
        public string TrackSurfaceTemp = "";
        public int SeriesId;
        public int SubSessionId;
        public bool TeamRacing;
        public int NumCarClasses = 1;
        public int DriverCarIdx = -1;
        public int PaceCarIdx = -1;
        public double FuelMaxLtr;
        public double MaxFuelPct = 1;
        public double DriverCarEstLapTime;
        public Dictionary<int, DriverEntry> Drivers = new Dictionary<int, DriverEntry>();
        public List<SessionEntry> Sessions = new List<SessionEntry>();
        public List<ResultPosition> QualifyResults = new List<ResultPosition>();

        public DriverEntry Player
        {
            get { DriverEntry d; return Drivers.TryGetValue(DriverCarIdx, out d) ? d : null; }
        }

        public SessionEntry Session(int num)
        {
            foreach (var s in Sessions) if (s.Num == num) return s;
            return null;
        }

        public double MaxFuel { get { return FuelMaxLtr * (MaxFuelPct > 0 ? MaxFuelPct : 1); } }

        public static SessionInfo Parse(string yaml)
        {
            var root = YamlLite.Parse(yaml);
            var s = new SessionInfo();

            s.TrackName = YamlLite.Str(root, "WeekendInfo.TrackName");
            s.TrackDisplayName = YamlLite.Str(root, "WeekendInfo.TrackDisplayName");
            s.TrackConfig = YamlLite.Str(root, "WeekendInfo.TrackConfigName");
            s.TrackLengthKm = YamlLite.Num(root, "WeekendInfo.TrackLength");
            s.TrackSurfaceTemp = YamlLite.Str(root, "WeekendInfo.TrackSurfaceTemp");
            s.SeriesId = YamlLite.Int(root, "WeekendInfo.SeriesID");
            s.SubSessionId = YamlLite.Int(root, "WeekendInfo.SubSessionID");
            s.TeamRacing = YamlLite.Int(root, "WeekendInfo.TeamRacing") != 0;
            s.NumCarClasses = Math.Max(1, YamlLite.Int(root, "WeekendInfo.NumCarClasses", 1));

            s.DriverCarIdx = YamlLite.Int(root, "DriverInfo.DriverCarIdx", -1);
            s.PaceCarIdx = YamlLite.Int(root, "DriverInfo.PaceCarIdx", -1);
            s.FuelMaxLtr = YamlLite.Num(root, "DriverInfo.DriverCarFuelMaxLtr");
            s.MaxFuelPct = YamlLite.Num(root, "DriverInfo.DriverCarMaxFuelPct", 1);
            s.DriverCarEstLapTime = YamlLite.Num(root, "DriverInfo.DriverCarEstLapTime");

            foreach (var o in YamlLite.List(root, "DriverInfo.Drivers"))
            {
                var d = new DriverEntry
                {
                    CarIdx = YamlLite.Int(o, "CarIdx", -1),
                    UserId = YamlLite.Int(o, "UserID"),
                    UserName = YamlLite.Str(o, "UserName"),
                    AbbrevName = YamlLite.Str(o, "AbbrevName"),
                    Initials = YamlLite.Str(o, "Initials"),
                    TeamName = YamlLite.Str(o, "TeamName"),
                    CarNumber = YamlLite.Str(o, "CarNumber"),
                    CarScreenName = YamlLite.Str(o, "CarScreenName"),
                    CarScreenNameShort = YamlLite.Str(o, "CarScreenNameShort"),
                    CarPath = YamlLite.Str(o, "CarPath"),
                    CarClassId = YamlLite.Int(o, "CarClassID"),
                    CarClassShortName = YamlLite.Str(o, "CarClassShortName"),
                    CarClassColor = (uint)YamlLite.Num(o, "CarClassColor"),
                    CarClassEstLapTime = YamlLite.Num(o, "CarClassEstLapTime"),
                    IRating = YamlLite.Int(o, "IRating"),
                    LicString = YamlLite.Str(o, "LicString"),
                    LicColor = (uint)YamlLite.Num(o, "LicColor"),
                    IsSpectator = YamlLite.Int(o, "IsSpectator") != 0,
                    IsPaceCar = YamlLite.Int(o, "CarIsPaceCar") != 0,
                    ClubName = YamlLite.Str(o, "ClubName"),
                };
                if (d.CarIdx >= 0) s.Drivers[d.CarIdx] = d;
            }

            foreach (var o in YamlLite.List(root, "SessionInfo.Sessions"))
            {
                var e = new SessionEntry
                {
                    Num = YamlLite.Int(o, "SessionNum"),
                    Type = YamlLite.Str(o, "SessionType"),
                    Name = YamlLite.Str(o, "SessionName"),
                };
                string laps = YamlLite.Str(o, "SessionLaps");
                e.LapsLimit = laps.StartsWith("unlimited", StringComparison.OrdinalIgnoreCase) ? -1 : (int)YamlLite.ParseNumber(laps, -1);
                string time = YamlLite.Str(o, "SessionTime");
                e.TimeLimit = time.StartsWith("unlimited", StringComparison.OrdinalIgnoreCase) ? -1 : YamlLite.ParseNumber(time, -1);
                e.Results = ParseResults(YamlLite.List(o, "ResultsPositions"));
                s.Sessions.Add(e);
            }

            s.QualifyResults = ParseResults(YamlLite.List(root, "QualifyResultsInfo.Results"));
            return s;
        }

        static List<ResultPosition> ParseResults(List<object> list)
        {
            var res = new List<ResultPosition>();
            foreach (var o in list)
            {
                res.Add(new ResultPosition
                {
                    Position = YamlLite.Int(o, "Position"),
                    ClassPosition = YamlLite.Int(o, "ClassPosition"),
                    CarIdx = YamlLite.Int(o, "CarIdx", -1),
                    Lap = YamlLite.Int(o, "Lap"),
                    FastestTime = YamlLite.Num(o, "FastestTime"),
                    LastTime = YamlLite.Num(o, "LastTime"),
                    LapsComplete = YamlLite.Int(o, "LapsComplete"),
                    Incidents = YamlLite.Int(o, "Incidents"),
                    ReasonOut = YamlLite.Str(o, "ReasonOutStr"),
                });
            }
            // iRacing uses 0-based positions in some lists (qualify results, ClassPosition)
            if (res.Count > 0 && res.Min(r => r.Position) == 0) foreach (var r in res) r.Position++;
            if (res.Count > 0 && res.Min(r => r.ClassPosition) == 0) foreach (var r in res) r.ClassPosition++;
            return res;
        }
    }
}
