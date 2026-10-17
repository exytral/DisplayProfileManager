using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;

using NLog;

using DisplayProfileManager.Application;
using DisplayProfileManager.Common;
using DisplayProfileManager.Settings;

namespace DisplayProfileManager.UI.Themes
{
    public sealed class ThemeService : IThemeService
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly Dictionary<string, ResourceDictionary> _themes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ResourceDictionary _baseTheme;
        private ResourceDictionary _currentColorTheme;
        private static readonly string[] _themeOrder = ["Light", "Dark", "Black"];
        private static readonly string[] _packagedThemes = ["Light", "Dark", "Black"];
        private readonly ThemeStore _store = new();
        private readonly SettingsService _settingsService;
        private readonly SystemThemeProvider _systemThemeProvider = new();
        private int _accentRefreshQueued;

        public event EventHandler ThemeChanged;
        public IEnumerable<string> AvailableThemes => _themeOrder.Concat(_themes.Keys.Where(k => !_themeOrder.Contains(k, StringComparer.OrdinalIgnoreCase)).OrderBy(k => k));

        internal ThemeService(SettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _baseTheme = new ResourceDictionary { Source = new Uri("/DisplayProfileManager;component/UI/Themes/Base.xaml", UriKind.Relative) };
            _themes["Light"] = new ResourceDictionary { Source = new Uri("/DisplayProfileManager;component/UI/Themes/Light.xaml", UriKind.Relative) };
            _themes["Dark"] = new ResourceDictionary { Source = new Uri("/DisplayProfileManager;component/UI/Themes/Dark.xaml", UriKind.Relative) };
            _themes["Black"] = new ResourceDictionary { Source = new Uri("/DisplayProfileManager;component/UI/Themes/Black.xaml", UriKind.Relative) };
            _systemThemeProvider.SystemThemeChanged += (_, __) => ApplyTheme("System");
            _systemThemeProvider.AccentChanged += (_, __) => RefreshSystemThemes();
        }

        public void InitializeTheme()
        {
            _store.EnsureThemesFolderExists();
            _store.LoadThemesFromFolder(_themes);

            var appResources = System.Windows.Application.Current.Resources;
            if (!appResources.MergedDictionaries.Contains(_baseTheme))
                appResources.MergedDictionaries.Add(_baseTheme);

            var settings = _settingsService.Settings;
            string theme = settings.Theme;
            if (theme != "System" && !_themes.ContainsKey(theme))
            {
                _logger.Warn($"Saved theme '{theme}' not found, falling back to System");
                theme = "System";
                _ = _settingsService.SetThemeAsync("System");
            }

            if (settings.Theme == "System")
                _systemThemeProvider.SetSystemThemeTracking(true);

            ApplyTheme(theme);
        }

        public void ApplyTheme(string theme)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var appResources = System.Windows.Application.Current.Resources;

                    if (!appResources.MergedDictionaries.Contains(_baseTheme))
                        appResources.MergedDictionaries.Add(_baseTheme);

                    if (_currentColorTheme != null && appResources.MergedDictionaries.Contains(_currentColorTheme))
                        appResources.MergedDictionaries.Remove(_currentColorTheme);

