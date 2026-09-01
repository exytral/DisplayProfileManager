using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;

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

            _profilesField = typeof(ProfileManager).GetField("_profiles", BindingFlags.NonPublic | BindingFlags.Instance);

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
            var p = MakeProfile("Profile");
            Seed(p);

            Assert.AreSame(p, _pm.GetProfileByName("Profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfileByName_CaseInsensitive_ReturnsProfile()
        {
            Seed(MakeProfile("Profile"));

            Assert.IsNotNull(_pm.GetProfileByName("profile"));
            Assert.IsNotNull(_pm.GetProfileByName("PROFILE"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProfileByName_TrimsWhitespace()
        {
            Seed(MakeProfile("Profile"));

            Assert.IsNotNull(_pm.GetProfileByName("  Profile  "));
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
            Seed(MakeProfile("Profile"));

            Assert.IsTrue(_pm.HasProfile("profile"));
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
            Assert.AreEqual("Profile", _pm.GetUniqueProfileName("Profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetUniqueProfileName_WhenNameTaken_AppendsCounter()
        {
            Seed(MakeProfile("Profile"));

            Assert.AreEqual("Profile (1)", _pm.GetUniqueProfileName("Profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetUniqueProfileName_WhenMultipleTaken_IncreasesCounter()
        {
            Seed(MakeProfile("Profile"), MakeProfile("Profile (1)"), MakeProfile("Profile (2)"));

            Assert.AreEqual("Profile (3)", _pm.GetUniqueProfileName("Profile"));
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

            Assert.AreEqual(2, _pm.GetProfileCount(), "GetAllProfiles must return a copy — clearing it must not affect the internal list.");
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
        public void GetDefaultProfile_WhenNoDefaultConfigured_ReturnsNull()
        {
            var a = MakeProfile("A");
            var b = MakeProfile("B");
            Seed(a, b);

            Assert.IsNull(_pm.GetDefaultProfile(), "The default is Settings.DefaultProfileId, which no seeded profile has set.");
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
            var p = MakeProfile("Profile");
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
        public void GetProfilesWithHotkeys_IncludesDisabledHotkeys()
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
        public void GetProfilesWithActiveHotkeys_ReturnsOnlyEnabledHotkeys()
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
        public void DuplicateProfile_HasDistinctId()
        {
            var original = MakeProfile("A");
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreNotEqual(original.Id, dup.Id, "A duplicate must receive a distinct profile ID.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_HotkeyIsCleared()
        {
            var original = ProfileWithHotkey("A", System.Windows.Input.Key.F1, System.Windows.Input.ModifierKeys.Control);
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreEqual(System.Windows.Input.Key.None, dup.HotkeyConfig.Key, "Duplicated profile must have hotkey cleared to avoid conflicts.");
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

            Assert.AreEqual(1, original.Scripts.Count, "Clearing the duplicate's Scripts must not affect the original.");
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
            var original = MakeProfile("Profile");
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreNotEqual(original.Name, dup.Name, "Duplicated profile must receive a unique name.");
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

            StringAssert.Contains(_pm.GetApplyResultErrorMessage("Profile", result), "Profile");
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

        // Duplicate Naming

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDuplicateProfileName_AppendsCopySuffix()
        {
            Seed(MakeProfile("Profile"));

            Assert.AreEqual("Profile - Copy", _pm.GetDuplicateProfileName("Profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDuplicateProfileName_SecondDuplicateNumbersCopy()
        {
            Seed(MakeProfile("Profile"), MakeProfile("Profile - Copy"));

            Assert.AreEqual("Profile - Copy (1)", _pm.GetDuplicateProfileName("Profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDuplicateProfileName_DuplicatingCopyChainsRatherThanNumbers()
        {
            Seed(MakeProfile("Profile - Copy"));

            Assert.AreEqual("Profile - Copy - Copy", _pm.GetDuplicateProfileName("Profile - Copy"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDuplicateProfileName_NeverExceedsNameLimit()
        {
            var longName = new string('z', ProfileManager.MaxProfileNameLength);
            Seed(MakeProfile(longName));

            var result = _pm.GetDuplicateProfileName(longName);

            Assert.IsTrue(result.Length <= ProfileManager.MaxProfileNameLength, $"'{result}' is {result.Length} characters");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDuplicateProfileName_TruncationKeepsMarkerAndMarksCut()
        {
            var longName = new string('z', ProfileManager.MaxProfileNameLength);
            Seed(MakeProfile(longName));

            var result = _pm.GetDuplicateProfileName(longName);

            StringAssert.EndsWith(result, " - Copy");
            StringAssert.Contains(result, "\u2026");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetUniqueProfileName_KeepsExistingCopyMarkerWhenNumbering()
        {
            var stem = new string('z', ProfileManager.MaxProfileNameLength - " - Copy".Length);
            var copy = stem + " - Copy";
            Seed(MakeProfile(stem), MakeProfile(copy));

            var result = _pm.GetUniqueProfileName(copy);

            Assert.IsTrue(result.Length <= ProfileManager.MaxProfileNameLength);
            StringAssert.Contains(result, " - Copy");
            StringAssert.EndsWith(result, " (1)");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDuplicateProfileName_TreatsHandTypedMarkerAsPartOfChain()
        {
            Seed(MakeProfile("Profile - Copy"));

            Assert.AreEqual("Profile - Copy - Copy", _pm.GetDuplicateProfileName("Profile - Copy"));
        }

        // SelectRollbackTarget

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_RecoveryDisabled_ReturnsNone()
        {
            var target = ProfileManager.SelectRollbackTarget(rollbackAfterApplyFailure: false, rollbackToPreviousProfile: true, hasPreviousProfile: true);

            Assert.AreEqual(ProfileManager.RollbackTarget.None, target);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_PreviousProfileRecoveryWithPreviousProfile_ReturnsPreviousProfile()
        {
            var target = ProfileManager.SelectRollbackTarget(rollbackAfterApplyFailure: true, rollbackToPreviousProfile: true, hasPreviousProfile: true);

            Assert.AreEqual(ProfileManager.RollbackTarget.PreviousProfile, target);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_PreviousProfileRecoveryWithoutPreviousProfile_ReturnsSnapshot()
        {
            var target = ProfileManager.SelectRollbackTarget(rollbackAfterApplyFailure: true, rollbackToPreviousProfile: true, hasPreviousProfile: false);

            Assert.AreEqual(ProfileManager.RollbackTarget.Snapshot, target);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_SnapshotRecovery_ReturnsSnapshot()
        {
            var target = ProfileManager.SelectRollbackTarget(rollbackAfterApplyFailure: true, rollbackToPreviousProfile: false, hasPreviousProfile: true);

            Assert.AreEqual(ProfileManager.RollbackTarget.Snapshot, target);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_SnapshotRecoveryWithoutPreviousProfile_ReturnsSnapshot()
        {
            var target = ProfileManager.SelectRollbackTarget(rollbackAfterApplyFailure: true, rollbackToPreviousProfile: false, hasPreviousProfile: false);

            Assert.AreEqual(ProfileManager.RollbackTarget.Snapshot, target);
        }

    }

    [TestClass]
    public class ResolveLiveDisplayTests
    {
        private static DisplayConfigHelper.DisplayConfigInfo Live(uint targetId, string manufacturer, string productCode, string deviceName)
        {
            return new DisplayConfigHelper.DisplayConfigInfo
            {
                TargetId = targetId,
                ManufacturerName = manufacturer,
                ProductCodeID = productCode,
                DeviceName = deviceName
            };
        }

        private static DisplaySetting Stored(uint targetId, string manufacturer, string productCode)
        {
            return new DisplaySetting
            {
                TargetId = targetId,
                ManufacturerName = manufacturer,
                ProductCodeID = productCode
            };
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_PrefersStoredTargetWhenIdentityAgrees()
        {
            var live = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Live(1, "MAN", "A1B2", "\\\\.\\DISPLAY1"),
                Live(2, "MAN", "A1B2", "\\\\.\\DISPLAY2")
            };

            var resolved = DisplayConfigHelper.ResolveLiveDisplay(Stored(2, "MAN", "A1B2"), live);

            Assert.AreEqual(2u, resolved.TargetId, "Two identical panels must resolve to the stored port, not to whichever matches first.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_FollowsIdentityWhenMonitorMovedPorts()
        {
            var live = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Live(1, "DEV", "C3D4", "\\\\.\\DISPLAY1"),
                Live(2, "MAN", "A1B2", "\\\\.\\DISPLAY2")
            };

            var resolved = DisplayConfigHelper.ResolveLiveDisplay(Stored(1, "MAN", "A1B2"), live);

            Assert.AreEqual(2u, resolved.TargetId);
            Assert.AreEqual("\\\\.\\DISPLAY2", resolved.DeviceName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_AppliesToPortWhenCapturedMonitorIsAbsent()
        {
            var live = new List<DisplayConfigHelper.DisplayConfigInfo> { Live(1, "DEV", "C3D4", "\\\\.\\DISPLAY1") };

            var resolved = DisplayConfigHelper.ResolveLiveDisplay(Stored(1, "MAN", "A1B2"), live);

            Assert.AreEqual(1u, resolved.TargetId, "A replaced monitor still applies, so a profile cannot leave the desktop unconfigured.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_MatchesOnTargetIdWhenIdentityIsAbsent()
        {
            var live = new List<DisplayConfigHelper.DisplayConfigInfo> { Live(1, "", "", "\\\\.\\DISPLAY1") };

            var resolved = DisplayConfigHelper.ResolveLiveDisplay(Stored(1, "", ""), live);

            Assert.AreEqual(1u, resolved.TargetId, "Profiles predating EDID capture must resolve exactly as they did before.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_MasksTargetIdToBaseId()
        {
            var live = new List<DisplayConfigHelper.DisplayConfigInfo> { Live(0x00010004, "MAN", "A1B2", "\\\\.\\DISPLAY1") };

            var resolved = DisplayConfigHelper.ResolveLiveDisplay(Stored(4, "MAN", "A1B2"), live);

            Assert.IsNotNull(resolved, "Profiles store the low 16 bits, so a live raw target id must still match.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_ReturnsNullWhenDisplayIsDisconnected()
        {
            var live = new List<DisplayConfigHelper.DisplayConfigInfo> { Live(1, "DEV", "C3D4", "\\\\.\\DISPLAY1") };

            Assert.IsNull(DisplayConfigHelper.ResolveLiveDisplay(Stored(9, "MAN", "A1B2"), live));
            Assert.IsNull(DisplayConfigHelper.ResolveLiveDisplay(Stored(9, "MAN", "A1B2"), new List<DisplayConfigHelper.DisplayConfigInfo>()));
        }
    }
}