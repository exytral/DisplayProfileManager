using DisplayProfileManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class CliParserTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Normalize_StripsLeadingDashesAndSlashes()
        {
            Assert.AreEqual("profile", CliParser.Normalize("--profile"));
            Assert.AreEqual("profile", CliParser.Normalize("-profile"));
            Assert.AreEqual("profile", CliParser.Normalize("/profile"));
            Assert.AreEqual("profile", CliParser.Normalize("profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Normalize_IsCaseInsensitive()
        {
            Assert.AreEqual("profile", CliParser.Normalize("--PROFILE"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Normalize_EmptyInputDoesNotThrow()
        {
            Assert.AreEqual(string.Empty, CliParser.Normalize(null));
            Assert.AreEqual(string.Empty, CliParser.Normalize("   "));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Matches_AcceptsAnyPrefixOfFlag()
        {
            foreach (var prefix in new[] { "p", "pr", "pro", "prof", "profile" })
                Assert.IsTrue(CliParser.Matches(prefix, "profile"), $"'{prefix}' should match profile");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Matches_RejectsNonPrefix()
        {
            Assert.IsFalse(CliParser.Matches("px", "profile"));
            Assert.IsFalse(CliParser.Matches("rofile", "profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Matches_RejectsSomethingLongerThanFlag()
        {
            Assert.IsFalse(CliParser.Matches("profiles", "profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Matches_DoesNotConfuseFlagsSharingNoFirstLetter()
        {
            Assert.IsFalse(CliParser.Matches("t", "profile"));
            Assert.IsTrue(CliParser.Matches("t", "theme"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsRefresh_AcceptsBothSpellingsAndShortForm()
        {
            foreach (var arg in new[] { "ref", "refresh", "rel", "reload", "r" })
                Assert.IsTrue(CliParser.IsRefresh(arg), $"'{arg}' should be refresh");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsRefresh_DoesNotSwallowOtherRFlags()
        {
            Assert.IsFalse(CliParser.IsRefresh("rotate"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsValueFor_TreatsFlagAsNotValue()
        {
            Assert.IsFalse(CliParser.IsValueFor("--tray"));
            Assert.IsFalse(CliParser.IsValueFor(null));
            Assert.IsTrue(CliParser.IsValueFor("Profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsValueFor_AcceptsProfileNameStartingWithDigitOrSpace()
        {
            Assert.IsTrue(CliParser.IsValueFor("0Test Profile"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_EmptyArgsYieldsNoIntent()
        {
            var o = CliParser.Parse([]);

            Assert.IsFalse(o.WantsExistingInstance);
            Assert.AreEqual(ShellAction.None, o.ShellAction);
            Assert.AreEqual(0, o.CommandQueue.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_ProfileQueuesExactlyOneCommand()
        {
            var o = CliParser.Parse(["--profile", "Profile"]);

            Assert.AreEqual("Profile", o.Profile);
            Assert.AreEqual(1, o.CommandQueue.Count, "PROFILE was queued twice at one point");
            Assert.AreEqual("PROFILE:Profile", o.CommandQueue[0]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_ProfileAndHeadlessTogetherStillQueueOnce()
        {
            var o = CliParser.Parse(["--profile", "A", "--headless"]);

            Assert.AreEqual(1, o.CommandQueue.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_ShellActionShortCircuits()
        {
            var o = CliParser.Parse(["--shell", "--profile", "A"]);

            Assert.AreEqual(ShellAction.Register, o.ShellAction);
            Assert.IsNull(o.Profile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_FlagFollowedByFlagDoesNotConsumeIt()
        {
            var o = CliParser.Parse(["--profile", "--tray"]);

            Assert.IsNull(o.Profile);
            Assert.IsTrue(o.StartInTray, "--tray must still be parsed as its own flag");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_ThemeWithoutValueQueuesEmptyTheme()
        {
            var o = CliParser.Parse(["--theme"]);

            Assert.IsTrue(o.IsTheme);
            Assert.AreEqual("THEME:", o.CommandQueue[0]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Parse_DevModeIsNotExistingInstanceRequest()
        {
            var o = CliParser.Parse(["--dev"]);

            Assert.IsTrue(o.DevMode);
            Assert.IsFalse(o.WantsExistingInstance);
        }
    }

    [TestClass]
    public class IpcServerTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void BuildPipeName_CarriesSessionId()
        {
            Assert.AreEqual("DPM_IpcPipe.0", IpcServer.BuildPipeName(0));
            Assert.AreEqual("DPM_IpcPipe.7", IpcServer.BuildPipeName(7));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildPipeName_DiffersBetweenSessions()
        {
            // Two logged-in users sharing a pipe should not let one steer other's instance
            Assert.AreNotEqual(IpcServer.BuildPipeName(1), IpcServer.BuildPipeName(2));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PipeName_UsesSameRuleAsBuilder()
        {
            var expected = IpcServer.BuildPipeName(
                System.Diagnostics.Process.GetCurrentProcess().SessionId);

            Assert.AreEqual(expected, IpcServer.PipeName);
        }
    }
}