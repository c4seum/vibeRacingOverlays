using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.UI;

namespace vibeRacingOverlays.App
{
    /// <summary>
    /// Undo / redo of every change you make: widgets and their settings, layouts (new, rename, delete), widget presets and
    /// layout presets (save, rename, delete, import) and the app settings (theme, data source, snapping...).
    /// Instead of every control reporting its changes, the settings are compared with the last snapshot a few times a
    /// second: any change becomes a step once it has settled, so a dragged slider or a typed number is one step, and new
    /// options are covered automatically. One history for the whole app (50 steps, while it runs); undoing a change in
    /// another layout switches to that layout first, so you see what comes back.
    /// Not tracked (they are where you are, not what you changed): the active layout, show/hide all widgets, the editor
    /// layout, and the app's own bookkeeping.
    /// </summary>
    public partial class MainWindow
    {
        const int MaxUndoSteps = 50;
        static readonly TimeSpan UndoSettle = TimeSpan.FromMilliseconds(700);
        static readonly string[] NotTracked = { "ActiveLayoutId", "OverlaysVisible", "PreviewSideBySide", "TrayTipShown", "UpdateNotified", "SavedByVersion", "Unreadable", "LegacyWidgets" };
        static readonly string[] Lists = { "Layouts", "Presets", "LayoutPresets" };

        sealed class Step { public string Before, After, Label; }

        readonly List<Step> undoSteps = new List<Step>(), redoSteps = new List<Step>();
        string undoBaseline, undoSeen;
        DateTime undoSeenAt;
        DispatcherTimer undoTimer;

