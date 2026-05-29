using DisplayProfileManager.Core;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DisplayProfileManager.Helpers
{
    public class DisplayConfigHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private static bool IsWindows1122H2OrGreater() =>
            Environment.OSVersion.Version.Build >= 22621;

        private static bool IsWindows24H2OrGreater() =>
            Environment.OSVersion.Version.Build >= 26100;

        #region P/Invoke

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(
            QueryDisplayConfigFlags flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);
        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            QueryDisplayConfigFlags flags,
            ref uint numPathArrayElements,
            [Out] DisplayConfigPathInfo[] pathArray,
            ref uint numModeInfoArrayElements,
            [Out] DisplayConfigModeInfo[] modeInfoArray,
            IntPtr currentTopologyId);
        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(
            uint numPathArrayElements,
            [In] DisplayConfigPathInfo[] pathArray,
            uint numModeInfoArrayElements,
            [In] DisplayConfigModeInfo[] modeInfoArray,
            SetDisplayConfigFlags flags);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName deviceName);
        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName deviceName);
        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo colorInfo);
        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetAdvancedColorState colorState);
        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetHdrState state);
        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetWcgState state);

        #endregion

        #region Constants

        private const int ErrorSuccess = 0;
        private const int ErrorInsufficientBuffer = 122;
        private const int ErrorGenFailure = 31;
        private const int ErrorInvalidParameter = 87;

        private const uint DisplayconfigPathSourceModeIdxInvalid = 0xffff;
        private const uint DisplayconfigPathModeIdxInvalid = 0xffffffff;

        #endregion

        #region Enums

        [Flags]
        public enum QueryDisplayConfigFlags : uint
        {
            AllPaths = 0x00000001,
            OnlyActivePaths = 0x00000002,
            DatabaseCurrent = 0x00000004,
            VirtualModeAware = 0x00000010,
            IncludeHmd = 0x00000020,
        }

        [Flags]
        public enum SetDisplayConfigFlags : uint
        {
            TopologyInternal = 0x00000001,
            TopologyClone = 0x00000002,
            TopologyExtend = 0x00000004,
            TopologyExternal = 0x00000008,
            TopologySupplied = 0x00000010,
            UseSuppliedDisplayConfig = 0x00000020,
            Validate = 0x00000040,
            Apply = 0x00000080,
            NoOptimization = 0x00000100,
            SaveToDatabase = 0x00000200,
            AllowChanges = 0x00000400,
            PathPersistIfRequired = 0x00000800,
            ForceModeEnumeration = 0x00001000,
            AllowPathOrderChanges = 0x00002000,
            VirtualModeAware = 0x00008000,
        }

        [Flags]
        public enum DisplayConfigPathInfoFlags : uint
        {
            Active = 0x00000001,
            PreferredUnscaled = 0x00000004,
            SupportVirtualMode = 0x00000008,
            ValidFlags = 0x0000000D,
        }

        [Flags]
        public enum DisplayConfigRotation : uint
        {
            Identity = 1,
            Rotate90 = 2,
            Rotate180 = 3,
            Rotate270 = 4,
            ForceUint32 = 0xFFFFFFFF
        }
        public enum DisplayConfigVideoOutputTechnology : uint
        {
            Other = 0xFFFFFFFF,
            Hd15 = 0,
            Svideo = 1,
            CompositeVideo = 2,
            ComponentVideo = 3,
            Dvi = 4,
            Hdmi = 5,
            Lvds = 6,
            DJpn = 8,
            Sdi = 9,
            DisplayPortExternal = 10,
            DisplayPortEmbedded = 11,
            UdiExternal = 12,
            UdiEmbedded = 13,
            SdtvDongle = 14,
            Miracast = 15,
            IndirectWired = 16,
            IndirectVirtual = 17,
            Internal = 0x80000000,
            ForceUint32 = 0xFFFFFFFF
        }
        public enum DisplayConfigModeInfoType : uint
        {
            Source = 1,
            Target = 2,
            DesktopImage = 3,
            ForceUint32 = 0xFFFFFFFF
        }
        public enum DisplayConfigDeviceInfoType : uint
        {
            GetSourceName = 1,
            GetTargetName = 2,
            GetTargetPreferredMode = 3,
            GetAdapterName = 4,
            SetTargetPersistence = 5,
            GetTargetBaseType = 6,
            GetSupportVirtualResolution = 7,
            SetSupportVirtualResolution = 8,
            GetAdvancedColorInfo = 9,
            SetAdvancedColorState = 10,
            GetSdrWhiteLevel = 11,
            GetMonitorSpecialization = 12,
            SetMonitorSpecialization = 13,
            SetReserved1 = 14,
            GetAdvancedColorInfo2 = 15,
            SetHdrState = 16,
            SetWcgState = 17,
            ForceUint32 = 0xFFFFFFFF
        }
        public enum DisplayConfigAdvancedColorInfoFlags : uint
        {
            AdvancedColorSupported = 0x1,
            AdvancedColorEnabled = 0x2,
            WideColorEnforced = 0x4,
            AdvancedColorForceDisabled = 0x8,
        }
        public enum DisplayConfigSetAdvancedColorFlags : uint
        {
            EnableAdvancedColor = 0x1
        }
        public enum DisplayConfigColorEncoding : uint
        {
            Rgb = 0,
            YCbCr444 = 1,
            YCbCr422 = 2,
            YCbCr420 = 3,
            Intensity = 4,
            ForceUint32 = 0xFFFFFFFF
        }
        public enum DisplayConfigColorIntent
        {
            Off,
            Acm,
            Hdr
        }

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECTL
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigRational
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfig2DRegion
        {
            public uint cx;
            public uint cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigPathSourceInfo
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;

            // Encodes clone group ID in lower 16 bits and marks source mode index as invalid in upper 16
            public void ResetModeAndSetCloneGroup(uint cloneGroup)
            {
                modeInfoIdx = (DisplayconfigPathSourceModeIdxInvalid << 16) | cloneGroup;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigPathTargetInfo
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public DisplayConfigVideoOutputTechnology outputTechnology;
            public uint rotation;
            public uint scaling;
            public DisplayConfigRational refreshRate;
            public uint scanLineOrdering;
            public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigPathInfo
        {
            public DisplayConfigPathSourceInfo sourceInfo;
            public DisplayConfigPathTargetInfo targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigVideoSignalInfo
        {
            public ulong pixelRate;
            public DisplayConfigRational hSyncFreq;
            public DisplayConfigRational vSyncFreq;
            public DisplayConfig2DRegion activeSize;
            public DisplayConfig2DRegion totalSize;
            public uint videoStandard;
            public uint scanLineOrdering;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigSourceMode
        {
            public uint width;
            public uint height;
            public uint pixelFormat;
            public POINTL position;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigTargetMode
        {
            public DisplayConfigVideoSignalInfo targetVideoSignalInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigDesktopImageInfo
        {
            public POINTL PathSourceSize;
            public RECTL DesktopImageRegion;
            public RECTL DesktopImageClip;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct DisplayConfigModeInfoUnion
        {
            [FieldOffset(0)] public DisplayConfigTargetMode targetMode;
            [FieldOffset(0)] public DisplayConfigSourceMode sourceMode;
            [FieldOffset(0)] public DisplayConfigDesktopImageInfo desktopImageInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigModeInfo
        {
            public DisplayConfigModeInfoType infoType;
            public uint id;
            public LUID adapterId;
            public DisplayConfigModeInfoUnion modeInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigDeviceInfoHeader
        {
            public DisplayConfigDeviceInfoType type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DisplayConfigTargetDeviceName
        {
            public DisplayConfigDeviceInfoHeader header;
            public uint flags;
            public DisplayConfigVideoOutputTechnology outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DisplayConfigSourceDeviceName
        {
            public DisplayConfigDeviceInfoHeader header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigGetAdvancedColorInfo
        {
            public DisplayConfigDeviceInfoHeader header;
            public DisplayConfigAdvancedColorInfoFlags values;
            public DisplayConfigColorEncoding colorEncoding;
            public int bitsPerColorChannel;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigSetAdvancedColorState
        {
            public DisplayConfigDeviceInfoHeader header;
            public DisplayConfigSetAdvancedColorFlags values;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigSetHdrState
        {
            public DisplayConfigDeviceInfoHeader header;
            public uint value; // bit 0 = enableHdr
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigSetWcgState
        {
            public DisplayConfigDeviceInfoHeader header;
            public uint value; // bit 0 = enableWcg (ACM)
        }

        #endregion

        #region Public Classes

        public class DisplayConfigInfo
        {
            // Identity
            public string DeviceName { get; set; } = string.Empty;
            public string FriendlyName { get; set; } = string.Empty;
            public LUID AdapterId { get; set; }
            public uint TargetId { get; set; }
            public uint RawTargetId { get; set; }
            public uint SourceId { get; set; }
            public uint PathIndex { get; set; }
            public DisplayConfigVideoOutputTechnology OutputTechnology { get; set; }
            // State
            public bool IsEnabled { get; set; }
            public bool IsAvailable { get; set; }
            public bool IsPrimary { get; set; }
            // Layout
            public int DisplayPositionX { get; set; }
            public int DisplayPositionY { get; set; }
            // Active configuration
            public int Width { get; set; }
            public int Height { get; set; }
            public double RefreshRate { get; set; }
            public DisplayConfigRotation Rotation { get; set; } = DisplayConfigRotation.Identity;
            public bool IsHdrSupported { get; set; } = false;
            public bool IsHdrEnabled { get; set; } = false;
            public bool IsAcmEnabled { get; set; } = false;
            public DisplayConfigColorEncoding ColorEncoding { get; set; } = DisplayConfigColorEncoding.Rgb;
            public uint BitsPerColorChannel { get; set; } = 8;
            public string ColorProfile { get; set; } = null;
            // Native
            public int NativeWidth { get; set; } = 0;
            public int NativeHeight { get; set; } = 0;
        }

        #endregion

        #region Public Methods

        public static List<DisplayConfigInfo> GetDisplayConfigs()
        {
            var displays = new List<DisplayConfigInfo>();

            try
            {
                int result = GetDisplayConfigBufferSizes(QueryDisplayConfigFlags.OnlyActivePaths, out uint pathCount, out uint modeCount);

                if (result != ErrorSuccess)
                {
                    logger.Error($"GetDisplayConfigBufferSizes failed with error: {result}");
                    return displays;
                }

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];

                result = QueryDisplayConfig(
                    QueryDisplayConfigFlags.OnlyActivePaths,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero);

                if (result != ErrorSuccess)
                {
                    logger.Error($"QueryDisplayConfig failed with error: {result}");
                    return displays;
                }

                for (uint i = 0; i < pathCount; i++)
                {
                    var path = paths[i];

                    if (!path.targetInfo.targetAvailable) continue;

                    bool isActive = (path.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0;
                    if (!isActive) continue;

                    uint baseTargetId = path.targetInfo.id & 0xFFFF; // Extract base TargetId (lower 16 bits) — Windows encodes SourceId in high bytes during clone mode

                    var displayConfig = new DisplayConfigInfo
                    {
                        PathIndex = i,
                        IsEnabled = isActive,
                        IsAvailable = path.targetInfo.targetAvailable,
                        AdapterId = path.sourceInfo.adapterId,
                        SourceId = path.sourceInfo.id,
                        TargetId = baseTargetId,
                        RawTargetId = path.targetInfo.id,
                        OutputTechnology = path.targetInfo.outputTechnology
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
                        displayConfig.FriendlyName = targetName.monitorFriendlyDeviceName;

                    // Advanced color state (HDR/ACM)
                    var colorInfo = new DisplayConfigGetAdvancedColorInfo();
                    colorInfo.header.type = DisplayConfigDeviceInfoType.GetAdvancedColorInfo;
                    colorInfo.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigGetAdvancedColorInfo));
                    colorInfo.header.adapterId = path.targetInfo.adapterId;
                    colorInfo.header.id = path.targetInfo.id;

                    result = DisplayConfigGetDeviceInfo(ref colorInfo);
                    if (result == ErrorSuccess)
                    {
                        var flags = colorInfo.values;
                        bool isSupported = (flags & DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported) != 0;
                        bool isEnabled = (flags & DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled) != 0;
                        bool isForceDisabled = (flags & DisplayConfigAdvancedColorInfoFlags.AdvancedColorForceDisabled) != 0;

                        bool finalSupported = isSupported && !isForceDisabled;
                        bool isHdrEncoding = colorInfo.colorEncoding == DisplayConfigColorEncoding.YCbCr444;

                        displayConfig.IsHdrSupported = finalSupported;
                        displayConfig.IsHdrEnabled = isEnabled && isHdrEncoding;
                        displayConfig.IsAcmEnabled = isEnabled && !isHdrEncoding;
                        displayConfig.ColorEncoding = colorInfo.colorEncoding;
                        displayConfig.BitsPerColorChannel = (uint)colorInfo.bitsPerColorChannel;
                    }
                    else
                    {
                        logger.Debug($"Failed to get HDR info for {displayConfig.DeviceName}: Error {result}");
                        displayConfig.IsHdrSupported = false;
                        displayConfig.IsHdrEnabled = false;
                    }

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

                    // Native resolution and refresh rate from target mode
                    if (displayConfig.IsEnabled && path.targetInfo.modeInfoIdx != DisplayconfigPathModeIdxInvalid)
                    {
                        var targetMode = modes[path.targetInfo.modeInfoIdx];
                        if (targetMode.infoType == DisplayConfigModeInfoType.Target)
                        {
                            var sig = targetMode.modeInfo.targetMode.targetVideoSignalInfo;

                            if (sig.activeSize.cx > 0 && sig.activeSize.cy > 0)
                            {
                                displayConfig.NativeWidth = (int)sig.activeSize.cx;
                                displayConfig.NativeHeight = (int)sig.activeSize.cy;
                            }

                            if (sig.vSyncFreq.Denominator != 0)
                                displayConfig.RefreshRate = Math.Round((double)sig.vSyncFreq.Numerator / sig.vSyncFreq.Denominator, 2);
                        }
                    }

                    displays.Add(displayConfig);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting display topology");
            }

            return displays;
        }

        public static Dictionary<uint, uint> BuildSourceIdMap(List<DisplayConfigInfo> displayConfigs)
        {
            return displayConfigs.Where(d => d.IsEnabled)
                .Select(d => d.SourceId)
                .Distinct()
                .OrderBy(id => id)
                .Select((id, index) => new { Original = id, Normalized = (uint)index })
                .ToDictionary(x => x.Original, x => x.Normalized);
        }

        public static bool ApplyDisplayTopology(List<DisplayConfigInfo> displayConfigs)
        {
            try
            {
                logger.Info("Applying display topology...");

                int result = GetDisplayConfigBufferSizes(QueryDisplayConfigFlags.AllPaths, out uint pathCount, out uint modeCount);

                if (result != ErrorSuccess)
                {
                    logger.Error($"GetDisplayConfigBufferSizes failed with error: {result}");
                    return false;
                }

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];

                result = QueryDisplayConfig(
                    QueryDisplayConfigFlags.AllPaths,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero);

                if (result != ErrorSuccess)
                {
                    logger.Error($"QueryDisplayConfig failed with error: {result}");
                    return false;
                }

                // Skip if topology already matches
                bool needsUpdate = false;
                var profileLookup = displayConfigs.ToDictionary(d => d.TargetId & 0xFFFF);
                var sourceIdMap = BuildSourceIdMap(displayConfigs);

                var pathsByTarget = paths.Where(p => p.targetInfo.targetAvailable).GroupBy(p => p.targetInfo.id & 0xFFFF);
                foreach (var group in pathsByTarget)
                {
                    uint hardwareId = group.Key;
                    bool isAnyPathActive = group.Any(p => (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0);

                    if (profileLookup.TryGetValue(hardwareId, out var profile))
                    {
                        if (isAnyPathActive != profile.IsEnabled)
                        {
                            logger.Debug($"Found TargetId {hardwareId}: Currently {(isAnyPathActive ? "on" : "off")} but should be {(profile.IsEnabled ? "on" : "off")}.");
                            needsUpdate = true;
                        }
                        else if (isAnyPathActive && profile.IsEnabled)
                        {
                            var activePath = group.First(p => (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0);
                            uint normalizedProfileSourceId = sourceIdMap[profile.SourceId];
                            if (activePath.sourceInfo.id != normalizedProfileSourceId)
                            {
                                logger.Debug($"Found TargetId {hardwareId}: CurrentSource={activePath.sourceInfo.id} but NormalizedProfileSource={normalizedProfileSourceId}");
                                needsUpdate = true;
                            }
                        }
                    }
                    else if (isAnyPathActive)
                    {
                        logger.Debug($"Found TargetId {hardwareId}: undefined in profile but currently active.");
                        needsUpdate = true;
                    }
                }

                if (!needsUpdate)
                {
                    logger.Info("Skipping -> Display topology already matches configuration.");
                    return true;
                }

                logger.Info("Display mismatch detected -> Applying topology update");

                // Map TargetId to path index
                var targetIdToPathIndex = new Dictionary<uint, int>();
                for (int i = 0; i < paths.Length; i++)
                {
                    if (paths[i].targetInfo.targetAvailable)
                    {
                        uint baseTargetId = paths[i].targetInfo.id & 0xFFFF;
                        if (!targetIdToPathIndex.ContainsKey(baseTargetId))
                            targetIdToPathIndex[baseTargetId] = i;
                    }
                }

                // Assign a clone group index per unique SourceId
                var sourceIdToCloneGroup = new Dictionary<uint, uint>();
                uint nextCloneGroup = 0;
                foreach (var display in displayConfigs.Where(d => d.IsEnabled))
                {
                    if (!sourceIdToCloneGroup.ContainsKey(display.SourceId))
                        sourceIdToCloneGroup[display.SourceId] = nextCloneGroup++;
                }

                var targetIdToDisplay = displayConfigs.Where(d => d.IsEnabled).ToDictionary(d => d.TargetId & 0xFFFF);
                foreach (var kvp in targetIdToPathIndex)
                {
                    uint targetId = kvp.Key;
                    int pathIndex = kvp.Value;

                    paths[pathIndex].targetInfo.modeInfoIdx = DisplayconfigPathModeIdxInvalid;

                    if (targetIdToDisplay.TryGetValue(targetId, out var display))
                    {
                        uint cloneGroup = sourceIdToCloneGroup[display.SourceId];
                        paths[pathIndex].flags |= (uint)DisplayConfigPathInfoFlags.Active;
                        paths[pathIndex].sourceInfo.ResetModeAndSetCloneGroup(cloneGroup);
                    }
                    else
                    {
                        paths[pathIndex].flags &= ~(uint)DisplayConfigPathInfoFlags.Active;
                        paths[pathIndex].sourceInfo.modeInfoIdx = DisplayconfigPathModeIdxInvalid;
                    }
                }

                // Assign contiguous source IDs per adapter across all active paths
                var sourceIdTable = new Dictionary<LUID, uint>();
                int activeCount = 0;

                for (int i = 0; i < paths.Length; i++)
                {
                    if ((paths[i].flags & (uint)DisplayConfigPathInfoFlags.Active) != 0)
                    {
                        LUID adapterId = paths[i].sourceInfo.adapterId;
                        if (!sourceIdTable.ContainsKey(adapterId))
                            sourceIdTable[adapterId] = 0;
                        paths[i].sourceInfo.id = sourceIdTable[adapterId]++;
                        activeCount++;
                    }
                }

                if (activeCount == 0)
                {
                    logger.Error("No active displays to enable.");
                    return false;
                }

                result = SetDisplayConfig(
                    pathCount,
                    paths,
                    0,
                    null,
                    SetDisplayConfigFlags.TopologySupplied |
                    SetDisplayConfigFlags.Apply |
                    SetDisplayConfigFlags.AllowPathOrderChanges |
                    SetDisplayConfigFlags.VirtualModeAware);

                if (result != ErrorSuccess)
                {
                    logger.Error($"Topology failed with error: {result}");
                    return false;
                }

                logger.Info("Successfully applied topology.");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error applying topology.");
                return false;
            }
        }

        public static async Task<bool> DeferDisplayLayoutAsync(List<DisplayConfigInfo> displayConfigs, int deferTimeout = 10000)
        {
            var deferWatch = Stopwatch.StartNew();
            var expectedMonitors = displayConfigs.Where(d => d.IsEnabled).ToList();
            var verifiedTargetIds = new HashSet<uint>();
            int deferCycles = 0;

            logger.Info($"Deferring configuration until {expectedMonitors.Count} enabled display(s) stabilize...");

            while (verifiedTargetIds.Count < expectedMonitors.Count && deferWatch.ElapsedMilliseconds < deferTimeout)
            {
                deferCycles++;
                var liveSnapshot = GetDisplayConfigs();
                foreach (var monitor in expectedMonitors)
                {
                    uint maskedProfileId = monitor.TargetId & 0xFFFF;
                    if (verifiedTargetIds.Contains(monitor.TargetId)) continue;

                    var match = liveSnapshot.FirstOrDefault(l => (l.TargetId & 0xFFFF) == maskedProfileId);
                    if (match != null && match.IsEnabled && match.Width > 0 && match.Height > 0)
                    {
                        verifiedTargetIds.Add(monitor.TargetId);
                        string name = !string.IsNullOrEmpty(monitor.FriendlyName) ? monitor.FriendlyName : monitor.DeviceName;
                        deferWatch.Stop();
                        logger.Debug($"{name} (TargetId {monitor.TargetId}) is active and stable.");
                    }
                }

                if (verifiedTargetIds.Count < expectedMonitors.Count)
                    await Task.Delay(250);
            }

            if (verifiedTargetIds.Count == expectedMonitors.Count)
                logger.Info($"{expectedMonitors.Count} display(s) enabled and stabilized in {deferWatch.ElapsedMilliseconds}ms.");
            else
            {
                var failedMonitors = expectedMonitors.Where(m => !verifiedTargetIds.Contains(m.TargetId));
                foreach (var failed in failedMonitors)
                {
                    string name = string.IsNullOrEmpty(failed.FriendlyName) ? failed.DeviceName : failed.FriendlyName;
                    logger.Warn($"TargetId {failed.TargetId} ({name}) FAILED to stabilize within timeout.");
                }
                logger.Error($"Display stabilization timed out! Only {verifiedTargetIds.Count}/{expectedMonitors.Count} display(s) ready.");
                return false;
            }

            return true;
        }

        public static bool ApplyDisplayLayout(List<DisplayConfigInfo> displayConfigs)
        {
            try
            {
                logger.Info("Applying display layout...");

                var queryFlags = QueryDisplayConfigFlags.AllPaths;
                int result = GetDisplayConfigBufferSizes(queryFlags, out uint pathCount, out uint modeCount);
                if (result != ErrorSuccess) return false;

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];
                result = QueryDisplayConfig(queryFlags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result != ErrorSuccess) return false;

                var sourceIdMap = BuildSourceIdMap(displayConfigs);

                // Offset all positions relative to the profile's primary display
                var primaryProfile = displayConfigs.FirstOrDefault(p => p.IsEnabled && p.IsPrimary) ?? displayConfigs.FirstOrDefault(p => p.IsEnabled);
                int offsetX = primaryProfile != null ? -primaryProfile.DisplayPositionX : 0;
                int offsetY = primaryProfile != null ? -primaryProfile.DisplayPositionY : 0;

                // Skip if layout already matches
                bool needsUpdate = false;

                foreach (var profile in displayConfigs)
                {
                    var pIdx = Array.FindIndex(paths, p => (p.targetInfo.id & 0xFFFF) == (profile.TargetId & 0xFFFF));
                    if (pIdx == -1) continue;

                    string mon = !string.IsNullOrEmpty(profile.FriendlyName) ? profile.FriendlyName : $"ID:{profile.TargetId}";
                    bool isActive = (paths[pIdx].flags & (uint)DisplayConfigPathInfoFlags.Active) != 0;

                    if (isActive != profile.IsEnabled)
                    {
                        logger.Debug($"[Topology] {mon}: Current={(isActive ? "Enabled" : "Disabled")}, Profile={(profile.IsEnabled ? "Enabled" : "Disabled")}");
                        needsUpdate = true;
                    }

                    if (profile.IsEnabled)
                    {
                        uint normalizedProfileSourceId = sourceIdMap[profile.SourceId];
                        if (paths[pIdx].sourceInfo.id != normalizedProfileSourceId)
                        {
                            logger.Debug($"[SourceId] {mon}: Current={paths[pIdx].sourceInfo.id}, NormalizedProfile={normalizedProfileSourceId}");
                            needsUpdate = true;
                        }

                        if (paths[pIdx].targetInfo.rotation != (uint)profile.Rotation && profile.Rotation != 0)
                        {
                            logger.Debug($"[Rotation] {mon}: Current={paths[pIdx].targetInfo.rotation}, Profile={profile.Rotation}");
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
                                logger.Debug($"[Resolution] {mon}: Current={src.width}x{src.height}, Profile={profile.Width}x{profile.Height}");
                                needsUpdate = true;
                            }
                            if (src.position.x != targetX || src.position.y != targetY)
                            {
                                logger.Debug($"[Position] {mon}: Current=({src.position.x},{src.position.y}), Profile=({targetX},{targetY})");
                                needsUpdate = true;
                            }
                        }

                        // Refresh rate check
                        uint tModeIdx = paths[pIdx].targetInfo.modeInfoIdx;
                        if (tModeIdx != DisplayconfigPathModeIdxInvalid && tModeIdx < modes.Length)
                        {
                            ref var sig = ref modes[tModeIdx].modeInfo.targetMode.targetVideoSignalInfo;
                            uint currentHz = sig.vSyncFreq.Numerator > 1000 ? sig.vSyncFreq.Numerator / 1000 : sig.vSyncFreq.Numerator;
                            if (currentHz != (uint)profile.RefreshRate)
                            {
                                logger.Debug($"[RefreshRate] {mon}: Current={currentHz}Hz, Profile={profile.RefreshRate}Hz");
                                needsUpdate = true;
                            }
                        }
                    }
                }

                if (!needsUpdate)
                {
                    logger.Info("Skipping -> Display configuration already matches profile");
                    return true;
                }

                logger.Info("Display mismatch detected -> Apply profile configuration");

                // Clear all active flags before rebuilding topology
                for (int i = 0; i < paths.Length; i++)
                    paths[i].flags &= ~(uint)DisplayConfigPathInfoFlags.Active;

                // All clone group members share one source mode entry, keyed by normalized SourceId
                var sourceIdToModeIdx = new Dictionary<uint, uint>();
                foreach (var profile in displayConfigs.Where(d => d.IsEnabled))
                {
                    int pIdx = Array.FindIndex(paths, p => (p.targetInfo.id & 0xFFFF) == (profile.TargetId & 0xFFFF));
                    if (pIdx == -1) continue;

                    uint normalizedSourceId = sourceIdMap[profile.SourceId];
                    paths[pIdx].flags |= (uint)DisplayConfigPathInfoFlags.Active;
                    paths[pIdx].sourceInfo.id = normalizedSourceId;

                    if (profile.Rotation != 0)
                        paths[pIdx].targetInfo.rotation = (uint)profile.Rotation;

                    // Ensure clone group members share the same source mode index
                    if (!sourceIdToModeIdx.TryGetValue(normalizedSourceId, out uint sModeIdx))
                    {
                        sModeIdx = paths[pIdx].sourceInfo.modeInfoIdx;
                        sourceIdToModeIdx[normalizedSourceId] = sModeIdx;
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
                        ref var sig = ref modes[tModeIdx].modeInfo.targetMode.targetVideoSignalInfo;
                        sig.vSyncFreq.Numerator = (uint)(profile.RefreshRate * 1000);
                        sig.vSyncFreq.Denominator = 1000;
                        sig.activeSize.cx = (uint)profile.Width;
                        sig.activeSize.cy = (uint)profile.Height;
                    }
                }

                // Commit topology and persist to the database
                result = SetDisplayConfig(
                    pathCount, paths,
                    modeCount, modes,
                    SetDisplayConfigFlags.Apply |
                    SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    SetDisplayConfigFlags.SaveToDatabase |
                    SetDisplayConfigFlags.AllowChanges);

                // Cross-check with VerifyDisplayConfiguration before failing — Windows can return non-fatal codes on valid configs
                if (result != ErrorSuccess)
                {
                    if (!VerifyDisplayConfiguration(displayConfigs))
                    {
                        logger.Error($"SetDisplayConfig failed: Error {result}");
                        return false;
                    }
                    logger.Debug($"SetDisplayConfig reported Error {result}, but currentConfigs correctly matches displayConfigs.");
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to apply layout.");
                return false;
            }
        }

        public static async Task<bool> ApplyDisplayConfig(List<DisplayConfigInfo> displayConfigs)
        {
            try
            {
                var totalWatch = Stopwatch.StartNew();
                logger.Info($"Applying configuration for {displayConfigs.Count(d => d.IsEnabled)} enabled display(s)...");

                // Apply resolution, position, and rotation atomically
                var layoutWatch = Stopwatch.StartNew();
                if (!ApplyDisplayLayout(displayConfigs))
                {
                    logger.Error("Failed to apply display layout");
                    return false;
                }
                layoutWatch.Stop();

                // Apply Advanced Color state (HDR/ACM) after layout — requires valid target handles
                var hdrWatch = Stopwatch.StartNew();
                ApplyAdvancedColorState(displayConfigs);
                hdrWatch.Stop();

                // Apply color profiles after Advanced Color state is established
                var colorWatch = Stopwatch.StartNew();
                ApplyColorProfiles(displayConfigs);
                colorWatch.Stop();

                totalWatch.Stop();
                logger.Info($"Configured - Layout: {layoutWatch.ElapsedMilliseconds}ms | HDR: {hdrWatch.ElapsedMilliseconds}ms | Color: {colorWatch.ElapsedMilliseconds}ms | TOTAL: {totalWatch.ElapsedMilliseconds}ms");

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during configuration application");
                return false;
            }
        }

        public static bool ApplyAdvancedColorState(List<DisplayConfigInfo> displayConfigs)
        {
            logger.Info("Applying Advanced Color state...");

            // Fresh live query — RawTargetId values are required by DisplayConfigSetDeviceInfo
            var currentConfigs = GetDisplayConfigs();
            bool allSuccessful = true;

            foreach (var profileDisplay in displayConfigs)
            {
                if (!profileDisplay.IsEnabled) continue;

                var activeDisplay = currentConfigs.FirstOrDefault(c => c.TargetId == profileDisplay.TargetId);
                if (activeDisplay == null)
                {
                    if (profileDisplay.IsHdrSupported)
                    {
                        logger.Warn($"Could not find active display matching TargetId {profileDisplay.TargetId} to apply advanced color.");
                        allSuccessful = false;
                    }
                    continue;
                }

                // Skip if HDR state already matches
                if (profileDisplay.IsHdrSupported)
                {
                    if (activeDisplay.IsHdrEnabled != profileDisplay.IsHdrEnabled)
                    {
                        logger.Info($"Setting {activeDisplay.FriendlyName} -> HDR to {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                        if (!SetHdrState(activeDisplay.AdapterId, activeDisplay.RawTargetId, profileDisplay.IsHdrEnabled))
                        {
                            logger.Error($"Failed to apply HDR setting for {activeDisplay.FriendlyName}.");
                            allSuccessful = false;
                        }
                    }
                    else
                    {
                        logger.Debug($"Skipping {activeDisplay.FriendlyName} -> HDR is already {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                    }
                }

                // ACM — forced on when HDR is on; independently toggleable otherwise
                bool wantAcm = profileDisplay.IsHdrEnabled || profileDisplay.IsAcmEnabled;
                if (wantAcm != activeDisplay.IsAcmEnabled)
                {
                    logger.Info($"Setting {activeDisplay.FriendlyName} -> ACM to {(wantAcm ? "on" : "off")}");
                    if (!SetAcmState(activeDisplay.AdapterId, activeDisplay.RawTargetId, wantAcm))
                    {
                        logger.Warn($"ACM state change failed for {activeDisplay.FriendlyName} (expected on W11 pre-24H2 HDR displays).");
                    }
                }
                else
                {
                    logger.Debug($"Skipping {activeDisplay.FriendlyName} -> ACM is already {(wantAcm ? "on" : "off")}");
                }
            }

            return allSuccessful;
        }

        public static bool SetAdvancedColorState(LUID adapterId, uint rawTargetId, DisplayConfigColorIntent intent)
        {
            try
            {
                // HDR and ACM share one enable bit; reset to SDR context first so Windows picks ACM rather than HDR
                if (intent == DisplayConfigColorIntent.Acm)
                {
                    var off = BuildColorStateStruct(adapterId, rawTargetId, false);
                    DisplayConfigSetDeviceInfo(ref off);
                }

                var state = BuildColorStateStruct(adapterId, rawTargetId, intent != DisplayConfigColorIntent.Off);
                int result = DisplayConfigSetDeviceInfo(ref state);
                if (result == ErrorSuccess)
                {
                    logger.Info($"Set advanced color to {intent} for RawTargetId {rawTargetId}");
                    return true;
                }

                logger.Error($"Failed to set advanced color for RawTargetId {rawTargetId}: Error {result}");
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error setting advanced color for RawTargetId {rawTargetId}");
                return false;
            }
        }

        public static bool SetHdrState(LUID adapterId, uint rawTargetId, bool enable)
        {
            if (IsWindows24H2OrGreater())
            {
                var s = new DisplayConfigSetHdrState();
                s.header.type = DisplayConfigDeviceInfoType.SetHdrState;
                s.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSetHdrState));
                s.header.adapterId = adapterId;
                s.header.id = rawTargetId;
                s.value = enable ? 1u : 0u;

                int result = DisplayConfigSetDeviceInfo(ref s);
                if (result == ErrorSuccess)
                {
                    logger.Info($"Set HDR to {enable} for RawTargetId {rawTargetId}"); return true;
                }

                logger.Error($"Failed to set HDR state for RawTargetId {rawTargetId}: Error {result}");
                return false;
            }
            // Pre-24H2: fall back to legacy advanced color path
            return SetAdvancedColorState(adapterId, rawTargetId, enable ? DisplayConfigColorIntent.Hdr : DisplayConfigColorIntent.Off);
        }

        public static bool SetAcmState(LUID adapterId, uint rawTargetId, bool enable)
        {
            if (IsWindows24H2OrGreater()) return SetWcgState(adapterId, rawTargetId, enable);

            if (!enable) return SetAdvancedColorState(adapterId, rawTargetId, DisplayConfigColorIntent.Off);

            // Pre-24H2: the ACM bit only works on SDR-only displays; on HDR-capable displays it maps to HDR
            var liveConfigs = GetDisplayConfigs();
            var display = liveConfigs.FirstOrDefault(c => c.RawTargetId == rawTargetId);
            if (display?.IsHdrSupported == true)
            {
                logger.Warn($"ACM is not supported on HDR-capable displays before Windows 11 24H2 (RawTargetId {rawTargetId})");
                return false;
            }

            return SetAdvancedColorState(adapterId, rawTargetId, DisplayConfigColorIntent.Acm);
        }

        public static bool IsAcmSupported(uint targetId)
        {
            try
            {
                if (!IsWindows1122H2OrGreater())
                    return false;

                var liveConfigs = GetDisplayConfigs();
                var display = liveConfigs.FirstOrDefault(c => c.TargetId == targetId);
                return display?.IsHdrSupported ?? false;
            }
            catch
            {
                return false;
            }
        }

        public static bool ApplyColorProfiles(List<DisplayConfigInfo> displayConfigs)
        {
            logger.Info("Applying color profiles...");
            var liveConfigs = GetDisplayConfigs();
            bool allSuccessful = true;

            foreach (var profileDisplay in displayConfigs)
            {
                if (!profileDisplay.IsEnabled || string.IsNullOrEmpty(profileDisplay.ColorProfile)) continue;

                var activeDisplay = liveConfigs.FirstOrDefault(c => c.TargetId == profileDisplay.TargetId);
                if (activeDisplay == null)
                {
                    logger.Warn($"Could not find active display matching TargetId {profileDisplay.TargetId} to apply color profile.");
                    allSuccessful = false;
                    continue;
                }

                var setting = new DisplaySetting
                {
                    DeviceName = activeDisplay.DeviceName,
                    AdapterLuid = activeDisplay.AdapterId,
                    SourceId = activeDisplay.SourceId,
                    TargetId = profileDisplay.TargetId,
                    ColorProfile = profileDisplay.ColorProfile,
                    IsEnabled = profileDisplay.IsEnabled
                };

                if (!ColorProfileHelper.ApplyColorProfile(setting, liveConfigs))
                    allSuccessful = false;
            }

            return allSuccessful;
        }

        public static bool VerifyDisplayConfiguration(List<DisplayConfigInfo> expectedConfigs)
        {
            try
            {
                var currentConfigs = GetDisplayConfigs();

                int expEnabled = expectedConfigs.Count(c => c.IsEnabled);
                int expDisabled = expectedConfigs.Count(c => !c.IsEnabled);
                int foundActive = currentConfigs.Count(c => c.IsEnabled);
                int foundInactive = currentConfigs.Count - foundActive;

                string expectedStr = $"{expEnabled} enabled";
                if (expDisabled > 0)
                    expectedStr += $" / {expDisabled} disabled";

                string foundStr = $"{foundActive} active";
                if (foundInactive > 0)
                    foundStr += $" / {foundInactive} inactive";

                logger.Info($"Verifying display configuration: Expected {expectedStr} display(s), found {foundStr}");

                bool allMatched = true;

                foreach (var expected in expectedConfigs)
                {
                    if (!expected.IsEnabled)
                    {
                        var found = currentConfigs.FirstOrDefault(c => c.TargetId == expected.TargetId);
                        if (found != null && found.IsEnabled)
                        {
                            logger.Error($"TargetId {expected.TargetId} should be DISABLED but is ACTIVE");
                            allMatched = false;
                        }
                        else
                            logger.Info($"TargetId {expected.TargetId} ({expected.FriendlyName}): disabled");
                        continue;
                    }

                    var current = currentConfigs.FirstOrDefault(c => c.TargetId == expected.TargetId);

                    if (current == null)
                    {
                        logger.Error($"Expected TargetId {expected.TargetId} not found in current configuration");
                        allMatched = false;
                        continue;
                    }

                    if (!current.IsEnabled)
                    {
                        logger.Error($"TargetId {expected.TargetId} ({expected.FriendlyName}) should be ENABLED but is DISABLED");
                        allMatched = false;
                        continue;
                    }

                    logger.Debug($"TargetId {expected.TargetId} ({expected.FriendlyName}): enabled");
                }

                // Verify targets sharing a profile SourceId also share a Windows SourceId
                var cloneGroups = expectedConfigs
                    .Where(e => e.IsEnabled)
                    .GroupBy(e => e.SourceId)
                    .Where(g => g.Count() > 1);

                foreach (var cloneGroup in cloneGroups)
                {
                    var targetIds = cloneGroup.Select(e => e.TargetId).ToList();
                    var actualSourceIds = targetIds
                        .Select(tid => currentConfigs.FirstOrDefault(c => c.TargetId == tid))
                        .Where(c => c != null)
                        .Select(c => c.SourceId)
                        .Distinct()
                        .ToList();

                    if (actualSourceIds.Count == 1)
                        logger.Info($"Clone group (profile SourceId {cloneGroup.Key}): Targets [{string.Join(", ", targetIds)}] correctly share actual SourceId {actualSourceIds[0]}");
                    else
                    {
                        logger.Error($"Clone group (profile SourceId {cloneGroup.Key}): Targets [{string.Join(", ", targetIds)}] have different actual SourceIds: [{string.Join(", ", actualSourceIds)}]");
                        allMatched = false;
                    }
                }

                if (allMatched)
                    logger.Info("Display configuration verification PASSED");
                else
                    logger.Error("Display configuration verification FAILED");

                return allMatched;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error verifying display configuration");
                return false;
            }
        }

        public static LUID GetLUIDFromString(string adapterIdString)
        {
            if (!string.IsNullOrEmpty(adapterIdString) && adapterIdString.Length == 16)
            {
                try
                {
                    var highPart = Convert.ToInt32(adapterIdString.Substring(0, 8), 16);
                    var lowPart = Convert.ToUInt32(adapterIdString.Substring(8, 8), 16);
                    return new LUID { HighPart = highPart, LowPart = lowPart };
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"Failed to parse AdapterId '{adapterIdString}'");
                }
            }
            return new LUID { HighPart = 0, LowPart = 0 };
        }

        #endregion

        #region Private Methods

        private static bool SetWcgState(LUID adapterId, uint rawTargetId, bool enable)
        {
            var s = new DisplayConfigSetWcgState();
            s.header.type = DisplayConfigDeviceInfoType.SetWcgState;
            s.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSetWcgState));
            s.header.adapterId = adapterId;
            s.header.id = rawTargetId;
            s.value = enable ? 1u : 0u;

            int result = DisplayConfigSetDeviceInfo(ref s);
            if (result == ErrorSuccess)
            {
                logger.Info($"Set WCG/ACM to {enable} for RawTargetId {rawTargetId}");
                return true;
            }

            logger.Error($"Failed to set WCG/ACM state for RawTargetId {rawTargetId}: Error {result}");
            return false;
        }

        private static DisplayConfigSetAdvancedColorState BuildColorStateStruct(LUID adapterId, uint rawTargetId, bool enable)
        {
            var s = new DisplayConfigSetAdvancedColorState();
            s.header.type = DisplayConfigDeviceInfoType.SetAdvancedColorState;
            s.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSetAdvancedColorState));
            s.header.adapterId = adapterId;
            s.header.id = rawTargetId;
            s.values = enable ? DisplayConfigSetAdvancedColorFlags.EnableAdvancedColor : 0;
            return s;
        }

        #endregion
    }
}