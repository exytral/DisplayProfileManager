using System;
using System.Collections.Generic;
using System.Linq;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.UI.Presentation
{
    public static class WallpaperPreviewProvider
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly HashSet<string> _imageExtensions = new([".jpg", ".jpeg", ".png", ".bmp", ".gif"], StringComparer.OrdinalIgnoreCase);

        public static string GetSnapshotPreviewPath(WallpaperSettings snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            if (snapshot.Mode == WallpaperMode.Picture)
            {
                return snapshot.PerMonitor.Values.Select(m => m.Path).FirstOrDefault(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p));
            }

            if (snapshot.Mode == WallpaperMode.Slideshow)
            {
                return FirstImageInSourceFolders(snapshot.SlideshowConfig?.SourcePaths);
            }

            if (snapshot.Mode == WallpaperMode.Spotlight)
            {
                return SpotlightProvider.GetCurrentSpotlightWallpaperPath();
            }

            return null;
        }

        internal static string FirstImageInSourceFolders(List<string> paths)
        {
            if (paths == null)
            {
                return null;
            }

            foreach (var folder in paths.Where(p => !string.IsNullOrEmpty(p) && System.IO.Directory.Exists(p)))
            {
                try
                {
                    var image = new System.IO.DirectoryInfo(folder)
                        .GetFiles()
                        .Where(f => _imageExtensions.Contains(f.Extension))
                        .OrderBy(f => f.Name)
                        .Select(f => f.FullName)
                        .FirstOrDefault();

                    if (image != null)
                    {
                        return image;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Could not read slideshow folder {folder}");
                }
            }

            return null;
        }
    }
}
