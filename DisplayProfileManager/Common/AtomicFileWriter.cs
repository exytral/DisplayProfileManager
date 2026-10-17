using System;
using System.IO;

using NLog;

namespace DisplayProfileManager.Common
{
    public static class AtomicFileWriter
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

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
                _logger.Error(ex, $"Atomic write failed for {Path.GetFileName(path)} -> existing file is unchanged");
                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch
                {
                }
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

                        _logger.Warn($"Removed orphaned temp file: {Path.GetFileName(file)}");
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
