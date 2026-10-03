using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Rendering;
using vibeRacingOverlays.Data.Model;

namespace vibeRacingOverlays.App.Widgets
{
    /// <summary>
    /// Header bar items shared by the widgets (standings, relative): the same items, formats and texts everywhere.
    /// Each widget picks the items it offers and their defaults; the user sets order, on/off and format.
    /// </summary>
    public static class HeaderBar
    {
        static readonly (string, string)[] TempFormats = { ("C1", "32.4°C"), ("C0", "32°C"), ("F1", "90.3°F"), ("F0", "90°F"), ("CF", "32.4°C 90.3°F"), ("FC", "90.3°F 32.4°C") };
        static readonly (string, string)[] RatingFormats = { ("full", "4567"), ("k1", "4.5k"), ("k0", "4k") };

        /// <summary>One header item; <paramref name="format"/> overrides the default format for this widget.</summary>
        public static ColumnDef Item(string key, bool on, string format = null)
        {
            ColumnDef d;
            switch (key)
            {
                case "title": d = new ColumnDef(key, "Widget name", "", 0, on, Align.Left); break;
                case "session": d = new ColumnDef(key, "Session", "", 0, on, Align.Left).WithFormats("letter", ("letter", "R"), ("name", "Race")); break;
                case "class": d = new ColumnDef(key, "Class (single class)", "", 0, on, Align.Left); break;
                case "laps": d = new ColumnDef(key, "Laps", "", 0, on, Align.Left).WithFormats("both", ("both", "5/12"), ("current", "Lap 5")); break;
                case "time":
                    d = new ColumnDef(key, "Time", "", 0, on, Align.Left)
                        .WithFormats("remain_total", ("remain_total", "12:55/31m"), ("remain", "12:55"), ("elapsed_total", "18:05/31m"));
                    break;
                case "spacer": d = new ColumnDef(key, "Push what follows to the right", "", 0, on, Align.Left); break;
                case "tracktemp": d = new ColumnDef(key, "Track temperature", "", 0, on, Align.Left).WithFormats("C1", TempFormats); break;
                case "airtemp": d = new ColumnDef(key, "Air temperature", "", 0, on, Align.Left).WithFormats("C1", TempFormats); break;
                case "humidity": d = new ColumnDef(key, "Humidity", "", 0, on, Align.Left).WithFormats("1", ("1", "55.2%"), ("0", "55%")); break;
                case "sof": d = new ColumnDef(key, "Strength of field", "", 0, on, Align.Left).WithFormats("full", RatingFormats); break;
                case "cars": d = new ColumnDef(key, "Cars", "", 0, on, Align.Left).WithFormats("running", ("running", "28/34"), ("total", "34")); break;
                case "incidents":
                    // your incidents / the next penalty / the DQ limit of the event (most officials: 17x, every 8x more, 25x)
                    d = new ColumnDef(key, "Incidents and limits", "", 0, on, Align.Left)
                        .WithFormats("full", ("full", "5x/17x/25x"), ("dq", "5x/25x"), ("count", "5x"));
                    break;
                case "clock": d = new ColumnDef(key, "Clock", "", 0, on, Align.Left).WithFormats("real", ("real", "21:30 (real time)"), ("sim", "14:05 (sim time)")); break;
                default: throw new ArgumentException("Unknown header item " + key);
            }
            if (format != null) d.DefaultFormat = format;
            return d;
        }

        // track and air temperature and humidity are told apart by an icon instead of a word ("Air 21.3°C", "55% RH" was the old look)
        static string IconOf(string key) { return key == "tracktemp" ? Icons.Track : key == "airtemp" ? Icons.Air : key == "humidity" ? Icons.Humidity : null; }
        const uint IconColor = 0xFFB4B4B4;

