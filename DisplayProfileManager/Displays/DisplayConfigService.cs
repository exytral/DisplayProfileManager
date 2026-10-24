using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays.ColorProfiles;
using DisplayProfileManager.Displays.Events;
using DisplayProfileManager.Displays.Models;
using static DisplayProfileManager.Displays.Interop.DisplayConfigNative;

namespace DisplayProfileManager.Displays
{
    public class DisplayConfigService
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        internal const int QueryDisplayConfigMaxAttempts = 3;

        internal sealed class DisplayConfigApplyResult
        {
            public bool Success { get; set; }
            public bool AdvancedColorSuccess { get; set; } = true;
            public bool ColorProfileSuccess { get; set; } = true;
        }

        private static int QueryDisplayConfigWithRetry(
            QueryDisplayConfigFlags flags,
            out uint pathCount,
            out DisplayConfigPathInfo[] paths,
            out uint modeCount,
            out DisplayConfigModeInfo[] modes)
        {
            return QueryDisplayConfigWithRetry(
                flags,
                out pathCount,
                out paths,
                out modeCount,
                out modes,
                GetDisplayConfigBufferSizes,
                QueryDisplayConfig);
        }

        internal static int QueryDisplayConfigWithRetry(
            QueryDisplayConfigFlags flags,
            out uint pathCount,
            out DisplayConfigPathInfo[] paths,
            out uint modeCount,
            out DisplayConfigModeInfo[] modes,
            GetDisplayConfigBufferSizesDelegate getBufferSizes,
            QueryDisplayConfigDelegate queryDisplayConfig)
        {
            if (getBufferSizes == null) throw new ArgumentNullException(nameof(getBufferSizes));
            if (queryDisplayConfig == null) throw new ArgumentNullException(nameof(queryDisplayConfig));

            pathCount = 0;
            modeCount = 0;
            paths = [];
            modes = [];
            int result = ErrorSuccess;

            for (int attempt = 1; attempt <= QueryDisplayConfigMaxAttempts; attempt++)
            {
                result = getBufferSizes(flags, out pathCount, out modeCount);
                if (result != ErrorSuccess)
                {
                    return result;
                }

                paths = new DisplayConfigPathInfo[pathCount];
                modes = new DisplayConfigModeInfo[modeCount];

                result = queryDisplayConfig(
                    flags,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero);

                if (result == ErrorSuccess)
                {
                    if (pathCount != paths.Length)
                        Array.Resize(ref paths, checked((int)pathCount));
                    if (modeCount != modes.Length)
                        Array.Resize(ref modes, checked((int)modeCount));
                    return ErrorSuccess;
                }

                if (result != ErrorInsufficientBuffer)
                {
                    return result;
                }

                _logger.Debug($"QueryDisplayConfig buffers became stale (attempt {attempt}/{QueryDisplayConfigMaxAttempts}); refreshing sizes");
            }

            return result;
        }

