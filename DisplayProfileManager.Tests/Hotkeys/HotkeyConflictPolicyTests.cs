using System.Windows.Input;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Hotkeys;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.Tests.Hotkeys
{
    [TestClass]
    public class HotkeyConflictPolicyTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void UsesSameChord_DisabledConfiguredHotkeys_StillConflict()
        {
            var first = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt) { IsEnabled = false };
            var second = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt) { IsEnabled = true };

            Assert.IsTrue(HotkeyConflictPolicy.UsesSameChord(first, second));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FindProfileConflict_ConfiguredDisabledProfile_IsReturned()
        {
            var profile = new Profile("Desk")
            {
                HotkeyConfig = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt) { IsEnabled = false }
            };

            var conflict = HotkeyConflictPolicy.FindProfileConflict(
                [profile],
                new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt));

            Assert.AreSame(profile, conflict);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FindProfileConflict_ExcludedProfile_IgnoresItsOwnChord()
        {
            var profile = new Profile("Desk")
            {
                Id = "profile-a",
                HotkeyConfig = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt)
            };

            var conflict = HotkeyConflictPolicy.FindProfileConflict(
                [profile],
                profile.HotkeyConfig,
                profile.Id);

            Assert.IsNull(conflict);
        }
    }
}
