namespace DisplayProfileManager.Application
{
    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
    }
}
