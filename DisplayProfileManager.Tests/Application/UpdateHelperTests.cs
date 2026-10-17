using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

using DisplayProfileManager.Integration;

namespace DisplayProfileManager.Tests.Application
{
    [TestClass]
    public class GitHubUpdateCheckerTests
    {
        // ParseVersion

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_ThreePartVersion_ParsesCorrectly()
        {
            var version = GitHubUpdateChecker.ParseVersion("1.2.3");

            Assert.AreEqual(new Version(1, 2, 3, 0), version);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_VPrefixedVersion_ParsesCorrectly()
        {
            var version = GitHubUpdateChecker.ParseVersion("v1.2.3");

            Assert.AreEqual(new Version(1, 2, 3, 0), version);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_TwoPartVersion_NormalizesToFourPart()
        {
            var version = GitHubUpdateChecker.ParseVersion("1.2");

            Assert.AreEqual(new Version(1, 2, 0, 0), version);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_FourPartVersion_ParsesCorrectly()
        {
            var version = GitHubUpdateChecker.ParseVersion("1.2.3.4");

            Assert.AreEqual(new Version(1, 2, 3, 4), version);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_EmptyOrWhitespace_ReturnsNull()
        {
            Assert.IsNull(GitHubUpdateChecker.ParseVersion(string.Empty));
            Assert.IsNull(GitHubUpdateChecker.ParseVersion("   "));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_InvalidText_ReturnsNull()
        {
            var version = GitHubUpdateChecker.ParseVersion("not-a-version");

            Assert.IsNull(version);
        }

        // IsPastCooldown

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_WellPastCooldown_ReturnsTrue()
        {
            var publishedAt = new JValue(DateTimeOffset.UtcNow.AddDays(-30).ToString("o"));

            Assert.IsTrue(GitHubUpdateChecker.IsPastCooldown(publishedAt));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_ExactlyThreeDaysOld_ReturnsTrue()
        {
            var publishedAt = new JValue(DateTimeOffset.UtcNow.AddDays(-3).ToString("o"));

            Assert.IsTrue(GitHubUpdateChecker.IsPastCooldown(publishedAt), "A release exactly three days old has cleared the cooldown window (boundary is inclusive).");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_JustUnderThreeDays_ReturnsFalse()
        {
            var publishedAt = new JValue(DateTimeOffset.UtcNow.AddDays(-3).AddHours(1).ToString("o"));

            Assert.IsFalse(GitHubUpdateChecker.IsPastCooldown(publishedAt), "A release just under three days old must still be inside the cooldown window.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_MissingPublishedAt_ReturnsTrue()
        {
            Assert.IsTrue(GitHubUpdateChecker.IsPastCooldown(null), "A missing published_at value is treated as old enough to advertise.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_MalformedPublishedAt_ReturnsTrue()
        {
            var publishedAt = new JValue("not-a-date");

            Assert.IsTrue(GitHubUpdateChecker.IsPastCooldown(publishedAt), "An unparseable published_at value is treated as old enough to advertise.");
        }

        // NormalizeVersionTag

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizeVersionTag_NoPrefix_ReturnsUnchanged()
        {
            Assert.AreEqual("1.2.3", GitHubUpdateChecker.NormalizeVersionTag("1.2.3"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizeVersionTag_LowercaseVPrefix_StripsPrefix()
        {
            Assert.AreEqual("1.2.3", GitHubUpdateChecker.NormalizeVersionTag("v1.2.3"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizeVersionTag_UppercaseVPrefix_StripsPrefix()
        {
            Assert.AreEqual("1.2.3", GitHubUpdateChecker.NormalizeVersionTag("V1.2.3"));
        }
    }
}
