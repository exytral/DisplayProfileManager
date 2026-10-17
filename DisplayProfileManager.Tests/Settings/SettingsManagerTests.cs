using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.Settings.Persistence;

namespace DisplayProfileManager.Tests.Settings
{
    [TestClass]
    public class SettingsManagerTests
    {
        private sealed class FakeSettingsStore : ISettingsStore
        {
            public string SettingsFilePath => @"C:\Config\Settings.json";
            public string AppDataFolder => @"C:\Config";
            public AppSettings LoadedSettings { get; set; } = new AppSettings();
            public bool SaveResult { get; set; } = true;
            public List<string> SavedCurrentProfileIds { get; } = [];
            public TaskCompletionSource<bool> FirstSaveEntered { get; set; }
            public TaskCompletionSource<bool> ReleaseFirstSave { get; set; }

            public Task<AppSettings> LoadAsync() => Task.FromResult(LoadedSettings);

            public async Task<bool> SaveAsync(AppSettings settings)
            {
                SavedCurrentProfileIds.Add(settings.CurrentProfileId);
                if (SavedCurrentProfileIds.Count == 1 && FirstSaveEntered != null && ReleaseFirstSave != null)
                {
                    FirstSaveEntered.TrySetResult(true);
                    await ReleaseFirstSave.Task;
                }
                return SaveResult;
            }
        }

        private static SettingsService CreateLoadedService(FakeSettingsStore store)
        {
            var service = new SettingsService(store);
            Assert.IsTrue(service.LoadSettingsAsync().GetAwaiter().GetResult());
            return service;
        }

