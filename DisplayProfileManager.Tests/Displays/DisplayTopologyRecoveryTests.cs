using System.Collections.Generic;

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
                flags = (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active |
                    (virtualMode ? (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.SupportVirtualMode : 0)
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

        private static bool RunTopologyWithNativeFakes(int topologyResult, bool matchingReadback, out int setCalls, out int queryCalls, out DisplayConfigNative.SetDisplayConfigFlags recoveryFlags, bool virtualMode = true)
        {
            int sets = 0;
            int queries = 0;
            DisplayConfigNative.SetDisplayConfigFlags observedFlags = 0;

            // Legacy submission adds one plain re-query before the first set, shifting recovery and readback by one
            int preparationQueries = virtualMode ? 2 : 3;
            int readbackQuery = preparationQueries + 2;

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pathCount,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths, out uint modeCount,
                out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queries++;
                var path = Path(1, virtualMode: virtualMode);
                if (queries <= preparationQueries || (queries == readbackQuery && !matchingReadback))
                    path.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                paths = new[] { path };
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
        public void ApplyTopology_Error31_UsesCompleteVirtualAwareRecoveryAndReadback()
        {
            bool ok = RunTopologyWithNativeFakes(31, true, out int calls, out int queries, out var flags);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, calls);
            Assert.AreEqual(4, queries);
            Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
            Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.AllowChanges));
            Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.UseSuppliedDisplayConfig));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_Error31_VirtualRecoverySetReceivesPackedModes()
        {
            int queryCalls = 0;
            int setCalls = 0;
            bool inspectedRecovery = false;

            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags,
                out uint pathCount, out DisplayConfigNative.DisplayConfigPathInfo[] paths,
                out uint modeCount, out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                queryCalls++;
                var path = Path(1, virtualMode: true);
                if (queryCalls == 1)
                    path.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                if (queryCalls == 3)
                    path.targetInfo.modeInfoIdx = 0x00010002u;
                paths = new[] { path };
                var typedModes = new List<DisplayConfigNative.DisplayConfigModeInfo>(Modes())
                {
                    new() { infoType = DisplayConfigNative.DisplayConfigModeInfoType.DesktopImage, adapterId = DisplayConfigNative.ToNative(Adapter(1)), id = 1 }
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
                    return 31;
                }

                Assert.AreEqual(2, setCalls);
                Assert.AreEqual(1u, pathCount);
                Assert.AreEqual(3u, modeCount);
                Assert.AreEqual(1, paths.Length);
                Assert.AreEqual(3, modes.Length);
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.UseSuppliedDisplayConfig));
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.AllowChanges));
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.SaveToDatabase));
                Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.Apply));
                Assert.IsTrue((paths[0].flags & (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active) != 0);
                Assert.IsTrue((paths[0].flags & (uint)DisplayConfigNative.DisplayConfigPathInfoFlags.SupportVirtualMode) != 0);
                Assert.AreEqual(5u, paths[0].sourceInfo.modeInfoIdx & 0xFFFF);
                Assert.AreEqual(0u, paths[0].sourceInfo.modeInfoIdx >> 16);
                Assert.AreEqual(1u, paths[0].targetInfo.modeInfoIdx >> 16);
                Assert.AreEqual(2u, paths[0].targetInfo.modeInfoIdx & 0xFFFF);
                Assert.AreEqual(DisplayConfigNative.DisplayConfigModeInfoType.Source, modes[0].infoType);
                Assert.AreEqual(DisplayConfigNative.DisplayConfigModeInfoType.Target, modes[1].infoType);
                Assert.AreEqual(DisplayConfigNative.DisplayConfigModeInfoType.DesktopImage, modes[2].infoType);
                Assert.AreEqual(DisplayConfigNative.ToNative(Adapter(1)), modes[0].adapterId);
                Assert.AreEqual(DisplayConfigNative.ToNative(Adapter(1)), modes[1].adapterId);
                Assert.AreEqual(DisplayConfigNative.ToNative(Adapter(1)), modes[2].adapterId);
                Assert.AreEqual(0u, modes[0].id);
                Assert.AreEqual(1u, modes[1].id);
                Assert.AreEqual(1u, modes[2].id);
                inspectedRecovery = true;
                return 0;
            }

            bool ok = DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set);

            Assert.IsTrue(ok);
            Assert.IsTrue(inspectedRecovery);
            Assert.AreEqual(2, setCalls);
            Assert.AreEqual(4, queryCalls);
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
            Assert.AreEqual(4, queries);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_LegacyPath_Error31_UsesLegacyRecoveryAndReadback()
        {
            bool ok = RunTopologyWithNativeFakes(31, true, out int calls, out int queries, out var flags, virtualMode: false);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, calls);
            Assert.AreEqual(5, queries);
            Assert.IsFalse(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware));
            Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.AllowChanges));
            Assert.IsTrue(flags.HasFlag(DisplayConfigNative.SetDisplayConfigFlags.UseSuppliedDisplayConfig));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_LegacyPath_Error87_FailsWithoutRecoveryMutation()
        {
            bool ok = RunTopologyWithNativeFakes(87, true, out int calls, out int queries, out _, virtualMode: false);

            Assert.IsFalse(ok);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(3, queries);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyTopology_LegacyPath_Error31_ReadbackMismatch_FailsClosed()
        {
            bool ok = RunTopologyWithNativeFakes(31, false, out int calls, out int queries, out _, virtualMode: false);

            Assert.IsFalse(ok);
            Assert.AreEqual(2, calls);
            Assert.AreEqual(5, queries);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacySubmissionProbe_ActiveVirtualPaths_UsesVirtualModeAware()
        {
            Assert.IsFalse(DisplayConfigService.RequiresLegacyTopologySubmission(new[] { Path(1, virtualMode: true), Path(2, virtualMode: true) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacySubmissionProbe_AnyActiveNonVirtualPath_RequiresLegacy()
        {
            Assert.IsTrue(DisplayConfigService.RequiresLegacyTopologySubmission(new[] { Path(1) }));
            Assert.IsTrue(DisplayConfigService.RequiresLegacyTopologySubmission(new[] { Path(1, virtualMode: true), Path(2) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacySubmissionProbe_InactiveNonVirtualPath_Ignored()
        {
            var inactive = Path(2);
            inactive.flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;

            Assert.IsFalse(DisplayConfigService.RequiresLegacyTopologySubmission(new[] { Path(1, virtualMode: true), inactive }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_ExactActiveTarget_Succeeds()
        {
            Assert.IsTrue(DisplayConfigService.RecoveryMatchesRequested(new[] { Path(1) }, new() { Display(1) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_MissingTarget_FailsClosed()
        {
            Assert.IsFalse(DisplayConfigService.RecoveryMatchesRequested(new[] { Path(1) }, new() { Display(2) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_UnexpectedActiveTarget_FailsClosed()
        {
            Assert.IsFalse(DisplayConfigService.RecoveryMatchesRequested(new[] { Path(1), Path(2) }, new() { Display(1) }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecoveryReadback_DuplicateActiveTarget_FailsClosed()
        {
            Assert.IsFalse(DisplayConfigService.RecoveryMatchesRequested(new[] { Path(1), Path(1) }, new() { Display(1) }));
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
        [TestCategory("Unit")]
        public void PrepareRecovery_CompleteLegacyModes_PreservesIndices()
        {
            var paths = new[] { Path(1) };

            bool ok = DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1) }, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual(0u, paths[0].sourceInfo.modeInfoIdx);
            Assert.AreEqual(1u, paths[0].targetInfo.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_VirtualMode_PreservesModeUnion()
        {
            var paths = new[] { Path(1, virtualMode: true) };

            bool ok = DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1) }, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual(5u, paths[0].sourceInfo.modeInfoIdx);
            Assert.AreEqual(0x0001FFFFu, paths[0].targetInfo.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_VirtualTargetUpperModeAndDesktopImage_Succeeds()
        {
            var paths = new[] { Path(1, virtualMode: true) };
            paths[0].targetInfo.modeInfoIdx = 0x00010002u;
            var modes = new List<DisplayConfigNative.DisplayConfigModeInfo>(Modes())
            {
                new() { infoType = DisplayConfigNative.DisplayConfigModeInfoType.DesktopImage, adapterId = DisplayConfigNative.ToNative(Adapter(1)), id = 1 }
            };

            Assert.IsTrue(DisplayConfigService.TryPrepareRecoveryPaths(paths, modes.ToArray(), new() { Display(1) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_VirtualTargetWrongDesktopTypeOrBounds_FailsClosed()
        {
            var paths = new[] { Path(1, virtualMode: true) };
            paths[0].targetInfo.modeInfoIdx = 0x00010002u;
            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1) }, out _));

            var modes = new List<DisplayConfigNative.DisplayConfigModeInfo>(Modes())
            {
                new() { infoType = DisplayConfigNative.DisplayConfigModeInfoType.Target, adapterId = DisplayConfigNative.ToNative(Adapter(1)), id = 1 }
            };
            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(paths, modes.ToArray(), new() { Display(1) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_VirtualTargetWrongUpperTargetTypeOrBounds_FailsClosed()
        {
            var paths = new[] { Path(1, virtualMode: true) };
            paths[0].targetInfo.modeInfoIdx = 0x0002FFFFu;
            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1) }, out _));
            paths[0].targetInfo.modeInfoIdx = 0x0000FFFFu;
            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_VirtualTargetReversedLegacyFixture_FailsClosed()
        {
            var paths = new[] { Path(1, virtualMode: true) };
            paths[0].targetInfo.modeInfoIdx = 1;

            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_InactiveCandidateMissingModes_FailsClosed()
        {
            var paths = new[] { Path(1) };
            paths[0].flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
            paths[0].sourceInfo.modeInfoIdx = 0xFFFFFFFFu;

            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_OutOfBoundsMode_FailsClosed()
        {
            var paths = new[] { Path(1) };
            paths[0].targetInfo.modeInfoIdx = 9;

            bool ok = DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1) }, out _);

            Assert.IsFalse(ok);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_ModeTypeMismatch_FailsClosed()
        {
            var paths = new[] { Path(1) };
            var modes = Modes();
            modes[1].infoType = DisplayConfigNative.DisplayConfigModeInfoType.Source;

            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(paths, modes, new() { Display(1) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_AbsentModeArray_FailsClosed()
        {
            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(new[] { Path(1) }, [], new() { Display(1) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_DuplicateAdapterQualifiedTarget_FailsClosed()
        {
            var paths = new[] { Path(1), Path(1) };

            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_LegacyZeroAdapterUniqueTarget_Resolves()
        {
            var paths = new[] { Path(1) };

            Assert.IsTrue(DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1, 0) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_LegacyZeroAdapterAmbiguousTarget_FailsClosed()
        {
            var paths = new[] { Path(1), Path(1, 2) };

            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(paths, Modes(), new() { Display(1, 0) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PrepareRecovery_UnknownRequestedTarget_FailsClosed()
        {
            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(new[] { Path(1) }, Modes(), new() { Display(2) }, out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ReleaseQuality_QueryFlagsAndSetFlags_AreStrictlyOrdered()
        {
            int queries = 0, sets = 0;
            var expectedQueries = new[] {
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths,
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths | DisplayConfigNative.QueryDisplayConfigFlags.VirtualModeAware,
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths | DisplayConfigNative.QueryDisplayConfigFlags.VirtualModeAware,
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths
            };
            int Query(DisplayConfigNative.QueryDisplayConfigFlags flags, out uint pc,
                out DisplayConfigNative.DisplayConfigPathInfo[] paths, out uint mc,
                out DisplayConfigNative.DisplayConfigModeInfo[] modes)
            {
                Assert.IsTrue(queries < expectedQueries.Length);
                Assert.AreEqual(expectedQueries[queries], flags);
                queries++;
                paths = new[] { Path(1, virtualMode: true) };
                if (queries <= 2) paths[0].flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                modes = Modes();
                pc = 1;
                mc = 2;
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
                        DisplayConfigNative.SetDisplayConfigFlags.AllowPathOrderChanges |
                        DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware, flags);
                    Assert.AreEqual(0xFFFF0000u, paths[0].sourceInfo.modeInfoIdx);
                    Assert.AreEqual(0xFFFFFFFFu, paths[0].targetInfo.modeInfoIdx);
                    return 31;
                }
                Assert.AreEqual(2u, mc);
                Assert.AreEqual(2, modes.Length);
                Assert.AreEqual(
                    DisplayConfigNative.SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    DisplayConfigNative.SetDisplayConfigFlags.Apply |
                    DisplayConfigNative.SetDisplayConfigFlags.SaveToDatabase |
                    DisplayConfigNative.SetDisplayConfigFlags.AllowChanges |
                    DisplayConfigNative.SetDisplayConfigFlags.VirtualModeAware, flags);
                return 0;
            }
            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(4, queries);
            Assert.AreEqual(2, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ReleaseQuality_LegacyQueryFlagsAndSetFlags_AreStrictlyOrdered()
        {
            int queries = 0, sets = 0;
            var expectedQueries = new[] {
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths,
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths | DisplayConfigNative.QueryDisplayConfigFlags.VirtualModeAware,
                DisplayConfigNative.QueryDisplayConfigFlags.AllPaths,
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
                paths = new[] { Path(1) };
                if (queries <= 3) paths[0].flags &= ~(uint)DisplayConfigNative.DisplayConfigPathInfoFlags.Active;
                modes = Modes();
                pc = 1;
                mc = 2;
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
                    Assert.AreEqual(0xFFFFFFFFu, paths[0].sourceInfo.modeInfoIdx);
                    Assert.AreEqual(0xFFFFFFFFu, paths[0].targetInfo.modeInfoIdx);
                    return 31;
                }
                Assert.AreEqual(2u, mc);
                Assert.AreEqual(2, modes.Length);
                Assert.AreEqual(
                    DisplayConfigNative.SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    DisplayConfigNative.SetDisplayConfigFlags.Apply |
                    DisplayConfigNative.SetDisplayConfigFlags.SaveToDatabase |
                    DisplayConfigNative.SetDisplayConfigFlags.AllowChanges, flags);
                return 0;
            }
            Assert.IsTrue(DisplayConfigService.ApplyDisplayTopologyCore(new() { Display(1) }, false, Query, Set));
            Assert.AreEqual(5, queries);
            Assert.AreEqual(2, sets);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ReleaseQuality_ModeAdapterIdentity_FailsClosedForEachRecord()
        {
            foreach (int slot in new[] { 0, 1 })
            {
                var modes = Modes();
                modes[slot].adapterId = DisplayConfigNative.ToNative(Adapter(2));
                Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(new[] { Path(1) }, modes, new() { Display(1) }, out _), $"Wrong adapter at mode index {slot}");
            }
            Assert.IsTrue(DisplayConfigService.TryPrepareRecoveryPaths(new[] { Path(1) }, Modes(), new() { Display(1) }, out _));
            var virtualPath = Path(1, virtualMode: true);
            virtualPath.targetInfo.modeInfoIdx = 0x00010002u;
            var good = new List<DisplayConfigNative.DisplayConfigModeInfo>(Modes())
            {
                new() { infoType = DisplayConfigNative.DisplayConfigModeInfoType.DesktopImage, adapterId = DisplayConfigNative.ToNative(Adapter(1)), id = 1 }
            };
            Assert.IsTrue(DisplayConfigService.TryPrepareRecoveryPaths(new[] { virtualPath }, good.ToArray(), new() { Display(1) }, out _));
            var mismatchedDesktop = good[2];
            mismatchedDesktop.adapterId = DisplayConfigNative.ToNative(Adapter(2));
            good[2] = mismatchedDesktop;
            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(new[] { virtualPath }, good.ToArray(), new() { Display(1) }, out _));
            var wrongSourceId = Modes();
            wrongSourceId[0].id = 9;
            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(new[] { Path(1) }, wrongSourceId, new() { Display(1) }, out _));
            var wrongTargetId = Modes();
            wrongTargetId[1].id = 9;
            Assert.IsFalse(DisplayConfigService.TryPrepareRecoveryPaths(new[] { Path(1) }, wrongTargetId, new() { Display(1) }, out _));
        }
    }
}
