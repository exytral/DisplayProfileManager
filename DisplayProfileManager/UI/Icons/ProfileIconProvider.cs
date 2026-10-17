using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Profiles.Icons;

namespace DisplayProfileManager.UI.Icons
{
    public static class ProfileIconProvider
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

        public static Icon LoadIcon(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return null;
            }

            string path = ProfileIconLibrary.ResolveIconPath(filename);
            return ProfileIconLibrary.TryLoadIconPath(path, out var icon) ? icon : null;
        }

        public static ImageSource LoadImageSource(string filename, int size = 24)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return null;
            }

            string path = ProfileIconLibrary.ResolveIconPath(filename);

            if (path == null || !File.Exists(path))
            {
                return null;
            }

            try
            {
                long ticks = File.GetLastWriteTimeUtc(path).Ticks;
                string cacheKey = $"{filename}|{size}|{ticks}";

                if (_cache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }

                int frameSize = size <= 24 ? 32 : size;
                ImageSource source;
                using (var icon = new Icon(path, frameSize, frameSize))
                using (var bmp = icon.ToBitmap())
                    source = BitmapToImageSource(bmp);

                _cache[cacheKey] = source;
                return source;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to load image source for icon '{filename}' at size {size}");
                return null;
            }
        }

        private static ImageSource BitmapToImageSource(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            ms.Seek(0, SeekOrigin.Begin);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = ms;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();

            return bi;
        }
    }
}
