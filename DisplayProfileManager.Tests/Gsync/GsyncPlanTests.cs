using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Gsync;
using DisplayProfileManager.Gsync.Models;

namespace DisplayProfileManager.Tests.Gsync
{
    [TestClass]
    public class GsyncPlanTests
    {
        private static readonly uint[] OffValues = [0, 4, 0, 0, 0];
        private static readonly uint[] FullscreenValues = [1, 0, 1, 1, 1];

        [TestMethod]
        [TestCategory("Unit")]
        public void Decode_KnownValues_ReturnModes()
        {
            Assert.AreEqual(GsyncMode.Off, GsyncPlan.Decode(0));
            Assert.AreEqual(GsyncMode.Fullscreen, GsyncPlan.Decode(1));
            Assert.AreEqual(GsyncMode.FullscreenAndWindowed, GsyncPlan.Decode(2));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Decode_UnknownValue_ReturnsNull() => Assert.IsNull(GsyncPlan.Decode(9));

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_OffFromFullscreen_ClearsFeatureAndSetsOverride()
        {
            CollectionAssert.AreEqual(OffValues, GsyncPlan.MakeTarget(GsyncMode.Off, FullscreenValues));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_FullscreenFromOff_EnablesEverySetting()
        {
            CollectionAssert.AreEqual(FullscreenValues, GsyncPlan.MakeTarget(GsyncMode.Fullscreen, OffValues));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_WindowedFromFullscreen_KeepsPerFeatureValues()
        {
            uint[] current = [1, 2, 0, 1, 0];

            CollectionAssert.AreEqual(new uint[] { 2, 2, 0, 2, 0 }, GsyncPlan.MakeTarget(GsyncMode.FullscreenAndWindowed, current));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_OnFromUnknownMode_UsesFullPlan()
        {
            uint[] current = [9, 2, 0, 1, 0];

            CollectionAssert.AreEqual(FullscreenValues, GsyncPlan.MakeTarget(GsyncMode.Fullscreen, current));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MakeTarget_SameMode_MatchesCurrentValues()
        {
            Assert.IsTrue(GsyncPlan.SameValues(FullscreenValues, GsyncPlan.MakeTarget(GsyncMode.Fullscreen, FullscreenValues)));
            Assert.IsTrue(GsyncPlan.SameValues(OffValues, GsyncPlan.MakeTarget(GsyncMode.Off, OffValues)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsOnOffChange_DetectsOnlyOnOffTransitions()
        {
            uint[] windowed = [2, 0, 1, 2, 1];

            Assert.IsTrue(GsyncPlan.IsOnOffChange(OffValues, FullscreenValues));
            Assert.IsTrue(GsyncPlan.IsOnOffChange(windowed, OffValues));
            Assert.IsFalse(GsyncPlan.IsOnOffChange(FullscreenValues, windowed));
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
    }
}
