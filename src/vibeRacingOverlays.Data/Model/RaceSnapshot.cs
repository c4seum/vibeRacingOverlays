using vibeRacingOverlays.Data.IRSdk;
using vibeRacingOverlays.Data.Telemetry;

namespace vibeRacingOverlays.Data.Model
{
    /// <summary>Everything the overlays know about one car. Built fresh for every snapshot.</summary>
    public sealed class CarInfo
    {
        public int CarIdx;
        public DriverEntry Driver;
        public string Name = "";
        public string ShortName = "";
        public string Number = "";
        public int ClassId;
        public string ClassName = "";
        public uint ClassColor;
        public string CarName = "";
        public string Brand = "";
        public bool IsPlayer;

        public int OverallPos;
        public int ClassPos;
        public int StartClassPos;
        public int? PositionsGained;

        public char LicLetter = ' ';
        public double LicSR;
        public uint LicColor;
        public int IRating;
        public double? IRatingDelta;

        public int Lap;
        public int LapCompleted;
        public float LapDistPct;
        public double Progress;          // laps completed + fraction
        /// <summary>Laps completed in this session (telemetry or iRacing's results, whichever knows more).</summary>
        public int LapsComplete;
        public TrackSurface Surface;
        public bool InWorld;

        public double? GapToClassLeader; // seconds
        public int LapsDown;             // laps behind the class leader
        public double? Interval;         // seconds to the car ahead in class
        public int IntervalLaps;

        public float LastLap;
        public float BestLap;
        /// <summary>Typical race pace: median of the last clean laps (no pit visit, caution or start lap), 0 = none yet.</summary>
        public double PaceLap;
        public bool LastIsClassBest;
        public bool LastIsPersonalBest;

        public bool OnPitRoad;
        public bool InPitStall;
        public bool Towing;
        /// <summary>Flags iRacing shows this driver (black flag, slowdown warning, meatball, DQ).</summary>
        public SessionFlags DriverFlags;
        public bool OutLap;
        public int PitCount;
        public int StintLaps;
        public double PitLaneTime;       // seconds in the pit lane during the current/last stop

        public double RelativeTime;      // seconds, + = ahead of the player on track
        public int RelativeLap;          // +1 = a lap ahead of the player (lapping), -1 = a lap down
        public int TireCompound;
    }

    public sealed class ClassStandings
    {
        public int ClassId;
        public string Name = "";
        public uint Color;
        public double EstLapTime;
        public int Sof;
        public List<CarInfo> Cars = new List<CarInfo>();
        public float BestLap;
    }

    public sealed class FuelInfo
    {
        public float Level;
        public float MaxFuel;
        public double AvgPerLap;
        public double LastPerLap;
        public int ValidLaps;
        /// <summary>Fuel used per valid lap, oldest first (shared, don't modify).</summary>
        public double[] Laps = new double[0];
        /// <summary>Average of the valid laps since the last pit stop, and how many there are.</summary>
        public double StintAvgPerLap;
        public int StintValidLaps;
        public double LapsToGo = -1;      // race laps still to drive (fractional), -1 unknown
        public bool Valid { get { return AvgPerLap > 0; } }

        /// <summary>
        /// The same values as taken at the player's last line crossing (or pit exit): the fuel widget's per-lap rows
        /// use these, so laps remaining, refuel and fuel at end change once a lap instead of ticking all the time.
        /// Null until the first snapshot.
        /// </summary>
        public FuelInfo AtLine;

        public FuelInfo Copy()
        {
            var c = (FuelInfo)MemberwiseClone();
            c.AtLine = null;
            return c;
        }

        /// <summary>Average of the last n valid laps (fewer when there aren't n yet; 0 = no laps).</summary>
        public double AverageOfLast(int n)
        {
            int k = Math.Min(Math.Max(1, n), Laps.Length);
            if (k == 0) return 0;
            double sum = 0;
            for (int i = Laps.Length - k; i < Laps.Length; i++) sum += Laps[i];
            return sum / k;
        }

        public double LapsRemaining(double perLap) { return perLap > 0 ? Level / perLap : 0; }
        public double Need(double perLap) { return LapsToGo >= 0 ? LapsToGo * perLap : 0; }
        public double Refuel(double perLap, double margin) { return LapsToGo >= 0 ? Math.Max(0, Need(perLap) - Level + margin) : 0; }
        public double FuelAtEnd(double perLap) { return Level - Need(perLap); }

        public int StopsNeeded(double perLap, double margin)
        {
            double r = Refuel(perLap, margin);
            if (r <= 0 || MaxFuel <= 0) return 0;
            return (int)Math.Ceiling(r / MaxFuel);
        }
    }

    public sealed class RaceSnapshot
    {
        public static readonly RaceSnapshot Empty = new RaceSnapshot();

        public long Version;
        public string Source = "";
        public bool Connected;
        public double SessionTime;
        public string SessionType = "";
        public string SessionName = "";
        public bool IsRace;
        public SessionState State;
        public SessionFlags Flags;
        public string TrackName = "";
        public double TrackLengthKm;
        public float TrackTemp;
        public float AirTemp;
        /// <summary>Relative humidity 0..1.</summary>
        public float Humidity;
        /// <summary>Tire compound names by CarIdxTireCompound index (from the player's car; empty when unknown).</summary>
        public Dictionary<int, string> TireNames = new Dictionary<int, string>();
        public double TimeRemain = -1;
        public double TimeTotal = -1;
        public double TimeOfDay;
        public int LeaderLap;
        public int TotalLaps = -1;
        public double EstTotalLaps = -1;
        public int Incidents;
        /// <summary>Incident limits of the event (-1 = unlimited): DQ, first penalty, then a penalty every N more.</summary>
        public int IncidentLimit = -1, IncidentWarningInitial = -1, IncidentWarningSubsequent = -1;

        /// <summary>The next incident count that brings a penalty (17, then 25, 33... with 17 / +8); -1 = none.</summary>
        public int NextIncidentPenalty
        {
            get
            {
                if (IncidentWarningInitial <= 0) return -1;
                if (Incidents < IncidentWarningInitial) return IncidentWarningInitial;
                if (IncidentWarningSubsequent <= 0) return -1;
                return IncidentWarningInitial + ((Incidents - IncidentWarningInitial) / IncidentWarningSubsequent + 1) * IncidentWarningSubsequent;
            }
        }

        public List<CarInfo> Cars = new List<CarInfo>();
        public List<ClassStandings> Classes = new List<ClassStandings>();
        public CarInfo Player;
        public ClassStandings PlayerClass;
        /// <summary>Cars in the world sorted by relative time, ahead of the player first.</summary>
        public List<CarInfo> Relative = new List<CarInfo>();
        public FuelInfo Fuel = new FuelInfo();
    }
}
