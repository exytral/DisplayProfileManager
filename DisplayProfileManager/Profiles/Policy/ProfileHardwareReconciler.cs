using System.Collections.Generic;
using System.Linq;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Displays.Policy;
using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.Profiles.Policy
{
    internal static class ProfileHardwareReconciler
    {
        public static bool HasIncompleteHardwareInfo(DisplaySetting setting)
        {
            if (setting == null)
            {
                return false;
            }
            return setting.NativeWidth == 0
                || setting.NativeHeight == 0
                || string.IsNullOrEmpty(setting.ManufacturerName)
                || string.IsNullOrEmpty(setting.ProductCodeID);
        }

        public static bool BackfillHardwareInfoFromLive(DisplaySetting setting, DisplayConfigInfo live)
        {
            if (setting == null || live == null)
            {
                return false;
            }

            bool changed = false;
            if (setting.NativeWidth == 0 && live.NativeWidth > 0)
            {
                setting.NativeWidth = live.NativeWidth;
                changed = true;
            }
            if (setting.NativeHeight == 0 && live.NativeHeight > 0)
            {
                setting.NativeHeight = live.NativeHeight;
                changed = true;
            }
            if (string.IsNullOrEmpty(setting.ManufacturerName) && !string.IsNullOrEmpty(live.ManufacturerName))
            {
                setting.ManufacturerName = live.ManufacturerName;
                changed = true;
            }
            if (string.IsNullOrEmpty(setting.ProductCodeID) && !string.IsNullOrEmpty(live.ProductCodeID))
            {
                setting.ProductCodeID = live.ProductCodeID;
                changed = true;
            }

            return changed;
        }

        public static DisplayConfigInfo ResolveHardwareBackfillDisplay(DisplaySetting setting, List<DisplayConfigInfo> liveConfigs) => DisplayAddressResolver.ResolveLiveDisplay(setting, liveConfigs);

        private static bool? MergeDurableCapability(bool? existing, bool? observed)
        {
            if (!observed.HasValue)
            {
                return existing;
            }
            if (existing == true || observed == true)
            {
                return true;
            }
            return false;
        }

        private static bool MergeDurableCapabilityEvidence(DisplaySetting target, bool? hdrSupported, bool? wcgSupported)
        {
            bool? mergedHdr = MergeDurableCapability(target.LastKnownHdrSupported, hdrSupported);
            bool? mergedWcg = MergeDurableCapability(target.LastKnownWcgSupported, wcgSupported);
            if (target.LastKnownHdrSupported == mergedHdr && target.LastKnownWcgSupported == mergedWcg)
            {
                return false;
            }

            target.LastKnownHdrSupported = mergedHdr;
            target.LastKnownWcgSupported = mergedWcg;
            return true;
        }

        public static List<Profile> HealAdvancedColorCapabilitiesAcrossProfiles(IEnumerable<Profile> profiles, List<DisplayConfigInfo> liveConfigs)
        {
            var changedProfiles = new List<Profile>();

            if (liveConfigs == null || liveConfigs.Count == 0)
            {
                return changedProfiles;
            }

            foreach (var profile in profiles ?? [])
            {
                if (profile?.DisplaySettings == null) continue;

                bool profileChanged = false;
                foreach (var setting in profile.DisplaySettings)
                {
                    var live = ResolveHardwareBackfillDisplay(setting, liveConfigs);
                    if (live?.IsAdvancedColorInfoAvailable != true) continue;

                    if (MergeDurableCapabilityEvidence(setting, live.IsHdrSupported, live.IsWcgSupported))
                        profileChanged = true;
                }

                if (profileChanged)
                    changedProfiles.Add(profile);
            }

            return changedProfiles;
        }

        private static DisplaySetting ResolveSavedDisplaySetting(DisplaySetting source, IReadOnlyList<DisplaySetting> candidates)
        {
            if (source == null || candidates == null || candidates.Count == 0)
            {
                return null;
            }

            if (source.HasEdidIdentity)
            {
                var edidMatches = candidates
                    .Where(candidate =>
                        candidate != null &&
                        candidate.HasEdidIdentity &&
                        string.Equals(candidate.ManufacturerName, source.ManufacturerName, System.StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(candidate.ProductCodeID, source.ProductCodeID, System.StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (edidMatches.Count == 1)
                {
                    return edidMatches[0];
                }

                if (edidMatches.Count > 1)
                {
                    return null;
                }
            }

            uint sourceTarget = source.TargetId & 0xFFFFu;
            var targetMatches = candidates
                .Where(candidate => candidate != null && (candidate.TargetId & 0xFFFFu) == sourceTarget)
                .ToList();

            return targetMatches.Count == 1 ? targetMatches[0] : null;
        }

        private static bool TryBuildSavedDisplayAssociations(
            IReadOnlyList<DisplaySetting> sources,
            IReadOnlyList<DisplaySetting> candidates,
            out List<(DisplaySetting Source, DisplaySetting Candidate)> associations)
        {
            associations = [];
            var claimedCandidates = new HashSet<DisplaySetting>();

            foreach (var source in sources ?? [])
            {
                var candidate = ResolveSavedDisplaySetting(source, candidates);
                if (candidate == null) continue;

                if (!claimedCandidates.Add(candidate))
                {
                    associations.Clear();
                    return false;
                }

                associations.Add((source, candidate));
            }

            return true;
        }

        internal static bool DisplayDomainEquals(IReadOnlyList<DisplaySetting> left, IReadOnlyList<DisplaySetting> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }
            if (!TryBuildDisplayEqualityAssociations(left, right, out var associations))
            {
                return false;
            }
            if (!associations.All(pair => DisplayIntentEquals(pair.Source, pair.Candidate)))
            {
                return false;
            }

            var leftToRightCloneGroups = new Dictionary<string, string>(System.StringComparer.Ordinal);
            var rightToLeftCloneGroups = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (var (source, candidate) in associations)
            {
                bool sourceCloned = !string.IsNullOrEmpty(source.CloneGroupId);
                bool candidateCloned = !string.IsNullOrEmpty(candidate.CloneGroupId);
                if (sourceCloned != candidateCloned)
                {
                    return false;
                }
                if (!sourceCloned) continue;

                if (leftToRightCloneGroups.TryGetValue(source.CloneGroupId, out string mappedRight) && !string.Equals(mappedRight, candidate.CloneGroupId, System.StringComparison.Ordinal))
                {
                    return false;
                }
                if (rightToLeftCloneGroups.TryGetValue(candidate.CloneGroupId, out string mappedLeft) && !string.Equals(mappedLeft, source.CloneGroupId, System.StringComparison.Ordinal))
                {
                    return false;
                }

                leftToRightCloneGroups[source.CloneGroupId] = candidate.CloneGroupId;
                rightToLeftCloneGroups[candidate.CloneGroupId] = source.CloneGroupId;
            }

            return true;
        }

        private static bool TryBuildDisplayEqualityAssociations(
            IReadOnlyList<DisplaySetting> sources,
            IReadOnlyList<DisplaySetting> candidates,
            out List<(DisplaySetting Source, DisplaySetting Candidate)> associations)
        {
            associations = [];

            foreach (var source in sources)
            {
                if (source == null)
                {
                    return false;
                }

                DisplaySetting candidate = null;
                if (source.HasEdidIdentity)
                {
                    var edidMatches = candidates
                        .Where(item =>
                            item != null &&
                            item.HasEdidIdentity &&
                            string.Equals(item.ManufacturerName, source.ManufacturerName, System.StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(item.ProductCodeID, source.ProductCodeID, System.StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (edidMatches.Count == 1)
                    {
                        candidate = edidMatches[0];
                    }
                    else if (edidMatches.Count > 1)
                    {
                        uint sourceTarget = source.TargetId & 0xFFFFu;
                        var targetMatches = edidMatches
                            .Where(item => (item.TargetId & 0xFFFFu) == sourceTarget)
                            .ToList();
                        if (targetMatches.Count != 1)
                        {
                            return false;
                        }
                        candidate = targetMatches[0];
                    }
                }

                if (candidate == null)
                {
                    uint sourceTarget = source.TargetId & 0xFFFFu;
                    var targetMatches = candidates
                        .Where(item => item != null && (item.TargetId & 0xFFFFu) == sourceTarget)
                        .ToList();
                    if (targetMatches.Count != 1)
                    {
                        return false;
                    }
                    candidate = targetMatches[0];
                }

                associations.Add((source, candidate));
            }

            var claimedCandidates = new HashSet<DisplaySetting>();
            foreach (var (_, candidate) in associations)
            {
                if (!claimedCandidates.Add(candidate))
                {
                    associations.Clear();
                    return false;
                }
            }

            return associations.Count == sources.Count;
        }

        private static bool DisplayIntentEquals(DisplaySetting left, DisplaySetting right) =>
            left.IsEnabled == right.IsEnabled &&
            left.IsPrimary == right.IsPrimary &&
            left.DisplayPositionX == right.DisplayPositionX &&
            left.DisplayPositionY == right.DisplayPositionY &&
            left.Width == right.Width &&
            left.Height == right.Height &&
            left.Frequency == right.Frequency &&
            left.Rotation == right.Rotation &&
            left.DpiScaling == right.DpiScaling &&
            left.IsHdrEnabled == right.IsHdrEnabled &&
            left.IsWcgEnabled == right.IsWcgEnabled &&
            string.Equals(left.ColorProfile, right.ColorProfile, System.StringComparison.OrdinalIgnoreCase) &&
            left.IsCloneSource == right.IsCloneSource;

        public static bool MergeCanonicalHardwareSelfHealMetadata(Profile targetProfile, Profile canonicalProfile)
        {
            if (targetProfile?.DisplaySettings == null || canonicalProfile?.DisplaySettings == null)
            {
                return true;
            }

            if (!TryBuildSavedDisplayAssociations(targetProfile.DisplaySettings, canonicalProfile.DisplaySettings, out var associations))
            {
                return false;
            }

            foreach (var (target, canonical) in associations)
            {
                if (target.NativeWidth == 0 && canonical.NativeWidth > 0)
                    target.NativeWidth = canonical.NativeWidth;
                if (target.NativeHeight == 0 && canonical.NativeHeight > 0)
                    target.NativeHeight = canonical.NativeHeight;
                if (string.IsNullOrEmpty(target.ManufacturerName) && !string.IsNullOrEmpty(canonical.ManufacturerName))
                    target.ManufacturerName = canonical.ManufacturerName;
                if (string.IsNullOrEmpty(target.ProductCodeID) && !string.IsNullOrEmpty(canonical.ProductCodeID))
                    target.ProductCodeID = canonical.ProductCodeID;

                if (canonical.LastKnownHdrSupported.HasValue)
                    target.LastKnownHdrSupported = canonical.LastKnownHdrSupported;
                if (canonical.LastKnownWcgSupported.HasValue)
                    target.LastKnownWcgSupported = canonical.LastKnownWcgSupported;
            }

            return true;
        }

        public static bool MergeHardwareSelfHealMetadata(Profile canonicalProfile, Profile metadataProfile)
        {
            if (canonicalProfile?.DisplaySettings == null || metadataProfile?.DisplaySettings == null)
            {
                return true;
            }

            if (!TryBuildSavedDisplayAssociations(canonicalProfile.DisplaySettings, metadataProfile.DisplaySettings, out var associations))
            {
                return false;
            }

            foreach (var (canonical, metadata) in associations)
            {
                if (canonical.NativeWidth == 0 && metadata.NativeWidth > 0)
                    canonical.NativeWidth = metadata.NativeWidth;
                if (canonical.NativeHeight == 0 && metadata.NativeHeight > 0)
                    canonical.NativeHeight = metadata.NativeHeight;
                if (string.IsNullOrEmpty(canonical.ManufacturerName) && !string.IsNullOrEmpty(metadata.ManufacturerName))
                    canonical.ManufacturerName = metadata.ManufacturerName;
                if (string.IsNullOrEmpty(canonical.ProductCodeID) && !string.IsNullOrEmpty(metadata.ProductCodeID))
                    canonical.ProductCodeID = metadata.ProductCodeID;

                MergeDurableCapabilityEvidence(
                    canonical,
                    metadata.LastKnownHdrSupported,
                    metadata.LastKnownWcgSupported);
            }

            return true;
        }

        public static List<Profile> BackfillHardwareInfoAcrossProfiles(
            Profile appliedProfile,
            IEnumerable<Profile> profiles,
            List<DisplayConfigInfo> liveConfigs)
        {
            var changedProfiles = new List<Profile>();

            if (appliedProfile?.DisplaySettings == null || liveConfigs == null || liveConfigs.Count == 0)
            {
                return changedProfiles;
            }
            if (!appliedProfile.DisplaySettings.Any(HasIncompleteHardwareInfo))
            {
                return changedProfiles;
            }

            var repairedTargets = new HashSet<CcdTargetKey>();
            bool appliedChanged = false;

            foreach (var setting in appliedProfile.DisplaySettings)
            {
                if (!HasIncompleteHardwareInfo(setting)) continue;
                var live = ResolveHardwareBackfillDisplay(setting, liveConfigs);
                if (live == null) continue;

                if (BackfillHardwareInfoFromLive(setting, live))
                {
                    appliedChanged = true;
                    repairedTargets.Add(CcdAddress.Target(live));
                }
            }

            if (appliedChanged) changedProfiles.Add(appliedProfile);
            if (repairedTargets.Count == 0)
            {
                return changedProfiles;
            }

            foreach (var other in (profiles ?? []).Where(p => p.Id != appliedProfile.Id))
            {
                bool otherChanged = false;
                foreach (var setting in other.DisplaySettings)
                {
                    if (!HasIncompleteHardwareInfo(setting)) continue;
                    var live = ResolveHardwareBackfillDisplay(setting, liveConfigs);
                    if (live == null || !repairedTargets.Contains(CcdAddress.Target(live))) continue;
                    if (BackfillHardwareInfoFromLive(setting, live)) otherChanged = true;
                }

                if (otherChanged) changedProfiles.Add(other);
            }

            return changedProfiles;
        }
    }
}
