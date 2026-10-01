using System.Windows;
using vibeRacingOverlays.App.Core;

namespace vibeRacingOverlays.App.UI
{
    /// <summary>
    /// Widget presets, used from the Widget menu. A widget in a layout is always the work version: loading a preset
    /// overwrites its settings (name and position stay), saving puts its settings into a preset. The Default preset is
    /// the app's own and stays intact (save as a new preset instead).
    /// </summary>
    public static class WidgetPresets
    {
        public static List<WidgetPreset> OfType(AppSettings app, WidgetSettings ws)
        {
            return app.Presets.Where(p => p.TypeName == ws.TypeName)
                .OrderBy(p => p.IsDefault ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void Mirror() { if (PresetLibrary.Current != null) PresetLibrary.Current.Mirror(); }

        static WidgetSettings Snapshot(WidgetSettings ws)
        {
            var snap = AppSettings.CloneWidget(ws);
            snap.PresetId = null;
            return snap;
        }

        /// <summary>Overwrites the widget's settings with the preset (asks first).</summary>
        public static bool Load(Window owner, WidgetSettings ws, WidgetPreset p)
        {
            if (AppSettings.SameSettings(ws, p.Settings)) { ws.PresetId = p.Id; return true; }
            if (MessageBox.Show(owner, "Load preset '" + p.Name + "' into widget '" + ws.Title + "'? Its current settings are replaced (name and position stay).",
                "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return false;
            SettingsPanel.Load(ws, p);
            return true;
        }

        public static bool SaveAs(Window owner, AppSettings app, WidgetSettings ws)
        {
            string name = InputDialog.Ask(owner, "Save as preset", "Save the settings of widget '" + ws.Title + "' as a new " + ws.TypeName + " preset:",
                app.UniquePresetName(ws.GetType(), ws.Title == ws.TypeName ? ws.TypeName + " preset " + OfType(app, ws).Count : ws.Title, null));
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            var p = new WidgetPreset { Name = app.UniquePresetName(ws.GetType(), string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ? "Default copy" : name, null), Settings = Snapshot(ws) };
            app.Presets.Add(p);
            ws.PresetId = p.Id;
            Mirror();
            return true;
        }

        /// <summary>
        /// Overwrites a preset with the widget's settings. Other widgets (in your layouts) that use the preset keep
        /// their own settings unless you choose to update them too; layout presets stay as they were saved.
        /// </summary>
        public static bool SaveTo(Window owner, AppSettings app, WidgetSettings ws, WidgetPreset p)
        {
            if (p == null || p.IsDefault) return false;
            if (MessageBox.Show(owner, "Overwrite preset '" + p.Name + "' with the settings of widget '" + ws.Title + "'?", "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return false;
            p.Settings = Snapshot(ws);
            ws.PresetId = p.Id;
            var others = app.OthersUsing(p, ws);
            if (others.Count > 0)
            {
                var where = app.Layouts.Where(l => l.Widgets.Any(others.Contains)).Select(l => "'" + l.Name + "'").ToList();
                string msg = others.Count + " other widget" + (others.Count == 1 ? "" : "s") + " (in layout " + string.Join(", ", where) + ") also use" + (others.Count == 1 ? "s" : "")
                    + " preset '" + p.Name + "'.\nUpdate " + (others.Count == 1 ? "it" : "them") + " to these settings too?\n\nNo: they keep their own settings.";
                if (MessageBox.Show(owner, msg, "vibeRacingOverlays", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    foreach (var w in others) SettingsPanel.Load(w, p);
            }
            Mirror();
            return true;
        }

        public static bool Rename(Window owner, AppSettings app, WidgetPreset p)
        {
            if (p == null || p.IsDefault) return false;
            string name = InputDialog.Ask(owner, "Rename preset", "New name for preset '" + p.Name + "':", p.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == p.Name) return false;
            name = name.Trim();
            p.Name = app.UniquePresetName(p.Settings.GetType(), string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ? "Default copy" : name, p);
            Mirror();
            return true;
        }

        public static bool Delete(Window owner, AppSettings app, WidgetPreset p)
        {
            if (p == null || p.IsDefault) return false;
            if (MessageBox.Show(owner, "Delete preset '" + p.Name + "'?\nWidgets keep their settings.", "vibeRacingOverlays", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return false;
            app.Presets.RemoveAll(x => x == p);
            var def = app.DefaultPreset(p.Settings.GetType());
            foreach (var w in app.Layouts.SelectMany(l => l.Widgets).Concat(app.LayoutPresets.SelectMany(l => l.Widgets)))
                if (w.PresetId == p.Id) w.PresetId = def != null ? def.Id : null;
            if (PresetLibrary.Current != null) PresetLibrary.Current.Removed(p);
            Mirror();
            return true;
        }

        /// <summary>Saves the widget's current settings as a preset file (to share).</summary>
        public static void Export(Window owner, WidgetSettings ws)
        {
            var p = new WidgetPreset { Name = ws.Title, Settings = Snapshot(ws) };
            string folder = System.IO.Path.Combine(Exchange.LibraryFolder, "exports");
            System.IO.Directory.CreateDirectory(folder);
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Export widget preset", Filter = Exchange.PresetFilter, InitialDirectory = folder,
                FileName = Exchange.SafeName(ws.TypeName + " - " + ws.Title) + ".vropreset.json", AddExtension = true };
            if (dlg.ShowDialog(owner) != true) return;
            try { Exchange.ExportPreset(p, dlg.FileName); }
            catch (Exception ex) { MessageBox.Show(owner, "The preset could not be exported:\n" + ex.Message, "vibeRacingOverlays", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        /// <summary>Adds a preset from a file; loads it into the widget when it's the same type (asks first). Returns the preset.</summary>
        public static WidgetPreset Import(Window owner, AppSettings app, WidgetSettings ws)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import widget preset", Filter = Exchange.PresetFilter, InitialDirectory = Exchange.LibraryFolder };
            if (dlg.ShowDialog(owner) != true) return null;
            try
            {
                var p = Exchange.ReadPreset(dlg.FileName);
                if (p == null) { MessageBox.Show(owner, "This preset is for a widget type this version doesn't know.", "vibeRacingOverlays"); return null; }
                p = app.AddImportedPreset(p);
                Mirror();
                if (ws != null && p.Settings.GetType() == ws.GetType()) Load(owner, ws, p);
                else MessageBox.Show(owner, "'" + p.Name + "' was added to the " + p.TypeName + " presets.", "vibeRacingOverlays");
                return p;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "The preset could not be imported:\n" + ex.Message, "vibeRacingOverlays", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }
    }
}
