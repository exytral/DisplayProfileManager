using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.UI.Controls;
using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class AdvancedColorCapabilityTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Info2_WcgOnlyTarget_ExposesWcgWithoutHdr()
        {
            var mapped = Map(
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.WideColorSupported,
                DisplayConfigNative.DisplayConfigAdvancedColorMode.Wcg);

            Assert.IsFalse(mapped.IsHdrSupported);
            Assert.IsTrue(mapped.IsWcgSupported);
            Assert.IsFalse(mapped.IsHdrEnabled);
            Assert.IsTrue(mapped.IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Info2_HdrAndWcgCapableTarget_MapsBothCapabilitiesIndependently()
        {
            var mapped = Map(
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.HighDynamicRangeSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.WideColorSupported,
                DisplayConfigNative.DisplayConfigAdvancedColorMode.Hdr);

            Assert.IsTrue(mapped.IsHdrSupported);
            Assert.IsTrue(mapped.IsWcgSupported);
            Assert.IsTrue(mapped.IsHdrEnabled);
            Assert.IsFalse(mapped.IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Info2_GenericAdvancedColorBit_AloneDoesNotInventHdrOrWcgCapability()
        {
            var mapped = Map(
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported,
                DisplayConfigNative.DisplayConfigAdvancedColorMode.Sdr);

            Assert.IsFalse(mapped.IsHdrSupported);
            Assert.IsFalse(mapped.IsWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Info2_PolicyLimitedTarget_HidesHdrAndWcgCapabilities()
        {
            var mapped = Map(
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.HighDynamicRangeSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.WideColorSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorLimitedByPolicy,
                DisplayConfigNative.DisplayConfigAdvancedColorMode.Sdr);

            Assert.IsFalse(mapped.IsHdrSupported);
            Assert.IsFalse(mapped.IsWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EditorWcgAvailability_DependsOnWcgCapabilityNotHdrCapability()
        {
            Assert.IsTrue(DisplaySettingControl.IsWcgControlAvailable(usesDedicatedWcgApi: true, isWcgSupported: true));
            Assert.IsFalse(DisplaySettingControl.IsWcgControlAvailable(usesDedicatedWcgApi: true, isWcgSupported: false));
            Assert.IsFalse(DisplaySettingControl.IsWcgControlAvailable(usesDedicatedWcgApi: false, isWcgSupported: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EditorReconciliation_RefreshesIndependentHdrAndWcgCapabilitiesFromLiveTarget()
        {
            var setting = new DisplaySetting { IsHdrSupported = true, IsWcgSupported = false };
            var live = new DisplayConfigInfo { IsHdrSupported = false, IsWcgSupported = true };

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(setting, live);

            Assert.IsFalse(setting.IsHdrSupported);
            Assert.IsTrue(setting.IsWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AdvancedColorCapabilities_AreLiveOnlyAndDoNotChangeSchema6Json()
        {
            string json = JsonConvert.SerializeObject(new DisplaySetting
            {
                IsHdrSupported = true,
                IsWcgSupported = true,
                IsHdrEnabled = true,
                IsWcgEnabled = true
            });

            Assert.IsFalse(json.Contains("isHdrSupported"));
            Assert.IsFalse(json.Contains("isWcgSupported"));
            StringAssert.Contains(json, "isHdrEnabled");
            StringAssert.Contains(json, "isWcgEnabled");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_WcgOnlyTarget_CanEnterWcgWithoutHdrSupport()
        {
            var operations = new List<string>();
            var desired = new DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrEnabled = false,
                IsWcgEnabled = true,
                TargetId = 9,
            };
            var live = new DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrSupported = false,
                IsWcgSupported = true,
                IsHdrEnabled = false,
                IsWcgEnabled = false,
                TargetId = 9,
                RawTargetId = 9,
                FriendlyName = "WCG-only",
            };

            bool result = AdvancedColorService.ApplyAdvancedColorState(
                [desired],
                [live],
                true,
                (_, _, enable) => { operations.Add($"HDR:{enable}"); return true; },
                (_, _, enable) => { operations.Add($"WCG:{enable}"); return true; },
                (_, _, _) => true);

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "WCG:True" }, operations);
        }

        private static DisplayConfigInfo Map(
            DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags flags,
            DisplayConfigNative.DisplayConfigAdvancedColorMode mode)
        {
            var target = new DisplayConfigInfo();
            AdvancedColorService.ApplyAdvancedColorInfo2(target, new DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2
            {
                values = flags,
                activeColorMode = mode,
                colorEncoding = DisplayConfigNative.DisplayConfigColorEncoding.Rgb,
                bitsPerColorChannel = 10,
            });
            return target;
        }
    }
}
