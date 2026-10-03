using System.IO;
using System.Text;

namespace vibeRacingOverlays.App.Overlay
{
    /// <summary>
    /// Diagnostics for --zorder-log &lt;file&gt;: one line every 2 seconds with the window in front, iRacing's window and
    /// display mode, and per widget whether something lies over it and whether it was lifted. Used to see how the widgets
    /// behave with iRacing in full screen, in a window and borderless.
    /// </summary>
    internal static class ZOrderDiag
    {
        public static string Describe(IntPtr h)
        {
            if (h == IntPtr.Zero) return "-";
            var cls = new StringBuilder(128);
            Native.GetClassName(h, cls, cls.Capacity);
            Native.RECT r;
            Native.GetWindowRect(h, out r);
            bool top = (Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
            // process and window class only: titles of other programs (web pages, documents) don't belong in a log
            return IRacingWindow.ProcessOf(h) + " [" + cls + "] " + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height + (top ? " topmost" : "");
        }

        static IntPtr SimWindow()
        {
            foreach (var name in new[] { "iRacingSim64DX11", "iRacingSim64" })
            {
                var ps = System.Diagnostics.Process.GetProcessesByName(name);
                try { if (ps.Length > 0) return ps[0].MainWindowHandle; }
                finally { foreach (var p in ps) p.Dispose(); }
            }
            return IntPtr.Zero;
        }

        public static string State(bool lifting)
        {
            var sim = SimWindow();
            string simInfo = sim == IntPtr.Zero ? "not running" : Describe(sim) + " style=" + Native.GetWindowLong(sim, Native.GWL_STYLE).ToString("X8");
            return DateTime.Now.ToString("HH:mm:ss") + " | front: " + Describe(Native.GetForegroundWindow())
                + " | iRacing: " + simInfo + " ini=" + (IRacingWindow.ModeSetting() ?? "?") + (IRacingWindow.ExclusiveFullScreenNow() ? " EXCLUSIVE-FULLSCREEN" : "")
                + " | lift " + (lifting ? "on" : "off");
        }

        public static void Write(string file, string line)
        {
            try { File.AppendAllText(file, line + Environment.NewLine); }
            catch (Exception) { }
        }
    }
}
