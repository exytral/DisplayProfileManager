using DisplayProfileManager.Helpers;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DisplayProfileManager.Core
{
    public enum AutoStartMode
    {
        Registry,
        TaskScheduler
    }

    #region AppSettings

    public class AppSettings
    {
        // General
        [JsonProperty("firstRun")]
        public bool FirstRun { get; set; } = true;
        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        [JsonProperty("currentProfileId")]
        public string CurrentProfileId { get; set; } = string.Empty;
        [JsonProperty("defaultProfileId")]
        public string DefaultProfileId { get; set; } = string.Empty;
        [JsonProperty("startupProfileId")]
        public string StartupProfileId { get; set; } = string.Empty;
        [JsonProperty("theme")]
        public string Theme { get; set; } = "System";
        [JsonProperty("language")]
        public string Language { get; set; } = "en-US";

        // Startup
        [JsonProperty("startWithWindows")]
        public bool StartWithWindows { get; set; } = false;
        [JsonProperty("startInSystemTray")]
        public bool StartInSystemTray { get; set; } = false;
        [JsonProperty("autoStartMode")]
        public AutoStartMode AutoStartMode { get; set; } = AutoStartMode.Registry;
        [JsonProperty("checkForUpdates")]
        public bool CheckForUpdates { get; set; } = false;
        [JsonProperty("applyStartupProfile")]
        public bool ApplyStartupProfile { get; set; } = false;

        // Windows Behavior
        [JsonProperty("closeToTray")]
        public bool CloseToTray { get; set; } = true;
        [JsonProperty("rememberCloseChoice")]
        public bool RememberCloseChoice { get; set; } = false;
        [JsonProperty("showNotifications")]
        public bool ShowNotifications { get; set; } = true;
        [JsonProperty("desktopContextMenu")]
        public bool DesktopContextMenuEnabled { get; set; } = false;

        // App Behavior
        [JsonProperty("abortOnApplyFailure")]
        public bool AbortOnApplyFailure { get; set; } = true;
        [JsonProperty("rollbackAfterApplyFailure")]
        public bool RollbackAfterApplyFailure { get; set; } = true;
        [JsonProperty("rollbackToPreviousProfile")]
        public bool RollbackToPreviousProfile { get; set; } = true;

        // Debug Flags
        [JsonProperty("debugFlags", NullValueHandling = NullValueHandling.Ignore)]
        public DebugFlags DebugFlags { get; set; }
    }

    #endregion

    #region DebugFlags

    public class DebugFlags
    {
        [JsonProperty("forceApplyFailure")]
        public int ForceApplyFailure { get; set; }

        [JsonProperty("forceTopologyRecovery")]
        public bool ForceTopologyRecovery { get; set; }

        [JsonProperty("skipSpotlightRepaint")]
        public bool SkipSpotlightRepaint { get; set; }

        [JsonProperty("centerIconGrid")]
        public bool CenterIconGrid { get; set; }

        public bool AnySet =>
            ForceApplyFailure != 0 ||
            ForceTopologyRecovery ||
            SkipSpotlightRepaint ||
            CenterIconGrid;

        public override string ToString()
        {
            var set = new List<string>();

            if (ForceApplyFailure != 0) set.Add($"{nameof(ForceApplyFailure)}={ForceApplyFailure}");
            if (ForceTopologyRecovery) set.Add(nameof(ForceTopologyRecovery));
            if (SkipSpotlightRepaint) set.Add(nameof(SkipSpotlightRepaint));
            if (CenterIconGrid) set.Add(nameof(CenterIconGrid));

            return string.Join(", ", set);
        }
    }

    #endregion

    public class SettingsManager
    {
        #region Core

        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly object _lock = new object();

        private static SettingsManager _instance;
        private AppSettings _settings;
        private readonly string _settingsFilePath;
        private readonly string _appDataFolder;

        private bool _settingsLoaded;
        private readonly System.Threading.SemaphoreSlim _saveLock =
            new System.Threading.SemaphoreSlim(1, 1);

        public static SettingsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new SettingsManager();
                    }
                }

                return _instance;
            }
        }

        public AppSettings Settings => _settings;

        public event EventHandler<AppSettings> SettingsChanged;

        private SettingsManager()
        {
            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayProfileManager");

            _settingsFilePath = Path.Combine(_appDataFolder, "Settings.json");
            _settings = new AppSettings();

            EnsureAppDataFolderExists();

            string legacyPath = Path.Combine(_appDataFolder, "settings.json");
            if (File.Exists(legacyPath) && Path.GetFileName(legacyPath) != "Settings.json")
            {
                try
                {
                    string tempPath = _settingsFilePath + ".tmp";
                    File.Move(legacyPath, tempPath);

                    if (File.Exists(_settingsFilePath))
                        File.Delete(_settingsFilePath);

                    File.Move(tempPath, _settingsFilePath);
                }
                catch (Exception) { }
            }
        }

        private void EnsureAppDataFolderExists()
        {
            if (!Directory.Exists(_appDataFolder))
                Directory.CreateDirectory(_appDataFolder);
        }

        #endregion

        #region Persistence

        private static AppSettings DeserializeSettingsTolerant(string json)
        {
            var tolerant = new JsonSerializerSettings
            {
                Error = (_, args) =>
                {
                    _logger.Warn($"Settings member '{args.ErrorContext.Path}' could not be read -> using its default");

                    args.ErrorContext.Handled = true;
                }
            };

            return JsonConvert.DeserializeObject<AppSettings>(json, tolerant) ?? new AppSettings();
        }

        public async Task<bool> LoadSettingsAsync()
        {
            try
            {
                FileHelper.CleanupOrphanedTemps(
                    Path.GetDirectoryName(_settingsFilePath),
                    "Settings.json.*.tmp");

                if (File.Exists(_settingsFilePath))
                {
                    var json = await Task.Run(() =>
                        File.ReadAllText(_settingsFilePath));

                    _settings = DeserializeSettingsTolerant(json);
                    _settingsLoaded = true;
                }
                else
                {
                    _settings = new AppSettings();
                    _settingsLoaded = true;
                    await SaveSettingsAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading settings");
                _settings = new AppSettings();

                return false;
            }
        }

        public async Task<bool> SaveSettingsAsync()
        {
            // Refuse to write until valid settings load has completed
            if (!_settingsLoaded)
            {
                _logger.Warn("Refusing to save settings — no successful load (in-memory copy is blank defaults)");

                return false;
            }

            await _saveLock.WaitAsync();

            try
            {
                _settings.LastUpdated = DateTime.Now;

                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);

                await Task.Run(() => FileHelper.AtomicWrite(_settingsFilePath, json));

                SettingsChanged?.Invoke(this, _settings);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving settings");
                return false;
            }
            finally
            {
                _saveLock.Release();
            }
        }

        #endregion

        #region General Settings

        public async Task<bool> CompleteFirstRunAsync()
        {
            _settings.FirstRun = false;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetThemeAsync(string theme)
        {
            _settings.Theme = theme;
            return await SaveSettingsAsync();
        }

        public async Task<bool> UpdateSettingAsync<T>(string propertyName, T value)
        {
            try
            {
                var property = typeof(AppSettings).GetProperty(propertyName);

                if (property != null && property.CanWrite)
                {
                    property.SetValue(_settings, value);
                    return await SaveSettingsAsync();
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error updating setting {propertyName}");
                return false;
            }
        }

        public async Task<bool> SetCurrentProfileIdAsync(string profileId)
        {
            _settings.CurrentProfileId = profileId;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetDefaultProfileIdAsync(string profileId)
        {
            _settings.DefaultProfileId = profileId ?? string.Empty;
            return await SaveSettingsAsync();
        }

        #endregion

        #region Startup Settings

        public async Task<bool> SetStartWithWindowsStateOnlyAsync(bool startWithWindows)
        {
            _settings.StartWithWindows = startWithWindows;
            return await SaveSettingsAsync();
        }

        public async Task<AutoStartOperationResult> SetStartWithWindowsAsync(bool startWithWindows)
        {
            try
            {
                bool previousState = _settings.StartWithWindows;
                var autoStartHelper = new AutoStartHelper();

                if (startWithWindows)
                {
                    var operationResult = autoStartHelper.EnableAutoStart(_settings.AutoStartMode, _settings.StartInSystemTray);

                    if (operationResult != AutoStartOperationResult.Success)
                    {
                        _logger.Error("Failed to enable auto-start");
                        return operationResult;
                    }
                }
                else
                {
                    var operationResult = autoStartHelper.DisableAutoStart();

                    if (operationResult != AutoStartOperationResult.Success)
                    {
                        _logger.Error("Failed to disable auto-start");
                        return operationResult;
                    }
                }

                _settings.StartWithWindows = startWithWindows;
                var settingsSaved = await SaveSettingsAsync();

                if (!settingsSaved)
                {
                    _logger.Error("Failed to save settings after task change");
                    _settings.StartWithWindows = previousState;

                    AutoStartOperationResult rollbackResult;
                    if (startWithWindows)
                        rollbackResult = autoStartHelper.DisableAutoStart();
                    else
                        rollbackResult = autoStartHelper.EnableAutoStart(_settings.AutoStartMode, _settings.StartInSystemTray);

                    if (rollbackResult != AutoStartOperationResult.Success)
                        _logger.Error("Failed to restore external auto-start state after settings save failure");

                    return AutoStartOperationResult.Failed;
                }

                return AutoStartOperationResult.Success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting start with Windows");
                return AutoStartOperationResult.Failed;
            }
        }

        public async Task<AutoStartOperationResult> SetStartInSystemTrayAsync(bool startInSystemTray)
        {
            try
            {
                bool previousState = _settings.StartInSystemTray;

                if (startInSystemTray && !_settings.StartWithWindows)
                {
                    _logger.Warn("Cannot enable StartInSystemTray without StartWithWindows");

                    return AutoStartOperationResult.Failed;
                }

                AutoStartHelper autoStartHelper = null;

                if (_settings.StartWithWindows)
                {
                    autoStartHelper = new AutoStartHelper();

                    var operationResult = autoStartHelper.EnableAutoStart(_settings.AutoStartMode, startInSystemTray);

                    if (operationResult != AutoStartOperationResult.Success)
                    {
                        _logger.Error("Failed to update auto-start with tray setting");
                        return operationResult;
                    }
                }

                _settings.StartInSystemTray = startInSystemTray;
                bool settingsSaved = await SaveSettingsAsync();

                if (!settingsSaved)
                {
                    _settings.StartInSystemTray = previousState;

                    if (autoStartHelper != null)
                    {
                        var rollbackResult = autoStartHelper.EnableAutoStart(_settings.AutoStartMode, previousState);

                        if (rollbackResult != AutoStartOperationResult.Success)
                            _logger.Error("Failed to restore external auto-start state after settings save failure");
                    }

                    return AutoStartOperationResult.Failed;
                }

                return AutoStartOperationResult.Success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting start in system tray");
                return AutoStartOperationResult.Failed;
            }
        }

        public async Task<AutoStartOperationResult> SetAutoStartModeAsync(AutoStartMode mode)
        {
            try
            {
                AutoStartMode previousMode = _settings.AutoStartMode;

                if (!_settings.StartWithWindows)
                {
                    _settings.AutoStartMode = mode;

                    if (!await SaveSettingsAsync())
                    {
                        _settings.AutoStartMode = previousMode;
                        return AutoStartOperationResult.Failed;
                    }

                    _logger.Info($"Auto-start mode set to {mode}");
                    return AutoStartOperationResult.Success;
                }

                if (_settings.AutoStartMode == mode)
                {
                    _logger.Debug($"Already using {mode} mode");
                    return AutoStartOperationResult.Success;
                }

                var autoStartHelper = new AutoStartHelper();

                var disableResult = autoStartHelper.DisableAutoStart();
                if (disableResult != AutoStartOperationResult.Success)
                {
                    _logger.Error($"Could not remove previous auto-start entry -> not switching to {mode}");
                    return disableResult;
                }

                var success = autoStartHelper.EnableAutoStart(mode, _settings.StartInSystemTray);
                if (success != AutoStartOperationResult.Success)
                {
                    _logger.Error($"Failed to switch to {mode} mode, restoring previous mode");

                    var rollbackSucceeded = autoStartHelper.EnableAutoStart(
                        previousMode,
                        _settings.StartInSystemTray);

                    if (rollbackSucceeded != AutoStartOperationResult.Success)
                        _logger.Error($"Failed to restore previous {previousMode} auto-start registration");

                    return rollbackSucceeded == AutoStartOperationResult.Success ? success : AutoStartOperationResult.Failed;
                }

                _settings.AutoStartMode = mode;

                if (await SaveSettingsAsync())
                {
                    _logger.Info($"Successfully switched to {mode} mode");
                    return AutoStartOperationResult.Success;
                }

                _settings.AutoStartMode = previousMode;

                var settingsRollbackSucceeded = autoStartHelper.EnableAutoStart(
                    previousMode,
                    _settings.StartInSystemTray);

                if (settingsRollbackSucceeded != AutoStartOperationResult.Success)
                    _logger.Error($"Failed to restore previous {previousMode} auto-start registration after settings save failure");

                return AutoStartOperationResult.Failed;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting auto-start mode");
                return AutoStartOperationResult.Failed;
            }
        }

        public async Task<bool> SetCheckForUpdatesAsync(bool enabled)
        {
            _settings.CheckForUpdates = enabled;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetStartupProfileAsync(string profileId, bool applyOnStartup)
        {
            _settings.StartupProfileId = profileId;
            _settings.ApplyStartupProfile = applyOnStartup;

            return await SaveSettingsAsync();
        }

        #endregion

        #region Window Behavior Settings

        public async Task<bool> SetCloseToTrayAsync(bool closeToTray)
        {
            _settings.CloseToTray = closeToTray;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetRememberCloseChoiceAsync(bool rememberChoice)
        {
            _settings.RememberCloseChoice = rememberChoice;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetNotificationsAsync(bool showNotifications)
        {
            _settings.ShowNotifications = showNotifications;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetDesktopContextMenuAsync(bool enabled)
        {
            _settings.DesktopContextMenuEnabled = enabled;
            return await SaveSettingsAsync();
        }

        #endregion

        #region App Behavior Settings

        public async Task<bool> SetAbortOnApplyFailureAsync(bool enabled)
        {
            _settings.AbortOnApplyFailure = enabled;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetRollbackAfterApplyFailureAsync(bool enabled)
        {
            _settings.RollbackAfterApplyFailure = enabled;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetRollbackToPreviousProfileAsync(bool enabled)
        {
            _settings.RollbackToPreviousProfile = enabled;
            return await SaveSettingsAsync();
        }

        #endregion

        #region Get Settings

        public string GetSettingsFilePath() => _settingsFilePath;

        public string GetAppDataFolder() => _appDataFolder;

        // General
        public bool IsFirstRun() => _settings.FirstRun;
        public string GetDefaultProfileId() => _settings.DefaultProfileId;
        public string GetStartupProfileId() => _settings.StartupProfileId;
        public string GetCurrentProfileId() => _settings.CurrentProfileId;
        public string GetTheme() => _settings.Theme;
        public string GetLanguage() => _settings.Language;
        public DateTime GetLastUpdated() => _settings.LastUpdated;

        // Startup
        public bool ShouldStartWithWindows() => _settings.StartWithWindows;
        public bool ShouldStartInSystemTray() => _settings.StartInSystemTray && _settings.StartWithWindows;
        public bool ShouldCheckForUpdates() => _settings.CheckForUpdates;
        public bool ShouldApplyStartupProfile() => _settings.ApplyStartupProfile && !string.IsNullOrEmpty(_settings.StartupProfileId);

        // Windows Behavior
        public bool ShouldRememberCloseChoice() => _settings.RememberCloseChoice;
        public bool ShouldCloseToTray() => _settings.CloseToTray;
        public bool ShouldShowNotifications() => _settings.ShowNotifications;
        public bool IsDesktopContextMenuEnabled() => _settings.DesktopContextMenuEnabled;

        // App Behavior
        public bool ShouldAbortOnApplyFailure() => _settings.AbortOnApplyFailure;
        public bool ShouldRollbackAfterApplyFailure() => _settings.RollbackAfterApplyFailure;
        public bool ShouldRollbackToPreviousProfile() => _settings.RollbackToPreviousProfile;

        // Debug
        public DebugFlags Debug => _settings.DebugFlags ?? (_settings.DebugFlags = new DebugFlags());

        #endregion

        #region Generic Access

        public T GetSetting<T>(string propertyName, T defaultValue = default)
        {
            try
            {
                var property = typeof(AppSettings).GetProperty(propertyName);

                if (property != null && property.CanRead)
                {
                    var value = property.GetValue(_settings);
                    return value != null ? (T)value : defaultValue;
                }

                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error getting setting {propertyName}");
                return defaultValue;
            }
        }

        #endregion
    }
}