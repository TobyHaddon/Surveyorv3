// Used to safely call a UI thread from either a UI Thread or a non-UI thread
//
// Version 10.0  01 Dec 2025
using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;

namespace Surveyor.Helper
{
    public class SafeUICall(DispatcherQueue _dispatcherQueue)
    {
        private readonly DispatcherQueue dispatcherQueue = _dispatcherQueue;

        /// <summary>
        /// Used to safely call UI thread code from non-UI threads
        /// For synchronous calls the don't return a value
        /// </summary>
        /// <param name="action"></param>
        public void Call(Action action)
        {
            var dispatcher = dispatcherQueue;
            if (dispatcher.HasThreadAccess)
            {
                action();
                return;
            }
            dispatcher.TryEnqueue(() => action());
            return;
        }


        /// <summary>
        /// Used for safely calling UI thread code from non-UI threads
        /// For async calls that do not return a value
        /// </summary>
        /// <param name="asyncAction"></param>
        /// <returns></returns>
        public Task CallAsync(Func<Task> asyncAction)
        {
            var dispatcher = dispatcherQueue;
            if (dispatcher.HasThreadAccess)
            {
                return InvokeWithGuardAsync(asyncAction);
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            dispatcher.TryEnqueue(() =>
            {
                Task task;
                try
                {
                    task = InvokeWithGuardAsync(asyncAction);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    return;
                }

                if (task.IsCompleted)
                {
                    if (task.IsFaulted)
                        tcs.SetException(task.Exception!.InnerExceptions.Count == 1 ? task.Exception.InnerException! : task.Exception);
                    else if (task.IsCanceled)
                        tcs.SetCanceled();
                    else
                        tcs.SetResult();
                    return;
                }

                // VSTHRD110 fix: observe the returned Task
                _ = task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        tcs.SetException(t.Exception!.InnerExceptions.Count == 1 ? t.Exception.InnerException! : t.Exception);
                    else if (t.IsCanceled)
                        tcs.SetCanceled();
                    else
                        tcs.SetResult();
                }, TaskScheduler.Default);
            });

            return tcs.Task;
        }



        // Internal guard to ensure exceptions are surfaced.
        private static async Task InvokeWithGuardAsync(Func<Task> asyncAction)
        {
            await asyncAction().ConfigureAwait(false);
        }

        private static async Task<T> InvokeWithGuardAsync<T>(Func<Task<T>> asyncFunc)
        {
            return await asyncFunc().ConfigureAwait(false);
        }
    }
}

