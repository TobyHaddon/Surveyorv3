// Contains the high level calibration workflow method
// 
using Microsoft.UI.Xaml.Controls;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Surveyor.Controls
{
    public sealed partial class UniversalCalibrationHead : UserControl
    {
        /// <summary>
        /// Find the start and end of the calibration board zone for this head
        /// This is the first and last time the calibration boards is seen in 
        /// the .MP4
        /// </summary>
        /// <returns></returns>
        public async Task<int> FindCalibrationBoardZoneAsync()
        {
            int ret = 0;

            try
            {
                isFindCalibrationFrameRunning = true;

                cts = new CancellationTokenSource();
                cancellationToken = cts.Token;

                // Reset any previous calibration board timeline ranges
                MediaTimeLineDisplayLeft.Clear();
                MediaTimeLineDisplayRight.Clear();

                // Move both methods to background threads
                var (startCalibration, stopCalibration) = await Task.Run(() =>
                    calibrationStereoFrameSet.FindCalibrationBoardZoneAsync(FrameProcessingCallbackFindCalibrationTimeLineRange,
                                                                            cancellationToken));

                if (startCalibration != -1 && stopCalibration != -1)
                {
                    // Update the timeline ranges
                    SetupMediaTimeLineDisplay();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Calibration search canceled.");
                ret = -1;

                if (ret == -1)
                {
                    calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.StartStopCalibrationBoardZone);
                    calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.StartStopCalibrationBoardZone);
                    MediaTimeLineDisplayLeft.Clear();
                    MediaTimeLineDisplayRight.Clear();

                    Debug.WriteLine("FindCalibrationBoardZoneAsync: User canceled.");
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FindCalibrationBoardZoneAsync: Error during calibration board zone search: {ex.Message}");
            }
            finally
            {
                isFindCalibrationFrameRunning = false;
            }

            return ret;
        }


        /// <summary>
        /// Search the media for the calibration boards
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public async Task<int> BuildFrameSetsAsync()
        {
            int ret = 0;

            try
            {
                isFindCalibrationFrameRunning = true;

                cts = new CancellationTokenSource();
                cancellationToken = cts.Token;

                int startCalibrationBoardZone = calibrationStereoFrameSet.GetStartCalibrationBoardZone();
                int stopCalibrationBoardZone = calibrationStereoFrameSet.GetStopCalibrationBoardZone();

                if (startCalibrationBoardZone != -1 && stopCalibrationBoardZone != -1)
                {
                    // Update the timeline ranges
                    SetupMediaTimeLineDisplay();

                    // Next find the calibration frames with in that range
                    ret = await Task.Run(async () =>
                    {
                        int ret = -1;

                        try
                        {
                            ret = await calibrationStereoFrameSet.FindCalibrationsFramesAsync(
                                            startCalibrationBoardZone,
                                            stopCalibrationBoardZone,
                                            FrameProcessingCallbackFindCalibrationsFrames,
                                            cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"BuildFrameSetsAsync: FindCalibrationsFramesAsync Failed, {ex.Message}");
                        }

                        return ret;
                    });

                    if (ret == -1)
                    {
                        calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.FrameSets);
                        calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.BestFrames);
                        CalibrationFrameSetViewerLeft.ClearDisplay(ViewModeCurrent);
                        CalibrationFrameSetViewerRight.ClearDisplay(ViewModeCurrent);

                        Debug.WriteLine("BuildFrameSetsAsync: User canceled.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Calibration search canceled.");
                ret = -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during calibration search: {ex.Message}");
            }
            finally
            {
                isFindCalibrationFrameRunning = false;
            }

            return ret;
        }


        /// <summary>
        /// Cancel the search for calibration frames.
        /// </summary>
        public void FindCalibrationFrameCancel()
        {
            cts?.Cancel();
        }


        /// <summary>
        /// Check if the Find Calibration Frames is running or not
        /// </summary>
        /// <returns></returns>
        public bool IsFindRunning()
        {
            return isFindCalibrationFrameRunning;
        }


        /// ** Safe to call from a background thread **
        /// ** All UI via SafeUICall **
        /// Extract the best frames and do a mono calibration.
        /// If it is called from a Stereo head both left and right are mono calibrated and the result 
        /// reported on screen.  However only the left MonoCalibrationCameraData array is returned
        /// </summary>
        /// <param name="calibProject"></param>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="movementMinThreshold"></param>
        /// <param name="blurMinThreshold"></param>
        /// <param name="monoCornersMinThreshold"></param>
        /// <param name="maxFramesFromEachSensorBin"></param>
        /// <param name="maxFramesFromEachPoseBin"></param>
        /// <returns>-1 if fails</returns>
        public async Task<int> FindBestMonoFramesSafeUIAsync(CalibProject calibProject,
                                                             bool trueLeftFalseRight,
                                                             double movementMinThreshold,
                                                             double blurMinThreshold,
                                                             int monoCornersMinThreshold,
                                                             int maxFramesFromEachSensorBin,
                                                             int maxFramesFromEachPoseBin,
                                                             int minFrameGap)
        {
            int ret = 0;
            bool doMonoBestFrames = false;

            // Guard
            if (headType is null) return -1;
            if (!IsHeadMono()) return -1;
            if (calibrationStereoFrameSet is null) return -1;

            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:     // There are only a stereo pair so also use this for the mono calibration
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    doMonoBestFrames = true;
                    break;
            }

            // Check we have a CalibrationStereoFrameSet and this is definitely a Mono head
            if (doMonoBestFrames)
            {
                try
                {
                    isFindCalibrationFrameRunning = true;

                    // Clear any existing best frames (on this mono head)
                    await ClearResultsSafeUIAsync(CalibrationStereoFrameSet.ClearRequest.BestFrames);

                    if (trueLeftFalseRight)
                        Debug.WriteLine($"Mono Left SelectBestStereoFramesUsingSensorBinOnly," +
                                        $" Min move={movementMinThreshold}, Min blur={blurMinThreshold}," +
                                        $" Corners threshold={monoCornersMinThreshold}:");
                    else
                        Debug.WriteLine($"Mono Right SelectBestStereoFramesUsingSensorBinOnly, " +
                                        $"Min move={movementMinThreshold}, Min blur={blurMinThreshold}, " +
                                        $"Corners threshold={monoCornersMinThreshold}:");

                    // Create a list of the best calibration frames best on the sensor bin only
                    calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(movementMinThreshold,
                                                                                       blurMinThreshold,
                                                                                       monoCornersMinThreshold,
                                                                                       maxFramesFromEachSensorBin);

                    int addedUsingSensorBins = calibrationStereoFrameSet.Data.BestFrameIndexes.Count;
                    int addedUsingPoseBins = 0;
                    int updatedUsingPoseBins = 0;
                    int removedNearlyFrames = 0;

                    // Update the UI
                    safeUICall.Call(() => RenderSensorCoverage());
                    safeUICall.Call(() => RenderMediaTimeLineDisplay());

                    // Temp mono calibration to get yaw and pitch for each frame
                    // Calibration using the best frames (calibration using K1,K2,P1,P2)
                    // This is used to calculate the yaw and pitch of each frame and
                    // ISN'T used for the ultimate mono calibration
                    MonoCalibrationCameraData? monoCalib = calibrationStereoFrameSet.MonoCalibrateUsingBestFrames(
                                                                                            trueStereoFalseMono: false,
                                                                                            trueLeftFalseRight,
                                                                                            frameSize,
                                                                                            monoCornersMinThreshold,
                                                                                            CalibrationParameters.K1K2P1P2);

                    // Check we have suitable calibration data to proceed
                    if (monoCalib is not null)
                    {
                        // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                        await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(monoCalib!, null/*monoCalibRight*/, frameSize);

                        // Next top-up with pose diverse frames
                        (addedUsingPoseBins, updatedUsingPoseBins) = calibrationStereoFrameSet.AddBestFramesUsingPoseBins(
                                                                             movementMinThreshold,
                                                                             blurMinThreshold,
                                                                             monoCornersMinThreshold,
                                                                             maxFramesFromEachPoseBin);

                        // Remove frames that are too close to each other
                        removedNearlyFrames = calibrationStereoFrameSet.CullNearbyFrames(minFrameGap);

                        // Report the counts of added and updated best frames
                        if (trueLeftFalseRight)
                            report?.Info("", $"FindBestMonoFrames Left: Added {addedUsingSensorBins} from sensor coverage, added {addedUsingPoseBins} from pose diversity and update {updatedUsingPoseBins}, removed nearly frames {removedNearlyFrames}");
                        else
                            report?.Info("", $"FindBestMonoFrames Right: Added {addedUsingSensorBins} from sensor coverage, added {addedUsingPoseBins} from pose diversity and update {updatedUsingPoseBins}, removed nearly frames {removedNearlyFrames}");

                        // Update the UI
                        safeUICall.Call(() => CalibrationFrameSetViewerLeft.RefreshSensorBin(_viewMode));
                        safeUICall.Call(() => CalibrationFrameSetViewerLeft.RefreshPoseBin(_viewMode));
                        safeUICall.Call(() => RenderSensorCoverage());
                        safeUICall.Call(() => RenderMediaTimeLineDisplay());
                    }
                    else
                        ret = -1;

                    // If best frames have been collected then change the
                    // MediaTimeLineDisplay tool tip to explain the dots
                    // on the timeline display
                    if (IsBestFramesSetup())
                    {
                        // Mono so left side only
                        MediaTimeLineDisplayLeft.SetToolTipLoadedProject();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FindBestMonoFramesSafeUIAsync: Error during best frames extraction: {ex.Message}");
                }
                finally
                {
                    isFindCalibrationFrameRunning = false;
                }
            }

            safeUICall.Call(() => BestFrameJump(0));

            return ret;
        }


        /// <summary>
        /// ** Safe to call from a background thread **
        /// ** All UI via SafeUICall **
        /// Mono calibration using the best frames already selected.
        /// If it is called from a Stereo head both left and right are mono calibrated and the result 
        /// reported on screen.  However only the left MonoCalibrationCameraData array is returned
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns>Zero is OK</returns>
        public int DoMonoCalibrationCalculationSafeUI(CalibProject calibProject,
                                                      bool trueLeftFalseRight,
                                                      int monoCornersMinThreshold)
        {
            int ret = 0;

            // Guard
            if (!IsHeadMono()) return -1;

            // Check we have a CalibrationStereoFrameSet
            if (calibrationStereoFrameSet is not null)
            {
                try
                {
                    isFindCalibrationFrameRunning = true;

                    // Proceed to do the mono calibration using each the calibration parameter set
                    foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                    {
                        // Calibration using the best frames (pass2 calibration)                    
                        MonoCalibrationCameraData? monoCalib2 = calibrationStereoFrameSet.MonoCalibrateUsingBestFrames(
                                                                                trueStereoFalseMono: false,
                                                                                trueLeftFalseRight,
                                                                                frameSize,
                                                                                monoCornersMinThreshold,
                                                                                calibrationParameters);

                        if (monoCalib2 is not null)
                        {
                            // Remember the mono calibration data
                            if (trueLeftFalseRight)
                                calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters] = monoCalib2;
                            else
                                calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters] = monoCalib2;

                            // Manually set the IsDirty flag.  The IsDirty is only automatically set if the whole array is replaced
                            calibProject.Data.CalibrationResults.IsDirty = true;
                        }
                        else
                            ret = -1;
                    }


                    // Display the mono calibration results
                    // Reset calibration output display 
                    // Note. We used the left side display control only for a mono head
                    // even if 'trueLeftFalseRight == false'

                    // Display the mono & stereo calibration results
                    DisplayCalibrationInfoSafeUI(calibProject, trueLeftFalseRight/*trueLeftFalseRightNullStereo*/);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DoMonoCalibrationCalculationSafeUI: Error during mono calibration calculation: {ex.Message}");
                }
                finally
                {
                    isFindCalibrationFrameRunning = false;
                }
            }

            safeUICall.Call(() => BestFrameJump(0));

            return ret;
        }


        /// <summary>
        /// Find the best stereo frames 
        /// </summary>
        /// <param name="calibProject"></param>
        /// <param name="movementMinThreshold"></param>
        /// <param name="blurMinThreshold"></param>
        /// <param name="stereoCornersMinThreshold"></param>
        /// <param name="maxFramesFromEachSensorBin"></param>
        /// <param name="maxFramesFromEachPoseBin"></param>
        /// <returns></returns>
        public async Task<int> FindBestStereoFramesSafeUIAsync(CalibProject calibProject,
                                                               double movementMinThreshold,
                                                               double blurMinThreshold,
                                                               int stereoCornersMinThreshold,
                                                               int maxFramesFromEachSensorBin,
                                                               int maxFramesFromEachPoseBin,
                                                               int minFrameGap)
        {
            int ret = -1;

            // Guard
            if (!IsHeadStereo()) return -1;

            if (calibrationStereoFrameSet is not null)
            {
                try
                {
                    isFindCalibrationFrameRunning = true;

                    // Clear any existing best frame on this stereo head
                    await ClearResultsSafeUIAsync(CalibrationStereoFrameSet.ClearRequest.BestFrames);

                    // Create a list of the best calibration frames best on the sensor bin only
                    calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(movementMinThreshold,
                                                                                       blurMinThreshold,
                                                                                       stereoCornersMinThreshold,
                                                                                       maxFramesFromEachSensorBin);

                    int addedUsingSensorBins = calibrationStereoFrameSet.Data.BestFrameIndexes.Count;
                    int addedUsingPoseBins = 0;
                    int updatedUsingPoseBins = 0;
                    int removedNearlyFrames = 0;

                    // Update the UI
                    safeUICall.Call(() => RenderSensorCoverage());
                    safeUICall.Call(() => RenderMediaTimeLineDisplay());

                    // Next we are going to use each calibration parameter set to recalculate the pitch and yaw 
                    // and top-up and best frames for each case.  This is probably overkill and just using
                    // the base K1K2P1P2 set would probably do the job
                    foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                    {
                        Debug.WriteLine($"FindBestStereoFramesAsync: {calibrationParameters.ToString()}");
                        MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                        MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                        if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                        {

                            // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                            await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(leftMonoCalibrationCameraData,
                                                                                                           rightMonoCalibrationCameraData,
                                                                                                           frameSize);
                            // Next top-up with pose diverse frames
                            (int added, int updated) = calibrationStereoFrameSet.AddBestFramesUsingPoseBins(
                                                                                 movementMinThreshold,
                                                                                 blurMinThreshold,
                                                                                 stereoCornersMinThreshold,
                                                                                 maxFramesFromEachPoseBin);
                            addedUsingPoseBins += added;
                            updatedUsingPoseBins += updated;
                            
                            // Remove frames that are too close to each other
                            int removed = calibrationStereoFrameSet.CullNearbyFrames(minFrameGap);

                            removedNearlyFrames += removed;

                            if (calibrationStereoFrameSet.Data.BestFrameIndexes.Count > 0)
                                ret = 0; // OK

                            safeUICall.Call(() => CalibrationFrameSetViewerLeft.RefreshPoseBin(_viewMode));
                            safeUICall.Call(() => CalibrationFrameSetViewerRight.RefreshPoseBin(_viewMode));
                            safeUICall.Call(() => RenderSensorCoverage());
                            safeUICall.Call(() => RenderMediaTimeLineDisplay());
                        }
                    }

                    // If best frames have been collected then change the
                    // MediaTimeLineDisplay tool tip to explain the dots
                    // on the timeline display
                    if (IsBestFramesSetup())
                    {
                        MediaTimeLineDisplayLeft.SetToolTipLoadedProject();
                        MediaTimeLineDisplayRight.SetToolTipLoadedProject();
                    }

                    // Report the counts of added and updated best frames                            
                    report?.Info("", $"FindBestStereoFrames Added {addedUsingSensorBins} from sensor coverage, added {addedUsingPoseBins} from pose diversity and update {updatedUsingPoseBins}, removed nearly frames {removedNearlyFrames}");
                }
                finally
                {
                    isFindCalibrationFrameRunning = false;
                }
            }

            safeUICall.Call(() => BestFrameJump(0));

            return ret;
        }


        /// <summary>
        /// Perform the stereo calibration calculations on all heads using the best frames
        /// </summary>
        /// <param name="runCalibrationParams"></param>
        /// <returns></returns>
        public int DoCalibrationStereoCalculations(CalibProject calibProject,
                                             int stereoCornersMinThreshold)
        {
            int ret = -1;

            // Guard
            if (!IsHeadStereo()) return -1;

            if (calibrationStereoFrameSet is not null)
            {
                try
                {
                    isFindCalibrationFrameRunning = true;

                    // Proceed to do the stereo calibration using each calibration parameter 
                    foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                    {
                        Debug.WriteLine($"DoCalibrationStereoCalculations: {calibrationParameters.ToString()}");
                        MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                        MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                        if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                        {

                            // Calibration stereo calculations using the best frames
                            CalibrationStereoCameraData? calibrationStereoCameraData = calibrationStereoFrameSet.StereoCalibrateUsingBestFrames(
                                                                        frameSize,
                                                                        stereoCornersMinThreshold,
                                                                        leftMonoCalibrationCameraData,
                                                                        rightMonoCalibrationCameraData,
                                                                        calibrationParameters);


                            if (calibrationStereoCameraData is not null)
                            {
                                calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)calibrationParameters] = calibrationStereoCameraData;

                                // Manually set the IsDirty flag.  The IsDirty is only automatically set if the whole array is replaced
                                calibProject.Data.CalibrationResults.IsDirty = true;

                                // We need at least one working stereo calibration
                                ret = 0;
                            }
                        }
                    }

                    // Display the mono & stereo calibration results
                    DisplayCalibrationInfoSafeUI(calibProject, null/*trueLeftFalseRightNullStereo*/);
                }
                finally
                {
                    isFindCalibrationFrameRunning = false;
                }
            }

            SetAppMode(AppMode.BestFramesView);
            BestFrameJump(0);

            return ret;
        }
    }
}
