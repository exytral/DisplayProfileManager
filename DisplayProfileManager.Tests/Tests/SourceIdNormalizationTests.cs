using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;
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

            Assert.AreEqual(0u, map[4],
                "A lone enabled display must normalize to SourceId 0 regardless of its saved value.");
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

            Assert.IsFalse(map.ContainsKey(1),
                "Disabled displays must not appear in the source ID map.");
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

            Assert.AreEqual(2, map.Count,
                "Two unique SourceIds must produce two map entries, not one per display.");
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

            CollectionAssert.AreEqual(new List<uint> { 0, 1 }, submitted,
                "Normalized SourceIds submitted to SetDisplayConfig must be contiguous starting from 0.");
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

            Assert.AreEqual(normalized.Count, normalized.Distinct().Count(),
                "All normalized SourceIds submitted to SetDisplayConfig must be unique.");
        }
    }

    [TestClass]
    public class SourceIdNormalizationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_WithGapInSourceIds_RequiresNormalization()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithSourceId(2).Build(),
            };

            var ids = settings.Where(s => s.IsEnabled).Select(s => s.SourceId).OrderBy(x => x).ToList();

            Assert.AreEqual(0u, ids[0]);
            Assert.AreEqual(2u, ids[1]);
            Assert.AreNotEqual((uint)(ids.Count - 1), ids.Last(),
                "Non-contiguous SourceIds must be normalized before passing to SetDisplayConfig.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Profile_WithContiguousSourceIds_DoesNotRequireNormalization()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithSourceId(1).Build(),
            };

            var ids = settings.Where(s => s.IsEnabled).Select(s => s.SourceId).OrderBy(x => x).ToList();

            for (int i = 0; i < ids.Count; i++)
                Assert.AreEqual((uint)i, ids[i], $"SourceId[{i}] is already {i} — no normalization needed.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CloneGroupDetection_GroupsBySourceId()
        {
            var configs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithSourceId(0).WithTargetId(0).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(0).WithTargetId(1).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(1).WithTargetId(2).Build(),
            };

            var cloneGroups = configs
                .GroupBy(dc => dc.SourceId)
                .Where(g => g.Count() > 1)
                .ToList();

            Assert.AreEqual(1, cloneGroups.Count, "One clone group must be detected.");
            Assert.AreEqual(0u, cloneGroups[0].Key, "Clone group must have SourceId=0.");
            Assert.AreEqual(2, cloneGroups[0].Count(), "Clone group must have 2 members.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CloneGroupDetection_ExtendedDisplays_ProduceNoGroups()
        {
            var configs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithSourceId(0).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(1).Build(),
                new DisplayConfigInfoBuilder().WithSourceId(2).Build(),
            };

            var cloneGroups = configs.GroupBy(dc => dc.SourceId).Where(g => g.Count() > 1).ToList();

            Assert.AreEqual(0, cloneGroups.Count,
                "Fully extended configuration must produce no clone groups.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DpiDeduplication_CloneGroupMembers_YieldsOneEntryPerDevice()
        {
            var s1a = new DisplaySettingBuilder().WithCloneGroup("c").Build();
            s1a.DeviceName = "\\\\.\\DISPLAY1";
            var s1b = new DisplaySettingBuilder().WithCloneGroup("c").Build();
            s1b.DeviceName = "\\\\.\\DISPLAY1";
            var s2 = new DisplaySettingBuilder().Build();
            s2.DeviceName = "\\\\.\\DISPLAY2";

            var deduped = new List<DisplaySetting> { s1a, s1b, s2 }
                .Where(s => s.IsEnabled)
                .GroupBy(s => s.DeviceName)
                .Select(g => g.First())
                .ToList();

            Assert.AreEqual(2, deduped.Count,
                "Two unique DeviceNames must produce 2 DPI entries, not one per clone member.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DpiDeduplication_DisabledDisplays_AreExcluded()
        {
            var enabled = new DisplaySettingBuilder().Build();
            enabled.DeviceName = "\\\\.\\DISPLAY1";
            var disabled = new DisplaySettingBuilder().Build();
            disabled.DeviceName = "\\\\.\\DISPLAY2";
            disabled.IsEnabled = false;

            var forDpi = new List<DisplaySetting> { enabled, disabled }
                .Where(s => s.IsEnabled)
                .GroupBy(s => s.DeviceName)
                .Select(g => g.First())
                .ToList();

            Assert.AreEqual(1, forDpi.Count);
            Assert.AreEqual("\\\\.\\DISPLAY1", forDpi[0].DeviceName);
        }
    }

    [TestClass]
    public class ApplyProfileScriptLogicTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void EnableScriptsFalse_ScriptListIsPreserved()
        {
            var profile = new Profile("Test");
            profile.Scripts.Add("myscript.ps1");
            profile.EnableScripts = false;

            Assert.AreEqual(1, profile.Scripts.Count,
                "Scripts must remain stored when EnableScripts is false.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EnableScriptsTrue_WithEmptyList_DoesNotExecute()
        {
            var profile = new Profile("Test");
            profile.EnableScripts = true;

            bool wouldExecute = profile.EnableScripts && profile.Scripts != null && profile.Scripts.Any();

            Assert.IsFalse(wouldExecute,
                "No scripts must execute when the list is empty, even if EnableScripts is true.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EnableScriptsFalse_WithScripts_DoesNotExecute()
        {
            var profile = new Profile("Test");
            profile.Scripts.Add("myscript.ps1");
            profile.EnableScripts = false;

            bool wouldExecute = profile.EnableScripts && profile.Scripts != null && profile.Scripts.Any();

            Assert.IsFalse(wouldExecute,
                "Scripts must not execute when EnableScripts is false, regardless of list contents.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EnableScriptsTrue_WithScripts_Executes()
        {
            var profile = new Profile("Test");
            profile.Scripts.Add("myscript.ps1");
            profile.EnableScripts = true;

            bool wouldExecute = profile.EnableScripts && profile.Scripts != null && profile.Scripts.Any();

            Assert.IsTrue(wouldExecute);
        }
    }
}