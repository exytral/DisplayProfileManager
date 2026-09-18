using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class SpotlightCurrentActiveDomainTests
    {
        private const string TargetA = @"\\?\DISPLAY#TARGET_A";
        private const string TargetB = @"\\?\DISPLAY#TARGET_B";
        private const string Historical = @"\\?\DISPLAY#HISTORICAL";
        private const string Extra = @"\\?\DISPLAY#EXTRA";

        private static DisplayConfigHelper.DisplayConfigInfo Ccd(string deviceName, string monitorPath) =>
            new DisplayConfigHelper.DisplayConfigInfo
            {
                DeviceName = deviceName,
                MonitorDevicePath = monitorPath,
                IsEnabled = true,
            };

        private static List<DisplayConfigHelper.DisplayConfigInfo> LiveTargets() =>
            new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Ccd(@"\\.\DISPLAY1", TargetA),
                Ccd(@"\\.\DISPLAY2", TargetB),
            };

        private static WallpaperHelper.DesktopMonitorRectResult AttachedRect() => new WallpaperHelper.DesktopMonitorRectResult(WallpaperHelper.HResultSOk, 0, 0, 1920, 1080);

        private static WallpaperHelper.DesktopMonitorRectResult DetachedRect() => new WallpaperHelper.DesktopMonitorRectResult(WallpaperHelper.HResultSFalse, 0, 0, 0, 0);

        [TestMethod]
        [TestCategory("Unit")]
        public void BlankHistoricalEntry_DoesNotBlockCurrentActiveDomain()
        {
            IReadOnlyList<string> domain = null;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Spotlight,
                LiveTargets(),
                () => 3,
                index => index == 0 ? TargetA : index == 1 ? TargetB : string.Empty,
                _ => AttachedRect(),
                _ => throw new InvalidOperationException("Spotlight source must not snapshot Picture state"),
                _ => Assert.Fail("Spotlight source must not retain Picture rollback"),
                ids => { domain = ids; return WallpaperHelper.WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { TargetA, TargetB }, domain.ToList());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DuplicateHistoricalLiveId_DeduplicatesWithoutSecondMutationTarget()
        {
            IReadOnlyList<string> domain = null;
            int rectReadsForA = 0;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Spotlight,
                LiveTargets(),
                () => 3,
                index => index == 0 ? TargetA : index == 1 ? TargetB : TargetA,
                monitorId =>
                {
                    if (monitorId == TargetA) rectReadsForA++;
                    return AttachedRect();
                },
                _ => throw new InvalidOperationException("Spotlight source must not snapshot Picture state"),
                _ => Assert.Fail("Spotlight source must not retain Picture rollback"),
                ids => { domain = ids; return WallpaperHelper.WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { TargetA, TargetB }, domain.ToList());
            Assert.AreEqual(1, rectReadsForA);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DetachedHistoricalEntry_DoesNotBlockCurrentActiveDomain()
        {
            IReadOnlyList<string> domain = null;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Spotlight,
                LiveTargets(),
                () => 3,
                index => index == 0 ? TargetA : index == 1 ? TargetB : Historical,
                monitorId => monitorId == Historical ? DetachedRect() : AttachedRect(),
                _ => throw new InvalidOperationException("Spotlight source must not snapshot Picture state"),
                _ => Assert.Fail("Spotlight source must not retain Picture rollback"),
                ids => { domain = ids; return WallpaperHelper.WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            CollectionAssert.AreEqual(new[] { TargetA, TargetB }, domain.ToList());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MissingExpectedLiveTarget_FailsBeforeSnapshotOrMutation()
        {
            int pictureReads = 0;
            bool activated = false;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                LiveTargets(),
                () => 2,
                index => index == 0 ? TargetA : Historical,
                monitorId => monitorId == Historical ? DetachedRect() : AttachedRect(),
                _ => { pictureReads++; return @"C:\Pictures\prior.jpg"; },
                _ => Assert.Fail("rollback must not be retained"),
                _ => { activated = true; return WallpaperHelper.WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.IsFalse(activated);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExpectedLiveTargetDetached_FailsBeforeSnapshotOrMutation()
        {
            bool activated = false;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                LiveTargets(),
                () => 2,
                index => index == 0 ? TargetA : TargetB,
                monitorId => monitorId == TargetB ? DetachedRect() : AttachedRect(),
                _ => throw new InvalidOperationException("Picture snapshot must not start"),
                _ => Assert.Fail("rollback must not be retained"),
                _ => { activated = true; return WallpaperHelper.WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.IsFalse(activated);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExpectedLiveTargetUnknownOrThrow_FailsBeforeMutation()
        {
            foreach (bool throwRect in new[] { false, true })
            {
                bool activated = false;
                var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                    WallpaperMode.Picture,
                    LiveTargets(),
                    () => 2,
                    index => index == 0 ? TargetA : TargetB,
                    monitorId =>
                    {
                        if (monitorId == TargetA) return AttachedRect();
                        if (throwRect) throw new InvalidOperationException("RECT failed");
                        return new WallpaperHelper.DesktopMonitorRectResult(unchecked((int)0x80070057), 0, 0, 0, 0);
                    },
                    _ => throw new InvalidOperationException("Picture snapshot must not start"),
                    _ => Assert.Fail("rollback must not be retained"),
                    _ => { activated = true; return WallpaperHelper.WallpaperApplyOutcome.Success; });

                Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
                Assert.IsFalse(activated);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AmbiguousEnabledCcdTargetIdentity_FailsBeforeWallpaperEnumeration()
        {
            int countReads = 0;
            var ambiguous = new[]
            {
                Ccd(@"\\.\DISPLAY1", TargetA),
                Ccd(@"\\.\DISPLAY2", TargetA),
            };

            bool result = WallpaperHelper.TryResolveCurrentActiveSpotlightMonitorIds(
                ambiguous,
                () => { countReads++; return 2; },
                _ => TargetA,
                _ => AttachedRect(),
                out _);

            Assert.IsFalse(result);
            Assert.AreEqual(0, countReads);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UnexpectedAttachedWallpaperMonitor_FailsClosedBeforeMutation()
        {
            int pictureReads = 0;
            bool activated = false;
            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                LiveTargets(),
                () => 3,
                index => index == 0 ? TargetA : index == 1 ? TargetB : Extra,
                _ => AttachedRect(),
                _ => { pictureReads++; return @"C:\Pictures\prior.jpg"; },
                _ => Assert.Fail("rollback must not be retained"),
                _ => { activated = true; return WallpaperHelper.WallpaperApplyOutcome.Success; });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(0, pictureReads);
            Assert.IsFalse(activated);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProvenDomain_FeedsSnapshotRepaintVerificationAndRollbackUnchanged()
        {
            string provider = System.IO.Path.Combine(
                Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
                "SystemApps",
                "MicrosoftWindows.Client.CBS_cw5n1h2txyewy",
                "DesktopSpotlight",
                "Assets",
                "Images",
                "provider.jpg");
            var rendered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TargetA] = @"C:\Pictures\A.jpg",
                [TargetB] = @"C:\Pictures\B.jpg",
            };
            WallpaperHelper.PictureRollbackSnapshot rollback = null;
            IReadOnlyList<string> activationDomain = null;
            var repaintIds = new List<string>();
            var verificationDomains = new List<string>();
            var restoreIds = new List<string>();

            var outcome = WallpaperHelper.PrepareSpotlightDestinationApply(
                WallpaperMode.Picture,
                LiveTargets(),
                () => 3,
                index => index == 0 ? TargetA : index == 1 ? TargetB : string.Empty,
                _ => AttachedRect(),
                monitorId => rendered[monitorId],
                captured => rollback = captured,
                monitorIds =>
                {
                    activationDomain = monitorIds;
                    return WallpaperHelper.ActivateSpotlightDestination(
                        () => true,
                        () => true,
                        () => true,
                        () => true,
                        () => true,
                        () =>
                        {
                            verificationDomains.Add(string.Join("|", monitorIds));
                            return false;
                        },
                        () => provider,
                        path => WallpaperHelper.PaintSpotlightProviderImageForMonitors(
                            path,
                            monitorIds,
                            _ => true,
                            (monitorId, providerPath) =>
                            {
                                repaintIds.Add(monitorId);
                                rendered[monitorId] = providerPath;
                            }));
                });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            CollectionAssert.AreEqual(new[] { TargetA, TargetB }, activationDomain.ToList());
            CollectionAssert.AreEqual(new[] { TargetA, TargetB }, rollback.Assignments.Select(item => item.MonitorId).ToList());
            CollectionAssert.AreEqual(new[] { TargetA, TargetB }, repaintIds);
            CollectionAssert.AreEqual(new[] { $"{TargetA}|{TargetB}", $"{TargetA}|{TargetB}" }, verificationDomains);

            bool restored = WallpaperHelper.RestorePictureRollbackSnapshot(
                rollback,
                (monitorId, path) =>
                {
                    restoreIds.Add(monitorId);
                    rendered[monitorId] = path;
                });

            Assert.IsTrue(restored);
            CollectionAssert.AreEqual(new[] { TargetA, TargetB }, restoreIds);
            Assert.AreEqual(@"C:\Pictures\A.jpg", rendered[TargetA]);
            Assert.AreEqual(@"C:\Pictures\B.jpg", rendered[TargetB]);
        }
    }

    [TestClass]
    public class SlideshowSettlementTests
    {
        private static int SlideshowBackgroundType => WallpaperHelper.BackgroundTypeForMode(WallpaperMode.Slideshow).Value;

        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultSettlementBound_NeverTrue_WaitsNoMoreThanTwoHundredMilliseconds()
        {
            int reads = 0;
            int delayedMilliseconds = 0;

            var outcome = WallpaperHelper.VerifySlideshowOwnershipSettled(
                () => SlideshowBackgroundType,
                () => { reads++; return false; },
                milliseconds => delayedMilliseconds += milliseconds);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(5, reads);
            Assert.AreEqual(200, delayedMilliseconds);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FalseFalseTrue_SettlesSuccessfullyWithinBound()
        {
            var status = new Queue<bool?>(new bool?[] { false, false, true });
            int delays = 0;

            var outcome = WallpaperHelper.VerifySlideshowOwnershipSettled(
                () => SlideshowBackgroundType,
                () => status.Dequeue(),
                _ => delays++,
                maxAttempts: 5,
                delayMs: 1);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual(2, delays);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TransientStatusExceptionThenTrue_SettlesSuccessfully()
        {
            int statusReads = 0;
            var outcome = WallpaperHelper.VerifySlideshowOwnershipSettled(
                () => SlideshowBackgroundType,
                () =>
                {
                    statusReads++;
                    if (statusReads == 1) throw new InvalidOperationException("transient GetStatus failure");
                    return true;
                },
                _ => { },
                maxAttempts: 3,
                delayMs: 1);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual(2, statusReads);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NeverTrue_TimesOutWithoutRegistryOnlySuccess()
        {
            int statusReads = 0;
            int delays = 0;
            var outcome = WallpaperHelper.VerifySlideshowOwnershipSettled(
                () => SlideshowBackgroundType,
                () => { statusReads++; return false; },
                _ => delays++,
                maxAttempts: 3,
                delayMs: 1);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(3, statusReads);
            Assert.AreEqual(2, delays);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ContradictoryBackgroundMode_FailsImmediately()
        {
            var modes = new Queue<int?>(new int?[] { SlideshowBackgroundType, WallpaperHelper.BackgroundTypeForMode(WallpaperMode.Picture) });
            int statusReads = 0;
            var outcome = WallpaperHelper.VerifySlideshowOwnershipSettled(
                () => modes.Dequeue(),
                () => { statusReads++; return false; },
                _ => { },
                maxAttempts: 5,
                delayMs: 1);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual(1, statusReads);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SettledSlideshow_RemainsAuthoritativelyDetectableAsLaterSource()
        {
            var status = new Queue<bool?>(new bool?[] { false, true });
            var outcome = WallpaperHelper.VerifySlideshowOwnershipSettled(
                () => SlideshowBackgroundType,
                () => status.Dequeue(),
                _ => { },
                maxAttempts: 3,
                delayMs: 1);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            Assert.IsTrue(WallpaperHelper.TryDetectCurrentModeForApply(SlideshowBackgroundType, string.Empty, () => 0x02, out WallpaperMode detected));
            Assert.AreEqual(WallpaperMode.Slideshow, detected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureAndSolidOwnershipVerification_RemainsImmediateAndAuthoritative()
        {
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, WallpaperHelper.VerifyDestinationOwnership(WallpaperMode.Picture, () => WallpaperMode.Picture, () => false));
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, WallpaperHelper.VerifyDestinationOwnership(WallpaperMode.Solid, () => WallpaperMode.Solid, () => false));
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, WallpaperHelper.VerifyDestinationOwnership(WallpaperMode.Picture, () => WallpaperMode.Picture, () => true));
        }
    }
}
