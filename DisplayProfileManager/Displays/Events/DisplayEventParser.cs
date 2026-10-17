using System;
using System.Runtime.InteropServices;

using DisplayProfileManager.Displays.Interop;

namespace DisplayProfileManager.Displays.Events
{
    internal static class DisplayEventParser
    {
        internal static DisplayRawEvent Parse(
            long sequence,
            DateTime wallUtc,
            long monotonicTicks,
            int messageId,
            IntPtr wParam,
            IntPtr lParam)
        {
            long value = wParam.ToInt64();
            var raw = new DisplayRawEvent
            {
                Sequence = sequence,
                WallUtc = wallUtc,
                MonotonicTicks = monotonicTicks,
                MessageId = messageId,
                Message = messageId switch
                {
                    DisplayEventNative.WmDisplayChange => "WM_DISPLAYCHANGE",
                    DisplayEventNative.WmDeviceChange => "WM_DEVICECHANGE",
                    DisplayEventNative.WmPowerBroadcast => "WM_POWERBROADCAST",
                    _ => $"0x{messageId:X}"
                },
                WParam = value,
                SubtypeName = messageId switch
                {
                    DisplayEventNative.WmDeviceChange => DisplayEventNative.DeviceSubtypeName(value),
                    DisplayEventNative.WmPowerBroadcast => DisplayEventNative.PowerSubtypeName(value),
                    _ => null
                }
            };

            if (messageId == DisplayEventNative.WmDisplayChange)
            {
                raw.BitsPerPixel = unchecked((int)value);
                long packed = lParam.ToInt64();
                raw.Width = unchecked((ushort)(packed & 0xFFFF));
                raw.Height = unchecked((ushort)((packed >> 16) & 0xFFFF));
            }
            else if (messageId == DisplayEventNative.WmDeviceChange &&
                (value == DisplayEventNative.DbtDeviceArrival || value == DisplayEventNative.DbtDeviceRemoveComplete))
            {
                ReadDeviceInterface(raw, lParam);
            }
            else if (messageId == DisplayEventNative.WmPowerBroadcast &&
                value == DisplayEventNative.PbtPowerSettingChange)
            {
                ReadPowerSetting(raw, lParam);
            }

            return raw;
        }

        private static void ReadDeviceInterface(DisplayRawEvent raw, IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return;

            var header = Marshal.PtrToStructure<DisplayEventNative.DeviceBroadcastInterface>(pointer);
            raw.DeviceType = header.DeviceType;
            raw.DeviceClassGuid = header.ClassGuid;

            int headerSize = Marshal.SizeOf<DisplayEventNative.DeviceBroadcastInterface>();
            if (header.Size <= headerSize || header.DeviceType != DisplayEventNative.DbtDevTypDeviceInterface) return;

            int charCount = checked(((int)header.Size - headerSize) / sizeof(char));
            raw.DeviceInterfacePath = Marshal.PtrToStringUni(IntPtr.Add(pointer, headerSize), charCount)?.TrimEnd('\0');
        }

        private static void ReadPowerSetting(DisplayRawEvent raw, IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return;

            var header = Marshal.PtrToStructure<DisplayEventNative.PowerBroadcastSetting>(pointer);
            raw.PowerSettingGuid = header.PowerSetting;
            if (header.DataLength >= sizeof(int))
                raw.PowerStateValue = Marshal.ReadInt32(IntPtr.Add(pointer, Marshal.SizeOf<DisplayEventNative.PowerBroadcastSetting>()));

            if (header.PowerSetting == DisplayEventNative.SessionDisplayStatusGuid)
                raw.PowerStateName = DisplayEventNative.SessionDisplayStateName(raw.PowerStateValue);
        }
    }
}
