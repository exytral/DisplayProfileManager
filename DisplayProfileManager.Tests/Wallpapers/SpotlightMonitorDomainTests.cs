using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Interop;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class SpotlightMonitorDomainTests
    {
        private static readonly string Provider = System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
            "SystemApps",
            "MicrosoftWindows.Client.CBS_cw5n1h2txyewy",
            "DesktopSpotlight",
            "Assets",
            "Images",
            "domain-provider.jpg");

        private static DesktopMonitorRectResult AttachedRect() => new(WindowsWallpaperPlatform.HResultSOk, 0, 0, 1920, 1080);

        [TestMethod]
        [TestCategory("Unit")]
        public void MonitorPathEnumerationFailure_StopsBeforeSnapshotOrRepaint()
        {
            int pictureReads = 0;
            bool activated = false;
            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                () => 2,
                index => index == 0 ? "MON1" : throw new InvalidOperationException("monitor path failed"),
                _ => AttachedRect(),
                _ => { pictureReads++; return @"C:\Pictures\A.jpg"; },
                _ => Assert.Fail("rollback must not be retained"),
                _ => { activated = true; return WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.IsFalse(activated);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UnknownMonitorRectFailure_StopsBeforeSnapshotOrRepaint()
        {
            int pictureReads = 0;
            bool activated = false;
            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                () => 2,
                index => index == 0 ? "MON1" : "MON2",
                monitorId => monitorId == "MON1"
                    ? AttachedRect()
                    : new DesktopMonitorRectResult(unchecked((int)0x80070057), 0, 0, 0, 0),
                _ => { pictureReads++; return @"C:\Pictures\A.jpg"; },
                _ => Assert.Fail("rollback must not be retained"),
                _ => { activated = true; return WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.IsFalse(activated);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DocumentedDetachedMonitor_IsOmittedButUnknownFailureIsNot()
        {
            IReadOnlyList<string> activatedDomain = null;
            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Spotlight,
                () => 2,
                index => index == 0 ? "MON1" : "DETACHED",
                monitorId => monitorId == "MON1"
                    ? AttachedRect()
                    : new DesktopMonitorRectResult(WindowsWallpaperPlatform.HResultSFalse, 0, 0, 0, 0),
                _ => throw new InvalidOperationException("Spotlight source must not snapshot Picture state"),
                _ => Assert.Fail("Spotlight source must not retain Picture rollback"),
                monitorIds =>
                {
                    activatedDomain = monitorIds;
                    return WallpaperApplyOutcome.Success;
                });

            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { "MON1" }, new List<string>(activatedDomain));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureRepaint_UsesExactStrictDomainAndLaterFailureRestoresBoth()
        {
            var rendered = new Dictionary<string, string>
            {
                ["MON1"] = @"C:\Pictures\A.jpg",
                ["MON2"] = @"C:\Pictures\B.jpg",
            };
            var repainted = new List<string>();
            PictureRollbackSnapshot rollback = null;
            var verifies = new Queue<bool>([false, false]);

            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                () => 2,
                index => index == 0 ? "MON1" : "MON2",
                _ => AttachedRect(),
                monitorId => rendered[monitorId],
                captured => rollback = captured,
                monitorIds => WallpaperApply.ActivateSpotlightDestination(
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => verifies.Dequeue(),
                    () => Provider,
                    path => WallpaperApply.PaintSpotlightProviderImageForMonitors(
                        path,
                        monitorIds,
                        _ => true,
                        (monitorId, providerPath) =>
                        {
                            repainted.Add(monitorId);
                            rendered[monitorId] = providerPath;
                        })));

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            CollectionAssert.AreEqual(new[] { "MON1", "MON2" }, repainted);

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
                logComplete: _ => { },
                logIncomplete: _ => { },
                restorePictureAssignments: () => WallpaperApply.RestorePictureRollbackSnapshot(rollback, (monitorId, priorPath) => rendered[monitorId] = priorPath));

            Assert.IsTrue(recovered);
            Assert.AreEqual(@"C:\Pictures\A.jpg", rendered["MON1"]);
            Assert.AreEqual(@"C:\Pictures\B.jpg", rendered["MON2"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FirstRestoreFailure_StillAttemptsSecondAndCannotLogComplete()
        {
            PictureRollbackSnapshot rollback = null;
            var source = new Dictionary<string, string>
            {
                ["MON1"] = @"C:\Pictures\A.jpg",
                ["MON2"] = @"C:\Pictures\B.jpg",
            };

            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                () => 2,
                index => index == 0 ? "MON1" : "MON2",
                _ => AttachedRect(),
                monitorId => source[monitorId],
                captured => rollback = captured,
                _ => WallpaperApplyOutcome.Failed);
            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);

            var attempts = new List<string>();
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
                    (monitorId, _) =>
                    {
                        attempts.Add(monitorId);
                        if (monitorId == "MON1")
                            throw new InvalidOperationException("first restore failed");
                    }));

            Assert.IsFalse(recovered);
            CollectionAssert.AreEqual(new[] { "MON1", "MON2" }, attempts);
            Assert.IsFalse(completeLogged);
            Assert.IsTrue(incompleteLogged);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PerMonitorRepaintFailure_AttemptsRemainingMonitorAndFails()
        {
            var attempts = new List<string>();
            bool repainted = WallpaperApply.PaintSpotlightProviderImageForMonitors(
                Provider,
                ["MON1", "MON2"],
                _ => true,
                (monitorId, _) =>
                {
                    attempts.Add(monitorId);
                    if (monitorId == "MON1") throw new InvalidOperationException("first repaint failed");
                });

            Assert.IsFalse(repainted);
            CollectionAssert.AreEqual(new[] { "MON1", "MON2" }, attempts);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AlreadyEstablishedSpotlight_StrictDiscoveryDoesNotSnapshotOrRepaint()
        {
            int pictureReads = 0;
            int repaintSelections = 0;
            int repaints = 0;
            var outcome = WallpaperApply.PrepareSpotlightDestinationApply(
                WallpaperMode.Spotlight,
                () => 2,
                index => index == 0 ? "MON1" : "MON2",
                _ => AttachedRect(),
                _ => { pictureReads++; throw new InvalidOperationException("should not snapshot Picture state"); },
                _ => Assert.Fail("should not retain Picture rollback"),
                monitorIds => WallpaperApply.ActivateSpotlightDestination(
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => { repaintSelections++; return Provider; },
                    _ => { repaints++; return true; }));

            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.AreEqual(0, repaintSelections);
            Assert.AreEqual(0, repaints);
        }
    }
}
