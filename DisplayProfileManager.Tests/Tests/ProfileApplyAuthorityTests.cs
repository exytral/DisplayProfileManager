using DisplayProfileManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ProfileApplyAuthorityTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public async Task EnqueueAsync_BlockedFirstRequest_PreservesFifoAndSingleConcurrency()
        {
            var authority = new ProfileApplyAuthority();
            var releaseA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var startedA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var starts = new List<string>();
            int active = 0;
            int maxActive = 0;

            async Task<string> Run(string name, bool block)
            {
                int now = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, now);
                lock (starts) starts.Add(name);
                if (name == "A") startedA.TrySetResult(true);
                if (block) await releaseA.Task;
                await Task.Yield();
                Interlocked.Decrement(ref active);
                return name;
            }

            Task<string> a = authority.EnqueueAsync(() => Run("A", block: true));
            await startedA.Task;
            Task<string> b = authority.EnqueueAsync(() => Run("B", block: false));
            Task<string> c = authority.EnqueueAsync(() => Run("C", block: false));
            releaseA.TrySetResult(true);
            await Task.WhenAll(a, b, c);

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, starts);
            Assert.AreEqual(1, maxActive);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task EnqueueAsync_RollbackCoreRunsInsideOwningRequestBeforeNextRequest()
        {
            var authority = new ProfileApplyAuthority();
            var releaseA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var trace = new List<string>();

            async Task<string> RollbackCore()
            {
                trace.Add("P");
                await Task.Yield();
                return "P";
            }

            Task<string> a = authority.EnqueueAsync(async () =>
            {
                trace.Add("A");
                await releaseA.Task;
                await RollbackCore();
                trace.Add("A-complete");
                return "A";
            });
            Task<string> b = authority.EnqueueAsync(async () =>
            {
                trace.Add("B");
                await Task.Yield();
                return "B";
            });

            releaseA.TrySetResult(true);
            await Task.WhenAll(a, b);
            CollectionAssert.AreEqual(new[] { "A", "P", "A-complete", "B" }, trace);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task EnqueueAsync_ExceptionDoesNotBlockFollowingRequest()
        {
            var authority = new ProfileApplyAuthority();
            var trace = new List<string>();
            Task<int> a = authority.EnqueueAsync<int>(() => throw new InvalidOperationException("boom"));
            Task<int> b = authority.EnqueueAsync(async () =>
            {
                trace.Add("B");
                await Task.Yield();
                return 2;
            });

            bool threwExpected = false;
            try
            {
                await a;
            }
            catch (InvalidOperationException ex) when (ex.Message == "boom")
            {
                threwExpected = true;
            }

            Assert.IsTrue(threwExpected, "The failed apply must propagate its own exception to its caller.");
            Assert.AreEqual(2, await b);
            CollectionAssert.AreEqual(new[] { "B" }, trace);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task EnqueueAsync_AllProductionApplySourcesShareOneOrderedAuthority()
        {
            var authority = new ProfileApplyAuthority();
            var sources = new[]
            {
                ProfileManager.ApplySource.Window,
                ProfileManager.ApplySource.Tray,
                ProfileManager.ApplySource.Hotkey,
                ProfileManager.ApplySource.CommandLine,
                ProfileManager.ApplySource.Startup,
            };
            var observed = new List<ProfileManager.ApplySource>();
            var tasks = sources.Select(source => authority.EnqueueAsync(async () =>
            {
                observed.Add(source);
                await Task.Yield();
                return source;
            })).ToArray();

            await Task.WhenAll(tasks);
            CollectionAssert.AreEqual(sources, observed);
        }
    }
}
