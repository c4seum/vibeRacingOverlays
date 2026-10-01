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

        [JsonIgnore] public abstract string TypeName { get; }
    }

    /// <summary>Saved settings of one widget type, reusable in every layout.</summary>
    public sealed class WidgetPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "";
        public WidgetSettings Settings { get; set; }

        [JsonIgnore] public string TypeName { get { return Settings != null ? Settings.TypeName : ""; } }
        public override string ToString() { return Name; }   // shown by the preset combo box
    }

    /// <summary>A named set of widgets (positions, scale and all widget settings).</summary>
    public sealed class LayoutConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "Default";
        public List<WidgetSettings> Widgets { get; set; } = new List<WidgetSettings>();

        public override string ToString() { return Name; }   // shown by the layout combo box
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

        public List<LayoutConfig> Layouts { get; set; } = new List<LayoutConfig>();
        public List<WidgetPreset> Presets { get; set; } = new List<WidgetPreset>();
        public string ActiveLayoutId { get; set; }

        /// <summary>Widgets of the active layout (everything that edits widgets works on this list).</summary>
        [JsonIgnore]
        public List<WidgetSettings> Widgets { get { return ActiveLayout.Widgets; } }

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
        public static string BackupFolder { get { return Path.Combine(Folder, "backups"); } }
        public static string ErrorLog { get { return Path.Combine(Folder, "errors.log"); } }

        /// <summary>App version that last saved this file (an update makes a backup first).</summary>
        public string SavedByVersion { get; set; }

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
                    if (s != null) { s.Normalize(); return s; }
                }
            }
            catch (Exception ex)
            {
                // never silently start over: the file goes to the backups folder (StartupBackup) and the user is told
                LoadProblem = ex.Message;
                LogError("Settings could not be read: " + ex);
            }
            var d = new AppSettings();
            d.Layouts.Add(new LayoutConfig { Name = "Default" });
            d.Widgets.Add(new Widgets.StandingsSettings { X = 40, Y = 200 });
            d.Widgets.Add(new Widgets.RelativeSettings { X = 1100, Y = 600 });
            d.Widgets.Add(new Widgets.FuelSettings { X = 1500, Y = 820 });
            d.Normalize();
            return d;
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
                    JsonArray target = where == "preset" ? presets
                        : layouts?.OfType<JsonObject>().FirstOrDefault(l => (string)l["Id"] == where)?["Widgets"] as JsonArray;
                    if (target != null) target.Add(item); else kept.Add(k.DeepClone());   // its layout is gone: keep it as it is
                }

            if (layouts != null)
                foreach (var l in layouts.OfType<JsonObject>())
                    SetAside(l["Widgets"] as JsonArray, (string)l["Id"], n => n.Deserialize<WidgetSettings>(Json), kept);
            SetAside(presets, "preset", n => n.Deserialize<WidgetPreset>(Json), kept);
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
            if (Layouts.Count == 0) Layouts.Add(new LayoutConfig { Name = "Default" });
            var ids = new HashSet<string>();
            foreach (var l in Layouts)
            {
                if (string.IsNullOrWhiteSpace(l.Name)) l.Name = "Layout";
                foreach (var w in l.Widgets)
                {
                    // widget ids must be unique across layouts (they identify the on-screen windows)
                    if (!ids.Add(w.Id)) { w.Id = Guid.NewGuid().ToString("N").Substring(0, 8); ids.Add(w.Id); }
                    var table = w as Widgets.ITableSettings;
                    if (table != null) table.MergeColumns();
                    var norm = w as Widgets.INormalizable;
                    if (norm != null) norm.Normalize();
                    if (string.IsNullOrEmpty(w.Title)) w.Title = w.TypeName;
                }
            }
            var active = ActiveLayout; // repairs an unknown ActiveLayoutId
            Presets.RemoveAll(p => p.Settings == null);
            foreach (var p in Presets)
            {
                var table = p.Settings as Widgets.ITableSettings;
                if (table != null) table.MergeColumns();
                var norm = p.Settings as Widgets.INormalizable;
                if (norm != null) norm.Normalize();
            }
        }

        /// <summary>Deep copy of widget settings (via JSON, so nothing is shared between widgets, presets or layouts).</summary>
        public static WidgetSettings CloneWidget(WidgetSettings w)
        {
            return JsonSerializer.Deserialize<WidgetSettings>(JsonSerializer.Serialize(w, Json), Json);
        }

        /// <summary>Deep copy of any settings value (for complex values like a session profile).</summary>
        public static object CloneValue(object v, Type type)
        {
            return v == null ? null : JsonSerializer.Deserialize(JsonSerializer.Serialize(v, type, Json), type, Json);
        }

        /// <summary>JSON text of a settings value, to compare complex values.</summary>
        public static string ToJson(object v, Type type) { return JsonSerializer.Serialize(v, type, Json); }

        /// <summary>Deep copy of a layout with fresh widget ids.</summary>
        public LayoutConfig CloneLayout(LayoutConfig source, string name)
        {
            var copy = JsonSerializer.Deserialize<LayoutConfig>(JsonSerializer.Serialize(source, Json), Json);
            copy.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            copy.Name = name;
            foreach (var w in copy.Widgets) w.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            return copy;
        }

        /// <summary>A layout with one widget of each type at sensible default positions.</summary>
        public static LayoutConfig DefaultLayout(string name)
        {
            var l = new LayoutConfig { Name = name };
            l.Widgets.Add(new Widgets.StandingsSettings { X = 40, Y = 200 });
            l.Widgets.Add(new Widgets.RelativeSettings { X = 1100, Y = 600 });
            l.Widgets.Add(new Widgets.FuelSettings { X = 1500, Y = 820 });
            return l;
        }
    }
}
