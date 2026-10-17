using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.UI.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public sealed class CloneLegacyAdvancedColorRegressionTests
    {
        private static ComboBox Combo(string value)
        {
            var c = new ComboBox();
            c.Items.Add(value);
            c.SelectedIndex = 0;
            return c;
        }

        private static void Field(DisplaySettingControl control, string name, object value)
        {
            var info = typeof(DisplaySettingControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, "Missing production editor field " + name);
            info.SetValue(control, value);
        }

        private static DisplaySettingControl Editor(DisplaySetting member, bool hdr, bool wcg, string resolution, List<DisplayConfigInfo> observations = null)
        {
            var c = (DisplaySettingControl)RuntimeHelpers.GetUninitializedObject(typeof(DisplaySettingControl));
            c.CloneGroupMembers = [member];
            Field(c, "_resolutionComboBox", Combo(resolution));
            Field(c, "_refreshRateComboBox", Combo("120Hz"));
            Field(c, "_dpiComboBox", Combo("150%"));
            Field(c, "_rotationComboBox", new ComboBox { SelectedIndex = -1 });
            Field(c, "_colorProfileComboBox", new ComboBox());
            Field(c, "_enabledCheckBox", new CheckBox { IsChecked = true });
            Field(c, "_hdrCheckBox", new CheckBox { IsChecked = hdr });
            Field(c, "_wcgCheckBox", new CheckBox { IsChecked = wcg });
            Field(c, "_displayConfigs", observations);
            return c;
        }

        private static DisplayConfigInfo Observation(bool hdr, bool wcg) => new()
        {
            TargetId = 818,
            RawTargetId = 818,
            IsAdvancedColorInfoAvailable = true,
            IsHdrSupported = hdr,
            IsWcgSupported = wcg
        };

        private static DisplaySetting Restored(bool hdrEnabled, bool wcgEnabled)
        {
            var source = new DisplaySettingBuilder().WithTargetId(817).WithResolution(1920, 1080).Build();
            var attached = new DisplaySettingBuilder().WithTargetId(818)
                .WithResolution(1920, 1080)
                .WithSavedPreCloneState(1200, 0, isPrimary: false,
                    width: 2560, height: 1440, frequency: 144, rotation: 1, dpiScaling: 125,
                    hdrEnabled: hdrEnabled, wcgEnabled: wcgEnabled, colorProfile: null)
                .Build();
            DisplaySettingControl.RestoreAttachedMemberState(attached, source);
            Assert.IsTrue(attached.OriginalSettings);
            Assert.IsTrue(string.IsNullOrEmpty(attached.CloneGroupId));
            Assert.AreEqual(hdrEnabled, attached.IsHdrEnabled);
            Assert.AreEqual(wcgEnabled, attached.IsWcgEnabled);
            return attached;
        }

        private static DisplaySetting Reopen(DisplaySetting saved)
        {
            var json = JsonSerializer.Serialize(saved);
            var result = JsonSerializer.Deserialize<DisplaySetting>(json);
            Assert.IsNotNull(result);
            return result;
        }

        private static void RunSta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "Editor STA fixture did not complete");
            if (error != null) throw error;
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BreakCloneLegacyWcg_RealRebuildThenEditSaveReopen_RespectsLiveAndUnknownCapability()
        {
            RunSta(() =>
            {
                // Restored former WCG intent takes the one-time original branch.
                var original = Restored(false, true);
                original.LastKnownWcgSupported = true;
                original.LastKnownHdrSupported = false;
                var live = new List<DisplayConfigInfo> { Observation(false, true) };
                var rebuild = Editor(original, false, false, "1920x1080", live).GetDisplaySettings();
                Assert.AreEqual(1, rebuild.Count);
                Assert.AreEqual(2560, rebuild[0].Width);
                Assert.AreEqual(1440, rebuild[0].Height);
                Assert.IsTrue(rebuild[0].IsWcgEnabled);
                Assert.IsFalse(rebuild[0].OriginalSettings);
                Assert.IsTrue(string.IsNullOrEmpty(rebuild[0].CloneGroupId));

                // Edit the now-detached monitor: turn WCG OFF, Save, JSON rehydrate, reopen and turn WCG ON.
                var savedOff = Editor(rebuild[0], false, false, "3840x2160", live).GetDisplaySettings();
                Assert.AreEqual(3840, savedOff[0].Width);
                Assert.IsFalse(savedOff[0].IsWcgEnabled);
                Assert.IsFalse(savedOff[0].OriginalSettings);
                var savedOn = Editor(Reopen(savedOff[0]), false, true, "3840x2160", live).GetDisplaySettings();
                Assert.IsTrue(savedOn[0].IsWcgEnabled, "WCG-only pre-24H2-style provider should permit WCG edit");
                Assert.IsFalse(savedOn[0].IsHdrEnabled);
                Assert.AreEqual(3840, savedOn[0].Width);
                Assert.IsFalse(savedOn[0].OriginalSettings);

                // A live HDR-only provider does NOT authorize WCG just because HDR is supported.
                var liveHdrOnly = new List<DisplayConfigInfo> { Observation(true, false) };
                var rejectWcg = Editor(Reopen(savedOff[0]), false, true, "3840x2160", liveHdrOnly).GetDisplaySettings();
                Assert.IsFalse(rejectWcg[0].IsWcgEnabled, "HDR capability must not be misread as WCG on legacy");

                // Missing live/last-known WCG evidence must not turn a prior OFF intent into newly enabled WCG.
                var unknown = Reopen(savedOff[0]);
                unknown.LastKnownWcgSupported = null;
                unknown.LastKnownHdrSupported = null;
                var unknownEdit = Editor(unknown, false, true, "3840x2160").GetDisplaySettings();
                Assert.IsFalse(unknownEdit[0].IsWcgEnabled, "Unknown capability must preserve known-OFF intent");
                Assert.IsFalse(unknownEdit[0].OriginalSettings);

                // Unknown capability must also preserve existing prior ON state rather than falsely clearing it.
                var unknownOn = Reopen(savedOn[0]);
                unknownOn.LastKnownHdrSupported = null;
                unknownOn.LastKnownWcgSupported = null;
                var unknownOnSaved = Editor(unknownOn, false, false, "3840x2160").GetDisplaySettings();
                Assert.IsTrue(unknownOnSaved[0].IsWcgEnabled, "Unknown must retain previous ON intent");
                Assert.IsFalse(unknownOnSaved[0].OriginalSettings);
            });
        }
    }
}
