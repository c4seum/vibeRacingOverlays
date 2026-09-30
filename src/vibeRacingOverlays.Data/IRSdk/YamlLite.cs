using System.Globalization;

namespace vibeRacingOverlays.Data.IRSdk
{
    /// <summary>
    /// Minimal, lenient parser for iRacing's session YAML.
    /// iRacing doesn't escape free-form values (driver/team names may contain ": ", quotes, #...),
    /// which trips up strict YAML parsers, so we only split on the first ": " of a line.
    /// Result: Dictionary&lt;string, object&gt; / List&lt;object&gt; / string.
    /// </summary>
    public static class YamlLite
    {
        sealed class Line
        {
            public int KeyIndent;     // column where the key (or scalar) starts
            public bool ListStart;    // line starts with "- "
            public int DashIndent;
            public string Key;        // null for a scalar list item
            public string Value;      // null when the value is on the following lines
        }

        public static Dictionary<string, object> Parse(string yaml)
        {
            var lines = Tokenize(yaml);
            int pos = 0;
            if (lines.Count == 0) return new Dictionary<string, object>();
            var root = ParseMap(lines, ref pos, lines[0].KeyIndent, false);
            return root;
        }

        static List<Line> Tokenize(string yaml)
        {
            var res = new List<Line>();
            foreach (var rawLine in yaml.Split('\n'))
            {
                string s = rawLine.TrimEnd('\r', ' ', '\t');
                if (s.Length == 0 || s == "---" || s == "...") continue;
                int indent = 0;
                while (indent < s.Length && s[indent] == ' ') indent++;
                if (indent >= s.Length || s[indent] == '#') continue;

                var l = new Line();
                int k = indent;
                if (s[k] == '-' && (k + 1 == s.Length || s[k + 1] == ' '))
                {
                    l.ListStart = true;
                    l.DashIndent = indent;
                    k++;
                    while (k < s.Length && s[k] == ' ') k++;
                }
                l.KeyIndent = k;
                string body = s.Substring(k);
                int colon = body.IndexOf(": ", StringComparison.Ordinal);
                if (colon > 0)
                {
                    l.Key = body.Substring(0, colon).Trim();
                    l.Value = Unquote(body.Substring(colon + 2).Trim());
                }
                else if (body.EndsWith(":"))
                {
                    l.Key = body.Substring(0, body.Length - 1).Trim();
                    l.Value = null;
                }
                else
                {
                    l.Key = null;
                    l.Value = Unquote(body.Trim());
                }
                res.Add(l);
            }
            return res;
        }

        static string Unquote(string v)
        {
            if (v.Length >= 2 && ((v[0] == '"' && v[v.Length - 1] == '"') || (v[0] == '\'' && v[v.Length - 1] == '\'')))
                return v.Substring(1, v.Length - 2);
            return v;
        }

        static Dictionary<string, object> ParseMap(List<Line> lines, ref int pos, int indent, bool firstIsListStart)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            bool first = true;
            while (pos < lines.Count)
            {
                var l = lines[pos];
                if (l.KeyIndent < indent) break;
                if (l.KeyIndent > indent) { pos++; continue; } // stray line, skip
                if (l.ListStart && !(first && firstIsListStart)) break;
                first = false;
                pos++;
                if (l.Key == null) continue;

                if (l.Value != null) { map[l.Key] = l.Value; continue; }
                map[l.Key] = ParseChild(lines, ref pos, indent);
            }
            return map;
        }

        static object ParseChild(List<Line> lines, ref int pos, int parentIndent)
        {
            if (pos >= lines.Count) return null;
            var next = lines[pos];
            if (next.ListStart && next.DashIndent >= parentIndent)
                return ParseList(lines, ref pos, next.KeyIndent);
            if (next.KeyIndent > parentIndent)
                return ParseMap(lines, ref pos, next.KeyIndent, false);
            return null;
        }

        static List<object> ParseList(List<Line> lines, ref int pos, int indent)
        {
            var list = new List<object>();
            while (pos < lines.Count)
            {
                var l = lines[pos];
                if (!l.ListStart || l.KeyIndent != indent) break;
                if (l.Key == null) { list.Add(l.Value); pos++; continue; }
                list.Add(ParseMap(lines, ref pos, indent, true));
            }
            return list;
        }

        // ------------------------------------------------------------------ access helpers

        public static object Path(object node, string path)
        {
            foreach (var part in path.Split('.'))
            {
                var map = node as Dictionary<string, object>;
                if (map == null || !map.TryGetValue(part, out node)) return null;
            }
            return node;
        }

        public static string Str(object node, string path, string def = "")
        {
            var v = Path(node, path) as string;
            return v ?? def;
        }

        /// <summary>Parses the leading number of values like "4.01 km", "1800.0000 sec", "0xffda59".</summary>
        public static double Num(object node, string path, double def = 0)
        {
            return ParseNumber(Path(node, path) as string, def);
        }

        public static int Int(object node, string path, int def = 0)
        {
            return (int)Math.Round(Num(node, path, def));
        }

        public static double ParseNumber(string s, double def = 0)
        {
            if (string.IsNullOrEmpty(s)) return def;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                long hex;
                return long.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hex) ? hex : def;
            }
            int sp = s.IndexOf(' ');
            if (sp > 0) s = s.Substring(0, sp);
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : def;
        }

        public static List<object> List(object node, string path)
        {
            return Path(node, path) as List<object> ?? new List<object>();
        }
    }
}