        void InitUndo()
        {
            UndoButton.Click += (s, e) => Undo();
            RedoButton.Click += (s, e) => Redo();
            PreviewKeyDown += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || (e.Key != Key.Z && e.Key != Key.Y)) return;
                // a text box has its own undo for what you're typing
                if (Keyboard.FocusedElement is TextBoxBase) return;
                bool redo = e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (redo) Redo(); else Undo();
                e.Handled = true;
            };
            undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            undoTimer.Tick += (s, e) => PollUndo();
            undoTimer.Start();
            PollUndo();
        }

        /// <summary>Everything that is tracked, as JSON.</summary>
        string TakeState()
        {
            var o = JsonSerializer.SerializeToNode(settings, AppSettings.JsonOptions) as JsonObject;
            foreach (var k in NotTracked) o.Remove(k);
            return o.ToJsonString();
        }

        void PollUndo()
        {
            // in the tray nothing is edited (except by dragging in edit mode); the first look afterwards catches up
            if (!IsVisible && !overlays.EditMode) return;
            var now = TakeState();
            if (undoBaseline == null) { undoBaseline = undoSeen = now; UpdateUndoButtons(); return; }
            if (now != undoSeen) { undoSeen = now; undoSeenAt = DateTime.UtcNow; }
            else if (now != undoBaseline && DateTime.UtcNow - undoSeenAt >= UndoSettle) Record(now);
        }

        /// <summary>Makes a not yet settled change a step right away (before undo / redo).</summary>
        void CommitUndo()
        {
            var now = TakeState();
            if (undoBaseline == null) undoBaseline = undoSeen = now;
            else if (now != undoBaseline) Record(now);
        }

        void Record(string now)
        {
            undoSteps.Add(new Step { Before = undoBaseline, After = now, Label = Describe(undoBaseline, now) });
            if (undoSteps.Count > MaxUndoSteps) undoSteps.RemoveAt(0);
            redoSteps.Clear();
            undoBaseline = undoSeen = now;
            UpdateUndoButtons();
        }

        void Undo()
        {
            CommitUndo();
            if (undoSteps.Count == 0) return;
            var step = undoSteps[undoSteps.Count - 1];
            undoSteps.RemoveAt(undoSteps.Count - 1);
            ApplyState(step.After, step.Before);
            redoSteps.Add(step);
            UpdateUndoButtons();
        }

        void Redo()
        {
            CommitUndo();   // a new change since the undo clears the redo steps
            if (redoSteps.Count == 0) return;
            var step = redoSteps[redoSteps.Count - 1];
            redoSteps.RemoveAt(redoSteps.Count - 1);
            ApplyState(step.Before, step.After);
            undoSteps.Add(step);
            UpdateUndoButtons();
        }

        // ---------------------------------------------------------------- putting a state back

        /// <summary>
        /// Puts the settings back to <paramref name="target"/>. Layouts and widgets that didn't change keep their objects
        /// (and their windows on screen); the rest is taken from the snapshot.
        /// </summary>
        void ApplyState(string from, string target)
        {
            var t = JsonSerializer.Deserialize<AppSettings>(target, AppSettings.JsonOptions);
            var a = Sections(from);
            var b = Sections(target);

            // app settings (everything that isn't a list or excluded)
            string oldFont = settings.Font;
            var oldTheme = settings.Theme;
            foreach (var p in typeof(AppSettings).GetProperties())
            {
                if (!p.CanWrite || !p.CanRead || NotTracked.Contains(p.Name) || Lists.Contains(p.Name)) continue;
                if (p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length > 0 || p.GetSetMethod() == null || p.GetSetMethod().IsStatic) continue;
                p.SetValue(settings, p.GetValue(t));
            }

            // layouts: same object when it exists, widgets kept when unchanged
            var changedWidgets = new HashSet<string>();
            var layouts = new List<LayoutConfig>();
            foreach (var tl in t.Layouts)
            {
                var l = settings.Layouts.FirstOrDefault(x => x.Id == tl.Id) ?? new LayoutConfig { Id = tl.Id };
                l.Name = tl.Name;
                l.PresetId = tl.PresetId;
                var widgets = new List<WidgetSettings>();
                foreach (var tw in tl.Widgets)
                {
                    var same = l.Widgets.FirstOrDefault(x => x.Id == tw.Id);
                    if (same != null && AppSettings.ToJson(same, typeof(WidgetSettings)) == AppSettings.ToJson(tw, typeof(WidgetSettings))) widgets.Add(same);
                    else { widgets.Add(tw); changedWidgets.Add(tw.Id); }
                }
                foreach (var w in l.Widgets) if (!tl.Widgets.Any(x => x.Id == w.Id)) changedWidgets.Add(w.Id);
                l.Widgets.Clear();
                l.Widgets.AddRange(widgets);
                layouts.Add(l);
            }

            // presets: deleted ones lose their library file, restored ones get it back (Mirror)
            var removedPresets = settings.Presets.Where(p => !t.Presets.Any(x => x.Id == p.Id)).ToList();
            settings.Layouts = layouts;
            settings.Presets = t.Presets;
            settings.LayoutPresets = t.LayoutPresets;
            if (PresetLibrary.Current != null)
            {
                foreach (var p in removedPresets) PresetLibrary.Current.Removed(p);
                PresetLibrary.Current.Mirror();
            }

            // show where the change is: the layout it was in (when it's another one), or another one when the active one is gone
            var touched = b.Keys.Union(a.Keys).Where(k => k.StartsWith("Layouts:") && (!a.ContainsKey(k) || !b.ContainsKey(k) || a[k] != b[k]))
                .Select(k => k.Substring("Layouts:".Length)).Where(id => layouts.Any(l => l.Id == id)).ToList();
            string active = touched.Count == 1 ? touched[0] : settings.ActiveLayoutId;
            if (!layouts.Any(l => l.Id == active)) active = layouts.Count > 0 ? layouts[0].Id : null;
            settings.ActiveLayoutId = active;

            // side effects of app settings
            loading = true;
            if (settings.Theme != oldTheme) ThemeManager.Apply(settings.Theme);
            ThemeBox.SelectedItem = settings.Theme;
            telemetry.Mode = settings.Source;
            telemetry.LivePositions = settings.LivePositions;
            SourceBox.SelectedItem = settings.Source;
            loading = false;
            if (settings.Font != oldFont) overlays.SetFont(settings.Font);

            overlays.Reload(changedWidgets);   // also creates / removes windows for the (new) active layout
            overlays.ReloadHotkeys();   // the bindings may have changed too
            UpdateFooter();
            overlays.SaveNow();

            // the editor follows: select the widget that changed (or keep the selection)
            var sel = Selected;
            string selectId = settings.Widgets.Where(w => changedWidgets.Contains(w.Id)).Select(w => w.Id).FirstOrDefault() ?? (sel != null ? sel.Id : null);
            shownLayoutId = settings.ActiveLayoutId;
            RefreshLayouts();
            RefreshList();
            foreach (ListBoxItem item in WidgetList.Items)
                if (((WidgetSettings)item.Tag).Id == selectId) WidgetList.SelectedItem = item;
            if (WidgetList.SelectedItem == null && WidgetList.Items.Count > 0) WidgetList.SelectedIndex = 0;
            ShowSelected();
            undoBaseline = undoSeen = TakeState();
        }

        // ---------------------------------------------------------------- what a step did (tooltip)

        /// <summary>The state split into parts: one per layout, preset and layout preset ("Layouts:id"...), their order, and each app setting.</summary>
        static Dictionary<string, string> Sections(string json)
        {
            var d = new Dictionary<string, string>();
            var o = JsonNode.Parse(json) as JsonObject;
            foreach (var kv in o)
            {
                var arr = kv.Value as JsonArray;
                if (arr != null && Lists.Contains(kv.Key))
                {
                    var ids = new List<string>();
                    foreach (var item in arr)
                    {
                        string id = (string)item["Id"];
                        ids.Add(id);
                        d[kv.Key + ":" + id] = item.ToJsonString();
                    }
                    d[kv.Key + "#order"] = string.Join(",", ids);
                }
                else d["set:" + kv.Key] = kv.Value != null ? kv.Value.ToJsonString() : "null";
            }
            return d;
        }

        static string NameIn(string json)
        {
            var o = JsonNode.Parse(json) as JsonObject;
            var n = o != null ? (string)o["Name"] : null;
            return string.IsNullOrEmpty(n) ? "?" : n;
        }

        static readonly Dictionary<string, string> SettingNames = new Dictionary<string, string>
        {
            { "Source", "data source" }, { "Theme", "theme" }, { "Font", "font" }, { "LivePositions", "live positions" },
            { "SnapEnabled", "snapping" }, { "SnapDistance", "snap distance" }, { "SnapMargin", "snap margin" },
            { "CloseToTray", "keep running in the tray" }, { "CheckForUpdates", "check for updates" }, { "StartLayoutDeclined", "Get started" },
        };

        static string Describe(string before, string after)
        {
            var a = Sections(before);
            var b = Sections(after);
            var keys = a.Keys.Union(b.Keys).Where(k => !a.ContainsKey(k) || !b.ContainsKey(k) || a[k] != b[k]).ToList();
            string Kind(string list) { return list == "Layouts" ? "layout" : list == "Presets" ? "preset" : "layout preset"; }

            // most telling first: something added or removed, then renamed or changed, then app settings
            foreach (var k in keys.Where(k => k.Contains(':') && !k.StartsWith("set:")))
            {
                string list = k.Substring(0, k.IndexOf(':'));
                if (!a.ContainsKey(k)) return (list == "Layouts" ? "new layout '" : "save " + Kind(list) + " '") + NameIn(b[k]) + "'";
                if (!b.ContainsKey(k)) return "delete " + Kind(list) + " '" + NameIn(a[k]) + "'";
            }
            var changed = keys.Where(k => k.Contains(':') && !k.StartsWith("set:")).ToList();
            foreach (var k in changed)
            {
                string list = k.Substring(0, k.IndexOf(':'));
                var ao = JsonNode.Parse(a[k]) as JsonObject;
                var bo = JsonNode.Parse(b[k]) as JsonObject;
                var diff = bo.Select(p => p.Key).Union(ao.Select(p => p.Key)).Where(x => !JsonNode.DeepEquals(ao[x], bo[x])).ToList();
                if (diff.SequenceEqual(new[] { "Name" })) return "rename " + Kind(list) + " '" + NameIn(a[k]) + "' to '" + NameIn(b[k]) + "'";
                if (list == "Layouts" && diff.Contains("Widgets"))
                {
                    string w = DescribeWidgets(ao["Widgets"] as JsonArray, bo["Widgets"] as JsonArray);
                    return changed.Count > 1 ? w + " (and more)" : w;
                }
                if (list == "Presets" && changed.Count == 1) return "update preset '" + NameIn(b[k]) + "'";
            }
            if (changed.Count > 0) return "changes to " + string.Join(", ", changed.Select(k => Kind(k.Substring(0, k.IndexOf(':'))) + " '" + NameIn(b[k]) + "'").Distinct().Take(2));
            var set = keys.FirstOrDefault(k => k.StartsWith("set:"));
            if (set != null)
            {
                string name = set.Substring(4), nice;
                return "change " + (SettingNames.TryGetValue(name, out nice) ? nice : name);
            }
            if (keys.Any(k => k.EndsWith("#order"))) return "change the order";
            return "change";
        }

        static readonly string[] PositionKeys = { "X", "Y", "Screen", "Anchor", "OffsetX", "OffsetY" };

        static string DescribeWidgets(JsonArray before, JsonArray after)
        {
            var a = (before ?? new JsonArray()).OfType<JsonObject>().ToDictionary(o => (string)o["Id"]);
            var b = (after ?? new JsonArray()).OfType<JsonObject>().ToDictionary(o => (string)o["Id"]);
            string T(JsonObject o) { var t = (string)o["Title"]; return string.IsNullOrEmpty(t) ? "widget" : t; }
            var added = b.Keys.Where(k => !a.ContainsKey(k)).ToList();
            var removed = a.Keys.Where(k => !b.ContainsKey(k)).ToList();
            var changed = b.Keys.Where(k => a.ContainsKey(k) && !JsonNode.DeepEquals(a[k], b[k])).ToList();
            int n = added.Count + removed.Count + changed.Count;
            if (n == 0) return "change the widget order";
            if (n > 1) return "changes to " + n + " widgets";
            if (added.Count == 1) return "add '" + T(b[added[0]]) + "'";
            if (removed.Count == 1) return "remove '" + T(a[removed[0]]) + "'";
            var ao = a[changed[0]];
            var bo = b[changed[0]];
            string t = T(bo);
            var diff = bo.Select(p => p.Key).Union(ao.Select(p => p.Key)).Where(k => !JsonNode.DeepEquals(ao[k], bo[k])).ToList();
            if (diff.All(k => PositionKeys.Contains(k))) return "move '" + t + "'";
            if (diff.SequenceEqual(new[] { "Scale" })) return "resize '" + t + "'";
            if (diff.SequenceEqual(new[] { "Enabled" })) return ((bool?)bo["Enabled"] == true ? "show '" : "hide '") + t + "'";
            if (diff.SequenceEqual(new[] { "Locked" })) return ((bool?)bo["Locked"] == true ? "lock '" : "unlock '") + t + "'";
            if (diff.SequenceEqual(new[] { "Title" })) return "rename '" + T(ao) + "' to '" + t + "'";
            return "change '" + t + "'";
        }

        // ---------------------------------------------------------------- buttons

        void UpdateUndoButtons()
        {
            bool canUndo = undoSteps.Count > 0, canRedo = redoSteps.Count > 0;
            UndoButton.IsEnabled = canUndo;
            RedoButton.IsEnabled = canRedo;
            UndoButton.ToolTip = canUndo ? "Undo: " + undoSteps[undoSteps.Count - 1].Label + " (Ctrl+Z)" : "Nothing to undo (Ctrl+Z)";
            RedoButton.ToolTip = canRedo ? "Redo: " + redoSteps[redoSteps.Count - 1].Label + " (Ctrl+Y)" : "Nothing to redo (Ctrl+Y)";
        }
    }
}
