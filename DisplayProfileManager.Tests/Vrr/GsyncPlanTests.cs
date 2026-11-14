using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Vrr.Models;
using DisplayProfileManager.Vrr.Nvidia;

namespace DisplayProfileManager.Tests.Vrr
{
    [TestClass]
    public class GsyncPlanTests
    {
        private static readonly uint[] OffValues = [0, 4, 0, 0, 0];
        private static readonly uint[] OnValues = [1, 0, 1, 1, 1];

        [TestMethod]
        [TestCategory("Unit")]
        public void Decode_KnownValues_ReturnModes()
        {
            Assert.AreEqual(VrrMode.Off, GsyncPlan.Decode(0));
            Assert.AreEqual(VrrMode.OnFullscreenOnly, GsyncPlan.Decode(1));
            Assert.AreEqual(VrrMode.On, GsyncPlan.Decode(2));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Decode_UnknownValue_ReturnsNull() => Assert.IsNull(GsyncPlan.Decode(9));

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_OffFromOn_ClearsFeatureAndSetsOverride()
        {
            CollectionAssert.AreEqual(OffValues, GsyncPlan.MakeTarget(VrrMode.Off, OnValues));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_FullscreenOnlyFromOff_EnablesEverySetting()
        {
            CollectionAssert.AreEqual(OnValues, GsyncPlan.MakeTarget(VrrMode.OnFullscreenOnly, OffValues));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_OnFromFullscreenOnly_KeepsPerFeatureValues()
        {
            uint[] current = [1, 2, 0, 1, 0];

            CollectionAssert.AreEqual(new uint[] { 2, 2, 0, 2, 0 }, GsyncPlan.MakeTarget(VrrMode.On, current));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_OnFromUnknownMode_UsesFullPlan()
        {
            uint[] current = [9, 2, 0, 1, 0];

            CollectionAssert.AreEqual(OnValues, GsyncPlan.MakeTarget(VrrMode.OnFullscreenOnly, current));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_SameMode_MatchesCurrentValues()
        {
            Assert.IsTrue(GsyncPlan.SameValues(OnValues, GsyncPlan.MakeTarget(VrrMode.OnFullscreenOnly, OnValues)));
            Assert.IsTrue(GsyncPlan.SameValues(OffValues, GsyncPlan.MakeTarget(VrrMode.Off, OffValues)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsOnOffChange_DetectsOnlyOnOffTransitions()
        {
            uint[] windowed = [2, 0, 1, 2, 1];

            Assert.IsTrue(GsyncPlan.IsOnOffChange(OffValues, OnValues));
            Assert.IsTrue(GsyncPlan.IsOnOffChange(windowed, OffValues));
            Assert.IsFalse(GsyncPlan.IsOnOffChange(OnValues, windowed));
            Assert.IsFalse(GsyncPlan.IsOnOffChange(OffValues, OffValues));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SaveGroups_CoverEverySettingOnce()
        {
            var seen = new bool[GsyncPlan.SettingCount];
            foreach (var group in GsyncPlan.SaveGroups)
            {
                foreach (int index in group)
                {
                    Assert.IsFalse(seen[index], $"Setting {index} appears twice");
                    seen[index] = true;
                }
            }

            CollectionAssert.DoesNotContain(seen, false);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasCapableDisplay_AnyCapableDisplay_ReturnsTrue()
        {
            Assert.IsTrue(GsyncPlan.HasCapableDisplay(new List<bool?> { false, true }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasCapableDisplay_EveryAnswerNo_ReturnsFalse()
        {
            Assert.IsFalse(GsyncPlan.HasCapableDisplay(new List<bool?> { false, false }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasCapableDisplay_NoAnswerFromDriver_FallsBackToCapable()
        {
            Assert.IsTrue(GsyncPlan.HasCapableDisplay(new List<bool?>()));
            Assert.IsTrue(GsyncPlan.HasCapableDisplay(new List<bool?> { null }));
            Assert.IsTrue(GsyncPlan.HasCapableDisplay(new List<bool?> { false, null }));
        }
    }
}
