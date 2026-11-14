using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Vrr;
using DisplayProfileManager.Vrr.Models;

namespace DisplayProfileManager.Tests.Vrr
{
    [TestClass]
    public class VrrServiceTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void GetCapability_NoBackendAvailable_IsUnavailable()
        {
            var service = new VrrService([new FakeBackend(VrrCapability.Unavailable), new FakeBackend(VrrCapability.Unavailable)]);

            Assert.IsFalse(service.GetCapability().IsAvailable);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetCapability_SingleBackend_ReportsItsTechnology()
        {
            var service = new VrrService([new FakeBackend(Nvidia), new FakeBackend(VrrCapability.Unavailable)]);

            var capability = service.GetCapability();

            Assert.IsTrue(capability.IsAvailable);
            Assert.AreEqual("NVIDIA G-SYNC", capability.Technology);
            Assert.IsTrue(capability.SupportsWindowed);
            Assert.AreEqual(VrrMode.Off, capability.CurrentMode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetCapability_AmdOnly_HasNoWindowedMode()
        {
            var service = new VrrService([new FakeBackend(VrrCapability.Unavailable), new FakeBackend(Amd)]);

            var capability = service.GetCapability();

            Assert.AreEqual("AMD FreeSync", capability.Technology);
            Assert.IsFalse(capability.SupportsWindowed);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetCapability_BothBackends_CombinesThem()
        {
            var service = new VrrService([new FakeBackend(Nvidia), new FakeBackend(Amd)]);

            var capability = service.GetCapability();

            Assert.AreEqual("NVIDIA G-SYNC and AMD FreeSync", capability.Technology);
            Assert.IsTrue(capability.SupportsWindowed);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyVrrSettings_AppliesOnlyToAvailableBackends()
        {
            var nvidia = new FakeBackend(VrrCapability.Unavailable);
            var amd = new FakeBackend(Amd);
            var service = new VrrService([nvidia, amd]);

            bool result = service.ApplyVrrSettings(new VrrSetting { Enabled = true, Mode = VrrMode.OnIncludingWindowed });

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new List<VrrMode>(), nvidia.Applied);
            CollectionAssert.AreEqual(new List<VrrMode> { VrrMode.OnIncludingWindowed }, amd.Applied);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyVrrSettings_NoCapableBackend_Fails()
        {
            var service = new VrrService([new FakeBackend(VrrCapability.Unavailable)]);

            Assert.IsFalse(service.ApplyVrrSettings(new VrrSetting { Enabled = true }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyVrrSettings_OneBackendFails_StillRunsTheOthersAndReportsFailure()
        {
            var failing = new FakeBackend(Nvidia) { ApplyResult = false };
            var other = new FakeBackend(Amd);
            var service = new VrrService([failing, other]);

            bool result = service.ApplyVrrSettings(new VrrSetting { Enabled = true, Mode = VrrMode.Off });

            Assert.IsFalse(result);
            Assert.AreEqual(1, failing.Applied.Count);
            Assert.AreEqual(1, other.Applied.Count);
        }

        private static readonly VrrCapability Nvidia = new(true, "NVIDIA G-SYNC", true, VrrMode.Off);
        private static readonly VrrCapability Amd = new(true, "AMD FreeSync", false, null);

        private sealed class FakeBackend(VrrCapability capability) : IVrrBackend
        {
            internal List<VrrMode> Applied { get; } = [];
            internal bool ApplyResult { get; set; } = true;

            public VrrCapability GetCapability() => capability;

            public bool Apply(VrrMode mode)
            {
                Applied.Add(mode);
                return ApplyResult;
            }
        }
    }
}
