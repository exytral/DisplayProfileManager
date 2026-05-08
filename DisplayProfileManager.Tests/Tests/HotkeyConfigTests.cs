using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Input;
using DisplayProfileManager.Core;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class HotkeyConfigTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultConstructor_KeyIsNone()
        {
            var hk = new HotkeyConfig();

            Assert.AreEqual(Key.None, hk.Key);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultConstructor_ModifiersAreNone()
        {
            var hk = new HotkeyConfig();

            Assert.AreEqual(ModifierKeys.None, hk.ModifierKeys);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultConstructor_IsEnabledIsFalse()
        {
            var hk = new HotkeyConfig();

            Assert.IsFalse(hk.IsEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParameterizedConstructor_SetsAllFields()
        {
            var hk = new HotkeyConfig(Key.F5, ModifierKeys.Control | ModifierKeys.Alt, isEnabled: true);

            Assert.AreEqual(Key.F5, hk.Key);
            Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Alt, hk.ModifierKeys);
            Assert.IsTrue(hk.IsEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsValid_WhenKeyIsNone_ReturnsFalse()
        {
            Assert.IsFalse(new HotkeyConfig(Key.None, ModifierKeys.Control).IsValid());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsValid_WhenKeyIsModifierKey_ReturnsFalse()
        {
            Assert.IsFalse(new HotkeyConfig(Key.LeftCtrl, ModifierKeys.None).IsValid());
            Assert.IsFalse(new HotkeyConfig(Key.RightCtrl, ModifierKeys.None).IsValid());
            Assert.IsFalse(new HotkeyConfig(Key.LeftAlt, ModifierKeys.None).IsValid());
            Assert.IsFalse(new HotkeyConfig(Key.RightAlt, ModifierKeys.None).IsValid());
            Assert.IsFalse(new HotkeyConfig(Key.LeftShift, ModifierKeys.None).IsValid());
            Assert.IsFalse(new HotkeyConfig(Key.RightShift, ModifierKeys.None).IsValid());
            Assert.IsFalse(new HotkeyConfig(Key.LWin, ModifierKeys.None).IsValid());
            Assert.IsFalse(new HotkeyConfig(Key.RWin, ModifierKeys.None).IsValid());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsValid_WhenKeyIsRegularKey_ReturnsTrue()
        {
            Assert.IsTrue(new HotkeyConfig(Key.F1, ModifierKeys.Control).IsValid());
            Assert.IsTrue(new HotkeyConfig(Key.A, ModifierKeys.Alt).IsValid());
            Assert.IsTrue(new HotkeyConfig(Key.D1, ModifierKeys.None).IsValid());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Equals_SameKeyAndModifiers_ReturnsTrue()
        {
            var a = new HotkeyConfig(Key.F5, ModifierKeys.Control);
            var b = new HotkeyConfig(Key.F5, ModifierKeys.Control);

            Assert.IsTrue(a.Equals(b));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Equals_DifferentKey_ReturnsFalse()
        {
            var a = new HotkeyConfig(Key.F5, ModifierKeys.Control);
            var b = new HotkeyConfig(Key.F6, ModifierKeys.Control);

            Assert.IsFalse(a.Equals(b));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Equals_DifferentModifiers_ReturnsFalse()
        {
            var a = new HotkeyConfig(Key.F5, ModifierKeys.Control);
            var b = new HotkeyConfig(Key.F5, ModifierKeys.Alt);

            Assert.IsFalse(a.Equals(b));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Equals_Null_ReturnsFalse()
        {
            var a = new HotkeyConfig(Key.F5, ModifierKeys.Control);

            Assert.IsFalse(a.Equals(null));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Equals_IsEnabledDifference_DoesNotAffectEquality()
        {
            var enabled = new HotkeyConfig(Key.F5, ModifierKeys.Control, isEnabled: true);
            var disabled = new HotkeyConfig(Key.F5, ModifierKeys.Control, isEnabled: false);

            Assert.IsTrue(enabled.Equals(disabled),
                "Equality is based on Key+Modifiers only; IsEnabled must not affect it.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetHashCode_SameKeyAndModifiers_ProducesSameHash()
        {
            var a = new HotkeyConfig(Key.F5, ModifierKeys.Control);
            var b = new HotkeyConfig(Key.F5, ModifierKeys.Control);

            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Clone_ProducesEqualButDistinctObject()
        {
            var original = new HotkeyConfig(Key.F1, ModifierKeys.Alt, isEnabled: true);

            var clone = original.Clone();

            Assert.IsTrue(original.Equals(clone), "Clone must equal original by Key+Modifiers.");
            Assert.IsFalse(ReferenceEquals(original, clone), "Clone must be a distinct object.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Clone_CopiesIsEnabled()
        {
            var original = new HotkeyConfig(Key.F2, ModifierKeys.None, isEnabled: true);

            var clone = original.Clone();

            Assert.AreEqual(original.IsEnabled, clone.IsEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Clone_MutatingClone_DoesNotAffectOriginal()
        {
            var original = new HotkeyConfig(Key.F3, ModifierKeys.Control, isEnabled: true);
            var clone = original.Clone();
            clone.Key = Key.None;
            clone.IsEnabled = false;

            Assert.AreEqual(Key.F3, original.Key, "Mutating clone must not change original Key.");
            Assert.IsTrue(original.IsEnabled, "Mutating clone must not change original IsEnabled.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_WhenKeyIsNone_ReturnsEmpty()
        {
            var hk = new HotkeyConfig();

            Assert.AreEqual(string.Empty, hk.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_CtrlAltF5_FormatsCorrectly()
        {
            var hk = new HotkeyConfig(Key.F5, ModifierKeys.Control | ModifierKeys.Alt);
            var result = hk.ToString();

            StringAssert.Contains(result, "Ctrl");
            StringAssert.Contains(result, "Alt");
            StringAssert.Contains(result, "F5");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_DigitKey_DoesNotShowDPrefix()
        {
            var hk = new HotkeyConfig(Key.D1, ModifierKeys.Control);
            var result = hk.ToString();

            StringAssert.Contains(result, "1");
            Assert.IsFalse(result.Contains("D1"), "Digit keys must not show the 'D' prefix.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_OemPlusKey_ShowsPlusSign()
        {
            StringAssert.Contains(new HotkeyConfig(Key.OemPlus, ModifierKeys.Control).ToString(), "+");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_OemMinusKey_ShowsMinusSign()
        {
            StringAssert.Contains(new HotkeyConfig(Key.OemMinus, ModifierKeys.Control).ToString(), "-");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_WinModifier_ShowsWin()
        {
            StringAssert.Contains(new HotkeyConfig(Key.F1, ModifierKeys.Windows).ToString(), "Win");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToString_ShiftModifier_ShowsShift()
        {
            StringAssert.Contains(new HotkeyConfig(Key.F1, ModifierKeys.Shift).ToString(), "Shift");
        }
    }
}