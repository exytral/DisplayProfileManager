using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class WallpaperSettingsTests
    {
        private static WallpaperSettings SnapshotWith(params string[] devices)
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            foreach (var device in devices)
                snapshot.PerMonitor[device] = new MonitorWallpaper { Path = @"C:\Wallpapers\Picture.jpg" };
            return snapshot;
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Snapshot_PositionDefaultsToFill()
        {
            Assert.AreEqual("fill", new WallpaperSettings().Position);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Snapshot_PositionIsDesktopWideNotPerMonitor()
        {
            var snapshot = SnapshotWith(@"\\.\DISPLAY1", @"\\.\DISPLAY2");
            snapshot.Position = "span";

            Assert.AreEqual("span", snapshot.Position);
            Assert.AreEqual(2, snapshot.PerMonitor.Count, "Per-monitor entries carry paths only; Windows applies one fitment to the whole desktop.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizePosition_UnknownOrNullToken_ResolvesToFill()
        {
            Assert.AreEqual("fill", WallpaperHelper.NormalizePosition(null));
            Assert.AreEqual("fill", WallpaperHelper.NormalizePosition("nonsense"));
            Assert.AreEqual("fill", WallpaperHelper.NormalizePosition("Fill"), "Tokens are lowercase on the wire; a capitalized value must fall back, not throw.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizePosition_EveryAdvertisedOption_RoundTrips()
        {
            foreach (var position in WallpaperHelper.AllPositions)
                Assert.AreEqual(position, WallpaperHelper.NormalizePosition(position), $"'{position}' is offered in the picker, so it must survive the enum round trip.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowConfig_LivesOnSnapshotSoItIsProfileLevel()
        {
            var profile = new Profile("test")
            {
                WallpaperSettings = new WallpaperSettings
                {
                    SolidColorArgb = 0x00112233,
                    Position = "fit",
                    SlideshowConfig = new SlideshowConfig { Shuffle = true }
                }
            };

            Assert.IsTrue(profile.WallpaperSettings.SlideshowConfig.Shuffle);
            Assert.AreEqual(0x00112233u, profile.WallpaperSettings.SolidColorArgb);
            Assert.AreEqual("fit", profile.WallpaperSettings.Position);
        }

        [TestMethod]
        [DataRow(WallpaperMode.Solid, WallpaperMode.Picture)]
        [DataRow(WallpaperMode.Solid, WallpaperMode.Slideshow)]
        [DataRow(WallpaperMode.Solid, WallpaperMode.Spotlight)]
        [DataRow(WallpaperMode.Picture, WallpaperMode.Solid)]
        [DataRow(WallpaperMode.Picture, WallpaperMode.Slideshow)]
        [DataRow(WallpaperMode.Picture, WallpaperMode.Spotlight)]
        [DataRow(WallpaperMode.Slideshow, WallpaperMode.Solid)]
        [DataRow(WallpaperMode.Slideshow, WallpaperMode.Picture)]
        [DataRow(WallpaperMode.Slideshow, WallpaperMode.Spotlight)]
        [DataRow(WallpaperMode.Spotlight, WallpaperMode.Solid)]
        [DataRow(WallpaperMode.Spotlight, WallpaperMode.Picture)]
        [DataRow(WallpaperMode.Spotlight, WallpaperMode.Slideshow)]
        [TestCategory("Unit")]
        public void TransitionPlan_AllDirectedModeChanges_UseDestinationOwnedSequence(WallpaperMode source, WallpaperMode destination)
        {
            var plan = WallpaperHelper.BuildTransitionPlan(source, destination);

            Assert.AreEqual(source, plan.SourceMode);
            Assert.AreEqual(destination, plan.DestinationMode);
            CollectionAssert.AreEqual(ExpectedSteps(source, destination), new List<WallpaperHelper.WallpaperTransitionStep>(plan.Steps));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TransitionPlan_SlideshowOwnsColorPositionSourceAndOptions()
        {
            var plan = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Slideshow);

            CollectionAssert.Contains(new List<WallpaperHelper.WallpaperTransitionStep>(plan.Steps), WallpaperHelper.WallpaperTransitionStep.SetBackgroundColor);
            CollectionAssert.Contains(new List<WallpaperHelper.WallpaperTransitionStep>(plan.Steps), WallpaperHelper.WallpaperTransitionStep.SetPosition);
            CollectionAssert.Contains(new List<WallpaperHelper.WallpaperTransitionStep>(plan.Steps), WallpaperHelper.WallpaperTransitionStep.SetSlideshowSource);
            CollectionAssert.Contains(new List<WallpaperHelper.WallpaperTransitionStep>(plan.Steps), WallpaperHelper.WallpaperTransitionStep.SetSlideshowOptions);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TransitionPlan_SpotlightNeverPaintsPictureOrSlideshowContent()
        {
            var plan = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Spotlight);
            var steps = new List<WallpaperHelper.WallpaperTransitionStep>(plan.Steps);

            CollectionAssert.DoesNotContain(steps, WallpaperHelper.WallpaperTransitionStep.SetPictureWallpapers);
            CollectionAssert.DoesNotContain(steps, WallpaperHelper.WallpaperTransitionStep.SetSlideshowSource);
            CollectionAssert.DoesNotContain(steps, WallpaperHelper.WallpaperTransitionStep.DisableDesktopBackground);
            CollectionAssert.AreEqual(new[] { WallpaperHelper.WallpaperTransitionStep.ActivateSpotlight }, steps);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_SolidRegistryStateWinsOverStalePicturePaths()
        {
            Assert.AreEqual(WallpaperMode.Solid, WallpaperHelper.ClassifyWallpaperMode(1, slideshowActive: false, hasPicture: true, hasSpotlightProviderPath: false));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_SpotlightRegistryStateDoesNotRequireProviderImagePath()
        {
            Assert.AreEqual(WallpaperMode.Spotlight, WallpaperHelper.ClassifyWallpaperMode(3, slideshowActive: false, hasPicture: false, hasSpotlightProviderPath: false));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_KnownPictureStateIsNotOverriddenBySpotlightLookingPath()
        {
            Assert.AreEqual(WallpaperMode.Picture, WallpaperHelper.ClassifyWallpaperMode(0, slideshowActive: false, hasPicture: true, hasSpotlightProviderPath: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_MissingModeCanUseProviderPathAsSpotlightFallback()
        {
            Assert.AreEqual(WallpaperMode.Spotlight, WallpaperHelper.ClassifyWallpaperMode(null, slideshowActive: false, hasPicture: true, hasSpotlightProviderPath: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_SlideshowRegistryStateWinsWithoutPicturePaths()
        {
            Assert.AreEqual(WallpaperMode.Slideshow, WallpaperHelper.ClassifyWallpaperMode(2, slideshowActive: false, hasPicture: false, hasSpotlightProviderPath: false));
        }

        private static WallpaperHelper.WallpaperTransitionStep[] ExpectedSteps(WallpaperMode source, WallpaperMode destination)
        {
            switch (destination)
            {
                case WallpaperMode.Solid:
                    var solidSteps = new List<WallpaperHelper.WallpaperTransitionStep>
                    {
                        WallpaperHelper.WallpaperTransitionStep.DeactivateSpotlight,
                        WallpaperHelper.WallpaperTransitionStep.SetBackgroundColor,
                    };
                    if (source == WallpaperMode.Slideshow)
                        solidSteps.Add(WallpaperHelper.WallpaperTransitionStep.SupersedeSlideshowOwnership);
                    solidSteps.Add(WallpaperHelper.WallpaperTransitionStep.DisableDesktopBackground);
                    solidSteps.Add(WallpaperHelper.WallpaperTransitionStep.EstablishDestinationMode);
                    solidSteps.Add(WallpaperHelper.WallpaperTransitionStep.NotifySettings);
                    solidSteps.Add(WallpaperHelper.WallpaperTransitionStep.VerifyDestinationMode);
                    return solidSteps.ToArray();
                case WallpaperMode.Picture:
                    return new[]
                    {
                        WallpaperHelper.WallpaperTransitionStep.DeactivateSpotlight,
                        WallpaperHelper.WallpaperTransitionStep.SetBackgroundColor,
                        WallpaperHelper.WallpaperTransitionStep.DisableDesktopBackground,
                        WallpaperHelper.WallpaperTransitionStep.SetPictureWallpapers,
                        WallpaperHelper.WallpaperTransitionStep.SetPosition,
                        WallpaperHelper.WallpaperTransitionStep.EstablishDestinationMode,
                        WallpaperHelper.WallpaperTransitionStep.NotifySettings,
                        WallpaperHelper.WallpaperTransitionStep.VerifyDestinationMode,
                    };
                case WallpaperMode.Slideshow:
                    return new[]
                    {
                        WallpaperHelper.WallpaperTransitionStep.DeactivateSpotlight,
                        WallpaperHelper.WallpaperTransitionStep.SetBackgroundColor,
                        WallpaperHelper.WallpaperTransitionStep.SetPosition,
                        WallpaperHelper.WallpaperTransitionStep.DisableDesktopBackground,
                        WallpaperHelper.WallpaperTransitionStep.SetSlideshowSource,
                        WallpaperHelper.WallpaperTransitionStep.SetSlideshowOptions,
                        WallpaperHelper.WallpaperTransitionStep.EstablishDestinationMode,
                        WallpaperHelper.WallpaperTransitionStep.NotifySettings,
                        WallpaperHelper.WallpaperTransitionStep.VerifyDestinationMode,
                    };
                case WallpaperMode.Spotlight:
                    return new[] { WallpaperHelper.WallpaperTransitionStep.ActivateSpotlight };
                default:
                    return new WallpaperHelper.WallpaperTransitionStep[0];
            }
        }
    }
}
