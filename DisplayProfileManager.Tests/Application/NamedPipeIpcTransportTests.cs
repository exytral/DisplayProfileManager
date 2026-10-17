using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Integration;

namespace DisplayProfileManager.Tests.Application
{
    [TestClass]
    public class IpcAuthorityTests
    {
        public TestContext TestContext { get; set; }

        private static string CreateTestPipeName(string authority) => $"DPM_TestIpcPipe.{authority}.{Guid.NewGuid():N}";

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildPipeName_NormalAndDev_AreDistinctWithinSameSession()
        {
            string normal = NamedPipeIpcTransport.BuildPipeName(1, IpcAuthorityKind.Normal);
            string dev = NamedPipeIpcTransport.BuildPipeName(1, IpcAuthorityKind.Dev);
            Assert.AreEqual("DPM_IpcPipe.1", normal);
            Assert.AreEqual("DPM_IpcPipe.Dev.1", dev);
            Assert.AreNotEqual(normal, dev);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetProbeOrder_ImplementsAuthorityRoutingPolicy()
        {
            CollectionAssert.AreEqual(new[] { IpcAuthorityKind.Normal }, NamedPipeIpcTransport.GetProbeOrder(devMode: false, isExit: false));
            CollectionAssert.AreEqual(new[] { IpcAuthorityKind.Dev }, NamedPipeIpcTransport.GetProbeOrder(devMode: true, isExit: false));
            CollectionAssert.AreEqual(new[] { IpcAuthorityKind.Normal, IpcAuthorityKind.Dev }, NamedPipeIpcTransport.GetProbeOrder(devMode: false, isExit: true));
            CollectionAssert.AreEqual(new[] { IpcAuthorityKind.Dev }, NamedPipeIpcTransport.GetProbeOrder(devMode: true, isExit: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task NormalListener_PreservesSourceBearingProfileMessage()
        {
            const string message = "PROFILE-SOURCE:DesktopContextMenu:profile-id";
            string pipeName = CreateTestPipeName("Normal");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                NamedPipeIpcTransport.StartListening(cancellation.Token, value =>
                {
                    received.TrySetResult(value);
                    return Task.CompletedTask;
                }, pipeName);

                bool sent = false;
                for (int attempt = 0; attempt < 20 && !sent; attempt++)
                {
                    sent = await NamedPipeIpcTransport.SendAsync(message, pipeName, 100);
                    if (!sent) await Task.Delay(25, TestContext.CancellationToken);
                }

                Assert.IsTrue(sent);
                Assert.AreEqual(message, await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken));
            }
            finally
            {
                cancellation.Cancel();
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task DevListener_PreservesSourceBearingProfileMessage()
        {
            const string message = "PROFILE-SOURCE:StartMenuShortcut:profile-id";
            string pipeName = CreateTestPipeName("Dev");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                NamedPipeIpcTransport.StartListening(cancellation.Token, value =>
                {
                    received.TrySetResult(value);
                    return Task.CompletedTask;
                }, pipeName);

                bool sent = false;
                for (int attempt = 0; attempt < 20 && !sent; attempt++)
                {
                    sent = await NamedPipeIpcTransport.SendAsync(message, pipeName, 100);
                    if (!sent) await Task.Delay(25, TestContext.CancellationToken);
                }

                Assert.IsTrue(sent);
                Assert.AreEqual(message, await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken));
            }
            finally
            {
                cancellation.Cancel();
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task DevListener_DoesNotConsumeNormalPipeMessage_AndReceivesDevMessage()
        {
            string normalPipeName = CreateTestPipeName("Normal");
            string devPipeName = CreateTestPipeName("Dev");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                NamedPipeIpcTransport.StartListening(cancellation.Token, message =>
                {
                    received.TrySetResult(message);
                    return Task.CompletedTask;
                }, devPipeName);

                bool normalSent = await NamedPipeIpcTransport.SendAsync("NORMAL", normalPipeName, 100);
                Assert.IsFalse(normalSent);

                bool devSent = false;
                for (int attempt = 0; attempt < 20 && !devSent; attempt++)
                {
                    devSent = await NamedPipeIpcTransport.SendAsync("DEV", devPipeName, 100);
                    if (!devSent) await Task.Delay(25, TestContext.CancellationToken);
                }

                Assert.IsTrue(devSent);
                Assert.AreEqual("DEV", await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken));
            }
            finally
            {
                cancellation.Cancel();
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        [Timeout(8000)]
        public async Task HeldOpenClient_CancellationReleasesListener()
        {
            var name = $"DPM_Test_Held.{Guid.NewGuid():N}";
            using var stop = new CancellationTokenSource();
            var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            NamedPipeIpcTransport.StartListening(stop.Token, text => { delivered.TrySetResult(text); return Task.CompletedTask; }, name);
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
            try
            {
                await client.ConnectAsync(2000);
                var bytes = Encoding.UTF8.GetBytes("PARTIAL");
                await client.WriteAsync(bytes);
                await client.FlushAsync();
                stop.Cancel();
                // A held-open client must not continue owning the only server instance after cancellation.
                await Task.Delay(250);
                using var replacement = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                Assert.IsNotNull(replacement);
            }
            finally { stop.Cancel(); }
        }
        [TestMethod]
        [TestCategory("Unit")]
        [Timeout(10000)]
        public async Task OversizeInputDoesNotPoisonFollowingValidMessage()
        {
            string name = $"DPM_Test_Oversize.{Guid.NewGuid():N}";
            using var stop = new CancellationTokenSource();
            var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            NamedPipeIpcTransport.StartListening(stop.Token, text =>
            {
                delivered.TrySetResult(text);
                return Task.CompletedTask;
            }, name);
            try
            {
                using (var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous))
                {
                    await client.ConnectAsync(2000);
                    try { await client.WriteAsync(Encoding.UTF8.GetBytes(new string('X', 70000))); } catch (System.IO.IOException) { /* expected if server rejects oversized input early */ }
                    try { await client.FlushAsync(); } catch (System.IO.IOException) { /* expected if oversized pipe closed */ }
                }
                var sent = false;
                for (int i = 0; i < 20 && !sent; i++)
                {
                    sent = await NamedPipeIpcTransport.SendAsync("VALID", name, 100);
                    if (!sent) await Task.Delay(50);
                }
                Assert.IsTrue(sent);
                Assert.AreEqual("VALID", await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            }
            finally { stop.Cancel(); }
        }

        [TestMethod]
        [TestCategory("Unit")]
        [Timeout(10000)]
        public async Task HeldOpenInputExpiresAndNextClientCanSend()
        {
            string name = $"DPM_Test_Timeout.{Guid.NewGuid():N}";
            using var stop = new CancellationTokenSource();
            var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            NamedPipeIpcTransport.StartListening(stop.Token, text =>
            {
                delivered.TrySetResult(text);
                return Task.CompletedTask;
            }, name);
            using var held = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
            try
            {
                await held.ConnectAsync(2000);
                await held.WriteAsync(Encoding.UTF8.GetBytes("PARTIAL"));
                await held.FlushAsync();
                await Task.Delay(2400);
                var sent = false;
                for (int i = 0; i < 20 && !sent; i++)
                {
                    sent = await NamedPipeIpcTransport.SendAsync("AFTER", name, 100);
                    if (!sent) await Task.Delay(50);
                }
                Assert.IsTrue(sent);
                Assert.AreEqual("AFTER", await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            }
            finally { stop.Cancel(); }
        }

    }

    [TestClass]
    public class IpcServerTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void BuildPipeName_CarriesSessionId()
        {
            Assert.AreEqual("DPM_IpcPipe.0", NamedPipeIpcTransport.BuildPipeName(0));
            Assert.AreEqual("DPM_IpcPipe.1", NamedPipeIpcTransport.BuildPipeName(1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildPipeName_DiffersBetweenSessions()
        {
            Assert.AreNotEqual(NamedPipeIpcTransport.BuildPipeName(0), NamedPipeIpcTransport.BuildPipeName(1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PipeNames_UseSameRuleAsBuilder()
        {
            int sessionId = Process.GetCurrentProcess().SessionId;
            string normal = NamedPipeIpcTransport.BuildPipeName(sessionId, IpcAuthorityKind.Normal);
            string dev = NamedPipeIpcTransport.BuildPipeName(sessionId, IpcAuthorityKind.Dev);

            Assert.AreEqual(normal, NamedPipeIpcTransport.PipeName);
            Assert.AreEqual(dev, NamedPipeIpcTransport.DevPipeName);
        }
    }
}
