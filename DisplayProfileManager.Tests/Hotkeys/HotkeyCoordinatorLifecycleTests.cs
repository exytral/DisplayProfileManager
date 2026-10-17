using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Hotkeys;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Settings;

namespace DisplayProfileManager.Tests.Hotkeys
{
    [TestClass]
    public class HotkeyCoordinatorLifecycleTests
    {
        private sealed class FakeRegistrar : IGlobalHotkeyRegistrar
        {
            public int UnregisterCalls;
            public int DisposeCalls;
            public int NativeThreadViolations;
            public int OwnerThread;
            public bool ThrowUnregisterOnce;
            public bool RejectCallsAfterDispose;
            private bool _nativeDisposed;

            private void Record()
            {
                if (Environment.CurrentManagedThreadId != OwnerThread)
                    Interlocked.Increment(ref NativeThreadViolations);
            }

            public int RegisterHotkey(uint virtualKey, uint modifiers, Action callback) => 1;
            public bool UnregisterHotkey(int id) { Record(); return true; }
            public int RegisterProfileHotkey(string id, HotkeyConfig config, Action callback) => 1;
            public bool UnregisterProfileHotkey(string id) { Record(); return true; }
            public void RegisterAllProfileHotkeys(Dictionary<string, HotkeyConfig> hotkeys, Func<string, Action> callbackFactory) => Record();
            public void UnregisterAllProfileHotkeys()
            {
                Record();
                if (RejectCallsAfterDispose && _nativeDisposed)
                    throw new ObjectDisposedException("Strict fake registrar");
                Interlocked.Increment(ref UnregisterCalls);
                if (ThrowUnregisterOnce)
                {
                    ThrowUnregisterOnce = false;
                    throw new InvalidOperationException("synthetic unregister failure");
                }
            }
            public void Dispose()
            {
                Record();
                _nativeDisposed = true;
                Interlocked.Increment(ref DisposeCalls);
            }
        }

        private static HotkeyCoordinator Make(Dispatcher owner, FakeRegistrar fake) =>
            new HotkeyCoordinator(fake,
                (ProfileService)RuntimeHelpers.GetUninitializedObject(typeof(ProfileService)),
                (ProfileApplyService)RuntimeHelpers.GetUninitializedObject(typeof(ProfileApplyService)),
                (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService)),
                () => { }, null, ownerDispatcher: owner);

