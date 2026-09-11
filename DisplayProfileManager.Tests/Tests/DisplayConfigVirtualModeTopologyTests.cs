using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class DisplayConfigVirtualModeTopologyTests
    {
        private static DisplayConfigHelper.LUID Adapter(uint low) => new DisplayConfigHelper.LUID { LowPart = low, HighPart = 0 };

        private static DisplayConfigHelper.DisplayConfigPathInfo Path(uint targetId, bool supportsVirtualMode, uint adapterLow = 1)
        {
            return new DisplayConfigHelper.DisplayConfigPathInfo
            {
                sourceInfo = new DisplayConfigHelper.DisplayConfigPathSourceInfo
                {
                    adapterId = Adapter(adapterLow),
                    id = 99,
                    modeInfoIdx = 7
                },
                targetInfo = new DisplayConfigHelper.DisplayConfigPathTargetInfo
                {
                    adapterId = Adapter(adapterLow),
                    id = targetId,
                    modeInfoIdx = 8
                },
                flags = (uint)DisplayConfigHelper.DisplayConfigPathInfoFlags.Active |
                    (supportsVirtualMode
                        ? (uint)DisplayConfigHelper.DisplayConfigPathInfoFlags.SupportVirtualMode
                        : 0u)
            };
        }

        private static DisplayConfigHelper.DisplayConfigInfo Display(uint targetId, uint sourceId) => new DisplayConfigHelper.DisplayConfigInfo
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
            var displays = new List<DisplayConfigHelper.DisplayConfigInfo> { Display(1, 10) };

            DisplayConfigHelper.PreparePathsForTopology(paths, displays);

            Assert.AreEqual(0xFFFF0000u, paths[0].sourceInfo.modeInfoIdx);
            Assert.AreEqual(0xFFFFFFFFu, paths[0].targetInfo.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreparePathsForTopology_NonVirtualPath_UsesWholeInvalidModeIndex()
        {
            var paths = new[] { Path(1, false) };
            var displays = new List<DisplayConfigHelper.DisplayConfigInfo> { Display(1, 10) };

            DisplayConfigHelper.PreparePathsForTopology(paths, displays);

            Assert.AreEqual(0xFFFFFFFFu, paths[0].sourceInfo.modeInfoIdx,
                "A non-virtual path interprets the whole union as modeInfoIdx.");
            Assert.AreEqual(0xFFFFFFFFu, paths[0].targetInfo.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreparePathsForTopology_NonVirtualExtendedPaths_GetDistinctSourceIds()
        {
            var paths = new[] { Path(1, false), Path(2, false) };
            var displays = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(1, 10),
                Display(2, 20)
            };

            DisplayConfigHelper.PreparePathsForTopology(paths, displays);

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
            var displays = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(1, 10),
                Display(2, 10)
            };

            DisplayConfigHelper.PreparePathsForTopology(paths, displays);

            Assert.AreEqual(paths[0].sourceInfo.id, paths[1].sourceInfo.id);
            Assert.AreEqual(0xFFFFFFFFu, paths[0].sourceInfo.modeInfoIdx);
            Assert.AreEqual(0xFFFFFFFFu, paths[1].sourceInfo.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreparePathsForTopology_MixedVirtualAndNonVirtualCloneMembers_ShareSemanticSourceId()
        {
            var paths = new[] { Path(1, true), Path(2, false) };
            var displays = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(1, 10),
                Display(2, 10)
            };

            DisplayConfigHelper.PreparePathsForTopology(paths, displays);

            Assert.AreEqual(paths[0].sourceInfo.id, paths[1].sourceInfo.id);
            Assert.AreEqual(0xFFFF0000u, paths[0].sourceInfo.modeInfoIdx);
            Assert.AreEqual(0xFFFFFFFFu, paths[1].sourceInfo.modeInfoIdx);
        }
    }
}
