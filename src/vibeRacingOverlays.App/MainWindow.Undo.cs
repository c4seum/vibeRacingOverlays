using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using vibeRacingOverlays.App.Core;

namespace vibeRacingOverlays.App
{
    /// <summary>
    /// Undo / redo of the work version of a layout (everything about its widgets) and of deleting a layout.
    /// Instead of every control reporting its changes, the active layout is compared with its last snapshot a few
    /// times a second: any change (settings, dragging, presets, show/hide) becomes a step once it has settled, so a
    /// dragged slider or a typed number is one step. Each layout has its own history; it lives only while the app runs.
    /// Presets, layout presets, files and the theme are not part of it (those ask before they change anything).
    /// </summary>
    public partial class MainWindow
    {
        const int MaxUndoSteps = 50;
        static readonly TimeSpan UndoSettle = TimeSpan.FromMilliseconds(700);

        /// <summary>The widgets of a layout as JSON, per widget id.</summary>
        sealed class Snapshot
        {
            public readonly List<KeyValuePair<string, string>> Widgets = new List<KeyValuePair<string, string>>();
            string all;
            public string All { get { return all ?? (all = string.Join("\n", Widgets.Select(w => w.Value))); } }
            public bool Same(Snapshot o) { return o != null && o.All == All; }
            public string JsonOf(string id) { foreach (var w in Widgets) if (w.Key == id) return w.Value; return null; }
        }

        abstract class Step { public string Label; }
        sealed class WidgetsStep : Step { public Snapshot Before, After; }
        /// <summary>A deleted layout (kept as it was, with its own history). Lives on the stack of the layout you look at.</summary>
        sealed class DeleteStep : Step { public LayoutConfig Layout; public int Index; public History Kept; public string OwnerId; }

        sealed class History
        {
            public readonly List<Step> Undo = new List<Step>(), Redo = new List<Step>();
            public Snapshot Baseline, Seen;
            public DateTime SeenAt;
        }

        readonly Dictionary<string, History> histories = new Dictionary<string, History>();
        string historyLayoutId;
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
            undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            undoTimer.Tick += (s, e) => PollUndo();
            undoTimer.Start();
            PollUndo();
        }

        History HistoryOf(string layoutId)
        {
            History h;
            if (!histories.TryGetValue(layoutId, out h)) histories[layoutId] = h = new History();
            return h;
        }

        static Snapshot Take(LayoutConfig l)
        {
            var s = new Snapshot();
            foreach (var w in l.Widgets) s.Widgets.Add(new KeyValuePair<string, string>(w.Id, AppSettings.ToJson(w, typeof(WidgetSettings))));
            return s;
        }

        void PollUndo()
        {
            var l = settings.ActiveLayout;
            if (l == null) return;
            if (historyLayoutId != l.Id)
            {
                // another layout became active: what changed in the previous one is a step of its own history
                var prev = settings.Layouts.FirstOrDefault(x => x.Id == historyLayoutId);
                if (prev != null) CommitUndo(prev);
                historyLayoutId = l.Id;
                UpdateUndoButtons();
            }
            var h = HistoryOf(l.Id);
            var now = Take(l);
            if (h.Baseline == null) { h.Baseline = h.Seen = now; UpdateUndoButtons(); return; }
            if (!now.Same(h.Seen)) { h.Seen = now; h.SeenAt = DateTime.UtcNow; }
            else if (!now.Same(h.Baseline) && DateTime.UtcNow - h.SeenAt >= UndoSettle) Record(h, now);
        }

        /// <summary>Makes a not yet settled change a step right away (before undo, switching or deleting).</summary>
        void CommitUndo(LayoutConfig l)
        {
            var h = HistoryOf(l.Id);
            var now = Take(l);
            if (h.Baseline == null) h.Baseline = h.Seen = now;
            else if (!now.Same(h.Baseline)) Record(h, now);
        }

        void Record(History h, Snapshot now)
        {
            Push(h.Undo, new WidgetsStep { Before = h.Baseline, After = now, Label = Describe(h.Baseline, now) });
            h.Redo.Clear();
            h.Baseline = h.Seen = now;
            UpdateUndoButtons();
        }

