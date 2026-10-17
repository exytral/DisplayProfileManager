using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Threading.Tasks;
using System.Windows;

using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.UI.Themes
{
    public sealed class ThemeStore
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly string _themesFolderPath;

        public ThemeStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayProfileManager", "Themes")) { }

        internal ThemeStore(string themesFolderPath)
        {
            _themesFolderPath = themesFolderPath;
        }
        private static readonly string[] RequiredThemeKeys = ["WindowBackgroundBrush", "PrimaryTextBrush", "ContentBackgroundBrush", "BorderBrush", "ButtonBackgroundBrush", "ButtonForegroundBrush"];

        internal void EnsureThemesFolderExists()
        {
            if (!Directory.Exists(_themesFolderPath))
                Directory.CreateDirectory(_themesFolderPath);
        }

        internal void LoadThemesFromFolder(IDictionary<string, ResourceDictionary> themes)
        {
            if (!Directory.Exists(_themesFolderPath)) return;

            foreach (var file in Directory.GetFiles(_themesFolderPath, "*.xaml"))
            {
                try
                {
                    if (!TryResolveUserThemeFileName(_themesFolderPath, Path.GetFileName(file), out string name, out string validatedPath))
                    {
                        _logger.Warn($"Skipping invalid user theme filename: {file}");
                        continue;
                    }

                    var dict = ReadDeclarativeTheme(validatedPath);

                    var missingKeys = RequiredThemeKeys.Where(k => !dict.Contains(k)).ToList();
                    if (missingKeys.Any())
                    {
                        _logger.Warn($"Theme missing required keys ({string.Join(", ", missingKeys)}), skipping: {Path.GetFileName(file)}");
                        continue;
                    }

                    themes[name] = dict;
                    _logger.Info($"Loaded custom theme: {name}");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to load theme file: {file}");
                }
            }
        }

        public async Task<string> ImportThemeAsync(string sourcePath)
        {
            string fileName = Path.GetFileName(sourcePath);
            if (!TryResolveUserThemeFileName(_themesFolderPath, fileName, out string name, out string validatedPath))
            {
                _logger.Warn($"Invalid or reserved user theme filename: {fileName}");
                return null;
            }

            if (!File.Exists(sourcePath)) { return null; }

            byte[] importedBytes;
            try
            {
                importedBytes = ReadThemeBytes(sourcePath);
                var dict = ReadDeclarativeThemeBytes(importedBytes);
                var missingKeys = RequiredThemeKeys.Where(k => !dict.Contains(k)).ToList();
                if (missingKeys.Any())
                {
                    _logger.Warn($"Theme file missing required keys: {string.Join(", ", missingKeys)}");
                    return null;
                }
            }
            catch
            {
                _logger.Warn($"Theme file failed to load as ResourceDictionary: {sourcePath}");
                return null;
            }

            if (string.Equals(Path.GetDirectoryName(sourcePath), _themesFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            EnsureThemesFolderExists();
            string destPath = validatedPath;
            int counter = 1;
            while (File.Exists(destPath))
            {
                string collisionName = $"{name} ({counter++})";
                if (!TryResolveUserThemePath(_themesFolderPath, collisionName, out destPath))
                {
                    _logger.Warn($"Invalid user theme collision filename: {collisionName}");
                    return null;
                }
            }

            await Task.Run(() =>
            {
                using var destination = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                destination.Write(importedBytes);
            });
            string importedName = Path.GetFileNameWithoutExtension(destPath);
            _logger.Info($"Imported theme: {importedName}");
            return importedName;
        }

        public async Task<bool> DeleteThemeAsync(string theme)
        {
            if (!TryResolveUserThemePath(_themesFolderPath, theme, out string path) || !File.Exists(path))
            {
                _logger.Warn($"Refusing to delete invalid or missing user theme '{theme}'");
                return false;
            }

            try
            {
                await Task.Run(() => File.Delete(path));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error deleting theme: {theme}");
                return false;
            }
        }

        public bool IsUserTheme(string theme) =>
            TryResolveUserThemePath(_themesFolderPath, theme, out string path) && File.Exists(path);

        internal ResourceDictionary LoadUserTheme(string theme)
        {
            if (!TryResolveUserThemePath(_themesFolderPath, theme, out string path) || !File.Exists(path))
                return null;

            try
            {
                return ReadDeclarativeTheme(path);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Invalid declarative theme: {theme}");
                return null;
            }
        }

        private static readonly XNamespace PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
        private const int MaxThemeBytes = 131072;

        internal static ResourceDictionary ReadDeclarativeTheme(string path) =>
            ReadDeclarativeThemeBytes(ReadThemeBytes(path));

        private static byte[] ReadThemeBytes(string path)
        {
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxThemeBytes)
                    throw new InvalidDataException("Theme exceeds the size limit");

                bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
            }

            return bytes;
        }

        internal static ResourceDictionary ReadDeclarativeThemeBytes(byte[] bytes, Func<string, Color> accentPalette = null)
        {
            accentPalette ??= ResolveSystemAccentColor;
            using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxThemeBytes,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            });
            while (reader.Read())
            {
                if (reader.Depth > 3)
                    throw new InvalidDataException("Theme nesting depth exceeded");
            }
            using var validatedReader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxThemeBytes,
                MaxCharactersFromEntities = 0
            });
            var document = XDocument.Load(validatedReader, LoadOptions.None);
            XElement root = document.Root;
            if (root == null || root.Name != PresentationNs + "ResourceDictionary" || root.Attributes().Any(a => !a.IsNamespaceDeclaration))
                throw new InvalidDataException("Unsupported theme root");

            var result = new ResourceDictionary();
            if (root.Elements().Count() > 128)
                throw new InvalidDataException("Too many theme resources");

            foreach (XElement element in root.Elements())
            {
                if (element.Elements().Any() || element.HasAttributes && element.Attributes().Any(a => a.IsNamespaceDeclaration))
                    throw new InvalidDataException("Nested resources and namespace overrides are unsupported");

                string key = (string)element.Attribute(XamlNs + "Key");
                if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || result.Contains(key))
                    throw new InvalidDataException("Invalid or duplicate resource key");

                string type = element.Name.Namespace == PresentationNs ? element.Name.LocalName : "";
                string[] attributes = type switch
                {
                    "Color" => [],
                    "SolidColorBrush" => ["Color", "Opacity"],
                    "DropShadowEffect" => ["Color", "Opacity", "BlurRadius", "ShadowDepth", "Direction"],
                    _ => throw new InvalidDataException("Unsupported theme object type")
                };
                if (element.Attributes().Any(a => a.Name != XamlNs + "Key" &&
                    (a.Name.Namespace != XNamespace.None || !attributes.Contains(a.Name.LocalName))))
                    throw new InvalidDataException("Unsupported theme attribute");

                if (type != "Color" && !string.IsNullOrWhiteSpace(element.Value))
                    throw new InvalidDataException("Unexpected theme content");

                object resource;
                if (type == "Color")
                {
                    resource = ParseColor(element.Value.Trim(), result, accentPalette);
                }
                else if (type == "SolidColorBrush")
                {
                    string color = (string)element.Attribute("Color") ?? throw new InvalidDataException("Brush has no color");
                    resource = new SolidColorBrush(ParseColor(color, result, accentPalette))
                    {
                        Opacity = ParseNumber(element, "Opacity", 1, 0, 1)
                    };
                }
                else
                {
                    resource = new DropShadowEffect
                    {
                        Color = ParseColor((string)element.Attribute("Color") ?? "Black", result, accentPalette),
                        Opacity = ParseNumber(element, "Opacity", 1, 0, 1),
                        BlurRadius = ParseNumber(element, "BlurRadius", 5, 0, 100),
                        ShadowDepth = ParseNumber(element, "ShadowDepth", 0, -100, 100),
                        Direction = ParseNumber(element, "Direction", 315, -3600, 3600)
                    };
                }

                result.Add(key, resource);
            }

            if (RequiredThemeKeys.Any(k => !result.Contains(k)))
                throw new InvalidDataException("Theme is missing required resources");

            return result;
        }

        private static double ParseNumber(XElement element, string name, double fallback, double min, double max)
        {
            string text = (string)element.Attribute(name);
            if (text == null)
                return fallback;

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                !double.IsFinite(value) || value < min || value > max)
                throw new InvalidDataException("Invalid numeric theme value");

            return value;
        }

        private static Color ParseColor(string value, ResourceDictionary resources, Func<string, Color> accentPalette)
        {
            if (value.StartsWith("{StaticResource ", StringComparison.Ordinal) && value.EndsWith('}'))
            {
                string reference = value[16..^1].Trim();
                if (resources.Contains(reference) && resources[reference] is Color color)
                    return color;
                throw new InvalidDataException("Unresolved color resource");
            }

            // Only known inert system accent references are accepted, not general markup extensions.
            if (value.StartsWith("{DynamicResource {x:Static SystemColors.", StringComparison.Ordinal) && value.EndsWith("Key}}", StringComparison.Ordinal))
            {
                string name = value[40..^5];
                return name is "AccentColor" or "AccentColorDark1" or "AccentColorDark2" or
                    "AccentColorLight1" or "AccentColorLight2"
                    ? accentPalette(name)
                    : throw new InvalidDataException("Unsupported system color");
            }

            if (value.Contains('{') || value.Contains('}') || value.Length > 64 || value.Length == 0)
                throw new InvalidDataException("Unsupported color expression");

            try
            {
                object converted = ColorConverter.ConvertFromString(value);
                return converted is Color color ? color : throw new InvalidDataException("Invalid color");
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("Invalid color", ex);
            }
        }

        private static Color ResolveSystemAccentColor(string name) => name switch
        {
            "AccentColor" => SystemColors.AccentColor,
            "AccentColorDark1" => SystemColors.AccentColorDark1,
            "AccentColorDark2" => SystemColors.AccentColorDark2,
            "AccentColorLight1" => SystemColors.AccentColorLight1,
            "AccentColorLight2" => SystemColors.AccentColorLight2,
            _ => throw new InvalidDataException("Unsupported system color")
        };

        internal static bool TryResolveUserThemeFileName(string root, string fileName, out string name, out string path)
        {
            name = null;
            path = null;
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(Path.GetExtension(fileName), ".xaml", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
                {
                return false;
                }

            string stem = Path.GetFileNameWithoutExtension(fileName);
            if (!TryResolveUserThemePath(root, stem, out string candidate) ||
                !string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            name = stem;
            path = candidate;
            return true;
        }

        internal static bool TryResolveUserThemePath(string root, string theme, out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(theme) ||
                string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase) ||
                theme is "." or ".." || theme.EndsWith(' ') || theme.EndsWith('.') ||
                theme.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) >= 0 ||
                theme.Any(c => char.IsControl(c)) || Path.IsPathRooted(theme))
                {
                return false;
                }

            for (int i = 0; i < theme.Length; i++)
            {
                if (char.IsSurrogate(theme[i]) &&
                    !(char.IsHighSurrogate(theme[i]) && i + 1 < theme.Length && char.IsLowSurrogate(theme[++i])))
                {
                    return false;
                }
            }

            string stem = theme.Split('.')[0].TrimEnd(' ');
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 &&
                 (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                  stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 ("123456789\u00B9\u00B2\u00B3".Contains(stem[3]))))
                  {
                return false;
                  }

            try
            {
                string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(canonicalRoot, theme + ".xaml"));
                if (!string.Equals(Path.GetDirectoryName(candidate), canonicalRoot, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetFileName(candidate), theme + ".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                path = candidate;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }
    }
}
