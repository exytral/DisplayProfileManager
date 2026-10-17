using DisplayProfileManager.Integration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.Integration
{
    [TestClass]
    public class ShellExtensionRegistrarSessionTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        [DataRow(4, "S-1-5-21-100", false, 4, "S-1-5-21-100", false, true)]
        [DataRow(5, "S-1-5-21-100", false, 4, "S-1-5-21-100", false, false)]
        [DataRow(4, "S-1-5-21-200", false, 4, "S-1-5-21-100", false, false)]
        [DataRow(4, "S-1-5-21-100", true, 4, "S-1-5-21-100", false, false)]
        [DataRow(4, "S-1-5-21-100", false, 4, "S-1-5-21-100", true, false)]
        public void EligibleExplorerRequiresExactSessionUserAndElevation(
            int candidateSession, string candidateSid, bool candidateElevated,
            int currentSession, string currentSid, bool currentElevated, bool expected)
        {
            Assert.AreEqual(expected, ShellExtensionRegistrar.IsEligibleExplorer(
                candidateSession, candidateSid, candidateElevated,
                currentSession, currentSid, currentElevated));
        }

        private static ShellExtensionRegistrar.ExplorerCandidate Fake(
            int session, string sid, bool elevated, System.Action kill,
            System.Action wait = null, bool exited = false, bool valid = true,
            System.Action dispose = null) => new()
        {
            HasExited = () => exited,
            SessionId = () => session,
            Identity = () => (valid, sid, elevated),
            Kill = kill,
            WaitForExit = wait ?? (() => { }),
            Dispose = dispose ?? (() => { })
        };

        [TestMethod]
        [TestCategory("Unit")]
        public void NoEligibleExplorer_ReturnsFailureWithoutStarting()
        {
            var starts = 0;
            var cases = new[]
            {
                Fake(1, "SID", false, () => Assert.Fail("must not kill")), // high caller, medium shell
                Fake(2, "SID", true, () => Assert.Fail("foreign session"))
            };
            Assert.IsFalse(ShellExtensionRegistrar.ExecuteExplorerRestart(
                cases, 1, "SID", true, () => { starts++; return true; }));
            Assert.AreEqual(0, starts);
            Assert.IsFalse(ShellExtensionRegistrar.ExecuteExplorerRestart(
                new ShellExtensionRegistrar.ExplorerCandidate[0], 1, "SID", true,
                () => { starts++; return true; }));
            Assert.AreEqual(0, starts);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MatchingKill_AlwaysRestartsOnceAndPropagatesFailure()
        {
            var kills = 0;
            var starts = 0;
            var disposed = 0;
            var cases = new[]
            {
                Fake(1, "SID", false, () => kills++, dispose: () => disposed++),
                Fake(1, "SID", false, () => throw new System.Exception("kill failed"), dispose: () => disposed++)
            };
            Assert.IsFalse(ShellExtensionRegistrar.ExecuteExplorerRestart(cases, 1, "SID", false,
                () => { starts++; return true; }));
            Assert.AreEqual(1, kills);
            Assert.AreEqual(1, starts);
            Assert.AreEqual(2, disposed);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RestartNullOrExceptionReportsFailure()
        {
            foreach (var throws in new[] { false, true })
            {
                var starts = 0;
                var cases = new[] { Fake(1, "SID", false, () => { }) };
                Assert.IsFalse(ShellExtensionRegistrar.ExecuteExplorerRestart(cases, 1, "SID", false,
                    () =>
                    {
                        starts++;
                        if (throws) throw new System.Exception("start failed");
                        return false;
                    }));
                Assert.AreEqual(1, starts);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void InaccessibleAndExitedAreSkippedAndDisposed()
        {
            var disposed = 0;
            var cases = new[]
            {
                Fake(1, "SID", false, () => Assert.Fail("exited"), exited: true, dispose: () => disposed++),
                Fake(1, "SID", false, () => Assert.Fail("inaccessible"), valid: false, dispose: () => disposed++)
            };
            Assert.IsFalse(ShellExtensionRegistrar.ExecuteExplorerRestart(cases, 1, "SID", false,
                () => { Assert.Fail("must not start"); return true; }));
            Assert.AreEqual(2, disposed);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MatchingKillAndRestartReturnsSuccess()
        {
            var kills = 0;
            var starts = 0;
            Assert.IsTrue(ShellExtensionRegistrar.ExecuteExplorerRestart(
                new[] { Fake(1, "SID", false, () => kills++) }, 1, "SID", false,
                () => { starts++; return true; }));
            Assert.AreEqual(1, kills);
            Assert.AreEqual(1, starts);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WaitInvalidOperationAfterSuccessfulKill_ReturnsFalseButRestartsOnce()
        {
            var kills = 0;
            var starts = 0;
            Assert.IsFalse(ShellExtensionRegistrar.ExecuteExplorerRestart(
                new[] { Fake(1, "SID", false, () => kills++,
                    wait: () => throw new System.InvalidOperationException("post-kill wait")) },
                1, "SID", false, () => { starts++; return true; }));
            Assert.AreEqual(1, kills);
            Assert.AreEqual(1, starts);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WaitIOExceptionAfterSuccessfulKill_ReturnsFalseButRestartsOnce()
        {
            var starts = 0;
            Assert.IsFalse(ShellExtensionRegistrar.ExecuteExplorerRestart(
                new[] { Fake(1, "SID", false, () => { },
                    wait: () => throw new System.IO.IOException("wait failure")) },
                1, "SID", false, () => { starts++; return true; }));
            Assert.AreEqual(1, starts);
        }
    }
}
