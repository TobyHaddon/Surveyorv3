// SurveyMeasurementHelper
// 
// Version 1.0  26 Feb 2025

namespace Surveyor.Helper
{
    using Surveyor.Events;
    using System;

    public static class SurveyMeasurementHelper
    {
        /// <summary>
        // Ensures the measurments points are not mixed up
        // How it works:
        // Calculate the angle using Math.Atan2(deltaY, deltaX), which gives the inclination of the line in degrees.
        // Compare the angles:
        //    -If their absolute difference is greater than 45 degrees, the corresponding points are incorrect.
        //    -We then swap TargetBLeft and TargetBRight to correct the mistake.
        /// </summary>
        /// <param name="measurement"></param>
        public static void EnsureCorrectCorrespondence(SurveyMeasurement measurement)
        {
            // Calculate angles in degrees
            double angleLeft = CalculateAngle(measurement.LeftXA, measurement.LeftYA, measurement.LeftXB, measurement.LeftYB);
            double angleRight = CalculateAngle(measurement.RightXA, measurement.RightYA, measurement.RightXB, measurement.RightYB);

            // Check if the angles are significantly different (> 45 degrees)
            if (Math.Abs(angleLeft - angleRight) > 45)
            {
                SwapRightTargets(measurement);
            }
        }


        ///
        /// PRIVATE
        ///


        /// <summary>
        /// Returns the angle between two points in degrees
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="y1"></param>
        /// <param name="x2"></param>
        /// <param name="y2"></param>
        /// <returns></returns>
        private static double CalculateAngle(double x1, double y1, double x2, double y2)
        {
            double deltaY = y2 - y1;
            double deltaX = x2 - x1;
            double angleRad = Math.Atan2(deltaY, deltaX); // Angle in radians
            double angleDeg = angleRad * (180.0 / Math.PI); // Convert to degrees
            return angleDeg;
        }


        /// <summary>
        /// Swaps the Right Target A and Right Target B coordinates
        /// </summary>
        /// <param name="measurement"></param>
        private static void SwapRightTargets(SurveyMeasurement measurement)
        {
            // Swap Right A and Right B coordinates
            (measurement.RightXA, measurement.RightXB) = (measurement.RightXB, measurement.RightXA);
            (measurement.RightYA, measurement.RightYB) = (measurement.RightYB, measurement.RightYA);
        }
    }
}
