// Contains the high level calibration workflow methods
// 
using Emgu.CV.Aruco;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor;
using Surveyor.Helper;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Surveyor.CalibProject.DataClass.CalibrationResultClass;
using static Surveyor.Controls.CalibrationIterationViewer;
using static Surveyor.Controls.UniversalCalibrationHead;

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

            CalibrationFrameSetViewerLeft.Clear();
            CalibrationFrameSetViewerRight.Clear();
            MediaTimeLineDisplayLeft.Clear();
            MediaTimeLineDisplayRight.Clear();

            try
            {
                isFindCalibrationFrameRunning = true;

                cts = new CancellationTokenSource();
                cancellationToken = cts.Token;

                // Reset any previous calibration board timeline ranges
                // Use SetRange as this cal clear all the data.  If we Clear()
                // then we would need to call SetRange() again anyway
                MediaTimeLineDisplayLeft.SetRange(0, _totalFramesLeft - 1, clearData: true);
                if (IsHeadStereo())
                    MediaTimeLineDisplayRight.SetRange(0, _totalFramesRight - 1, clearData: true);

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
                Debug.WriteLine($"{ToString()} Calibration search canceled.");
                ret = -1;

                if (ret == -1)
                {
                    calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.StartStopCalibrationBoardZone);
                    calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.StartStopCalibrationBoardZone);
                    MediaTimeLineDisplayLeft.SetRange(0, _totalFramesLeft - 1, clearData: true);
                    if (IsHeadStereo())
                        MediaTimeLineDisplayRight.SetRange(0, _totalFramesRight - 1, clearData: true);

                    Debug.WriteLine($"{ToString()} FindCalibrationBoardZoneAsync: User canceled.");
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
        public async Task<int> BuildFrameSetsAsync(CalibProject calibProject)
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
                    ClearDisplay(ViewModeCurrent);
                    SetupMediaTimeLineDisplay();

                    await Task.Yield();

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
                            Debug.WriteLine($"{ToString()} BuildFrameSetsAsync: FindCalibrationsFramesAsync Failed, {ex.Message}");
                        }

                        return ret;
                    });

                    if (ret == -1)
                    {
                        // Clear the frame sets and Frame set viewer
                        calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.FrameSets);

                        // Clear all the items from the best frames list
                        ClearResults(calibProject, CalibrationStereoFrameSet.ClearRequest.BestFrames_All);

                        // Clear the graphs, sensor and pose bins
                        ClearDisplay(ViewModeCurrent);

                        Debug.WriteLine("{ToString()} BuildFrameSetsAsync: User canceled.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"{ToString()} Calibration search canceled.");
                ret = -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ToString()} Error during calibration search: {ex.Message}");
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


        /// <summary>
        /// The purpose of the method is to iterate through movement and corners count thresholds 
        /// to find the best frames for mono calibration. We start with movementMinThreshold and
        /// monoCornersMinThreshold find the best frames and do a mono calibration. We record the
        /// reprojection RMS and the max error and iterate by increasing the movementMinThreshold 
        /// and decreasing the monoCornersMinThreshold. The initial low movement threshold and high
        /// corners threshold will mean fewer frames are selected but they will be of higher quality 
        /// and give a better mono calibration. However there is a minimum number of frames required 
        /// for a mono calibration to work and if the thresholds are too strict then we won't have 
        /// enough frames to do the calibration. By iterating we can find the best frames for mono 
        /// calibration.
        /// </summary>
        /// <param name="report"></param>
        /// <param name="calibProject"></param>
        /// <param name="infoBarProcessing"></param>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="movementThresholdStart"></param>
        /// <param name="movementThresholdEnd"></param>
        /// <param name="movementThresholdStepUp"></param>
        /// <param name="blurMinThreshold"></param>
        /// <param name="monoCornersMinThresholdStart">Start is typically a larger number then End</param>
        /// <param name="monoCornersMinThresholdEnd">End is typically a smaller number than Start</param>
        /// <param name="monoCornersThresholdStepDown"></param>
        /// <param name="maxFramesFromEachSensorBin"></param>
        /// <param name="maxFramesFromEachPoseBin"></param>
        /// <param name="maxFramesFromEachDepthBin"></param>
        /// <param name="minFrameGap"></param>
        /// <param name="minFramesAllowedForMonoCalibration"></param>
        /// <param name="maxFramesAllowedForMonoCalibration"></param>
        /// <returns></returns>

        private enum IterationElement
        {
            movementAdjustment,
            cornerAdjustment
        }

        public async Task<(int, IterationResultList)> DoIterationBestFramesAndCalibrationMonoCalcsAsync(Reporter report,
                                                             CalibProject calibProject,
                                                             ProcessingInfoBar infoBarProcessing,
                                                             bool trueLeftFalseRight,
                                                             double movementThresholdStart,
                                                             double movementThresholdEnd,
                                                             double movementThresholdStepUp,
                                                             double blurMinThreshold,
                                                             int monoCornersMinThresholdStart,
                                                             int monoCornersMinThresholdEnd,
                                                             int monoCornersThresholdStepDown,
                                                             int maxFramesFromEachSensorBin,
                                                             int maxFramesFromEachPoseBin,
                                                             int maxFramesFromEachDepthBin,
                                                             int minFrameGap,
                                                             int minFramesAllowedForMonoCalibration,
                                                             int maxFramesAllowedForMonoCalibration)
        {
            int ret = -1;

            // Create a list to hold the results for each iteration so we can compare
            // the results and find the best result at the end. We will
            // need to record the thresholds used for each iteration along with the
            // reprojection RMS and max error so we can compare the results and find
            // the best result at the end.
            IterationResultList iterationResultList = new();

            // Guard
            if (calibrationBoardDefinition is null) return (-1, iterationResultList);
            if (headType is null) return (-1, iterationResultList);

            bool stopIterating = false;
            bool resultFound = false;
           
            // Get the appropriate best frame list
            List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType((HeadType)headType));

            // Set the data for the Calibration Iteration Viewer
            CalibrationIterationViewerData dataLeft = new((HeadType)headType, iterationResultList);
            CalibrationIterationViewerLeft.Data = dataLeft;


            isFindCalibrationFrameRunning = true;

            cts = new CancellationTokenSource();
            cancellationToken = cts.Token;


            // Track how many iterations
            int iterationNumber = 0;

            try
            {
                // Loop up between the movement iteration range
                for (double movementMinThreshold = movementThresholdStart ;
                     movementMinThreshold < movementThresholdEnd;
                     movementMinThreshold += movementThresholdStepUp)
                {
                    // Check for cancellation at the start of outer loop
                    cancellationToken.ThrowIfCancellationRequested();

                    // Iteration settings control loop
                    // manages alternating between adjusting movement and corner thresholds
                    // and the stopping conditions for the iteration

                    // Loop down between the corners range
                    for (int monoCornersMinThreshold = monoCornersMinThresholdStart;
                         monoCornersMinThreshold >= monoCornersMinThresholdEnd;
                         monoCornersMinThreshold -= monoCornersThresholdStepDown)
                    {
                        // Check for cancellation at the start of inner loop
                        cancellationToken.ThrowIfCancellationRequested();

                        iterationNumber++;

                        // Report inputs
                        safeUICall.Call(() => CalibrationIterationViewerLeft.RefreshInputsAndCounts(
                                                    movementMinThreshold, movementThresholdStart , CalibProject.DataClass.CalibrationInputsClass.MOVEMENT_LARGE_VALUE - 0.1,
                                                    monoCornersMinThreshold, calibrationBoardDefinition.GetTotalSquareCount(), monoCornersMinThresholdEnd,
                                                    blurMinThreshold,
                                                    bestFrameCount: 0, iterationNumber));
                        await Task.Yield();

                        // Find the best frame for this iteration's thresholds 
                        ret = await FindBestMonoFramesSafeUIAsync(report!,
                                                                  calibProject,
                                                                  trueLeftFalseRight,
                                                                  movementMinThreshold,
                                                                  blurMinThreshold,
                                                                  monoCornersMinThreshold,
                                                                  maxFramesAllowedForMonoCalibration,
                                                                  maxFramesFromEachSensorBin,
                                                                  maxFramesFromEachPoseBin,
                                                                  maxFramesFromEachDepthBin,
                                                                  minFrameGap,
                                                                  limitUIUpdates: true);

                        if (ret == 0)
                        {
                            // Report inputs and best frames count
                            safeUICall.Call(() => CalibrationIterationViewerLeft.RefreshInputsAndCounts(
                                                        movementMinThreshold, movementThresholdStart , CalibProject.DataClass.CalibrationInputsClass.MOVEMENT_LARGE_VALUE,
                                                        monoCornersMinThreshold, calibrationBoardDefinition.GetTotalSquareCount(), monoCornersMinThresholdEnd,
                                                        blurMinThreshold,
                                                        bestFramesList.Count, iterationNumber));
                            await Task.Yield();

                            // Did we meet the minimum best frame threshold
                            if (bestFramesList.Count >= minFramesAllowedForMonoCalibration &&
                                bestFramesList.Count <= maxFramesAllowedForMonoCalibration)
                            {
                                // Get a hash value for the generated best frames list
                                int bestFramesListHash = calibProject.Data.CalibrationInputs.GetBestFramesListHash(ConvertHeadType((HeadType)headType));

                                // Check if the last best frames list had the same hash value and if so skip doing the calibration calculation
                                if (iterationResultList.Results.Count > 0 && iterationResultList.Results[^1].BestFramesListHash == bestFramesListHash)
                                {
                                    // We have already done the mono calibration calculation for this best frames list
                                    // so we can skip doing the calculation again 
                                    report?.Debug(ChannelConvert((HeadType)headType), $"{(HeadType)headType} Movement Threshold:{movementMinThreshold}, " +
                                                        $"Corners:{monoCornersMinThreshold} didn't create a different best frames list, skip doing calibration");
                                }
                                else
                                {
                                    // Do the mono calibration using the best frames found with this iteration's thresholds
                                    // for each calibration parameter set and record the reprojection RMS and max error
                                    ret = DoMonoCalibrationCalculationSafeUI(calibProject, trueLeftFalseRight, monoCornersMinThreshold);

                                    if (ret == 0)
                                    {
                                        // Harvest the results
                                        foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                                        {
                                            // Mono always used the left side MonoCalibrationCameraData
                                            // array to store the results even for a right mono head
                                            MonoCalibrationCameraData? monoCalib = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                                            if (monoCalib is not null)
                                            {
                                                await Task.Yield();

                                                iterationResultList.Results.Add(new IterationResult(movementMinThreshold,
                                                                                            blurMinThreshold,
                                                                                            monoCornersMinThreshold,
                                                                                            bestFramesList.Count,
                                                                                            calibrationParameters,
                                                                                            monoCalib.ReprojectionRMS,
                                                                                            monoCalib.MaxError,
                                                                                            monoCalib.P95Error,
                                                                                            MonoCalibrationQualityClassifier.Classify(monoCalib.ReprojectionRMS, monoCalib.MaxError),
                                                                                            StereoCalibrationQuality: null,
                                                                                            bestFramesListHash));

                                                safeUICall.Call(() => CalibrationIterationViewerLeft.DrawGraphs());
                                                await Task.Yield();
                                            }
                                        }

                                        // Find best result so far
                                        IterationResult bestResult = iterationResultList.GetBestResult();

                                        if (IsExcellentResult(bestResult))
                                        {
                                            report?.Info(ChannelConvert((HeadType)headType), $"{(HeadType)headType} Excellent result found, Reprojection RMS:{bestResult.ReprojectionRMS:F2}, " +
                                                                $"Max Error:{bestResult.MaxError:F2}, Movement Threshold:{movementMinThreshold}, " +
                                                                $"Corners:{monoCornersMinThreshold}, Calibration Parameters:{bestResult.CalibrationParameters} ");
                                            stopIterating = true;
                                            resultFound = true;
                                            ret = 0; // OK
                                        }
                                        else
                                        {
                                            // Check if results are trending worst and we should stop iterating
                                            if (!stopIterating)
                                            {
                                                stopIterating = iterationResultList.AreResultingTrendingWorse();

                                                if (stopIterating)
                                                {
                                                    report?.Info(ChannelConvert((HeadType)headType), $"{(HeadType)headType} Stop iterating as results are trending is a worse direction");
                                                    ret = 0; // OK
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else if (bestFramesList.Count < minFramesAllowedForMonoCalibration)
                            {
                                report?.Debug(ChannelConvert((HeadType)headType), $"{(HeadType)headType} Too few frames found, {bestFramesList.Count} found and {minFramesAllowedForMonoCalibration} required. Movement Threshold:{movementMinThreshold}, " +
                                                 $"Corners:{monoCornersMinThreshold}");
                            }
                            else if (bestFramesList.Count > maxFramesAllowedForMonoCalibration)
                            {
                                report?.Debug(ChannelConvert((HeadType)headType), $"{(HeadType)headType} Too many frames found, {bestFramesList.Count} found and {maxFramesAllowedForMonoCalibration} is the limit. Movement Threshold:{movementMinThreshold}, " +
                                                 $"Corners:{monoCornersMinThreshold}");

                                // Break from the decreasing corners loop because the number
                                // of best will only keep increasing
                                break;
                            }
                        }

                        if (stopIterating)
                            break;
                    }
                    if (stopIterating)
                        break;
                }
            }
            catch (OperationCanceledException )
            {
                Debug.WriteLine($"{ToString()} Mono calibration iteration search canceled.");
                ret = -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ToString()} Error during mono calibration iteration search: {ex.Message}");
            }
            finally
            {
                isFindCalibrationFrameRunning = false;
            }


            // If the iteration don't stop early due to a excellent result being found
            // then we can look at the results and find the best result. We will need to
            // repeat the best frames and mono calibration to get the best mono calibration
            // data into the CalibProject instance
            if (!resultFound && iterationResultList.Results.Count > 0)
            {
                // Get the best result
                IterationResult bestResult = iterationResultList.GetBestResult();

                // Find the best frame for this iteration's thresholds 
                ret = await FindBestMonoFramesSafeUIAsync(report!,
                                                          calibProject,
                                                          trueLeftFalseRight,
                                                          bestResult.MovementMinThreshold,
                                                          bestResult.BlurMinThreshold,
                                                          bestResult.MonoCornersMinThreshold,
                                                          maxFramesAllowedForMonoCalibration,
                                                          maxFramesFromEachSensorBin,
                                                          maxFramesFromEachPoseBin,
                                                          maxFramesFromEachDepthBin,
                                                          minFrameGap,
                                                          limitUIUpdates: false);

                if (ret == 0)
                {
                    await Task.Yield();

                    // Do the mono calibration calculation on the best frames
                    ret = DoMonoCalibrationCalculationSafeUI(calibProject,
                                                             trueLeftFalseRight,
                                                             bestResult.MonoCornersMinThreshold);
                    if (ret == 0)
                    {
                        await Task.Yield();
                        string monoRPEQuality = MonoCalibrationQualityClassifier.ToDisplayString(MonoCalibrationQualityClassifier.ClassifyByReprojectionRMS(bestResult.ReprojectionRMS));
                        report?.Info(ChannelConvert((HeadType)headType), $"{(HeadType)headType} {monoRPEQuality} result found, Reprojection RMS:{bestResult.ReprojectionRMS:F2}, " +
                                         $"Max Error:{bestResult.MaxError:F2}, Movement Threshold:{bestResult.MovementMinThreshold}, " +
                                         $"Corners:{bestResult.MonoCornersMinThreshold}, Calibration Parameters:{bestResult.CalibrationParameters} ");
                        ret = 0; // OK
                    }
                }
            }

            return (ret, iterationResultList);



            // Determine if the result is excellent based on the reprojection RMS and max error. 
            static bool IsExcellentResult(IterationResult iterationResult)
            {
                bool ret = false;

                if (MonoCalibrationQualityClassifier.Classify(iterationResult.ReprojectionRMS, iterationResult.P95Error) == MonoCalibrationQuality.Excellent)
                    return true;

                return ret;
            }
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
        public async Task<int> FindBestMonoFramesSafeUIAsync(Reporter report, 
                                                             CalibProject calibProject,
                                                             bool trueLeftFalseRight,
                                                             double movementMinThreshold,
                                                             double blurMinThreshold,                                                             
                                                             int monoCornersMinThreshold,
                                                             int maxFramesAllowedForMonoCalibration,
                                                             int maxFramesFromEachSensorBin,
                                                             int maxFramesFromEachPoseBin,
                                                             int maxFramesFromEachDepthBin,
                                                             int minFrameGap,
                                                             bool limitUIUpdates)
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
                    await ClearResultsSafeUIAsync(calibProject, CalibrationStereoFrameSet.ClearRequest.BestFrames_AutoOnly);

                    if (trueLeftFalseRight)
                        report?.Debug("Left", $"Mono Left find best frames," +
                                        $" Min move={movementMinThreshold}, Min blur={blurMinThreshold}," +
                                        $" Corners threshold={monoCornersMinThreshold}");
                    else
                        report?.Debug("Right", $"Mono Right find best frames," +
                                        $"Min move={movementMinThreshold}, Min blur={blurMinThreshold}, " +
                                        $"Corners threshold={monoCornersMinThreshold}");

                    List<int>? foundIndexes = null;
                    int addedUsingSensorBins = 0;
                    int addedUsingPoseBins = 0;
                    int updatedUsingPoseBins = 0;
                    int addedUsingDepthBins = 0;
                    int updatedUsingDepthBins = 0;
                    int removedNearlyFrames = 0;

                    // Create a list of the best calibration frames best on the sensor bin only
                    foundIndexes = calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(
                                                            movementMinThreshold,
                                                            blurMinThreshold,
                                                            monoCornersMinThreshold,
                                                            maxFramesFromEachSensorBin);
                    if (foundIndexes is not null)
                    {
                        // Exit if too few best frames
                        if (foundIndexes.Count <= maxFramesAllowedForMonoCalibration)
                        {
                            // Add to the best frames list only allowing unique indexes
                            (addedUsingSensorBins, _) = AddBestFrames(calibProject, foundIndexes, BestFrameReason.SensorCoverage);
                        }
                        else
                        {
                            report?.Debug(ChannelConvert((HeadType)headType), $"FindBestMonoFramesSafeUIAsync: Too many best frames found, {foundIndexes.Count} max frames is {maxFramesAllowedForMonoCalibration}");
                            ret = -1;
                        }
                    }

                    if (ret == 0)
                    {
                        // Update the UI
                        safeUICall.Call(() =>
                        {
                            RenderSensorCoverage();
                            if (!limitUIUpdates)
                                RenderMediaTimeLineDisplay();
                        });

                        // Get the appropriate best frame list
                        List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType((HeadType)headType));

                        // Temp mono calibration to get yaw and pitch for each frame
                        // Calibration using the best frames (calibration using K1,K2,P1,P2)
                        // This is used to calculate the yaw and pitch of each frame and
                        // ISN'T used for the ultimate mono calibration
                        MonoCalibrationCameraData? monoCalib = calibrationStereoFrameSet.MonoCalibrateUsingBestFrames(
                                                                                                trueStereoFalseMono: false,
                                                                                                trueLeftFalseRight,
                                                                                                frameSize,
                                                                                                monoCornersMinThreshold,
                                                                                                CalibrationParameters.K1K2P1P2,
                                                                                                bestFramesList);

                        // Check we have suitable calibration data to proceed
                        if (monoCalib is not null)
                        {
                            // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                            await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(monoCalib!, null/*monoCalibRight*/, frameSize);

                            // Next top-up with pose diverse frames
                            foundIndexes = calibrationStereoFrameSet.AddBestFramesUsingPoseBins(
                                                                                 movementMinThreshold,
                                                                                 blurMinThreshold,
                                                                                 monoCornersMinThreshold,
                                                                                 maxFramesFromEachPoseBin);

                            if (foundIndexes is not null)
                            {
                                // Add to the best frames list only allowing unique indexes
                                (addedUsingPoseBins, updatedUsingPoseBins) = AddBestFrames(calibProject, foundIndexes, BestFrameReason.PoseDiversity);
                            }

                            // Next top-up with depth diverse frames
                            foundIndexes = calibrationStereoFrameSet.AddBestFramesUsingDepthBins(
                                                     movementMinThreshold,
                                                     blurMinThreshold,
                                                     monoCornersMinThreshold,
                                                     maxFramesFromEachDepthBin);

                            if (foundIndexes is not null)
                            {
                                // Add to the best frames list only allowing unique indexes
                                (addedUsingDepthBins, updatedUsingDepthBins) = AddBestFrames(calibProject, foundIndexes, BestFrameReason.DepthDiversity);
                            }

                            // Remove frames that are too close to each other
                            removedNearlyFrames = calibrationStereoFrameSet.CullNearbyFrames(calibProject, (HeadType)headType, minFrameGap);

                            if (bestFramesList.Count <= maxFramesAllowedForMonoCalibration)
                            {
                                // Report the counts of added and updated best frames
                                report?.Debug(ChannelConvert((HeadType)headType), $"FindBestMonoFramesSafeUIAsync: Added {addedUsingSensorBins} from sensor coverage, added {addedUsingPoseBins} from pose diversity and update {updatedUsingPoseBins}, added {addedUsingDepthBins} from depth diversity and update {updatedUsingDepthBins}, removed nearly frames {removedNearlyFrames}");

                                // Update the UI
                                safeUICall.Call(() =>
                                {
                                    RenderSensorCoverage();
                                    if (!limitUIUpdates)
                                    {
                                        RefreshSensorBin(_viewMode, trueLeftFalseRight: true);
                                        RefreshPoseBin(_viewMode, trueLeftFalseRight: true);
                                        RefreshDepthBin(_viewMode, trueLeftFalseRight: true);

                                        RenderMediaTimeLineDisplay();
                                        // If best frames have been collected then change the
                                        // MediaTimeLineDisplay tool tip to explain the dots
                                        // on the timeline display
                                        // Mono so left side only
                                        MediaTimeLineDisplayLeft.SetToolTipLoadedProject();
                                    }
                                });
                            }
                            else
                            {
                                report?.Debug(ChannelConvert((HeadType)headType), $"FindBestMonoFramesSafeUIAsync: Too many best frames found, {foundIndexes.Count} max frames is {maxFramesAllowedForMonoCalibration}");
                                ret = -1;
                            }
                        }
                        else
                            ret = -1;

                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{ToString()} FindBestMonoFramesSafeUIAsync: Error during best frames extraction: {ex.Message}");
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
            if (!IsHeadMono()) 
                return -1;
            if (headType is null)
                return -1;

            // Check we have a CalibrationStereoFrameSet
            if (calibrationStereoFrameSet is not null)
            {
                try
                {
                    isFindCalibrationFrameRunning = true;

                    // Get the appropriate best frame list
                    List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType((HeadType)headType));

                    // Proceed to do the mono calibration using each the calibration parameter set
                    foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                    {
                        // Calibration using the best frames (pass2 calibration)                    
                        MonoCalibrationCameraData? monoCalib2 = calibrationStereoFrameSet.MonoCalibrateUsingBestFrames(
                                                                                trueStereoFalseMono: false,
                                                                                trueLeftFalseRight,
                                                                                frameSize,
                                                                                monoCornersMinThreshold,
                                                                                calibrationParameters,
                                                                                bestFramesList);
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
                    Debug.WriteLine($"{ToString()} DoMonoCalibrationCalculationSafeUI: Error during mono calibration calculation: {ex.Message}");
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
        public async Task<int> FindBestStereoFramesSafeUIAsync(Reporter report,
                                                               CalibProject calibProject,
                                                               double movementMinThreshold,
                                                               double blurMinThreshold,
                                                               int stereoCornersMinThreshold,
                                                               int maxFramesFromEachSensorBin,
                                                               int maxFramesFromEachPoseBin,
                                                               int maxFramesFromEachDepthBin,
                                                               int minFrameGap)
        {
            int ret = -1;

            // Guard
            if (!IsHeadStereo()) return -1;
            if (headType is null) return -1;

            if (calibrationStereoFrameSet is not null)
            {
                // Report the parameters we are using for the best frame selection
                report?.Info("Stereo", $"Stereo find best stereo frames," +
                                $" Min move={movementMinThreshold}, Min blur={blurMinThreshold}," +
                                $" Corners threshold={stereoCornersMinThreshold}");


                try
                {
                    isFindCalibrationFrameRunning = true;

                    // Clear any existing automatically added best frames on this
                    // stereo head (keep any manually added frames)
                    await ClearResultsSafeUIAsync(calibProject, CalibrationStereoFrameSet.ClearRequest.BestFrames_AutoOnly);

                    List<int>? foundIndexes = null;
                    int addedUsingSensorBins = 0;
                    int addedUsingPoseBins = 0;
                    int updatedUsingPoseBins = 0;
                    int addedUsingDepthBins = 0;
                    int updatedUsingDepthBins = 0;
                    int removedNearlyFrames = 0;

                    // Create a list of the best calibration frames best on the sensor bin only
                    foundIndexes = calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(
                                                            movementMinThreshold,
                                                            blurMinThreshold,
                                                            stereoCornersMinThreshold,
                                                            maxFramesFromEachSensorBin);

                    if (foundIndexes is not null)
                    {
                        // Add to the best frames list only allowing unique indexes
                        (addedUsingSensorBins, _) = AddBestFrames(calibProject, foundIndexes, BestFrameReason.SensorCoverage);
                    }

                    // Update the UI
                    safeUICall.Call(() =>
                    {
                        RenderSensorCoverage();
                        RenderMediaTimeLineDisplay();
                    });

                    // Next we are going to use each calibration parameter set to recalculate the pitch and yaw 
                    // and top-up and best frames for each case.  This is probably overkill and just using
                    // the base K1K2P1P2 set would probably do the job
                    foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                    {
                        //???Debug.WriteLine($"{ToString()} FindBestStereoFramesAsync: {calibrationParameters}");
                        MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                        MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                        if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                        {

                            // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                            await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(leftMonoCalibrationCameraData,
                                                                                                           rightMonoCalibrationCameraData,
                                                                                                           frameSize);
                            // Next top-up with pose diverse frames
                            foundIndexes = calibrationStereoFrameSet.AddBestFramesUsingPoseBins(
                                                                                 movementMinThreshold,
                                                                                 blurMinThreshold,
                                                                                 stereoCornersMinThreshold,
                                                                                 maxFramesFromEachPoseBin);
                            if (foundIndexes is not null)
                            {
                                // Add to the best frames list only allowing unique indexes
                                (int added, int updated) = AddBestFrames(calibProject, foundIndexes, BestFrameReason.PoseDiversity);
                                addedUsingPoseBins += added;
                                updatedUsingPoseBins += updated;
                            }

                            // Next top-up with depth diverse frames
                            foundIndexes = calibrationStereoFrameSet.AddBestFramesUsingDepthBins(
                                                     movementMinThreshold,
                                                     blurMinThreshold,
                                                     stereoCornersMinThreshold,
                                                     maxFramesFromEachDepthBin);

                            if (foundIndexes is not null)
                            {
                                // Add to the best frames list only allowing unique indexes
                                (addedUsingDepthBins, updatedUsingDepthBins) = AddBestFrames(calibProject, foundIndexes, BestFrameReason.DepthDiversity);
                            }

                            // Remove frames that are too close to each other
                            int removed = calibrationStereoFrameSet.CullNearbyFrames(calibProject, (HeadType)headType, minFrameGap);
                            removedNearlyFrames += removed;

                            safeUICall.Call(() =>
                            {
                                RefreshSensorBin(_viewMode, trueLeftFalseRight: true);
                                RefreshSensorBin(_viewMode, trueLeftFalseRight: false);
                                RefreshPoseBin(_viewMode, trueLeftFalseRight: true);
                                RefreshPoseBin(_viewMode, trueLeftFalseRight: false);
                                RefreshDepthBin(_viewMode, trueLeftFalseRight: true);
                                RefreshDepthBin(_viewMode, trueLeftFalseRight: false);
                                RenderSensorCoverage();
                                RenderMediaTimeLineDisplay();
                            });
                        }
                    }

                    // If best frames have been collected then change the
                    // MediaTimeLineDisplay tool tip to explain the dots
                    // on the timeline display
                    if (IsBestFramesSetup(calibProject))
                    {
                        MediaTimeLineDisplayLeft.SetToolTipLoadedProject();
                        MediaTimeLineDisplayRight.SetToolTipLoadedProject();
                        ret = 0;
                    }

                    // Report the counts of added and updated best frames                            
                    report?.Info(ChannelConvert((HeadType)headType), $"{ToString()} FindBestStereoFrames Added {addedUsingSensorBins} from sensor coverage, added {addedUsingPoseBins} from pose diversity and update {updatedUsingPoseBins}, removed nearly frames {removedNearlyFrames}");
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

                    // Get the appropriate best frame list
                    List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType(HeadType.Stereo));

                    // Proceed to do the stereo calibration using each calibration parameter 
                    foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                    {
                        Debug.WriteLine($"{ToString()} DoCalibrationStereoCalculations: {calibrationParameters.ToString()}");
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
                                                                        calibrationParameters,
                                                                        bestFramesList);


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
