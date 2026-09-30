using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace vibeRacingOverlays.Setup
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
                return RunUninstall(args.Contains("--quiet", StringComparer.OrdinalIgnoreCase));

            // unattended install:  --silent [--dir <folder>] [--no-desktop] [--launch]
            if (args.Contains("--silent", StringComparer.OrdinalIgnoreCase))
            {
                int d = Array.FindIndex(args, a => a.Equals("--dir", StringComparison.OrdinalIgnoreCase));
                string folder = d >= 0 && d + 1 < args.Length ? args[d + 1] : Installer.InstalledFolder() ?? Installer.DefaultFolder;
                try
                {
                    SetupForm.CloseRunningApp(true);
                    Installer.Install(folder, !args.Contains("--no-desktop", StringComparer.OrdinalIgnoreCase), msg => { });
                    if (args.Contains("--launch", StringComparer.OrdinalIgnoreCase))
                        Process.Start(new ProcessStartInfo(Path.Combine(folder, Installer.ExeName)) { WorkingDirectory = folder, UseShellExecute = true });
                    return 0;
                }
                catch { return 2; }
            }

            Application.Run(new SetupForm());
            return 0;
        }

        static int RunUninstall(bool quiet)
        {
            const string title = "vibeRacingOverlays";
            if (!quiet && MessageBox.Show("Remove vibeRacingOverlays from this computer?", title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 1;
            if (!SetupForm.CloseRunningApp(quiet)) return 1;

            bool removeSettings = !quiet && MessageBox.Show(
                "Also remove your layouts, presets and settings?\n\nChoose No to keep them for a later reinstall.",
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            try
            {
                Installer.Uninstall(removeSettings);
                if (!quiet) MessageBox.Show("vibeRacingOverlays has been removed.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show("Uninstall failed:\n" + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }
    }

    /// <summary>Single-page installer: folder, desktop shortcut, launch after install.</summary>
    internal sealed class SetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(27, 29, 33), Panel = Color.FromArgb(36, 39, 44), Field = Color.FromArgb(48, 52, 58),
            Fg = Color.FromArgb(232, 232, 232), Muted = Color.FromArgb(154, 160, 166), Accent = Color.FromArgb(47, 140, 255);

        readonly TextBox folderBox = new TextBox();
        readonly CheckBox desktop = new CheckBox { Text = "Create a desktop shortcut", Checked = true, AutoSize = true };
        readonly CheckBox launch = new CheckBox { Text = "Start vibeRacingOverlays after installation", Checked = true, AutoSize = true };
        readonly Button install = new Button { Text = "Install", Width = 110, Height = 32 };
        readonly Button cancel = new Button { Text = "Cancel", Width = 110, Height = 32 };
        readonly Label status = new Label { AutoSize = true };

        public SetupForm()
        {
            Text = "vibeRacingOverlays " + Installer.Version + " Setup";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 330);
            BackColor = Bg; ForeColor = Fg;
            Font = new Font("Segoe UI", 9.5f);

            var title = new Label { Text = "vibeRacingOverlays", Font = new Font("Segoe UI Semibold", 18f), AutoSize = true, Location = new Point(24, 20) };
            var sub = new Label { Text = "Lightweight iRacing overlays  -  version " + Installer.Version, ForeColor = Muted, AutoSize = true, Location = new Point(27, 58) };

            string existing = Installer.InstalledFolder();
            var info = new Label
            {
                Text = existing != null ? "An existing installation will be updated. Your layouts, presets and settings are kept."
                                        : "Installs for your Windows user only; no administrator rights needed.",
                ForeColor = Muted, AutoSize = true, Location = new Point(27, 96),
            };

            var folderLabel = new Label { Text = "Install folder", AutoSize = true, Location = new Point(27, 134) };
            folderBox.Text = existing ?? Installer.DefaultFolder;
            folderBox.Location = new Point(27, 156); folderBox.Width = 410;
            folderBox.BackColor = Field; folderBox.ForeColor = Fg; folderBox.BorderStyle = BorderStyle.FixedSingle;
            var browse = new Button { Text = "Browse...", Location = new Point(447, 154), Width = 86, Height = 26 };
            Style(browse, false);
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { SelectedPath = folderBox.Text, Description = "Install vibeRacingOverlays to:" })
                    if (d.ShowDialog(this) == DialogResult.OK) folderBox.Text = Path.Combine(d.SelectedPath, Path.GetFileName(d.SelectedPath).Equals(Installer.AppName, StringComparison.OrdinalIgnoreCase) ? "" : Installer.AppName);
            };

            desktop.Location = new Point(27, 196);
            launch.Location = new Point(27, 222);
            status.Location = new Point(27, 290); status.ForeColor = Muted;

            install.Location = new Point(ClientSize.Width - 2 * 110 - 34, ClientSize.Height - 50);
            cancel.Location = new Point(ClientSize.Width - 110 - 24, ClientSize.Height - 50);
            Style(install, true); Style(cancel, false);
            install.Click += (s, e) => DoInstall();
            cancel.Click += (s, e) => Close();
            AcceptButton = install; CancelButton = cancel;

            Controls.AddRange(new Control[] { title, sub, info, folderLabel, folderBox, browse, desktop, launch, status, install, cancel });
        }

        static void Style(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(60, 65, 72);
            b.BackColor = primary ? Accent : Field;
            b.ForeColor = Color.White;
        }

        /// <summary>The app must not run while its exe is replaced or removed.</summary>
        public static bool CloseRunningApp(bool quiet)
        {
            var running = Installer.RunningApp();
            if (running.Length == 0) return true;
            if (!quiet && MessageBox.Show("vibeRacingOverlays is running. Close it to continue?", "vibeRacingOverlays",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                return false;
            foreach (var p in running)
            {
                try { if (!p.CloseMainWindow() || !p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(3000); } }
                catch { }
            }
            return true;
        }

        void DoInstall()
        {
            string folder = folderBox.Text.Trim();
            if (folder.Length == 0) return;
            if (!CloseRunningApp(false)) return;

            install.Enabled = cancel.Enabled = false;
            try
            {
                Installer.Install(folder, desktop.Checked, msg => { status.Text = msg; status.Refresh(); });
                status.Text = "Installed.";
                if (launch.Checked)
                    Process.Start(new ProcessStartInfo(Path.Combine(folder, Installer.ExeName)) { WorkingDirectory = folder, UseShellExecute = true });
                else
                    MessageBox.Show(this, "vibeRacingOverlays has been installed.\nYou can start it from the Start menu.", "vibeRacingOverlays", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                status.Text = "";
                MessageBox.Show(this, "Installation failed:\n" + ex.Message, "vibeRacingOverlays", MessageBoxButtons.OK, MessageBoxIcon.Error);
                install.Enabled = cancel.Enabled = true;
            }
        }
    }
}
