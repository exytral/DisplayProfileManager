using DisplayProfileManager.Helpers;
using Newtonsoft.Json;
using NLog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DisplayProfileManager.Core
{
    public enum AutoStartMode
    {
        Registry,
        TaskScheduler
    }

    public class AppSettings
    {
        [JsonProperty("startWithWindows")]
        public bool StartWithWindows { get; set; } = false;
        [JsonProperty("startInSystemTray")]
        public bool StartInSystemTray { get; set; } = false;
        [JsonProperty("autoStartMode")]
        public AutoStartMode AutoStartMode { get; set; } = AutoStartMode.Registry;
        [JsonProperty("startupProfileId")]
        public string StartupProfileId { get; set; } = string.Empty;
        [JsonProperty("applyStartupProfile")]
        public bool ApplyStartupProfile { get; set; } = false;
        [JsonProperty("rememberCloseChoice")]
        public bool RememberCloseChoice { get; set; } = false;
        [JsonProperty("closeToTray")]
        public bool CloseToTray { get; set; } = true;
        [JsonProperty("showNotifications")]
        public bool ShowNotifications { get; set; } = true;
        [JsonProperty("theme")]
        public string Theme { get; set; } = "System";
        [JsonProperty("language")]
        public string Language { get; set; } = "en-US";
        [JsonProperty("firstRun")]
        public bool FirstRun { get; set; } = true;
        [JsonProperty("currentProfileId")]
        public string CurrentProfileId { get; set; } = string.Empty;
        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    public class SettingsManager
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();
        private static readonly object _lock = new object();

        private static SettingsManager _instance;
        private AppSettings _settings;
        private readonly string _settingsFilePath;

        private readonly string _appDataFolder;

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
                    if (File.Exists(_settingsFilePath)) File.Delete(_settingsFilePath);
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

        private static void AtomicWrite(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        #region Public Methods

        public async Task<bool> LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await Task.Run(() => File.ReadAllText(_settingsFilePath));
                    _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    _settings = new AppSettings();
                    await SaveSettingsAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error loading settings");
                _settings = new AppSettings();

                return false;
            }
        }

        public async Task<bool> SaveSettingsAsync()
        {
            try
            {
                _settings.LastUpdated = DateTime.Now;
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                await Task.Run(() => AtomicWrite(_settingsFilePath, json));

                SettingsChanged?.Invoke(this, _settings);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error saving settings");
                return false;
            }
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
                logger.Error(ex, $"Error updating setting {propertyName}");
                return false;
            }
        }

        public T GetSetting<T>(string propertyName, T defaultValue = default(T))
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
                logger.Error(ex, $"Error getting setting {propertyName}");
                return defaultValue;
            }
        }

        public async Task<bool> SetStartWithWindowsAsync(bool startWithWindows)
        {
            try
            {
                var autoStartHelper = new AutoStartHelper();
                bool taskOperationSucceeded = false;

                if (startWithWindows)
                {
                    taskOperationSucceeded = autoStartHelper.EnableAutoStart(_settings.AutoStartMode, _settings.StartInSystemTray);
                    if (!taskOperationSucceeded)
                    {
                        logger.Error("Failed to enable auto start");
                        return false;
                    }
                }
                else
                {
                    taskOperationSucceeded = autoStartHelper.DisableAutoStart();
                    if (!taskOperationSucceeded)
                    {
                        logger.Error("Failed to disable auto start");
                        return false;
                    }
                }

                _settings.StartWithWindows = startWithWindows;

                if (!startWithWindows)
                {
                    _settings.StartInSystemTray = false;
                }

                var settingsSaved = await SaveSettingsAsync();

                if (!settingsSaved)
                {
                    logger.Error("Failed to save settings after task change");
                    if (startWithWindows)
                        autoStartHelper.DisableAutoStart();
                    else
                        autoStartHelper.EnableAutoStart(_settings.AutoStartMode, _settings.StartInSystemTray);

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error setting start with Windows");
                return false;
            }
        }

        public async Task<bool> SetStartInSystemTrayAsync(bool startInSystemTray)
        {
            try
            {
                if (startInSystemTray && !_settings.StartWithWindows)
                {
                    logger.Warn("Cannot enable StartInSystemTray without StartWithWindows");
                    return false;
                }

                if (_settings.StartWithWindows)
                {
                    var autoStartHelper = new AutoStartHelper();
                    bool taskOperationSucceeded = autoStartHelper.EnableAutoStart(_settings.AutoStartMode, startInSystemTray);
                    if (!taskOperationSucceeded)
                    {
                        logger.Error("Failed to update auto start with tray setting");
                        return false;
                    }
                }

                _settings.StartInSystemTray = startInSystemTray;
                return await SaveSettingsAsync();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error setting start in system tray");
                return false;
            }
        }

        public async Task<bool> SetAutoStartModeAsync(AutoStartMode mode)
        {
            try
            {
                if (!_settings.StartWithWindows)
                {
                    logger.Warn("Cannot change auto-start mode when auto-start is disabled");
                    return false;
                }

                if (_settings.AutoStartMode == mode)
                {
                    logger.Debug($"Already using {mode} mode");
                    return true;
                }

                var autoStartHelper = new AutoStartHelper();
                autoStartHelper.DisableAutoStart();

                bool success = autoStartHelper.EnableAutoStart(mode, _settings.StartInSystemTray);

                if (success)
                {
                    _settings.AutoStartMode = mode;
                    await SaveSettingsAsync();

                    logger.Info($"Successfully switched to {mode} mode");
                    return true;
                }
                else
                {
                    logger.Error($"Failed to switch to {mode} mode, restoring previous mode");

                    autoStartHelper.EnableAutoStart(_settings.AutoStartMode, _settings.StartInSystemTray);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error setting auto-start mode");
                return false;
            }
        }

        public async Task<bool> SetStartupProfileAsync(string profileId, bool applyOnStartup)
        {
            _settings.StartupProfileId = profileId;
            _settings.ApplyStartupProfile = applyOnStartup;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetThemeAsync(string theme)
        {
            _settings.Theme = theme;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetNotificationsAsync(bool showNotifications)
        {
            _settings.ShowNotifications = showNotifications;
            return await SaveSettingsAsync();
        }


        public async Task<bool> SetRememberCloseChoiceAsync(bool rememberChoice)
        {
            _settings.RememberCloseChoice = rememberChoice;
            return await SaveSettingsAsync();
        }

        public async Task<bool> SetCloseToTrayAsync(bool closeToTray)
        {
            _settings.CloseToTray = closeToTray;
            return await SaveSettingsAsync();
        }

        public async Task<bool> CompleteFirstRunAsync()
        {
            _settings.FirstRun = false;
            return await SaveSettingsAsync();
        }

        #endregion

        #region Get Settings

        public string GetSettingsFilePath() => _settingsFilePath;
        
        public string GetAppDataFolder() => _appDataFolder;
        
        public bool IsFirstRun() => _settings.FirstRun;
        
        public bool ShouldStartWithWindows() => _settings.StartWithWindows;
        
        public bool ShouldStartInSystemTray() => _settings.StartInSystemTray && _settings.StartWithWindows;
        
        public bool ShouldApplyStartupProfile() => _settings.ApplyStartupProfile && !string.IsNullOrEmpty(_settings.StartupProfileId);
        
        public string GetStartupProfileId() => _settings.StartupProfileId;
        
        public bool ShouldRememberCloseChoice() => _settings.RememberCloseChoice;
        
        public bool ShouldCloseToTray() => _settings.CloseToTray;
        
        public bool ShouldShowNotifications() => _settings.ShowNotifications;
        
        public string GetTheme() => _settings.Theme;
        
        public string GetLanguage() => _settings.Language;
        
        public DateTime GetLastUpdated() => _settings.LastUpdated;
        
        public string GetCurrentProfileId() => _settings.CurrentProfileId;

        #endregion

        public async Task<bool> SetCurrentProfileIdAsync(string profileId)
        {
            _settings.CurrentProfileId = profileId;
            return await SaveSettingsAsync();
        }
    }
}