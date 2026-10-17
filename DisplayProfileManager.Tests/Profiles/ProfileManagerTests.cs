using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Displays.Policy;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Profiles.Policy;
using DisplayProfileManager.Scripts.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.UI.Presentation;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileServiceTests
    {
        private ProfileService _pm;
        private string _appDataFolder;

        [TestInitialize]
        public void Setup()
        {
            _appDataFolder = Path.Combine(Path.GetTempPath(), "DPM-ProfileManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_appDataFolder);
            _pm = CreateService(_appDataFolder, () => []);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_appDataFolder))
                Directory.Delete(_appDataFolder, true);
        }

        private static ProfileService CreateService(
            string appDataFolder,
            Func<List<DisplayConfigInfo>> getDisplays,
            IProfileStore profileStore = null)
        {
            var settings = new SettingsService(new SettingsStore(appDataFolder));
            return new ProfileService(
                profileStore ?? new ProfileStore(appDataFolder),
                new ProfileMigrationService(settings),
                new ProfileDraftFactory(),
                new ProfileCaptureService(),
                settings,
                getDisplays,
                getDisplays);
        }

        private sealed class BlockingProfileStore(string appDataFolder) : IProfileStore
        {
            private readonly TaskCompletionSource<bool> _releaseFirstWrite =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _writeCount;

            public string AppDataFolder { get; } = appDataFolder;
            public TaskCompletionSource<bool> FirstWriteStarted { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public string LastWrittenJson { get; private set; }

            public string[] EnumerateProfileFiles() => [];
            public Task<string> ReadProfileTextAsync(string path) => Task.FromResult(string.Empty);

            public async Task<bool> WriteProfileTextAsync(string profileId, string json)
            {
                int write = System.Threading.Interlocked.Increment(ref _writeCount);
                if (write == 1)
                {
                    FirstWriteStarted.TrySetResult(true);
                    await _releaseFirstWrite.Task;
                }

                LastWrittenJson = json;
                return true;
            }

            public Task<bool> DeleteProfileAsync(string profileId) => Task.FromResult(true);
            public void ReleaseFirstWrite() => _releaseFirstWrite.TrySetResult(true);
        }

        private void Seed(params Profile[] profiles)
        {
            foreach (var profile in profiles)
                _pm.AddProfile(profile);
        }

        private Profile MakeProfile(string name) => new(name);

        private Profile ProfileWithHotkey(string name, System.Windows.Input.Key key,
            System.Windows.Input.ModifierKeys mods, bool enabled = true)
        {
            var p = MakeProfile(name);
            p.HotkeyConfig = new HotkeyConfig(key, mods, enabled);
            return p;
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task HealAdvancedColorCapabilitiesAsync_PersistsOnlyChangedProfilesWithoutChangingLastModified()
        {
            var changedSetting = new DisplaySetting
            {
                TargetId = 11,
                ManufacturerName = "MAN",
                ProductCodeID = "1001"
            };
            var unchangedSetting = new DisplaySetting
            {
                TargetId = 11,
                ManufacturerName = "MAN",
                ProductCodeID = "1001",
                LastKnownHdrSupported = true,
                LastKnownWcgSupported = false
            };
            var unrelatedSetting = new DisplaySetting
            {
                TargetId = 22,
                ManufacturerName = "DEV",
                ProductCodeID = "1002"
            };
            var changedProfile = MakeProfile("A");
            changedProfile.DisplaySettings.Add(changedSetting);
            var unchangedProfile = MakeProfile("B");
            unchangedProfile.DisplaySettings.Add(unchangedSetting);
            var unrelatedProfile = MakeProfile("C");
            unrelatedProfile.DisplaySettings.Add(unrelatedSetting);
            var changedDate = DateTime.Now.AddDays(-3);
            var unchangedDate = DateTime.Now.AddDays(-2);
            changedProfile.LastModifiedDate = changedDate;
            unchangedProfile.LastModifiedDate = unchangedDate;
            Seed(changedProfile, unchangedProfile, unrelatedProfile);

            var persisted = new List<Profile>();
            var live = new List<DisplayConfigInfo>
            {
                new() {
                    TargetId = 11,
                    RawTargetId = 11,
                    ManufacturerName = "MAN",
                    ProductCodeID = "1001",
                    IsAdvancedColorInfoAvailable = true,
                    IsHdrSupported = true,
                    IsWcgSupported = false
                }
            };

            var changed = await _pm.HealAdvancedColorCapabilitiesAsync(
                live,
                profile =>
                {
                    persisted.Add(profile);
                    return Task.FromResult(true);
                });

            Assert.AreEqual(1, changed.Count);
            CollectionAssert.AreEqual(new[] { changedProfile }, persisted);
            Assert.AreEqual(changedDate, changedProfile.LastModifiedDate);
            Assert.AreEqual(unchangedDate, unchangedProfile.LastModifiedDate);
            Assert.AreEqual(true, changedSetting.LastKnownHdrSupported);
            Assert.AreEqual(false, changedSetting.LastKnownWcgSupported);
            Assert.IsNull(unrelatedSetting.LastKnownHdrSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_StaleDraft_PreservesNewerCanonicalSupportedEvidence()
        {
            var canonical = MakeProfile("Profile");
            canonical.DisplaySettings.Add(new DisplaySetting
            {
                TargetId = 11,
                ManufacturerName = "MAN",
                ProductCodeID = "1001",
                LastKnownHdrSupported = true,
                LastKnownWcgSupported = true
            });
            Seed(canonical);

            var draft = ProfileDraftFactory.CreateWorkingCopy(canonical);
            draft.DisplaySettings[0].LastKnownHdrSupported = null;
            draft.DisplaySettings[0].LastKnownWcgSupported = false;
            draft.Description = "edited";

            bool success = await _pm.UpdateProfileAsync(draft, _ => Task.FromResult(true));

            Assert.IsTrue(success);
            Assert.AreEqual(true, draft.DisplaySettings[0].LastKnownHdrSupported);
            Assert.AreEqual(true, draft.DisplaySettings[0].LastKnownWcgSupported);
            Assert.AreEqual("edited", _pm.GetProfile(canonical.Id).Description);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_StaleDraft_PreservesNewerCanonicalUnsupportedEvidence()
        {
            var canonical = MakeProfile("Profile");
            canonical.DisplaySettings.Add(new DisplaySetting
            {
                TargetId = 11,
                ManufacturerName = "MAN",
                ProductCodeID = "1001",
                LastKnownHdrSupported = false,
                LastKnownWcgSupported = false
            });
            Seed(canonical);

            var draft = ProfileDraftFactory.CreateWorkingCopy(canonical);
            draft.DisplaySettings[0].LastKnownHdrSupported = true;
            draft.DisplaySettings[0].LastKnownWcgSupported = null;
            draft.Description = "edited";

            bool success = await _pm.UpdateProfileAsync(draft, _ => Task.FromResult(true));

            Assert.IsTrue(success);
            Assert.AreEqual(false, draft.DisplaySettings[0].LastKnownHdrSupported);
            Assert.AreEqual(false, draft.DisplaySettings[0].LastKnownWcgSupported);
            Assert.AreEqual("edited", _pm.GetProfile(canonical.Id).Description);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task PersistHardwareSelfHealMetadataAsync_MetadataFirstThenStaleUserSave_PreservesFullRepairAndUserEdit()
        {
            var store = new BlockingProfileStore(_appDataFolder);
            _pm = CreateService(_appDataFolder, () => [], store);

            var canonical = MakeProfile("Profile");
            canonical.SchemaVersion = ProfileSerializer.CurrentSchemaVersion;
            canonical.DisplaySettings.Add(new DisplaySetting
            {
                TargetId = 11,
                NativeWidth = 0,
                NativeHeight = 0,
                ManufacturerName = "",
                ProductCodeID = ""
            });
            Seed(canonical);

            var staleUserDraft = ProfileDraftFactory.CreateWorkingCopy(canonical);
            staleUserDraft.Description = "newer user edit";

            var metadataSnapshot = ProfileDraftFactory.CreateWorkingCopy(canonical);
            metadataSnapshot.DisplaySettings[0].NativeWidth = 3840;
            metadataSnapshot.DisplaySettings[0].NativeHeight = 2160;
            metadataSnapshot.DisplaySettings[0].ManufacturerName = "SAM";
            metadataSnapshot.DisplaySettings[0].ProductCodeID = "1234";
            metadataSnapshot.DisplaySettings[0].LastKnownHdrSupported = true;
            metadataSnapshot.DisplaySettings[0].LastKnownWcgSupported = false;

            var metadataWrite = _pm.PersistHardwareSelfHealMetadataAsync(metadataSnapshot);
            await store.FirstWriteStarted.Task;

            var userWrite = _pm.UpdateProfileAsync(staleUserDraft);
            await Task.Yield();
            Assert.IsFalse(userWrite.IsCompleted);

            store.ReleaseFirstWrite();

            Assert.IsTrue(await metadataWrite);
            Assert.IsTrue(await userWrite);

            var final = _pm.GetProfile(canonical.Id);
            Assert.AreEqual("newer user edit", final.Description);
            Assert.AreEqual(3840, final.DisplaySettings[0].NativeWidth);
            Assert.AreEqual(2160, final.DisplaySettings[0].NativeHeight);
            Assert.AreEqual("SAM", final.DisplaySettings[0].ManufacturerName);
            Assert.AreEqual("1234", final.DisplaySettings[0].ProductCodeID);
            Assert.AreEqual(true, final.DisplaySettings[0].LastKnownHdrSupported);
            Assert.AreEqual(false, final.DisplaySettings[0].LastKnownWcgSupported);

            var persisted = ProfileSerializer.DeserializeProfile(store.LastWrittenJson);
            Assert.AreEqual("newer user edit", persisted.Description);
            Assert.AreEqual(3840, persisted.DisplaySettings[0].NativeWidth);
            Assert.AreEqual(2160, persisted.DisplaySettings[0].NativeHeight);
            Assert.AreEqual("SAM", persisted.DisplaySettings[0].ManufacturerName);
            Assert.AreEqual("1234", persisted.DisplaySettings[0].ProductCodeID);
            Assert.AreEqual(true, persisted.DisplaySettings[0].LastKnownHdrSupported);
            Assert.AreEqual(false, persisted.DisplaySettings[0].LastKnownWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task PersistHardwareSelfHealMetadataAsync_StaleRepairAfterNewerUserSave_PreservesBoth()
        {
            var store = new BlockingProfileStore(_appDataFolder);
            _pm = CreateService(_appDataFolder, () => [], store);

            var canonical = MakeProfile("Profile");
            canonical.SchemaVersion = ProfileSerializer.CurrentSchemaVersion;
            canonical.DisplaySettings.Add(new DisplaySetting
            {
                TargetId = 11,
                NativeWidth = 0,
                NativeHeight = 0,
                ManufacturerName = "",
                ProductCodeID = ""
            });
            Seed(canonical);

            var staleRepair = ProfileDraftFactory.CreateWorkingCopy(canonical);
            staleRepair.DisplaySettings[0].NativeWidth = 3840;
            staleRepair.DisplaySettings[0].NativeHeight = 2160;
            staleRepair.DisplaySettings[0].ManufacturerName = "SAM";
            staleRepair.DisplaySettings[0].ProductCodeID = "1234";
            staleRepair.DisplaySettings[0].LastKnownHdrSupported = true;
            staleRepair.DisplaySettings[0].LastKnownWcgSupported = false;

            var userDraft = ProfileDraftFactory.CreateWorkingCopy(canonical);
            userDraft.Description = "newer user edit";

            var userWrite = _pm.UpdateProfileAsync(userDraft);
            await store.FirstWriteStarted.Task;

            var metadataWrite = _pm.PersistHardwareSelfHealMetadataAsync(staleRepair);
            await Task.Yield();
            Assert.IsFalse(metadataWrite.IsCompleted);

            store.ReleaseFirstWrite();

            Assert.IsTrue(await userWrite);
            Assert.IsTrue(await metadataWrite);

            var final = _pm.GetProfile(canonical.Id);
            Assert.AreEqual("newer user edit", final.Description);
            Assert.AreEqual(3840, final.DisplaySettings[0].NativeWidth);
            Assert.AreEqual(2160, final.DisplaySettings[0].NativeHeight);
            Assert.AreEqual("SAM", final.DisplaySettings[0].ManufacturerName);
            Assert.AreEqual("1234", final.DisplaySettings[0].ProductCodeID);
            Assert.AreEqual(true, final.DisplaySettings[0].LastKnownHdrSupported);
            Assert.AreEqual(false, final.DisplaySettings[0].LastKnownWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task AddProfileAsync_PersistenceFails_DoesNotPublish()
        {
            var profile = MakeProfile("A");
            bool eventRaised = false;
            EventHandler<Profile> handler = (_, _) => eventRaised = true;
            _pm.ProfileAdded += handler;

            try
            {
                bool success = await _pm.AddProfileAsync(profile, _ => Task.FromResult(false));

                Assert.IsFalse(success);
                Assert.AreEqual(0, _pm.GetProfileCount());
                Assert.IsFalse(eventRaised);
            }
            finally
            {
                _pm.ProfileAdded -= handler;
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task AddProfileAsync_PersistenceSucceeds_PublishesAfterPersistence()
        {
            var profile = MakeProfile("A");
            bool persisted = false;
            bool eventObservedPersistence = false;
            EventHandler<Profile> handler = (_, _) => eventObservedPersistence = persisted;
            _pm.ProfileAdded += handler;

            try
            {
                bool success = await _pm.AddProfileAsync(profile, _ =>
                {
                    persisted = true;
                    return Task.FromResult(true);
                });

                Assert.IsTrue(success);
                Assert.AreEqual(1, _pm.GetProfileCount());
                Assert.IsTrue(eventObservedPersistence);
            }
            finally
            {
                _pm.ProfileAdded -= handler;
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_PersistenceFails_PreservesAuthoritativeProfile()
        {
            var original = MakeProfile("A");
            Seed(original);
            var replacement = MakeProfile("B");
            replacement.Id = original.Id;
            bool eventRaised = false;
            EventHandler<Profile> handler = (_, _) => eventRaised = true;
            _pm.ProfileUpdated += handler;

            try
            {
                bool success = await _pm.UpdateProfileAsync(replacement, _ => Task.FromResult(false));

                Assert.IsFalse(success);
                Assert.AreSame(original, _pm.GetProfile(original.Id));
                Assert.IsFalse(eventRaised);
            }
            finally
            {
                _pm.ProfileUpdated -= handler;
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task DeleteProfileAsync_PersistenceFails_PreservesAuthoritativeProfile()
        {
            var profile = MakeProfile("A");
            Seed(profile);
            bool eventRaised = false;
            EventHandler<string> handler = (_, _) => eventRaised = true;
            _pm.ProfileDeleted += handler;

            try
            {
                bool success = await _pm.DeleteProfileAsync(profile.Id, _ => Task.FromResult(false));

                Assert.IsFalse(success);
                Assert.AreSame(profile, _pm.GetProfile(profile.Id));
                Assert.IsFalse(eventRaised);
            }
            finally
            {
                _pm.ProfileDeleted -= handler;
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task DeleteProfileAsync_PersistenceSucceeds_PublishesAfterPersistence()
        {
            var profile = MakeProfile("A");
            Seed(profile);
            bool persisted = false;
            bool eventObservedPersistence = false;
            EventHandler<string> handler = (_, _) => eventObservedPersistence = persisted;
            _pm.ProfileDeleted += handler;

            try
            {
                bool success = await _pm.DeleteProfileAsync(profile.Id, _ =>
                {
                    persisted = true;
                    return Task.FromResult(true);
                });

                Assert.IsTrue(success);
                Assert.IsNull(_pm.GetProfile(profile.Id));
                Assert.IsTrue(eventObservedPersistence);
            }
            finally
            {
                _pm.ProfileDeleted -= handler;
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasTotalProfileLoadFailure_ExistingFilesWithNoLoadedProfiles_ReturnsTrue()
        {
            Assert.IsTrue(ProfileService.HasTotalProfileLoadFailure(2, 0));
            Assert.IsFalse(ProfileService.HasTotalProfileLoadFailure(0, 0));
            Assert.IsFalse(ProfileService.HasTotalProfileLoadFailure(2, 1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryNormalizeProfileId_ValidAlternateGuidForm_NormalizesToCanonicalForm()
        {
            const string canonicalId = "f16b2268-0208-4cb8-b479-f0206791f014";

            bool success = ProfileStore.TryNormalizeProfileId("{F16B2268-0208-4CB8-B479-F0206791F014}", out string normalizedId);

            Assert.IsTrue(success);
            Assert.AreEqual(canonicalId, normalizedId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryNormalizeProfileId_PathLikeValue_IsRejected()
        {
            bool success = ProfileStore.TryNormalizeProfileId(@"..\outside.dpm", out string normalizedId);

            Assert.IsFalse(success);
            Assert.IsNull(normalizedId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveProfileNameOrId_IdMatchTakesPrecedenceOverNameCollision()
        {
            const string profileId = "f16b2268-0208-4cb8-b479-f0206791f014";
            var idMatch = MakeProfile("ID Match");
            idMatch.Id = profileId;
            var nameCollision = MakeProfile(profileId);
            Seed(nameCollision, idMatch);

            var resolved = _pm.ResolveProfileNameOrId(profileId);

            Assert.AreSame(idMatch, resolved);
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
            p.LastModifiedDate = DateTime.Now.AddDays(-1);
            Seed(p);
            var before = p.LastModifiedDate;

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
            original.ScriptSettings.Scripts.Add(new Script("script.ps1"));
            original.ScriptSettings.Enabled = true;
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreEqual(1, dup.ScriptSettings.Scripts.Count);
            Assert.IsTrue(dup.ScriptSettings.Enabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_Scripts_AreDeepCopy()
        {
            var original = MakeProfile("A");
            original.ScriptSettings.Scripts.Add(new Script("script.ps1"));
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);
            dup.ScriptSettings.Scripts.Clear();

            Assert.AreEqual(1, original.ScriptSettings.Scripts.Count, "Clearing the duplicate's Scripts must not affect the original.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_CopiesAudioSettings()
        {
            var original = MakeProfile("A");
            original.AudioSettings = new AudioSetting("pb-id", "Speakers", "cap-id", "Mic")
            {
                Enabled = true,
                ApplyPlaybackDevice = true
            };
            Seed(original);

            var dup = _pm.DuplicateProfile(original.Id);

            Assert.AreEqual("pb-id", dup.AudioSettings.DefaultPlaybackDeviceId);
            Assert.AreEqual("Speakers", dup.AudioSettings.PlaybackDeviceName);
            Assert.IsTrue(dup.AudioSettings.Enabled);
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
            var result = new ProfileApplyResult
            {
                DisplayConfigApplied = false,
                DpiChanged = true,
                AudioSuccess = true
            };

            StringAssert.Contains(ProfileApplyFormatter.GetApplyResultErrorMessage("Profile", result), "Profile");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyResultErrorMessage_ContainsDisplayConfigStatus()
        {
            var result = new ProfileApplyResult { DisplayConfigApplied = false };

            StringAssert.Contains(ProfileApplyFormatter.GetApplyResultErrorMessage("X", result), "Display");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyResultErrorMessage_ContainsDpiStatus()
        {
            var result = new ProfileApplyResult { DpiChanged = false };

            StringAssert.Contains(ProfileApplyFormatter.GetApplyResultErrorMessage("X", result), "DPI");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyResultErrorMessage_ContainsAudioStatus()
        {
            var result = new ProfileApplyResult { AudioSuccess = false };

            StringAssert.Contains(ProfileApplyFormatter.GetApplyResultErrorMessage("X", result), "Audio");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileApplyResult_DefaultSuccess_IsFalse()
        {
            var result = new ProfileApplyResult();

            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileApplyResult_ColorStageDefaults_AreSuccessful()
        {
            var result = new ProfileApplyResult();

            Assert.IsTrue(result.AdvancedColorSuccess);
            Assert.IsTrue(result.ColorProfileSuccess);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyResultErrorMessage_ContainsColorStageStatuses()
        {
            var result = new ProfileApplyResult
            {
                AdvancedColorSuccess = false,
                ColorProfileSuccess = false
            };

            string message = ProfileApplyFormatter.GetApplyResultErrorMessage("X", result);

            StringAssert.Contains(message, "Advanced Color: False");
            StringAssert.Contains(message, "Color Profile: False");
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
            var longName = new string('z', ProfileNamingPolicy.MaxProfileNameLength);
            Seed(MakeProfile(longName));

            var result = _pm.GetDuplicateProfileName(longName);

            Assert.IsTrue(result.Length <= ProfileNamingPolicy.MaxProfileNameLength, $"'{result}' is {result.Length} characters");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetDuplicateProfileName_TruncationKeepsMarkerAndMarksCut()
        {
            var longName = new string('z', ProfileNamingPolicy.MaxProfileNameLength);
            Seed(MakeProfile(longName));

            var result = _pm.GetDuplicateProfileName(longName);

            StringAssert.EndsWith(result, " - Copy");
            StringAssert.Contains(result, "\u2026");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetUniqueProfileName_KeepsExistingCopyMarkerWhenNumbering()
        {
            var stem = new string('z', ProfileNamingPolicy.MaxProfileNameLength - " - Copy".Length);
            var copy = stem + " - Copy";
            Seed(MakeProfile(stem), MakeProfile(copy));

            var result = _pm.GetUniqueProfileName(copy);

            Assert.IsTrue(result.Length <= ProfileNamingPolicy.MaxProfileNameLength);
            StringAssert.Contains(result, " - Copy");
            StringAssert.EndsWith(result, " (1)");
        }

        // SelectRollbackTarget

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_RecoveryDisabled_ReturnsNone()
        {
            var target = ProfileRollbackPolicy.SelectRollbackTarget(rollbackAfterApplyFailure: false, rollbackToPreviousProfile: true, hasPreviousProfile: true);

            Assert.AreEqual(ProfileRollbackPolicy.RollbackTarget.None, target);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_PreviousProfileRecoveryWithPreviousProfile_ReturnsPreviousProfile()
        {
            var target = ProfileRollbackPolicy.SelectRollbackTarget(rollbackAfterApplyFailure: true, rollbackToPreviousProfile: true, hasPreviousProfile: true);

            Assert.AreEqual(ProfileRollbackPolicy.RollbackTarget.PreviousProfile, target);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_PreviousProfileRecoveryWithoutPreviousProfile_ReturnsSnapshot()
        {
            var target = ProfileRollbackPolicy.SelectRollbackTarget(rollbackAfterApplyFailure: true, rollbackToPreviousProfile: true, hasPreviousProfile: false);

            Assert.AreEqual(ProfileRollbackPolicy.RollbackTarget.Snapshot, target);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_SnapshotRecovery_ReturnsSnapshot()
        {
            var target = ProfileRollbackPolicy.SelectRollbackTarget(rollbackAfterApplyFailure: true, rollbackToPreviousProfile: false, hasPreviousProfile: true);

            Assert.AreEqual(ProfileRollbackPolicy.RollbackTarget.Snapshot, target);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectRollbackTarget_SnapshotRecoveryWithoutPreviousProfile_ReturnsSnapshot()
        {
            var target = ProfileRollbackPolicy.SelectRollbackTarget(rollbackAfterApplyFailure: true, rollbackToPreviousProfile: false, hasPreviousProfile: false);

            Assert.AreEqual(ProfileRollbackPolicy.RollbackTarget.Snapshot, target);
        }
    }

    [TestClass]
    public class ResolveLiveDisplayTests
    {
        private static DisplayConfigInfo Live(uint targetId, string manufacturer, string productCode, string deviceName)
        {
            return new DisplayConfigInfo
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
            var live = new List<DisplayConfigInfo>
            {
                Live(1, "MAN", "A1B2", "\\\\.\\DISPLAY1"),
                Live(2, "MAN", "A1B2", "\\\\.\\DISPLAY2")
            };

            var resolved = DisplayAddressResolver.ResolveLiveDisplay(Stored(2, "MAN", "A1B2"), live);

            Assert.AreEqual(2u, resolved.TargetId, "Two identical panels must resolve to the stored port, not to whichever matches first.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_FollowsIdentityWhenMonitorMovedPorts()
        {
            var live = new List<DisplayConfigInfo>
            {
                Live(1, "DEV", "C3D4", "\\\\.\\DISPLAY1"),
                Live(2, "MAN", "A1B2", "\\\\.\\DISPLAY2")
            };

            var resolved = DisplayAddressResolver.ResolveLiveDisplay(Stored(1, "MAN", "A1B2"), live);

            Assert.AreEqual(2u, resolved.TargetId);
            Assert.AreEqual("\\\\.\\DISPLAY2", resolved.DeviceName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_AppliesToPortWhenCapturedMonitorIsAbsent()
        {
            var live = new List<DisplayConfigInfo> { Live(1, "DEV", "C3D4", "\\\\.\\DISPLAY1") };

            var resolved = DisplayAddressResolver.ResolveLiveDisplay(Stored(1, "MAN", "A1B2"), live);

            Assert.AreEqual(1u, resolved.TargetId, "A replaced monitor still applies, so a profile cannot leave the desktop unconfigured.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_MatchesOnTargetIdWhenIdentityIsAbsent()
        {
            var live = new List<DisplayConfigInfo> { Live(1, "", "", "\\\\.\\DISPLAY1") };

            var resolved = DisplayAddressResolver.ResolveLiveDisplay(Stored(1, "", ""), live);

            Assert.AreEqual(1u, resolved.TargetId, "Profiles predating EDID capture must resolve exactly as they did before.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_MasksTargetIdToBaseId()
        {
            var live = new List<DisplayConfigInfo> { Live(0x00010004, "MAN", "A1B2", "\\\\.\\DISPLAY1") };

            var resolved = DisplayAddressResolver.ResolveLiveDisplay(Stored(4, "MAN", "A1B2"), live);

            Assert.IsNotNull(resolved, "Profiles store the low 16 bits, so a live raw target id must still match.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_ReturnsNullWhenDisplayIsDisconnected()
        {
            var live = new List<DisplayConfigInfo> { Live(1, "DEV", "C3D4", "\\\\.\\DISPLAY1") };

            Assert.IsNull(DisplayAddressResolver.ResolveLiveDisplay(Stored(9, "MAN", "A1B2"), live));
            Assert.IsNull(DisplayAddressResolver.ResolveLiveDisplay(Stored(9, "MAN", "A1B2"), []));
        }
    }
}
