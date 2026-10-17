using System;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Displays.Interop
{
    internal static class DisplayEventNative
    {
        #region P/Invoke

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr RegisterDeviceNotification(
            IntPtr recipient, IntPtr notificationFilter, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterDeviceNotification(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr RegisterPowerSettingNotification(
            IntPtr recipient, ref Guid powerSettingGuid, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceBroadcastInterface
        {
            internal uint Size;
            internal uint DeviceType;
            internal uint Reserved;
            internal Guid ClassGuid;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PowerBroadcastSetting
        {
            internal Guid PowerSetting;
            internal uint DataLength;
        }

        #endregion

        #region Constants

        internal const int WmDisplayChange = 0x007E;
        internal const int WmDeviceChange = 0x0219;
        internal const int WmPowerBroadcast = 0x0218;
        internal const int DbtDevNodesChanged = 0x0007;
        internal const int DbtDeviceArrival = 0x8000;
        internal const int DbtDeviceRemoveComplete = 0x8004;
        internal const uint DbtDevTypDeviceInterface = 0x00000005;
        internal const int PbtApmSuspend = 0x0004;
        internal const int PbtApmResumeSuspend = 0x0007;
        internal const int PbtApmResumeAutomatic = 0x0012;
        internal const int PbtPowerSettingChange = 0x8013;
        internal const uint DeviceNotifyWindowHandle = 0;

        internal static readonly Guid MonitorInterfaceGuid = new("E6F07B5F-EE97-4A90-B076-33F57BF4EAA7");
        internal static readonly Guid SessionDisplayStatusGuid = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");

        #endregion

        #region Event Names

        internal static string DeviceSubtypeName(long value) => value switch
        {
            DbtDevNodesChanged => nameof(DbtDevNodesChanged),
            DbtDeviceArrival => nameof(DbtDeviceArrival),
            DbtDeviceRemoveComplete => nameof(DbtDeviceRemoveComplete),
            _ => $"DBT_0x{value:X}"
        };

        internal static string PowerSubtypeName(long value) => value switch
        {
            PbtApmSuspend => nameof(PbtApmSuspend),
            PbtApmResumeSuspend => nameof(PbtApmResumeSuspend),
            PbtApmResumeAutomatic => nameof(PbtApmResumeAutomatic),
            PbtPowerSettingChange => nameof(PbtPowerSettingChange),
            _ => $"PBT_0x{value:X}"
        };

        internal static string SessionDisplayStateName(int? value) => value switch
        {
            0 => "Off",
            1 => "On",
            2 => "Dim",
            null => null,
            _ => $"Unknown({value})"
        };

        #endregion
    }
}
