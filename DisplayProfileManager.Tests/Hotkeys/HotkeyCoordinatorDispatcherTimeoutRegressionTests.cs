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
    public sealed class HotkeyCoordinatorDispatcherTimeoutRegressionTests
    {
        private sealed class Fake : IGlobalHotkeyRegistrar
        {
            public int OwnerId;
            public int WrongThread;
            public int UnregisterCalls;
            public int DisposeCalls;
            private void Owner() { if (Thread.CurrentThread.ManagedThreadId != OwnerId) Interlocked.Increment(ref WrongThread); }
            public int RegisterHotkey(uint key, uint mods, Action handler) { Owner(); return 1; }
            public bool UnregisterHotkey(int id) { Owner(); return true; }
            public int RegisterProfileHotkey(string id, HotkeyConfig config, Action handler) { Owner(); return 1; }
            public bool UnregisterProfileHotkey(string id) { Owner(); return true; }
            public void RegisterAllProfileHotkeys(Dictionary<string, HotkeyConfig> entries, Func<string, Action> factory) => Owner();
            public void UnregisterAllProfileHotkeys() { Owner(); UnregisterCalls++; }
            public void Dispose() { Owner(); DisposeCalls++; }
        }
        [TestMethod]
        [TestCategory("Unit")]
        public void B1_FiveSecondOwnerQueueTimeout_AbortsAndThenOwnerCanFinishOnce()
        {
            Dispatcher dispatcher = null;
            HotkeyCoordinator coordinator = null;
            Fake fake = null;
            Exception ownerError = null, callerError = null;
            var ready = new ManualResetEventSlim();
            var blockerEntered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var owner = new Thread(() =>
            {
                try
                {
                    dispatcher = Dispatcher.CurrentDispatcher;
                    fake = new Fake { OwnerId = Thread.CurrentThread.ManagedThreadId };
                    coordinator = new HotkeyCoordinator(fake,
                        (ProfileService)RuntimeHelpers.GetUninitializedObject(typeof(ProfileService)),
                        (ProfileApplyService)RuntimeHelpers.GetUninitializedObject(typeof(ProfileApplyService)),
                        (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService)),
                        () => { }, null, ownerDispatcher: dispatcher);
                    ready.Set();
                    dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                    {
                        blockerEntered.Set();
                        if (!release.Wait(TimeSpan.FromSeconds(12))) throw new TimeoutException("Fixture gate not released");
                        coordinator.Dispose();
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
                Assert.IsTrue(blockerEntered.Wait(TimeSpan.FromSeconds(9)));
                foreign = new Thread(() =>
                {
                    try { coordinator.Dispose(); } catch (Exception ex) { callerError = ex; }
                });
                foreign.IsBackground = true;
                foreign.Start();
                Assert.IsTrue(foreign.Join(TimeSpan.FromSeconds(9)), "Bounded foreign Dispose must terminate");
                Assert.IsNotNull(callerError, "Timeout before owner completion cannot look like successful Dispose");
                Assert.AreEqual(0, fake.DisposeCalls);
            }
            finally { release.Set(); }
            Assert.IsTrue(owner.Join(TimeSpan.FromSeconds(9)));
            Assert.IsNull(ownerError);
            Assert.AreEqual(1, fake.UnregisterCalls, "Owner must finish unregister exactly once");
            Assert.AreEqual(1, fake.DisposeCalls, "Owner must finish registrar Dispose exactly once");
            Assert.AreEqual(0, fake.WrongThread);
        }
    }
}
