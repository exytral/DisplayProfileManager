using System.Windows.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using KeyConverter = DisplayProfileManager.Helpers.KeyConverter;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class KeyConverterTests
    {
        // ToVirtualKey / ToWpfKey

        [TestMethod]
        [TestCategory("Unit")]
        public void ToVirtualKey_LetterKey_ReturnsExpectedCode()
        {
            Assert.AreEqual(0x41, KeyConverter.ToVirtualKey(Key.A));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToVirtualKey_FunctionKey_ReturnsExpectedCode()
        {
            Assert.AreEqual(0x70, KeyConverter.ToVirtualKey(Key.F1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToVirtualKey_UnmappedKey_ReturnsZero()
        {
            // Key.ImeProcessed has no entry in the lookup table
            Assert.AreEqual(0, KeyConverter.ToVirtualKey(Key.ImeProcessed));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToWpfKey_KnownVirtualKeyCode_ReturnsExpectedKey()
        {
            Assert.AreEqual(Key.A, KeyConverter.ToWpfKey(0x41));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToWpfKey_UnknownVirtualKeyCode_ReturnsNone()
        {
            Assert.AreEqual(Key.None, KeyConverter.ToWpfKey(0xFFFF));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ToVirtualKey_ToWpfKey_RoundTripsForMappedKey()
        {
            var vk = KeyConverter.ToVirtualKey(Key.F5);

            Assert.AreEqual(Key.F5, KeyConverter.ToWpfKey(vk));
        }

        // ConvertModifierKeys / ConvertToModifierKeys

        [TestMethod]
        [TestCategory("Unit")]
        public void ConvertModifierKeys_SingleModifier_SetsExpectedBit()
        {
            Assert.AreEqual(0x0002u, KeyConverter.ConvertModifierKeys(ModifierKeys.Control));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ConvertModifierKeys_CombinedModifiers_SetsAllExpectedBits()
        {
            var combined = ModifierKeys.Control | ModifierKeys.Shift;

            Assert.AreEqual(0x0006u, KeyConverter.ConvertModifierKeys(combined));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ConvertModifierKeys_None_ReturnsZero()
        {
            Assert.AreEqual(0u, KeyConverter.ConvertModifierKeys(ModifierKeys.None));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ConvertToModifierKeys_CombinedBits_ReturnsExpectedModifiers()
        {
            var result = KeyConverter.ConvertToModifierKeys(0x0006u);

            Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Shift, result);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ModifierKeys_RoundTripThroughBothConversions_PreservesValue()
        {
            var original = ModifierKeys.Alt | ModifierKeys.Windows;

            var bits = KeyConverter.ConvertModifierKeys(original);
            var restored = KeyConverter.ConvertToModifierKeys(bits);

            Assert.AreEqual(original, restored);
        }

        // IsModifierKey

        [TestMethod]
        [TestCategory("Unit")]
        public void IsModifierKey_ShiftKey_ReturnsTrue()
        {
            Assert.IsTrue(KeyConverter.IsModifierKey(Key.LeftShift));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsModifierKey_LetterKey_ReturnsFalse()
        {
            Assert.IsFalse(KeyConverter.IsModifierKey(Key.A));
        }
    }
}