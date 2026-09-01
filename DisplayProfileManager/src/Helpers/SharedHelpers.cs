using NLog;
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace DisplayProfileManager.Helpers
{
    public static class FileHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        public static void AtomicWrite(string path, string content)
        {
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; // Use unique temp file for each write

            try
            {
                // Flush before replacing destination
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(true);
                }

                // Retry transient file-sharing failures
                const int attempts = 5;
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Replace(tmp, path, null);
                        else
                            File.Move(tmp, path);

                        return;
                    }
                    catch (IOException) when (attempt < attempts)
                    {
                        System.Threading.Thread.Sleep(50 * attempt);
                    }
                    catch (UnauthorizedAccessException) when (attempt < attempts)
                    {
                        System.Threading.Thread.Sleep(50 * attempt);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Atomic write failed for {Path.GetFileName(path)} -> existing file is unchanged");
                throw;
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public static void CleanupOrphanedTemps(string directory, string pattern = "*.tmp")
        {
            try
            {
                if (!Directory.Exists(directory)) return;

                foreach (var file in Directory.GetFiles(directory, pattern))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) >= DateTime.UtcNow.AddMinutes(-5)) continue;

                        File.Delete(file);

                        logger.Warn($"Removed orphaned temp file: {Path.GetFileName(file)}");
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    public static class UiOpacity
    {
        public const double Blocked = 0.4;
        public const double Inactive = 0.7;
    }

    public sealed class NaturalStringComparer : IComparer<string>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string x, string y);

        public static NaturalStringComparer Instance { get; } = new NaturalStringComparer();

        public int Compare(string x, string y)
        {
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;

            try
            {
                return StrCmpLogicalW(x, y);
            }
            catch (DllNotFoundException)
            {
                return string.Compare(x, y, StringComparison.CurrentCulture);
            }
        }
    }

    public static class TextHelper
    {
        public static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

        public static string Plural(uint count, string noun) => Plural((int)count, noun);
    }

    public static class TitleBarHelper
    {
        private const double NormalHeight = 32;
        private const double MaximizedHeight = 40;

        public static void UpdateMargin(Window window, Grid titleBarGrid, RowDefinition titleBarRow)
        {
            // Maximized window loses its border, so caption needs inset and extra height
            if (titleBarGrid == null) return;

            bool maximized = window.WindowState == WindowState.Maximized;

            titleBarGrid.Margin = maximized ? new Thickness(8, 8, 6, 0) : new Thickness(0);
            UpdateHeight(window, titleBarRow, maximized ? MaximizedHeight : NormalHeight);
        }

        public static void UpdateHeight(Window window, RowDefinition titleBarRow, double height)
        {
            // CaptionHeight has to track row or draggable area stops matching what is drawn
            if (titleBarRow != null)
                titleBarRow.Height = new GridLength(height);

            var chrome = WindowChrome.GetWindowChrome(window);
            if (chrome != null)
                chrome.CaptionHeight = height;
        }
    }
}