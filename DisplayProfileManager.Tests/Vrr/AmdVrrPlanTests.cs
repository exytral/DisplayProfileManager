using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Vrr.Amd;
using DisplayProfileManager.Vrr.Models;

namespace DisplayProfileManager.Tests.Vrr
{
    [TestClass]
    public class AmdVrrPlanTests
    {
        private static readonly AmdVrrPlan.DisplayState SupportedOff = new(true, false);
        private static readonly AmdVrrPlan.DisplayState SupportedOn = new(true, true);
        private static readonly AmdVrrPlan.DisplayState Unsupported = new(false, false);

        [TestMethod]
        [TestCategory("Unit")]
        public void ShouldEnable_OnlyOffDisables()
        {
            Assert.IsFalse(AmdVrrPlan.ShouldEnable(VrrMode.Off));
            Assert.IsTrue(AmdVrrPlan.ShouldEnable(VrrMode.On));
            Assert.IsTrue(AmdVrrPlan.ShouldEnable(VrrMode.OnIncludingWindowed));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EffectiveMode_WindowedDegradesToOn()
        {
            Assert.AreEqual(VrrMode.On, AmdVrrPlan.EffectiveMode(VrrMode.OnIncludingWindowed));
            Assert.AreEqual(VrrMode.On, AmdVrrPlan.EffectiveMode(VrrMode.On));
            Assert.AreEqual(VrrMode.Off, AmdVrrPlan.EffectiveMode(VrrMode.Off));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectChanges_EnableSkipsUnsupportedAndAlreadyOn()
        {
            var displays = new List<AmdVrrPlan.DisplayState> { SupportedOff, Unsupported, SupportedOn, SupportedOff };

            CollectionAssert.AreEqual(new[] { 0, 3 }, AmdVrrPlan.SelectChanges(displays, VrrMode.On));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectChanges_OffTouchesOnlySupportedEnabledDisplays()
        {
            var displays = new List<AmdVrrPlan.DisplayState> { SupportedOn, new(false, true), SupportedOff };

            CollectionAssert.AreEqual(new[] { 0 }, AmdVrrPlan.SelectChanges(displays, VrrMode.Off));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SelectChanges_NothingSupported_ReturnsEmpty()
        {
            Assert.AreEqual(0, AmdVrrPlan.SelectChanges([Unsupported], VrrMode.On).Count);
            Assert.AreEqual(0, AmdVrrPlan.SelectChanges([], VrrMode.Off).Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DecodeCurrent_AgreeingDisplays_ReturnMode()
        {
            Assert.AreEqual(VrrMode.On, AmdVrrPlan.DecodeCurrent([SupportedOn, Unsupported, SupportedOn]));
            Assert.AreEqual(VrrMode.Off, AmdVrrPlan.DecodeCurrent([SupportedOff, SupportedOff]));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DecodeCurrent_MixedOrUnsupported_ReturnsNull()
        {
            Assert.IsNull(AmdVrrPlan.DecodeCurrent([SupportedOn, SupportedOff]));
            Assert.IsNull(AmdVrrPlan.DecodeCurrent([Unsupported]));
            Assert.IsNull(AmdVrrPlan.DecodeCurrent([]));
        }
    }
}
