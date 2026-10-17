using System.Collections.Generic;

using Newtonsoft.Json;

namespace DisplayProfileManager.Scripts.Models
{
    public class ScriptSettings
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;
        [JsonProperty("scripts")]
        public List<Script> Scripts { get; set; } = [];
    }

    public class Script
    {
        [JsonProperty("fileName")]
        public string FileName { get; set; } = string.Empty;
        [JsonProperty("arguments")]
        public string Arguments { get; set; } = string.Empty;
        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        public Script() { }

        public Script(string fileName, string arguments = "", bool isEnabled = true)
        {
            FileName = fileName;
            Arguments = arguments;
            IsEnabled = isEnabled;
        }

        public override string ToString()
        {
            string quoted = FileName.Contains(" ") ? $"\"{FileName}\"" : FileName;
            return string.IsNullOrWhiteSpace(Arguments) ? quoted : $"{quoted} {Arguments.Trim()}";
        }
    }
}