        internal static bool TryGetTargetPreferredResolution(
            LUID adapterId,
            uint targetId,
            out int width,
            out int height,
            GetTargetPreferredModeDelegate getPreferredMode = null)
        {
            var preferredMode = new DisplayConfigTargetPreferredMode();
            preferredMode.header.type = DisplayConfigDeviceInfoType.GetTargetPreferredMode;
            preferredMode.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigTargetPreferredMode));
            preferredMode.header.adapterId = adapterId;
            preferredMode.header.id = targetId;

            int result = getPreferredMode != null
                ? getPreferredMode(ref preferredMode)
                : DisplayConfigGetDeviceInfo(ref preferredMode);

            if (result != ErrorSuccess || preferredMode.width == 0 || preferredMode.height == 0)
            {
                width = 0;
                height = 0;
                return false;
            }

            width = checked((int)preferredMode.width);
            height = checked((int)preferredMode.height);
            return true;
        }

        internal static bool PopulatePreferredResolution(
            DisplayConfigInfo displayConfig,
            LUID adapterId,
            uint targetId,
            GetTargetPreferredModeDelegate getPreferredMode = null)
        {
            if (displayConfig == null) throw new ArgumentNullException(nameof(displayConfig));

            displayConfig.NativeWidth = 0;
            displayConfig.NativeHeight = 0;
            if (!TryGetTargetPreferredResolution(adapterId, targetId, out int width, out int height, getPreferredMode))
            {
                return false;
            }

            displayConfig.NativeWidth = width;
            displayConfig.NativeHeight = height;
            return true;
        }

        public static List<DisplayConfigInfo> GetDisplayConfigs() => GetDisplayConfigs(out _);

        internal static List<DisplayConfigInfo> GetDisplayConfigs(out int queryStatus)
        {
            var displays = new List<DisplayConfigInfo>();
            queryStatus = ErrorSuccess;

            try
            {
                // Preserve virtual refresh modes when supported
                var queryFlags = QueryDisplayConfigFlags.OnlyActivePaths | QueryDisplayConfigFlags.VirtualRefreshRateAware;
                if (GetDisplayConfigBufferSizes(queryFlags, out _, out _) != ErrorSuccess)
                    queryFlags = QueryDisplayConfigFlags.OnlyActivePaths;

                int result = QueryDisplayConfigWithRetry(
                    queryFlags,
                    out uint pathCount,
                    out DisplayConfigPathInfo[] paths,
                    out uint modeCount,
                    out DisplayConfigModeInfo[] modes);
                queryStatus = result;
                if (result != ErrorSuccess)
                {
                    _logger.Error($"Display configuration query failed with error: {result}");
                    return displays;
                }

                for (uint i = 0; i < pathCount; i++)
                {
                    var path = paths[i];

                    if (!path.targetInfo.targetAvailable) continue;

                    bool isActive = (path.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0;

                    if (!isActive) continue;

                    uint baseTargetId = path.targetInfo.id & 0xFFFF; // Mask clone-encoded TargetId to its base value

                    var displayConfig = new DisplayConfigInfo
                    {
                        PathIndex = i,
                        IsEnabled = isActive,
                        AdapterId = ToManaged(path.sourceInfo.adapterId),
                        SourceId = path.sourceInfo.id,
                        TargetId = baseTargetId,
                        RawTargetId = path.targetInfo.id,
                        OutputTechnology = path.targetInfo.outputTechnology,
                        SupportsDrr = (path.flags & (uint)DisplayConfigPathInfoFlags.BoostRefreshRate) != 0
                    };

                    // GDI device name (\\.\DISPLAYX)
                    var sourceName = new DisplayConfigSourceDeviceName();
                    sourceName.header.type = DisplayConfigDeviceInfoType.GetSourceName;
                    sourceName.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSourceDeviceName));
                    sourceName.header.adapterId = path.sourceInfo.adapterId;
                    sourceName.header.id = path.sourceInfo.id;

                    result = DisplayConfigGetDeviceInfo(ref sourceName);
                    if (result == ErrorSuccess)
                        displayConfig.DeviceName = sourceName.viewGdiDeviceName;

                    // Monitor friendly name
                    var targetName = new DisplayConfigTargetDeviceName();
                    targetName.header.type = DisplayConfigDeviceInfoType.GetTargetName;
                    targetName.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigTargetDeviceName));
                    targetName.header.adapterId = path.targetInfo.adapterId;
                    targetName.header.id = path.targetInfo.id;

                    result = DisplayConfigGetDeviceInfo(ref targetName);
                    if (result == ErrorSuccess)
                    {
                        displayConfig.FriendlyName = targetName.monitorFriendlyDeviceName;
                        displayConfig.ManufacturerName = DecodeEdidManufacturer(targetName.edidManufactureId);
                        displayConfig.ProductCodeID = targetName.edidProductCodeId.ToString("X4");
                        displayConfig.MonitorDevicePath = targetName.monitorDevicePath;
                    }

                    PopulatePreferredResolution(displayConfig, path.targetInfo.adapterId, path.targetInfo.id);

                    // Advanced color state (HDR/WCG)
                    AdvancedColorService.CaptureAdvancedColorState(
                        displayConfig,
                        path.targetInfo.adapterId,
                        path.targetInfo.id,
                        AdvancedColorService.IsWindows24H2OrGreater(),
                        AdvancedColorService.GetAdvancedColorInfo2,
                        AdvancedColorService.GetLegacyAdvancedColorInfo);

                    // Resolution and position from source mode
                    if (displayConfig.IsEnabled && path.sourceInfo.modeInfoIdx != DisplayconfigPathModeIdxInvalid)
                    {
                        var sourceMode = modes[path.sourceInfo.modeInfoIdx];
                        if (sourceMode.infoType == DisplayConfigModeInfoType.Source)
                        {
                            displayConfig.Width = (int)sourceMode.modeInfo.sourceMode.width;
                            displayConfig.Height = (int)sourceMode.modeInfo.sourceMode.height;
                            displayConfig.DisplayPositionX = sourceMode.modeInfo.sourceMode.position.x;
                            displayConfig.DisplayPositionY = sourceMode.modeInfo.sourceMode.position.y;
                            displayConfig.Rotation = (DisplayConfigRotation)path.targetInfo.rotation;
                        }
                    }

                    // Refresh rate from current target mode
                    if (displayConfig.IsEnabled && path.targetInfo.modeInfoIdx != DisplayconfigPathModeIdxInvalid)
                    {
                        var targetMode = modes[path.targetInfo.modeInfoIdx];
                        if (targetMode.infoType == DisplayConfigModeInfoType.Target)
                        {
                            var sig = targetMode.modeInfo.targetMode.targetVideoSignalInfo;
                            if (sig.vSyncFreq.Denominator != 0)
                                displayConfig.RefreshRate = Math.Round((double)sig.vSyncFreq.Numerator / sig.vSyncFreq.Denominator, 2);
                        }
                    }

                    displays.Add(displayConfig);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting display topology");
            }

            return displays;
        }

        internal static DisplayEventSnapshot CaptureDisplayEventSnapshot()
        {
            var activeDisplays = GetDisplayConfigs(out int activeQueryStatus);
            var allPaths = new List<DisplayEventPathState>();
            int allPathsQueryStatus = ErrorSuccess;

            try
            {
                allPathsQueryStatus = QueryDisplayConfigWithRetry(
                    QueryDisplayConfigFlags.AllPaths,
                    out uint pathCount,
                    out DisplayConfigPathInfo[] paths,
                    out uint modeCount,
                    out DisplayConfigModeInfo[] modes);

                if (allPathsQueryStatus == ErrorSuccess)
                {
                    for (uint i = 0; i < pathCount; i++)
                    {
                        var path = paths[i];
                        var adapterId = ToManaged(path.sourceInfo.adapterId);
                        var state = new DisplayEventPathState
                        {
                            PathIndex = i,
                            AdapterLuid = CcdAddress.FormatLuid(adapterId),
                            SourceId = path.sourceInfo.id,
                            TargetId = path.targetInfo.id & 0xFFFF,
                            RawTargetId = path.targetInfo.id,
                            SourceGroup = CcdAddress.Source(adapterId, path.sourceInfo.id).ToString(),
                            Active = (path.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0,
                            TargetAvailable = path.targetInfo.targetAvailable,
                            PathFlags = path.flags,
                            SourceStatusFlags = path.sourceInfo.statusFlags,
                            TargetStatusFlags = path.targetInfo.statusFlags,
                            OutputTechnology = (uint)path.targetInfo.outputTechnology
                        };

                        var sourceName = new DisplayConfigSourceDeviceName();
                        sourceName.header.type = DisplayConfigDeviceInfoType.GetSourceName;
                        sourceName.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSourceDeviceName));
                        sourceName.header.adapterId = path.sourceInfo.adapterId;
                        sourceName.header.id = path.sourceInfo.id;
                        if (DisplayConfigGetDeviceInfo(ref sourceName) == ErrorSuccess)
                            state.DeviceName = sourceName.viewGdiDeviceName;

                        var targetName = new DisplayConfigTargetDeviceName();
                        targetName.header.type = DisplayConfigDeviceInfoType.GetTargetName;
                        targetName.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigTargetDeviceName));
                        targetName.header.adapterId = path.targetInfo.adapterId;
                        targetName.header.id = path.targetInfo.id;
                        if (DisplayConfigGetDeviceInfo(ref targetName) == ErrorSuccess)
                        {
                            state.FriendlyName = targetName.monitorFriendlyDeviceName;
                            state.ManufacturerName = DecodeEdidManufacturer(targetName.edidManufactureId);
                            state.ProductCodeId = targetName.edidProductCodeId.ToString("X4");
                            state.MonitorDevicePath = targetName.monitorDevicePath;
                        }

                        allPaths.Add(state);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error capturing display-event all-path evidence");
                if (allPathsQueryStatus == ErrorSuccess)
                    allPathsQueryStatus = -1;
            }

            return CreateDisplayEventSnapshot(activeQueryStatus, allPathsQueryStatus, activeDisplays, allPaths);
        }

        internal static DisplayEventSnapshot CreateDisplayEventSnapshot(
            int activeQueryStatus,
            int allPathsQueryStatus,
            IEnumerable<DisplayConfigInfo> activeDisplays,
            IEnumerable<DisplayEventPathState> allPaths)
        {
            if (activeDisplays == null) throw new ArgumentNullException(nameof(activeDisplays));
            if (allPaths == null) throw new ArgumentNullException(nameof(allPaths));

            var active = activeDisplays
                .Select(display => new DisplayEventActiveDisplay
                {
                    AdapterLuid = CcdAddress.FormatLuid(display.AdapterId),
                    SourceId = display.SourceId,
                    TargetId = display.TargetId,
                    RawTargetId = display.RawTargetId,
                    PathIndex = display.PathIndex,
                    DeviceName = display.DeviceName,
                    FriendlyName = display.FriendlyName,
                    ManufacturerName = display.ManufacturerName,
                    ProductCodeId = display.ProductCodeID,
                    MonitorDevicePath = display.MonitorDevicePath,
                    SourceGroup = CcdAddress.Source(display.AdapterId, display.SourceId).ToString(),
                    PositionX = display.DisplayPositionX,
                    PositionY = display.DisplayPositionY,
                    Width = display.Width,
                    Height = display.Height,
                    RefreshRate = display.RefreshRate,
                    Rotation = (uint)display.Rotation,
                    OutputTechnology = (uint)display.OutputTechnology,
                    SupportsDrr = display.SupportsDrr,
                    AdvancedColorInfoAvailable = display.IsAdvancedColorInfoAvailable,
                    HdrSupported = display.IsHdrSupported,
                    HdrEnabled = display.IsHdrEnabled,
                    WcgSupported = display.IsWcgSupported,
                    WcgEnabled = display.IsWcgEnabled
                })
                .OrderBy(display => display.AdapterLuid, StringComparer.Ordinal)
                .ThenBy(display => display.SourceId)
                .ThenBy(display => display.RawTargetId)
                .ThenBy(display => display.PathIndex)
                .ToList();

            var orderedPaths = allPaths
                .OrderBy(path => path.AdapterLuid, StringComparer.Ordinal)
                .ThenBy(path => path.SourceId)
                .ThenBy(path => path.RawTargetId)
                .ThenBy(path => path.PathIndex)
                .ToList();

            var snapshot = new DisplayEventSnapshot
            {
                ActiveQueryStatus = activeQueryStatus,
                AllPathsQueryStatus = allPathsQueryStatus,
                ActiveDisplays = active,
                AllPaths = orderedPaths
            };

            string canonical = JsonSerializer.Serialize(new
            {
                snapshot.ActiveQueryStatus,
                snapshot.AllPathsQueryStatus,
                snapshot.ActiveDisplays,
                snapshot.AllPaths
            });
            snapshot.TopologyDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            return snapshot;
        }

        internal static List<DisplayConfigInfo> GetAllPathDisplayAddresses()
        {
            var displaysByTarget = new Dictionary<CcdTargetKey, DisplayConfigInfo>();

            try
            {
                int result = QueryDisplayConfigWithRetry(
                    QueryDisplayConfigFlags.AllPaths,
                    out uint pathCount,
                    out DisplayConfigPathInfo[] paths,
                    out uint modeCount,
                    out DisplayConfigModeInfo[] modes);
                if (result != ErrorSuccess)
                {
                    _logger.Error($"All-path display-address query failed with error: {result}");
                    return [.. displaysByTarget.Values];
                }

                for (uint i = 0; i < pathCount; i++)
                {
                    var path = paths[i];
                    var targetKey = CcdAddress.Target(ToManaged(path.targetInfo.adapterId), path.targetInfo.id);
                    bool isActive = (path.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0;

                    if (displaysByTarget.TryGetValue(targetKey, out var existing) && (existing.IsEnabled || !isActive)) continue;

                    var displayConfig = new DisplayConfigInfo
                    {
                        PathIndex = i,
                        IsEnabled = isActive,
                        AdapterId = ToManaged(path.targetInfo.adapterId),
                        SourceId = path.sourceInfo.id,
                        TargetId = path.targetInfo.id & 0xFFFF,
                        RawTargetId = path.targetInfo.id,
                        OutputTechnology = path.targetInfo.outputTechnology
                    };

                    var sourceName = new DisplayConfigSourceDeviceName();
                    sourceName.header.type = DisplayConfigDeviceInfoType.GetSourceName;
                    sourceName.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSourceDeviceName));
                    sourceName.header.adapterId = path.sourceInfo.adapterId;
                    sourceName.header.id = path.sourceInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref sourceName) == ErrorSuccess)
                        displayConfig.DeviceName = sourceName.viewGdiDeviceName;

                    var targetName = new DisplayConfigTargetDeviceName();
                    targetName.header.type = DisplayConfigDeviceInfoType.GetTargetName;
                    targetName.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigTargetDeviceName));
                    targetName.header.adapterId = path.targetInfo.adapterId;
                    targetName.header.id = path.targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref targetName) == ErrorSuccess)
                    {
                        displayConfig.FriendlyName = targetName.monitorFriendlyDeviceName;
                        displayConfig.ManufacturerName = DecodeEdidManufacturer(targetName.edidManufactureId);
                        displayConfig.ProductCodeID = targetName.edidProductCodeId.ToString("X4");
                        displayConfig.MonitorDevicePath = targetName.monitorDevicePath;
                    }

                    PopulatePreferredResolution(displayConfig, path.targetInfo.adapterId, path.targetInfo.id);

                    displaysByTarget[targetKey] = displayConfig;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error resolving all-path display addresses");
            }

            return [.. displaysByTarget.Values];
        }

        public static HashSet<uint> GetAllPathTargetIds()
        {
            var result = new HashSet<uint>();
            try
            {
                int ret = QueryDisplayConfigWithRetry(
                    QueryDisplayConfigFlags.AllPaths,
                    out uint pathCount,
                    out DisplayConfigPathInfo[] paths,
                    out uint modeCount,
                    out DisplayConfigModeInfo[] modes);
                if (ret != ErrorSuccess)
                {
                    return result;
                }

                foreach (var path in paths)
                    result.Add(path.targetInfo.id & 0xFFFF);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error querying all-paths target presence");
            }

            return result;
        }

        internal static HashSet<CcdTargetKey> GetAllPathTargets()
        {
            var result = new HashSet<CcdTargetKey>();
            try
            {
                int ret = QueryDisplayConfigWithRetry(
                    QueryDisplayConfigFlags.AllPaths,
                    out uint pathCount,
                    out DisplayConfigPathInfo[] paths,
                    out uint modeCount,
                    out DisplayConfigModeInfo[] modes);
                if (ret != ErrorSuccess)
                {
                    return result;
                }

                foreach (var path in paths)
                    result.Add(CcdAddress.Target(ToManaged(path.targetInfo.adapterId), path.targetInfo.id));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error querying all-paths target presence");
            }

            return result;
        }

        private static int GetLivePathIndex(DisplayConfigPathInfo[] paths, AdapterLuid adapterId, uint targetId)
        {
            var key = CcdAddress.Target(adapterId, targetId);
            int active = Array.FindIndex(paths, p => CcdAddress.Target(ToManaged(p.targetInfo.adapterId), p.targetInfo.id).Equals(key) && (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0);
            return active >= 0 ? active : Array.FindIndex(paths, p => CcdAddress.Target(ToManaged(p.targetInfo.adapterId), p.targetInfo.id).Equals(key));
        }

        internal static Dictionary<CcdSourceKey, uint> BuildSourceIdMap(List<DisplayConfigInfo> displayConfigs)
        {
            var result = new Dictionary<CcdSourceKey, uint>();
            foreach (var adapterGroup in displayConfigs.Where(d => d.IsEnabled)
                .GroupBy(d => d.AdapterId)
                .OrderBy(g => g.Key.HighPart)
                .ThenBy(g => g.Key.LowPart))
            {
                uint normalized = 0;
                foreach (uint sourceId in adapterGroup.Select(d => d.SourceId).Distinct().OrderBy(id => id))
                    result[CcdAddress.Source(adapterGroup.Key, sourceId)] = normalized++;
            }

            return result;
        }

        internal static bool TopologyRequiresUpdate(DisplayConfigPathInfo[] paths, List<DisplayConfigInfo> displayConfigs)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (displayConfigs == null) throw new ArgumentNullException(nameof(displayConfigs));

            bool needsUpdate = false;
            var profileLookup = displayConfigs.ToDictionary(CcdAddress.Target);
            var sourceIdMap = BuildSourceIdMap(displayConfigs);

            foreach (var group in paths.GroupBy(p => CcdAddress.Target(ToManaged(p.targetInfo.adapterId), p.targetInfo.id)))
            {
                var hardwareId = group.Key;
                bool isAnyPathActive = group.Any(p => (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0);

                if (profileLookup.TryGetValue(hardwareId, out var profile))
                {
                    if (isAnyPathActive != profile.IsEnabled)
                    {
                        _logger.Debug($"Found target {hardwareId}: Currently {(isAnyPathActive ? "on" : "off")} but should be {(profile.IsEnabled ? "on" : "off")}");
                        needsUpdate = true;
                    }
                    else if (isAnyPathActive && profile.IsEnabled)
                    {
                        var activePath = group.First(p => (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0);
                        uint normalizedProfileSourceId = sourceIdMap[CcdAddress.Source(profile)];
                        if (activePath.sourceInfo.id != normalizedProfileSourceId)
                        {
                            _logger.Debug($"Found target {hardwareId}: CurrentSource={activePath.sourceInfo.id} but NormalizedProfileSource={normalizedProfileSourceId}");
                            needsUpdate = true;
                        }
                    }
                }
                else if (isAnyPathActive)
                {
                    _logger.Debug($"Found target {hardwareId}: undefined in profile but currently active");
                    needsUpdate = true;
                }
            }

            return needsUpdate;
        }

        internal static void PreparePathsForTopology(DisplayConfigPathInfo[] targetPaths, List<DisplayConfigInfo> displayConfigs)
        {
            if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
            if (displayConfigs == null) throw new ArgumentNullException(nameof(displayConfigs));

            var targetIdToPathIndex = new Dictionary<CcdTargetKey, int>();
            for (int i = 0; i < targetPaths.Length; i++)
            {
                var targetKey = CcdAddress.Target(ToManaged(targetPaths[i].targetInfo.adapterId), targetPaths[i].targetInfo.id);
                bool isActive = (targetPaths[i].flags & (uint)DisplayConfigPathInfoFlags.Active) != 0;
                if (!targetIdToPathIndex.TryGetValue(targetKey, out int existingIndex) || (isActive && (targetPaths[existingIndex].flags & (uint)DisplayConfigPathInfoFlags.Active) == 0))
                {
                    targetIdToPathIndex[targetKey] = i;
                }
            }

            var targetIdToDisplay = new Dictionary<CcdTargetKey, DisplayConfigInfo>();
            var sourceIdToCloneGroup = new Dictionary<CcdSourceKey, uint>();
            uint nextCloneGroup = 0;
            foreach (var display in displayConfigs.Where(d => d.IsEnabled))
            {
                CcdTargetKey targetKey;
                if (display.AdapterId.HighPart == 0 && display.AdapterId.LowPart == 0)
                {
                    var candidates = targetIdToPathIndex.Keys
                        .Where(key => key.TargetId == (display.TargetId & 0xFFFF))
                        .ToList();
                    if (candidates.Count != 1)
                    {
                        _logger.Warn($"Skipping display topology -> TargetId {display.TargetId & 0xFFFF} has no unique adapter-qualified path");
                        continue;
                    }

                    targetKey = candidates[0];
                }
                else
                {
                    targetKey = CcdAddress.Target(display);
                }

                if (!targetIdToDisplay.TryAdd(targetKey, display))
                {
                    _logger.Warn($"Multiple enabled profile displays resolve to target {targetKey}; keeping the first topology entry.");
                    continue;
                }

                var sourceKey = CcdAddress.Source(targetKey.AdapterId, display.SourceId);
                if (!sourceIdToCloneGroup.ContainsKey(sourceKey))
                    sourceIdToCloneGroup[sourceKey] = nextCloneGroup++;
            }
            // Keep desired clone membership outside the source-info union because non-virtual paths require the whole modeInfoIdx to remain DISPLAYCONFIG_PATH_MODE_IDX_INVALID
            var desiredCloneGroupByPathIndex = new Dictionary<int, uint>();

            foreach (var kvp in targetIdToPathIndex)
            {
                var targetKey = kvp.Key;
                int pathIndex = kvp.Value;
                ref var path = ref targetPaths[pathIndex];

                // Topology-supplied calls have no mode array, so 0xFFFFFFFF is the valid unavailable target-union representation for both legacy and virtual-aware paths
                path.targetInfo.modeInfoIdx = DisplayconfigPathModeIdxInvalid;

                if (targetIdToDisplay.TryGetValue(targetKey, out var display))
                {
                    uint cloneGroup = sourceIdToCloneGroup[CcdAddress.Source(targetKey.AdapterId, display.SourceId)];
                    path.flags |= (uint)DisplayConfigPathInfoFlags.Active;

                    if ((path.flags & (uint)DisplayConfigPathInfoFlags.SupportVirtualMode) != 0)
                        path.sourceInfo.ResetModeAndSetCloneGroup(cloneGroup);
                    else
                        path.sourceInfo.modeInfoIdx = DisplayconfigPathModeIdxInvalid;

                    desiredCloneGroupByPathIndex[pathIndex] = cloneGroup;
                }
                else
                {
                    path.flags &= ~(uint)DisplayConfigPathInfoFlags.Active;
                    path.sourceInfo.modeInfoIdx = DisplayconfigPathModeIdxInvalid;
                }
            }

            // Source IDs are adapter-local; use the desired semantic group instead of union bits so non-virtual paths retain the required all-invalid modeInfoIdx
            var sourceIdTable = new Dictionary<LUID, uint>();
            var groupSourceId = new Dictionary<Tuple<LUID, uint>, uint>();
            foreach (var kvp in desiredCloneGroupByPathIndex.OrderBy(k => k.Key))
            {
                int pathIndex = kvp.Key;
                if ((targetPaths[pathIndex].flags & (uint)DisplayConfigPathInfoFlags.Active) == 0) continue;

                LUID adapterId = targetPaths[pathIndex].sourceInfo.adapterId;
                uint cloneGroup = kvp.Value;
                var key = Tuple.Create(adapterId, cloneGroup);

                if (!groupSourceId.TryGetValue(key, out uint assigned))
                {
                    if (!sourceIdTable.ContainsKey(adapterId))
                        sourceIdTable[adapterId] = 0;

                    assigned = sourceIdTable[adapterId]++;
                    groupSourceId[key] = assigned;
                }

                targetPaths[pathIndex].sourceInfo.id = assigned;
            }

            foreach (var kvp in desiredCloneGroupByPathIndex.OrderBy(k => k.Key))
            {
                int i = kvp.Key;
                if ((targetPaths[i].flags & (uint)DisplayConfigPathInfoFlags.Active) != 0)
                    _logger.Debug($"Topology path: target {targetPaths[i].targetInfo.id & 0xFFFF} source {targetPaths[i].sourceInfo.id} cloneGroup {kvp.Value} virtual={((targetPaths[i].flags & (uint)DisplayConfigPathInfoFlags.SupportVirtualMode) != 0)}");
            }
        }

        public static bool ApplyDisplayTopology(List<DisplayConfigInfo> displayConfigs, bool forceTopologyRecovery = false)
        {
            try
            {
                _logger.Info("Applying display topology...");

                // Compare without virtual mode so source IDs remain comparable
                const QueryDisplayConfigFlags compareQueryFlags = QueryDisplayConfigFlags.AllPaths;
                const QueryDisplayConfigFlags topologyQueryFlags = QueryDisplayConfigFlags.AllPaths | QueryDisplayConfigFlags.VirtualModeAware;

                int result = QueryDisplayConfigWithRetry(
                    compareQueryFlags,
                    out uint pathCount,
                    out DisplayConfigPathInfo[] paths,
                    out uint modeCount,
                    out DisplayConfigModeInfo[] modes);

                if (result != ErrorSuccess)
                {
                    _logger.Error($"Display topology query failed with error: {result}");
                    return false;
                }

                bool needsUpdate = TopologyRequiresUpdate(paths, displayConfigs);

                if (!needsUpdate)
                {
                    _logger.Info("Skipping display topology -> already matches configuration");
                    return true;
                }

                _logger.Info("Display mismatch detected -> Applying topology update");

                // Re-query with VirtualModeAware so clone-group encoding is preserved
                result = QueryDisplayConfigWithRetry(
                    topologyQueryFlags,
                    out pathCount,
                    out paths,
                    out modeCount,
                    out modes);
                if (result != ErrorSuccess)
                {
                    _logger.Error($"Display topology apply query failed with error: {result}");
                    return false;
                }

                PreparePathsForTopology(paths, displayConfigs);

                int activeCount = paths.Count(p => (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0);
                if (activeCount == 0)
                {
                    _logger.Error("No active displays to enable");
                    return false;
                }

                var topologyFlags =
                    SetDisplayConfigFlags.TopologySupplied |
                    SetDisplayConfigFlags.Apply |
                    SetDisplayConfigFlags.AllowPathOrderChanges |
                    SetDisplayConfigFlags.VirtualModeAware;

                result = SetDisplayConfig(pathCount, paths, 0, null, topologyFlags);

                // Reaching recovery normally requires blank configuration database
                if (forceTopologyRecovery && result == ErrorSuccess)
                {
                    _logger.Warn("event=EngineeringForcedTopologyRecovery");
                    result = ErrorGenFailure;
                }

                if (result == ErrorSuccess)
                {
                    _logger.Info("Applied display topology.");
                    return true;
                }

                if (result != ErrorGenFailure && result != 87)
                {
                    _logger.Error($"SetDisplayConfig failed to apply topology: Error {result}");
                    return false;
                }

                // Retry with the complete path + mode configuration.
                // The topology-only call can fail for non-virtual paths when the
                // requested topology is not yet represented in the configuration database.
                _logger.Warn("Topology database lookup failed -> retrying with full supplied configuration");

                result = QueryDisplayConfigWithRetry(
                    QueryDisplayConfigFlags.AllPaths,
                    out uint recoveryPathCount,
                    out DisplayConfigPathInfo[] recoveryPaths,
                    out uint recoveryModeCount,
                    out DisplayConfigModeInfo[] recoveryModes);

                if (result != ErrorSuccess)
                {
                    _logger.Error($"Recovery display query failed: Error {result}");
                    return false;
                }

                // Start with every path disabled, then enable the paths represented
                // by the profile using the same source-ID normalization as the normal
                // topology path. Keep QueryDisplayConfig's original modeInfoIdx values.
                for (int i = 0; i < recoveryPaths.Length; i++)
                    recoveryPaths[i].flags &= ~(uint)DisplayConfigPathInfoFlags.Active;

                var recoverySourceIdMap = BuildSourceIdMap(displayConfigs);

                foreach (var display in displayConfigs.Where(d => d.IsEnabled))
                {
                    var targetKey = CcdAddress.Target(display);
                    var profileSourceKey = CcdAddress.Source(display);

                    if (!recoverySourceIdMap.TryGetValue(profileSourceKey, out uint normalizedSourceId))
                    {
                        _logger.Error($"Recovery could not resolve source mapping for target {display.TargetId}");
                        return false;
                    }

                    int pathIndex = Array.FindIndex(
                        recoveryPaths,
                        p =>
                            CcdAddress.Target(
                                ToManaged(p.targetInfo.adapterId),
                                p.targetInfo.id
                            ).Equals(targetKey)
                            && p.sourceInfo.id == normalizedSourceId);

                    if (pathIndex < 0)
                    {
                        _logger.Error(
                            $"Recovery could not find path for target {display.TargetId}, normalized source {normalizedSourceId}");
                        return false;
                    }

                    recoveryPaths[pathIndex].flags |= (uint)DisplayConfigPathInfoFlags.Active;

                    _logger.Debug(
                        $"Full recovery path: target {recoveryPaths[pathIndex].targetInfo.id & 0xFFFF} " +
                        $"source {recoveryPaths[pathIndex].sourceInfo.id} " +
                        $"sourceModeIdx {recoveryPaths[pathIndex].sourceInfo.modeInfoIdx} " +
                        $"targetModeIdx {recoveryPaths[pathIndex].targetInfo.modeInfoIdx}");
                }

                var recoveryFlags =
                    SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    SetDisplayConfigFlags.Apply |
                    SetDisplayConfigFlags.SaveToDatabase |
                    SetDisplayConfigFlags.AllowChanges;

                result = SetDisplayConfig(
                    recoveryPathCount,
                    recoveryPaths,
                    recoveryModeCount,
                    recoveryModes,
                    recoveryFlags);

                if (result != ErrorSuccess)
                {
                    _logger.Error($"Full topology recovery failed: Error {result}");
                    return false;
                }

                // Refresh live configuration after recovery
                GetDisplayConfigs();

                _logger.Info("Applied display topology and saved to configuration database.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying topology");
                return false;
            }
        }

        public static async Task<bool> DeferDisplayLayoutAsync(List<DisplayConfigInfo> displayConfigs, int deferTimeout = 10000, bool traceEnabled = false)
        {
            var deferWatch = Stopwatch.StartNew();
            bool result = await DeferDisplayLayoutAsync(
                displayConfigs,
                deferTimeout,
                GetDisplayConfigs,
                milliseconds => Task.Delay(milliseconds),
                message => _logger.Debug(message),
                () => deferWatch.ElapsedMilliseconds,
                traceEnabled);
            deferWatch.Stop();
            return result;
        }

        internal static async Task<bool> DeferDisplayLayoutAsync(
            List<DisplayConfigInfo> displayConfigs,
            int deferTimeout,
            Func<List<DisplayConfigInfo>> liveSnapshotProvider,
            Func<int, Task> delayAsync,
            Action<string> debugLog,
            Func<long> elapsedMilliseconds,
            bool traceEnabled = false)
        {
            var expectedMonitors = displayConfigs.Where(d => d.IsEnabled).ToList();
            var verifiedTargetIds = new HashSet<CcdTargetKey>();
            int pollNumber = 0;

            _logger.Info($"Deferring configuration until {TextFormat.Plural(expectedMonitors.Count, "enabled display")} stabilize...");

            while (verifiedTargetIds.Count < expectedMonitors.Count && elapsedMilliseconds() < deferTimeout)
            {
                pollNumber++;
                var liveSnapshot = liveSnapshotProvider();
                foreach (var monitor in expectedMonitors)
                {
                    var targetKey = CcdAddress.Target(monitor);
                    if (verifiedTargetIds.Contains(targetKey)) continue;

                    var match = liveSnapshot.FirstOrDefault(l => CcdAddress.Target(l).Equals(targetKey));
                    if (match != null && match.IsEnabled)
                    {
                        verifiedTargetIds.Add(targetKey);
                        if (traceEnabled)
                        {
                            string name = !string.IsNullOrEmpty(monitor.FriendlyName) ? monitor.FriendlyName : monitor.DeviceName;
                            debugLog($"{name} ({targetKey}) is active at {elapsedMilliseconds()}ms");
                        }
                    }
                }

                if (traceEnabled)
                {
                    var expectedTargets = expectedMonitors.Select(CcdAddress.Target).ToHashSet();
                    int liveTargetCount = liveSnapshot
                        .Select(CcdAddress.Target)
                        .Where(expectedTargets.Contains)
                        .Distinct()
                        .Count();
                    var pending = expectedMonitors
                        .Where(monitor => !verifiedTargetIds.Contains(CcdAddress.Target(monitor)))
                        .Select(monitor =>
                        {
                            var targetKey = CcdAddress.Target(monitor);
                            var match = liveSnapshot.FirstOrDefault(l => CcdAddress.Target(l).Equals(targetKey));
                            string name = !string.IsNullOrEmpty(monitor.FriendlyName) ? monitor.FriendlyName : monitor.DeviceName;
                            string state = match == null ? "absent" : "present-disabled";
                            return $"{targetKey} ({name}):{state}";
                        })
                        .ToArray();
                    debugLog(
                        $"Display defer poll #{pollNumber} at {elapsedMilliseconds()}ms: " +
                        $"expected={expectedMonitors.Count} live={liveTargetCount} ready={verifiedTargetIds.Count} " +
                        $"pending=[{string.Join(", ", pending)}]");
                }

                if (verifiedTargetIds.Count < expectedMonitors.Count)
                    await delayAsync(250);
            }

            long elapsed = elapsedMilliseconds();
            if (verifiedTargetIds.Count == expectedMonitors.Count)
                _logger.Info($"{TextFormat.Plural(expectedMonitors.Count, "display")} enabled and available in {elapsed}ms");
            else
            {
                var failedMonitors = expectedMonitors.Where(m => !verifiedTargetIds.Contains(CcdAddress.Target(m)));
                foreach (var failed in failedMonitors)
                {
                    string name = string.IsNullOrEmpty(failed.FriendlyName) ? failed.DeviceName : failed.FriendlyName;
                    _logger.Warn($"Target {CcdAddress.Target(failed)} ({name}) failed to stabilize within timeout");
                }
                _logger.Error($"Display stabilization timed out -> only {verifiedTargetIds.Count}/{expectedMonitors.Count} displays ready");
                return false;
            }

            return true;
        }

        public static bool ApplyDisplayLayout(List<DisplayConfigInfo> displayConfigs, out int errorCode)
        {
            errorCode = ErrorSuccess;

            try
            {
                _logger.Info("Applying display layout...");

                var queryFlags = QueryDisplayConfigFlags.AllPaths;
                if (AdvancedColorService.IsWindows11OrGreater())
                    queryFlags |= QueryDisplayConfigFlags.VirtualRefreshRateAware;

                int result = QueryDisplayConfigWithRetry(
                    queryFlags,
                    out uint pathCount,
                    out DisplayConfigPathInfo[] paths,
                    out uint modeCount,
                    out DisplayConfigModeInfo[] modes);
                if (result != ErrorSuccess)
                {
                    _logger.Error($"Display layout query failed with error: {result}");
                    errorCode = result;
                    return false;
                }

                var sourceIdMap = BuildSourceIdMap(displayConfigs);

                // Offset all positions relative to profile's primary display
                var primaryProfile = displayConfigs.FirstOrDefault(p => p.IsEnabled && p.IsPrimary) ?? displayConfigs.FirstOrDefault(p => p.IsEnabled);
                int offsetX = primaryProfile != null ? -primaryProfile.DisplayPositionX : 0;
                int offsetY = primaryProfile != null ? -primaryProfile.DisplayPositionY : 0;

                // Skip if layout already matches
                bool needsUpdate = false;

                foreach (var profile in displayConfigs)
                {
                    var pIdx = GetLivePathIndex(paths, profile.AdapterId, profile.TargetId);
                    if (pIdx == -1) continue;

                    string mon = !string.IsNullOrEmpty(profile.FriendlyName) ? profile.FriendlyName : $"ID:{profile.TargetId}";
                    bool isActive = (paths[pIdx].flags & (uint)DisplayConfigPathInfoFlags.Active) != 0;

                    if (isActive != profile.IsEnabled)
                    {
                        _logger.Debug($"[Topology] {mon}: Current={(isActive ? "Enabled" : "Disabled")}, Profile={(profile.IsEnabled ? "Enabled" : "Disabled")}");
                        needsUpdate = true;
                    }

                    if (profile.IsEnabled)
                    {
                        uint normalizedProfileSourceId = sourceIdMap[CcdAddress.Source(profile)];
                        if (paths[pIdx].sourceInfo.id != normalizedProfileSourceId)
                        {
                            _logger.Debug($"[SourceId] {mon}: Current={paths[pIdx].sourceInfo.id}, NormalizedProfile={normalizedProfileSourceId}");
                            needsUpdate = true;
                        }

                        if (paths[pIdx].targetInfo.rotation != (uint)profile.Rotation && profile.Rotation != 0)
                        {
                            _logger.Debug($"[Rotation] {mon}: Current={paths[pIdx].targetInfo.rotation}, Profile={profile.Rotation}");
                            needsUpdate = true;
                        }

                        // Resolution and position check
                        uint sModeIdx = paths[pIdx].sourceInfo.modeInfoIdx;
                        if (sModeIdx != DisplayconfigPathModeIdxInvalid && sModeIdx < modes.Length)
                        {
                            ref var src = ref modes[sModeIdx].modeInfo.sourceMode;
                            int targetX = profile.DisplayPositionX + offsetX;
                            int targetY = profile.DisplayPositionY + offsetY;
                            if (src.width != (uint)profile.Width || src.height != (uint)profile.Height)
                            {
                                _logger.Debug($"[Resolution] {mon}: Current={src.width}x{src.height}, Profile={profile.Width}x{profile.Height}");
                                needsUpdate = true;
                            }
                            if (src.position.x != targetX || src.position.y != targetY)
                            {
                                _logger.Debug($"[Position] {mon}: Current=({src.position.x},{src.position.y}), Profile=({targetX},{targetY})");
                                needsUpdate = true;
                            }
                        }

                        // Refresh rate check
                        uint tModeIdx = paths[pIdx].targetInfo.modeInfoIdx;
                        if (tModeIdx != DisplayconfigPathModeIdxInvalid && tModeIdx < modes.Length)
                        {
                            ref var sig = ref modes[tModeIdx].modeInfo.targetMode.targetVideoSignalInfo;
                            uint liveHz = sig.vSyncFreq.Numerator > 1000 ? sig.vSyncFreq.Numerator / 1000 : sig.vSyncFreq.Numerator;
                            if (liveHz != (uint)profile.RefreshRate)
                            {
                                _logger.Debug($"[RefreshRate] {mon}: Current={liveHz}Hz, Profile={profile.RefreshRate}Hz");
                                needsUpdate = true;
                            }
                        }
                    }
                }

                if (!needsUpdate)
                {
                    _logger.Info("Skipping display layout -> already matches configuration");
                    return true;
                }

                _logger.Info("Display mismatch detected -> Apply profile configuration");

                // Record active paths before clearing flags
                var livePathByTarget = new Dictionary<CcdTargetKey, int>();
                for (int i = 0; i < paths.Length; i++)
                {
                    if ((paths[i].flags & (uint)DisplayConfigPathInfoFlags.Active) == 0) continue;

                    var targetKey = CcdAddress.Target(ToManaged(paths[i].targetInfo.adapterId), paths[i].targetInfo.id);
                    if (!livePathByTarget.ContainsKey(targetKey))
                        livePathByTarget[targetKey] = i;
                }

                // Clear all active flags before rebuilding topology
                for (int i = 0; i < paths.Length; i++)
                    paths[i].flags &= ~(uint)DisplayConfigPathInfoFlags.Active;

                // All clone group members share one source mode entry, keyed by normalized SourceId
                var sourceIdToModeIdx = new Dictionary<CcdSourceKey, uint>();
                foreach (var profile in displayConfigs.Where(d => d.IsEnabled))
                {
                    var targetKey = CcdAddress.Target(profile);
                    if (!livePathByTarget.TryGetValue(targetKey, out int pIdx))
                        pIdx = Array.FindIndex(paths, p => CcdAddress.Target(ToManaged(p.targetInfo.adapterId), p.targetInfo.id).Equals(targetKey));

                    if (pIdx == -1) continue;

                    uint normalizedSourceId = sourceIdMap[CcdAddress.Source(profile)];
                    paths[pIdx].flags |= (uint)DisplayConfigPathInfoFlags.Active;
                    paths[pIdx].sourceInfo.id = normalizedSourceId;

                    if (profile.Rotation != 0)
                        paths[pIdx].targetInfo.rotation = (uint)profile.Rotation;

                    // Share one source mode entry across clone-group members
                    var normalizedSourceKey = CcdAddress.Source(profile.AdapterId, normalizedSourceId);
                    if (!sourceIdToModeIdx.TryGetValue(normalizedSourceKey, out uint sModeIdx))
                    {
                        sModeIdx = paths[pIdx].sourceInfo.modeInfoIdx;
                        sourceIdToModeIdx[normalizedSourceKey] = sModeIdx;
                    }

                    paths[pIdx].sourceInfo.modeInfoIdx = sModeIdx;

                    if (sModeIdx != DisplayconfigPathModeIdxInvalid && sModeIdx < modes.Length)
                    {
                        ref var src = ref modes[sModeIdx].modeInfo.sourceMode;
                        modes[sModeIdx].id = normalizedSourceId;
                        src.width = (uint)profile.Width;
                        src.height = (uint)profile.Height;
                        src.position.x = profile.DisplayPositionX + offsetX;
                        src.position.y = profile.DisplayPositionY + offsetY;
                    }

                    uint tModeIdx = paths[pIdx].targetInfo.modeInfoIdx;
                    if (tModeIdx != DisplayconfigPathModeIdxInvalid && tModeIdx < modes.Length)
                    {
                        ref var targetInfo = ref paths[pIdx].targetInfo;
                        ref var sig = ref modes[tModeIdx].modeInfo.targetMode.targetVideoSignalInfo;

                        // Keep virtual and physical refresh rates in sync
                        targetInfo.refreshRate.Numerator = (uint)(profile.RefreshRate * 1000);
                        targetInfo.refreshRate.Denominator = 1000;

                        sig.vSyncFreq.Numerator = (uint)(profile.RefreshRate * 1000);
                        sig.vSyncFreq.Denominator = 1000;
                        sig.activeSize.cx = (uint)profile.Width;
                        sig.activeSize.cy = (uint)profile.Height;
                    }
                }

                // Commit layout and persist to database
                var layoutFlags =
                    SetDisplayConfigFlags.Apply |
                    SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    SetDisplayConfigFlags.SaveToDatabase |
                    SetDisplayConfigFlags.AllowChanges;
                if (AdvancedColorService.IsWindows11OrGreater())
                    layoutFlags |= SetDisplayConfigFlags.VirtualRefreshRateAware;
                result = SetDisplayConfig(
                    pathCount, paths,
                    modeCount, modes,
                    layoutFlags);

                if (result != ErrorSuccess)
                {
                    _logger.Error($"SetDisplayConfig failed to apply layout: Error {result}");
                    errorCode = result;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to apply layout");
                return false;
            }
        }

        public static async Task<bool> ApplyDisplayConfig(List<DisplayConfigInfo> displayConfigs)
        {
            var result = await ApplyDisplayConfigDetailed(displayConfigs);
            return result.Success;
        }

        internal static async Task<DisplayConfigApplyResult> ApplyDisplayConfigDetailed(List<DisplayConfigInfo> displayConfigs, bool traceEnabled = false)
        {
            var applyResult = new DisplayConfigApplyResult();

            try
            {
                var totalWatch = Stopwatch.StartNew();
                _logger.Info($"Applying configuration for {TextFormat.Plural(displayConfigs.Count(d => d.IsEnabled), "enabled display")}...");

                // Exclude displays absent from enumeration from defer set
                var allPathTargets = GetAllPathTargets();
                var liveConfigs = displayConfigs.Where(d => d.IsEnabled && allPathTargets.Contains(CcdAddress.Target(d))).ToList();

                // Defer until expected displays stabilize
                var deferWatch = Stopwatch.StartNew();
                await DeferDisplayLayoutAsync(liveConfigs, traceEnabled: traceEnabled);
                deferWatch.Stop();

                // Apply resolution, position, and rotation atomically
                var layoutWatch = Stopwatch.StartNew();
                if (!ApplyDisplayLayout(displayConfigs, out int layoutErrorCode))
                {
                    if (layoutErrorCode == ErrorGenFailure)
                    {
                        _logger.Warn("Display layout failed with Error 31 -> waiting for displays and retrying layout once");
                        await DeferDisplayLayoutAsync(liveConfigs, traceEnabled: traceEnabled);

                        if (!ApplyDisplayLayout(displayConfigs, out _))
                        {
                            _logger.Error("Failed to apply display layout after Error 31 retry");
                            return applyResult;
                        }
                    }
                    else
                    {
                        _logger.Error("Failed to apply display layout");
                        return applyResult;
                    }
                }
                layoutWatch.Stop();

                // Preserve post-layout color outcomes without changing the rollback boundary
                var hdrWatch = Stopwatch.StartNew();
                applyResult.AdvancedColorSuccess = AdvancedColorService.ApplyAdvancedColorState(displayConfigs);
                hdrWatch.Stop();

                var colorWatch = Stopwatch.StartNew();
                applyResult.ColorProfileSuccess = ColorProfileAdapter.ApplyColorProfiles(displayConfigs);
                colorWatch.Stop();

                totalWatch.Stop();
                _logger.Info($"Configured - Defer: {deferWatch.ElapsedMilliseconds}ms | Layout: {layoutWatch.ElapsedMilliseconds}ms | HDR: {hdrWatch.ElapsedMilliseconds}ms | Color: {colorWatch.ElapsedMilliseconds}ms | TOTAL: {totalWatch.ElapsedMilliseconds}ms");

                applyResult.Success = true;
                return applyResult;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during configuration application");
                return applyResult;
            }
        }

        public static string DecodeEdidManufacturer(ushort edidManufactureId)
        {
            if (edidManufactureId == 0)
            {
                return string.Empty;
            }

            ushort value = (ushort)((edidManufactureId >> 8) | (edidManufactureId << 8));
            var letters = new char[3];
            for (int i = 0; i < 3; i++)
            {
                int code = (value >> (10 - i * 5)) & 0x1F;
                if (code < 1 || code > 26)
                {
                    return string.Empty;
                }

                letters[i] = (char)('A' + code - 1);
            }

            return new string(letters);
        }
    }
}
