using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Events;
using SurveyorCalibrationData;
using System;
using System.Threading.Tasks;
using Windows.Foundation;


// SurveyMeasurementHelper
// 
// Version 1.0  26 Feb 2025
// Version 1.1  19 Aug 2025
// Move CheckIfEventMeasurementsAreUpToDate, DoMeasurementAndRulesCalculations, DoRulesCalculations
// From MainWindow.cs to this helper so they can be accessed from the bulk export utility


namespace Surveyor.Helper
{

    public static class SurveyMeasurementHelper
    {
        /// <summary>
        /// Get the Calibration ID from the preferred calibration data and check if was used for
        /// all the event EventMeasurements.  If not then ask the user if they want to update the
        /// calculation.
        /// The media player must be open so the frame width and height is known
        /// </summary>
        /// <returns>true if anything changed</returns>
        public static async Task<bool> CheckIfEventMeasurementsAreUpToDateAsync(StereoProjection stereoProjection, Survey survey, int frameWidth, int frameHeight, XamlRoot? xamlRoot, bool forceReCalc)
        {
            bool ret = false;
            
            // Get the Calibration ID from the preferred calibration data
            if (survey is not null)
            {
                CalibrationData? calibrationData = survey!.Data.Calibration.GetPreferredCalibrationData(frameWidth, frameHeight);

                if (calibrationData is not null)
                {
                    Guid? calibrationID = calibrationData.CalibrationID;

                    if (calibrationID is not null)
                    {
                        // Check if the preferred calibration data is the one being using for
                        // the current event measurements calculations
                        bool upToDate = true;
                        if (!forceReCalc)
                        {
                            foreach (Event evt in survey.Data.Events.EventList)
                            {
                                if (evt.EventDataType == SurveyDataType.SurveyMeasurementPoints && evt.EventData is not null)
                                {
                                    SurveyMeasurement surveyMeasurement = (SurveyMeasurement)evt.EventData;
                                    if (surveyMeasurement.CalibrationID != calibrationID || surveyMeasurement.Measurement == -1)
                                    {
                                        upToDate = false;
                                        break;
                                    }
                                }
                                else if (evt.EventDataType == SurveyDataType.SurveyStereoPoint && evt.EventData is not null)
                                {
                                    SurveyStereoPoint surveyStereoPoint = (SurveyStereoPoint)evt.EventData;
                                    if (surveyStereoPoint.CalibrationID != calibrationID)
                                    {
                                        upToDate = false;
                                        break;
                                    }
                                }
                            }
                        }

                        if (!upToDate && !forceReCalc)
                        {
                            // Ask the user if they want to update the event measurements
                            string message = $"The current event measurements are not up to date with the preferred calibration data. Do you want to update the event measurements?";
                            string primaryButtonText = "Yes";
                            string secondaryButtonText = "No";

                            // Ask the user
                            ContentDialog confirmationDialog = new()
                            {
                                Title = "Update Measurements",
                                Content = message,
                                PrimaryButtonText = primaryButtonText,
                                SecondaryButtonText = secondaryButtonText,
                                CloseButtonText = "Cancel",

                                // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                                XamlRoot = xamlRoot
                            };

                            // Display the dialog
                            ContentDialogResult result = await confirmationDialog.ShowAsync();

                            if (result == ContentDialogResult.Primary)
                            {
                                upToDate = false;
                            }
                            else if (result == ContentDialogResult.Secondary)
                            {
                                upToDate = true;
                            }
                        }

                        if (!upToDate || forceReCalc)
                        {
                            // Update the event measurements if the Calibration ID is different
                            // there has been a recalibration
                            foreach (Event evt in survey.Data.Events.EventList)
                            {
                                if (evt.EventData is not null)
                                {
                                    if (evt.EventDataType == SurveyDataType.SurveyMeasurementPoints)
                                    {
                                        SurveyMeasurement surveyMeasurement = (SurveyMeasurement)evt.EventData;
                                        if (surveyMeasurement.CalibrationID != calibrationID || surveyMeasurement.Measurement == -1 || forceReCalc)
                                        {
                                            // Recalculate for a measurement
                                            if (DoMeasurementAndRulesCalculations(stereoProjection, 
                                                                                  survey, 
                                                                                  surveyMeasurement))
                                            {
                                                // Updates were make to the event
                                                ret = true;
                                                survey.Data.Events.IsDirty = true;
                                            }
                                        }
                                    }
                                    else if (evt.EventDataType == SurveyDataType.SurveyStereoPoint)
                                    {
                                        SurveyStereoPoint surveyStereoPoint = (SurveyStereoPoint)evt.EventData;
                                        if (surveyStereoPoint.CalibrationID != calibrationID || forceReCalc)
                                        {
                                            // Recalculate for a stereo point
                                            if (DoRulesCalculations(stereoProjection, 
                                                                    survey, 
                                                                    surveyStereoPoint))
                                            {
                                                // Updates were make to the event
                                                ret = true;
                                                survey.Data.Events.IsDirty = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// Populate the SurveyMeasurement with the measurement calculates from the stereo projection
        /// Note the LeftX, LeftY, RightX, RightY should have already been loaded in 
        /// SurveyMeasurement surveyMeasurement
        /// Survey rules are also calculated
        /// </summary>
        /// <param name="surveyMeasurement"></param>
        /// <returns></returns>
        public static bool DoMeasurementAndRulesCalculations(StereoProjection stereoProjection, Survey survey, SurveyMeasurement surveyMeasurement)
        {
            bool updated = false;

            if (stereoProjection.PointsLoad(
                new Point(surveyMeasurement.LeftXA, surveyMeasurement.LeftYA),
                new Point(surveyMeasurement.LeftXB, surveyMeasurement.LeftYB),
                new Point(surveyMeasurement.RightXA, surveyMeasurement.RightYA),
                new Point(surveyMeasurement.RightXB, surveyMeasurement.RightYB)) == true)
            {

                // Calculate fish length
                double? measurement = stereoProjection.Measurement();
                surveyMeasurement.Measurement = measurement;

                SurveyRulesCalc newRules = new();
                newRules.ApplyCalculations(stereoProjection);

                // Apply the survey rules
                if (survey is not null &&
                    survey.Data.SurveyRules.SurveyRulesActive == true)
                {
                    newRules.ApplyRules(survey.Data.SurveyRules.SurveyRulesData);
                }
                else
                {
                    // No rules applied
                    newRules.ClearRules();  // This clears the rules and but the calculations
                }

                if (!newRules.Equals(surveyMeasurement.SurveyRulesCalc))
                {
                    surveyMeasurement.SurveyRulesCalc = newRules;
                    updated = true;
                }

                // Record the calibration data Guid used to calculate the measurement
                // This is used to enable recalculation of the measurement if the calibration data is changed
                Guid? newCalibrationID = stereoProjection.GetCalibrationID();
                Guid? currentCalibrationID = surveyMeasurement.CalibrationID;
                if (!Nullable.Equals(currentCalibrationID, newCalibrationID))
                {
                    surveyMeasurement.CalibrationID = newCalibrationID;
                    updated = true;
                }
            }

            return updated;
        }


        /// <summary>
        /// Populate the SurveyMeasurement with the measurement calculates from the stereo projection
        /// Note the LeftX, LeftY, RightX, RightY should have already been loaded in 
        /// SurveyMeasurement surveyMeasurement
        /// Survey rules are also calculated
        /// </summary>
        /// <param name="surveyMeasurement"></param>
        /// <returns></returns>
        public static bool DoRulesCalculations(StereoProjection stereoProjection, Survey survey, SurveyStereoPoint surveyStereoPoint)
        {
            bool updated = false;

            if (stereoProjection.PointsLoad(
                new Point(surveyStereoPoint.LeftX, surveyStereoPoint.LeftY),
                new Point(surveyStereoPoint.RightX, surveyStereoPoint.RightY)) == true)
            {
                SurveyRulesCalc newRules = new();
                newRules.ApplyCalculations(stereoProjection);

                // Apply the survey rules
                if (survey is not null &&
                    survey.Data.SurveyRules.SurveyRulesActive == true)
                {
                    newRules.ApplyRules(survey.Data.SurveyRules.SurveyRulesData);
                }
                else
                {
                    // No rules to apply
                    newRules.ClearRules();  // This clears the rules and but the calculations
                }

                if (!newRules.Equals(surveyStereoPoint.SurveyRulesCalc))
                {
                    surveyStereoPoint.SurveyRulesCalc = newRules;
                    updated = true;
                }

                // Record the calibration data Guid used to calculate the measurement
                // This is used to enable recalculation of the measurement if the calibration data is changed
                Guid? newCalibrationID = stereoProjection.GetCalibrationID();
                Guid? currentCalibrationID = surveyStereoPoint.CalibrationID;
                if (!Nullable.Equals(currentCalibrationID, newCalibrationID))
                {
                    surveyStereoPoint.CalibrationID = newCalibrationID;
                    updated = true;
                }
            }

            return updated;
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
