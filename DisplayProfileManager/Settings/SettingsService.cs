using System;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.Settings.Persistence;

namespace DisplayProfileManager.Settings
{
    internal sealed class SettingsService(ISettingsStore store)
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private readonly ISettingsStore _store = store ?? throw new ArgumentNullException(nameof(store));
        private readonly SemaphoreSlim _mutationLock = new(1, 1);
        private AppSettings _settings = new();
        private bool _settingsLoaded;

        public event EventHandler<AppSettings> SettingsChanged;

        public AppSettings Settings => _settings;
        public bool SettingsLoaded => _settingsLoaded;
        public string SettingsFilePath => _store.SettingsFilePath;
        public string AppDataFolder => _store.AppDataFolder;

        public async Task<bool> LoadSettingsAsync()
        {
            _settingsLoaded = false;
            try
            {
                _settings = await _store.LoadAsync() ?? new AppSettings();
                _settingsLoaded = true;
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
            if (!_settingsLoaded)
            {
                _logger.Warn("Refusing to save settings — no successful load (in-memory copy is blank defaults)");
                return false;
            }

            await _mutationLock.WaitAsync();
            try
            {
                var proposed = Clone(_settings);
                proposed.LastUpdated = DateTime.Now;
                if (!await _store.SaveAsync(proposed))
                {
                    return false;
                }

                _settings = proposed;
                NotifySettingsChanged(_settings);
                return true;
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        internal async Task<bool> PersistMutationAsync(Action<AppSettings> mutate)
        {
            if (mutate == null) throw new ArgumentNullException(nameof(mutate));
            if (!_settingsLoaded)
            {
                _logger.Warn("Refusing to update settings — no successful load");
                return false;
            }

            await _mutationLock.WaitAsync();
            try
            {
                var proposed = Clone(_settings);
                mutate(proposed);
                proposed.LastUpdated = DateTime.Now;

                if (!await _store.SaveAsync(proposed))
                {
                    return false;
                }

                _settings = proposed;
                NotifySettingsChanged(_settings);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating settings");
                return false;
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        private void NotifySettingsChanged(AppSettings settings)
        {
            var handlers = SettingsChanged;
            if (handlers == null) return;

            foreach (EventHandler<AppSettings> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, settings);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "SettingsChanged subscriber failed after settings were persisted");
                }
            }
        }

        private static AppSettings Clone(AppSettings source) => JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(source)) ?? new AppSettings();

        public Task<bool> CompleteFirstRunAsync() => PersistMutationAsync(s => s.FirstRun = false);
        public Task<bool> SetThemeAsync(string theme) => PersistMutationAsync(s => s.Theme = theme);
        public Task<bool> SetCurrentProfileIdAsync(string profileId) => PersistMutationAsync(s => s.CurrentProfileId = profileId ?? string.Empty);
        public Task<bool> SetDefaultProfileIdAsync(string profileId) => PersistMutationAsync(s => s.DefaultProfileId = profileId ?? string.Empty);
        public Task<bool> SetStartWithWindowsStateOnlyAsync(bool value) => PersistMutationAsync(s => s.StartWithWindows = value);
        public Task<bool> SetCheckForUpdatesAsync(bool value) => PersistMutationAsync(s => s.CheckForUpdates = value);
        public Task<bool> SetCloseToTrayAsync(bool value) => PersistMutationAsync(s => s.CloseToTray = value);
        public Task<bool> SetRememberCloseChoiceAsync(bool value) => PersistMutationAsync(s => s.RememberCloseChoice = value);
        public Task<bool> SetNotificationsAsync(bool value) => PersistMutationAsync(s => s.ShowNotifications = value);
        public Task<bool> SetDesktopContextMenuAsync(bool value) => PersistMutationAsync(s => s.DesktopContextMenuEnabled = value);
        public Task<bool> SetManageStartMenuProfileShortcutsAsync(bool value) => PersistMutationAsync(s => s.ManageStartMenuProfileShortcuts = value);
        public Task<bool> SetAbortOnApplyFailureAsync(bool value) => PersistMutationAsync(s => s.AbortOnApplyFailure = value);
        public Task<bool> SetRollbackAfterApplyFailureAsync(bool value) => PersistMutationAsync(s => s.RollbackAfterApplyFailure = value);
        public Task<bool> SetRollbackToPreviousProfileAsync(bool value) => PersistMutationAsync(s => s.RollbackToPreviousProfile = value);
        public Task<bool> SetQuickSwitchEnabledAsync(bool value) => PersistMutationAsync(s => s.QuickSwitchEnabled = value);
        public Task<bool> SetQuickSwitchUseActiveMonitorAsync(bool value) => PersistMutationAsync(s => s.QuickSwitchUseActiveMonitor = value);
        public Task<bool> SetQuickSwitchActiveMonitorSourceAsync(QuickSwitchActiveMonitorSource value) =>
            PersistMutationAsync(s => s.QuickSwitchActiveMonitorSource =
                Enum.IsDefined(typeof(QuickSwitchActiveMonitorSource), value)
                    ? value
                    : QuickSwitchActiveMonitorSource.MousePosition);
        public Task<bool> SetQuickSwitchHotkeyAsync(HotkeyConfig value) => PersistMutationAsync(s => s.QuickSwitchHotkey = value?.Clone() ?? new HotkeyConfig());
        public Task<bool> ClearQuickSwitchHotkeyAsync() => PersistMutationAsync(s =>
        {
            s.QuickSwitchEnabled = false;
            s.QuickSwitchHotkey = new HotkeyConfig();
        });

        public Task<bool> SetStartupProfileAsync(string profileId, bool applyOnStartup) =>
            PersistMutationAsync(s =>
            {
                s.StartupProfileId = profileId;
                s.ApplyStartupProfile = applyOnStartup;
            });

        public Task<bool> SetStartInSystemTrayStateOnlyAsync(bool value) => PersistMutationAsync(s => s.StartInSystemTray = value);

        public Task<bool> SetAutoStartModeStateOnlyAsync(AutoStartMode mode) => PersistMutationAsync(s => s.AutoStartMode = mode);

        public async Task<bool> UpdateSettingAsync<T>(string propertyName, T value)
        {
            var property = typeof(AppSettings).GetProperty(propertyName);
            if (property == null || !property.CanWrite)
            {
                return false;
            }

            return await PersistMutationAsync(s => property.SetValue(s, value));
        }

        public bool IsFirstRun() => _settings.FirstRun;
        public string GetDefaultProfileId() => _settings.DefaultProfileId;
        public string GetStartupProfileId() => _settings.StartupProfileId;
        public string GetCurrentProfileId() => _settings.CurrentProfileId;
        public string GetTheme() => _settings.Theme;
        public string GetLanguage() => _settings.Language;
        public DateTime GetLastUpdated() => _settings.LastUpdated;
        public bool ShouldStartWithWindows() => _settings.StartWithWindows;
        public bool ShouldStartInSystemTray() => _settings.StartInSystemTray && _settings.StartWithWindows;
        public bool ShouldCheckForUpdates() => _settings.CheckForUpdates;
        public bool ShouldApplyStartupProfile() => _settings.ApplyStartupProfile && !string.IsNullOrEmpty(_settings.StartupProfileId);
        public bool ShouldRememberCloseChoice() => _settings.RememberCloseChoice;
        public bool ShouldCloseToTray() => _settings.CloseToTray;
        public bool ShouldShowNotifications() => _settings.ShowNotifications;
        public bool IsDesktopContextMenuEnabled() => _settings.DesktopContextMenuEnabled;
        public bool ShouldManageStartMenuProfileShortcuts() => _settings.ManageStartMenuProfileShortcuts;
        public bool ShouldAbortOnApplyFailure() => _settings.AbortOnApplyFailure;
        public bool ShouldRollbackAfterApplyFailure() => _settings.RollbackAfterApplyFailure;
        public bool ShouldRollbackToPreviousProfile() => _settings.RollbackToPreviousProfile;
        public bool IsQuickSwitchEnabled() => _settings.QuickSwitchEnabled;
        public bool ShouldQuickSwitchUseActiveMonitor() => _settings.QuickSwitchUseActiveMonitor;
        public QuickSwitchActiveMonitorSource GetQuickSwitchActiveMonitorSource() =>
            Enum.IsDefined(typeof(QuickSwitchActiveMonitorSource), _settings.QuickSwitchActiveMonitorSource)
                ? _settings.QuickSwitchActiveMonitorSource
                : QuickSwitchActiveMonitorSource.MousePosition;
        public HotkeyConfig GetQuickSwitchHotkey() => _settings.QuickSwitchHotkey?.Clone() ?? new HotkeyConfig();

        public T GetSetting<T>(string propertyName, T defaultValue = default)
        {
            try
            {
                var property = typeof(AppSettings).GetProperty(propertyName);
                if (property == null || !property.CanRead)
                {
                    return defaultValue;
                }

                var value = property.GetValue(_settings);
                return value != null ? (T)value : defaultValue;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error getting setting {propertyName}");
                return defaultValue;
            }
        }
    }
}
