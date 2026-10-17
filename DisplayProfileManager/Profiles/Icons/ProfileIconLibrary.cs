using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.Profiles.Icons
{
    public static class ProfileIconLibrary
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        public static string GetIconsFolderPath()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayProfileManager", "Icons");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            return folder;
        }

        public static string ResolveIconPath(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return null;
            }

            if (filename.Contains('/') || filename.Contains('\\') || filename.Contains(".."))
            {
                _logger.Warn($"Icon filename '{filename}' rejected — contains path traversal characters");
                return null;
            }

            return Path.Combine(GetIconsFolderPath(), filename);
        }

        public static bool TryLoadIconPath(string path, out Icon icon)
        {
            icon = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                icon = new Icon(path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Icon '{path}' is not usable");
                icon?.Dispose();
                icon = null;
                return false;
            }
        }

        public static bool IsUsableIconPath(string path)
        {
            if (!TryLoadIconPath(path, out var icon))
            {
                return false;
            }

            icon.Dispose();
            return true;
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

            _logger.Info($"Imported icon '{sourcePath}' → '{destPath}'");
            return destFilename;
        }

        public static IReadOnlyList<string> GetAvailableIcons()
        {
            try
            {
                return [.. Directory.GetFiles(GetIconsFolderPath(), "*.ico").Select(Path.GetFileName).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to enumerate icons folder");
                return [];
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
    }
}
