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
        private readonly ProfileApplyAuthority _applyAuthority = new ProfileApplyAuthority();

        private static ProfileManager _instance;
        private readonly ScriptManager _scriptManager = ScriptManager.Instance;
        private readonly SettingsManager _settingsManager = SettingsManager.Instance;

        private List<Profile> _profiles;
        private string _currentProfileId;

        private readonly string _appDataFolder;
        private readonly string _profilesFolderPath;
        private readonly Func<List<DisplayConfigHelper.DisplayConfigInfo>> _getMigrationDisplayConfigs;
        private readonly Func<List<DisplayConfigHelper.DisplayConfigInfo>> _getMigrationDisplayAddresses;
        private readonly ProfileApplyRuntime _applyRuntime;

        internal const int CurrentSchemaVersion = 6;

        public enum ApplySource { Unknown, Window, Tray, Hotkey, CommandLine, Startup }

        public enum RollbackTarget { None, PreviousProfile, Snapshot }

        public class ProfileAppliedEventArgs : EventArgs
        {
            public Profile Profile { get; }
            public ApplySource Source { get; }
            public long DurationMilliseconds { get; }
            public string WarningSummary { get; }

            public ProfileAppliedEventArgs(Profile profile, ApplySource source, long durationMilliseconds, string warningSummary = null)
            {
                Profile = profile;
                Source = source;
                DurationMilliseconds = durationMilliseconds;
                WarningSummary = warningSummary ?? string.Empty;
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
            public bool WallpaperSuccess { get; set; } = true;
            public bool AdvancedColorSuccess { get; set; } = true;
            public bool ColorProfileSuccess { get; set; } = true;
            public bool CurrentProfilePersisted { get; set; } = true;
        }

        internal static string GetApplyWarningSummary(ProfileApplyResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            var failures = new List<string>();
            if (!result.AdvancedColorSuccess) failures.Add("Advanced Color");
            if (!result.ColorProfileSuccess) failures.Add("Color Profile");
            if (!result.DpiChanged) failures.Add("DPI");
            if (!result.WallpaperSuccess) failures.Add("Wallpaper");
            if (!result.AudioSuccess) failures.Add("Audio");
            if (!result.CurrentProfilePersisted) failures.Add("Current Profile Marker");

            return string.Join(", ", failures);
        }

        internal static string AppendApplyWarnings(string message, string warningSummary) =>
            string.IsNullOrEmpty(warningSummary) ? message : $"{message} — {warningSummary} failed to apply";


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
            : this(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayProfileManager"),
                DisplayConfigHelper.GetDisplayConfigs,
                null,
                DisplayConfigHelper.GetAllPathDisplayAddresses)
        {
        }

        internal ProfileManager(
            string appDataFolder,
            Func<List<DisplayConfigHelper.DisplayConfigInfo>> getMigrationDisplayConfigs,
            ProfileApplyRuntime applyRuntime = null,
            Func<List<DisplayConfigHelper.DisplayConfigInfo>> getMigrationDisplayAddresses = null)
        {
            if (string.IsNullOrWhiteSpace(appDataFolder)) throw new ArgumentException("Profile storage folder is required.", nameof(appDataFolder));

            _getMigrationDisplayConfigs = getMigrationDisplayConfigs ?? throw new ArgumentNullException(nameof(getMigrationDisplayConfigs));
            _getMigrationDisplayAddresses = getMigrationDisplayAddresses ?? _getMigrationDisplayConfigs;
            _applyRuntime = applyRuntime ?? ProfileApplyRuntime.CreateDefault(_settingsManager);
            _appDataFolder = appDataFolder;
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
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var root = JObject.Parse(json);

            RecoverOptionalProperty(root, "schemaVersion", typeof(int));
            ReconcileProfileSchema(root);

            RecoverScriptEntries(root);
            RecoverOptionalProperty(root, "audioSettings", typeof(AudioSetting));
            RecoverOptionalProperty(root, "wallpaperSettings", typeof(WallpaperSettings));
            RecoverOptionalProperty(root, "scriptSettings", typeof(ScriptSettings));
            RecoverOptionalProperty(root, "hotkeyConfig", typeof(HotkeyConfig));
            RecoverOptionalProperty(root, "enableWallpaper", typeof(bool));
            RecoverOptionalProperty(root, "enableAudio", typeof(bool));
            RecoverOptionalProperty(root, "enableScripts", typeof(bool));
            RecoverOptionalProperty(root, "name", typeof(string));
            RecoverOptionalProperty(root, "description", typeof(string));
            RecoverOptionalProperty(root, "icon", typeof(string));
            RecoverOptionalProperty(root, "createdDate", typeof(DateTime));
            RecoverOptionalProperty(root, "lastModifiedDate", typeof(DateTime));

            NormalizeAdvancedColorSchema(root);
            NormalizeSubsystemSettings(root);
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
            RecoverScriptEntries(root, "scripts");

            if (root["scriptSettings"] is JObject scriptSettings)
                RecoverScriptEntries(scriptSettings, "scripts");
        }

        private static void RecoverScriptEntries(JObject owner, string propertyName)
        {
            var token = owner[propertyName];
            if (token == null) return;

            if (!(token is JArray scripts))
            {
                owner.Remove(propertyName);
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

        internal static int ReconcileProfileSchema(JObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            int declaredSchema = root["schemaVersion"]?.Value<int>() ?? 0;
            if (declaredSchema > CurrentSchemaVersion) throw new InvalidDataException($"Profile schema {declaredSchema} is newer than supported schema {CurrentSchemaVersion}.");

            int effectiveSchema = declaredSchema;

            if (root["isDefault"] != null)
                effectiveSchema = Math.Min(effectiveSchema, 3);

            if (root["enableWallpaper"] != null ||
                root["enableAudio"] != null ||
                root["enableScripts"] != null ||
                root["scripts"] != null)
            {
                effectiveSchema = Math.Min(effectiveSchema, 4);
            }

            if (root["displaySettings"] is JArray displaySettings && displaySettings.OfType<JObject>().Any(setting => setting["isAcmEnabled"] != null))
                effectiveSchema = Math.Min(effectiveSchema, 5);

            if (effectiveSchema != declaredSchema)
                root["schemaVersion"] = effectiveSchema;

            return effectiveSchema;
        }

        internal static bool NeedsProfileMigration(Profile profile) => profile != null && profile.SchemaVersion < CurrentSchemaVersion;

        internal static bool CanPersistProfileSchema(Profile profile) => profile != null && profile.SchemaVersion == CurrentSchemaVersion;

        private static void NormalizeAdvancedColorSchema(JObject root)
        {
            int schemaVersion = root["schemaVersion"]?.Value<int>() ?? 0;
            if (schemaVersion >= 6 || !(root["displaySettings"] is JArray displaySettings)) return;

            foreach (var displaySetting in displaySettings.OfType<JObject>())
            {
                bool hdrEnabled = displaySetting["isHdrEnabled"]?.Value<bool>() ?? false;
                bool legacyWcgEnabled = displaySetting["isAcmEnabled"]?.Value<bool>() ?? false;

                displaySetting["isWcgEnabled"] = hdrEnabled ? false : legacyWcgEnabled;
                displaySetting.Remove("isAcmEnabled");
            }
        }

        private static void NormalizeSubsystemSettings(JObject root)
        {
            var wallpaperSettings = root["wallpaperSettings"] as JObject;
            if (wallpaperSettings != null && wallpaperSettings["enabled"] == null)
                wallpaperSettings["enabled"] = root["enableWallpaper"]?.Value<bool>() ?? false;

            var audioSettings = root["audioSettings"] as JObject;
            if (audioSettings != null && audioSettings["enabled"] == null)
                audioSettings["enabled"] = root["enableAudio"]?.Value<bool>() ?? false;

            var scriptSettings = root["scriptSettings"] as JObject;
            if (scriptSettings == null)
            {
                scriptSettings = new JObject();
                root["scriptSettings"] = scriptSettings;
            }

            if (scriptSettings["enabled"] == null)
                scriptSettings["enabled"] = root["enableScripts"]?.Value<bool>() ?? false;

            if (scriptSettings["scripts"] == null && root["scripts"] is JArray legacyScripts)
                scriptSettings["scripts"] = legacyScripts.DeepClone();

            root.Remove("enableWallpaper");
            root.Remove("enableAudio");
            root.Remove("enableScripts");
            root.Remove("scripts");
        }

        public async Task<bool> LoadProfilesAsync()
        {
            EnsureProfilesFolderExists();
            var previousProfiles = _profiles;

            try
            {
                var loadedProfiles = new List<Profile>();
                var acceptedProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var profileFiles = Directory.GetFiles(_profilesFolderPath, "*.dpm")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                List<DisplayConfigHelper.DisplayConfigInfo> liveConfigs = null;
                List<DisplayConfigHelper.DisplayConfigInfo> addressConfigs = null;
                foreach (var file in profileFiles)
                {
                    try
                    {
                        var json = await Task.Run(() => File.ReadAllText(file));
                        var profile = DeserializeProfile(json);

                        if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || profile.DisplaySettings == null)
                        {
                            _logger.Warn($"Skipping profile load -> invalid file: {Path.GetFileName(file)}");
                            continue;
                        }

                        if (!TryValidateManagedProfileIdentity(file, profile, acceptedProfileIds, out string identityFailure))
                        {
                            _logger.Warn($"Skipping profile load -> managed file '{file}' cannot be modified: {identityFailure}");
                            continue;
                        }

                        if (NeedsProfileMigration(profile))
                        {
                            if (liveConfigs == null)
                                liveConfigs = _getMigrationDisplayConfigs();
                            if (addressConfigs == null)
                                addressConfigs = _getMigrationDisplayAddresses();

                            bool migrated = await MigrateProfileAsync(profile, liveConfigs, addressConfigs, json);
                            if (migrated)
                            {
                                var savedDate = profile.LastModifiedDate;
                                await SaveProfileAsync(profile);
                                profile.LastModifiedDate = savedDate;
                                _logger.Info($"Migrated profile '{profile.Name}' to schema {CurrentSchemaVersion}");
                            }
                        }

                        loadedProfiles.Add(profile);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Error loading profile from {file}");
                    }
                }

                if (HasTotalProfileLoadFailure(profileFiles.Length, loadedProfiles.Count))
                {
                    _logger.Error($"Failed to load any of {profileFiles.Length} existing profile files -> keeping the previous in-memory profiles");
                    return false;
                }

                _profiles = loadedProfiles;
                if (_profiles.Count == 0 && await CreateDefaultProfileAsync() == null)
                {
                    _profiles = previousProfiles;
                    return false;
                }

                _currentProfileId = _settingsManager.GetCurrentProfileId();
                ProfilesLoaded?.Invoke(this, EventArgs.Empty);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading profiles");
                _profiles = previousProfiles;
                return false;
            }
        }

        internal static bool HasTotalProfileLoadFailure(int discoveredProfileFileCount, int loadedProfileCount) => discoveredProfileFileCount > 0 && loadedProfileCount == 0;

        private async Task<bool> MigrateProfileAsync(
            Profile profile,
            List<DisplayConfigHelper.DisplayConfigInfo> liveConfigs,
            List<DisplayConfigHelper.DisplayConfigInfo> addressConfigs,
            string rawJson = null)
        {
            bool changed = false;

            // Backfill display name
            if (profile.SchemaVersion < 1)
            {
                foreach (var setting in profile.DisplaySettings)
                {
                    var match = ResolveHardwareBackfillDisplay(setting, liveConfigs);
                    if (match != null)
                    {
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
                        var match = ResolveHardwareBackfillDisplay(setting, liveConfigs);
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
                    var match = ResolveHardwareBackfillDisplay(setting, liveConfigs);
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

            // Consolidate subsystem enablement into the settings objects
            if (profile.SchemaVersion < 5)
            {
                profile.SchemaVersion = 5;
                changed = true;
            }
            if (profile.SchemaVersion < 6)
            {
                var currentAddresses = addressConfigs ?? new List<DisplayConfigHelper.DisplayConfigInfo>();
                foreach (var setting in profile.DisplaySettings ?? new List<DisplaySetting>())
                {
                    if (setting.IsHdrEnabled)
                        setting.IsWcgEnabled = false;

                    setting.NativeWidth = 0;
                    setting.NativeHeight = 0;

                    var match = ResolveHardwareBackfillDisplay(setting, currentAddresses);
                    if (match?.NativeWidth > 0 && match.NativeHeight > 0)
                    {
                        setting.NativeWidth = match.NativeWidth;
                        setting.NativeHeight = match.NativeHeight;
                    }
                }

                profile.WallpaperSettings = new WallpaperSettings();
                profile.SchemaVersion = 6;
                changed = true;
            }

            return changed;
        }

        public async Task<bool> SaveProfileAsync(Profile profile)
        {
            EnsureProfilesFolderExists();

            try
            {
                if (!CanPersistProfileSchema(profile))
                {
                    string schema = profile == null ? "<null>" : profile.SchemaVersion.ToString();
                    _logger.Warn($"Refusing to save profile schema {schema}; expected {CurrentSchemaVersion}");
                    return false;
                }

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

                if (NeedsProfileMigration(profile))
                {
                    await MigrateProfileAsync(
                        profile,
                        _getMigrationDisplayConfigs(),
                        _getMigrationDisplayAddresses(),
                        json);
                }

                if (!TryNormalizeProfileId(profile.Id, out string normalizedProfileId) || GetProfile(normalizedProfileId) != null)
                    profile.Id = Guid.NewGuid().ToString();
                else
                    profile.Id = normalizedProfileId;

                profile.Name = GetUniqueProfileName(profile.Name);
                profile.UpdateLastModified();

                if (!await AddProfileAsync(profile))
                {
                    return null;
                }

                return profile;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error importing profile");
                return null;
            }
        }

        internal static bool TryNormalizeProfileId(string profileId, out string normalizedProfileId)
        {
            if (Guid.TryParse(profileId, out Guid parsedProfileId))
            {
                normalizedProfileId = parsedProfileId.ToString("D");
                return true;
            }

            normalizedProfileId = null;
            return false;
        }

        internal static bool TryValidateManagedProfileIdentity(string filePath, Profile profile, ISet<string> acceptedProfileIds, out string failureReason)
        {
            failureReason = string.Empty;
            if (profile == null || acceptedProfileIds == null)
            {
                failureReason = "profile identity context is missing";
                return false;
            }

            if (!TryNormalizeProfileId(profile.Id, out string normalizedProfileId))
            {
                failureReason = $"profile id '{profile.Id}' is not a GUID";
                return false;
            }

            string fileStem = Path.GetFileNameWithoutExtension(filePath);
            if (!Guid.TryParse(fileStem, out Guid fileGuid))
            {
                failureReason = $"filename stem '{fileStem}' is not a GUID";
                return false;
            }

            string normalizedFileId = fileGuid.ToString("D");
            if (!string.Equals(normalizedProfileId, normalizedFileId, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = $"profile id '{normalizedProfileId}' does not match filename id '{normalizedFileId}'";
                return false;
            }

            if (!string.Equals(fileStem, normalizedFileId, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = $"filename stem '{fileStem}' is not the canonical GUID path";
                return false;
            }

            if (!acceptedProfileIds.Add(normalizedProfileId))
            {
                failureReason = $"duplicate profile id '{normalizedProfileId}'";
                return false;
            }

            profile.Id = normalizedProfileId;
            return true;
        }

        public Profile DuplicateProfile(string profileId)
        {
            var sourceProfile = GetProfile(profileId);
            if (sourceProfile == null)
            {
                return null;
            }

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
                    TargetId = ds.TargetId,
                    CloneGroupId = ds.CloneGroupId,
                    IsCloneSource = ds.IsCloneSource,
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
                    IsWcgSupported = ds.IsWcgSupported,
                    IsHdrEnabled = ds.IsHdrEnabled,
                    IsWcgEnabled = ds.IsWcgEnabled,
                    ColorProfile = ds.ColorProfile,
                    // Native
                    NativeWidth = ds.NativeWidth,
                    NativeHeight = ds.NativeHeight,
                    // Capabilities
                    AvailableResolutions = ds.AvailableResolutions != null ? ds.AvailableResolutions : new List<string>(),
                    AvailableRefreshRates = ds.AvailableRefreshRates != null ? new Dictionary<string, List<int>>(ds.AvailableRefreshRates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)) : new Dictionary<string, List<int>>(),
                    AvailableDpiScaling = ds.AvailableDpiScaling != null ? ds.AvailableDpiScaling : new List<uint>()
                }).ToList(),
                AudioSettings = sourceProfile.AudioSettings != null ? new AudioSetting
                {
                    Enabled = sourceProfile.AudioSettings.Enabled,
                    DefaultPlaybackDeviceId = sourceProfile.AudioSettings.DefaultPlaybackDeviceId,
                    PlaybackDeviceName = sourceProfile.AudioSettings.PlaybackDeviceName,
                    DefaultCaptureDeviceId = sourceProfile.AudioSettings.DefaultCaptureDeviceId,
                    CaptureDeviceName = sourceProfile.AudioSettings.CaptureDeviceName,
                    ApplyPlaybackDevice = sourceProfile.AudioSettings.ApplyPlaybackDevice,
                    ApplyCaptureDevice = sourceProfile.AudioSettings.ApplyCaptureDevice
                } : new AudioSetting(),
                WallpaperSettings = sourceProfile.WallpaperSettings != null ? new WallpaperSettings
                {
                    Enabled = sourceProfile.WallpaperSettings.Enabled,
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
                ScriptSettings = sourceProfile.ScriptSettings != null ? new ScriptSettings
                {
                    Enabled = sourceProfile.ScriptSettings.Enabled,
                    Scripts = sourceProfile.ScriptSettings.Scripts?
                        .Select(s => new Script
                        {
                            FileName = s.FileName,
                            Arguments = s.Arguments,
                            IsEnabled = s.IsEnabled
                        })
                        .ToList() ?? new List<Script>()
                } : new ScriptSettings(),
                HotkeyConfig = new HotkeyConfig()
            };

            return duplicatedProfile;
        }

        public async Task<Profile> DuplicateProfileAsync(string profileId)
        {
            var duplicatedProfile = DuplicateProfile(profileId);
            if (duplicatedProfile == null)
            {
                return null;
            }

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

                if (!await AddProfileAsync(defaultProfile))
                {
                    _logger.Error("Failed to persist default profile");
                    return null;
                }

                _currentProfileId = defaultProfile.Id;
                await _settingsManager.SetCurrentProfileIdAsync(defaultProfile.Id);
                await _settingsManager.SetDefaultProfileIdAsync(defaultProfile.Id);

                return defaultProfile;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating default profile");
                return null;
            }
        }

        #endregion

        #region Apply

        internal sealed class AdvancedColorCaptureUnavailableException : InvalidOperationException
        {
            public AdvancedColorCaptureUnavailableException(string message) : base(message) { }
        }

        internal static List<DisplayConfigHelper.DisplayConfigInfo> CaptureDisplayConfigsForProfile(
            Func<List<DisplayConfigHelper.DisplayConfigInfo>> getDisplayConfigs,
            bool requireKnownAdvancedColor,
            int maxAttempts = 3,
            Action<int> retryDelay = null)
        {
            if (getDisplayConfigs == null) throw new ArgumentNullException(nameof(getDisplayConfigs));
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

            int attempts = requireKnownAdvancedColor ? maxAttempts : 1;
            List<DisplayConfigHelper.DisplayConfigInfo> configs = null;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                configs = getDisplayConfigs() ?? new List<DisplayConfigHelper.DisplayConfigInfo>();
                if (!requireKnownAdvancedColor || configs.All(config => config == null || config.IsAdvancedColorInfoAvailable))
                {
                    return configs;
                }

                if (attempt < attempts)
                    retryDelay?.Invoke(attempt);
            }

            var unavailableDisplays = configs
                .Where(config => config != null && !config.IsAdvancedColorInfoAvailable)
                .Select(config => !string.IsNullOrWhiteSpace(config.FriendlyName) ? config.FriendlyName : config.DeviceName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            string suffix = unavailableDisplays.Count > 0 ? $" for {string.Join(", ", unavailableDisplays)}" : string.Empty;
            throw new AdvancedColorCaptureUnavailableException($"Advanced Color state is temporarily unavailable{suffix}; current display capture was deferred.");
        }

        internal static void ProjectAdvancedColorForCapture(
            DisplaySetting setting,
            DisplayConfigHelper.DisplayConfigInfo live,
            bool requireKnownAdvancedColor)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));
            if (live == null) throw new ArgumentNullException(nameof(live));

            if (requireKnownAdvancedColor && !live.IsAdvancedColorInfoAvailable) throw new AdvancedColorCaptureUnavailableException("Advanced Color state became unavailable while current display settings were being captured.");

            setting.IsHdrSupported = live.IsHdrSupported;
            setting.IsWcgSupported = live.IsWcgSupported;
            setting.IsHdrEnabled = live.IsHdrEnabled;
            setting.IsWcgEnabled = live.IsWcgEnabled;
        }

        public async Task<List<DisplaySetting>> GetCurrentDisplaySettingsAsync()
        {
            return await Task.Run(() =>
            {
                var settings = new List<DisplaySetting>();

                try
                {
                    _logger.Debug("Reading current display settings...");

                    List<DisplayHelper.DisplayInfo> displays = DisplayHelper.GetDisplays();

                    bool requireKnownAdvancedColor = DisplayConfigHelper.IsWindows24H2OrGreater();
                    List<DisplayConfigHelper.DisplayConfigInfo> displayConfigs = CaptureDisplayConfigsForProfile(
                        DisplayConfigHelper.GetDisplayConfigs,
                        requireKnownAdvancedColor,
                        maxAttempts: 3,
                        retryDelay: attempt => System.Threading.Thread.Sleep(100 * attempt));

                    if (displayConfigs.Count > 0)
                    {
                        for (int i = 0; i < displayConfigs.Count; i++)
                        {
                            var foundConfig = displayConfigs[i];
                            var foundDisplay = displays.Find(x => x.DeviceName == foundConfig.DeviceName);

                            DpiHelper.DPIScalingInfo dpiInfo = DpiHelper.GetDPIScalingInfo(foundConfig.DeviceName, foundConfig);

                            DisplaySetting setting = new DisplaySetting();
                            // Identity
                            setting.DeviceName = foundConfig.DeviceName;
                            setting.DeviceString = foundDisplay?.DeviceString ?? foundConfig.DeviceName;
                            setting.ReadableDeviceName = !string.IsNullOrEmpty(foundConfig.FriendlyName) ? foundConfig.FriendlyName : foundConfig.DeviceName;
                            setting.ManufacturerName = foundConfig.ManufacturerName;
                            setting.ProductCodeID = foundConfig.ProductCodeID;
                            setting.AdapterLuid = foundConfig.AdapterId;
                            setting.TargetId = foundConfig.TargetId;
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
                            ProjectAdvancedColorForCapture(setting, foundConfig, requireKnownAdvancedColor);
                            setting.ColorProfile = ColorProfileHelper.GetDisplayDefaultColorProfile(foundConfig.AdapterId, foundConfig.SourceId);
                            // Native
                            setting.NativeWidth = foundConfig.NativeWidth;
                            setting.NativeHeight = foundConfig.NativeHeight;

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

                        // Detect clone groups from live CCD source relationships
                        var cloneGroups = displayConfigs
                            .Select((config, index) => new { config, index })
                            .Where(item => item.config.IsEnabled)
                            .GroupBy(item => new { item.config.AdapterId.HighPart, item.config.AdapterId.LowPart, item.config.SourceId })
                            .Where(group => group.Count() > 1)
                            .ToList();
                        if (cloneGroups.Any())
                        {
                            int cloneGroupIndex = 1;
                            foreach (var group in cloneGroups)
                            {
                                string cloneGroupId = $"clone-group-{cloneGroupIndex}";
                                bool first = true;
                                foreach (var item in group)
                                {
                                    var setting = settings[item.index];
                                    setting.CloneGroupId = cloneGroupId;
                                    setting.IsCloneSource = first;
                                    first = false;
                                    _logger.Info($"Detected clone group '{cloneGroupId}': " + $"{setting.ReadableDeviceName} (TargetId: {setting.TargetId})");
                                }
                                cloneGroupIndex++;
                            }
                            _logger.Info($"Detected {TextHelper.Plural(cloneGroups.Count, "clone group")} with {cloneGroups.Sum(g => g.Count())} total displays");
                        }
                    }
                }
                catch (AdvancedColorCaptureUnavailableException ex)
                {
                    _logger.Warn(ex.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error getting current display settings");
                }

                return settings;
            });
        }

        internal static bool TryMapDisplaySettingForApply(
            DisplaySetting setting,
            List<DisplayConfigHelper.DisplayConfigInfo> activeDisplayConfigs,
            List<DisplayConfigHelper.DisplayConfigInfo> addressDisplayConfigs,
            out DisplayConfigHelper.DisplayConfigInfo mapped,
            out DisplayConfigHelper.CurrentAddressResolutionStatus resolutionStatus)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));

            var currentAddress = DisplayConfigHelper.ResolveCurrentApplyAddressDetailed(setting, addressDisplayConfigs, out resolutionStatus);
            if (currentAddress == null && resolutionStatus != DisplayConfigHelper.CurrentAddressResolutionStatus.Absent)
            {
                mapped = null;
                return false;
            }

            var active = currentAddress != null
                ? activeDisplayConfigs?.FirstOrDefault(c => CcdAddress.Target(c).Equals(CcdAddress.Target(currentAddress)))
                : null;

            mapped = new DisplayConfigHelper.DisplayConfigInfo
            {
                // Current hardware address
                DeviceName = !string.IsNullOrEmpty(currentAddress?.DeviceName) ? currentAddress.DeviceName : setting.DeviceName,
                FriendlyName = setting.ReadableDeviceName,
                ManufacturerName = !string.IsNullOrEmpty(currentAddress?.ManufacturerName) ? currentAddress.ManufacturerName : setting.ManufacturerName,
                ProductCodeID = !string.IsNullOrEmpty(currentAddress?.ProductCodeID) ? currentAddress.ProductCodeID : setting.ProductCodeID,
                MonitorDevicePath = currentAddress?.MonitorDevicePath ?? string.Empty,
                AdapterId = currentAddress?.AdapterId ?? default,
                TargetId = currentAddress?.TargetId ?? setting.TargetId,
                RawTargetId = currentAddress?.RawTargetId ?? setting.TargetId,
                PathIndex = currentAddress?.PathIndex ?? 0,
                OutputTechnology = currentAddress?.OutputTechnology ?? default,
                // Desired topology state
                SourceId = 0,
                IsEnabled = setting.IsEnabled,
                IsPrimary = setting.IsPrimary,
                DisplayPositionX = setting.DisplayPositionX,
                DisplayPositionY = setting.DisplayPositionY,
                Width = setting.Width,
                Height = setting.Height,
                RefreshRate = setting.Frequency,
                Rotation = (DisplayConfigHelper.DisplayConfigRotation)setting.Rotation,
                IsHdrSupported = active?.IsHdrSupported ?? setting.IsHdrSupported,
                IsWcgSupported = active?.IsWcgSupported ?? setting.IsWcgSupported,
                IsHdrEnabled = setting.IsHdrEnabled,
                IsWcgEnabled = setting.IsWcgEnabled,
                ColorProfile = setting.ColorProfile
            };
            return true;
        }

        internal static void AssignDesiredSourceGroups(IEnumerable<(DisplaySetting Setting, DisplayConfigHelper.DisplayConfigInfo Mapped)> mappings)
        {
            var sourceByGroup = new Dictionary<string, uint>(StringComparer.Ordinal);
            var nextSourceByAdapter = new Dictionary<string, uint>(StringComparer.Ordinal);
            int independentOrdinal = 0;

            foreach (var pair in mappings ?? Enumerable.Empty<(DisplaySetting, DisplayConfigHelper.DisplayConfigInfo)>())
            {
                if (pair.Setting == null || pair.Mapped == null) continue;

                string adapterKey = CcdAddress.FormatLuid(pair.Mapped.AdapterId);
                string semanticGroup = pair.Setting.IsPartOfCloneGroup()
                    ? $"clone:{pair.Setting.CloneGroupId}"
                    : $"display:{independentOrdinal++}";
                string key = $"{adapterKey}|{semanticGroup}";

                if (!sourceByGroup.TryGetValue(key, out uint sourceId))
                {
                    nextSourceByAdapter.TryGetValue(adapterKey, out sourceId);
                    sourceByGroup[key] = sourceId;
                    nextSourceByAdapter[adapterKey] = sourceId + 1;
                }

                pair.Mapped.SourceId = sourceId;
            }
        }

        internal static bool TryMapDisplaySettingsForApply(
            IEnumerable<DisplaySetting> settings,
            List<DisplayConfigHelper.DisplayConfigInfo> activeDisplayConfigs,
            List<DisplayConfigHelper.DisplayConfigInfo> addressDisplayConfigs,
            out List<DisplayConfigHelper.DisplayConfigInfo> mappedDisplayConfigs,
            Action<DisplaySetting, DisplayConfigHelper.CurrentAddressResolutionStatus> unsafeResolution = null)
        {
            mappedDisplayConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>();
            var pendingSettings = (settings ?? Enumerable.Empty<DisplaySetting>()).ToList();
            if (pendingSettings.Count > 0 && (addressDisplayConfigs == null || addressDisplayConfigs.Count == 0))
            {
                foreach (var setting in pendingSettings)
                    unsafeResolution?.Invoke(setting, DisplayConfigHelper.CurrentAddressResolutionStatus.InsufficientEvidence);
                return false;
            }

            bool allSafe = true;
            var mappedPairs = new List<(DisplaySetting Setting, DisplayConfigHelper.DisplayConfigInfo Mapped)>();
            foreach (var setting in pendingSettings)
            {
                if (!TryMapDisplaySettingForApply(setting, activeDisplayConfigs, addressDisplayConfigs, out var mapped, out var resolutionStatus))
                {
                    allSafe = false;
                    unsafeResolution?.Invoke(setting, resolutionStatus);
                    continue;
                }

                mappedDisplayConfigs.Add(mapped);
                mappedPairs.Add((setting, mapped));
            }

            AssignDesiredSourceGroups(mappedPairs);
            return allSafe;
        }

        internal static DisplayConfigHelper.DisplayConfigInfo MapDisplaySettingForApply(
            DisplaySetting setting,
            List<DisplayConfigHelper.DisplayConfigInfo> activeDisplayConfigs,
            List<DisplayConfigHelper.DisplayConfigInfo> addressDisplayConfigs)
        {
            TryMapDisplaySettingForApply(setting, activeDisplayConfigs, addressDisplayConfigs, out var mapped, out _);
            if (mapped != null)
                AssignDesiredSourceGroups(new[] { (setting, mapped) });
            return mapped;
        }
        internal static Task<bool> ApplyWallpaperOffUiAsync(
            WallpaperSettings settings,
            Func<WallpaperSettings, bool> apply = null) =>
            Task.Run(() => (apply ?? WallpaperHelper.Apply)(settings));
        public Task<ProfileApplyResult> ApplyProfileAsync(Profile profile, ApplySource source = ApplySource.Unknown) => _applyAuthority.EnqueueAsync(() => ApplyProfileCoreAsync(profile, source));

        private async Task<ProfileApplyResult> ApplyProfileCoreAsync(Profile profile, ApplySource source)
        {
            try
            {
                var totalWatch = Stopwatch.StartNew();
                var previousProfileId = _currentProfileId;
                _logger.Info($"Applying profile '{profile.Name}'...");

                // Capture Pre-Apply Snapshot
                List<DisplayConfigHelper.DisplayConfigInfo> preApplySnapshot = null;
                if (!_rollingBack && _applyRuntime.ShouldRollbackAfterApplyFailure())
                    preApplySnapshot = _applyRuntime.GetDisplayConfigs();

                // Map Display Configurations
                ProfileApplyResult result = new ProfileApplyResult { AudioSuccess = true, DpiChanged = true };
                var mapWatch = Stopwatch.StartNew();
                var displayConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>();
                var displayAddressAuthorizations = new List<DisplayApplyAddressAuthorization>();
                bool displayAddressResolutionSafe = true;
                if (profile.DisplaySettings.Count > 0)
                {
                    var activeDisplayConfigs = _applyRuntime.GetDisplayConfigs();
                    var addressDisplayConfigs = _applyRuntime.GetAllPathDisplayAddresses();
                    displayAddressResolutionSafe = DisplayApplyAuthorization.TryMapDisplaySettingsForApply(
                        profile.DisplaySettings,
                        activeDisplayConfigs,
                        addressDisplayConfigs,
                        out displayConfigs,
                        out displayAddressAuthorizations,
                        (setting, resolutionStatus) => _logger.Error(
                            $"Cannot safely resolve current display address for '{setting.ReadableDeviceName}' ({resolutionStatus}); refusing topology mutation."));
                }
                mapWatch.Stop();

                // Apply Display Topology
                var topologyWatch = Stopwatch.StartNew();
                bool topologyApplied = displayAddressResolutionSafe && _applyRuntime.ApplyDisplayTopology(displayConfigs);
                if (!displayAddressResolutionSafe)
                    _logger.Warn($"Topology apply blocked for '{profile.Name}' because one or more display addresses were unsafe to resolve.");
                if (ShouldForceApplyFailureAt(1))
                    topologyApplied = false;
                if (!topologyApplied)
                    _logger.Warn($"Topology apply failed for '{profile.Name}'");
                topologyWatch.Stop();

                // Apply Display Configuration
                var configWatch = Stopwatch.StartNew();
                if (topologyApplied)
                {
                    var displayResult = await _applyRuntime.ApplyDisplayConfigDetailed(displayConfigs);
                    result.DisplayConfigApplied = displayResult.Success;
                    result.AdvancedColorSuccess = displayResult.AdvancedColorSuccess;
                    result.ColorProfileSuccess = displayResult.ColorProfileSuccess;
                }
                configWatch.Stop();

                if (topologyApplied && ShouldForceApplyFailureAt(2))
                    result.DisplayConfigApplied = false;

                // Handle Display-Stage Failure
                if (!result.DisplayConfigApplied && !_rollingBack && _applyRuntime.ShouldAbortOnApplyFailure())
                {
                    _logger.Warn($"Aborting apply of '{profile.Name}' — display configuration failed");
                    result.Success = false;

                    if (_applyRuntime.ShouldRollbackAfterApplyFailure())
                        await RollbackFailedApplyAsync(previousProfileId, preApplySnapshot, profile.Name);

                    return result;
                }

                // Apply DPI Settings
                var dpiWatch = Stopwatch.StartNew();
                var dpiLiveConfigs = _applyRuntime.GetDisplayConfigs();
                result.DpiChanged = DisplayApplyAuthorization.ApplyDpiSettings(
                    displayAddressAuthorizations,
                    dpiLiveConfigs,
                    _applyRuntime.SetDpiScaling);
                dpiWatch.Stop();

                // Apply Wallpaper Settings
                var wallpaperWatch = Stopwatch.StartNew();
                if (profile.WallpaperSettings?.Enabled == true)
                {
                    try
                    {
                        result.WallpaperSuccess = await ApplyWallpaperOffUiAsync(profile.WallpaperSettings);
                    }
                    catch (Exception ex)
                    {
                        result.WallpaperSuccess = false;
                        _logger.Warn(ex, "Wallpaper apply failed");
                    }
                }
                wallpaperWatch.Stop();

                // Apply Audio Settings
                var audioWatch = Stopwatch.StartNew();
                if (profile.AudioSettings?.Enabled == true)
                    result.AudioSuccess = AudioHelper.ApplyAudioSettings(profile.AudioSettings);
                audioWatch.Stop();

                // Finalize Result
                var finalizeWatch = new Stopwatch();
                var scriptWatch = new Stopwatch();

                result.Success = result.DisplayConfigApplied;

                // Execute Scripts
                scriptWatch.Start();
                if (profile.ScriptSettings?.Enabled == true && profile.ScriptSettings.Scripts?.Any() == true)
                {
                    var enabledScripts = profile.ScriptSettings.Scripts.Count(s => s.IsEnabled);
                    var disabledNote = enabledScripts == profile.ScriptSettings.Scripts.Count
                        ? ""
                        : $" ({profile.ScriptSettings.Scripts.Count - enabledScripts} disabled)";

                    _logger.Info($"Executing {TextHelper.Plural(enabledScripts, "script")}{disabledNote}...");
                    foreach (var command in profile.ScriptSettings.Scripts)
                        _scriptManager.ExecuteScript(command);
                }
                else if (profile.ScriptSettings?.Enabled != true && profile.ScriptSettings?.Scripts?.Any() == true)
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
                    result.CurrentProfilePersisted = await _settingsManager.SetCurrentProfileIdAsync(profile.Id);
                    if (!result.CurrentProfilePersisted)
                        _logger.Warn($"Profile '{profile.Name}' applied, but the current-profile marker could not be persisted");
                    finalizeWatch.Stop();

                    // Self-Heal Missing Hardware Info
                    var postApplyLiveConfigs = DisplayConfigHelper.GetDisplayConfigs();
                    var profilesToPersist = BackfillHardwareInfoAcrossProfiles(profile, postApplyLiveConfigs);
                    foreach (var changedProfile in profilesToPersist)
                        await SaveProfileAsync(changedProfile);
                }

                totalWatch.Stop();

                if (result.Success)
                {
                    string warningSummary = GetApplyWarningSummary(result);
                    if (!string.IsNullOrEmpty(warningSummary))
                        _logger.Warn($"Profile '{profile.Name}' applied with warnings: {warningSummary}");

                    ProfileApplied?.Invoke(this, new ProfileAppliedEventArgs(profile, source, totalWatch.ElapsedMilliseconds, warningSummary));
                }

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

        internal static DisplayConfigHelper.DisplayConfigInfo ResolveHardwareBackfillDisplay(DisplaySetting setting, List<DisplayConfigHelper.DisplayConfigInfo> liveConfigs) => DisplayConfigHelper.ResolveLiveDisplay(setting, liveConfigs);

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

            var repairedTargets = new HashSet<CcdTargetKey>();
            bool appliedChanged = false;

            foreach (var setting in appliedProfile.DisplaySettings)
            {
                if (!HasIncompleteHardwareInfo(setting)) continue;
                var live = ResolveHardwareBackfillDisplay(setting, liveConfigs);
                if (live == null) continue;

                if (BackfillHardwareInfoFromLive(setting, live))
                {
                    appliedChanged = true;
                    repairedTargets.Add(CcdAddress.Target(live));
                }
            }

            if (appliedChanged)
                changedProfiles.Add(appliedProfile);

            if (repairedTargets.Count == 0)
            {
                return changedProfiles;
            }

            foreach (var other in _profiles.Where(p => p.Id != appliedProfile.Id))
            {
                bool otherChanged = false;

                foreach (var setting in other.DisplaySettings)
                {
                    if (!HasIncompleteHardwareInfo(setting)) continue;
                    var live = ResolveHardwareBackfillDisplay(setting, liveConfigs);
                    if (live == null || !repairedTargets.Contains(CcdAddress.Target(live))) continue;

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
            if (!rollbackAfterApplyFailure)
            {
                return RollbackTarget.None;
            }

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

                    var rollbackResult = await ApplyProfileCoreAsync(previous, ApplySource.Unknown);
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
                $"Display Layout: {result.DisplayConfigApplied},\n" +
                $"Advanced Color: {result.AdvancedColorSuccess},\n" +
                $"Color Profile: {result.ColorProfileSuccess},\n" +
                $"DPI: {result.DpiChanged},\n" +
                $"Wallpaper: {result.WallpaperSuccess},\n" +
                $"Audio: {result.AudioSuccess}";

            return errorDetails;
        }

        #endregion

        #region Query

        public Profile GetProfile(string profileId) => _profiles.FirstOrDefault(p => p.Id == profileId);

        public Profile GetProfileByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string cleanName = name.Trim();
            return _profiles.FirstOrDefault(p => p.Name.Trim().Equals(cleanName, StringComparison.OrdinalIgnoreCase));
        }

        internal Profile ResolveProfileNameOrId(string nameOrId)
        {
            if (string.IsNullOrWhiteSpace(nameOrId))
            {
                return null;
            }

            return GetProfile(nameOrId) ?? GetProfileByName(nameOrId);
        }

        public Profile GetCurrentProfile()
        {
            if (string.IsNullOrEmpty(_currentProfileId))
            {
                return null;
            }

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

        public Task<bool> AddProfileAsync(Profile profile) => AddProfileAsync(profile, SaveProfileAsync);

        internal async Task<bool> AddProfileAsync(Profile profile, Func<Profile, Task<bool>> saveProfile)
        {
            if (!await saveProfile(profile))
            {
                return false;
            }

            AddProfile(profile);
            return true;
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

        public Task<bool> UpdateProfileAsync(Profile profile) => UpdateProfileAsync(profile, SaveProfileAsync);

        internal async Task<bool> UpdateProfileAsync(Profile profile, Func<Profile, Task<bool>> saveProfile)
        {
            var existingProfile = GetProfile(profile.Id);
            if (existingProfile == null)
            {
                return false;
            }

            profile.UpdateLastModified();
            if (!await saveProfile(profile))
            {
                return false;
            }

            var index = _profiles.IndexOf(existingProfile);
            _profiles[index] = profile;
            ProfileUpdated?.Invoke(this, profile);
            return true;
        }

        public void DeleteProfile(string profileId)
        {
            _profiles.RemoveAll(p => p.Id == profileId);
            ProfileDeleted?.Invoke(this, profileId);
        }

        public Task<bool> DeleteProfileAsync(string profileId) => DeleteProfileAsync(profileId, DeleteProfileFileAsync);

        internal async Task<bool> DeleteProfileAsync(string profileId, Func<string, Task<bool>> deleteProfileFile)
        {
            try
            {
                if (!await deleteProfileFile(profileId))
                {
                    return false;
                }

                DeleteProfile(profileId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting profile");
                return false;
            }
        }

        private async Task<bool> DeleteProfileFileAsync(string profileId)
        {
            try
            {
                var filePath = GetProfileFilePath(profileId);
                if (File.Exists(filePath))
                    await Task.Run(() => File.Delete(filePath));

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting profile file");
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
            if (composed.Length <= MaxProfileNameLength)
            {
                return composed;
            }

            var existing = MarkerChain(stem);
            var root = stem.Substring(0, stem.Length - existing.Length);
            var tail = existing + marker;

            var room = MaxProfileNameLength - tail.Length - 1;
            if (room < 1)
            {
                return composed.Substring(0, MaxProfileNameLength);
            }

            return root.Substring(0, Math.Min(room, root.Length)).TrimEnd() + "\u2026" + tail;
        }

        private static readonly Regex _markerPattern = new Regex(@"(( - Copy)|( \(\d+\)))+$", RegexOptions.Compiled);

        private static string MarkerChain(string name)
        {
            var m = _markerPattern.Match(name);
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
