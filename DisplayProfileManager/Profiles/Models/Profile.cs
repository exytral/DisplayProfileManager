using System;
using System.Collections.Generic;

using Newtonsoft.Json;

using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Scripts.Models;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Profiles.Models
{
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
        [JsonProperty("createdDate")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        [JsonProperty("lastModifiedDate")]
        public DateTime LastModifiedDate { get; set; } = DateTime.Now;
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = 0;
        [JsonProperty("displaySettings")]
        public List<DisplaySetting> DisplaySettings { get; set; } = [];
        [JsonProperty("wallpaperSettings")]
        public WallpaperSettings WallpaperSettings { get; set; } = null;
        [JsonProperty("audioSettings")]
        public AudioSetting AudioSettings { get; set; } = new AudioSetting();
        [JsonProperty("scriptSettings")]
        public ScriptSettings ScriptSettings { get; set; } = new ScriptSettings();
        [JsonProperty("hotkeyConfig")]
        public HotkeyConfig HotkeyConfig { get; set; } = new HotkeyConfig();

        public Profile() { }

        public Profile(string name, string description = "")
        {
            Name = name;
            Description = description;
        }

        public void UpdateLastModified() => LastModifiedDate = DateTime.Now;

        internal Profile CreateWorkingCopy() => ProfileDraftFactory.CreateWorkingCopy(this);

        public override string ToString() => Name;
    }
}
