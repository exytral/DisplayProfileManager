using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class DisplayConfigQueryRetryTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void QueryDisplayConfigWithRetry_InsufficientBuffer_RefreshesSizesAndTrimsResults()
        {
            int sizeCalls = 0, queryCalls = 0;

            int GetSizes(DisplayConfigHelper.QueryDisplayConfigFlags flags, out uint pc, out uint mc)
            {
                sizeCalls++;
                pc = sizeCalls == 1 ? 1u : 4u;
                mc = sizeCalls == 1 ? 1u : 5u;
                return 0;
            }

            int Query(DisplayConfigHelper.QueryDisplayConfigFlags flags, ref uint pc,
                DisplayConfigHelper.DisplayConfigPathInfo[] paths, ref uint mc,
                DisplayConfigHelper.DisplayConfigModeInfo[] modes, IntPtr topologyId)
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

            int result = DisplayConfigHelper.QueryDisplayConfigWithRetry(
                DisplayConfigHelper.QueryDisplayConfigFlags.AllPaths,
                out uint pc, out DisplayConfigHelper.DisplayConfigPathInfo[] paths,
                out uint mc, out DisplayConfigHelper.DisplayConfigModeInfo[] modes, GetSizes, Query);

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

            int GetSizes(DisplayConfigHelper.QueryDisplayConfigFlags flags, out uint pc, out uint mc)
            {
                sizeCalls++;
                pc = 1;
                mc = 1;
                return 0;
            }

            int Query(DisplayConfigHelper.QueryDisplayConfigFlags flags, ref uint pc,
                DisplayConfigHelper.DisplayConfigPathInfo[] paths, ref uint mc,
                DisplayConfigHelper.DisplayConfigModeInfo[] modes, IntPtr topologyId)
            {
                queryCalls++;
                return 87;
            }

            int result = DisplayConfigHelper.QueryDisplayConfigWithRetry(
                DisplayConfigHelper.QueryDisplayConfigFlags.AllPaths, out _, out _, out _, out _, GetSizes, Query);

            Assert.AreEqual(87, result);
            Assert.AreEqual(1, sizeCalls);
            Assert.AreEqual(1, queryCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QueryDisplayConfigWithRetry_RepeatedInsufficientBuffer_IsBounded()
        {
            int sizeCalls = 0, queryCalls = 0;

            int GetSizes(DisplayConfigHelper.QueryDisplayConfigFlags flags, out uint pc, out uint mc)
            {
                sizeCalls++;
                pc = 1;
                mc = 1;
                return 0;
            }

            int Query(DisplayConfigHelper.QueryDisplayConfigFlags flags, ref uint pc,
                DisplayConfigHelper.DisplayConfigPathInfo[] paths, ref uint mc,
                DisplayConfigHelper.DisplayConfigModeInfo[] modes, IntPtr topologyId)
            {
                queryCalls++;
                return 122;
            }

            int result = DisplayConfigHelper.QueryDisplayConfigWithRetry(
                DisplayConfigHelper.QueryDisplayConfigFlags.AllPaths, out _, out _, out _, out _, GetSizes, Query);

            Assert.AreEqual(122, result);
            Assert.AreEqual(DisplayConfigHelper.QueryDisplayConfigMaxAttempts, sizeCalls);
            Assert.AreEqual(DisplayConfigHelper.QueryDisplayConfigMaxAttempts, queryCalls);
        }
    }
}
