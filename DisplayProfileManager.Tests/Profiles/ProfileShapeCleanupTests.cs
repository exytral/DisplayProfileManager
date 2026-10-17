using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles.Policy;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileShapeCleanupTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyRuntimeAddressMembers_LoadButDoNotReserialize()
        {
            var setting = JsonConvert.DeserializeObject<DisplaySetting>(@"{
                ""adapterId"": ""0000000000000111"",
                ""targetId"": 33,
                ""sourceId"": 7,
                ""pathIndex"": 9,
                ""manufacturerName"": ""MAN"",
                ""productCodeID"": ""C004""
            }");

            var root = JObject.Parse(JsonConvert.SerializeObject(setting));

            Assert.IsNotNull(setting);
            Assert.AreEqual(33u, setting.TargetId);
            Assert.IsTrue(setting.HasEdidIdentity);
            Assert.IsNull(root["adapterId"]);
            Assert.IsNull(root["sourceId"]);
            Assert.IsNull(root["pathIndex"]);
            Assert.IsNull(root["hasEdidIdentity"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AssignDesiredSourceGroups_CloneGroupIdDrivesTopologyGrouping()
        {
            var adapter = new AdapterLuid { LowPart = 1 };
            var first = new DisplaySetting { CloneGroupId = "clone-a" };
            var second = new DisplaySetting { CloneGroupId = "clone-a" };
            var independent = new DisplaySetting();
            var firstMapped = new DisplayConfigInfo { AdapterId = adapter, TargetId = 1 };
            var secondMapped = new DisplayConfigInfo { AdapterId = adapter, TargetId = 2 };
            var independentMapped = new DisplayConfigInfo { AdapterId = adapter, TargetId = 3 };

            DisplayApplyPolicy.AssignDesiredSourceGroups(
            [
                (first, firstMapped),
                (second, secondMapped),
                (independent, independentMapped)
            ]);

            Assert.AreEqual(firstMapped.SourceId, secondMapped.SourceId);
            Assert.AreNotEqual(firstMapped.SourceId, independentMapped.SourceId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AssignDesiredSourceGroups_SameCloneLabelAcrossAdapters_RemainsAdapterLocal()
        {
            var first = new DisplaySetting { CloneGroupId = "clone-a" };
            var second = new DisplaySetting { CloneGroupId = "clone-a" };
            var firstMapped = new DisplayConfigInfo
            {
                AdapterId = new AdapterLuid { LowPart = 1 },
                TargetId = 1
            };
            var secondMapped = new DisplayConfigInfo
            {
                AdapterId = new AdapterLuid { LowPart = 2 },
                TargetId = 1
            };

            DisplayApplyPolicy.AssignDesiredSourceGroups(
            [
                (first, firstMapped),
                (second, secondMapped)
            ]);

            Assert.IsFalse(CcdAddress.Source(firstMapped).Equals(CcdAddress.Source(secondMapped)));
        }
    }
}
