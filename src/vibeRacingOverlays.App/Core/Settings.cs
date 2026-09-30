using System.IO;
using System.Text.Json;
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

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BuildInfo.SettingsFolderName); }
        }

        static string FilePath { get { return Path.Combine(Folder, "settings.json"); } }

        public static AppSettings Load()
        {
            MigrateFromOldName();
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                    if (s != null) { s.Normalize(); return s; }
                }
            }
            catch (Exception ex)
            {
                // keep a copy of the broken file instead of silently losing the layout
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
                System.Diagnostics.Debug.WriteLine("Settings load failed: " + ex.Message);
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

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
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
