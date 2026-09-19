using DisplayProfileManager.Core;
using DisplayProfileManager.UI.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class HotkeyEditorPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void HasAssignedHotkey_NullConfig_ReturnsFalse()
        {
            Assert.IsFalse(HotkeyEditorControl.HasAssignedHotkey(null));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasAssignedHotkey_UnassignedConfig_ReturnsFalse()
        {
            Assert.IsFalse(HotkeyEditorControl.HasAssignedHotkey(new HotkeyConfig()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasAssignedHotkey_AssignedConfig_ReturnsTrue()
        {
            var config = new HotkeyConfig
            {
                Key = System.Windows.Input.Key.A
            };

            Assert.IsTrue(HotkeyEditorControl.HasAssignedHotkey(config));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasAssignedHotkey_ClearedConfig_ReturnsFalse()
        {
            var config = new HotkeyConfig
            {
                Key = System.Windows.Input.Key.None,
                IsEnabled = true
            };

            Assert.IsFalse(HotkeyEditorControl.HasAssignedHotkey(config));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Presentation_Unassigned_DisablesActions()
        {
            var state = DisplayProfileManager.UI.Windows.ProfileEditWindow.ResolveHotkeyPresentation(false, true);

            Assert.IsFalse(state.EnableChecked);
            Assert.IsFalse(state.EnableInteractive);
            Assert.IsFalse(state.ClearEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Presentation_AssignedEnabled_KeepsActionsAvailable()
        {
            var state = DisplayProfileManager.UI.Windows.ProfileEditWindow.ResolveHotkeyPresentation(true, true);

            Assert.IsTrue(state.EnableChecked);
            Assert.IsTrue(state.EnableInteractive);
            Assert.IsTrue(state.ClearEnabled);
        }
    }
}
