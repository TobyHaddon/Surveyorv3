// Contains best frame methods
// 
using Microsoft.UI.Xaml.Controls;
using Surveyor.Calibration;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Surveyor.Controls
{
    public sealed partial class UniversalCalibrationHead : UserControl
    {
        // Callback up to MainWindow to get a reference to the appropriate
        // best frame list instance based on the head type
        public delegate List<BestFrame> GetBestFrameListDelegate(HeadType headType);

        public event GetBestFrameListDelegate? GetBestFrameListCallback = null;


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Move back in the best frame list
        /// This moves both the left and right frame
        /// </summary>
        private void BestFrameMoveBack()
        {
            int targetIndex;

            // BestFrameJump does the out of bounds check
            targetIndex = _currentBestFrame - 1;
            BestFrameJump(targetIndex);
        }


        /// <summary>
        /// Move forward in the best frame list
        /// This moves both the left and right frame
        /// </summary>
        private void BestFrameMoveForward()
        {
            int targetIndex;

            // BestFrameJump does the out of bounds check

            targetIndex = _currentBestFrame + 1;
            BestFrameJump(targetIndex);
        }


        /// <summary>
        /// Navigates to the specified best frame in the calibration stereo frame set, or to the current best frame if
        /// no index is provided.
        /// </summary>
        /// <remarks>If the specified index is out of range, the method clamps it to the nearest valid
        /// index. No action is taken if the calibration stereo frame set is not available.</remarks>
        /// <param name="targetIndexRequest">The zero-based index of the best frame to navigate to, or null to use the current best frame index.</param>
        private void BestFrameJump(int? targetIndexRequest)
        {
            int targetIndex;

            // Guard
            if (headType is null)
                return;

            // Get the best frame list for the current head type using the callback
            List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

            if (bestFramesList is not null)
            {
                // Guard
                if (bestFramesList.Count == 0) return;

                // Was there a request to re-display the last displayed frame
                if (targetIndexRequest is null)
                    targetIndex = _currentBestFrame;
                else
                    targetIndex = (int)targetIndexRequest;

                // Check the request best frame index is with range
                if (targetIndex < 0)
                    targetIndex = 0;

                if (targetIndex >= bestFramesList.Count)
                    targetIndex = bestFramesList.Count - 1;

                try
                {
                    // Get the absolute frame index from the best frame index
                    BestFrame bestFrame = bestFramesList[targetIndex];

                    int frameIndex = bestFrame.FrameIndex;

                    // Get stereo frame pair
                    (FrameData leftTarget, FrameData? rightTarget, int correspondingCount) = calibrationStereoFrameSet.Data.Frames[frameIndex];

                    // Remember the last best frame index (must be set before the _JumpFrame())
                    _currentBestFrame = targetIndex;

                    // Jump to the left side best frame
                    _JumpFrame(trueLeftFalseRight: true, leftTarget.FrameIndex, leftTarget, correspondingCount);

                    if (rightTarget is not null)
                    {
                        // Jump to the right side best frame
                        _JumpFrame(trueLeftFalseRight: false, rightTarget.FrameIndex, rightTarget, correspondingCount);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to display best frames index:{targetIndex}, {ex.Message}");
                }
            }
        }




        /// <summary>
        /// Adds the best frame indexes from the specified list the best frame list.  
        /// This method ensures no duplicate frames are add to the best frame list. 
        /// If the frame index already exists it's reason can be updated
        /// </summary>
        /// <param name="frameIndexes">A list of frame indexes to consider for addition. Cannot be <c>null</c>.</param>
        /// <returns>The number of frames successfully added from the provided list.</returns>
        private (int added, int updated) AddBestFrames(CalibProject calibProject, List<int> frameIndexes, BestFrameReason reason)
        {
            // Guard
            if (frameIndexes == null || frameIndexes.Count == 0)
                return (0, 0);
            if (headType is null)
                return (0, 0);

            BestFramesHeadType bestFramesHeadType = ConvertHeadType((HeadType)headType);

            int addedCount = 0;
            int updatedCount = 0;

            foreach (int frameIndex in frameIndexes)
            {
                try
                {
                    // Ensure frame exists in the Frames dictionary
                    if (!calibrationStereoFrameSet.Data.Frames.ContainsKey(frameIndex))
                        continue;

                    bool? result = calibProject.Data.CalibrationInputs.AddBestFrame(bestFramesHeadType, new BestFrame(frameIndex, reason));

                    if (result == true)
                        addedCount++;
                    else if (result == false)
                        updatedCount++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AddBestFrames: Failed to add frame:{frameIndex} to the best frames list, {ex.Message}");
                }
            }

            // Keep best frames list sorted by FrameIndex
            if (addedCount > 0)
            {
                calibProject.Data.CalibrationInputs.Sort(bestFramesHeadType);
            }

            return (addedCount, updatedCount);
        }
    }
}