        private static AppSettings DeserializeSettingsTolerant(string json) =>
            SettingsStore.DeserializeSettingsTolerant(json);

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeSettingsTolerant_MalformedBooleanMember_FallsBackToDefaultAndPreservesSiblingMember()
        {
            var settings = DeserializeSettingsTolerant(@"{ ""theme"": ""Dark"", ""checkForUpdates"": ""not-a-bool"" }");
            Assert.IsNotNull(settings);
            Assert.AreEqual("Dark", settings.Theme);
            Assert.IsFalse(settings.CheckForUpdates);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeSettingsTolerant_DifferentMalformedMember_StillLoadsRemainingSettings()
        {
            var settings = DeserializeSettingsTolerant(@"{ ""startWithWindows"": ""not-a-bool"", ""language"": ""fr-FR"" }");
            Assert.IsNotNull(settings);
            Assert.AreEqual("fr-FR", settings.Language);
            Assert.IsFalse(settings.StartWithWindows);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeSettingsTolerant_MalformedMember_StillReturnsNonNullAppSettings()
        {
            Assert.IsNotNull(DeserializeSettingsTolerant(@"{ ""autoStartMode"": [ 1, 2, 3 ] }"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FindLegacySettingsPath_ModernCasingOnly_DoesNotSelectModernFile()
        {
            Assert.IsNull(SettingsStore.FindLegacySettingsPath([@"C:\Config\Settings.json"]));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FindLegacySettingsPath_ExactLegacyCasing_SelectsLegacyFile()
        {
            const string legacyPath = @"C:\Config\settings.json";
            Assert.AreEqual(legacyPath, SettingsStore.FindLegacySettingsPath([@"C:\Config\Settings.json", legacyPath]));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SaveSettingsAsync_WhenSettingsNotLoaded_RefusesToSave()
        {
            var service = new SettingsService(new FakeSettingsStore());
            Assert.IsFalse(service.SaveSettingsAsync().GetAwaiter().GetResult());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task SetDefaultProfileIdAsync_SaveFails_RestoresPreviousInMemoryValue()
        {
            var store = new FakeSettingsStore
            {
                LoadedSettings = new AppSettings { DefaultProfileId = "previous-profile" },
                SaveResult = false
            };
            var service = CreateLoadedService(store);

            bool result = await service.SetDefaultProfileIdAsync("new-profile");

            Assert.IsFalse(result);
            Assert.AreEqual("previous-profile", service.Settings.DefaultProfileId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task SetDefaultProfileIdAsync_SaveSucceeds_KeepsNewInMemoryValue()
        {
            var store = new FakeSettingsStore
            {
                LoadedSettings = new AppSettings { DefaultProfileId = "previous-profile" }
            };
            var service = CreateLoadedService(store);

            bool result = await service.SetDefaultProfileIdAsync("new-profile");

            Assert.IsTrue(result);
            Assert.AreEqual("new-profile", service.Settings.DefaultProfileId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task SetCurrentProfileIdAsync_SaveFails_RestoresPreviousInMemoryValue()
        {
            var store = new FakeSettingsStore
            {
                LoadedSettings = new AppSettings { CurrentProfileId = "previous-profile" },
                SaveResult = false
            };
            var service = CreateLoadedService(store);

            bool result = await service.SetCurrentProfileIdAsync("new-profile");

            Assert.IsFalse(result);
            Assert.AreEqual("previous-profile", service.Settings.CurrentProfileId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task SetCurrentProfileIdAsync_SaveSucceeds_KeepsNewInMemoryValue()
        {
            var store = new FakeSettingsStore
            {
                LoadedSettings = new AppSettings { CurrentProfileId = "previous-profile" }
            };
            var service = CreateLoadedService(store);

            bool result = await service.SetCurrentProfileIdAsync("new-profile");

            Assert.IsTrue(result);
            Assert.AreEqual("new-profile", service.Settings.CurrentProfileId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task PersistMutation_SaveFails_DoesNotPublishUncommittedState()
        {
            var store = new FakeSettingsStore
            {
                LoadedSettings = new AppSettings { Theme = "System" },
                SaveResult = false
            };
            var service = CreateLoadedService(store);
            int events = 0;
            service.SettingsChanged += (_, __) => events++;

            bool result = await service.SetThemeAsync("Dark");

            Assert.IsFalse(result);
            Assert.AreEqual("System", service.Settings.Theme);
            Assert.AreEqual(0, events);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task PersistMutation_ThrowingSubscriber_PersistsAndReportsSuccess()
        {
            string folder = Path.Combine(Path.GetTempPath(), "DPM-Settings-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SettingsStore(folder);
                var service = new SettingsService(store);
                Assert.IsTrue(await service.LoadSettingsAsync());
                bool laterSubscriberCalled = false;
                service.SettingsChanged += (_, __) => throw new InvalidOperationException("Synthetic subscriber failure");
                service.SettingsChanged += (_, __) => laterSubscriberCalled = true;

                bool result = await Task.Run(() => service.SetCurrentProfileIdAsync("persisted-profile"));
                AppSettings persisted = await new SettingsStore(folder).LoadAsync();

                Assert.IsTrue(result);
                Assert.AreEqual("persisted-profile", service.Settings.CurrentProfileId);
                Assert.AreEqual(service.Settings.CurrentProfileId, persisted.CurrentProfileId);
                Assert.IsTrue(laterSubscriberCalled);
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task PersistMutation_ConcurrentCalls_AreSerializedInOrder()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new FakeSettingsStore
            {
                LoadedSettings = new AppSettings { CurrentProfileId = "initial" },
                FirstSaveEntered = entered,
                ReleaseFirstSave = release
            };
            var service = CreateLoadedService(store);

            var first = service.SetCurrentProfileIdAsync("first");
            await entered.Task;
            var second = service.SetCurrentProfileIdAsync("second");
            release.TrySetResult(true);
            await Task.WhenAll(first, second);

            CollectionAssert.AreEqual(new[] { "first", "second" }, store.SavedCurrentProfileIds.ToArray());
            Assert.AreEqual("second", service.Settings.CurrentProfileId);
        }
        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_QuickSwitchDefaults_AreSafe()
        {
            var settings = new AppSettings();

            Assert.IsFalse(settings.QuickSwitchEnabled);
            Assert.IsFalse(settings.QuickSwitchUseAcrylic);
            Assert.AreEqual(Key.P, settings.QuickSwitchHotkey.Key);
            Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Alt, settings.QuickSwitchHotkey.ModifierKeys);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ClearQuickSwitchHotkey_DisablesAndClearsInOnePersistedState()
        {
            var store = new FakeSettingsStore
            {
                LoadedSettings = new AppSettings
                {
                    QuickSwitchEnabled = true,
                    QuickSwitchHotkey = new HotkeyConfig(Key.O, ModifierKeys.Control | ModifierKeys.Alt)
                }
            };
            var service = CreateLoadedService(store);

            Assert.IsTrue(await service.ClearQuickSwitchHotkeyAsync());

            Assert.IsFalse(service.Settings.QuickSwitchEnabled);
            Assert.AreEqual(Key.None, service.Settings.QuickSwitchHotkey.Key);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task QuickSwitchHotkey_AfterClear_CanBeAssignedWhileRemainingDisabled()
        {
            var store = new FakeSettingsStore
            {
                LoadedSettings = new AppSettings
                {
                    QuickSwitchEnabled = true,
                    QuickSwitchHotkey = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt)
                }
            };
            var service = CreateLoadedService(store);
            var replacement = new HotkeyConfig(Key.O, ModifierKeys.Control | ModifierKeys.Shift);

            Assert.IsTrue(await service.ClearQuickSwitchHotkeyAsync());
            Assert.IsTrue(await service.SetQuickSwitchHotkeyAsync(replacement));

            Assert.IsFalse(service.Settings.QuickSwitchEnabled);
            Assert.AreEqual(replacement, service.Settings.QuickSwitchHotkey);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task QuickSwitchAcrylicCompatibility_UnrelatedMutation_PreservesSerializedTrue()
        {
            AppSettings loaded = DeserializeSettingsTolerant(
                @"{ ""theme"": ""System"", ""quickSwitchAcrylic"": true }");
            var store = new FakeSettingsStore { LoadedSettings = loaded };
            var service = CreateLoadedService(store);

            Assert.IsTrue(await service.SetThemeAsync("Dark"));

            string json = JsonConvert.SerializeObject(service.Settings);
            StringAssert.Contains(json, "\"quickSwitchAcrylic\":true");
            Assert.IsTrue(service.Settings.QuickSwitchUseAcrylic);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task LegacyDebugBlock_UnrelatedMutation_DropsBlockAndPreservesKnownSiblings()
        {
            AppSettings loaded = DeserializeSettingsTolerant(
                @"{ ""theme"": ""Dark"", ""quickSwitchAcrylic"": true, ""debugFlags"": { ""forceApplyFailure"": 2, ""centerIconGrid"": true } }");
            var store = new FakeSettingsStore { LoadedSettings = loaded };
            var service = CreateLoadedService(store);

            Assert.AreEqual("Dark", service.Settings.Theme);
            Assert.IsTrue(service.Settings.QuickSwitchUseAcrylic);
            Assert.IsTrue(await service.SetThemeAsync("Light"));

            string json = JsonConvert.SerializeObject(service.Settings);
            Assert.IsFalse(json.Contains("\"debugFlags\"", StringComparison.Ordinal));
            StringAssert.Contains(json, "\"quickSwitchAcrylic\":true");
            Assert.AreEqual("Light", service.Settings.Theme);
            Assert.IsTrue(service.Settings.QuickSwitchUseAcrylic);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchMonitorTargeting_DefaultsToActiveMouseSource()
        {
            var settings = new AppSettings();

            Assert.IsTrue(settings.QuickSwitchUseActiveMonitor);
            Assert.AreEqual(QuickSwitchActiveMonitorSource.MousePosition, settings.QuickSwitchActiveMonitorSource);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchMonitorTargeting_MissingKeys_PreservesDefaultsAndSibling()
        {
            var settings = DeserializeSettingsTolerant(@"{ ""theme"": ""Dark"" }");

            Assert.IsTrue(settings.QuickSwitchUseActiveMonitor);
            Assert.AreEqual(QuickSwitchActiveMonitorSource.MousePosition, settings.QuickSwitchActiveMonitorSource);
            Assert.AreEqual("Dark", settings.Theme);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchMonitorTargeting_InactiveWindowFocus_RoundTrips()
        {
            var settings = DeserializeSettingsTolerant(
                @"{ ""quickSwitchUseActiveMonitor"": false, ""quickSwitchActiveMonitorSource"": ""WindowFocus"" }");

            string json = JsonConvert.SerializeObject(settings);

            Assert.IsFalse(settings.QuickSwitchUseActiveMonitor);
            Assert.AreEqual(QuickSwitchActiveMonitorSource.WindowFocus, settings.QuickSwitchActiveMonitorSource);
            StringAssert.Contains(json, "\"quickSwitchActiveMonitorSource\":\"WindowFocus\"");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task QuickSwitchMonitorTargeting_ToggleActive_PreservesRetainedSource()
        {
            var service = CreateLoadedService(new FakeSettingsStore
            {
                LoadedSettings = new AppSettings
                {
                    QuickSwitchUseActiveMonitor = false,
                    QuickSwitchActiveMonitorSource = QuickSwitchActiveMonitorSource.WindowFocus
                }
            });

            Assert.IsTrue(await service.SetQuickSwitchUseActiveMonitorAsync(true));
            Assert.AreEqual(QuickSwitchActiveMonitorSource.WindowFocus, service.GetQuickSwitchActiveMonitorSource());

            Assert.IsTrue(await service.SetQuickSwitchUseActiveMonitorAsync(false));
            Assert.AreEqual(QuickSwitchActiveMonitorSource.WindowFocus, service.GetQuickSwitchActiveMonitorSource());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchMonitorTargeting_MalformedMembers_FallBackAndPreserveSibling()
        {
            var malformedBool = DeserializeSettingsTolerant(
                @"{ ""theme"": ""Dark"", ""quickSwitchUseActiveMonitor"": ""bad"" }");
            var malformedSource = DeserializeSettingsTolerant(
                @"{ ""theme"": ""Dark"", ""quickSwitchActiveMonitorSource"": { ""bad"": true } }");

            Assert.IsTrue(malformedBool.QuickSwitchUseActiveMonitor);
            Assert.AreEqual("Dark", malformedBool.Theme);
            Assert.AreEqual(QuickSwitchActiveMonitorSource.MousePosition, malformedSource.QuickSwitchActiveMonitorSource);
            Assert.AreEqual("Dark", malformedSource.Theme);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task QuickSwitchMonitorTargeting_UndefinedSource_NormalizesInModelAndService()
        {
            var settings = new AppSettings
            {
                QuickSwitchActiveMonitorSource = (QuickSwitchActiveMonitorSource)99
            };
            Assert.AreEqual(QuickSwitchActiveMonitorSource.MousePosition, settings.QuickSwitchActiveMonitorSource);

            var service = CreateLoadedService(new FakeSettingsStore());
            Assert.IsTrue(await service.SetQuickSwitchActiveMonitorSourceAsync((QuickSwitchActiveMonitorSource)99));
            Assert.AreEqual(QuickSwitchActiveMonitorSource.MousePosition, service.GetQuickSwitchActiveMonitorSource());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppSettings_StartMenuProfileShortcutDefault_IsOff()
        {
            Assert.IsFalse(new AppSettings().ManageStartMenuProfileShortcuts);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task StartMenuProfileShortcutSetting_SaveSucceeds_PersistsState()
        {
            var service = CreateLoadedService(new FakeSettingsStore());

            Assert.IsTrue(await service.SetManageStartMenuProfileShortcutsAsync(true));

            Assert.IsTrue(service.Settings.ManageStartMenuProfileShortcuts);
            Assert.IsTrue(service.ShouldManageStartMenuProfileShortcuts());
        }

    }
}
