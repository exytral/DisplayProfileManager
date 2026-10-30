using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayTopologyRecoveryTests
    {
        private static AdapterLuid Adapter(uint low) => new() { LowPart = low };

        private static DisplayConfigNative.DisplayConfigPathInfo Path(uint target, uint adapter = 1, bool virtualMode = false)
        {
            return new DisplayConfigNative.DisplayConfigPathInfo
            {
                sourceInfo = new() { adapterId = DisplayConfigNative.ToNative(Adapter(adapter)), id = 0, modeInfoIdx = virtualMode ? 0x00000005u : 0u },
                targetInfo = new() { adapterId = DisplayConfigNative.ToNative(Adapter(adapter)), id = target, modeInfoIdx = virtualMode ? 0x0001FFFFu : 1u },
                flags = (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active | (virtualMode ? (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.SupportVirtualMode : 0)
            };
        }

        private static DisplayConfigNative.DisplayConfigModeInfo[] Modes(uint target = 1, uint adapter = 1) =>
        [
            new() { infoType = DisplayConfigNative.DisplayConfigModeInfoType.Source, adapterId = DisplayConfigNative.ToNative(Adapter(adapter)), id = 0 },
            new() { infoType = DisplayConfigNative.DisplayConfigModeInfoType.Target, adapterId = DisplayConfigNative.ToNative(Adapter(adapter)), id = target }
        ];

        private static DisplayConfigInfo Display(uint target, uint adapter = 1) => new()
        {
            AdapterId = Adapter(adapter), TargetId = target, SourceId = 0, IsEnabled = true
        };

        private static bool RunTopologyWithNativeFakes(int topologyResult, bool matchingReadback, out int setCalls, out int queryCalls, out DisplayConfigNative.SetDisplayConfigFlags recoveryFlags)
        {
            int sets = 0;
            int queries = 0;
            DisplayConfigNative.SetDisplayConfigFlags observedFlags = 0;

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths, out uint modeCount,
                out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var path = Path(1);
                const int readbackQuery = 3;
                if (queries <= 2 || (queries == readbackQuery && !matchingReadback))
                    path.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                paths = [path];
                modes = Modes();
                pathCount = 1;
                modeCount = 2;
                return 0;
            }

            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                observedFlags = flags;
                return sets == 1 ? topologyResult : 0;
            }

            bool result = DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set);
            setCalls = sets;
            queryCalls = queries;
            recoveryFlags = observedFlags;
            return result;
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TopologyRequiresUpdate_VirtualRepresentation_IsEquivalent()
        {
            var cloneRequested = new List<DisplayConfigInfo> { Display(1), Display(2) };
            var plainClone = new[] { Path(1), Path(2) };
            var virtualClone = new[] { Path(1, virtualMode: true), Path(2, virtualMode: true) };
            virtualClone[0].sourceInfo.modeInfoIdx = 0x00030005u;
            virtualClone[0].targetInfo.modeInfoIdx = 0x00040006u;
            virtualClone[1].sourceInfo.modeInfoIdx = 0x00070008u;
            virtualClone[1].targetInfo.modeInfoIdx = 0x0009000Au;

            Assert.IsFalse(DisplayConfigService.TopologyRequiresUpdate(plainClone, cloneRequested));
            Assert.IsFalse(DisplayConfigService.TopologyRequiresUpdate(virtualClone, cloneRequested));

            var extendRequested = new List<DisplayConfigInfo> { Display(1), Display(2) };
            extendRequested[1].SourceId = 1;
            var plainExtend = new[] { Path(1), Path(2) };
            plainExtend[1].sourceInfo.id = 1;
            var virtualExtend = new[] { Path(1, virtualMode: true), Path(2, virtualMode: true) };
            virtualExtend[1].sourceInfo.id = 1;
            virtualExtend[0].sourceInfo.modeInfoIdx = 0x000B000Cu;
            virtualExtend[1].targetInfo.modeInfoIdx = 0x000D000Eu;

            Assert.IsFalse(DisplayConfigService.TopologyRequiresUpdate(plainExtend, extendRequested));
            Assert.IsFalse(DisplayConfigService.TopologyRequiresUpdate(virtualExtend, extendRequested));

            plainClone[1].sourceInfo.id = 1;
            virtualClone[1].sourceInfo.id = 1;
            Assert.IsTrue(DisplayConfigService.TopologyRequiresUpdate(plainClone, cloneRequested));
            Assert.IsTrue(DisplayConfigService.TopologyRequiresUpdate(virtualClone, cloneRequested));

            plainClone[1].sourceInfo.id = 0;
            virtualClone[1].sourceInfo.id = 0;
            plainClone[1].flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
            virtualClone[1].flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
            Assert.IsTrue(DisplayConfigService.TopologyRequiresUpdate(plainClone, cloneRequested));
            Assert.IsTrue(DisplayConfigService.TopologyRequiresUpdate(virtualClone, cloneRequested));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_VirtualAwareFirstQueryMatchingTopology_IsNoOp()
        {
            int queries = 0;
            int sets = 0;

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                Assert.AreEqual(
                    DisplayConfigNative.QueryDisplayConfigFlags.AllPaths |
                    DisplayConfigNative.QueryDisplayConfigFlags.VirtualModeAware,
                    flags);
                paths = [Path(1, virtualMode: true)];
                modes = Modes();
                pathCount = 1;
                modeCount = (uint)modes.Length;
                return 0;
            }

            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                return 0;
            }

            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(1, queries);
            Assert.AreEqual(0, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [DataRow(true, true, true, 2)]
        [DataRow(false, false, false, 3)]
        [DataRow(true, false, true, 2)]
        public void ApplyTopology_RequestedPathCapability_SelectsSubmissionMode(bool requestedVirtual, bool includeLegacyPath, bool expectedVirtual, int expectedQueries)
        {
            int queries = 0;
            int sets = 0;

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var requested = Path(1, virtualMode: requestedVirtual);
                if (queries == 1)
                    requested.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                if (includeLegacyPath)
                {
                    var inactive = Path(2);
                    inactive.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                    paths = [requested, inactive];
                }
                else
                    paths = [requested];

                modes = Modes();
                pathCount = (uint)paths.Length;
                modeCount = (uint)modes.Length;
                if (queries == 2 && !requestedVirtual)
                    Assert.IsFalse(flags.HasFlag(DisplayConfigNative.QueryDisplayConfigFlags.VirtualModeAware));
                return 0;
            }

            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                Assert.AreEqual(expectedVirtual, flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
                Assert.AreEqual(0u, modeCount);
                Assert.IsNull(modes);
                return 0;
            }

            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(expectedQueries, queries);
            Assert.AreEqual(1, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_MixedRequestedAdapters_UsesLegacyBestModeRecovery()
        {
            int queries = 0;
            int sets = 0;
            var input = new List<DisplayConfigInfo> { Display(1), Display(2, 2) };

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var virtualPath = Path(1, virtualMode: true);
                virtualPath.sourceInfo.modeInfoIdx = 0;
                virtualPath.targetInfo.modeInfoIdx = 1;
                var legacyPath = Path(2, adapter: 2);
                legacyPath.sourceInfo.modeInfoIdx = 2;
                legacyPath.targetInfo.modeInfoIdx = 3;
                if (queries == 1)
                    legacyPath.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                paths = [virtualPath, legacyPath];
                modes =
                [
                    Modes()[0], Modes()[1],
                    new DisplayConfigNative.DisplayConfigModeInfo { infoType = DisplayConfigNative.DisplayConfigModeInfoType.Source, adapterId = DisplayConfigNative.ToNative(Adapter(2)), id = 0 },
                    new DisplayConfigNative.DisplayConfigModeInfo { infoType = DisplayConfigNative.DisplayConfigModeInfoType.Target, adapterId = DisplayConfigNative.ToNative(Adapter(2)), id = 2 }
                ];
                pathCount = 2;
                modeCount = 4;
                if (queries >= 2)
                    Assert.IsFalse(flags.HasFlag(DisplayConfigNative.QueryDisplayConfigFlags.VirtualModeAware));
                return 0;
            }

            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                Assert.IsFalse(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
                Assert.AreEqual(2u, pathCount);
                if (sets == 1)
                {
                    Assert.AreEqual(uint.MaxValue, paths[0].sourceInfo.modeInfoIdx);
                    Assert.AreEqual(uint.MaxValue, paths[0].targetInfo.modeInfoIdx);
                    Assert.AreEqual(0u, modeCount);
                    return 31;
                }
                Assert.AreEqual(0u, modeCount);
                Assert.IsNull(modes);
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.UseSuppliedDisplayConfig));
                Assert.IsFalse(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.AllowChanges));
                return 0;
            }

            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(input, false, Query, Set));
            Assert.AreEqual(3, queries);
            Assert.AreEqual(2, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [DataRow(1, 0)]
        [DataRow(2, 0)]
        [DataRow(3, 2)]
        public void ApplyTopology_MismatchedQueryArrays_FailsBeforeAffectedSubmission(int invalidQuery, int expectedSets)
        {
            int queries = 0;
            int sets = 0;

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var path = Path(1);
                if (queries == 1)
                    path.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                paths = new[] { path };
                modes = Modes();
                pathCount = 1;
                modeCount = (uint)(queries == invalidQuery ? 5 : modes.Length);
                return 0;
            }

            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                return sets == 1 ? 31 : 0;
            }

            Assert.IsFalse(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(expectedSets, sets);
            Assert.AreEqual(invalidQuery, queries);
        }

        [TestMethod]
        [TestCategory("Unit")]
        [DataRow("missing")]
        [DataRow("adapter")]
        [DataRow("route")]
        public void ApplyTopology_LegacyRequeryRouteDrift_FailsBeforeSet(string mutation)
        {
            int queries = 0;
            int sets = 0;
            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags,
                out uint pathCount, out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var path = Path(1);
                if (queries == 1) path.flags = 0;
                if (queries == 2)
                {
                    switch (mutation)
                    {
                        case "missing":
                            path.targetInfo.id = 8;
                            break;
                        case "adapter":
                            path.sourceInfo.adapterId = DisplayConfigNative.ToNative(Adapter(2));
                            path.targetInfo.adapterId = DisplayConfigNative.ToNative(Adapter(2));
                            break;
                        case "route":
                            path.targetInfo.outputTechnology = (DisplayConfigNative.DisplayConfigVideoOutputTechnology)2;
                            break;
                    }
                }
                paths = mutation == "duplicate" && queries == 2
                    ? [path, path]
                    : [path];
                modes = Modes();
                pathCount = (uint)paths.Length;
                modeCount = (uint)modes.Length;
                return 0;
            }
            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                return 0;
            }

            Assert.IsFalse(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(0, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_InconsistentSourceTargetAdapter_FailsBeforeSet()
        {
            int sets = 0;
            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths, out uint modeCount,
                out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                var path = Path(1);
                path.flags = 0;
                path.sourceInfo.adapterId = DisplayConfigNative.ToNative(Adapter(2));
                paths = new[] { path };
                modes = Modes();
                pathCount = 1;
                modeCount = (uint)modes.Length;
                return 0;
            }
            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                return 0;
            }

            Assert.IsFalse(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(0, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_DistinctInactiveTargetRoutes_FailsBeforeSet()
        {
            int sets = 0;
            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths, out uint modeCount,
                out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                var first = Path(1);
                first.flags = 0;
                var second = first;
                second.sourceInfo.id = 1;
                second.targetInfo.outputTechnology = (DisplayConfigNative.DisplayConfigVideoOutputTechnology)2;
                paths = new[] { first, second };
                modes = Modes();
                pathCount = 2;
                modeCount = (uint)modes.Length;
                return 0;
            }
            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                return 0;
            }

            Assert.IsFalse(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(0, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_DuplicateRequestedTarget_FailsBeforeSet()
        {
            int sets = 0;
            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths, out uint modeCount,
                out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                var path = Path(1);
                path.flags = 0;
                paths = new[] { path };
                modes = Modes();
                pathCount = 1;
                modeCount = (uint)modes.Length;
                return 0;
            }
            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                return 0;
            }

            Assert.IsFalse(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1), Display(1) }, false, Query, Set));
            Assert.AreEqual(0, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_LegacyRequerySourcePlacementChange_Submits()
        {
            int queries = 0;
            int sets = 0;
            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths, out uint modeCount,
                out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var path = Path(1);
                if (queries == 1)
                    path.flags = 0;
                if (queries == 2)
                    path.sourceInfo.id = 7;
                paths = new[] { path };
                modes = Modes();
                pathCount = 1;
                modeCount = (uint)modes.Length;
                return 0;
            }
            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                return 0;
            }

            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(1, sets);
            Assert.AreEqual(3, queries);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_InactiveTargetWithSevenSourceCandidates_Submits()
        {
            int queries = 0;
            int sets = 0;
            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths, out uint modeCount,
                out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                if (queries == 1)
                {
                    var candidates = new List<DisplayConfigNative.DisplayConfigPathInfo>();
                    for (uint sourceId = 0; sourceId < 7; sourceId++)
                    {
                        var candidate = Path(4242, adapter: 0x1234, virtualMode: true);
                        candidate.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                        candidate.sourceInfo.id = sourceId;
                        candidate.targetInfo.outputTechnology = DisplayConfigNative.DisplayConfigVideoOutputTechnology.Hdmi;
                        candidates.Add(candidate);
                    }
                    paths = candidates.ToArray();
                }
                else
                {
                    var active = Path(4242, adapter: 0x1234, virtualMode: true);
                    active.targetInfo.outputTechnology = DisplayConfigNative.DisplayConfigVideoOutputTechnology.Hdmi;
                    paths = new[] { active };
                }

                modes = Modes(4242, 0x1234);
                pathCount = (uint)paths.Length;
                modeCount = (uint)modes.Length;
                return 0;
            }
            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
                Assert.AreEqual(1, paths.Count(path => (path.flags & (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active) != 0));
                return 0;
            }

            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(4242, 0x1234) }, false, Query, Set));
            Assert.AreEqual(1, sets);
            Assert.AreEqual(2, queries);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_Error31_UsesSamePreparedPathsAndReadback()
        {
            bool ok = RunTopologyWithNativeFakes(31, true, out int calls, out int queries, out var flags);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, calls);
            Assert.AreEqual(3, queries);
            Assert.IsFalse(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
            Assert.IsFalse(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.AllowChanges));
            Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.UseSuppliedDisplayConfig));
            Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.SaveToDatabase));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_Error31_VirtualRecoveryReusesPreparedPathsWithoutModes()
        {
            int queryCalls = 0;
            int setCalls = 0;
            bool inspectedRecovery = false;
            DisplayConfigNative.DisplayConfigPathInfo[] firstSubmissionPaths = null;

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags,
                out uint pathCount, out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queryCalls++;
                var path = Path(1, virtualMode: true);
                if (queryCalls == 1)
                    path.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                if (queryCalls == 2)
                    path.targetInfo.modeInfoIdx = 0x00010002u;
                paths = [path];
                var typedModes = new List<DisplayConfigNative.DisplayConfigModeInfo>(Modes())
                {
                    new() { infoType = DisplayConfigNative.DisplayConfigModeInfoType.DesktopImage,
                        adapterId = DisplayConfigNative.ToNative(Adapter(1)), id = 1 }
                };
                modes = typedModes.ToArray();
                pathCount = (uint)paths.Length;
                modeCount = (uint)modes.Length;
                return 0;
            }

            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                setCalls++;
                if (setCalls == 1)
                {
                    firstSubmissionPaths = paths;
                    Assert.AreEqual(0u, modeCount);
                    Assert.IsNull(modes);
                    return 31;
                }

                Assert.AreEqual(2, setCalls);
                Assert.AreSame(firstSubmissionPaths, paths);
                Assert.AreEqual(1u, pathCount);
                Assert.AreEqual(0u, modeCount);
                Assert.IsNull(modes);
                Assert.AreEqual(1, paths.Length);
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.UseSuppliedDisplayConfig));
                Assert.IsFalse(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.AllowChanges));
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.SaveToDatabase));
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.Apply));
                Assert.IsTrue((paths[0].flags & (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active) != 0);
                Assert.IsTrue((paths[0].flags & (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.SupportVirtualMode) != 0);
                inspectedRecovery = true;
                return 0;
            }

            bool ok = DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set);

            Assert.IsTrue(ok);
            Assert.IsTrue(inspectedRecovery);
            Assert.AreEqual(2, setCalls);
            Assert.AreEqual(2, queryCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_FirstSetSuccess_ReadbackMismatch_FailsClosed()
        {
            bool ok = RunTopologyWithNativeFakes(0, false, out int calls, out int queries, out _);

            Assert.IsFalse(ok);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(3, queries);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_FirstSetSuccess_VirtualCloneReadbackMatch_Succeeds()
        {
            int queries = 0;
            int sets = 0;
            var requested = new List<DisplayConfigInfo> { Display(1), Display(2) };

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var first = Path(1, virtualMode: true);
                var second = Path(2, virtualMode: true);
                if (queries == 1)
                {
                    first.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                    second.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                }
                paths = [first, second];
                modes = Modes();
                pathCount = (uint)paths.Length;
                modeCount = (uint)modes.Length;
                return 0;
            }

            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
                return 0;
            }

            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(requested, false, Query, Set));
            Assert.AreEqual(2, queries);
            Assert.AreEqual(1, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_FirstSetSuccess_MixedLegacyReadbackMatch_Succeeds()
        {
            int queries = 0;
            int sets = 0;
            var requested = new List<DisplayConfigInfo> { Display(1), Display(2, 2) };

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var first = Path(1, virtualMode: true);
                var second = Path(2, 2);
                if (queries == 1)
                {
                    first.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                    second.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                }
                paths = [first, second];
                modes = Modes();
                pathCount = (uint)paths.Length;
                modeCount = (uint)modes.Length;
                return 0;
            }

            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                Assert.IsFalse(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
                return 0;
            }

            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(requested, false, Query, Set));
            Assert.AreEqual(3, queries);
            Assert.AreEqual(1, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_FirstSetSuccess_InvalidReadbackCounts_FailsClosed()
        {
            int queries = 0;
            int sets = 0;

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var path = Path(1);
                if (queries == 1)
                    path.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                paths = [path];
                modes = Modes();
                pathCount = queries == 3 ? 2u : 1u;
                modeCount = (uint)modes.Length;
                return 0;
            }

            int Set(uint pathCount, DisplayConfigNative.DisplayConfigPathInfo[] paths,
                uint modeCount, DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                return 0;
            }

            Assert.IsFalse(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(3, queries);
            Assert.AreEqual(1, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_Error87_FailsWithoutRecoveryMutation()
        {
            bool ok = RunTopologyWithNativeFakes(87, true, out int calls, out int queries, out _);

            Assert.IsFalse(ok);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(2, queries);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_Error31_ReadbackMismatch_FailsClosed()
        {
            bool ok = RunTopologyWithNativeFakes(31, false, out int calls, out int queries, out _);

            Assert.IsFalse(ok);
            Assert.AreEqual(2, calls);
            Assert.AreEqual(3, queries);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_ExactActiveTarget_Succeeds()
        {
            Assert.IsTrue(DisplayConfigService.RecoveryMatchesRequested([Path(1)], new() { Display(1) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_MissingTarget_FailsClosed()
        {
            Assert.IsFalse(DisplayConfigService.RecoveryMatchesRequested([Path(1)], new() { Display(2) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_UnexpectedActiveTarget_FailsClosed()
        {
            Assert.IsFalse(DisplayConfigService.RecoveryMatchesRequested([Path(1), Path(2)], new() { Display(1) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_DuplicateActiveTarget_FailsClosed()
        {
            Assert.IsFalse(DisplayConfigService.RecoveryMatchesRequested([Path(1), Path(1)], new() { Display(1) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_CloneMismatch_FailsClosed()
        {
            var paths = new[] { Path(1), Path(2) };
            paths[1].sourceInfo.id = 1;

            Assert.IsFalse(DisplayConfigService.RecoveryMatchesRequested(paths, new() { Display(1), Display(2) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_UnexpectedClone_FailsClosed()
        {
            var first = Display(1);
            var second = Display(2);
            second.SourceId = 1;

            Assert.IsFalse(DisplayConfigService.RecoveryMatchesRequested(new[] { Path(1), Path(2) }, new() { first, second }));
        }

        [TestMethod]
        public void ReleaseQuality_QueryFlagsAndSetFlags_AreStrictlyOrdered()
        {
            int queries = 0, sets = 0;
            var expectedQueries = new[] {
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths | DisplayConfigNative.QueryDisplayConfigFlags.VirtualModeAware,
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths,
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths
            };
            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pc,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths, out uint mc,
                out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                Assert.IsTrue(queries < expectedQueries.Length);
                Assert.AreEqual(expectedQueries[queries], flags);
                queries++;
                paths = [Path(1)];
                if (queries == 1) paths[0].flags = 0;
                modes = Modes(); pc = 1; mc = 2;
                return 0;
            }
            int Set(uint pc, DisplayConfigNative.DisplayConfigPathInfo[] paths, uint mc,
                DisplayConfigNative.DisplayConfigModeInfo[] modes,
                DisplayConfigNative.SetDisplayConfigFlags flags)
            {
                sets++;
                Assert.AreEqual(1u, pc);
                if (sets == 1)
                {
                    Assert.AreEqual(0u, mc);
                    Assert.IsNull(modes);
                    Assert.AreEqual(
                        DisplayConfigNative.SetDisplayConfigFlags.TopologySupplied |
                        DisplayConfigNative.SetDisplayConfigFlags.Apply |
                        DisplayConfigNative.SetDisplayConfigFlags.AllowPathOrderChanges, flags);
                    return 31;
                }
                Assert.AreEqual(0u, mc);
                Assert.IsNull(modes);
                Assert.AreEqual(
                    DisplayConfigNative.SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    DisplayConfigNative.SetDisplayConfigFlags.Apply |
                    DisplayConfigNative.SetDisplayConfigFlags.SaveToDatabase, flags);
                return 0;
            }
            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(3, queries);
            Assert.AreEqual(2, sets);
        }
    }
}
