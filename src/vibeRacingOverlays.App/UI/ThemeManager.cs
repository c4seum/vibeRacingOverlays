using System.Windows;

namespace vibeRacingOverlays.App.UI
{
    public enum AppTheme { Dark, Light }

    /// <summary>Swaps the color dictionary (App.xaml MergedDictionaries[0]); all UI uses DynamicResource.</summary>
    public static class ThemeManager
    {
        public static void Apply(AppTheme theme)
        {
            var dicts = Application.Current.Resources.MergedDictionaries;
            var dict = new ResourceDictionary { Source = new Uri("Themes/" + theme + ".xaml", UriKind.Relative) };
            if (dicts.Count > 0) dicts[0] = dict; else dicts.Add(dict);
        }
    }
}
