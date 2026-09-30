using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace vibeRacingOverlays.App.UI
{
    public enum AppTheme { Dark, Light }

    /// <summary>Swaps the color dictionary (App.xaml MergedDictionaries[0]); all UI uses DynamicResource.</summary>
    public static class ThemeManager
    {
        public static AppTheme Current { get; private set; } = AppTheme.Dark;

        public static void Apply(AppTheme theme)
        {
            Current = theme;
            var dicts = Application.Current.Resources.MergedDictionaries;
            var dict = new ResourceDictionary { Source = new Uri("Themes/" + theme + ".xaml", UriKind.Relative) };
            if (dicts.Count > 0) dicts[0] = dict; else dicts.Add(dict);
            foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        /// <summary>Dark or light Windows title bar to match the theme (Windows 10 20H1+ / 11; ignored elsewhere).</summary>
        public static void ApplyTitleBar(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = Current == AppTheme.Dark ? 1 : 0;
            try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)); } catch { }
        }
    }
}
