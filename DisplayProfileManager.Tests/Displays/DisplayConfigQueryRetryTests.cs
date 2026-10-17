using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Tests.Helpers;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayConfigQueryRetryTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void QueryDisplayConfigWithRetry_InsufficientBuffer_RefreshesSizesAndTrimsResults()
        {
            int sizeCalls = 0, queryCalls = 0;

            int GetSizes(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pc, out uint mc)
            {
                sizeCalls++;
                pc = sizeCalls == 1 ? 1u : 4u;
                mc = sizeCalls == 1 ? 1u : 5u;
                return 0;
            }

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, ref uint pc,
                DisplayConfigNative.DisplayConfigPathInfo[] paths, ref uint mc,
                DisplayConfigNative.DisplayConfigModeInfo[] modes, IntPtr topologyId)
            {
                queryCalls++;
                if (queryCalls == 1)
                {
                    Assert.AreEqual(1, paths.Length);
                    Assert.AreEqual(1, modes.Length);
                    return 122;
                }

                Assert.AreEqual(4, paths.Length);
                Assert.AreEqual(5, modes.Length);
                pc = 2;
                mc = 3;
                return 0;
            }

            int result = DisplayConfigService.QueryDisplayConfigWithRetry(
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths,
                out uint pc, out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint mc, out DisplayConfigNative.DisplayConfigModeInfo[] modes, GetSizes, Query);

            Assert.AreEqual(0, result);
            Assert.AreEqual(2, sizeCalls);
            Assert.AreEqual(2, queryCalls);
            Assert.AreEqual(2u, pc);
            Assert.AreEqual(3u, mc);
            Assert.AreEqual(2, paths.Length);
            Assert.AreEqual(3, modes.Length);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QueryDisplayConfigWithRetry_NonRetryableFailure_DoesNotRetry()
        {
            int sizeCalls = 0, queryCalls = 0;

            int GetSizes(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pc, out uint mc)
            {
                sizeCalls++;
                pc = 1;
                mc = 1;
                return 0;
            }

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, ref uint pc,
                DisplayConfigNative.DisplayConfigPathInfo[] paths, ref uint mc,
                DisplayConfigNative.DisplayConfigModeInfo[] modes, IntPtr topologyId)
            {
                queryCalls++;
                return 87;
            }

            int result = DisplayConfigService.QueryDisplayConfigWithRetry(
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths, out _, out _, out _, out _, GetSizes, Query);

            Assert.AreEqual(87, result);
            Assert.AreEqual(1, sizeCalls);
            Assert.AreEqual(1, queryCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task DeferDisplayLayout_TraceToggle_DoesNotChangeQueriesCadenceOrReadiness()
        {
            async Task<(bool Result, int Queries, int[] Delays, string[] Logs)> RunAsync(bool traceEnabled)
            {
                var expectedOne = new DisplayConfigInfoBuilder().WithTargetId(1).WithFriendlyName("One").Build();
                var expectedTwo = new DisplayConfigInfoBuilder().WithTargetId(2).WithFriendlyName("Two").Build();
                var liveOne = new DisplayConfigInfoBuilder().WithTargetId(1).WithFriendlyName("One").Build();
                var liveTwoDisabled = new DisplayConfigInfoBuilder().WithTargetId(2).WithFriendlyName("Two").Disabled().Build();
                var liveTwoEnabled = new DisplayConfigInfoBuilder().WithTargetId(2).WithFriendlyName("Two").Build();
                var snapshots = new Queue<List<DisplayConfigInfo>>();
                snapshots.Enqueue([liveOne, liveTwoDisabled]);
                snapshots.Enqueue([liveOne]);
                snapshots.Enqueue([liveOne, liveTwoEnabled]);
                var logs = new List<string>();
                var delays = new List<int>();
                long elapsed = 0;
                int queries = 0;

                bool result = await DisplayConfigService.DeferDisplayLayoutAsync(
                    [expectedOne, expectedTwo],
                    10000,
                    () =>
                    {
                        queries++;
                        return snapshots.Dequeue();
                    },
                    milliseconds =>
                    {
                        delays.Add(milliseconds);
                        elapsed += milliseconds;
                        return Task.CompletedTask;
                    },
                    logs.Add,
                    () => elapsed,
                    traceEnabled);

                return (result, queries, delays.ToArray(), logs.ToArray());
            }

            var traceOff = await RunAsync(false);
            var traceOn = await RunAsync(true);

            Assert.IsTrue(traceOff.Result);
            Assert.IsTrue(traceOn.Result);
            Assert.AreEqual(3, traceOff.Queries);
            Assert.AreEqual(traceOff.Queries, traceOn.Queries);
            CollectionAssert.AreEqual(new[] { 250, 250 }, traceOff.Delays);
            CollectionAssert.AreEqual(traceOff.Delays, traceOn.Delays);
            Assert.AreEqual(0, traceOff.Logs.Length);

            string[] pollLogs = [.. traceOn.Logs.Where(line => line.StartsWith("Display defer poll #"))];
            Assert.AreEqual(traceOn.Queries, pollLogs.Length);
            Assert.AreEqual(2, traceOn.Logs.Count(line => line.Contains(" is active at ")));
            StringAssert.Contains(pollLogs[0], "expected=2 live=2 ready=1");
            StringAssert.Contains(pollLogs[0], "present-disabled");
            StringAssert.Contains(pollLogs[1], "expected=2 live=1 ready=1");
            StringAssert.Contains(pollLogs[1], "absent");
            StringAssert.Contains(pollLogs[2], "expected=2 live=2 ready=2 pending=[]");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QueryDisplayConfigWithRetry_RepeatedInsufficientBuffer_IsBounded()
        {
            int sizeCalls = 0, queryCalls = 0;

            int GetSizes(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pc, out uint mc)
            {
                sizeCalls++;
                pc = 1;
                mc = 1;
                return 0;
            }

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, ref uint pc,
                DisplayConfigNative.DisplayConfigPathInfo[] paths, ref uint mc,
                DisplayConfigNative.DisplayConfigModeInfo[] modes, IntPtr topologyId)
            {
                queryCalls++;
                return 122;
            }

            int result = DisplayConfigService.QueryDisplayConfigWithRetry(
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths, out _, out _, out _, out _, GetSizes, Query);

            Assert.AreEqual(122, result);
            Assert.AreEqual(DisplayConfigService.QueryDisplayConfigMaxAttempts, sizeCalls);
            Assert.AreEqual(DisplayConfigService.QueryDisplayConfigMaxAttempts, queryCalls);
        }
    }
}
