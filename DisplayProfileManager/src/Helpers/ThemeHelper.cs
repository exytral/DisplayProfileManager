using DisplayProfileManager.Core;
using Microsoft.Win32;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace DisplayProfileManager.Helpers
{
    public static class ThemeHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private static readonly Dictionary<string, ResourceDictionary> _themes = new Dictionary<string, ResourceDictionary>(StringComparer.OrdinalIgnoreCase);
        private static ResourceDictionary _baseTheme;
        private static ResourceDictionary _currentColorTheme;

        private static readonly string _themesFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayProfileManager", "Themes");
        private static readonly string[] _themeOrder = { "Light", "Dark", "Black" };
        private static readonly string[] _packagedThemes = { "Light", "Dark", "Black" };
        private static readonly string[] _requiredThemeKeys = { "WindowBackgroundBrush", "PrimaryTextBrush", "ContentBackgroundBrush", "BorderBrush", "ButtonBackgroundBrush", "ButtonForegroundBrush" };

        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValueName = "AppsUseLightTheme";

        public static event EventHandler ThemeChanged;

        public static IEnumerable<string> AvailableThemes => _themeOrder.Concat(_themes.Keys.Where(k => !_themeOrder.Contains(k, StringComparer.OrdinalIgnoreCase)).OrderBy(k => k));

        static ThemeHelper()
        {
            _baseTheme = new ResourceDictionary
            {
                Source = new Uri("/DisplayProfileManager;component/src/UI/Themes/Base.xaml", UriKind.Relative)
            };
            _themes["Light"] = new ResourceDictionary
            {
                Source = new Uri("/DisplayProfileManager;component/src/UI/Themes/Light.xaml", UriKind.Relative)
            };
            _themes["Dark"] = new ResourceDictionary
            {
                Source = new Uri("/DisplayProfileManager;component/src/UI/Themes/Dark.xaml", UriKind.Relative)
            };
            _themes["Black"] = new ResourceDictionary
            {
                Source = new Uri("/DisplayProfileManager;component/src/UI/Themes/Black.xaml", UriKind.Relative)
            };
        }

        private static void EnsureThemesFolderExists()
        {
            if (!Directory.Exists(_themesFolderPath))
                Directory.CreateDirectory(_themesFolderPath);
        }

        public static void InitializeTheme()
        {
            EnsureThemesFolderExists();
            LoadThemesFromFolder();

            var appResources = Application.Current.Resources;
            if (!appResources.MergedDictionaries.Contains(_baseTheme))
                appResources.MergedDictionaries.Add(_baseTheme);

            var settings = SettingsManager.Instance.Settings;
            string theme = settings.Theme;
            if (theme != "System" && !_themes.ContainsKey(theme))
            {
                logger.Warn($"Saved theme '{theme}' not found, falling back to System");
                theme = "System";
                _ = SettingsManager.Instance.SetThemeAsync("System");
            }

            if (settings.Theme == "System")
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            SystemEvents.UserPreferenceChanged += OnAccentChanged;
            ApplyTheme(theme);
        }

        private static void LoadThemesFromFolder()
        {
            if (!Directory.Exists(_themesFolderPath)) return;

            var files = Directory.GetFiles(_themesFolderPath, "*.xaml");
            if (!files.Any()) return;

            foreach (var file in files)
            {
                try
                {
                    var dict = new ResourceDictionary { Source = new Uri(file, UriKind.Absolute) };

                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name == "System")
                    {
                        logger.Warn($"Theme name 'System' is reserved, skipping: {file}");
                        continue;
                    }

                    var missingKeys = _requiredThemeKeys.Where(k => !dict.Contains(k)).ToList();
                    if (missingKeys.Any())
                    {
                        logger.Warn($"Theme missing required keys ({string.Join(", ", missingKeys)}), skipping: {Path.GetFileName(file)}");
                        continue;
                    }

                    _themes[name] = dict;
                    logger.Info($"Loaded custom theme: {name}");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"Failed to load theme file: {file}");
                }
            }
        }

        public static void ApplyTheme(string theme)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var appResources = Application.Current.Resources;

                    if (!appResources.MergedDictionaries.Contains(_baseTheme))
                        appResources.MergedDictionaries.Add(_baseTheme);

                    if (_currentColorTheme != null && appResources.MergedDictionaries.Contains(_currentColorTheme))
                        appResources.MergedDictionaries.Remove(_currentColorTheme);

                    string resolvedTheme = string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase) ? (IsSystemUsingDarkTheme() ? "Dark" : "Light") : theme;
                    if (_themes.TryGetValue(resolvedTheme, out var dict))
                        _currentColorTheme = dict;
                    else
                    {
                        var fallback = IsSystemUsingDarkTheme() ? "Dark" : "Light";
                        logger.Warn($"Theme '{theme}' not found -> falling back to {fallback}");
                        _currentColorTheme = _themes[fallback];
                    }

                    try
                    {
                        appResources.MergedDictionaries.Add(_currentColorTheme);
                        ApplyAccentForeground();
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Theme '{resolvedTheme}' failed to merge -> falling back to Light");
                        _currentColorTheme = _themes["Light"];
                        appResources.MergedDictionaries.Add(_currentColorTheme);
                    }
                    ThemeChanged?.Invoke(null, EventArgs.Empty);
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error applying theme");
            }
        }

        public static async Task<string> ImportThemeAsync(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath)) return null;

                ResourceDictionary dict;
                try
                {
                    dict = new ResourceDictionary { Source = new Uri(sourcePath, UriKind.Absolute) };
                }
                catch
                {
                    logger.Warn($"Theme file failed to load as ResourceDictionary: {sourcePath}");
                    return null;
                }

                var missingKeys = _requiredThemeKeys.Where(k => !dict.Contains(k)).ToList();
                if (missingKeys.Any())
                {
                    logger.Warn($"Theme file missing required keys: {string.Join(", ", missingKeys)}");
                    return null;
                }

                EnsureThemesFolderExists();

                if (string.Equals(Path.GetDirectoryName(sourcePath), _themesFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileNameWithoutExtension(sourcePath);
                }

                string fileName = Path.GetFileName(sourcePath);
                string name = Path.GetFileNameWithoutExtension(fileName);

                if (name == "System")
                {
                    logger.Warn("Theme name 'System' is reserved");
                    return null;
                }

                string destPath = Path.Combine(_themesFolderPath, fileName);

                int counter = 1;
                while (File.Exists(destPath))
                {
                    destPath = Path.Combine(_themesFolderPath, $"{name} ({counter}).xaml");
                    counter++;
                }

                await Task.Run(() => File.Copy(sourcePath, destPath));

                string importedName = Path.GetFileNameWithoutExtension(destPath);
                _themes[importedName] = new ResourceDictionary { Source = new Uri(destPath, UriKind.Absolute) };

                logger.Info($"Imported theme: {importedName}");
                ApplyTheme(importedName);
                _ = SettingsManager.Instance.SetThemeAsync(importedName);
                ThemeChanged?.Invoke(null, EventArgs.Empty);

                return importedName;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error importing theme: {sourcePath}");
                return null;
            }
        }

        public static async Task<bool> DeleteThemeAsync(string theme)
        {
            if (!IsUserTheme(theme))
            {
                logger.Warn($"Refusing to delete system '{theme}' theme file");
                return false;
            }

            try
            {
                await Task.Run(() => File.Delete(Path.Combine(_themesFolderPath, theme + ".xaml")));

                _themes.Remove(theme);

                // Restore packaged theme when shadowing file is removed
                if (_packagedThemes.Contains(theme, StringComparer.OrdinalIgnoreCase))
                {
                    _themes[theme] = new ResourceDictionary
                    {
                        Source = new Uri($"/DisplayProfileManager;component/src/UI/Themes/{theme}.xaml", UriKind.Relative)
                    };
                }

                LoadThemesFromFolder();

                var target = _themes.ContainsKey(theme) ? theme : "System";
                await SettingsManager.Instance.SetThemeAsync(target);
                ApplyTheme(target);

                logger.Info($"Deleted theme: {theme}");
                ThemeChanged?.Invoke(null, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error deleting theme: {theme}");
                return false;
            }
        }

        private static void ApplyAccentForeground()
        {
            try
            {
                var accent = SystemColors.AccentColor;
                double luma = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255.0;
                var foreground = luma > 0.55 ? Colors.Black : Colors.White;
                Application.Current.Resources["ButtonForegroundBrush"] = new SolidColorBrush(foreground);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Could not derive accent foreground -> leaving theme value");
            }
        }

        public static void RefreshThemes()
        {
            var userKeys = _themes.Keys.Except(_packagedThemes, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var key in userKeys)
                _themes.Remove(key);

            foreach (var name in _themeOrder)
            {
                _themes[name] = new ResourceDictionary
                {
                    Source = new Uri($"/DisplayProfileManager;component/src/UI/Themes/{name}.xaml", UriKind.Relative)
                };
            }

            LoadThemesFromFolder();
            ApplyTheme(SettingsManager.Instance.Settings.Theme);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void RefreshSystemThemes()
        {
            foreach (var name in _themeOrder)
            {
                _themes[name] = new ResourceDictionary
                {
                    Source = new Uri($"/DisplayProfileManager;component/src/UI/Themes/{name}.xaml", UriKind.Relative)
                };
            }

            ApplyTheme(SettingsManager.Instance.Settings.Theme);
        }

        public static bool ThemeExists(string theme)
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

        public static bool IsSystemUsingDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(RegistryValueName);
                        if (value != null)
                        {
                            return (int)value == 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error reading system theme");
            }

            return false;
        }

        public static bool IsUserTheme(string theme)
        {
            if (string.IsNullOrWhiteSpace(theme) || theme == "System")
            {
                return false;
            }

            return File.Exists(Path.Combine(_themesFolderPath, theme + ".xaml"));
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                if (SettingsManager.Instance.Settings.Theme == "System")
                    ApplyTheme("System");
            }
        }

        private static void OnAccentChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.Color && e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.VisualStyle) return;

            RefreshSystemThemes();
        }

        public static void UpdateThemeSubscription(string theme)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (theme == "System")
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public static void Cleanup()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.UserPreferenceChanged -= OnAccentChanged;
        }
    }
}