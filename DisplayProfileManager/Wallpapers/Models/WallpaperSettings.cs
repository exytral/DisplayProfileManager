using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DisplayProfileManager.Wallpapers.Models
{
    public enum WallpaperMode
    {
        Unknown,
        Solid,
        Picture,
        Slideshow,
        Spotlight
    }

    public class WallpaperSettings
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonProperty("mode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public WallpaperMode Mode { get; set; } = WallpaperMode.Unknown;

        [JsonProperty("solidColorArgb")]
        public uint SolidColorArgb { get; set; } = 0;

        [JsonProperty("position")]
        public string Position { get; set; } = "fill";

        [JsonProperty("perMonitor")]
        public Dictionary<string, MonitorWallpaper> PerMonitor { get; set; } = [];

        [JsonProperty("slideshowConfig")]
        public SlideshowConfig SlideshowConfig { get; set; } = null;
    }

    public class MonitorWallpaper
    {
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("monitorId", NullValueHandling = NullValueHandling.Ignore)]
        public string MonitorId { get; set; }
    }

    public class SlideshowConfig
    {
        [JsonProperty("intervalSeconds")]
        public uint IntervalSeconds { get; set; } = 1800;

        [JsonProperty("shuffle")]
        public bool Shuffle { get; set; } = false;

        [JsonProperty("sourcePaths")]
        public List<string> SourcePaths { get; set; } = [];
    }
}
