using System;
using System.Runtime.InteropServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Events;
using DisplayProfileManager.Displays.Interop;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayEventParserTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_DisplayChange_CopiesDimensionsAndBitsPerPixel()
        {
            IntPtr lParam = new((1080 << 16) | 1920);

            var result = DisplayEventParser.Parse(7, DateTime.UnixEpoch, 100, DisplayEventNative.WmDisplayChange, new IntPtr(32), lParam);

            Assert.AreEqual(7, result.Sequence);
            Assert.AreEqual("WM_DISPLAYCHANGE", result.Message);
            Assert.AreEqual(32, result.BitsPerPixel);
            Assert.AreEqual(1920, result.Width);
            Assert.AreEqual(1080, result.Height);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_DevNodesChanged_ClassifiesSubtypeWithoutPayload()
        {
            var result = DisplayEventParser.Parse(1, DateTime.UnixEpoch, 1, DisplayEventNative.WmDeviceChange, new IntPtr(DisplayEventNative.DbtDevNodesChanged), IntPtr.Zero);

            Assert.AreEqual(nameof(DisplayEventNative.DbtDevNodesChanged), result.SubtypeName);
            Assert.IsNull(result.DeviceInterfacePath);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_MonitorInterfaceArrival_CopiesManagedPayload()
        {
            string path = @"\\?\DISPLAY#MAN1001#1";

            var result = ParseMonitorInterface(DisplayEventNative.DbtDeviceArrival, path);

            Assert.AreEqual(DisplayEventNative.DbtDevTypDeviceInterface, result.DeviceType);
            Assert.AreEqual(DisplayEventNative.MonitorInterfaceGuid, result.DeviceClassGuid);
            Assert.AreEqual(path, result.DeviceInterfacePath);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_MonitorInterfaceRemoval_CopiesManagedPayload()
        {
            string path = @"\\?\DISPLAY#MAN1001#1";

            var result = ParseMonitorInterface(DisplayEventNative.DbtDeviceRemoveComplete, path);

            Assert.AreEqual(nameof(DisplayEventNative.DbtDeviceRemoveComplete), result.SubtypeName);
            Assert.AreEqual(DisplayEventNative.DbtDevTypDeviceInterface, result.DeviceType);
            Assert.AreEqual(DisplayEventNative.MonitorInterfaceGuid, result.DeviceClassGuid);
            Assert.AreEqual(path, result.DeviceInterfacePath);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_SessionDisplayStatus_CopiesPowerState()
        {
            int headerSize = Marshal.SizeOf<DisplayEventNative.PowerBroadcastSetting>();
            IntPtr pointer = Marshal.AllocHGlobal(headerSize + sizeof(int));
            try
            {
                var header = new DisplayEventNative.PowerBroadcastSetting
                {
                    PowerSetting = DisplayEventNative.SessionDisplayStatusGuid,
                    DataLength = sizeof(int)
                };
                Marshal.StructureToPtr(header, pointer, false);
                Marshal.WriteInt32(IntPtr.Add(pointer, headerSize), 2);

                var result = DisplayEventParser.Parse(3, DateTime.UnixEpoch, 3, DisplayEventNative.WmPowerBroadcast, new IntPtr(DisplayEventNative.PbtPowerSettingChange), pointer);

                Assert.AreEqual(DisplayEventNative.SessionDisplayStatusGuid, result.PowerSettingGuid);
                Assert.AreEqual(2, result.PowerStateValue);
                Assert.AreEqual("Dim", result.PowerStateName);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MonitorMessageBoundary_RecognizesOnlySupportedDisplayFamilies()
        {
            Assert.IsTrue(DisplayEventMonitor.IsSupportedMessage(DisplayEventNative.WmDisplayChange));
            Assert.IsTrue(DisplayEventMonitor.IsSupportedMessage(DisplayEventNative.WmDeviceChange));
            Assert.IsTrue(DisplayEventMonitor.IsSupportedMessage(DisplayEventNative.WmPowerBroadcast));
            Assert.IsFalse(DisplayEventMonitor.IsSupportedMessage(0x1234));
        }

        private static DisplayRawEvent ParseMonitorInterface(int subtype, string path)
        {
            int headerSize = Marshal.SizeOf<DisplayEventNative.DeviceBroadcastInterface>();
            int bytes = headerSize + (path.Length + 1) * sizeof(char);
            IntPtr pointer = Marshal.AllocHGlobal(bytes);
            try
            {
                for (int i = 0; i < bytes; i++)
                    Marshal.WriteByte(pointer, i, 0);

                var header = new DisplayEventNative.DeviceBroadcastInterface
                {
                    Size = (uint)bytes,
                    DeviceType = DisplayEventNative.DbtDevTypDeviceInterface,
                    ClassGuid = DisplayEventNative.MonitorInterfaceGuid
                };
                Marshal.StructureToPtr(header, pointer, false);
                Marshal.Copy(path.ToCharArray(), 0, IntPtr.Add(pointer, headerSize), path.Length);

                return DisplayEventParser.Parse(
                    2,
                    DateTime.UnixEpoch,
                    2,
                    DisplayEventNative.WmDeviceChange,
                    new IntPtr(subtype),
                    pointer);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }
}