        static void Push(List<Step> stack, Step step)
        {
            stack.Add(step);
            if (stack.Count > MaxUndoSteps) stack.RemoveAt(0);
        }

        static Step Pop(List<Step> stack)
        {
            var s = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return s;
        }

        // ---------------------------------------------------------------- undo / redo

        void Undo()
        {
            var l = settings.ActiveLayout;
            CommitUndo(l);
            var h = HistoryOf(l.Id);
            if (h.Undo.Count == 0) return;
            var step = Pop(h.Undo);
            var ws = step as WidgetsStep;
            if (ws != null)
            {
                ApplySnapshot(l, ws.Before);
                h.Baseline = h.Seen = ws.Before;
                Push(h.Redo, step);
            }
            else RestoreLayout((DeleteStep)step);
            UpdateUndoButtons();
        }

        void Redo()
        {
            var l = settings.ActiveLayout;
            CommitUndo(l);   // a new change since the undo clears the redo steps
            var h = HistoryOf(l.Id);
            if (h.Redo.Count == 0) return;
            var step = Pop(h.Redo);
            var ws = step as WidgetsStep;
            if (ws != null)
            {
                ApplySnapshot(l, ws.After);
                h.Baseline = h.Seen = ws.After;
                Push(h.Undo, step);
            }
            else DeleteAgain((DeleteStep)step);
            UpdateUndoButtons();
        }

        /// <summary>Puts a layout's widgets back as they were. Unchanged widgets keep their windows; changed ones get new ones.</summary>
        void ApplySnapshot(LayoutConfig l, Snapshot target)
        {
            var current = Take(l);
            var list = new List<WidgetSettings>();
            var changed = new HashSet<string>();
            foreach (var w in target.Widgets)
            {
                var same = current.JsonOf(w.Key) == w.Value ? l.Widgets.FirstOrDefault(x => x.Id == w.Key) : null;
                if (same != null) list.Add(same);
                else { list.Add(AppSettings.ParseWidget(w.Value)); changed.Add(w.Key); }
            }
            foreach (var w in current.Widgets) if (target.JsonOf(w.Key) == null) changed.Add(w.Key);
            l.Widgets.Clear();
            l.Widgets.AddRange(list);
            overlays.Reload(changed);
            overlays.ScheduleSave();

            // show what changed: select that widget (or keep the selection)
            var sel = Selected;
            string selectId = list.Where(w => changed.Contains(w.Id)).Select(w => w.Id).FirstOrDefault() ?? (sel != null ? sel.Id : null);
            RefreshList();
            foreach (ListBoxItem item in WidgetList.Items)
                if (((WidgetSettings)item.Tag).Id == selectId) WidgetList.SelectedItem = item;
            if (WidgetList.SelectedItem == null && WidgetList.Items.Count > 0) WidgetList.SelectedIndex = 0;
            ShowSelected();
        }

        // ---------------------------------------------------------------- deleting a layout

        /// <summary>Called just before a layout is deleted; the step goes on the history of the layout shown next.</summary>
        void RecordLayoutDeleted(LayoutConfig deleted, int index, LayoutConfig next)
        {
            CommitUndo(deleted);
            var kept = HistoryOf(deleted.Id);
            histories.Remove(deleted.Id);
            var h = HistoryOf(next.Id);
            Push(h.Undo, new DeleteStep { Layout = deleted, Index = index, Kept = kept, OwnerId = next.Id, Label = "delete layout '" + deleted.Name + "'" });
            h.Redo.Clear();
        }

        /// <summary>Undo of a delete: the layout comes back where it was, with its history, and becomes active.</summary>
        void RestoreLayout(DeleteStep step)
        {
            var l = step.Layout;
            settings.Layouts.Insert(Math.Min(step.Index, settings.Layouts.Count), l);
            histories[l.Id] = step.Kept;
            Push(step.Kept.Redo, step);   // redo (delete it again) is offered while you look at it
            historyLayoutId = l.Id;
            overlays.SwitchLayout(l.Id);
            overlays.SaveNow();
            ShowActiveLayout();
        }

