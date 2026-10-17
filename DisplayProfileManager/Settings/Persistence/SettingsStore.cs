using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using DisplayProfileManager.Common;
using DisplayProfileManager.Settings.Models;

namespace DisplayProfileManager.Settings.Persistence
{
    internal sealed class SettingsStore : ISettingsStore
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        public string SettingsFilePath { get; }
        public string AppDataFolder { get; }

        public SettingsStore(string appDataFolder)
        {
            if (string.IsNullOrWhiteSpace(appDataFolder)) throw new ArgumentException("Settings storage folder is required.", nameof(appDataFolder));

            AppDataFolder = appDataFolder;
            SettingsFilePath = Path.Combine(appDataFolder, "Settings.json");
            EnsureAppDataFolderExists();
            MigrateLegacySettingsFile();
        }

        private void EnsureAppDataFolderExists()
        {
            if (!Directory.Exists(AppDataFolder))
                Directory.CreateDirectory(AppDataFolder);
        }

        private void MigrateLegacySettingsFile()
        {
            try
            {
                string legacyPath = FindLegacySettingsPath(Directory.GetFiles(AppDataFolder, "settings.json"));
                MigrateLegacySettings(SettingsFilePath, legacyPath, SettingsFilePath + ".tmp",
                    File.Exists, File.ReadAllText, AtomicFileWriter.AtomicWrite);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Legacy settings migration could not complete; source files preserved");
            }
        }

        internal static void MigrateLegacySettings(
            string canonicalPath,
            string legacyPath,
            string strandedTempPath,
            Func<string, bool> exists,
            Func<string, string> readAllText,
            Action<string, string> atomicWrite)
        {
            // An existing canonical file owns precedence; migration never removes it.
            if (exists(canonicalPath))
                return;

            foreach (string source in new[] { legacyPath, strandedTempPath })
            {
                if (string.IsNullOrEmpty(source) || !exists(source))
                    continue;

                string json = readAllText(source);
                if (JToken.Parse(json).Type != JTokenType.Object)
                    throw new JsonSerializationException("Legacy settings JSON must contain an object.");

                DeserializeSettingsTolerant(json);

                atomicWrite(canonicalPath, json);
                return;
            }
        }

        internal static string FindLegacySettingsPath(IEnumerable<string> candidates)
        {
            if (candidates == null)
            {
                return null;
            }

            foreach (var candidate in candidates)
            {
                if (string.Equals(Path.GetFileName(candidate), "settings.json", StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        internal static AppSettings DeserializeSettingsTolerant(string json)
        {
            var tolerant = new JsonSerializerSettings
            {
                Error = (_, args) =>
                {
                    _logger.Warn($"Settings member '{args.ErrorContext.Path}' could not be read -> using its default");
                    args.ErrorContext.Handled = true;
                }
            };

            return JsonConvert.DeserializeObject<AppSettings>(json, tolerant) ?? new AppSettings();
        }

        public async Task<AppSettings> LoadAsync()
        {
            AtomicFileWriter.CleanupOrphanedTemps(
                Path.GetDirectoryName(SettingsFilePath),
                "Settings.json.*.tmp");

            if (!File.Exists(SettingsFilePath))
            {
                if (FindLegacySettingsPath(Directory.GetFiles(AppDataFolder, "settings.json")) != null
                    || File.Exists(SettingsFilePath + ".tmp"))
                    throw new IOException("Legacy settings remain available but migration did not complete.");

                var defaults = new AppSettings();
                if (!await SaveAsync(defaults)) throw new IOException("Could not persist default settings.");
                return defaults;
            }

            var json = await Task.Run(() => File.ReadAllText(SettingsFilePath));
            return DeserializeSettingsTolerant(json);
        }

        public async Task<bool> SaveAsync(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            await _saveLock.WaitAsync();
            try
            {
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                await Task.Run(() => AtomicFileWriter.AtomicWrite(SettingsFilePath, json));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving settings");
                return false;
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}
