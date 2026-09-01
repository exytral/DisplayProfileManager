using System.Collections.Generic;
using System.Linq;
using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class NaturalStringComparerTests
    {
        private static List<string> Sort(params string[] input) =>
            input.OrderBy(x => x, NaturalStringComparer.Instance).ToList();

        [TestMethod]
        [TestCategory("Unit")]
        public void DoubleDigitsSortAfterSingleDigits()
        {
            // An ordinal sort puts (10) between (1) and (2)
            var sorted = Sort("Profile - Copy (10)", "Profile - Copy (2)", "Profile - Copy (1)");

            CollectionAssert.AreEqual(new[] { "Profile - Copy (1)", "Profile - Copy (2)", "Profile - Copy (10)" }, sorted);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PlainNamesStillSortAlphabetically()
        {
            CollectionAssert.AreEqual(new[] { "Alpha", "Beta", "Gamma" }, Sort("Gamma", "Alpha", "Beta"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CopyPrecedesItsNumberedSiblings()
        {
            var sorted = Sort("Profile - Copy (2)", "Profile - Copy", "Profile - Copy (1)");

            Assert.AreEqual("Profile - Copy", sorted[0]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EmbeddedNumbersCompareNumericallyNotTextually()
        {
            CollectionAssert.AreEqual(new[] { "Display 2", "Display 10" }, Sort("Display 10", "Display 2"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NullsSortBeforeValuesAndDoNotThrow()
        {
            var c = NaturalStringComparer.Instance;

            Assert.AreEqual(0, c.Compare(null, null));
            Assert.IsTrue(c.Compare(null, "Profile") < 0);
            Assert.IsTrue(c.Compare("Profile", null) > 0);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CaseDoesNotSplitOtherwiseAdjacentNames()
        {
            var sorted = Sort("beta", "Alpha", "BETA", "alpha");

            Assert.IsTrue(sorted[0].Equals("alpha", System.StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(sorted[1].Equals("alpha", System.StringComparison.OrdinalIgnoreCase));
        }
    }
}