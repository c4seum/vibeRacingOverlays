using vibeRacingOverlays.Data.IRSdk;

namespace vibeRacingOverlays.Data.Telemetry
{
    public enum TrackSurface { NotInWorld = -1, OffTrack = 0, InPitStall = 1, ApproachingPits = 2, OnTrack = 3 }

    public enum SessionState { Invalid = 0, GetInCar = 1, Warmup = 2, ParadeLaps = 3, Racing = 4, Checkered = 5, CoolDown = 6 }

    [Flags]
    public enum SessionFlags : uint
    {
        Checkered = 0x1, White = 0x2, Green = 0x4, Yellow = 0x8, Red = 0x10, Blue = 0x20, Debris = 0x40, Crossed = 0x80,
        YellowWaving = 0x100, OneLapToGreen = 0x200, GreenHeld = 0x400, TenToGo = 0x800, FiveToGo = 0x1000,
        RandomWaving = 0x2000, Caution = 0x4000, CautionWaving = 0x8000,
        // per driver (CarIdxSessionFlags): Black = penalty to serve (drive-through / stop-and-go),
        // Furled = rolled-up black flag (warning, e.g. slow down after cutting), Repair = meatball
        Black = 0x10000, Disqualify = 0x20000, Servicible = 0x40000, Furled = 0x80000, Repair = 0x100000,
    }

    /// <summary>
    /// The subset of iRacing telemetry the app uses, copied out of one 60 Hz frame.
    /// </summary>
    public sealed class TelemetryState
    {
        public const int MaxCars = 64;

        public double SessionTime;
        public int SessionNum;
        public SessionState SessionState;
        public SessionFlags SessionFlags;
        public double SessionTimeRemain;
        public int SessionLapsRemain;
        public double SessionTimeOfDay;

        public int PlayerCarIdx = -1;
        /// <summary>Seconds left of the player's tow (race only; 0 = not being towed).</summary>
        public float PlayerCarTowTime;
        public bool IsOnTrack;
        public bool IsReplayPlaying;
        public bool OnPitRoad;
        public int Lap;
        public int LapCompleted;
        public float LapDistPct;
        public float FuelLevel;
        public float FuelUsePerHour;
        public float TrackTemp;
        public float AirTemp;
        /// <summary>Relative humidity 0..1.</summary>
        public float Humidity;
        public float LapLastLapTime;
        public float LapBestLapTime;
        public float Speed;
        public int Incidents;

        public int[] CarIdxLap = new int[MaxCars];
        public int[] CarIdxLapCompleted = new int[MaxCars];
        public float[] CarIdxLapDistPct = new float[MaxCars];
        public int[] CarIdxPosition = new int[MaxCars];
        public int[] CarIdxClassPosition = new int[MaxCars];
        public bool[] CarIdxOnPitRoad = new bool[MaxCars];
        public int[] CarIdxTrackSurface = new int[MaxCars];
        public float[] CarIdxLastLapTime = new float[MaxCars];
        public float[] CarIdxBestLapTime = new float[MaxCars];
        public float[] CarIdxF2Time = new float[MaxCars];
        public float[] CarIdxEstTime = new float[MaxCars];
        public int[] CarIdxTireCompound = new int[MaxCars];
        public int[] CarIdxSessionFlags = new int[MaxCars];

        public static TelemetryState FromFrame(RawFrame f)
        {
            var s = new TelemetryState
            {
                SessionTime = f.Double("SessionTime"),
                SessionNum = f.Int("SessionNum"),
                SessionState = (SessionState)f.Int("SessionState"),
                SessionFlags = (SessionFlags)(uint)f.Int("SessionFlags"),
                SessionTimeRemain = f.Double("SessionTimeRemain"),
                SessionLapsRemain = f.Int("SessionLapsRemainEx", 32767),
                SessionTimeOfDay = f.Float("SessionTimeOfDay"),
                PlayerCarIdx = f.Int("PlayerCarIdx", -1),
                PlayerCarTowTime = f.Float("PlayerCarTowTime"),
                IsOnTrack = f.Bool("IsOnTrack"),
                IsReplayPlaying = f.Bool("IsReplayPlaying"),
                OnPitRoad = f.Bool("OnPitRoad"),
                Lap = f.Int("Lap"),
                LapCompleted = f.Int("LapCompleted"),
                LapDistPct = f.Float("LapDistPct"),
                FuelLevel = f.Float("FuelLevel"),
                FuelUsePerHour = f.Float("FuelUsePerHour"),
                TrackTemp = f.Float("TrackTempCrew", f.Float("TrackTemp")),
                AirTemp = f.Float("AirTemp"),
                Humidity = f.Float("RelativeHumidity"),
                LapLastLapTime = f.Float("LapLastLapTime"),
                LapBestLapTime = f.Float("LapBestLapTime"),
                Speed = f.Float("Speed"),
                Incidents = f.Int("PlayerCarMyIncidentCount"),
                CarIdxLap = f.IntArray("CarIdxLap", MaxCars),
                CarIdxLapCompleted = f.IntArray("CarIdxLapCompleted", MaxCars),
                CarIdxLapDistPct = f.FloatArray("CarIdxLapDistPct", MaxCars),
                CarIdxPosition = f.IntArray("CarIdxPosition", MaxCars),
                CarIdxClassPosition = f.IntArray("CarIdxClassPosition", MaxCars),
                CarIdxOnPitRoad = f.BoolArray("CarIdxOnPitRoad", MaxCars),
                CarIdxTrackSurface = f.IntArray("CarIdxTrackSurface", MaxCars),
                CarIdxLastLapTime = f.FloatArray("CarIdxLastLapTime", MaxCars),
                CarIdxBestLapTime = f.FloatArray("CarIdxBestLapTime", MaxCars),
                CarIdxF2Time = f.FloatArray("CarIdxF2Time", MaxCars),
                CarIdxEstTime = f.FloatArray("CarIdxEstTime", MaxCars),
                CarIdxTireCompound = f.IntArray("CarIdxTireCompound", MaxCars),
                CarIdxSessionFlags = f.IntArray("CarIdxSessionFlags", MaxCars),
            };
            return s;
        }
    }
}
