using DisplayProfileManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class IpcAuthorityTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void BuildPipeName_NormalAndDev_AreDistinctWithinSameSession()
        {
            string normal = IpcServer.BuildPipeName(42, IpcAuthorityKind.Normal);
            string dev = IpcServer.BuildPipeName(42, IpcAuthorityKind.Dev);

            Assert.AreEqual("DPM_IpcPipe.42", normal);
            Assert.AreEqual("DPM_IpcPipe.Dev.42", dev);
            Assert.AreNotEqual(normal, dev);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProbeOrder_ImplementsAuthorityRoutingPolicy()
        {
            CollectionAssert.AreEqual(new[] { IpcAuthorityKind.Normal }, IpcServer.GetProbeOrder(devMode: false, isExit: false));
            CollectionAssert.AreEqual(new[] { IpcAuthorityKind.Dev }, IpcServer.GetProbeOrder(devMode: true, isExit: false));
            CollectionAssert.AreEqual(new[] { IpcAuthorityKind.Normal, IpcAuthorityKind.Dev }, IpcServer.GetProbeOrder(devMode: false, isExit: true));
            CollectionAssert.AreEqual(new[] { IpcAuthorityKind.Dev }, IpcServer.GetProbeOrder(devMode: true, isExit: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task DevListener_DoesNotConsumeNormalPipeMessage_AndReceivesDevMessage()
        {
            using var cancellation = new CancellationTokenSource();
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            IpcServer.StartListening(cancellation.Token, message =>
            {
                received.TrySetResult(message);
                return Task.CompletedTask;
            }, IpcAuthorityKind.Dev);

            bool normalSent = await IpcServer.SendAsync("NORMAL", IpcAuthorityKind.Normal, 100);
            Assert.IsFalse(normalSent);

            bool devSent = false;
            for (int attempt = 0; attempt < 20 && !devSent; attempt++)
            {
                devSent = await IpcServer.SendAsync("DEV", IpcAuthorityKind.Dev, 100);
                if (!devSent) await Task.Delay(25);
            }

            Assert.IsTrue(devSent);
            Assert.AreEqual("DEV", await received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            cancellation.Cancel();
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
            var expected = IpcServer.BuildPipeName( System.Diagnostics.Process.GetCurrentProcess().SessionId);

            Assert.AreEqual(expected, IpcServer.PipeName);
        }
    }
}
