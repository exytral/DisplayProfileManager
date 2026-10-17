using System;

using Microsoft.Win32;
using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.UI.Themes
{
    public sealed class SystemThemeProvider : IDisposable
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValueName = "AppsUseLightTheme";
        private bool _trackSystemTheme;

        public event EventHandler SystemThemeChanged;
        public event EventHandler AccentChanged;

        public SystemThemeProvider()
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public bool IsSystemUsingDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key?.GetValue(RegistryValueName) is int value)
                {
                    return value == 0;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading system theme");
            }

            return false;
        }

        public void SetSystemThemeTracking(bool enabled) => _trackSystemTheme = enabled;

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_trackSystemTheme && e.Category == UserPreferenceCategory.General)
                SystemThemeChanged?.Invoke(this, EventArgs.Empty);

            if (e.Category == UserPreferenceCategory.Color ||
                e.Category == UserPreferenceCategory.General ||
                e.Category == UserPreferenceCategory.VisualStyle)
                AccentChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }
}
