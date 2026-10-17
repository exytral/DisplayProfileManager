using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.UI.Presentation;
using DisplayProfileManager.Wallpapers.Interop;
using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;

namespace DisplayProfileManager.Tests.Wallpapers
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
            Assert.AreEqual("fill", WindowsWallpaperPlatform.NormalizePosition(null));
            Assert.AreEqual("fill", WindowsWallpaperPlatform.NormalizePosition("nonsense"));
            Assert.AreEqual("fill", WindowsWallpaperPlatform.NormalizePosition("Fill"), "Tokens are lowercase on the wire; a capitalized value must fall back, not throw.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizePosition_EveryAdvertisedOption_RoundTrips()
        {
            foreach (var position in WallpaperModeFormatter.AllPositions)
            {
                Assert.AreEqual(position, WindowsWallpaperPlatform.NormalizePosition(position), $"'{position}' is offered in the picker, so it must survive the enum round trip.");
            }
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
            var plan = WallpaperTransitionPlanner.BuildTransitionPlan(source, destination);

            Assert.AreEqual(source, plan.SourceMode);
            Assert.AreEqual(destination, plan.DestinationMode);
            CollectionAssert.AreEqual(ExpectedSteps(source, destination), new List<WallpaperTransitionStep>(plan.Steps));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TransitionPlan_SlideshowOwnsColorPositionSourceAndOptions()
        {
            var plan = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Slideshow);

            CollectionAssert.Contains(new List<WallpaperTransitionStep>(plan.Steps), WallpaperTransitionStep.SetBackgroundColor);
            CollectionAssert.Contains(new List<WallpaperTransitionStep>(plan.Steps), WallpaperTransitionStep.SetPosition);
            CollectionAssert.Contains(new List<WallpaperTransitionStep>(plan.Steps), WallpaperTransitionStep.SetSlideshowSource);
            CollectionAssert.Contains(new List<WallpaperTransitionStep>(plan.Steps), WallpaperTransitionStep.SetSlideshowOptions);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TransitionPlan_SpotlightNeverPaintsPictureOrSlideshowContent()
        {
            var plan = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Spotlight);
            var steps = new List<WallpaperTransitionStep>(plan.Steps);

            CollectionAssert.DoesNotContain(steps, WallpaperTransitionStep.SetPictureWallpapers);
            CollectionAssert.DoesNotContain(steps, WallpaperTransitionStep.SetSlideshowSource);
            CollectionAssert.DoesNotContain(steps, WallpaperTransitionStep.DisableDesktopBackground);
            CollectionAssert.AreEqual(new[] { WallpaperTransitionStep.ActivateSpotlight }, steps);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_SolidRegistryStateWinsOverStalePicturePaths()
        {
            Assert.AreEqual(WallpaperMode.Solid, WindowsWallpaperPlatform.ClassifyWallpaperMode(1, slideshowActive: false, hasPicture: true, hasSpotlightProviderPath: false));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_SpotlightRegistryStateDoesNotRequireProviderImagePath()
        {
            Assert.AreEqual(WallpaperMode.Spotlight, WindowsWallpaperPlatform.ClassifyWallpaperMode(3, slideshowActive: false, hasPicture: false, hasSpotlightProviderPath: false));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_KnownPictureStateIsNotOverriddenBySpotlightLookingPath()
        {
            Assert.AreEqual(WallpaperMode.Picture, WindowsWallpaperPlatform.ClassifyWallpaperMode(0, slideshowActive: false, hasPicture: true, hasSpotlightProviderPath: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_MissingModeCanUseProviderPathAsSpotlightFallback()
        {
            Assert.AreEqual(WallpaperMode.Spotlight, WindowsWallpaperPlatform.ClassifyWallpaperMode(null, slideshowActive: false, hasPicture: true, hasSpotlightProviderPath: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyWallpaperMode_SlideshowRegistryStateWinsWithoutPicturePaths()
        {
            Assert.AreEqual(WallpaperMode.Slideshow, WindowsWallpaperPlatform.ClassifyWallpaperMode(2, slideshowActive: false, hasPicture: false, hasSpotlightProviderPath: false));
        }

        private static WallpaperTransitionStep[] ExpectedSteps(WallpaperMode source, WallpaperMode destination)
        {
            switch (destination)
            {
                case WallpaperMode.Solid:
                    var solidSteps = new List<WallpaperTransitionStep>
                    {
                        WallpaperTransitionStep.DeactivateSpotlight,
                        WallpaperTransitionStep.SetBackgroundColor,
                    };
                    if (source == WallpaperMode.Slideshow)
                        solidSteps.Add(WallpaperTransitionStep.SupersedeSlideshowOwnership);
                    solidSteps.Add(WallpaperTransitionStep.DisableDesktopBackground);
                    solidSteps.Add(WallpaperTransitionStep.EstablishDestinationMode);
                    solidSteps.Add(WallpaperTransitionStep.NotifySettings);
                    solidSteps.Add(WallpaperTransitionStep.VerifyDestinationMode);
                    return [.. solidSteps];
                case WallpaperMode.Picture:
                    return
                    [
                        WallpaperTransitionStep.DeactivateSpotlight,
                        WallpaperTransitionStep.SetBackgroundColor,
                        WallpaperTransitionStep.DisableDesktopBackground,
                        WallpaperTransitionStep.SetPictureWallpapers,
                        WallpaperTransitionStep.SetPosition,
                        WallpaperTransitionStep.EstablishDestinationMode,
                        WallpaperTransitionStep.NotifySettings,
                        WallpaperTransitionStep.VerifyDestinationMode,
                    ];
                case WallpaperMode.Slideshow:
                    return
                    [
                        WallpaperTransitionStep.DeactivateSpotlight,
                        WallpaperTransitionStep.SetBackgroundColor,
                        WallpaperTransitionStep.SetPosition,
                        WallpaperTransitionStep.DisableDesktopBackground,
                        WallpaperTransitionStep.SetSlideshowSource,
                        WallpaperTransitionStep.SetSlideshowOptions,
                        WallpaperTransitionStep.EstablishDestinationMode,
                        WallpaperTransitionStep.NotifySettings,
                        WallpaperTransitionStep.VerifyDestinationMode,
                    ];
                case WallpaperMode.Spotlight:
                    return [WallpaperTransitionStep.ActivateSpotlight];
                default:
                    return [];
            }
        }
    }
}
