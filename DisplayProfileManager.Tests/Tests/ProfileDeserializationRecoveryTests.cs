using Newtonsoft.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ProfileDeserializationRecoveryTests
    {
        private static Profile Deserialize(string json) => ProfileManager.DeserializeProfile(json);

        // Small, independent sections self-heal to their default

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedWallpaperSettings_FallsBackToDefaultAndProfileStillLoads()
        {
            var json = @"{
                ""name"": ""Profile"",
                ""wallpaperSettings"": ""not-an-object""
            }";

            var profile = Deserialize(json);

            Assert.IsNotNull(profile);
            Assert.IsNull(profile.WallpaperSettings);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedAudioSettings_FallsBackToDefaultAndProfileStillLoads()
        {
            var json = @"{
                ""name"": ""Profile"",
                ""audioSettings"": ""not-an-object""
            }";

            var profile = Deserialize(json);

            Assert.IsNotNull(profile);
            Assert.IsNotNull(profile.AudioSettings);
            Assert.IsFalse(profile.AudioSettings.HasPlaybackDevice());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedHotkeyConfig_FallsBackToDefaultAndProfileStillLoads()
        {
            var json = @"{
                ""name"": ""Profile"",
                ""hotkeyConfig"": ""not-an-object""
            }";

            var profile = Deserialize(json);

            Assert.IsNotNull(profile);
            Assert.IsNotNull(profile.HotkeyConfig);
        }

        // Descriptive metadata self-heals to its default

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedDescription_FallsBackToDefaultAndProfileStillLoads()
        {
            var json = @"{
                ""name"": ""Profile"",
                ""description"": [ 1, 2, 3 ]
            }";

            var profile = Deserialize(json);

            Assert.IsNotNull(profile);
            Assert.AreEqual(string.Empty, profile.Description);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedCreatedDate_FallsBackToDefaultAndProfileStillLoads()
        {
            var json = @"{
                ""name"": ""Test Profile"",
                ""createdDate"": ""not-a-date""
            }";

            var profile = Deserialize(json);

            Assert.IsNotNull(profile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedNameOnly_ProfileDeserializesButIsBlankName()
        {
            // Actual rejection happens downstream via ProfileManager's IsNullOrWhiteSpace(Name) check.
            var json = @"{
                ""name"": [ 1, 2, 3 ],
                ""description"": ""Kept intact""
            }";

            var profile = Deserialize(json);

            Assert.IsNotNull(profile);
            Assert.AreEqual(string.Empty, profile.Name);
            Assert.AreEqual("Kept intact", profile.Description);
        }

        // Critical members fail the whole profile

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedDisplaySettings_StillThrows()
        {
            var json = @"{
                ""name"": ""Profile"",
                ""displaySettings"": ""not-an-array""
            }";

            Assert.Throws<JsonException>(() => Deserialize(json));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedDisplaySettingsItem_StillThrows()
        {
            var json = @"{
                ""name"": ""Profile"",
                ""displaySettings"": [ ""not-an-object"" ]
            }";

            Assert.Throws<JsonException>(() => Deserialize(json));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedId_StillThrows()
        {
            var json = @"{
                ""id"": [ 1, 2, 3 ],
                ""name"": ""Profile""
            }";

            Assert.Throws<JsonException>(() => Deserialize(json));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedSchemaVersion_FallsBackToDefaultAndEntersMigration()
        {
            var json = @"{
                ""name"": ""Profile"",
                ""schemaVersion"": [ 1, 2, 3 ]
            }";

            var profile = Deserialize(json);

            Assert.IsNotNull(profile);
            Assert.AreEqual(0, profile.SchemaVersion, "A malformed schemaVersion must recover to the Profile.SchemaVersion initializer (0) so migration still runs.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_CriticalFieldMalformedAlongsideRecoverableSections_StillThrows()
        {
            var json = @"{
                ""id"": [ 1, 2, 3 ],
                ""name"": ""Profile"",
                ""audioSettings"": ""not-an-object"",
                ""scripts"": [ ""script.ps1"" ]
            }";

            Assert.Throws<JsonException>(() => Deserialize(json));
        }
    }
}