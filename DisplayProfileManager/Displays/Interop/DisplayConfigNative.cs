using System;
using System.Runtime.InteropServices;

using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Displays.Interop
{
    public static class DisplayConfigNative
    {
        #region P/Invoke

        [DllImport("user32.dll")]
        internal static extern int GetDisplayConfigBufferSizes(
            QueryDisplayConfigFlags flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        internal static extern int QueryDisplayConfig(
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
        internal static extern int SetDisplayConfig(
            uint numPathArrayElements,
            [In] DisplayConfigPathInfo[] pathArray,
            uint numModeInfoArrayElements,
            [In] DisplayConfigModeInfo[] modeInfoArray,
            SetDisplayConfigFlags flags);

        [DllImport("user32.dll")]
        internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName deviceName);
        [DllImport("user32.dll")]
        internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetPreferredMode preferredMode);
        [DllImport("user32.dll")]
        internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName deviceName);
        [DllImport("user32.dll")]
        internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo colorInfo);
        [DllImport("user32.dll")]
        internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo2 colorInfo);
        [DllImport("user32.dll")]
        internal static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetAdvancedColorState colorState);
        [DllImport("user32.dll")]
        internal static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetHdrState state);
        [DllImport("user32.dll")]
        internal static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetWcgState state);

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

        #endregion

        #region Constants

        internal const int ErrorSuccess = 0;
        internal const int ErrorGenFailure = 31;
        internal const int ErrorInvalidParameter = 87;
        internal const int ErrorInsufficientBuffer = 122;

        internal const uint DisplayconfigPathSourceModeIdxInvalid = 0xffff;
        internal const uint DisplayconfigPathModeIdxInvalid = 0xffffffff;

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

        internal static AdapterLuid ToManaged(LUID value) => new(value.LowPart, value.HighPart);

        internal static LUID ToNative(AdapterLuid value) => new() { LowPart = value.LowPart, HighPart = value.HighPart };
    }
}
