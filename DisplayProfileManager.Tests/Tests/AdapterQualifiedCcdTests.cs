using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class AdapterQualifiedCcdTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void PreparePathsForTopology_DuplicateTargetIdsAcrossAdapters_RetainsBoth()
        {
            var adapterA = Luid(1);
            var adapterB = Luid(2);
            var paths = new[] { Path(adapterA, 0, 0), Path(adapterB, 0, 0) };
            var displays = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(adapterA, 0, 0),
                Display(adapterB, 0, 0)
            };

            DisplayConfigHelper.PreparePathsForTopology(paths, displays);

            Assert.AreNotEqual(0u, paths[0].flags & (uint)DisplayConfigHelper.DisplayConfigPathInfoFlags.Active);
            Assert.AreNotEqual(0u, paths[1].flags & (uint)DisplayConfigHelper.DisplayConfigPathInfoFlags.Active);
            Assert.AreEqual(0u, paths[0].sourceInfo.id);
            Assert.AreEqual(0u, paths[1].sourceInfo.id);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildSourceIdMap_DuplicateSourceIdsAcrossAdapters_RetainsBoth()
        {
            var adapterA = Luid(1);
            var adapterB = Luid(2);

            var map = DisplayConfigHelper.BuildSourceIdMap(new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(adapterA, 0, 0),
                Display(adapterB, 0, 0)
            });

            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(0u, map[CcdAddress.Source(adapterA, 0)]);
            Assert.AreEqual(0u, map[CcdAddress.Source(adapterB, 0)]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_DuplicateTargetIds_IgnoresTransientAdapterAndReturnsNull()
        {
            var adapterA = Luid(1);
            var adapterB = Luid(2);
            var setting = new DisplaySetting
            {
                AdapterLuid = adapterB,
                TargetId = 0,
                ReadableDeviceName = "Stored B"
            };

            var resolved = DisplayConfigHelper.ResolveLiveDisplay(setting, new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(adapterA, 0, 0),
                Display(adapterB, 0, 3)
            });

            Assert.IsNull(resolved);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_EdidMoveAcrossAdapters_ReturnsCompleteLiveAddress()
        {
            var adapterA = Luid(1);
            var adapterB = Luid(2);
            var setting = new DisplaySetting
            {
                AdapterLuid = adapterA,
                TargetId = 0,
                ManufacturerName = "MAN",
                ProductCodeID = "1234",
                ReadableDeviceName = "Moved"
            };
            var oldPort = Display(adapterA, 0, 0, "DEV", "9999");
            var moved = Display(adapterB, 7, 4, "MAN", "1234");
            moved.RawTargetId = 0x10007;
            moved.PathIndex = 8;

            var resolved = DisplayConfigHelper.ResolveLiveDisplay(setting, new List<DisplayConfigHelper.DisplayConfigInfo> { oldPort, moved });

            Assert.AreSame(moved, resolved);
            Assert.IsTrue(CcdAddress.LuidEquals(adapterB, resolved.AdapterId));
            Assert.AreEqual(7u, resolved.TargetId);
            Assert.AreEqual(0x10007u, resolved.RawTargetId);
            Assert.AreEqual(4u, resolved.SourceId);
            Assert.AreEqual(8u, resolved.PathIndex);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCurrentApplyAddress_ExactAddressMatchingEdid_UsesExactAddress()
        {
            var adapter = Luid(0x111);
            var setting = Setting(adapter, 33, 7, true, "MAN", "1234");
            var exact = Display(adapter, 33, 9, "MAN", "1234");
            var other = Display(Luid(2), 33, 3, "DEV", "9999");

            var resolved = DisplayConfigHelper.ResolveCurrentApplyAddressDetailed(setting, new List<DisplayConfigHelper.DisplayConfigInfo> { other, exact }, out var status);

            Assert.AreSame(exact, resolved);
            Assert.AreEqual(DisplayConfigHelper.CurrentAddressResolutionStatus.Resolved, status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCurrentApplyAddress_ExactAddressMismatchedEdid_RetainsLocationRoleFallback()
        {
            var adapter = Luid(0x111);
            var setting = Setting(adapter, 33, 7, true, "MAN", "1234");
            var exact = Display(adapter, 33, 9, "MAN", "5678");

            var resolved = DisplayConfigHelper.ResolveCurrentApplyAddressDetailed(setting, new List<DisplayConfigHelper.DisplayConfigInfo> { exact }, out var status);

            Assert.AreSame(exact, resolved);
            Assert.AreEqual(DisplayConfigHelper.CurrentAddressResolutionStatus.Resolved, status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCurrentApplyAddress_ContradictoryExactAddressUniqueEdid_FollowsPanelBeforeFallback()
        {
            var oldAdapter = Luid(0x111);
            var newAdapter = Luid(0x222);
            var setting = Setting(oldAdapter, 33, 7, true, "MAN", "1234");
            var exact = Display(oldAdapter, 33, 9, "MAN", "5678");
            var moved = Display(newAdapter, 44, 3, "MAN", "1234");

            var resolved = DisplayConfigHelper.ResolveCurrentApplyAddressDetailed(setting, new List<DisplayConfigHelper.DisplayConfigInfo> { exact, moved }, out var status);

            Assert.AreSame(moved, resolved);
            Assert.AreEqual(DisplayConfigHelper.CurrentAddressResolutionStatus.Resolved, status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCurrentApplyAddress_StaleAddressDuplicateEdidTwins_ReturnsAmbiguous()
        {
            var setting = Setting(Luid(0x111), 999, 7, true, "MAN", "1234");
            var live = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(Luid(1), 33, 1, "MAN", "1234"),
                Display(Luid(2), 44, 2, "MAN", "1234")
            };

            var resolved = DisplayConfigHelper.ResolveCurrentApplyAddressDetailed(setting, live, out var status);

            Assert.IsNull(resolved);
            Assert.AreEqual(DisplayConfigHelper.CurrentAddressResolutionStatus.Ambiguous, status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCurrentApplyAddress_UniqueTargetWithoutEdid_UsesGenericPortFallback()
        {
            var setting = Setting(Luid(0x111), 33, 7, true);
            var current = Display(Luid(0x222), 33, 99);
            current.IsEnabled = false;

            var resolved = DisplayConfigHelper.ResolveCurrentApplyAddressDetailed(setting, new List<DisplayConfigHelper.DisplayConfigInfo> { current }, out var status);

            Assert.AreSame(current, resolved);
            Assert.AreEqual(DisplayConfigHelper.CurrentAddressResolutionStatus.Resolved, status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCurrentApplyAddress_DuplicateTargetsWithoutIdentity_ReturnsAmbiguous()
        {
            var setting = Setting(Luid(0x111), 33, 7, true);
            var live = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(Luid(1), 33, 1),
                Display(Luid(2), 33, 2)
            };

            var resolved = DisplayConfigHelper.ResolveCurrentApplyAddressDetailed(setting, live, out var status);

            Assert.IsNull(resolved);
            Assert.AreEqual(DisplayConfigHelper.CurrentAddressResolutionStatus.Ambiguous, status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveLiveDisplay_DuplicateTargetsWithUniqueEdid_FollowsIdentity()
        {
            var setting = Setting(Luid(99), 33, 7, true, "MAN", "1234");
            var other = Display(Luid(1), 33, 1, "DEV", "9999");
            var match = Display(Luid(2), 33, 2, "MAN", "1234");

            var resolved = DisplayConfigHelper.ResolveLiveDisplay(
                setting,
                new List<DisplayConfigHelper.DisplayConfigInfo> { other, match });

            Assert.AreSame(match, resolved);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveCurrentApplyAddress_StaleAdapterUniqueEdidInactiveTarget_FollowsCurrentAddress()
        {
            var setting = Setting(Luid(0x111), 33, 7, true, "MAN", "1234");
            var current = Display(Luid(0x222), 33, 99, "MAN", "1234");
            current.IsEnabled = false;

            var resolved = DisplayConfigHelper.ResolveCurrentApplyAddressDetailed(setting, new List<DisplayConfigHelper.DisplayConfigInfo> { current }, out var status);

            Assert.AreSame(current, resolved);
            Assert.AreEqual(DisplayConfigHelper.CurrentAddressResolutionStatus.Resolved, status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryMapDisplaySettingsForApply_EmptyAddressSnapshot_FailsClosed()
        {
            var setting = Setting(Luid(0x111), 33, 7, true, "MAN", "1234");

            bool allSafe = ProfileManager.TryMapDisplaySettingsForApply(
                new[] { setting },
                new List<DisplayConfigHelper.DisplayConfigInfo>(),
                new List<DisplayConfigHelper.DisplayConfigInfo>(),
                out var executionView);

            Assert.IsFalse(allSafe);
            Assert.AreEqual(0, executionView.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CanonicalizeDisplayEndpoints_AlternateRoutes_PrefersActiveRepresentative()
        {
            var adapter = Luid(0x222);
            var inactive = Display(adapter, 33, 8, "MAN", "1234");
            inactive.IsEnabled = false;
            inactive.PathIndex = 2;
            var active = Display(adapter, 33, 1, "MAN", "1234");
            active.PathIndex = 9;

            var endpoints = DisplayConfigHelper.CanonicalizeDisplayEndpoints(new List<DisplayConfigHelper.DisplayConfigInfo> { inactive, active });

            Assert.AreEqual(1, endpoints.Count);
            Assert.AreSame(active, endpoints[0]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TryMapDisplaySettingsForApply_AmbiguousIdentity_FailsClosedBeforeTopologyExecution()
        {
            var safe = Setting(Luid(1), 11, 0, true, "MAN", "1001");
            var ambiguous = Setting(Luid(0x111), 999, 7, true, "MAN", "1234");
            var addresses = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(Luid(1), 11, 0, "MAN", "1001"),
                Display(Luid(1), 33, 1, "MAN", "1234"),
                Display(Luid(2), 44, 2, "MAN", "1234")
            };

            bool allSafe = ProfileManager.TryMapDisplaySettingsForApply(
                new[] { safe, ambiguous },
                new List<DisplayConfigHelper.DisplayConfigInfo>(),
                addresses,
                out var executionView);

            Assert.IsFalse(allSafe);
            Assert.AreEqual(1, executionView.Count);
            Assert.AreEqual(11u, executionView[0].TargetId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MapDisplaySettingForApply_InactiveCurrentAddress_UsesLivePathAndSyntheticSourceGroup()
        {
            var staleAdapter = Luid(0x111);
            var currentAdapter = Luid(0x222);
            var setting = Setting(staleAdapter, 33, 42, true, "MAN", "1234");
            var address = Display(currentAdapter, 33, 9, "MAN", "1234");
            address.IsEnabled = false;
            address.RawTargetId = 0x10000u | 33u;
            address.PathIndex = 6;

            var mapped = ProfileManager.MapDisplaySettingForApply(
                setting,
                new List<DisplayConfigHelper.DisplayConfigInfo>(),
                new List<DisplayConfigHelper.DisplayConfigInfo> { address });

            Assert.IsTrue(CcdAddress.LuidEquals(currentAdapter, mapped.AdapterId));
            Assert.AreEqual(33u, mapped.TargetId);
            Assert.AreEqual(0x10000u | 33u, mapped.RawTargetId);
            Assert.AreEqual(6u, mapped.PathIndex);
            Assert.AreEqual(0u, mapped.SourceId, "A single independent display receives one synthetic per-apply source group.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TransientAdapter_SingleTargetTopology_EnablesOnlyDesiredTarget()
        {
            var staleAdapter = Luid(0x111);
            var currentAdapter = Luid(0x222);
            var settings = new[]
            {
                Setting(staleAdapter, 11, 0, false, "MAN", "1001"),
                Setting(staleAdapter, 22, 1, false, "MAN", "1002"),
                Setting(staleAdapter, 33, 2, true, "MAN", "1003")
            };
            var addresses = CurrentAddresses(currentAdapter);
            var active = addresses.FindAll(d => d.IsEnabled);
            var mapped = Map(settings, active, addresses);
            var paths = CurrentPaths(currentAdapter);

            DisplayConfigHelper.PreparePathsForTopology(paths, mapped);

            Assert.AreEqual(1, System.Array.FindAll(paths, p => (p.flags & (uint)DisplayConfigHelper.DisplayConfigPathInfoFlags.Active) != 0).Length);
            Assert.AreEqual(0u, paths[0].flags & (uint)DisplayConfigHelper.DisplayConfigPathInfoFlags.Active);
            Assert.AreEqual(0u, paths[1].flags & (uint)DisplayConfigHelper.DisplayConfigPathInfoFlags.Active);
            Assert.AreNotEqual(0u, paths[2].flags & (uint)DisplayConfigHelper.DisplayConfigPathInfoFlags.Active);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TransientAdapter_AllTargetsTopology_RequiresUpdateForInactiveDesiredTarget()
        {
            var staleAdapter = Luid(0x111);
            var currentAdapter = Luid(0x222);
            var settings = new[]
            {
                Setting(staleAdapter, 11, 0, true, "MAN", "1001"),
                Setting(staleAdapter, 22, 1, true, "MAN", "1002"),
                Setting(staleAdapter, 33, 2, true, "MAN", "1003")
            };
            var addresses = CurrentAddresses(currentAdapter);
            var mapped = Map(settings, addresses.FindAll(d => d.IsEnabled), addresses);

            Assert.IsTrue(DisplayConfigHelper.TopologyRequiresUpdate(CurrentPaths(currentAdapter), mapped));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TransientAdapter_PartialTopology_AlreadyMatchingRemainsNoOp()
        {
            var staleAdapter = Luid(0x111);
            var currentAdapter = Luid(0x222);
            var settings = new[]
            {
                Setting(staleAdapter, 11, 0, true, "MAN", "1001"),
                Setting(staleAdapter, 22, 1, true, "MAN", "1002"),
                Setting(staleAdapter, 33, 2, false, "MAN", "1003")
            };
            var addresses = CurrentAddresses(currentAdapter);
            var mapped = Map(settings, addresses.FindAll(d => d.IsEnabled), addresses);

            Assert.IsFalse(DisplayConfigHelper.TopologyRequiresUpdate(CurrentPaths(currentAdapter), mapped));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MapDisplaySettingForApply_CurrentExtendedRoutes_UsesCloneGroupIdNotCapturedSourceId()
        {
            var staleAdapter = Luid(0x111);
            var currentAdapter = Luid(0x222);
            var first = Setting(staleAdapter, 11, 42, true, "MAN", "1001");
            var second = Setting(staleAdapter, 22, 77, true, "MAN", "1002");
            first.CloneGroupId = "clone-a";
            first.IsCloneSource = true;
            second.CloneGroupId = "clone-a";
            var addresses = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(currentAdapter, 11, 0, "MAN", "1001"),
                Display(currentAdapter, 22, 1, "MAN", "1002")
            };
            var mapped = Map(new[] { first, second }, addresses, addresses);
            var paths = new[] { Path(currentAdapter, 11, 0), Path(currentAdapter, 22, 1) };

            DisplayConfigHelper.PreparePathsForTopology(paths, mapped);

            Assert.AreEqual(paths[0].sourceInfo.id, paths[1].sourceInfo.id);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveHardwareBackfillDisplay_AmbiguousTargetOnly_ReturnsNull()
        {
            var setting = new DisplaySetting { TargetId = 0, ReadableDeviceName = "Legacy" };
            var live = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(Luid(1), 0, 0),
                Display(Luid(2), 0, 0)
            };

            Assert.IsNull(ProfileManager.ResolveHardwareBackfillDisplay(setting, live));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_DuplicateTargets_UsesIntendedAdapter()
        {
            var adapterA = Luid(1);
            var adapterB = Luid(2);
            var profile = Display(adapterB, 0, 0);
            profile.IsHdrSupported = true;
            profile.IsHdrEnabled = true;
            var liveA = Display(adapterA, 0, 0);
            liveA.IsHdrSupported = true;
            var liveB = Display(adapterB, 0, 0);
            liveB.IsHdrSupported = true;
            DisplayConfigHelper.LUID calledAdapter = default;

            bool result = DisplayConfigHelper.ApplyAdvancedColorState(
                new List<DisplayConfigHelper.DisplayConfigInfo> { profile },
                new List<DisplayConfigHelper.DisplayConfigInfo> { liveA, liveB },
                isWindows24H2OrGreater: true,
                (adapter, _, _) => { calledAdapter = adapter; return true; },
                (_, _, _) => true,
                (_, _, _) => true);

            Assert.IsTrue(result);
            Assert.IsTrue(CcdAddress.LuidEquals(adapterB, calledAdapter));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyColorProfiles_DuplicateTargets_PassesIntendedTargetAddress()
        {
            var adapterA = Luid(1);
            var adapterB = Luid(2);
            var profile = Display(adapterB, 0, 0);
            profile.ColorProfile = "test.icc";
            var liveA = Display(adapterA, 0, 1);
            var liveB = Display(adapterB, 0, 5);
            DisplaySetting captured = null;

            bool result = DisplayConfigHelper.ApplyColorProfiles(
                new List<DisplayConfigHelper.DisplayConfigInfo> { profile },
                new List<DisplayConfigHelper.DisplayConfigInfo> { liveA, liveB },
                (setting, _) => { captured = setting; return true; });

            Assert.IsTrue(result);
            Assert.IsNotNull(captured);
            Assert.IsTrue(CcdAddress.LuidEquals(adapterB, captured.AdapterLuid));
            Assert.AreEqual(0u, captured.TargetId);
        }

        private static DisplaySetting Setting(
            DisplayConfigHelper.LUID adapterId,
            uint targetId,
            uint _legacySourceId,
            bool enabled,
            string manufacturer = "",
            string product = "") =>
            new DisplaySetting
            {
                AdapterLuid = adapterId,
                TargetId = targetId,
                IsEnabled = enabled,
                ReadableDeviceName = $"Target {targetId}",
                ManufacturerName = manufacturer,
                ProductCodeID = product
            };

        private static List<DisplayConfigHelper.DisplayConfigInfo> CurrentAddresses(DisplayConfigHelper.LUID adapterId)
        {
            var result = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                Display(adapterId, 11, 0, "MAN", "1001"),
                Display(adapterId, 22, 1, "MAN", "1002"),
                Display(adapterId, 33, 2, "MAN", "1003")
            };
            result[2].IsEnabled = false;
            return result;
        }

        private static List<DisplayConfigHelper.DisplayConfigInfo> Map(
            DisplaySetting[] settings,
            List<DisplayConfigHelper.DisplayConfigInfo> active,
            List<DisplayConfigHelper.DisplayConfigInfo> addresses)
        {
            bool safe = ProfileManager.TryMapDisplaySettingsForApply(settings, active, addresses, out var result);
            Assert.IsTrue(safe);
            return result;
        }

        private static DisplayConfigHelper.DisplayConfigPathInfo[] CurrentPaths(DisplayConfigHelper.LUID adapterId)
        {
            var paths = new[] { Path(adapterId, 11, 0), Path(adapterId, 22, 1), Path(adapterId, 33, 2) };
            paths[2].flags = 0;
            return paths;
        }

        private static DisplayConfigHelper.LUID Luid(uint lowPart) => new DisplayConfigHelper.LUID { LowPart = lowPart, HighPart = 0 };

        private static DisplayConfigHelper.DisplayConfigInfo Display(
            DisplayConfigHelper.LUID adapterId,
            uint targetId,
            uint sourceId,
            string manufacturer = "",
            string product = "") =>
            new DisplayConfigHelper.DisplayConfigInfo
            {
                AdapterId = adapterId,
                TargetId = targetId,
                RawTargetId = targetId,
                SourceId = sourceId,
                IsEnabled = true,
                ManufacturerName = manufacturer,
                ProductCodeID = product,
                FriendlyName = $"{CcdAddress.FormatLuid(adapterId)}:{targetId}"
            };

        private static DisplayConfigHelper.DisplayConfigPathInfo Path(DisplayConfigHelper.LUID adapterId, uint targetId, uint sourceId) =>
            new DisplayConfigHelper.DisplayConfigPathInfo
            {
                sourceInfo = new DisplayConfigHelper.DisplayConfigPathSourceInfo
                {
                    adapterId = adapterId,
                    id = sourceId,
                    modeInfoIdx = 0xFFFFFFFF
                },
                targetInfo = new DisplayConfigHelper.DisplayConfigPathTargetInfo
                {
                    adapterId = adapterId,
                    id = targetId,
                    modeInfoIdx = 0xFFFFFFFF
                },
                flags = (uint)DisplayConfigHelper.DisplayConfigPathInfoFlags.Active
            };
    }
}
