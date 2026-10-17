using System.Linq;
using System.Windows.Input;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class QuickSwitchHotkeyPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ConfiguredChord_ProjectsModifiersAndKeyInOrder()
        {
            var hotkey = new HotkeyConfig(
                Key.P,
                ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows,
                true);

            var keycaps = QuickSwitchHotkeyPresentation.Build(hotkey);

            CollectionAssert.AreEqual(
                new[] { "Ctrl", "Alt", "Windows", "P" },
                keycaps.Select(keycap => keycap.Label).ToArray());
            Assert.IsTrue(keycaps.Single(keycap => keycap.Label == "Windows").IsWindowsModifier);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NoConfiguredKey_ProjectsNoKeycaps()
        {
            Assert.AreEqual(0, QuickSwitchHotkeyPresentation.Build(new HotkeyConfig()).Count);
        }
    }
}
