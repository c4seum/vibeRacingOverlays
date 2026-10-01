using System.IO;
using System.Text.Json.Nodes;
using vibeRacingOverlays.App.Overlay;

namespace vibeRacingOverlays.App.Core
{
    /// <summary>
    /// The app's built-in defaults. The Default preset of every widget type is a preset file in the repo's
    /// defaults\presets folder, built into the app (a type without a file uses its code defaults). A fresh install
    /// starts with the layout "Get started": those presets at fixed places on the main screen. Every new widget
    /// starts in the centre of the main screen.
    /// </summary>
    public static class Defaults
    {
        public const string StartLayoutName = "Get started";

        static List<WidgetSettings> presets;

        static List<WidgetSettings> BuiltInPresets
        {
            get
            {
                if (presets != null) return presets;
                var list = new List<WidgetSettings>();
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
                        if (format != Exchange.PresetFormat && format != Exchange.WidgetFormat) continue;
                        var w = AppSettings.ParseDefaults<WidgetSettings>(root["Widget"]?.ToJsonString() ?? "null");
                        if (w != null && !list.Any(x => x.GetType() == w.GetType())) list.Add(w);
                    }
                    catch (Exception ex) { AppSettings.LogError("Built-in default " + name + " could not be read: " + ex.Message); }
                }
                foreach (var e in Widgets.Widget.Catalog)
                    if (!list.Any(p => p.GetType() == e.Make().GetType())) list.Add(e.Make());
                foreach (var w in list) { w.PresetId = null; w.Title = w.TypeName; }
                presets = list;
                return list;
            }
        }

        /// <summary>A fresh copy of the built-in Default preset of a widget type.</summary>
        public static WidgetSettings Preset(Type type)
        {
            var p = BuiltInPresets.FirstOrDefault(x => x.GetType() == type);
            return p != null ? AppSettings.CloneWidget(p) : (WidgetSettings)Activator.CreateInstance(type);
        }

        /// <summary>The main screen of this PC (by its label, like "Middle" or "Main"), so new widgets land there.</summary>
        public static string MainScreen()
        {
            try
            {
                var m = Placement.Monitors();
                var main = m.FirstOrDefault(x => x.Primary) ?? m.FirstOrDefault();
                return main != null ? main.Label : null;
            }
            catch { return null; }
        }

        /// <summary>Puts a widget on the main screen at an anchor with offsets.</summary>
        public static void Place(WidgetSettings w, Anchor anchor, double offsetX, double offsetY)
        {
            w.Screen = MainScreen();
            w.Anchor = anchor;
            w.OffsetX = offsetX;
            w.OffsetY = offsetY;
        }

        /// <summary>The widgets of the "Get started" layout: Standings top left, Relative bottom right, Fuel calculator bottom left.</summary>
        public static List<WidgetSettings> StartLayout()
        {
            var list = new List<WidgetSettings>();
            Action<Type, Anchor> add = (t, a) =>
            {
                var w = Preset(t);
                w.Id = NewId();
                Place(w, a, 10, 10);
                list.Add(w);
            };
            add(typeof(Widgets.StandingsSettings), Anchor.TopLeft);
            add(typeof(Widgets.RelativeSettings), Anchor.BottomRight);
            add(typeof(Widgets.FuelSettings), Anchor.BottomLeft);
            return list;
        }

        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 8); }
    }
}
