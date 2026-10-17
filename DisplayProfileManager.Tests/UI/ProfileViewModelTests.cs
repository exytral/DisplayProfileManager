using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.UI.ViewModels;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class ProfileViewModelTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayCountText_MixedSavedDisplays_UsesEnabledOverTotal()
        {
            var profile = new Profile("Test");
            profile.DisplaySettings.Add(new DisplaySettingBuilder().WithName("Enabled").Enabled().Build());
            profile.DisplaySettings.Add(new DisplaySettingBuilder().WithName("Disabled").Enabled(false).Build());

            var viewModel = new ProfileViewModel(profile);

            Assert.AreEqual(1, viewModel.EnabledDisplayCount);
            Assert.AreEqual(2, viewModel.TotalDisplayCount);
            Assert.AreEqual("1/2 displays", viewModel.DisplayCountText);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplayCountText_SingleSavedDisplay_UsesSingularLabel()
        {
            var profile = new Profile("Test");
            profile.DisplaySettings.Add(new DisplaySettingBuilder().Enabled().Build());

            var viewModel = new ProfileViewModel(profile);

            Assert.AreEqual("1/1 display", viewModel.DisplayCountText);
        }
    }
}
