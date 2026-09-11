using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ProfileSchemaTests
    {
        private static Profile Deserialize(string json) => ProfileManager.DeserializeProfile(json);

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_UpstreamShapeWithoutSchema_RemainsReadable()
        {
            var json = @"{
                ""name"": ""Upstream"",
                ""isDefault"": true,
                ""displaySettings"": [
                    {
                        ""deviceName"": ""\\\\.\\DISPLAY1"",
                        ""width"": 1920,
                        ""height"": 1080,
                        ""frequency"": 60,
                        ""dpiScaling"": 100,
                        ""rotation"": 1
                    }
                ],
                ""audioSettings"": {
                    ""defaultPlaybackDeviceId"": ""device-id"",
                    ""playbackDeviceName"": ""Speakers"",
                    ""applyPlaybackDevice"": true
                }
            }";

            var profile = Deserialize(json);

            Assert.AreEqual(0, profile.SchemaVersion);
            Assert.AreEqual("Upstream", profile.Name);
            Assert.AreEqual(1, profile.DisplaySettings.Count);
            Assert.AreEqual(@"\\.\DISPLAY1", profile.DisplaySettings[0].DeviceName);
            Assert.AreEqual("device-id", profile.AudioSettings.DefaultPlaybackDeviceId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_LegacyV4Subsystems_PreserveEffectiveState()
        {
            var json = @"{
                ""schemaVersion"": 4,
                ""name"": ""Legacy"",
                ""enableWallpaper"": true,
                ""wallpaperSettings"": {
                    ""mode"": ""Solid"",
                    ""solidColorArgb"": 255
                },
                ""enableAudio"": true,
                ""audioSettings"": {
                    ""defaultPlaybackDeviceId"": ""device-id"",
                    ""playbackDeviceName"": ""Speakers"",
                    ""applyPlaybackDevice"": true
                },
                ""enableScripts"": true,
                ""scripts"": [
                    {
                        ""fileName"": ""legacy.ps1"",
                        ""arguments"": ""-Test"",
                        ""isEnabled"": true
                    }
                ]
            }";

            var profile = Deserialize(json);

            Assert.IsTrue(profile.WallpaperSettings.Enabled);
            Assert.AreEqual(WallpaperMode.Solid, profile.WallpaperSettings.Mode);
            Assert.IsTrue(profile.AudioSettings.Enabled);
            Assert.IsTrue(profile.AudioSettings.ApplyPlaybackDevice);
            Assert.IsTrue(profile.ScriptSettings.Enabled);
            Assert.AreEqual(1, profile.ScriptSettings.Scripts.Count);
            Assert.AreEqual("legacy.ps1", profile.ScriptSettings.Scripts[0].FileName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_LegacyEnabledWithoutUsableSettings_DoesNotBecomeActionable()
        {
            var json = @"{
                ""schemaVersion"": 4,
                ""name"": ""Legacy"",
                ""enableWallpaper"": true,
                ""wallpaperSettings"": null,
                ""enableAudio"": true,
                ""audioSettings"": null,
                ""enableScripts"": true
            }";

            var profile = Deserialize(json);

            Assert.IsFalse(profile.WallpaperSettings?.Enabled == true);
            Assert.IsFalse(profile.AudioSettings?.Enabled == true);
            Assert.IsTrue(profile.ScriptSettings.Enabled);
            Assert.IsFalse(profile.ScriptSettings.Scripts.Any());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_NestedValues_WinOverLegacyRootFields()
        {
            var json = @"{
                ""schemaVersion"": 5,
                ""name"": ""Mixed"",
                ""enableWallpaper"": true,
                ""wallpaperSettings"": {
                    ""enabled"": false,
                    ""mode"": ""Solid""
                },
                ""enableAudio"": true,
                ""audioSettings"": {
                    ""enabled"": false,
                    ""applyPlaybackDevice"": true
                },
                ""enableScripts"": true,
                ""scripts"": [
                    { ""fileName"": ""legacy.ps1"", ""isEnabled"": true }
                ],
                ""scriptSettings"": {
                    ""enabled"": false,
                    ""scripts"": [
                        { ""fileName"": ""nested.ps1"", ""isEnabled"": true }
                    ]
                }
            }";

            var profile = Deserialize(json);

            Assert.IsFalse(profile.WallpaperSettings.Enabled);
            Assert.IsFalse(profile.AudioSettings.Enabled);
            Assert.IsFalse(profile.ScriptSettings.Enabled);
            Assert.AreEqual(1, profile.ScriptSettings.Scripts.Count);
            Assert.AreEqual("nested.ps1", profile.ScriptSettings.Scripts[0].FileName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SerializeProfile_UsesNormalizedSubsystemShapeOnly()
        {
            var profile = new Profile("Normalized")
            {
                SchemaVersion = 5,
                WallpaperSettings = new WallpaperSettings
                {
                    Enabled = true,
                    Mode = WallpaperMode.Solid
                },
                AudioSettings = new AudioSetting
                {
                    Enabled = true,
                    ApplyPlaybackDevice = true
                },
                ScriptSettings = new ScriptSettings
                {
                    Enabled = true,
                    Scripts = new List<Script>
                    {
                        new Script("normalized.ps1")
                    }
                }
            };

            var root = JObject.Parse(JsonConvert.SerializeObject(profile));

            Assert.IsNull(root["enableWallpaper"]);
            Assert.IsNull(root["enableAudio"]);
            Assert.IsNull(root["enableScripts"]);
            Assert.IsNull(root["scripts"]);
            Assert.IsTrue(root["wallpaperSettings"]["enabled"].Value<bool>());
            Assert.IsTrue(root["audioSettings"]["enabled"].Value<bool>());
            Assert.IsTrue(root["scriptSettings"]["enabled"].Value<bool>());
            Assert.AreEqual("normalized.ps1", root["scriptSettings"]["scripts"][0]["fileName"].Value<string>());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedNestedScriptEntry_DropsOnlyThatEntry()
        {
            var json = @"{
                ""schemaVersion"": 5,
                ""name"": ""Recovery"",
                ""scriptSettings"": {
                    ""enabled"": true,
                    ""scripts"": [
                        { ""fileName"": ""valid.ps1"", ""isEnabled"": true },
                        ""not-an-object""
                    ]
                }
            }";

            var profile = Deserialize(json);

            Assert.IsTrue(profile.ScriptSettings.Enabled);
            Assert.AreEqual(1, profile.ScriptSettings.Scripts.Count);
            Assert.AreEqual("valid.ps1", profile.ScriptSettings.Scripts[0].FileName);
        }
    }
}
