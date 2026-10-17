using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Events;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayEventRegistrationsTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Dispose_SuccessfulRegistrations_UnregistersEachHandleOnce()
        {
            var released = new List<IntPtr>();
            var registrations = new DisplayEventRegistrations(
                new IntPtr(1),
                (hwnd, filter, flags) => new IntPtr(11),
                (IntPtr hwnd, ref Guid setting, uint flags) => new IntPtr(22),
                handle => { released.Add(handle); return true; },
                handle => { released.Add(handle); return true; });

            registrations.Dispose();
            registrations.Dispose();

            CollectionAssert.AreEquivalent(new[] { new IntPtr(11), new IntPtr(22) }, released);
            Assert.AreEqual(IntPtr.Zero, registrations.DeviceHandle);
            Assert.AreEqual(IntPtr.Zero, registrations.PowerHandle);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Dispose_FailedRegistrations_DoesNotAttemptUnregister()
        {
            int unregisterCalls = 0;
            using var registrations = new DisplayEventRegistrations(
                new IntPtr(1),
                (hwnd, filter, flags) => IntPtr.Zero,
                (IntPtr hwnd, ref Guid setting, uint flags) => IntPtr.Zero,
                handle => { unregisterCalls++; return true; },
                handle => { unregisterCalls++; return true; });

            Assert.AreEqual(IntPtr.Zero, registrations.DeviceHandle);
            Assert.AreEqual(IntPtr.Zero, registrations.PowerHandle);
            Assert.AreEqual(0, unregisterCalls);
        }
    }
}
