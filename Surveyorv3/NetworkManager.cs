// NetworkManager  Manages intermittent internet connections and executes registered actions when the connection is available.
//
// Version 1.0 10 Apr 2025

using CommunityToolkit.WinUI.Helpers;
using Surveyor.User_Controls;
using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Surveyor
{
    public enum Priority
    {
        Critical,
        Normal,
        Low
    }

    public partial class NetworkManager : IDisposable
    {
        private Reporter? Report { get; set; } = null;

        private readonly System.Timers.Timer _timer;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<Priority, ConcurrentBag<RegisteredAction>> _registeredActions;

        private bool _disposed;

        public NetworkManager(Reporter? _report)
        {
            Report = _report;

            _httpClient = new HttpClient();
            _registeredActions = new ConcurrentDictionary<Priority, ConcurrentBag<RegisteredAction>>();
            foreach (Priority p in Enum.GetValues(typeof(Priority)))
                _registeredActions[p] = new ConcurrentBag<RegisteredAction>();

            _timer = new System.Timers.Timer(10_000); // 10 seconds
            _timer.Elapsed +=  (s, e) => CheckConnection();
            _timer.AutoReset = true;
            _timer.Start();
        }


        /// <summary>
        /// Register an action to be executed when the internet connection is available.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="priority"></param>
        public void RegisterAction(Func<bool, Task> action, Priority priority)
        {
            var registeredAction = new RegisteredAction(action);
            _registeredActions[priority].Add(registeredAction);
        }


        /// <summary>
        /// Dispose method to clean up resources.
        /// </summary>
        public async void Dispose()
        {
            await Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected async virtual Task Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_disposed) return;

                _disposed = true;
                _timer?.Stop();

                await Task.Delay(300);
                _timer?.Dispose();
                _httpClient?.Dispose();
            }
        }


        /// <summary>
        /// Check if connected to the internet by making a request to a known URL.
        /// </summary>
        /// <returns></returns>
        public bool IsConnectedToInternet()
        {
            bool ret;

            try
            {
                ret = NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable;
                //???using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                //???using var response = await _httpClient.GetAsync("https://www.google.com/", cts.Token);
                //???return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }

            return ret;
        }




        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Check if connected to the internet and execute registered actions if idle.
        /// </summary>
        /// <returns></returns>
        private void CheckConnection()
        {
            bool isOnline = IsConnectedToInternet();

            foreach (Priority priority in Enum.GetValues(typeof(Priority)))
            {
                foreach (var registeredAction in _registeredActions[priority])
                {
                    _ = ExecuteActionIfIdle(registeredAction, isOnline);
                }
            }
        }


        /// <summary>
        /// Execute the action if it is not already running.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        private async Task ExecuteActionIfIdle(RegisteredAction action, bool isOnline)
        {
            if (action.IsRunning) return;

            try
            {
                action.IsRunning = true;
                await action.Action(isOnline);
            }
            finally
            {
                action.IsRunning = false;
            }
        }


        /// <summary>
        /// Class to hold registered actions and their state.
        /// </summary>
        private class RegisteredAction
        {
            public Func<bool, Task> Action { get; }
            public volatile bool IsRunning;

            public RegisteredAction(Func<bool, Task> action)
            {
                Action = action;
                IsRunning = false;
            }
        }
    }
}
