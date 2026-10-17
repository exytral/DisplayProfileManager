using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class WallpaperBackgroundColorOwnershipTests
    {
        [TestMethod]
        [DataRow(WallpaperMode.Solid, true)]
        [DataRow(WallpaperMode.Picture, true)]
        [DataRow(WallpaperMode.Slideshow, true)]
        [DataRow(WallpaperMode.Spotlight, false)]
        [TestCategory("Unit")]
        public void ModeOwnsBackgroundColor_Mode_ReportsSemanticOwnership(WallpaperMode mode, bool expected)
        {
            Assert.AreEqual(expected, WallpaperTransitionPlanner.ModeOwnsBackgroundColor(mode));
        }
    }
}
