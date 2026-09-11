using DisplayProfileManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ProfileApplyPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyWarningSummary_AllSecondaryFailures_UsesApplyOrder()
        {
            var result = new ProfileManager.ProfileApplyResult
            {
                AdvancedColorSuccess = false,
                ColorProfileSuccess = false,
                DpiChanged = false,
                AudioSuccess = false
            };

            Assert.AreEqual("Advanced Color, Color Profile, DPI, Audio", ProfileManager.GetApplyWarningSummary(result));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyWarningSummary_SuccessfulSecondaryStages_ReturnsEmpty()
        {
            var result = new ProfileManager.ProfileApplyResult
            {
                AdvancedColorSuccess = true,
                ColorProfileSuccess = true,
                DpiChanged = true,
                AudioSuccess = true
            };

            Assert.AreEqual(string.Empty, ProfileManager.GetApplyWarningSummary(result));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppendApplyWarnings_WarningsPresent_AppendsNonBlockingSummary()
        {
            string message = ProfileManager.AppendApplyWarnings("Applied in 1.2 seconds", "Advanced Color, Audio");

            Assert.AreEqual("Applied in 1.2 seconds — Advanced Color, Audio failed to apply", message);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileAppliedEventArgs_WarningSummaryProvided_PreservesSummary()
        {
            var args = new ProfileManager.ProfileAppliedEventArgs(new Profile("Test"), ProfileManager.ApplySource.Tray, 1200, "DPI");

            Assert.AreEqual("DPI", args.WarningSummary);
        }
    }
}
