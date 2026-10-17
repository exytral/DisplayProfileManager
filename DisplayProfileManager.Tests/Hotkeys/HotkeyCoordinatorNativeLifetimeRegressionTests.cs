using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using DisplayProfileManager.Hotkeys;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.Hotkeys
{
    [TestClass]
    public sealed class HotkeyCoordinatorNativeLifetimeRegressionTests
    {
        private sealed class StrictRegistrar : IGlobalHotkeyRegistrar
        {
            public int OwnerThread;
            public int WrongThread;
            public int UnregisterCount;
            public int DisposeCount;
            public int CallsAfterDispose;
            public int UnregisterFailuresRemaining;
            public int DisposeFailuresRemaining;
            public bool Disposed;
            private void Check()
            {
                if (Thread.CurrentThread.ManagedThreadId != OwnerThread)
                    Interlocked.Increment(ref WrongThread);
            }
            public int RegisterHotkey(uint k, uint m, Action callback) { Check(); return 1; }
            public bool UnregisterHotkey(int id) { Check(); return true; }
            public int RegisterProfileHotkey(string id, HotkeyConfig config, Action callback) { Check(); return 1; }
            public bool UnregisterProfileHotkey(string id) { Check(); return true; }
            public void RegisterAllProfileHotkeys(Dictionary<string, HotkeyConfig> all, Func<string,Action> factory) { Check(); }
            public void UnregisterAllProfileHotkeys()
            {
                Check();
                if (Disposed) { CallsAfterDispose++; throw new ObjectDisposedException("StrictRegistrar"); }
                UnregisterCount++;
                if (UnregisterFailuresRemaining > 0)
                {
                    UnregisterFailuresRemaining--;
                    throw new InvalidOperationException("synthetic native unregister failure");
                }
            }
            public void Dispose()
            {
                Check();
                if (Disposed) { CallsAfterDispose++; throw new ObjectDisposedException("StrictRegistrar"); }
                DisposeCount++;
                if (DisposeFailuresRemaining > 0)
                {
                    DisposeFailuresRemaining--;
                    throw new InvalidOperationException("synthetic native disposal failure");
                }
                Disposed = true;
            }
        }

        private static HotkeyCoordinator Create(StrictRegistrar r, Dispatcher d) =>
            new HotkeyCoordinator(r,
                (ProfileService)RuntimeHelpers.GetUninitializedObject(typeof(ProfileService)),
                (ProfileApplyService)RuntimeHelpers.GetUninitializedObject(typeof(ProfileApplyService)),
                (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService)),
                () => {}, null, ownerDispatcher: d);

        private static void Sta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(12)), "STA test did not finish");
            if (failure != null) throw failure;
        }

        private static Exception Attempt(HotkeyCoordinator coordinator)
        {
            try { coordinator.Dispose(); return null; }
            catch (Exception ex) { return ex; }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void B2_OneTimeUnregisterFailure_KeepsNativeRegistrarLiveUntilRetryCompletes()
        {
            Sta(() =>
            {
                var r = new StrictRegistrar { OwnerThread = Thread.CurrentThread.ManagedThreadId, UnregisterFailuresRemaining = 1 };
                var coordinator = Create(r, Dispatcher.CurrentDispatcher);
                Assert.IsNotNull(Attempt(coordinator), "First unregister error must be communicated");
                Assert.AreEqual(1, r.UnregisterCount);
                Assert.AreEqual(0, r.DisposeCount, "Native lifetime cannot end while unregister remains incomplete");
                Assert.IsFalse(r.Disposed);
                Assert.IsNull(Attempt(coordinator), "Retry must complete with live native registrar");
                Assert.AreEqual(2, r.UnregisterCount);
                Assert.AreEqual(1, r.DisposeCount);
                Assert.IsTrue(r.Disposed);
                Assert.IsNull(Attempt(coordinator), "Completed Dispose must be idempotent");
                Assert.AreEqual(2, r.UnregisterCount);
                Assert.AreEqual(1, r.DisposeCount);
                Assert.AreEqual(0, r.CallsAfterDispose);
                Assert.AreEqual(0, r.WrongThread);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void B2_TwoUnregisterFailures_MustNotPrematurelyDisposeRegistrar()
        {
            Sta(() =>
            {
                var r = new StrictRegistrar { OwnerThread = Thread.CurrentThread.ManagedThreadId, UnregisterFailuresRemaining = 2 };
                var c = Create(r, Dispatcher.CurrentDispatcher);
                Assert.IsNotNull(Attempt(c));
                Assert.IsNotNull(Attempt(c));
                Assert.AreEqual(0, r.DisposeCount);
                Assert.IsFalse(r.Disposed);
                Assert.AreEqual(2, r.UnregisterCount);
                Assert.IsNull(Attempt(c));
                Assert.AreEqual(3, r.UnregisterCount);
                Assert.AreEqual(1, r.DisposeCount);
                Assert.AreEqual(0, r.CallsAfterDispose);
                Assert.AreEqual(0, r.WrongThread);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void B2_OnlyNativeDisposeFailsOnce_UnregisterNotRepeated()
        {
            Sta(() =>
            {
                var r = new StrictRegistrar { OwnerThread = Thread.CurrentThread.ManagedThreadId, DisposeFailuresRemaining = 1 };
                var c = Create(r, Dispatcher.CurrentDispatcher);
                Assert.IsNotNull(Attempt(c), "Synthetic native Dispose failure must be reported");
                Assert.AreEqual(1, r.UnregisterCount);
                Assert.AreEqual(1, r.DisposeCount);
                Assert.IsFalse(r.Disposed);
                Assert.IsNull(Attempt(c), "Only failed native Dispose stage must retry");
                Assert.AreEqual(1, r.UnregisterCount);
                Assert.AreEqual(2, r.DisposeCount);
                Assert.IsTrue(r.Disposed);
                Assert.AreEqual(0, r.CallsAfterDispose);
                Assert.AreEqual(0, r.WrongThread);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void B1_QueuedDisposeShutdown_ReportsFailureToOriginalCallerWithoutForeignNativeEffect()
        {
            Dispatcher dispatcher = null;
            HotkeyCoordinator coordinator = null;
            StrictRegistrar r = null;
            Exception ownerError = null;
            Exception callerError = null;
            var ready = new ManualResetEventSlim();
            var entered = new ManualResetEventSlim();
            var allowShutdown = new ManualResetEventSlim();
            var owner = new Thread(() =>
            {
                try
                {
                    dispatcher = Dispatcher.CurrentDispatcher;
                    r = new StrictRegistrar { OwnerThread = Thread.CurrentThread.ManagedThreadId };
                    coordinator = Create(r, dispatcher);
                    ready.Set();
                    dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                    {
                        entered.Set();
                        if (!allowShutdown.Wait(TimeSpan.FromSeconds(9))) throw new TimeoutException("gate not released");
                        dispatcher.InvokeShutdown();
                    }));
                    Dispatcher.Run();
                }
                catch (Exception ex) { ownerError = ex; ready.Set(); }
            });
            owner.IsBackground = true;
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(9)));
            Thread foreign = null;
            try
            {
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(9)));
                foreign = new Thread(() => callerError = Attempt(coordinator));
                foreign.IsBackground = true;
                foreign.Start();
                Assert.IsTrue(SpinWait.SpinUntil(() => foreign.ThreadState.HasFlag(ThreadState.WaitSleepJoin), 3000)
                    || SpinWait.SpinUntil(() => !foreign.IsAlive, 1500));
            }
            finally { allowShutdown.Set(); }
            Assert.IsTrue(foreign.Join(TimeSpan.FromSeconds(9)));
            Assert.IsTrue(owner.Join(TimeSpan.FromSeconds(9)));
            Assert.IsNull(ownerError);
            Assert.IsNotNull(callerError);
            Assert.AreEqual(0, r.UnregisterCount);
            Assert.AreEqual(0, r.DisposeCount);
            Assert.AreEqual(0, r.WrongThread);
        }
    }
}
