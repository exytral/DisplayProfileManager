using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Profiles;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileApplyInteractionTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task WallpaperApplyHelper_RunsBlockingWallpaperWorkOffCallerThread()
        {
            var delegateStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseDelegate = new ManualResetEventSlim(false);
            var taskReturned = new TaskCompletionSource<Task<bool>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var caller = new Thread(() =>
            {
                try
                {
                    Task<bool> wallpaperTask = ProfileApplyService.ApplyWallpaperOffUiAsync(new WallpaperSettings(), _ =>
                    {
                        delegateStarted.TrySetResult(true);
                        releaseDelegate.Wait(TestContext.CancellationToken);
                        return true;
                    });
                    taskReturned.TrySetResult(wallpaperTask);
                }
                catch (Exception ex)
                {
                    taskReturned.TrySetException(ex);
                }
            })
            {
                IsBackground = true
            };

            caller.Start();
            try
            {
                await delegateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken);
                Task<bool> wallpaperTask = await taskReturned.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken);
                Assert.IsFalse(wallpaperTask.IsCompleted);

                releaseDelegate.Set();

                Assert.IsTrue(await wallpaperTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken));
            }
            finally
            {
                releaseDelegate.Set();
                caller.Join(2000);
            }
        }
    }
}
