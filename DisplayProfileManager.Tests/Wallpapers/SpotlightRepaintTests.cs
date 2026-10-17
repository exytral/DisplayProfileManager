using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class SpotlightRepaintTests
    {
        private static readonly string BuiltIn = System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
            "SystemApps",
            "MicrosoftWindows.Client.CBS_cw5n1h2txyewy",
            "DesktopSpotlight",
            "Assets",
            "Images",
            "image_0.jpg");
        private static readonly string Iris = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages",
            "MicrosoftWindows.Client.CBS_cw5n1h2txyewy",
            "LocalCache",
            "Microsoft",
            "IrisService",
            "iris.jpg");

        [TestMethod]
        [TestCategory("Unit")]
        public void RepaintSelection_PrefersCurrentProviderOwnedRendering()
        {
            string selected = SpotlightProvider.SelectSpotlightRepaintPath(
                [BuiltIn],
                [Iris],
                [],
                _ => true);

            Assert.AreEqual(BuiltIn, selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RepaintSelection_UsesBuiltInProviderFallbackWhenIrisIsUnavailable()
        {
            string selected = SpotlightProvider.SelectSpotlightRepaintPath(
                [@"C:\Pictures\user.jpg"],
                [Iris],
                [BuiltIn],
                path => path == BuiltIn);

            Assert.AreEqual(BuiltIn, selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RepaintSelection_NeverAcceptsArbitraryUserContent()
        {
            string selected = SpotlightProvider.SelectSpotlightRepaintPath(
                [@"C:\Pictures\user.jpg"],
                [@"C:\Temp\cached.jpg"],
                [],
                _ => true);

            Assert.IsNull(selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RepaintSelection_RejectsProviderMarkerSpoofOutsideOwnedRoots()
        {
            string spoof = @"C:\Pictures\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images\spoof.jpg";
            Assert.IsTrue(SpotlightProvider.IsSpotlightProviderPath(spoof));
            Assert.IsFalse(SpotlightProvider.IsVerifiedSpotlightProviderPath(spoof));
            Assert.IsNull(SpotlightProvider.SelectSpotlightRepaintPath([spoof], [], [], _ => true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Activation_ProviderAlreadyRepainted_SkipsDirectPaint()
        {
            var calls = new List<string>();
            var outcome = WallpaperApply.ActivateSpotlightDestination(
                () => { calls.Add("enable-desktop"); return true; },
                () => { calls.Add("relinquish"); return true; },
                () => { calls.Add("provider"); return true; },
                () => { calls.Add("mode"); return true; },
                () => { calls.Add("refresh"); return true; },
                () => { calls.Add("verify"); return true; },
                () => { calls.Add("select"); return BuiltIn; },
                _ => { calls.Add("paint"); return true; });

            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { "enable-desktop", "relinquish", "provider", "mode", "refresh", "verify" }, calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Activation_MissingShellRepaint_PaintsThenReassertsAndVerifies()
        {
            var calls = new List<string>();
            var verifies = new Queue<bool>([false, true]);
            var outcome = WallpaperApply.ActivateSpotlightDestination(
                () => { calls.Add("enable-desktop"); return true; },
                () => { calls.Add("relinquish"); return true; },
                () => { calls.Add("provider"); return true; },
                () => { calls.Add("mode"); return true; },
                () => { calls.Add("refresh"); return true; },
                () => { calls.Add("verify"); return verifies.Dequeue(); },
                () => { calls.Add("select"); return BuiltIn; },
                path => { calls.Add("paint"); return path == BuiltIn; });

            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { "enable-desktop", "relinquish", "provider", "mode", "refresh", "verify", "select", "paint", "provider", "mode", "refresh", "verify" }, calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Activation_NoProviderOwnedRepaintCandidate_FailsWithoutPolling()
        {
            int verifies = 0;
            int paints = 0;
            var outcome = WallpaperApply.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => true,
                () => true,
                () => true,
                () => { verifies++; return false; },
                () => null,
                _ => { paints++; return true; });

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(1, verifies);
            Assert.AreEqual(0, paints);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Activation_DirectPaintNeverSubstitutesForFinalAuthority()
        {
            int verifies = 0;
            var outcome = WallpaperApply.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => true,
                () => true,
                () => true,
                () => { verifies++; return false; },
                () => BuiltIn,
                _ => true);

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(2, verifies);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DestinationVerification_CanRequireProviderImageToStillExist()
        {
            Assert.IsTrue(WallpaperApply.IsSpotlightDestinationEstablished(3, 1, false, [BuiltIn], path => path == BuiltIn));
            Assert.IsFalse(WallpaperApply.IsSpotlightDestinationEstablished(3, 1, false, [BuiltIn], _ => false));
            Assert.IsFalse(WallpaperApply.IsSpotlightDestinationEstablished(3, 1, false, [@"C:\Pictures\user.jpg"], _ => true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RefreshSelection_UsesSpiBeforeSettingsBroadcast()
        {
            var calls = new List<string>();
            bool result = WallpaperApply.RefreshSpotlightSelection(
                () => { calls.Add("spi"); return true; },
                () => calls.Add("notify"));

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "spi", "notify" }, calls);
        }
    }
}
