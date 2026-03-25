using System;
using System.Globalization;
using System.Text.RegularExpressions;

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
        /// Return the TimeSpan as a double in seconds
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


        /// <summary>
        /// Calculate the frame index from the time position and frame rate
        /// </summary>
        /// <param name="position"></param>
        /// <param name="frameRate"></param>
        /// <returns></returns>
        public static long ToFrameIndex(TimeSpan position, double frameRate)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRate);

            double frame = position.TotalSeconds * frameRate;
            return (long)Math.Round(frame, MidpointRounding.AwayFromZero);
        }


        /// <summary>
        /// Calculate the frame index from the time position and frame stride.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="frameStride">Duration of one frame (e.g. 33.333ms at 30fps)</param>
        /// <returns></returns>
        public static long ToFrameIndex(TimeSpan position, TimeSpan frameStride)
        {
            if (frameStride <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(frameStride), "Frame stride must be greater than zero.");

            double frame = position.Ticks / (double)frameStride.Ticks;
            return (long)Math.Round(frame, MidpointRounding.AwayFromZero);
        }


        /// <summary>
        /// Compare two media TimeSpan values and determine if they correspond 
        /// to the same frame index at the given frame rate.
        /// </summary>
        /// <param name="requested">The requested time position</param>
        /// <param name="actual">The actual time position</param>
        /// <param name="frameRate">Number of frames per second</param>
        /// <returns></returns>
        public static bool IsExactFrameMatch(TimeSpan requested, TimeSpan actual, double frameRate)
        {
            return ToFrameIndex(requested, frameRate) == ToFrameIndex(actual, frameRate);
        }


        /// <summary>
        /// Compare two media TimeSpan values and determine if they correspond
        /// to the same frame index at the given frame stride.
        /// </summary>
        /// <param name="requested"></param>
        /// <param name="actual"></param>
        /// <param name="frameStride">Duration of one frame</param>
        /// <returns></returns>
        public static bool IsExactFrameMatch(TimeSpan requested, TimeSpan actual, TimeSpan frameStride)
        {
            return ToFrameIndex(requested, frameStride) == ToFrameIndex(actual, frameStride);
        }
    }
}
