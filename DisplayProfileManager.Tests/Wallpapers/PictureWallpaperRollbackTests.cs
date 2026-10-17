using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;
using static DisplayProfileManager.Wallpapers.Interop.DesktopWallpaperNative;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class PictureWallpaperRollbackTests
    {
        private static readonly string Provider = System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
            "SystemApps",
            "MicrosoftWindows.Client.CBS_cw5n1h2txyewy",
            "DesktopSpotlight",
            "Assets",
            "Images",
            "rollback-provider.jpg");

        [TestMethod]
        [TestCategory("Unit")]
        public void FinalVerificationFailure_RestoresExactPerMonitorPictureState()
        {
            var rendered = new Dictionary<string, string>
            {
                ["MON1"] = @"C:\Pictures\A.jpg",
                ["MON2"] = @"C:\Pictures\B.jpg",
            };
            PictureRollbackSnapshot rollback = null;
            var verifies = new Queue<bool>([false, false]);

            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                ["MON1", "MON2"],
                monitorId => rendered[monitorId],
                captured => rollback = captured,
                () => WallpaperApply.ActivateSpotlightDestination(
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => verifies.Dequeue(),
                    () => Provider,
                    path =>
                    {
                        rendered["MON1"] = path;
                        rendered["MON2"] = path;
                        return true;
                    }));

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            bool completeLogged = false;
            bool incompleteLogged = false;
            bool recovered = WallpaperApply.RecoverPriorOwnerAfterFailure(() => WallpaperApply.RestorePriorOwner(
                WallpaperMode.Picture,
                legacyWallpaperCaptured: true,
                priorLegacyWallpaperExists: true,
                priorLegacyWallpaper: @"C:\Pictures\legacy.jpg",
                restoreSharedState: () => true,
                restoreSpotlightOwner: () => true,
                deactivateSpotlight: () => true,
                setLegacyWallpaper: _ => true,
                deleteLegacyWallpaper: () => true,
                restoreSourceOwnership: () => true,
                establishSourceMode: () => true,
                notifySettings: () => { },
                logComplete: _ => completeLogged = true,
                logIncomplete: _ => incompleteLogged = true,
                restorePictureAssignments: () => WallpaperApply.RestorePictureRollbackSnapshot(rollback, (monitorId, path) => rendered[monitorId] = path)));

            Assert.IsTrue(recovered);
            Assert.AreEqual(@"C:\Pictures\A.jpg", rendered["MON1"]);
            Assert.AreEqual(@"C:\Pictures\B.jpg", rendered["MON2"]);
            Assert.IsTrue(completeLogged);
            Assert.IsFalse(incompleteLogged);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProviderReassertionFailure_RestoresExactPerMonitorPictureState()
        {
            var rendered = new Dictionary<string, string>
            {
                ["MON1"] = @"C:\Pictures\A.jpg",
                ["MON2"] = @"C:\Pictures\B.jpg",
            };
            PictureRollbackSnapshot rollback = null;
            var providerResults = new Queue<bool>([true, false]);

            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                ["MON1", "MON2"],
                monitorId => rendered[monitorId],
                captured => rollback = captured,
                () => WallpaperApply.ActivateSpotlightDestination(
                    () => true,
                    () => true,
                    () => providerResults.Dequeue(),
                    () => true,
                    () => true,
                    () => false,
                    () => Provider,
                    path =>
                    {
                        rendered["MON1"] = path;
                        rendered["MON2"] = path;
                        return true;
                    }));

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            bool recovered = WallpaperApply.RecoverPriorOwnerAfterFailure(() => WallpaperApply.RestorePriorOwner(
                WallpaperMode.Picture,
                legacyWallpaperCaptured: true,
                priorLegacyWallpaperExists: true,
                priorLegacyWallpaper: @"C:\Pictures\legacy.jpg",
                restoreSharedState: () => true,
                restoreSpotlightOwner: () => true,
                deactivateSpotlight: () => true,
                setLegacyWallpaper: _ => true,
                deleteLegacyWallpaper: () => true,
                restoreSourceOwnership: () => true,
                establishSourceMode: () => true,
                notifySettings: () => { },
                logComplete: _ => { },
                logIncomplete: _ => { },
                restorePictureAssignments: () => WallpaperApply.RestorePictureRollbackSnapshot(rollback, (monitorId, path) => rendered[monitorId] = path)));

            Assert.IsTrue(recovered);
            Assert.AreEqual(@"C:\Pictures\A.jpg", rendered["MON1"]);
            Assert.AreEqual(@"C:\Pictures\B.jpg", rendered["MON2"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PerMonitorRestoreFailure_MarksAggregateIncompleteAndNeverLogsComplete()
        {
            var original = new Dictionary<string, string>
            {
                ["MON1"] = @"C:\Pictures\A.jpg",
                ["MON2"] = @"C:\Pictures\B.jpg",
            };
            Assert.IsTrue(WallpaperApply.TryCapturePictureRollbackSnapshot(["MON1", "MON2"], monitorId => original[monitorId], out PictureRollbackSnapshot rollback));

            var rendered = new Dictionary<string, string>
            {
                ["MON1"] = Provider,
                ["MON2"] = Provider,
            };
            bool completeLogged = false;
            bool incompleteLogged = false;
            bool recovered = WallpaperApply.RestorePriorOwner(
                WallpaperMode.Picture,
                legacyWallpaperCaptured: true,
                priorLegacyWallpaperExists: true,
                priorLegacyWallpaper: @"C:\Pictures\legacy.jpg",
                restoreSharedState: () => true,
                restoreSpotlightOwner: () => true,
                deactivateSpotlight: () => true,
                setLegacyWallpaper: _ => true,
                deleteLegacyWallpaper: () => true,
                restoreSourceOwnership: () => true,
                establishSourceMode: () => true,
                notifySettings: () => { },
                logComplete: _ => completeLogged = true,
                logIncomplete: _ => incompleteLogged = true,
                restorePictureAssignments: () => WallpaperApply.RestorePictureRollbackSnapshot(
                    rollback,
                    (monitorId, path) =>
                    {
                        if (monitorId == "MON2") throw new InvalidOperationException("setter failed");
                        rendered[monitorId] = path;
                    }));

            Assert.IsFalse(recovered);
            Assert.AreEqual(@"C:\Pictures\A.jpg", rendered["MON1"]);
            Assert.AreEqual(Provider, rendered["MON2"]);
            Assert.IsFalse(completeLogged);
            Assert.IsTrue(incompleteLogged);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IncompletePictureSnapshot_StopsBeforeSpotlightActivationOrGlobalRepaint()
        {
            bool activated = false;
            bool remembered = false;
            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                ["MON1", "MON2"],
                monitorId => monitorId == "MON1" ? @"C:\Pictures\A.jpg" : throw new InvalidOperationException("read failed"),
                _ => remembered = true,
                () =>
                {
                    activated = true;
                    return WallpaperApplyOutcome.Success;
                });

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.IsFalse(remembered);
            Assert.IsFalse(activated);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AlreadySpotlightDestination_DoesNotRequirePictureRollbackSnapshot()
        {
            int pictureReads = 0;
            bool remembered = false;
            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Spotlight,
                ["MON1", "MON2"],
                _ => { pictureReads++; throw new InvalidOperationException("should not read Picture rollback state"); },
                _ => remembered = true,
                () => WallpaperApplyOutcome.Success);

            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.IsFalse(remembered);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void PictureSharedRollbackState_CapturesExactColorAndPosition()
        {
            bool captured = WallpaperApply.TryCapturePictureSharedRollbackState(
                () => 0xFF123456,
                () => DesktopWallpaperPosition.Fit,
                out uint color,
                out DesktopWallpaperPosition position);

            Assert.IsTrue(captured);
            Assert.AreEqual(0xFF123456u, color);
            Assert.AreEqual(DesktopWallpaperPosition.Fit, position);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureSharedRollbackState_PositionReadFailureFailsClosed()
        {
            bool captured = WallpaperApply.TryCapturePictureSharedRollbackState(
                () => 0xFF123456,
                () => throw new InvalidOperationException("position read failed"),
                out _,
                out _);

            Assert.IsFalse(captured);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureSourceRecovery_MultipleMonitorsPreservesIdentityOrderAndPaths()
        {
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MON-A"] = @"C:\Pictures\A.jpg",
                ["MON-B"] = @"C:\Pictures\B.jpg",
                ["MON-C"] = @"C:\Pictures\C.jpg",
            };
            PictureRollbackSnapshot remembered = null;

            bool prepared = WallpaperApply.PreparePictureSourceRecovery(
                WallpaperMode.Picture,
                ["MON-C", "MON-A", "MON-B"],
                monitorId => current[monitorId],
                snapshot => remembered = snapshot);

            Assert.IsTrue(prepared);
            Assert.IsNotNull(remembered);
            CollectionAssert.AreEqual(
                new[] { "MON-C", "MON-A", "MON-B" },
                remembered.Assignments.ConvertAll(assignment => assignment.MonitorId));
            CollectionAssert.AreEqual(
                new[] { @"C:\Pictures\C.jpg", @"C:\Pictures\A.jpg", @"C:\Pictures\B.jpg" },
                remembered.Assignments.ConvertAll(assignment => assignment.Path));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureSourceRecovery_UnavailableSnapshotFailsBeforeRetention()
        {
            bool remembered = false;

            bool prepared = WallpaperApply.PreparePictureSourceRecovery(
                WallpaperMode.Picture,
                ["MON1", "MON2"],
                monitorId => monitorId == "MON1"
                    ? @"C:\Pictures\A.jpg"
                    : throw new InvalidOperationException("provider read failed"),
                _ => remembered = true);

            Assert.IsFalse(prepared);
            Assert.IsFalse(remembered);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureSourceRecovery_DuplicateNormalizedMonitorIdentityFailsClosed()
        {
            bool remembered = false;

            bool prepared = WallpaperApply.PreparePictureSourceRecovery(
                WallpaperMode.Picture,
                [" MON1 ", "mon1"],
                _ => @"C:\Pictures\A.jpg",
                _ => remembered = true);

            Assert.IsFalse(prepared);
            Assert.IsFalse(remembered);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NonPictureSourceRecovery_DoesNotReadOrRetainPictureState()
        {
            int reads = 0;
            bool remembered = false;

            bool prepared = WallpaperApply.PreparePictureSourceRecovery(
                WallpaperMode.Slideshow,
                ["MON1"],
                _ => { reads++; throw new InvalidOperationException("should not read Picture state"); },
                _ => remembered = true);

            Assert.IsTrue(prepared);
            Assert.AreEqual(0, reads);
            Assert.IsFalse(remembered);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureToSlideshow_PostMutationFailure_RestoresPathsAndStyle()
        {
            var rendered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MON1"] = @"C:\Pictures\A.jpg",
                ["MON2"] = @"C:\Pictures\B.jpg",
            };
            uint backgroundColor = 0xFF112233;
            string position = "fit";
            WallpaperMode mode = WallpaperMode.Picture;
            PictureRollbackSnapshot rollback = null;
            Assert.IsTrue(WallpaperApply.PreparePictureSourceRecovery(
                WallpaperMode.Picture,
                ["MON2", "MON1"],
                monitorId => rendered[monitorId],
                snapshot => rollback = snapshot));

            var plan = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Slideshow);
            bool mutationStarted = false;
            var outcome = WallpaperApply.ExecuteTransitionSteps(plan, step =>
            {
                if (step != WallpaperTransitionStep.NotifySettings &&
                    step != WallpaperTransitionStep.VerifyDestinationMode &&
                    step != WallpaperTransitionStep.ActivateSpotlight)
                {
                    mutationStarted = true;
                }

                if (step == WallpaperTransitionStep.SetBackgroundColor)
                {
                    backgroundColor = 0xFF445566;
                }
                if (step == WallpaperTransitionStep.SetPosition)
                {
                    position = "span";
                }
                if (step == WallpaperTransitionStep.SetSlideshowSource)
                {
                    rendered["MON1"] = @"C:\Slideshow\frame.jpg";
                    rendered["MON2"] = @"C:\Slideshow\frame.jpg";
                    mode = WallpaperMode.Slideshow;
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            });

            bool completeLogged = false;
            bool incompleteLogged = false;
            bool recovered = WallpaperApply.RecoverPriorOwnerForOutcome(
                WallpaperMode.Picture,
                outcome,
                mutationStarted,
                () => WallpaperApply.RestorePriorOwner(
                    WallpaperMode.Picture,
                    legacyWallpaperCaptured: false,
                    priorLegacyWallpaperExists: false,
                    priorLegacyWallpaper: null,
                    restoreSharedState: () =>
                    {
                        backgroundColor = 0xFF112233;
                        position = "fit";
                        return true;
                    },
                    restoreSpotlightOwner: () => true,
                    deactivateSpotlight: () => true,
                    setLegacyWallpaper: _ => true,
                    deleteLegacyWallpaper: () => true,
                    restoreSourceOwnership: () => true,
                    establishSourceMode: () =>
                    {
                        mode = WallpaperMode.Picture;
                        return true;
                    },
                    notifySettings: () => { },
                    logComplete: _ => completeLogged = true,
                    logIncomplete: _ => incompleteLogged = true,
                    restorePictureAssignments: () => WallpaperApply.RestorePictureRollbackSnapshot(
                        rollback,
                        (monitorId, path) => rendered[monitorId] = path)));

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.IsTrue(recovered);
            Assert.AreEqual(@"C:\Pictures\A.jpg", rendered["MON1"]);
            Assert.AreEqual(@"C:\Pictures\B.jpg", rendered["MON2"]);
            Assert.AreEqual(0xFF112233u, backgroundColor);
            Assert.AreEqual("fit", position);
            Assert.AreEqual(WallpaperMode.Picture, mode);
            Assert.IsTrue(completeLogged);
            Assert.IsFalse(incompleteLogged);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureToPicture_SecondMonitorFailure_RestoresEveryChangedMonitor()
        {
            var destination = new WallpaperSettings { Mode = WallpaperMode.Picture };
            destination.PerMonitor["DISPLAY1"] = new MonitorWallpaper { Path = @"C:\New\A.jpg", MonitorId = "MON-A" };
            destination.PerMonitor["DISPLAY2"] = new MonitorWallpaper { Path = @"C:\New\B.jpg", MonitorId = "MON-B" };
            destination.PerMonitor["DISPLAY3"] = new MonitorWallpaper { Path = @"C:\New\C.jpg", MonitorId = "MON-C" };
            var monitorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DISPLAY3"] = "MON-C",
                ["DISPLAY1"] = "MON-A",
                ["DISPLAY2"] = "MON-B",
            };
            var rendered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MON-A"] = @"C:\Old\A.jpg",
                ["MON-B"] = @"C:\Old\B.jpg",
                ["MON-C"] = @"C:\Old\C.jpg",
            };

            var preflight = WallpaperApply.PreflightPictureAssignments(destination, monitorMap, _ => true);
            PictureRollbackSnapshot rollback = null;
            Assert.IsTrue(WallpaperApply.PreparePictureSourceRecovery(
                WallpaperMode.Picture,
                ["MON-C", "MON-A", "MON-B"],
                monitorId => rendered[monitorId],
                snapshot => rollback = snapshot));

            var outcome = WallpaperApply.ApplyPictureWallpapers(
                preflight,
                (monitorId, path) =>
                {
                    if (monitorId == "MON-B")
                        throw new InvalidOperationException("second monitor failed");
                    rendered[monitorId] = path;
                });

            bool recovered = WallpaperApply.RecoverPriorOwnerForOutcome(
                WallpaperMode.Picture,
                outcome,
                mutationStarted: true,
                () => WallpaperApply.RestorePriorOwner(
                    WallpaperMode.Picture,
                    legacyWallpaperCaptured: false,
                    priorLegacyWallpaperExists: false,
                    priorLegacyWallpaper: null,
                    restoreSharedState: () => true,
                    restoreSpotlightOwner: () => true,
                    deactivateSpotlight: () => true,
                    setLegacyWallpaper: _ => true,
                    deleteLegacyWallpaper: () => true,
                    restoreSourceOwnership: () => true,
                    establishSourceMode: () => true,
                    notifySettings: () => { },
                    logComplete: _ => { },
                    logIncomplete: _ => { },
                    restorePictureAssignments: () => WallpaperApply.RestorePictureRollbackSnapshot(
                        rollback,
                        (monitorId, path) => rendered[monitorId] = path)));

            Assert.AreEqual(WallpaperApplyOutcome.Partial, outcome);
            Assert.IsTrue(recovered);
            Assert.AreEqual(@"C:\Old\A.jpg", rendered["MON-A"]);
            Assert.AreEqual(@"C:\Old\B.jpg", rendered["MON-B"]);
            Assert.AreEqual(@"C:\Old\C.jpg", rendered["MON-C"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureRestore_MissingSnapshotIsIncompleteAndNeverLogsComplete()
        {
            bool completeLogged = false;
            bool incompleteLogged = false;

            bool recovered = WallpaperApply.RestorePriorOwner(
                WallpaperMode.Picture,
                legacyWallpaperCaptured: false,
                priorLegacyWallpaperExists: false,
                priorLegacyWallpaper: null,
                restoreSharedState: () => true,
                restoreSpotlightOwner: () => true,
                deactivateSpotlight: () => true,
                setLegacyWallpaper: _ => true,
                deleteLegacyWallpaper: () => true,
                restoreSourceOwnership: () => true,
                establishSourceMode: () => true,
                notifySettings: () => { },
                logComplete: _ => completeLogged = true,
                logIncomplete: _ => incompleteLogged = true,
                restorePictureAssignments: null);

            Assert.IsFalse(recovered);
            Assert.IsFalse(completeLogged);
            Assert.IsTrue(incompleteLogged);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureSuccess_DoesNotInvokeRecovery()
        {
            bool recoveryCalled = false;

            bool result = WallpaperApply.RecoverPriorOwnerForOutcome(
                WallpaperMode.Picture,
                WallpaperApplyOutcome.Success,
                mutationStarted: true,
                () => { recoveryCalled = true; return true; });

            Assert.IsTrue(result);
            Assert.IsFalse(recoveryCalled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NonPicturePartial_DoesNotChangeExistingRecoveryPolicy()
        {
            bool recoveryCalled = false;

            bool result = WallpaperApply.RecoverPriorOwnerForOutcome(
                WallpaperMode.Slideshow,
                WallpaperApplyOutcome.Partial,
                mutationStarted: true,
                () => { recoveryCalled = true; return true; });

            Assert.IsTrue(result);
            Assert.IsFalse(recoveryCalled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NonPictureFailure_StillRunsExistingRecovery()
        {
            int recoveries = 0;

            bool result = WallpaperApply.RecoverPriorOwnerForOutcome(
                WallpaperMode.Slideshow,
                WallpaperApplyOutcome.Failed,
                mutationStarted: true,
                () => { recoveries++; return true; });

            Assert.IsTrue(result);
            Assert.AreEqual(1, recoveries);
        }
    }
}
