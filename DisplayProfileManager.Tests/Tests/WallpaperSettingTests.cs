using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;

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
            var profile = new Profile("test") { WallpaperSettings = new WallpaperSettings { SlideshowConfig = new SlideshowConfig { Shuffle = true } } };

            Assert.IsTrue(profile.WallpaperSettings.SlideshowConfig.Shuffle, "Shuffle is a whole-desktop setting and must not sit on DisplaySetting.");
        }
    }
}