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
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private static bool IsWindows11OrGreater() => Environment.OSVersion.Version.Build >= 22000;
        private static bool IsWindows22H2OrGreater() => Environment.OSVersion.Version.Build >= 22621;
        internal static bool IsWindows24H2OrGreater() => Environment.OSVersion.Version.Build >= 26100;
        internal static bool IsLegacyAcmSupported(bool isAdvancedColorSupported) => IsWindows22H2OrGreater() && !IsWindows24H2OrGreater() && isAdvancedColorSupported;

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

        internal delegate int GetDisplayConfigBufferSizesDelegate(
            QueryDisplayConfigFlags flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        internal delegate int QueryDisplayConfigDelegate(
            QueryDisplayConfigFlags flags,
            ref uint numPathArrayElements,
            DisplayConfigPathInfo[] pathArray,
            ref uint numModeInfoArrayElements,
            DisplayConfigModeInfo[] modeInfoArray,
            IntPtr currentTopologyId);

        internal delegate bool GetAdvancedColorInfo2Delegate(
            LUID adapterId,
            uint targetId,
            out DisplayConfigGetAdvancedColorInfo2 colorInfo);

        internal delegate bool GetLegacyAdvancedColorInfoDelegate(
            LUID adapterId,
            uint targetId,
            out DisplayConfigGetAdvancedColorInfo colorInfo);

        internal delegate int GetTargetPreferredModeDelegate(ref DisplayConfigTargetPreferredMode preferredMode);

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
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetPreferredMode preferredMode);
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

        internal enum LegacyAcmMutationResult
        {
            Applied,
            NonFatalProviderSkip,
            Failed
        }

        internal enum CurrentAddressResolutionStatus
        {
            Resolved,
            Absent,
            Ambiguous,
            Conflicting,
            InsufficientEvidence
        }

        #endregion

        #region Constants

        private const int ErrorSuccess = 0;
        private const int ErrorGenFailure = 31;
        private const int ErrorInvalidParameter = 87;
        private const int ErrorInsufficientBuffer = 122;
        internal const int QueryDisplayConfigMaxAttempts = 3;

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

            // Virtual-mode form only: lower 16 bits are cloneGroupId and upper 16 bits are sourceModeInfoIdx; call only with DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE
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

        [StructLayout(LayoutKind.Sequential)]
        internal struct DisplayConfigTargetPreferredMode
        {
            public DisplayConfigDeviceInfoHeader header;
            public uint width;
            public uint height;
            public DisplayConfigTargetMode targetMode;
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
            public uint bitsPerColorChannel;
            public DisplayConfigAdvancedColorMode activeColorMode;
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
            public uint value; // bit 0 = enableWcg
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
            public string MonitorDevicePath { get; set; } = string.Empty;
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
            public bool IsWcgSupported { get; set; } = false;
            public bool IsHdrEnabled { get; set; } = false;
            public bool IsWcgEnabled { get; set; } = false;
            public bool IsAdvancedColorInfoAvailable { get; set; } = true;
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
            paths = Array.Empty<DisplayConfigPathInfo>();
            modes = Array.Empty<DisplayConfigModeInfo>();
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

        #region Public Methods

        internal static void ApplyAdvancedColorInfo2(DisplayConfigInfo displayConfig, DisplayConfigGetAdvancedColorInfo2 colorInfo2)
        {
            if (displayConfig == null) throw new ArgumentNullException(nameof(displayConfig));

            var flags = colorInfo2.values;
            bool limitedByPolicy = (flags & DisplayConfigAdvancedColorInfo2Flags.AdvancedColorLimitedByPolicy) != 0;

            displayConfig.IsHdrSupported = !limitedByPolicy && (flags & DisplayConfigAdvancedColorInfo2Flags.HighDynamicRangeSupported) != 0;
            displayConfig.IsWcgSupported = !limitedByPolicy && (flags & DisplayConfigAdvancedColorInfo2Flags.WideColorSupported) != 0;
            displayConfig.IsHdrEnabled = colorInfo2.activeColorMode == DisplayConfigAdvancedColorMode.Hdr;
            displayConfig.IsWcgEnabled = colorInfo2.activeColorMode == DisplayConfigAdvancedColorMode.Wcg;
            displayConfig.IsAdvancedColorInfoAvailable = true;
            displayConfig.ColorEncoding = colorInfo2.colorEncoding;
            displayConfig.BitsPerColorChannel = colorInfo2.bitsPerColorChannel;
        }

        internal static void CaptureAdvancedColorState(
            DisplayConfigInfo displayConfig,
            LUID adapterId,
            uint targetId,
            bool useModernContract,
            GetAdvancedColorInfo2Delegate getInfo2,
            GetLegacyAdvancedColorInfoDelegate getLegacyInfo)
        {
            if (displayConfig == null) throw new ArgumentNullException(nameof(displayConfig));

            if (useModernContract)
            {
                if (getInfo2 == null) throw new ArgumentNullException(nameof(getInfo2));

                if (getInfo2(adapterId, targetId, out var colorInfo2))
                {
                    ApplyAdvancedColorInfo2(displayConfig, colorInfo2);
                    return;
                }

                MarkAdvancedColorUnavailable(displayConfig);
                _logger.Debug($"Failed to get Advanced Color INFO_2 for {displayConfig.DeviceName}; HDR/WCG capability and state remain unavailable.");
                return;
            }

            if (getLegacyInfo == null) throw new ArgumentNullException(nameof(getLegacyInfo));

            if (getLegacyInfo(adapterId, targetId, out var colorInfo))
            {
                ApplyLegacyAdvancedColorInfo(displayConfig, colorInfo);
                return;
            }

            MarkAdvancedColorUnavailable(displayConfig);
            _logger.Debug($"Failed to get legacy Advanced Color info for {displayConfig.DeviceName}; Advanced Color state remains unavailable.");
        }

        private static void ApplyLegacyAdvancedColorInfo(DisplayConfigInfo displayConfig, DisplayConfigGetAdvancedColorInfo colorInfo)
        {
            var flags = colorInfo.values;
            bool isSupported = (flags & DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported) != 0;
            bool isEnabled = (flags & DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled) != 0;
            bool isForceDisabled = (flags & DisplayConfigAdvancedColorInfoFlags.AdvancedColorForceDisabled) != 0;
            bool finalSupported = isSupported && !isForceDisabled;
            bool isHdrEncoding = colorInfo.colorEncoding == DisplayConfigColorEncoding.YCbCr444;

            displayConfig.IsHdrSupported = finalSupported;
            displayConfig.IsWcgSupported = false;
            displayConfig.IsHdrEnabled = isEnabled && isHdrEncoding;
            displayConfig.IsWcgEnabled = isEnabled && !isHdrEncoding;
            displayConfig.IsAdvancedColorInfoAvailable = true;
            displayConfig.ColorEncoding = colorInfo.colorEncoding;
            displayConfig.BitsPerColorChannel = (uint)colorInfo.bitsPerColorChannel;
        }

        private static void MarkAdvancedColorUnavailable(DisplayConfigInfo displayConfig)
        {
            displayConfig.IsHdrSupported = false;
            displayConfig.IsWcgSupported = false;
            displayConfig.IsHdrEnabled = false;
            displayConfig.IsWcgEnabled = false;
            displayConfig.IsAdvancedColorInfoAvailable = false;
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

        public static List<DisplayConfigInfo> GetDisplayConfigs()
        {
            var displays = new List<DisplayConfigInfo>();

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
                        displayConfig.MonitorDevicePath = targetName.monitorDevicePath;
                    }

                    PopulatePreferredResolution(displayConfig, path.targetInfo.adapterId, path.targetInfo.id);

                    // Advanced color state (HDR/WCG)
                    CaptureAdvancedColorState(
                        displayConfig,
                        path.targetInfo.adapterId,
                        path.targetInfo.id,
                        IsWindows24H2OrGreater(),
                        GetAdvancedColorInfo2,
                        GetLegacyAdvancedColorInfo);

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
                    return displaysByTarget.Values.ToList();
                }

                for (uint i = 0; i < pathCount; i++)
                {
                    var path = paths[i];
                    var targetKey = CcdAddress.Target(path.targetInfo.adapterId, path.targetInfo.id);
                    bool isActive = (path.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0;

                    if (displaysByTarget.TryGetValue(targetKey, out var existing) && (existing.IsEnabled || !isActive)) continue;

                    var displayConfig = new DisplayConfigInfo
                    {
                        PathIndex = i,
                        IsEnabled = isActive,
                        AdapterId = path.targetInfo.adapterId,
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

            return displaysByTarget.Values.ToList();
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
                    result.Add(CcdAddress.Target(path.targetInfo.adapterId, path.targetInfo.id));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error querying all-paths target presence");
            }

            return result;
        }

        private static int GetLivePathIndex(DisplayConfigPathInfo[] paths, LUID adapterId, uint targetId)
        {
            var key = CcdAddress.Target(adapterId, targetId);
            int active = Array.FindIndex(paths, p => CcdAddress.Target(p.targetInfo.adapterId, p.targetInfo.id).Equals(key) && (p.flags & (uint)DisplayConfigPathInfoFlags.Active) != 0);
            return active >= 0 ? active : Array.FindIndex(paths, p => CcdAddress.Target(p.targetInfo.adapterId, p.targetInfo.id).Equals(key));
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

        internal static bool TopologyRequiresUpdate(
            DisplayConfigPathInfo[] paths,
            List<DisplayConfigInfo> displayConfigs)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (displayConfigs == null) throw new ArgumentNullException(nameof(displayConfigs));

            bool needsUpdate = false;
            var profileLookup = displayConfigs.ToDictionary(CcdAddress.Target);
            var sourceIdMap = BuildSourceIdMap(displayConfigs);

            foreach (var group in paths.GroupBy(p => CcdAddress.Target(p.targetInfo.adapterId, p.targetInfo.id)))
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

        internal static void PreparePathsForTopology(
            DisplayConfigPathInfo[] targetPaths,
            List<DisplayConfigInfo> displayConfigs)
        {
            if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
            if (displayConfigs == null) throw new ArgumentNullException(nameof(displayConfigs));

            var targetIdToPathIndex = new Dictionary<CcdTargetKey, int>();
            for (int i = 0; i < targetPaths.Length; i++)
            {
                var targetKey = CcdAddress.Target(targetPaths[i].targetInfo.adapterId, targetPaths[i].targetInfo.id);
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

        public static bool ApplyDisplayTopology(List<DisplayConfigInfo> displayConfigs)
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
                if (SettingsManager.Instance.Debug.ForceTopologyRecovery && result == ErrorSuccess)
                {
                    _logger.Warn("[debugFlag: forceTopologyRecovery] Ignoring success and taking recovery path");
                    result = ErrorGenFailure;
                }

                if (result == ErrorSuccess)
                {
                    _logger.Info("Applied display topology.");
                    return true;
                }

                if (result != ErrorGenFailure)
                {
                    _logger.Error($"SetDisplayConfig failed to apply topology: Error {result}");
                    return false;
                }

                // Retry with supplied configuration on ERROR_GEN_FAILURE
                _logger.Warn("Topology not in configuration database: retrying with supplied configuration (Error 31)");

                var recoveryFlags =
                    SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    SetDisplayConfigFlags.Apply |
                    SetDisplayConfigFlags.SaveToDatabase |
                    SetDisplayConfigFlags.VirtualModeAware;

                result = SetDisplayConfig(pathCount, paths, 0, null, recoveryFlags);

                if (result != ErrorSuccess)
                {
                    _logger.Error($"Topology recovery failed: Error {result}");
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

        public static async Task<bool> DeferDisplayLayoutAsync(List<DisplayConfigInfo> displayConfigs, int deferTimeout = 10000)
        {
            var deferWatch = Stopwatch.StartNew();
            var expectedMonitors = displayConfigs.Where(d => d.IsEnabled).ToList();
            var verifiedTargetIds = new HashSet<CcdTargetKey>();

            _logger.Info($"Deferring configuration until {TextHelper.Plural(expectedMonitors.Count, "enabled display")} stabilize...");

            while (verifiedTargetIds.Count < expectedMonitors.Count && deferWatch.ElapsedMilliseconds < deferTimeout)
            {
                var liveSnapshot = GetDisplayConfigs();
                foreach (var monitor in expectedMonitors)
                {
                    var targetKey = CcdAddress.Target(monitor);
                    if (verifiedTargetIds.Contains(targetKey)) continue;

                    var match = liveSnapshot.FirstOrDefault(l => CcdAddress.Target(l).Equals(targetKey));
                    if (match != null && match.IsEnabled)
                    {
                        verifiedTargetIds.Add(targetKey);
                        string name = !string.IsNullOrEmpty(monitor.FriendlyName) ? monitor.FriendlyName : monitor.DeviceName;
                        _logger.Debug($"{name} ({targetKey}) is active at {deferWatch.ElapsedMilliseconds}ms");
                    }
                }

                if (verifiedTargetIds.Count < expectedMonitors.Count)
                    await Task.Delay(250);
            }
            deferWatch.Stop();

            if (verifiedTargetIds.Count == expectedMonitors.Count)
                _logger.Info($"{TextHelper.Plural(expectedMonitors.Count, "display")} enabled and available in {deferWatch.ElapsedMilliseconds}ms");
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
                if (IsWindows11OrGreater())
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

                    var targetKey = CcdAddress.Target(paths[i].targetInfo.adapterId, paths[i].targetInfo.id);
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
                        pIdx = Array.FindIndex(paths, p => CcdAddress.Target(p.targetInfo.adapterId, p.targetInfo.id).Equals(targetKey));

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
                if (IsWindows11OrGreater())
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

        internal static async Task<DisplayConfigApplyResult> ApplyDisplayConfigDetailed(List<DisplayConfigInfo> displayConfigs)
        {
            var applyResult = new DisplayConfigApplyResult();

            try
            {
                var totalWatch = Stopwatch.StartNew();
                _logger.Info($"Applying configuration for {TextHelper.Plural(displayConfigs.Count(d => d.IsEnabled), "enabled display")}...");

                // Exclude displays absent from enumeration from defer set
                var allPathTargets = GetAllPathTargets();
                var liveConfigs = displayConfigs.Where(d => d.IsEnabled && allPathTargets.Contains(CcdAddress.Target(d))).ToList();

                // Defer until expected displays stabilize
                var deferWatch = Stopwatch.StartNew();
                await DeferDisplayLayoutAsync(liveConfigs);
                deferWatch.Stop();

                // Apply resolution, position, and rotation atomically
                var layoutWatch = Stopwatch.StartNew();
                if (!ApplyDisplayLayout(displayConfigs, out int layoutErrorCode))
                {
                    if (layoutErrorCode == ErrorGenFailure)
                    {
                        _logger.Warn("Display layout failed with Error 31 -> waiting for displays and retrying layout once");
                        await DeferDisplayLayoutAsync(liveConfigs);

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
                applyResult.AdvancedColorSuccess = ApplyAdvancedColorState(displayConfigs);
                hdrWatch.Stop();

                var colorWatch = Stopwatch.StartNew();
                applyResult.ColorProfileSuccess = ApplyColorProfiles(displayConfigs);
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

        public static bool ApplyAdvancedColorState(List<DisplayConfigInfo> displayConfigs)
        {
            _logger.Info("Applying Advanced Color state...");

            return ApplyAdvancedColorState(
                displayConfigs,
                GetDisplayConfigs(),
                IsWindows24H2OrGreater(),
                SetHdrState,
                SetWcgState,
                SetLegacyAcmState);
        }

        internal static DisplayConfigAdvancedColorMode GetEffectiveAdvancedColorMode(bool hdrEnabled, bool wcgEnabled) => hdrEnabled ? DisplayConfigAdvancedColorMode.Hdr : (wcgEnabled ? DisplayConfigAdvancedColorMode.Wcg : DisplayConfigAdvancedColorMode.Sdr);

        internal static LegacyAcmMutationResult ClassifyLegacyAcmMutationResult(
            bool setterSucceeded,
            bool enable,
            bool isHdrSupported)
        {
            if (setterSucceeded)
            {
                return LegacyAcmMutationResult.Applied;
            }

            if (enable && isHdrSupported)
            {
                return LegacyAcmMutationResult.NonFatalProviderSkip;
            }

            return LegacyAcmMutationResult.Failed;
        }

        internal static bool ApplyAdvancedColorState(
            List<DisplayConfigInfo> displayConfigs,
            List<DisplayConfigInfo> liveConfigs,
            bool isWindows24H2OrGreater,
            Func<LUID, uint, bool, bool> setHdrState,
            Func<LUID, uint, bool, bool> setWcgState,
            Func<LUID, uint, bool, bool> setLegacyAcmState)
        {
            bool allSuccessful = true;
            foreach (var profileDisplay in displayConfigs)
            {
                if (!profileDisplay.IsEnabled) continue;

                var targetKey = CcdAddress.Target(profileDisplay);
                var activeDisplay = liveConfigs.FirstOrDefault(c => CcdAddress.Target(c).Equals(targetKey));
                if (activeDisplay == null)
                {
                    if (profileDisplay.IsHdrSupported || profileDisplay.IsHdrEnabled || profileDisplay.IsWcgEnabled)
                    {
                        _logger.Warn($"Could not find active display matching TargetId {profileDisplay.TargetId} to apply Advanced Color");
                        allSuccessful = false;
                    }
                    continue;
                }

                try
                {
                    if (isWindows24H2OrGreater)
                    {
                        if (!activeDisplay.IsAdvancedColorInfoAvailable)
                        {
                            _logger.Warn($"Cannot apply Advanced Color for {activeDisplay.FriendlyName}: live HDR/WCG state is unavailable");
                            allSuccessful = false;
                            continue;
                        }

                        var currentMode = GetEffectiveAdvancedColorMode(activeDisplay.IsHdrEnabled, activeDisplay.IsWcgEnabled);
                        var desiredMode = GetEffectiveAdvancedColorMode(profileDisplay.IsHdrEnabled, profileDisplay.IsWcgEnabled);

                        if (currentMode == desiredMode)
                        {
                            _logger.Debug($"Skipping Advanced Color for {activeDisplay.FriendlyName} -> already {desiredMode}");
                            continue;
                        }

                        if ((currentMode == DisplayConfigAdvancedColorMode.Hdr || desiredMode == DisplayConfigAdvancedColorMode.Hdr) && !activeDisplay.IsHdrSupported)
                        {
                            _logger.Warn($"Cannot change HDR state for {activeDisplay.FriendlyName}: HDR is not currently supported or is limited by policy");
                            allSuccessful = false;
                            continue;
                        }

                        if ((currentMode == DisplayConfigAdvancedColorMode.Wcg || desiredMode == DisplayConfigAdvancedColorMode.Wcg) && !activeDisplay.IsWcgSupported)
                        {
                            _logger.Warn($"Cannot change WCG state for {activeDisplay.FriendlyName}: WCG is not currently supported or is limited by policy");
                            allSuccessful = false;
                            continue;
                        }

                        if (desiredMode == DisplayConfigAdvancedColorMode.Hdr)
                        {
                            _logger.Info($"Setting {activeDisplay.FriendlyName} -> Advanced Color to HDR");
                            if (!setHdrState(activeDisplay.AdapterId, activeDisplay.RawTargetId, true))
                            {
                                _logger.Error($"Failed to apply HDR setting for {activeDisplay.FriendlyName}");
                                allSuccessful = false;
                                continue;
                            }

                            // Effective Advanced Color destination remains HDR regardless of the context-dependent WCG user bit
                            continue;
                        }

                        if (currentMode == DisplayConfigAdvancedColorMode.Hdr)
                        {
                            _logger.Info($"Setting {activeDisplay.FriendlyName} -> HDR off before {desiredMode}");
                            if (!setHdrState(activeDisplay.AdapterId, activeDisplay.RawTargetId, false))
                            {
                                _logger.Error($"Failed to disable HDR for {activeDisplay.FriendlyName}");
                                allSuccessful = false;
                                continue;
                            }
                        }

                        bool wantWcg = desiredMode == DisplayConfigAdvancedColorMode.Wcg;
                        if (!wantWcg && currentMode == DisplayConfigAdvancedColorMode.Hdr && !activeDisplay.IsWcgSupported)
                        {
                            _logger.Debug($"Skipping WCG-off mutation for {activeDisplay.FriendlyName} -> WCG is not supported on this target");
                            continue;
                        }

                        _logger.Info($"Setting {activeDisplay.FriendlyName} -> WCG to {(wantWcg ? "on" : "off")}");
                        if (!setWcgState(activeDisplay.AdapterId, activeDisplay.RawTargetId, wantWcg))
                        {
                            _logger.Error($"Failed to apply WCG setting for {activeDisplay.FriendlyName}");
                            allSuccessful = false;
                        }

                        continue;
                    }

                    // Pre-24H2, legacy provider exposes HDR on HDR-capable targets and ACM otherwise
                    if (profileDisplay.IsHdrEnabled && !activeDisplay.IsHdrSupported)
                    {
                        _logger.Warn($"Cannot apply HDR for {activeDisplay.FriendlyName}: HDR is not supported by the live target");
                        allSuccessful = false;
                        continue;
                    }

                    if (activeDisplay.IsHdrSupported && activeDisplay.IsHdrEnabled != profileDisplay.IsHdrEnabled)
                    {
                        _logger.Info($"Setting {activeDisplay.FriendlyName} -> HDR to {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                        if (!setHdrState(activeDisplay.AdapterId, activeDisplay.RawTargetId, profileDisplay.IsHdrEnabled))
                        {
                            _logger.Error($"Failed to apply HDR setting for {activeDisplay.FriendlyName}");
                            allSuccessful = false;
                            continue;
                        }
                    }
                    else if (activeDisplay.IsHdrSupported)
                    {
                        _logger.Debug($"Skipping HDR for {activeDisplay.FriendlyName} -> already {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                    }

                    if (profileDisplay.IsHdrEnabled) continue;

                    bool wantLegacyAcm = profileDisplay.IsWcgEnabled;
                    if (wantLegacyAcm != activeDisplay.IsWcgEnabled)
                    {
                        _logger.Info($"Setting {activeDisplay.FriendlyName} -> legacy ACM to {(wantLegacyAcm ? "on" : "off")}");
                        bool setterSucceeded = setLegacyAcmState(activeDisplay.AdapterId, activeDisplay.RawTargetId, wantLegacyAcm);
                        var mutationResult = ClassifyLegacyAcmMutationResult(setterSucceeded, wantLegacyAcm, activeDisplay.IsHdrSupported);

                        if (mutationResult == LegacyAcmMutationResult.Failed)
                        {
                            _logger.Error($"Failed to apply legacy ACM setting for {activeDisplay.FriendlyName}");
                            allSuccessful = false;
                        }
                    }
                    else
                        _logger.Debug($"Skipping legacy ACM for {activeDisplay.FriendlyName} -> already {(wantLegacyAcm ? "on" : "off")}");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Advanced color state failed for {activeDisplay.FriendlyName} (TargetId {activeDisplay.TargetId})");
                    allSuccessful = false;
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
                    _logger.Info($"Set advanced color to {intent} for RawTargetId {rawTargetId}");
                    return true;
                }

                _logger.Error($"Failed to set advanced color for RawTargetId {rawTargetId}: Error {result}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error setting advanced color for RawTargetId {rawTargetId}");
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
                    _logger.Info($"Set HDR to {enable} for RawTargetId {rawTargetId}");
                    return true;
                }

                _logger.Error($"Failed to set HDR state for RawTargetId {rawTargetId}: Error {result}");
                return false;
            }
            // Pre-24H2, fall back to legacy Advanced Color path
            return SetAdvancedColorState(adapterId, rawTargetId, enable ? DisplayConfigColorIntent.Hdr : DisplayConfigColorIntent.Off);
        }

        public static bool SetLegacyAcmState(LUID adapterId, uint rawTargetId, bool enable)
        {
            if (!enable)
            {
                return SetAdvancedColorState(adapterId, rawTargetId, DisplayConfigColorIntent.Off);
            }

            // Pre-24H2, legacy Advanced Color switch represents HDR on HDR-capable providers and ACM otherwise
            var liveConfigs = GetDisplayConfigs();
            var display = liveConfigs.FirstOrDefault(c => CcdAddress.LuidEquals(c.AdapterId, adapterId) && c.RawTargetId == rawTargetId);
            if (display?.IsHdrSupported == true)
            {
                _logger.Warn($"ACM is not supported on HDR-capable displays before Windows 11 24H2 (RawTargetId {rawTargetId})");
                return false;
            }

            return SetAdvancedColorState(adapterId, rawTargetId, DisplayConfigColorIntent.Acm);
        }

        public static bool ApplyColorProfiles(List<DisplayConfigInfo> displayConfigs)
        {
            _logger.Info("Applying color profiles...");
            return ApplyColorProfiles(displayConfigs, GetDisplayConfigs(), ColorProfileHelper.ApplyColorProfile);
        }

        internal static bool ApplyColorProfiles(
            List<DisplayConfigInfo> displayConfigs,
            List<DisplayConfigInfo> liveConfigs,
            Func<DisplaySetting, List<DisplayConfigInfo>, bool> applyColorProfile)
        {
            bool allSuccessful = true;

            foreach (var profileDisplay in displayConfigs)
            {
                if (!profileDisplay.IsEnabled || string.IsNullOrEmpty(profileDisplay.ColorProfile)) continue;

                var targetKey = CcdAddress.Target(profileDisplay);
                var activeDisplay = liveConfigs.FirstOrDefault(c => CcdAddress.Target(c).Equals(targetKey));
                if (activeDisplay == null)
                {
                    _logger.Warn($"Could not find active display matching target {targetKey} to apply color profile");
                    allSuccessful = false;
                    continue;
                }

                var setting = new DisplaySetting
                {
                    DeviceName = activeDisplay.DeviceName,
                    AdapterLuid = activeDisplay.AdapterId,
                    TargetId = activeDisplay.TargetId,
                    ColorProfile = profileDisplay.ColorProfile,
                    IsEnabled = profileDisplay.IsEnabled
                };

                if (!applyColorProfile(setting, liveConfigs))
                    allSuccessful = false;
            }

            return allSuccessful;
        }

        internal static List<DisplayConfigInfo> CanonicalizeDisplayEndpoints(IEnumerable<DisplayConfigInfo> displayConfigs)
        {
            var endpoints = new Dictionary<CcdTargetKey, DisplayConfigInfo>();
            if (displayConfigs == null)
            {
                return endpoints.Values.ToList();
            }

            foreach (var display in displayConfigs)
            {
                if (display == null) continue;

                var key = CcdAddress.Target(display);
                if (!endpoints.TryGetValue(key, out var existing) || (!existing.IsEnabled && display.IsEnabled))
                    endpoints[key] = display;
            }

            return endpoints.Values.ToList();
        }

        internal static DisplayConfigInfo ResolveCurrentApplyAddress(
            DisplaySetting setting,
            List<DisplayConfigInfo> currentEndpoints) =>
            ResolveCurrentApplyAddressDetailed(setting, currentEndpoints, out _);

        internal static DisplayConfigInfo ResolveCurrentApplyAddressDetailed(
            DisplaySetting setting,
            List<DisplayConfigInfo> currentEndpoints,
            out CurrentAddressResolutionStatus status)
        {
            status = CurrentAddressResolutionStatus.Absent;
            if (setting == null || currentEndpoints == null || currentEndpoints.Count == 0)
            {
                return null;
            }

            var endpoints = CanonicalizeDisplayEndpoints(currentEndpoints);
            uint maskedTargetId = setting.TargetId & 0xFFFF;
            var targetMatches = endpoints.Where(c => (c.TargetId & 0xFFFF) == maskedTargetId).ToList();
            var savedPort = targetMatches.Count == 1 ? targetMatches[0] : null;

            if (savedPort != null && (!setting.HasEdidIdentity || setting.MatchesEdid(savedPort)))
            {
                status = CurrentAddressResolutionStatus.Resolved;
                return savedPort;
            }

            var edidMatches = setting.HasEdidIdentity
                ? endpoints.Where(setting.MatchesEdid).ToList()
                : new List<DisplayConfigInfo>();

            if (edidMatches.Count == 1)
            {
                var byEdid = edidMatches[0];
                if (savedPort == null)
                    _logger.Warn($"'{setting.ReadableDeviceName}' moved from saved target {maskedTargetId} to {CcdAddress.Target(byEdid)} -> following unique EDID match");
                else
                    _logger.Warn($"Saved target {maskedTargetId} now holds {savedPort.ManufacturerName}{savedPort.ProductCodeID}; '{setting.ReadableDeviceName}' is on {CcdAddress.Target(byEdid)} -> following unique EDID match");

                status = CurrentAddressResolutionStatus.Resolved;
                return byEdid;
            }

            if (savedPort != null)
            {
                _logger.Warn($"Saved target {maskedTargetId} does not uniquely match captured EDID {setting.ManufacturerName}{setting.ProductCodeID}; retaining the unambiguous current port as a location-role fallback");
                status = CurrentAddressResolutionStatus.Resolved;
                return savedPort;
            }

            if (targetMatches.Count > 1 || edidMatches.Count > 1)
            {
                _logger.Warn($"Current display evidence for '{setting.ReadableDeviceName}' is ambiguous across adapter-qualified endpoints -> refusing address selection");
                status = CurrentAddressResolutionStatus.Ambiguous;
                return null;
            }

            status = CurrentAddressResolutionStatus.Absent;
            return null;
        }

        public static DisplayConfigInfo ResolveLiveDisplay(DisplaySetting setting, List<DisplayConfigInfo> liveConfigs) => ResolveCurrentApplyAddressDetailed(setting, liveConfigs, out _);
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

        #endregion

        #region Private Methods

        private static bool GetLegacyAdvancedColorInfo(LUID adapterId, uint targetId, out DisplayConfigGetAdvancedColorInfo colorInfo)
        {
            colorInfo = new DisplayConfigGetAdvancedColorInfo();
            colorInfo.header.type = DisplayConfigDeviceInfoType.GetAdvancedColorInfo;
            colorInfo.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigGetAdvancedColorInfo));
            colorInfo.header.adapterId = adapterId;
            colorInfo.header.id = targetId;

            return DisplayConfigGetDeviceInfo(ref colorInfo) == ErrorSuccess;
        }

        private static bool GetAdvancedColorInfo2(LUID adapterId, uint targetId, out DisplayConfigGetAdvancedColorInfo2 colorInfo)
        {
            colorInfo = new DisplayConfigGetAdvancedColorInfo2();
            colorInfo.header.type = DisplayConfigDeviceInfoType.GetAdvancedColorInfo2;
            colorInfo.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigGetAdvancedColorInfo2));
            colorInfo.header.adapterId = adapterId;
            colorInfo.header.id = targetId;

            return DisplayConfigGetDeviceInfo(ref colorInfo) == ErrorSuccess;
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
                _logger.Info($"Set WCG to {enable} for RawTargetId {rawTargetId}");
                return true;
            }

            _logger.Error($"Failed to set WCG state for RawTargetId {rawTargetId}: Error {result}");
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
