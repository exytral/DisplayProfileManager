using System;
using System.Runtime.InteropServices;

using DisplayProfileManager.Displays.Interop;

namespace DisplayProfileManager.Displays.Events
{
    internal sealed class DisplayEventRegistrations : IDisposable
    {
        internal delegate IntPtr RegisterDeviceDelegate(IntPtr hwnd, IntPtr filter, uint flags);
        internal delegate bool UnregisterDelegate(IntPtr handle);
        internal delegate IntPtr RegisterPowerDelegate(IntPtr hwnd, ref Guid setting, uint flags);

        private readonly UnregisterDelegate _unregisterDevice;
        private readonly UnregisterDelegate _unregisterPower;

        internal IntPtr DeviceHandle { get; private set; }
        internal IntPtr PowerHandle { get; private set; }
        internal int DeviceError { get; private set; }
        internal int PowerError { get; private set; }

        internal DisplayEventRegistrations(
            IntPtr hwnd,
            RegisterDeviceDelegate registerDevice = null,
            RegisterPowerDelegate registerPower = null,
            UnregisterDelegate unregisterDevice = null,
            UnregisterDelegate unregisterPower = null)
        {
            registerDevice ??= DisplayEventNative.RegisterDeviceNotification;
            registerPower ??= DisplayEventNative.RegisterPowerSettingNotification;
            _unregisterDevice = unregisterDevice ?? DisplayEventNative.UnregisterDeviceNotification;
            _unregisterPower = unregisterPower ?? DisplayEventNative.UnregisterPowerSettingNotification;

            IntPtr filter = CreateMonitorFilter();
            try
            {
                DeviceHandle = registerDevice(hwnd, filter, DisplayEventNative.DeviceNotifyWindowHandle);
                if (DeviceHandle == IntPtr.Zero)
                    DeviceError = Marshal.GetLastWin32Error();
            }
            finally
            {
                Marshal.FreeHGlobal(filter);
            }

            Guid setting = DisplayEventNative.SessionDisplayStatusGuid;
            PowerHandle = registerPower(hwnd, ref setting, DisplayEventNative.DeviceNotifyWindowHandle);
            if (PowerHandle == IntPtr.Zero)
                PowerError = Marshal.GetLastWin32Error();
        }

        private static IntPtr CreateMonitorFilter()
        {
            var filter = new DisplayEventNative.DeviceBroadcastInterface
            {
                Size = (uint)(Marshal.SizeOf<DisplayEventNative.DeviceBroadcastInterface>() + sizeof(char)),
                DeviceType = DisplayEventNative.DbtDevTypDeviceInterface,
                ClassGuid = DisplayEventNative.MonitorInterfaceGuid
            };

            IntPtr pointer = Marshal.AllocHGlobal((int)filter.Size);
            for (int i = 0; i < filter.Size; i++)
                Marshal.WriteByte(pointer, i, 0);
            Marshal.StructureToPtr(filter, pointer, false);
            return pointer;
        }

        public void Dispose()
        {
            if (DeviceHandle != IntPtr.Zero)
            {
                _unregisterDevice(DeviceHandle);
                DeviceHandle = IntPtr.Zero;
            }

            if (PowerHandle != IntPtr.Zero)
            {
                _unregisterPower(PowerHandle);
                PowerHandle = IntPtr.Zero;
            }
        }
    }
}
