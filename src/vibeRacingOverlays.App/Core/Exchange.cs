using System.IO;
using System.Text.Json.Nodes;

namespace vibeRacingOverlays.App.Core
{
    /// <summary>
    /// Export / import of a whole layout (all widgets with their options) or a single widget, as JSON files
    /// to share or to move to another PC. Importing is tolerant: widget types this version doesn't know are skipped.
    /// </summary>
    public static class Exchange
    {
        public const string LayoutFormat = "vibeRacingOverlays.layout", WidgetFormat = "vibeRacingOverlays.widget", PresetFormat = "vibeRacingOverlays.preset";
        public const string LayoutFilter = "vibeRacingOverlays layout (*.vrolayout.json)|*.vrolayout.json|JSON files (*.json)|*.json";
        public const string WidgetFilter = "vibeRacingOverlays widget (*.vrowidget.json)|*.vrowidget.json|JSON files (*.json)|*.json";
        public const string PresetFilter = "vibeRacingOverlays preset (*.vropreset.json)|*.vropreset.json|vibeRacingOverlays widget (*.vrowidget.json)|*.vrowidget.json|JSON files (*.json)|*.json";

        /// <summary>Where exports go by default (and the preset library lives): Documents\vRO, or inside a test folder.</summary>
        public static string LibraryFolder
        {
            get
            {
                return AppSettings.FolderOverride != null ? Path.Combine(AppSettings.FolderOverride, "library")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "vRO");
            }
        }

        sealed class PresetFile
        {
            public string Format { get; set; } = PresetFormat;
            public string AppVersion { get; set; } = BuildInfo.Version;
            public string Id { get; set; }
            public string Name { get; set; }
            public WidgetSettings Widget { get; set; }
        }

        /// <summary>A preset as a file: its name and settings (no position; that belongs to the widget in a layout).</summary>
        public static string PresetJson(WidgetPreset p)
        {
            var w = AppSettings.CloneWidget(p.Settings);
            w.PresetId = null;
            return AppSettings.ToJson(new PresetFile { Id = p.Id, Name = p.Name, Widget = w }, typeof(PresetFile));
        }

        public static void ExportPreset(WidgetPreset p, string path) { File.WriteAllText(path, PresetJson(p)); }

        /// <summary>What a file holds: "layout", "preset" (also an exported widget) or null.</summary>
        public static string Kind(string path)
        {
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                string f = (string)root?["Format"];
                return f == LayoutFormat ? "layout" : f == PresetFormat || f == WidgetFormat ? "preset" : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Reads a preset file (or an exported widget, named after its file): id (may be null), name and settings.
        /// Null when the widget type is unknown to this version; throws when it isn't a preset or widget file.
        /// </summary>
        public static WidgetPreset ReadPreset(string path)
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            string format = (string)root?["Format"];
            if (format != PresetFormat && format != WidgetFormat) throw new InvalidDataException("This is not a vibeRacingOverlays preset or widget file.");
            WidgetSettings w = null;
            try { w = AppSettings.ParseDefaults<WidgetSettings>(root["Widget"]?.ToJsonString() ?? "null"); } catch { }
            if (w == null) return null;
            string name = format == PresetFormat ? (string)root["Name"] : null;
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(path).Split('.')[0];
            return new WidgetPreset { Id = format == PresetFormat ? (string)root["Id"] : null, Name = name.Trim(), Settings = w };
        }

        sealed class LayoutFile
        {
            public string Format { get; set; } = LayoutFormat;
            public string AppVersion { get; set; } = BuildInfo.Version;
            public string Name { get; set; }
            public List<WidgetSettings> Widgets { get; set; }
        }

        sealed class WidgetFile
        {
            public string Format { get; set; } = WidgetFormat;
            public string AppVersion { get; set; } = BuildInfo.Version;
            public WidgetSettings Widget { get; set; }
        }

        /// <summary>The layout as you see it now (its work version, so unsaved changes are included).</summary>
        public static void ExportLayout(LayoutConfig l, string path)
        {
            var f = new LayoutFile { Name = l.Name, Widgets = AppSettings.CloneList(l.Widgets) };
            File.WriteAllText(path, AppSettings.ToJson(f, typeof(LayoutFile)));
        }

        public static void ExportWidget(WidgetSettings w, string path)
        {
            File.WriteAllText(path, AppSettings.ToJson(new WidgetFile { Widget = AppSettings.CloneWidget(w) }, typeof(WidgetFile)));
        }

        /// <summary>
        /// Reads a layout file as a new layout (not yet added). <paramref name="skipped"/> = widgets this version
        /// can't read. Also accepts a widget file (a layout with that one widget). Throws on a file that isn't one.
        /// </summary>
        public static LayoutConfig ImportLayout(string path, AppSettings s, out int skipped)
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            string format = (string)root?["Format"];
            JsonArray items;
            string name = (string)root?["Name"];
            if (format == LayoutFormat) items = root["Widgets"] as JsonArray;
            else if (format == WidgetFormat) items = new JsonArray(root["Widget"]?.DeepClone());
            else throw new InvalidDataException("This is not a vibeRacingOverlays layout or widget file.");
            var widgets = ReadWidgets(items, s, out skipped);
            var l = new LayoutConfig { Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name, Widgets = widgets };
            l.Saved = AppSettings.CloneList(l.Widgets);
            return l;
        }

        /// <summary>Reads the widgets of a widget file (or of a layout file, all of them). Throws on a file that isn't one.</summary>
        public static List<WidgetSettings> ImportWidgets(string path, AppSettings s, out int skipped)
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            string format = (string)root?["Format"];
            JsonArray items;
            if (format == WidgetFormat) items = new JsonArray(root["Widget"]?.DeepClone());
            else if (format == LayoutFormat) items = root["Widgets"] as JsonArray;
            else throw new InvalidDataException("This is not a vibeRacingOverlays widget or layout file.");
            return ReadWidgets(items, s, out skipped);
        }

        static List<WidgetSettings> ReadWidgets(JsonArray items, AppSettings s, out int skipped)
        {
            var list = new List<WidgetSettings>();
            skipped = 0;
            if (items == null) return list;
            foreach (var n in items)
            {
                WidgetSettings w = null;
                try { w = n != null ? AppSettings.ParseDefaults<WidgetSettings>(n.ToJsonString()) : null; } catch { }
                if (w == null) { skipped++; continue; }
                s.PrepareImported(w);
                list.Add(w);
            }
            return list;
        }

        /// <summary>A file name without characters Windows doesn't allow.</summary>
        public static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "export" : name.Trim();
        }
    }
}
