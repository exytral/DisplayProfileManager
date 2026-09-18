using DisplayProfileManager.Helpers;
using DisplayProfileManager.UI.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace DisplayProfileManager.Tests.Tests
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
            string selected = WallpaperHelper.SelectSpotlightRepaintPath(
                new[] { BuiltIn },
                new[] { Iris },
                Array.Empty<string>(),
                _ => true);

            Assert.AreEqual(BuiltIn, selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RepaintSelection_UsesBuiltInProviderFallbackWhenIrisIsUnavailable()
        {
            string selected = WallpaperHelper.SelectSpotlightRepaintPath(
                new[] { @"C:\Pictures\user.jpg" },
                new[] { Iris },
                new[] { BuiltIn },
                path => path == BuiltIn);

            Assert.AreEqual(BuiltIn, selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RepaintSelection_NeverAcceptsArbitraryUserContent()
        {
            string selected = WallpaperHelper.SelectSpotlightRepaintPath(
                new[] { @"C:\Pictures\user.jpg" },
                new[] { @"C:\Temp\cached.jpg" },
                Array.Empty<string>(),
                _ => true);

            Assert.IsNull(selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RepaintSelection_RejectsProviderMarkerSpoofOutsideOwnedRoots()
        {
            string spoof = @"C:\Pictures\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images\spoof.jpg";
            Assert.IsTrue(WallpaperHelper.IsSpotlightProviderPath(spoof));
            Assert.IsFalse(WallpaperHelper.IsVerifiedSpotlightProviderPath(spoof));
            Assert.IsNull(WallpaperHelper.SelectSpotlightRepaintPath(new[] { spoof }, Array.Empty<string>(), Array.Empty<string>(), _ => true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Activation_ProviderAlreadyRepainted_SkipsDirectPaint()
        {
            var calls = new List<string>();
            var outcome = WallpaperHelper.ActivateSpotlightDestination(
                () => { calls.Add("enable-desktop"); return true; },
                () => { calls.Add("relinquish"); return true; },
                () => { calls.Add("provider"); return true; },
                () => { calls.Add("mode"); return true; },
                () => { calls.Add("refresh"); return true; },
                () => { calls.Add("verify"); return true; },
                () => { calls.Add("select"); return BuiltIn; },
                _ => { calls.Add("paint"); return true; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { "enable-desktop", "relinquish", "provider", "mode", "refresh", "verify" }, calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Activation_MissingShellRepaint_PaintsThenReassertsAndVerifies()
        {
            var calls = new List<string>();
            var verifies = new Queue<bool>(new[] { false, true });
            var outcome = WallpaperHelper.ActivateSpotlightDestination(
                () => { calls.Add("enable-desktop"); return true; },
                () => { calls.Add("relinquish"); return true; },
                () => { calls.Add("provider"); return true; },
                () => { calls.Add("mode"); return true; },
                () => { calls.Add("refresh"); return true; },
                () => { calls.Add("verify"); return verifies.Dequeue(); },
                () => { calls.Add("select"); return BuiltIn; },
                path => { calls.Add("paint"); return path == BuiltIn; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { "enable-desktop", "relinquish", "provider", "mode", "refresh", "verify", "select", "paint", "provider", "mode", "refresh", "verify" }, calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Activation_NoProviderOwnedRepaintCandidate_FailsWithoutPolling()
        {
            int verifies = 0;
            int paints = 0;
            var outcome = WallpaperHelper.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => true,
                () => true,
                () => true,
                () => { verifies++; return false; },
                () => null,
                _ => { paints++; return true; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(1, verifies);
            Assert.AreEqual(0, paints);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Activation_DirectPaintNeverSubstitutesForFinalAuthority()
        {
            int verifies = 0;
            var outcome = WallpaperHelper.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => true,
                () => true,
                () => true,
                () => { verifies++; return false; },
                () => BuiltIn,
                _ => true);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(2, verifies);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DestinationVerification_CanRequireProviderImageToStillExist()
        {
            Assert.IsTrue(WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, false, new[] { BuiltIn }, path => path == BuiltIn));
            Assert.IsFalse(WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, false, new[] { BuiltIn }, _ => false));
            Assert.IsFalse(WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, false, new[] { @"C:\Pictures\user.jpg" }, _ => true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RefreshSelection_UsesSpiBeforeSettingsBroadcast()
        {
            var calls = new List<string>();
            bool result = WallpaperHelper.RefreshSpotlightSelection(
                () => { calls.Add("spi"); return true; },
                () => calls.Add("notify"));

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "spi", "notify" }, calls);
        }
    }

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
            WallpaperHelper.PictureRollbackSnapshot rollback = null;
            var verifies = new Queue<bool>(new[] { false, false });

            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                new[] { "MON1", "MON2" },
                monitorId => rendered[monitorId],
                captured => rollback = captured,
                () => WallpaperHelper.ActivateSpotlightDestination(
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

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            bool completeLogged = false;
            bool incompleteLogged = false;
            bool recovered = WallpaperHelper.RecoverPriorOwnerAfterFailure(() => WallpaperHelper.RestorePriorOwner(
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
                restorePictureAssignments: () => WallpaperHelper.RestorePictureRollbackSnapshot(rollback, (monitorId, path) => rendered[monitorId] = path)));

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
            WallpaperHelper.PictureRollbackSnapshot rollback = null;
            var providerResults = new Queue<bool>(new[] { true, false });

            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                new[] { "MON1", "MON2" },
                monitorId => rendered[monitorId],
                captured => rollback = captured,
                () => WallpaperHelper.ActivateSpotlightDestination(
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

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            bool recovered = WallpaperHelper.RecoverPriorOwnerAfterFailure(() => WallpaperHelper.RestorePriorOwner(
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
                restorePictureAssignments: () => WallpaperHelper.RestorePictureRollbackSnapshot(rollback, (monitorId, path) => rendered[monitorId] = path)));

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
            Assert.IsTrue(WallpaperHelper.TryCapturePictureRollbackSnapshot(new[] { "MON1", "MON2" }, monitorId => original[monitorId], out WallpaperHelper.PictureRollbackSnapshot rollback));

            var rendered = new Dictionary<string, string>
            {
                ["MON1"] = Provider,
                ["MON2"] = Provider,
            };
            bool completeLogged = false;
            bool incompleteLogged = false;
            bool recovered = WallpaperHelper.RestorePriorOwner(
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
                restorePictureAssignments: () => WallpaperHelper.RestorePictureRollbackSnapshot(
                    rollback,
                    (monitorId, path) =>
                    {
                        if (monitorId == "MON2")
                            throw new InvalidOperationException("setter failed");
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
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                new[] { "MON1", "MON2" },
                monitorId => monitorId == "MON1" ? @"C:\Pictures\A.jpg" : throw new InvalidOperationException("read failed"),
                _ => remembered = true,
                () =>
                {
                    activated = true;
                    return WallpaperHelper.WallpaperApplyOutcome.Success;
                });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.IsFalse(remembered);
            Assert.IsFalse(activated);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AlreadySpotlightDestination_DoesNotRequirePictureRollbackSnapshot()
        {
            int pictureReads = 0;
            bool remembered = false;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Spotlight,
                new[] { "MON1", "MON2" },
                _ => { pictureReads++; throw new InvalidOperationException("should not read Picture rollback state"); },
                _ => remembered = true,
                () => WallpaperHelper.WallpaperApplyOutcome.Success);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.IsFalse(remembered);
        }
    }

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

        private static WallpaperHelper.DesktopMonitorRectResult AttachedRect() => new WallpaperHelper.DesktopMonitorRectResult(WallpaperHelper.HResultSOk, 0, 0, 1920, 1080);

        [TestMethod]
        [TestCategory("Unit")]
        public void MonitorPathEnumerationFailure_StopsBeforeSnapshotOrRepaint()
        {
            int pictureReads = 0;
            bool activated = false;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                () => 2,
                index => index == 0 ? "MON1" : throw new InvalidOperationException("monitor path failed"),
                _ => AttachedRect(),
                _ => { pictureReads++; return @"C:\Pictures\A.jpg"; },
                _ => Assert.Fail("rollback must not be retained"),
                _ => { activated = true; return WallpaperHelper.WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.IsFalse(activated);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UnknownMonitorRectFailure_StopsBeforeSnapshotOrRepaint()
        {
            int pictureReads = 0;
            bool activated = false;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                () => 2,
                index => index == 0 ? "MON1" : "MON2",
                monitorId => monitorId == "MON1"
                    ? AttachedRect()
                    : new WallpaperHelper.DesktopMonitorRectResult(unchecked((int)0x80070057), 0, 0, 0, 0),
                _ => { pictureReads++; return @"C:\Pictures\A.jpg"; },
                _ => Assert.Fail("rollback must not be retained"),
                _ => { activated = true; return WallpaperHelper.WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.IsFalse(activated);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DocumentedDetachedMonitor_IsOmittedButUnknownFailureIsNot()
        {
            IReadOnlyList<string> activatedDomain = null;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Spotlight,
                () => 2,
                index => index == 0 ? "MON1" : "DETACHED",
                monitorId => monitorId == "MON1"
                    ? AttachedRect()
                    : new WallpaperHelper.DesktopMonitorRectResult(WallpaperHelper.HResultSFalse, 0, 0, 0, 0),
                _ => throw new InvalidOperationException("Spotlight source must not snapshot Picture state"),
                _ => Assert.Fail("Spotlight source must not retain Picture rollback"),
                monitorIds =>
                {
                    activatedDomain = monitorIds;
                    return WallpaperHelper.WallpaperApplyOutcome.Success;
                });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
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
            WallpaperHelper.PictureRollbackSnapshot rollback = null;
            var verifies = new Queue<bool>(new[] { false, false });

            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                () => 2,
                index => index == 0 ? "MON1" : "MON2",
                _ => AttachedRect(),
                monitorId => rendered[monitorId],
                captured => rollback = captured,
                monitorIds => WallpaperHelper.ActivateSpotlightDestination(
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => verifies.Dequeue(),
                    () => Provider,
                    path => WallpaperHelper.PaintSpotlightProviderImageForMonitors(
                        path,
                        monitorIds,
                        _ => true,
                        (monitorId, providerPath) =>
                        {
                            repainted.Add(monitorId);
                            rendered[monitorId] = providerPath;
                        })));

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            CollectionAssert.AreEqual(new[] { "MON1", "MON2" }, repainted);

            bool recovered = WallpaperHelper.RestorePriorOwner(
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
                restorePictureAssignments: () => WallpaperHelper.RestorePictureRollbackSnapshot(rollback, (monitorId, priorPath) => rendered[monitorId] = priorPath));

            Assert.IsTrue(recovered);
            Assert.AreEqual(@"C:\Pictures\A.jpg", rendered["MON1"]);
            Assert.AreEqual(@"C:\Pictures\B.jpg", rendered["MON2"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FirstRestoreFailure_StillAttemptsSecondAndCannotLogComplete()
        {
            WallpaperHelper.PictureRollbackSnapshot rollback = null;
            var source = new Dictionary<string, string>
            {
                ["MON1"] = @"C:\Pictures\A.jpg",
                ["MON2"] = @"C:\Pictures\B.jpg",
            };

            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                () => 2,
                index => index == 0 ? "MON1" : "MON2",
                _ => AttachedRect(),
                monitorId => source[monitorId],
                captured => rollback = captured,
                _ => WallpaperHelper.WallpaperApplyOutcome.Failed);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);

            var attempts = new List<string>();
            bool completeLogged = false;
            bool incompleteLogged = false;
            bool recovered = WallpaperHelper.RestorePriorOwner(
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
                restorePictureAssignments: () => WallpaperHelper.RestorePictureRollbackSnapshot(
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
            bool repainted = WallpaperHelper.PaintSpotlightProviderImageForMonitors(
                Provider,
                new[] { "MON1", "MON2" },
                _ => true,
                (monitorId, _) =>
                {
                    attempts.Add(monitorId);
                    if (monitorId == "MON1")
                        throw new InvalidOperationException("first repaint failed");
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
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Spotlight,
                () => 2,
                index => index == 0 ? "MON1" : "MON2",
                _ => AttachedRect(),
                _ => { pictureReads++; throw new InvalidOperationException("should not snapshot Picture state"); },
                _ => Assert.Fail("should not retain Picture rollback"),
                monitorIds => WallpaperHelper.ActivateSpotlightDestination(
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => true,
                    () => { repaintSelections++; return Provider; },
                    _ => { repaints++; return true; }));

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.AreEqual(0, repaintSelections);
            Assert.AreEqual(0, repaints);
        }
    }

    [TestClass]
    public class SolidWallpaperOwnershipTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ClassicSolidBridge_WritesEmptyLegacyWallpaperBeforeShellApply()
        {
            var calls = new List<string>();
            bool result = WallpaperHelper.ApplyClassicSolidOwnershipBridge(
                () => { calls.Add("registry-empty"); return true; },
                () => { calls.Add("spi-empty"); return true; });

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "registry-empty", "spi-empty" }, calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassicSolidBridge_RegistryFailureStopsBeforeShellApply()
        {
            bool shellCalled = false;
            bool result = WallpaperHelper.ApplyClassicSolidOwnershipBridge(
                () => false,
                () => { shellCalled = true; return true; });

            Assert.IsFalse(result);
            Assert.IsFalse(shellCalled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowSupersession_StillRequiresAuthoritativePostBridgeClear()
        {
            var states = new Queue<bool?>(new bool?[] { true, false });
            int bridgeCalls = 0;
            var outcome = WallpaperHelper.SupersedeSlideshowOwnership(
                () => states.Dequeue(),
                () => { bridgeCalls++; return true; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual(1, bridgeCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowSupersession_BridgeCannotClearStatus_RemainsFailure()
        {
            var states = new Queue<bool?>(new bool?[] { true, true });
            var outcome = WallpaperHelper.SupersedeSlideshowOwnership(
                () => states.Dequeue(),
                () => true);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
        }
    }

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
                sdrCompatibleProfiles: Array.Empty<string>());

            Assert.IsNull(enteredHdr.SelectedProfile);
            Assert.AreEqual("sdr-only.icc", enteredHdr.RememberedSdrProfile);

            var leftHdr = DisplaySettingControl.ResolveHdrColorProfileEditorState(
                hdrOn: false,
                wasHdrOn: true,
                currentProfile: null,
                rememberedSdrProfile: enteredHdr.RememberedSdrProfile,
                hdrSelectionChanged: false,
                sdrCompatibleProfiles: new[] { "sdr-only.icc" });

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
                sdrCompatibleProfiles: new[] { "sdr-only.icc", "dual-mode.icc" });

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
                sdrCompatibleProfiles: new[] { "sdr-only.icc" });

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
                sdrCompatibleProfiles: new[] { "sdr-only.icc" });

            Assert.IsNull(state.SelectedProfile);
            Assert.IsNull(state.RememberedSdrProfile);
        }
    }
}
