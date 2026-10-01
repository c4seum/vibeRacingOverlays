using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace vibeRacingOverlays.App.Core
{
    /// <summary>
    /// The app's built-in defaults: the Default layout and the Default preset of every widget type.
    /// They come from defaults.json (built into the app) and fall back to the code defaults of the settings classes.
    /// In a DEV build, saving the Default layout or a Default preset writes them back to defaults.json in the
    /// source folder, so the next build (and, once committed, every release) ships them.
    /// </summary>
    public static class Defaults
    {
        sealed class File
        {
            public List<WidgetSettings> Layout { get; set; }
            public List<WidgetSettings> Presets { get; set; }
        }

        static File builtIn;

        static File BuiltIn
        {
            get
            {
                if (builtIn != null) return builtIn;
                File f = null;
                try
                {
                    using (var s = typeof(Defaults).Assembly.GetManifestResourceStream("defaults.json"))
                        if (s != null) using (var r = new StreamReader(s)) f = AppSettings.ParseDefaults<File>(r.ReadToEnd());
                }
                catch (Exception ex) { AppSettings.LogError("Built-in defaults could not be read, using the code defaults: " + ex.Message); }
                if (f == null) f = new File();
                if (f.Layout == null || f.Layout.Count == 0) f.Layout = CodeLayout();
                if (f.Presets == null) f.Presets = new List<WidgetSettings>();
                foreach (var e in Widgets.Widget.Catalog)
                {
                    var made = e.Make();
                    if (!f.Presets.Any(p => p.GetType() == made.GetType())) f.Presets.Add(made);
                }
                builtIn = f;
                return f;
            }
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

        /// <summary>defaults.json in the source folder (DEV builds only; null for releases or when it's not there).</summary>
        public static string SourceFile
        {
            get
            {
                if (!BuildInfo.IsDev) return null;
                var a = typeof(Defaults).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(x => x.Key == "DefaultsSource");
                return a != null && Directory.Exists(Path.GetDirectoryName(a.Value)) ? a.Value : null;
            }
        }

        /// <summary>
        /// DEV builds: writes the user's Default layout (saved version) and Default presets to defaults.json in the
        /// source folder, so they become the built-in defaults of the next build. Returns the file, or null.
        /// </summary>
        public static string Export(AppSettings s)
        {
            string target = SourceFile;
            if (target == null) return null;
            try
            {
                var layout = s.DefaultLayout;
                var f = new File
                {
                    Layout = (layout.Saved ?? layout.Widgets).Select(AppSettings.CloneWidget).ToList(),
                    Presets = s.Presets.Where(p => p.IsDefault && p.Settings != null).Select(p => AppSettings.CloneWidget(p.Settings)).ToList(),
                };
                foreach (var w in f.Presets) { w.PresetId = null; w.Title = w.TypeName; }
                System.IO.File.WriteAllText(target, AppSettings.ToJson(f, typeof(File)));
                builtIn = null;   // the factory defaults follow right away
                return target;
            }
            catch (Exception ex)
            {
                AppSettings.LogError("Defaults could not be written to " + target + ": " + ex.Message);
                return null;
            }
        }
    }
}
