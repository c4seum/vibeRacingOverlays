using vibeRacingOverlays.Data.Model;
using vibeRacingOverlays.Data.Telemetry;

namespace vibeRacingOverlays.Data.Engine
{
    public enum SourceMode { Auto, IRacing, Demo }

    /// <summary>
    /// Background thread: reads telemetry at 60 Hz, tracks every frame, and publishes a new
    /// RaceSnapshot at <see cref="SnapshotHz"/>. Overlays just read <see cref="Latest"/>.
    /// In Auto mode the demo race runs while iRacing isn't connected.
    /// </summary>
    public sealed class TelemetryService : IDisposable
    {
        readonly IRacingSource iracing = new IRacingSource();
        DemoSource demo;
        RaceEngine engine = new RaceEngine();
        ITelemetrySource active;
        Thread thread;
        volatile bool running;
        volatile RaceSnapshot latest = RaceSnapshot.Empty;

        public SourceMode Mode = SourceMode.Auto;
        public double SnapshotHz = 20;
        public bool LivePositions = true;
        /// <summary>Demo race layout: GT3 + GT4 (true) or GT3 only (false).</summary>
        public bool DemoMultiClass = true;

        public RaceSnapshot Latest { get { return latest; } }
        public string ActiveSource { get { var a = active; return a != null ? a.Name : "-"; } }
        public bool IRacingConnected { get { return iracing.Connected; } }

        public void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "Telemetry", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        public void Dispose()
        {
            running = false;
            if (thread != null) thread.Join(1000);
            iracing.Dispose();
            if (demo != null) demo.Dispose();
        }

        void Run()
        {
            DateTime nextSnapshot = DateTime.MinValue;
            TelemetryState lastState = null;
            while (running)
            {
                try
                {
                    var src = PickSource();
                    if (src != active)
                    {
                        active = src;
                        engine = new RaceEngine();
                        // the new engine needs the session info, which a source normally only sends when it changes
                        if (src != null) src.ResendSession();
                        lastState = null;
                        latest = RaceSnapshot.Empty;
                    }
                    if (src == null) { Thread.Sleep(250); continue; }

                    src.WaitForData(50);
                    TelemetryState state;
                    IRSdk.SessionInfo session;
                    if (src.Poll(out state, out session))
                    {
                        if (session != null) engine.SetSession(session);
                        engine.LivePositions = LivePositions;
                        engine.Track(state);
                        lastState = state;
                    }

                    var now = DateTime.UtcNow;
                    if (lastState != null && engine.Session != null && now >= nextSnapshot)
                    {
                        nextSnapshot = now.AddSeconds(1.0 / Math.Max(1, SnapshotHz));
                        latest = engine.Build(lastState, src.Name);
                    }
                    if (src == iracing && !iracing.Connected && latest.Connected) latest = RaceSnapshot.Empty;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Telemetry: " + ex);
                    Thread.Sleep(100);
                }
            }
        }

        ITelemetrySource PickSource()
        {
            switch (Mode)
            {
                case SourceMode.IRacing:
                    iracing.WaitForData(0);
                    return iracing;
                case SourceMode.Demo:
                    return Demo();
                default:
                    if (!iracing.Connected) iracing.WaitForData(0); // tries to (re)open the memory map
                    return iracing.Connected ? (ITelemetrySource)iracing : Demo();
            }
        }

        DemoSource Demo()
        {
            if (demo == null) demo = new DemoSource(DemoMultiClass);
            return demo;
        }
    }
}
