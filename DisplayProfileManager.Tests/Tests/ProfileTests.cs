using DisplayProfileManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ProfileTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_DefaultConstructor_HasValidGuidId()
        {
            var profile = new Profile();

            Assert.IsTrue(Guid.TryParse(profile.Id, out _),
                "Profile.Id must be a valid GUID string on default construction.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_DefaultConstructor_HasEmptyDisplaySettings()
        {
            var profile = new Profile();

            Assert.IsNotNull(profile.DisplaySettings);
            Assert.AreEqual(0, profile.DisplaySettings.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_DefaultConstructor_HasEmptyScripts()
        {
            var profile = new Profile();

            Assert.IsNotNull(profile.Scripts);
            Assert.AreEqual(0, profile.Scripts.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_DefaultConstructor_EnableScriptsIsTrue()
        {
            var profile = new Profile();

            Assert.IsTrue(profile.EnableScripts,
                "EnableScripts must default true so scripts run on apply unless explicitly disabled.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_DefaultConstructor_IsNotDefault()
        {
            var profile = new Profile();

            Assert.IsFalse(profile.IsDefault);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_DefaultConstructor_SchemaVersionIsZero()
        {
            var profile = new Profile();

            Assert.AreEqual(0, profile.SchemaVersion,
                "SchemaVersion must default to 0 so old profiles without this field trigger migration on load.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_NameConstructor_SetsName()
        {
            var profile = new Profile("Gaming");

            Assert.AreEqual("Gaming", profile.Name);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_NameConstructor_SetsDescription()
        {
            var profile = new Profile("Gaming", "High performance setup");

            Assert.AreEqual("High performance setup", profile.Description);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_NameConstructor_EmptyDescriptionByDefault()
        {
            var profile = new Profile("Gaming");

            Assert.AreEqual(string.Empty, profile.Description);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_ToString_ReturnsName()
        {
            var profile = new Profile("Work Setup");

            Assert.AreEqual("Work Setup", profile.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UpdateLastModified_AdvancesLastModifiedDate()
        {
            var profile = new Profile("Test");
            var before = profile.LastModifiedDate;

            System.Threading.Thread.Sleep(10);
            profile.UpdateLastModified();

            Assert.IsTrue(profile.LastModifiedDate > before,
                "LastModifiedDate must advance after UpdateLastModified().");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UpdateLastModified_DoesNotChangeCreatedDate()
        {
            var profile = new Profile("Test");
            var created = profile.CreatedDate;

            System.Threading.Thread.Sleep(10);
            profile.UpdateLastModified();

            Assert.AreEqual(created, profile.CreatedDate,
                "CreatedDate must not change on UpdateLastModified().");
        }
    }

    [TestClass]
    public class DisplaySettingModelTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void DisplaySetting_DefaultRotation_IsIdentity()
        {
            var setting = new DisplaySetting();

            Assert.AreEqual(1, setting.Rotation,
                "Default rotation must be 1 (IDENTITY) for backward compat with old .dpm files.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplaySetting_DefaultDpiScaling_Is100()
        {
            var setting = new DisplaySetting();

            Assert.AreEqual(100u, setting.DpiScaling);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplaySetting_DefaultFrequency_Is60()
        {
            var setting = new DisplaySetting();

            Assert.AreEqual(60, setting.Frequency);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplaySetting_DefaultIsEnabled_IsTrue()
        {
            var setting = new DisplaySetting();

            Assert.IsTrue(setting.IsEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplaySetting_DefaultIsHdrEnabled_IsFalse()
        {
            var setting = new DisplaySetting();

            Assert.IsFalse(setting.IsHdrEnabled,
                "IsHdrEnabled must default false for backward compat with pre-1.3.0 .dpm files.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplaySetting_DefaultCloneGroupId_IsEmpty()
        {
            var setting = new DisplaySetting();

            Assert.AreEqual(string.Empty, setting.CloneGroupId,
                "CloneGroupId must default to empty string so old profiles load as extended mode.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplaySetting_DefaultNativeWidth_IsZero()
        {
            var setting = new DisplaySetting();

            Assert.AreEqual(0, setting.NativeWidth,
                "NativeWidth must default to 0 so old profiles without this field trigger migration backfill.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplaySetting_DefaultNativeHeight_IsZero()
        {
            var setting = new DisplaySetting();

            Assert.AreEqual(0, setting.NativeHeight,
                "NativeHeight must default to 0 so old profiles without this field trigger migration backfill.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetResolutionString_FormatsCorrectly()
        {
            var setting = new DisplaySetting { Width = 2560, Height = 1440, Frequency = 144 };

            Assert.AreEqual("2560x1440 • 144Hz", setting.GetResolutionString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDpiString_FormatsCorrectly()
        {
            var setting = new DisplaySetting { DpiScaling = 150 };

            Assert.AreEqual("150%", setting.GetDpiString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_IncludesDeviceNameAndResolution()
        {
            var setting = new DisplaySetting
            {
                DeviceName = "\\\\.\\DISPLAY1",
                Width = 1920,
                Height = 1080,
                Frequency = 60,
                DpiScaling = 100
            };

            string result = setting.ToString();

            StringAssert.Contains(result, "DISPLAY1");
            StringAssert.Contains(result, "1920x1080");
        }
    }

    [TestClass]
    public class AudioSettingTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void HasPlaybackDevice_WhenIdSet_ReturnsTrue()
        {
            var audio = new AudioSetting { DefaultPlaybackDeviceId = "{some-guid}" };

            Assert.IsTrue(audio.HasPlaybackDevice());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasPlaybackDevice_WhenIdEmpty_ReturnsFalse()
        {
            var audio = new AudioSetting();

            Assert.IsFalse(audio.HasPlaybackDevice());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasCaptureDevice_WhenIdSet_ReturnsTrue()
        {
            var audio = new AudioSetting { DefaultCaptureDeviceId = "{some-guid}" };

            Assert.IsTrue(audio.HasCaptureDevice());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasCaptureDevice_WhenIdEmpty_ReturnsFalse()
        {
            var audio = new AudioSetting();

            Assert.IsFalse(audio.HasCaptureDevice());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AudioSetting_DefaultApplyPlayback_IsFalse()
        {
            var audio = new AudioSetting();

            Assert.IsFalse(audio.ApplyPlaybackDevice,
                "ApplyPlaybackDevice must default false so audio is not switched unless explicitly enabled.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AudioSetting_DefaultApplyCapture_IsFalse()
        {
            var audio = new AudioSetting();

            Assert.IsFalse(audio.ApplyCaptureDevice,
                "ApplyCaptureDevice must default false so audio is not switched unless explicitly enabled.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_WhenNoDevices_ReturnsNoneConfigured()
        {
            var audio = new AudioSetting();

            Assert.AreEqual("No audio devices configured", audio.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_WhenPlaybackSet_IncludesOutput()
        {
            var audio = new AudioSetting { PlaybackDeviceName = "Speakers" };

            StringAssert.Contains(audio.ToString(), "Output: Speakers");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_WhenCaptureSet_IncludesInput()
        {
            var audio = new AudioSetting { CaptureDeviceName = "Microphone" };

            StringAssert.Contains(audio.ToString(), "Input: Microphone");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_WhenBothSet_IncludesBoth()
        {
            var audio = new AudioSetting("id1", "Speakers", "id2", "Microphone");
            var result = audio.ToString();

            StringAssert.Contains(result, "Output: Speakers");
            StringAssert.Contains(result, "Input: Microphone");
        }
    }

    [TestClass]
    public class ApplyProfileScriptLogicTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void EnableScriptsFalse_ScriptListIsPreserved()
        {
            var profile = new Profile("Test");
            profile.Scripts.Add(new Script("script.ps1"));
            profile.EnableScripts = false;

            Assert.AreEqual(1, profile.Scripts.Count,
                "Scripts must remain stored when EnableScripts is false.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EnableScriptsTrue_WithEmptyList_DoesNotExecute()
        {
            var profile = new Profile("Test");
            profile.EnableScripts = true;

            bool wouldExecute = profile.EnableScripts && profile.Scripts != null && profile.Scripts.Any();

            Assert.IsFalse(wouldExecute,
                "No scripts must execute when the list is empty, even if EnableScripts is true.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EnableScriptsFalse_WithScripts_DoesNotExecute()
        {
            var profile = new Profile("Test");
            profile.Scripts.Add(new Script("script.ps1"));
            profile.EnableScripts = false;

            bool wouldExecute = profile.EnableScripts && profile.Scripts != null && profile.Scripts.Any();

            Assert.IsFalse(wouldExecute,
                "Scripts must not execute when EnableScripts is false, regardless of list contents.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EnableScriptsTrue_WithScripts_Executes()
        {
            var profile = new Profile("Test");
            profile.Scripts.Add(new Script("script.ps1"));
            profile.EnableScripts = true;

            bool wouldExecute = profile.EnableScripts && profile.Scripts != null && profile.Scripts.Any();

            Assert.IsTrue(wouldExecute);
        }
    }
}