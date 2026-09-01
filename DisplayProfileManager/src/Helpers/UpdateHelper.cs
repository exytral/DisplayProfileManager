using DisplayProfileManager.Core;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DisplayProfileManager.Helpers
{
    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
    }

    public static class UpdateHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private const string ReleasesApi = "https://api.github.com/repos/exytral/DisplayProfileManager/releases/latest";
        private const string ReleasesPage = "https://github.com/exytral/DisplayProfileManager/releases/latest";

        private const int ReleaseDaysCooldown = 7;

        public static async Task<UpdateCheckResult> CheckAsync()
        {
            if (!SettingsManager.Instance.ShouldCheckForUpdates()) return null;

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("DisplayProfileManager");
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                    var json = await client.GetStringAsync(ReleasesApi);
                    var release = JObject.Parse(json);
                    var tag = release["tag_name"]?.ToString();

                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        logger.Debug("Update check: response carried no tag_name");
                        return null;
                    }

                    var latest = ParseVersion(tag);
                    var current = ParseVersion(AboutHelper.GetInformationalVersion());

                    if (latest == null || current == null)
                    {
                        logger.Debug($"Update check: could not compare '{tag}' against running version");
                        return null;
                    }

                    var isNewer = latest > current;
                    var clearedCooldown = IsPastCooldown(release["published_at"]);

                    var result = new UpdateCheckResult
                    {
                        UpdateAvailable = isNewer && clearedCooldown,
                        LatestVersion = NormalizeVersionTag(tag),
                        ReleaseUrl = ReleasesPage
                    };

                    if (isNewer && !clearedCooldown)
                        logger.Info($"Update check: {latest} is available but still inside {ReleaseDaysCooldown}-day cooldown window");
                    else
                        logger.Info($"Update check: running {current}, latest {latest}");

                    return result;
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Update check failed");
                return null;
            }
        }

        public static bool IsPastCooldown(JToken publishedAt)
        {
            if (publishedAt == null)
            {
                return true;
            }

            if (!DateTimeOffset.TryParse(publishedAt.ToString(), out var published))
            {
                return true;
            }

            return DateTimeOffset.UtcNow - published >= TimeSpan.FromDays(ReleaseDaysCooldown);
        }

        public static string NormalizeVersionTag(string tag) => tag?.TrimStart('v', 'V') ?? string.Empty;

        public static Version ParseVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var match = Regex.Match(raw, @"(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!match.Success) return null;

            int Part(int i) => match.Groups[i].Success ? int.Parse(match.Groups[i].Value) : 0;
            return new Version(Part(1), Part(2), Part(3), Part(4));
        }
    }
}