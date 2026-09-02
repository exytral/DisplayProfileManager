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

        private static bool IsWindows22H2OrGreater() => Environment.OSVersion.Version.Build >= 22621;
        private static bool IsWindows24H2OrGreater() => Environment.OSVersion.Version.Build >= 26100;
        public static bool IsAcmSupported(bool isHdrSupported) => IsWindows22H2OrGreater() && isHdrSupported;

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
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo2 colorInfo);
        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetAdvancedColorState colorState);
        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetHdrState state);
        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetWcgState state);

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
            VirtualRefreshRateAware = 0x00000040,
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
            VirtualRefreshRateAware = 0x00020000,
        }

        [Flags]
        public enum DisplayConfigPathInfoFlags : uint
        {
            Active = 0x00000001,
            PreferredUnscaled = 0x00000004,
            SupportVirtualMode = 0x00000008,
            BoostRefreshRate = 0x00000010,
            ValidFlags = 0x0000001D,
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

        public enum DisplayConfigAdvancedColorMode : uint
        {
            Sdr = 0,
            Wcg = 1,
            Hdr = 2,
        }

        public enum DisplayConfigSetAdvancedColorFlags : uint
        {
            EnableAdvancedColor = 0x1
        }

        public enum DisplayConfigAdvancedColorInfoFlags : uint
        {
            AdvancedColorSupported = 0x1,
            AdvancedColorEnabled = 0x2,
            WideColorEnforced = 0x4,
            AdvancedColorForceDisabled = 0x8,
        }

        public enum DisplayConfigAdvancedColorInfo2Flags : uint
        {
            AdvancedColorSupported = 0x1,
            AdvancedColorActive = 0x2,
            AdvancedColorLimitedByPolicy = 0x8,
            HighDynamicRangeSupported = 0x10,
            HighDynamicRangeUserEnabled = 0x20,
            WideColorSupported = 0x40,
            WideColorUserEnabled = 0x80,
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

        #region Constants

        private const int ErrorSuccess = 0;
        private const int ErrorGenFailure = 31;
        private const int ErrorInvalidParameter = 87;

        private const uint DisplayconfigPathSourceModeIdxInvalid = 0xffff;
        private const uint DisplayconfigPathModeIdxInvalid = 0xffffffff;

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
        public struct DisplayConfigGetAdvancedColorInfo2
        {
            public DisplayConfigDeviceInfoHeader header;
            public DisplayConfigAdvancedColorInfo2Flags values;
            public DisplayConfigColorEncoding colorEncoding;
            public DisplayConfigAdvancedColorMode activeColorMode;
            public uint bitsPerColorChannel;
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
            public string ManufacturerName { get; set; } = string.Empty;
            public string ProductCodeID { get; set; } = string.Empty;
            public LUID AdapterId { get; set; }
            public uint TargetId { get; set; }
            public uint RawTargetId { get; set; }
            public uint SourceId { get; set; }
            public uint PathIndex { get; set; }
            public DisplayConfigVideoOutputTechnology OutputTechnology { get; set; }
            // State
            public bool IsEnabled { get; set; }
            public bool IsPrimary { get; set; }
            // Layout
            public int DisplayPositionX { get; set; }
            public int DisplayPositionY { get; set; }
            // Active Configuration
            public int Width { get; set; }
            public int Height { get; set; }
            public double RefreshRate { get; set; }
            public DisplayConfigRotation Rotation { get; set; } = DisplayConfigRotation.Identity;
            public bool IsHdrSupported { get; set; } = false;
            public bool IsHdrEnabled { get; set; } = false;
            public bool IsAcmEnabled { get; set; } = false;
            // DRR Capability
            public bool SupportsDrr { get; set; } = false;
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
                // Preserve virtual refresh modes when supported
                var queryFlags = QueryDisplayConfigFlags.OnlyActivePaths | QueryDisplayConfigFlags.VirtualRefreshRateAware;
                if (GetDisplayConfigBufferSizes(queryFlags, out _, out _) != ErrorSuccess)
                    queryFlags = QueryDisplayConfigFlags.OnlyActivePaths;

                int result = GetDisplayConfigBufferSizes(queryFlags, out uint pathCount, out uint modeCount);
                if (result != ErrorSuccess)
                {
                    logger.Error($"GetDisplayConfigBufferSizes failed with error: {result}");
                    return displays;
                }

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];

                result = QueryDisplayConfig(
                    queryFlags,
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

                    uint baseTargetId = path.targetInfo.id & 0xFFFF; // Mask clone-encoded TargetId to its base value

                    var displayConfig = new DisplayConfigInfo
                    {
                        PathIndex = i,
                        IsEnabled = isActive,
                        AdapterId = path.sourceInfo.adapterId,
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
                    }

                    // Advanced color state (HDR/ACM)
                    if (IsWindows24H2OrGreater() && GetAdvancedColorInfo2(path.targetInfo.adapterId, path.targetInfo.id, out var colorInfo2))
                    {
                        var flags2 = colorInfo2.values;
                        bool isForceDisabled2 = (flags2 & DisplayConfigAdvancedColorInfo2Flags.AdvancedColorLimitedByPolicy) != 0;

                        bool hdrBit = (flags2 & DisplayConfigAdvancedColorInfo2Flags.HighDynamicRangeSupported) != 0;
                        bool advancedBit = (flags2 & DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported) != 0;
                        displayConfig.IsHdrSupported = (hdrBit || advancedBit) && !isForceDisabled2;
                        displayConfig.IsHdrEnabled = (flags2 & DisplayConfigAdvancedColorInfo2Flags.HighDynamicRangeUserEnabled) != 0;
                        displayConfig.IsAcmEnabled = (flags2 & DisplayConfigAdvancedColorInfo2Flags.WideColorUserEnabled) != 0;
                        displayConfig.ColorEncoding = colorInfo2.colorEncoding;
                        displayConfig.BitsPerColorChannel = colorInfo2.bitsPerColorChannel;
                    }
                    else
                    {
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

        public static HashSet<uint> GetPresentTargetIds()
        {
            var result = new HashSet<uint>();
            try
            {
                int ret = GetDisplayConfigBufferSizes(QueryDisplayConfigFlags.AllPaths, out uint pathCount, out uint modeCount);
                if (ret != ErrorSuccess)
                {
                    return result;
                }

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];
                ret = QueryDisplayConfig(QueryDisplayConfigFlags.AllPaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (ret != ErrorSuccess)
                {
                    return result;
                }

                foreach (var path in paths)
                    result.Add(path.targetInfo.id & 0xFFFF);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error querying all-paths target presence");
            }

            return result;
        }

        private static int GetLivePathIndex(DisplayConfigPathInfo[] paths, uint targetId)
        {
            uint masked = targetId & 0xFFFF;
            int active = Array.FindIndex(paths, p => (p.targetInfo.id & 0xFFFF) == masked && (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0);
            return active >= 0 ? active : Array.FindIndex(paths, p => (p.targetInfo.id & 0xFFFF) == masked);
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

                // Compare without virtual mode so source IDs remain comparable
                const QueryDisplayConfigFlags compareQueryFlags = QueryDisplayConfigFlags.AllPaths;
                const QueryDisplayConfigFlags topologyQueryFlags = QueryDisplayConfigFlags.AllPaths | QueryDisplayConfigFlags.VirtualModeAware;

                int result = GetDisplayConfigBufferSizes(compareQueryFlags, out uint pathCount, out uint modeCount);

                if (result != ErrorSuccess)
                {
                    logger.Error($"GetDisplayConfigBufferSizes failed with error: {result}");
                    return false;
                }

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];

                result = QueryDisplayConfig(
                    compareQueryFlags,
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

                void MutatePathsForTopology(DisplayConfigPathInfo[] targetPaths)
                {
                    var targetIdToPathIndex = new Dictionary<uint, int>();
                    for (int i = 0; i < targetPaths.Length; i++)
                    {
                        if (!targetPaths[i].targetInfo.targetAvailable) continue;

                        uint baseTargetId = targetPaths[i].targetInfo.id & 0xFFFF;
                        bool isActive = (targetPaths[i].flags & (uint)DisplayConfigPathInfoFlags.Active) != 0;

                        // Prefer active path for each target
                        if (!targetIdToPathIndex.TryGetValue(baseTargetId, out int existingIndex) || (isActive && (targetPaths[existingIndex].flags & (uint)DisplayConfigPathInfoFlags.Active) == 0))
                            targetIdToPathIndex[baseTargetId] = i;
                    }

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

                        targetPaths[pathIndex].targetInfo.modeInfoIdx = DisplayconfigPathModeIdxInvalid;

                        if (targetIdToDisplay.TryGetValue(targetId, out var display))
                        {
                            uint cloneGroup = sourceIdToCloneGroup[display.SourceId];
                            targetPaths[pathIndex].flags |= (uint)DisplayConfigPathInfoFlags.Active;
                            targetPaths[pathIndex].sourceInfo.ResetModeAndSetCloneGroup(cloneGroup);
                        }
                        else
                        {
                            targetPaths[pathIndex].flags &= ~(uint)DisplayConfigPathInfoFlags.Active;
                            targetPaths[pathIndex].sourceInfo.modeInfoIdx = DisplayconfigPathModeIdxInvalid;
                        }
                    }

                    // Shared source IDs represent clone groups
                    var sourceIdTable = new Dictionary<LUID, uint>();
                    var groupSourceId = new Dictionary<Tuple<LUID, uint>, uint>();
                    for (int i = 0; i < targetPaths.Length; i++)
                    {
                        if ((targetPaths[i].flags & (uint)DisplayConfigPathInfoFlags.Active) == 0) continue;

                        LUID adapterId = targetPaths[i].sourceInfo.adapterId;
                        uint cloneGroup = targetPaths[i].sourceInfo.modeInfoIdx & 0xFFFF;
                        var key = Tuple.Create(adapterId, cloneGroup);

                        if (!groupSourceId.TryGetValue(key, out uint assigned))
                        {
                            if (!sourceIdTable.ContainsKey(adapterId))
                                sourceIdTable[adapterId] = 0;

                            assigned = sourceIdTable[adapterId]++;
                            groupSourceId[key] = assigned;
                        }

                        targetPaths[i].sourceInfo.id = assigned;
                    }

                    foreach (var p in targetPaths.Where(p => (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0))
                        logger.Debug($"Topology path: target {p.targetInfo.id & 0xFFFF} source {p.sourceInfo.id} cloneGroup {p.sourceInfo.modeInfoIdx & 0xFFFF}");
                }

                // Re-query with VirtualModeAware so clone-group encoding is preserved
                result = GetDisplayConfigBufferSizes(topologyQueryFlags, out pathCount, out modeCount);
                if (result != ErrorSuccess)
                {
                    logger.Error($"GetDisplayConfigBufferSizes failed for apply query with error: {result}");
                    return false;
                }

                paths = new DisplayConfigPathInfo[pathCount];
                modes = new DisplayConfigModeInfo[modeCount];

                result = QueryDisplayConfig(topologyQueryFlags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result != ErrorSuccess)
                {
                    logger.Error($"QueryDisplayConfig failed for apply query with error: {result}");
                    return false;
                }

                MutatePathsForTopology(paths);

                int activeCount = paths.Count(p => (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0);
                if (activeCount == 0)
                {
                    logger.Error("No active displays to enable.");
                    return false;
                }

                var topologyFlags =
                    SetDisplayConfigFlags.TopologySupplied |
                    SetDisplayConfigFlags.Apply |
                    SetDisplayConfigFlags.AllowPathOrderChanges |
                    SetDisplayConfigFlags.VirtualModeAware;

                result = SetDisplayConfig(pathCount, paths, 0, null, topologyFlags);

                // Reaching recovery normally requires blank configuration database
                if (SettingsManager.Instance.Debug.ForceTopologyRecovery && result == ErrorSuccess)
                {
                    logger.Warn("[debugFlag: forceTopologyRecovery] Ignoring success and taking recovery path");
                    result = ErrorGenFailure;
                }

                if (result == ErrorSuccess)
                {
                    logger.Info("Successfully applied topology.");
                    return true;
                }

                if (result != ErrorGenFailure)
                {
                    logger.Error($"SetDisplayConfig failed to apply topology: Error {result}");
                    return false;
                }

                // Retry with supplied configuration on ERROR_GEN_FAILURE
                logger.Warn("Topology not in configuration database: retrying with supplied configuration (Error 31)");

                var recoveryFlags =
                    SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    SetDisplayConfigFlags.Apply |
                    SetDisplayConfigFlags.SaveToDatabase |
                    SetDisplayConfigFlags.VirtualModeAware;

                result = SetDisplayConfig(pathCount, paths, 0, null, recoveryFlags);

                if (result != ErrorSuccess)
                {
                    logger.Error($"Topology recovery failed: Error {result}");
                    return false;
                }

                // Refresh live configuration after recovery
                GetDisplayConfigs();

                logger.Info("Successfully applied topology and saved to configuration database.");
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

            logger.Info($"Deferring configuration until {TextHelper.Plural(expectedMonitors.Count, "enabled display")} stabilize...");

            while (verifiedTargetIds.Count < expectedMonitors.Count && deferWatch.ElapsedMilliseconds < deferTimeout)
            {
                var liveSnapshot = GetDisplayConfigs();
                foreach (var monitor in expectedMonitors)
                {
                    uint maskedProfileId = monitor.TargetId & 0xFFFF;
                    if (verifiedTargetIds.Contains(monitor.TargetId)) continue;

                    var match = liveSnapshot.FirstOrDefault(l => (l.TargetId & 0xFFFF) == maskedProfileId);
                    if (match != null && match.IsEnabled)
                    {
                        verifiedTargetIds.Add(monitor.TargetId);
                        string name = !string.IsNullOrEmpty(monitor.FriendlyName) ? monitor.FriendlyName : monitor.DeviceName;
                        logger.Debug($"{name} (TargetId {monitor.TargetId}) is active at {deferWatch.ElapsedMilliseconds}ms.");
                    }
                }

                if (verifiedTargetIds.Count < expectedMonitors.Count)
                    await Task.Delay(250);
            }
            deferWatch.Stop();

            if (verifiedTargetIds.Count == expectedMonitors.Count)
                logger.Info($"{TextHelper.Plural(expectedMonitors.Count, "display")} enabled and available in {deferWatch.ElapsedMilliseconds}ms.");
            else
            {
                var failedMonitors = expectedMonitors.Where(m => !verifiedTargetIds.Contains(m.TargetId));
                foreach (var failed in failedMonitors)
                {
                    string name = string.IsNullOrEmpty(failed.FriendlyName) ? failed.DeviceName : failed.FriendlyName;
                    logger.Warn($"TargetId {failed.TargetId} ({name}) failed to stabilize within timeout.");
                }
                logger.Error($"Display stabilization timed out -> only {verifiedTargetIds.Count}/{expectedMonitors.Count} displays ready.");
                return false;
            }

            return true;
        }

        public static bool ApplyDisplayLayout(List<DisplayConfigInfo> displayConfigs, out int errorCode)
        {
            errorCode = ErrorSuccess;

            try
            {
                logger.Info("Applying display layout...");

                var queryFlags = QueryDisplayConfigFlags.AllPaths | QueryDisplayConfigFlags.VirtualRefreshRateAware;
                if (GetDisplayConfigBufferSizes(queryFlags, out _, out _) != ErrorSuccess)
                    queryFlags = QueryDisplayConfigFlags.AllPaths;

                int result = GetDisplayConfigBufferSizes(queryFlags, out uint pathCount, out uint modeCount);
                if (result != ErrorSuccess)
                {
                    return false;
                }

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];
                result = QueryDisplayConfig(queryFlags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result != ErrorSuccess)
                {
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
                    var pIdx = GetLivePathIndex(paths, profile.TargetId);
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
                            uint liveHz = sig.vSyncFreq.Numerator > 1000 ? sig.vSyncFreq.Numerator / 1000 : sig.vSyncFreq.Numerator;
                            if (liveHz != (uint)profile.RefreshRate)
                            {
                                logger.Debug($"[RefreshRate] {mon}: Current={liveHz}Hz, Profile={profile.RefreshRate}Hz");
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

                // Record active paths before clearing flags
                var livePathByTarget = new Dictionary<uint, int>();
                for (int i = 0; i < paths.Length; i++)
                {
                    if ((paths[i].flags & (uint)DisplayConfigPathInfoFlags.Active) == 0) continue;

                    uint masked = paths[i].targetInfo.id & 0xFFFF;
                    if (!livePathByTarget.ContainsKey(masked))
                        livePathByTarget[masked] = i;
                }

                // Clear all active flags before rebuilding topology
                for (int i = 0; i < paths.Length; i++)
                    paths[i].flags &= ~(uint)DisplayConfigPathInfoFlags.Active;

                // All clone group members share one source mode entry, keyed by normalized SourceId
                var sourceIdToModeIdx = new Dictionary<uint, uint>();
                foreach (var profile in displayConfigs.Where(d => d.IsEnabled))
                {
                    if (!livePathByTarget.TryGetValue(profile.TargetId & 0xFFFF, out int pIdx))
                        pIdx = Array.FindIndex(paths, p => (p.targetInfo.id & 0xFFFF) == (profile.TargetId & 0xFFFF));

                    if (pIdx == -1) continue;

                    uint normalizedSourceId = sourceIdMap[profile.SourceId];
                    paths[pIdx].flags |= (uint)DisplayConfigPathInfoFlags.Active;
                    paths[pIdx].sourceInfo.id = normalizedSourceId;

                    if (profile.Rotation != 0)
                        paths[pIdx].targetInfo.rotation = (uint)profile.Rotation;

                    // Share one source mode entry across clone-group members
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
                result = SetDisplayConfig(
                    pathCount, paths,
                    modeCount, modes,
                    SetDisplayConfigFlags.Apply |
                    SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    SetDisplayConfigFlags.SaveToDatabase |
                    SetDisplayConfigFlags.AllowChanges |
                    SetDisplayConfigFlags.VirtualRefreshRateAware);

                if (result != ErrorSuccess)
                {
                    logger.Error($"SetDisplayConfig failed to apply layout: Error {result}");
                    return false;
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
                logger.Info($"Applying configuration for {TextHelper.Plural(displayConfigs.Count(d => d.IsEnabled), "enabled display")}...");

                // Exclude disconnected displays from defer set
                var availableTargetIds = GetPresentTargetIds();
                var liveConfigs = displayConfigs
                    .Where(d => d.IsEnabled && availableTargetIds.Contains(d.TargetId))
                    .ToList();

                // Defer until currently available displays stabilize
                var deferWatch = Stopwatch.StartNew();
                await DeferDisplayLayoutAsync(liveConfigs);
                deferWatch.Stop();

                // Apply resolution, position, and rotation atomically
                var layoutWatch = Stopwatch.StartNew();
                if (!ApplyDisplayLayout(displayConfigs, out int layoutErrorCode))
                {
                    if (layoutErrorCode == ErrorGenFailure)
                    {
                        logger.Warn("Display layout failed with Error 31 -> waiting for displays and retrying layout once");
                        await DeferDisplayLayoutAsync(liveConfigs);

                        if (!ApplyDisplayLayout(displayConfigs, out _))
                        {
                            logger.Error("Failed to apply display layout after Error 31 retry");
                            return false;
                        }
                    }
                    else
                    {
                        logger.Error("Failed to apply display layout");
                        return false;
                    }
                }
                layoutWatch.Stop();

                // Apply advanced color state after layout
                var hdrWatch = Stopwatch.StartNew();
                ApplyAdvancedColorState(displayConfigs);
                hdrWatch.Stop();

                // Apply color profiles after advanced color state is established
                var colorWatch = Stopwatch.StartNew();
                ApplyColorProfiles(displayConfigs);
                colorWatch.Stop();

                totalWatch.Stop();
                logger.Info($"Configured - Defer: {deferWatch.ElapsedMilliseconds}ms | Layout: {layoutWatch.ElapsedMilliseconds}ms | HDR: {hdrWatch.ElapsedMilliseconds}ms | Color: {colorWatch.ElapsedMilliseconds}ms | TOTAL: {totalWatch.ElapsedMilliseconds}ms");

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
            var liveConfigs = GetDisplayConfigs();
            bool allSuccessful = true;
            foreach (var profileDisplay in displayConfigs)
            {
                if (!profileDisplay.IsEnabled) continue;

                var activeDisplay = liveConfigs.FirstOrDefault(c => c.TargetId == profileDisplay.TargetId);
                if (activeDisplay == null)
                {
                    if (profileDisplay.IsHdrSupported)
                    {
                        logger.Warn($"Could not find active display matching TargetId {profileDisplay.TargetId} to apply advanced color.");
                        allSuccessful = false;
                    }
                    continue;
                }

                // Isolate per-device failures on virtual display paths
                try
                {
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
                            else if (!VerifyHdrState(activeDisplay.RawTargetId, profileDisplay.IsHdrEnabled))
                                logger.Warn($"HDR state for {activeDisplay.FriendlyName} did not verify as {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                        }
                        else
                            logger.Debug($"Skipping {activeDisplay.FriendlyName} -> HDR is already {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                    }

                    // ACM follows HDR when HDR is enabled
                    bool wantAcm = profileDisplay.IsHdrEnabled || profileDisplay.IsAcmEnabled;

                    if (wantAcm != activeDisplay.IsAcmEnabled)
                    {
                        logger.Info($"Setting {activeDisplay.FriendlyName} -> ACM to {(wantAcm ? "on" : "off")}");
                        if (!SetAcmState(activeDisplay.AdapterId, activeDisplay.RawTargetId, wantAcm))
                            logger.Warn($"ACM state change failed for {activeDisplay.FriendlyName} (expected on W11 pre-24H2 HDR displays).");
                    }
                    else
                        logger.Debug($"Skipping {activeDisplay.FriendlyName} -> ACM is already {(wantAcm ? "on" : "off")}");
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"Advanced color state failed for {activeDisplay.FriendlyName} (TargetId {activeDisplay.TargetId}): skipping");
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
                    logger.Info($"Set HDR to {enable} for RawTargetId {rawTargetId}");
                    return true;
                }

                logger.Error($"Failed to set HDR state for RawTargetId {rawTargetId}: Error {result}");
                return false;
            }
            // Pre-24H2: fall back to legacy advanced color path
            return SetAdvancedColorState(adapterId, rawTargetId, enable ? DisplayConfigColorIntent.Hdr : DisplayConfigColorIntent.Off);
        }

        public static bool SetAcmState(LUID adapterId, uint rawTargetId, bool enable)
        {
            if (IsWindows24H2OrGreater())
            {
                return SetWcgState(adapterId, rawTargetId, enable);
            }

            if (!enable)
            {
                return SetAdvancedColorState(adapterId, rawTargetId, DisplayConfigColorIntent.Off);
            }

            // Pre-24H2: ACM bit only works on SDR-only displays; on HDR-capable displays it maps to HDR
            var liveConfigs = GetDisplayConfigs();
            var display = liveConfigs.FirstOrDefault(c => c.RawTargetId == rawTargetId);
            if (display?.IsHdrSupported == true)
            {
                logger.Warn($"ACM is not supported on HDR-capable displays before Windows 11 24H2 (RawTargetId {rawTargetId})");
                return false;
            }

            return SetAdvancedColorState(adapterId, rawTargetId, DisplayConfigColorIntent.Acm);
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

        public static DisplayConfigInfo ResolveLiveDisplay(DisplaySetting setting, List<DisplayConfigInfo> liveConfigs)
        {
            if (setting == null || liveConfigs == null || liveConfigs.Count == 0) return null;

            uint masked = setting.TargetId & 0xFFFF;
            var onPort = liveConfigs.FirstOrDefault(c => (c.TargetId & 0xFFFF) == masked);

            if (onPort != null && (!setting.HasEdidIdentity || setting.MatchesEdid(onPort)))
            {
                return onPort;
            }

            var byEdid = setting.HasEdidIdentity ? liveConfigs.FirstOrDefault(c => setting.MatchesEdid(c)) : null;
            if (byEdid != null)
            {
                if (onPort == null)
                    logger.Warn($"'{setting.ReadableDeviceName}' moved from TargetId {masked} to {byEdid.TargetId & 0xFFFF} -> following monitor");
                else
                    logger.Warn($"TargetId {masked} now holds {onPort.ManufacturerName}{onPort.ProductCodeID}; '{setting.ReadableDeviceName}' is on TargetId {byEdid.TargetId & 0xFFFF} -> following monitor");

                return byEdid;
            }

            if (onPort != null && setting.HasEdidIdentity)
                logger.Warn($"TargetId {masked} holds {onPort.ManufacturerName}{onPort.ProductCodeID}, not captured {setting.ManufacturerName}{setting.ProductCodeID} -> applying to port anyway");

            return onPort;
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

        private static bool GetAdvancedColorInfo2(LUID adapterId, uint targetId, out DisplayConfigGetAdvancedColorInfo2 colorInfo)
        {
            colorInfo = new DisplayConfigGetAdvancedColorInfo2();
            colorInfo.header.type = DisplayConfigDeviceInfoType.GetAdvancedColorInfo2;
            colorInfo.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigGetAdvancedColorInfo2));
            colorInfo.header.adapterId = adapterId;
            colorInfo.header.id = targetId;

            return DisplayConfigGetDeviceInfo(ref colorInfo) == ErrorSuccess;
        }

        private static bool VerifyHdrState(uint rawTargetId, bool expectedEnabled)
        {
            const int maxAttempts = 3;
            const int delayMs = 100;

            // Re-query because HDR state may settle asynchronously
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                System.Threading.Thread.Sleep(delayMs);
                var live = GetDisplayConfigs().FirstOrDefault(c => c.RawTargetId == rawTargetId);
                if (live != null && live.IsHdrEnabled == expectedEnabled)
                {
                    return true;
                }
            }

            return false;
        }

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