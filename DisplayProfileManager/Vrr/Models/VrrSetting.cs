using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DisplayProfileManager.Vrr.Models
{
    public enum VrrMode
    {
        Off,
        On,
        OnIncludingWindowed
    }

    public class VrrSetting
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;
        [JsonProperty("mode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public VrrMode Mode { get; set; } = VrrMode.On;

        public override string ToString() => GetModeLabel(Mode);

        public static string GetModeLabel(VrrMode mode) => mode switch
        {
            VrrMode.Off => "Off",
            VrrMode.On => "On",
            VrrMode.OnIncludingWindowed => "On, including windowed",
            _ => mode.ToString()
        };
    }
}