        void DeleteAgain(DeleteStep step)
        {
            var l = step.Layout;
            if (settings.Layouts.Count < 2 || !settings.Layouts.Contains(l)) return;
            int i = settings.Layouts.IndexOf(l);
            var next = settings.Layouts.FirstOrDefault(x => x.Id == step.OwnerId && x != l) ?? settings.Layouts[i == 0 ? 1 : i - 1];
            step.Kept = HistoryOf(l.Id);
            histories.Remove(l.Id);
            step.Index = i;
            step.OwnerId = next.Id;
            Push(HistoryOf(next.Id).Undo, step);
            historyLayoutId = next.Id;
            overlays.SwitchLayout(next.Id);
            settings.Layouts.Remove(l);
            overlays.SaveNow();
            ShowActiveLayout();
        }

        // ---------------------------------------------------------------- buttons

        void UpdateUndoButtons()
        {
            var l = settings.ActiveLayout;
            var h = l != null ? HistoryOf(l.Id) : null;
            bool canUndo = h != null && h.Undo.Count > 0, canRedo = h != null && h.Redo.Count > 0;
            UndoButton.IsEnabled = canUndo;
            RedoButton.IsEnabled = canRedo;
            UndoButton.ToolTip = canUndo ? "Undo: " + h.Undo[h.Undo.Count - 1].Label + " (Ctrl+Z)" : "Nothing to undo (Ctrl+Z)";
            RedoButton.ToolTip = canRedo ? "Redo: " + h.Redo[h.Redo.Count - 1].Label + " (Ctrl+Y)" : "Nothing to redo (Ctrl+Y)";
        }

        // what a step did, for the tooltip
        static readonly string[] PositionKeys = { "X", "Y", "Screen", "Anchor", "OffsetX", "OffsetY" };

        static string Describe(Snapshot before, Snapshot after)
        {
            var added = after.Widgets.Where(w => before.JsonOf(w.Key) == null).ToList();
            var removed = before.Widgets.Where(w => after.JsonOf(w.Key) == null).ToList();
            var changed = after.Widgets.Where(w => before.JsonOf(w.Key) != null && before.JsonOf(w.Key) != w.Value).ToList();
            int n = added.Count + removed.Count + changed.Count;
            if (n == 0) return "change the widget order";
            if (n > 1) return "changes to " + n + " widgets";
            if (added.Count == 1) return "add '" + TitleOf(added[0].Value) + "'";
            if (removed.Count == 1) return "remove '" + TitleOf(removed[0].Value) + "'";

            var a = JsonNode.Parse(before.JsonOf(changed[0].Key)) as JsonObject;
            var b = JsonNode.Parse(changed[0].Value) as JsonObject;
            string t = TitleOf(changed[0].Value);
            var diff = b.Select(p => p.Key).Union(a.Select(p => p.Key))
                .Where(k => !JsonNode.DeepEquals(a[k], b[k])).ToList();
            if (diff.All(k => PositionKeys.Contains(k))) return "move '" + t + "'";
            if (diff.SequenceEqual(new[] { "Scale" })) return "resize '" + t + "'";
            if (diff.SequenceEqual(new[] { "Enabled" })) return ((bool?)b["Enabled"] == true ? "show '" : "hide '") + t + "'";
            if (diff.SequenceEqual(new[] { "Locked" })) return ((bool?)b["Locked"] == true ? "lock '" : "unlock '") + t + "'";
            if (diff.SequenceEqual(new[] { "Title" })) return "rename '" + TitleOf(before.JsonOf(changed[0].Key)) + "' to '" + t + "'";
            return "change '" + t + "'";
        }

        static string TitleOf(string json)
        {
            var o = JsonNode.Parse(json) as JsonObject;
            var t = o != null ? (string)o["Title"] : null;
            return string.IsNullOrEmpty(t) ? "widget" : t;
        }
    }
}
