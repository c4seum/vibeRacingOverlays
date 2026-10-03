using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace vibeRacingOverlays.App.Core
{
    /// <summary>A release that is newer than the running version.</summary>
    public sealed class UpdateInfo
    {
        public string Version;
        public string Url;
    }

    /// <summary>
    /// Asks GitHub for the latest release and compares it with this version. Only reads public information
    /// (no account, nothing about the user is sent besides the request itself). Every failure (offline,
    /// GitHub down, repository not public) is silent: the check simply finds nothing.
    /// </summary>
    public static class UpdateCheck
    {
        // GitHub only answers for a public repository; while the repository is private the check finds nothing
        public const string Repository = "c4seum/vibeRacingOverlays";
        const string LatestRelease = "https://api.github.com/repos/" + Repository + "/releases/latest";

        /// <summary>
        /// Test feed (--update-feed): a URL or a local JSON file in GitHub's format ({"tag_name": "v1.4.0", "html_url": "..."}).
        /// Also turns the check on in DEV builds (their version number isn't a release number).
        /// </summary>
        public static string FeedOverride;

        public static bool Enabled(AppSettings s) { return FeedOverride != null || (s.CheckForUpdates && !BuildInfo.IsDev); }

        public static async Task<UpdateInfo> FindAsync()
        {
            try
            {
                string feed = FeedOverride ?? LatestRelease;
                string json;
                if (File.Exists(feed)) json = await File.ReadAllTextAsync(feed);
                else
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                    {
                        // GitHub's API refuses requests without a User-Agent
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("vibeRacingOverlays/" + BuildInfo.Version);
                        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                        var resp = await http.GetAsync(feed);
                        if (!resp.IsSuccessStatusCode) return null;
                        json = await resp.Content.ReadAsStringAsync();
                    }
                }
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
                    if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;
                    string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                    string url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
                    var latest = Parse(tag);
                    if (latest == null || latest <= Parse(BuildInfo.Version)) return null;
                    return new UpdateInfo { Version = latest.ToString(3), Url = IsWebLink(url) ? url : "https://github.com/" + Repository + "/releases/latest" };
                }
            }
            catch (Exception)
            {
                return null;   // no update notice is better than an error about it
            }
        }

        static Version Parse(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            Version v;
            if (!System.Version.TryParse(tag.Trim().TrimStart('v', 'V'), out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        // the link is opened in the browser: only accept a normal https page
        static bool IsWebLink(string url)
        {
            Uri u;
            return url != null && Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps;
        }

        public static void Open(UpdateInfo info)
        {
            if (info == null) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.Url) { UseShellExecute = true }); }
            catch (Exception) { }
        }
    }
}
