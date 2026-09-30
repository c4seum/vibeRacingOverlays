namespace vibeRacingOverlays.Data.Engine
{
    /// <summary>
    /// How far the race still goes, in laps (fractional). iRacing rules:
    /// - lap race: over when the overall leader completes the scheduled laps;
    /// - timed race: when the time runs out the overall leader finishes the lap he is on, then the checkered flag;
    /// - both limits set: whichever comes first;
    /// - every other car (any class, lapped or not) finishes the next time it crosses the line after that.
    /// Progress = laps completed + fraction of the current lap (negative before the start line).
    /// </summary>
    public static class RaceDistance
    {
        /// <summary>Laps the overall leader still has to drive, -1 = unknown.</summary>
        /// <param name="totalLaps">scheduled laps, 0 or less = no lap limit</param>
        /// <param name="timed">the session has a time limit</param>
        /// <param name="timeRemain">session time left (s); 0 or less = the time has run out</param>
        public static double LeaderLapsToGo(int totalLaps, bool timed, double timeRemain, double leaderProgress, double leaderLap)
        {
            double best = double.MaxValue;
            if (totalLaps > 0) best = Math.Max(0, totalLaps - leaderProgress);
            if (timed && leaderLap > 0)
            {
                double remain = Math.Max(0, timeRemain);
                // the first line crossing after the time has run out
                double byTime = Math.Ceiling(leaderProgress + remain / leaderLap) - leaderProgress;
                best = Math.Min(best, Math.Max(0, byTime));
            }
            return best == double.MaxValue ? -1 : best;
        }

        /// <summary>Laps a car still has to drive when the leader has leaderToGo laps left: until its first line crossing after the leader finishes.</summary>
        public static double CarLapsToGo(double leaderToGo, double leaderLap, double progress, double lap)
        {
            if (leaderToGo < 0 || lap <= 0) return -1;
            double timeLeft = leaderToGo * leaderLap;
            return Math.Max(0, Math.Ceiling(progress + timeLeft / lap - 1e-6) - progress);
        }
    }
}
