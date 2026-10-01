using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;

namespace vibeRacingOverlays.App.Core
{
    /// <summary>
    /// The app's built-in defaults: the Default layout and the Default preset of every widget type.
    /// They are the export files in the repo's defaults folder (layouts\Default.vrolayout.json and
    /// widgets\&lt;type&gt;.vrowidget.json), built into the app. A widget type without a widget file takes its widget
    /// from the Default layout, then the code defaults. In a DEV build, saving the Default layout or a Default preset
    /// writes these files back, so the next build (and, once committed, every release) ships them.
    /// </summary>
    public static class Defaults
    {
        sealed class Set
        {
            public List<WidgetSettings> Layout = new List<WidgetSettings>();
            public List<WidgetSettings> Presets = new List<WidgetSettings>();
        }

        static Set builtIn;

        static Set BuiltIn
        {
            get
            {
                if (builtIn != null) return builtIn;
                var set = new Set();
                var asm = typeof(Defaults).Assembly;
                foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("defaults/")))
                {
                    try
                    {
                        string text;
                        using (var s = asm.GetManifestResourceStream(name))
                        using (var r = new StreamReader(s)) text = r.ReadToEnd();
                        var root = JsonNode.Parse(text) as JsonObject;
                        string format = (string)root?["Format"];
                        if (format == Exchange.LayoutFormat && Path.GetFileName(name).StartsWith("Default.", StringComparison.OrdinalIgnoreCase))
                            set.Layout = Read(root["Widgets"] as JsonArray);
                        else if (format == Exchange.WidgetFormat)
                            set.Presets.AddRange(Read(new JsonArray(root["Widget"]?.DeepClone())));
                    }
                    catch (Exception ex) { AppSettings.LogError("Built-in default " + name + " could not be read: " + ex.Message); }
                }
                if (set.Layout.Count == 0) set.Layout = CodeLayout();
                foreach (var e in Widgets.Widget.Catalog)
                {
                    var type = e.Make().GetType();
                    if (set.Presets.Any(p => p.GetType() == type)) continue;
                    var fromLayout = set.Layout.FirstOrDefault(w => w.GetType() == type);
                    set.Presets.Add(fromLayout != null ? AppSettings.CloneWidget(fromLayout) : e.Make());
                }
                // defaults belong to nobody's presets: widgets follow the Default preset of their type
                foreach (var w in set.Layout.Concat(set.Presets)) w.PresetId = null;
                foreach (var w in set.Presets) w.Title = w.TypeName;
                builtIn = set;
                return set;
            }
        }

        static List<WidgetSettings> Read(JsonArray items)
        {
            var list = new List<WidgetSettings>();
            if (items == null) return list;
            foreach (var n in items)
            {
                try { var w = n != null ? AppSettings.ParseDefaults<WidgetSettings>(n.ToJsonString()) : null; if (w != null) list.Add(w); }
                catch { }   // a type this version doesn't know
            }
            return list;
        }

        static List<WidgetSettings> CodeLayout()
        {
            return new List<WidgetSettings>
            {
                new Widgets.StandingsSettings { X = 40, Y = 200 },
                new Widgets.RelativeSettings { X = 1100, Y = 600 },
                new Widgets.FuelSettings { X = 1500, Y = 820 },
            };
        }

        /// <summary>A fresh copy of the built-in Default layout's widgets (new ids).</summary>
        public static List<WidgetSettings> Layout()
        {
            return BuiltIn.Layout.Select(w => { var c = AppSettings.CloneWidget(w); c.Id = NewId(); return c; }).ToList();
        }

        /// <summary>A fresh copy of the built-in Default preset of a widget type.</summary>
        public static WidgetSettings Preset(Type type)
        {
            var p = BuiltIn.Presets.FirstOrDefault(x => x.GetType() == type);
            return p != null ? AppSettings.CloneWidget(p) : (WidgetSettings)Activator.CreateInstance(type);
        }

        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 8); }

        /// <summary>The repo's defaults folder (DEV builds only; null for releases or when it's not there).</summary>
        public static string SourceFolder
        {
            get
            {
                if (!BuildInfo.IsDev) return null;
                var a = typeof(Defaults).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(x => x.Key == "DefaultsSource");
                return a != null && Directory.Exists(a.Value) ? a.Value : null;
            }
        }

        /// <summary>
        /// DEV builds: writes the user's Default layout (saved version) and Default presets into the repo's defaults
        /// folder, so they become the built-in defaults of the next build. Returns the folder, or null.
        /// </summary>
        public static string Export(AppSettings s)
        {
            string folder = SourceFolder;
            if (folder == null) return null;
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "layouts"));
                Directory.CreateDirectory(Path.Combine(folder, "widgets"));
                var layout = s.DefaultLayout;
                var copy = new LayoutConfig { Name = "Default", Widgets = AppSettings.CloneList(layout.Saved ?? layout.Widgets) };
                Exchange.ExportLayout(copy, Path.Combine(folder, "layouts", "Default.vrolayout.json"));
                foreach (var p in s.Presets.Where(p => p.IsDefault && p.Settings != null))
                {
                    var w = AppSettings.CloneWidget(p.Settings);
                    w.Title = w.TypeName;
                    Exchange.ExportWidget(w, Path.Combine(folder, "widgets", Exchange.SafeName(w.TypeName) + ".vrowidget.json"));
                }
                builtIn = null;   // the factory defaults follow right away
                return folder;
            }
            catch (Exception ex)
            {
                AppSettings.LogError("Defaults could not be written to " + folder + ": " + ex.Message);
                return null;
            }
        }
    }
}
