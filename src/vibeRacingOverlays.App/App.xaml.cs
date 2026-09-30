using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Overlay;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.App.Widgets;
using vibeRacingOverlays.Data.Engine;

namespace vibeRacingOverlays.App
{
    public partial class App : Application
    {
        TelemetryService telemetry;
        OverlayManager overlays;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            int snapIdx = Array.IndexOf(e.Args, "--snapshot");
            if (snapIdx >= 0)
            {
                string dir = snapIdx + 1 < e.Args.Length ? e.Args[snapIdx + 1] : ".";
                RenderSnapshots(dir, Array.IndexOf(e.Args, "--live") >= 0);
                Shutdown();
                return;
            }

            int dumpIdx = Array.IndexOf(e.Args, "--dump");
            if (dumpIdx >= 0)
            {
                DumpRaw(dumpIdx + 1 < e.Args.Length ? e.Args[dumpIdx + 1] : "dump.txt");
                Shutdown();
                return;
            }

            var settings = AppSettings.Load();
            UI.ThemeManager.Apply(settings.Theme);
            TextMeasure.SetFont(settings.Font);
            telemetry = new TelemetryService { Mode = settings.Source, LivePositions = settings.LivePositions };
            telemetry.Start();
            overlays = new OverlayManager(settings, telemetry);

            var main = new MainWindow(settings, telemetry, overlays);
            MainWindow = main;
            main.Show();
            overlays.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (overlays != null) overlays.Dispose();
            if (telemetry != null) telemetry.Dispose();
            base.OnExit(e);
        }

