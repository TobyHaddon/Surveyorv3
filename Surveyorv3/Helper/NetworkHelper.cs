using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Net.Http;


namespace Surveyor.Helper
{
    internal class NetworkHelper

    {

        /// <summary>
        /// Is Windows connected to a network
        /// </summary>
        /// <returns></returns>
        public static bool IsRealNetworkAvailable()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .Any(ni =>
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Unknown &&                   
                    !ni.Description.ToLowerInvariant().Contains("virtual") &&
                    !ni.Description.ToLowerInvariant().Contains("pseudo"));
        }


        /// <summary>
        /// Is the internet accessible. The underlying check only happens every 5 seconds.
        /// If the method is called more often, the last result is returned.
        /// Calling with force = true will force a check.
        /// </summary>
        /// <returns></returns>
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
        private static DateTime _lastChecked = DateTime.MinValue;
        private static bool _lastResult = false;
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        public static async Task<bool> IsInternetAvailableHttpAsync(bool force = false)
        {
            if (!force && DateTime.UtcNow - _lastChecked < CheckInterval)
            {
                return _lastResult;
            }

            try
            {
                using var response = await _httpClient.GetAsync("https://www.google.com/generate_204");
                _lastResult = response.StatusCode == System.Net.HttpStatusCode.NoContent;
            }
            catch
            {
                _lastResult = false;
            }

            _lastChecked = DateTime.UtcNow;
            return _lastResult;
        }
    }
}
