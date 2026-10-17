using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Events;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayEventSnapshotTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void CreateDisplayEventSnapshot_UnorderedAdapterQualifiedRows_ProducesStableOrderAndDigest()
        {
            var activeA = new List<DisplayConfigInfo>
            {
                Display(2, 9, 22, 2),
                Display(1, 4, 11, 1)
            };
            var activeB = new List<DisplayConfigInfo>
            {
                Display(1, 4, 11, 1),
                Display(2, 9, 22, 2)
            };
            var pathsA = new List<DisplayEventPathState>
            {
                Path("0000000200000000", 9, 22, 3),
                Path("0000000100000000", 4, 11, 2)
            };
            var pathsB = new List<DisplayEventPathState>
            {
                Path("0000000100000000", 4, 11, 2),
                Path("0000000200000000", 9, 22, 3)
            };

            var first = DisplayConfigService.CreateDisplayEventSnapshot(0, 0, activeA, pathsA);
            var second = DisplayConfigService.CreateDisplayEventSnapshot(0, 0, activeB, pathsB);

            Assert.AreEqual("0000000100000000", first.ActiveDisplays[0].AdapterLuid);
            Assert.AreEqual("0000000100000000", first.AllPaths[0].AdapterLuid);
            Assert.AreEqual(first.TopologyDigest, second.TopologyDigest);
            Assert.AreEqual(64, first.TopologyDigest.Length);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CreateDisplayEventSnapshot_QueryStatusChanges_ChangesDigest()
        {
            var active = new[] { Display(1, 4, 11, 1) };
            var paths = new[] { Path("0000000100000000", 4, 11, 2) };

            var success = DisplayConfigService.CreateDisplayEventSnapshot(0, 0, active, paths);
            var failed = DisplayConfigService.CreateDisplayEventSnapshot(5, 0, active, paths);

            Assert.AreNotEqual(success.TopologyDigest, failed.TopologyDigest);
            Assert.AreEqual(5, failed.ActiveQueryStatus);
        }

        private static DisplayConfigInfo Display(int high, uint source, uint target, uint path) => new()
        {
            AdapterId = new AdapterLuid { HighPart = high, LowPart = 0 },
            SourceId = source,
            TargetId = target,
            RawTargetId = target,
            PathIndex = path,
            DeviceName = $"DISPLAY{path}"
        };

        private static DisplayEventPathState Path(string adapter, uint source, uint target, uint path) => new()
        {
            AdapterLuid = adapter,
            SourceId = source,
            TargetId = target,
            RawTargetId = target,
            PathIndex = path,
            SourceGroup = $"{adapter}:{source}"
        };
    }
}
