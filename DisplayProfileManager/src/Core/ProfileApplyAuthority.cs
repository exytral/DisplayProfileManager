using System;
using System.Threading.Tasks;

namespace DisplayProfileManager.Core
{
    internal sealed class ProfileApplyAuthority
    {
        private readonly object _sync = new object();
        private Task _tail = Task.CompletedTask;

        public Task<T> EnqueueAsync<T>(Func<Task<T>> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
                _tail = RunAfterAsync(_tail, operation, completion);

            return completion.Task;
        }

        private static async Task RunAfterAsync<T>(Task predecessor, Func<Task<T>> operation, TaskCompletionSource<T> completion)
        {
            await predecessor.ConfigureAwait(false);

            try
            {
                completion.TrySetResult(await operation().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }
    }
}
