using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.UI.Controls;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class HdrColorProfileMemoryTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void HdrRoundTrip_RestoresPreHdrSdrProfileWhenFilteringTemporarilyHidesIt()
        {
            var enteredHdr = DisplaySettingControl.ResolveHdrColorProfileEditorState(
                hdrOn: true,
                wasHdrOn: false,
                currentProfile: "sdr-only.icc",
                rememberedSdrProfile: null,
                hdrSelectionChanged: false,
                sdrCompatibleProfiles: []);

            Assert.IsNull(enteredHdr.SelectedProfile);
            Assert.AreEqual("sdr-only.icc", enteredHdr.RememberedSdrProfile);

            var leftHdr = DisplaySettingControl.ResolveHdrColorProfileEditorState(
                hdrOn: false,
                wasHdrOn: true,
                currentProfile: null,
                rememberedSdrProfile: enteredHdr.RememberedSdrProfile,
                hdrSelectionChanged: false,
                sdrCompatibleProfiles: ["sdr-only.icc"]);

            Assert.AreEqual("sdr-only.icc", leftHdr.SelectedProfile);
            Assert.AreEqual("sdr-only.icc", leftHdr.RememberedSdrProfile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrRoundTrip_DeliberateCompatibleSelectionWinsOverRememberedSdrProfile()
        {
            var state = DisplaySettingControl.ResolveHdrColorProfileEditorState(
                hdrOn: false,
                wasHdrOn: true,
                currentProfile: "dual-mode.icc",
                rememberedSdrProfile: "sdr-only.icc",
                hdrSelectionChanged: true,
                sdrCompatibleProfiles: ["sdr-only.icc", "dual-mode.icc"]);

            Assert.AreEqual("dual-mode.icc", state.SelectedProfile);
            Assert.AreEqual("dual-mode.icc", state.RememberedSdrProfile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrRoundTrip_IncompatibleHdrSelectionDoesNotEraseRememberedSdrProfile()
        {
            var state = DisplaySettingControl.ResolveHdrColorProfileEditorState(
                hdrOn: false,
                wasHdrOn: true,
                currentProfile: "hdr-only.icc",
                rememberedSdrProfile: "sdr-only.icc",
                hdrSelectionChanged: true,
                sdrCompatibleProfiles: ["sdr-only.icc"]);

            Assert.AreEqual("sdr-only.icc", state.SelectedProfile);
            Assert.AreEqual("sdr-only.icc", state.RememberedSdrProfile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrRoundTrip_DeliberateNotAppliedSelectionIsPreserved()
        {
            var state = DisplaySettingControl.ResolveHdrColorProfileEditorState(
                hdrOn: false,
                wasHdrOn: true,
                currentProfile: null,
                rememberedSdrProfile: "sdr-only.icc",
                hdrSelectionChanged: true,
                sdrCompatibleProfiles: ["sdr-only.icc"]);

            Assert.IsNull(state.SelectedProfile);
            Assert.IsNull(state.RememberedSdrProfile);
        }
    }
}
