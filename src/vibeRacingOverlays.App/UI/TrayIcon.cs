using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Overlay;
using Forms = System.Windows.Forms;

namespace vibeRacingOverlays.App.UI
{
    /// <summary>
    /// The app's icon in the system tray. The main window can be closed to it while the widgets keep running;
    /// its menu has the global actions (widgets on/off, edit layout, layouts), the update link and Exit.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly Forms.NotifyIcon icon;
        readonly AppSettings settings;
        readonly OverlayManager overlays;
        readonly Action open, exit;
        Action balloonClick;

        /// <summary>A newer release, once the update check found one (shown in the menu).</summary>
        public UpdateInfo Update;

        public TrayIcon(AppSettings settings, OverlayManager overlays, Action open, Action exit)
        {
            this.settings = settings;
            this.overlays = overlays;
            this.open = open;
            this.exit = exit;
            icon = new Forms.NotifyIcon { Text = BuildInfo.AppName, Icon = LoadIcon(), ContextMenuStrip = new Forms.ContextMenuStrip { ShowItemToolTips = true }, Visible = true };
            // the menu is built when it opens, so it always shows the current state and layouts
            icon.ContextMenuStrip.Opening += (s, e) => Build();
            icon.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
            icon.BalloonTipClicked += (s, e) => { var a = balloonClick; balloonClick = null; if (a != null) a(); };
            icon.BalloonTipClosed += (s, e) => balloonClick = null;
            Build();
        }

        static System.Drawing.Icon LoadIcon()
        {
            // the small size of the app icon (not the 32 px exe icon scaled down)
            var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            using (var s = res.Stream) return new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);
        }

        /// <summary>A Windows notification from the tray icon; clicking it runs the action.</summary>
        public void Notify(string title, string text, Action click = null)
        {
            balloonClick = click;
            icon.ShowBalloonTip(8000, title, text, Forms.ToolTipIcon.None);
        }

        void Build()
        {
            var m = icon.ContextMenuStrip;
            m.Items.Clear();
            var openItem = Item("Open " + BuildInfo.AppName, open);
            openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
            m.Items.Add(openItem);
            m.Items.Add(new Forms.ToolStripSeparator());
            m.Items.Add(Item("Show widgets", overlays.ToggleOverlays, settings.OverlaysVisible, "Show or hide all widgets"));
            m.Items.Add(Item("Edit layout", () => overlays.SetEditMode(!overlays.EditMode), overlays.EditMode, "Drag widgets on screen to move them, mouse wheel to resize"));
            var layouts = new Forms.ToolStripMenuItem("Layout");
            foreach (var l in settings.Layouts)
            {
                string id = l.Id;
                layouts.DropDownItems.Add(Item(l.Name, () => overlays.SwitchLayout(id), l.Id == settings.ActiveLayoutId));
            }
            m.Items.Add(layouts);
            m.Items.Add(new Forms.ToolStripSeparator());
            if (Update != null)
            {
                var u = Update;
                m.Items.Add(Item("Download version " + u.Version + "...", () => UpdateCheck.Open(u)));
            }
            m.Items.Add(Item("Keep running when the window is closed", () => { settings.CloseToTray = !settings.CloseToTray; overlays.ScheduleSave(); }, settings.CloseToTray, "On: closing the window keeps the app and widgets running here. Off: closing the window exits the app"));
            m.Items.Add(Item("Check for updates", () => { settings.CheckForUpdates = !settings.CheckForUpdates; overlays.ScheduleSave(); }, settings.CheckForUpdates, "Look for a new version at start and twice a day (nothing is downloaded by itself)"));
            m.Items.Add(new Forms.ToolStripSeparator());
            m.Items.Add(Item("Exit", exit));
        }

        static Forms.ToolStripMenuItem Item(string text, Action click, bool? check = null, string tooltip = null)
        {
            var mi = new Forms.ToolStripMenuItem(text) { ToolTipText = tooltip };
            if (check.HasValue) mi.Checked = check.Value;
            mi.Click += (s, e) => click();
            return mi;
        }

        public void Dispose()
        {
            icon.Visible = false;   // otherwise the icon stays in the tray until the mouse passes over it
            icon.Dispose();
        }
    }
}
