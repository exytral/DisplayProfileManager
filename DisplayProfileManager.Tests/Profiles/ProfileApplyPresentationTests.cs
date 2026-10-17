using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.UI.Presentation;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileApplyPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyWarningSummary_AllSecondaryFailures_UsesApplyOrder()
        {
            var result = new ProfileApplyResult
            {
                AdvancedColorSuccess = false,
                ColorProfileSuccess = false,
                DpiChanged = false,
                AudioSuccess = false
            };

            Assert.AreEqual("Advanced Color, Color Profile, DPI, Audio", ProfileApplyFormatter.GetApplyWarningSummary(result));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyWarningSummary_SuccessfulSecondaryStages_ReturnsEmpty()
        {
            var result = new ProfileApplyResult
            {
                AdvancedColorSuccess = true,
                ColorProfileSuccess = true,
                DpiChanged = true,
                AudioSuccess = true
            };

            Assert.AreEqual(string.Empty, ProfileApplyFormatter.GetApplyWarningSummary(result));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AppendApplyWarnings_WarningsPresent_AppendsNonBlockingSummary()
        {
            string message = ProfileApplyFormatter.AppendApplyWarnings("Applied in 1.2 seconds", "Advanced Color, Audio");

            Assert.AreEqual("Applied in 1.2 seconds — Advanced Color, Audio failed to apply", message);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileAppliedEventArgs_WarningSummaryProvided_PreservesSummary()
        {
            var args = new ProfileAppliedEventArgs(new Profile("Test"), ApplySource.Tray, 1200, "DPI");

            Assert.AreEqual("DPI", args.WarningSummary);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplySourceLabel_FirstPartyExternalSources_AreDistinct()
        {
            Assert.AreEqual("Applied from desktop context menu", ProfileApplyFormatter.GetApplySourceLabel(ApplySource.DesktopContextMenu));
            Assert.AreEqual("Applied from Start menu", ProfileApplyFormatter.GetApplySourceLabel(ApplySource.StartMenuShortcut));
            Assert.AreEqual("Applied via CLI", ProfileApplyFormatter.GetApplySourceLabel(ApplySource.CommandLine));
            Assert.AreEqual("Applied by Quick Switch", ProfileApplyFormatter.GetApplySourceLabel(ApplySource.QuickSwitch));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetApplyWarningSummary_CurrentProfilePersistenceFailure_ReportsSecondaryWarning()
        {
            var result = new ProfileApplyResult
            {
                DpiChanged = true,
                AudioSuccess = true,
                CurrentProfilePersisted = false
            };

            Assert.AreEqual("Current Profile Marker", ProfileApplyFormatter.GetApplyWarningSummary(result));
        }
    }
}
