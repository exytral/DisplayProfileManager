using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Application;
using DisplayProfileManager.Integration;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.Settings.Persistence;

namespace DisplayProfileManager.Tests.Integration
{
    [TestClass]
    public class ProfileShortcutPlannerTests
    {
        private const string ExecutablePath = @"C:\Program Files\Display Profile Manager\DisplayProfileManager.exe";
        private const string ProfileId = "f16b2268-0208-4cb8-b479-f0206791f014";
        private string _tempFolder;

        [TestInitialize]
        public void Setup()
        {
            _tempFolder = Path.Combine(Path.GetTempPath(), "DPM-ProfileShortcutPlannerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempFolder);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempFolder))
                Directory.Delete(_tempFolder, true);
        }

        private string CreateIconFile(string name, bool valid)
        {
            string path = Path.Combine(_tempFolder, name);
            if (valid)
            {
                using var stream = File.Create(path);
                SystemIcons.Application.Save(stream);
            }
            else
            {
                File.WriteAllText(path, "not an icon");
            }

            return path;
        }

        private static Profile CreateProfile(string name = "Profile", string id = ProfileId) =>
            new(name) { Id = id };

        private static ProfileShortcutSnapshot Snapshot(ProfileShortcutSpec shortcut) =>
            new()
            {
                FileName = shortcut.FileName,
                TargetPath = shortcut.TargetPath,
                Arguments = shortcut.Arguments,
                WorkingDirectory = shortcut.WorkingDirectory,
                Description = shortcut.Description,
                IconPath = shortcut.IconPath
            };

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildDesiredShortcuts_ProfileId_IsUsedAsStableArgument()
        {
            var profile = CreateProfile();

            var shortcut = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => null).Single();

            Assert.AreEqual($"--profile {ProfileId} --source StartMenuShortcut", shortcut.Arguments);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildDesiredShortcuts_ValidExistingIcon_UsesConfiguredIcon()
        {
            var profile = CreateProfile();
            string iconPath = CreateIconFile("valid.ico", valid: true);

            var shortcut = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => iconPath).Single();

            Assert.AreEqual(iconPath, shortcut.IconPath);
            Assert.AreEqual($"--profile {ProfileId} --source StartMenuShortcut", shortcut.Arguments);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildDesiredShortcuts_MissingIcon_UsesExecutableIcon()
        {
            var profile = CreateProfile();
            string iconPath = Path.Combine(_tempFolder, "missing.ico");

            var shortcut = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => iconPath).Single();

            Assert.AreEqual(ExecutablePath, shortcut.IconPath);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildDesiredShortcuts_MalformedExistingIcon_UsesExecutableIcon()
        {
            var profile = CreateProfile();
            string iconPath = CreateIconFile("malformed.ico", valid: false);

            var shortcut = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => iconPath).Single();

            Assert.AreEqual(ExecutablePath, shortcut.IconPath);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Plan_IconChangesFromValidToInvalid_RewritesToExecutableIcon()
        {
            var profile = CreateProfile();
            string validIconPath = CreateIconFile("valid.ico", valid: true);
            string invalidIconPath = CreateIconFile("invalid.ico", valid: false);
            var before = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => validIconPath).Single();
            var after = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => invalidIconPath).Single();

            var operations = ProfileShortcutPlanner.Plan([Snapshot(before)], [after]);

            Assert.AreEqual(1, operations.Count);
            Assert.AreEqual(ProfileShortcutOperationKind.Upsert, operations[0].Kind);
            Assert.AreEqual(ExecutablePath, operations[0].Shortcut.IconPath);
            Assert.AreEqual(before.Arguments, operations[0].Shortcut.Arguments);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Plan_IconChangesFromInvalidToValid_RewritesToConfiguredIcon()
        {
            var profile = CreateProfile();
            string invalidIconPath = CreateIconFile("invalid.ico", valid: false);
            string validIconPath = CreateIconFile("valid.ico", valid: true);
            var before = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => invalidIconPath).Single();
            var after = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => validIconPath).Single();

            var operations = ProfileShortcutPlanner.Plan([Snapshot(before)], [after]);

