using DisplayProfileManager.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DisplayProfileManager.Core
{
    public class ProfileManager
    {
        #region Core

        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly object _lock = new object();

        private bool _rollingBack;

        private static ProfileManager _instance;
        private readonly ScriptManager _scriptManager = ScriptManager.Instance;
        private readonly SettingsManager _settingsManager = SettingsManager.Instance;

        private List<Profile> _profiles;
        private string _currentProfileId;

        private readonly string _appDataFolder;
        private readonly string _profilesFolderPath;

        private const int CurrentSchemaVersion = 4;

        public enum ApplySource { Unknown, Window, Tray, Hotkey, CommandLine, Startup }

        public enum RollbackTarget { None, PreviousProfile, Snapshot }

        public class ProfileAppliedEventArgs : EventArgs
        {
            public Profile Profile { get; }
            public ApplySource Source { get; }
            public long DurationMilliseconds { get; }

            public ProfileAppliedEventArgs(Profile profile, ApplySource source, long durationMilliseconds)
            {
                Profile = profile;
                Source = source;
                DurationMilliseconds = durationMilliseconds;
            }
        }

        public class ProfileApplyResult
        {
            public bool Success { get; set; }
            public bool PrimaryChanged { get; set; }
            public bool DisplayConfigApplied { get; set; }
            public bool ResolutionChanged { get; set; }
            public bool DpiChanged { get; set; }
            public bool AudioSuccess { get; set; }
        }


        public static ProfileManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new ProfileManager();
                    }
                }
                return _instance;
            }
        }

        public event EventHandler<Profile> ProfileAdded;
        public event EventHandler<Profile> ProfileUpdated;
        public event EventHandler<string> ProfileDeleted;
        public event EventHandler<ProfileAppliedEventArgs> ProfileApplied;

        public event EventHandler ProfilesLoaded;

        public string CurrentProfileId => _currentProfileId;

        private ProfileManager()
        {
            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayProfileManager");
            _profilesFolderPath = Path.Combine(_appDataFolder, "Profiles");
            _profiles = new List<Profile>();
            _currentProfileId = null;

            EnsureProfilesFolderExists();
        }

        private void EnsureProfilesFolderExists()
        {
            if (!Directory.Exists(_profilesFolderPath))
                Directory.CreateDirectory(_profilesFolderPath);
        }

        #endregion

        #region I/O

        private string GetProfileFilePath(string profileId) => Path.Combine(_profilesFolderPath, $"{profileId}.dpm");

        public static Profile DeserializeProfile(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            var root = JObject.Parse(json);

            RecoverScriptEntries(root);
            RecoverOptionalProperty(root, "audioSettings", typeof(AudioSetting));
            RecoverOptionalProperty(root, "wallpaperSettings", typeof(WallpaperSettings));
            RecoverOptionalProperty(root, "hotkeyConfig", typeof(HotkeyConfig));
            RecoverOptionalProperty(root, "enableWallpaper", typeof(bool));
            RecoverOptionalProperty(root, "enableAudio", typeof(bool));
            RecoverOptionalProperty(root, "enableScripts", typeof(bool));
            RecoverOptionalProperty(root, "name", typeof(string));
            RecoverOptionalProperty(root, "description", typeof(string));
            RecoverOptionalProperty(root, "icon", typeof(string));
            RecoverOptionalProperty(root, "createdDate", typeof(DateTime));
            RecoverOptionalProperty(root, "lastModifiedDate", typeof(DateTime));
            RecoverOptionalProperty(root, "schemaVersion", typeof(int));

            return root.ToObject<Profile>();
        }

        private static void RecoverOptionalProperty(JObject root, string propertyName, Type targetType)
        {
            var token = root[propertyName];
            if (token == null) return;

            try
            {
                token.ToObject(targetType);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Profile member '{propertyName}' could not be read -> using its default: {ex.Message}");
                root.Remove(propertyName);
            }
        }

        private static void RecoverScriptEntries(JObject root)
        {
            var token = root["scripts"];
            if (token == null) return;

            if (!(token is JArray scripts))
            {
                root.Remove("scripts");
                return;
            }

            for (int i = scripts.Count - 1; i >= 0; i--)
            {
                if (scripts[i].Type != JTokenType.Object)
                {
                    scripts.RemoveAt(i);
                    continue;
                }

                try
                {
                    scripts[i].ToObject<Script>();
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Profile script entry at index {i} could not be read -> dropping it: {ex.Message}");
                    scripts.RemoveAt(i);
                }
            }
        }

        public async Task<bool> LoadProfilesAsync()
        {
            EnsureProfilesFolderExists();

            try
            {
                _profiles.Clear();

                var profileFiles = Directory.GetFiles(_profilesFolderPath, "*.dpm");
                List<DisplayConfigHelper.DisplayConfigInfo> liveConfigs = null;
                foreach (var file in profileFiles)
                {
                    try
                    {
                        var json = await Task.Run(() => File.ReadAllText(file));
                        var profile = DeserializeProfile(json);

                        if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || profile.DisplaySettings == null)
                        {
                            _logger.Warn($"Skipping invalid profile file: {Path.GetFileName(file)}");
                            continue;
                        }

                        if (profile.SchemaVersion < CurrentSchemaVersion)
                        {
                            if (liveConfigs == null)
                                liveConfigs = DisplayConfigHelper.GetDisplayConfigs();

                            bool migrated = await MigrateProfileAsync(profile, liveConfigs, json);
                            if (migrated)
                            {
                                var savedDate = profile.LastModifiedDate;
                                await SaveProfileAsync(profile);
                                profile.LastModifiedDate = savedDate;
                                _logger.Info($"Migrated profile '{profile.Name}' to schema {CurrentSchemaVersion}");
                            }
                        }

                        _profiles.Add(profile);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Error loading profile from {file}");
                    }
                }

                if (_profiles.Count == 0)
                    await CreateDefaultProfileAsync();

                _currentProfileId = _settingsManager.GetCurrentProfileId();
                ProfilesLoaded?.Invoke(this, EventArgs.Empty);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading profiles");
                _profiles = new List<Profile>();
                return false;
            }
        }

        private async Task<bool> MigrateProfileAsync(Profile profile, List<DisplayConfigHelper.DisplayConfigInfo> liveConfigs, string rawJson = null)
        {
            bool changed = false;

            // Backfill native dimensions and display name
            if (profile.SchemaVersion < 1)
            {
                foreach (var setting in profile.DisplaySettings)
                {
                    var match = liveConfigs.FirstOrDefault(c => c.TargetId == setting.TargetId);
                    if (match != null)
                    {
                        if (setting.NativeWidth == 0 && match.NativeWidth > 0)
                        {
                            setting.NativeWidth = match.NativeWidth;
                            setting.NativeHeight = match.NativeHeight;
                            changed = true;
                        }

                        if (!string.IsNullOrEmpty(match.FriendlyName))
                        {
                            setting.ReadableDeviceName = match.FriendlyName;
                            changed = true;
                        }
                    }
                    else
                        _logger.Info($"Migration: {setting.ReadableDeviceName} (TargetId {setting.TargetId}) not connected, skipping backfill");
                }

                profile.SchemaVersion = 1;
                changed = true;
            }

            // Add icon
            if (profile.SchemaVersion < 2)
            {
                profile.SchemaVersion = 2;
                changed = true;
            }

            // Backfill color profile
            if (profile.SchemaVersion < 3)
            {
                foreach (var setting in profile.DisplaySettings)
                {
                    if (string.IsNullOrEmpty(setting.ColorProfile))
                    {
                        var match = liveConfigs.FirstOrDefault(c => c.TargetId == setting.TargetId);
                        if (match != null)
                        {
                            try
                            {
                                setting.ColorProfile = ColorProfileHelper.GetDisplayDefaultColorProfile(
                                    match.AdapterId, match.SourceId);
                                if (setting.ColorProfile != null)
                                    changed = true;
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn(ex, $"Migration: failed to get color profile for {setting.ReadableDeviceName}");
                            }
                        }
                        else
                            _logger.Info($"Migration: {setting.ReadableDeviceName} (TargetId {setting.TargetId}) not connected, skipping color profile backfill");
                    }
                }

                profile.SchemaVersion = 3;
                changed = true;
            }

            // Migrate default profile and EDID identity
            if (profile.SchemaVersion < 4)
            {
                if (rawJson != null)
                {
                    try
                    {
                        if (JObject.Parse(rawJson)["isDefault"]?.Value<bool>() == true)
                        {
                            var existing = _settingsManager.GetDefaultProfileId();
                            if (!string.IsNullOrEmpty(existing) && existing != profile.Id)
                                _logger.Warn($"Migration: '{profile.Name}' also claims default -> keeping {existing}");
                            else
                                await _settingsManager.SetDefaultProfileIdAsync(profile.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Could not read isDefault flag");
                    }
                }

                foreach (var setting in profile.DisplaySettings)
                {
                    var match = liveConfigs.FirstOrDefault(c => (c.TargetId & 0xFFFF) == (setting.TargetId & 0xFFFF));
                    if (match == null)
                    {
                        _logger.Info($"Migration: {setting.ReadableDeviceName} (TargetId {setting.TargetId}) not connected, skipping identity backfill");
                        continue;
                    }

                    setting.ManufacturerName = match.ManufacturerName;
                    setting.ProductCodeID = match.ProductCodeID;
                    changed = true;
                }

                profile.SchemaVersion = 4;
                changed = true;
            }

            return changed;
        }

        public async Task<bool> SaveProfileAsync(Profile profile)
        {
            EnsureProfilesFolderExists();

            try
            {
                var filePath = GetProfileFilePath(profile.Id);
                var json = JsonConvert.SerializeObject(profile, Formatting.Indented);
                await Task.Run(() => FileHelper.AtomicWrite(filePath, json));

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving profile");
                return false;
            }
        }

        public async Task<Profile> ImportProfileAsync(string sourcePath)
        {
            EnsureProfilesFolderExists();

            try
            {
                var json = await Task.Run(() => File.ReadAllText(sourcePath));
                var profile = DeserializeProfile(json);

                if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || profile.DisplaySettings == null)
                {
                    _logger.Warn($"Invalid profile file: {sourcePath}");
                    return null;
                }

                if (GetProfile(profile.Id) != null)
                    profile.Id = Guid.NewGuid().ToString();

                profile.Name = GetUniqueProfileName(profile.Name);
                profile.UpdateLastModified();

                await AddProfileAsync(profile);
                return profile;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error importing profile");
                return null;
            }
        }

        public Profile DuplicateProfile(string profileId)
        {
            var sourceProfile = GetProfile(profileId);
            if (sourceProfile == null) return null;

            var duplicatedProfile = new Profile
            {
                Id = Guid.NewGuid().ToString(),
                Name = GetDuplicateProfileName(sourceProfile.Name),
                Description = sourceProfile.Description,
                Icon = sourceProfile.Icon,
                CreatedDate = DateTime.Now,
                LastModifiedDate = DateTime.Now,
                SchemaVersion = CurrentSchemaVersion,
                DisplaySettings = sourceProfile.DisplaySettings.Select(ds => new DisplaySetting
                {
                    // Identity
                    DeviceName = ds.DeviceName,
                    DeviceString = ds.DeviceString,
                    ReadableDeviceName = ds.ReadableDeviceName,
                    ManufacturerName = ds.ManufacturerName,
                    ProductCodeID = ds.ProductCodeID,
                    AdapterId = ds.AdapterId,
                    TargetId = ds.TargetId,
                    SourceId = ds.SourceId,
                    CloneGroupId = ds.CloneGroupId,
                    IsCloneSource = ds.IsCloneSource,
                    PathIndex = ds.PathIndex,
                    // State
                    IsEnabled = ds.IsEnabled,
                    IsPrimary = ds.IsPrimary,
                    // Layout
                    DisplayPositionX = ds.DisplayPositionX,
                    DisplayPositionY = ds.DisplayPositionY,
                    // Configuration
                    Width = ds.Width,
                    Height = ds.Height,
                    Frequency = ds.Frequency,
                    Rotation = ds.Rotation,
                    DpiScaling = ds.DpiScaling,
                    IsHdrSupported = ds.IsHdrSupported,
                    IsHdrEnabled = ds.IsHdrEnabled,
                    IsAcmEnabled = ds.IsAcmEnabled,
                    ColorProfile = ds.ColorProfile,
                    // Native
                    NativeWidth = ds.NativeWidth,
                    NativeHeight = ds.NativeHeight,
                    // Capabilities
                    AvailableResolutions = ds.AvailableResolutions != null ? ds.AvailableResolutions : new List<string>(),
                    AvailableRefreshRates = ds.AvailableRefreshRates != null ? new Dictionary<string, List<int>>(ds.AvailableRefreshRates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)) : new Dictionary<string, List<int>>(),
                    AvailableDpiScaling = ds.AvailableDpiScaling != null ? ds.AvailableDpiScaling : new List<uint>()
                }).ToList(),
                EnableAudio = sourceProfile.EnableAudio,
                AudioSettings = sourceProfile.AudioSettings != null ? new AudioSetting
                {
                    DefaultPlaybackDeviceId = sourceProfile.AudioSettings.DefaultPlaybackDeviceId,
                    PlaybackDeviceName = sourceProfile.AudioSettings.PlaybackDeviceName,
                    DefaultCaptureDeviceId = sourceProfile.AudioSettings.DefaultCaptureDeviceId,
                    CaptureDeviceName = sourceProfile.AudioSettings.CaptureDeviceName,
                    ApplyPlaybackDevice = sourceProfile.AudioSettings.ApplyPlaybackDevice,
                    ApplyCaptureDevice = sourceProfile.AudioSettings.ApplyCaptureDevice
                } : new AudioSetting(),
                EnableWallpaper = sourceProfile.EnableWallpaper,
                WallpaperSettings = sourceProfile.WallpaperSettings != null ? new WallpaperSettings
                {
                    Mode = sourceProfile.WallpaperSettings.Mode,
                    SolidColorArgb = sourceProfile.WallpaperSettings.SolidColorArgb,
                    Position = sourceProfile.WallpaperSettings.Position,
                    PerMonitor = new Dictionary<string, MonitorWallpaper>(
                        sourceProfile.WallpaperSettings.PerMonitor.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new MonitorWallpaper { Path = kvp.Value.Path, MonitorId = kvp.Value.MonitorId })),
                    SlideshowConfig = sourceProfile.WallpaperSettings.SlideshowConfig != null ? new SlideshowConfig
                    {
                        IntervalSeconds = sourceProfile.WallpaperSettings.SlideshowConfig.IntervalSeconds,
                        SourcePaths = (sourceProfile.WallpaperSettings.SlideshowConfig.SourcePaths),
                        Shuffle = sourceProfile.WallpaperSettings.SlideshowConfig.Shuffle
                    } : null
                } : null,
                EnableScripts = sourceProfile.EnableScripts,
                Scripts = sourceProfile.Scripts?
                    .Select(s => new Script
                    {
                        FileName = s.FileName,
                        Arguments = s.Arguments,
                        IsEnabled = s.IsEnabled
                    })
                    .ToList() ?? new List<Script>(),
                HotkeyConfig = new HotkeyConfig()
            };

            return duplicatedProfile;
        }

        public async Task<Profile> DuplicateProfileAsync(string profileId)
        {
            var duplicatedProfile = DuplicateProfile(profileId);
            if (duplicatedProfile == null) return null;

            if (await AddProfileAsync(duplicatedProfile))
            {
                return duplicatedProfile;
            }

            return null;
        }

        public async Task<Profile> CreateDefaultProfileAsync()
        {
            var defaultProfile = new Profile("Default", "Default system profile created automatically");
            try
            {
                var currentSettings = await GetCurrentDisplaySettingsAsync();
                defaultProfile.DisplaySettings.AddRange(currentSettings);

                AddProfile(defaultProfile);
                _currentProfileId = defaultProfile.Id;
                await SaveProfileAsync(defaultProfile);
                await _settingsManager.SetCurrentProfileIdAsync(defaultProfile.Id);
                await _settingsManager.SetDefaultProfileIdAsync(defaultProfile.Id);

                return defaultProfile;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating default profile");
                AddProfile(defaultProfile);
                return defaultProfile;
            }
        }

        #endregion

        #region Apply

        public async Task<List<DisplaySetting>> GetCurrentDisplaySettingsAsync()
        {
            return await Task.Run(() =>
            {
                var settings = new List<DisplaySetting>();

                try
                {
                    _logger.Debug("Getting current display settings...");

                    List<DisplayHelper.DisplayInfo> displays = DisplayHelper.GetDisplays();

                    List<DisplayConfigHelper.DisplayConfigInfo> displayConfigs = DisplayConfigHelper.GetDisplayConfigs();

                    if (displayConfigs.Count > 0)
                    {
                        for (int i = 0; i < displayConfigs.Count; i++)
                        {
                            var foundConfig = displayConfigs[i];
                            var foundDisplay = displays.Find(x => x.DeviceName == foundConfig.DeviceName);

                            string adpaterIdText = $"{foundConfig.AdapterId.HighPart:X8}{foundConfig.AdapterId.LowPart:X8}";
                            DpiHelper.DPIScalingInfo dpiInfo = DpiHelper.GetDPIScalingInfo(foundConfig.DeviceName, foundConfig);

                            DisplaySetting setting = new DisplaySetting();
                            // Identity
                            setting.DeviceName = foundConfig.DeviceName;
                            setting.DeviceString = foundDisplay?.DeviceString ?? foundConfig.DeviceName;
                            setting.ReadableDeviceName = !string.IsNullOrEmpty(foundConfig.FriendlyName) ? foundConfig.FriendlyName : foundConfig.DeviceName;
                            setting.ManufacturerName = foundConfig.ManufacturerName;
                            setting.ProductCodeID = foundConfig.ProductCodeID;
                            setting.AdapterId = adpaterIdText;
                            setting.TargetId = foundConfig.TargetId;
                            setting.SourceId = foundConfig.SourceId;
                            setting.PathIndex = foundConfig.PathIndex;
                            // State
                            setting.IsEnabled = foundConfig.IsEnabled;
                            setting.IsPrimary = foundDisplay?.IsPrimary ?? foundConfig.IsPrimary;
                            // Layout
                            setting.DisplayPositionX = foundConfig.DisplayPositionX;
                            setting.DisplayPositionY = foundConfig.DisplayPositionY;
                            // Configuration
                            setting.Width = foundConfig.Width;
                            setting.Height = foundConfig.Height;
                            setting.Frequency = foundDisplay?.Frequency ?? (int)foundConfig.RefreshRate;
                            setting.Rotation = (int)foundConfig.Rotation;
                            setting.DpiScaling = dpiInfo.Current;
                            setting.IsHdrSupported = foundConfig.IsHdrSupported;
                            setting.IsHdrEnabled = foundConfig.IsHdrEnabled;
                            setting.IsAcmEnabled = foundConfig.IsAcmEnabled;
                            setting.ColorProfile = ColorProfileHelper.GetDisplayDefaultColorProfile(foundConfig.AdapterId, foundConfig.SourceId);
                            // Native
                            setting.NativeWidth = foundConfig.NativeWidth;
                            setting.NativeHeight = foundConfig.NativeHeight;

                            // Capture display capabilities
                            try
                            {
                                var capabilities = DisplayHelper.GetDisplayCapabilities(setting.DeviceName);
                                setting.AvailableResolutions = capabilities.Resolutions;
                                setting.AvailableRefreshRates = capabilities.RefreshRates
                                    .Where(kv => kv.Value.Count > 0)
                                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                                setting.AvailableDpiScaling = DpiHelper.GetSupportedDpiScalingOnly(setting.DeviceName, foundConfig).ToList();

                                _logger.Debug($"Captured options for {setting.DeviceName}: " +
                                    $"{setting.AvailableResolutions.Count} resolutions, " +
                                    $"{setting.AvailableRefreshRates.Count} refresh-rate mappings, " +
                                    $"{setting.AvailableDpiScaling.Count} DPI values");
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, $"Error capturing available options for {setting.DeviceName}");
                            }

                            settings.Add(setting);
                        }

                        _logger.Info($"Created {settings.Count} display settings from {displayConfigs.Count} configs");

                        // Detect clone groups
                        var cloneGroups = settings.GroupBy(s => new { s.DeviceName, s.SourceId }).Where(g => g.Count() > 1).ToList();
                        if (cloneGroups.Any())
                        {
                            int cloneGroupIndex = 1;
                            foreach (var group in cloneGroups)
                            {
                                string cloneGroupId = $"clone-group-{cloneGroupIndex}";
                                foreach (var setting in group)
                                {
                                    setting.CloneGroupId = cloneGroupId;
                                    _logger.Info($"Detected clone group '{cloneGroupId}': " + $"{setting.ReadableDeviceName} (TargetId: {setting.TargetId})");
                                }
                                cloneGroupIndex++;
                            }
                            _logger.Info($"Detected {TextHelper.Plural(cloneGroups.Count, "clone group")} with {cloneGroups.Sum(g => g.Count())} total displays");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error getting current display settings");
                }

                return settings;
            });
        }

        public async Task<ProfileApplyResult> ApplyProfileAsync(Profile profile, ApplySource source = ApplySource.Unknown)
        {
            try
            {
                var totalWatch = Stopwatch.StartNew();
                var previousProfileId = _currentProfileId;
                _logger.Info($"Applying profile '{profile.Name}'...");

                // Capture Pre-Apply Snapshot
                List<DisplayConfigHelper.DisplayConfigInfo> preApplySnapshot = null;
                if (!_rollingBack && _settingsManager.ShouldRollbackAfterApplyFailure())
                    preApplySnapshot = DisplayConfigHelper.GetDisplayConfigs();

                // Map Display Configurations
                ProfileApplyResult result = new ProfileApplyResult { AudioSuccess = true, DpiChanged = true };
                var mapWatch = Stopwatch.StartNew();
                var displayConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>();
                List<DisplayConfigHelper.DisplayConfigInfo> liveDisplayConfigs = null;
                if (profile.DisplaySettings.Count > 0)
                {
                    liveDisplayConfigs = DisplayConfigHelper.GetDisplayConfigs();
                    foreach (var setting in profile.DisplaySettings)
                    {
                        var live = DisplayConfigHelper.ResolveLiveDisplay(setting, liveDisplayConfigs);

                        displayConfigs.Add(new DisplayConfigHelper.DisplayConfigInfo
                        {
                            // Identity
                            DeviceName = live?.DeviceName ?? setting.DeviceName,
                            FriendlyName = setting.ReadableDeviceName,
                            AdapterId = DisplayConfigHelper.GetLUIDFromString(setting.AdapterId),
                            SourceId = setting.SourceId,
                            TargetId = live?.TargetId ?? setting.TargetId,
                            PathIndex = setting.PathIndex,
                            // State
                            IsEnabled = setting.IsEnabled,
                            IsPrimary = setting.IsPrimary,
                            // Layout
                            DisplayPositionX = setting.DisplayPositionX,
                            DisplayPositionY = setting.DisplayPositionY,
                            // Configuration
                            Width = setting.Width,
                            Height = setting.Height,
                            RefreshRate = setting.Frequency,
                            Rotation = (DisplayConfigHelper.DisplayConfigRotation)setting.Rotation,
                            IsHdrSupported = setting.IsHdrSupported,
                            IsHdrEnabled = setting.IsHdrEnabled,
                            IsAcmEnabled = setting.IsAcmEnabled,
                            ColorProfile = setting.ColorProfile
                        });
                    }
                }
                mapWatch.Stop();

                // Apply Display Topology
                var topologyWatch = Stopwatch.StartNew();
                bool topologyApplied = DisplayConfigHelper.ApplyDisplayTopology(displayConfigs);
                if (ShouldForceApplyFailureAt(1))
                    topologyApplied = false;
                if (!topologyApplied)
                    _logger.Warn($"Topology apply failed for '{profile.Name}'");
                topologyWatch.Stop();

                // Apply Display Configuration
                var configWatch = Stopwatch.StartNew();
                result.DisplayConfigApplied = topologyApplied &&
                    await DisplayConfigHelper.ApplyDisplayConfig(displayConfigs);
                configWatch.Stop();

                if (topologyApplied && ShouldForceApplyFailureAt(2))
                    result.DisplayConfigApplied = false;

                // Handle Display-Stage Failure
                if (!result.DisplayConfigApplied && !_rollingBack && _settingsManager.ShouldAbortOnApplyFailure())
                {
                    _logger.Warn($"Aborting apply of '{profile.Name}' — display configuration failed");
                    result.Success = false;

                    if (_settingsManager.ShouldRollbackAfterApplyFailure())
                        await RollbackFailedApplyAsync(previousProfileId, preApplySnapshot, profile.Name);

                    return result;
                }

                // Apply DPI Settings
                var dpiWatch = Stopwatch.StartNew();
                bool allDpiChanged = true;
                var dpiLiveConfigs = DisplayConfigHelper.GetDisplayConfigs();
                var dpiTargets = profile.DisplaySettings
                    .Where(s => s.IsEnabled)
                    .Select(setting => new { setting, live = DisplayConfigHelper.ResolveLiveDisplay(setting, dpiLiveConfigs) })
                    .Where(x => x.live != null)
                    .GroupBy(x => x.live.DeviceName)
                    .Select(g => g.First())
                    .ToList();
                foreach (var target in dpiTargets)
                {
                    if (!DpiHelper.SetDPIScaling(target.live.DeviceName, target.setting.DpiScaling, target.live))
                    {
                        _logger.Warn($"Failed to set DPI scaling for {target.live.DeviceName}");
                        allDpiChanged = false;
                    }
                }
                result.DpiChanged = allDpiChanged;
                dpiWatch.Stop();

                // Apply Wallpaper Settings
                var wallpaperWatch = Stopwatch.StartNew();
                if (profile.EnableWallpaper && profile.WallpaperSettings != null)
                {
                    try
                    {
                        WallpaperHelper.Apply(profile.WallpaperSettings);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Wallpaper apply failed");
                    }
                }
                wallpaperWatch.Stop();

                // Apply Audio Settings
                var audioWatch = Stopwatch.StartNew();
                if (profile.EnableAudio && profile.AudioSettings != null)
                    result.AudioSuccess = AudioHelper.ApplyAudioSettings(profile.AudioSettings);
                audioWatch.Stop();

                // Finalize Result
                var finalizeWatch = new Stopwatch();
                var scriptWatch = new Stopwatch();

                result.Success = result.DisplayConfigApplied;

                // Execute Scripts
                scriptWatch.Start();
                if (profile.EnableScripts && profile.Scripts != null && profile.Scripts.Any())
                {
                    var enabledScripts = profile.Scripts.Count(s => s.IsEnabled);
                    var disabledNote = enabledScripts == profile.Scripts.Count
                        ? ""
                        : $" ({profile.Scripts.Count - enabledScripts} disabled)";

                    _logger.Info($"Executing {TextHelper.Plural(enabledScripts, "script")}{disabledNote}...");
                    foreach (var command in profile.Scripts)
                        _scriptManager.ExecuteScript(command);
                }
                else if (!profile.EnableScripts && profile.Scripts?.Any() == true)
                    _logger.Debug("Scripts disabled, skipping execution");
                scriptWatch.Stop();

                if (result.Success)
                {
                    // Log Result and Persist Success
                    var cloneGroupCount = profile.DisplaySettings
                        .Where(s => s.IsPartOfCloneGroup())
                        .GroupBy(s => s.CloneGroupId)
                        .Count();

                    var activeCount = profile.DisplaySettings.Count(d => d.IsEnabled);
                    var sb = new StringBuilder();
                    sb.Append($"Applied profile '{profile.Name}' -> ({TextHelper.Plural(activeCount, "active display")})");
                    if (cloneGroupCount > 0)
                        sb.Append($" | ({TextHelper.Plural(cloneGroupCount, "clone group")})");

                    _logger.Info(sb.ToString());

                    finalizeWatch.Start();
                    _currentProfileId = profile.Id;
                    await _settingsManager.SetCurrentProfileIdAsync(profile.Id);
                    finalizeWatch.Stop();

                    // Self-Heal Missing Hardware Info
                    var profilesToPersist = BackfillHardwareInfoAcrossProfiles(profile, liveDisplayConfigs);
                    foreach (var changedProfile in profilesToPersist)
                        await SaveProfileAsync(changedProfile);
                }

                totalWatch.Stop();

                if (result.Success)
                    ProfileApplied?.Invoke(this, new ProfileAppliedEventArgs(profile, source, totalWatch.ElapsedMilliseconds));

                // Timing Summary
                _logger.Info($"[PERF] Map: {mapWatch.ElapsedMilliseconds} ms | Topology: {topologyWatch.ElapsedMilliseconds} ms | Config: {configWatch.ElapsedMilliseconds} ms");
                _logger.Info($"[PERF] DPI: {dpiWatch.ElapsedMilliseconds} ms | Wallpaper: {wallpaperWatch.ElapsedMilliseconds} ms | Audio: {audioWatch.ElapsedMilliseconds} ms | Scripts: {scriptWatch.ElapsedMilliseconds} ms");
                _logger.Info($"[PERF] Finalize: {finalizeWatch.ElapsedMilliseconds} ms | TOTAL: {totalWatch.ElapsedMilliseconds} ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying profile");
                return new ProfileApplyResult { Success = false };
            }
        }

        private static bool HasIncompleteHardwareInfo(DisplaySetting setting)
        {
            if (setting == null)
            {
                return false;
            }

            return setting.NativeWidth == 0 || setting.NativeHeight == 0 || string.IsNullOrEmpty(setting.ManufacturerName) || string.IsNullOrEmpty(setting.ProductCodeID);
        }

        private static bool BackfillHardwareInfoFromLive(DisplaySetting setting, DisplayConfigHelper.DisplayConfigInfo live)
        {
            if (setting == null || live == null)
            {
                return false;
            }

            bool changed = false;

            if (setting.NativeWidth == 0 && live.NativeWidth > 0)
            {
                setting.NativeWidth = live.NativeWidth;
                changed = true;
            }

            if (setting.NativeHeight == 0 && live.NativeHeight > 0)
            {
                setting.NativeHeight = live.NativeHeight;
                changed = true;
            }

            if (string.IsNullOrEmpty(setting.ManufacturerName) && !string.IsNullOrEmpty(live.ManufacturerName))
            {
                setting.ManufacturerName = live.ManufacturerName;
                changed = true;
            }

            if (string.IsNullOrEmpty(setting.ProductCodeID) && !string.IsNullOrEmpty(live.ProductCodeID))
            {
                setting.ProductCodeID = live.ProductCodeID;
                changed = true;
            }

            return changed;
        }

        private List<Profile> BackfillHardwareInfoAcrossProfiles(Profile appliedProfile, List<DisplayConfigHelper.DisplayConfigInfo> liveConfigs)
        {
            var changedProfiles = new List<Profile>();

            if (appliedProfile?.DisplaySettings == null || liveConfigs == null || liveConfigs.Count == 0)
            {
                return changedProfiles;
            }

            if (!appliedProfile.DisplaySettings.Any(HasIncompleteHardwareInfo))
            {
                return changedProfiles;
            }
            
            var liveByTargetId = liveConfigs.GroupBy(c => c.TargetId).ToDictionary(g => g.Key, g => g.First());
            var repairedTargetIds = new HashSet<uint>();
            bool appliedChanged = false;

            foreach (var setting in appliedProfile.DisplaySettings)
            {
                if (!HasIncompleteHardwareInfo(setting)) continue;
                if (!liveByTargetId.TryGetValue(setting.TargetId, out var live)) continue;

                if (BackfillHardwareInfoFromLive(setting, live))
                {
                    appliedChanged = true;
                    repairedTargetIds.Add(setting.TargetId);
                }
            }

            if (appliedChanged)
                changedProfiles.Add(appliedProfile);

            if (repairedTargetIds.Count == 0)
            {
                return changedProfiles;
            }

            foreach (var other in _profiles.Where(p => p.Id != appliedProfile.Id))
            {
                bool otherChanged = false;

                foreach (var setting in other.DisplaySettings)
                {
                    if (!repairedTargetIds.Contains(setting.TargetId)) continue;
                    if (!HasIncompleteHardwareInfo(setting)) continue;
                    if (!liveByTargetId.TryGetValue(setting.TargetId, out var live)) continue;

                    if (BackfillHardwareInfoFromLive(setting, live))
                        otherChanged = true;
                }

                if (otherChanged)
                    changedProfiles.Add(other);
            }

            return changedProfiles;
        }

        private bool ShouldForceApplyFailureAt(int stage)
        {
            if (_rollingBack || _settingsManager.Debug.ForceApplyFailure != stage)
            {
                return false;
            }

            _logger.Warn($"[debugFlag: forceApplyFailure] Treating stage {stage} as failed");
            return true;
        }

        public static RollbackTarget SelectRollbackTarget(bool rollbackAfterApplyFailure, bool rollbackToPreviousProfile, bool hasPreviousProfile)
        {
            if (!rollbackAfterApplyFailure) return RollbackTarget.None;

            return rollbackToPreviousProfile && hasPreviousProfile ? RollbackTarget.PreviousProfile : RollbackTarget.Snapshot;
        }

        private async Task RollbackFailedApplyAsync(string previousProfileId, List<DisplayConfigHelper.DisplayConfigInfo> preApplySnapshot, string failedProfileName)
        {
            if (_rollingBack)
            {
                _logger.Error("Rollback skipped while rollback already in progress");
                return;
            }

            var hasPreviousProfile = !string.IsNullOrEmpty(previousProfileId) && GetProfile(previousProfileId) != null;
            var rollbackTarget = SelectRollbackTarget(_settingsManager.ShouldRollbackAfterApplyFailure(), _settingsManager.ShouldRollbackToPreviousProfile(), hasPreviousProfile);
            var previous = rollbackTarget == RollbackTarget.PreviousProfile ? GetProfile(previousProfileId) : null;

            try
            {
                _rollingBack = true;

                if (previous != null)
                {
                    _logger.Info($"Rolling back to '{previous.Name}' after '{failedProfileName}' apply failed...");

                    var rollbackResult = await ApplyProfileAsync(previous, ApplySource.Unknown);
                    if (!rollbackResult.Success)
                        _logger.Error($"Rollback to '{previous.Name}' failed; leaving current desktop state as is");

                    return;
                }

                _logger.Info($"Previous profile unavailable after '{failedProfileName}' failed; rolling back to pre-apply display snapshot");
                await RollbackToSnapshotAsync(preApplySnapshot, failedProfileName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Rollback after '{failedProfileName}' threw; leaving current desktop state as is");
            }
            finally
            {
                _rollingBack = false;
            }
        }

        private async Task RollbackToSnapshotAsync(List<DisplayConfigHelper.DisplayConfigInfo> snapshot, string failedProfileName)
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                _logger.Warn($"Cannot roll back after '{failedProfileName}' apply failed — no pre-apply snapshot was captured");
                return;
            }

            _logger.Info($"Rolling back display state of ({TextHelper.Plural(snapshot.Count, "display")}) after '{failedProfileName}' apply failed");

            if (!DisplayConfigHelper.ApplyDisplayTopology(snapshot))
            {
                _logger.Error("Rollback failed at topology -> desktop may be in mixed state");
                return;
            }

            if (!await DisplayConfigHelper.ApplyDisplayConfig(snapshot))
            {
                _logger.Error("Rollback failed at layout -> desktop may be in mixed state");
                return;
            }

            _currentProfileId = null;
            await _settingsManager.SetCurrentProfileIdAsync(string.Empty);
            _logger.Info("Display state rolled back -> no profile is marked active");
        }

        public string GetApplyResultErrorMessage(string profileName, ProfileApplyResult result)
        {
            string errorDetails =
                $"Failed to apply profile '{profileName}'.\n" +
                $"Some settings may not have been applied correctly.\n\n" +
                $"Display: {result.DisplayConfigApplied},\n" +
                $"DPI: {result.DpiChanged},\n" +
                $"Audio: {result.AudioSuccess}";

            return errorDetails;
        }

        #endregion

        #region Query

        public Profile GetProfile(string profileId) => _profiles.FirstOrDefault(p => p.Id == profileId);

        public Profile GetProfileByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            string cleanName = name.Trim();
            return _profiles.FirstOrDefault(p => p.Name.Trim().Equals(cleanName, StringComparison.OrdinalIgnoreCase));
        }

        public Profile GetCurrentProfile()
        {
            if (string.IsNullOrEmpty(_currentProfileId)) return null;

            return GetProfile(_currentProfileId);
        }

        public List<Profile> GetAllProfiles() => _profiles.ToList();

        public Profile GetDefaultProfile()
        {
            var id = _settingsManager.GetDefaultProfileId();
            return string.IsNullOrEmpty(id) ? null : _profiles.FirstOrDefault(p => p.Id == id);
        }

        #endregion

        #region CRUD

        public void AddProfile(Profile profile)
        {
            _profiles.Add(profile);
            ProfileAdded?.Invoke(this, profile);
        }

        public async Task<bool> AddProfileAsync(Profile profile)
        {
            AddProfile(profile);
            return await SaveProfileAsync(profile);
        }

        public void UpdateProfile(Profile profile)
        {
            var existingProfile = GetProfile(profile.Id);
            if (existingProfile != null)
            {
                var index = _profiles.IndexOf(existingProfile);
                profile.UpdateLastModified();
                _profiles[index] = profile;
                ProfileUpdated?.Invoke(this, profile);
            }
        }

        public async Task<bool> UpdateProfileAsync(Profile profile)
        {
            UpdateProfile(profile);
            return await SaveProfileAsync(profile);
        }

        public void DeleteProfile(string profileId)
        {
            _profiles.RemoveAll(p => p.Id == profileId);
            ProfileDeleted?.Invoke(this, profileId);
        }

        public async Task<bool> DeleteProfileAsync(string profileId)
        {
            try
            {
                DeleteProfile(profileId);
                var filePath = GetProfileFilePath(profileId);
                if (File.Exists(filePath))
                {
                    await Task.Run(() => File.Delete(filePath));
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting profile");
                return false;
            }
        }

        public async Task<bool> SetDefaultProfileAsync(string profileId) => await _settingsManager.SetDefaultProfileIdAsync(profileId);

        #endregion

        #region Checks

        public bool HasProfile(string name) => _profiles.Exists(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public const int MaxProfileNameLength = 60;
        private const string CopySuffix = " - Copy";

        public string GetDuplicateProfileName(string baseName) => GetUniqueProfileName(Compose(baseName, CopySuffix));

        public string GetUniqueProfileName(string baseName)
        {
            if (!HasProfile(baseName))
            {
                return baseName;
            }

            int counter = 1;
            string uniqueName;
            do
            {
                uniqueName = Compose(baseName, $" ({counter})");
                counter++;
            } while (HasProfile(uniqueName));

            return uniqueName;
        }

        private static string Compose(string stem, string marker)
        {
            var composed = stem + marker;
            if (composed.Length <= MaxProfileNameLength) return composed;

            var existing = MarkerChain(stem);
            var root = stem.Substring(0, stem.Length - existing.Length);
            var tail = existing + marker;

            var room = MaxProfileNameLength - tail.Length - 1;
            if (room < 1) return composed.Substring(0, MaxProfileNameLength);

            return root.Substring(0, Math.Min(room, root.Length)).TrimEnd() + "\u2026" + tail;
        }

        private static readonly Regex MarkerPattern = new Regex(@"(( - Copy)|( \(\d+\)))+$", RegexOptions.Compiled);

        private static string MarkerChain(string name)
        {
            var m = MarkerPattern.Match(name);
            return m.Success ? m.Value : string.Empty;
        }

        public int GetProfileCount() => _profiles.Count;

        public string GetAppDataFolder() => _appDataFolder;

        #endregion

        #region Hotkeys

        public List<Profile> GetProfilesWithHotkeys() => _profiles.Where(p => p.HotkeyConfig != null && p.HotkeyConfig.Key != System.Windows.Input.Key.None).ToList();

        public List<Profile> GetProfilesWithActiveHotkeys() => _profiles.Where(p => p.HotkeyConfig != null && p.HotkeyConfig.IsEnabled && p.HotkeyConfig.Key != System.Windows.Input.Key.None).ToList();

        public Dictionary<string, HotkeyConfig> GetAllHotkeys()
        {
            var hotkeys = new Dictionary<string, HotkeyConfig>();

            foreach (var profile in _profiles.Where(p => p.HotkeyConfig != null && p.HotkeyConfig.IsEnabled && p.HotkeyConfig.Key != System.Windows.Input.Key.None))
                hotkeys[profile.Id] = profile.HotkeyConfig;

            return hotkeys;
        }

        #endregion
    }
}