using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Wallpapers.Interop;

namespace DisplayProfileManager.Wallpapers
{
    internal static class SpotlightProvider
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private const string SpotlightBuiltInMarker = @"MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images";
        private const string SpotlightIrisMarker = @"MicrosoftWindows.Client.CBS_cw5n1h2txyewy\LocalCache\Microsoft\IrisService";
        private const string SpotlightPackageName = "MicrosoftWindows.Client.CBS_cw5n1h2txyewy";
        internal static string SpotlightIrisRoot => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages",
            SpotlightPackageName,
            "LocalCache",
            "Microsoft",
            "IrisService");
        internal static string SpotlightBuiltInRoot => System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("WINDIR") ?? string.Empty,
            "SystemApps",
            SpotlightPackageName,
            "DesktopSpotlight",
            "Assets",
            "Images");

        internal static string GetCurrentSpotlightWallpaperPath()
        {
            var path = WindowsWallpaperPlatform.HkcuString(WindowsWallpaperPlatform.ControlPanelDesktopSubkey, WindowsWallpaperPlatform.WallpaperValue);
            return IsSpotlightProviderPath(path) && System.IO.File.Exists(path) ? path : null;
        }

        internal static bool IsSpotlightProviderPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.IndexOf(SpotlightBuiltInMarker, StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf(SpotlightIrisMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        internal static bool IsPathUnderRoot(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            try
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                string fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsVerifiedSpotlightProviderPath(string path) =>
            IsPathUnderRoot(path, SpotlightIrisRoot) || IsPathUnderRoot(path, SpotlightBuiltInRoot);

        internal static string SelectSpotlightRepaintPath(
            IEnumerable<string> currentPaths,
            IEnumerable<string> irisCandidates,
            IEnumerable<string> builtInCandidates,
            Func<string, bool> isUsableImage)
        {
            return SelectSpotlightRepaintPath(currentPaths, irisCandidates, builtInCandidates, isUsableImage, isUsableImage);
        }

        internal static string SelectSpotlightRepaintPath(
            IEnumerable<string> currentPaths,
            IEnumerable<string> irisCandidates,
            IEnumerable<string> builtInCandidates,
            Func<string, bool> isUsableImage,
            Func<string, bool> isLandscapeImage)
        {
            if (isUsableImage == null || isLandscapeImage == null)
            {
                return null;
            }

            foreach (string path in currentPaths ?? [])
            {
                if (!string.IsNullOrWhiteSpace(path) && IsVerifiedSpotlightProviderPath(path) && isUsableImage(path))
                {
                    return path;
                }
            }

            foreach (var source in new[] { irisCandidates, builtInCandidates })
            {
                foreach (string path in source ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(path) && IsVerifiedSpotlightProviderPath(path) && isLandscapeImage(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }
        internal static bool IsUsableSpotlightProviderImage(string path)
        {
            if (!IsVerifiedSpotlightProviderPath(path) || !System.IO.File.Exists(path))
            {
                return false;
            }

            try
            {
                using var stream = System.IO.File.OpenRead(path);
                var frame = BitmapFrame.Create(
                    stream,
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);
                return frame.PixelWidth > 0 && frame.PixelHeight > 0;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Spotlight provider image is not usable: {path}");
                return false;
            }
        }

        internal static bool IsLandscapeImageDimensions(int pixelWidth, int pixelHeight) => pixelWidth > 0 && pixelHeight > 0 && pixelWidth >= pixelHeight;

        internal static bool IsLandscapeSpotlightProviderImage(string path)
        {
            if (!IsVerifiedSpotlightProviderPath(path) || !System.IO.File.Exists(path))
            {
                return false;
            }

            try
            {
                using var stream = System.IO.File.OpenRead(path);
                var frame = BitmapFrame.Create(
                    stream,
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);
                return IsLandscapeImageDimensions(frame.PixelWidth, frame.PixelHeight);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Spotlight provider image is not a usable landscape image: {path}");
                return false;
            }
        }

        internal static List<string> EnumerateSpotlightProviderImages(string root, bool newestFirst)
        {
            if (string.IsNullOrWhiteSpace(root) || !System.IO.Directory.Exists(root))
            {
                return [];
            }

            try
            {
                var files = new System.IO.DirectoryInfo(root)
                    .EnumerateFiles("*", System.IO.SearchOption.AllDirectories)
                    .Where(file => IsSpotlightProviderPath(file.FullName));

                files = newestFirst
                    ? files.OrderByDescending(file => file.LastWriteTimeUtc).ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    : files.OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase);

                return [.. files.Select(file => file.FullName)];
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Could not enumerate Spotlight provider images under {root}");
                return [];
            }
        }
    }
}
