using System.Collections.Generic;
using System.Windows.Input;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class QuickSwitchSelectionPolicyTests
    {
        private static List<Profile> Profiles() =>
        [
            new Profile("Profile A") { Id = "a" },
            new Profile("Profile B") { Id = "b" },
            new Profile("Profile C") { Id = "c" }
        ];


        [TestMethod]
        [TestCategory("Unit")]
        public void GetInputAction_EscapeWithEmptyOverlay_Dismisses()
        {
            Assert.AreEqual(QuickSwitchInputAction.Dismiss, QuickSwitchSelectionPolicy.GetInputAction(Key.Escape, 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetInputAction_EnterWithSelection_Applies()
        {
            Assert.AreEqual(QuickSwitchInputAction.Apply, QuickSwitchSelectionPolicy.GetInputAction(Key.Enter, 1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetInputAction_EnterWithEmptyOverlay_DoesNothing()
        {
            Assert.AreEqual(QuickSwitchInputAction.None, QuickSwitchSelectionPolicy.GetInputAction(Key.Enter, 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetInitialIndex_CurrentProfileAvailable_SelectsCurrent()
        {
            Assert.AreEqual(1, QuickSwitchSelectionPolicy.GetInitialIndex(Profiles(), "b"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetInitialIndex_CurrentProfileUnavailable_SelectsFirst()
        {
            Assert.AreEqual(0, QuickSwitchSelectionPolicy.GetInitialIndex(Profiles(), "missing"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetInitialIndex_CurrentProfileUnavailable_PreservesPreviousSelection()
        {
            Assert.AreEqual(2, QuickSwitchSelectionPolicy.GetInitialIndex(Profiles(), "missing", "c"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Move_DownFromLast_WrapsToFirst()
        {
            Assert.AreEqual(0, QuickSwitchSelectionPolicy.Move(2, 3, 1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Move_UpFromFirst_WrapsToLast()
        {
            Assert.AreEqual(2, QuickSwitchSelectionPolicy.Move(0, 3, -1));
        }
    }
}
