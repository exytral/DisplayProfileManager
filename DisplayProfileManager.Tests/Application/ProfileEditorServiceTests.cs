using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Application;
using DisplayProfileManager.Audio;
using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Scripts;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Tests.Application
{
    [TestClass]
    public class ProfileEditorServiceTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileConstructor_DoesNotOwnCurrentSchema()
        {
            var profile = new Profile("Persisted");

            Assert.AreEqual(0, profile.SchemaVersion);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CreateNewProfile_AssignsCurrentSchema()
        {
            var profile = ProfileDraftFactory.CreateNewProfile("New");

            Assert.AreEqual(ProfileSerializer.CurrentSchemaVersion, profile.SchemaVersion);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task CreateWorkingDraft_NewProfile_IsCurrentSchemaAndPersistable()
        {
            using var harness = new EditorHarness();

            var draft = harness.Editor.CreateWorkingDraft(null);
            bool saved = await harness.Profiles.SaveProfileAsync(draft);

            Assert.AreEqual(ProfileSerializer.CurrentSchemaVersion, draft.SchemaVersion);
            Assert.IsTrue(saved);
            Assert.AreEqual(draft.Id, harness.ProfileStore.LastWrittenProfile.Id);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultProfileFactoryPath_AssignsCurrentSchema()
        {
            var profile = ProfileDraftFactory.CreateDefaultProfile();

            Assert.AreEqual(ProfileSerializer.CurrentSchemaVersion, profile.SchemaVersion);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateProfile_AssignsCurrentSchema()
        {
            var source = new Profile
            {
                Name = "Source",
                SchemaVersion = 1
            };
            var duplicate = new ProfileDraftFactory().DuplicateProfile(source, [source]);

            Assert.AreEqual(ProfileSerializer.CurrentSchemaVersion, duplicate.SchemaVersion);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AudioOperations_DelegateToAudioService()
        {
            using var harness = new EditorHarness();

            Assert.AreSame(harness.Audio.PlaybackDevices, harness.Editor.GetPlaybackDevices());
            Assert.AreSame(harness.Audio.CaptureDevices, harness.Editor.GetCaptureDevices());
            Assert.AreSame(harness.Audio.DefaultPlaybackDevice, harness.Editor.GetDefaultPlaybackDevice());
            Assert.AreSame(harness.Audio.DefaultCaptureDevice, harness.Editor.GetDefaultCaptureDevice());
            Assert.AreEqual(1, harness.Audio.PlaybackCalls);
            Assert.AreEqual(1, harness.Audio.CaptureCalls);
            Assert.AreEqual(1, harness.Audio.DefaultPlaybackCalls);
            Assert.AreEqual(1, harness.Audio.DefaultCaptureCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CaptureWallpaper_DelegatesToWallpaperService()
        {
            using var harness = new EditorHarness();

            var snapshot = harness.Editor.CaptureWallpaper();

            Assert.AreSame(harness.Wallpaper.Snapshot, snapshot);
            Assert.AreEqual(1, harness.Wallpaper.CaptureCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_DelegatesThroughDisplayEditorService()
        {
            using var harness = new EditorHarness();
            var stored = new DisplaySetting
            {
                TargetId = 1,
                ManufacturerName = "MAN",
                ProductCodeID = "A1B2"
            };
            var live = new List<DisplayConfigInfo>
            {
                new() {
                    TargetId = 2,
                    ManufacturerName = "DEV",
                    ProductCodeID = "C3D4",
                    DeviceName = @"\\.\DISPLAY2"
                },
                new() {
                    TargetId = 3,
                    ManufacturerName = "MAN",
                    ProductCodeID = "A1B2",
                    DeviceName = @"\\.\DISPLAY3"
                }
            };

            var resolved = harness.Editor.ResolveLiveDisplay(stored, live);

            Assert.IsNotNull(resolved);
            Assert.AreEqual(3u, resolved.TargetId);
            Assert.AreEqual(@"\\.\DISPLAY3", resolved.DeviceName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayApplicationSeam_ExposesAcceptedOperations()
        {
            string[] methodNames =
            [
                nameof(ProfileEditorService.GetLiveDisplays),
                nameof(ProfileEditorService.GetGdiDisplays),
                nameof(ProfileEditorService.GetDisplayCapabilities),
                nameof(ProfileEditorService.GetAvailableResolutions),
                nameof(ProfileEditorService.GetDpiScaling),
                nameof(ProfileEditorService.GetColorProfiles),
                nameof(ProfileEditorService.GetDefaultColorProfile),
                nameof(ProfileEditorService.TryGetMonitorBounds),
                nameof(ProfileEditorService.IsMonitorConnected),
                nameof(ProfileEditorService.ResolveLiveDisplay)
            ];

            foreach (string methodName in methodNames)
            {
                Assert.IsNotNull(typeof(ProfileEditorService).GetMethod(methodName), methodName);
            }
        }

        private sealed class EditorHarness : IDisposable
        {
            private readonly string _tempFolder;

            public FakeProfileStore ProfileStore { get; }
            public ProfileService Profiles { get; }
            public FakeAudioDeviceService Audio { get; }
            public FakeWallpaperService Wallpaper { get; }
            public ProfileEditorService Editor { get; }

            public EditorHarness()
            {
                _tempFolder = Path.Combine(Path.GetTempPath(), "DPM-ProfileEditorServiceTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_tempFolder);

                var settings = new SettingsService(new FakeSettingsStore(_tempFolder));
                ProfileStore = new FakeProfileStore(_tempFolder);
                Profiles = new ProfileService(
                    ProfileStore,
                    new ProfileMigrationService(settings),
                    new ProfileDraftFactory(),
                    new ProfileCaptureService(),
                    settings,
                    () => [],
                    () => []);

                Audio = new FakeAudioDeviceService();
                Wallpaper = new FakeWallpaperService();
                Editor = new ProfileEditorService(
                    Profiles,
                    new DisplayEditorService(),
                    new ScriptLibrary(_tempFolder),
                    Audio,
                    Wallpaper);
            }

            public void Dispose()
            {
                if (Directory.Exists(_tempFolder))
                    Directory.Delete(_tempFolder, true);
            }
        }

        private sealed class FakeProfileStore(string appDataFolder) : IProfileStore
        {
            public string AppDataFolder { get; } = appDataFolder;
            public Profile LastWrittenProfile { get; private set; }

            public string[] EnumerateProfileFiles() => [];
            public Task<string> ReadProfileTextAsync(string path) => Task.FromResult<string>(null);

            public Task<bool> WriteProfileTextAsync(string profileId, string json)
            {
                LastWrittenProfile = ProfileSerializer.DeserializeProfile(json);
                return Task.FromResult(true);
            }

            public Task<bool> DeleteProfileAsync(string profileId) => Task.FromResult(true);
        }

        private sealed class FakeSettingsStore(string appDataFolder) : ISettingsStore
        {
            public string SettingsFilePath { get; } = Path.Combine(appDataFolder, "Settings.json");
            public string AppDataFolder { get; } = appDataFolder;

            public Task<AppSettings> LoadAsync() => Task.FromResult(new AppSettings());
            public Task<bool> SaveAsync(AppSettings settings) => Task.FromResult(true);
        }

        private sealed class FakeAudioDeviceService : IAudioDeviceService
        {
            public List<AudioDeviceInfo> PlaybackDevices { get; } =
            [
                new AudioDeviceInfo { Id = "playback", Name = "Speakers", Type = DeviceType.Playback }
            ];
            public List<AudioDeviceInfo> CaptureDevices { get; } =
            [
                new AudioDeviceInfo { Id = "capture", Name = "Microphone", Type = DeviceType.Capture }
            ];
            public AudioDeviceInfo DefaultPlaybackDevice => PlaybackDevices[0];
            public AudioDeviceInfo DefaultCaptureDevice => CaptureDevices[0];

            public int PlaybackCalls { get; private set; }
            public int CaptureCalls { get; private set; }
            public int DefaultPlaybackCalls { get; private set; }
            public int DefaultCaptureCalls { get; private set; }

            public List<AudioDeviceInfo> GetPlaybackDevices()
            {
                PlaybackCalls++;
                return PlaybackDevices;
            }

            public List<AudioDeviceInfo> GetCaptureDevices()
            {
                CaptureCalls++;
                return CaptureDevices;
            }

            public AudioDeviceInfo GetDefaultPlaybackDevice()
            {
                DefaultPlaybackCalls++;
                return DefaultPlaybackDevice;
            }

            public AudioDeviceInfo GetDefaultCaptureDevice()
            {
                DefaultCaptureCalls++;
                return DefaultCaptureDevice;
            }

            public bool SetDefaultPlaybackDevice(string deviceId) => true;
            public bool SetDefaultCaptureDevice(string deviceId) => true;
            public bool ApplyAudioSettings(AudioSetting settings) => true;
        }

        private sealed class FakeWallpaperService : IWallpaperService
        {
            public WallpaperSettings Snapshot { get; } = new WallpaperSettings();
            public int CaptureCalls { get; private set; }

            public WallpaperSettings Capture()
            {
                CaptureCalls++;
                return Snapshot;
            }

            public bool Apply(WallpaperSettings snapshot) => true;
        }
    }
}
