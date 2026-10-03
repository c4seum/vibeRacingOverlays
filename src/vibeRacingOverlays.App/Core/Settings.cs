using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using vibeRacingOverlays.Data.Engine;

namespace vibeRacingOverlays.App.Core
{
    /// <summary>Marks a property as editable in the settings panel.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SettingAttribute : Attribute
    {
        public string Label;
        public string Group = "General";
        public double Min = 0, Max = 100, Step = 1;
        public bool IsColor;
        public int Order;
        public string Tooltip;
        public SettingAttribute(string label) { Label = label; }
    }

    public sealed class ColumnConfig
    {
        public string Key { get; set; }
        public bool Enabled { get; set; } = true;
        public float Width { get; set; }
        /// <summary>Display format of the column / header item (a key from its ColumnDef.Formats); null = default.</summary>
        public string Format { get; set; }
    }

    public enum ShowWhen { Always, InCar, InRace }

    /// <summary>Point of the screen (and of the widget) that the position offsets are measured from.</summary>
    public enum Anchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(Widgets.StandingsSettings), "standings")]
    [JsonDerivedType(typeof(Widgets.RelativeSettings), "relative")]
    [JsonDerivedType(typeof(Widgets.FuelSettings), "fuel")]
    public abstract class WidgetSettings
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Title { get; set; } = "";
        public bool Enabled { get; set; } = true;
        /// <summary>Last on-screen position (screen pixels). Only used to place widgets without a Screen yet.</summary>
        public double X { get; set; } = 100;
        public double Y { get; set; } = 100;
        /// <summary>Monitor the widget is anchored to ("Left", "Middle", "Right"...); null = derive it (and the offsets) from X/Y.</summary>
        public string Screen { get; set; }
        public Anchor Anchor { get; set; } = Anchor.TopLeft;
        /// <summary>Distance (px) from the anchor, measured inwards; signed for the centre anchors.</summary>
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        /// <summary>Locked widgets can't be dragged or resized in edit layout mode.</summary>
        public bool Locked { get; set; }
        /// <summary>Preset last loaded into / saved from this widget (null = none).</summary>
        public string PresetId { get; set; }

        [Setting("Scale", Group = "Layout", Min = 0.5, Max = 3, Step = 0.05, Order = -10)]
        public double Scale { get; set; } = 1.0;

        [Setting("Background opacity", Group = "Layout", Min = 0, Max = 1, Step = 0.05, Order = -9)]
        public double BackgroundOpacity { get; set; } = 0.9;

        [Setting("Refresh rate (Hz)", Group = "Layout", Min = 1, Max = 60, Step = 1, Order = -8,
            Tooltip = "How often the widget may redraw. Lower = less FPS impact.")]
        public int RefreshHz { get; set; } = 10;

        [Setting("Show", Group = "Layout", Order = -7)]
        public ShowWhen Show { get; set; } = ShowWhen.Always;

        // Bahnschrift: the look every widget had before fonts could be chosen (also in the Default presets)
        [Setting("Font", Group = "Style", Order = 19, Tooltip = "The widget's font. Bahnschrift is the original look; the others come with the app (open-license fonts).")]
        public Rendering.WidgetFont Font { get; set; } = Rendering.WidgetFont.Bahnschrift;

        [JsonIgnore] public abstract string TypeName { get; }
    }

    /// <summary>Saved settings of one widget type, reusable in every layout.</summary>
    public sealed class WidgetPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "";
        public WidgetSettings Settings { get; set; }
        /// <summary>
        /// The "Default" preset of its widget type: the app's built-in settings, always intact (it can't be saved over,
        /// renamed or deleted; use Save as). New widgets start from it, the reset buttons go back to it.
        /// </summary>
        public bool IsDefault { get; set; }

        [JsonIgnore] public string TypeName { get { return Settings != null ? Settings.TypeName : ""; } }
        public override string ToString() { return Name; }   // shown by the preset combo box
    }

    /// <summary>
    /// A layout you work with: a named set of widgets, always the current version (every change is saved right away).
    /// Loading a layout preset overwrites its widgets.
    /// </summary>
    public sealed class LayoutConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "Layout";
        public List<WidgetSettings> Widgets { get; set; } = new List<WidgetSettings>();
        /// <summary>The layout preset last loaded into / saved from this layout (for Save and the "*"); null = none.</summary>
        public string PresetId { get; set; }

        public override string ToString() { return Name; }   // shown by the layout combo box
    }

    /// <summary>A saved set of widgets that can be loaded into a layout. "Get started" is the app's own (read-only).</summary>
    public sealed class LayoutPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "Preset";
        public List<WidgetSettings> Widgets { get; set; } = new List<WidgetSettings>();
        /// <summary>"Get started": always the app's built-in layout; can't be saved over, renamed or deleted.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsBuiltIn { get; set; }
        /// <summary>Shown in the combo box (with "*" when the active layout differs from it).</summary>
        [JsonIgnore] public string Label { get; set; }

        public override string ToString() { return Label ?? Name; }
    }

    public sealed class AppSettings
    {
        public SourceMode Source { get; set; } = SourceMode.Auto;
        public bool LivePositions { get; set; } = true;
        public string Font { get; set; } = "Bahnschrift";
        public UI.AppTheme Theme { get; set; } = UI.AppTheme.Dark;
        /// <summary>Editor: preview next to the settings (true) or above them (false).</summary>
        public bool PreviewSideBySide { get; set; }
        /// <summary>Dragged widgets snap to the edges of other widgets and of the screen.</summary>
        public bool SnapEnabled { get; set; } = true;
        /// <summary>How close (px) an edge must come before it snaps.</summary>
        public int SnapDistance { get; set; } = 12;
        /// <summary>Space (px) kept between snapped widgets and from the screen edge.</summary>
        public int SnapMargin { get; set; } = 0;
        public bool OverlaysVisible { get; set; } = true;
        public string HotkeyEditMode { get; set; } = "Ctrl+Shift+E";
        public string HotkeyToggleOverlays { get; set; } = "Ctrl+Shift+H";
        public string HotkeyNextLayout { get; set; } = "Ctrl+Shift+L";
        /// <summary>The window's close button hides the app to the system tray (widgets keep running); Exit is in the tray menu.</summary>
        public bool CloseToTray { get; set; } = true;
        /// <summary>The tray tip "still running" was shown once, so it doesn't come back every time.</summary>
        public bool TrayTipShown { get; set; }
        /// <summary>Look for a newer release on GitHub at start and twice a day.</summary>
        public bool CheckForUpdates { get; set; } = true;
        /// <summary>The newest version a tray notification was shown for (each new version is announced once).</summary>
        public string UpdateNotified { get; set; }

        public List<LayoutConfig> Layouts { get; set; } = new List<LayoutConfig>();
        public List<WidgetPreset> Presets { get; set; } = new List<WidgetPreset>();
        public List<LayoutPreset> LayoutPresets { get; set; } = new List<LayoutPreset>();
        public string ActiveLayoutId { get; set; }

        /// <summary>Widgets of the active layout (everything that edits widgets works on this list).</summary>
        [JsonIgnore]
        public List<WidgetSettings> Widgets { get { return ActiveLayout.Widgets; } }


        /// <summary>The Default preset of a widget type (always there after loading).</summary>
        public WidgetPreset DefaultPreset(Type type) { return Presets.FirstOrDefault(p => p.IsDefault && p.Settings != null && p.Settings.GetType() == type); }

        /// <summary>A copy of the Default preset's settings: the defaults of new widgets and of the reset buttons.</summary>
        public WidgetSettings DefaultsFor(Type type)
        {
            var p = DefaultPreset(type);
            return p != null ? CloneWidget(p.Settings) : Core.Defaults.Preset(type);
        }

        /// <summary>A new widget of this type, made from its Default preset, in the centre of the main screen.</summary>
        public WidgetSettings NewWidget(Type type)
        {
            var w = DefaultsFor(type);
            w.Id = Core.Defaults.NewId();
            w.Title = w.TypeName;
            Core.Defaults.Place(w, Anchor.Center, 0, 0);
            var p = DefaultPreset(type);
            w.PresetId = p != null ? p.Id : null;
            return w;
        }

        // ---- comparing (layout work version vs saved, widget vs its preset)

        // a layout and a layout preset are the same when their widgets are (ids differ, X/Y follow from screen and offsets)
        static readonly string[] LayoutSkip = { "Id", "X", "Y", "PresetId" };
        static readonly string[] Instance = { "Id", "Title", "Enabled", "X", "Y", "Screen", "Anchor", "OffsetX", "OffsetY", "Locked", "PresetId" };

        static string CompareJson(IEnumerable<WidgetSettings> widgets, string[] skip)
        {
            var arr = new JsonArray();
            foreach (var w in widgets)
            {
                var o = JsonSerializer.SerializeToNode(w, Json) as JsonObject;
                foreach (var k in skip) o?.Remove(k);
                arr.Add(o);
            }
            return arr.ToJsonString();
        }

        /// <summary>The work version of the layout differs from its saved version.</summary>
        public static bool SameWidgets(IEnumerable<WidgetSettings> a, IEnumerable<WidgetSettings> b)
        {
            return CompareJson(a, LayoutSkip) == CompareJson(b, LayoutSkip);
        }

        public LayoutPreset LayoutPresetOf(LayoutConfig l) { return LayoutPresets.FirstOrDefault(p => p.Id == l.PresetId); }

        /// <summary>The active layout differs from the layout preset it was loaded from / saved to.</summary>
        public bool DiffersFromPreset(LayoutConfig l)
        {
            var p = LayoutPresetOf(l);
            return p != null && !SameWidgets(l.Widgets, p.Widgets);
        }

        /// <summary>Overwrites a layout's widgets with a copy of a layout preset (new widget ids).</summary>
        public void LoadLayoutPreset(LayoutConfig l, LayoutPreset p)
        {
            l.Widgets = CloneList(p.Widgets);
            foreach (var w in l.Widgets) PrepareImported(w);
            l.PresetId = p.Id;
        }

        /// <summary>A layout preset with a copy of a layout's widgets.</summary>
        public static List<WidgetSettings> PresetWidgets(LayoutConfig l) { return CloneList(l.Widgets); }

        public string UniqueLayoutPresetName(string name, LayoutPreset except)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Preset";
            string n = name;
            for (int i = 2; LayoutPresets.Any(p => p != except && string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))
                || string.Equals(n, Core.Defaults.StartLayoutName, StringComparison.OrdinalIgnoreCase) && LayoutPresets.Any(p => p.IsBuiltIn && p != except); i++)
                n = name + " (" + i + ")";
            return n;
        }

        /// <summary>Same widget settings, ignoring name and position (what a preset holds).</summary>
        public static bool SameSettings(WidgetSettings a, WidgetSettings b)
        {
            return a != null && b != null && a.GetType() == b.GetType() && CompareJson(new[] { a }, Instance) == CompareJson(new[] { b }, Instance);
        }

        public static List<WidgetSettings> CloneList(IEnumerable<WidgetSettings> widgets) { return widgets.Select(CloneWidget).ToList(); }

        public static T ParseDefaults<T>(string text) { return JsonSerializer.Deserialize<T>(text, Json); }

        [JsonIgnore]
        public LayoutConfig ActiveLayout
        {
            get
            {
                var l = Layouts.FirstOrDefault(x => x.Id == ActiveLayoutId);
                if (l == null)
                {
                    if (Layouts.Count == 0) Layouts.Add(new LayoutConfig());
                    l = Layouts[0];
                    ActiveLayoutId = l.Id;
                }
                return l;
            }
        }

        /// <summary>Settings files from before layouts existed stored the widgets at the top level.</summary>
        [JsonPropertyName("Widgets"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<WidgetSettings> LegacyWidgets { get; set; }

        /// <summary>The options the settings file is written with (undo compares and restores with them too).</summary>
        public static JsonSerializerOptions JsonOptions { get { return Json; } }
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Other settings folder (--settings-dir), so tests never touch the user's own settings.</summary>
        public static string FolderOverride;

        public static string Folder
        {
            get { return FolderOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BuildInfo.SettingsFolderName); }
        }

        static string FilePath { get { return Path.Combine(Folder, "settings.json"); } }
        public static string SettingsFile { get { return FilePath; } }
        public static string BackupFolder { get { return Path.Combine(Folder, "backups"); } }
        public static string ErrorLog { get { return Path.Combine(Folder, "errors.log"); } }

        /// <summary>App version that last saved this file (an update makes a backup first).</summary>
        public string SavedByVersion { get; set; }

        /// <summary>The user deleted the "Get started" layout: an install or update doesn't put it back.</summary>
        public bool StartLayoutDeclined { get; set; }

        /// <summary>Something the user should know about this start (shown for a while in the status bar), null = nothing.</summary>
        [JsonIgnore] public static string Notice { get; private set; }

        /// <summary>
        /// Widgets and presets this version can't read (e.g. from a newer version, after going back to an older one).
        /// They are kept as they are and put back when a version that knows them loads the file.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonArray Unreadable { get; set; }

        /// <summary>Why the settings file couldn't be read (null = fine); the file is kept in the backups folder.</summary>
        [JsonIgnore] public static string LoadProblem { get; private set; }
        /// <summary>Last save error (null = the last save worked); shown in the status bar.</summary>
        [JsonIgnore] public static string SaveProblem { get; private set; }

        public static AppSettings Load()
        {
            MigrateFromOldName();
            LoadProblem = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = Parse(File.ReadAllText(FilePath));
                    if (s != null) { s.Normalize(); s.EnsureStartLayout(); return s; }
                }
            }
            catch (Exception ex)
            {
                // never silently start over: the file goes to the backups folder (StartupBackup) and the user is told
                LoadProblem = ex.Message;
                LogError("Settings could not be read: " + ex);
            }
            // first start (or an unreadable file): the built-in Default layout and presets
            var d = new AppSettings();
            d.Normalize();
            return d;
        }

        /// <summary>
        /// First start after an install or update (another version saved the file): a missing "Get started" layout
        /// is added again from the built-in preset, so every user has the starting point. An existing one (by name)
        /// is never touched, it may be the user's work version; a renamed one counts as missing. Not when the user
        /// deleted it (<see cref="StartLayoutDeclined"/>). The added layout isn't made active: nothing on screen changes.
        /// </summary>
        internal void EnsureStartLayout()
        {
            if (SavedByVersion == BuildInfo.Version || StartLayoutDeclined) return;
            if (Layouts.Any(l => string.Equals((l.Name ?? "").Trim(), Core.Defaults.StartLayoutName, StringComparison.OrdinalIgnoreCase))) return;
            var start = LayoutPresets.FirstOrDefault(p => p.IsBuiltIn);
            if (start == null) return;
            var layout = new LayoutConfig { Name = Core.Defaults.StartLayoutName, Widgets = CloneList(start.Widgets), PresetId = start.Id };
            foreach (var w in layout.Widgets) w.Id = Core.Defaults.NewId();   // widget ids identify the windows: unique across layouts
            Layouts.Add(layout);
            Notice = "Layout '" + Core.Defaults.StartLayoutName + "' added";
        }

        /// <summary>
        /// Reads the file item by item: a widget or preset this version can't read doesn't take the rest of the
        /// layouts down with it; it is kept in <see cref="Unreadable"/> and saved again as it was.
        /// </summary>
        static AppSettings Parse(string text)
        {
            var root = JsonNode.Parse(text) as JsonObject;
            if (root == null) return null;
            var layouts = root["Layouts"] as JsonArray;
            var presets = root["Presets"] as JsonArray;

            // put back what an earlier start couldn't read; this version may know it
            var earlier = root["Unreadable"] as JsonArray;
            root.Remove("Unreadable");
            var kept = new JsonArray();
            if (earlier != null)
                foreach (var k in earlier.ToList())
                {
                    string where = (string)k?["Where"];
                    var item = k?["Item"]?.DeepClone();
                    if (item == null) continue;
                    // "preset", "lpreset:<layout preset id>" or "<layout id>" ("saved:<id>": older files, dropped)
                    if (where != null && where.StartsWith("saved:")) continue;
                    JsonArray target;
                    if (where == "preset") target = presets;
                    else if (where != null && where.StartsWith("lpreset:"))
                        target = (root["LayoutPresets"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(l => (string)l["Id"] == where.Substring(8))?["Widgets"] as JsonArray;
                    else target = layouts?.OfType<JsonObject>().FirstOrDefault(l => (string)l["Id"] == where)?["Widgets"] as JsonArray;
                    if (target != null) target.Add(item); else kept.Add(k.DeepClone());   // its layout is gone: keep it as it is
                }

            if (layouts != null)
                foreach (var l in layouts.OfType<JsonObject>())
                {
                    SetAside(l["Widgets"] as JsonArray, (string)l["Id"], n => n.Deserialize<WidgetSettings>(Json), kept);

                }
            SetAside(presets, "preset", n => n.Deserialize<WidgetPreset>(Json), kept);
            var lpresets = root["LayoutPresets"] as JsonArray;
            if (lpresets != null)
                foreach (var lp in lpresets.OfType<JsonObject>())
                    SetAside(lp["Widgets"] as JsonArray, "lpreset:" + (string)lp["Id"], n => n.Deserialize<WidgetSettings>(Json), kept);
            SetAside(root["Widgets"] as JsonArray, "legacy", n => n.Deserialize<WidgetSettings>(Json), kept);

            var s = root.Deserialize<AppSettings>(Json);
            if (s != null && kept.Count > 0)
            {
                s.Unreadable = kept;
                LogError(kept.Count + " widget(s) / preset(s) could not be read by this version and are kept in the settings file.");
            }
            return s;
        }

        static void SetAside(JsonArray items, string where, Func<JsonNode, object> read, JsonArray kept)
        {
            if (items == null) return;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                try { if (items[i] != null && read(items[i]) != null) continue; }
                catch { }
                kept.Add(new JsonObject { ["Where"] = where, ["Item"] = items[i]?.DeepClone() });
                items.RemoveAt(i);
            }
        }

        /// <summary>
        /// At app start, before anything can be saved: a copy of the settings file in the backups folder,
        /// once a day (the last 7 days are kept), before the first save by a new version (kept), and of a file
        /// that couldn't be read (kept). So an update, a bug or a test never takes layouts and presets with it.
        /// </summary>
        public void StartupBackup()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                Directory.CreateDirectory(BackupFolder);
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                if (LoadProblem != null) CopyTo(Path.Combine(BackupFolder, "settings-unreadable-" + stamp + ".json"));
                else if (SavedByVersion != BuildInfo.Version) CopyTo(Path.Combine(BackupFolder, "settings-before-" + BuildInfo.Version + "-" + stamp + ".json"));

                string daily = Path.Combine(BackupFolder, "settings-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json");
                if (!File.Exists(daily)) CopyTo(daily);
                foreach (var old in Directory.GetFiles(BackupFolder, "settings-????-??-??.json").OrderByDescending(p => p).Skip(7))
                    File.Delete(old);
            }
            catch (Exception ex) { LogError("Backup failed: " + ex.Message); }
        }

        /// <summary>Copy of the settings file, never read-only (a read-only copy couldn't be cleaned up later).</summary>
        static void CopyTo(string target)
        {
            File.Copy(FilePath, target, true);
            File.SetAttributes(target, FileAttributes.Normal);
        }

        public static void LogError(string message)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                // keep the log small: start over when it gets big
                if (File.Exists(ErrorLog) && new FileInfo(ErrorLog).Length > 256 * 1024) File.Delete(ErrorLog);
                File.AppendAllText(ErrorLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + BuildInfo.Version + "  " + message + Environment.NewLine);
            }
            catch { }
        }
        /// <summary>
        /// First start: DEV builds start from a copy of the release settings; releases carry over the
        /// layout of the old "RaceOverlay" name.
        /// </summary>
        static void MigrateFromOldName()
        {
            if (FolderOverride != null) return;   // a separate (test) folder never takes other settings
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string old = BuildInfo.IsDev ? Path.Combine(appData, "vibeRacingOverlays", "settings.json") : Path.Combine(appData, "RaceOverlay", "settings.json");
                if (File.Exists(FilePath) || !File.Exists(old)) return;
                Directory.CreateDirectory(Folder);
                File.Copy(old, FilePath);
            }
            catch { }
        }

        /// <summary>Writes the file (via a temp file, so a crash never leaves half a file). Errors are logged and shown, then rethrown.</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                SavedByVersion = BuildInfo.Version;
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
                File.Move(tmp, FilePath, true);
                SaveProblem = null;
            }
            catch (Exception ex)
            {
                SaveProblem = ex.Message;
                LogError("Settings could not be saved: " + ex);
                throw;
            }
        }

        void Normalize()
        {
            if (LegacyWidgets != null)
            {
                if (Layouts.Count == 0) Layouts.Add(new LayoutConfig { Name = "Default", Widgets = LegacyWidgets });
                LegacyWidgets = null;
            }
            // the layout preset "Get started" is always there and always the app's built-in layout
            var start = LayoutPresets.FirstOrDefault(p => p.IsBuiltIn);
            if (start == null) { start = new LayoutPreset { IsBuiltIn = true }; LayoutPresets.Insert(0, start); }
            start.Name = Core.Defaults.StartLayoutName;
            start.Widgets = Core.Defaults.StartLayout();
            foreach (var p in LayoutPresets)
            {
                if (p != start) p.IsBuiltIn = false;
                if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Preset";
                p.Widgets = p.Widgets ?? new List<WidgetSettings>();
                foreach (var w in p.Widgets) NormalizeWidget(w);
            }
            // a fresh start: one layout, made from "Get started"
            if (Layouts.Count == 0)
            {
                var first = new LayoutConfig { Name = Core.Defaults.StartLayoutName };
                Layouts.Add(first);
                first.Widgets = CloneList(start.Widgets);
                first.PresetId = start.Id;
            }

            var ids = new HashSet<string>();
            foreach (var l in Layouts)
            {
                if (string.IsNullOrWhiteSpace(l.Name)) l.Name = "Layout";
                foreach (var w in l.Widgets)
                {
                    // widget ids must be unique across layouts (they identify the on-screen windows)
                    if (!ids.Add(w.Id)) { w.Id = Core.Defaults.NewId(); ids.Add(w.Id); }
                    NormalizeWidget(w);
                }
                if (l.PresetId != null && !LayoutPresets.Any(p => p.Id == l.PresetId)) l.PresetId = null;
            }
            var active = ActiveLayout; // repairs an unknown ActiveLayoutId

            Presets.RemoveAll(p => p.Settings == null);
            foreach (var p in Presets) NormalizeWidget(p.Settings);
            // exactly one Default preset per widget type (the one marked, else one called "Default", else the built-in one)
            foreach (var e in vibeRacingOverlays.App.Widgets.Widget.Catalog)
            {
                var type = e.Make().GetType();
                var ofType = Presets.Where(p => p.Settings.GetType() == type).ToList();
                var dp = ofType.FirstOrDefault(p => p.IsDefault) ?? ofType.FirstOrDefault(p => string.Equals(p.Name, "Default", StringComparison.OrdinalIgnoreCase));
                if (dp == null)
                {
                    var fresh = Core.Defaults.Preset(type);
                    NormalizeWidget(fresh);
                    dp = new WidgetPreset { Name = "Default", Settings = fresh };
                    Presets.Add(dp);
                    ofType.Add(dp);
                }
                foreach (var p in ofType) p.IsDefault = p == dp;
                dp.Name = "Default";
                // always the app's built-in settings (also after an update that changed them)
                var builtIn = Core.Defaults.Preset(type);
                NormalizeWidget(builtIn);
                dp.Settings = builtIn;
            }
        }

        /// <summary>
        /// An imported widget joins these settings: a new id (ids are unique across layouts), current columns and
        /// migrations, and its preset only when that exists here (otherwise the Default preset of its type).
        /// </summary>
        public void PrepareImported(WidgetSettings w)
        {
            w.Id = Core.Defaults.NewId();
            NormalizeWidget(w);
            // the widget's own settings are what counts; it follows one of your presets only when that holds exactly
            // these settings (no "*"), otherwise your Default preset (and shows "*": it really differs from it)
            var same = Presets.Where(p => p.Settings != null && SameSettings(w, p.Settings)).OrderBy(p => p.IsDefault ? 0 : 1).FirstOrDefault();
            var d = same ?? DefaultPreset(w.GetType());
            w.PresetId = d != null ? d.Id : null;
        }

        /// <summary>
        /// Adds a preset from a file: a unique name within its type (never "Default": your Default is never replaced),
        /// its own id unless that is taken. Returns the new preset.
        /// </summary>
        /// <summary>
        /// Other widgets (all layouts, work versions) that follow this preset and would change if they got its settings,
        /// for the "also update them?" question when a preset is saved.
        /// </summary>
        public List<WidgetSettings> OthersUsing(WidgetPreset p, WidgetSettings except)
        {
            return Layouts.SelectMany(l => l.Widgets).Where(w => w != except && w.PresetId == p.Id && !SameSettings(w, p.Settings)).ToList();
        }

        public WidgetPreset AddImportedPreset(WidgetPreset p)
        {
            NormalizeWidget(p.Settings);
            p.Settings.PresetId = null;
            p.IsDefault = false;
            if (string.IsNullOrEmpty(p.Id) || Presets.Any(x => x.Id == p.Id)) p.Id = Core.Defaults.NewId();
            p.Name = UniquePresetName(p.Settings.GetType(), string.Equals(p.Name, "Default", StringComparison.OrdinalIgnoreCase) ? "Default (imported)" : p.Name, null);
            Presets.Add(p);
            return p;
        }

        public string UniquePresetName(Type type, string name, WidgetPreset except)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Preset";
            string n = name;
            for (int i = 2; Presets.Any(p => p != except && p.Settings != null && p.Settings.GetType() == type && string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)); i++)
                n = name + " (" + i + ")";
            return n;
        }


        static void NormalizeWidget(WidgetSettings w)
        {
            var table = w as Widgets.ITableSettings;
            if (table != null) table.MergeColumns();
            var norm = w as Widgets.INormalizable;
            if (norm != null) norm.Normalize();
            if (string.IsNullOrEmpty(w.Title)) w.Title = w.TypeName;
        }
        /// <summary>Deep copy of widget settings (via JSON, so nothing is shared between widgets, presets or layouts).</summary>
        public static WidgetSettings CloneWidget(WidgetSettings w)
        {
            return JsonSerializer.Deserialize<WidgetSettings>(JsonSerializer.Serialize(w, Json), Json);
        }

        /// <summary>Widget settings from their JSON (as written by ToJson(w, typeof(WidgetSettings))).</summary>
        public static WidgetSettings ParseWidget(string json) { return JsonSerializer.Deserialize<WidgetSettings>(json, Json); }

        /// <summary>Deep copy of any settings value (for complex values like a session profile).</summary>
        public static object CloneValue(object v, Type type)
        {
            return v == null ? null : JsonSerializer.Deserialize(JsonSerializer.Serialize(v, type, Json), type, Json);
        }

        /// <summary>JSON text of a settings value, to compare complex values.</summary>
        public static string ToJson(object v, Type type) { return JsonSerializer.Serialize(v, type, Json); }

    }
}
