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

            // --settings-dir <folder>: a separate settings folder (tests run on a copy, never on the user's own file)
            int dirIdx = Array.IndexOf(e.Args, "--settings-dir");
            if (dirIdx >= 0 && dirIdx + 1 < e.Args.Length) AppSettings.FolderOverride = Path.GetFullPath(e.Args[dirIdx + 1]);

            // a bug in the editor must not take the widgets (and unsaved changes) down with it: log it, tell the user
            // once in a while, and keep running
            DateTime lastShown = DateTime.MinValue;
            DispatcherUnhandledException += (s, ev) =>
            {
                AppSettings.LogError("Unexpected error: " + ev.Exception);
                ev.Handled = true;
                if (DateTime.UtcNow - lastShown < TimeSpan.FromSeconds(10)) return;
                lastShown = DateTime.UtcNow;
                MessageBox.Show("Something went wrong: " + ev.Exception.Message + "\n\nThe app keeps running. The details are in " + AppSettings.ErrorLog + ".",
                    BuildInfo.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            // --zorder-log <file>: log what's in front and what the widgets do (iRacing display modes, see ZOrderDiag)
            int zIdx = Array.IndexOf(e.Args, "--zorder-log");
            if (zIdx >= 0 && zIdx + 1 < e.Args.Length) OverlayManager.ZOrderLog = Path.GetFullPath(e.Args[zIdx + 1]);

            int snapIdx = Array.IndexOf(e.Args, "--snapshot");
            if (snapIdx >= 0)
            {
                string dir = snapIdx + 1 < e.Args.Length ? e.Args[snapIdx + 1] : ".";
                RenderSnapshots(dir, Array.IndexOf(e.Args, "--live") >= 0);
                Shutdown();
                return;
            }

            int fontIdx = Array.IndexOf(e.Args, "--font-samples");
            if (fontIdx >= 0)
            {
                // --text-mode ideal|display: lay out and draw the text in one mode (default: as the overlays do)
                int modeIdx = Array.IndexOf(e.Args, "--text-mode");
                if (modeIdx >= 0 && modeIdx + 1 < e.Args.Length)
                {
                    var m = e.Args[modeIdx + 1] == "display" ? TextFormattingMode.Display : TextFormattingMode.Ideal;
                    TabularText.LayoutMode = m;
                    PreviewRenderer.VisualMode = m;
                }
                RenderFontSamples(fontIdx + 1 < e.Args.Length ? e.Args[fontIdx + 1] : "fonts.png", e.Args.Skip(fontIdx + 2).Where(a => !a.StartsWith("--")).ToArray());
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

            if (!FirstInstance()) { Shutdown(); return; }

            var settings = AppSettings.Load();
            settings.StartupBackup();   // before anything can be saved
            var settingsSaved = File.Exists(AppSettings.SettingsFile) ? File.GetLastWriteTimeUtc(AppSettings.SettingsFile) : DateTime.MinValue;
            library = new PresetLibrary(settings, Dispatcher);
            library.Start(settingsSaved);   // takes in preset files that were added or changed while the app was closed
            UI.ThemeManager.Apply(settings.Theme);
            TextMeasure.SetFont(settings.Font);
            telemetry = new TelemetryService { Mode = settings.Source, LivePositions = settings.LivePositions };
            telemetry.Start();
            overlays = new OverlayManager(settings, telemetry);

            // --update-feed <url or file>: test the update notice (also in DEV builds)
            int feedIdx = Array.IndexOf(e.Args, "--update-feed");
            if (feedIdx >= 0 && feedIdx + 1 < e.Args.Length) UpdateCheck.FeedOverride = e.Args[feedIdx + 1];

            var main = new MainWindow(settings, telemetry, overlays);
            MainWindow = main;
            tray = new UI.TrayIcon(settings, overlays, ShowMain, ExitApp);
            // closing the window keeps the app (and its widgets) running in the tray, unless the user turned that off
            main.Closing += (s, ev) =>
            {
                if (exiting || !settings.CloseToTray) return;
                ev.Cancel = true;
                main.Hide();
                if (!settings.TrayTipShown)
                {
                    tray.Notify(BuildInfo.AppName + " is still running", "Your widgets stay on screen. Open the app or exit it from this icon in the system tray.");
                    settings.TrayTipShown = true;
                    overlays.ScheduleSave();
                }
            };
            SessionEnding += (s, ev) => exiting = true;
            main.Show();
            overlays.Start();
            StartUpdateChecks(settings, main);

            if (AppSettings.LoadProblem != null)
                MessageBox.Show(main, "Your settings file could not be read, so the app started with the default layout.\n\n"
                    + "Nothing is lost: the file was copied to\n" + AppSettings.BackupFolder + "\n(settings-unreadable-...json).\n\nReason: " + AppSettings.LoadProblem,
                    BuildInfo.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            else if (settings.Unreadable != null)
                MessageBox.Show(main, settings.Unreadable.Count + " widget(s) or preset(s) in your settings were made with another version and can't be used by this one.\n\n"
                    + "They are kept in your settings file and come back with a version that knows them.",
                    BuildInfo.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        PresetLibrary library;
        UI.TrayIcon tray;
        bool exiting;

        Mutex instance;
        EventWaitHandle showRequest, exitRequest;

        /// <summary>Same name in the installer (Program.CloseRunningApp).</summary>
        public static string ExitEventName(int processId) { return @"Local\vibeRacingOverlays-exit-" + processId; }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

        /// <summary>
        /// One app per settings folder: with the app running in the tray, starting it again (Start menu) would
        /// otherwise give two apps writing the same settings. The second start asks the first to show its window.
        /// DEV, release and --settings-dir test folders each count separately.
        /// </summary>
        bool FirstInstance()
        {
            byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppSettings.Folder.TrimEnd('\\').ToLowerInvariant()));
            string name = @"Local\vibeRacingOverlays-" + Convert.ToHexString(hash, 0, 8);
            bool first;
            instance = new Mutex(true, name, out first);
            if (!first)
            {
                try
                {
                    AllowSetForegroundWindow(-1);   // ASFW_ANY: the running app may bring its window to the front
                    using (var ev = EventWaitHandle.OpenExisting(name + "-show")) ev.Set();
                }
                catch (Exception) { }
                return false;
            }
            showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-show");
            ThreadPool.RegisterWaitForSingleObject(showRequest, (s, timedOut) => Dispatcher.BeginInvoke(new Action(ShowMain)), null, -1, false);
            // the installer (and the DEV build script) ask the app to exit through this event: closing the window only hides it to the tray
            exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName(Environment.ProcessId));
            ThreadPool.RegisterWaitForSingleObject(exitRequest, (s, timedOut) => Dispatcher.BeginInvoke(new Action(ExitApp)), null, -1, true);
            return true;
        }

        void ShowMain()
        {
            var w = MainWindow;
            if (w == null) return;
            w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }

        void ExitApp()
        {
            exiting = true;
            Shutdown();
        }

        /// <summary>Looks for a newer release shortly after the start and then twice a day; each new version is announced once in the tray.</summary>
        void StartUpdateChecks(AppSettings settings, MainWindow main)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += async (s, e) =>
            {
                timer.Interval = TimeSpan.FromHours(12);
                if (!UpdateCheck.Enabled(settings)) return;
                var info = await UpdateCheck.FindAsync();
                if (info == null || exiting) return;
                tray.Update = info;
                main.ShowUpdate(info);
                if (settings.UpdateNotified != info.Version)
                {
                    tray.Notify("Version " + info.Version + " is available", "Click to open the download page.", () => UpdateCheck.Open(info));
                    settings.UpdateNotified = info.Version;
                    overlays.ScheduleSave();
                }
            };
            timer.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (tray != null) tray.Dispose();
            if (library != null) library.Dispose();
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
                var done = f.IntArray("CarIdxLapCompleted", 64); var flags = f.IntArray("CarIdxSessionFlags", 64);
                sb.AppendLine("--- cars (session " + (sess != null ? sess.Type : "?") + ", results " + (sess != null ? sess.Results.Count : 0) + ")");
                sb.AppendLine("idx  num  raw   class        tPos tCls  tBest    tLast   done | rPos rCls  rFast    rLast   rLaps  flags     name");
                foreach (var d in info.Drivers.Values.OrderBy(d => d.CarIdx))
                {
                    int i = d.CarIdx;
                    var r = sess != null ? sess.Results.FirstOrDefault(x => x.CarIdx == i) : null;
                    sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0,3} {1,4} {2,5} {3,-12} {4,4} {5,4} {6,8:0.000} {7,8:0.000} {8,4} | {9,4} {10,4} {11,8:0.000} {12,8:0.000} {13,5}  {15,8:X}  {14}",
                        i, d.CarNumber, d.CarNumberRaw, Truncate(d.CarClassShortName, 12), pos[i], cpos[i], best[i], last[i], done[i],
                        r != null ? r.Position : 0, r != null ? r.ClassPosition : 0, r != null ? r.FastestTime : 0, r != null ? r.LastTime : 0, r != null ? r.LapsComplete : 0,
                        d.UserName, flags[i]));
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

        /// <summary>
        /// --font-samples &lt;png&gt; [font...]: one sheet with the Relative and Fuel calculator (Default presets, demo
        /// multiclass race) in each font, to compare fonts in the widgets' own look. "!Font" = without pixel snapping.
        /// </summary>
        static void RenderFontSamples(string file, string[] fonts)
        {
            if (fonts.Length == 0) fonts = new[] { "Bahnschrift" };
            var defaults = AppSettings.Load();
            using (var svc = new TelemetryService { Mode = SourceMode.Demo, DemoMultiClass = true })
            {
                svc.Start();
                var until = DateTime.UtcNow.AddSeconds(30);
                while (svc.Latest.SessionTime < 600 && DateTime.UtcNow < until) Thread.Sleep(50);
                Thread.Sleep(200);
                var snap = svc.Latest;
                var rows = new List<(string Label, BitmapSource A, BitmapSource B)>();
                foreach (var spec in fonts)
                {
                    bool unsnapped = spec.StartsWith("!");
                    string font = spec.TrimStart('!');
                    TabularText.PixelSnap = !unsnapped;
                    BitmapSource Render(Type t)
                    {
                        var dl = new DisplayList();
                        // any font name, also installed fonts that aren't in the widget font list
                        using (TextMeasure.Use(font)) Widget.Create(defaults.DefaultsFor(t)).Draw(dl, snap);
                        dl.Font = font;
                        return PreviewRenderer.ToBitmap(dl, font, 8);
                    }
                    rows.Add((font + (unsnapped ? "  (before: not on whole pixels)" : ""), Render(typeof(RelativeSettings)), Render(typeof(FuelSettings))));
                }
                TabularText.PixelSnap = true;

                const int labelH = 26, gap = 12;
                int width = (int)rows.Max(r => r.A.PixelWidth + gap + r.B.PixelWidth) + 2 * gap;
                int height = rows.Sum(r => labelH + Math.Max(r.A.PixelHeight, r.B.PixelHeight) + gap) + gap;
                var visual = new DrawingVisual();
                TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x22, 0x26)), null, new Rect(0, 0, width, height));
                    double y = gap;
                    var face = new Typeface("Segoe UI");
                    foreach (var r in rows)
                    {
                        dc.DrawText(new FormattedText(r.Label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 15, Brushes.White, 1), new Point(gap, y + 3));
                        y += labelH;
                        dc.DrawImage(r.A, new Rect(gap, y, r.A.PixelWidth, r.A.PixelHeight));
                        dc.DrawImage(r.B, new Rect(gap + r.A.PixelWidth + gap, y, r.B.PixelWidth, r.B.PixelHeight));
                        y += Math.Max(r.A.PixelHeight, r.B.PixelHeight) + gap;
                    }
                }
                var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(visual);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(file)) enc.Save(fs);
            }
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
                        Widget.Create(job.Item1).Paint(dl, snap);
                        var bmp = PreviewRenderer.ToBitmap(dl, font, 10);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        string name = string.Concat(job.Item2.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')) + run.Suffix + ".png";
                        using (var fs = File.Create(Path.Combine(dir, name))) enc.Save(fs);
                    }

                    // relative with the cars right ahead and behind in the pits, in both "Cars in the pits" styles (demo only: changes the snapshot)
                    if (!live && snap.Player != null)
                    {
                        int pi = snap.Relative.IndexOf(snap.Player);
                        foreach (int k in new[] { pi - 1, pi + 1 })
                            if (k >= 0 && k < snap.Relative.Count) snap.Relative[k].OnPitRoad = true;
                        // and the three flags of the pit column: meatball, black flag, furled black flag
                        var flags = new[] { (pi - 2, Data.Telemetry.SessionFlags.Repair), (pi + 2, Data.Telemetry.SessionFlags.Black), (pi + 3, Data.Telemetry.SessionFlags.Furled) };
                        foreach (var fl in flags)
                            if (fl.Item1 >= 0 && fl.Item1 < snap.Relative.Count) { snap.Relative[fl.Item1].DriverFlags = fl.Item2; snap.Relative[fl.Item1].OnPitRoad = false; snap.Relative[fl.Item1].InPitStall = false; }
                        foreach (var style in new[] { PitRowStyle.DimText, PitRowStyle.DimRow })
                        {
                            var rs = new RelativeSettings { PitRows = style };
                            var dl = new DisplayList();
                            Widget.Create(rs).Paint(dl, snap);
                            var enc = new PngBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(PreviewRenderer.ToBitmap(dl, font, 10)));
                            using (var fs = File.Create(Path.Combine(dir, "relative_pits_" + style.ToString().ToLowerInvariant() + run.Suffix + ".png"))) enc.Save(fs);
                        }
                    }
                }
            }
        }
    }
}