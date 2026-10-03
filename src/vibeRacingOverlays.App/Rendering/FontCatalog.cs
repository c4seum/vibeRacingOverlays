using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace vibeRacingOverlays.App.Rendering
{
    /// <summary>The fonts a widget can use (Style > Font). Bahnschrift is the original look.</summary>
    public enum WidgetFont
    {
        Bahnschrift,
        Rajdhani,
        [Description("Titillium Web")] TitilliumWeb,
        [Description("Chakra Petch")] ChakraPetch,
        [Description("Exo 2")] Exo2,
        Oxanium,
        Orbitron,
        [Description("JetBrains Mono")] JetBrainsMono,
    }

    /// <summary>
    /// How heavy a widget's text is. WPF draws semibold heavier than a browser does: Regular in the widget looks like the
    /// browser's semibold (the font picker the user compared with).
    /// </summary>
    public enum TextWeight { Regular, [Description("Semibold")] SemiBold }

    /// <summary>
    /// Where each font comes from. Bahnschrift is part of Windows; the others are open-license fonts (SIL OFL)
    /// bundled in Fonts\ with their licence, so they look the same on every PC. Widgets draw in two weights:
    /// normal and semibold ("bold" in the display list).
    /// </summary>
    public static class FontCatalog
    {
        sealed class Entry
        {
            public string Family;          // family name inside the font files (or the installed font)
            public bool Bundled;
            public string BoldFamily;      // when the semibold file is its own family for WPF
            public FontWeight BoldWeight = FontWeights.SemiBold;
        }

        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
        {
            { "Bahnschrift", new Entry { Family = "Bahnschrift" } },
            { "Rajdhani", new Entry { Family = "Rajdhani", Bundled = true } },
            { "Titillium Web", new Entry { Family = "Titillium Web", Bundled = true } },
            { "Chakra Petch", new Entry { Family = "Chakra Petch", Bundled = true } },
            { "Exo 2", new Entry { Family = "Exo 2", Bundled = true } },
            { "Oxanium", new Entry { Family = "Oxanium", Bundled = true } },
            { "Orbitron", new Entry { Family = "Orbitron", Bundled = true } },
            // JetBrains' semibold file names itself "JetBrains Mono SemiBold"; asking "JetBrains Mono" for SemiBold
            // gives a fake bold made from the regular file
            { "JetBrains Mono", new Entry { Family = "JetBrains Mono", Bundled = true, BoldFamily = "JetBrains Mono SemiBold", BoldWeight = FontWeights.Normal } },
        };

        static readonly Uri PackRoot = new Uri("pack://application:,,,/");
        static readonly Dictionary<string, FontFamily> families = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);

        const string RegularSuffix = "|regular";

        /// <summary>The font key of a widget: its font, plus "|regular" when its text isn't semibold (display lists, renderer, measuring).</summary>
        public static string Key(WidgetFont f, TextWeight w) { return Name(f) + (w == TextWeight.Regular ? RegularSuffix : ""); }

        /// <summary>The name used in display lists and the renderer for a widget's font.</summary>
        public static string Name(WidgetFont f)
        {
            var field = typeof(WidgetFont).GetField(f.ToString());
            var d = field != null ? (DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) : null;
            return d != null ? d.Description : f.ToString();
        }

        static FontFamily Family(string name, bool bundled)
        {
            lock (families)
            {
                FontFamily f;
                if (!families.TryGetValue(name, out f))
                    families[name] = f = bundled ? new FontFamily(PackRoot, "./Fonts/#" + name) : new FontFamily(name);
                return f;
            }
        }

        /// <summary>The typeface for a font name (an installed font when it isn't one of ours).</summary>
        public static Typeface Face(string name, bool bold)
        {
            if (name != null && name.EndsWith(RegularSuffix)) { name = name.Substring(0, name.Length - RegularSuffix.Length); bold = false; }
            if (string.IsNullOrWhiteSpace(name)) name = "Bahnschrift";
            Entry e;
            if (!entries.TryGetValue(name, out e)) e = new Entry { Family = name };
            var family = Family(bold && e.BoldFamily != null ? e.BoldFamily : e.Family, e.Bundled);
            return new Typeface(family, FontStyles.Normal, bold ? e.BoldWeight : FontWeights.Normal, FontStretches.Normal);
        }
    }
}