        [TestMethod]
        [TestCategory("Unit")]
        public void Capture_OffOwnerRejectsBeforeAnyRegistrarMutation()
        {
            HotkeyCoordinator coordinator = null;
            FakeRegistrar fake = null;
            var owner = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                fake = new FakeRegistrar { OwnerThread = Environment.CurrentManagedThreadId };
                coordinator = Make(dispatcher, fake);
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(owner.Join(5000));

            Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.SuspendForHotkeyCapture());
            Assert.AreEqual(0, fake.UnregisterCalls);
            Assert.AreEqual(0, fake.NativeThreadViolations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Dispose_LiveForeignThreadQueuesOneOwnerTeardown()
        {
            Dispatcher dispatcher = null;
            HotkeyCoordinator coordinator = null;
            FakeRegistrar fake = null;
            var started = new ManualResetEventSlim();
            var owner = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                fake = new FakeRegistrar { OwnerThread = Environment.CurrentManagedThreadId };
                coordinator = Make(dispatcher, fake);
                started.Set();
                Dispatcher.Run();
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(started.Wait(5000));
            try
            {
                coordinator.Dispose();
                coordinator.Dispose();
                dispatcher.Invoke(() => { }, DispatcherPriority.Background);

                Assert.AreEqual(1, fake.DisposeCalls);
                Assert.AreEqual(1, fake.UnregisterCalls);
                Assert.AreEqual(0, fake.NativeThreadViolations);
            }
            finally
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                Assert.IsTrue(owner.Join(5000));
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Dispose_ForeignAfterShutdownReportsIncompleteAndNeverCallsNative()
        {
            Dispatcher dispatcher = null;
            HotkeyCoordinator coordinator = null;
            FakeRegistrar fake = null;
            var owner = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                fake = new FakeRegistrar { OwnerThread = Environment.CurrentManagedThreadId };
                coordinator = Make(dispatcher, fake);
                dispatcher.InvokeShutdown();
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(owner.Join(5000));

            Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.Dispose());
            Assert.AreEqual(0, fake.DisposeCalls);
            Assert.AreEqual(0, fake.UnregisterCalls);
            Assert.AreEqual(0, fake.NativeThreadViolations);
        }
        [TestMethod]
        [TestCategory("Unit")]
        public void Dispose_QueuedThenShutdown_MustReportIncompleteToOriginalCaller()
        {
            Dispatcher dispatcher = null;
            HotkeyCoordinator coordinator = null;
            FakeRegistrar fake = null;
            var ready = new ManualResetEventSlim();
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var owner = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                fake = new FakeRegistrar { OwnerThread = Environment.CurrentManagedThreadId };
                coordinator = Make(dispatcher, fake);
                ready.Set();
                Dispatcher.Run();
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(ready.Wait(5000));
            dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                entered.Set();
                release.Wait(5000);
                dispatcher.InvokeShutdown();
            }));
            Assert.IsTrue(entered.Wait(5000));
            Exception failure = null;
            var foreign = new Thread(() =>
            {
                try { coordinator.Dispose(); }
                catch (Exception ex) { failure = ex; }
            });
            foreign.Start();
            try
            {
                // Give a queued disposal attempt a chance to run only after owner shutdown.
                Assert.IsTrue(SpinWait.SpinUntil(() => foreign.ThreadState.HasFlag(ThreadState.WaitSleepJoin), 1500)
                    || SpinWait.SpinUntil(() => !foreign.IsAlive, 1500));
            }
            finally
            {
                release.Set();
                Assert.IsTrue(foreign.Join(7000));
                Assert.IsTrue(owner.Join(7000));
            }
            Assert.IsNotNull(failure, "A void Dispose must not return normally without owner cleanup");
            Assert.AreEqual(0, fake.DisposeCalls);
            Assert.AreEqual(0, fake.NativeThreadViolations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Dispose_StrictNativeLifetime_DeferredRegistrarDisposeUntilUnregisterCompleted()
        {
            Exception first = null;
            Exception second = null;
            int afterFirstDisposeCount = -1;
            FakeRegistrar fake = null;
            var owner = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                fake = new FakeRegistrar
                {
                    OwnerThread = Environment.CurrentManagedThreadId,
                    ThrowUnregisterOnce = true,
                    RejectCallsAfterDispose = true
                };
                var coordinator = Make(dispatcher, fake);
                try { coordinator.Dispose(); }
                catch (Exception ex) { first = ex; }
                afterFirstDisposeCount = fake.DisposeCalls;
                try { coordinator.Dispose(); }
                catch (Exception ex) { second = ex; }
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(owner.Join(5000));
            Assert.IsNotNull(first);
            Assert.AreEqual(0, afterFirstDisposeCount, "Registrar must remain live until retry succeeds");
            Assert.IsNull(second);
            Assert.AreEqual(2, fake.UnregisterCalls);
            Assert.AreEqual(1, fake.DisposeCalls);
            Assert.AreEqual(0, fake.NativeThreadViolations);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Dispose_FirstUnregisterThrows_RetryCompletesOutstandingStages()
        {
            Exception first = null;
            Exception second = null;
            FakeRegistrar fake = null;
            var owner = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                fake = new FakeRegistrar { OwnerThread = Environment.CurrentManagedThreadId, ThrowUnregisterOnce = true };
                var coordinator = Make(dispatcher, fake);
                try { coordinator.Dispose(); }
                catch (Exception ex) { first = ex; }
                try { coordinator.Dispose(); }
                catch (Exception ex) { second = ex; }
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            Assert.IsTrue(owner.Join(5000));
            Assert.IsNotNull(first);
            Assert.IsNull(second);
            Assert.AreEqual(2, fake.UnregisterCalls);
            Assert.AreEqual(1, fake.DisposeCalls);
            Assert.AreEqual(0, fake.NativeThreadViolations);
        }
    }
}
