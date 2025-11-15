using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.ApplicationInsights;

namespace Surveyor.Helper
{

    public static class TelemetryLogger
    {
        /// <summary>
        /// Telemetry client for Application Insights
        /// </summary>
        public static TelemetryClient? Client { get; set; }
        private static string userIdHash = string.Empty;

        /// <summary>
        /// TrackEvent - Not public implement a method for event type
        /// </summary>
        /// <param name="name"></param>
        /// <param name="props"></param>
        private static void TrackEvent(string name, IDictionary<string, string>? props = null)
        {
            Client?.TrackEvent(name, props);

            // The inputs are Windows users + machine name
            string rawId = Environment.UserName + Environment.MachineName;
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(rawId);

            // Create a hash to keep info private
            var hash = sha256.ComputeHash(bytes);

            // Convert to hex string      
            userIdHash = Convert.ToHexString(hash); 
        }


        /// <summary>
        /// TrackTrace
        /// </summary>
        /// <param name="message"></param>
        public static void TrackTrace(string message)
        {
            Client?.TrackTrace(message);
        }


        /// <summary>
        /// TrackException
        /// </summary>
        /// <param name="ex"></param>
        public static void TrackException(Exception ex)
        {
            Client?.TrackException(ex);
        }


        ///
        /// CUSTOM EVENTS
        /// 

        /// <summary>
        /// TrackSettingTelemetry
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="telemetryEnabled"></param>
        public static void TrackSettingTelemetry(bool telemetryEnabled)
        {
            Client?.TrackEvent("SettingTelemetry", new Dictionary<string, string>
            {
                { "UserId", userIdHash },
                { "TelemetryEnabled", telemetryEnabled.ToString() }
            });
        }


        /// <summary>
        /// TrackAppStartStop
        /// </summary>
        /// <param name="telemetryEnabled"></param>
        public enum TrackAppStartStopType
        {
            AppStart,
            AppStopOk,
            AppStopCrash
        }
        public static void TrackAppStartStop(TrackAppStartStopType trackAppStartStopType)
        {
            Client?.TrackEvent("SettingTelemetry", new Dictionary<string, string>
            {
                { "UserId", userIdHash },
                { "AppStartStop", trackAppStartStopType.ToString() }
            });
        }

    }
}
