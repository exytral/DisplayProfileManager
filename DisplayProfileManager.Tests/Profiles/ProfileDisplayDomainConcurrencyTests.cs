using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Profiles.Policy;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.Tests.Helpers;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileDisplayDomainConcurrencyTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_CanonicalRemovesDisplayWhileEditorOnlyRenames_DoesNotResurrectDisplay()
        {
            await WithService(async service =>
            {
                var original = ProfileWith(Setting(11, "MAN", "1001"), Setting(22, "DEV", "1002"));
                Assert.IsTrue(await service.AddProfileAsync(original));
                var baseline = CloneSettings(original);
                var staleDraft = ProfileDraftFactory.CreateWorkingCopy(original);

                var current = ProfileDraftFactory.CreateWorkingCopy(original);
                current.DisplaySettings.RemoveAt(1);
                Assert.IsTrue(await service.UpdateProfileAsync(current));

                staleDraft.Name = "Renamed";
                Assert.IsTrue(await service.UpdateProfileAsync(staleDraft, baseline));

                var saved = service.GetProfile(original.Id);
                Assert.AreEqual("Renamed", saved.Name);
                Assert.AreEqual(1, saved.DisplaySettings.Count);
                Assert.AreEqual("1001", saved.DisplaySettings[0].ProductCodeID);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_CanonicalReplacesDisplayWhileEditorDomainUnchanged_CarriesCurrentGraphForward()
        {
            await WithService(async service =>
            {
                var original = ProfileWith(Setting(11, "MAN", "1001"), Setting(22, "DEV", "1002"));
                Assert.IsTrue(await service.AddProfileAsync(original));
                var baseline = CloneSettings(original);
                var staleDraft = ProfileDraftFactory.CreateWorkingCopy(original);

                var current = ProfileDraftFactory.CreateWorkingCopy(original);
                current.DisplaySettings[1] = Setting(33, "ALT", "1003");
                Assert.IsTrue(await service.UpdateProfileAsync(current));

                staleDraft.Description = "Unrelated edit";
                Assert.IsTrue(await service.UpdateProfileAsync(staleDraft, baseline));

                var saved = service.GetProfile(original.Id);
                CollectionAssert.AreEquivalent(
                    new[] { "1001", "1003" },
                    saved.DisplaySettings.Select(setting => setting.ProductCodeID).ToArray());
                Assert.AreEqual("Unrelated edit", saved.Description);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_EditorChangesDisplayGraphWhileCanonicalUnchanged_PersistsEditorGraph()
        {
            await WithService(async service =>
            {
                var original = ProfileWith(Setting(11, "MAN", "1001"), Setting(22, "DEV", "1002"));
                Assert.IsTrue(await service.AddProfileAsync(original));
                var baseline = CloneSettings(original);
                var draft = ProfileDraftFactory.CreateWorkingCopy(original);
                draft.DisplaySettings.RemoveAt(1);

                Assert.IsTrue(await service.UpdateProfileAsync(draft, baseline));

                var saved = service.GetProfile(original.Id);
                Assert.AreEqual(1, saved.DisplaySettings.Count);
                Assert.AreEqual("1001", saved.DisplaySettings[0].ProductCodeID);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_EditorAndCanonicalChangeDisplayGraphIncompatibly_LeavesStateAndBytesUnchanged()
        {
            string appDataFolder = Path.Combine(Path.GetTempPath(), "DpmDisplayConcurrency-" + Guid.NewGuid().ToString("N"));
            var service = CreateService(appDataFolder);
            var original = ProfileWith(Setting(11, "MAN", "1001"), Setting(22, "DEV", "1002"));
            original.SchemaVersion = ProfileSerializer.CurrentSchemaVersion;

            try
            {
                Assert.IsTrue(await service.AddProfileAsync(original));
                var baseline = CloneSettings(original);
                var draft = ProfileDraftFactory.CreateWorkingCopy(original);
                draft.DisplaySettings[1] = Setting(44, "EDIT", "1004");

                var current = ProfileDraftFactory.CreateWorkingCopy(original);
                current.DisplaySettings[1] = Setting(33, "CUR", "1003");
                Assert.IsTrue(await service.UpdateProfileAsync(current));

                string path = Path.Combine(appDataFolder, "Profiles", original.Id + ".dpm");
                string before = await File.ReadAllTextAsync(path, TestContext.CancellationToken);
                var canonicalBefore = service.GetProfile(original.Id);

                bool saved = await service.UpdateProfileAsync(draft, baseline);
                string after = await File.ReadAllTextAsync(path, TestContext.CancellationToken);

                Assert.IsFalse(saved);
                Assert.AreSame(canonicalBefore, service.GetProfile(original.Id));
                Assert.AreEqual("1003", service.GetProfile(original.Id).DisplaySettings[1].ProductCodeID);
                Assert.AreEqual(before, after);
            }
            finally
            {
                if (Directory.Exists(appDataFolder))
                    Directory.Delete(appDataFolder, true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayDomainEquals_SameModelTwinsWithDistinctTargetIds_IsEqual()
        {
            var left = new List<DisplaySetting>
            {
                Setting(11, "MAN", "1001"),
                Setting(22, "MAN", "1001")
            };
            var right = left.Select(CloneSetting).ToList();

            Assert.IsTrue(ProfileHardwareReconciler.DisplayDomainEquals(left, right));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayDomainEquals_ReorderedSameModelTwinsWithDistinctTargetIds_IsEqual()
        {
            var left = new List<DisplaySetting>
            {
                Setting(11, "MAN", "1001"),
                Setting(22, "MAN", "1001")
            };
            var right = new List<DisplaySetting>
            {
                CloneSetting(left[1]),
                CloneSetting(left[0])
            };

            Assert.IsTrue(ProfileHardwareReconciler.DisplayDomainEquals(left, right));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_SameModelTwinsAndUnrelatedEdit_SavesSuccessfully()
        {
            await WithService(async service =>
            {
                var original = ProfileWith(Setting(11, "MAN", "1001"), Setting(22, "MAN", "1001"));
                Assert.IsTrue(await service.AddProfileAsync(original));
                var baseline = CloneSettings(original);
                var draft = ProfileDraftFactory.CreateWorkingCopy(original);

                draft.Name = "Renamed";
                Assert.IsTrue(await service.UpdateProfileAsync(draft, baseline));

                Assert.AreEqual("Renamed", service.GetProfile(original.Id).Name);
                Assert.AreEqual(2, service.GetProfile(original.Id).DisplaySettings.Count);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayDomainEquals_EquivalentClonePartitionsIgnoreGeneratedLabels()
        {
            var left = new List<DisplaySetting>
            {
                Setting(11, "MAN", "1001"),
                Setting(22, "DEV", "1002"),
                Setting(33, "ALT", "1003")
            };
            var right = left.Select(CloneSetting).ToList();

            left[0].CloneGroupId = "clone-left-a";
            left[1].CloneGroupId = "clone-left-a";
            left[0].IsCloneSource = true;
            right[0].CloneGroupId = "clone-right-z";
            right[1].CloneGroupId = "clone-right-z";
            right[0].IsCloneSource = true;

            Assert.IsTrue(ProfileHardwareReconciler.DisplayDomainEquals(left, right));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayDomainEquals_DifferentClonePartition_IsNotEqual()
        {
            var left = new List<DisplaySetting>
            {
                Setting(11, "MAN", "1001"),
                Setting(22, "DEV", "1002"),
                Setting(33, "ALT", "1003")
            };
            var right = left.Select(CloneSetting).ToList();

            left[0].CloneGroupId = "clone-left-a";
            left[1].CloneGroupId = "clone-left-a";
            left[0].IsCloneSource = true;

            right[0].CloneGroupId = "clone-right-z";
            right[2].CloneGroupId = "clone-right-z";
            right[0].IsCloneSource = true;

            Assert.IsFalse(ProfileHardwareReconciler.DisplayDomainEquals(left, right));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayDomainEquals_ChangedCloneSourceRole_IsNotEqual()
        {
            var left = new List<DisplaySetting>
            {
                Setting(11, "MAN", "1001"),
                Setting(22, "DEV", "1002")
            };
            var right = left.Select(CloneSetting).ToList();

            left[0].CloneGroupId = "clone-left";
            left[1].CloneGroupId = "clone-left";
            left[0].IsCloneSource = true;
            right[0].CloneGroupId = "clone-right";
            right[1].CloneGroupId = "clone-right";
            right[1].IsCloneSource = true;

            Assert.IsFalse(ProfileHardwareReconciler.DisplayDomainEquals(left, right));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayDomainEquals_EdidAuthorityCannotBeStolenByEarlierTargetFallback_IsNotEqual()
        {
            var left = new List<DisplaySetting>
            {
                RelationshipSetting(11, "A", "1001"),
                RelationshipSetting(22, "B", "1002")
            };
            var right = new List<DisplaySetting>
            {
                RelationshipSetting(11, "B", "1002"),
                RelationshipSetting(22, "C", "1003")
            };

            Assert.IsFalse(ProfileHardwareReconciler.DisplayDomainEquals(left, right));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayDomainEquals_EdidAuthorityCannotBeStolenByTargetFallback_WhenSourcesReordered_IsNotEqual()
        {
            var left = new List<DisplaySetting>
            {
                RelationshipSetting(22, "B", "1002"),
                RelationshipSetting(11, "A", "1001")
            };
            var right = new List<DisplaySetting>
            {
                RelationshipSetting(11, "B", "1002"),
                RelationshipSetting(22, "C", "1003")
            };

            Assert.IsFalse(ProfileHardwareReconciler.DisplayDomainEquals(left, right));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_EdidVsFallbackContention_DoesNotResurrectStaleDisplayGraph()
        {
            string appDataFolder = Path.Combine(Path.GetTempPath(), "DpmDisplayConcurrency-" + Guid.NewGuid().ToString("N"));
            var service = CreateService(appDataFolder);
            var original = ProfileWith(
                RelationshipSetting(11, "A", "1001"),
                RelationshipSetting(22, "B", "1002"));
            original.SchemaVersion = ProfileSerializer.CurrentSchemaVersion;

            try
            {
                Assert.IsTrue(await service.AddProfileAsync(original));
                var baseline = CloneSettings(original);
                var staleDraft = ProfileDraftFactory.CreateWorkingCopy(original);
                staleDraft.Description = "Unrelated stale edit";

                var canonical = service.GetProfile(original.Id);
                canonical.DisplaySettings.Clear();
                canonical.DisplaySettings.Add(RelationshipSetting(11, "B", "1002"));
                canonical.DisplaySettings.Add(RelationshipSetting(22, "C", "1003"));
                Assert.IsTrue(await service.SaveProfileAsync(canonical));

                string path = Path.Combine(appDataFolder, "Profiles", original.Id + ".dpm");
                var canonicalBefore = service.GetProfile(original.Id);
                var serializedBefore = ProfileSerializer.DeserializeProfile(await File.ReadAllTextAsync(path, TestContext.CancellationToken));

                Assert.IsTrue(await service.UpdateProfileAsync(staleDraft, baseline));

                var saved = service.GetProfile(original.Id);
                var serializedAfter = ProfileSerializer.DeserializeProfile(await File.ReadAllTextAsync(path, TestContext.CancellationToken));
                Assert.AreEqual("Unrelated stale edit", saved.Description);
                CollectionAssert.AreEqual(
                    new[] { "B/1002/11", "C/1003/22" },
                    saved.DisplaySettings.Select(RelationshipKey).ToArray());
                CollectionAssert.AreEqual(
                    serializedBefore.DisplaySettings.Select(RelationshipKey).ToArray(),
                    serializedAfter.DisplaySettings.Select(RelationshipKey).ToArray());
                Assert.AreNotSame(canonicalBefore, saved);
            }
            finally
            {
                if (Directory.Exists(appDataFolder))
                    Directory.Delete(appDataFolder, true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayDomainEquals_AmbiguousSameEdidWithoutUniqueBaseTargetPairing_FailsClosed()
        {
            var left = new List<DisplaySetting>
            {
                Setting(11, "MAN", "1001"),
                Setting(22, "MAN", "1001")
            };
            var right = new List<DisplaySetting>
            {
                Setting(33, "MAN", "1001"),
                Setting(44, "MAN", "1001")
            };

            Assert.IsFalse(ProfileHardwareReconciler.DisplayDomainEquals(left, right));
        }

        private static DisplaySetting CloneSetting(DisplaySetting setting)
        {
            var profile = ProfileWith(setting);
            return ProfileDraftFactory.CreateWorkingCopy(profile).DisplaySettings[0];
        }

        private static List<DisplaySetting> CloneSettings(Profile profile) =>
            ProfileDraftFactory.CreateWorkingCopy(profile).DisplaySettings;

        private static Profile ProfileWith(params DisplaySetting[] settings)
        {
            var profile = new Profile("Profile")
            {
                SchemaVersion = ProfileSerializer.CurrentSchemaVersion
            };
            profile.DisplaySettings.AddRange(settings);
            return profile;
        }

        private static DisplaySetting RelationshipSetting(uint targetId, string manufacturer, string product)
        {
            return new DisplaySettingBuilder()
                .WithTargetId(targetId)
                .WithEdid(manufacturer, product)
                .WithResolution(1920, 1080)
                .WithPosition(0, 0)
                .Build();
        }

        private static string RelationshipKey(DisplaySetting setting) =>
            $"{setting.ManufacturerName}/{setting.ProductCodeID}/{setting.TargetId & 0xFFFFu}";

        private static DisplaySetting Setting(uint targetId, string manufacturer, string product)
        {
            return new DisplaySettingBuilder()
                .WithTargetId(targetId)
                .WithEdid(manufacturer, product)
                .WithResolution(1920, 1080)
                .WithPosition((int)targetId, 0)
                .Build();
        }

        private static async Task WithService(Func<ProfileService, Task> action)
        {
            string appDataFolder = Path.Combine(Path.GetTempPath(), "DpmDisplayConcurrency-" + Guid.NewGuid().ToString("N"));
            try
            {
                await action(CreateService(appDataFolder));
            }
            finally
            {
                if (Directory.Exists(appDataFolder))
                    Directory.Delete(appDataFolder, true);
            }
        }

        private static ProfileService CreateService(string appDataFolder)
        {
            var settings = new SettingsService(new SettingsStore(appDataFolder));
            return new ProfileService(
                new ProfileStore(appDataFolder),
                new ProfileMigrationService(settings),
                new ProfileDraftFactory(),
                new ProfileCaptureService(),
                settings,
                () => [],
                () => []);
        }
    }
}
