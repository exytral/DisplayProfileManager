using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class SpotlightPresentationTests
    {
        private static string BuiltInProviderPath(string name) => Path.Combine(
            Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
            "SystemApps",
            "MicrosoftWindows.Client.CBS_cw5n1h2txyewy",
            "DesktopSpotlight",
            "Assets",
            "Images",
            name);

        private static int SpotlightBackgroundType => WallpaperHelper.BackgroundTypeForMode(WallpaperMode.Spotlight).Value;

        private static WallpaperHelper.WallpaperApplyOutcome ActivateWithPresentation(
            Action normalize,
            Func<bool> verify,
            Func<string> selectRepaintPath,
            Func<string, bool> paintProviderImage,
            Action providerActivation = null)
        {
            return WallpaperHelper.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => { providerActivation?.Invoke(); return true; },
                () => true,
                () => true,
                () => WallpaperHelper.NormalizeSpotlightPositionToFill(normalize),
                verify,
                selectRepaintPath,
                paintProviderImage);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureFitToSpotlight_NormalizesFillAndRequiresFillAtFinalVerification()
        {
            string position = "fit";
            int providerActivations = 0;
            int repaintSelections = 0;
            string rendered = BuiltInProviderPath("image_0.jpg");

            var plan = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Spotlight);
            CollectionAssert.AreEqual(new[] { WallpaperHelper.WallpaperTransitionStep.ActivateSpotlight }, plan.Steps.ToArray());

            var outcome = ActivateWithPresentation(
                () => position = "fill",
                () => WallpaperHelper.IsSpotlightDestinationEstablished(
                    SpotlightBackgroundType,
                    1,
                    false,
                    position == "fill",
                    new[] { rendered },
                    _ => true),
                () => { repaintSelections++; return rendered; },
                _ => throw new InvalidOperationException("verified provider rendering must skip repaint"),
                () => providerActivations++);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual("fill", position);
            Assert.AreEqual(2, providerActivations, "provider/mode must be reasserted after SetPosition(Fill)");
            Assert.AreEqual(0, repaintSelections);
            Assert.IsFalse(WallpaperHelper.IsSpotlightDestinationEstablished(SpotlightBackgroundType, 1, false, false, new[] { rendered }, _ => true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CenterAndStretchToSpotlight_NormalizeToFill()
        {
            foreach (string initialPosition in new[] { "center", "stretch" })
            {
                string position = initialPosition;
                string rendered = BuiltInProviderPath("image_0.jpg");
                var outcome = ActivateWithPresentation(
                    () => position = "fill",
                    () => WallpaperHelper.IsSpotlightDestinationEstablished(
                        SpotlightBackgroundType, 1, false, position == "fill", new[] { rendered }, _ => true),
                    () => throw new InvalidOperationException("repaint should not be needed"),
                    _ => false);

                Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome, initialPosition);
                Assert.AreEqual("fill", position, initialPosition);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightReapply_WithStaleFit_NormalizesEvenWhenRepaintIsSkipped()
        {
            string position = "fit";
            int repaintSelections = 0;
            string rendered = BuiltInProviderPath("image_0.jpg");
            var plan = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Spotlight, WallpaperMode.Spotlight);

            var outcome = ActivateWithPresentation(
                () => position = "fill",
                () => WallpaperHelper.IsSpotlightDestinationEstablished(
                    SpotlightBackgroundType, 1, false, position == "fill", new[] { rendered }, _ => true),
                () => { repaintSelections++; return rendered; },
                _ => true);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual("fill", position);
            Assert.AreEqual(0, repaintSelections);
            CollectionAssert.AreEqual(new[] { WallpaperHelper.WallpaperTransitionStep.ActivateSpotlight }, plan.Steps.ToArray());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SetPositionFillFailure_FailsSpotlightDestinationBeforeSuccess()
        {
            bool verified = false;
            var outcome = ActivateWithPresentation(
                () => throw new COMException("SetPosition failed"),
                () => { verified = true; return true; },
                () => BuiltInProviderPath("image_0.jpg"),
                _ => true);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.IsFalse(verified);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LaterSpotlightFailure_RestoresExactPriorPositionAndPictureAssignments()
        {
            const string monitorA = @"\\?\DISPLAY#A";
            const string monitorB = @"\\?\DISPLAY#B";
            string priorPosition = "fit";
            string position = priorPosition;
            var rendered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [monitorA] = @"C:\Pictures\A.jpg",
                [monitorB] = @"C:\Pictures\B.jpg",
            };
            Assert.IsTrue(WallpaperHelper.TryCapturePictureRollbackSnapshot(new[] { monitorA, monitorB }, monitorId => rendered[monitorId], out var snapshot));

            string provider = BuiltInProviderPath("image_0.jpg");
            var outcome = ActivateWithPresentation(
                () => position = "fill",
                () => false,
                () => provider,
                path => WallpaperHelper.PaintSpotlightProviderImageForMonitors(
                    path,
                    new[] { monitorA, monitorB },
                    _ => true,
                    (monitorId, providerPath) => rendered[monitorId] = providerPath));

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual("fill", position);
            Assert.AreEqual(provider, rendered[monitorA]);
            Assert.AreEqual(provider, rendered[monitorB]);

            bool restored = WallpaperHelper.RestorePriorOwner(
                WallpaperMode.Picture,
                legacyWallpaperCaptured: false,
                priorLegacyWallpaperExists: false,
                priorLegacyWallpaper: null,
                restoreSharedState: () => { position = priorPosition; return true; },
                restoreSpotlightOwner: () => true,
                deactivateSpotlight: () => true,
                setLegacyWallpaper: _ => true,
                deleteLegacyWallpaper: () => true,
                restoreSourceOwnership: () => true,
                establishSourceMode: () => true,
                notifySettings: () => { },
                logComplete: _ => { },
                logIncomplete: _ => { },
                restorePictureAssignments: () => WallpaperHelper.RestorePictureRollbackSnapshot(snapshot, (monitorId, path) => rendered[monitorId] = path));

            Assert.IsTrue(restored);
            Assert.AreEqual("fit", position);
            Assert.AreEqual(@"C:\Pictures\A.jpg", rendered[monitorA]);
            Assert.AreEqual(@"C:\Pictures\B.jpg", rendered[monitorB]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureAndSlideshowConfiguredFitmentPlansRemainUnchanged()
        {
            var picture = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Solid, WallpaperMode.Picture);
            var slideshow = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Solid, WallpaperMode.Slideshow);
            var spotlight = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Spotlight);

            CollectionAssert.Contains(picture.Steps.ToList(), WallpaperHelper.WallpaperTransitionStep.SetPosition);
            CollectionAssert.Contains(slideshow.Steps.ToList(), WallpaperHelper.WallpaperTransitionStep.SetPosition);
            CollectionAssert.DoesNotContain(spotlight.Steps.ToList(), WallpaperHelper.WallpaperTransitionStep.SetPosition);
            Assert.AreEqual("fit", WallpaperHelper.NormalizePosition("fit"));
            Assert.AreEqual("center", WallpaperHelper.NormalizePosition("center"));
            Assert.AreEqual("stretch", WallpaperHelper.NormalizePosition("stretch"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CurrentVerifiedProviderRendering_RemainsHighestPriorityEvenIfPortrait()
        {
            string currentPortrait = BuiltInProviderPath("current-portrait.jpg");
            string fallbackLandscape = BuiltInProviderPath("fallback-landscape.jpg");

            string selected = WallpaperHelper.SelectSpotlightRepaintPath(
                new[] { currentPortrait },
                new[] { fallbackLandscape },
                Array.Empty<string>(),
                _ => true,
                path => path.Contains("landscape", StringComparison.OrdinalIgnoreCase));

            Assert.AreEqual(currentPortrait, selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FallbackSelection_SkipsPortraitAndKeepsLandscapeOrder()
        {
            string portrait = BuiltInProviderPath("01-portrait.jpg");
            string landscapeA = BuiltInProviderPath("02-landscape-a.jpg");
            string landscapeB = BuiltInProviderPath("03-landscape-b.jpg");

            string selected = WallpaperHelper.SelectSpotlightRepaintPath(
                Array.Empty<string>(),
                new[] { portrait, landscapeA, landscapeB },
                Array.Empty<string>(),
                _ => true,
                path => path.Contains("landscape", StringComparison.OrdinalIgnoreCase));

            Assert.AreEqual(landscapeA, selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PortraitOnlyFallback_IsRejected()
        {
            string portraitA = BuiltInProviderPath("portrait-a.jpg");
            string portraitB = BuiltInProviderPath("portrait-b.jpg");

            string selected = WallpaperHelper.SelectSpotlightRepaintPath(
                Array.Empty<string>(),
                new[] { portraitA },
                new[] { portraitB },
                _ => true,
                _ => false);

            Assert.IsNull(selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LandscapeDimensionPredicate_RejectsPortraitAndInvalidImages()
        {
            Assert.IsTrue(WallpaperHelper.IsLandscapeImageDimensions(1920, 1080));
            Assert.IsTrue(WallpaperHelper.IsLandscapeImageDimensions(1080, 1080));
            Assert.IsFalse(WallpaperHelper.IsLandscapeImageDimensions(1080, 1920));
            Assert.IsFalse(WallpaperHelper.IsLandscapeImageDimensions(0, 1080));
            Assert.IsFalse(WallpaperHelper.IsLandscapeImageDimensions(1920, 0));
        }
    }
}
