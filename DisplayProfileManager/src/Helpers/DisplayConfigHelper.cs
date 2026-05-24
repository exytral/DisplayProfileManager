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
        #region P/Invoke Declarations

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(
            QueryDisplayConfigFlags flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            QueryDisplayConfigFlags flags,
            ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(
            uint numPathArrayElements,
            [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements,
            [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            SetDisplayConfigFlags flags);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO colorInfo);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE colorState);

        #endregion

        #region Constants

        private const int ERROR_SUCCESS = 0;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const int ERROR_GEN_FAILURE = 31;
        private const int ERROR_INVALID_PARAMETER = 87;

        private const uint DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID = 0xffff;

        #endregion

        #region Enums

        [Flags]
        public enum QueryDisplayConfigFlags : uint
        {
            QDC_ALL_PATHS = 0x00000001,
            QDC_ONLY_ACTIVE_PATHS = 0x00000002,
            QDC_DATABASE_CURRENT = 0x00000004,
            QDC_VIRTUAL_MODE_AWARE = 0x00000010,
            QDC_INCLUDE_HMD = 0x00000020,
        }

        [Flags]
        public enum SetDisplayConfigFlags : uint
        {
            SDC_TOPOLOGY_INTERNAL = 0x00000001,
            SDC_TOPOLOGY_CLONE = 0x00000002,
            SDC_TOPOLOGY_EXTEND = 0x00000004,
            SDC_TOPOLOGY_EXTERNAL = 0x00000008,
            SDC_TOPOLOGY_SUPPLIED = 0x00000010,
            SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020,
            SDC_VALIDATE = 0x00000040,
            SDC_APPLY = 0x00000080,
            SDC_NO_OPTIMIZATION = 0x00000100,
            SDC_SAVE_TO_DATABASE = 0x00000200,
            SDC_ALLOW_CHANGES = 0x00000400,
            SDC_PATH_PERSIST_IF_REQUIRED = 0x00000800,
            SDC_FORCE_MODE_ENUMERATION = 0x00001000,
            SDC_ALLOW_PATH_ORDER_CHANGES = 0x00002000,
            SDC_VIRTUAL_MODE_AWARE = 0x00008000,
        }

        [Flags]
        public enum DisplayConfigPathInfoFlags : uint
        {
            DISPLAYCONFIG_PATH_ACTIVE = 0x00000001,
            DISPLAYCONFIG_PATH_PREFERRED_UNSCALED = 0x00000004,
            DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 0x00000008,
            DISPLAYCONFIG_PATH_VALID_FLAGS = 0x0000000D,
        }

        public enum DisplayConfigVideoOutputTechnology : uint
        {
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = 0xFFFFFFFF,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 = 0,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO = 1,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO = 2,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO = 3,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI = 4,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI = 5,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN = 8,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI = 9,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL = 10,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL = 12,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE = 14,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST = 15,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED = 16,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL = 17,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_FORCE_UINT32 = 0xFFFFFFFF
        }

        public enum DisplayConfigModeInfoType : uint
        {
            DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1,
            DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2,
            DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE = 3,
            DISPLAYCONFIG_MODE_INFO_TYPE_FORCE_UINT32 = 0xFFFFFFFF
        }

        public enum DisplayConfigDeviceInfoType : uint
        {
            DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1,
            DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2,
            DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE = 3,
            DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME = 4,
            DISPLAYCONFIG_DEVICE_INFO_SET_TARGET_PERSISTENCE = 5,
            DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_BASE_TYPE = 6,
            DISPLAYCONFIG_DEVICE_INFO_GET_SUPPORT_VIRTUAL_RESOLUTION = 7,
            DISPLAYCONFIG_DEVICE_INFO_SET_SUPPORT_VIRTUAL_RESOLUTION = 8,
            DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9,
            DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10,
            DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11,
            DISPLAYCONFIG_DEVICE_INFO_FORCE_UINT32 = 0xFFFFFFFF
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
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx; // Encodes CloneGroupId (lower 16 bits)
            public uint statusFlags;

            // Clears source mode index and sets clone group; required for SDC_TOPOLOGY_SUPPLIED
            public void ResetModeAndSetCloneGroup(uint cloneGroup)
            {
                modeInfoIdx = (DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID << 16) | cloneGroup;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public DisplayConfigVideoOutputTechnology outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_2DREGION
        {
            public uint cx;
            public uint cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
        {
            public ulong pixelRate;
            public DISPLAYCONFIG_RATIONAL hSyncFreq;
            public DISPLAYCONFIG_RATIONAL vSyncFreq;
            public DISPLAYCONFIG_2DREGION activeSize;
            public DISPLAYCONFIG_2DREGION totalSize;
            public uint videoStandard;
            public uint scanLineOrdering;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SOURCE_MODE
        {
            public uint width;
            public uint height;
            public uint pixelFormat;
            public POINTL position;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_TARGET_MODE
        {
            public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
        {
            public POINTL PathSourceSize;
            public RECTL DesktopImageRegion;
            public RECTL DesktopImageClip;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECTL
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct DISPLAYCONFIG_MODE_INFO_UNION
        {
            [FieldOffset(0)]
            public DISPLAYCONFIG_TARGET_MODE targetMode;

            [FieldOffset(0)]
            public DISPLAYCONFIG_SOURCE_MODE sourceMode;

            [FieldOffset(0)]
            public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_MODE_INFO
        {
            public DisplayConfigModeInfoType infoType;
            public uint id;
            public LUID adapterId;
            public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
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
        public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public DisplayConfigDeviceInfoType type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        public const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xffffffff;

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public DISPLAYCONFIG_ADVANCED_COLOR_INFO_FLAGS values;
            public DISPLAYCONFIG_COLOR_ENCODING colorEncoding;
            public int bitsPerColorChannel;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public DISPLAYCONFIG_SET_ADVANCED_COLOR_FLAGS values;
        }

        [Flags]
        public enum DISPLAYCONFIG_ADVANCED_COLOR_INFO_FLAGS : uint
        {
            // A type of advanced color is supported
            AdvancedColorSupported = 0x1,
            // A type of advanced color is enabled  
            AdvancedColorEnabled = 0x2,
            // Wide color gamut is enabled
            WideColorEnforced = 0x4,
            // Advanced color is force disabled due to system/OS policy
            AdvancedColorForceDisabled = 0x8
        }

        [Flags]
        public enum DISPLAYCONFIG_SET_ADVANCED_COLOR_FLAGS : uint
        {
            EnableAdvancedColor = 0x1
        }

        public enum DISPLAYCONFIG_COLOR_ENCODING : uint
        {
            DISPLAYCONFIG_COLOR_ENCODING_RGB = 0,
            DISPLAYCONFIG_COLOR_ENCODING_YCBCR444 = 1,
            DISPLAYCONFIG_COLOR_ENCODING_YCBCR422 = 2,
            DISPLAYCONFIG_COLOR_ENCODING_YCBCR420 = 3,
            DISPLAYCONFIG_COLOR_ENCODING_INTENSITY = 4,
            DISPLAYCONFIG_COLOR_ENCODING_FORCE_UINT32 = 0xFFFFFFFF
        }

        public enum DISPLAYCONFIG_ROTATION : uint
        {
            DISPLAYCONFIG_ROTATION_IDENTITY = 1,
            DISPLAYCONFIG_ROTATION_ROTATE90 = 2,
            DISPLAYCONFIG_ROTATION_ROTATE180 = 3,
            DISPLAYCONFIG_ROTATION_ROTATE270 = 4,
            DISPLAYCONFIG_ROTATION_FORCE_UINT32 = 0xFFFFFFFF
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
            public DISPLAYCONFIG_ROTATION Rotation { get; set; } = DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY;
            public bool IsHdrSupported { get; set; } = false;
            public bool IsHdrEnabled { get; set; } = false;
            public DISPLAYCONFIG_COLOR_ENCODING ColorEncoding { get; set; } = DISPLAYCONFIG_COLOR_ENCODING.DISPLAYCONFIG_COLOR_ENCODING_RGB;
            public uint BitsPerColorChannel { get; set; } = 8;

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
                uint pathCount = 0;
                uint modeCount = 0;

                // Get buffer sizes for active paths
                int result = GetDisplayConfigBufferSizes(
                    QueryDisplayConfigFlags.QDC_ONLY_ACTIVE_PATHS,
                    out pathCount,
                    out modeCount);

                if (result != ERROR_SUCCESS)
                {
                    logger.Error($"GetDisplayConfigBufferSizes failed with error: {result}");
                    return displays;
                }

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

                // Query active display paths
                result = QueryDisplayConfig(
                    QueryDisplayConfigFlags.QDC_ONLY_ACTIVE_PATHS,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero);

                if (result != ERROR_SUCCESS)
                {
                    logger.Error($"QueryDisplayConfig failed with error: {result}");
                    return displays;
                }

                // Process each path
                for (uint i = 0; i < pathCount; i++)
                {
                    var path = paths[i];

                    // Only process paths with available targets
                    if (!path.targetInfo.targetAvailable)
                        continue;

                    // Only process ACTIVE paths during detection
                    bool isActive = (path.flags & (uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE) != 0;
                    if (!isActive)
                    {
                        continue;
                    }

                    // Extract base TargetId (lower 16 bits) for stable identification - Windows encodes SourceId in high bytes when in clone mode
                    uint baseTargetId = path.targetInfo.id & 0xFFFF;

                    var displayConfig = new DisplayConfigInfo
                    {
                        PathIndex = i,
                        IsEnabled = (path.flags & (uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE) != 0,
                        IsAvailable = path.targetInfo.targetAvailable,
                        AdapterId = path.sourceInfo.adapterId,
                        SourceId = path.sourceInfo.id,
                        TargetId = baseTargetId,  // Use base TargetId, not clone-encoded value
                        RawTargetId = path.targetInfo.id,
                        OutputTechnology = path.targetInfo.outputTechnology
                    };

                    // Get source device name (e.g., \\.\DISPLAY1)
                    var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    sourceName.header.type = DisplayConfigDeviceInfoType.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    sourceName.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                    sourceName.header.adapterId = path.sourceInfo.adapterId;
                    sourceName.header.id = path.sourceInfo.id;

                    result = DisplayConfigGetDeviceInfo(ref sourceName);
                    if (result == ERROR_SUCCESS)
                    {
                        displayConfig.DeviceName = sourceName.viewGdiDeviceName;
                    }

                    // Get target device name (monitor friendly name)
                    var targetName = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    targetName.header.type = DisplayConfigDeviceInfoType.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    targetName.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
                    targetName.header.adapterId = path.targetInfo.adapterId;
                    targetName.header.id = path.targetInfo.id;

                    result = DisplayConfigGetDeviceInfo(ref targetName);
                    if (result == ERROR_SUCCESS)
                    {
                        displayConfig.FriendlyName = targetName.monitorFriendlyDeviceName;
                    }

                    // Get HDR information
                    var colorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
                    colorInfo.header.type = DisplayConfigDeviceInfoType.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
                    colorInfo.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
                    colorInfo.header.adapterId = path.targetInfo.adapterId;
                    colorInfo.header.id = path.targetInfo.id;

                    result = DisplayConfigGetDeviceInfo(ref colorInfo);

                    if (result == ERROR_SUCCESS)
                    {
                        var flags = colorInfo.values;
                        bool isSupported = (flags & DISPLAYCONFIG_ADVANCED_COLOR_INFO_FLAGS.AdvancedColorSupported) != 0;
                        bool isEnabled = (flags & DISPLAYCONFIG_ADVANCED_COLOR_INFO_FLAGS.AdvancedColorEnabled) != 0;
                        bool isForceDisabled = (flags & DISPLAYCONFIG_ADVANCED_COLOR_INFO_FLAGS.AdvancedColorForceDisabled) != 0;

                        // Supported if flag is set and not force disabled
                        bool finalSupported = isSupported && !isForceDisabled;
                        // Enabled if flag is set or force disabled but YCbCr444 is present
                        bool finalEnabled = isEnabled || (finalSupported && colorInfo.colorEncoding == DISPLAYCONFIG_COLOR_ENCODING.DISPLAYCONFIG_COLOR_ENCODING_YCBCR444);

                        displayConfig.IsHdrSupported = finalSupported;
                        displayConfig.IsHdrEnabled = finalEnabled;
                        displayConfig.ColorEncoding = colorInfo.colorEncoding;
                        displayConfig.BitsPerColorChannel = (uint)colorInfo.bitsPerColorChannel;
                    }
                    else
                    {
                        logger.Debug($"Failed to get HDR info for {displayConfig.DeviceName}: Error {result}");
                        displayConfig.IsHdrSupported = false;
                        displayConfig.IsHdrEnabled = false;
                    }

                    // Get resolution and refresh rate if display is active
                    if (displayConfig.IsEnabled && path.sourceInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
                    {
                        var sourceMode = modes[path.sourceInfo.modeInfoIdx];
                        if (sourceMode.infoType == DisplayConfigModeInfoType.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
                        {
                            displayConfig.Width = (int)sourceMode.modeInfo.sourceMode.width;
                            displayConfig.Height = (int)sourceMode.modeInfo.sourceMode.height;
                            displayConfig.DisplayPositionX = sourceMode.modeInfo.sourceMode.position.x;
                            displayConfig.DisplayPositionY = sourceMode.modeInfo.sourceMode.position.y;
                            displayConfig.Rotation = (DISPLAYCONFIG_ROTATION)path.targetInfo.rotation;
                        }
                    }

                    if (displayConfig.IsEnabled && path.targetInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
                    {
                        var targetMode = modes[path.targetInfo.modeInfoIdx];
                        if (targetMode.infoType == DisplayConfigModeInfoType.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
                        {
                            var sig = targetMode.modeInfo.targetMode.targetVideoSignalInfo;

                            if (sig.activeSize.cx > 0 && sig.activeSize.cy > 0)
                            {
                                displayConfig.NativeWidth = (int)sig.activeSize.cx;
                                displayConfig.NativeHeight = (int)sig.activeSize.cy;
                            }

                            if (sig.vSyncFreq.Denominator != 0)
                            {
                                double hz = (double)sig.vSyncFreq.Numerator / sig.vSyncFreq.Denominator;
                                displayConfig.RefreshRate = Math.Round(hz, 2);
                            }
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
            return displayConfigs
                .Where(d => d.IsEnabled)
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

                uint pathCount = 0;
                uint modeCount = 0;
                int result = GetDisplayConfigBufferSizes(
                    QueryDisplayConfigFlags.QDC_ALL_PATHS,
                    out pathCount,
                    out modeCount);

                if (result != ERROR_SUCCESS)
                {
                    logger.Error($"GetDisplayConfigBufferSizes failed with error: {result}");
                    return false;
                }

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

                result = QueryDisplayConfig(
                    QueryDisplayConfigFlags.QDC_ALL_PATHS,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero);

                if (result != ERROR_SUCCESS)
                {
                    logger.Error($"QueryDisplayConfig failed with error: {result}");
                    return false;
                }

                // Perform detailed mismatch check between current hardware state and the requested profile
                bool needsUpdate = false;
                var profileLookup = displayConfigs.ToDictionary(d => d.TargetId & 0xFFFF);

                // Group paths by target to handle multiple paths per physical monitor
                var pathsByTarget = paths
                    .Where(p => p.targetInfo.targetAvailable)
                    .GroupBy(p => p.targetInfo.id & 0xFFFF);

                // Normalize profile SourceIds to contiguous indices
                var sourceIdMap = BuildSourceIdMap(displayConfigs);

                foreach (var group in pathsByTarget)
                {
                    uint hardwareId = group.Key;

                    // Check if any path in group is currently active
                    bool isAnyPathActive = group.Any(p => (p.flags & (uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE) != 0);

                    if (profileLookup.TryGetValue(hardwareId, out var profile))
                    {
                        if (isAnyPathActive != profile.IsEnabled)
                        {
                            logger.Debug($"Found TargetId {hardwareId}: Currently {(isAnyPathActive ? "on" : "off")} but should be {(profile.IsEnabled ? "on" : "off")}.");
                            needsUpdate = true;
                        }
                        else if (isAnyPathActive && profile.IsEnabled)
                        {
                            var activePath = group.First(p => (p.flags & (uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE) != 0);

                            // Validate actual state against normalized SourceId
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
                        // Display physically connected and active, but undefined in profile
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

                // Build mapping of TargetId to path index
                var targetIdToPathIndex = new Dictionary<uint, int>();
                for (int i = 0; i < paths.Length; i++)
                {
                    if (paths[i].targetInfo.targetAvailable)
                    {
                        uint baseTargetId = paths[i].targetInfo.id & 0xFFFF;
                        if (!targetIdToPathIndex.ContainsKey(baseTargetId))
                        {
                            targetIdToPathIndex[baseTargetId] = i;
                        }
                    }
                }

                // Build clone group mapping from profile
                var sourceIdToCloneGroup = new Dictionary<uint, uint>();
                uint nextCloneGroup = 0;
                foreach (var display in displayConfigs.Where(d => d.IsEnabled))
                {
                    if (!sourceIdToCloneGroup.ContainsKey(display.SourceId))
                    {
                        sourceIdToCloneGroup[display.SourceId] = nextCloneGroup++;
                    }
                }

                var targetIdToDisplay = displayConfigs.Where(d => d.IsEnabled).ToDictionary(d => d.TargetId & 0xFFFF);

                // Configure each available display path
                foreach (var kvp in targetIdToPathIndex)
                {
                    uint targetId = kvp.Key;
                    int pathIndex = kvp.Value;

                    paths[pathIndex].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;

                    if (targetIdToDisplay.TryGetValue(targetId, out var display))
                    {
                        uint cloneGroup = sourceIdToCloneGroup[display.SourceId];
                        paths[pathIndex].flags |= (uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE;
                        paths[pathIndex].sourceInfo.ResetModeAndSetCloneGroup(cloneGroup);
                    }
                    else
                    {
                        paths[pathIndex].flags &= ~(uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE;
                        paths[pathIndex].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                    }
                }

                // Assign unique source IDs per adapter for all active paths
                var sourceIdTable = new Dictionary<LUID, uint>();
                int activeCount = 0;

                for (int i = 0; i < paths.Length; i++)
                {
                    if ((paths[i].flags & (uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                    {
                        LUID adapterId = paths[i].sourceInfo.adapterId;
                        if (!sourceIdTable.ContainsKey(adapterId))
                        {
                            sourceIdTable[adapterId] = 0;
                        }
                        paths[i].sourceInfo.id = sourceIdTable[adapterId]++;
                        activeCount++;
                    }
                }

                if (activeCount == 0)
                {
                    logger.Error("No active displays to enable.");
                    return false;
                }

                // Commit reconstructed topology
                result = SetDisplayConfig(
                    pathCount,
                    paths,
                    0,
                    null,
                    SetDisplayConfigFlags.SDC_TOPOLOGY_SUPPLIED |
                    SetDisplayConfigFlags.SDC_APPLY |
                    SetDisplayConfigFlags.SDC_ALLOW_PATH_ORDER_CHANGES |
                    SetDisplayConfigFlags.SDC_VIRTUAL_MODE_AWARE);

                if (result != ERROR_SUCCESS)
                {
                    logger.Error($"Topology failed with error: {result}");
                    return false;
                }

                logger.Info("Successfully pplied topology.");
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
                {
                    await Task.Delay(250);
                }
            }
            if (verifiedTargetIds.Count == expectedMonitors.Count)
            {
                logger.Info($"{expectedMonitors.Count} display(s) enabled and stabilized in {deferWatch.ElapsedMilliseconds}ms.");
            }
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

                var queryFlags = QueryDisplayConfigFlags.QDC_ALL_PATHS;
                int result = GetDisplayConfigBufferSizes(queryFlags, out uint pathCount, out uint modeCount);
                if (result != ERROR_SUCCESS) return false;

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                result = QueryDisplayConfig(queryFlags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result != ERROR_SUCCESS) return false;

                // Normalize profile SourceIds to contiguous indices
                var sourceIdMap = BuildSourceIdMap(displayConfigs);

                // Offset layout coordinates relative to profile designated primary display
                var primaryProfile = displayConfigs.FirstOrDefault(p => p.IsEnabled && p.IsPrimary) ?? displayConfigs.FirstOrDefault(p => p.IsEnabled);

                int offsetX = primaryProfile != null ? -primaryProfile.DisplayPositionX : 0;
                int offsetY = primaryProfile != null ? -primaryProfile.DisplayPositionY : 0;

                // Skip SetDisplayConfig if layout already matches
                bool needsUpdate = false;

                foreach (var profile in displayConfigs)
                {
                    var pIdx = Array.FindIndex(paths, p => (p.targetInfo.id & 0xFFFF) == (profile.TargetId & 0xFFFF));
                    if (pIdx == -1) continue;

                    string mon = !string.IsNullOrEmpty(profile.FriendlyName) ? profile.FriendlyName : $"ID:{profile.TargetId}";
                    bool isActive = (paths[pIdx].flags & (uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE) != 0;

                    // Check active state
                    if (isActive != profile.IsEnabled)
                    {
                        logger.Debug($"[Topology] {mon}: Current={(isActive ? "Enabled" : "Disabled")}, Profile={(profile.IsEnabled ? "Enabled" : "Disabled")}");
                        needsUpdate = true;
                    }

                    if (profile.IsEnabled)
                    {
                        // Check rotation and normalized source topology
                        uint normalizedProfileSourceId = sourceIdMap[profile.SourceId];
                        if (paths[pIdx].sourceInfo.id != normalizedProfileSourceId)
                        {
                            logger.Debug($"[SourceId] {mon}: Current={paths[pIdx].sourceInfo.id}, NormalizedProfile={normalizedProfileSourceId}");
                            needsUpdate = true;
                        }

                        if (paths[pIdx].targetInfo.rotation != (uint)profile.Rotation)
                        {
                            logger.Debug($"[Rotation] {mon}: Current={paths[pIdx].targetInfo.rotation}, Profile={profile.Rotation}");
                            needsUpdate = true;
                        }

                        // Check resolution and normalized position (source mode)
                        uint sModeIdx = paths[pIdx].sourceInfo.modeInfoIdx;
                        if (sModeIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && sModeIdx < modes.Length)
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

                        // Check refresh rate (target mode)
                        uint tModeIdx = paths[pIdx].targetInfo.modeInfoIdx;
                        if (tModeIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && tModeIdx < modes.Length)
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

                logger.Info($"Display mismatch detected -> Apply profile configuration");

                // Clear all active flags to ensure a clean reconstruction of the topology
                for (int i = 0; i < paths.Length; i++)
                    paths[i].flags &= ~(uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE;

                var sourceIdToModeIdx = new Dictionary<uint, uint>();
                foreach (var profile in displayConfigs.Where(d => d.IsEnabled))
                {
                    int pIdx = Array.FindIndex(paths, p => (p.targetInfo.id & 0xFFFF) == (profile.TargetId & 0xFFFF));
                    if (pIdx == -1) continue;

                    uint normalizedSourceId = sourceIdMap[profile.SourceId];
                    paths[pIdx].flags |= (uint)DisplayConfigPathInfoFlags.DISPLAYCONFIG_PATH_ACTIVE;
                    paths[pIdx].sourceInfo.id = normalizedSourceId;
                    paths[pIdx].targetInfo.rotation = (uint)profile.Rotation;

                    // FIX: Ensure all members of a clone group use the SAME mode index
                    if (!sourceIdToModeIdx.TryGetValue(normalizedSourceId, out uint sModeIdx))
                    {
                        sModeIdx = paths[pIdx].sourceInfo.modeInfoIdx;
                        sourceIdToModeIdx[normalizedSourceId] = sModeIdx;
                    }

                    paths[pIdx].sourceInfo.modeInfoIdx = sModeIdx;

                    if (sModeIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && sModeIdx < modes.Length)
                    {
                        ref var src = ref modes[sModeIdx].modeInfo.sourceMode;
                        modes[sModeIdx].id = normalizedSourceId;
                        src.width = (uint)profile.Width;
                        src.height = (uint)profile.Height;
                        src.position.x = profile.DisplayPositionX + offsetX;
                        src.position.y = profile.DisplayPositionY + offsetY;
                    }

                    uint tModeIdx = paths[pIdx].targetInfo.modeInfoIdx;
                    if (tModeIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && tModeIdx < modes.Length)
                    {
                        ref var sig = ref modes[tModeIdx].modeInfo.targetMode.targetVideoSignalInfo;
                        sig.vSyncFreq.Numerator = (uint)(profile.RefreshRate * 1000);
                        sig.vSyncFreq.Denominator = 1000;
                        sig.activeSize.cx = (uint)profile.Width;
                        sig.activeSize.cy = (uint)profile.Height;
                    }
                }

                // Commit the reconstructed topology and save to the persistence database
                result = SetDisplayConfig(
                    pathCount, paths,
                    modeCount, modes,
                    SetDisplayConfigFlags.SDC_APPLY |
                    SetDisplayConfigFlags.SDC_USE_SUPPLIED_DISPLAY_CONFIG |
                    SetDisplayConfigFlags.SDC_SAVE_TO_DATABASE |
                    SetDisplayConfigFlags.SDC_ALLOW_CHANGES);

                // Verify that layout is actually applied
                if (result != ERROR_SUCCESS)
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

        public static bool ApplyDisplayConfig(List<DisplayConfigInfo> displayConfigs)
        {
            try
            {
                var totalWatch = Stopwatch.StartNew();
                logger.Info($"Applying configuration for {displayConfigs.Count(d => d.IsEnabled)} enabled display(s)...");

                // Apply physical topology and layout (atomic update including resolution, position, and rotation)
                var layoutWatch = Stopwatch.StartNew();
                if (!ApplyDisplayLayout(displayConfigs))
                {
                    logger.Error("Failed to apply display layout");
                    return false;
                }
                layoutWatch.Stop();

                // Apply HDR state via WinRT/Advanced Color APIs (handled after layout to ensure valid target handles)
                var hdrWatch = Stopwatch.StartNew();
                ApplyHdrSettings(displayConfigs);
                hdrWatch.Stop();
                totalWatch.Stop();

                logger.Info($"Configured - Layout: {layoutWatch.ElapsedMilliseconds}ms | HDR: {hdrWatch.ElapsedMilliseconds}ms | TOTAL: {totalWatch.ElapsedMilliseconds}ms");

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during configuration application");
                return false;
            }
        }

        public static bool SetHdrState(LUID adapterId, uint targetId, bool enableHdr)
        {
            try
            {
                var colorState = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
                colorState.header.type = DisplayConfigDeviceInfoType.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
                colorState.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
                colorState.header.adapterId = adapterId;
                colorState.header.id = targetId;

                if (enableHdr)
                {
                    colorState.values = DISPLAYCONFIG_SET_ADVANCED_COLOR_FLAGS.EnableAdvancedColor;
                }
                else
                {
                    colorState.values = 0;
                }

                int result = DisplayConfigSetDeviceInfo(ref colorState);
                if (result == ERROR_SUCCESS)
                {
                    logger.Info($"Successfully set HDR state to {enableHdr} for TargetId {targetId}");
                    return true;
                }
                else
                {
                    logger.Error($"Failed to set HDR state for TargetId {targetId}: Error {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error setting HDR state for TargetId {targetId}");
                return false;
            }
        }

        public static bool ApplyHdrSettings(List<DisplayConfigInfo> profileConfigs)
        {
            logger.Info("Applying HDR settings...");

            // Get live config to skip redundant HDR state changes
            var currentConfigs = GetDisplayConfigs();
            bool allSuccessful = true;

            foreach (var profileDisplay in profileConfigs)
            {
                // Skip if displays are disabled or lack HDR hardware support
                if (profileDisplay.IsHdrSupported && profileDisplay.IsEnabled)
                {
                    var activeDisplay = currentConfigs.FirstOrDefault(c => c.TargetId == profileDisplay.TargetId);

                    if (activeDisplay != null)
                    {
                        // Only toggle the state if the hardware does not already match the requested profile
                        if (activeDisplay.IsHdrEnabled == profileDisplay.IsHdrEnabled)
                        {
                            logger.Debug($"Skipping {activeDisplay.FriendlyName} -> HDR is already {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                            continue;
                        }

                        logger.Info($"Setting {activeDisplay.FriendlyName} -> HDR to {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                        bool success = SetHdrState(activeDisplay.AdapterId, activeDisplay.RawTargetId, profileDisplay.IsHdrEnabled);

                        if (!success)
                        {
                            logger.Error($"Failed to apply HDR setting for {activeDisplay.FriendlyName}.");
                            allSuccessful = false;
                        }
                    }
                    else
                    {
                        logger.Warn($"Could not find active display matching TargetId {profileDisplay.TargetId} to apply HDR.");
                        allSuccessful = false;
                    }
                }
            }

            return allSuccessful;
        }

        public static LUID GetLUIDFromString(string adapterIdString)
        {
            // Reconstruct the 64-bit LUID from its hex-string representation
            if (!string.IsNullOrEmpty(adapterIdString) && adapterIdString.Length == 16)
            {
                try
                {
                    var highPart = Convert.ToInt32(adapterIdString.Substring(0, 8), 16);
                    var lowPart = Convert.ToUInt32(adapterIdString.Substring(8, 8), 16);
                    return new LUID
                    {
                        HighPart = highPart,
                        LowPart = lowPart
                    };
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"Failed to parse AdapterId '{adapterIdString}'");
                }
            }
            return new LUID { HighPart = 0, LowPart = 0 };
        }

        public static bool ValidateCloneGroups(List<DisplaySetting> settings)
        {
            // Group by CloneGroupId to validate members share required hardware properties
            var cloneGroups = settings
                .Where(s => s.IsPartOfCloneGroup())
                .GroupBy(s => s.CloneGroupId);

            foreach (var group in cloneGroups)
            {
                var groupList = group.ToList();
                if (groupList.Count < 2)
                {
                    logger.Warn($"Clone group {group.Key} has only one member - ignoring");
                    continue;
                }

                var first = groupList[0];

                foreach (var setting in groupList.Skip(1))
                {
                    // Critical technical constraints (cloned displays MUST share these to maintain driver-level sync)
                    if (setting.Width != first.Width ||
                        setting.Height != first.Height ||
                        setting.Frequency != first.Frequency ||
                        setting.SourceId != first.SourceId ||
                        setting.DisplayPositionX != first.DisplayPositionX ||
                        setting.DisplayPositionY != first.DisplayPositionY)
                    {
                        logger.Error($"Clone group {group.Key} has inconsistent critical settings: " +
                                $"{setting.ReadableDeviceName} ({setting.Width}x{setting.Height}@{setting.Frequency}Hz at {setting.DisplayPositionX},{setting.DisplayPositionY}) vs " +
                                $"{first.ReadableDeviceName} ({first.Width}x{first.Height}@{first.Frequency}Hz at {first.DisplayPositionX},{first.DisplayPositionY})");
                        return false;
                    }

                    // Non-critical validation (mismatched DPI scaling works but causes visual layout shifts)
                    if (setting.DpiScaling != first.DpiScaling)
                    {
                        logger.Warn($"Clone group {group.Key} has different DPI settings - " +
                                $"{setting.ReadableDeviceName}: {setting.DpiScaling}% vs " +
                                $"{first.ReadableDeviceName}: {first.DpiScaling}% - " +
                                $"may cause visual inconsistency");
                    }
                }

                logger.Debug($"Clone group {group.Key} validation passed ({groupList.Count} displays)");
            }

            return true;
        }

        public static bool VerifyDisplayConfiguration(List<DisplayConfigInfo> expectedConfigs)
        {
            try
            {
                // Get live config to diff against expected
                var currentConfigs = GetDisplayConfigs();

                int expEnabled = expectedConfigs.Count(c => c.IsEnabled);
                int expDisabled = expectedConfigs.Count(c => !c.IsEnabled);
                int foundActive = currentConfigs.Count(c => c.IsEnabled);
                int foundInactive = currentConfigs.Count - foundActive;

                string expectedStr = $"{expEnabled} enabled";
                if (expDisabled > 0) expectedStr += $" / {expDisabled} disabled";

                string foundStr = $"{foundActive} active";
                if (foundInactive > 0) foundStr += $" / {foundInactive} inactive";

                logger.Info($"Verifying display configuration: Expected {expectedStr} display(s), found {foundStr}");

                bool allMatched = true;

                foreach (var expected in expectedConfigs)
                {
                    if (!expected.IsEnabled)
                    {
                        // Verify displays disabled in profile are not active in hardware
                        var found = currentConfigs.FirstOrDefault(c => c.TargetId == expected.TargetId);
                        if (found != null && found.IsEnabled)
                        {
                            logger.Error($"TargetId {expected.TargetId} should be DISABLED but is ACTIVE");
                            allMatched = false;
                        }
                        else
                        {
                            logger.Info($"TargetId {expected.TargetId} ({expected.FriendlyName}): disabled");
                        }
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

                // Verify clone group integrity (targets sharing a profile SourceId must share a Windows SourceId)
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
                    {
                        logger.Info($"Clone group (profile SourceId {cloneGroup.Key}): Targets [{string.Join(", ", targetIds)}] correctly share actual SourceId {actualSourceIds[0]}");
                    }
                    else
                    {
                        logger.Error($"Clone group (profile SourceId {cloneGroup.Key}): Targets [{string.Join(", ", targetIds)}] have different actual SourceIds: [{string.Join(", ", actualSourceIds)}]");
                        allMatched = false;
                    }
                }

                if (allMatched)
                {
                    logger.Info("Display configuration verification PASSED");
                }
                else
                {
                    logger.Error("Display configuration verification FAILED");
                }

                return allMatched;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error verifying display configuration");
                return false;
            }
        }
        #endregion
    }
}