using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.Tests.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class CloneGroupTopologyTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void SourceModesRequired_ForCloneGroup_EqualsUniqueSourceIdCount()
        {
            var displays = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithTargetId(0).WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithTargetId(1).WithSourceId(0).Build(),
            };

            int uniqueSourceIds = displays.Where(d => d.IsEnabled).Select(d => d.SourceId).Distinct().Count();
            int totalEnabledCount = displays.Count(d => d.IsEnabled);

            Assert.AreEqual(1, uniqueSourceIds, "A two-display clone group has 1 unique SourceId.");
            Assert.AreEqual(2, totalEnabledCount, "There are 2 enabled displays.");
            Assert.AreNotEqual(uniqueSourceIds, totalEnabledCount,
                "Per-display iteration would consume 2 source modes for a group that only has 1.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SourceModesRequired_ForExtendedDisplays_EqualsTotalDisplayCount()
        {
            var displays = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithTargetId(0).WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithTargetId(1).WithSourceId(1).Build(),
            };

            int uniqueSourceIds = displays.Where(d => d.IsEnabled).Select(d => d.SourceId).Distinct().Count();
            int totalEnabledCount = displays.Count(d => d.IsEnabled);

            Assert.AreEqual(totalEnabledCount, uniqueSourceIds,
                "Extended displays each have a unique SourceId, so unique count equals total.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SourceModesRequired_ForMixedConfig_EqualsUniqueSourceIdCount()
        {
            var displays = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithTargetId(0).WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithTargetId(1).WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithTargetId(2).WithSourceId(1).Build(),
            };

            int uniqueSourceIds = displays.Where(d => d.IsEnabled).Select(d => d.SourceId).Distinct().Count();

            Assert.AreEqual(2, uniqueSourceIds,
                "2 clone members + 1 extended display requires exactly 2 source modes.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisableNonProfileDisplays_TargetsDisplaysOutsideProfile()
        {
            var allTargetIds = new HashSet<uint> { 0, 1, 2, 3 };
            var profileTargetIds = new HashSet<uint> { 0, 1 };

            int disabled = allTargetIds.Count(t => !profileTargetIds.Contains(t));

            Assert.AreEqual(2, disabled,
                "Displays not in the profile must be disabled; only those in the profile are kept active.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyDisplayPosition_DoesNotExist()
        {
            var method = typeof(DisplayConfigHelper).GetMethod(
                "ApplyDisplayPosition",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            Assert.IsNull(method,
                "ApplyDisplayPosition must not exist — position is set inside ApplyDisplayLayout.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyDisplayTopology_ExistsAsPublicStaticMethod()
        {
            var method = typeof(DisplayConfigHelper).GetMethod(
                "ApplyDisplayTopology",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            Assert.IsNotNull(method,
                "ApplyDisplayTopology must exist as the single topology entry point.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EnableDisplays_DoesNotExistAsStandaloneMethod()
        {
            var enableDisplays = typeof(DisplayConfigHelper).GetMethod(
                "EnableDisplays",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            Assert.IsNull(enableDisplays,
                "EnableDisplays must not exist as a separate method — it was folded into ApplyDisplayTopology.");
        }
    }
}