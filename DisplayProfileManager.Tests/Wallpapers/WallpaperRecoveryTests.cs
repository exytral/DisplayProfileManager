using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Interop;
using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class WallpaperRecoveryTests
    {
        private static readonly string SpotlightPath = System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
            "SystemApps",
            "MicrosoftWindows.Client.CBS_cw5n1h2txyewy",
            "DesktopSpotlight",
            "Assets",
            "Images",
            "asset.jpg");

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowToSolidPlan_SupersedesSlideshowBeforeDisablingDesktop()
        {
            var steps = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Slideshow, WallpaperMode.Solid).Steps;
            int supersede = ((List<WallpaperTransitionStep>)[.. steps]).IndexOf(WallpaperTransitionStep.SupersedeSlideshowOwnership);
            int disable = ((List<WallpaperTransitionStep>)[.. steps]).IndexOf(WallpaperTransitionStep.DisableDesktopBackground);

            Assert.IsTrue(supersede >= 0);
            Assert.IsTrue(disable > supersede);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SupersedeSlideshow_ReplacedAndOwnershipCleared_IsSuccess()
        {
            var states = new Queue<bool?>([true, false]);
            var outcome = WallpaperApply.SupersedeSlideshowOwnership(() => states.Dequeue(), () => true);
            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AuthoritativeSlideshowStatus_ReadFailure_IsUnavailable()
        {
            Assert.IsFalse(WindowsWallpaperPlatform.TryGetSlideshowActive(() => throw new InvalidOperationException("status failed"), out bool active));
            Assert.IsFalse(active);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowToSolid_InitialApplyStatusReadFailure_AbortsSourceDetection()
        {
            bool result = WindowsWallpaperPlatform.TryDetectCurrentModeForApply(backgroundType: null, liveWallpaper: @"C:\Pictures\before.jpg", readStatus: () => throw new InvalidOperationException("status failed"), out WallpaperMode mode);

            Assert.IsFalse(result);
            Assert.AreEqual(WallpaperMode.Unknown, mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SupersedeSlideshow_InitialStatusReadFailure_IsFailure()
        {
            bool replacementCalled = false;
            var outcome = WallpaperApply.SupersedeSlideshowOwnership(
                () => null,
                () => { replacementCalled = true; return true; });

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.IsFalse(replacementCalled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SupersedeSlideshow_PostReplacementStatusReadFailure_IsFailure()
        {
            var states = new Queue<bool?>([true, null]);
            var outcome = WallpaperApply.SupersedeSlideshowOwnership(() => states.Dequeue(), () => true);
            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void VerifySolidOwnership_RejectsRegistrySolidWhileSlideshowIsStillActive()
        {
            var outcome = WallpaperApply.VerifyDestinationOwnership(WallpaperMode.Solid, () => WallpaperMode.Solid, () => true);
            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void VerifySolidOwnership_StatusReadFailure_IsFailure()
        {
            var outcome = WallpaperApply.VerifyDestinationOwnership(WallpaperMode.Solid, () => WallpaperMode.Solid, () => null);
            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightAcquisition_RelinquishesPriorOwnerBeforeProviderSelection()
        {
            var calls = new List<string>();
            var outcome = WallpaperApply.ActivateSpotlightDestination(
                () => { calls.Add("enable-desktop"); return true; },
                () => { calls.Add("relinquish-owner"); return true; },
                () => { calls.Add("enable-provider"); return true; },
                () => { calls.Add("select-mode"); return true; },
                () => { calls.Add("refresh"); return true; },
                () => { calls.Add("verify"); return true; },
                () => { calls.Add("select-repaint"); return SpotlightPath; },
                _ => { calls.Add("paint"); return true; });

            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { "enable-desktop", "relinquish-owner", "enable-provider", "select-mode", "refresh", "verify" }, calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightAcquisition_PriorOwnerCannotBeRelinquished_FailsClosed()
        {
            bool providerTouched = false;
            var outcome = WallpaperApply.ActivateSpotlightDestination(
                () => true,
                () => false,
                () => { providerTouched = true; return true; },
                () => true,
                () => true,
                () => true,
                () => SpotlightPath,
                _ => true);

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.IsFalse(providerTouched);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightRelinquish_StatusReadFailure_FailsBeforeMutation()
        {
            bool supersedeCalled = false;
            bool deleteCalled = false;
            bool result = WallpaperApply.RelinquishWallpaperForSpotlight(
                () => null,
                () => { supersedeCalled = true; return WallpaperApplyOutcome.Success; },
                () => { deleteCalled = true; return true; });

            Assert.IsFalse(result);
            Assert.IsFalse(supersedeCalled);
            Assert.IsFalse(deleteCalled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightVerification_StatusReadFailure_FailsAfterSingleRepaintRecheck()
        {
            int attempts = 0;
            var renderedPaths = new[] { SpotlightPath, SpotlightPath.Replace("asset.jpg", "asset2.jpg") };
            var outcome = WallpaperApply.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => true,
                () => true,
                () => true,
                () =>
                {
                    attempts++;
                    bool? slideshowActive = WindowsWallpaperPlatform.TryGetSlideshowActive(
                        () => throw new InvalidOperationException("status failed"),
                        out bool active)
                        ? active
                        : null;
                    return WallpaperApply.IsSpotlightDestinationEstablished(3, 1, slideshowActive, renderedPaths);
                },
                () => SpotlightPath,
                _ => true);

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(2, attempts);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightRollback_RestoresPriorLegacyWallpaperValueWhenItExisted()
        {
            string restored = null;
            bool deleted = false;
            bool result = WallpaperApply.RestoreLegacyWallpaperRegistration(
                true,
                @"C:\Pictures\before.jpg",
                value => { restored = value; return true; },
                () => { deleted = true; return true; });

            Assert.IsTrue(result);
            Assert.AreEqual(@"C:\Pictures\before.jpg", restored);
            Assert.IsFalse(deleted);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightRollback_RestoresPriorAbsenceByDeletingLegacyWallpaperValue()
        {
            bool setterCalled = false;
            bool deleted = false;
            bool result = WallpaperApply.RestoreLegacyWallpaperRegistration(
                false,
                null,
                _ => { setterCalled = true; return true; },
                () => { deleted = true; return true; });

            Assert.IsTrue(result);
            Assert.IsFalse(setterCalled);
            Assert.IsTrue(deleted);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FailedSpotlightOutcome_RestoreCoordinator_RestoresPriorPresentValue()
        {
            string restored = null;
            bool deleted = false;
            var completeLogs = new List<string>();
            var incompleteLogs = new List<string>();

            bool result = WallpaperApply.RecoverPriorOwnerAfterFailure(() => RestorePriorPictureOwner(
                priorExists: true,
                priorValue: @"C:\Pictures\before.jpg",
                setValue: value => { restored = value; return true; },
                deleteValue: () => { deleted = true; return true; },
                restoreSourceOwnership: () => true,
                completeLogs,
                incompleteLogs));

            Assert.IsTrue(result);
            Assert.AreEqual(@"C:\Pictures\before.jpg", restored);
            Assert.IsFalse(deleted);
            Assert.AreEqual(1, completeLogs.Count);
            Assert.AreEqual(0, incompleteLogs.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FailedSpotlightOutcome_RestoreCoordinator_RestoresPriorAbsence()
        {
            bool setterCalled = false;
            bool deleted = false;
            var completeLogs = new List<string>();
            var incompleteLogs = new List<string>();

            bool result = WallpaperApply.RecoverPriorOwnerAfterFailure(() => RestorePriorPictureOwner(
                priorExists: false,
                priorValue: null,
                setValue: _ => { setterCalled = true; return true; },
                deleteValue: () => { deleted = true; return true; },
                restoreSourceOwnership: () => true,
                completeLogs,
                incompleteLogs));

            Assert.IsTrue(result);
            Assert.IsFalse(setterCalled);
            Assert.IsTrue(deleted);
            Assert.AreEqual(1, completeLogs.Count);
            Assert.AreEqual(0, incompleteLogs.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RollbackLegacyRestorationFailure_IsIncompleteAndNeverLoggedComplete()
        {
            var completeLogs = new List<string>();
            var incompleteLogs = new List<string>();

            bool result = WallpaperApply.RecoverPriorOwnerAfterFailure(() => RestorePriorPictureOwner(
                priorExists: true,
                priorValue: @"C:\Pictures\before.jpg",
                setValue: _ => false,
                deleteValue: () => true,
                restoreSourceOwnership: () => true,
                completeLogs,
                incompleteLogs));

            Assert.IsFalse(result);
            Assert.AreEqual(0, completeLogs.Count);
            Assert.AreEqual(1, incompleteLogs.Count);
            StringAssert.Contains(incompleteLogs[0], "Incomplete restore");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RollbackOwnershipStepFailure_IsIncompleteAndNeverLoggedComplete()
        {
            var completeLogs = new List<string>();
            var incompleteLogs = new List<string>();

            bool result = WallpaperApply.RecoverPriorOwnerAfterFailure(() => RestorePriorPictureOwner(
                priorExists: true,
                priorValue: @"C:\Pictures\before.jpg",
                setValue: _ => true,
                deleteValue: () => true,
                restoreSourceOwnership: () => false,
                completeLogs,
                incompleteLogs));

            Assert.IsFalse(result);
            Assert.AreEqual(0, completeLogs.Count);
            Assert.AreEqual(1, incompleteLogs.Count);
        }

        private static bool RestorePriorPictureOwner(
            bool priorExists,
            string priorValue,
            Func<string, bool> setValue,
            Func<bool> deleteValue,
            Func<bool> restoreSourceOwnership,
            List<string> completeLogs,
            List<string> incompleteLogs)
        {
            return WallpaperApply.RestorePriorOwner(
                WallpaperMode.Picture,
                legacyWallpaperCaptured: true,
                priorLegacyWallpaperExists: priorExists,
                priorLegacyWallpaper: priorValue,
                restoreSharedState: () => true,
                restoreSpotlightOwner: () => true,
                deactivateSpotlight: () => true,
                setLegacyWallpaper: setValue,
                deleteLegacyWallpaper: deleteValue,
                restoreSourceOwnership: restoreSourceOwnership,
                establishSourceMode: () => true,
                notifySettings: () => { },
                logComplete: completeLogs.Add,
                logIncomplete: incompleteLogs.Add,
                restorePictureAssignments: () => true);
        }
    }
}
