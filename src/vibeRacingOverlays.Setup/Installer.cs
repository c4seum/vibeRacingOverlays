using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace vibeRacingOverlays.Setup
{
    /// <summary>Per-user install (no admin): files, shortcuts and the "Apps & features" entry.</summary>
    internal static class Installer
    {
        public const string AppName = "vibeRacingOverlays";
        public const string ExeName = "vibeRacingOverlays.exe";
        public const string UninstallerName = "uninstall.exe";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\vibeRacingOverlays";

        public static string Version
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public static string DefaultFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName); }
        }

        static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); }
        }

        static string DesktopShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); }
        }

        /// <summary>Folder of an existing installation, or null.</summary>
        public static string InstalledFolder()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                return k != null ? k.GetValue("InstallLocation") as string : null;
        }

        public static Process[] RunningApp()
        {
            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName));
        }

        public static void Install(string folder, bool desktopShortcut, Action<string> progress)
        {
            progress("Copying files...");
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, ExeName);
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.exe"))
            {
                if (payload == null) throw new InvalidOperationException("This setup does not contain the application (payload missing).");
                string tmp = exe + ".new";
                using (var f = File.Create(tmp)) payload.CopyTo(f);
                if (File.Exists(exe)) File.Delete(exe);
                File.Move(tmp, exe);
            }

            // uninstaller: the small payload-less build of this program (falls back to a copy of the setup itself)
            string self = Assembly.GetExecutingAssembly().Location;
            string uninstaller = Path.Combine(folder, UninstallerName);
            using (var small = Assembly.GetExecutingAssembly().GetManifestResourceStream("uninstall.exe"))
            {
                if (small != null) using (var f = File.Create(uninstaller)) small.CopyTo(f);
                else if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase)) File.Copy(self, uninstaller, true);
            }

            progress("Creating shortcuts...");
            CreateShortcut(StartMenuShortcut, exe, folder);
            if (desktopShortcut) CreateShortcut(DesktopShortcut, exe, folder);
            else if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);

            progress("Registering...");
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", AppName);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", folder);
                k.SetValue("UninstallString", "\"" + uninstaller + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" --uninstall --quiet");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }

        /// <summary>Removes shortcuts, the registry entry and (after this process exits) the install folder.</summary>
        public static void Uninstall(bool removeUserSettings)
        {
            string folder = InstalledFolder() ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            foreach (var lnk in new[] { StartMenuShortcut, DesktopShortcut })
                if (File.Exists(lnk) && ShortcutPointsInto(lnk, folder)) File.Delete(lnk);

            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            string exe = Path.Combine(folder, ExeName);
            if (File.Exists(exe)) File.Delete(exe);

            if (removeUserSettings)
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
                if (Directory.Exists(data)) Directory.Delete(data, true);
            }

            // Only our own files are removed; the folder only when it is empty afterwards (it may be a
            // folder the user picked that contains other things). The running uninstaller removes itself
            // once this process has exited.
            string self = Assembly.GetExecutingAssembly().Location;
            string uninstaller = Path.Combine(folder, UninstallerName);
            if (string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + uninstaller + "\" & rmdir \"" + folder + "\"")
                {
                    CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            else
            {
                if (File.Exists(uninstaller)) File.Delete(uninstaller);
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
        }

        static bool ShortcutPointsInto(string lnk, string folder)
        {
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                string target = shell.CreateShortcut(lnk).TargetPath;
                return target.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static void CreateShortcut(string lnk, string target, string workingDir)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic s = shell.CreateShortcut(lnk);
            s.TargetPath = target;
            s.WorkingDirectory = workingDir;
            s.IconLocation = target + ",0";
            s.Description = "vibeRacingOverlays - iRacing overlays";
            s.Save();
        }
    }
}
