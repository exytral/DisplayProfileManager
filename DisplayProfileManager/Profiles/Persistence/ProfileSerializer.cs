using System;
using System.IO;
using System.Linq;

using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Common;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Scripts.Models;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Profiles.Persistence
{
    public static class ProfileSerializer
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        public const int CurrentSchemaVersion = 6;

        public static Profile DeserializeProfile(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var root = JObject.Parse(json);

            RecoverOptionalProperty(root, "schemaVersion", typeof(int));
            ReconcileProfileSchema(root);

            RecoverScriptEntries(root);
            RecoverOptionalProperty(root, "audioSettings", typeof(AudioSetting));
            RecoverOptionalProperty(root, "wallpaperSettings", typeof(WallpaperSettings));
            RecoverOptionalProperty(root, "scriptSettings", typeof(ScriptSettings));
            RecoverOptionalProperty(root, "hotkeyConfig", typeof(HotkeyConfig));
            RecoverOptionalProperty(root, "enableWallpaper", typeof(bool));
            RecoverOptionalProperty(root, "enableAudio", typeof(bool));
            RecoverOptionalProperty(root, "enableScripts", typeof(bool));
            RecoverOptionalProperty(root, "name", typeof(string));
            RecoverOptionalProperty(root, "description", typeof(string));
            RecoverOptionalProperty(root, "icon", typeof(string));
            RecoverOptionalProperty(root, "createdDate", typeof(DateTime));
            RecoverOptionalProperty(root, "lastModifiedDate", typeof(DateTime));

            NormalizeAdvancedColorSchema(root);
            NormalizeSubsystemSettings(root);
            return root.ToObject<Profile>();
        }

        private static void RecoverOptionalProperty(JObject root, string propertyName, Type targetType)
        {
            var token = root[propertyName];
            if (token == null) return;

            try
            {
                token.ToObject(targetType);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Profile member '{propertyName}' could not be read -> using its default: {ex.Message}");
                root.Remove(propertyName);
            }
        }

        private static void RecoverScriptEntries(JObject root)
        {
            RecoverScriptEntries(root, "scripts");

            if (root["scriptSettings"] is JObject scriptSettings)
                RecoverScriptEntries(scriptSettings, "scripts");
        }

        private static void RecoverScriptEntries(JObject owner, string propertyName)
        {
            var token = owner[propertyName];
            if (token == null) return;

            if (token is not JArray scripts)
            {
                owner.Remove(propertyName);
                return;
            }

            for (int i = scripts.Count - 1; i >= 0; i--)
            {
                if (scripts[i].Type != JTokenType.Object)
                {
                    scripts.RemoveAt(i);
                    continue;
                }

                try
                {
                    scripts[i].ToObject<Script>();
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Profile script entry at index {i} could not be read -> dropping it: {ex.Message}");
                    scripts.RemoveAt(i);
                }
            }
        }

        internal static int ReconcileProfileSchema(JObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            int declaredSchema = root["schemaVersion"]?.Value<int>() ?? 0;
            if (declaredSchema > CurrentSchemaVersion) throw new InvalidDataException($"Profile schema {declaredSchema} is newer than supported schema {CurrentSchemaVersion}.");

            int effectiveSchema = declaredSchema;

            if (root["isDefault"] != null)
                effectiveSchema = Math.Min(effectiveSchema, 3);

            if (root["enableWallpaper"] != null || root["enableAudio"] != null || root["enableScripts"] != null || root["scripts"] != null)
                effectiveSchema = Math.Min(effectiveSchema, 4);

            if (root["displaySettings"] is JArray displaySettings && displaySettings.OfType<JObject>().Any(setting => setting["isAcmEnabled"] != null))
                effectiveSchema = Math.Min(effectiveSchema, 5);

            if (effectiveSchema != declaredSchema)
                root["schemaVersion"] = effectiveSchema;

            return effectiveSchema;
        }

        internal static bool NeedsProfileMigration(Profile profile) => profile != null && profile.SchemaVersion < CurrentSchemaVersion;

        internal static bool CanPersistProfileSchema(Profile profile) => profile != null && profile.SchemaVersion == CurrentSchemaVersion;

        private static void NormalizeAdvancedColorSchema(JObject root)
        {
            int schemaVersion = root["schemaVersion"]?.Value<int>() ?? 0;
            if (schemaVersion >= 6 || root["displaySettings"] is not JArray displaySettings) return;

            foreach (var displaySetting in displaySettings.OfType<JObject>())
            {
                bool hdrEnabled = displaySetting["isHdrEnabled"]?.Value<bool>() ?? false;
                bool legacyWcgEnabled = displaySetting["isAcmEnabled"]?.Value<bool>() ?? false;

                displaySetting["isWcgEnabled"] = !hdrEnabled && legacyWcgEnabled;
                displaySetting.Remove("isAcmEnabled");
            }
        }

        private static void NormalizeSubsystemSettings(JObject root)
        {
            if (root["wallpaperSettings"] is JObject wallpaperSettings && wallpaperSettings["enabled"] == null)
                wallpaperSettings["enabled"] = root["enableWallpaper"]?.Value<bool>() ?? false;

            if (root["audioSettings"] is JObject audioSettings && audioSettings["enabled"] == null)
                audioSettings["enabled"] = root["enableAudio"]?.Value<bool>() ?? false;

            if (root["scriptSettings"] is not JObject scriptSettings)
            {
                scriptSettings = [];
                root["scriptSettings"] = scriptSettings;
            }

            if (scriptSettings["enabled"] == null)
                scriptSettings["enabled"] = root["enableScripts"]?.Value<bool>() ?? false;

            if (scriptSettings["scripts"] == null && root["scripts"] is JArray legacyScripts)
                scriptSettings["scripts"] = legacyScripts.DeepClone();

            root.Remove("enableWallpaper");
            root.Remove("enableAudio");
            root.Remove("enableScripts");
            root.Remove("scripts");
        }

        public static string SerializeProfile(Profile profile) => JsonConvert.SerializeObject(profile, Formatting.Indented);
    }
}
