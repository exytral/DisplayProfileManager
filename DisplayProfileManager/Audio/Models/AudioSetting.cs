using System.Collections.Generic;

using Newtonsoft.Json;

namespace DisplayProfileManager.Audio.Models
{
    public class AudioSetting
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;
        [JsonProperty("defaultPlaybackDeviceId")]
        public string DefaultPlaybackDeviceId { get; set; } = string.Empty;
        [JsonProperty("defaultCaptureDeviceId")]
        public string DefaultCaptureDeviceId { get; set; } = string.Empty;
        [JsonProperty("playbackDeviceName")]
        public string PlaybackDeviceName { get; set; } = string.Empty;
        [JsonProperty("captureDeviceName")]
        public string CaptureDeviceName { get; set; } = string.Empty;
        [JsonProperty("applyPlaybackDevice")]
        public bool ApplyPlaybackDevice { get; set; } = false;
        [JsonProperty("applyCaptureDevice")]
        public bool ApplyCaptureDevice { get; set; } = false;

        public AudioSetting() { }

        public AudioSetting(string playbackId, string playbackName, string captureId, string captureName)
        {
            DefaultPlaybackDeviceId = playbackId;
            PlaybackDeviceName = playbackName;
            DefaultCaptureDeviceId = captureId;
            CaptureDeviceName = captureName;
        }

        public bool HasPlaybackDevice() => !string.IsNullOrEmpty(DefaultPlaybackDeviceId);

        public bool HasCaptureDevice() => !string.IsNullOrEmpty(DefaultCaptureDeviceId);

        public override string ToString()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(PlaybackDeviceName))
                parts.Add($"Output: {PlaybackDeviceName}");
            if (!string.IsNullOrEmpty(CaptureDeviceName))
                parts.Add($"Input: {CaptureDeviceName}");

            return parts.Count > 0 ? string.Join(", ", parts) : "No audio devices configured";
        }
    }
}
