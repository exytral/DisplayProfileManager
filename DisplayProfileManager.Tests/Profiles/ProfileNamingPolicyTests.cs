using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Policy;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileNamingPolicyTests
    {
        [TestMethod]
        [Timeout(5000)]
        public void SaturatedMarkerChainTerminatesWithDistinctBoundedName()
        {
            var saturated = string.Concat(Enumerable.Repeat(" (123)", 10));
            var result = ProfileNamingPolicy.GetUniqueProfileName([new Profile(saturated)], saturated);
            Assert.AreNotEqual(saturated, result);
            Assert.IsTrue(result.Length <= 60);
            Assert.AreEqual("… (1)", result);
            Assert.AreNotEqual(saturated, ProfileNamingPolicy.GetDuplicateProfileName([new Profile(saturated)], saturated));
        }

        [TestMethod]
        [Timeout(5000)]
        public void RepeatedCopyChainsAndCaseInsensitiveCollisionsProgress()
        {
            var chain = string.Concat(Enumerable.Repeat(" - Copy", 12));
            var profiles = new List<Profile> { new(chain), new("… (1)") };
            var result = ProfileNamingPolicy.GetUniqueProfileName(profiles, chain);
            Assert.AreNotEqual(chain, result);
            Assert.IsTrue(result.Length <= 60);
            var ordinary = ProfileNamingPolicy.GetDuplicateProfileName(
                [new Profile("Desk"), new Profile("DESK - COPY")], "Desk");
            Assert.AreEqual("Desk - Copy (1)", ordinary);
        }

        [TestMethod]
        [Timeout(5000)]
        public void AstralCharactersAreNotSplitAtCompactBoundary()
        {
            var stem = new string('A', 56) + "😀" + "ZZ";
            var candidate = ProfileNamingPolicy.GetUniqueProfileName([new Profile(stem)], stem);
            Assert.IsTrue(candidate.Length <= 60);
            for (int i = 0; i < candidate.Length; i++)
            {
                if (char.IsHighSurrogate(candidate[i]))
                {
                    Assert.IsTrue(i + 1 < candidate.Length && char.IsLowSurrogate(candidate[i + 1]));
                    i++;
                }
            }
        }

        [TestMethod]
        [Timeout(5000)]
        public void NearLimitAndOccupiedPoolsRemainFinite()
        {
            foreach (var count in new[] { 59, 60, 61 })
            {
                var name = new string('B', count);
                var taken = new List<Profile> { new(name) };
                var first = ProfileNamingPolicy.GetUniqueProfileName(taken, name);
                Assert.IsTrue(first.Length <= 60);
                taken.Add(new Profile(first));
                var second = ProfileNamingPolicy.GetUniqueProfileName(taken, name);
                Assert.AreNotEqual(first, second);
                Assert.IsTrue(second.Length <= 60);
            }
        }

        [TestMethod]
        [Timeout(5000)]
        public void ActualAstralCutoffNeverSplitsSurrogatePair()
        {
            var stem = new string('A', 54) + "😀" + new string('Z', 4);
            var result = ProfileNamingPolicy.GetUniqueProfileName([new Profile(stem)], stem);
            Assert.IsTrue(result.Length <= 60);
            for (var i = 0; i < result.Length; i++)
            {
                if (char.IsHighSurrogate(result[i]))
                {
                    Assert.IsTrue(i + 1 < result.Length && char.IsLowSurrogate(result[i + 1]));
                    i++;
                }
                else
                {
                    Assert.IsFalse(char.IsLowSurrogate(result[i]));
                }
            }
        }

        [TestMethod]
        [Timeout(5000)]
        public void RepeatedTruncatedAliasUsesCompactFreeCandidate()
        {
            var stem = string.Concat(Enumerable.Repeat(" (123)", 9)) + " (12)";
            var occupiedAlias = stem + " ";
            var result = ProfileNamingPolicy.GetUniqueProfileName(
                [new Profile(stem), new Profile(occupiedAlias)], stem);
            Assert.AreEqual("… (1)", result);
            Assert.IsTrue(result.Length <= 60);
        }

        [TestMethod]
        [Timeout(5000)]
        public void FullyOccupiedTenThousandCandidatesFailWithFiniteException()
        {
            var profiles = Enumerable.Range(1, 10000)
                .Select(n => new Profile($"Desk ({n})")).ToList();
            profiles.Add(new Profile("Desk"));
            Assert.ThrowsExactly<InvalidOperationException>(
                () => ProfileNamingPolicy.GetUniqueProfileName(profiles, "Desk"));
        }

        [TestMethod]
        [Timeout(5000)]
        public void OnePassEnumerableIsNotReEnumerated()
        {
            var reads = 0;
            IEnumerable<Profile> Source()
            {
                reads++;
                if (reads != 1)
                {
                    throw new InvalidOperationException("Repeated enumeration");
                }
                yield return new Profile("Desk");
                yield return new Profile("Desk (1)");
            }
            Assert.AreEqual("Desk (2)", ProfileNamingPolicy.GetUniqueProfileName(Source(), "Desk"));
            Assert.AreEqual(1, reads);
        }

        [TestMethod]
        [Timeout(5000)]
        public void GiantMarkerChainAstralDuplicatePreservesCopyAndUtf16()
        {
            var name = new string('G', 59) + "😀" +
                string.Concat(Enumerable.Repeat(" (123)", 9));
            var result = ProfileNamingPolicy.GetDuplicateProfileName([new Profile(name)], name);
            Assert.IsTrue(result.Length <= 60);
            Assert.IsTrue(result.EndsWith(" - Copy", StringComparison.Ordinal));
            Assert.AreNotEqual(name, result);
            AssertWellFormed(result);
            AssertWellFormed(ProfileNamingPolicy.Compose(name, " - Copy"));
        }

        [TestMethod]
        [Timeout(5000)]
        public void AllMarkerDuplicateRetainsCopySuffix()
        {
            var name = string.Concat(Enumerable.Repeat(" (123)", 9)) + " (12)";
            var result = ProfileNamingPolicy.GetDuplicateProfileName([new Profile(name)], name);
            Assert.IsTrue(result.Length <= 60);
            Assert.IsTrue(result.EndsWith(" - Copy", StringComparison.Ordinal));
            Assert.AreNotEqual(name, result);
        }

        [TestMethod]
        [Timeout(5000)]
        public void GiantMarkerCopyCollisionRetainsNumberedCopyConvention()
        {
            var name = new string('G', 59) + "😀" +
                string.Concat(Enumerable.Repeat(" (123)", 9));
            var first = ProfileNamingPolicy.Compose(name, " - Copy");
            var result = ProfileNamingPolicy.GetDuplicateProfileName(
                [new Profile(name), new Profile(first)], name);
            Assert.IsTrue(result.Length <= 60);
            Assert.IsTrue(result.EndsWith(" (1)", StringComparison.Ordinal));
            AssertWellFormed(result);
        }

        private static void AssertWellFormed(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]))
                {
                    Assert.IsTrue(i + 1 < value.Length &&
                        char.IsLowSurrogate(value[i + 1]));
                    i++;
                }
                else
                {
                    Assert.IsFalse(char.IsLowSurrogate(value[i]));
                }
            }
        }

    }
}
