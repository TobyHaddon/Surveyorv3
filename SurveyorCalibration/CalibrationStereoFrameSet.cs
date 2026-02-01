// Ignore Spelling: Json Coeffs Uco Reprojection

using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Surveyor.Calibration;
using Surveyor.Controls;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Surveyor
{

    public enum CalibrationParameters
    {
        K1K2P1P2,              // 4 coefficients: k1, k2, p1, p2
        K1K2K3P1P2,            // 5 coefficients: k1, k2, k3, p1, p2
        K1K2K3K4P1P2,          // 6 coefficients: k1–k4, p1, p2
        K1K2K3K4P1P2K5K6       // 8 coefficients: k1–k6, p1, p2
    }


    [Flags]
    public enum BestFrameReason
    {
        None = 0,
        SensorCoverage = 1 << 0,
        PoseDiversity = 1 << 1,
    }

    public sealed record BestFrame(int FrameIndex, BestFrameReason Reason);


    /// <summary>
    /// A CalibrationStereoFrameSet instance holds all the extracted calibration frames metadata (FrameCalibrationTarget)
    /// in a sorted directory called 'Frames'.
    /// </summary>
    public class CalibrationStereoFrameSet
    {
        public class DataClass
        {

            public DataClass() 
            {
                // Set the Version
                Version = version;
            }

            // Version of the class (use for data migrations)
            private const int version = 7;
            // Data Version
            [JsonProperty(nameof(Version))]
            public int Version { get; set; } = -1;

            [JsonProperty(nameof(StartCalibrationBoardZone))]
            public int StartCalibrationBoardZone { get; set; } = -1;

            [JsonProperty(nameof(StopCalibrationBoardZone))]
            public int StopCalibrationBoardZone { get; set; } = -1;


            // A sorted dictionary of frames, sorted by frame index that holds the calibration
            // board corners and ids, the blur factor and the movement factor
            [JsonProperty(nameof(Frames))]
            public SortedDictionary<int, (FrameData frameCalibrationTargetLeft, FrameData? frameCalibrationTargetRight, int correspondingCount)> Frames { get; set; } = [];

            [JsonProperty(nameof(BestFrameIndexes))]
            public List<BestFrame> BestFrameIndexes = [];

            // A dictionary of sensor bin totals, where the key is a tuple
            // this is updated as frames are added or removed from the set.
            // This dictionary is persisted to JSON 
            //???[JsonProperty(nameof(AllFramesSensorBinTotalsLeft))]
            //???[TypeConverter(typeof(TupleInt2JsonConverter))]
            //???public Dictionary<(int binx, int biny), int> AllFramesSensorBinTotalsLeft = [];
            //???[JsonProperty(nameof(BestFramesSensorBinTotalsLeft))]
            //???[TypeConverter(typeof(TupleInt2JsonConverter))]
            //???public Dictionary<(int binx, int biny), int> BestFramesSensorBinTotalsLeft = [];

            // A dictionary of sensor bin totals, where the key is a tuple
            // this is updated as frames are added or removed from the set.
            // This dictionary is persisted to JSON 
            //???[JsonProperty(nameof(AllFramesSensorBinTotalsRight))]
            //???[TypeConverter(typeof(TupleInt2JsonConverter))]
            //???public Dictionary<(int binx, int biny), int> AllFramesSensorBinTotalsRight = [];
            //???[JsonProperty(nameof(BestFramesSensorBinTotalsRight))]
            //???[TypeConverter(typeof(TupleInt2JsonConverter))]
            //???public Dictionary<(int binx, int biny), int> BestFramesSensorBinTotalsRight = [];

            // A dictionary of the left pose bin totals, where the key is a tuple
            //???[JsonProperty(nameof(AllFramesPoseBinTotalsLeft))]
            //???[TypeConverter(typeof(TupleInt2JsonConverter))]
            //???public Dictionary<(int binx, int biny), int> AllFramesPoseBinTotalsLeft { get; set; } = [];
            //???[JsonProperty(nameof(BestFramesPoseBinTotalsLeft))]
            //???[TypeConverter(typeof(TupleInt2JsonConverter))]
            //???public Dictionary<(int binx, int biny), int> BestFramesPoseBinTotalsLeft { get; set; } = [];

            // A dictionary of the right pose bin totals, where the key is a tuple of (binx, biny)
            //???[JsonProperty(nameof(AllFramesPoseBinTotalsRight))]
            //???[TypeConverter(typeof(TupleInt2JsonConverter))]
            //???public Dictionary<(int binx, int biny), int> AllFramesPoseBinTotalsRight { get; set; } = [];
            //???[JsonProperty(nameof(BestFramesPoseBinTotalsRight))]
            //???[TypeConverter(typeof(TupleInt2JsonConverter))]
            //???public Dictionary<(int binx, int biny), int> BestFramesPoseBinTotalsRight { get; set; } = [];
        }

        public DataClass Data = new();

        public const double BLUR_LARGE_VALUE = 10.0;
        public const double MOVEMENT_LARGE_VALUE = 400.0;

        public const int MONO_CORNER_COUNT_THRESHOLD = 80;
        public const int STEREO_CORNER_COUNT_THRESHOLD = 50;

        /// 
        /// DYNAMIC variables (If you add any more private/JsonIgnore 
        ///                    variables remember to preserve them in LoadFromFile)
        ///  

        // Indicate if used in stereo or mono mode
        [JsonIgnore]
        private bool? headTrueIsStereoFalseIsMode = null;

        // Reporter
        [JsonIgnore]
        private Reporter? report = null;

        // Lock point (passed in from Calibration Project)
        [JsonIgnore]
        private int LockFrameIndexLeft = -1;

        [JsonIgnore]
        private int LockFrameIndexRight = -1;

        [JsonIgnore]
        private VideoCapture? leftCapture = null;

        [JsonIgnore]
        private VideoCapture? rightCapture = null;

        // Target calibration board setup
        [JsonIgnore]
        private CalibrationBoardDefinition? chArUcoBoardDefinition;

        // Total frame count
        [JsonIgnore]
        private int totalFramesLeft = -1;
        [JsonIgnore]
        private int totalFramesRight = -1;

        public CalibrationStereoFrameSet()
        {
        }


        /// <summary>
        /// Set the Reporter, used to output messages.
        /// Call as early as possible after creating the class instance.
        /// </summary>
        /// <param name="_report"></param>
        public void SetReporter(Reporter _report)
        {
            report = _report;
        }


        public enum ClearRequest
        {
            All,
            StartStopCalibrationBoardZone,
            FrameSets,
            BestFrames
        }
        public void ClearResults(ClearRequest clearRequest)
        {
            // Reset 

            if (clearRequest == ClearRequest.All || clearRequest == ClearRequest.StartStopCalibrationBoardZone)
            {
                Data.StartCalibrationBoardZone = -1;
                Data.StopCalibrationBoardZone = -1;
            }

            if (clearRequest == ClearRequest.All || clearRequest == ClearRequest.FrameSets)
            {
                Data.Frames = [];

                //???Data.AllFramesSensorBinTotalsLeft = [];
                //???Data.AllFramesSensorBinTotalsRight = [];
                //???Data.AllFramesPoseBinTotalsLeft = [];
                //???Data.AllFramesPoseBinTotalsRight = [];
            }

            if (clearRequest == ClearRequest.All || clearRequest == ClearRequest.BestFrames)
            {
                Data.BestFrameIndexes = [];

                //???Data.BestFramesSensorBinTotalsLeft = [];
                //???Data.BestFramesSensorBinTotalsRight = [];
                //???Data.BestFramesPoseBinTotalsLeft = [];
                //???Data.BestFramesPoseBinTotalsRight = [];
            }
        }


        /// <summary>
        /// Pass the open video capture instances for the mono (left) media file.
        /// </summary>
        /// <param name="leftCapture"></param>
        /// <param name="rightCapture"></param>
        /// <returns></returns>
        public virtual bool SetupMediaMono(VideoCapture _leftCapture)
        {
            bool ret = false;
            bool leftOpenAndReady = false;

            headTrueIsStereoFalseIsMode = false;
            leftCapture = _leftCapture;
            rightCapture = null;
            totalFramesLeft = 0;
            totalFramesRight = 0;

            // Get left total frame count
            if (leftCapture is not null && leftCapture.IsOpened)
            {
                leftOpenAndReady = true;

                totalFramesLeft = (int)leftCapture.Get(CapProp.FrameCount);
                if (totalFramesLeft > 0)
                {
                    ret = true;
                }
            }

            // Report
            if (ret)
            {
                if (leftOpenAndReady)
                    Debug.WriteLine($"Mono media opened total frames: Left={totalFramesLeft}");
                else
                    Debug.WriteLine($"No media opened");
            }
            else
            {
                if (leftOpenAndReady)
                    Debug.WriteLine($"Failed to open mono media total frames: Left={totalFramesLeft}");
                else
                    Debug.WriteLine($"No media opened");
            }

            return ret;
        }

        /// <summary>
        /// Pass the open video capture instances for the left and right media files.
        /// </summary>
        /// <param name="leftCapture"></param>
        /// <param name="rightCapture"></param>
        /// <returns></returns>
        public virtual bool SetupMediaStereo(VideoCapture _leftCapture, VideoCapture? _rightCapture)
        {
            bool ret = false;
            bool leftOpenAndReady = false;
            bool rightOpenAndReady = false;

            headTrueIsStereoFalseIsMode = true;
            leftCapture = _leftCapture;
            rightCapture = _rightCapture;
            totalFramesLeft = 0;
            totalFramesRight = 0;
            // Reset the lock index
            LockFrameIndexLeft = -1;
            LockFrameIndexRight = -1;

            // Get left total frame count
            if (leftCapture is not null && leftCapture.IsOpened)
            {
                leftOpenAndReady = true;

                totalFramesLeft = (int)leftCapture.Get(CapProp.FrameCount);
                if (totalFramesLeft > 0)
                {
                    ret = true;
                }
            }

            // Get right total frame count
            if (rightCapture is not null && rightCapture.IsOpened)
            {
                rightOpenAndReady = true;
                totalFramesRight = (int)rightCapture.Get(CapProp.FrameCount);
                if (totalFramesRight > 0)
                    ret = true;
                else
                    ret = false;
            }

            // Report
            if (ret)
            {
                if (leftOpenAndReady && rightOpenAndReady)
                    Debug.WriteLine($"Stereo media opened total frames: Left={totalFramesLeft}, Right={totalFramesRight}");
                else
                    Debug.WriteLine($"No media opened");
            }
            else
            {
                if (leftOpenAndReady && rightOpenAndReady)
                    Debug.WriteLine($"Failed to open stereo media total frames: Left={totalFramesLeft}, Right={totalFramesRight}");
                else
                    Debug.WriteLine($"No media opened");
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
        /// The board name is just for reporting
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
        public bool SetupCalibrationBoardType(CalibrationBoardDefinition _chArUcoBoardDefinition)
        {
            chArUcoBoardDefinition = _chArUcoBoardDefinition;

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
        public (int startFrameLeft, int startFrameRight) GetStartIndexes()
        {
            if (LockFrameIndexLeft == -1 && LockFrameIndexRight == -1)
            {
                // Media not locked, so return the first frame indexes
                return (0, 0);
            }
            if (LockFrameIndexLeft < LockFrameIndexRight)
            {
                // Media locked and right sided filming first
                return (0, LockFrameIndexRight - LockFrameIndexLeft);
            }
            else if (LockFrameIndexLeft > LockFrameIndexRight)
            {
                // Media locked and left sided filming first
                return (LockFrameIndexLeft - LockFrameIndexRight, 0);                
            }
            else
            {
                // Media unlocked but perfectly aligned
                return (0, 0);
            }
        }


        /// <summary>
        /// Get the left and right frame indexes for a given frame set index.
        /// </summary>
        /// <param name="frameSetIndex"></param>
        /// <returns></returns>
        public (int frameLeft, int frameRight) GetIndexes(int frameSetIndex)
        {
            int frameLeft;
            int frameRight;

            (int startFrameLeft, int startFrameRight) = GetStartIndexes();

            if (startFrameLeft == 0)
            {
                frameLeft = frameSetIndex;
                frameRight = frameSetIndex + startFrameRight;
            }
            else
            {
                frameLeft = frameSetIndex + startFrameLeft;
                frameRight = frameSetIndex;
            }

            return (frameLeft, frameRight);
        }


        /// <summary>
        /// Get the frame set index from the left and right frame indexes.
        /// </summary>
        /// <param name="leftIndex"></param>
        /// <param name="rightIndex"></param>
        /// <returns></returns>
        public int GetFrameSetIndexFromLeftRightIndexes(int leftIndex, int rightIndex)
        {
            int framesetIndex = -1;

            // Require media to be set up and lock indexes defined
            if (leftIndex < 0 || rightIndex < 0)
                return -1;

            var (startLeft, startRight) = GetStartIndexes();

            // Inverse of GetIndexes(framesetIndex)
            // Case 1: left starts at 0, right is offset
            //   left = i
            //   right = i + startRight  => i = right - startRight
            
            if (startLeft == 0)
            {
                int i = leftIndex;
                int expectedRight = i + startRight;
                framesetIndex = expectedRight == rightIndex ? i : -1;
            }

            // Case 2: right starts at 0, left is offset
            //   left = i + startLeft
            //   right = i               => i = right
            if (startRight == 0)
            {
                int i = rightIndex;
                int expectedLeft = i + startLeft;
                framesetIndex = expectedLeft == leftIndex ? i : -1;
            }

            //???
            // Double check - ChatGPT wrote the GetFrameSetIndexFromLeftRightIndexes
            // GetFrameSetIndexFromLeftIndexes and GetFrameSetIndexFromRightIndexes
            // methods, so let's verify the result here
            int fsIFromLeft = GetFrameSetIndexFromLeftIndexes(leftIndex);
            int fsIFromRight = GetFrameSetIndexFromRightIndexes(rightIndex);

            if ((framesetIndex != fsIFromLeft) && (framesetIndex != fsIFromRight))
            {
                throw new InvalidOperationException(
                    $"Stereo index mismatch: computed={framesetIndex}, fromLeft={fsIFromLeft}, fromRight={fsIFromRight}.");
            }

            return framesetIndex;
        }


        /// <summary>
        /// Calculate the frame set index from the left index
        /// </summary>
        /// <param name="leftIndex"></param>
        /// <returns></returns>
        public int GetFrameSetIndexFromLeftIndexes(int leftIndex)
        {
            if (leftIndex < 0) return -1;

            var (startLeft, _) = GetStartIndexes();

            // If left starts at 0, frame set index == leftIndex
            // Otherwise left = framesetIndex + startLeft  => framesetIndex = leftIndex - startLeft
            int i = (startLeft == 0) ? leftIndex : (leftIndex - startLeft);

            return i >= 0 ? i : -1;
        }


        /// <summary>
        /// Calculate the frame set index from the right index
        /// </summary>
        /// <param name="rightIndex"></param>
        /// <returns></returns>
        public int GetFrameSetIndexFromRightIndexes(int rightIndex)
        {
            if (rightIndex < 0) return -1;

            var (_, startRight) = GetStartIndexes();

            // If right starts at 0, frame set index == rightIndex
            // Otherwise right = framesetIndex + startRight  => framesetIndex = rightIndex - startRight
            int i = (startRight == 0) ? rightIndex : (rightIndex - startRight);

            return i >= 0 ? i : -1;
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
        /// For each pair in Frames, it flattens the pair into a list of two items: the left 
        /// and the right FrameCalibrationData.
        /// Then:
        /// If either is null, it's skipped by the .Where(...) clause.
        /// If both exist, it evaluates both independently.
        /// Finally, it finds the maximum MovementFactor among all present and valid left 
        /// and right entries, not the max of the two per-pair, but across all.
        /// So to clarify:
        /// If Right is null ⇒ only Left is considered.
        /// If both exist ⇒ both are considered separately in the global pool.
        /// It does not compare left/right within each frame to pick the higher — it gathers 
        /// all available valid MovementFactors and returns the overall max.
        /// </summary>
        public double MaxMovementFactor => Data.Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.MovementFactor >= 0)
                .Select(f => f!.MovementFactor)
                .DefaultIfEmpty(0) // Prevents exception if filtered list is empty
                .Max();

        public double MinMovementFactor => Data.Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.MovementFactor >= 0)
                .Select(f => f!.MovementFactor)
                .DefaultIfEmpty(0) // Prevents exception if filtered list is empty
                .Min();


        /// <summary>
        /// Returns the maximum BlurFactor across all frames in the set.
        /// </summary>
        public double MaxBlurFactor => Data.Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.BlurFactor != double.MaxValue)
                .Select(f => f!.BlurFactor)
                .DefaultIfEmpty(0) // or any appropriate fallback
                .Max();

        public double MinBlurFactor => Data.Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.BlurFactor != double.MaxValue)
                .Select(f => f!.BlurFactor)
                .DefaultIfEmpty(0) // or any appropriate fallback
                .Min();


        /// <summary>
        /// Returns the maximum number of corners found
        /// </summary>
        public int MaxChArUcoCorners => Data.Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight
                })
                .Where(f => f is not null)
                .Select(f => f!.ChArUcoCorners.Length)
                .DefaultIfEmpty(0)
                .Max();


        /// <summary>
        /// 
        /// </summary>
        public double MaxBestMovementFactor => Data.BestFrameIndexes
                            .SelectMany(bestFrame =>
                            {
                                if (!Data.Frames.TryGetValue(bestFrame.FrameIndex, out var pair))
                                    return Enumerable.Empty<FrameData>();

                                return new[] { pair.frameCalibrationTargetLeft!, pair.frameCalibrationTargetRight! };
                            })
                            .Where(f => f is not null && f.MovementFactor >= 0)
                            .Select(f => f!.MovementFactor)
                            .DefaultIfEmpty(0)
                            .Max();
        public double MinBestMovementFactor => Data.BestFrameIndexes
                    .SelectMany(bestFrame =>
                    {
                        if (!Data.Frames.TryGetValue(bestFrame.FrameIndex, out var pair))
                            return Enumerable.Empty<FrameData>();

                        return new[] { pair.frameCalibrationTargetLeft!, pair.frameCalibrationTargetRight! };
                    })
                    .Where(f => f is not null && f.MovementFactor >= 0)
                    .Select(f => f!.MovementFactor)
                    .DefaultIfEmpty(0)
                    .Min();
        

        /// <summary>
        /// Add a stereo pair of FrameCalibrationTarget to the set.
        /// </summary>
        /// <param name="stereoFrameIndex"></param>
        /// <param name="frameLeft"></param>
        /// <param name="frameRight"></param>
        /// <returns>The count of the markers that match between the left and right frames</returns>
        public virtual int AddFrame(int stereoFrameIndex, FrameData frameLeft, FrameData? frameRight)
        {
            if (frameLeft.ChArUcoCorners == null || frameLeft.ChArUcoCorners.Length == 0)
                return -1;

            if (frameRight is not null && (frameRight.ChArUcoCorners == null || frameRight.ChArUcoCorners.Length == 0))
                return -1;

            // If stereo calculate the corresponding count (number of markers that are the same on both the left and right side)
            int correspondingCount = -1;    // Set to -1 to indicate no correspondence applicable (in case it is a mono head)

            if (frameRight is not null)
            {
                var leftIds = frameLeft.ChArUcoIds;
                var rightIds = frameRight.ChArUcoIds;

                correspondingCount = leftIds.Intersect(rightIds).Count();
            }

            Data.Frames[stereoFrameIndex] = (frameLeft, frameRight, correspondingCount);

            // If there is a prior and/or next contiguous frame, calculate the movement
            // from this frame to those previous frames (note values in all three frames
            // maybe updated
            CalculateCornerMovement(stereoFrameIndex);

            // Update the sensor bin totals
            //???AddToTheSensorBinTotals(frameLeft, Data.AllFramesSensorBinTotalsLeft);
            //???if (frameRight is not null)
            //???    AddToTheSensorBinTotals(frameRight, Data.AllFramesSensorBinTotalsRight);

            return correspondingCount;
        }


        /// <summary>
        /// Remove a frame from the set by its stereo frame index.
        /// </summary>
        /// <param name="stereoFrameIndex"></param>
        /// <returns>True is frame removed</returns>
        public bool RemoveFrame(int stereoFrameIndex)
        {
            bool ret = false;

            if (Data.Frames.ContainsKey(stereoFrameIndex))
            {
                (FrameData leftTarget, FrameData? rightTarget, _) = Data.Frames[stereoFrameIndex];

                // Remove the bins from the bin totals
                //???RemoveFromTheBinTotals(leftTarget, Data.AllFramesSensorBinTotalsLeft);
                //???if (rightTarget is not null)
                //???    RemoveFromTheBinTotals(rightTarget, Data.AllFramesSensorBinTotalsRight);

                // Helper
                //???static void RemoveFromTheBinTotals(FrameData target, Dictionary<(int binx, int biny), int> BinTotals)
                //{
                //    foreach (var bin in target.SensorBinsOccupied)
                //    {
                //        BinTotals[bin] = BinTotals.GetValueOrDefault(bin) - 1;
                //        if (BinTotals[bin] == 0)
                //        {
                //            BinTotals.Remove(bin);
                //        }
                //    }
                //}

                // Remove the frame from the dictionary 
                ret = Data.Frames.Remove(stereoFrameIndex);

                if (ret)
                {
                    // Is there a previous contiguous frame?
                    if (Data.Frames.ContainsKey(stereoFrameIndex - 1))
                    {
                        (FrameData leftTargetPrevious, FrameData? rightTargetPrevious, _) = Data.Frames[stereoFrameIndex - 1];

                        // Movement from this frame to the previous frame need to be recalculated
                        leftTargetPrevious.MovementToNext = -1;
                        if (rightTargetPrevious is not null)
                        {
                            rightTargetPrevious.MovementFromPrevious = -1;
                        }
                    }
                    // Is there a next contiguous frame?
                    if (Data.Frames.ContainsKey(stereoFrameIndex + 1))
                    {
                        (FrameData leftTargetNext, FrameData? rightTargetNext, _) = Data.Frames[stereoFrameIndex + 1];

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
        /// Adds the best frame indexes from the specified list the best frame list.  
        /// This method ensures no duplicate frames are add to the best frame list. 
        /// If the frame index already exists it's reason can be updated
        /// </summary>
        /// <param name="frameIndexes">A list of frame indexes to consider for addition. Cannot be <c>null</c>.</param>
        /// <returns>The number of frames successfully added from the provided list.</returns>
        private (int added, int updated) AddBestFrames(List<int> frameIndexes, BestFrameReason reason)
        {
            if (frameIndexes == null || frameIndexes.Count == 0)
                return (0,0);

            int addedCount = 0;
            int updatedCount = 0;

            foreach (int frameIndex in frameIndexes)
            {
                try
                {
                    // Ensure frame exists in the Frames dictionary
                    if (!Data.Frames.ContainsKey(frameIndex))
                        continue;

                    // Locate existing BestFrame (if any)
                    int existingIndex = Data.BestFrameIndexes.FindIndex(f => f.FrameIndex == frameIndex);

                    if (existingIndex >= 0)
                    {
                        // Update 

                        // Merge reason flags
                        BestFrame existing = Data.BestFrameIndexes[existingIndex];
                        BestFrameReason mergedReason = existing.Reason | reason;

                        if (mergedReason != existing.Reason)
                        {
                            Data.BestFrameIndexes[existingIndex] = existing with { Reason = mergedReason };
                            updatedCount++;
                        }
                    }
                    else
                    {
                        // New

                        // Add new entry
                        Data.BestFrameIndexes.Add(new BestFrame(frameIndex, reason));
                        addedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AddBestFrames: Failed to add frame:{frameIndex} to the best frames list, {ex.Message}");
                }
            }

            // Keep BestFrameIndexes sorted by FrameIndex for deterministic traversal
            if (addedCount > 0)
            {
                Data.BestFrameIndexes.Sort(static (a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
            }

            return (addedCount, updatedCount);
        }


        /// <summary>
        /// Find and report on very large movement values in the set.
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="suppressValues"></param>
        public void ReportOnLargeValues(bool trueLeftFalseRight, bool suppressValues)
        {
            // Return a list of frame indexes where the movement factor is large
            List<int> largeMovementList = [.. Data.Frames.Where(f =>
                                                           f.Value.frameCalibrationTargetLeft.MovementFactor > MOVEMENT_LARGE_VALUE ||
                                                           (f.Value.frameCalibrationTargetRight?.MovementFactor ?? -1) > MOVEMENT_LARGE_VALUE)
                                                    .Select(f => f.Key)];

            if (largeMovementList.Count > 0)
            {
                string side = trueLeftFalseRight ? "Left" : "Right";
                Debug.WriteLine($"{side} side large movement frames: {string.Join(", ", largeMovementList)}");
            }
        }


        /// <summary>
        /// Extract a list of the best frames for calibration based on the movement and blur factors.
        /// </summary>
        /// <returns></returns>
        public bool SelectBestStereoFramesUsingSensorBinOnly(double maxMovementFactor, double maxBlurFactor, int chArUcoCornersThreshold, int maxFramePerBin)
        {
            List<int> frameIndexes;

            // Get the sensor grid size
            var (gx, gy) = FrameData.SensorBinGrid;

            for (int biny = 0; biny < gy; biny++)
            {
                for (int binx = 0; binx < gx; binx++)
                {
                    var targetBin = (binx, biny);

                    if (headTrueIsStereoFalseIsMode == false)
                    {

                        frameIndexes = [.. Data.Frames.Select(kvp => (kvp.Key, Left: kvp.Value.Item1)) // pull out only what we sort/filter on
                                                .Where(x =>
                                                    x.Left.ChArUcoCorners != null &&
                                                    x.Left.ChArUcoCorners.Length > chArUcoCornersThreshold &&
                                                    x.Left.SensorBinsOccupied != null &&
                                                    x.Left.SensorBinsOccupied.Contains(targetBin) &&
                                                    x.Left.MovementFactor <= maxMovementFactor &&
                                                    x.Left.BlurFactor <= maxBlurFactor)
                                                .OrderByDescending(x => x.Left.ChArUcoCorners.Length) // more corners first
                                                .ThenBy(x => x.Left.MovementFactor)                   // less movement next
                                                .ThenBy(x => x.Left.BlurFactor)                       // less blur next
                                                .ThenBy(x => x.Key)                                   // stable tiebreaker (optional)
                                                .Take(maxFramePerBin)
                                                .Select(x => x.Key)];
                    }
                    else
                    {
                        frameIndexes = [.. Data.Frames.Select(kvp => new {
                                                    kvp.Key,
                                                    Left = kvp.Value.Item1,
                                                    Right = kvp.Value.Item2,
                                                    Count = kvp.Value.Item3
                                                })
                                                .Where(x =>
                                                    x.Count > chArUcoCornersThreshold &&
                                                    x.Left?.SensorBinsOccupied != null &&
                                                    x.Left.SensorBinsOccupied.Contains(targetBin) &&
                                                    x.Left.MovementFactor <= maxMovementFactor &&
                                                    x.Left.BlurFactor <= maxBlurFactor &&
                                                    x.Right != null &&
                                                    x.Right.MovementFactor <= maxMovementFactor &&
                                                    x.Right.BlurFactor <= maxBlurFactor)
                                                .OrderByDescending(x => x.Count) // correspondingCount desc
                                                .ThenBy(x => Math.MaxMagnitude(x.Left.MovementFactor, x.Right!.MovementFactor))
                                                .ThenBy(x => Math.MaxMagnitude(x.Left.BlurFactor, x.Right!.BlurFactor))
                                                .ThenBy(x => x.Key) // optional: deterministic tiebreaker
                                                .Take(maxFramePerBin)
                                                .Select(x => x.Key)];
                    }


                    // Add to the best frames list only allowing unique indexes
                    AddBestFrames(frameIndexes, BestFrameReason.SensorCoverage);

                }
            }


            return true;
        }


        /// <summary>
        /// Get the sensor bin counts 
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns></returns>
        public Dictionary<(int binx, int biny), int> GetSensorBinCounts(UniversalCalibrationHead.ViewMode viewModel, bool trueLeftFalseRight)
        {
            var counts = new Dictionary<(int binx, int biny), int>();


            if (viewModel == UniversalCalibrationHead.ViewMode.AllFrames)
            {
                FrameData? target;

                foreach ((FrameData leftTarget, FrameData? rightTarget, _) in Data.Frames.Values)
                {              
                    if (trueLeftFalseRight)
                        target = leftTarget;
                    else
                        target = rightTarget;

                    if (target is not null)
                        ProcessFrameData(target, counts);
                }
            }
            else if (viewModel == UniversalCalibrationHead.ViewMode.BestFrames)
            {
                FrameData leftTarget;
                FrameData? rightTarget;
                FrameData? target;

                foreach (BestFrame bestFrame in Data.BestFrameIndexes)
                {
                    int frameIndex = bestFrame.FrameIndex;

                    if (Data.Frames.TryGetValue(frameIndex, out var tuple))
                    {
                        (leftTarget, rightTarget, _) = tuple;
                        
                        if (trueLeftFalseRight)
                            target = leftTarget;
                        else
                            target = rightTarget;

                        if (target is not null)
                            ProcessFrameData(target, counts);
                    }
                }
            }

            // Helper
            static void ProcessFrameData(FrameData target, Dictionary<(int binx, int biny), int> counts)
                {
                    foreach (var bin in target.SensorBinsOccupied)
                    {
                        // Find the this bin in the counts list, if not found create an new entry in counts
                        counts[bin] = counts.GetValueOrDefault(bin) + 1;
                    }
                }

            return counts;
        }


        /// <summary>
        /// Get the pose bin counts.
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns></returns>
        public Dictionary<(int binx, int biny), int> GetPoseBinCounts(UniversalCalibrationHead.ViewMode viewModel, bool trueLeftFalseRight)
        {
            var counts = new Dictionary<(int binx, int biny), int>();
            if (viewModel == UniversalCalibrationHead.ViewMode.AllFrames)
            {
                FrameData? target;

                foreach ((FrameData leftTarget, FrameData? rightTarget, _) in Data.Frames.Values)
                {
                    if (trueLeftFalseRight)
                        target = leftTarget;
                    else
                        target = rightTarget;

                    if (target is not null)
                        ProcessFrameData(target, counts);
                }
            }
            else if (viewModel == UniversalCalibrationHead.ViewMode.BestFrames)
            {
                FrameData leftTarget;
                FrameData? rightTarget;
                FrameData? target;

                foreach (BestFrame bestFrame in Data.BestFrameIndexes)
                {
                    int frameIndex = bestFrame.FrameIndex;

                    if (Data.Frames.TryGetValue(frameIndex, out var tuple))
                    {
                        (leftTarget, rightTarget, _) = tuple;

                        if (trueLeftFalseRight)
                            target = leftTarget;
                        else
                            target = rightTarget;

                        if (target is not null)
                            ProcessFrameData(target, counts);
                    }
                }
            }

            // Helper
            static void ProcessFrameData(FrameData target, Dictionary<(int binx, int biny), int> counts)
            {
                if (target.PoseBinX != -1 &&
                    target.PoseBinY != -1)
                {
                    // Increase the count for this pose bin   
                    counts[(target.PoseBinX, target.PoseBinY)] = counts.GetValueOrDefault((target.PoseBinX, target.PoseBinY)) + 1;
                }
            }

            return counts;
        }


        /// <summary>
        /// Load a CalibrationFrameSet from a JSON file.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        [RequiresUnreferencedCode("LoadFromFileAsync uses Json.NET serialization which may not be compatible with trimming.")]
        public async Task<bool> LoadFromFileAsync(string path)
        {
            bool ret = false;

            CalibrationStereoFrameSet.DataClass? frameSetDataLoaded;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                if (json is not null)
                {
                    try
                    {
                        var settings = new JsonSerializerSettings
                        {
                            Converters = { /*???new TupleInt2JsonConverter(),*/ 
                                           new Newtonsoft.Json.Converters.StringEnumConverter()
                            }
                        };

                        frameSetDataLoaded = JsonConvert.DeserializeObject<CalibrationStereoFrameSet.DataClass>(json, settings);

                        if (frameSetDataLoaded is not null)
                        {
                            // Apply the loaded frame set
                            this.Data = frameSetDataLoaded;

                            // Check the FrameCalibrationTarget Version used in the Frames dictionary
                            FrameData? firstFrameCalibrationTarget = null;

                            if (frameSetDataLoaded.Frames.Count > 0)
                            {
                                // Get the version of the FrameCalibrationTarget Frame in the Frames list
                                (_,(firstFrameCalibrationTarget, _, _)) = frameSetDataLoaded.Frames.First();
                            }

                            // Recalculate values flag
                            bool recalcFramesDictionary = false;

                            // Migrations Section
                            int lastestVersion = new CalibrationStereoFrameSet.DataClass().Version;
                            if (frameSetDataLoaded.Version != lastestVersion)
                            {
                                // Future migrations
                                //if (frameSetDataLoaded.Version == ??)
                                //{
                                //}
                                //else
                                {
                                    // Old cache so fail the load
                                    Debug.WriteLine($"Old format cache file:{path}, version:{frameSetDataLoaded.Version}, current required version is:{lastestVersion}");

                                    // Clear the loaded data
                                    this.ClearResults(ClearRequest.All);

                                    return false;
                                }
                            }

                            if (firstFrameCalibrationTarget is not null)
                            {
                                if (firstFrameCalibrationTarget.Version == -1)
                                {
                                    recalcFramesDictionary = true;
                                }
                            }

                            // Recalculate Frames Dictionary
                            if (recalcFramesDictionary)
                            {
                                int frameCalibrationTargetLastestVersion = new FrameData().Version;
                                foreach (var frame in frameSetDataLoaded.Frames)
                                {
                                    (FrameData frameCalibrationTargetLeft, FrameData? frameCalibrationTargetRight, int correspondingCount) = frame.Value;

                                    frameCalibrationTargetLeft.Version = frameCalibrationTargetLastestVersion;
                                    if (frameCalibrationTargetRight is not null)
                                        frameCalibrationTargetRight.Version = frameCalibrationTargetLastestVersion;
                                }
                            }
                        }

                        ret = true;
                    }
                    catch (JsonSerializationException jsex)
                    {
                        Debug.WriteLine($"LoadFromFile: JSON Serialization Error: {jsex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file access issues)
                Debug.WriteLine($"LoadFromFile: Error loading from file: {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// Tries to locate where the calibration first appears in the stereo media 
        /// and when it stops appearing.
        /// This is done with periodic sampling of the media and looking for
        /// the target corners to appear and disappear.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<(int startCalibration, int stopCalibration)> FindCalibrationBoardZoneAsync(FrameProcessingCallback? callbackDisplay, CancellationToken cancellationToken)
        {
            bool leftReady = false;
            bool rightReady = false;

            // Clears results ready for the next run
            ClearResults(ClearRequest.All);

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

            // We need to find at least one frame with the calibration target present
            // to start with.  We will loop reducing the step size until we find one.
            while (GetFirstTrueKeyBounds(calibrationTargetSearch) == (null, null) && startStep > 0)
            {
                await FindCalibrationAsync(trueStereoFalseSoloLeft,
                                           rangeStart, rangeEnd,
                                           startStep,
                                           calibrationTargetSearch,
                                           false/*trueRecursive*/,
                                           null,/*Used if recursive is true*/
                                           callbackDisplay,
                                           null,
                                           cancellationToken);
                if (GetFirstTrueKeyBounds(calibrationTargetSearch) == (null, null))
                {
                    startStep = startStep / 2;                    
                }
            }

            // Present if we have at least one 'true' entry
            int startCalibration = -1;
            int stopCalibration = -1;

            if (GetFirstTrueKeyBounds(calibrationTargetSearch) != (null, null))
            {

                if (leftReady)  // At least one ready
                {
                    // Adjust the frame step 625 > 125 > 25 > 5
                    int frameStep2 = startStep / 5;
                    if (frameStep2 < 5)
                        frameStep2 = 1;

                    // First get the current beginning of where the calibration board was seen
                    (int? firstTrueKey, int? beforeFirstKey) = GetFirstTrueKeyBounds(calibrationTargetSearch);

                    // Force a search from the beginning is no suitable starting point was previously located
                    if (beforeFirstKey is null)
                        beforeFirstKey = 0;
                    // Force a search to the end is no suitable ending point was previously located
                    if (firstTrueKey is null)
                        firstTrueKey = rangeEnd + 1;

                    // Work on the front of the range (recursively)
                    if (firstTrueKey is not null && beforeFirstKey is not null)
                    {
                        int newStartFrame = (int)beforeFirstKey + 1;
                        int newEndFrame = (int)firstTrueKey - 1;
                        if (newStartFrame < newEndFrame)
                        {
                            await FindCalibrationAsync(trueStereoFalseSoloLeft,
                                                        (int)beforeFirstKey + 1, (int)firstTrueKey - 1, frameStep2,
                                                        calibrationTargetSearch,
                                                        true/*trueRecursive*/,
                                                        true/*trueWorkOnStartFalseWorkOnEnd*/,
                                                        callbackDisplay,
                                                        null,
                                                        cancellationToken);
                        }
                    }


                    // First get the current end of where the calibration board was last seen
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
                            await FindCalibrationAsync(trueStereoFalseSoloLeft,
                                                        newStartFrame, newEndFrame, frameStep2,
                                                        calibrationTargetSearch,
                                                        true/*trueRecursive*/,
                                                        false/*trueWorkOnStartFalseWorkOnEnd*/,
                                                        callbackDisplay,
                                                        null,
                                                        cancellationToken);
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
            }

            Data.StartCalibrationBoardZone = startCalibration;
            Data.StopCalibrationBoardZone = stopCalibration;

            return (startCalibration, stopCalibration);
        }


        /// <summary>
        /// Return the start frame index of the calibration board zone
        /// previously found in FindCalibrationBoardZoneAsync
        /// </summary>
        /// <returns>-1 not found</returns>
        public int GetStartCalibrationBoardZone()
        {
            return Data.StartCalibrationBoardZone;
        }


        /// <summary>
        /// Return the end frame index of the calibration board zone
        /// previously found in FindCalibrationBoardZoneAsync
        /// </summary>
        /// <returns>-1 not found</returns>

        public int GetStopCalibrationBoardZone()
        {
            return Data.StopCalibrationBoardZone;
        }



        /// <summary>
        /// Does a recursive search to find the end/stop of the calibration boards in the video
        /// </summary>
        /// <param name="trueStereoFalseMonoLeft"></param>
        /// <param name="startFrame"></param>
        /// <param name="endFrame"></param>
        /// <param name="frameStep"></param>
        /// <param name="calibrationTargetSearch"></param>
        /// <param name="trueRecursive"></param>
        /// <param name="trueWorkOnStartFalseWorkOnEnd"></param>
        /// <param name="callback"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task FindCalibrationAsync(bool trueStereoFalseMonoLeft,
                                                int startFrame, int endFrame, int frameStep,
                                                SortedDictionary<int, bool> calibrationTargetSearch,
                                                bool trueRecursive,
                                                bool? trueWorkOnStartFalseWorkOnEnd,
                                                FrameProcessingCallback? callback,
                                                object? userData,
                                                CancellationToken cancellationToken)
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


                if (trueStereoFalseMonoLeft)
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
                FrameData? targetLeft = null;
                FrameData? targetRight = null;

                if (matLeft is not null && !matLeft.IsEmpty)
                {
                    targetLeft = DetectAndCreateFrameCalibrationTarget(true/*leftTrueRightFalse*/,
                                                                       leftFrameIndex, matLeft);
                    if (targetLeft is not null)
                        DrawMarkersToMat(targetLeft, matLeft, true/*trueMonoHeadfalseStereoHead*/);
                }

                if (trueStereoFalseMonoLeft && matRight is not null && !matRight.IsEmpty)
                {
                    targetRight = DetectAndCreateFrameCalibrationTarget(false/*leftTrueRightFalse*/,
                                                                       rightFrameIndex, matRight);

                    if (targetRight is not null)
                        DrawMarkersToMat(targetRight, matRight, true/*trueMonoHeadfalseStereoHead*/);
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
                                 0, // Corresponding count is not used here
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
                        newEndFrame = (int)firstTrueKey - 1;
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
                        newEndFrame = (int)afterLastKey - 1;
                    }
                }

                // Adjust the frame step 625 > 125 > 25 > 5
                int frameStep2 = frameStep / 5;
                if (frameStep2 < 5)
                    frameStep2 = 1;

                if (newStartFrame < newEndFrame)
                {
                    await FindCalibrationAsync(trueStereoFalseMonoLeft, 
                                                newStartFrame, 
                                                newEndFrame, 
                                                frameStep2,
                                                calibrationTargetSearch,
                                                true/*trueRecursive*/,
                                                trueWorkOnStartFalseWorkOnEnd,
                                                callback, userData, 
                                                cancellationToken);
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
                    beforeFirstKey = i > 0 ? keys[i - 1] : null;
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
                    afterLastKey = i < keys.Count - 1 ? keys[i + 1] : null;
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
                            FrameData? leftFrameCalibrationTarget,
                            int rightFrameIndex,
                            Mat? rightMat,
                            FrameData? rightFrameCalibrationTarget,
                            int correspondingCount,
                            object? userData);


        /// <summary>
        /// Finds the best frames for calibration by processing each frame in the specified range.
        /// </summary>
        /// <param name="startCalibrationFrameIndex"></param>
        /// <param name="stopCalibrationFrameIndex"></param>
        /// <param name="callback"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>0 = OK, -1 = Canceled</returns>
        public async Task<int> FindCalibrationsFramesAsync(int startCalibrationFrameIndex,
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
                FrameData? targetLeft = null;
                FrameData? targetRight = null;

                if (matLeft is not null && !matLeft.IsEmpty)
                {
                    targetLeft = DetectAndCreateFrameCalibrationTarget(true/*leftTrueRightFalse*/, leftFrameIndex, matLeft);

                    if (targetLeft is not null)
                        DrawMarkersToMat(targetLeft, matLeft, true/*trueMonoHeadfalseStereoHead*/);
                }

                if (trueStereoFalseSoloLeft && matRight is not null && !matRight.IsEmpty)
                {
                    targetRight = DetectAndCreateFrameCalibrationTarget(false/*leftTrueRightFalse*/, rightFrameIndex, matRight);
                    if (targetRight is not null)
                    {
                        DrawMarkersToMat(targetRight, matRight, true/*trueMonoHeadfalseStereoHead*/);
                    }
                }

                // Process result
                int correspondingCount = -1;
                try
                {
                    if (trueStereoFalseSoloLeft)
                    {
                        if (targetLeft is not null && targetRight is not null)
                        {
                            correspondingCount = AddFrame(frameIndex, targetLeft, targetRight);
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
                                 correspondingCount,
                                 null);
                    }
                }

                // Simulate work
                await Task.Delay(10, cancellationToken);
            }

            return 0;
        }


        /// <summary>
        /// Save the CalibrationFrameSet.Data class to a JSON file.
        /// </summary>
        /// <param name="path"></param>
        [RequiresUnreferencedCode("SaveToFile uses Json.NET serialization which may not be compatible with trimming.")]
        public bool SaveToFile(string path)
        {
            bool ret = false;
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.None,
                    Converters = { /*new TupleInt2JsonConverter(),*/
                                   new Newtonsoft.Json.Converters.StringEnumConverter()}
                };

                var json = JsonConvert.SerializeObject(this.Data, settings);
                File.WriteAllText(path, json);
                ret = true;
                Debug.WriteLine($"Info Saved to file: {path}");
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file access issues)
                Debug.WriteLine($"Error saving to file: {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// Calculate the movement of the corners from this frame to the previous (if any)
        /// frame and the next frame (if any). Update the movement values in all three frames
        /// </summary>
        /// <param name=""></param>
        /// <returns>true if any changes</returns>
        public bool CalculateCornerMovement(int stereoFrameIndex)
        {
            bool ret = false;

            // Get pair for stereoFrameIndex
            (FrameData leftTarget, FrameData? rightTarget, _) = Data.Frames[stereoFrameIndex];

            // Is there a previous contiguous frame?
            if (Data.Frames.ContainsKey(stereoFrameIndex - 1))
            {
                // Get pair for stereoFrameIndex - 1
                (FrameData leftTargetPrev, FrameData? rightTargetPrev, _) = Data.Frames[stereoFrameIndex - 1];

                // Movement from this left frame to the previous left frame
                double leftMovement = FrameData.CalculateCornerMovement(leftTarget, leftTargetPrev);

                leftTarget.MovementFromPrevious = leftMovement;
                leftTargetPrev.MovementToNext = leftMovement;

                // Check the right frame if it exists
                if (rightTarget is not null && rightTargetPrev is not null)
                {
                    // Movement from this right frame to the previous right frame
                    double rightMovement = FrameData.CalculateCornerMovement(rightTarget, rightTargetPrev);
                    rightTarget.MovementFromPrevious = rightMovement;
                    rightTargetPrev.MovementToNext = rightMovement;
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

            // Is there a next contiguous frame?
            if (Data.Frames.ContainsKey(stereoFrameIndex + 1))
            {
                // Get pair for stereoFrameIndex + 1
                (FrameData leftTargetNext, FrameData? rightTargetNext, _) = Data.Frames[stereoFrameIndex + 1];

                // Movement from this left frame to the next left frame
                double leftMovement = FrameData.CalculateCornerMovement(leftTarget, leftTargetNext);
                leftTarget.MovementToNext = leftMovement;
                leftTargetNext.MovementFromPrevious = leftMovement;

                // Check the right frame if it exists
                if (rightTarget is not null && rightTargetNext is not null)
                {
                    // Movement from this right frame to the next right frame
                    double rightMovement = FrameData.CalculateCornerMovement(rightTarget, rightTargetNext);
                    rightTarget.MovementToNext = rightMovement;
                    rightTargetNext.MovementFromPrevious = rightMovement;
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



        ///
        /// PRIVATE
        /// 


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

                // EMGU.CV: use Set with CapProp
                cap!.Set(CapProp.PosFrames, frameIndex);

                mat = new Mat();
                cap.Read(mat);
            }

            return mat;
        }


        /// <summary>
        /// Detect the ChArUco calibration board in the passed image
        /// </summary>
        /// <param name="trueLeftfalseRight"></param>
        /// <param name="frameIndex"></param>
        /// <param name="frame"></param>
        /// <returns></returns>
        private FrameData? DetectAndCreateFrameCalibrationTarget(bool trueLeftfalseRight, int frameIndex, Mat frame)
        {
            FrameData? ret = null;

            if (chArUcoBoardDefinition is not null)
            {
                try
                {

                    // Convert to gray scale for detection
                    using var gray = new Mat();
                    CvInvoke.CvtColor(frame, gray, ColorConversion.Bgr2Gray);

                    // Detect ArUco markers
                    using var markerCorners = new VectorOfVectorOfPointF();
                    using var markerIds = new VectorOfInt();
                    var parameters = DetectorParameters.GetDefault();

                    ArucoInvoke.DetectMarkers(gray, chArUcoBoardDefinition.Dictionary, markerCorners, markerIds, parameters);


                    // Interpolate ChArUco corners
                    using var charucoCorners = new Mat();
                    using var charucoIds = new VectorOfInt();

                    if (markerIds.Size > 0)
                    {
                        // Optional: Refine marker corners to sub-pixel accuracy
                        for (int i = 0; i < markerCorners.Size; i++)
                        {
                            using var singleMarker = markerCorners[i]; // Access each marker's corner set
                            CvInvoke.CornerSubPix(
                                gray,
                                singleMarker,
                                new Size(3, 3),    // Search window size
                                new Size(-1, -1),  // No dead zone
                                new MCvTermCriteria(30, 0.01)
                            );
                        }

                        // Converts detected marker corners + IDs into interpolated ChArUco corners.
                        ArucoInvoke.InterpolateCornersCharuco(
                            markerCorners,
                            markerIds,
                            gray,
                            chArUcoBoardDefinition.Board,
                            charucoCorners,
                            charucoIds
                        );

                        //???Debug.WriteLine($"Frame:{frameIndex} Detected {charucoIds.Size} ChArUco corners");


                        // Convert detected ChArUco corners to managed types
                        PointF[] managedCorners = new PointF[charucoCorners.Rows];
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
                        //???Debug.WriteLine("Frame:{frameIndex} No markers detected");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Frame:{frameIndex} DetectAndDrawMarkers: Error processing ChArUco board: {ex.Message}");
                }
            }

            return ret;
        }


        /// <summary>
        /// From the metadata stored in the list for the indicated frame index draw the 
        /// markers to the frame Mat and update the screen 
        /// </summary>
        /// <param name="trueLeftfalseRight"></param>
        /// <param name="frameIndex"></param>
        /// <param name="frame"></param>
        /// <returns></returns>
        public static int DrawMarkersToMat(FrameData frameCalibrationTarget, Mat frame, bool headTrueIsStereoFalseIsMode)
        {
            int ret = 0;

            try
            {
                // Create a VectorOfPointF and populate it from the managed array
                var charucoCorners = new VectorOfPointF();
                charucoCorners.Push(frameCalibrationTarget.ChArUcoCorners);

                // managedIds is int[]
                var charucoIds = new VectorOfInt();
                charucoIds.Push(frameCalibrationTarget.ChArUcoIds);


                ArucoInvoke.DrawDetectedCornersCharuco(
                    frame,
                    charucoCorners,
                    charucoIds,
                    new MCvScalar(0, 255, 0)  // Green for ChArUco IDs
                );


                // If Mono
                if (headTrueIsStereoFalseIsMode == false)
                { 
                    // If there are re-projected points then draw them
                    int index = frameCalibrationTarget.monoProjectedPoints?
                            .Select((arr, i) => new { arr, i })
                            .FirstOrDefault(x => x.arr != null && x.arr.Length > 0)?.i ?? -1;

                    if (index != -1 && frameCalibrationTarget?.monoProjectedPoints is not null)
                    {
                        foreach (var pt in frameCalibrationTarget.monoProjectedPoints[index])
                        {
                            CvInvoke.Circle(
                                frame,
                                new Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)),
                                10,
                                new MCvScalar(0, 0, 255),  // Red for re-projected
                                3  // Filled circle
                            );
                        }
                    }

                    // Draw any ChArUco corners if they exist
                    if (frameCalibrationTarget is not null &&
                        frameCalibrationTarget.ChArUcoCorners is not null &&
                        frameCalibrationTarget.ChArUcoCorners.Length > 0)
                    {
                        foreach (var pt in frameCalibrationTarget.ChArUcoCorners)
                        {
                            CvInvoke.Circle(
                                frame,
                                new Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)),
                                14,
                                new MCvScalar(255, 0, 0), // Blue for CHarUco corners
                                3  // Filled circle
                            );
                        }
                    }
                }
                else 
                {

                    // Draw any ChArUco corners if they exist
                    if (frameCalibrationTarget is not null &&
                        frameCalibrationTarget.StereoSharedChArUcoCorners is not null)
                    {
                        int indexStereoSharedCharucoCorners = frameCalibrationTarget.StereoSharedChArUcoCorners?
                                            .Select((arr, i) => new { arr, i })
                                            .FirstOrDefault(x => x.arr != null && x.arr.Length > 0)?.i ?? -1;

                        if (indexStereoSharedCharucoCorners != -1 &&
                            frameCalibrationTarget.StereoSharedChArUcoCorners is not null &&
                            frameCalibrationTarget.StereoSharedChArUcoCorners[indexStereoSharedCharucoCorners] is not null)
                        {
                            foreach (var pt in frameCalibrationTarget.StereoSharedChArUcoCorners[indexStereoSharedCharucoCorners])
                            {
                                CvInvoke.Circle(
                                    frame,
                                    new Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)),
                                    14,
                                    new MCvScalar(255, 0, 0), // Blue for CHarUco corners
                                    3  // Filled circle
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DrawMarkersToMat: Error processing ChArUco board: {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// Performs a mono calibration on a stereo head using the best frames found in the Frames dictionary 
        /// on both the left and right side
        /// </summary>
        /// <returns></returns>
        public (MonoCalibrationCameraData? left, MonoCalibrationCameraData? right) MonoCalibrateLeftAndRightUsingBestFrames(Windows.Foundation.Size frameSize, int monoCornersMinThreshold, CalibrationParameters calibrationParameters)
        {
            MonoCalibrationCameraData? monoCalibLeft = null;
            MonoCalibrationCameraData? monoCalibRight = null;

            if (chArUcoBoardDefinition is not null)
            {
                monoCalibLeft = MonoCalibrateUsingBestFrames(true/*trueStereoFalseMonoLeft*/, 
                                                             true/*trueLeftFalseRight*/, 
                                                             frameSize, 
                                                             monoCornersMinThreshold, 
                                                             calibrationParameters);

                if (monoCalibLeft is not null)
                {
                    // Check if right side is active
                    if (rightCapture is not null)
                    {
                        monoCalibRight = MonoCalibrateUsingBestFrames(true/*trueStereoFalseMonoLeft*/,
                                                                      false/*trueLeftFalseRight*/, 
                                                                      frameSize, 
                                                                      monoCornersMinThreshold, 
                                                                      calibrationParameters);
                    }
                }
            }
            return (monoCalibLeft, monoCalibRight);
        }



        // Updated MonoCalibrateUsingBestFrames function

        // Collecting Corner Data: We iterate over each index in BestFrameIndexes and gather
        // the detected ChArUco corner positions(CharucoCorners) and IDs(CharucoIds) from
        // either the left or right frame data.We only include frames that have a sufficient
        // number of detected corners (at least 80 in this case) to ensure robust calibration data.
        // Calibrating the Camera: Using the aggregated allCharucoCorners and allCharucoIds,
        // we call ArucoInvoke.CalibrateCameraCharuco to compute the camera’s intrinsic matrix
        // and distortion coefficients. This function returns the overall re-projection error (RMS)
        // of the calibration docs.opencv.org, and also outputs a rotation vector (rvecs) and
        // translation vector (tvecs) for each frame (representing the camera pose for that frame).
        // We copy the resulting camera matrix and distortion coefficients into convenient
        // Matrix<double> objects for later use.
        // Projecting Points for Error Calculation: After calibration, we loop through each
        // frame’s data again.For each frame, we take the observed 2D corner points
        // (observedCorners) and the corresponding known 3D coordinates of those ChArUco
        // corners on the board (objectPoints). We use CvInvoke.ProjectPoints to project the
        // 3D points back into the image plane using the computed camera parameters.This gives
        // us the projected 2D points for each corner in that frame.
        // Per-Frame Error Metrics: We compute the re-projection error for each corner by measuring
        // the distance (Euclidean error) between the observed corner position and its projected
        // position. From these, we calculate the RMS error and maximum error for each frame.
        // These per-frame errors are output to the debug log (Debug.WriteLine) to help identify
        // if any particular frame has a high error (which could indicate an outlier or a
        // detection issue).
        // Overall Error Metrics: We aggregate all observed and projected points from every
        // frame to compute an overall RMS re-projection error(ProjectionRMS) across all points,
        // as well as the maximum re-projection error(MaxError) among all the corners in all
        // frames.This provides a global measure of calibration accuracy in addition to the
        // RMS error returned by the calibration function.
        // Output Structure: Finally, we populate the MonoCalibrationCameraData object with
        // the calibration results: the intrinsic camera matrix, distortion coefficients,
        // the calibration’s re-projection RMS (as returned by CalibrateCameraCharuco), and
        // the calculated ProjectionRMS and MaxError.This gives the calling code access to
        // both the camera parameters and the error metrics for further analysis or display.

        public MonoCalibrationCameraData? MonoCalibrateUsingBestFrames(
                                                    bool trueStereoFalseMono,
                                                    bool trueLeftFalseRight,
                                                    Windows.Foundation.Size frameSize,
                                                    int monoCornerCountThreshold,
                                                    CalibrationParameters calibrationParameters)
        {
            MonoCalibrationCameraData? monoCalibrationCameraData = null;
            double reprojectionRMS = -1;
            double rmsUpper = 3.0; // Set high
            double maxUpper = 5.0; // Set high
            int imageUsable; // Count of usable images

            string side = string.Empty;

            bool trueTargetLeftFalseUseTargetRight;
            if (trueStereoFalseMono)
            {
                // Stereo Head
                trueTargetLeftFalseUseTargetRight = trueLeftFalseRight;
                side = trueLeftFalseRight ? "Left" : "Right";
            }
            else
            {
                // Mono Head (always uses the left side)
                trueTargetLeftFalseUseTargetRight = true/*trueLeftFalseRight*/;
                side = trueLeftFalseRight ? "Left" : "Right";
            }

            if (chArUcoBoardDefinition is null)
            {
                Debug.WriteLine($"{side} ChArUco board definition is null.");
                return null;
            }

            int passCount = 1;

            for (int pass = 0; pass < passCount; pass++)
            {
                var allCharucoCorners = new VectorOfVectorOfPointF();
                var allCharucoIds = new VectorOfVectorOfInt();
                List<FrameData> allFrameData = [];
                imageUsable = 0;

                // Collect ChArUco corner detections from the best frames
                int frameRemovedFromRMSOrMaxError = 0;
                foreach (BestFrame bestFrame in Data.BestFrameIndexes)
                {
                    int frameIndex = bestFrame.FrameIndex;

                    
                    if (!Data.Frames.TryGetValue(frameIndex, out var framePair))
                        continue;

                    FrameData? calibrationFrame = null;
                    try
                    {
                        calibrationFrame = trueTargetLeftFalseUseTargetRight
                            ? framePair.frameCalibrationTargetLeft
                            : framePair.frameCalibrationTargetRight;
                    }
                    catch(Exception ex)
                    {
                        Debug.WriteLine($"{side} MonoCalibrateUsingBestFrames: Error accessing frame data for frame index {frameIndex}: {ex.Message}");
                    }

                    // The first pass (pass zero) is used to gather all frames with sufficient corners.
                    if (pass == 0)
                    {
                        if (calibrationFrame is null ||
                            calibrationFrame.ChArUcoCorners.Length == 0 ||
                            calibrationFrame.ChArUcoIds.Length == 0 ||
                            calibrationFrame.ChArUcoCorners.Length < monoCornerCountThreshold)
                        {
                            // Ensure the projected RMS and Max Error are reset (may have been previously set)
                            if (calibrationFrame is not null)
                            {
                                calibrationFrame.monoFrameRms[(int)calibrationParameters] = -1; // Mark as invalid
                                calibrationFrame.monoFrameMaxError[(int)calibrationParameters] = -1; // Mark as invalid
                            }
                            continue;
                        }
                    }
                    // Second pass (pass one) is used to gather all frames with sufficient corners and
                    // the projected RMS and Max Error are with thresholds.
                    // Note the projected RMS and Max Error were calculated in the first pass
                    else if (pass == 1)
                    {
                        if (calibrationFrame is null ||
                            calibrationFrame.ChArUcoCorners.Length == 0 ||
                            calibrationFrame.ChArUcoIds.Length == 0 ||
                            calibrationFrame.ChArUcoCorners.Length < monoCornerCountThreshold )
                        {
                            continue;
                        }
                        if (calibrationFrame is not null &&
                            (calibrationFrame.monoFrameRms[(int)calibrationParameters] > rmsUpper || 
                             calibrationFrame.monoFrameMaxError[(int)calibrationParameters] > maxUpper))
                        {
                            frameRemovedFromRMSOrMaxError++;
                            continue;
                        }
                    }


                    if (calibrationFrame is not null)
                    {
                        imageUsable++;
                        allCharucoCorners.Push(new VectorOfPointF(calibrationFrame.ChArUcoCorners));
                        allCharucoIds.Push(new VectorOfInt(calibrationFrame.ChArUcoIds));
                        allFrameData.Add(calibrationFrame);
                    }
                }

                if (allCharucoCorners.Size == 0)
                {
                    Debug.WriteLine($"{side} No valid ChArUco data found in best frames.");
                    return null;
                }
                if (pass == 1 && frameRemovedFromRMSOrMaxError > 0)
                {
                    Debug.WriteLine($"{side} Mono calibration second pass (pass 1) removed {frameRemovedFromRMSOrMaxError} frames due to RMS or Max Error thresholds.");
                }

                Size frameSizeCv = new((int)frameSize.Width, (int)frameSize.Height);
                using var cameraMatrix = new Mat();
                using var distCoeffs = new Mat();
                using var rvecs = new VectorOfMat();
                using var tvecs = new VectorOfMat();

                var intrinsicMatrix = new Matrix<double>(3, 3);

                // Setup the distortionCoeffs with a 5, or 8 row matrix depending on the calibration parameters
                (CalibType flags, int distortionRowCount) = GetCalibrationFlags(calibrationParameters);
                var distortionCoeffs = new Matrix<double>(1, distortionRowCount);                

                try
                {
                    reprojectionRMS = ArucoInvoke.CalibrateCameraCharuco(
                                                    allCharucoCorners,
                                                    allCharucoIds,
                                                    chArUcoBoardDefinition.Board,
                                                    frameSizeCv,
                                                    cameraMatrix,
                                                    distCoeffs,
                                                    rvecs,
                                                    tvecs,
                                                    flags,
                                                    new MCvTermCriteria(30, 1e-6));

                    cameraMatrix.CopyTo(intrinsicMatrix);
                    distCoeffs.CopyTo(distortionCoeffs);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Mono calibration failed: {ex.Message}");
                    return null;
                }

                // Compute projection error
                var charucoCorner3DMap = GetCharucoCorner3DPoints(chArUcoBoardDefinition);
                (double projectionRms, double maxError) = MonoComputeProjectionErrors(
                                                    allCharucoCorners,
                                                    allCharucoIds,
                                                    allFrameData,
                                                    rvecs,
                                                    tvecs,
                                                    intrinsicMatrix,
                                                    distortionCoeffs,
                                                    charucoCorner3DMap,
                                                    calibrationParameters);

                // Return results
                monoCalibrationCameraData = new MonoCalibrationCameraData
                {
                    CalibrationParameters = calibrationParameters,
                    ImageTotal = Data.BestFrameIndexes.Count,
                    ImagesUsed = imageUsable,
                    IntrinsicMatrix = intrinsicMatrix,
                    DistortionCoeffs = distortionCoeffs,
                    ReprojectionRMS = reprojectionRMS,  // return from ArucoInvoke.CalibrateCameraCharuco
                    ProjectionRMS = projectionRms,
                    MaxError = maxError
                };

                Debug.WriteLine($"{side} mono calibration first pass (pass 0) complete. Re-projection RMS: {reprojectionRMS:F4}, Projection RMS: {projectionRms:F4}, Max Error: {maxError:F4}");

                // Check if frames can be improved and if so re-run the calibration
                // Select relevant FrameCalibrationData from BestFrameIndexes
                var selectedFrames = Data.BestFrameIndexes
                    .Select(bestFrame => Data.Frames.TryGetValue(bestFrame.FrameIndex, out var tuple)
                        ? (success: true, data: trueTargetLeftFalseUseTargetRight ? tuple.frameCalibrationTargetLeft : tuple.frameCalibrationTargetRight)
                        : (success: false, data: null))
                    .Where(t => t.success && t.data != null)
                    .Select(t => t.data!)
                    .ToList();

                // Compute Q3 + 1.5*IQR for RMS and Max error
                var rmsValues = selectedFrames.Select(f => f.monoFrameRms[(int)calibrationParameters]).OrderBy(v => v).ToList();
                var maxValues = selectedFrames.Select(f => f.monoFrameMaxError[(int)calibrationParameters]).OrderBy(v => v).ToList();

                rmsUpper = GetUpperFence(rmsValues);
                maxUpper = GetUpperFence(maxValues);

                if (selectedFrames.Any(f =>f.monoFrameRms[(int)calibrationParameters] > rmsUpper || f.monoFrameMaxError[(int)calibrationParameters] > maxUpper))
                {
                    Debug.WriteLine($"{side} mono calibration second pass (pass 1), projection RMS threshold: {rmsUpper:F2}, max error threshold: {maxUpper:F2}");
                    passCount = 2;
                }
            }


            return monoCalibrationCameraData;
        }


        /// <summary>
        /// Compute the projection errors for the given ChArUco corners and IDs
        /// </summary>
        /// <param name="allCharucoCorners"></param>
        /// <param name="allCharucoIds"></param>
        /// <param name="allFrameData"></param>
        /// <param name="rvecs"></param>
        /// <param name="tvecs"></param>
        /// <param name="intrinsicMatrix"></param>
        /// <param name="distortionCoeffs"></param>
        /// <param name="charucoCorner3DMap"></param>
        /// <param name="calibrationParameters"></param>
        /// <returns></returns>
        private static (double ProjectionRms, double MaxError) MonoComputeProjectionErrors(
                                                    VectorOfVectorOfPointF allCharucoCorners,
                                                    VectorOfVectorOfInt allCharucoIds,
                                                    List<FrameData> allFrameData,
                                                    VectorOfMat rvecs,
                                                    VectorOfMat tvecs,
                                                    Matrix<double> intrinsicMatrix,
                                                    Matrix<double> distortionCoeffs,
                                                    Dictionary<int, MCvPoint3D32f> charucoCorner3DMap,
                                                    CalibrationParameters calibrationParameters)
        {
            var allObserved = new List<PointF>();
            var allProjected = new List<PointF>();

            for (int i = 0; i < allCharucoIds.Size; i++)
            {
                var ids = allCharucoIds[i].ToArray();
                var corners = allCharucoCorners[i].ToArray();

                if (ids.Length == 0 || corners.Length == 0)
                    continue;

                var objectPoints = new List<MCvPoint3D32f>();
                var observedCorners = new List<PointF>();

                for (int j = 0; j < ids.Length; j++)
                {
                    if (charucoCorner3DMap.TryGetValue(ids[j], out var point3D))
                    {
                        objectPoints.Add(point3D);
                        observedCorners.Add(corners[j]);
                    }
                    else
                    {
                        Debug.WriteLine($"Missing 3D point for ChArUco ID {ids[j]}");
                    }
                }

                if (objectPoints.Count == 0)
                    continue;

                var projectedPoints = CvInvoke.ProjectPoints(
                    [.. objectPoints],
                    rvecs[i],
                    tvecs[i],
                    intrinsicMatrix,
                    distortionCoeffs);
                
                allObserved.AddRange(observedCorners);
                allProjected.AddRange(projectedPoints);

                // Compute per-frame errors
                var errors = observedCorners.Zip(projectedPoints, (obs, proj) =>
                    Math.Sqrt(Math.Pow(obs.X - proj.X, 2) + Math.Pow(obs.Y - proj.Y, 2))).ToList();

                double frameRms = errors.Count > 0 ? Math.Sqrt(errors.Sum(e => e * e) / errors.Count) : 0.0;
                double frameMaxError = errors.Count > 0 ? errors.Max() : 0.0;

                // Save the frame quality tests
                allFrameData[i].monoProjectedPoints[(int)calibrationParameters] = projectedPoints;
                allFrameData[i].monoFrameRms[(int)calibrationParameters] = frameRms;
                allFrameData[i].monoFrameMaxError[(int)calibrationParameters] = frameMaxError;
                
                //???Debug.WriteLine($"{i}: Frame {allFrameData[i].FrameIndex}: RMS = {frameRms:F2}, Max = {frameMaxError:F2}");
            }

            // Compute overall errors
            double projectionRms = 0.0, maxError = 0.0;
            if (allObserved.Count > 0)
            {
                var allErrors = allObserved.Zip(allProjected, (obs, proj) =>
                    Math.Sqrt(Math.Pow(obs.X - proj.X, 2) + Math.Pow(obs.Y - proj.Y, 2))).ToList();

                projectionRms = Math.Sqrt(allErrors.Sum(e => e * e) / allErrors.Count);
                maxError = allErrors.Max();
            }

            Debug.WriteLine($"{calibrationParameters}: Overall Projection RMS: {projectionRms:F2}, Max Error: {maxError:F2}");

            return (projectionRms, maxError);
        }


        /// <summary>
        /// Get the upper fence for the IQR method to detect outliers.
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>
        private static double GetUpperFence(List<double> values)
        {
            int count = values.Count;
            if (count < 4) return double.MaxValue; // not enough data to compute IQR reliably

            double Q1 = values[(int)(0.25 * count)];
            double Q3 = values[(int)(0.75 * count)];
            double IQR = Q3 - Q1;
            return Q3 + 1.5 * IQR;
        }


        /// <summary>
        /// Return a text of the mono calibration data
        /// </summary>
        /// <param name="monoCalibration"></param>
        /// <returns></returns>
        public static string CalibrationCameraDataText(MonoCalibrationCameraData? monoCalibration)
        {
            int DistRowCount  = -1;

            try
            {
                // Display the calibration results
                if (monoCalibration is not null && monoCalibration.IntrinsicMatrix is not null && monoCalibration.DistortionCoeffs is not null)
                {
                    StringBuilder sb = new();
                    sb.AppendLine("Intrinsic:");
                    for (int i = 0; i < 3; i++)
                    {
                        sb.AppendLine($"{monoCalibration.IntrinsicMatrix[i, 0],7:F1} {monoCalibration.IntrinsicMatrix[i, 1],7:F1} {monoCalibration.IntrinsicMatrix[i, 2],7:F1}");
                    }

                    sb.AppendLine($"Distortion:");
                    (_, DistRowCount) = GetCalibrationFlags(monoCalibration.CalibrationParameters);

                    if (DistRowCount >= 5)
                    {
                        sb.AppendLine($"k1:{monoCalibration.DistortionCoeffs[0, 0],7:F3}  k2:{monoCalibration.DistortionCoeffs[0, 1],7:F3}");
                        sb.AppendLine($"p1:{monoCalibration.DistortionCoeffs[0, 2],7:F3}  p2:{monoCalibration.DistortionCoeffs[0, 3],7:F3}");
                        sb.AppendLine($"k3:{monoCalibration.DistortionCoeffs[0, 4],7:F3}");
                    }
                    if (DistRowCount >= 8)
                    {
                        sb.AppendLine($"k4:{monoCalibration.DistortionCoeffs[0, 5],7:F3}");
                        sb.AppendLine($"k5:{monoCalibration.DistortionCoeffs[0, 6],7:F3}  k6:{monoCalibration.DistortionCoeffs[0, 7],7:F3}");
                    }
                    if (DistRowCount >= 12)
                    {
                        sb.AppendLine($"s1:{monoCalibration.DistortionCoeffs[0, 8],7:F3}  s2:{monoCalibration.DistortionCoeffs[0, 9],7:F3}");
                        sb.AppendLine($"s3:{monoCalibration.DistortionCoeffs[0, 10],7:F3}  s4:{monoCalibration.DistortionCoeffs[0, 11],7:F3}");
                    }
                    if (DistRowCount >= 14)
                    {
                        sb.AppendLine($"tx:{monoCalibration.DistortionCoeffs[0, 12],7:F3}  ty:{monoCalibration.DistortionCoeffs[0, 13],7:F3}");
                    }


                    // RPE RMS
                    string rpeQuanlity = string.Empty;
                    if (monoCalibration.ReprojectionRMS <= 0.2)
                        rpeQuanlity = "(excellent)";
                    else if (monoCalibration.ReprojectionRMS <= 0.5)
                        rpeQuanlity = "(very good)";
                    else if (monoCalibration.ReprojectionRMS <= 1.0)
                        rpeQuanlity = "(acceptable)";
                    else if (monoCalibration.ReprojectionRMS <= 1.5)
                        rpeQuanlity = "(poor)";
                    else if (monoCalibration.ReprojectionRMS <= 2.0)
                        rpeQuanlity = "(very poor)";
                    else
                        rpeQuanlity = "(terrible)";
                    // < 0.2    Excellent(usually only in studio/lab with perfect lighting and corner visibility)
                    // 0.2–0.5  Very good; suitable for accurate 3D reconstructions and pose estimates
                    // 0.5–1.0  Acceptable for many real-world use cases, especially underwater, drone, etc.
                    // > 1.0    Often indicates blur, motion, poor corner detection, or bad coverage
                    sb.AppendLine($"RPE: {monoCalibration.ReprojectionRMS:F2}px {rpeQuanlity}");

                    // Project RMS and MAX Error
                    sb.AppendLine($"Projection RMS: {monoCalibration.ProjectionRMS:F2}px");
                    sb.AppendLine($"Max Error: {monoCalibration.MaxError:F2}px");

                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                
                Debug.WriteLine($"CalibrationCameraDataText: Error generating calibration text, " +
                    $"Distortion coefficient Count={DistRowCount}, " +
                    $"Distortion matrix is {monoCalibration?.DistortionCoeffs?.Rows}x{monoCalibration?.DistortionCoeffs?.Cols}" +
                    $"Intrinsic matrix is {monoCalibration?.IntrinsicMatrix?.Rows}x{monoCalibration?.IntrinsicMatrix?.Cols}, " +
                    $"{ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// Return text for the stereo calibration data
        /// </summary>
        /// <param name="monoCalib"></param>
        /// <returns></returns>
        public static string CalibrationCameraDataText(CalibrationStereoCameraData? stereoCalibration)
        {
            try
            {
                // Display the calibration results
                if (stereoCalibration is not null && stereoCalibration.Rotation is not null && stereoCalibration.Translation is not null)
                {
                    StringBuilder sb = new();
                    sb.AppendLine("Rotation:");
                    for (int i = 0; i < 3; i++)
                    {
                        sb.AppendLine($"{stereoCalibration.Rotation[i, 0],6:F3}  {stereoCalibration.Rotation[i, 1],6:F3}  {stereoCalibration.Rotation[i, 2],6:F3}");
                    }

                    sb.AppendLine($"Translation:");

                    for (int i = 0; i < 3; i++)
                    {
                        if (i > 0)
                            sb.Append("  ");

                        sb.Append($"{stereoCalibration.Translation[i, 0],6:F3}");
                    }
                    sb.AppendLine();


                    // RPE RMS
                    string rpeQuanlity = string.Empty;
                    if (stereoCalibration.RMS <= 0.5)
                        rpeQuanlity = "(excellent)";
                    else if (stereoCalibration.RMS <= 1.0)
                        rpeQuanlity = "(very good)";
                    else if (stereoCalibration.RMS <= 1.8)
                        rpeQuanlity = "(acceptable)";
                    else if (stereoCalibration.RMS <= 2.5)
                        rpeQuanlity = "(poor)";
                    else if (stereoCalibration.RMS <= 3.5)
                        rpeQuanlity = "(very poor)";
                    else
                        rpeQuanlity = "(terrible)";
                    // Stereo re-projection RMS (px)
                    // Stereo RMS includes inter-camera geometry and is naturally higher than mono RMS.
                    //<= 0.5   excellent
                    //<= 1.0   very good
                    //<= 1.8   acceptable
                    //<= 2.5   poor
                    //<= 3.5   very poor
                    //> 3.5   terrible
                    string rmsText = stereoCalibration.RMS > 999
                                        ? stereoCalibration.RMS.ToString("0.###E+0")    // exponent format for very large values
                                        : stereoCalibration.RMS.ToString("F2");         // normal fixed-point for typical values
                    sb.AppendLine($"RPE: {rmsText}px {rpeQuanlity}");

                    // Project RMS and MAX Error
                    if (stereoCalibration.ProjectionRMS != 0)
                        sb.AppendLine($"Projection RMS: {stereoCalibration.ProjectionRMS:F2}px");
                    if (stereoCalibration.MaxError != 0)
                        sb.AppendLine($"Max Error: {stereoCalibration.MaxError:F2}px");

                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CalibrationCameraDataText: Error generating calibration text: {ex.Message}");
            }

            return string.Empty;
        }


        /// <summary>
        /// Manually compute the 3D corner positions based on the known board 
        /// layout (squaresX, squaresY, squareLength, markerLength) and ChArUco 
        /// ID indexing.
        /// THis is needed because ChArUcoBoard in EMGU.CV (and OpenCV) doesn't 
        /// expose a direct method like GetChessboardCorners() to retrieve the 
        /// 3D object points for individual ChArUco
        /// </summary>
        /// <param name="board"></param>
        /// <returns></returns>
        private static Dictionary<int, MCvPoint3D32f> GetCharucoCorner3DPoints(CalibrationBoardDefinition board)
        {
            var cornerMap = new Dictionary<int, MCvPoint3D32f>();
            
            int id = 0;
            for (int y = 1; y < board.SquaresY; y++)
            {
                for (int x = 1; x < board.SquaresX; x++)
                {
                    float fx = x * board.SquareLength;
                    float fy = y * board.SquareLength;
                    cornerMap[id++] = new MCvPoint3D32f(fx, fy, 0.0f);
                }
            }

            return cornerMap;
        }


        /// <summary>
        /// Stereo calibration
        /// </summary>
        /// <param name="frameSize"></param>
        /// <param name="stereoCornerCountThreshold"></param>
        /// <param name="leftMonoCalibrationCameraData"></param>
        /// <param name="rightMonoCalibrationCameraData"></param>
        /// <param name="calibrationParameters"></param>
        /// <returns></returns>
        public CalibrationStereoCameraData? StereoCalibrateUsingBestFrames(
                                                    Windows.Foundation.Size frameSize,
                                                    int stereoCornerCountThreshold,
                                                    MonoCalibrationCameraData leftMonoCalibrationCameraData,
                                                    MonoCalibrationCameraData rightMonoCalibrationCameraData,
                                                    CalibrationParameters calibrationParameters)
        {
            CalibrationStereoCameraData? calibrationStereoCameraData = null;
            int imageUseable = 0;

            const int minCharucoIdsCount = 4;

            // Add debug asset code to check the input is as expected
            // i.e. charucoBoardDefinition is not null
            // BestFrameIndexes has values
            if (chArUcoBoardDefinition is null)
                return null;

            // Define output containers
            List<MCvPoint3D32f[]> objectPoints = [];
            List<PointF[]> imagePointsLeft = [];
            List<PointF[]> imagePointsRight = [];

            Dictionary<int, MCvPoint3D32f> charucoCorner3DMap = GetCharucoCorner3DPoints(chArUcoBoardDefinition);

            foreach (BestFrame bestFrame in Data.BestFrameIndexes)
            {
                int frameIndex = bestFrame.FrameIndex;

                if (!Data.Frames.TryGetValue(frameIndex, out var framePair))
                    continue;

                var left = framePair.frameCalibrationTargetLeft;
                var right = framePair.frameCalibrationTargetRight;

                if (left == null || right == null || 
                    left.ChArUcoIds.Length < minCharucoIdsCount || right.ChArUcoIds.Length < minCharucoIdsCount)
                    continue;

                // Match corners by ID
                Dictionary<int, PointF> leftDict = [];
                for (int i = 0; i < left.ChArUcoIds.Length; i++)
                    leftDict[left.ChArUcoIds[i]] = left.ChArUcoCorners[i];

                Dictionary<int, PointF> rightDict = [];
                for (int i = 0; i < right.ChArUcoIds.Length; i++)
                    rightDict[right.ChArUcoIds[i]] = right.ChArUcoCorners[i];

                List<int> sharedIds = [.. leftDict.Keys.Intersect(rightDict.Keys)];
                if (sharedIds.Count < stereoCornerCountThreshold)
                    continue;

                List<MCvPoint3D32f> objPts = [];
                List<PointF> imgPtsLeft = [];
                List<PointF> imgPtsRight = [];
                List<int> usedIds = [];

                foreach (var id in sharedIds)
                {
                    if (!charucoCorner3DMap.TryGetValue(id, out var pt3D))
                        continue;

                    objPts.Add(pt3D);
                    imgPtsLeft.Add(leftDict[id]);
                    imgPtsRight.Add(rightDict[id]);
                    usedIds.Add(id);
                }

                // And store of displaying later
                left.StereoSharedChArUcoCorners[(int)calibrationParameters] = [.. imgPtsLeft];
                right.StereoSharedChArUcoCorners[(int)calibrationParameters] = [.. imgPtsRight];
                left.StereoSharedChArUcoIDs[(int)calibrationParameters] = [.. usedIds];  // Only need on the left side as both left and right are the same

                if (objPts.Count >= stereoCornerCountThreshold)
                {
                    imageUseable++;
                    objectPoints.Add([.. objPts]);
                    imagePointsLeft.Add([.. imgPtsLeft]);
                    imagePointsRight.Add([.. imgPtsRight]);
                }
            }


            if (objectPoints.Count < 3)
                return null;

            if (leftMonoCalibrationCameraData.IntrinsicMatrix is null ||
                rightMonoCalibrationCameraData.IntrinsicMatrix is null ||
                leftMonoCalibrationCameraData.DistortionCoeffs is null ||
                rightMonoCalibrationCameraData.DistortionCoeffs is null)
            {
                return null;
            }

            var camMatL = leftMonoCalibrationCameraData.IntrinsicMatrix.Mat.Clone();
            var camMatR = rightMonoCalibrationCameraData.IntrinsicMatrix.Mat.Clone();
            var distL = leftMonoCalibrationCameraData.DistortionCoeffs.Mat.Clone();
            var distR = rightMonoCalibrationCameraData.DistortionCoeffs.Mat.Clone();

            var R = new Mat();
            var T = new Mat();
            var E = new Mat();
            var F = new Mat();

            Size imgSize = new((int)frameSize.Width, (int)frameSize.Height);
            double error;

            try
            {
                error = CvInvoke.StereoCalibrate([.. objectPoints],
                                                [.. imagePointsLeft],
                                                [.. imagePointsRight],
                                                camMatL, distL,
                                                camMatR, distR,
                                                imgSize,
                                                R, T, E, F,
                                                CalibType.FixIntrinsic,
                                                new MCvTermCriteria(30, 1e-6));

            }
            catch (Exception ex)
            {
                report?.Warning("", $"Stereo calibration CvInvoke.StereoCalibrate failed: {ex.Message}");
                return null;
            }

            var Rmat = new Matrix<double>(3, 3);
            R.CopyTo(Rmat);
            var Tmat = new Matrix<double>(3, 1);
            T.CopyTo(Tmat);
            var Emat = new Matrix<double>(3, 3);
            E.CopyTo(Emat);
            var Fmat = new Matrix<double>(3, 3);
            F.CopyTo(Fmat);

            calibrationStereoCameraData = new()
            {
                Rotation = Rmat /*???new Emgu.CV.Matrix<double>(3, 3)*/,
                Translation = Tmat /*???new Emgu.CV.Matrix<double>(3, 1)*/,
                ImageTotal = Data.Frames.Count,
                ImagesUsed = imageUseable,
                RMS = error
            };

            // Stereo per-frame + overall projection errors 
            var allErrors = new List<double>(capacity: 4096);

            // Re-projection test               
            foreach (BestFrame bestFrame in Data.BestFrameIndexes)
            {
                int frameIndex = bestFrame.FrameIndex;

                if (!Data.Frames.TryGetValue(frameIndex, out var framePair))
                    continue;

                var left = framePair.frameCalibrationTargetLeft;
                var right = framePair.frameCalibrationTargetRight;

                if (left == null || right == null ||
                    left.ChArUcoIds.Length < minCharucoIdsCount || right.ChArUcoIds.Length < minCharucoIdsCount)
                    continue;

                // Reset (in case previously set)
                left.stereoFrameRms[(int)calibrationParameters] = -1;
                left.stereoFrameMaxError[(int)calibrationParameters] = -1;
                right.stereoFrameRms[(int)calibrationParameters] = -1;
                right.stereoFrameMaxError[(int)calibrationParameters] = -1;

                // Match corners by ID
                Dictionary<int, PointF> leftDict = [];
                for (int i = 0; i < left.ChArUcoIds.Length; i++)
                    leftDict[left.ChArUcoIds[i]] = left.ChArUcoCorners[i];

                Dictionary<int, PointF> rightDict = [];
                for (int i = 0; i < right.ChArUcoIds.Length; i++)
                    rightDict[right.ChArUcoIds[i]] = right.ChArUcoCorners[i];

                List<int> sharedIds = [.. leftDict.Keys.Intersect(rightDict.Keys)];
                if (sharedIds.Count < stereoCornerCountThreshold)
                    continue;

                List<MCvPoint3D32f> objPts = [];
                List<PointF> imgPtsLeft = [];
                List<PointF> imgPtsRight = [];

                foreach (var id in sharedIds)
                {
                    if (!charucoCorner3DMap.TryGetValue(id, out var pt3D))
                        continue;

                    objPts.Add(pt3D);
                    imgPtsLeft.Add(leftDict[id]);
                    imgPtsRight.Add(rightDict[id]);
                }


                // Re-Setup distL & distR because CvInvoke.StereoCalibrate will change them
                distL = leftMonoCalibrationCameraData.DistortionCoeffs.Mat.Clone();
                distR = rightMonoCalibrationCameraData.DistortionCoeffs.Mat.Clone();

                // Calculate the re-projection errors for this frame
                PointF[] leftProjected;
                PointF[] rightProjected;

                try
                {
                    (leftProjected, rightProjected) = ValidateStereoProjectionReprojectionError(frameIndex,
                                                                                                [.. objPts], [.. imgPtsLeft], [.. imgPtsRight],
                                                                                                camMatL, distL,
                                                                                                camMatR, distR,
                                                                                                Rmat, Tmat);
                }
                catch (Exception ex)
                {
                    report?.Warning("", $"Stereo calibration ValidateStereoProjectionReprojectionError failed: {ex.Message}");
                    return null;
                }

                if (leftProjected.Length == 0 || rightProjected.Length == 0)
                    continue;

                int n = Math.Min(imgPtsLeft.Count, Math.Min(leftProjected.Length, rightProjected.Length));
                if (n <= 0)
                    continue;

                var errorsFrame = new double[n * 2];
                int k = 0;

                for (int j = 0; j < n; j++)
                {
                    double errL = Math.Sqrt(Math.Pow(imgPtsLeft[j].X - leftProjected[j].X, 2) + Math.Pow(imgPtsLeft[j].Y - leftProjected[j].Y, 2));
                    double errR = Math.Sqrt(Math.Pow(imgPtsRight[j].X - rightProjected[j].X, 2) + Math.Pow(imgPtsRight[j].Y - rightProjected[j].Y, 2));

                    errorsFrame[k++] = errL;
                    errorsFrame[k++] = errR;
                }

                double frameRms = Math.Sqrt(errorsFrame.Sum(e => e * e) / errorsFrame.Length);
                double frameMax = errorsFrame.Max();

                left.stereoProjectedPoints[(int)calibrationParameters] = leftProjected;
                right.stereoProjectedPoints[(int)calibrationParameters] = rightProjected;

                left.stereoFrameRms[(int)calibrationParameters] = frameRms;
                left.stereoFrameMaxError[(int)calibrationParameters] = frameMax;

                right.stereoFrameRms[(int)calibrationParameters] = frameRms;
                right.stereoFrameMaxError[(int)calibrationParameters] = frameMax;

                allErrors.AddRange(errorsFrame);
            }
            

            if (allErrors.Count > 0)
            {
                calibrationStereoCameraData.ProjectionRMS = Math.Sqrt(allErrors.Sum(e => e * e) / allErrors.Count);
                calibrationStereoCameraData.MaxError = allErrors.Max();
            }
            else
            {
                calibrationStereoCameraData.ProjectionRMS = 0;
                calibrationStereoCameraData.MaxError = 0;
            }

            return calibrationStereoCameraData;
        }


        /// <summary>
        /// Calculate the yaw and pitch angles for each frame in the set,
        /// </summary>
        /// <returns></returns>
        public async Task CalculateFramesYawPitchAndPopulatePoseBinAsync(MonoCalibrationCameraData monoCalibrationLeft, 
                                                                         MonoCalibrationCameraData? monoCalibrationRight, 
                                                                         Windows.Foundation.Size frameSize)
        {
            if (chArUcoBoardDefinition is not null)
            {
                await Task.Run(() =>
                {
                    // EMGU.CV uses System.Drawing
                    Size frameSizeCorrectedType = new((int)frameSize.Width, (int)frameSize.Height);

                    // Parse the Frames
                    foreach (var (frameIndex, (left, right, _)) in Data.Frames)
                    {
                        if (left is not null)
                        {
                            CalcYawAndPitcAndWhichPoseBin(left, monoCalibrationLeft);

                            //???AddToThePoseBinTotals(left, Data.AllFramesPoseBinTotalsLeft);
                        }
                        if (right is not null && monoCalibrationRight is not null)
                        {
                            CalcYawAndPitcAndWhichPoseBin(right, monoCalibrationRight);

                            //???AddToThePoseBinTotals(right, Data.AllFramesPoseBinTotalsRight);
                        }
                    }
                });
            }

            void CalcYawAndPitcAndWhichPoseBin(FrameData frameCalibrationData, MonoCalibrationCameraData monoCalib)
            {
                if (chArUcoBoardDefinition is not null)
                {
                    if (frameCalibrationData.ChArUcoCorners.Length > 0 && frameCalibrationData.ChArUcoIds.Length >= 6/*min required for DLT calculation*/)
                    {
                        using var cornersVec = new VectorOfPointF(frameCalibrationData.ChArUcoCorners);
                        using var idsVec = new VectorOfInt(frameCalibrationData.ChArUcoIds);

                        var rvec = new Mat();
                        var tvec = new Mat();

                        bool success = ArucoInvoke.EstimatePoseCharucoBoard(cornersVec,
                                                                            idsVec,
                                                                            chArUcoBoardDefinition.Board,
                                                                            monoCalib.IntrinsicMatrix,
                                                                            monoCalib.DistortionCoeffs,
                                                                            rvec,
                                                                            tvec);

                        if (success && !rvec.IsEmpty)
                        {
                            var rotationMatrix = new Mat();
                            CvInvoke.Rodrigues(rvec, rotationMatrix);

                            var rotArr = new double[9];
                            using (var rotMat = new Matrix<double>(3, 3))
                            {
                                rotationMatrix.CopyTo(rotMat);
                                Buffer.BlockCopy(rotMat.Data, 0, rotArr, 0, rotArr.Length * sizeof(double));
                            }

                            double r00 = rotArr[0], r01 = rotArr[1], r02 = rotArr[2];
                            double r10 = rotArr[3], r11 = rotArr[4], r12 = rotArr[5];
                            double r20 = rotArr[6], r21 = rotArr[7], r22 = rotArr[8];

                            // Standard Euler angles from rotation matrix
                            double pitchRad = Math.Atan2(r21, r22);                          // board tilting forward/back (X-axis rotation)
                            double yawRad = Math.Atan2(-r20, Math.Sqrt(r00 * r00 + r10 * r10)); // board turning left/right (Y-axis rotation)


                            double yawDeg = Math.Round(yawRad * 180.0 / Math.PI, 1, MidpointRounding.AwayFromZero);
                            double pitchDeg = Math.Round(pitchRad * 180.0 / Math.PI, 1, MidpointRounding.AwayFromZero);

                            // Store the angles in the left frame
                            frameCalibrationData.YawDeg = yawDeg;
                            frameCalibrationData.PitchDeg = pitchDeg;

                            // Place correct pose bin
                            int yawBin = BinYawFromAngle(frameCalibrationData.YawDeg);
                            int pitchBin = BinPitchFromAngle(frameCalibrationData.PitchDeg);

                            frameCalibrationData.PoseBinX = yawBin;
                            frameCalibrationData.PoseBinY = pitchBin;
                        }
                    }
                    else
                    {
                        // Clear values
                        frameCalibrationData.YawDeg = 0;
                        frameCalibrationData.PitchDeg = 0;

                        frameCalibrationData.PoseBinX = -1;
                        frameCalibrationData.PoseBinY = -1;
                    }

                }
            }
        }


        /// <summary>
        /// Increments the total count for the pose bin corresponding to the specified frame data within the provided
        /// bin totals dictionary.
        /// </summary>
        /// <param name="target">The frame data whose pose bin coordinates are used to identify the bin to increment.</param>
        /// <param name="BinTotals">A dictionary mapping pose bin coordinates to their current totals. The count for the bin specified by
        /// <paramref name="target"/> is incremented by one.</param>
        private static void AddToThePoseBinTotals(FrameData target, Dictionary<(int binx, int biny), int> BinTotals)
        {
            BinTotals[(target.PoseBinX, target.PoseBinY)] = BinTotals.GetValueOrDefault((target.PoseBinX, target.PoseBinY)) + 1;
        }


        /// <summary>
        /// Increments the count for each sensor bin occupied in the specified frame within the provided bin totals
        /// dictionary.
        /// </summary>
        /// <remarks>If a bin from <paramref name="target"/> is not present in <paramref
        /// name="BinTotals"/>, it is added with a count of 1. This method modifies the <paramref name="BinTotals"/>
        /// dictionary in place.</remarks>
        /// <param name="target">The frame data containing the collection of sensor bins that are occupied.</param>
        /// <param name="BinTotals">A dictionary mapping sensor bin coordinates to their current totals. The method updates this dictionary by
        /// incrementing the count for each bin found in <paramref name="target"/>.</param>
        private static void AddToTheSensorBinTotals(FrameData target, Dictionary<(int binx, int biny), int> BinTotals)
        {
            foreach (var bin in target.SensorBinsOccupied)
            {
                BinTotals[bin] = BinTotals.GetValueOrDefault(bin) + 1;
            }
        }


        // Helper for binning an angle
        private static int BinYawFromAngle(double angle)
        {
            (int gx, _) = FrameData.PoseBinGrid;
            int ret = gx - 1;

            for (int i = 0; i < FrameData.PoseBinThresholdYaw.Count; i++)
            {
                if (angle <= FrameData.PoseBinThresholdYaw[i])
                    return i;
            }
            return ret;
        }
        private static int BinPitchFromAngle(double angle)
        {
            (_, int gy) = FrameData.PoseBinGrid;
            int ret = gy - 1;

            for (int i = 0; i < FrameData.PoseBinThresholdPitch.Count; i++)
            {
                if (angle <= FrameData.PoseBinThresholdPitch[i])
                    return i;
            }
            return ret;
        }


        /// <summary>
        /// Add to the existing best frame array 2 frames from each of the 
        /// pose bins to ensure pose diversity
        /// </summary>
        public (int added, int updated) AddBestFramesUsingPoseBins(double maxMovementFactor, double maxBlurFactor, int cornersMinThreshold, int maxFramePerBin)
        {
            int totalAdded = 0;
            int totalUpdated = 0;


            // Layer dimensions
            var (px, py) = FrameData.PoseBinGrid;
            
            List<int>? frameIndexes = null;

            for (int biny = 0; biny < py; biny++)
            {
                for (int binx = 0; binx < px; binx++)
                {
                    if (headTrueIsStereoFalseIsMode == false)
                    {
                        frameIndexes = [.. Data.Frames
                                         .Where(kvp =>
                                         {
                                             var (left, _, _) = kvp.Value;
                                             return /*left.ChArUcoCorners.Length >= cornersMinThreshold &&*/
                                                    left.PoseBinX == binx &&
                                                    left.PoseBinY == biny &&
                                                    left.MovementFactor <= maxMovementFactor &&
                                                    left.BlurFactor <= maxBlurFactor;
                                         })
                                         .OrderByDescending(kvp => kvp.Value.Item1.ChArUcoCorners.Length) // correspondingCount descending
                                         .ThenBy(kvp =>
                                         {
                                             var (left, _, _) = kvp.Value;
                                             return left.MovementFactor;
                                         })
                                         .ThenBy(kvp =>
                                         {
                                             var (left, _, _) = kvp.Value;
                                             return left.BlurFactor;
                                         })
                                         .Take(maxFramePerBin)
                                         .Select(kvp => kvp.Key)];

                        // Add to the mono best frames list only allowing unique to be
                        // indexes added and updating the reason in existing entries if
                        // needed
                        (int added, int updated) = AddBestFrames(frameIndexes, BestFrameReason.PoseDiversity);
                        totalAdded += added;
                        totalUpdated += updated;
                    }
                    else
                    {
                        int added;
                        int updated;

                        // Find diverse poses based on the left frame
                        frameIndexes = [.. Data.Frames
                                        .Where(kvp =>
                                        {
                                            var (left, right, correspondingCount) = kvp.Value;
                                            return correspondingCount >= cornersMinThreshold &&
                                                   left.PoseBinX == binx &&
                                                   left.PoseBinY == biny &&
                                                   left.MovementFactor <= maxMovementFactor &&
                                                   left.BlurFactor <= maxBlurFactor &&
                                                   right != null &&
                                                   right.MovementFactor <= maxMovementFactor &&
                                                   right.BlurFactor <= maxBlurFactor;
                                        })
                                        .OrderByDescending(kvp => kvp.Value.Item3) // correspondingCount descending
                                        .ThenBy(kvp =>
                                        {
                                            var (left, right, _) = kvp.Value;
                                            return right is null ? left.MovementFactor
                                                                 : Math.MaxMagnitude(left.MovementFactor, right.MovementFactor);
                                        })
                                        .ThenBy(kvp =>
                                        {
                                            var (left, right, _) = kvp.Value;
                                            return right is null ? left.BlurFactor
                                                                 : Math.MaxMagnitude(left.BlurFactor, right.BlurFactor);
                                        })
                                        .Take(maxFramePerBin / 2) /* Divide by 2 because each stereo pair adds 2 frames */
                                        .Select(kvp => kvp.Key)];

                        // Add to the left stereo best frames list only allowing unique to be
                        // indexes added and updating the reason in existing entries if
                        // needed
                        (added, updated) = AddBestFrames(frameIndexes, BestFrameReason.PoseDiversity);
                        totalAdded += added;
                        totalUpdated += updated;


                        // Find diverse poses based on the right frame
                        frameIndexes = [.. Data.Frames
                                            .Where(kvp =>
                                            {
                                                var (left, right, correspondingCount) = kvp.Value;
                                                return correspondingCount >= cornersMinThreshold &&
                                                       left.MovementFactor <= maxMovementFactor &&
                                                       left.BlurFactor <= maxBlurFactor &&
                                                       right != null &&
                                                       right.PoseBinX == binx &&
                                                       right.PoseBinY == biny &&
                                                       right.MovementFactor <= maxMovementFactor &&
                                                       right.BlurFactor <= maxBlurFactor;
                                            })
                                            .OrderByDescending(kvp => kvp.Value.Item3) // correspondingCount descending
                                            .ThenBy(kvp =>
                                            {
                                                var (left, right, _) = kvp.Value;
                                                return right is null ? left.MovementFactor
                                                                     : Math.MaxMagnitude(left.MovementFactor, right.MovementFactor);
                                            })
                                            .ThenBy(kvp =>
                                            {
                                                var (left, right, _) = kvp.Value;
                                                return right is null ? left.BlurFactor
                                                                     : Math.MaxMagnitude(left.BlurFactor, right.BlurFactor);
                                            })
                                            .Take(maxFramePerBin / 2) /* Divide by 2 because each stereo pair adds 2 frames */
                                            .Select(kvp => kvp.Key)];

                        // Add to the right stereo best frames list only allowing unique to be
                        // indexes added and updating the reason in existing entries if
                        // needed
                        (added, updated) = AddBestFrames(frameIndexes, BestFrameReason.PoseDiversity);
                        totalAdded += added;
                        totalUpdated += updated;
                    }
                }
            }
                    
            return (totalAdded, totalUpdated);
        }


        private static (CalibType, int distRowCount) GetCalibrationFlags(CalibrationParameters calibrationParameters)
        {
            CalibType flags = CalibType.Default;
            int distRowCount = 0;

            switch (calibrationParameters)
            {
                case CalibrationParameters.K1K2P1P2:
                    flags = CalibType.FixK3;
                    distRowCount = 5;   // k1,k2,p1,p2,k3(=0)
                    break;

                case CalibrationParameters.K1K2K3P1P2:
                    flags = CalibType.Default;
                    distRowCount = 5;   // k1,k2,k3,p1,p2
                    break;

                case CalibrationParameters.K1K2K3K4P1P2:
                    flags = CalibType.RationalModel | CalibType.FixK5 | CalibType.FixK6;
                    distRowCount = 14;  // k1..k4,p1,p2 plus extended slots (k5,k6,s1..s4,tauX,tauY) fixed/unused

                    break;               
                case CalibrationParameters.K1K2K3K4P1P2K5K6:
                    flags = CalibType.RationalModel;
                    distRowCount = 14;  // k1..k6,p1,p2 uses RationalModel
                    break;

                default:
                    throw new NotSupportedException($"Calibration Parameter {calibrationParameters} not implemented");
            }

            return (flags, distRowCount);
        }
        private (PointF[] leftProjected, PointF[] rightProjected) ValidateStereoProjectionReprojectionError(
                        int frameIndex,
                        MCvPoint3D32f[] objPts,
                        PointF[] imgPtsLeft,
                        PointF[] imgPtsRight,
                        Mat intrLeft, Mat distLeft,
                        Mat intrRight, Mat distRight,
                        Matrix<double> R, Matrix<double> T)
        {
            if (imgPtsLeft.Length == 0 || imgPtsRight.Length == 0)
                return ([], []);

            if (imgPtsLeft.Length != imgPtsRight.Length)
                throw new InvalidOperationException($"Stereo validation: point count mismatch frame={frameIndex} left={imgPtsLeft.Length} right={imgPtsRight.Length}");

            // Convert R/T to Mat for OpenCV ops
            using var Rmat = new Mat(3, 3, DepthType.Cv64F, 1);
            using var Tmat = new Mat(3, 1, DepthType.Cv64F, 1);
            R.Mat.CopyTo(Rmat);
            T.Mat.CopyTo(Tmat);

            // Undistort to normalized coordinates (P = null => normalized)
            using var vecLeft = new VectorOfPointF(imgPtsLeft);
            using var vecRight = new VectorOfPointF(imgPtsRight);
            using var undistLeft = new VectorOfPointF();
            using var undistRight = new VectorOfPointF();

            CvInvoke.UndistortPoints(vecLeft, undistLeft, intrLeft, distLeft, null, null);
            CvInvoke.UndistortPoints(vecRight, undistRight, intrRight, distRight, null, null);

            // Build projection matrices for normalized coordinates:
            // P1 = [I|0], P2 = [R|T]   (both 3x4)
            using var P1 = new Mat(3, 4, DepthType.Cv64F, 1);
            using var P2 = new Mat(3, 4, DepthType.Cv64F, 1);
            P1.SetTo(new MCvScalar(0));
            P2.SetTo(new MCvScalar(0));

            // P1 left 3x3 = Identity
            using (var I = Mat.Eye(3, 3, DepthType.Cv64F, 1))
            using (var left33 = new Mat(P1, new Rectangle(0, 0, 3, 3)))
                I.CopyTo(left33);

            // P2 left 3x3 = R
            using (var right33 = new Mat(P2, new Rectangle(0, 0, 3, 3)))
                Rmat.CopyTo(right33);

            // P2 right column = T
            using (var rightCol = new Mat(P2, new Rectangle(3, 0, 1, 3)))
                Tmat.CopyTo(rightCol);

            // Triangulate -> homogeneous 4D points (4 x N)
            using var points4D = new Mat();
            CvInvoke.TriangulatePoints(P1, P2, undistLeft, undistRight, points4D);

            // Convert 4D homogeneous -> 3D points in left camera coordinates
            var objectPointsTriangulated = new List<MCvPoint3D32f>(imgPtsLeft.Length);
            using var ptsMat = new Matrix<float>(points4D.Rows, points4D.Cols);
            points4D.CopyTo(ptsMat);

            for (int j = 0; j < ptsMat.Cols; j++)
            {
                float w = ptsMat[3, j];
                if (Math.Abs(w) < 1e-12f)
                    continue;

                objectPointsTriangulated.Add(new MCvPoint3D32f(
                    ptsMat[0, j] / w,
                    ptsMat[1, j] / w,
                    ptsMat[2, j] / w));
            }

            // Reproject into both images in pixel coordinates
            var leftProj = CvInvoke.ProjectPoints(
                [.. objectPointsTriangulated],
                new Matrix<double>(3, 1), // rvecs = 0
                new Matrix<double>(3, 1), // tvecs = 0
                intrLeft,
                distLeft);

            var rightProj = CvInvoke.ProjectPoints(
                [.. objectPointsTriangulated],
                R,
                T,
                intrRight,
                distRight);

            // Compute reprojection errors
            double totalErrorLeft = 0.0;
            double totalErrorRight = 0.0;
            int n = Math.Min(imgPtsLeft.Length, objectPointsTriangulated.Count);

            for (int j = 0; j < n; j++)
            {
                double errL = Math.Sqrt(Math.Pow(imgPtsLeft[j].X - leftProj[j].X, 2) + Math.Pow(imgPtsLeft[j].Y - leftProj[j].Y, 2));
                double errR = Math.Sqrt(Math.Pow(imgPtsRight[j].X - rightProj[j].X, 2) + Math.Pow(imgPtsRight[j].Y - rightProj[j].Y, 2));
                totalErrorLeft += errL;
                totalErrorRight += errR;
            }

            //if (n > 0)
            //{
            //    Debug.WriteLine($"[Stereo Validation] Frame {frameIndex}: Avg re-projection error L={totalErrorLeft / n:F3}px R={totalErrorRight / n:F3}px");
            //}

            return (leftProj, rightProj);
        }
        /*** End of CalibrationStereoFrameSet ***/
    }


    //???public class TupleInt2JsonConverter : JsonConverter
    //{
    //    public override bool CanConvert(Type objectType)
    //    {
    //        return objectType == typeof(Dictionary<(int, int), int>);
    //    }

    //    [RequiresUnreferencedCode("ReadJson uses Json.NET serialization which may not be compatible with trimming.")]
    //    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    //    {
    //        var result = new Dictionary<(int, int), int>();
    //        var obj = JObject.Load(reader);

    //        foreach (var prop in obj.Properties())
    //        {
    //            // Parse string key: "(6, 4)"
    //            var keyString = prop.Name.Trim('(', ')');
    //            var parts = keyString.Split(',');

    //            if (parts.Length == 4 &&
    //                int.TryParse(parts[0], out int a) &&
    //                int.TryParse(parts[1], out int b))
    //            {
    //                var key = (a, b);
    //                var value = prop.Value.ToObject<int>();
    //                result[key] = value;
    //            }
    //        }

    //        return result;
    //    }

    //    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    //    {
    //        var dict = value as Dictionary<(int, int), int>;
    //        if (dict == null)
    //        {
    //            writer.WriteNull();
    //            return;
    //        }

    //        writer.WriteStartObject();
    //        foreach (var kvp in dict)
    //        {
    //            string key = $"({kvp.Key.Item1}, {kvp.Key.Item2})";
    //            writer.WritePropertyName(key);
    //            writer.WriteValue(kvp.Value);
    //        }
    //        writer.WriteEndObject();
    //    }

    //    /*** End of TupleInt2JsonConverter ***/
    //}

    
    /// <summary>
    /// Let or right camera calibration data.
    /// </summary>
    public class MonoCalibrationCameraData
    {
        public CalibrationParameters CalibrationParameters { get; set; }
        public Matrix<double>? IntrinsicMatrix { get; set; }
        public Matrix<double>? DistortionCoeffs { get; set; }
        public int ImageTotal { get; set; }
        public int ImagesUsed{ get; set; }
        public double ReprojectionRMS { get; set; }     // Re-projection RMS Error (RPE RMS)
                                                        // Definition: Root mean square of distances between observed and projected image points.
                                                        // < 0.2    Excellent(usually only in studio/lab with perfect lighting and corner visibility)
                                                        // 0.2–0.5  Very good; suitable for accurate 3D reconstructions and pose estimates
                                                        // 0.5–1.0  Acceptable for many real-world use cases, especially underwater, drone, etc.
                                                        // > 1.0    Often indicates blur, motion, poor corner detection, or bad coverage
        public double ProjectionRMS { get; set; }       // Projection RMS (Point-Level RMS Error)
                                                        // Definition: RMS error calculated per projected point, averaged across all points and frames.
                                                        // ≤ 0.20px     Excellent — very accurate calibration, typically achievable with high-res sensors and clean data.
                                                        // 0.20–0.50px  Good — typical for well-done calibrations on decent setups.
                                                        // 0.50–1.00px  Acceptable — usable, but may indicate imperfect board detection or slightly noisy images.
                                                        // > 1.00px     Poor — indicates significant error in detections or insufficient coverage/angles in calibration images.
                                                        // Contextual Notes:
                                                        // Lower is always better, but diminishing returns apply below ~0.2 px.
                                                        // On high-res cameras(e.g., 4K), even 0.5 px is a very small angular deviation.
                                                        // If you’re planning accurate triangulation(e.g.fish measurement from stereo), staying under 0.5 px helps ensure depth precision.
                                                        
        public double MaxError { get; set; }            // Maximum Error
                                                        // Definition: Maximum Euclidean distance between an observed 2D point and its re-projection.
                                                        // ≤ 0.50px     Excellent — very tight calibration with no significant outliers.
                                                        // 0.5–1.0px    Good — minor outliers, acceptable for most applications.
                                                        // 1.0–2.0px    Acceptable — some frames may have off detections or bad coverage.
                                                        // > 2.0px      Poor — likely issues with blurred frames, incorrect detections, or too few diverse poses.
                                                        // Contextual Considerations
                                                        // High max error doesn't always mean the calibration is bad, but it does indicate a possible weak frame.
                                                        // If your Re-projection RMS error is good(~0.3–0.4 px) but your max error is >2 px, consider reviewing:
                                                        //    - Frame sharpness
                                                        //    - Angle coverage of the calibration board
                                                        //    - Board detection quality(false or partial matches)
                                                        // It’s common to filter out worst frames(e.g., >2 px error) after an initial calibration round to improve a second pass.
    }
}

