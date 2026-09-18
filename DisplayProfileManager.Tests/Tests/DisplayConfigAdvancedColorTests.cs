using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class DisplayConfigAdvancedColorTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void AdvancedColorInfo2_Layout_MatchesNativeAbi()
        {
            Assert.AreEqual(0, Marshal.OffsetOf<DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2.header)).ToInt32());
            Assert.AreEqual(20, Marshal.OffsetOf<DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2.values)).ToInt32());
            Assert.AreEqual(24, Marshal.OffsetOf<DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2.colorEncoding)).ToInt32());
            Assert.AreEqual(28, Marshal.OffsetOf<DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2.bitsPerColorChannel)).ToInt32());
            Assert.AreEqual(32, Marshal.OffsetOf<DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2.activeColorMode)).ToInt32());
            Assert.AreEqual(36, Marshal.SizeOf<DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2>());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AdvancedColorInfo2_NativeBuffer_MapsTrailingFieldsByOffset()
        {
            const uint bitsSentinel = 0x11223344;
            const uint modeSentinel = 0x55667788;
            var buffer = new byte[36];
            BitConverter.GetBytes(bitsSentinel).CopyTo(buffer, 28);
            BitConverter.GetBytes(modeSentinel).CopyTo(buffer, 32);
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var info = Marshal.PtrToStructure<DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2>(handle.AddrOfPinnedObject());
                Assert.AreEqual(bitsSentinel, info.bitsPerColorChannel);
                Assert.AreEqual((DisplayConfigHelper.DisplayConfigAdvancedColorMode)modeSentinel, info.activeColorMode);
            }
            finally
            {
                handle.Free();
            }
        }

        [TestMethod]
        [DataRow(false, false, DisplayConfigHelper.DisplayConfigAdvancedColorMode.Sdr)]
        [DataRow(false, true, DisplayConfigHelper.DisplayConfigAdvancedColorMode.Wcg)]
        [DataRow(true, false, DisplayConfigHelper.DisplayConfigAdvancedColorMode.Hdr)]
        [DataRow(true, true, DisplayConfigHelper.DisplayConfigAdvancedColorMode.Hdr)]
        [TestCategory("Unit")]
        public void GetEffectiveAdvancedColorMode_CollapsesToThreeDestinations(bool hdr, bool wcg, DisplayConfigHelper.DisplayConfigAdvancedColorMode expected)
        {
            Assert.AreEqual(expected, DisplayConfigHelper.GetEffectiveAdvancedColorMode(hdr, wcg));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_24H2ExactEffectiveMode_IsNoOp(bool hdr, bool wcg)
        {
            var probe = new SetterProbe();
            bool result = ApplyPolicy(hdr, wcg, hdr, wcg, true, probe);
            Assert.IsTrue(result);
            Assert.AreEqual(0, probe.Operations.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_24H2LegacyHdrWithWcgBit_IsStillHdrNoOp()
        {
            var probe = new SetterProbe();
            Assert.IsTrue(ApplyPolicy(true, false, true, true, true, probe));
            Assert.AreEqual(0, probe.Operations.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_24H2SdrToWcg_UsesOnlyWcgSetter()
        {
            var probe = new SetterProbe();
            Assert.IsTrue(ApplyPolicy(false, true, false, false, true, probe));
            CollectionAssert.AreEqual(new[] { "WCG:on" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_24H2WcgToSdr_UsesOnlyWcgSetter()
        {
            var probe = new SetterProbe();
            Assert.IsTrue(ApplyPolicy(false, false, false, true, true, probe));
            CollectionAssert.AreEqual(new[] { "WCG:off" }, probe.Operations);
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_24H2ToHdr_NeverMutatesWcg(bool liveHdr, bool liveWcg)
        {
            var probe = new SetterProbe();
            Assert.IsTrue(ApplyPolicy(true, true, liveHdr, liveWcg, true, probe));
            CollectionAssert.AreEqual(new[] { "HDR:on" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_24H2HdrToSdr_DisablesHdrThenExplicitlyDisablesWcg()
        {
            var probe = new SetterProbe();
            Assert.IsTrue(ApplyPolicy(false, false, true, false, true, probe));
            CollectionAssert.AreEqual(new[] { "HDR:off", "WCG:off" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_24H2HdrToWcg_DisablesHdrThenEnablesWcg()
        {
            var probe = new SetterProbe();
            Assert.IsTrue(ApplyPolicy(false, true, true, true, true, probe));
            CollectionAssert.AreEqual(new[] { "HDR:off", "WCG:on" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_24H2HdrSetterFailure_BlocksDependentWcgMutation()
        {
            var probe = new SetterProbe { HdrResult = false };
            Assert.IsFalse(ApplyPolicy(false, true, true, false, true, probe));
            CollectionAssert.AreEqual(new[] { "HDR:off" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_24H2WcgSetterFailure_PropagatesFailure()
        {
            var probe = new SetterProbe { WcgResult = false };
            Assert.IsFalse(ApplyPolicy(false, true, false, false, true, probe));
            CollectionAssert.AreEqual(new[] { "WCG:on" }, probe.Operations);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_OldHdrProfileVariants_BothResolveToHdrWithoutWcgMutation(bool oldAcmValue)
        {
            var probe = new SetterProbe();
            Assert.IsTrue(ApplyPolicy(true, oldAcmValue, false, false, true, probe));
            CollectionAssert.AreEqual(new[] { "HDR:on" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClassifyLegacyAcmMutationResult_DistinguishesProviderSkipAndFailure()
        {
            Assert.AreEqual(DisplayConfigHelper.LegacyAcmMutationResult.Applied, DisplayConfigHelper.ClassifyLegacyAcmMutationResult(true, true, true));
            Assert.AreEqual(DisplayConfigHelper.LegacyAcmMutationResult.NonFatalProviderSkip, DisplayConfigHelper.ClassifyLegacyAcmMutationResult(false, true, true));
            Assert.AreEqual(DisplayConfigHelper.LegacyAcmMutationResult.Failed, DisplayConfigHelper.ClassifyLegacyAcmMutationResult(false, true, false));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_Pre24H2HdrIntent_UsesLiveHdrCapabilityWithoutLegacyAcmMutation()
        {
            var probe = new SetterProbe();
            Assert.IsTrue(ApplyPolicy(true, false, false, false, false, probe, liveHdrSupported: true, profileHdrSupported: false));
            CollectionAssert.AreEqual(new[] { "HDR:on" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_Pre24H2UnsupportedHdrIntent_FailsWithoutReinterpretingAsAcm()
        {
            var probe = new SetterProbe();
            Assert.IsFalse(ApplyPolicy(true, false, false, false, false, probe, liveHdrSupported: false, profileHdrSupported: true));
            Assert.AreEqual(0, probe.Operations.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_Pre24H2RealAcmFailure_ReturnsFalse()
        {
            var probe = new SetterProbe { LegacyAcmResult = false };
            Assert.IsFalse(ApplyPolicy(false, true, false, false, false, probe, false));
            CollectionAssert.AreEqual(new[] { "ACM:on" }, probe.Operations);
        }

        private static bool ApplyPolicy(bool desiredHdr, bool desiredWcg, bool liveHdr, bool liveWcg, bool isWindows24H2OrGreater, SetterProbe probe, bool liveHdrSupported = true, bool profileHdrSupported = false)
        {
            const uint targetId = 7;
            var profile = new DisplayConfigHelper.DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrSupported = profileHdrSupported,
                IsHdrEnabled = desiredHdr,
                IsWcgEnabled = desiredWcg,
                TargetId = targetId,
            };
            var live = new DisplayConfigHelper.DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrSupported = liveHdrSupported,
                IsWcgSupported = isWindows24H2OrGreater,
                IsHdrEnabled = liveHdr,
                IsWcgEnabled = liveWcg,
                TargetId = targetId,
                RawTargetId = targetId,
                FriendlyName = "Test Display",
            };

            return DisplayConfigHelper.ApplyAdvancedColorState(
                new List<DisplayConfigHelper.DisplayConfigInfo> { profile },
                new List<DisplayConfigHelper.DisplayConfigInfo> { live },
                isWindows24H2OrGreater,
                probe.SetHdr,
                probe.SetWcg,
                probe.SetLegacyAcm);
        }

        private sealed class SetterProbe
        {
            public List<string> Operations { get; } = new List<string>();
            public bool HdrResult { get; set; } = true;
            public bool WcgResult { get; set; } = true;
            public bool LegacyAcmResult { get; set; } = true;

            public bool SetHdr(DisplayConfigHelper.LUID adapterId, uint rawTargetId, bool enable)
            {
                Operations.Add($"HDR:{(enable ? "on" : "off")}");
                return HdrResult;
            }

            public bool SetWcg(DisplayConfigHelper.LUID adapterId, uint rawTargetId, bool enable)
            {
                Operations.Add($"WCG:{(enable ? "on" : "off")}");
                return WcgResult;
            }

            public bool SetLegacyAcm(DisplayConfigHelper.LUID adapterId, uint rawTargetId, bool enable)
            {
                Operations.Add($"ACM:{(enable ? "on" : "off")}");
                return LegacyAcmResult;
            }
        }
    }
}
