using DisplayProfileManager.Helpers;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DisplayProfileManager.Core
{
    public class ProfileManager
    {
        #region Core

        private static readonly Logger logger = LoggerHelper.GetLogger();
        private static readonly object _lock = new object();

        public class ProfileApplyResult
        {
            public bool Success { get; set; }
            public bool PrimaryChanged { get; set; }
            public bool DisplayConfigApplied { get; set; }
            public bool ResolutionChanged { get; set; }
            public bool DpiChanged { get; set; }
            public bool AudioSuccess { get; set; }
            public List<string> DisconnectedDisplays { get; set; } = new List<string>();
        }

        private const int CurrentSchemaVersion = 3;

        private static ProfileManager _instance;
        private readonly ScriptManager _scriptManager = ScriptManager.Instance;
        private readonly SettingsManager _settingsManager = SettingsManager.Instance;

        private List<Profile> _profiles;
        private string _currentProfileId;

        private readonly string _appDataFolder;
        private readonly string _profilesFolderPath;

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
        public event EventHandler<Profile> ProfileApplied;
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

        private string GetProfileFilePath(string profileId) =>  Path.Combine(_profilesFolderPath, $"{profileId}.dpm");

        private static void AtomicWrite(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
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
                        var profile = JsonConvert.DeserializeObject<Profile>(json);

                        if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || profile.DisplaySettings == null)
                        {
                            logger.Warn($"Skipping invalid profile file: {Path.GetFileName(file)}");
                            continue;
                        }

                        if (profile.SchemaVersion < CurrentSchemaVersion)
                        {
                            if (liveConfigs == null)
                                liveConfigs = DisplayConfigHelper.GetDisplayConfigs();

                            bool migrated = await MigrateProfileAsync(profile, liveConfigs);
                            if (migrated)
                            {
                                var savedDate = profile.LastModifiedDate;
                                await SaveProfileAsync(profile);
                                profile.LastModifiedDate = savedDate;
                                logger.Info($"Migrated profile '{profile.Name}' to schema version {CurrentSchemaVersion}");
                            }
                        }

                        _profiles.Add(profile);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Error loading profile from {file}");
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
                logger.Error(ex, "Error loading profiles");
                _profiles = new List<Profile>();
                return false;
            }
        }

        private async Task<bool> MigrateProfileAsync(Profile profile, List<DisplayConfigHelper.DisplayConfigInfo> liveConfigs)
        {
            bool changed = false;

            // Version 0 → 1: backfill NativeWidth/NativeHeight and fix ReadableDeviceName
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
                        logger.Info($"Migration: {setting.ReadableDeviceName} (TargetId {setting.TargetId}) not connected, skipping backfill");
                }

                profile.SchemaVersion = 1;
                changed = true;
            }

            // Version 1 → 2: icon field added
            if (profile.SchemaVersion < 2)
            {
                profile.SchemaVersion = 2;
                changed = true;
            }

            // Version 2 → 3: List<string> scripts migrated to List<Script>; backfill ColorProfile from OS
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
                                logger.Warn(ex, $"Migration: failed to get color profile for {setting.ReadableDeviceName}");
                            }
                        }
                        else
                            logger.Info($"Migration: {setting.ReadableDeviceName} (TargetId {setting.TargetId}) not connected, skipping color profile backfill");
                    }
                }

                profile.SchemaVersion = 3;
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
                await Task.Run(() => AtomicWrite(filePath, json));

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error saving profile");
                return false;
            }
        }

        public async Task<Profile> ImportProfileAsync(string sourcePath)
        {
            EnsureProfilesFolderExists();

            try
            {
                var json = await Task.Run(() => File.ReadAllText(sourcePath));
                var profile = JsonConvert.DeserializeObject<Profile>(json);

                if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || profile.DisplaySettings == null)
                {
                    logger.Warn($"Invalid profile file: {sourcePath}");
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
                logger.Error(ex, "Error importing profile");
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
                Name = GetUniqueProfileName(sourceProfile.Name),
                Description = sourceProfile.Description,
                Icon = sourceProfile.Icon,
                IsDefault = false,
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
                    SerialNumberID = ds.SerialNumberID,
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
                    AvailableResolutions = ds.AvailableResolutions != null ? new List<string>(ds.AvailableResolutions) : new List<string>(),
                    AvailableRefreshRates = ds.AvailableRefreshRates != null ? new Dictionary<string, List<int>>(ds.AvailableRefreshRates.ToDictionary(kvp => kvp.Key, kvp => new List<int>(kvp.Value))) : new Dictionary<string, List<int>>(),
                    AvailableDpiScaling = ds.AvailableDpiScaling != null ? new List<uint>(ds.AvailableDpiScaling) : new List<uint>()
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
                EnableScripts = sourceProfile.EnableScripts,
                Scripts = new List<Script>(sourceProfile.Scripts),
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
            defaultProfile.IsDefault = true;
            try
            {
                var currentSettings = await GetCurrentDisplaySettingsAsync();
                defaultProfile.DisplaySettings.AddRange(currentSettings);

                AddProfile(defaultProfile);
                _currentProfileId = defaultProfile.Id;
                await SaveProfileAsync(defaultProfile);
                await _settingsManager.SetCurrentProfileIdAsync(defaultProfile.Id);

                return defaultProfile;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error creating default profile");
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
                    logger.Debug("Getting current display settings...");

                    List<DisplayHelper.DisplayInfo> displays = DisplayHelper.GetDisplays();

                    List<DisplayHelper.MonitorInfo> monitors = DisplayHelper.GetMonitorsFromWin32PnPEntity();
                    List<DisplayHelper.MonitorIdInfo> monitorIDs = DisplayHelper.GetMonitorIDsFromWmiMonitorID();

                    List<DisplayConfigHelper.DisplayConfigInfo> displayConfigs = DisplayConfigHelper.GetDisplayConfigs();

                    if (monitors.Count > 0 &&
                        monitorIDs.Count > 0 &&
                        displayConfigs.Count > 0)
                    {
                        for (int i = 0; i < displayConfigs.Count; i++)
                        {
                            var foundConfig = displayConfigs[i];
                            var foundDisplay = displays.Find(x => x.DeviceName == foundConfig.DeviceName);
                            var foundMonitor = monitors.Find(x => x.DeviceID.Contains($"UID{foundConfig.TargetId}"));

                            if (foundMonitor == null)
                                logger.Warn($"No WMI monitor found for TargetId {foundConfig.TargetId} using DisplayConfigHelper data");

                            DisplayHelper.MonitorIdInfo foundMonitorId = null;
                            if (foundMonitor != null)
                            {
                                foundMonitorId = monitorIDs.Find(x => x.InstanceName.ToUpper().Contains(foundMonitor.PnPDeviceID.ToUpper()));
                                if (foundMonitorId == null)
                                    logger.Warn($"No WMI monitor ID found for {foundMonitor.PnPDeviceID} using generic data");
                            }

                            string adpaterIdText = $"{foundConfig.AdapterId.HighPart:X8}{foundConfig.AdapterId.LowPart:X8}";
                            DpiHelper.DPIScalingInfo dpiInfo = DpiHelper.GetDPIScalingInfo(foundConfig.DeviceName, foundConfig);

                            DisplaySetting setting = new DisplaySetting();
                            // Identity
                            setting.DeviceName = foundConfig.DeviceName;
                            setting.DeviceString = foundDisplay?.DeviceString ?? foundConfig.DeviceName;
                            setting.ReadableDeviceName = !string.IsNullOrEmpty(foundConfig.FriendlyName) ? foundConfig.FriendlyName : foundMonitor?.Name ?? foundConfig.DeviceName;
                            setting.ManufacturerName = foundMonitorId?.ManufacturerName ?? "";
                            setting.ProductCodeID = foundMonitorId?.ProductCodeID ?? "";
                            setting.SerialNumberID = foundMonitorId?.SerialNumberID ?? "";
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

                            // Capture available options for this monitor
                            try
                            {
                                setting.AvailableResolutions = DisplayHelper.GetSupportedResolutionsOnly(setting.DeviceName);
                                setting.AvailableRefreshRates = new Dictionary<string, List<int>>();
                                setting.AvailableDpiScaling = DpiHelper.GetSupportedDpiScalingOnly(setting.DeviceName).ToList();

                                foreach (var resolution in setting.AvailableResolutions)
                                {
                                    var parts = resolution.Split('x');
                                    if (parts.Length == 2 &&
                                        int.TryParse(parts[0], out int width) &&
                                        int.TryParse(parts[1], out int height))
                                    {
                                        var refreshRates = DisplayHelper.GetAvailableRefreshRates(setting.DeviceName, width, height);
                                        if (refreshRates.Count > 0)
                                        {
                                            setting.AvailableRefreshRates[resolution] = refreshRates;
                                        }
                                    }
                                }

                                logger.Debug($"Captured available options for {setting.DeviceName}: " +
                                    $"{setting.AvailableResolutions.Count} resolutions, " +
                                    $"{setting.AvailableRefreshRates.Count} resolution-refresh rate mappings" +
                                    $"{setting.AvailableDpiScaling.Count} DPI values, ");
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex, $"Error capturing available options for {setting.DeviceName}");
                            }

                            settings.Add(setting);
                        }

                        logger.Info($"Created {settings.Count} display settings from {displayConfigs.Count} configs");

                        // Detect clone groups by grouping displays with same DeviceName and SourceId
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
                                    logger.Info($"Detected clone group '{cloneGroupId}': " + $"{setting.ReadableDeviceName} (TargetId: {setting.TargetId})");
                                }
                                cloneGroupIndex++;
                            }
                            logger.Info($"Detected {cloneGroups.Count} clone group(s) with {cloneGroups.Sum(g => g.Count())} total displays");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error getting current display settings");
                }

                return settings;
            });
        }

        public async Task<ProfileApplyResult> ApplyProfileAsync(Profile profile)
        {
            try
            {
                var totalWatch = Stopwatch.StartNew();
                logger.Info($"Applying profile '{profile.Name}'...");
                ProfileApplyResult result = new ProfileApplyResult { AudioSuccess = true };

                // Map Display Configurations
                var mapWatch = Stopwatch.StartNew();
                var displayConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>();
                if (profile.DisplaySettings.Count > 0)
                {
                    var wmiMonitorIds = DisplayHelper.GetMonitorIDsFromWmiMonitorID();
                    foreach (var setting in profile.DisplaySettings)
                    {
                        setting.UpdateDeviceNameFromWMI(wmiMonitorIds);
                        displayConfigs.Add(new DisplayConfigHelper.DisplayConfigInfo
                        {
                            // Identity
                            DeviceName = setting.DeviceName,
                            FriendlyName = setting.ReadableDeviceName,
                            AdapterId = DisplayConfigHelper.GetLUIDFromString(setting.AdapterId),
                            SourceId = setting.SourceId,
                            TargetId = setting.TargetId,
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

                // Detect Disconnected Displays
                var liveConfigs = DisplayConfigHelper.GetDisplayConfigs();
                var disconnected = displayConfigs.Where(dc => dc.IsEnabled && !liveConfigs.Any(c => c.TargetId == dc.TargetId)).ToList();
                if (disconnected.Any())
                {
                    foreach (var dc in disconnected)
                    {
                        var name = !string.IsNullOrEmpty(dc.FriendlyName) ? dc.FriendlyName : dc.DeviceName;
                        logger.Warn($"Display not detected: {name} (TargetId: {dc.TargetId})");
                        result.DisconnectedDisplays.Add(name);
                    }
                }

                // Apply Display Topology
                var topologyWatch = Stopwatch.StartNew();
                if (!DisplayConfigHelper.ApplyDisplayTopology(displayConfigs))
                {
                    result.Success = false;
                    return result;
                }
                topologyWatch.Stop();

                // Defer until Topology is Stabilized
                var deferWatch = Stopwatch.StartNew();
                var connectedConfigs = displayConfigs.Where(dc => !result.DisconnectedDisplays.Any(name => name.Equals(dc.FriendlyName, StringComparison.OrdinalIgnoreCase))).ToList();
                await DisplayConfigHelper.DeferDisplayLayoutAsync(connectedConfigs);
                deferWatch.Stop();

                // Apply Display Configuration
                var configWatch = Stopwatch.StartNew();
                result.DisplayConfigApplied = await DisplayConfigHelper.ApplyDisplayConfig(displayConfigs);
                configWatch.Stop();

                // Apply DPI Settings
                var dpiWatch = Stopwatch.StartNew();
                bool allDpiChanged = true;
                var uniqueDevicesForDpi = profile.DisplaySettings.Where(s => s.IsEnabled).GroupBy(s => s.DeviceName).Select(g => g.First()).ToList();

                foreach (var setting in uniqueDevicesForDpi)
                {
                    if (DisplayHelper.IsMonitorConnected(setting.DeviceName))
                    {
                        if (!DpiHelper.SetDPIScaling(setting.DeviceName, setting.DpiScaling))
                        {
                            logger.Warn($"Failed to set DPI scaling for {setting.DeviceName}");
                            allDpiChanged = false;
                        }
                    }
                }
                result.DpiChanged = allDpiChanged;
                dpiWatch.Stop();

                // Apply Audio Settings
                var audioWatch = Stopwatch.StartNew();
                if (profile.AudioSettings != null)
                    result.AudioSuccess = AudioHelper.ApplyAudioSettings(profile.AudioSettings);
                audioWatch.Stop();

                // Finalize Result
                var finalizeWatch = new Stopwatch();
                var scriptWatch = new Stopwatch();

                result.Success = result.DisplayConfigApplied && result.DpiChanged;
                if (result.Success)
                {
                    // Execute Script(s)
                    scriptWatch.Start();
                    if (profile.EnableScripts && profile.Scripts != null && profile.Scripts.Any())
                    {
                        logger.Info($"Executing {profile.Scripts.Count} script(s)...");
                        foreach (var command in profile.Scripts)
                        {
                            _scriptManager.ExecuteScript(command);
                        }
                    }
                    else if (!profile.EnableScripts && profile.Scripts?.Any() == true)
                    {
                        logger.Debug("Scripts disabled, skipping execution");
                    }
                    scriptWatch.Stop();

                    // Log Result and Persist Success
                    var cloneGroupCount = profile.DisplaySettings.Where(s => s.IsPartOfCloneGroup()).GroupBy(s => s.CloneGroupId).Count();

                    var activeCount = profile.DisplaySettings.Count(d => d.IsEnabled);
                    var sb = new StringBuilder();
                    sb.Append($"Successfully applied profile '{profile.Name}' -> ({activeCount} active display{(activeCount == 1 ? "" : "s")})");
                    if (cloneGroupCount > 0)
                        sb.Append($" | ({cloneGroupCount} clone group{(cloneGroupCount == 1 ? "" : "s")})");
                    if (result.DisconnectedDisplays.Any())
                        sb.Append($" | ({result.DisconnectedDisplays.Count} display{(result.DisconnectedDisplays.Count == 1 ? "" : "s")} not detected)");
                    logger.Info(sb.ToString());

                    finalizeWatch.Start();
                    _currentProfileId = profile.Id;
                    await _settingsManager.SetCurrentProfileIdAsync(profile.Id);
                    ProfileApplied?.Invoke(this, profile);
                    finalizeWatch.Stop(); totalWatch.Stop();
                }

                // Timing Summary
                logger.Info($"[PERF] Map: {mapWatch.ElapsedMilliseconds} ms");
                logger.Info($"[PERF] Topology: {topologyWatch.ElapsedMilliseconds} ms | Defer: {deferWatch.ElapsedMilliseconds} ms | Config: {configWatch.ElapsedMilliseconds} ms");
                logger.Info($"[PERF] DPI: {dpiWatch.ElapsedMilliseconds} ms | Audio: {audioWatch.ElapsedMilliseconds} ms | Scripts: {scriptWatch.ElapsedMilliseconds} ms");
                logger.Info($"[PERF] Finalize: {finalizeWatch.ElapsedMilliseconds} ms");
                logger.Info($"[PERF] TOTAL: {totalWatch.ElapsedMilliseconds} ms");

                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error applying profile");
                return new ProfileApplyResult { Success = false };
            }
        }

        public string GetApplyResultErrorMessage(string profileName, ProfileApplyResult result)
        {
            string errorDetails =
                $"Failed to apply profile '{profileName}'.\n" +
                $"Some settings may not have been applied correctly.\n\n" +
                $"Display Config: {result.DisplayConfigApplied},\n" +
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
            return _profiles.FirstOrDefault(p =>
                p.Name.Trim().Equals(cleanName, StringComparison.OrdinalIgnoreCase));
        }

        public Profile GetCurrentProfile()
        {
            if (string.IsNullOrEmpty(_currentProfileId)) return null;

            return GetProfile(_currentProfileId);
        }

        public List<Profile> GetAllProfiles() => _profiles.ToList();

        public Profile GetDefaultProfile() => _profiles.FirstOrDefault(p => p.IsDefault);

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
                logger.Error(ex, "Error deleting profile");
                return false;
            }
        }

        public void SetDefaultProfile(string profileId)
        {
            foreach (var profile in _profiles)
            {
                profile.IsDefault = profile.Id == profileId;
            }
        }

        public async Task<bool> SetDefaultProfileAsync(string profileId)
        {
            SetDefaultProfile(profileId);
            bool success = true;
            foreach (var profile in _profiles)
            {
                if (!await SaveProfileAsync(profile))
                {
                    success = false;
                }
            }

            return success;
        }

        #endregion

        #region Checks

        public bool HasProfile(string name) => _profiles.Exists(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public string GetUniqueProfileName(string baseName)
        {
            if (!HasProfile(baseName))
                return baseName;

            int counter = 1;
            string uniqueName;
            do
            {
                uniqueName = $"{baseName} ({counter})";
                counter++;
            } while (HasProfile(uniqueName));

            return uniqueName;
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