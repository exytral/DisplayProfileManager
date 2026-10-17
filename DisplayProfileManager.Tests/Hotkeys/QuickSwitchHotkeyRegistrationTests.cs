using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Hotkeys;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.Settings.Persistence;

namespace DisplayProfileManager.Tests.Hotkeys
{
    [TestClass]
    public class QuickSwitchHotkeyRegistrationTests
    {
        private sealed class FakeRegistrar : IGlobalHotkeyRegistrar
        {
            public int NextId { get; set; } = 100;
            public bool FailNext { get; set; }
            public int FailuresRemaining { get; set; }
            public List<int> Unregistered { get; } = [];
            public List<uint> RegisterAttempts { get; } = [];
            public Dictionary<int, Action> Callbacks { get; } = [];

            public int RegisterHotkey(uint virtualKey, uint modifiers, Action callback)
            {
                RegisterAttempts.Add(virtualKey);
                if (FailNext || FailuresRemaining > 0)
                {
                    FailNext = false;
                    if (FailuresRemaining > 0)
                        FailuresRemaining--;
                    return -1;
                }

                int id = NextId++;
                Callbacks[id] = callback;
                return id;
            }

            public bool UnregisterHotkey(int hotkeyId)
            {
                Unregistered.Add(hotkeyId);
                Callbacks.Remove(hotkeyId);
                return true;
            }

            public bool TryInvoke(int hotkeyId)
            {
                if (!Callbacks.TryGetValue(hotkeyId, out var callback))
                {
                    return false;
                }

                callback();
                return true;
            }

            public int RegisterProfileHotkey(string profileId, HotkeyConfig hotkey, Action callback) => -1;
            public bool UnregisterProfileHotkey(string profileId) => true;
            public void RegisterAllProfileHotkeys(Dictionary<string, HotkeyConfig> profileHotkeys, Func<string, Action> callbackFactory) { }
            public void UnregisterAllProfileHotkeys() { }
            public void Dispose() { }
        }


        private sealed class FakeSettingsStore : ISettingsStore
        {
            public string SettingsFilePath => @"C:\Config\Settings.json";
            public string AppDataFolder => @"C:\Config";
            public AppSettings LoadedSettings { get; set; } = new AppSettings();

            public Task<AppSettings> LoadAsync() => Task.FromResult(LoadedSettings);

            public Task<bool> SaveAsync(AppSettings settings)
            {
                LoadedSettings = settings;
                return Task.FromResult(true);
            }
        }

        private static SettingsService CreateLoadedSettings(HotkeyConfig hotkey, bool enabled)
        {
            var store = new FakeSettingsStore
            {
                LoadedSettings = new AppSettings
                {
                    QuickSwitchEnabled = enabled,
                    QuickSwitchHotkey = hotkey.Clone()
                }
            };
            var settings = new SettingsService(store);
            Assert.IsTrue(settings.LoadSettingsAsync().GetAwaiter().GetResult());
            return settings;
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Reconcile_Enabled_RegistersHotkey()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            var hotkey = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);

            var result = registration.Reconcile(true, hotkey);

            Assert.AreEqual(QuickSwitchRegistrationResult.Registered, result);
            Assert.IsTrue(registration.IsRegistered);
            Assert.AreEqual(hotkey, registration.RegisteredHotkey);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Reconcile_Disabled_UnregistersActiveHotkey()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            registration.Reconcile(true, new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt));

            var result = registration.Reconcile(false, null);