        static string Truncate(string s, int n) { return string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n)); }

        /// <summary>Diagnostics: writes the raw per-car telemetry of one iRacing frame to a text file.</summary>
        static void DumpRaw(string file)
        {
            var sb = new System.Text.StringBuilder();
            using (var sdk = new Data.IRSdk.IRacingSdk())
            {
                if (!sdk.TryOpen() || !sdk.IsConnected) { File.WriteAllText(file, "iRacing not connected"); return; }
                Data.IRSdk.RawFrame f = null;
                for (int i = 0; i < 20 && f == null; i++) { sdk.WaitForData(50); f = sdk.ReadFrame(); }
                if (f == null) { File.WriteAllText(file, "no frame"); return; }
                string yaml = sdk.ReadSessionInfoIfChanged() ?? "";
                foreach (var line in yaml.Split('\n').Where(l => l.Contains("EstLapTime")).Take(6)) sb.AppendLine("yaml: " + line.TrimEnd());
                // every distinct CarClass*/CarScreenName* line, to see which class info iRacing provides
                foreach (var line in yaml.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("CarClass") || l.StartsWith("CarScreenName") || l.StartsWith("CarID:") || l.StartsWith("CarPath")).Distinct().Take(80))
                    sb.AppendLine("class: " + line);
                sb.AppendLine("PlayerCarIdx=" + f.Int("PlayerCarIdx") + " CamCarIdx=" + f.Int("CamCarIdx") + " IsReplayPlaying=" + f.Int("IsReplayPlaying")
                    + " SessionTime=" + f.Double("SessionTime") + " SessionState=" + f.Int("SessionState") + " ReplayFrameNum=" + f.Int("ReplayFrameNum"));
                var pct = f.FloatArray("CarIdxLapDistPct", 64); var est = f.FloatArray("CarIdxEstTime", 64);
                var lap = f.IntArray("CarIdxLap", 64); var pos = f.IntArray("CarIdxPosition", 64); var surf = f.IntArray("CarIdxTrackSurface", 64);
                var f2 = f.FloatArray("CarIdxF2Time", 64);
                for (int i = 0; i < 64; i++)
                    if (pos[i] > 0 || surf[i] >= 0)
                        sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0,2} pos{1,3} lap{2,4} pct {3:0.0000} est {4,8:0.000} f2 {5,8:0.000} surf {6}", i, pos[i], lap[i], pct[i], est[i], f2[i], surf[i]));

                // per car: telemetry lap data next to the session results iRacing shows in its own lists
                var info = Data.IRSdk.SessionInfo.Parse(yaml);
                var sess = info.Session(f.Int("SessionNum"));
                var cpos = f.IntArray("CarIdxClassPosition", 64); var best = f.FloatArray("CarIdxBestLapTime", 64); var last = f.FloatArray("CarIdxLastLapTime", 64);
                var done = f.IntArray("CarIdxLapCompleted", 64);
                sb.AppendLine("--- cars (session " + (sess != null ? sess.Type : "?") + ", results " + (sess != null ? sess.Results.Count : 0) + ")");
                sb.AppendLine("idx  num  raw   class        tPos tCls  tBest    tLast   done | rPos rCls  rFast    rLast   rLaps  name");
                foreach (var d in info.Drivers.Values.OrderBy(d => d.CarIdx))
                {
                    int i = d.CarIdx;
                    var r = sess != null ? sess.Results.FirstOrDefault(x => x.CarIdx == i) : null;
                    sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0,3} {1,4} {2,5} {3,-12} {4,4} {5,4} {6,8:0.000} {7,8:0.000} {8,4} | {9,4} {10,4} {11,8:0.000} {12,8:0.000} {13,5}  {14}",
                        i, d.CarNumber, d.CarNumberRaw, Truncate(d.CarClassShortName, 12), pos[i], cpos[i], best[i], last[i], done[i],
                        r != null ? r.Position : 0, r != null ? r.ClassPosition : 0, r != null ? r.FastestTime : 0, r != null ? r.LastTime : 0, r != null ? r.LapsComplete : 0,
                        d.UserName));
                }
                File.WriteAllText(file + ".yaml", yaml);
            }
            using (var svc = new TelemetryService { Mode = SourceMode.IRacing })
            {
                svc.Start();
                var until = DateTime.UtcNow.AddSeconds(5);
                while (svc.Latest.Version < 5 && DateTime.UtcNow < until) Thread.Sleep(50);
                var snap = svc.Latest;
                sb.AppendLine("--- relative (player " + (snap.Player != null ? snap.Player.CarIdx + " pct " + snap.Player.LapDistPct : "none")
                    + ", estLap " + (snap.PlayerClass != null ? snap.PlayerClass.EstLapTime : 0) + ")");
                foreach (var c in snap.Relative.Take(12))
                    sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0,2} {1,-24} pct {2:0.0000} rel {3,8:0.000} lap {4}", c.CarIdx, c.Name, c.LapDistPct, c.RelativeTime, c.RelativeLap));
            }
            File.WriteAllText(file, sb.ToString());
        }

        /// <summary>
        /// Renders every widget type to PNG files (design checks): with live iRacing data (--live),
        /// otherwise with both the single-class and the multiclass demo race.
        /// </summary>
        /// <summary>Stress test for text fitting: every column narrower than its content.</summary>
        static StandingsSettings NarrowStandings()
        {
            // every column on with its widest format, a narrow name and no spacing: nothing may overlap
            var s = new StandingsSettings { ColumnSpacing = 0 };
            var widest = new Dictionary<string, string> { { "lic", "L2" }, { "ir", "full" }, { "gap", "3" }, { "int", "3" }, { "last", "3" }, { "best", "3" }, { "tire", "always" } };
            foreach (var c in s.PracticeQualify.Columns.Concat(s.Race.Columns))
            {
                string fmt;
                if (widest.TryGetValue(c.Key, out fmt)) c.Format = fmt;
                if (c.Key == "name") c.Width = 120;
                c.Enabled = true;
            }
            return s;
        }

        static void RenderSnapshots(string dir, bool live)
        {
            Directory.CreateDirectory(dir);
            var font = AppSettings.Load().Font;
            TextMeasure.SetFont(font);
            var runs = live ? new[] { (Mode: SourceMode.IRacing, Multi: true, Practice: false, Suffix: "") }
                            : new[] { (Mode: SourceMode.Demo, Multi: false, Practice: false, Suffix: "_single"), (Mode: SourceMode.Demo, Multi: true, Practice: false, Suffix: "_multi"),
                                      (Mode: SourceMode.Demo, Multi: false, Practice: true, Suffix: "_single_pq"), (Mode: SourceMode.Demo, Multi: true, Practice: true, Suffix: "_multi_pq") };
            foreach (var run in runs)
            {
                using (var svc = new TelemetryService { Mode = run.Mode, DemoMultiClass = run.Multi, DemoPractice = run.Practice })
                {
                    svc.Start();
                    var until = DateTime.UtcNow.AddSeconds(live ? 8 : 30);
                    if (live) while (svc.Latest.Version < 5 && DateTime.UtcNow < until) Thread.Sleep(50);
                    else while (svc.Latest.SessionTime < 600 && DateTime.UtcNow < until) Thread.Sleep(50);
                    Thread.Sleep(200);
                    var snap = svc.Latest;

                    // default settings per widget type, plus the user's own saved overlays
                    var jobs = Widget.Catalog.Select(e => { var ws = e.Make(); return (ws, ws.TypeName); })
                        .Concat(AppSettings.Load().Widgets.Select(ws => (ws, "user " + ws.Title)))
                        .Concat(new[] { ((WidgetSettings)NarrowStandings(), "standings narrow") });
                    foreach (var job in jobs)
                    {
                        var dl = new DisplayList();
                        Widget.Create(job.Item1).Draw(dl, snap);
                        var bmp = PreviewRenderer.ToBitmap(dl, font, 10);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        string name = string.Concat(job.Item2.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')) + run.Suffix + ".png";
                        using (var fs = File.Create(Path.Combine(dir, name))) enc.Save(fs);
                    }
                }
            }
        }
    }
}