using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class QuickSwitchOrchestrationTests
    {
        private sealed class FakeOverlay : IQuickSwitchOverlay
        {
            public event EventHandler Closed;
            public int ActivationCount { get; private set; }
            public int CloseCount { get; private set; }

            public void RefreshAndActivate() => ActivationCount++;

            public void Close()
            {
                CloseCount++;
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OverlayHost_OpenTwice_ReusesSingleOverlay()
        {
            int factoryCalls = 0;
            var overlay = new FakeOverlay();
            var host = new QuickSwitchOverlayHost(() =>
            {
                factoryCalls++;
                return overlay;
            });

            host.OpenOrActivate();
            host.OpenOrActivate();

            Assert.AreEqual(1, factoryCalls);
            Assert.AreEqual(2, overlay.ActivationCount);
            Assert.IsTrue(host.HasOpenOverlay);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OverlayHost_OpenWithoutMainWindow_CreatesAndActivatesOverlay()
        {
            var overlay = new FakeOverlay();
            var host = new QuickSwitchOverlayHost(() => overlay);

            host.OpenOrActivate();

            Assert.AreEqual(1, overlay.ActivationCount);
            Assert.IsTrue(host.HasOpenOverlay);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Interaction_EnterApply_AppliesSelectedProfileExactlyOnceThroughQuickSwitchSource()
        {
            var profile = new Profile("Profile A") { Id = "a" };
            var release = new TaskCompletionSource<ProfileApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            int applyCalls = 0;
            int closeCalls = 0;
            ApplySource source = ApplySource.Unknown;
            var interaction = new QuickSwitchInteractionController(
                (selected, applySource) =>
                {
                    Assert.AreSame(profile, selected);
                    source = applySource;
                    applyCalls++;
                    return release.Task;
                },
                () => closeCalls++,
                null);

            var first = interaction.ApplyAsync(profile);
            bool second = await interaction.ApplyAsync(profile);
            release.SetResult(new ProfileApplyResult { Success = true });
            bool firstResult = await first;

            Assert.IsTrue(firstResult);
            Assert.IsFalse(second);
            Assert.AreEqual(1, applyCalls);
            Assert.AreEqual(1, closeCalls);
            Assert.AreEqual(ApplySource.QuickSwitch, source);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Interaction_ClickApply_AppliesSelectedProfileAndReportsCompletionOnce()
        {
            var profile = new Profile("Profile B") { Id = "b" };
            int applyCalls = 0;
            int completionCalls = 0;
            var interaction = new QuickSwitchInteractionController(
                (selected, source) =>
                {
                    Assert.AreSame(profile, selected);
                    Assert.AreEqual(ApplySource.QuickSwitch, source);
                    applyCalls++;
                    return Task.FromResult(new ProfileApplyResult { Success = true });
                },
                () => { },
                (selected, result) =>
                {
                    Assert.AreSame(profile, selected);
                    Assert.IsTrue(result.Success);
                    completionCalls++;
                });

            bool applied = await interaction.ApplyAsync(profile);

            Assert.IsTrue(applied);
            Assert.AreEqual(1, applyCalls);
            Assert.AreEqual(1, completionCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Interaction_EscapeDismiss_DoesNotApply()
        {
            int applyCalls = 0;
            int closeCalls = 0;
            var interaction = new QuickSwitchInteractionController(
                (profile, source) =>
                {
                    applyCalls++;
                    return Task.FromResult(new ProfileApplyResult());
                },
                () => closeCalls++,
                null);

            bool dismissed = interaction.Dismiss();

            Assert.IsTrue(dismissed);
            Assert.AreEqual(0, applyCalls);
            Assert.AreEqual(1, closeCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Interaction_DeactivationDismiss_LatchesBeforeDeferredCloseAndRejectsRaces()
        {
            var profile = new Profile("Profile") { Id = "profile" };
            int applyCalls = 0;
            int immediateCloseCalls = 0;
            int scheduledCloseCalls = 0;
            Action pendingPhysicalClose = null;
            var interaction = new QuickSwitchInteractionController(
                (selected, source) =>
                {
                    applyCalls++;
                    return Task.FromResult(new ProfileApplyResult());
                },
                () => immediateCloseCalls++,
                null);

            bool first = interaction.Dismiss(() =>
            {
                scheduledCloseCalls++;
                pendingPhysicalClose = () => immediateCloseCalls++;
            });
            bool second = interaction.Dismiss(() => scheduledCloseCalls++);
            bool appliedWhilePending = await interaction.ApplyAsync(profile);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.IsTrue(interaction.IsDismissed);
            Assert.IsFalse(appliedWhilePending);
            Assert.AreEqual(0, applyCalls);
            Assert.AreEqual(0, immediateCloseCalls);
            Assert.AreEqual(1, scheduledCloseCalls);
            Assert.IsNotNull(pendingPhysicalClose);

            pendingPhysicalClose();

            Assert.AreEqual(1, immediateCloseCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Interaction_FooterDismissAndExecute_ClosesAndRoutesExactlyOnce()
        {
            int closeCalls = 0;
            int actionCalls = 0;
            var interaction = new QuickSwitchInteractionController(
                (profile, source) => Task.FromResult(new ProfileApplyResult()),
                () => closeCalls++,
                null);

            bool first = interaction.DismissAndExecute(() => actionCalls++);
            bool second = interaction.DismissAndExecute(() => actionCalls++);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.AreEqual(1, closeCalls);
            Assert.AreEqual(1, actionCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Interaction_ApplyCloseThenDeactivate_DoesNotDuplicateClose()
        {
            var profile = new Profile("Profile") { Id = "profile" };
            var release = new TaskCompletionSource<ProfileApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            int closeCalls = 0;
            var interaction = new QuickSwitchInteractionController(
                (selected, source) => release.Task,
                () => closeCalls++,
                null);

            int deferredCloseSchedules = 0;
            var apply = interaction.ApplyAsync(profile);
            bool deactivationDismissed = interaction.Dismiss(() => deferredCloseSchedules++);
            release.SetResult(new ProfileApplyResult { Success = true });
            await apply;

            Assert.IsFalse(deactivationDismissed);
            Assert.AreEqual(0, deferredCloseSchedules);
            Assert.AreEqual(1, closeCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileRefresh_AddUpdateDeleteLoad_AllRequestRefresh()
        {
            int refreshCalls = 0;
            var refresh = new QuickSwitchProfileRefreshCoordinator(() => refreshCalls++);
            var profile = new Profile("Profile") { Id = "profile" };

            refresh.OnProfileAdded(null, profile);
            refresh.OnProfileUpdated(null, profile);
            refresh.OnProfileDeleted(null, profile.Id);
            refresh.OnProfilesLoaded(null, EventArgs.Empty);

            Assert.AreEqual(4, refreshCalls);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public async Task SettingsRefresh_OffDispatcher_RunsOnOwnerDispatcher()
        {
            var owner = StartStaDispatcher();
            try
            {
                var refreshed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var coordinator = new QuickSwitchSettingsRefreshCoordinator(
                    owner.Dispatcher,
                    () => refreshed.TrySetResult(Environment.CurrentManagedThreadId));

                await Task.Run(coordinator.RequestRefresh);
                int refreshThread = await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.AreEqual(owner.ThreadId, refreshThread);
            }
            finally
            {
                StopStaDispatcher(owner.Dispatcher, owner.Thread);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task SettingsRefresh_DisposedBeforeQueuedCallback_DropsRefresh()
        {
            var owner = StartStaDispatcher();
            using var blockerEntered = new ManualResetEventSlim();
            using var releaseBlocker = new ManualResetEventSlim();
            try
            {
                _ = owner.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        blockerEntered.Set();
                        releaseBlocker.Wait(TimeSpan.FromSeconds(5));
                    }),
                    DispatcherPriority.Send);
                Assert.IsTrue(blockerEntered.Wait(TimeSpan.FromSeconds(5)));

                int refreshCalls = 0;
                using var coordinator = new QuickSwitchSettingsRefreshCoordinator(
                    owner.Dispatcher,
                    () => Interlocked.Increment(ref refreshCalls));

                await Task.Run(coordinator.RequestRefresh);
                coordinator.Dispose();
                releaseBlocker.Set();
                await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.AreEqual(0, Volatile.Read(ref refreshCalls));
            }
            finally
            {
                releaseBlocker.Set();
                StopStaDispatcher(owner.Dispatcher, owner.Thread);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OpeningSettings_DoesNotAcquireHotkeySuspension()
        {
            string main = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml.cs")));

            int open = main.IndexOf("public void OpenSettingsWindow()", StringComparison.Ordinal);
            int construct = main.IndexOf("new SettingsWindow(", open, StringComparison.Ordinal);
            int show = main.IndexOf("settingsWindow.ShowDialog();", open, StringComparison.Ordinal);
            string openPath = main[open..show];

            Assert.IsTrue(open >= 0);
            Assert.IsTrue(construct > open);
            Assert.IsFalse(openPath.Contains("using var hotkeySuspension", StringComparison.Ordinal));
            Assert.IsFalse(openPath.Contains("SuspendForHotkeyCapture()", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CaptureSuspension_CoversBothHotkeyFamiliesAndBlocksEarlyReconcile()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "Hotkeys", "HotkeyCoordinator.cs")));

            int suspend = code.IndexOf("public IDisposable SuspendForHotkeyCapture()", StringComparison.Ordinal);
            int callback = code.IndexOf("private Action CreateProfileHotkeyCallback", suspend, StringComparison.Ordinal);
            string suspendPath = code[suspend..callback];

            StringAssert.Contains(suspendPath, "_registrar.UnregisterAllProfileHotkeys();");
            StringAssert.Contains(suspendPath, "_quickSwitchRegistration.Disable();");
            StringAssert.Contains(code, "if (_disposed || _captureSuspension.IsSuspended) return;");
            StringAssert.Contains(code, "if (_disposed || _captureSuspension.IsSuspended || _revertingQuickSwitch) return;");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CaptureResume_PreservesLatestFirstQuickSwitchFallbackPolicy()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "Hotkeys", "HotkeyCoordinator.cs")));

            StringAssert.Contains(code, "_captureQuickSwitchFallback = _quickSwitchRegistration.IsRegistered");
            StringAssert.Contains(code, "RegisterAllProfileHotkeys();");
            StringAssert.Contains(code, "bool enabled = _settings.IsQuickSwitchEnabled();");
            StringAssert.Contains(code, "HotkeyConfig requested = _settings.GetQuickSwitchHotkey();");
            StringAssert.Contains(code, "ReconcileQuickSwitchWithFallback(");
            StringAssert.Contains(code, "_quickSwitchWarning?.Invoke(warning);");
            StringAssert.Contains(code, "_ = RevertFailedQuickSwitchSettingAsync();");
            StringAssert.Contains(code, "_captureSuspension.Shutdown();");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BothEditorsReceiveTheSameCaptureScopeFactory()
        {
            string settings = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "SettingsWindow.xaml.cs")));
            string profile = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "ProfileEditWindow.xaml.cs")));

            StringAssert.Contains(settings, "QuickSwitchHotkeyEditor.CaptureScopeFactory = _suspendHotkeysForCapture;");
            StringAssert.Contains(profile, "HotkeyEditor.CaptureScopeFactory = _suspendHotkeysForCapture;");
        }

        private static (Dispatcher Dispatcher, Thread Thread, int ThreadId) StartStaDispatcher()
        {
            using var ready = new ManualResetEventSlim();
            Dispatcher dispatcher = null;
            int threadId = 0;
            var thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                threadId = Environment.CurrentManagedThreadId;
                ready.Set();
                Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(5)));
            return (dispatcher, thread, threadId);
        }

        private static void StopStaDispatcher(Dispatcher dispatcher, Thread thread)
        {
            if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);

            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
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