            Assert.AreEqual(QuickSwitchRegistrationResult.Disabled, result);
            Assert.IsFalse(registration.IsRegistered);
            CollectionAssert.AreEqual(new[] { 100 }, registrar.Unregistered.ToArray());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Reconcile_ClearedAndDisabled_UnregistersWithoutStaleRegistration()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            registration.Reconcile(true, new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt));

            var result = registration.Reconcile(false, new HotkeyConfig());

            Assert.AreEqual(QuickSwitchRegistrationResult.Disabled, result);
            Assert.IsFalse(registration.IsRegistered);
            Assert.IsNull(registration.RegisteredHotkey);
            CollectionAssert.AreEqual(new[] { 100 }, registrar.Unregistered.ToArray());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Reconcile_ClearAssignWhileDisabledThenEnable_RegistersReplacement()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            var original = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);
            var replacement = new HotkeyConfig(Key.O, ModifierKeys.Control | ModifierKeys.Shift);

            Assert.AreEqual(QuickSwitchRegistrationResult.Registered, registration.Reconcile(true, original));
            Assert.AreEqual(QuickSwitchRegistrationResult.Disabled, registration.Reconcile(false, new HotkeyConfig()));
            Assert.IsFalse(registration.IsRegistered);
            CollectionAssert.AreEqual(new[] { 100 }, registrar.Unregistered.ToArray());

            Assert.AreEqual(QuickSwitchRegistrationResult.Disabled, registration.Reconcile(false, replacement));
            Assert.IsFalse(registration.IsRegistered);
            Assert.AreEqual(101, registrar.NextId);

            Assert.AreEqual(QuickSwitchRegistrationResult.Registered, registration.Reconcile(true, replacement));
            Assert.IsTrue(registration.IsRegistered);
            Assert.AreEqual(replacement, registration.RegisteredHotkey);
            Assert.AreEqual(102, registrar.NextId);
            CollectionAssert.AreEqual(new[] { 100 }, registrar.Unregistered.ToArray());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Reconcile_ChangedHotkey_ReplacesRegistrationWithoutStaleCallback()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            registration.Reconcile(true, new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt));

            var result = registration.Reconcile(true, new HotkeyConfig(Key.O, ModifierKeys.Control | ModifierKeys.Alt));

            Assert.AreEqual(QuickSwitchRegistrationResult.Registered, result);
            Assert.AreEqual(Key.O, registration.RegisteredHotkey.Key);
            CollectionAssert.AreEqual(new[] { 100 }, registrar.Unregistered.ToArray());
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void Reconcile_EnabledInvalidReplacement_PreservesPreviousRegistration()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            var original = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);
            registration.Reconcile(true, original);

            var result = registration.Reconcile(true, new HotkeyConfig());

            Assert.AreEqual(QuickSwitchRegistrationResult.Failed, result);
            Assert.IsTrue(registration.IsRegistered);
            Assert.AreEqual(original, registration.RegisteredHotkey);
            Assert.AreEqual(0, registrar.Unregistered.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Reconcile_StartupEnabledInvalidHotkey_FailsWithoutCreatingRegistration()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });

            var result = registration.Reconcile(true, new HotkeyConfig());

            Assert.AreEqual(QuickSwitchRegistrationResult.Failed, result);
            Assert.IsFalse(registration.IsRegistered);
            Assert.AreEqual(100, registrar.NextId);
            Assert.AreEqual(0, registrar.Unregistered.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Reconcile_ReplacementConflict_PreservesPreviousRegistration()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            registration.Reconcile(true, new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt));
            registrar.FailNext = true;

            var result = registration.Reconcile(true, new HotkeyConfig(Key.O, ModifierKeys.Control | ModifierKeys.Alt));

            Assert.AreEqual(QuickSwitchRegistrationResult.Failed, result);
            Assert.IsTrue(registration.IsRegistered);
            Assert.AreEqual(Key.P, registration.RegisteredHotkey.Key);
            Assert.AreEqual(0, registrar.Unregistered.Count);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void Disable_RegisteredChord_RemovesCallbackBeforeRecorderReuse()
        {
            var registrar = new FakeRegistrar();
            int invoked = 0;
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => invoked++);
            registration.Reconcile(true, new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt));

            Assert.IsTrue(registrar.TryInvoke(100));
            Assert.AreEqual(1, invoked);

            registration.Disable();

            Assert.IsFalse(registrar.TryInvoke(100));
            Assert.AreEqual(1, invoked);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SuspensionGate_NestedScopes_ResumeOnlyAfterFinalDispose()
        {
            var gate = new HotkeySuspensionGate(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            int suspended = 0;
            int resumed = 0;

            using var first = gate.Enter(() => suspended++, () => resumed++);
            var second = gate.Enter(() => suspended++, () => resumed++);

            Assert.IsTrue(gate.IsSuspended);
            Assert.AreEqual(1, suspended);
            first.Dispose();
            Assert.IsTrue(gate.IsSuspended);
            Assert.AreEqual(0, resumed);

            second.Dispose();

            Assert.IsFalse(gate.IsSuspended);
            Assert.AreEqual(1, resumed);
            second.Dispose();
            Assert.AreEqual(1, resumed);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SuspensionGate_ShutdownDuringSuspension_DoesNotResume()
        {
            var gate = new HotkeySuspensionGate(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            int resumed = 0;
            var scope = gate.Enter(() => { }, () => resumed++);

            gate.Shutdown();
            scope.Dispose();

            Assert.IsFalse(gate.IsSuspended);
            Assert.AreEqual(0, resumed);
            using var afterShutdown = gate.Enter(() => Assert.Fail("shutdown gate must not suspend again"), () => resumed++);
            Assert.AreEqual(0, resumed);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public async Task SettingsResume_ReplacementFailure_RestoresWorkingFallbackAndPersistsIt()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            var previous = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);
            var requested = new HotkeyConfig(Key.O, ModifierKeys.Control | ModifierKeys.Shift);
            var settings = CreateLoadedSettings(requested, true);

            Assert.AreEqual(QuickSwitchRegistrationResult.Registered, registration.Reconcile(true, previous));
            var fallback = registration.RegisteredHotkey.Clone();
            registration.Disable();
            registrar.FailNext = true;

            var result = HotkeyCoordinator.ReconcileQuickSwitchWithFallback(
                registration,
                true,
                requested,
                fallback);

            Assert.AreEqual(QuickSwitchRegistrationResult.Failed, result);
            Assert.IsTrue(registration.IsRegistered);
            Assert.AreEqual(previous, registration.RegisteredHotkey);
            CollectionAssert.AreEqual(
                new[]
                {
                    (uint)DisplayProfileManager.Hotkeys.Interop.KeyConverter.ToVirtualKey(previous.Key),
                    (uint)DisplayProfileManager.Hotkeys.Interop.KeyConverter.ToVirtualKey(requested.Key),
                    (uint)DisplayProfileManager.Hotkeys.Interop.KeyConverter.ToVirtualKey(previous.Key)
                },
                registrar.RegisterAttempts.ToArray());
            string warning = HotkeyCoordinator.BuildQuickSwitchRegistrationFailureWarning(requested, registration);
            StringAssert.Contains(warning, "previous registration was preserved");

            await HotkeyCoordinator.RevertFailedQuickSwitchSettingAsync(settings, registration);

            Assert.IsTrue(settings.IsQuickSwitchEnabled());
            Assert.AreEqual(previous, settings.GetQuickSwitchHotkey());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SettingsResume_ExplicitDisableOrClear_DoesNotResurrectWorkingFallback()
        {
            var previous = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);

            foreach (var requested in new HotkeyConfig[] { previous, new() })
            {
                var registrar = new FakeRegistrar();
                using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
                registration.Reconcile(true, previous);
                var fallback = registration.RegisteredHotkey.Clone();
                registration.Disable();

                var result = HotkeyCoordinator.ReconcileQuickSwitchWithFallback(
                    registration,
                    false,
                    requested,
                    fallback);

                Assert.AreEqual(QuickSwitchRegistrationResult.Disabled, result);
                Assert.IsFalse(registration.IsRegistered);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task SettingsResume_EscapeEmptyWhileEnabled_RestoresAndRevertsWorkingFallback()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            var previous = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);
            var requested = new HotkeyConfig();
            var settings = CreateLoadedSettings(requested, true);

            Assert.AreEqual(QuickSwitchRegistrationResult.Registered, registration.Reconcile(true, previous));
            var fallback = registration.RegisteredHotkey.Clone();
            registration.Disable();

            var result = HotkeyCoordinator.ReconcileQuickSwitchWithFallback(
                registration,
                true,
                requested,
                fallback);

            Assert.AreEqual(QuickSwitchRegistrationResult.Failed, result);
            Assert.IsTrue(registration.IsRegistered);
            Assert.AreEqual(previous, registration.RegisteredHotkey);
            string warning = HotkeyCoordinator.BuildQuickSwitchRegistrationFailureWarning(requested, registration);
            StringAssert.Contains(warning, "previous registration was preserved");

            await HotkeyCoordinator.RevertFailedQuickSwitchSettingAsync(settings, registration);

            Assert.IsTrue(settings.IsQuickSwitchEnabled());
            Assert.AreEqual(previous, settings.GetQuickSwitchHotkey());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task SettingsResume_ReplacementAndFallbackUnavailable_DisablesDeterministically()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            var previous = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);
            var requested = new HotkeyConfig(Key.O, ModifierKeys.Control | ModifierKeys.Shift);
            var settings = CreateLoadedSettings(requested, true);

            registration.Reconcile(true, previous);
            var fallback = registration.RegisteredHotkey.Clone();
            registration.Disable();
            registrar.FailuresRemaining = 2;

            var result = HotkeyCoordinator.ReconcileQuickSwitchWithFallback(
                registration,
                true,
                requested,
                fallback);

            Assert.AreEqual(QuickSwitchRegistrationResult.Failed, result);
            Assert.IsFalse(registration.IsRegistered);
            string warning = HotkeyCoordinator.BuildQuickSwitchRegistrationFailureWarning(requested, registration);
            StringAssert.Contains(warning, "Quick Switch was disabled");

            await HotkeyCoordinator.RevertFailedQuickSwitchSettingAsync(settings, registration);

            Assert.IsFalse(settings.IsQuickSwitchEnabled());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SettingsSuspension_NestedScopes_CaptureFallbackOnceAndRestoreOnlyOnFinalRelease()
        {
            var registrar = new FakeRegistrar();
            using var registration = new QuickSwitchHotkeyRegistration(registrar, () => { });
            var previous = new HotkeyConfig(Key.P, ModifierKeys.Control | ModifierKeys.Alt);
            var requested = new HotkeyConfig(Key.O, ModifierKeys.Control | ModifierKeys.Shift);
            registration.Reconcile(true, previous);

            var gate = new HotkeySuspensionGate(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            HotkeyConfig fallback = null;
            Action suspend = () =>
            {
                fallback = registration.RegisteredHotkey?.Clone();
                registration.Disable();
            };
            Action resume = () =>
            {
                registrar.FailNext = true;
                HotkeyCoordinator.ReconcileQuickSwitchWithFallback(
                    registration,
                true,
                requested,
                fallback);
            };

            var outer = gate.Enter(suspend, resume);
            var nested = gate.Enter(
                () => Assert.Fail("nested suspension must not recapture fallback"),
                () => Assert.Fail("nested suspension must not resume"));

            Assert.AreEqual(previous, fallback);
            Assert.IsFalse(registration.IsRegistered);

            nested.Dispose();
            Assert.IsFalse(registration.IsRegistered);

            outer.Dispose();

            Assert.IsTrue(registration.IsRegistered);
            Assert.AreEqual(previous, registration.RegisteredHotkey);
        }
        [TestMethod]
        [TestCategory("Unit")]
        public void SuspensionGate_OffOwnerEnter_RejectsBeforeNativeEffects()
        {
            Exception failure = null;
            int suspends = 0;
            var ready = new System.Threading.ManualResetEventSlim();
            HotkeySuspensionGate gate = null;
            var owner = new System.Threading.Thread(() =>
            {
                gate = new HotkeySuspensionGate(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                ready.Set();
            });
            owner.SetApartmentState(System.Threading.ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(ready.Wait(5000));
            owner.Join();
            try { gate.Enter(() => suspends++, () => { }); }
            catch (Exception ex) { failure = ex; }
            Assert.IsInstanceOfType<InvalidOperationException>(failure);
            Assert.AreEqual(0, suspends);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SuspensionGate_ForeignTokenDispose_RejectsAndPreservesOwnerResume()
        {
            Exception failure = null;
            int resumed = 0;
            var owner = new System.Threading.Thread(() =>
            {
                var gate = new HotkeySuspensionGate(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                var token = gate.Enter(() => { }, () => resumed++);
                var foreign = new System.Threading.Thread(() =>
                {
                    try { token.Dispose(); }
                    catch (Exception ex) { failure = ex; }
                });
                foreign.Start();
                foreign.Join();
                Assert.IsInstanceOfType<InvalidOperationException>(failure);
                Assert.IsTrue(gate.IsSuspended);
                token.Dispose();
                token.Dispose();
                Assert.IsFalse(gate.IsSuspended);
            });
            owner.SetApartmentState(System.Threading.ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(owner.Join(5000));
            Assert.AreEqual(1, resumed);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SuspensionGate_OffOwnerShutdown_RejectsWithoutChangingSuspension()
        {
            Exception failure = null;
            var owner = new System.Threading.Thread(() =>
            {
                var gate = new HotkeySuspensionGate(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                var token = gate.Enter(() => { }, () => { });
                var foreign = new System.Threading.Thread(() =>
                {
                    try { gate.Shutdown(); }
                    catch (Exception ex) { failure = ex; }
                });
                foreign.Start();
                foreign.Join();
                Assert.IsInstanceOfType<InvalidOperationException>(failure);
                Assert.IsTrue(gate.IsSuspended);
                token.Dispose();
            });
            owner.SetApartmentState(System.Threading.ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(owner.Join(5000));
        }

    }
}
