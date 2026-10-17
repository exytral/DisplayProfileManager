using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using DisplayProfileManager.Profiles.Icons;
using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.Integration
{
    internal sealed class ProfileShortcutSpec
    {
        public string FileName { get; init; }
        public string TargetPath { get; init; }
        public string Arguments { get; init; }
        public string WorkingDirectory { get; init; }
        public string Description { get; init; }
        public string IconPath { get; init; }
    }

    internal sealed class ProfileShortcutSnapshot
    {
        public string FileName { get; init; }
        public string TargetPath { get; init; }
        public string Arguments { get; init; }
        public string WorkingDirectory { get; init; }
        public string Description { get; init; }
        public string IconPath { get; init; }
        public bool IsReadable { get; init; } = true;
    }

    internal enum ProfileShortcutOperationKind
    {
        Upsert,
        Delete
    }

    internal sealed class ProfileShortcutOperation
    {
        public ProfileShortcutOperationKind Kind { get; init; }
        public string FileName { get; init; }
        public ProfileShortcutSpec Shortcut { get; init; }
    }

    internal static class ProfileShortcutPlanner
    {
        private static readonly HashSet<string> ReservedNames = new(
            [ "CON", "CONIN$", "CONOUT$", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" ],
            StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<ProfileShortcutSpec> BuildDesiredShortcuts(
            IEnumerable<Profile> profiles,
            string executablePath,
            Func<Profile, string> iconResolver = null)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("Executable path is required.", nameof(executablePath));

            iconResolver ??= ResolveIconPath;
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var desired = new List<ProfileShortcutSpec>();

            foreach (var profile in profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                string baseName = SanitizeShortcutName(profile.Name);
                string fileName = baseName + ".lnk";
                if (!usedFileNames.Add(fileName))
                {
                    string suffix = StableSuffix(profile.Id);
                    int discriminator = 0;
                    do
                    {
                        string token = discriminator == 0 ? suffix : $"{suffix}-{discriminator}";
                        fileName = $"{TruncateBaseName(baseName, 255 - token.Length - 7)} ({token}).lnk";
                        discriminator++;
                    }
                    while (!usedFileNames.Add(fileName));
                }

                string iconPath = iconResolver(profile);
                if (!ProfileIconLibrary.IsUsableIconPath(iconPath))
                    iconPath = executablePath;

                desired.Add(new ProfileShortcutSpec
                {
                    FileName = fileName,
                    TargetPath = executablePath,
                    Arguments = $"--profile {profile.Id} --source {ApplySource.StartMenuShortcut}",
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                    Description = $"Apply '{profile.Name}' with Display Profile Manager",
                    IconPath = iconPath
                });
            }

            return desired;
        }

        public static IReadOnlyList<ProfileShortcutOperation> Plan(IEnumerable<ProfileShortcutSnapshot> current, IEnumerable<ProfileShortcutSpec> desired)
        {
            var currentByName = (current ?? []).ToDictionary(s => s.FileName, StringComparer.OrdinalIgnoreCase);
            var desiredByName = (desired ?? []).ToDictionary(s => s.FileName, StringComparer.OrdinalIgnoreCase);
            var operations = new List<ProfileShortcutOperation>();

            foreach (var existing in currentByName.Values.OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase))
            {
                if (!desiredByName.ContainsKey(existing.FileName))
                    operations.Add(new ProfileShortcutOperation { Kind = ProfileShortcutOperationKind.Delete, FileName = existing.FileName });
            }

            foreach (var wanted in desiredByName.Values.OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase))
            {
                if (!currentByName.TryGetValue(wanted.FileName, out var existing) || !Matches(existing, wanted))
                    operations.Add(new ProfileShortcutOperation { Kind = ProfileShortcutOperationKind.Upsert, FileName = wanted.FileName, Shortcut = wanted });
            }

            return operations;
        }

        internal static string SanitizeShortcutName(string name)
        {
            string value = string.IsNullOrWhiteSpace(name) ? "Profile" : name.Trim();
            value = new string(value.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray());
            value = value.TrimEnd(' ', '.');
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == "..")
                value = "Profile";
            if (IsReservedDeviceName(value))
                value = "_" + value;

            return TruncateBaseName(value, 251);
        }

        private static string TruncateBaseName(string value, int maximumLength)
        {
            if (value.Length <= maximumLength)
            {
                return value;
            }

            int end = maximumLength;
            if (end > 0 && char.IsHighSurrogate(value[end - 1]) && end < value.Length && char.IsLowSurrogate(value[end]))
                end--;

            string truncated = value[..end].TrimEnd(' ', '.');
            return string.IsNullOrWhiteSpace(truncated) ? "Profile" : truncated;
        }

        private static bool IsReservedDeviceName(string name)
        {
            string stem = name.Split('.')[0].TrimEnd(' ');
            if (ReservedNames.Contains(stem))
            {
                return true;
            }

            return stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && (stem[3] == '¹' || stem[3] == '²' || stem[3] == '³');
        }

        private static bool HasValidSurrogates(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]))
                {
                    if (++i >= value.Length || !char.IsLowSurrogate(value[i]))
                    {
                        return false;
                    }
                }
                else if (char.IsLowSurrogate(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal static string GetManagedShortcutPath(string folderPath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255
                || !fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                || fileName.Length <= 4 || fileName.EndsWith(' ') || fileName.EndsWith('.')
                || fileName == "." || fileName == ".."
                || fileName.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))
                || !HasValidSurrogates(fileName)
                || IsReservedDeviceName(fileName))
                throw new ArgumentException("Invalid managed shortcut filename.", nameof(fileName));

            string root = Path.GetFullPath(folderPath);
            string child = Path.GetFullPath(Path.Combine(root, fileName));
            if (!string.Equals(Path.GetDirectoryName(child), root, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(child), fileName, StringComparison.Ordinal))
                throw new ArgumentException("Shortcut must be a direct child of the managed folder.", nameof(fileName));

            return child;
        }
        private static bool Matches(ProfileShortcutSnapshot current, ProfileShortcutSpec desired)
        {
            if (!current.IsReadable)
            {
                return false;
            }

            return string.Equals(current.TargetPath, desired.TargetPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.Arguments, desired.Arguments, StringComparison.Ordinal)
                && string.Equals(current.WorkingDirectory, desired.WorkingDirectory, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.Description, desired.Description, StringComparison.Ordinal)
                && string.Equals(current.IconPath, desired.IconPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string StableSuffix(string profileId)
        {
            if (Guid.TryParse(profileId, out var id))
            {
                return id.ToString("N")[..8];
            }

            return unchecked((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(profileId ?? string.Empty)).ToString("x8");
        }

        private static string ResolveIconPath(Profile profile) => string.IsNullOrWhiteSpace(profile?.Icon) ? null : ProfileIconLibrary.ResolveIconPath(profile.Icon);
    }
}
