using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using DisplayProfileManager.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class UpdateHelperTests
    {
        // ParseVersion

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_ThreePartVersion_ParsesCorrectly()
        {
            var version = UpdateHelper.ParseVersion("1.2.3");

            Assert.AreEqual(new Version(1, 2, 3, 0), version);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_VPrefixedVersion_ParsesCorrectly()
        {
            var version = UpdateHelper.ParseVersion("v1.2.3");

            Assert.AreEqual(new Version(1, 2, 3, 0), version);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_TwoPartVersion_NormalizesToFourPart()
        {
            var version = UpdateHelper.ParseVersion("1.2");

            Assert.AreEqual(new Version(1, 2, 0, 0), version);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_FourPartVersion_ParsesCorrectly()
        {
            var version = UpdateHelper.ParseVersion("1.2.3.4");

            Assert.AreEqual(new Version(1, 2, 3, 4), version);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_EmptyOrWhitespace_ReturnsNull()
        {
            Assert.IsNull(UpdateHelper.ParseVersion(string.Empty));
            Assert.IsNull(UpdateHelper.ParseVersion("   "));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseVersion_InvalidText_ReturnsNull()
        {
            var version = UpdateHelper.ParseVersion("not-a-version");

            Assert.IsNull(version);
        }

        // Version comparison (as used to decide UpdateAvailable)

        [TestMethod]
        [TestCategory("Unit")]
        public void Version_NewerRelease_ComparesGreaterThanCurrent()
        {
            var latest = UpdateHelper.ParseVersion("1.2.3.4");
            var current = UpdateHelper.ParseVersion("1.2.3");

            Assert.IsTrue(latest > current);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Version_SameRelease_IsNotNewerThanCurrent()
        {
            var latest = UpdateHelper.ParseVersion("1.2.3");
            var current = UpdateHelper.ParseVersion("1.2.3");

            Assert.IsFalse(latest > current);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Version_OlderRelease_IsNotNewerThanCurrent()
        {
            var latest = UpdateHelper.ParseVersion("1.2.3");
            var current = UpdateHelper.ParseVersion("1.2.3.4");

            Assert.IsFalse(latest > current);
        }

        // IsPastCooldown

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_WellPastSevenDays_ReturnsTrue()
        {
            var publishedAt = new JValue(DateTimeOffset.UtcNow.AddDays(-30).ToString("o"));

            Assert.IsTrue(UpdateHelper.IsPastCooldown(publishedAt));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_ExactlySevenDaysOld_ReturnsTrue()
        {
            var publishedAt = new JValue(DateTimeOffset.UtcNow.AddDays(-7).ToString("o"));

            Assert.IsTrue(UpdateHelper.IsPastCooldown(publishedAt), "A release exactly seven days old has cleared the cooldown window (boundary is inclusive).");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_JustUnderSevenDays_ReturnsFalse()
        {
            var publishedAt = new JValue(DateTimeOffset.UtcNow.AddDays(-7).AddHours(1).ToString("o"));

            Assert.IsFalse(UpdateHelper.IsPastCooldown(publishedAt), "A release just under seven days old must still be inside the cooldown window.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_MissingPublishedAt_ReturnsTrue()
        {
            Assert.IsTrue(UpdateHelper.IsPastCooldown(null), "A missing published_at value is treated as old enough to advertise.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPastCooldown_MalformedPublishedAt_ReturnsTrue()
        {
            var publishedAt = new JValue("not-a-date");

            Assert.IsTrue(UpdateHelper.IsPastCooldown(publishedAt), "An unparseable published_at value is treated as old enough to advertise.");
        }

        // NormalizeVersionTag (LatestVersion formatting)

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizeVersionTag_NoPrefix_ReturnsUnchanged()
        {
            Assert.AreEqual("1.2.3", UpdateHelper.NormalizeVersionTag("1.2.3"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizeVersionTag_LowercaseVPrefix_StripsPrefix()
        {
            Assert.AreEqual("1.2.3", UpdateHelper.NormalizeVersionTag("v1.2.3"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizeVersionTag_UppercaseVPrefix_StripsPrefix()
        {
            Assert.AreEqual("1.2.3", UpdateHelper.NormalizeVersionTag("V1.2.3"));
        }
    }
}