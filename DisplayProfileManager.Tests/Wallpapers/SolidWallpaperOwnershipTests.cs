using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class SolidWallpaperOwnershipTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ClassicSolidBridge_WritesEmptyLegacyWallpaperBeforeShellApply()
        {
            var calls = new List<string>();
            bool result = WallpaperApply.ApplyClassicSolidOwnershipBridge(
                () => { calls.Add("registry-empty"); return true; },
                () => { calls.Add("spi-empty"); return true; });

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "registry-empty", "spi-empty" }, calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassicSolidBridge_RegistryFailureStopsBeforeShellApply()
        {
            bool shellCalled = false;
            bool result = WallpaperApply.ApplyClassicSolidOwnershipBridge(
                () => false,
                () => { shellCalled = true; return true; });

            Assert.IsFalse(result);
            Assert.IsFalse(shellCalled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowSupersession_StillRequiresAuthoritativePostBridgeClear()
        {
            var states = new Queue<bool?>([true, false]);
            int bridgeCalls = 0;
            var outcome = WallpaperApply.SupersedeSlideshowOwnership(
                () => states.Dequeue(),
                () => { bridgeCalls++; return true; });

            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual(1, bridgeCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowSupersession_BridgeCannotClearStatus_RemainsFailure()
        {
            var states = new Queue<bool?>([true, true]);
            var outcome = WallpaperApply.SupersedeSlideshowOwnership(
                () => states.Dequeue(),
                () => true);

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
        }
    }
}
