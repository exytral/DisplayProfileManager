using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class SettingsManagerTests
    {
        private MethodInfo _deserializeTolerantMethod;

        [TestInitialize]
        public void Setup()
        {
            _deserializeTolerantMethod = typeof(SettingsManager).GetMethod("DeserializeSettingsTolerant", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(_deserializeTolerantMethod, "DeserializeSettingsTolerant not found — was it renamed?");
        }

        private AppSettings DeserializeSettingsTolerant(string json) => (AppSettings)_deserializeTolerantMethod.Invoke(null, [json]);

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeSettingsTolerant_MalformedBooleanMember_FallsBackToDefaultAndPreservesSiblingMember()
        {
            var json = @"{
                ""theme"": ""Dark"",
                ""checkForUpdates"": ""not-a-bool""
            }";

            var settings = DeserializeSettingsTolerant(json);

            Assert.IsNotNull(settings);
            Assert.AreEqual("Dark", settings.Theme);
            Assert.IsFalse(settings.CheckForUpdates, "The malformed member must fall back to the AppSettings.CheckForUpdates initializer (false).");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeSettingsTolerant_DifferentMalformedMember_StillLoadsRemainingSettings()
        {
            var json = @"{
                ""startWithWindows"": ""not-a-bool"",
                ""language"": ""fr-FR""
            }";

            var settings = DeserializeSettingsTolerant(json);

            Assert.IsNotNull(settings);
            Assert.AreEqual("fr-FR", settings.Language);
            Assert.IsFalse(settings.StartWithWindows, "The malformed member must fall back to the AppSettings.StartWithWindows initializer (false).");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeSettingsTolerant_MalformedMember_StillReturnsNonNullAppSettings()
        {
            var json = @"{ ""autoStartMode"": [ 1, 2, 3 ] }";

            var settings = DeserializeSettingsTolerant(json);

            Assert.IsNotNull(settings, "A malformed member must not fail the whole deserialization when other members are valid.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SaveSettingsAsync_WhenSettingsNotLoaded_RefusesToSave()
        {
            var sm = SettingsManager.Instance;
            var loadedField = typeof(SettingsManager).GetField("_settingsLoaded", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(loadedField, "_settingsLoaded field not found — was it renamed?");

            var originalValue = (bool)loadedField.GetValue(sm);
            try
            {
                loadedField.SetValue(sm, false);

                var result = sm.SaveSettingsAsync().GetAwaiter().GetResult();

                Assert.IsFalse(result, "SaveSettingsAsync must refuse to write while _settingsLoaded is false, or a failed load could overwrite the settings file with blank defaults.");
            }
            finally
            {
                loadedField.SetValue(sm, originalValue);
            }
        }
    }
}