        /// <summary>
        /// Draws the enabled items left to right with measured widths; items after "spacer" are right-aligned.
        /// <paramref name="title"/> is the text of the "title" item.
        /// </summary>
        public static void Draw(DisplayList dl, RaceSnapshot snap, IEnumerable<ColumnConfig> items, string title, float width, float h, float fs, float pad, float gap)
        {
            var left = new List<(string Text, string Icon)>();
            var right = new List<(string Text, string Icon)>();
            var target = left;
            foreach (var item in items.Where(i => i.Enabled))
            {
                if (item.Key == "spacer") { target = right; continue; }
                string t = item.Key == "title" ? title : Text(item, snap);
                if (!string.IsNullOrEmpty(t)) target.Add((t, IconOf(item.Key)));
            }
            float iconSize = (float)Math.Round(fs * 1.15f), iconGap = (float)Math.Round(fs * 0.3f);
            float Width((string Text, string Icon) e) { return dl.Measure(e.Text, fs) + (e.Icon != null ? iconSize + iconGap : 0); }
            void Put((string Text, string Icon) e, float x)
            {
                if (e.Icon != null)
                {
                    dl.Icon(x, (h - iconSize) / 2, iconSize, e.Icon, IconColor);
                    x += iconSize + iconGap;
                }
                dl.Text(x, 0, dl.Measure(e.Text, fs) + 2, h, e.Text, fs, 0xFFFFFFFF);
            }

            float lx = pad;
            foreach (var e in left)
            {
                Put(e, lx);
                lx += Width(e) + gap;
            }
            float rx = width - pad;
            for (int i = right.Count - 1; i >= 0; i--)
            {
                float w = Width(right[i]);
                if (rx - w < lx) break;   // never draw over the left group
                Put(right[i], rx - w - 2);
                rx -= w + gap;
            }
        }

        public static string Text(ColumnConfig item, RaceSnapshot snap)
        {
            bool multi = snap.Classes.Count > 1;
            var cs = multi ? null : snap.PlayerClass;
            switch (item.Key)
            {
                case "session":
                    if (string.IsNullOrEmpty(snap.SessionType)) return "";
                    return item.Format == "name" ? snap.SessionType : snap.SessionType.Substring(0, 1).ToUpperInvariant();
                case "class":
                    // single class: the class (or the car for single-make series); multiclass: the class headers do that
                    return multi || snap.PlayerClass == null ? "" : snap.PlayerClass.Name;
                case "laps":
                    if (item.Format == "current") return snap.LeaderLap > 0 ? "Lap " + snap.LeaderLap : "";
                    if (snap.TotalLaps > 0) return snap.LeaderLap + "/" + snap.TotalLaps;
                    if (snap.EstTotalLaps > 0) return snap.LeaderLap + "/≈" + Fmt.Num(snap.EstTotalLaps, "0.0");
                    return snap.LeaderLap > 0 ? "Lap " + snap.LeaderLap : "";
                case "time":
                    if (snap.TimeRemain < 0) return "";
                    if (item.Format == "remain") return Fmt.Clock(snap.TimeRemain);
                    if (item.Format == "elapsed_total" && snap.TimeTotal > 0) return Fmt.Clock(Math.Max(0, snap.TimeTotal - snap.TimeRemain)) + "/" + Fmt.Short(snap.TimeTotal);
                    return Fmt.Clock(snap.TimeRemain) + (snap.TimeTotal > 0 ? "/" + Fmt.Short(snap.TimeTotal) : "");
                case "tracktemp": return Fmt.Temperature(snap.TrackTemp, item.Format);
                case "airtemp": return Fmt.Temperature(snap.AirTemp, item.Format);
                case "humidity": return snap.Humidity > 0 ? Fmt.Humidity(snap.Humidity, item.Format) : "";   // the drop icon says what it is
                case "sof":
                    int sof = cs != null ? cs.Sof : Data.Engine.RatingMath.StrengthOfField(snap.Cars.Select(c => c.IRating));
                    return sof > 0 ? "SOF " + Fmt.Rating(sof, item.Format) : "";
                case "cars":
                    int count = cs != null ? cs.Cars.Count : snap.Cars.Count;
                    int running = cs != null ? cs.Cars.Count(c => c.InWorld) : snap.Cars.Count(c => c.InWorld);
                    return item.Format == "total" ? count.ToString() : running + "/" + count;
                case "incidents":
                    // limits only when the event has them (practice and many hosted sessions are unlimited)
                    string inc = snap.Incidents + "x";
                    if (item.Format == "count") return inc;
                    int next = snap.NextIncidentPenalty, dq = snap.IncidentLimit;
                    if (item.Format == "full" && next > 0 && (dq <= 0 || next < dq)) inc += "/" + next + "x";
                    if (dq > 0) inc += "/" + dq + "x";
                    return inc;
                case "clock":
                    return item.Format == "sim" ? TimeSpan.FromSeconds(snap.TimeOfDay).ToString(@"hh\:mm") : DateTime.Now.ToString("HH:mm");
                default: return "";
            }
        }
    }
}
