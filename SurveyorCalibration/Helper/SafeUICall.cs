// Used to safely call a UI thread from either a UI Thread or a non-UI thread
//
// Version 10.0  01 Dec 2025
// Version 11.0  28 Feb 2025
//   Added tracking code to allow checking the backlog of pending requests in the 
//   UI thread
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;

namespace Surveyor.Helper
{
    public class SafeUICall(DispatcherQueue _dispatcherQueue)
    {
        private readonly DispatcherQueue dispatcherQueue = _dispatcherQueue;

        private long _nextWorkId = 0;
        private int _pendingCount = 0;
        private int _runningCount = 0;

        private const int RecentWorkLimit = 64;
        private readonly ConcurrentQueue<string> _recentWork = new();

        public int PendingCount => Volatile.Read(ref _pendingCount);
        public int RunningCount => Volatile.Read(ref _runningCount);

        /// <summary>
        /// Returns a snapshot of recently enqueued UI work (most recent last).
        /// Each entry includes a work id and an optional label.
        /// </summary>
        public IReadOnlyList<string> GetRecentWorkSnapshot()
        {
            return [.. _recentWork];
        }

        private string TrackEnqueue(string label)
        {
            long id = Interlocked.Increment(ref _nextWorkId);
            string entry = $"UIWork#{id} {label}";

            _recentWork.Enqueue(entry);
            while (_recentWork.Count > RecentWorkLimit && _recentWork.TryDequeue(out _)) { }

            Interlocked.Increment(ref _pendingCount);
            return entry;
        }

        private void TrackStart()
        {
            Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _runningCount);
        }

        private void TrackEnd()
        {
            Interlocked.Decrement(ref _runningCount);
        }


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

            string label = TrackEnqueue(action.Method.Name);

            dispatcher.TryEnqueue(() =>
            {
                TrackStart();
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SafeUICall.Call: {label} threw: {ex}");
                    throw;
                }
                finally
                {
                    TrackEnd();
                }
            });
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

            string label = TrackEnqueue(asyncAction.Method.Name);

            dispatcher.TryEnqueue(() =>
            {
                TrackStart();

                Task task;
                try
                {
                    task = InvokeWithGuardAsync(asyncAction);
                }
                catch (Exception ex)
                {
                    TrackEnd();
                    tcs.SetException(ex);
                    Debug.WriteLine($"SafeUICall.CallAsync: {label} threw before returning Task: {ex}");
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
                    try
                    {
                        if (t.IsFaulted)
                            tcs.SetException(t.Exception!.InnerExceptions.Count == 1 ? t.Exception.InnerException! : t.Exception);
                        else if (t.IsCanceled)
                            tcs.SetCanceled();
                        else
                            tcs.SetResult();
                    }
                    finally
                    {
                        TrackEnd();
                    }
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

