using System.IO;
using System.Runtime.InteropServices;

namespace vibeRacingOverlays.App.Overlay
{
    /// <summary>
    /// What the widgets need to know about the iRacing window: is it in front, and is it in (exclusive) full screen.
    /// iRacing's display mode is in Documents\iRacing\rendererDX11Monitor.ini, [Display]: fullScreen=1 is full screen,
    /// fullScreen=0 is a window (border=0: without frame, the usual "borderless" setup, also across triple screens).
    /// </summary>
    internal static class IRacingWindow
    {
        static readonly HashSet<string> SimProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "iRacingSim64DX11", "iRacingSim64" };
        static readonly Dictionary<uint, string> names = new Dictionary<uint, string>();

        public static string ProcessName(uint pid)
        {
            string name;
            if (names.TryGetValue(pid, out name)) return name;
            try { using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) name = p.ProcessName; }
            catch (Exception) { name = ""; }
            if (names.Count > 200) names.Clear();
            names[pid] = name;
            return name;
        }

        public static string ProcessOf(IntPtr hwnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return ProcessName(pid);
        }

        public static bool IsSim(IntPtr hwnd) { return hwnd != IntPtr.Zero && SimProcesses.Contains(ProcessOf(hwnd)); }

        /// <summary>iRacing is the window in front (you're driving or in its menus).</summary>
        public static bool InFront() { return IsSim(Native.GetForegroundWindow()); }

        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

        /// <summary>Windows reports a Direct3D app in exclusive full screen (nothing can be drawn over it).</summary>
        public static bool ExclusiveFullScreenNow()
        {
            int state;
            return SHQueryUserNotificationState(out state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN;
        }

        public static string IniFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iRacing", "rendererDX11Monitor.ini"); } }

        static DateTime iniTime;
        static string iniMode;

        /// <summary>"fullscreen", "window" or "borderless" from iRacing's settings file (null when unknown). Re-read when the file changes.</summary>
        public static string ModeSetting()
        {
            try
            {
                var f = new FileInfo(IniFile);
                if (!f.Exists) return null;
                if (f.LastWriteTimeUtc == iniTime) return iniMode;
                iniTime = f.LastWriteTimeUtc;
                string full = null, border = null;
                bool display = false;
                foreach (var raw in File.ReadLines(f.FullName))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[")) { display = line == "[Display]"; continue; }
                    if (!display) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Split(';')[0].Trim();
                    if (key == "fullScreen") full = val;
                    else if (key == "border") border = val;
                }
                iniMode = full == "1" ? "fullscreen" : full == "0" ? (border == "1" ? "window" : "borderless") : null;
                return iniMode;
            }
            catch (Exception) { return null; }
        }

        /// <summary>The widgets can't be shown over iRacing: it runs in exclusive full screen.</summary>
        public static bool BlocksWidgets()
        {
            return ExclusiveFullScreenNow() && InFront() || ModeSetting() == "fullscreen";
        }
    }
}
