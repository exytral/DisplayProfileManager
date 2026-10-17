using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.UI.Controls;
using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class AdvancedColorEditorCapabilityTests
    {
        private static DisplaySetting Setting(uint targetId, bool? hdrSupported, bool? wcgSupported, bool hdrEnabled = false, bool wcgEnabled = false) =>
            new()
            {
                TargetId = targetId,
                LastKnownHdrSupported = hdrSupported,
                LastKnownWcgSupported = wcgSupported,
                IsHdrEnabled = hdrEnabled,
                IsWcgEnabled = wcgEnabled
            };

        private static DisplayConfigInfo Live(uint targetId, bool hdrSupported, bool wcgSupported, bool available = true) =>
            new()
            {
                TargetId = targetId,
                RawTargetId = targetId,
                IsAdvancedColorInfoAvailable = available,
                IsHdrSupported = hdrSupported,
                IsWcgSupported = wcgSupported
            };

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCapabilities_InactiveKnownTarget_UsesLastKnownEvidenceWithoutLiveAuthority()
        {
            var setting = Setting(11, hdrSupported: true, wcgSupported: false);

            var (hasLiveObservation, hdrSupported, wcgSupported) = DisplaySettingControl.ResolveAdvancedColorCapabilitiesForEditor(setting, []);

            Assert.IsFalse(hasLiveObservation);
            Assert.AreEqual(true, hdrSupported);
            Assert.AreEqual(false, wcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCapabilities_InactiveUnknownTarget_RemainsUnknown()
        {
            var (hasLiveObservation, hdrSupported, wcgSupported) = DisplaySettingControl.ResolveAdvancedColorCapabilitiesForEditor(Setting(11, hdrSupported: null, wcgSupported: null), []);

            Assert.IsFalse(hasLiveObservation);
            Assert.IsNull(hdrSupported);
            Assert.IsNull(wcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ReconcileCapabilities_KnownLiveObservation_RefreshesRuntimeAndLastKnownEvidence()
        {
            var setting = Setting(11, hdrSupported: true, wcgSupported: false);

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(setting, Live(11, hdrSupported: false, wcgSupported: true));

            Assert.IsFalse(setting.IsHdrSupported);
            Assert.IsTrue(setting.IsWcgSupported);
            Assert.AreEqual(false, setting.LastKnownHdrSupported);
            Assert.AreEqual(true, setting.LastKnownWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ReconcileCapabilities_UnavailableLiveObservation_PreservesLastKnownEvidence()
        {
            var setting = Setting(11, hdrSupported: true, wcgSupported: false);

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(
                setting,
                Live(11, hdrSupported: false, wcgSupported: true, available: false));

            Assert.AreEqual(true, setting.LastKnownHdrSupported);
            Assert.AreEqual(false, setting.LastKnownWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCapabilities_KnownLiveUnsupported_OverridesSupportedHistory()
        {
            var setting = Setting(11, hdrSupported: true, wcgSupported: true);
            var live = Live(11, hdrSupported: false, wcgSupported: false);

            var (hasLiveObservation, hdrSupported, wcgSupported) = DisplaySettingControl.ResolveAdvancedColorCapabilitiesForEditor(setting, [live]);

            Assert.IsTrue(hasLiveObservation);
            Assert.AreEqual(false, hdrSupported);
            Assert.AreEqual(false, wcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveIntent_InactiveKnownCapabilities_AllowsStoredIntentToBeEdited()
        {
            var original = Setting(11, hdrSupported: true, wcgSupported: true);

            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(
                original,
                hasLiveObservation: false,
                hdrSupported: true,
                wcgSupported: true,
                requestedHdrEnabled: false,
                requestedWcgEnabled: true);

            Assert.IsFalse(isHdrEnabled);
            Assert.IsTrue(isWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveIntent_InactiveUnknownCapabilities_PreservesStoredIntent()
        {
            var original = Setting(11, hdrSupported: null, wcgSupported: null, hdrEnabled: true);

            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(
                original,
                hasLiveObservation: false,
                hdrSupported: null,
                wcgSupported: null,
                requestedHdrEnabled: false,
                requestedWcgEnabled: false);

            Assert.IsTrue(isHdrEnabled);
            Assert.IsFalse(isWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveIntent_LiveUnsupportedCapabilities_ClearUnsupportedRequestedIntent()
        {
            var original = Setting(11, hdrSupported: true, wcgSupported: true, hdrEnabled: true, wcgEnabled: true);

            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(
                original,
                hasLiveObservation: true,
                hdrSupported: false,
                wcgSupported: false,
                requestedHdrEnabled: true,
                requestedWcgEnabled: true);

            Assert.IsFalse(isHdrEnabled);
            Assert.IsFalse(isWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveIntent_Pre24H2KnownWcgCapability_PreservesLegacyAcmEditingSemantics()
        {
            var original = Setting(11, hdrSupported: false, wcgSupported: true);

            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(
                original,
                hasLiveObservation: false,
                hdrSupported: false,
                wcgSupported: true,
                requestedHdrEnabled: false,
                requestedWcgEnabled: true);

            Assert.IsFalse(isHdrEnabled);
            Assert.IsTrue(isWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveIntent_Pre24H2WcgProvider_AllowsAcmWithoutInventingHdr()
        {
            var original = Setting(11, hdrSupported: false, wcgSupported: true);

            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(
                original,
                hasLiveObservation: true,
                hdrSupported: false,
                wcgSupported: true,
                requestedHdrEnabled: true,
                requestedWcgEnabled: true);

            Assert.IsFalse(isHdrEnabled);
            Assert.IsTrue(isWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveIntent_Pre24H2HdrProvider_DoesNotTreatHdrCapabilityAsAcm()
        {
            var original = Setting(11, hdrSupported: true, wcgSupported: false);

            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(
                original,
                hasLiveObservation: true,
                hdrSupported: true,
                wcgSupported: false,
                requestedHdrEnabled: false,
                requestedWcgEnabled: true);

            Assert.IsFalse(isHdrEnabled);
            Assert.IsFalse(isWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCapabilities_CloneMembers_RetainIndependentLiveEvidence()
        {
            var first = Setting(11, hdrSupported: false, wcgSupported: false);
            var second = Setting(22, hdrSupported: true, wcgSupported: false);
            var live = new List<DisplayConfigInfo>
            {
                Live(11, hdrSupported: true, wcgSupported: false),
                Live(22, hdrSupported: false, wcgSupported: true)
            };

            var firstResult = DisplaySettingControl.ResolveAdvancedColorCapabilitiesForEditor(first, live);
            var secondResult = DisplaySettingControl.ResolveAdvancedColorCapabilitiesForEditor(second, live);

            Assert.AreEqual(true, firstResult.HdrSupported);
            Assert.AreEqual(false, firstResult.WcgSupported);
            Assert.AreEqual(false, secondResult.HdrSupported);
            Assert.AreEqual(true, secondResult.WcgSupported);
        }
    }
}
