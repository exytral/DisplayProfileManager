using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DisplayProfileManager.Application
{
    public interface IThemeService
    {
        event EventHandler ThemeChanged;
        IEnumerable<string> AvailableThemes { get; }
        void InitializeTheme();
        void ApplyTheme(string theme);
        Task<string> ImportThemeAsync(string sourcePath);
        Task<bool> DeleteThemeAsync(string theme);
        void RefreshThemes();
        void RefreshSystemThemes();
        bool ThemeExists(string theme);
        bool IsSystemUsingDarkTheme();
        bool IsUserTheme(string theme);
        void UpdateThemeSubscription(string theme);
        void Cleanup();
    }
}
