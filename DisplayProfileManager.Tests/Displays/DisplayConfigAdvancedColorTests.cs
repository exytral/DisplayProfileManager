using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayConfigAdvancedColorTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void AdvancedColorInfo2_Layout_MatchesNativeAbi()
        {
            Assert.AreEqual(0, Marshal.OffsetOf<DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2.header)).ToInt32());
            Assert.AreEqual(20, Marshal.OffsetOf<DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2.values)).ToInt32());
            Assert.AreEqual(24, Marshal.OffsetOf<DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2.colorEncoding)).ToInt32());
            Assert.AreEqual(28, Marshal.OffsetOf<DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2.bitsPerColorChannel)).ToInt32());
            Assert.AreEqual(32, Marshal.OffsetOf<DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2>(nameof(DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2.activeColorMode)).ToInt32());
            Assert.AreEqual(36, Marshal.SizeOf<DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2>());
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
                var info = Marshal.PtrToStructure<DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2>(handle.AddrOfPinnedObject());
                Assert.AreEqual(bitsSentinel, info.bitsPerColorChannel);
                Assert.AreEqual((DisplayConfigNative.DisplayConfigAdvancedColorMode)modeSentinel, info.activeColorMode);
            }
            finally
            {
                handle.Free();
            }
        }

        [TestMethod]
        [DataRow(false, false, DisplayConfigNative.DisplayConfigAdvancedColorMode.Sdr)]
        [DataRow(false, true, DisplayConfigNative.DisplayConfigAdvancedColorMode.Wcg)]
        [DataRow(true, false, DisplayConfigNative.DisplayConfigAdvancedColorMode.Hdr)]
        [DataRow(true, true, DisplayConfigNative.DisplayConfigAdvancedColorMode.Hdr)]
        [TestCategory("Unit")]
        public void GetEffectiveAdvancedColorMode_CollapsesToThreeDestinations(bool hdr, bool wcg, DisplayConfigNative.DisplayConfigAdvancedColorMode expected)
        {
            Assert.AreEqual(expected, AdvancedColorService.GetEffectiveAdvancedColorMode(hdr, wcg));
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
            Assert.AreEqual(AdvancedColorService.LegacyAcmMutationResult.Applied, AdvancedColorService.ClassifyLegacyAcmMutationResult(true, true, true));
            Assert.AreEqual(AdvancedColorService.LegacyAcmMutationResult.NonFatalProviderSkip, AdvancedColorService.ClassifyLegacyAcmMutationResult(false, true, true));
            Assert.AreEqual(AdvancedColorService.LegacyAcmMutationResult.Failed, AdvancedColorService.ClassifyLegacyAcmMutationResult(false, true, false));
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
        public void ApplyAdvancedColorState_Pre24H2CapturedHdrRestore_UsesHdrSetterOnly()
        {
            var profile = CaptureLegacy(
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled,
                DisplayConfigNative.DisplayConfigColorEncoding.Rgb);
            var live = new DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrSupported = true,
                IsWcgSupported = false,
                IsHdrEnabled = false,
                IsWcgEnabled = false,
                TargetId = 7,
                RawTargetId = 7,
                FriendlyName = "Legacy HDR",
            };
            var probe = new SetterProbe();

            bool result = AdvancedColorService.ApplyAdvancedColorState(
                [profile],
                [live],
                false,
                probe.SetHdr,
                probe.SetWcg,
                probe.SetLegacyAcm);

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "HDR:on" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_Pre24H2CapturedWcgRestore_UsesLegacyAcmSetter()
        {
            var profile = CaptureLegacy(
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.WideColorEnforced |
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled,
                DisplayConfigNative.DisplayConfigColorEncoding.Rgb);
            var live = new DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrSupported = false,
                IsWcgSupported = true,
                IsHdrEnabled = false,
                IsWcgEnabled = false,
                TargetId = 7,
                RawTargetId = 7,
                FriendlyName = "Legacy WCG",
            };
            var probe = new SetterProbe();

            bool result = AdvancedColorService.ApplyAdvancedColorState(
                [profile],
                [live],
                false,
                probe.SetHdr,
                probe.SetWcg,
                probe.SetLegacyAcm);

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "ACM:on" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_Pre24H2RealAcmFailure_ReturnsFalse()
        {
            var probe = new SetterProbe { LegacyAcmResult = false };

            Assert.IsFalse(ApplyPolicy(false, true, false, false, false, probe, liveHdrSupported: false, liveWcgSupported: true));

            CollectionAssert.AreEqual(new[] { "ACM:on" }, probe.Operations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_Pre24H2UnsupportedWcgIntent_FailsWithoutSetter()
        {
            var probe = new SetterProbe();

            Assert.IsFalse(ApplyPolicy(false, true, false, false, false, probe, liveHdrSupported: false, liveWcgSupported: false));

            Assert.AreEqual(0, probe.Operations.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_Pre24H2StaleWcgIntentOnHdrProvider_RemainsNonFatalProviderSkip()
        {
            var probe = new SetterProbe { LegacyAcmResult = false };

            Assert.IsTrue(ApplyPolicy(false, true, false, false, false, probe, liveHdrSupported: true, liveWcgSupported: false));

            CollectionAssert.AreEqual(new[] { "ACM:on" }, probe.Operations);
        }

        [TestMethod]
        [DataRow(0, 0, true, 2)]
        [DataRow(5, 0, false, 1)]
        [DataRow(5, 5, false, 1)]
        [DataRow(0, 5, false, 2)]
        [TestCategory("Unit")]
        public void SetAdvancedColorState_LegacyAcmResetNativeStatus_PreventsFalseSuccess(int offStatus, int onStatus, bool expected, int expectedCalls)
        {
            var calls = new List<bool>();
            bool result = AdvancedColorService.SetAdvancedColorState(
                default(AdapterLuid), 7, AdvancedColorService.DisplayConfigColorIntent.Acm,
                enabled =>
                {
                    calls.Add(enabled);
                    return enabled ? onStatus : offStatus;
                });

            Assert.AreEqual(expected, result);
            Assert.AreEqual(expectedCalls, calls.Count);
            Assert.IsFalse(calls[0]);
            if (expectedCalls == 2)
                Assert.IsTrue(calls[1]);
        }

        [TestMethod]
        [DataRow(AdvancedColorService.DisplayConfigColorIntent.Hdr, true)]
        [DataRow(AdvancedColorService.DisplayConfigColorIntent.Off, false)]
        [TestCategory("Unit")]
        public void SetAdvancedColorState_HdrAndOff_DirectSingleCall(AdvancedColorService.DisplayConfigColorIntent intent, bool enabled)
        {
            var calls = new List<bool>();
            Assert.IsTrue(AdvancedColorService.SetAdvancedColorState(default(AdapterLuid), 7, intent,
                value => { calls.Add(value); return 0; }));
            Assert.AreEqual(1, calls.Count);
            Assert.AreEqual(enabled, calls[0]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SetAdvancedColorState_ProviderSkipClassifier_RemainsNonFatalOnlyOnHdrCapableTarget()
        {
            Assert.AreEqual(AdvancedColorService.LegacyAcmMutationResult.NonFatalProviderSkip,
                AdvancedColorService.ClassifyLegacyAcmMutationResult(false, true, true));
            Assert.AreEqual(AdvancedColorService.LegacyAcmMutationResult.Failed,
                AdvancedColorService.ClassifyLegacyAcmMutationResult(false, true, false));
        }

        private static bool ApplyPolicy(
            bool desiredHdr,
            bool desiredWcg,
            bool liveHdr,
            bool liveWcg,
            bool isWindows24H2OrGreater,
            SetterProbe probe,
            bool liveHdrSupported = true,
            bool profileHdrSupported = false,
            bool? liveWcgSupported = null)
        {
            const uint targetId = 7;
            var profile = new DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrSupported = profileHdrSupported,
                IsHdrEnabled = desiredHdr,
                IsWcgEnabled = desiredWcg,
                TargetId = targetId,
            };
            var live = new DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrSupported = liveHdrSupported,
                IsWcgSupported = liveWcgSupported ?? isWindows24H2OrGreater,
                IsHdrEnabled = liveHdr,
                IsWcgEnabled = liveWcg,
                TargetId = targetId,
                RawTargetId = targetId,
                FriendlyName = "Test Display",
            };

            return AdvancedColorService.ApplyAdvancedColorState(
                [profile],
                [live],
                isWindows24H2OrGreater,
                probe.SetHdr,
                probe.SetWcg,
                probe.SetLegacyAcm);
        }

        private static DisplayConfigInfo CaptureLegacy(
            DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags flags,
            DisplayConfigNative.DisplayConfigColorEncoding encoding)
        {
            var target = new DisplayConfigInfo
            {
                IsEnabled = true,
                TargetId = 7,
                RawTargetId = 7,
                FriendlyName = "Captured Legacy",
            };

            AdvancedColorService.CaptureAdvancedColorState(
                target,
                default,
                7,
                useModernContract: false,
                null,
                (DisplayConfigNative.LUID adapterId, uint targetId, out DisplayConfigNative.DisplayConfigGetAdvancedColorInfo info) =>
                {
                    info = new DisplayConfigNative.DisplayConfigGetAdvancedColorInfo
                    {
                        values = flags,
                        colorEncoding = encoding,
                        bitsPerColorChannel = 10,
                    };
                    return true;
                });

            return target;
        }

        private sealed class SetterProbe
        {
            public List<string> Operations { get; } = [];
            public bool HdrResult { get; set; } = true;
            public bool WcgResult { get; set; } = true;
            public bool LegacyAcmResult { get; set; } = true;

            public bool SetHdr(AdapterLuid adapterId, uint rawTargetId, bool enable)
            {
                Operations.Add($"HDR:{(enable ? "on" : "off")}");
                return HdrResult;
            }

            public bool SetWcg(AdapterLuid adapterId, uint rawTargetId, bool enable)
            {
                Operations.Add($"WCG:{(enable ? "on" : "off")}");
                return WcgResult;
            }

            public bool SetLegacyAcm(AdapterLuid adapterId, uint rawTargetId, bool enable)
            {
                Operations.Add($"ACM:{(enable ? "on" : "off")}");
                return LegacyAcmResult;
            }
        }
    }
}
