using System;
using System.Windows.Input;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using DisplayProfileManager.Hotkeys.Models;

namespace DisplayProfileManager.Settings.Models
{
    public enum AutoStartMode
    {
        Registry,
        TaskScheduler
    }

    public enum QuickSwitchActiveMonitorSource
    {
        MousePosition,
        WindowFocus
    }

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
        [JsonProperty("manageStartMenuProfileShortcuts")]
        public bool ManageStartMenuProfileShortcuts { get; set; } = false;

        // App Behavior
        [JsonProperty("abortOnApplyFailure")]
        public bool AbortOnApplyFailure { get; set; } = true;
        [JsonProperty("rollbackAfterApplyFailure")]
        public bool RollbackAfterApplyFailure { get; set; } = true;
        [JsonProperty("rollbackToPreviousProfile")]
        public bool RollbackToPreviousProfile { get; set; } = true;

        // Quick Switch
        [JsonProperty("quickSwitchEnabled")]
        public bool QuickSwitchEnabled { get; set; } = false;
        [JsonProperty("quickSwitchHotkey")]
        public HotkeyConfig QuickSwitchHotkey { get; set; } = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt, true);
        [JsonProperty("quickSwitchAcrylic")]
        public bool QuickSwitchUseAcrylic { get; set; } = false;
        [JsonProperty("quickSwitchUseActiveMonitor")]
        public bool QuickSwitchUseActiveMonitor { get; set; } = true;

        private QuickSwitchActiveMonitorSource _quickSwitchActiveMonitorSource = QuickSwitchActiveMonitorSource.MousePosition;

        [JsonProperty("quickSwitchActiveMonitorSource")]
        [JsonConverter(typeof(StringEnumConverter))]
        public QuickSwitchActiveMonitorSource QuickSwitchActiveMonitorSource
        {
            get => _quickSwitchActiveMonitorSource;
            set => _quickSwitchActiveMonitorSource = Enum.IsDefined(typeof(QuickSwitchActiveMonitorSource), value) ? value : QuickSwitchActiveMonitorSource.MousePosition;
        }

    }
}
