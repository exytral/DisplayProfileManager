using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayConfigVirtualModeTopologyTests
    {
        private static AdapterLuid Adapter(uint low) => new() { LowPart = low, HighPart = 0 };

        private static DisplayConfigNative.DisplayConfigPathInfo Path(uint targetId, bool supportsVirtualMode, uint adapterLow = 1)
        {
            return new DisplayConfigNative.DisplayConfigPathInfo
            {
                sourceInfo = new DisplayConfigNative.DisplayConfigPathSourceInfo
                {
                    adapterId = DisplayConfigNative.ToNative(Adapter(adapterLow)),
                    id = 99,
                    modeInfoIdx = 7
                },
                targetInfo = new DisplayConfigNative.DisplayConfigPathTargetInfo
                {
                    adapterId = DisplayConfigNative.ToNative(Adapter(adapterLow)),
                    id = targetId,
                    modeInfoIdx = 8
                },
                flags = (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active |
                    (supportsVirtualMode
                        ? (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.SupportVirtualMode
                        : 0u)
            };
        }

        private static DisplayConfigInfo Display(uint targetId, uint sourceId) => new()
        {
                TargetId = targetId,
                SourceId = sourceId,
                IsEnabled = true
            };

        [TestMethod]
        [TestCategory("Unit")]
        public void PreparePathsForTopology_VirtualPath_UsesPackedCloneUnion()
        {
            var paths = new[] { Path(1, true) };
            var displays = new List<DisplayConfigInfo> { Display(1, 10) };

            DisplayConfigService.PreparePathsForTopology(paths, displays);

            Assert.AreEqual(0xFFFF0000u, paths[0].sourceInfo.modeInfoIdx);
            Assert.AreEqual(0xFFFFFFFFu, paths[0].targetInfo.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreparePathsForTopology_NonVirtualPath_UsesWholeInvalidModeIndex()
        {
            var paths = new[] { Path(1, false) };
            var displays = new List<DisplayConfigInfo> { Display(1, 10) };

            DisplayConfigService.PreparePathsForTopology(paths, displays);

            Assert.AreEqual(0xFFFFFFFFu, paths[0].sourceInfo.modeInfoIdx,
                "A non-virtual path interprets the whole union as modeInfoIdx.");
            Assert.AreEqual(0xFFFFFFFFu, paths[0].targetInfo.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreparePathsForTopology_NonVirtualExtendedPaths_GetDistinctSourceIds()
        {
            var paths = new[] { Path(1, false), Path(2, false) };
            var displays = new List<DisplayConfigInfo>
            {
                Display(1, 10),
                Display(2, 20)
            };

            DisplayConfigService.PreparePathsForTopology(paths, displays);

            Assert.AreNotEqual(paths[0].sourceInfo.id, paths[1].sourceInfo.id,
                "Invalid modeInfoIdx low bits must not collapse distinct extended groups together.");
            Assert.AreEqual(0xFFFFFFFFu, paths[0].sourceInfo.modeInfoIdx);
            Assert.AreEqual(0xFFFFFFFFu, paths[1].sourceInfo.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreparePathsForTopology_NonVirtualCloneMembers_ShareSourceId()
        {
            var paths = new[] { Path(1, false), Path(2, false) };
            var displays = new List<DisplayConfigInfo>
            {
                Display(1, 10),
                Display(2, 10)
            };

            DisplayConfigService.PreparePathsForTopology(paths, displays);

            Assert.AreEqual(paths[0].sourceInfo.id, paths[1].sourceInfo.id);
            Assert.AreEqual(0xFFFFFFFFu, paths[0].sourceInfo.modeInfoIdx);
            Assert.AreEqual(0xFFFFFFFFu, paths[1].sourceInfo.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreparePathsForTopology_MixedVirtualAndNonVirtualCloneMembers_ShareSemanticSourceId()
        {
            var paths = new[] { Path(1, true), Path(2, false) };
            var displays = new List<DisplayConfigInfo>
            {
                Display(1, 10),
                Display(2, 10)
            };

            DisplayConfigService.PreparePathsForTopology(paths, displays);

            Assert.AreEqual(paths[0].sourceInfo.id, paths[1].sourceInfo.id);
            Assert.AreEqual(0xFFFF0000u, paths[0].sourceInfo.modeInfoIdx);
            Assert.AreEqual(0xFFFFFFFFu, paths[1].sourceInfo.modeInfoIdx);
        }
    }
}
