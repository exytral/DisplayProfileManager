using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileManagedIdentityTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void TryValidateManagedProfileIdentity_PathLikeId_IsRejectedBeforePathUse()
        {
            var profile = ValidProfile("..\\escape");
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool result = ProfileStore.TryValidateManagedProfileIdentity(ManagedPath(Guid.NewGuid()), profile, accepted, out _);

            Assert.IsFalse(result);
            Assert.AreEqual(0, accepted.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryValidateManagedProfileIdentity_RootedId_IsRejected()
        {
            var profile = ValidProfile(@"C:\outside\profile");
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Assert.IsFalse(ProfileStore.TryValidateManagedProfileIdentity(ManagedPath(Guid.NewGuid()), profile, accepted, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryValidateManagedProfileIdentity_InvalidGuid_IsRejected()
        {
            var profile = ValidProfile("not-a-guid");
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Assert.IsFalse(ProfileStore.TryValidateManagedProfileIdentity(Path.Combine("Profiles", "not-a-guid.dpm"), profile, accepted, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryValidateManagedProfileIdentity_MismatchedFilename_IsRejected()
        {
            Guid profileId = Guid.NewGuid();
            var profile = ValidProfile(profileId.ToString("D"));
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Assert.IsFalse(ProfileStore.TryValidateManagedProfileIdentity(ManagedPath(Guid.NewGuid()), profile, accepted, out _));
            Assert.AreEqual(profileId.ToString("D"), profile.Id);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryValidateManagedProfileIdentity_SameGuidDifferentCase_NormalizesToCanonicalIdentity()
        {
            Guid id = Guid.Parse("ABCDEF01-2345-4678-9ABC-DEF012345678");
            var profile = ValidProfile(id.ToString("D").ToUpperInvariant());
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine("Profiles", id.ToString("D").ToUpperInvariant() + ".dpm");

            bool result = ProfileStore.TryValidateManagedProfileIdentity(path, profile, accepted, out _);

            Assert.IsTrue(result);
            Assert.AreEqual(id.ToString("D"), profile.Id);
            Assert.IsTrue(accepted.Contains(id.ToString("D")));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryValidateManagedProfileIdentity_DuplicateCanonicalId_IsRejected()
        {
            Guid id = Guid.NewGuid();
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var first = ValidProfile(id.ToString("D"));
            var second = ValidProfile(id.ToString("D").ToUpperInvariant());

            Assert.IsTrue(ProfileStore.TryValidateManagedProfileIdentity(ManagedPath(id), first, accepted, out _));
            Assert.IsFalse(ProfileStore.TryValidateManagedProfileIdentity(ManagedPath(id), second, accepted, out string reason));
            StringAssert.Contains(reason, "duplicate");
            Assert.AreEqual(1, accepted.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryValidateManagedProfileIdentity_NonCanonicalFilenameForm_IsRejected()
        {
            Guid id = Guid.NewGuid();
            var profile = ValidProfile(id.ToString("D"));
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine("Profiles", "{" + id.ToString("D") + "}.dpm");

            Assert.IsFalse(ProfileStore.TryValidateManagedProfileIdentity(path, profile, accepted, out _));
        }

        private static Profile ValidProfile(string id) => new()
        {
            Id = id,
            Name = "Managed",
            DisplaySettings = []
        };

        private static string ManagedPath(Guid id) => Path.Combine("Profiles", id.ToString("D") + ".dpm");
    }
}
