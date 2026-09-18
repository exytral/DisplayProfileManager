using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.UI.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayProfileManager.Tests.Tests
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
            var steps = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Slideshow, WallpaperMode.Solid).Steps;
            int supersede = ((List<WallpaperHelper.WallpaperTransitionStep>)new List<WallpaperHelper.WallpaperTransitionStep>(steps)).IndexOf(WallpaperHelper.WallpaperTransitionStep.SupersedeSlideshowOwnership);
            int disable = ((List<WallpaperHelper.WallpaperTransitionStep>)new List<WallpaperHelper.WallpaperTransitionStep>(steps)).IndexOf(WallpaperHelper.WallpaperTransitionStep.DisableDesktopBackground);

            Assert.IsTrue(supersede >= 0);
            Assert.IsTrue(disable > supersede);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SupersedeSlideshow_VisualReplacementButSlideshowStillActive_IsFailure()
        {
            var states = new Queue<bool?>(new bool?[] { true, true });
            var outcome = WallpaperHelper.SupersedeSlideshowOwnership(() => states.Dequeue(), () => true);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SupersedeSlideshow_ReplacedAndOwnershipCleared_IsSuccess()
        {
            var states = new Queue<bool?>(new bool?[] { true, false });
            var outcome = WallpaperHelper.SupersedeSlideshowOwnership(() => states.Dequeue(), () => true);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AuthoritativeSlideshowStatus_ReadFailure_IsUnavailable()
        {
            Assert.IsFalse(WallpaperHelper.TryGetSlideshowActive(
                () => throw new InvalidOperationException("status failed"),
                out bool active));
            Assert.IsFalse(active);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowToSolid_InitialApplyStatusReadFailure_AbortsSourceDetection()
        {
            bool result = WallpaperHelper.TryDetectCurrentModeForApply(backgroundType: null, liveWallpaper: @"C:\Pictures\before.jpg", readStatus: () => throw new InvalidOperationException("status failed"), out WallpaperMode mode);

            Assert.IsFalse(result);
            Assert.AreEqual(WallpaperMode.Unknown, mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SupersedeSlideshow_InitialStatusReadFailure_IsFailure()
        {
            bool replacementCalled = false;
            var outcome = WallpaperHelper.SupersedeSlideshowOwnership(
                () => null,
                () => { replacementCalled = true; return true; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.IsFalse(replacementCalled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SupersedeSlideshow_PostReplacementStatusReadFailure_IsFailure()
        {
            var states = new Queue<bool?>(new bool?[] { true, null });
            var outcome = WallpaperHelper.SupersedeSlideshowOwnership(() => states.Dequeue(), () => true);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void VerifySolidOwnership_RejectsRegistrySolidWhileSlideshowIsStillActive()
        {
            var outcome = WallpaperHelper.VerifyDestinationOwnership(WallpaperMode.Solid, () => WallpaperMode.Solid, () => true);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void VerifySolidOwnership_StatusReadFailure_IsFailure()
        {
            var outcome = WallpaperHelper.VerifyDestinationOwnership(WallpaperMode.Solid, () => WallpaperMode.Solid, () => null);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightAcquisition_RelinquishesPriorOwnerBeforeProviderSelection()
        {
            var calls = new List<string>();
            var outcome = WallpaperHelper.ActivateSpotlightDestination(
                () => { calls.Add("enable-desktop"); return true; },
                () => { calls.Add("relinquish-owner"); return true; },
                () => { calls.Add("enable-provider"); return true; },
                () => { calls.Add("select-mode"); return true; },
                () => { calls.Add("refresh"); return true; },
                () => { calls.Add("verify"); return true; },
                () => { calls.Add("select-repaint"); return SpotlightPath; },
                _ => { calls.Add("paint"); return true; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { "enable-desktop", "relinquish-owner", "enable-provider", "select-mode", "refresh", "verify" }, calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightAcquisition_PriorOwnerCannotBeRelinquished_FailsClosed()
        {
            bool providerTouched = false;
            var outcome = WallpaperHelper.ActivateSpotlightDestination(
                () => true,
                () => false,
                () => { providerTouched = true; return true; },
                () => true,
                () => true,
                () => true,
                () => SpotlightPath,
                _ => true);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.IsFalse(providerTouched);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightRelinquish_StatusReadFailure_FailsBeforeMutation()
        {
            bool supersedeCalled = false;
            bool deleteCalled = false;
            bool result = WallpaperHelper.RelinquishWallpaperForSpotlight(
                () => null,
                () => { supersedeCalled = true; return WallpaperHelper.WallpaperApplyOutcome.Success; },
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
            var outcome = WallpaperHelper.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => true,
                () => true,
                () => true,
                () =>
                {
                    attempts++;
                    bool? slideshowActive = WallpaperHelper.TryGetSlideshowActive(
                        () => throw new InvalidOperationException("status failed"),
                        out bool active)
                        ? active
                        : (bool?)null;
                    return WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, slideshowActive, renderedPaths);
                },
                () => SpotlightPath,
                _ => true);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(2, attempts);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightRollback_RestoresPriorLegacyWallpaperValueWhenItExisted()
        {
            string restored = null;
            bool deleted = false;
            bool result = WallpaperHelper.RestoreLegacyWallpaperRegistration(
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
            bool result = WallpaperHelper.RestoreLegacyWallpaperRegistration(
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

            bool result = WallpaperHelper.RecoverPriorOwnerAfterFailure(() => RestorePriorPictureOwner(
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

            bool result = WallpaperHelper.RecoverPriorOwnerAfterFailure(() => RestorePriorPictureOwner(
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

            bool result = WallpaperHelper.RecoverPriorOwnerAfterFailure(() => RestorePriorPictureOwner(
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

            bool result = WallpaperHelper.RecoverPriorOwnerAfterFailure(() => RestorePriorPictureOwner(
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
            return WallpaperHelper.RestorePriorOwner(
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
                logIncomplete: incompleteLogs.Add);
        }
    }
    [TestClass]
    public class ProfileApplyInteractionTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public async Task WallpaperApplyHelper_RunsBlockingWallpaperWorkOffCallerThread()
        {
            int callerThread = Environment.CurrentManagedThreadId;
            int workerThread = callerThread;
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim(false);

            Task<bool> task = ProfileManager.ApplyWallpaperOffUiAsync(new WallpaperSettings(), _ =>
            {
                workerThread = Environment.CurrentManagedThreadId;
                started.TrySetResult(true);
                release.Wait();
                return true;
            });

            await started.Task;
            Assert.AreNotEqual(callerThread, workerThread);
            Assert.IsFalse(task.IsCompleted);
            release.Set();
            Assert.IsTrue(await task);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task BusyStateWrapper_RestoresInteractionAfterNormalCompletion()
        {
            var states = new List<bool>();
            await MainWindow.RunWithApplyBusyStateAsync(states.Add, () => Task.CompletedTask);
            CollectionAssert.AreEqual(new[] { true, false }, states);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task BusyStateWrapper_RestoresInteractionAfterException()
        {
            var states = new List<bool>();
            bool threw = false;
            try
            {
                await MainWindow.RunWithApplyBusyStateAsync(states.Add, () => throw new InvalidOperationException("boom"));
            }
            catch (InvalidOperationException ex) when (ex.Message == "boom")
            {
                threw = true;
            }

            Assert.IsTrue(threw);
            CollectionAssert.AreEqual(new[] { true, false }, states);
        }
    }
}
