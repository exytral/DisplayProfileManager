using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DisplayProfileManager.Helpers
{
    public static class IconHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();
        private static readonly ConcurrentDictionary<string, ImageSource> _cache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static string GetIconsFolderPath()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayProfileManager", "Icons");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            return folder;
        }

        public static string ResolveIconPath(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;

            if (filename.Contains('/') || filename.Contains('\\') || filename.Contains(".."))
            {
                logger.Warn($"Icon filename '{filename}' rejected — contains path traversal characters");
                return null;
            }

            return Path.Combine(GetIconsFolderPath(), filename);
        }

        public static Icon LoadIcon(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;

            string path = ResolveIconPath(filename);
            if (path == null || !File.Exists(path)) return null;

            try
            {
                return new Icon(path);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to load icon '{filename}'");
                return null;
            }
        }

        public static ImageSource LoadImageSource(string filename, int size = 24)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;

            string path = ResolveIconPath(filename);
            if (path == null || !File.Exists(path)) return null;

            try
            {
                long ticks = File.GetLastWriteTimeUtc(path).Ticks;
                string cacheKey = $"{filename}|{size}|{ticks}";

                if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

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
                logger.Error(ex, $"Failed to load image source for icon '{filename}' at size {size}");
                return null;
            }
        }

        public static async Task<string> ImportIconAsync(string sourcePath)
        {
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext != ".ico") throw new InvalidOperationException($"Only .ico files are supported. Got '{ext}'.");

            string iconsFolder = GetIconsFolderPath();
            string destFilename = ResolveNameConflict(iconsFolder, Path.GetFileName(sourcePath));
            string destPath = Path.Combine(iconsFolder, destFilename);

            using (var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await src.CopyToAsync(dst);

            logger.Info($"Imported icon '{sourcePath}' → '{destPath}'");
            return destFilename;
        }

        public static IReadOnlyList<string> GetAvailableIcons()
        {
            try
            {
                return Directory.GetFiles(GetIconsFolderPath(), "*.ico").Select(Path.GetFileName).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to enumerate icons folder");
                return Array.Empty<string>();
            }
        }

        private static string ResolveNameConflict(string folder, string filename)
        {
            string candidate = filename;
            int i = 1;
            while (File.Exists(Path.Combine(folder, candidate)))
            {
                string nameNoExt = Path.GetFileNameWithoutExtension(filename);
                string ext = Path.GetExtension(filename);
                candidate = $"{nameNoExt} ({i}){ext}";
                i++;
            }

            return candidate;
        }

        private static ImageSource BitmapToImageSource(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
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
}