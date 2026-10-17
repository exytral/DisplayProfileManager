using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.Profiles.Policy
{
    internal static class ProfileNamingPolicy
    {
        public const int MaxProfileNameLength = 60;
        private const string CopySuffix = " - Copy";
        private static readonly Regex MarkerPattern = new(@"(( - Copy)|( \(\d+\)))+$", RegexOptions.Compiled);

        public static bool HasProfile(IEnumerable<Profile> profiles, string name)
        {
            if (profiles == null || name == null)
            {
                return false;
            }

            foreach (var profile in profiles)
            {
                if (profile?.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }

            return false;
        }

        public static string GetDuplicateProfileName(IEnumerable<Profile> profiles, string baseName) =>
            GetUniqueProfileName(profiles, Compose(baseName, CopySuffix));

        public static string GetUniqueProfileName(IEnumerable<Profile> profiles, string baseName)
        {
            var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    if (profile?.Name != null)
                    {
                        occupied.Add(profile.Name);
                    }
                }
            }

            if (!occupied.Contains(baseName))
            {
                return baseName;
            }

            const int maxAttempts = 10000;
            for (int counter = 1; counter <= maxAttempts; counter++)
            {
                var marker = $" ({counter})";
                var candidate = Compose(baseName, marker);
                if (candidate.Length > MaxProfileNameLength ||
                    !candidate.EndsWith(marker, StringComparison.Ordinal) ||
                    string.Equals(candidate, baseName, StringComparison.OrdinalIgnoreCase))
                {
                    candidate = CompactCandidate(baseName, marker);
                }

                if (!occupied.Contains(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                $"Cannot generate a unique profile name within {maxAttempts} attempts.");
        }

        private static string CompactCandidate(string name, string marker)
        {
            var chain = MarkerChain(name);
            var root = name[..^chain.Length].TrimEnd();
            // A fully occupied marker chain has no root. Retain a visible anchor.
            if (root.Length == 0)
            {
                root = "…";
            }

            var room = MaxProfileNameLength - marker.Length;
            if (room < 1)
            {
                throw new InvalidOperationException("Profile name counter exceeds the length limit.");
            }

            if (root.Length > room)
            {
                root = SafePrefix(root, room);
            }

            return root + marker;
        }

        private static string SafePrefix(string value, int length)
        {
            if (length > 0 && length < value.Length &&
                char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
            {
                length--;
            }

            return value[..length];
        }

        internal static string Compose(string stem, string marker)
        {
            var composed = stem + marker;
            if (composed.Length <= MaxProfileNameLength)
            {
                return composed;
            }

            var existing = MarkerChain(stem);
            var root = stem[..^existing.Length];
            var tail = existing + marker;
            var room = MaxProfileNameLength - tail.Length - 1;
            if (room < 1)
            {
                return CompactCandidate(stem, marker);
            }

            return SafePrefix(root, Math.Min(room, root.Length)).TrimEnd() + "\u2026" + tail;
        }

        internal static string MarkerChain(string name)
        {
            var m = MarkerPattern.Match(name);
            return m.Success ? m.Value : string.Empty;
        }
    }
}
