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
        public const string LayoutFormat = "vibeRacingOverlays.layout", WidgetFormat = "vibeRacingOverlays.widget";
        public const string LayoutFilter = "vibeRacingOverlays layout (*.vrolayout.json)|*.vrolayout.json|JSON files (*.json)|*.json";
        public const string WidgetFilter = "vibeRacingOverlays widget (*.vrowidget.json)|*.vrowidget.json|JSON files (*.json)|*.json";

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
