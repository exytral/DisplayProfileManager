using System;
using System.IO;
using System.Windows.Input;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.UI.Controls;
using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class HotkeyEditorPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void HasAssignedHotkey_NullConfig_ReturnsFalse()
        {
            Assert.IsFalse(HotkeyEditorControl.HasAssignedHotkey(null));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasAssignedHotkey_UnassignedConfig_ReturnsFalse()
        {
            Assert.IsFalse(HotkeyEditorControl.HasAssignedHotkey(new HotkeyConfig()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasAssignedHotkey_AssignedConfig_ReturnsTrue()
        {
            var config = new HotkeyConfig
            {
                Key = System.Windows.Input.Key.A
            };

            Assert.IsTrue(HotkeyEditorControl.HasAssignedHotkey(config));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasAssignedHotkey_ClearedConfig_ReturnsFalse()
        {
            var config = new HotkeyConfig
            {
                Key = System.Windows.Input.Key.None,
                IsEnabled = true
            };

            Assert.IsFalse(HotkeyEditorControl.HasAssignedHotkey(config));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Presentation_Unassigned_DisablesActions()
        {
            var state = DisplayProfileManager.UI.Windows.ProfileEditWindow.ResolveHotkeyPresentation(false, true);

            Assert.IsFalse(state.EnableChecked);
            Assert.IsFalse(state.EnableInteractive);
            Assert.IsFalse(state.ClearEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Presentation_AssignedEnabled_KeepsActionsAvailable()
        {
            var state = DisplayProfileManager.UI.Windows.ProfileEditWindow.ResolveHotkeyPresentation(true, true);

            Assert.IsTrue(state.EnableChecked);
            Assert.IsTrue(state.EnableInteractive);
            Assert.IsTrue(state.ClearEnabled);
        }
        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchConflict_ProfileOwnsConfiguredChord_IsRejected()
        {
            var profile = new Profile("Desk")
            {
                HotkeyConfig = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt) { IsEnabled = false }
            };
            var requested = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);

            var conflict = SettingsWindow.FindQuickSwitchHotkeyConflict([profile], requested);

            Assert.AreSame(profile, conflict);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileConflict_QuickSwitchOwnsConfiguredChord_IsRejected()
        {
            var profileHotkey = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);
            var quickSwitchHotkey = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt) { IsEnabled = false };

            Assert.IsTrue(ProfileEditWindow.HasQuickSwitchHotkeyConflict(profileHotkey, quickSwitchHotkey));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ConfiguredHotkeys_QuickSwitchAssigned_IsIncluded()
        {
            var hotkey = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt) { IsEnabled = false };

            Assert.IsTrue(SettingsWindow.ShouldShowConfiguredQuickSwitch(hotkey));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ConfiguredHotkeys_QuickSwitchCleared_IsRemoved()
        {
            Assert.IsFalse(SettingsWindow.ShouldShowConfiguredQuickSwitch(new HotkeyConfig()));
        }



        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderPresentation_UsesFixedGeometryAndVisibleNonColorStates()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml")));

            StringAssert.Contains(xaml, "BorderThickness=\"1\"");
            Assert.IsFalse(xaml.Contains("BorderThickness\" Value=\"2\"", StringComparison.Ordinal));
            StringAssert.Contains(xaml, "●");
            StringAssert.Contains(xaml, "⚠");
            StringAssert.Contains(xaml, "✓");
            StringAssert.Contains(xaml, "AutomationProperties.HelpText");
            StringAssert.Contains(xaml, "ControlHoverBackgroundBrush");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderArm_AcquiresCaptureBeforeExposingRecordingState()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            int arm = code.IndexOf("private bool TryArm()", StringComparison.Ordinal);
            int acquire = code.IndexOf("CaptureScopeFactory?.Invoke()", arm, StringComparison.Ordinal);
            int record = code.IndexOf("IsRecording = true;", arm, StringComparison.Ordinal);
            int prompt = code.IndexOf("HotkeyText = \"Recording shortcut…\";", arm, StringComparison.Ordinal);

            Assert.IsTrue(arm >= 0);
            Assert.IsTrue(acquire > arm && acquire < record);
            Assert.IsTrue(record < prompt);
            StringAssert.Contains(code, "HotkeyTextBox_PreviewMouseDown");
            StringAssert.Contains(code, "HotkeyTextBox_GotKeyboardFocus");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderEscapeAndLostFocus_CancelWithoutClearOrEarlyPendingRelease()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            int keyDown = code.IndexOf("private void HotkeyTextBox_PreviewKeyDown", StringComparison.Ordinal);
            int escape = code.IndexOf("if (key == Key.Escape)", keyDown, StringComparison.Ordinal);
            int cancel = code.IndexOf("CancelCapture();", escape, StringComparison.Ordinal);
            int lost = code.IndexOf("private void HotkeyTextBox_LostKeyboardFocus", StringComparison.Ordinal);
            int pendingGuard = code.IndexOf("if (!IsCommitPending)", lost, StringComparison.Ordinal);

            Assert.IsTrue(escape > keyDown && cancel > escape);
            Assert.IsTrue(pendingGuard > lost);
            Assert.IsFalse(code.Contains("ClearHotkey();", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SettingsCapture_HoldsThroughPersistenceAndExplicitClear()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "SettingsWindow.xaml.cs")));

            int handler = code.IndexOf("QuickSwitchHotkeyEditor_HotkeyChanged", StringComparison.Ordinal);
            int persist = code.IndexOf("await _settingsService.SetQuickSwitchHotkeyAsync(e)", handler, StringComparison.Ordinal);
            int accept = code.IndexOf("QuickSwitchHotkeyEditor.AcceptPendingHotkey", persist, StringComparison.Ordinal);
            int clearPreview = code.IndexOf("QuickSwitchClearButton_PreviewMouseDown", StringComparison.Ordinal);
            int externalCommit = code.IndexOf("QuickSwitchHotkeyEditor.BeginExternalCommit();", clearPreview, StringComparison.Ordinal);
            int clearPersist = code.IndexOf("await _settingsService.ClearQuickSwitchHotkeyAsync()", externalCommit, StringComparison.Ordinal);

            Assert.IsTrue(persist > handler && accept > persist);
            Assert.IsTrue(externalCommit > clearPreview && clearPersist > externalCommit);
            StringAssert.Contains(code, "if (_quickSwitchMutationPending)");
            StringAssert.Contains(code, "_closeAfterQuickSwitchMutation = true;");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileEditor_UsesCaptureScopedDraftAcceptanceWithoutWindowLifetimeSuppression()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "ProfileEditWindow.xaml.cs")));

            StringAssert.Contains(code, "HotkeyEditor.CaptureScopeFactory = _suspendHotkeysForCapture;");
            StringAssert.Contains(code, "ProfileHotkeyEditor_HotkeyChanged");
            StringAssert.Contains(code, "HasQuickSwitchHotkeyConflict(candidate, quickSwitchHotkey)");
            StringAssert.Contains(code, "HotkeyEditor.AcceptPendingHotkey(candidate);");
            Assert.IsFalse(code.Contains("DisableProfileHotkeys", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("EnableProfileHotkeys", StringComparison.Ordinal));
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderTabTraversal_CancelsBeforeNormalForwardOrReverseNavigation()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            int keyDown = code.IndexOf("private void HotkeyTextBox_PreviewKeyDown", StringComparison.Ordinal);
            int resolveKey = code.IndexOf("var key = e.Key == Key.System ? e.SystemKey : e.Key;", keyDown, StringComparison.Ordinal);
            int tab = code.IndexOf("if (key == Key.Tab && !IsCommitPending)", resolveKey, StringComparison.Ordinal);
            int cancel = code.IndexOf("CancelCapture();", tab, StringComparison.Ordinal);
            int unhandled = code.IndexOf("e.Handled = false;", cancel, StringComparison.Ordinal);
            int recordingGuard = code.IndexOf("if (IsCommitPending || !IsRecording)", unhandled, StringComparison.Ordinal);

            Assert.IsTrue(resolveKey > keyDown);
            Assert.IsTrue(tab > resolveKey);
            Assert.IsTrue(cancel > tab && unhandled > cancel);
            Assert.IsTrue(recordingGuard > unhandled);
            StringAssert.Contains(code, "key == Key.Tab");
            Assert.IsFalse(code.Contains("new HotkeyConfig(Key.Tab", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderEscapeThenTab_DoesNotUseKeyDownAsARearmSeam()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            int keyDown = code.IndexOf("private void HotkeyTextBox_PreviewKeyDown", StringComparison.Ordinal);
            int nextMethod = code.IndexOf("private void HotkeyTextBox_PreviewKeyUp", keyDown, StringComparison.Ordinal);
            string keyDownBody = code[keyDown..nextMethod];

            Assert.IsFalse(keyDownBody.Contains("TryArm()", StringComparison.Ordinal));
            StringAssert.Contains(keyDownBody, "if (key == Key.Tab && !IsCommitPending)");
            StringAssert.Contains(keyDownBody, "if (key == Key.Escape)");
            StringAssert.Contains(keyDownBody, "CancelCapture();");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderHighContrast_UsesSystemBrushAuthorityAndRefreshesAtRuntime()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            StringAssert.Contains(xaml, "Path=IsHighContrast");
            StringAssert.Contains(xaml, "SystemColors.HighlightBrushKey");
            StringAssert.Contains(xaml, "SystemColors.WindowTextBrushKey");
            StringAssert.Contains(xaml, "SystemColors.WindowBrushKey");

            StringAssert.Contains(code, "private bool _isHighContrast = SystemParameters.HighContrast;");
            StringAssert.Contains(code, "SystemParameters.StaticPropertyChanged += OnSystemParametersStaticPropertyChanged;");
            StringAssert.Contains(code, "IsHighContrast = SystemParameters.HighContrast;");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderHighContrast_PreservesAcceptedGeometryTextAndGlyphStates()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            StringAssert.Contains(xaml, "BorderThickness=\"1\"");
            StringAssert.Contains(xaml, "Width=\"16\" Height=\"16\"");
            StringAssert.Contains(xaml, "●");
            StringAssert.Contains(xaml, "⚠");
            StringAssert.Contains(xaml, "✓");
            StringAssert.Contains(code, "HotkeyText = \"Recording shortcut…\";");
            StringAssert.Contains(code, "string.Join(\" + \", parts) + \" + …\"");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderAssignedArm_InvalidatesIdleValidAndRecordingTriggerWins()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml")));

            int recordingSetter = code.IndexOf("public bool IsRecording", StringComparison.Ordinal);
            int recordingChanged = code.IndexOf("OnPropertyChanged();", recordingSetter, StringComparison.Ordinal);
            int validChanged = code.IndexOf("OnPropertyChanged(nameof(IsValid));", recordingChanged, StringComparison.Ordinal);
            Assert.IsTrue(recordingSetter >= 0 && recordingChanged > recordingSetter && validChanged > recordingChanged);

            int validTrigger = xaml.IndexOf("Path=IsValid", StringComparison.Ordinal);
            int recordingTrigger = xaml.IndexOf("Path=IsRecording", validTrigger, StringComparison.Ordinal);
            int conflictTrigger = xaml.IndexOf("Path=HasConflict", recordingTrigger, StringComparison.Ordinal);
            Assert.IsTrue(validTrigger >= 0);
            Assert.IsTrue(recordingTrigger > validTrigger, "Armed presentation must override an idle valid checkmark.");
            Assert.IsTrue(conflictTrigger > recordingTrigger, "Conflict must remain the final ordinary semantic state.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderArm_AssignedOrEmpty_UsesOneExplicitActivePresentation()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml")));

            StringAssert.Contains(code, "HotkeyText = \"Recording shortcut…\";");
            StringAssert.Contains(code, "? string.Join(\" + \", parts) + \" + …\"");
            StringAssert.Contains(code, ": \"Recording shortcut…\";");
            StringAssert.Contains(xaml, "Path=IsRecording");
            StringAssert.Contains(xaml, "Property=\"Text\" Value=\"●\"");
            StringAssert.Contains(xaml, "SystemAccentBrush");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderExitStates_RestoreIdleValidEmptyOrConflictPresentation()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml")));

            int cancel = code.IndexOf("internal void CancelCapture()", StringComparison.Ordinal);
            int cancelApply = code.IndexOf("ApplyCommittedHotkey(_preArmHotkey", cancel, StringComparison.Ordinal);
            int cancelFinish = code.IndexOf("FinishCapture();", cancelApply, StringComparison.Ordinal);
            Assert.IsTrue(cancelApply > cancel && cancelFinish > cancelApply);

            int accept = code.IndexOf("internal void AcceptPendingHotkey", StringComparison.Ordinal);
            int acceptApply = code.IndexOf("ApplyCommittedHotkey(value);", accept, StringComparison.Ordinal);
            int acceptFinish = code.IndexOf("FinishCapture();", acceptApply, StringComparison.Ordinal);
            Assert.IsTrue(acceptApply > accept && acceptFinish > acceptApply);

            int reject = code.IndexOf("internal void RejectPendingHotkey", StringComparison.Ordinal);
            int conflict = code.IndexOf("ConflictingProfile = conflictingProfile;", reject, StringComparison.Ordinal);
            int rejectFinish = code.IndexOf("FinishCapture();", conflict, StringComparison.Ordinal);
            Assert.IsTrue(conflict > reject && rejectFinish > conflict);

            StringAssert.Contains(xaml, "Property=\"Text\" Value=\"✓\"");
            StringAssert.Contains(xaml, "Property=\"Text\" Value=\"⚠\"");
            StringAssert.Contains(xaml, "<TextBlock x:Name=\"IconText\"");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderPresentation_RemainsSharedAndAccessibilityGeometryFrozen()
        {
            string settings = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "SettingsWindow.xaml")));
            string profile = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "ProfileEditWindow.xaml")));
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            StringAssert.Contains(settings, "<controls:HotkeyEditorControl x:Name=\"QuickSwitchHotkeyEditor\"");
            StringAssert.Contains(profile, "x:Name=\"HotkeyEditor\"");
            StringAssert.Contains(xaml, "BorderThickness=\"1\"");
            StringAssert.Contains(xaml, "Width=\"16\" Height=\"16\"");
            StringAssert.Contains(xaml, "AutomationProperties.HelpText");
            StringAssert.Contains(code, "Recording shortcut. Press shortcut. Escape cancels.");
            StringAssert.Contains(xaml, "SystemColors.HighlightBrushKey");
            StringAssert.Contains(xaml, "SystemColors.WindowTextBrushKey");
            StringAssert.Contains(xaml, "SystemColors.WindowBrushKey");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderPulse_ExistsOnlyForActiveRecordingAndIsGeometryNeutral()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            StringAssert.Contains(code, "IsPulseActive => IsRecording && !HasConflict && !IsHighContrast && AreAnimationsEnabled");
            StringAssert.Contains(xaml, "Path=IsPulseActive");
            StringAssert.Contains(xaml, "Storyboard.TargetName=\"IconText\"");
            StringAssert.Contains(xaml, "Storyboard.TargetProperty=\"Opacity\"");
            StringAssert.Contains(xaml, "To=\"0.55\"");
            StringAssert.Contains(xaml, "Duration=\"0:0:0.85\"");
            Assert.IsFalse(xaml.Contains("Storyboard.TargetProperty=\"Width", StringComparison.Ordinal));
            Assert.IsFalse(xaml.Contains("Storyboard.TargetProperty=\"Height", StringComparison.Ordinal));
            Assert.IsFalse(xaml.Contains("Storyboard.TargetProperty=\"Margin", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderPulse_StopsAndResetsWhenRecordingAuthorityEnds()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            StringAssert.Contains(xaml, "<RemoveStoryboard BeginStoryboardName=\"RecordingPulseStoryboard\"/>");
            StringAssert.Contains(code, "IsRecording = false;");
            StringAssert.Contains(code, "OnPropertyChanged(nameof(IsPulseActive));");
            StringAssert.Contains(code, "HasConflict = !string.IsNullOrEmpty(e.NewValue as string)");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RecorderPulse_UsesExistingReducedMotionAndHighContrastAuthority()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "HotkeyEditorControl.xaml.cs")));

            StringAssert.Contains(code, "private bool _areAnimationsEnabled = SystemParameters.ClientAreaAnimation;");
            StringAssert.Contains(code, "AreAnimationsEnabled = SystemParameters.ClientAreaAnimation;");
            StringAssert.Contains(code, "nameof(SystemParameters.ClientAreaAnimation)");
            StringAssert.Contains(code, "!IsHighContrast && AreAnimationsEnabled");
            Assert.IsFalse(code.Contains("DispatcherTimer", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("Thread.Sleep", StringComparison.Ordinal));
        }

        private static string FindRepositoryFile(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                    directory = directory.Parent;
                }
            }

            throw new FileNotFoundException($"Could not find repository file '{relativePath}'.");
        }
    }
}
