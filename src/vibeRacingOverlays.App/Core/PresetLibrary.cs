using System.IO;
using System.Windows.Threading;

namespace vibeRacingOverlays.App.Core
{
    /// <summary>
    /// The preset library: every preset (except the Default ones) is also a file in Documents\vibeRacingOverlays\presets, and a
    /// preset file that appears there (copied in, shared by someone, changed by hand) shows up in the app, also while
    /// it runs. The app stays in charge of deleting: a preset file deleted outside the app is written again, so a
    /// preset is only ever lost by deleting it in the app.
    /// </summary>
    public sealed class PresetLibrary : IDisposable
    {
        /// <summary>The running library (null in snapshot / test modes without one).</summary>
        public static PresetLibrary Current { get; private set; }

        public static string Folder { get { return Path.Combine(Exchange.LibraryFolder, "presets"); } }

        /// <summary>Presets were added or changed from files (the UI refreshes its preset lists).</summary>
        public event Action Changed;

        readonly AppSettings settings;
        readonly Dispatcher ui;
        readonly Dictionary<string, string> written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // what we wrote, per file
        readonly HashSet<string> removed = new HashSet<string>();   // ids of presets deleted in the app
        FileSystemWatcher watcher;
        DispatcherTimer debounce;

        public PresetLibrary(AppSettings settings, Dispatcher ui)
        {
            this.settings = settings;
            this.ui = ui;
        }

        public void Start(DateTime settingsSavedUtc)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                Scan(settingsSavedUtc);
                Mirror();
                debounce = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(700) };
                debounce.Tick += (s, e) => { debounce.Stop(); if (Scan(DateTime.MinValue)) Raise(); Mirror(); };
                watcher = new FileSystemWatcher(Folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler poke = (s, e) => ui.BeginInvoke(new Action(() => { debounce.Stop(); debounce.Start(); }));
                watcher.Created += poke; watcher.Changed += poke; watcher.Deleted += poke;
                watcher.Renamed += (s, e) => poke(s, e);
                watcher.EnableRaisingEvents = true;
                Current = this;
            }
            catch (Exception ex) { AppSettings.LogError("Preset library " + Folder + " not available: " + ex.Message); }
        }

        void Raise() { if (Changed != null) Changed(); }

        static string FileFor(WidgetPreset p) { return Path.Combine(Folder, Exchange.SafeName(p.TypeName + " - " + p.Name) + ".vropreset.json"); }

        /// <summary>A preset is deleted in the app: its file goes too (call before <see cref="Mirror"/>).</summary>
        public void Removed(WidgetPreset p) { if (p != null) removed.Add(p.Id); }

        /// <summary>Writes every preset to its file (only what changed) and removes files of renamed / deleted presets.</summary>
        public void Mirror()
        {
            try
            {
                var wanted = new Dictionary<string, WidgetPreset>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in settings.Presets.Where(p => !p.IsDefault && p.Settings != null)) wanted[FileFor(p)] = p;
                foreach (var f in Directory.GetFiles(Folder, "*.vropreset.json"))
                {
                    if (wanted.ContainsKey(f)) continue;
                    // a file of one of our presets under an old name, or of a preset deleted in the app
                    var fp = TryRead(f);
                    if (fp != null && fp.Id != null && (removed.Contains(fp.Id) || settings.Presets.Any(p => p.Id == fp.Id))) { File.Delete(f); written.Remove(f); }
                }
                foreach (var kv in wanted)
                {
                    string text = Exchange.PresetJson(kv.Value);
                    string have;
                    if (written.TryGetValue(kv.Key, out have) && have == text && File.Exists(kv.Key)) continue;
                    if (!File.Exists(kv.Key) || File.ReadAllText(kv.Key) != text) File.WriteAllText(kv.Key, text);
                    written[kv.Key] = text;
                }
            }
            catch (Exception ex) { AppSettings.LogError("Preset library could not be updated: " + ex.Message); }
        }

        static WidgetPreset TryRead(string path)
        {
            try { return Exchange.ReadPreset(path); } catch { return null; }
        }

        /// <summary>
        /// Takes in preset files: new ones become presets, changed ones update theirs. At start a file only wins
        /// when it is newer than the settings file (<paramref name="settingsSavedUtc"/>); while running every change wins.
        /// Returns whether presets changed.
        /// </summary>
        bool Scan(DateTime settingsSavedUtc)
        {
            bool changed = false;
            IEnumerable<string> files;
            try { files = Directory.GetFiles(Folder, "*.vropreset.json").Concat(Directory.GetFiles(Folder, "*.vrowidget.json")).ToList(); }
            catch { return false; }
            foreach (var f in files)
            {
                try
                {
                    string text = File.ReadAllText(f), have;
                    if (written.TryGetValue(f, out have) && have == text) continue;   // our own file, unchanged
                    var fp = Exchange.ReadPreset(f);
                    if (fp == null) continue;   // a widget type this version doesn't know: leave the file alone
                    var known = fp.Id != null ? settings.Presets.FirstOrDefault(p => p.Id == fp.Id) : null;
                    if (known != null)
                    {
                        if (known.IsDefault || File.GetLastWriteTimeUtc(f) <= settingsSavedUtc) continue;
                        if (!AppSettings.SameSettings(known.Settings, fp.Settings) || known.Name != fp.Name)
                        {
                            fp.Settings.PresetId = null;
                            known.Settings = fp.Settings;
                            known.Name = settings.UniquePresetName(known.Settings.GetType(), fp.Name, known);
                            changed = true;
                        }
                        continue;
                    }
                    if (fp.Id != null && removed.Contains(fp.Id)) continue;
                    string fileId = fp.Id;
                    var added = settings.AddImportedPreset(fp);
                    changed = true;
                    // the file becomes the library file of the new preset (Mirror writes it under its own name)
                    if (!string.Equals(FileFor(added), f, StringComparison.OrdinalIgnoreCase) || fileId != added.Id) File.Delete(f);
                }
                catch (Exception ex) { AppSettings.LogError("Preset file " + f + " could not be read: " + ex.Message); }
            }
            return changed;
        }

        public void Dispose()
        {
            if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); }
            if (Current == this) Current = null;
        }
    }
}