            Assert.AreEqual(1, operations.Count);
            Assert.AreEqual(ProfileShortcutOperationKind.Upsert, operations[0].Kind);
            Assert.AreEqual(validIconPath, operations[0].Shortcut.IconPath);
            Assert.AreEqual(before.Arguments, operations[0].Shortcut.Arguments);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildDesiredShortcuts_Rename_PreservesIdentityAndUpdatesPresentation()
        {
            var profile = CreateProfile("Profile A");
            var before = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => null).Single();
            profile.Name = "Profile B";

            var after = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => null).Single();

            Assert.AreEqual(before.Arguments, after.Arguments);
            Assert.AreEqual("Profile B.lnk", after.FileName);
            Assert.AreNotEqual(before.Description, after.Description);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Plan_MissingDesiredShortcut_CreatesIt()
        {
            var desired = ProfileShortcutPlanner.BuildDesiredShortcuts([CreateProfile()], ExecutablePath, _ => null);

            var operations = ProfileShortcutPlanner.Plan([], desired);

            Assert.AreEqual(1, operations.Count);
            Assert.AreEqual(ProfileShortcutOperationKind.Upsert, operations[0].Kind);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Plan_DeletedProfile_RemovesStaleShortcut()
        {
            var stale = ProfileShortcutPlanner.BuildDesiredShortcuts([CreateProfile()], ExecutablePath, _ => null).Single();

            var operations = ProfileShortcutPlanner.Plan([Snapshot(stale)], []);

            Assert.AreEqual(1, operations.Count);
            Assert.AreEqual(ProfileShortcutOperationKind.Delete, operations[0].Kind);
            Assert.AreEqual(stale.FileName, operations[0].FileName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Plan_ExecutablePathChanges_RewritesShortcut()
        {
            var profile = CreateProfile();
            var oldShortcut = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], @"C:\Old\DisplayProfileManager.exe", _ => null).Single();
            var newShortcut = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => null).Single();

            var operations = ProfileShortcutPlanner.Plan([Snapshot(oldShortcut)], [newShortcut]);

            Assert.AreEqual(1, operations.Count);
            Assert.AreEqual(ProfileShortcutOperationKind.Upsert, operations[0].Kind);
            Assert.AreEqual(ExecutablePath, operations[0].Shortcut.TargetPath);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Plan_LegacyArguments_RewritesShortcutWithSourceHint()
        {
            var desired = ProfileShortcutPlanner.BuildDesiredShortcuts([CreateProfile()], ExecutablePath, _ => null).Single();
            var legacy = new ProfileShortcutSnapshot
            {
                FileName = desired.FileName,
                TargetPath = desired.TargetPath,
                Arguments = $"--profile {ProfileId}",
                WorkingDirectory = desired.WorkingDirectory,
                Description = desired.Description,
                IconPath = desired.IconPath
            };

            var operations = ProfileShortcutPlanner.Plan([legacy], [desired]);

            Assert.AreEqual(1, operations.Count);
            Assert.AreEqual(ProfileShortcutOperationKind.Upsert, operations[0].Kind);
            Assert.AreEqual($"--profile {ProfileId} --source StartMenuShortcut", operations[0].Shortcut.Arguments);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Plan_ExactManagedState_IsIdempotent()
        {
            var shortcut = ProfileShortcutPlanner.BuildDesiredShortcuts([CreateProfile()], ExecutablePath, _ => null).Single();

            var operations = ProfileShortcutPlanner.Plan([Snapshot(shortcut)], [shortcut]);

            Assert.AreEqual(0, operations.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Plan_UnreadableStaleShortcut_RemovesItSafely()
        {
            var unreadable = new ProfileShortcutSnapshot { FileName = "Stale.lnk", IsReadable = false };

            var operations = ProfileShortcutPlanner.Plan([unreadable], []);

            Assert.AreEqual(1, operations.Count);
            Assert.AreEqual(ProfileShortcutOperationKind.Delete, operations[0].Kind);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Plan_Rename_DeletesOldPresentationAndCreatesNewPresentation()
        {
            var profile = CreateProfile("Profile A");
            var before = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => null).Single();
            profile.Name = "Profile B";
            var after = ProfileShortcutPlanner.BuildDesiredShortcuts([profile], ExecutablePath, _ => null).Single();

            var operations = ProfileShortcutPlanner.Plan([Snapshot(before)], [after]);

            Assert.AreEqual(2, operations.Count);
            Assert.IsTrue(operations.Any(o => o.Kind == ProfileShortcutOperationKind.Delete && o.FileName == "Profile A.lnk"));
            Assert.IsTrue(operations.Any(o => o.Kind == ProfileShortcutOperationKind.Upsert && o.FileName == "Profile B.lnk"));
        }
    }

    [TestClass]
    public class StartMenuProfileShortcutManagerTests
    {
        private sealed class FakeSettingsStore : ISettingsStore
        {
            public string SettingsFilePath => @"C:\Config\Settings.json";
            public string AppDataFolder { get; init; }
            public AppSettings LoadedSettings { get; init; } = new AppSettings();
            public Task<AppSettings> LoadAsync() => Task.FromResult(LoadedSettings);
            public Task<bool> SaveAsync(AppSettings settings) => Task.FromResult(true);
        }

        private sealed class FakeShortcutStore : IProfileShortcutStore
        {
            private readonly Dictionary<string, ProfileShortcutSnapshot> _entries = new(StringComparer.OrdinalIgnoreCase);

            public int Writes { get; private set; }
            public int Deletes { get; private set; }
            public IReadOnlyCollection<ProfileShortcutSnapshot> Entries => _entries.Values;

            public IReadOnlyList<ProfileShortcutSnapshot> ReadAll() => [.. _entries.Values];

            public void Write(ProfileShortcutSpec shortcut)
            {
                Writes++;
                _entries[shortcut.FileName] = new ProfileShortcutSnapshot
                {
                    FileName = shortcut.FileName,
                    TargetPath = shortcut.TargetPath,
                    Arguments = shortcut.Arguments,
                    WorkingDirectory = shortcut.WorkingDirectory,
                    Description = shortcut.Description,
                    IconPath = shortcut.IconPath
                };
            }

            public void Delete(string fileName)
            {
                Deletes++;
                _entries.Remove(fileName);
            }

            public void DeleteFolderIfEmpty()
            {
            }

            public void Clear() => _entries.Clear();
        }

        private string _appDataFolder;

        [TestInitialize]
        public void Setup()
        {
            _appDataFolder = Path.Combine(Path.GetTempPath(), "DPM-StartMenuShortcutTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_appDataFolder);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_appDataFolder))
                Directory.Delete(_appDataFolder, true);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ProfileLifecycle_EnabledManagement_ReconcilesLoadAddUpdateDelete()
        {
            var settings = new SettingsService(new FakeSettingsStore
            {
                AppDataFolder = _appDataFolder,
                LoadedSettings = new AppSettings { ManageStartMenuProfileShortcuts = true }
            });
            Assert.IsTrue(await settings.LoadSettingsAsync());

            var profiles = CreateProfileService(settings);
            var profile = new Profile("Profile A")
            {
                Id = "f16b2268-0208-4cb8-b479-f0206791f014",
                SchemaVersion = ProfileSerializer.CurrentSchemaVersion
            };
            Assert.IsTrue(await profiles.AddProfileAsync(profile));

            var shortcutStore = new FakeShortcutStore();
            using var manager = new StartMenuProfileShortcutManager(
                profiles,
                settings,
                shortcutStore,
                () => @"C:\App\DisplayProfileManager.exe");

            Assert.IsTrue(await profiles.LoadProfilesAsync());
            Assert.AreEqual(1, shortcutStore.Entries.Count);
            Assert.AreEqual("--profile f16b2268-0208-4cb8-b479-f0206791f014 --source StartMenuShortcut", shortcutStore.Entries.Single().Arguments);

            var added = new Profile("Profile B")
            {
                Id = "d85c395b-67a7-4e70-aa79-ce1784127001",
                SchemaVersion = ProfileSerializer.CurrentSchemaVersion
            };
            profiles.AddProfile(added);
            Assert.AreEqual(2, shortcutStore.Entries.Count);

            added.Name = "Profile C";
            profiles.UpdateProfile(added);
            Assert.IsFalse(shortcutStore.Entries.Any(e => e.FileName == "Profile B.lnk"));
            Assert.IsTrue(shortcutStore.Entries.Any(e => e.FileName == "Profile C.lnk"));

            profiles.DeleteProfile(added.Id);
            Assert.AreEqual(1, shortcutStore.Entries.Count);
            Assert.IsTrue(shortcutStore.Deletes >= 2);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task RemoveAllAsync_DedicatedManagedStore_RemovesOnlyManagedEntries()
        {
            var settings = new SettingsService(new FakeSettingsStore { AppDataFolder = _appDataFolder });
            Assert.IsTrue(await settings.LoadSettingsAsync());
            var profiles = CreateProfileService(settings);
            profiles.AddProfile(new Profile("Profile") { Id = "f16b2268-0208-4cb8-b479-f0206791f014" });
            var shortcutStore = new FakeShortcutStore();

            using var manager = new StartMenuProfileShortcutManager(
                profiles,
                settings,
                shortcutStore,
                () => @"C:\App\DisplayProfileManager.exe");

            Assert.IsTrue(await manager.SynchronizeAsync());
            Assert.AreEqual(1, shortcutStore.Entries.Count);

            Assert.IsTrue(await manager.RemoveAllAsync());

            Assert.AreEqual(0, shortcutStore.Entries.Count);
            Assert.AreEqual(1, shortcutStore.Deletes);
        }

        private ProfileService CreateProfileService(SettingsService settings)
        {
            var store = new ProfileStore(_appDataFolder);
            return new ProfileService(
                store,
                new ProfileMigrationService(settings),
                new ProfileDraftFactory(),
                new ProfileCaptureService(),
                settings,
                () => [],
                () => []);
        }
    }

    [TestClass]
    public class ProfileShortcutSettingsCoordinatorTests
    {
        private sealed class FakeSettingsStore : ISettingsStore
        {
            public string SettingsFilePath => @"C:\Config\Settings.json";
            public string AppDataFolder => @"C:\Config";
            public AppSettings LoadedSettings { get; init; } = new AppSettings();
            public bool SaveResult { get; init; } = true;
            public Task<AppSettings> LoadAsync() => Task.FromResult(LoadedSettings);
            public Task<bool> SaveAsync(AppSettings settings) => Task.FromResult(SaveResult);
        }

        private sealed class FakeShortcutManager : IProfileShortcutManager
        {
            public bool SynchronizeResult { get; init; } = true;
            public bool RemoveResult { get; init; } = true;
            public int SynchronizeCalls { get; private set; }
            public int RemoveCalls { get; private set; }

            public Task<bool> SynchronizeAsync()
            {
                SynchronizeCalls++;
                return Task.FromResult(SynchronizeResult);
            }

            public Task<bool> RemoveAllAsync()
            {
                RemoveCalls++;
                return Task.FromResult(RemoveResult);
            }
        }

        private sealed class FakeAutoStartService : IAutoStartService
        {
            public bool IsAutoStartEnabled() => false;
            public AutoStartOperationResult EnableAutoStart(AutoStartMode mode, bool startInTray = false) => AutoStartOperationResult.Success;
            public AutoStartOperationResult DisableAutoStart() => AutoStartOperationResult.Success;
        }

        private sealed class FakeShellRegistrar : IShellExtensionRegistrar
        {
            public bool IsRegistered() => false;
            public bool Register() => true;
            public bool Unregister() => true;
            public bool RestartExplorer() => true;
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task SetManageStartMenuProfileShortcuts_EnableSyncFails_PreservesDisabledSetting()
        {
            var settings = await CreateSettingsAsync(new FakeSettingsStore());
            var shortcuts = new FakeShortcutManager { SynchronizeResult = false };
            var coordinator = CreateCoordinator(settings, shortcuts);

            bool result = await coordinator.SetManageStartMenuProfileShortcutsAsync(true);

            Assert.IsFalse(result);
            Assert.IsFalse(settings.ShouldManageStartMenuProfileShortcuts());
            Assert.AreEqual(1, shortcuts.SynchronizeCalls);
            Assert.AreEqual(1, shortcuts.RemoveCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task SetManageStartMenuProfileShortcuts_SettingsSaveFails_RestoresDisabledExternalState()
        {
            var settings = await CreateSettingsAsync(new FakeSettingsStore { SaveResult = false });
            var shortcuts = new FakeShortcutManager();
            var coordinator = CreateCoordinator(settings, shortcuts);

            bool result = await coordinator.SetManageStartMenuProfileShortcutsAsync(true);

            Assert.IsFalse(result);
            Assert.IsFalse(settings.ShouldManageStartMenuProfileShortcuts());
            Assert.AreEqual(1, shortcuts.SynchronizeCalls);
            Assert.AreEqual(1, shortcuts.RemoveCalls);
        }

        private static async Task<SettingsService> CreateSettingsAsync(FakeSettingsStore store)
        {
            var settings = new SettingsService(store);
            Assert.IsTrue(await settings.LoadSettingsAsync());
            return settings;
        }

        private static SettingsCoordinator CreateCoordinator(SettingsService settings, IProfileShortcutManager shortcuts) =>
            new(settings, new FakeAutoStartService(), new FakeShellRegistrar(), shortcuts);
    }

    [TestClass]
    public class ProfileShortcutBoundaryTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "DPM-StartMenuSynthetic-" + Guid.NewGuid().ToString("N"));

        [TestMethod]
        [TestCategory("Unit")]
        public void Planner_ReservedAndUnicodeNames_ProduceSafeComponents()
        {
            foreach (string name in new[] { "CON.txt", "NUL.log", "COM¹", "LPT¹.foo", "AUX.csv", "Résumé", ".", "..", "sub\\child", "a:b", new string('x', 400) })
            {
                string fileName = ProfileShortcutPlanner.SanitizeShortcutName(name) + ".lnk";
                string resolved = ProfileShortcutPlanner.GetManagedShortcutPath(Root, fileName);
                Assert.AreEqual(fileName, Path.GetFileName(resolved), name);
                Assert.IsTrue(fileName.Length <= 255, name);
            }

            Assert.AreEqual("_CON.txt", ProfileShortcutPlanner.SanitizeShortcutName("CON.txt"));
            Assert.AreEqual("_NUL.log", ProfileShortcutPlanner.SanitizeShortcutName("NUL.log"));
            Assert.AreEqual("_COM¹", ProfileShortcutPlanner.SanitizeShortcutName("COM¹"));
            Assert.AreEqual("_LPT¹.foo", ProfileShortcutPlanner.SanitizeShortcutName("LPT¹.foo"));
            Assert.AreEqual("Résumé", ProfileShortcutPlanner.SanitizeShortcutName("Résumé"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Planner_CaseInsensitiveLongCollision_RemainsDistinctWithinLimit()
        {
            var profiles = new[]
            {
                new Profile(new string('A', 400)) { Id = "f16b2268-0208-4cb8-b479-f0206791f014" },
                new Profile(new string('a', 400)) { Id = "d85c395b-67a7-4e70-aa79-ce1784127001" }
            };
            var shortcuts = ProfileShortcutPlanner.BuildDesiredShortcuts(profiles, @"C:\App\DisplayProfileManager.exe", _ => null);
            Assert.AreEqual(2, shortcuts.Count);
            Assert.IsFalse(string.Equals(shortcuts[0].FileName, shortcuts[1].FileName, StringComparison.OrdinalIgnoreCase));
            foreach (var shortcut in shortcuts)
            {
                Assert.IsTrue(shortcut.FileName.Length <= 255);
                ProfileShortcutPlanner.GetManagedShortcutPath(Root, shortcut.FileName);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Store_InvalidName_RejectsBeforeCreatingManagedFolder()
        {
            string parent = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            var store = new StartMenuProfileShortcutStore(parent);
            foreach (string name in new[]
            {
                null, "", " ", ".", "..", ".lnk", "name", "name.txt", "item.lnk.", "item.lnk ",
                "../x.lnk", @"..\x.lnk", @"sub\x.lnk", "sub/x.lnk", @"C:\Other\x.lnk", @"\\host\share\x.lnk",
                "x:ads.lnk", "CON.txt.lnk", "NUL.log.lnk", "COM¹.lnk", "LPT².foo.lnk",
                "control\u0001.lnk", new string('z', 252) + ".lnk"
            })
            {
                Assert.ThrowsExactly<ArgumentException>(() => store.Delete(name), name ?? "(null)");
                Assert.ThrowsExactly<ArgumentException>(() => store.Write(new ProfileShortcutSpec { FileName = name }), name ?? "(null)");
                Assert.IsFalse(Directory.Exists(parent), name);
            }
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void Planner_DotPrefixTruncation_AlwaysProducesNonemptyValidName()
        {
            foreach (int dots in new[] { 250, 251, 252 })
            {
                string name = new string('.', dots) + "A";
                string fileName = ProfileShortcutPlanner.SanitizeShortcutName(name) + ".lnk";

                Assert.IsTrue(fileName.Length > 4, name);
                Assert.IsTrue(fileName.Length <= 255, name);
                ProfileShortcutPlanner.GetManagedShortcutPath(Root, fileName);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Planner_SurrogateBoundary_PreservesCompleteScalars()
        {
            foreach (int prefix in new[] { 249, 250, 251, 252 })
            {
                string name = new string('A', prefix) + "😊";
                string fileName = ProfileShortcutPlanner.SanitizeShortcutName(name) + ".lnk";

                Assert.IsTrue(fileName.Length <= 255);
                ProfileShortcutPlanner.GetManagedShortcutPath(Root, fileName);
                Assert.IsFalse(char.IsHighSurrogate(fileName[^5]));
            }
            Assert.AreEqual(new string('A', 249) + "😊", ProfileShortcutPlanner.SanitizeShortcutName(new string('A', 249) + "😊"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Store_UnpairedSurrogates_RejectBeforeFilesystemEffects()
        {
            string parent = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            var store = new StartMenuProfileShortcutStore(parent);

            foreach (string name in new[] { "bad\uD83D.lnk", "bad\uDE0A.lnk", "\uD83D\uD83D.lnk", "\uDE0A\uD83D.lnk" })
            {
                Assert.ThrowsExactly<ArgumentException>(() => store.Write(new ProfileShortcutSpec { FileName = name }));
                Assert.ThrowsExactly<ArgumentException>(() => store.Delete(name));
                Assert.IsFalse(Directory.Exists(parent));
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Planner_ConsoleAliases_SanitizeAndStoreReject()
        {
            foreach (string name in new[] { "CONIN$", "CONOUT$", "conin$.txt", "ConOut$.foo" })
            {
                string safe = ProfileShortcutPlanner.SanitizeShortcutName(name) + ".lnk";

                Assert.IsTrue(safe.StartsWith("_", StringComparison.Ordinal));
                ProfileShortcutPlanner.GetManagedShortcutPath(Root, safe);
                Assert.ThrowsExactly<ArgumentException>(() => ProfileShortcutPlanner.GetManagedShortcutPath(Root, name + ".lnk"));
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Planner_SharedGuidPrefixCollisionOrder_IsDeterministicAndValid()
        {
            var profiles = Enumerable.Range(0, 128).Select(i =>
                new Profile(new string('A', 300))
                {
                    Id = $"f16b2268-0208-4cb8-b479-{i:x12}"
                }).ToArray();

            var forward = ProfileShortcutPlanner.BuildDesiredShortcuts(profiles, @"C:\App\DisplayProfileManager.exe", _ => null);
            var reverse = ProfileShortcutPlanner.BuildDesiredShortcuts(profiles.Reverse(), @"C:\App\DisplayProfileManager.exe", _ => null);

            CollectionAssert.AreEqual(forward.Select(x => x.FileName).ToArray(), reverse.Select(x => x.FileName).ToArray());
            Assert.AreEqual(128, forward.Select(x => x.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var spec in forward)
            {
                Assert.IsTrue(spec.FileName.Length <= 255);
                ProfileShortcutPlanner.GetManagedShortcutPath(Root, spec.FileName);
                Assert.IsTrue(profiles.Any(p => spec.Arguments.Contains(p.Id, StringComparison.Ordinal)));
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Store_CanonicalChild_RejectsTraversalAndAcceptsOrdinaryNamesWithoutEffects()
        {
            foreach (string name in new[] { "Profile.lnk", "Résumé.lnk", "A.B.lnk", "Profile (f16b2268).lnk" })
            {
                string path = ProfileShortcutPlanner.GetManagedShortcutPath(Root, name);
                Assert.AreEqual(Path.GetFullPath(Root), Path.GetDirectoryName(path));
                Assert.AreEqual(name, Path.GetFileName(path));
            }
            Assert.IsFalse(Directory.Exists(Root));
        }
    }

}
