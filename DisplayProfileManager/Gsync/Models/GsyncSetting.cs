using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DisplayProfileManager.Gsync.Models
{
    public enum GsyncMode
    {
        Off,
        Fullscreen,
        FullscreenAndWindowed
    }

    public class GsyncSetting
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;
        [JsonProperty("mode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public GsyncMode Mode { get; set; } = GsyncMode.Fullscreen;

        public override string ToString() => GetModeLabel(Mode);

        public static string GetModeLabel(GsyncMode mode) => mode switch
        {
            GsyncMode.Off => "Off",
            GsyncMode.Fullscreen => "Fullscreen only",
            GsyncMode.FullscreenAndWindowed => "Fullscreen and windowed",
            _ => mode.ToString()
        };
    }
}
