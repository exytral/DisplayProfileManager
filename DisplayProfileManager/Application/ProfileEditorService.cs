using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using DisplayProfileManager.Audio;
using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Hotkeys;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Icons;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Scripts;
using DisplayProfileManager.Scripts.Models;
using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Application
{
    internal sealed class ProfileEditorService(
        ProfileService profiles,
        DisplayEditorService displays,
        ScriptLibrary scripts,
        IAudioDeviceService audio,
        IWallpaperService wallpaper)
    {
        private readonly ProfileService _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        private readonly DisplayEditorService _displays = displays ?? throw new ArgumentNullException(nameof(displays));
        private readonly ScriptLibrary _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
        private readonly IAudioDeviceService _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        private readonly IWallpaperService _wallpaper = wallpaper ?? throw new ArgumentNullException(nameof(wallpaper));
        private readonly ConditionalWeakTable<Profile, Profile> _displayBaselines = [];

        public Profile CreateWorkingDraft(Profile profile)
        {
            if (profile == null)
            {
                return ProfileDraftFactory.CreateNewProfile();
            }

            var draft = ProfileDraftFactory.CreateWorkingCopy(profile);
            _displayBaselines.Add(draft, ProfileDraftFactory.CreateWorkingCopy(profile));
            return draft;
        }

        public string GetUniqueProfileName(string baseName) => _profiles.GetUniqueProfileName(baseName);
        public Profile GetDefaultProfile() => _profiles.GetDefaultProfile();
        public bool HasProfile(string name) => _profiles.HasProfile(name);

        public Task<List<DisplaySetting>> CaptureCurrentDisplaySettingsAsync() => _profiles.GetCurrentDisplaySettingsAsync();

        public List<DisplayConfigInfo> GetLiveDisplays()
        {
            var liveDisplays = _displays.GetLiveDisplays();
            _profiles.ObserveAdvancedColorCapabilities(liveDisplays);
            return liveDisplays;
        }
        public List<DisplayInfo> GetGdiDisplays() => _displays.GetGdiDisplays();
        public DisplayCapabilities GetDisplayCapabilities(string deviceName) => _displays.GetCapabilities(deviceName);
        public List<ResolutionInfo> GetAvailableResolutions(string deviceName) => _displays.GetAvailableResolutions(deviceName);
        public List<string> GetSupportedResolutionsOnly(string deviceName) => _displays.GetSupportedResolutionsOnly(deviceName);
        public List<int> GetAvailableRefreshRates(string deviceName, int width, int height) => _displays.GetAvailableRefreshRates(deviceName, width, height);
        public DpiScalingInfo GetDpiScaling(DisplayConfigInfo display) => _displays.GetDpiScaling(display);
        public List<uint> GetSupportedDpiScaling(string deviceName) => _displays.GetSupportedDpiScaling(deviceName);
        public IReadOnlyList<string> GetColorProfiles(bool hdrOnly) => _displays.GetColorProfiles(hdrOnly);
        public string GetDefaultColorProfile(DisplayConfigInfo display) => _displays.GetDefaultColorProfile(display);
        public bool TryGetMonitorBounds(string deviceName, out System.Windows.Rect bounds) => _displays.TryGetMonitorBounds(deviceName, out bounds);
        public bool IsMonitorConnected(string deviceName) => _displays.IsMonitorConnected(deviceName);
        public DisplayConfigInfo ResolveLiveDisplay(DisplaySetting setting, List<DisplayConfigInfo> liveConfigs) => _displays.ResolveLiveDisplay(setting, liveConfigs);

        public List<AudioDeviceInfo> GetPlaybackDevices() => _audio.GetPlaybackDevices();
        public List<AudioDeviceInfo> GetCaptureDevices() => _audio.GetCaptureDevices();
        public AudioDeviceInfo GetDefaultPlaybackDevice() => _audio.GetDefaultPlaybackDevice();
        public AudioDeviceInfo GetDefaultCaptureDevice() => _audio.GetDefaultCaptureDevice();
        public WallpaperSettings CaptureWallpaper() => _wallpaper.Capture();

        public Task<ScriptImportBatchResult> ImportScriptsAsync(IEnumerable<string> sourcePaths) => _scripts.ImportScriptsAsync(sourcePaths);

        public Task<string> ImportIconAsync(string sourcePath) => ProfileIconLibrary.ImportIconAsync(sourcePath);

        public IReadOnlyList<string> GetAvailableIcons() => ProfileIconLibrary.GetAvailableIcons();

        public Profile FindHotkeyConflict(Profile draft)
        {
            if (draft?.HotkeyConfig == null || !draft.HotkeyConfig.IsEnabled)
            {
                return null;
            }

            return HotkeyConflictPolicy.FindProfileConflict(
                _profiles.GetProfilesWithHotkeys(),
                draft.HotkeyConfig,
                draft.Id);
        }

        public Profile FindHotkeyConflict(Profile draft, HotkeyConfig hotkey) =>
            draft == null
                ? null
                : HotkeyConflictPolicy.FindProfileConflict(_profiles.GetProfilesWithHotkeys(), hotkey, draft.Id);

        public Task<bool> SaveAsync(Profile draft, bool isNew)
        {
            if (isNew)
            {
                return _profiles.AddProfileAsync(draft);
            }

            return _displayBaselines.TryGetValue(draft, out var baseline)
                ? _profiles.UpdateProfileAsync(draft, baseline.DisplaySettings)
                : _profiles.UpdateProfileAsync(draft);
        }

        public Task<bool> SetDefaultAsync(string profileId) => _profiles.SetDefaultProfileAsync(profileId);
    }
}
