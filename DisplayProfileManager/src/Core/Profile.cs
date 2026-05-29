using DisplayProfileManager.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace DisplayProfileManager.Core
{
    #region Profile

    public class Profile
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
        [JsonProperty("icon")]
        public string Icon { get; set; } = null;
        [JsonProperty("isDefault")]
        public bool IsDefault { get; set; } = false;
        [JsonProperty("createdDate")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        [JsonProperty("lastModifiedDate")]
        public DateTime LastModifiedDate { get; set; } = DateTime.Now;
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = 0;
        [JsonProperty("displaySettings")]
        public List<DisplaySetting> DisplaySettings { get; set; } = new List<DisplaySetting>();
        [JsonProperty("enableAudio")]
        public bool EnableAudio { get; set; } = true;
        [JsonProperty("audioSettings")]
        public AudioSetting AudioSettings { get; set; } = new AudioSetting();
        [JsonProperty("enableScripts")]
        public bool EnableScripts { get; set; } = false;
        [JsonProperty("scripts")]
        [JsonConverter(typeof(ScriptListConverter))]
        public List<Script> Scripts { get; set; } = new List<Script>();
        [JsonProperty("hotkeyConfig")]
        public HotkeyConfig HotkeyConfig { get; set; } = new HotkeyConfig();

        public Profile() { }

        public Profile(string name, string description = "")
        {
            Name = name;
            Description = description;
        }

        public void UpdateLastModified() => LastModifiedDate = DateTime.Now;

        public override string ToString() => Name;
    }

    #endregion

    #region DisplaySetting

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
        [JsonProperty("serialNumberID")]
        public string SerialNumberID { get; set; } = string.Empty;
        [JsonIgnore]
        public DisplayConfigHelper.LUID AdapterLuid { get; set; }
        [JsonProperty("adapterId")]
        public string AdapterId { get; set; } = string.Empty;
        [JsonProperty("targetId")]
        public uint TargetId { get; set; } = 0;
        [JsonProperty("sourceId")]
        public uint SourceId { get; set; } = 0;
        [JsonProperty("cloneGroupId")]
        public string CloneGroupId { get; set; } = string.Empty;
        [JsonProperty("isCloneSource")]
        public bool IsCloneSource { get; set; } = false;
        [JsonProperty("pathIndex")]
        public uint PathIndex { get; set; } = 0;

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
        [JsonProperty("isHdrSupported")]
        public bool IsHdrSupported { get; set; } = false;
        [JsonProperty("isHdrEnabled")]
        public bool IsHdrEnabled { get; set; } = false;
        [JsonProperty("isAcmEnabled")]
        public bool IsAcmEnabled { get; set; } = false;
        [JsonProperty("colorProfile")]
        public string ColorProfile { get; set; } = null;

        // Clone
        [JsonIgnore] public int? OriginalPositionX { get; set; } = null;
        [JsonIgnore] public int? OriginalPositionY { get; set; } = null;
        [JsonIgnore] public uint? OriginalSourceId { get; set; } = null;
        [JsonIgnore] public int? OriginalWidth { get; set; } = null;
        [JsonIgnore] public int? OriginalHeight { get; set; } = null;
        [JsonIgnore] public int? OriginalFrequency { get; set; } = null;
        [JsonIgnore] public bool? OriginalIsPrimary { get; set; } = null;
        [JsonIgnore] public uint? OriginalDpiScaling { get; set; } = null;
        [JsonIgnore] public int? OriginalRotation { get; set; } = null;
        [JsonIgnore] public string OriginalColorProfile { get; set; } = null;
        [JsonIgnore] public bool? OriginalIsHdrEnabled { get; set; } = null;
        [JsonIgnore] public bool? OriginalIsAcmEnabled { get; set; } = null;

        // Native
        [JsonProperty("nativeWidth")]
        public int NativeWidth { get; set; } = 0;
        [JsonProperty("nativeHeight")]
        public int NativeHeight { get; set; } = 0;

        // Capabilities
        [JsonProperty("availableResolutions")]
        public List<string> AvailableResolutions { get; set; } = new List<string>();
        [JsonProperty("availableRefreshRates")]
        public Dictionary<string, List<int>> AvailableRefreshRates { get; set; } = new Dictionary<string, List<int>>();
        [JsonProperty("availableDpiScaling")]
        public List<uint> AvailableDpiScaling { get; set; } = new List<uint>();

        public DisplaySetting() { }

        public string GetResolutionString() => $"{Width}x{Height} • {Frequency}Hz";

        public string GetDpiString() => $"{DpiScaling}%";

        public override string ToString()
        {
            var name = !string.IsNullOrEmpty(ReadableDeviceName) ? ReadableDeviceName : DeviceName;
            var hdrStatus = IsHdrSupported ? (IsHdrEnabled ? "HDR On" : "HDR Off") : "No HDR";
            var enabledStatus = IsEnabled ? "Enabled" : "Disabled";

            return $"{name}: {GetResolutionString()}, DPI: {GetDpiString()}, {hdrStatus} [{enabledStatus}]";
        }

        public void UpdateDeviceNameFromWMI(List<DisplayHelper.MonitorIdInfo> monitorIds = null, List<DisplayConfigHelper.DisplayConfigInfo> displayConfigs = null)
        {
            string resolvedDeviceName = DisplayHelper.GetDeviceNameFromWMIMonitorID(ManufacturerName, ProductCodeID, SerialNumberID, monitorIds, displayConfigs);
            if (string.IsNullOrEmpty(resolvedDeviceName))
                resolvedDeviceName = DeviceName;

            DeviceName = resolvedDeviceName;
        }

        public bool IsPartOfCloneGroup() => !string.IsNullOrEmpty(CloneGroupId);
    }

    #endregion

    #region AudioSetting

    public class AudioSetting
    {
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

    #endregion

    #region ScriptListConverter

    public class ScriptListConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            objectType == typeof(List<Script>);

        public override object ReadJson(JsonReader reader, Type objectType,
        object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type == JTokenType.Null) return new List<Script>();

            var list = new List<Script>();
            foreach (var item in token.Children())
            {
                if (item.Type == JTokenType.String)
                {
                    var parsed = ScriptHelper.ParseScriptString(item.Value<string>());
                    list.Add(new Script
                    {
                        FileName = parsed.Path,
                        Arguments = parsed.Args,
                        IsEnabled = true
                    });
                }
                else if (item.Type == JTokenType.Object)
                    list.Add(item.ToObject<Script>(serializer));
            }

            return list;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => serializer.Serialize(writer, value);
    }

    #endregion
}