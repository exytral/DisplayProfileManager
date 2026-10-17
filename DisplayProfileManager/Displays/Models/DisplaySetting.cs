using System.Collections.Generic;

using Newtonsoft.Json;

namespace DisplayProfileManager.Displays.Models
{
    public class DisplaySetting
    {
        // Identity
        [JsonProperty("deviceName")]
        public string DeviceName { get; set; } = string.Empty;
        [JsonProperty("deviceString")]
        public string DeviceString { get; set; } = string.Empty;
        [JsonProperty("readableDeviceName")]
        public string ReadableDeviceName { get; set; } = string.Empty;
        [JsonProperty("manufacturerName")]
        public string ManufacturerName { get; set; } = string.Empty;
        [JsonProperty("productCodeID")]
        public string ProductCodeID { get; set; } = string.Empty;
        [JsonIgnore]
        public AdapterLuid AdapterLuid { get; set; }
        [JsonProperty("targetId")]
        public uint TargetId { get; set; } = 0;
        [JsonProperty("cloneGroupId")]
        public string CloneGroupId { get; set; } = string.Empty;
        [JsonProperty("isCloneSource")]
        public bool IsCloneSource { get; set; } = false;

        // State
        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; } = true;
        [JsonProperty("isPrimary")]
        public bool IsPrimary { get; set; } = false;

        // Layout
        [JsonProperty("displayPositionX")]
        public int DisplayPositionX { get; set; } = 0;
        [JsonProperty("displayPositionY")]
        public int DisplayPositionY { get; set; } = 0;

        // Configuration
        [JsonProperty("width")]
        public int Width { get; set; }
        [JsonProperty("height")]
        public int Height { get; set; }
        [JsonProperty("frequency")]
        public int Frequency { get; set; } = 60;
        [JsonProperty("rotation")]
        public int Rotation { get; set; } = 1;
        [JsonProperty("dpiScaling")]
        public uint DpiScaling { get; set; } = 100;
        [JsonIgnore]
        public bool IsHdrSupported { get; set; } = false;
        [JsonIgnore]
        public bool IsWcgSupported { get; set; } = false;
        [JsonProperty("lastKnownHdrSupported", NullValueHandling = NullValueHandling.Ignore)]
        public bool? LastKnownHdrSupported { get; set; } = null;
        [JsonProperty("lastKnownWcgSupported", NullValueHandling = NullValueHandling.Ignore)]
        public bool? LastKnownWcgSupported { get; set; } = null;
        [JsonProperty("isHdrEnabled")]
        public bool IsHdrEnabled { get; set; } = false;
        [JsonProperty("isWcgEnabled")]
        public bool IsWcgEnabled { get; set; } = false;
        [JsonProperty("colorProfile")]
        public string ColorProfile { get; set; } = null;

        // Clone
        [JsonIgnore] public bool OriginalSettings { get; set; } = false;
        [JsonIgnore] public int? OriginalPositionX { get; set; } = null;
        [JsonIgnore] public int? OriginalPositionY { get; set; } = null;
        [JsonIgnore] public bool? OriginalIsPrimary { get; set; } = null;
        [JsonIgnore] public int? OriginalWidth { get; set; } = null;
        [JsonIgnore] public int? OriginalHeight { get; set; } = null;
        [JsonIgnore] public int? OriginalFrequency { get; set; } = null;
        [JsonIgnore] public int? OriginalRotation { get; set; } = null;
        [JsonIgnore] public uint? OriginalDpiScaling { get; set; } = null;
        [JsonIgnore] public bool? OriginalIsHdrEnabled { get; set; } = null;
        [JsonIgnore] public bool? OriginalIsWcgEnabled { get; set; } = null;
        [JsonIgnore] public string OriginalColorProfile { get; set; } = null;

        // Native
        [JsonProperty("nativeWidth")]
        public int NativeWidth { get; set; } = 0;
        [JsonProperty("nativeHeight")]
        public int NativeHeight { get; set; } = 0;

        // Capabilities
        [JsonProperty("availableResolutions")]
        public List<string> AvailableResolutions { get; set; } = [];
        [JsonProperty("availableRefreshRates")]
        public Dictionary<string, List<int>> AvailableRefreshRates { get; set; } = [];
        [JsonProperty("availableDpiScaling")]
        public List<uint> AvailableDpiScaling { get; set; } = [];

        public DisplaySetting() { }

        public string ResolutionString() => $"{Width}x{Height} • {Frequency}Hz";

        public string DpiString() => $"{DpiScaling}%";

        public bool IsPartOfCloneGroup() => !string.IsNullOrEmpty(CloneGroupId);

        [JsonIgnore]
        public bool HasEdidIdentity => !string.IsNullOrEmpty(ManufacturerName) && !string.IsNullOrEmpty(ProductCodeID);

        public override string ToString()
        {
            var name = !string.IsNullOrEmpty(ReadableDeviceName) ? ReadableDeviceName : DeviceName;
            var hdrStatus = IsHdrSupported ? (IsHdrEnabled ? "HDR On" : "HDR Off") : "No HDR";
            var enabledStatus = IsEnabled ? "Enabled" : "Disabled";

            return $"{name}: {ResolutionString()}, DPI: {DpiString()}, {hdrStatus} [{enabledStatus}]";
        }
    }
}
