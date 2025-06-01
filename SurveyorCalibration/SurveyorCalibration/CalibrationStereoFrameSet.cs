using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Surveyor.Calibration
{
    /// <summary>
    /// A CalibrationStereoFrameSet instance holds all the extracted calibration frames metadata (FrameCalibrationTarget)
    /// in a sorted directory called 'Frames'.
    /// </summary>
    public class CalibrationStereoFrameSet
    {
        // Lock point
        [JsonProperty(nameof(LockFrameIndexLeft))]
        public int LockFrameIndexLeft = -1;

        [JsonProperty(nameof(LockFrameIndexRight))]
        public int LockFrameIndexRight = -1;

        // A sorted dictionary of frames, sorted by frame index that holds the calibration
        // board corners and ids, the blur factor and the movement factor
        [JsonProperty(nameof(Frames))]
        public SortedDictionary<int, (FrameCalibrationTarget frameCalibrationTargetLeft, FrameCalibrationTarget? frameCalibrationTargetRight)> Frames { get; set; } = [];

        [JsonProperty(nameof(BestFrameIndexes))]
        public List<int> BestFrameIndexes = [];

        // A dictionary of bin totals, where the key is a tuple of (gx, gy, binx, biny)
        [JsonProperty(nameof(BinTotalsLeft))]
        [TypeConverter(typeof(TupleInt4JsonConverter))]
        public Dictionary<(int gx, int gy, int binx, int biny), int> BinTotalsLeft = [];

        [JsonProperty(nameof(BinTotalsRight))]
        [TypeConverter(typeof(TupleInt4JsonConverter))]
        public Dictionary<(int gx, int gy, int binx, int biny), int> BinTotalsRight = [];


        public const double BLUR_LARGEVALUE = 10.0;
        public const double MOVEMENT_LARGEVALUE = 400.0;


        /// 
        /// DYNAMIC variables
        ///         
        [JsonIgnore]
        private VideoCapture? leftCapture = null;

        [JsonIgnore]
        private VideoCapture? rightCapture = null;

        // Target calibration board setup
        [JsonIgnore]
        private Dictionary? arucoDictionary;
        [JsonIgnore]
        private CharucoBoard? board;
        [JsonIgnore]
        private string boardName = string.Empty;


        // Total frame count
        [JsonIgnore]
        private int totalFramesLeft = -1;
        [JsonIgnore]
        private int totalFramesRight = -1;

        /// <summary>
        /// Pass the open video capture instances for the left and right media files.
        /// </summary>
        /// <param name="leftCapture"></param>
        /// <param name="rightCapture"></param>
        /// <returns></returns>
        public virtual bool SetupMedia(VideoCapture _leftCapture, VideoCapture? _rightCapture)
        {
            bool ret = false;

            leftCapture = _leftCapture;
            rightCapture = _rightCapture;

            // Get left total frame count
            if (leftCapture is not null && leftCapture.IsOpened)
            {
                totalFramesLeft = (int)leftCapture.Get(CapProp.FrameCount);
                if (totalFramesLeft > 0)
                {
                    ret = true;
                }
            }

            // Get right total frame count
            if (rightCapture is not null && rightCapture.IsOpened)
            {
                totalFramesRight = (int)rightCapture.Get(CapProp.FrameCount);
                if (totalFramesRight > 0)
                {
                    ret = true;
                }
            }

            return ret;
        }


        /// <summary>
        /// Clear the media handles and reset the total frame counts.
        /// Note we don't own the video capture handles, so we don't dispose them here.
        /// </summary>
        public void ShutDownMedia()
        {
            leftCapture = null;
            rightCapture = null;

            totalFramesLeft = -1;
            totalFramesRight = -1;
        }


        /// <summary>
        /// Set up the calibration board type for the stereo camera calibration.
        /// The boardname is just for reporting
        /// Example setup:
        /// 
        ///         // Create dictionary
        ///         dictionary5x5_100 = new Dictionary(PredefinedDictionaryName.Dict5X5_100);
        ///
        ///         // Create ChArUco board
        ///         float squareLength = 40.0f / 1000.0f;
        ///         float markerLength = 30.0f / 1000.0f;
        ///         int squaresX = 14;
        ///         int squaresY = 9;
        ///         board5x5_100 = new CharucoBoard(squaresX, squaresY, squareLength, markerLength, dictionary5x5_100);
        ///
        /// </summary>
        /// <param name="arucoDictionary"></param>
        /// <param name="boardName"></param>
        /// <returns></returns>
        public bool SetupCalibrationBoardType(Dictionary _arucoDictionary, CharucoBoard _board, string _boardName)
        {
            arucoDictionary = _arucoDictionary;
            board = _board;
            boardName = _boardName;

            return true;
        }


        /// <summary>
        /// Set the lock frame indexes for the left and right media.
        /// </summary>
        /// <param name="lockFrameIndexLeft"></param>
        /// <param name="lockFrameIndexRight"></param>
        public virtual void SetupLockFrameIndexes(int lockFrameIndexLeft, int lockFrameIndexRight)
        {
            LockFrameIndexLeft = lockFrameIndexLeft;
            LockFrameIndexRight = lockFrameIndexRight;
        }


        /// <summary>
        /// Get the calculated start points
        /// </summary>
        /// <returns></returns>
        public virtual (int startFrameLeft, int startFrameRight) GetStartIndexes()
        {
            if (LockFrameIndexLeft == -1 && LockFrameIndexRight == -1)
            {
                // Media not locked, so return the first frame indexes
                return (0, 0);
            }
            if (LockFrameIndexLeft < LockFrameIndexRight)
            {
                // Media locked and right sided filming first
                return (LockFrameIndexRight - LockFrameIndexLeft, 0);
            }
            else if (LockFrameIndexLeft > LockFrameIndexRight)
            {
                // Media locked and left sided filming first
                return (0, LockFrameIndexLeft - LockFrameIndexRight);
            }
            else
            {
                // Media unlocked but perfectly aligned
                return (0, 0);
            }
        }


        /// <summary>
        /// Get the left and right frame indexes for a given target index.
        /// </summary>
        /// <param name="targetIndex"></param>
        /// <returns></returns>
        public virtual (int frameLeft, int frameRight) GetIndexes(int targetIndex)
        {
            int frameLeft;
            int frameRight;

            (int startFrameLeft, int startFrameRight) = GetStartIndexes();

            if (startFrameLeft == 0)
            {
                frameLeft = targetIndex;
                frameRight = targetIndex + startFrameRight;
            }
            else
            {
                frameLeft = targetIndex + startFrameLeft;
                frameRight = targetIndex;
            }

            return (frameLeft, frameRight);
        }


        /// <summary>
        /// Get the natural duration of the locked stereo media.
        /// </summary>
        /// <returns></returns>
        public int GetNaturalDuration()
        {
            int naturalDuration = -1;

            if (totalFramesLeft != -1 && totalFramesRight != -1 &&
                LockFrameIndexLeft != -1 && LockFrameIndexRight != -1)
            {
                // Get the start indexes
                (int startFrameLeft, int startFrameRight) = GetStartIndexes();

                int leftDuration = totalFramesLeft - startFrameLeft;
                int rightDuration = totalFramesRight - startFrameRight;

                // The natural duration is the minimum of the two durations
                naturalDuration = Math.Min(leftDuration, rightDuration);
            }

            return naturalDuration;
        }


        /// <summary>
        /// Returns the maximum MovementFactor across all frames in the set.
        /// </summary>
        public double MaxMovementFactor => Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.MovementFactor >= 0)
                .Select(f => f!.MovementFactor)
                .DefaultIfEmpty(0) // Prevents exception if filtered list is empty
                .Max();



        /// <summary>
        /// Returns the maximum BlurFactor across all frames in the set.
        /// </summary>
        public double MaxBlurFactor => Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.BlurFactor != double.MaxValue)
                .Select(f => f!.BlurFactor)
                .DefaultIfEmpty(0) // or any appropriate fallback
                .Max();



        /// <summary>
        /// Returns the maximum number of corners found
        /// </summary>
        public int MaxCharucoCorners => Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight
                })
                .Where(f => f is not null)
                .Select(f => f!.CharucoCorners.Length)
                .DefaultIfEmpty(0)
                .Max();


        /// <summary>
        /// Add a stereo pair of FrameCalibrationTarget to the set.
        /// </summary>
        /// <param name="stereoFrameIndex"></param>
        /// <param name="frameLeft"></param>
        /// <param name="frameRight"></param>
        public virtual void AddFrame(int stereoFrameIndex, FrameCalibrationTarget frameLeft, FrameCalibrationTarget? frameRight)
        {
            if (frameLeft.CharucoCorners == null || frameLeft.CharucoCorners.Length == 0)
                return;

            if (frameRight is not null && (frameRight.CharucoCorners == null || frameRight.CharucoCorners.Length == 0))
                return;

            Frames[stereoFrameIndex] = (frameLeft, frameRight);

            // If there is a prior and/or next continious frame, calculate the movement
            // from this frame to those previous frames (note values in all three frames
            // maybe updated
            CalculateCornerMovement(stereoFrameIndex);

            // Update the bin totals
            AddToTheBinTotals(frameLeft, BinTotalsLeft);
            if (frameRight is not null)
                AddToTheBinTotals((FrameCalibrationTarget)frameRight, BinTotalsRight);

            // Helper
            static void AddToTheBinTotals(FrameCalibrationTarget target, Dictionary<(int gx, int gy, int binx, int biny), int> BinTotals)
            {
                foreach (var bin in target.BinsOccupied)
                {
                    BinTotals[bin] = BinTotals.GetValueOrDefault(bin) + 1;
                }
            }
        }


        /// <summary>
        /// Remove a frame from the set by its stereo frame index.
        /// </summary>
        /// <param name="stereoFrameIndex"></param>
        /// <returns></returns>
        public bool RemoveFrame(int stereoFrameIndex)
        {
            bool ret = false;

            if (Frames.ContainsKey(stereoFrameIndex))
            {
                (FrameCalibrationTarget leftTarget, FrameCalibrationTarget? rightTarget) = Frames[stereoFrameIndex];

                // Remove the bins from the bin totals
                RemoveFromTheBinTotals(leftTarget, BinTotalsLeft);
                if (rightTarget is not null)
                    RemoveFromTheBinTotals(rightTarget, BinTotalsRight);

                // Helper
                static void RemoveFromTheBinTotals(FrameCalibrationTarget target, Dictionary<(int gx, int gy, int binx, int biny), int> BinTotals)
                {
                    foreach (var bin in target.BinsOccupied)
                    {
                        BinTotals[bin] = BinTotals.GetValueOrDefault(bin) - 1;
                        if (BinTotals[bin] == 0)
                        {
                            BinTotals.Remove(bin);
                        }
                    }
                }

                // Remove the frame from the dictionary 
                ret = Frames.Remove(stereoFrameIndex);

                if (ret)
                {
                    // Is there a previous contiguious frame?
                    if (Frames.ContainsKey(stereoFrameIndex - 1))
                    {
                        (FrameCalibrationTarget leftTargetPrevious, FrameCalibrationTarget? rightTargetPrevious) = Frames[stereoFrameIndex - 1];

                        // Movement from this frame to the previous frame need to be recalculated
                        leftTargetPrevious.MovementToNext = -1;
                        if (rightTargetPrevious is not null)
                        {
                            rightTargetPrevious.MovementFromPrevious = -1;
                        }
                    }
                    // Is there a next contiguious frame?
                    if (Frames.ContainsKey(stereoFrameIndex + 1))
                    {
                        (FrameCalibrationTarget leftTargetNext, FrameCalibrationTarget? rightTargetNext) = Frames[stereoFrameIndex + 1];

                        // Movement from this frame to the next frame need to be recalculated
                        leftTargetNext.MovementFromPrevious = -1;
                        if (rightTargetNext is not null)
                        {
                            rightTargetNext.MovementToNext = -1;
                        }
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Find and report on very large movement values in the set.
        /// </summary>
        /// <param name="trueLeftrightFalse"></param>
        /// <param name="suppressValues"></param>
        public void ReportOnLargeValues(bool trueLeftrightFalse, bool suppressValues)
        {
            // Return a list of frame indexes where the movement factor is large
            List<int> largeMovementList = [.. Frames.Where(f =>
                                                           f.Value.frameCalibrationTargetLeft.MovementFactor > MOVEMENT_LARGEVALUE ||
                                                           (f.Value.frameCalibrationTargetRight?.MovementFactor ?? -1) > MOVEMENT_LARGEVALUE)
                                                    .Select(f => f.Key)];

            if (largeMovementList.Count > 0)
            {
                string side = trueLeftrightFalse ? "Left" : "Right";
                Debug.WriteLine($"{side} side large movement frames: {string.Join(", ", largeMovementList)}");
            }
        }


        /// <summary>
        /// Extract a list of the best frames for calibration based on the movement and blur factors.
        /// </summary>
        /// <returns></returns>
        public bool SelectBestStereoFrames()
        {
            HashSet<int> frameIndexSet = [];

            foreach (var (gx, gy) in FrameCalibrationTarget.GridLayers)
            {
                for (int biny = 0; biny < gy; biny++)
                {
                    for (int binx = 0; binx < gx; binx++)
                    {
                        var targetBin = (gx, gy, binx, biny);

                        var frameIndexes = Frames.Values
                            .Where(pair =>
                                pair.frameCalibrationTargetLeft.BinsOccupied.Contains(targetBin) &&
                                pair.frameCalibrationTargetLeft.MovementFactor >= 0 &&
                                (pair.frameCalibrationTargetRight == null || pair.frameCalibrationTargetRight.MovementFactor >= 0)
                            )
                            .OrderBy(pair =>
                            {
                                double leftMove = pair.frameCalibrationTargetLeft.MovementFactor;
                                double rightMove = pair.frameCalibrationTargetRight?.MovementFactor ?? leftMove;

                                return (leftMove + rightMove) / 2.0;
                            })
                            .ThenBy(pair =>
                            {
                                double leftBlur = pair.frameCalibrationTargetLeft.BlurFactor;
                                double rightBlur = pair.frameCalibrationTargetRight?.BlurFactor ?? leftBlur;

                                return (leftBlur + rightBlur) / 2.0;
                            })
                            .Take(2)
                            .Select(pair => pair.frameCalibrationTargetLeft.FrameIndex);

                        foreach (var index in frameIndexes)
                            frameIndexSet.Add(index);
                    }
                }
            }

            BestFrameIndexes = frameIndexSet.ToList();
            return true;
        }


        /// <summary>
        /// Get the bin counts for a given grid layer (gx, gy) and bin (binx, biny).
        /// </summary>
        /// <param name="gx"></param>
        /// <param name="gy"></param>
        /// <returns></returns>
        public Dictionary<(int gx, int gy, int binx, int biny), int> GetBinCounts(bool trueLeftFalseRight, int gx, int gy)
        {
            var counts = new Dictionary<(int gx, int gy, int binx, int biny), int>();

            Dictionary<(int gx, int gy, int binx, int biny), int> BinTotals;

            if (trueLeftFalseRight)
            {
                BinTotals = BinTotalsLeft;
            }
            else
            {
                BinTotals = BinTotalsRight;
            }

            foreach ((FrameCalibrationTarget leftTarget, FrameCalibrationTarget? rightTarget) in Frames.Values)
            {
                FrameCalibrationTarget? target;

                if (trueLeftFalseRight)
                {
                    target = leftTarget;
                }
                else
                {
                    target = rightTarget;
                }

                if (target is not null)
                {
                    foreach (var bin in target.BinsOccupied)
                    {
                        // Find the this bin in the counts list, if not found create an new entry in counts
                        counts[bin] = counts.GetValueOrDefault(bin) + 1;
                    }

                }
            }

            return counts;
        }


        /// <summary>
        /// Load a CalibrationFrameSet from a JSON file.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static CalibrationStereoFrameSet? LoadFromFile(string path)
        {
            CalibrationStereoFrameSet? ret = null;

            try
            {
                var json = File.ReadAllText(path);
                if (json is not null)
                {
                    try
                    {
                        var settings = new JsonSerializerSettings
                        {
                            Converters = { new TupleInt4JsonConverter() }
                        };


                        ret = JsonConvert.DeserializeObject<CalibrationStereoFrameSet>(json, settings);
                    }
                    catch (JsonSerializationException jsex)
                    {
                        Debug.WriteLine($"JSON Serialization Error: {jsex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file access issues)
                Debug.WriteLine($"Error loading from file: {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// Tries to locate where the calibration first appears in the stereo media 
        /// and when it stops appearring.
        /// This is done with periodic sampling of the media and looking for
        /// the target corners to appear and disappear.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<(int startCalibration, int stopCalibration)> FindCalibrationTimeLineRange(FrameProcessingCallback? callbackDisplay, CancellationToken cancellationToken)
        {
            bool leftReady = false;
            bool rightReady = false;

            if (leftCapture is not null && leftCapture.IsOpened)
                leftReady = true;

            if (rightCapture is not null && rightCapture.IsOpened)
                rightReady = true;

            SortedDictionary<int, bool> calibrationTargetSearch = [];


            int rangeStart;
            int rangeEnd;
            int startStep;
            bool trueStereoFalseSoloLeft;

            if (leftReady && rightReady && LockFrameIndexLeft != -1 && LockFrameIndexRight != -1)
            {
                // Stereo find timeline range
                trueStereoFalseSoloLeft = true;
                rangeStart = 0;
                rangeEnd = GetNaturalDuration() - 1;
                startStep = 625;
            }
            else
            {
                // Left only find timeline range
                trueStereoFalseSoloLeft = false;
                rangeStart = 0;
                rangeEnd = totalFramesLeft;
                startStep = 625;
            }

            await FindCalibration(trueStereoFalseSoloLeft,
                                  rangeStart, rangeEnd,
                                  startStep,
                                  calibrationTargetSearch,
                                  false/*trueRecursive*/,
                                  null,/*Used if recursive is true*/
                                  callbackDisplay,
                                  cancellationToken,
                                  null);



            int startCalibration = -1;
            int stopCalibration = -1;

            if (leftReady)  // At least one ready
            {
                // Adjust the frame step 625 > 125 > 25 > 5
                int frameStep2 = startStep / 5;
                if (frameStep2 < 5)
                    frameStep2 = 1;

                // First the current beginning of where the calibration board was seen and refine it
                (int? firstTrueKey, int? beforeFirstKey) = GetFirstTrueKeyBounds(calibrationTargetSearch);

                if (firstTrueKey is not null && beforeFirstKey is null)
                {
                    beforeFirstKey = 0;
                }

                // Work on the front of the range (recursively)
                if (firstTrueKey is not null && beforeFirstKey is not null)
                {
                    int newStartFrame = (int)beforeFirstKey + 1;
                    int newEndFrame = ((int)firstTrueKey - 1);
                    if (newStartFrame < newEndFrame)
                    {
                        await FindCalibration(trueStereoFalseSoloLeft,
                            (int)beforeFirstKey + 1, (int)firstTrueKey - 1, frameStep2,
                            calibrationTargetSearch,
                            true/*trueRecursive*/,
                            true/*trueWorkOnStartFalseWorkOnEnd*/,
                            callbackDisplay,
                            cancellationToken,
                            null);
                    }
                }


                // First the current end of where the calibration board was last and refine it
                (int? lastTrueKey, int? afterLastKey) = GetLastTrueKeyBounds(calibrationTargetSearch);
                if (lastTrueKey is not null && afterLastKey is null)
                {
                    afterLastKey = rangeEnd;
                }

                // Work on the back of the range (recursively)
                if (lastTrueKey is not null && afterLastKey is not null)
                {
                    int newStartFrame = (int)lastTrueKey + 1;
                    int newEndFrame = (int)afterLastKey - 1;
                    if (newStartFrame < newEndFrame)
                    {
                        await FindCalibration(trueStereoFalseSoloLeft,
                            newStartFrame, newEndFrame, frameStep2,
                            calibrationTargetSearch,
                            true/*trueRecursive*/,
                            false/*trueWorkOnStartFalseWorkOnEnd*/,
                            callbackDisplay,
                            cancellationToken,
                            null);
                    }
                }
            }

            // Get start and end of the range
            (int? firstTrueKeyExit, int? beforeFirstKeyExit) = GetFirstTrueKeyBounds(calibrationTargetSearch);
            (int? lastTrueKeyExit, int? afterLastKeyExit) = GetLastTrueKeyBounds(calibrationTargetSearch);
            if (firstTrueKeyExit is not null)
                startCalibration = (int)firstTrueKeyExit;
            if (lastTrueKeyExit is not null)
                stopCalibration = (int)lastTrueKeyExit;

            // Make sure both are valid
            if (firstTrueKeyExit is null || lastTrueKeyExit is null || startCalibration > stopCalibration)
            {
                startCalibration = -1;
                stopCalibration = -1;
            }

            //// This callback stores the calibration target search results and
            //// then calls the 'callbackDisplay' for screen updating
            //// a tuple is passed through the userData
            //static void FrameProcessingCallbackFindCalibrationTimeLineRange(
            //        int stereoFrameIndex,
            //        int stereoFrameTotal,
            //        int leftFrameIndex,
            //        Mat leftMat,
            //        FrameCalibrationTarget? leftFrameCalibrationTarget,
            //        int rightFrameIndex,
            //        Mat? rightMat,
            //        FrameCalibrationTarget? rightFrameCalibrationTarget,
            //        object userData)
            //{
            //    // Unpack the userData tuple
            //    var (_, calibrationTargetSearch, callBackDisplay) =  ((bool, SortedDictionary<int, bool>, FrameProcessingCallback?))userData;


            //    // Call the display callback if provided
            //    if (callBackDisplay is not null)
            //    {
            //        callBackDisplay(stereoFrameIndex, stereoFrameTotal,
            //                        leftFrameIndex, leftMat, leftFrameCalibrationTarget,
            //                        rightFrameIndex, rightMat, rightFrameCalibrationTarget,
            //                        userData);
            //    }
            //}

            return (startCalibration, stopCalibration);
        }

        /// <summary>
        /// Does a recursive search to find the end/stop of the calibration boards in the video
        /// </summary>
        /// <param name="trueStereoFalseSoloLeft"></param>
        /// <param name="startFrame"></param>
        /// <param name="endFrame"></param>
        /// <param name="frameStep"></param>
        /// <param name="calibrationTargetSearch"></param>
        /// <param name="trueRecursive"></param>
        /// <param name="trueWorkOnStartFalseWorkOnEnd"></param>
        /// <param name="callback"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task FindCalibration(bool trueStereoFalseSoloLeft,
                                           int startFrame, int endFrame, int frameStep,
                                           SortedDictionary<int, bool> calibrationTargetSearch,
                                           bool trueRecursive,
                                           bool? trueWorkOnStartFalseWorkOnEnd,
                                           FrameProcessingCallback? callback,
                                           CancellationToken cancellationToken,
                                           object? userData)
        {
            Debug.WriteLine($"FindCalibration: Frame:{startFrame} to {endFrame} step:{frameStep}");

            for (int frameIndex = startFrame; frameIndex < endFrame; frameIndex += frameStep)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine("FindCalibration Canceled by user");
                    return;
                }

                // Get the left and right frame indexes
                (int leftFrameIndex, int rightFrameIndex) = GetIndexes(frameIndex);

                // Jump to correct frame & Read the frame
                Mat? matLeft = null;
                Mat? matRight = null;

                // If step size is one then use FrameForward instead of FrameJump (quicker)
                if (frameStep != 1 || frameIndex == startFrame)
                {
                    matLeft = FrameJump(true/*leftTrueRightFalse*/, leftFrameIndex);
                }
                else
                {
                    matLeft = FrameForward(true/*leftTrueRightFalse*/);
                }


                if (trueStereoFalseSoloLeft)
                {
                    if (frameStep != 1 || frameIndex == startFrame)
                    {
                        matRight = FrameJump(false/*leftTrueRightFalse*/, rightFrameIndex);
                    }
                    else
                    {
                        matRight = FrameForward(false/*leftTrueRightFalse*/);
                    }
                }

                // Detect the board
                FrameCalibrationTarget? targetLeft = null;
                FrameCalibrationTarget? targetRight = null;

                if (matLeft is not null && !matLeft.IsEmpty && arucoDictionary is not null && board is not null)
                {
                    targetLeft = DetectAndCreateFrameCalibrationTarget(true/*leftTrueRightFalse*/,
                                                                       leftFrameIndex, matLeft,
                                                                       arucoDictionary, board, boardName);
                    if (targetLeft is not null)
                        DrawMarkersToMat(targetLeft, matLeft);
                }

                if (trueStereoFalseSoloLeft && matRight is not null && !matRight.IsEmpty && arucoDictionary is not null && board is not null)
                {
                    targetRight = DetectAndCreateFrameCalibrationTarget(false/*leftTrueRightFalse*/,
                                                                       rightFrameIndex, matRight,
                                                                       arucoDictionary, board, boardName);
                    if (targetRight is not null)
                        DrawMarkersToMat(targetRight, matRight);
                }

                // Process result
                bool hasTarget = false;
                if (targetLeft is not null || targetRight is not null)
                {
                    hasTarget = true;
                }
                try
                {
                    calibrationTargetSearch.Add(frameIndex, hasTarget);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error adding frame {frameIndex} to calibration search: {ex.Message}");
                }


                // Callback
                if (callback is not null)
                {
                    if (matLeft is not null)
                    {
                        // We are probably not on the UI Thread
                        // The callback will need to dispatch to the UI thread
                        callback(frameIndex, endFrame,
                                 leftFrameIndex, matLeft, targetLeft,
                                 rightFrameIndex, matRight, targetRight,
                                 userData);
                    }
                }

                // Simulate work
                await Task.Delay(10, cancellationToken);
            }

            if (trueRecursive && trueWorkOnStartFalseWorkOnEnd is not null)
            {
                int newStartFrame = -1;
                int newEndFrame = -1;

                if ((bool)trueWorkOnStartFalseWorkOnEnd)
                {
                    (int? firstTrueKey, int? beforeFirstKey) = GetFirstTrueKeyBounds(calibrationTargetSearch);

                    if (beforeFirstKey is null)
                    {
                        beforeFirstKey = 0;
                    }

                    if (firstTrueKey is not null && beforeFirstKey is not null)
                    {
                        newStartFrame = (int)beforeFirstKey + 1;
                        newEndFrame = ((int)firstTrueKey - 1);
                    }
                }
                else
                {
                    (int? lastTrueKey, int? afterLastKey) = GetLastTrueKeyBounds(calibrationTargetSearch);

                    if (lastTrueKey is not null && afterLastKey is null)
                    {
                        afterLastKey = endFrame;
                    }

                    if (lastTrueKey is not null && afterLastKey is not null)
                    {
                        newStartFrame = (int)lastTrueKey + 1;
                        newEndFrame = ((int)afterLastKey - 1);
                    }
                }

                // Adjust the frame step 625 > 125 > 25 > 5
                int frameStep2 = frameStep / 5;
                if (frameStep2 < 5)
                    frameStep2 = 1;

                if (newStartFrame < newEndFrame)
                {
                    await FindCalibration(trueStereoFalseSoloLeft, newStartFrame, newEndFrame, frameStep2,
                        calibrationTargetSearch,
                        true/*trueRecursive*/,
                        trueWorkOnStartFalseWorkOnEnd,
                        callback, cancellationToken, userData);
                }
            }

            return;
        }


        /// <summary>
        /// Helper function to find the first 'true' entries
        /// </summary>
        /// <param name="calibrationTargetSearch"></param>
        /// <returns></returns>
        private static (int? firstTrueKey, int? beforeFirstKey) GetFirstTrueKeyBounds(SortedDictionary<int, bool> calibrationTargetSearch)
        {
            int? firstTrueKey = null;
            int? beforeFirstKey = null;

            var keys = calibrationTargetSearch.Keys.ToList();

            // Find first true and the one before
            for (int i = 0; i < keys.Count; i++)
            {
                if (calibrationTargetSearch[keys[i]])
                {
                    firstTrueKey = keys[i];
                    beforeFirstKey = (i > 0) ? keys[i - 1] : null;
                    break;
                }
            }

            return (firstTrueKey, beforeFirstKey);
        }


        /// <summary>
        /// Helper function to find the last 'true' entries
        /// </summary>
        /// <param name="calibrationTargetSearch"></param>
        /// <returns></returns>
        private static (int? lastTrueKey, int? afterLastKey) GetLastTrueKeyBounds(SortedDictionary<int, bool> calibrationTargetSearch)
        {

            int? lastTrueKey = null;
            int? afterLastKey = null;

            var keys = calibrationTargetSearch.Keys.ToList();

            // Find last true and the one after
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                if (calibrationTargetSearch[keys[i]])
                {
                    lastTrueKey = keys[i];
                    afterLastKey = (i < keys.Count - 1) ? keys[i + 1] : null;
                    break;
                }
            }

            return (lastTrueKey, afterLastKey);
        }


        /// <summary>
        /// A callback that is called for each frame in the stereo media.
        /// This is used to display the progress
        /// </summary>
        /// <param name="stereoFrameIndex"></param>
        /// <param name="leftFrameIndex"></param>
        /// <param name="leftMat"></param>
        /// <param name="leftFrameCalibrationTarget"></param>
        /// <param name="rightFrameIndex"></param>
        /// <param name="rightMat"></param>
        /// <param name="rightFrameCalibrationTarget"></param>
        /// <param name="userData"></param>
        public delegate void FrameProcessingCallback(
                            int stereoFrameIndex,
                            int stereoFrameTotal,
                            int leftFrameIndex,
                            Mat leftMat,
                            FrameCalibrationTarget? leftFrameCalibrationTarget,
                            int rightFrameIndex,
                            Mat? rightMat,
                            FrameCalibrationTarget? rightFrameCalibrationTarget,
                            object? userData);


        /// <summary>
        /// Finds the best frames for calibration by processing each frame in the specified range.
        /// </summary>
        /// <param name="startCalibrationFrameIndex"></param>
        /// <param name="stopCalibrationFrameIndex"></param>
        /// <param name="callback"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<int> FindCalibrationsFrames(int startCalibrationFrameIndex,
                                                      int stopCalibrationFrameIndex,
                                                      FrameProcessingCallback callback,
                                                      CancellationToken cancellationToken)
        {
            Debug.WriteLine($"FindCalibrationsFrames: Frame:{startCalibrationFrameIndex} to {stopCalibrationFrameIndex}");

            bool leftReady = false;
            bool rightReady = false;

            if (leftCapture is not null && leftCapture.IsOpened)
                leftReady = true;

            if (rightCapture is not null && rightCapture.IsOpened)
                rightReady = true;


            bool trueStereoFalseSoloLeft;
            if (leftReady && rightReady && LockFrameIndexLeft != -1 && LockFrameIndexRight != -1)
            {
                // Stereo find timeline range
                trueStereoFalseSoloLeft = true;
            }
            else
            {
                // Left only find timeline range
                trueStereoFalseSoloLeft = false;
            }


            // Loop through the frames in the target range
            for (int frameIndex = startCalibrationFrameIndex; frameIndex <= stopCalibrationFrameIndex; frameIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine("FindCalibrationsFrames Canceled by user");
                    return -1;
                }

                // Get the left and right frame indexes
                (int leftFrameIndex, int rightFrameIndex) = GetIndexes(frameIndex);

                // Jump to correct frame & Read the frame
                Mat? matLeft = null;
                Mat? matRight = null;

                // If step size is one then use FrameForward instead of FrameJump (quicker)
                if (frameIndex == startCalibrationFrameIndex)
                {
                    matLeft = FrameJump(true/*leftTrueRightFalse*/, leftFrameIndex);
                }
                else
                {
                    matLeft = FrameForward(true/*leftTrueRightFalse*/);
                }


                if (trueStereoFalseSoloLeft)
                {
                    if (frameIndex == startCalibrationFrameIndex)
                    {
                        matRight = FrameJump(false/*leftTrueRightFalse*/, rightFrameIndex);
                    }
                    else
                    {
                        matRight = FrameForward(false/*leftTrueRightFalse*/);
                    }
                }

                // Detect the board
                FrameCalibrationTarget? targetLeft = null;
                FrameCalibrationTarget? targetRight = null;

                if (matLeft is not null && !matLeft.IsEmpty && arucoDictionary is not null && board is not null)
                {
                    targetLeft = DetectAndCreateFrameCalibrationTarget(true/*leftTrueRightFalse*/,
                                                                       leftFrameIndex, matLeft,
                                                                       arucoDictionary, board, boardName);
                    if (targetLeft is not null)
                        DrawMarkersToMat(targetLeft, matLeft);
                }

                if (trueStereoFalseSoloLeft && matRight is not null && !matRight.IsEmpty && arucoDictionary is not null && board is not null)
                {
                    targetRight = DetectAndCreateFrameCalibrationTarget(false/*leftTrueRightFalse*/,
                                                                       rightFrameIndex, matRight,
                                                                       arucoDictionary, board, boardName);
                    if (targetRight is not null)
                        DrawMarkersToMat(targetRight, matRight);
                }

                // Process result
                try
                {
                    if (trueStereoFalseSoloLeft)
                    {
                        if (targetLeft is not null && targetRight is not null)
                        {
                            AddFrame(frameIndex, targetLeft, targetRight);
                        }
                    }
                    else
                    {
                        if (targetLeft is not null)
                        {
                            AddFrame(frameIndex, targetLeft, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error adding frame {frameIndex} to calibration search: {ex.Message}");
                }


                // Callback
                if (callback is not null)
                {
                    if (matLeft is not null)
                    {
                        // We are probably not on the UI Thread
                        // The callback will need to dispatch to the UI thread
                        callback(frameIndex, stopCalibrationFrameIndex,
                                 leftFrameIndex, matLeft, targetLeft,
                                 rightFrameIndex, matRight, targetRight,
                                 null);
                    }
                }

                // Simulate work
                await Task.Delay(10, cancellationToken);
            }



            return 0; // or number of frames processed
        }


        /// <summary>
        /// Save the CalibrationFrameSet to a JSON file.
        /// </summary>
        /// <param name="path"></param>
        public bool SaveToFile(string path)
        {
            bool ret = false;
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.None,
                    Converters = { new TupleInt4JsonConverter() }
                };

                var json = JsonConvert.SerializeObject(this, settings);
                File.WriteAllText(path, json);
                ret = true;
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file access issues)
                Debug.WriteLine($"Error saving to file: {ex.Message}");
            }

            return ret;
        }


        ///
        /// PRIVATE
        /// 

        /// <summary>
        /// Calculate the movement of the corners from this frame to the previous (if any)
        /// frame and the next frame (if any). Update the movement values in all three frames
        /// </summary>
        /// <param name=""></param>
        /// <returns>true if any changes</returns>
        private bool CalculateCornerMovement(int stereoFrameIndex)
        {
            bool ret = false;

            // Get pair for stereoFrameIndex
            (FrameCalibrationTarget leftTarget, FrameCalibrationTarget? rightTarget) = Frames[stereoFrameIndex];

            // Is there a previous contiguious frame?
            if (Frames.ContainsKey(stereoFrameIndex - 1))
            {
                // Get pair for stereoFrameIndex - 1
                (FrameCalibrationTarget leftTargetPrevious, FrameCalibrationTarget? rightTargetPrevious) = Frames[stereoFrameIndex - 1];

                // Movement from this left frame to the previous left frame
                double leftMovement = FrameCalibrationTarget.CalculateCornerMovement(leftTarget, leftTargetPrevious);

                leftTarget.MovementFromPrevious = leftMovement;
                leftTargetPrevious.MovementToNext = leftMovement;

                // Check the right frame if it exists
                if (rightTarget is not null && rightTargetPrevious is not null)
                {
                    // Movement from this right frame to the previous right frame
                    double rightMovement = FrameCalibrationTarget.CalculateCornerMovement(rightTarget, rightTargetPrevious);
                    rightTarget.MovementFromPrevious = rightMovement;
                    rightTargetPrevious.MovementToNext = rightMovement;
                }

                ret = true;
            }
            else
            {
                // No previous frame, we should assume a large movement value
                // this is because we are trying to ultimate detect frame with
                // the lowest movement factor. In this case we just don't know.
                // So we set the movement to a large value, so it will be ignored
                // and return false
                leftTarget.MovementFromPrevious = -1;
                if (rightTarget is not null)
                    rightTarget.MovementFromPrevious = -1;
            }

            // Is there a next contiguious frame?
            if (Frames.ContainsKey(stereoFrameIndex + 1))
            {
                // Get pair for stereoFrameIndex + 1
                (FrameCalibrationTarget leftTargetNext, FrameCalibrationTarget? rightTargetNext) = Frames[stereoFrameIndex + 1];

                // Movement from this left frame to the next left frame
                double leftMovement = FrameCalibrationTarget.CalculateCornerMovement(leftTarget, leftTargetNext);

                leftTarget.MovementFromPrevious = leftMovement;
                leftTargetNext.MovementToNext = leftMovement;

                // Check the right frame if it exists
                if (rightTarget is not null && rightTargetNext is not null)
                {
                    // Movement from this right frame to the next right frame
                    double rightMovement = FrameCalibrationTarget.CalculateCornerMovement(rightTarget, rightTargetNext);
                    rightTarget.MovementFromPrevious = rightMovement;
                    rightTargetNext.MovementToNext = rightMovement;
                }

                ret = true;
            }
            else
            {
                leftTarget.MovementToNext = -1;
                if (rightTarget is not null)
                {
                    // No next frame, we should assume a large movement value
                    // this is because we are trying to ultimate detect frame with
                    // the lowest movement factor. In this case we just don't know.
                    // So we set the movement to a large value, so it will be ignored
                    // and return false
                    rightTarget.MovementToNext = -1;
                }
            }

            return ret;
        }


        /// <summary>
        /// Read the next frame
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        private Mat? FrameForward(bool leftTrueRightFalse)
        {
            Mat? mat = null;
            VideoCapture? cap;


            if (leftTrueRightFalse)
            {
                cap = leftCapture;
            }
            else
            {
                cap = rightCapture;
            }

            if (cap is not null && cap.IsOpened)
            {
                mat = new Mat();

                cap!.Read(mat);
            }

            return mat;
        }


        /// <summary>
        /// Read a particular frame
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        /// <param name="targetIndex"></param>
        private Mat? FrameJump(bool leftTrueRightFalse, int frameIndex)
        {
            Mat? mat = null;
            VideoCapture? cap;
            int totalFrames;

            if (leftTrueRightFalse)
            {
                cap = leftCapture;
                totalFrames = totalFramesLeft;
            }
            else
            {
                cap = rightCapture;
                totalFrames = totalFramesRight;
            }

            if (cap is not null && cap.IsOpened)
            {
                frameIndex = Math.Clamp(frameIndex, 0, totalFrames - 1);

                // Emgu: use Set with CapProp
                cap!.Set(CapProp.PosFrames, frameIndex);

                mat = new Mat();
                cap.Read(mat);
            }

            return mat;
        }


        private static FrameCalibrationTarget? DetectAndCreateFrameCalibrationTarget(bool trueLeftfalseRight,
                                                            int frameIndex, Mat frame,
                                                            Dictionary arucoDictionary, CharucoBoard board,
                                                            string boardName)
        {
            FrameCalibrationTarget? ret = null;

            try
            {

                // Convert to grayscale for detection
                using var gray = new Mat();
                CvInvoke.CvtColor(frame, gray, ColorConversion.Bgr2Gray);

                // Detect ArUco markers
                using var markerCorners = new VectorOfVectorOfPointF();
                using var markerIds = new VectorOfInt();
                var parameters = DetectorParameters.GetDefault();

                ArucoInvoke.DetectMarkers(gray, arucoDictionary, markerCorners, markerIds, parameters);


                // Interpolate ChArUco corners
                using var charucoCorners = new Mat();
                using var charucoIds = new Emgu.CV.Util.VectorOfInt();

                if (markerIds.Size > 0)
                {
                    ArucoInvoke.InterpolateCornersCharuco(
                        markerCorners,
                        markerIds,
                        gray,
                        board,
                        charucoCorners,
                        charucoIds
                    );

                    Debug.WriteLine($"Frame:{frameIndex} {boardName} Detected {charucoIds.Size} ChArUco corners");


                    // Convert detected Charuco corners to managed types
                    var managedCorners = new PointF[charucoCorners.Rows];
                    charucoCorners.CopyTo(managedCorners);
                    var managedIds = charucoIds.ToArray();

                    // Draw corners on the color frame
                    if (charucoIds.Size > 0)
                    {
                        ret = new(frameIndex, gray, managedCorners, managedIds, frame.Width, frame.Height);
                    }
                }
                else
                {
                    Debug.WriteLine("Frame:{frameIndex} No markers detected");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Frame:{frameIndex} DetectAndDrawMarkers: Error processing ChArUco board: {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// From the metadata storaged in the list for the indicated frame index draw the 
        /// markers to the frame Mat and update the screen 
        /// </summary>
        /// <param name="trueLeftfalseRight"></param>
        /// <param name="frameIndex"></param>
        /// <param name="frame"></param>
        /// <returns></returns>
        private int DrawMarkersToMat(FrameCalibrationTarget frameCalibrationTarget, Mat frame)
        {
            int ret = 0;

            try
            {
                // Create a VectorOfPointF and populate it from the managed array
                var charucoCorners = new VectorOfPointF();
                charucoCorners.Push(frameCalibrationTarget.CharucoCorners);

                // managedIds is int[]
                var charucoIds = new VectorOfInt();
                charucoIds.Push(frameCalibrationTarget.CharucoIds);


                Emgu.CV.Aruco.ArucoInvoke.DrawDetectedCornersCharuco(
                    frame,
                    charucoCorners,
                    charucoIds,
                    new MCvScalar(0, 255, 0)
                );

                // Draw the centre point
                PointF boardCentre = frameCalibrationTarget.Center;
                int radius = 40;
                MCvScalar color = new(0, 255, 0); // Green (B, G, R)
                int thickness = 20;

                // Draw the circle on the Mat
                CvInvoke.Circle(frame, new Point((int)boardCentre.X, (int)boardCentre.Y), radius, color, thickness);

                // Display movement and blur factor
                double movementFactor = frameCalibrationTarget.MovementFactor;

                double blurFactor = frameCalibrationTarget.BlurFactor;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DrawMarkersToMat: Error processing ChArUco board: {ex.Message}");
            }

            return ret;
        }


        /*** End of CalibrationStereoFrameSet ***/
    }
}

