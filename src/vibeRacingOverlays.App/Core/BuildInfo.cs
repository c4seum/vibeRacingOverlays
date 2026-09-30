namespace vibeRacingOverlays.App.Core
{
    /// <summary>
    /// Local builds are DEV builds (own name and settings folder), so testing never touches the layouts of
    /// an installed release. Release builds (build-release.ps1 / GitHub Actions) pass -p:ReleaseBuild=true.
    /// </summary>
    public static class BuildInfo
    {
#if DEV
        public static readonly bool IsDev = true;
        public const string AppName = "vibeRacingOverlays DEV";
        public const string SettingsFolderName = "vibeRacingOverlays-dev";
#else
        public static readonly bool IsDev = false;   // not const: code that only runs in DEV builds would be "unreachable" (CS0162) in releases
        public const string AppName = "vibeRacingOverlays";
        public const string SettingsFolderName = "vibeRacingOverlays";
#endif

        public static string Version
        {
            get
            {
                var v = typeof(BuildInfo).Assembly.GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }
}
