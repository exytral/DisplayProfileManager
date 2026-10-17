using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Interop;
using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;

namespace DisplayProfileManager.Tests.Wallpapers
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

        private static int SpotlightBackgroundType => WallpaperApply.BackgroundTypeForMode(WallpaperMode.Spotlight).Value;

        private static WallpaperApplyOutcome ActivateWithPresentation(
            Action normalize,
            Func<bool> verify,
            Func<string> selectRepaintPath,
            Func<string, bool> paintProviderImage,
            Action providerActivation = null)
        {
            return WallpaperApply.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => { providerActivation?.Invoke(); return true; },
                () => true,
                () => true,
                () => WallpaperApply.NormalizeSpotlightPositionToFill(normalize),
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

            var plan = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Spotlight);
            CollectionAssert.AreEqual(new[] { WallpaperTransitionStep.ActivateSpotlight }, plan.Steps.ToArray());

            var outcome = ActivateWithPresentation(
                () => position = "fill",
                () => WallpaperApply.IsSpotlightDestinationEstablished(
                    SpotlightBackgroundType,
                    1,
                    false,
                    position == "fill",
                    [rendered],
                    _ => true),
                () => { repaintSelections++; return rendered; },
                _ => throw new InvalidOperationException("verified provider rendering must skip repaint"),
                () => providerActivations++);

            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual("fill", position);
            Assert.AreEqual(2, providerActivations, "provider/mode must be reasserted after SetPosition(Fill)");
            Assert.AreEqual(0, repaintSelections);
            Assert.IsFalse(WallpaperApply.IsSpotlightDestinationEstablished(SpotlightBackgroundType, 1, false, false, [rendered], _ => true));
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
                    () => WallpaperApply.IsSpotlightDestinationEstablished(
                        SpotlightBackgroundType, 1, false, position == "fill", [rendered], _ => true),
                    () => throw new InvalidOperationException("repaint should not be needed"),
                    _ => false);

                Assert.AreEqual(WallpaperApplyOutcome.Success, outcome, initialPosition);
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
            var plan = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Spotlight, WallpaperMode.Spotlight);

            var outcome = ActivateWithPresentation(
                () => position = "fill",
                () => WallpaperApply.IsSpotlightDestinationEstablished(
                    SpotlightBackgroundType, 1, false, position == "fill", [rendered], _ => true),
                () => { repaintSelections++; return rendered; },
                _ => true);

            Assert.AreEqual(WallpaperApplyOutcome.Success, outcome);
            Assert.AreEqual("fill", position);
            Assert.AreEqual(0, repaintSelections);
            CollectionAssert.AreEqual(new[] { WallpaperTransitionStep.ActivateSpotlight }, plan.Steps.ToArray());
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

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
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
            Assert.IsTrue(WallpaperApply.TryCapturePictureRollbackSnapshot([monitorA, monitorB], monitorId => rendered[monitorId], out var snapshot));

            string provider = BuiltInProviderPath("image_0.jpg");
            var outcome = ActivateWithPresentation(
                () => position = "fill",
                () => false,
                () => provider,
                path => WallpaperApply.PaintSpotlightProviderImageForMonitors(
                    path,
                    [monitorA, monitorB],
                    _ => true,
                    (monitorId, providerPath) => rendered[monitorId] = providerPath));

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            Assert.AreEqual("fill", position);
            Assert.AreEqual(provider, rendered[monitorA]);
            Assert.AreEqual(provider, rendered[monitorB]);

            bool restored = WallpaperApply.RestorePriorOwner(
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
                restorePictureAssignments: () => WallpaperApply.RestorePictureRollbackSnapshot(snapshot, (monitorId, path) => rendered[monitorId] = path));

            Assert.IsTrue(restored);
            Assert.AreEqual("fit", position);
            Assert.AreEqual(@"C:\Pictures\A.jpg", rendered[monitorA]);
            Assert.AreEqual(@"C:\Pictures\B.jpg", rendered[monitorB]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureAndSlideshowConfiguredFitmentPlansRemainUnchanged()
        {
            var picture = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Solid, WallpaperMode.Picture);
            var slideshow = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Solid, WallpaperMode.Slideshow);
            var spotlight = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Spotlight);

            CollectionAssert.Contains(picture.Steps.ToList(), WallpaperTransitionStep.SetPosition);
            CollectionAssert.Contains(slideshow.Steps.ToList(), WallpaperTransitionStep.SetPosition);
            CollectionAssert.DoesNotContain(spotlight.Steps.ToList(), WallpaperTransitionStep.SetPosition);
            Assert.AreEqual("fit", WindowsWallpaperPlatform.NormalizePosition("fit"));
            Assert.AreEqual("center", WindowsWallpaperPlatform.NormalizePosition("center"));
            Assert.AreEqual("stretch", WindowsWallpaperPlatform.NormalizePosition("stretch"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CurrentVerifiedProviderRendering_RemainsHighestPriorityEvenIfPortrait()
        {
            string currentPortrait = BuiltInProviderPath("current-portrait.jpg");
            string fallbackLandscape = BuiltInProviderPath("fallback-landscape.jpg");

            string selected = SpotlightProvider.SelectSpotlightRepaintPath(
                [currentPortrait],
                [fallbackLandscape],
                [],
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

            string selected = SpotlightProvider.SelectSpotlightRepaintPath(
                [],
                [portrait, landscapeA, landscapeB],
                [],
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

            string selected = SpotlightProvider.SelectSpotlightRepaintPath(
                [],
                [portraitA],
                [portraitB],
                _ => true,
                _ => false);

            Assert.IsNull(selected);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LandscapeDimensionPredicate_RejectsPortraitAndInvalidImages()
        {
            Assert.IsTrue(SpotlightProvider.IsLandscapeImageDimensions(1920, 1080));
            Assert.IsTrue(SpotlightProvider.IsLandscapeImageDimensions(1080, 1080));
            Assert.IsFalse(SpotlightProvider.IsLandscapeImageDimensions(1080, 1920));
            Assert.IsFalse(SpotlightProvider.IsLandscapeImageDimensions(0, 1080));
            Assert.IsFalse(SpotlightProvider.IsLandscapeImageDimensions(1920, 0));
        }
    }
}