                    string resolvedTheme = string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase) ? (_systemThemeProvider.IsSystemUsingDarkTheme() ? "Dark" : "Light") : theme;
                    bool useAutomaticForeground = _packagedThemes.Contains(resolvedTheme, StringComparer.OrdinalIgnoreCase) && !_store.IsUserTheme(resolvedTheme);
                    if (_themes.TryGetValue(resolvedTheme, out var dict))
                        _currentColorTheme = dict;
                    else
                    {
                        var fallback = _systemThemeProvider.IsSystemUsingDarkTheme() ? "Dark" : "Light";
                        _logger.Warn($"Theme '{theme}' not found -> falling back to {fallback}");
                        _currentColorTheme = _themes[fallback];
                    }

                    try
                    {
                        appResources.MergedDictionaries.Add(_currentColorTheme);
                        ApplyButtonForegroundOwnership(appResources, useAutomaticForeground, SystemColors.AccentColor);
                        ApplyDetailsBorderBrushOwnership(appResources, _currentColorTheme);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Theme '{resolvedTheme}' failed to merge -> falling back to Light");
                        _currentColorTheme = _themes["Light"];
                        appResources.MergedDictionaries.Add(_currentColorTheme);
                        ApplyButtonForegroundOwnership(appResources, !_store.IsUserTheme("Light"), SystemColors.AccentColor);
                        ApplyDetailsBorderBrushOwnership(appResources, _currentColorTheme);
                    }
                    ThemeChanged?.Invoke(null, EventArgs.Empty);
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying theme");
            }
        }

        public async Task<string> ImportThemeAsync(string sourcePath)
        {
            try
            {
                string importedName = await _store.ImportThemeAsync(sourcePath);
                if (importedName == null)
                {
                    return null;
                }

                var dict = _store.LoadUserTheme(importedName);
                if (dict != null)
                    _themes[importedName] = dict;

                ApplyTheme(importedName);
                _ = _settingsService.SetThemeAsync(importedName);
                ThemeChanged?.Invoke(null, EventArgs.Empty);
                return importedName;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error importing theme: {sourcePath}");
                return null;
            }
        }

        public async Task<bool> DeleteThemeAsync(string theme)
        {
            if (!await _store.DeleteThemeAsync(theme))
            {
                return false;
            }

            _themes.Remove(theme);
            if (_packagedThemes.Contains(theme, StringComparer.OrdinalIgnoreCase))
                _themes[theme] = new ResourceDictionary { Source = new Uri($"/DisplayProfileManager;component/UI/Themes/{theme}.xaml", UriKind.Relative) };

            _store.LoadThemesFromFolder(_themes);
            var target = _themes.ContainsKey(theme) ? theme : "System";
            await _settingsService.SetThemeAsync(target);
            ApplyTheme(target);
            _logger.Info($"Deleted theme: {theme}");
            ThemeChanged?.Invoke(null, EventArgs.Empty);
            return true;
        }

        internal const string DetailsBorderBrushKey = "DetailsBorderBrush";

        internal static void ApplyDetailsBorderBrushOwnership(ResourceDictionary appResources, ResourceDictionary colorTheme)
        {
            appResources.Remove(DetailsBorderBrushKey);
            if (colorTheme == null) return;

            string sourceKey = colorTheme.Contains("TertiaryTextBrush") ? "TertiaryTextBrush" : "BorderBrush";
            if (colorTheme.Contains(sourceKey))
                appResources[DetailsBorderBrushKey] = colorTheme[sourceKey];
        }

        internal static void ApplyButtonForegroundOwnership(ResourceDictionary appResources, bool useAutomaticForeground, Color accent)
        {
            appResources.Remove("ButtonForegroundBrush");
            if (!useAutomaticForeground) return;

            try
            {
                double luma = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255.0;
                var foreground = luma > 0.55 ? Colors.Black : Colors.White;
                appResources["ButtonForegroundBrush"] = new SolidColorBrush(foreground);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not derive accent foreground -> leaving theme value");
            }
        }

        public void RefreshThemes()
        {
            var userKeys = _themes.Keys.Except(_packagedThemes, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var key in userKeys)
                _themes.Remove(key);

            foreach (var name in _themeOrder)
            {
                _themes[name] = new ResourceDictionary
                {
                    Source = new Uri($"/DisplayProfileManager;component/UI/Themes/{name}.xaml", UriKind.Relative)
                };
            }

            _store.LoadThemesFromFolder(_themes);
            ApplyTheme(_settingsService.Settings.Theme);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        public void RefreshSystemThemes()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            if (!dispatcher.CheckAccess())
            {
                if (Interlocked.Exchange(ref _accentRefreshQueued, 1) != 0)
                    return;

                try
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        Interlocked.Exchange(ref _accentRefreshQueued, 0);
                        if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                            RefreshSystemThemesOnDispatcher();
                    }));
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Exchange(ref _accentRefreshQueued, 0);
                }
                return;
            }

            RefreshSystemThemesOnDispatcher();
        }

        private void RefreshSystemThemesOnDispatcher()
        {
            foreach (var name in _themeOrder)
            {
                if (_store.IsUserTheme(name)) continue;

                _themes[name] = new ResourceDictionary
                {
                    Source = new Uri($"/DisplayProfileManager;component/UI/Themes/{name}.xaml", UriKind.Relative)
                };
            }

            string selected = _settingsService.Settings.Theme;
            if (_store.IsUserTheme(selected))
            {
                var refreshed = _store.LoadUserTheme(selected);
                if (refreshed != null)
                    _themes[selected] = refreshed;
                else
                    _logger.Warn($"Could not refresh custom theme '{selected}' for accent change; retaining previous resources");
            }

            ApplyTheme(selected);
        }

        public bool ThemeExists(string theme)
        {
            if (string.IsNullOrWhiteSpace(theme))
            {
                return false;
            }
            if (string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _themes.Keys.Contains(theme, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsSystemUsingDarkTheme() => _systemThemeProvider.IsSystemUsingDarkTheme();
        public bool IsUserTheme(string theme) => _store.IsUserTheme(theme);

        public void UpdateThemeSubscription(string theme) => _systemThemeProvider.SetSystemThemeTracking(theme == "System");

        public void Cleanup()
        {
            _systemThemeProvider.SetSystemThemeTracking(false);
            _systemThemeProvider.Dispose();
        }
    }
}
