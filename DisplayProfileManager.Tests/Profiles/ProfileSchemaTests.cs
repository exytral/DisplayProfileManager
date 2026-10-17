using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Scripts.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileSchemaTests
    {
        private static Profile Deserialize(string json) => ProfileSerializer.DeserializeProfile(json);

        private static ProfileService CreateIsolatedManager(
            string appDataFolder,
            List<DisplayConfigInfo> active = null,
            List<DisplayConfigInfo> addresses = null)
        {
            var settings = new SettingsService(new SettingsStore(appDataFolder));
            return new ProfileService(
                new ProfileStore(appDataFolder),
                new ProfileMigrationService(settings),
                new ProfileDraftFactory(),
                new ProfileCaptureService(),
                settings,
                () => active ?? [],
                () => addresses ?? active ?? []);
        }

        private static DisplayConfigInfo Address(
            uint targetId,
            int nativeWidth,
            int nativeHeight,
            bool enabled = true,
            uint adapterLowPart = 2,
            string manufacturer = "",
            string product = "")
        {
            var info = new DisplayConfigInfoBuilder()
                .WithTargetId(targetId)
                .WithRawTargetId(targetId)
                .WithSourceId(9)
                .WithNativeResolution(nativeWidth, nativeHeight)
                .Enabled(enabled)
                .WithEdid(manufacturer, product)
                .Build();
            info.AdapterId = new AdapterLuid { LowPart = adapterLowPart };
            return info;
        }

        private static readonly string[] CurrentProfilePropertyNames =
        [
            "id",
            "name",
            "description",
            "icon",
            "createdDate",
            "lastModifiedDate",
            "schemaVersion",
            "displaySettings",
            "wallpaperSettings",
            "audioSettings",
            "scriptSettings",
            "hotkeyConfig"
        ];

        private static void AssertCurrentProfileShape(JObject root)
        {
            Assert.AreEqual(6, root["schemaVersion"]?.Value<int>());
            CollectionAssert.AreEqual(CurrentProfilePropertyNames, root.Properties().Select(property => property.Name).ToArray());
        }

        private static string CreateTempAppDataFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), "DPM-ProfileSchemaTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string ManagedProfilePath(string appDataFolder, string profileId) => Path.Combine(appDataFolder, "Profiles", $"{profileId}.dpm");

        private static void AssertCanonicalSchema6Migration(string profilePath)
        {
            var root = JObject.Parse(File.ReadAllText(profilePath));
            AssertCurrentProfileShape(root);
            Assert.IsNull(root["displaySettings"]?[0]?["adapterId"]);
            Assert.IsNull(root["displaySettings"]?[0]?["isAcmEnabled"]);
            Assert.IsTrue(root["displaySettings"]?[0]?["isWcgEnabled"]?.Value<bool>() == true);
            Assert.IsFalse(root["wallpaperSettings"]?["enabled"]?.Value<bool>() == true);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SerializeProfile_CurrentSchema_UsesCanonicalProfileAndDisplayShape()
        {
            var setting = new DisplaySettingBuilder()
                .WithTargetId(33)
                .WithEdid("MAN", "C003")
                .Build();
            setting.AdapterLuid = new AdapterLuid { LowPart = 2 };
            setting.IsHdrSupported = true;
            setting.IsWcgSupported = true;
            setting.LastKnownHdrSupported = true;
            setting.LastKnownWcgSupported = false;
            setting.IsHdrEnabled = true;
            var profile = ProfileDraftFactory.CreateNewProfile("Current");
            profile.DisplaySettings.Add(setting);

            var root = JObject.Parse(JsonConvert.SerializeObject(profile));
            var display = (JObject)root["displaySettings"][0];

            AssertCurrentProfileShape(root);
            Assert.IsNull(display["adapterId"]);
            Assert.IsNull(display["sourceId"]);
            Assert.IsNull(display["pathIndex"]);
            Assert.IsNull(display["hasEdidIdentity"]);
            Assert.IsNull(display["isHdrSupported"]);
            Assert.IsNull(display["isWcgSupported"]);
            Assert.IsTrue(display["lastKnownHdrSupported"]?.Value<bool>() == true);
            Assert.IsTrue(display["lastKnownWcgSupported"]?.Value<bool>() == false);
            Assert.IsTrue(display["isHdrEnabled"]?.Value<bool>() == true);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MissingAdvancedColorCapabilityEvidence_RemainsUnknown()
        {
            var profile = Deserialize(@"{ ""schemaVersion"": 6, ""name"": ""Unknown capability"", ""displaySettings"": [ { ""isHdrEnabled"": true, ""isWcgEnabled"": false } ] }");

            var setting = profile.DisplaySettings[0];

            Assert.IsNull(setting.LastKnownHdrSupported);
            Assert.IsNull(setting.LastKnownWcgSupported);
            Assert.IsFalse(setting.IsHdrSupported);
            Assert.IsFalse(setting.IsWcgSupported);
            Assert.IsTrue(setting.IsHdrEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SerializeDeserialize_AdvancedColorCapabilityEvidence_RoundTripsWithoutBecomingLiveSupport()
        {
            var profile = ProfileDraftFactory.CreateNewProfile("Capability history");
            profile.DisplaySettings.Add(new DisplaySetting
            {
                LastKnownHdrSupported = true,
                LastKnownWcgSupported = false,
                IsHdrEnabled = true
            });

            var roundTrip = Deserialize(JsonConvert.SerializeObject(profile));
            var setting = roundTrip.DisplaySettings[0];

            Assert.AreEqual(true, setting.LastKnownHdrSupported);
            Assert.AreEqual(false, setting.LastKnownWcgSupported);
            Assert.IsFalse(setting.IsHdrSupported);
            Assert.IsFalse(setting.IsWcgSupported);
            Assert.IsTrue(setting.IsHdrEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_UnknownAdapterId_LoadsButReserializationDropsField()
        {
            var profile = Deserialize(@"{
                ""schemaVersion"": 6,
                ""name"": ""Old adapter field"",
                ""displaySettings"": [
                    { ""adapterId"": ""0000000000000111"", ""targetId"": 33, ""sourceId"": 7, ""pathIndex"": 9 }
                ]
            }");

            var root = JObject.Parse(JsonConvert.SerializeObject(profile));

            Assert.IsNotNull(profile);
            Assert.AreEqual(33u, profile.DisplaySettings[0].TargetId);
            Assert.IsNull(root["displaySettings"]?[0]?["adapterId"]);
            Assert.IsNull(root["displaySettings"]?[0]?["sourceId"]);
            Assert.IsNull(root["displaySettings"]?[0]?["pathIndex"]);
        }

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
                SchemaVersion = 6,
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
                    Scripts =
                    [
                        new Script("normalized.ps1")
                    ]
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
        public void DeserializeProfile_PreV6Sdr_MigratesLegacyAcmFieldToWcg()
        {
            var profile = Deserialize(@"{
                ""schemaVersion"": 5,
                ""name"": ""Legacy WCG"",
                ""displaySettings"": [
                    { ""isHdrEnabled"": false, ""isAcmEnabled"": true }
                ]
            }");

            Assert.IsTrue(profile.DisplaySettings[0].IsWcgEnabled);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        [TestCategory("Unit")]
        public void DeserializeProfile_PreV6Hdr_CollapsesLegacyAcmVariants(bool legacyAcm)
        {
            var profile = Deserialize($@"{{
                ""schemaVersion"": 5,
                ""name"": ""Legacy HDR"",
                ""displaySettings"": [
                    {{ ""isHdrEnabled"": true, ""isAcmEnabled"": {legacyAcm.ToString().ToLowerInvariant()} }}
                ]
            }}");

            Assert.IsTrue(profile.DisplaySettings[0].IsHdrEnabled);
            Assert.IsFalse(profile.DisplaySettings[0].IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SerializeProfile_V6_UsesWcgFieldAndDoesNotWriteLegacyAcmField()
        {
            var profile = new Profile("WCG")
            {
                SchemaVersion = 6,
                DisplaySettings =
                [
                    new DisplaySetting { IsWcgEnabled = true }
                ]
            };

            var root = JObject.Parse(JsonConvert.SerializeObject(profile));
            var display = (JObject)root["displaySettings"][0];

            Assert.IsTrue(display["isWcgEnabled"].Value<bool>());
            Assert.IsNull(display["isAcmEnabled"]);
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

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_CurrentSchemaShape_RemainsCurrent()
        {
            var profile = Deserialize(@"{ ""schemaVersion"": 6, ""name"": ""Current"", ""displaySettings"": [ {} ] }");

            Assert.AreEqual(6, profile.SchemaVersion);
            Assert.IsFalse(ProfileSerializer.NeedsProfileMigration(profile));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_DeclaredV6WithLegacyAcm_ReconcilesToSchema5()
        {
            var profile = Deserialize(@"{
                ""schemaVersion"": 6,
                ""name"": ""Mixed legacy"",
                ""wallpaperSettings"": { ""enabled"": true, ""mode"": ""Solid"" },
                ""displaySettings"": [ { ""isHdrEnabled"": false, ""isAcmEnabled"": true } ]
            }");

            Assert.AreEqual(5, profile.SchemaVersion);
            Assert.IsTrue(profile.DisplaySettings[0].IsWcgEnabled);
            Assert.IsTrue(ProfileSerializer.NeedsProfileMigration(profile));
            Assert.IsTrue(profile.WallpaperSettings.Enabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_DeclaredV6WithLegacySubsystemShape_ReconcilesToV4()
        {
            var profile = Deserialize(@"{
                ""schemaVersion"": 6,
                ""name"": ""Legacy subsystem"",
                ""enableAudio"": true,
                ""audioSettings"": { ""applyPlaybackDevice"": true },
                ""displaySettings"": []
            }");

            Assert.AreEqual(4, profile.SchemaVersion);
            Assert.IsTrue(profile.AudioSettings.Enabled);
            Assert.IsTrue(profile.AudioSettings.ApplyPlaybackDevice);
            Assert.IsTrue(ProfileSerializer.NeedsProfileMigration(profile));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_DeclaredV6WithLegacyDefaultMarker_ReconcilesToV3()
        {
            var profile = Deserialize(@"{ ""schemaVersion"": 6, ""name"": ""Legacy default"", ""isDefault"": true, ""displaySettings"": [] }");

            Assert.AreEqual(3, profile.SchemaVersion);
            Assert.IsTrue(ProfileSerializer.NeedsProfileMigration(profile));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MultipleLegacyMarkers_UsesEarliestProvenSchema()
        {
            var profile = Deserialize(@"{
                ""schemaVersion"": 6,
                ""name"": ""Mixed legacy"",
                ""isDefault"": true,
                ""enableScripts"": true,
                ""scripts"": [],
                ""displaySettings"": [ { ""isAcmEnabled"": false } ]
            }");

            Assert.AreEqual(3, profile.SchemaVersion);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MissingOptionalCurrentFields_DoesNotLowerSchema()
        {
            var profile = Deserialize(@"{ ""schemaVersion"": 6, ""name"": ""Sparse current"", ""displaySettings"": [] }");

            Assert.AreEqual(6, profile.SchemaVersion);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_FutureSchema_FailsClosed()
        {
            Assert.ThrowsExactly<InvalidDataException>(() => Deserialize(@"{ ""schemaVersion"": 7, ""name"": ""Future"", ""displaySettings"": [] }"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LoadProfilesAsync_Schema5InactivePresent_RefreshesPreferredNativeMetadata()
        {
            string appDataFolder = CreateTempAppDataFolder();
            try
            {
                string profileId = Guid.NewGuid().ToString();
                var inactive = Address(33, 1800, 900, enabled: false);
                var manager = CreateIsolatedManager(
                    appDataFolder,
                    [],
                    [inactive]);
                string profilePath = ManagedProfilePath(appDataFolder, profileId);
                File.WriteAllText(profilePath, $@"{{
                    ""schemaVersion"": 5,
                    ""id"": ""{profileId}"",
                    ""name"": ""Inactive present"",
                    ""displaySettings"": [ {{
                        ""targetId"": 33,
                        ""nativeWidth"": 2000,
                        ""nativeHeight"": 900
                    }} ]
                }}");

                Assert.IsTrue(await manager.LoadProfilesAsync());

                var profile = manager.GetProfile(profileId);
                Assert.AreEqual(6, profile.SchemaVersion);
                Assert.AreEqual(1800, profile.DisplaySettings[0].NativeWidth);
                Assert.AreEqual(900, profile.DisplaySettings[0].NativeHeight);
                AssertCurrentProfileShape(JObject.Parse(File.ReadAllText(profilePath)));
            }
            finally
            {
                Directory.Delete(appDataFolder, recursive: true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LoadProfilesAsync_Schema5UnresolvedDisplay_LeavesNativeUnknown()
        {
            string appDataFolder = CreateTempAppDataFolder();
            try
            {
                string profileId = Guid.NewGuid().ToString();
                var manager = CreateIsolatedManager(appDataFolder);
                File.WriteAllText(ManagedProfilePath(appDataFolder, profileId), $@"{{
                    ""schemaVersion"": 5,
                    ""id"": ""{profileId}"",
                    ""name"": ""Unavailable"",
                    ""displaySettings"": [ {{
                        ""targetId"": 33,
                        ""nativeWidth"": 2000,
                        ""nativeHeight"": 900
                    }} ]
                }}");

                Assert.IsTrue(await manager.LoadProfilesAsync());

                var profile = manager.GetProfile(profileId);
                Assert.AreEqual(6, profile.SchemaVersion);
                Assert.AreEqual(0, profile.DisplaySettings[0].NativeWidth);
                Assert.AreEqual(0, profile.DisplaySettings[0].NativeHeight);
            }
            finally
            {
                Directory.Delete(appDataFolder, recursive: true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LoadProfilesAsync_Schema0_FinalNativeRefreshUsesAllPathPreferredMetadata()
        {
            string appDataFolder = CreateTempAppDataFolder();
            try
            {
                string profileId = Guid.NewGuid().ToString();
                var active = Address(33, 1600, 800);
                active.DeviceName = @"\\.\DISPLAY1";
                active.FriendlyName = "Current display";
                var preferred = Address(33, 1800, 900, enabled: false);
                var manager = CreateIsolatedManager(
                    appDataFolder,
                    [active],
                    [preferred]);
                File.WriteAllText(ManagedProfilePath(appDataFolder, profileId), $@"{{
                    ""schemaVersion"": 0,
                    ""id"": ""{profileId}"",
                    ""name"": ""Schema zero"",
                    ""displaySettings"": [ {{
                        ""targetId"": 33,
                        ""colorProfile"": ""color.icc"",
                        ""nativeWidth"": 2000,
                        ""nativeHeight"": 900
                    }} ]
                }}");

                Assert.IsTrue(await manager.LoadProfilesAsync());

                var profile = manager.GetProfile(profileId);
                Assert.AreEqual("Current display", profile.DisplaySettings[0].ReadableDeviceName);
                Assert.AreEqual(1800, profile.DisplaySettings[0].NativeWidth);
                Assert.AreEqual(900, profile.DisplaySettings[0].NativeHeight);
            }
            finally
            {
                Directory.Delete(appDataFolder, recursive: true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LoadProfilesAsync_CurrentSchemaWithoutLegacyMarker_DoesNotReenterMigration()
        {
            string appDataFolder = CreateTempAppDataFolder();
            try
            {
                string profileId = Guid.NewGuid().ToString();
                var preferred = Address(33, 1800, 900, enabled: false);
                var manager = CreateIsolatedManager(
                    appDataFolder,
                    [],
                    [preferred]);
                File.WriteAllText(ManagedProfilePath(appDataFolder, profileId), $@"{{
                    ""schemaVersion"": 6,
                    ""id"": ""{profileId}"",
                    ""name"": ""Current"",
                    ""displaySettings"": [ {{
                        ""targetId"": 33,
                        ""nativeWidth"": 2000,
                        ""nativeHeight"": 900
                    }} ]
                }}");

                Assert.IsTrue(await manager.LoadProfilesAsync());

                var profile = manager.GetProfile(profileId);
                Assert.AreEqual(2000, profile.DisplaySettings[0].NativeWidth);
                Assert.AreEqual(900, profile.DisplaySettings[0].NativeHeight);
            }
            finally
            {
                Directory.Delete(appDataFolder, recursive: true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LoadProfilesAsync_DeclaredV6WithLegacyAcm_MigratesAndPersistsCanonicalCurrentSchema()
        {
            string appDataFolder = CreateTempAppDataFolder();
            try
            {
                var manager = CreateIsolatedManager(appDataFolder);
                string profileId = Guid.NewGuid().ToString();
                string profilePath = ManagedProfilePath(appDataFolder, profileId);
                File.WriteAllText(profilePath, $@"{{
                    ""schemaVersion"": 6,
                    ""id"": ""{profileId}"",
                    ""name"": ""Managed legacy"",
                    ""wallpaperSettings"": {{ ""enabled"": true, ""mode"": ""Solid"" }},
                    ""displaySettings"": [ {{ ""isHdrEnabled"": false, ""isAcmEnabled"": true }} ]
                }}");

                bool loaded = await manager.LoadProfilesAsync();

                Assert.IsTrue(loaded);
                var profile = manager.GetProfile(profileId);
                Assert.IsNotNull(profile);
                Assert.AreEqual(6, profile.SchemaVersion);
                Assert.IsTrue(profile.DisplaySettings[0].IsWcgEnabled);
                Assert.IsFalse(profile.WallpaperSettings.Enabled);
                AssertCanonicalSchema6Migration(profilePath);
            }
            finally
            {
                Directory.Delete(appDataFolder, recursive: true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LoadProfilesAsync_FutureSchema_SkipsWithoutRewriteOrPublish()
        {
            string appDataFolder = CreateTempAppDataFolder();
            try
            {
                var manager = CreateIsolatedManager(appDataFolder);
                string profileId = Guid.NewGuid().ToString();
                string profilePath = ManagedProfilePath(appDataFolder, profileId);
                string source = $@"{{ ""schemaVersion"": 7, ""id"": ""{profileId}"", ""name"": ""Future"", ""displaySettings"": [] }}";
                File.WriteAllText(profilePath, source);

                bool loaded = await manager.LoadProfilesAsync();

                Assert.IsFalse(loaded);
                Assert.AreEqual(source, File.ReadAllText(profilePath));
                Assert.AreEqual(0, manager.GetProfileCount());
                Assert.IsNull(manager.GetProfile(profileId));
            }
            finally
            {
                Directory.Delete(appDataFolder, recursive: true);
            }
        }

        private static string DemoFixturePath(string id)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "docs", "demo", "profiles", id + ".dpm");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new FileNotFoundException("Repository demo fixture is missing", id);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LoadProfilesAsync_CanonicalDemoFiles_LoadsBothWithoutApplying()
        {
            string root = CreateTempAppDataFolder();
            try
            {
                const string deskId = "00000000-0000-4000-8000-000000000001";
                const string tvId = "00000000-0000-4000-8000-000000000002";
                string deskFile = ManagedProfilePath(root, deskId);
                string tvFile = ManagedProfilePath(root, tvId);
                Directory.CreateDirectory(Path.Combine(root, "Profiles"));
                File.Copy(DemoFixturePath(deskId), deskFile);
                File.Copy(DemoFixturePath(tvId), tvFile);
                var manager = CreateIsolatedManager(root);

                Assert.IsTrue(await manager.LoadProfilesAsync());
                Assert.AreEqual(2, manager.GetProfileCount());
                var desk = manager.GetProfile(deskId);
                var tv = manager.GetProfile(tvId);
                Assert.IsNotNull(desk);
                Assert.IsNotNull(tv);
                Assert.AreEqual("Desk Setup", desk.Name);
                Assert.AreEqual("Dual-monitor layout for everyday desktop use.", desk.Description);
                Assert.AreEqual(2, desk.DisplaySettings.Count);
                Assert.AreEqual("TV Setup", tv.Name);
                Assert.AreEqual("4K HDR layout for games and media.", tv.Description);
                Assert.AreEqual(1, tv.DisplaySettings.Count);
                Assert.AreEqual(6, desk.SchemaVersion);
                Assert.AreEqual(6, tv.SchemaVersion);
                Assert.IsFalse(desk.ScriptSettings.Enabled);
                Assert.IsFalse(tv.ScriptSettings.Enabled);
                Assert.IsFalse(desk.AudioSettings.Enabled);
                Assert.IsFalse(tv.AudioSettings.Enabled);
                Assert.AreEqual(File.ReadAllText(DemoFixturePath(deskId)), File.ReadAllText(deskFile));
                Assert.AreEqual(File.ReadAllText(DemoFixturePath(tvId)), File.ReadAllText(tvFile));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LoadProfilesAsync_RejectsFriendlyFilenameAndMismatchedDemoId()
        {
            string root = CreateTempAppDataFolder();
            try
            {
                const string deskId = "00000000-0000-4000-8000-000000000001";
                const string tvId = "00000000-0000-4000-8000-000000000002";
                Directory.CreateDirectory(Path.Combine(root, "Profiles"));
                File.Copy(DemoFixturePath(deskId), Path.Combine(root, "Profiles", "Desk Setup.dpm"));
                File.Copy(DemoFixturePath(tvId), ManagedProfilePath(root, deskId));
                var manager = CreateIsolatedManager(root);
                Assert.IsFalse(await manager.LoadProfilesAsync());
                Assert.AreEqual(0, manager.GetProfileCount());
                Assert.IsNull(manager.GetProfile(deskId));
                Assert.IsNull(manager.GetProfile(tvId));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LoadProfilesAsync_DemoIdCollision_DoesNotReplaceExistingManagedProfile()
        {
            string root = CreateTempAppDataFolder();
            try
            {
                const string deskId = "00000000-0000-4000-8000-000000000001";
                var manager = CreateIsolatedManager(root);
                var existing = ProfileDraftFactory.CreateNewProfile("Keep Existing");
                existing.Id = deskId;
                Assert.IsTrue(await manager.AddProfileAsync(existing));
                string managed = ManagedProfilePath(root, deskId);
                string prior = File.ReadAllText(managed);
                // Copying with no overwrite must reject an occupied managed GUID filename.
                Assert.Throws<IOException>(() => File.Copy(DemoFixturePath(deskId), managed, overwrite: false));
                Assert.IsTrue(File.Exists(managed));
                Assert.IsTrue(await manager.LoadProfilesAsync());
                Assert.AreEqual("Keep Existing", manager.GetProfile(deskId).Name);
                Assert.AreEqual(prior, File.ReadAllText(managed));
                Assert.AreEqual(1, manager.GetProfileCount());
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CanPersistProfileSchema_AcceptsOnlyCurrentSchema()
        {
            Assert.IsTrue(ProfileSerializer.CanPersistProfileSchema(ProfileDraftFactory.CreateNewProfile("Current")));
            Assert.IsFalse(ProfileSerializer.CanPersistProfileSchema(new Profile("Model default")));
            Assert.IsFalse(ProfileSerializer.CanPersistProfileSchema(new Profile { SchemaVersion = 5 }));
            Assert.IsFalse(ProfileSerializer.CanPersistProfileSchema(new Profile { SchemaVersion = 7 }));
            Assert.IsFalse(ProfileSerializer.CanPersistProfileSchema(null));
        }
    }
}
