using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Surveyor.Helper
{
    class TimePositionHelper
    {
        /// <summary>
        /// Format the time position as a string
        /// </summary>
        /// <param name="timePosition"></param>
        /// <returns></returns>
        public static string Format(TimeSpan timePosition, int dp)
        {
            if (dp == 2)
                // Format to 2 decimal places
                return $"{Math.Round(timePosition.TotalSeconds, 2):F2} secs";
            else if (dp == 3)
                // Format to 3 decimal places
                return $"{Math.Round(timePosition.TotalSeconds, 3):F3} secs";
            else
                // Not implemented exception
                throw new NotImplementedException();
        }


        /// <summary>
        /// Parse the time position from a string
        /// must be a number followed by "secs" or "s" in lower or upper case
        /// </summary>
        /// <param name="timePosition"></param>
        /// <param name="timeSpan"></param>
        /// <returns></returns>
        public static bool Parse(string timePosition, out TimeSpan? timeSpan)
        {
            bool ret = false;
            timeSpan = null;

            if (string.IsNullOrWhiteSpace(timePosition))
                return false;

            // Trim and normalize input
            timePosition = timePosition.Trim().ToLowerInvariant();

            // Regex: float or int + 's' or 'secs'
            var match = Regex.Match(timePosition, @"^(\d+(\.\d+)?)(s|secs)$");

            if (match.Success)
            {
                string numberPart = match.Groups[1].Value;

                // Try parse the numeric portion
                if (double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                {
                    timeSpan = TimeSpan.FromSeconds(seconds);
                    return true;
                }
            }

            return ret;
        }


        /// <summary>
        /// Return the timespan as a double in seconds
        /// </summary>
        public static double ToSeconds(TimeSpan timePosition, int dp)
        {
            if (dp == 2)
                // Format to 2 decimal places
                return Math.Round(timePosition.TotalSeconds, 2);
            else if (dp == 3)
                // Format to 3 decimal places
                return Math.Round(timePosition.TotalSeconds, 3);
            else
                // Not implemented exception
                throw new NotImplementedException();

        }
    }
}
