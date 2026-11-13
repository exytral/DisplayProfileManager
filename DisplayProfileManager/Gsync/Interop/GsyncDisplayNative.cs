using System;
using System.Runtime.InteropServices;

using static DisplayProfileManager.Displays.Interop.DisplayConfigNative;

namespace DisplayProfileManager.Gsync.Interop
{
    // Device notifications and adapter lookup used while the driver re-trains displays after a G-SYNC on/off change
    internal static class GsyncDisplayNative
    {
        #region P/Invoke

        [DllImport("user32.dll")]
        internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigAdapterName adapterName);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Register_Notification(ref CmNotifyFilter filter, IntPtr context, CmNotifyCallback callback, out IntPtr notification);

        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Unregister_Notification(IntPtr notification);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate uint CmNotifyCallback(IntPtr notification, IntPtr context, uint action, IntPtr eventData, uint eventDataSize);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DisplayConfigAdapterName
        {
            public DisplayConfigDeviceInfoHeader header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string adapterDevicePath;
        }

        // CM_NOTIFY_FILTER with the device-interface union member; the union is sized by its 200-wchar instance ID member
        [StructLayout(LayoutKind.Explicit, Size = 416)]
        internal struct CmNotifyFilter
        {
            [FieldOffset(0)] public uint cbSize;
            [FieldOffset(4)] public uint flags;
            [FieldOffset(8)] public uint filterType;
            [FieldOffset(12)] public uint reserved;
            [FieldOffset(16)] public Guid classGuid;
        }

        #endregion

        #region Constants

        internal const uint CrSuccess = 0;
        internal const uint CmNotifyFilterTypeDeviceInterface = 0;
        internal const uint CmNotifyActionDeviceInterfaceArrival = 0;
        internal const uint CmNotifyActionDeviceInterfaceRemoval = 1;

        // CM_NOTIFY_EVENT_DATA: filter type, reserved, class GUID, then the symbolic link string
        internal const int EventSymbolicLinkOffset = 24;

        internal static readonly Guid MonitorInterface = new("E6F07B5F-EE97-4A90-B076-33F57BF4EAA7");

        #endregion
    }
}
