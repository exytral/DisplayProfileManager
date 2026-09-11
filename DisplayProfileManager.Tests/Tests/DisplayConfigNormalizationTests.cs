using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.Tests.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class BuildSourceIdMapTests
    {
        private static Dictionary<uint, uint> Map(params (uint sourceId, bool enabled)[] displays)
        {
            var configs = displays
                .Select(d => new DisplayConfigHelper.DisplayConfigInfo
                {
                    SourceId = d.sourceId,
                    IsEnabled = d.enabled,
                })
                .ToList();
            return DisplayConfigHelper.BuildSourceIdMap(configs);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_WithGap_ProducesContiguousIndices()
        {
            var map = Map((0, true), (2, true));

            Assert.AreEqual(0u, map[0], "SourceId 0 must normalize to 0.");
            Assert.AreEqual(1u, map[2], "SourceId 2 must normalize to 1, not 2.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_AlreadyContiguous_IsNoOp()
        {
            var map = Map((0, true), (1, true), (2, true));

            Assert.AreEqual(0u, map[0]);
            Assert.AreEqual(1u, map[1]);
            Assert.AreEqual(2u, map[2]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_SingleDisplay_NormalizesToZero()
        {
            var map = Map((0, true));

            Assert.AreEqual(0u, map[0]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_SingleDisplayWithNonZeroSourceId_NormalizesToZero()
        {
            var map = Map((4, true));

            Assert.AreEqual(0u, map[4], "A lone enabled display must normalize to SourceId 0 regardless of its saved value.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_LargeGaps_ProducesContiguousOutput()
        {
            var map = Map((0, true), (5, true), (12, true));

            Assert.AreEqual(0u, map[0]);
            Assert.AreEqual(1u, map[5]);
            Assert.AreEqual(2u, map[12]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_OutputCountEqualsUniqueEnabledSourceIds()
        {
            var map = Map((0, true), (1, true), (2, true), (3, false));

            Assert.AreEqual(3, map.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_DisabledDisplays_AreExcluded()
        {
            var map = Map((0, true), (1, false), (2, true));

            Assert.IsFalse(map.ContainsKey(1), "Disabled displays must not appear in the source ID map.");
            Assert.AreEqual(2, map.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_AllDisabled_ReturnsEmptyMap()
        {
            var map = Map((0, false), (1, false));

            Assert.AreEqual(0, map.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_EmptyList_ReturnsEmptyMap()
        {
            var map = DisplayConfigHelper.BuildSourceIdMap(new List<DisplayConfigHelper.DisplayConfigInfo>());

            Assert.AreEqual(0, map.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_CloneGroupMembers_GetSameNormalizedId()
        {
            var configs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(1).Build(),
            };

            var map = DisplayConfigHelper.BuildSourceIdMap(configs);

            Assert.AreEqual(2, map.Count, "Two unique SourceIds must produce two map entries, not one per display.");
            Assert.AreEqual(0u, map[0], "Clone group SourceId=0 must normalize to 0.");
            Assert.AreEqual(1u, map[1], "Extended SourceId=1 must normalize to 1.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_CloneGroupWithNonZeroSourceId_NormalizesCorrectly()
        {
            var map = Map((3, true), (3, true), (5, true));

            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(0u, map[3], "Clone group SourceId=3 must normalize to 0.");
            Assert.AreEqual(1u, map[5], "Extended SourceId=5 must normalize to 1.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_OutputIsDeterministic_RegardlessOfInputOrder()
        {
            var forward = DisplayConfigHelper.BuildSourceIdMap(new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(2).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(4).Build(),
            });

            var reversed = DisplayConfigHelper.BuildSourceIdMap(new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithSourceId(4).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(2).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(0).Build(),
            });

            Assert.AreEqual(forward[0], reversed[0]);
            Assert.AreEqual(forward[2], reversed[2]);
            Assert.AreEqual(forward[4], reversed[4]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_SubmittedSourceIds_AreContiguous()
        {
            var configs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(2).Build(),
            };
            var map = DisplayConfigHelper.BuildSourceIdMap(configs);

            var submitted = configs
                .Where(c => c.IsEnabled)
                .Select(c => map[c.SourceId])
                .OrderBy(x => x)
                .ToList();

            CollectionAssert.AreEqual(new List<uint> { 0, 1 }, submitted, "Normalized SourceIds submitted to SetDisplayConfig must be contiguous starting from 0.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_SubmittedSourceIds_AreUnique()
        {
            var configs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(3).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(7).Build(),
            };
            var map = DisplayConfigHelper.BuildSourceIdMap(configs);

            var normalized = configs
                .Where(c => c.IsEnabled)
                .Select(c => map[c.SourceId])
                .ToList();

            Assert.AreEqual(normalized.Count, normalized.Distinct().Count(), "All normalized SourceIds submitted to SetDisplayConfig must be unique.");
        }
    }
}
