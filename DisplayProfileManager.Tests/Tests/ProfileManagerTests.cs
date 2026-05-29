using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.Tests.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ProfileManagerTests
    {
        private ProfileManager _pm;
        private FieldInfo _profilesField;

        [TestInitialize]
        public void Setup()
        {
            _pm = ProfileManager.Instance;

            _profilesField = typeof(ProfileManager).GetField(
                "_profiles",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(_profilesField, "_profiles field not found — was it renamed?");

            _profilesField.SetValue(_pm, new List<Profile>());
        }

        private void Seed(params Profile[] profiles)
        {
            var list = (List<Profile>)_profilesField.GetValue(_pm);
            list.AddRange(profiles);
        }

        private Profile MakeProfile(string name) => new Profile(name);

        private Profile ProfileWithHotkey(string name, System.Windows.Input.Key key,
            System.Windows.Input.ModifierKeys mods, bool enabled = true)
        {
            var p = MakeProfile(name);
            p.HotkeyConfig = new HotkeyConfig(key, mods, enabled);
            return p;
        }

        // GetProfileByName

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfileByName_ExactMatch_ReturnsProfile()
        {
            var p = MakeProfile("Gaming");
            Seed(p);

            Assert.AreSame(p, _pm.GetProfileByName("Gaming"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfileByName_CaseInsensitive_ReturnsProfile()
        {
            Seed(MakeProfile("Gaming"));

            Assert.IsNotNull(_pm.GetProfileByName("gaming"));
            Assert.IsNotNull(_pm.GetProfileByName("GAMING"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfileByName_TrimsWhitespace()
        {
            Seed(MakeProfile("Gaming"));

            Assert.IsNotNull(_pm.GetProfileByName("  Gaming  "));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfileByName_WhenNotFound_ReturnsNull()
        {
            Seed(MakeProfile("A"));

            Assert.IsNull(_pm.GetProfileByName("B"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfileByName_NullInput_ReturnsNull()
        {
            Seed(MakeProfile("A"));

            Assert.IsNull(_pm.GetProfileByName(null));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfileByName_EmptyInput_ReturnsNull()
        {
            Seed(MakeProfile("A"));

            Assert.IsNull(_pm.GetProfileByName(string.Empty));
        }

        // HasProfile

        [TestMethod]
        [TestCategory("Unit")]
        public void HasProfile_ExistingName_ReturnsTrue()
        {
            Seed(MakeProfile("Work"));

            Assert.IsTrue(_pm.HasProfile("work"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasProfile_AbsentName_ReturnsFalse()
        {
            Seed(MakeProfile("A"));

            Assert.IsFalse(_pm.HasProfile("B"));
        }

        // GetUniqueProfileName

        [TestMethod]
        [TestCategory("Unit")]
        public void GetUniqueProfileName_WhenNameNotTaken_ReturnsOriginal()
        {
            Assert.AreEqual("Gaming", _pm.GetUniqueProfileName("Gaming"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetUniqueProfileName_WhenNameTaken_AppendsCounter()
        {
            Seed(MakeProfile("Gaming"));

            Assert.AreEqual("Gaming (1)", _pm.GetUniqueProfileName("Gaming"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetUniqueProfileName_WhenMultipleTaken_IncreasesCounter()
        {
            Seed(MakeProfile("Gaming"), MakeProfile("Gaming (1)"), MakeProfile("Gaming (2)"));

            Assert.AreEqual("Gaming (3)", _pm.GetUniqueProfileName("Gaming"));
        }

        // GetProfile / GetAllProfiles / GetProfileCount

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfile_ExistingId_ReturnsProfile()
        {
            var p = MakeProfile("A");
            Seed(p);

            Assert.AreSame(p, _pm.GetProfile(p.Id));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfile_UnknownId_ReturnsNull()
        {
            Seed(MakeProfile("A"));

            Assert.IsNull(_pm.GetProfile(Guid.NewGuid().ToString()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetAllProfiles_ReturnsCopyNotLiveList()
        {
            Seed(MakeProfile("A"), MakeProfile("B"));

            var copy = _pm.GetAllProfiles();
            copy.Clear();

            Assert.AreEqual(2, _pm.GetProfileCount(),
                "GetAllProfiles must return a copy — clearing it must not affect the internal list.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfileCount_ReflectsCurrentState()
        {
            Seed(MakeProfile("A"), MakeProfile("B"), MakeProfile("C"));

            Assert.AreEqual(3, _pm.GetProfileCount());
        }

        // Default profile

        [TestMethod]
        [TestCategory("Unit")]
        public void SetDefaultProfile_MarksCorrectProfile()
        {
            var a = MakeProfile("A");
            var b = MakeProfile("B");
            Seed(a, b);

            _pm.SetDefaultProfile(b.Id);

            Assert.IsFalse(a.IsDefault);
            Assert.IsTrue(b.IsDefault);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SetDefaultProfile_ClearsOldDefault()
        {
            var a = MakeProfile("A");
            var b = MakeProfile("B");
            a.IsDefault = true;
            Seed(a, b);

            _pm.SetDefaultProfile(b.Id);

            Assert.IsFalse(a.IsDefault,
                "SetDefaultProfile must clear the previously-default profile.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDefaultProfile_WhenNoneIsDefault_ReturnsNull()
        {
            Seed(MakeProfile("A"), MakeProfile("B"));

            Assert.IsNull(_pm.GetDefaultProfile());
        }

        // Add / Update / Delete

        [TestMethod]
        [TestCategory("Unit")]
        public void AddProfile_IncreasesCount()
        {
            _pm.AddProfile(MakeProfile("A"));

            Assert.AreEqual(1, _pm.GetProfileCount());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UpdateProfile_ReplacesEntry()
        {
            var p = MakeProfile("Old");
            Seed(p);
            p.Name = "New";

            _pm.UpdateProfile(p);

            Assert.AreEqual("New", _pm.GetProfile(p.Id).Name);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UpdateProfile_AdvancesLastModifiedDate()
        {
            var p = MakeProfile("Test");
            Seed(p);
            var before = p.LastModifiedDate;

            System.Threading.Thread.Sleep(10);
            _pm.UpdateProfile(p);

            Assert.IsTrue(_pm.GetProfile(p.Id).LastModifiedDate > before);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeleteProfile_RemovesById()
        {
            var p = MakeProfile("ToDelete");
            Seed(p);

            _pm.DeleteProfile(p.Id);

            Assert.IsNull(_pm.GetProfile(p.Id));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeleteProfile_OnlyRemovesTargetProfile()
        {
            var keep = MakeProfile("Keep");
            var remove = MakeProfile("Remove");
            Seed(keep, remove);

            _pm.DeleteProfile(remove.Id);

            Assert.IsNotNull(_pm.GetProfile(keep.Id));
            Assert.AreEqual(1, _pm.GetProfileCount());
        }

        // Hotkeys

        [TestMethod]
        [TestCategory("Unit")]
        public void GetAllProfilesWithHotkeys_IncludesDisabledHotkeys()
        {
            var enabled = ProfileWithHotkey("A", System.Windows.Input.Key.F1, System.Windows.Input.ModifierKeys.Control, enabled: true);
            var disabled = ProfileWithHotkey("B", System.Windows.Input.Key.F2, System.Windows.Input.ModifierKeys.Control, enabled: false);
            Seed(enabled, disabled);

            var result = _pm.GetProfilesWithHotkeys();

            CollectionAssert.Contains(result, enabled);
            CollectionAssert.Contains(result, disabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfilesWithHotkeys_ReturnsOnlyEnabledHotkeys()
        {
            var enabled = ProfileWithHotkey("A", System.Windows.Input.Key.F1, System.Windows.Input.ModifierKeys.Control, enabled: true);
            var disabled = ProfileWithHotkey("B", System.Windows.Input.Key.F2, System.Windows.Input.ModifierKeys.Control, enabled: false);
            var noHotkey = MakeProfile("C");
            Seed(enabled, disabled, noHotkey);

            var result = _pm.GetProfilesWithActiveHotkeys();

            CollectionAssert.Contains(result, enabled);
            CollectionAssert.DoesNotContain(result, disabled);
            CollectionAssert.DoesNotContain(result, noHotkey);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetAllHotkeys_ReturnsOnlyEnabledHotkeys()
        {
            var enabled = ProfileWithHotkey("A", System.Windows.Input.Key.F1, System.Windows.Input.ModifierKeys.Control, enabled: true);
            var disabled = ProfileWithHotkey("B", System.Windows.Input.Key.F2, System.Windows.Input.ModifierKeys.Control, enabled: false);
            Seed(enabled, disabled);

            var hotkeys = _pm.GetAllHotkeys();

            Assert.IsTrue(hotkeys.ContainsKey(enabled.Id));
            Assert.IsFalse(hotkeys.ContainsKey(disabled.Id));
        }

        // DuplicateProfile

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_UnknownId_ReturnsNull()
        {
            Assert.IsNull(_pm.DuplicateProfile(Guid.NewGuid().ToString()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_HasNewId()
        {
            var original = MakeProfile("A");
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreNotEqual(original.Id, dup.Id);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_IsNotDefault()
        {
            var original = MakeProfile("A");
            original.IsDefault = true;
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.IsFalse(dup.IsDefault,
                "A duplicated profile must never be marked as default.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_HotkeyIsCleared()
        {
            var original = ProfileWithHotkey("A", System.Windows.Input.Key.F1, System.Windows.Input.ModifierKeys.Control);
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreEqual(System.Windows.Input.Key.None, dup.HotkeyConfig.Key,
                "Duplicated profile must have hotkey cleared to avoid conflicts.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_CopiesScriptsList()
        {
            var original = MakeProfile("A");
            original.Scripts.Add(new Script("script.ps1"));
            original.EnableScripts = true;
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreEqual(1, dup.Scripts.Count);
            Assert.IsTrue(dup.EnableScripts);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_Scripts_AreDeepCopy()
        {
            var original = MakeProfile("A");
            original.Scripts.Add(new Script("script.ps1"));
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);
            dup.Scripts.Clear();

            Assert.AreEqual(1, original.Scripts.Count,
                "Clearing the duplicate's Scripts must not affect the original.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_CopiesAudioSettings()
        {
            var original = MakeProfile("A");
            original.AudioSettings = new AudioSetting("pb-id", "Speakers", "cap-id", "Mic");
            original.AudioSettings.ApplyPlaybackDevice = true;
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreEqual("pb-id", dup.AudioSettings.DefaultPlaybackDeviceId);
            Assert.AreEqual("Speakers", dup.AudioSettings.PlaybackDeviceName);
            Assert.IsTrue(dup.AudioSettings.ApplyPlaybackDevice);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_GetsUniqueName()
        {
            var original = MakeProfile("Gaming");
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreNotEqual(original.Name, dup.Name,
                "Duplicated profile must receive a unique name.");
        }

        // GetApplyResultErrorMessage / ProfileApplyResult

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyResultErrorMessage_ContainsProfileName()
        {
            var result = new ProfileManager.ProfileApplyResult
            {
                DisplayConfigApplied = false,
                DpiChanged = true,
                AudioSuccess = true
            };

            StringAssert.Contains(_pm.GetApplyResultErrorMessage("Work Setup", result), "Work Setup");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyResultErrorMessage_ContainsDisplayConfigStatus()
        {
            var result = new ProfileManager.ProfileApplyResult { DisplayConfigApplied = false };

            StringAssert.Contains(_pm.GetApplyResultErrorMessage("X", result), "Display");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyResultErrorMessage_ContainsDpiStatus()
        {
            var result = new ProfileManager.ProfileApplyResult { DpiChanged = false };

            StringAssert.Contains(_pm.GetApplyResultErrorMessage("X", result), "DPI");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyResultErrorMessage_ContainsAudioStatus()
        {
            var result = new ProfileManager.ProfileApplyResult { AudioSuccess = false };

            StringAssert.Contains(_pm.GetApplyResultErrorMessage("X", result), "Audio");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileApplyResult_DefaultSuccess_IsFalse()
        {
            var result = new ProfileManager.ProfileApplyResult();

            Assert.IsFalse(result.Success);
        }
    }
}