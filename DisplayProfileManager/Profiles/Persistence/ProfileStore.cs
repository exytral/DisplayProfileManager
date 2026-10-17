using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.Profiles.Persistence
{
    internal sealed class ProfileStore : IProfileStore
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly string _profilesFolderPath;

        public string AppDataFolder { get; }

        public ProfileStore(string appDataFolder)
        {
            if (string.IsNullOrWhiteSpace(appDataFolder)) throw new ArgumentException("Profile storage folder is required.", nameof(appDataFolder));

            AppDataFolder = appDataFolder;
            _profilesFolderPath = Path.Combine(appDataFolder, "Profiles");
            EnsureFolder();
        }

        private void EnsureFolder()
        {
            if (!Directory.Exists(_profilesFolderPath))
                Directory.CreateDirectory(_profilesFolderPath);
        }

        private string GetProfileFilePath(string profileId) => Path.Combine(_profilesFolderPath, $"{profileId}.dpm");

        public string[] EnumerateProfileFiles()
        {
            EnsureFolder();
            return [.. Directory.GetFiles(_profilesFolderPath, "*.dpm").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
        }

        public Task<string> ReadProfileTextAsync(string path) => Task.Run(() => File.ReadAllText(path));

        public async Task<bool> WriteProfileTextAsync(string profileId, string json)
        {
            EnsureFolder();
            try
            {
                await Task.Run(() => AtomicFileWriter.AtomicWrite(GetProfileFilePath(profileId), json));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving profile");
                return false;
            }
        }

        public async Task<bool> DeleteProfileAsync(string profileId)
        {
            try
            {
                var filePath = GetProfileFilePath(profileId);
                if (File.Exists(filePath))
                    await Task.Run(() => File.Delete(filePath));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting profile file");
                return false;
            }
        }

        internal static bool TryNormalizeProfileId(string profileId, out string normalizedProfileId)
        {
            if (Guid.TryParse(profileId, out Guid parsedProfileId))
            {
                normalizedProfileId = parsedProfileId.ToString("D");
                return true;
            }

            normalizedProfileId = null;
            return false;
        }

        internal static bool TryValidateManagedProfileIdentity(
            string filePath,
            Profile profile,
            ISet<string> acceptedProfileIds,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (profile == null || acceptedProfileIds == null)
            {
                failureReason = "profile identity context is missing";
                return false;
            }

            if (!TryNormalizeProfileId(profile.Id, out string normalizedProfileId))
            {
                failureReason = $"profile id '{profile.Id}' is not a GUID";
                return false;
            }

            string fileStem = Path.GetFileNameWithoutExtension(filePath);
            if (!Guid.TryParse(fileStem, out Guid fileGuid))
            {
                failureReason = $"filename stem '{fileStem}' is not a GUID";
                return false;
            }

            string normalizedFileId = fileGuid.ToString("D");
            if (!string.Equals(normalizedProfileId, normalizedFileId, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = $"profile id '{normalizedProfileId}' does not match filename id '{normalizedFileId}'";
                return false;
            }

            if (!string.Equals(fileStem, normalizedFileId, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = $"filename stem '{fileStem}' is not the canonical GUID path";
                return false;
            }

            if (!acceptedProfileIds.Add(normalizedProfileId))
            {
                failureReason = $"duplicate profile id '{normalizedProfileId}'";
                return false;
            }

            profile.Id = normalizedProfileId;
            return true;
        }
    }
}
