using vibeRacingOverlays.Data.IRSdk;

namespace vibeRacingOverlays.Data.Telemetry
{
    public interface ITelemetrySource : IDisposable
    {
        string Name { get; }

        /// <summary>True while the source delivers data (iRacing running and in a session).</summary>
        bool Connected { get; }

        /// <summary>Blocks until new data may be available.</summary>
        void WaitForData(int timeoutMs);

        /// <summary>
        /// Reads the latest data. Returns false when there is no new frame.
        /// <paramref name="session"/> is only set when the session info changed.
        /// </summary>
        bool Poll(out TelemetryState state, out SessionInfo session);

        /// <summary>Deliver the (unchanged) session info again with the next Poll, e.g. for a fresh engine.</summary>
        void ResendSession();
    }

    public sealed class IRacingSource : ITelemetrySource
    {
        readonly IRacingSdk sdk = new IRacingSdk();
        DateTime nextOpenAttempt = DateTime.MinValue;
        SessionInfo pendingSession;

        public string Name { get { return "iRacing"; } }

        public bool Connected { get { return sdk.IsOpen && sdk.IsConnected; } }

        public void WaitForData(int timeoutMs)
        {
            if (!sdk.IsOpen)
            {
                if (DateTime.UtcNow >= nextOpenAttempt)
                {
                    nextOpenAttempt = DateTime.UtcNow.AddSeconds(1);
                    if (sdk.TryOpen()) return;
                }
                Thread.Sleep(Math.Min(timeoutMs, 250));
                return;
            }
            if (!sdk.IsConnected)
            {
                // release the mapping so a restarted iRacing gets picked up cleanly
                sdk.Close();
                Thread.Sleep(Math.Min(timeoutMs, 250));
                return;
            }
            sdk.WaitForData(timeoutMs);
        }

        public bool Poll(out TelemetryState state, out SessionInfo session)
        {
            state = null; session = null;
            if (!sdk.IsOpen || !sdk.IsConnected) return false;

            string yaml = sdk.ReadSessionInfoIfChanged();
            if (yaml != null)
            {
                try { pendingSession = SessionInfo.Parse(yaml); } catch { }
            }

            var frame = sdk.ReadFrame();
            if (frame == null) return false;
            state = TelemetryState.FromFrame(frame);
            session = pendingSession;
            pendingSession = null;
            return true;
        }

        public void ResendSession() { sdk.ForceSessionReread(); }

        public IReadOnlyCollection<VarHeader> Variables { get { return sdk.Variables; } }

        public void Dispose() { sdk.Dispose(); }
    }
}
