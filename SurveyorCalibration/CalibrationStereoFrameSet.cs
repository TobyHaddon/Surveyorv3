using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace Surveyor.Calibration
{
    /// <summary>
    /// A CalibrationStereoFrameSet instance holds all the extracted calibration frames metadata (FrameCalibrationTarget)
    /// in a sorted directory called 'Frames'.
    /// </summary>
    public class CalibrationStereoFrameSet
    {
        // Version of the class (use for data migrations)
        private const int version = 2;
        // Data Version
        public int Version { get; set; } = -1;

        // Lock point
        [JsonProperty(nameof(LockFrameIndexLeft))]
        public int LockFrameIndexLeft = -1;

        [JsonProperty(nameof(LockFrameIndexRight))]
        public int LockFrameIndexRight = -1;

        // A sorted dictionary of frames, sorted by frame index that holds the calibration
        // board corners and ids, the blur factor and the movement factor
        [JsonProperty(nameof(Frames))]
        public SortedDictionary<int, (FrameCalibrationData frameCalibrationTargetLeft, FrameCalibrationData? frameCalibrationTargetRight, int correspondingCount)> Frames { get; set; } = [];

        [JsonProperty(nameof(BestFrameIndexes))]
        public List<int> BestFrameIndexes = [];

        // A dictionary of sensor bin totals, where the key is a tuple of (gx, gy, binx, biny)
        // this is updated as frames are added or removed from the set.
        // This dictionary is persistied to JSON 
        [JsonProperty(nameof(SensorBinTotalsLeft))]
        [TypeConverter(typeof(TupleInt4JsonConverter))]
        public Dictionary<(int gx, int gy, int binx, int biny), int> SensorBinTotalsLeft = [];

        // A dictionary of sensor bin totals, where the key is a tuple of (gx, gy, binx, biny)
        // this is updated as frames are added or removed from the set.
        // This dictionary is persistied to JSON 
        [JsonProperty(nameof(SensorBinTotalsRight))]
        [TypeConverter(typeof(TupleInt4JsonConverter))]
        public Dictionary<(int gx, int gy, int binx, int biny), int> SensorBinTotalsRight = [];

        // A dictionary of the left pose bin totals, where the key is a tuple of (binx, biny)
        [JsonProperty(nameof(PoseBinTotalsLeft))]
        [TypeConverter(typeof(TupleInt2JsonConverter))]
        public Dictionary<(int binx, int biny), int> PoseBinTotalsLeft { get; set; } = [];

        // A dictionary of the right pose bin totals, where the key is a tuple of (binx, biny)
        [JsonProperty(nameof(PoseBinTotalsRight))]
        [TypeConverter(typeof(TupleInt2JsonConverter))]
        public Dictionary<(int binx, int biny), int> PoseBinTotalsRight { get; set; } = [];

        public const double BLUR_LARGEVALUE = 10.0;
        public const double MOVEMENT_LARGEVALUE = 400.0;

        public const int MONO_CORNER_COUNT_THESHOLD = 80;
        public const int STEREO_CORNER_COUNT_THESHOLD = 50;


        /// 
        /// DYNAMIC variables
        ///         
        [JsonIgnore]
        private VideoCapture? leftCapture = null;

        [JsonIgnore]
        private VideoCapture? rightCapture = null;

        // Target calibration board setup
        [JsonIgnore]
        private CharucoBoardDefinition? charucoBoardDefinition;


        // Total frame count
        [JsonIgnore]
        private int totalFramesLeft = -1;
        [JsonIgnore]
        private int totalFramesRight = -1;

        public CalibrationStereoFrameSet()
        {
            // Set the Version
            Version = version;

        }

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
        public bool SetupCalibrationBoardType(CharucoBoardDefinition _charucoBoardDefinition)
        {
            charucoBoardDefinition = _charucoBoardDefinition;

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
        public double MaxMovementFactor => Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.MovementFactor >= 0)
                .Select(f => f!.MovementFactor)
                .DefaultIfEmpty(0) // Prevents exception if filtered list is empty
                .Max();

        public double MinMovementFactor => Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.MovementFactor >= 0)
                .Select(f => f!.MovementFactor)
                .DefaultIfEmpty(0) // Prevents exception if filtered list is empty
                .Min();


        /// <summary>
        /// Returns the maximum BlurFactor across all frames in the set.
        /// </summary>
        public double MaxBlurFactor => Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.BlurFactor != double.MaxValue)
                .Select(f => f!.BlurFactor)
                .DefaultIfEmpty(0) // or any appropriate fallback
                .Max();

        public double MinBlurFactor => Frames
                .SelectMany(pair => new[] { pair.Value.frameCalibrationTargetLeft, pair.Value.frameCalibrationTargetRight })
                .Where(f => f is not null && f.BlurFactor != double.MaxValue)
                .Select(f => f!.BlurFactor)
                .DefaultIfEmpty(0) // or any appropriate fallback
                .Min();


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
        /// 
        /// </summary>
        public double MaxBestMovementFactor => BestFrameIndexes
                            .SelectMany(index =>
                            {
                                if (!Frames.TryGetValue(index, out var pair))
                                    return Enumerable.Empty<FrameCalibrationData>();

                                return new[] { pair.frameCalibrationTargetLeft, pair.frameCalibrationTargetRight };
                            })
                            .Where(f => f is not null && f.MovementFactor >= 0)
                            .Select(f => f!.MovementFactor)
                            .DefaultIfEmpty(0)
                            .Max();
        public double MinBestMovementFactor => BestFrameIndexes
                    .SelectMany(index =>
                    {
                        if (!Frames.TryGetValue(index, out var pair))
                            return Enumerable.Empty<FrameCalibrationData>();

                        return new[] { pair.frameCalibrationTargetLeft, pair.frameCalibrationTargetRight };
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
        public virtual int AddFrame(int stereoFrameIndex, FrameCalibrationData frameLeft, FrameCalibrationData? frameRight)
        {
            if (frameLeft.CharucoCorners == null || frameLeft.CharucoCorners.Length == 0)
                return -1;

            if (frameRight is not null && (frameRight.CharucoCorners == null || frameRight.CharucoCorners.Length == 0))
                return -1;

            // If stereo calc the corresponding count (number of markers that are the same on both the left and right side)
            int correspondingCount = 0;

            if (frameRight is not null)
            {
                var leftIds = frameLeft.CharucoIds;
                var rightIds = frameRight.CharucoIds;

                correspondingCount = leftIds.Intersect(rightIds).Count();
            }

            Frames[stereoFrameIndex] = (frameLeft, frameRight, correspondingCount);

            // If there is a prior and/or next continious frame, calculate the movement
            // from this frame to those previous frames (note values in all three frames
            // maybe updated
            CalculateCornerMovement(stereoFrameIndex);

            // Update the sensor bin totals
            AddToTheSensorBinTotals(frameLeft, SensorBinTotalsLeft);
            if (frameRight is not null)
                AddToTheSensorBinTotals((FrameCalibrationData)frameRight, SensorBinTotalsRight);

            // Helper
            static void AddToTheSensorBinTotals(FrameCalibrationData target, Dictionary<(int gx, int gy, int binx, int biny), int> BinTotals)
            {
                foreach (var bin in target.SensorBinsOccupied)
                {
                    BinTotals[bin] = BinTotals.GetValueOrDefault(bin) + 1;
                }
            }

            return correspondingCount;
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
                (FrameCalibrationData leftTarget, FrameCalibrationData? rightTarget, _) = Frames[stereoFrameIndex];

                // Remove the bins from the bin totals
                RemoveFromTheBinTotals(leftTarget, SensorBinTotalsLeft);
                if (rightTarget is not null)
                    RemoveFromTheBinTotals(rightTarget, SensorBinTotalsRight);

                // Helper
                static void RemoveFromTheBinTotals(FrameCalibrationData target, Dictionary<(int gx, int gy, int binx, int biny), int> BinTotals)
                {
                    foreach (var bin in target.SensorBinsOccupied)
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
                        (FrameCalibrationData leftTargetPrevious, FrameCalibrationData? rightTargetPrevious, _) = Frames[stereoFrameIndex - 1];

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
                        (FrameCalibrationData leftTargetNext, FrameCalibrationData? rightTargetNext, _) = Frames[stereoFrameIndex + 1];

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
        public bool SelectBestStereoFramesUsingSensorBinOnly(double maxMovementFactor, double maxBlurFactor)
        {
            HashSet<int> frameIndexSet = [];

            bool mono = false;
            List<int> frameIndexes;

            // Check if mono or stereo by checking for null right values
            mono = !Frames.TryGetValue(Frames.Keys.FirstOrDefault(), out var tuple) || tuple.Item2 is null;


            foreach (var (gx, gy) in FrameCalibrationData.SensorBinGridLayers)
            {
                for (int biny = 0; biny < gy; biny++)
                {
                    for (int binx = 0; binx < gx; binx++)
                    {
                        var targetBin = (gx, gy, binx, biny);

                        //var frameIndexes = Frames.Values
                        //    .Where(pair =>
                        //        pair.frameCalibrationTargetLeft.SensorBinsOccupied.Contains(targetBin) &&
                        //        pair.frameCalibrationTargetLeft.MovementFactor >= 0 &&
                        //        (pair.frameCalibrationTargetRight == null || pair.frameCalibrationTargetRight.MovementFactor >= 0)
                        //    )
                        //    .OrderBy(pair =>
                        //    {
                        //        double leftMove = pair.frameCalibrationTargetLeft.MovementFactor;
                        //        double rightMove = pair.frameCalibrationTargetRight?.MovementFactor ?? leftMove;

                        //        return (leftMove + rightMove) / 2.0;
                        //    })
                        //    .ThenBy(pair =>
                        //    {
                        //        double leftBlur = pair.frameCalibrationTargetLeft.BlurFactor;
                        //        double rightBlur = pair.frameCalibrationTargetRight?.BlurFactor ?? leftBlur;

                        //        return (leftBlur + rightBlur) / 2.0;
                        //    })
                        //    .Take(2)
                        //    .Select(pair => pair.frameCalibrationTargetLeft.FrameIndex);

                        if (mono)
                        {
                            frameIndexes = Frames
                                             .Where(kvp =>
                                             {
                                                 var (left, _, _) = kvp.Value;
                                                 return left.CharucoCorners.Length > 50 &&
                                                        left.SensorBinsOccupied.Contains(targetBin) &&
                                                        left.MovementFactor <= maxMovementFactor &&
                                                        left.BlurFactor <= maxBlurFactor;
                                             })
                                             .OrderByDescending(kvp => kvp.Value.Item1.CharucoCorners.Length) // correspondingCount descending
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
                                             .Take(2)
                                             .Select(kvp => kvp.Key)
                                             .ToList(); ;
                        }
                        else
                        {
                            frameIndexes = Frames
                                            .Where(kvp =>
                                            {
                                                var (left, right, correspondingCount) = kvp.Value;
                                                return correspondingCount > 50 &&
                                                       left.SensorBinsOccupied.Contains(targetBin) &&
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
                                            .Take(2)
                                            .Select(kvp => kvp.Key)
                                            .ToList(); ;
                        }

                        foreach (var index in frameIndexes)
                            frameIndexSet.Add(index);
                    }
                }
            }

            BestFrameIndexes = frameIndexSet.ToList();
            return true;
        }


        /// <summary>
        /// Get the sensor bin counts for a given grid layer (gx, gy) and bin (binx, biny).
        /// </summary>
        /// <param name="gx"></param>
        /// <param name="gy"></param>
        /// <returns></returns>
        public Dictionary<(int gx, int gy, int binx, int biny), int> GetSensorBinCounts(bool trueLeftFalseRight, int gx, int gy)
        {
            var counts = new Dictionary<(int gx, int gy, int binx, int biny), int>();

            Dictionary<(int gx, int gy, int binx, int biny), int> BinTotals;

            if (trueLeftFalseRight)
            {
                BinTotals = SensorBinTotalsLeft;
            }
            else
            {
                BinTotals = SensorBinTotalsRight;
            }

            foreach ((FrameCalibrationData leftTarget, FrameCalibrationData? rightTarget, _) in Frames.Values)
            {
                FrameCalibrationData? target;

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
                    foreach (var bin in target.SensorBinsOccupied)
                    {
                        // Find the this bin in the counts list, if not found create an new entry in counts
                        counts[bin] = counts.GetValueOrDefault(bin) + 1;
                    }

                }
            }

            return counts;
        }


        /// <summary>
        /// Get the pose bin counts.
        /// </summary>
        /// <param name="gx"></param>
        /// <param name="gy"></param>
        /// <returns></returns>
        public Dictionary<(int binx, int biny), int> GetPoseBinCounts(bool trueLeftFalseRight)
        {
            var counts = new Dictionary<(int binx, int biny), int>();

            Dictionary<(int binx, int biny), int> BinTotals;

            if (trueLeftFalseRight)
            {
                BinTotals = PoseBinTotalsLeft;
            }
            else
            {
                BinTotals = PoseBinTotalsRight;
            }

            foreach ((FrameCalibrationData leftTarget, FrameCalibrationData? rightTarget, _) in Frames.Values)
            {
                FrameCalibrationData? target;

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
                    foreach (var bin in target.PoseBinsOccupied)
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
            CalibrationStereoFrameSet? calibrationStereoFrameSet = null;

            try
            {
                var json = File.ReadAllText(path);
                if (json is not null)
                {
                    try
                    {
                        var settings = new JsonSerializerSettings
                        {
                            Converters = { new TupleInt4JsonConverter(), new TupleInt2JsonConverter() }
                        };

                        calibrationStereoFrameSet = JsonConvert.DeserializeObject<CalibrationStereoFrameSet>(json, settings);

                        if (calibrationStereoFrameSet is not null)
                        {
                            // Check the FrameCalibrationTarget Version used in the Frames dictionary
                            FrameCalibrationData? firstFrameCalibrationTarget = null;

                            if (calibrationStereoFrameSet.Frames.Count > 0)
                            {
                                // Get the verison of the FrameCalibrationTarget Frame in the Frames list
                                (_,(firstFrameCalibrationTarget, _, _)) = calibrationStereoFrameSet.Frames.First();
                            }

                            // Recalc values flag
                            bool recalcCalibrationStereoFrameSet = false;
                            bool recalcFramesDictionary = false;

                            // Migrations Section
                            if (calibrationStereoFrameSet.Version == -1/*From version*/)
                            {
                                // No Migrations action just set the version number to latest
                                calibrationStereoFrameSet.Version = (new CalibrationStereoFrameSet()).Version;
                            }
                            // Furture migrations
                            //else if (calibrationStereoFrameSet.Version == ??)
                            //{
                            //}

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
                                int frameCalibrationTargetLastestVersion = (new FrameCalibrationData()).Version;
                                foreach (var frame in calibrationStereoFrameSet.Frames)
                                {
                                    (FrameCalibrationData frameCalibrationTargetLeft, FrameCalibrationData? frameCalibrationTargetRight, int correspondingCount) = frame.Value;

                                    frameCalibrationTargetLeft.Version = frameCalibrationTargetLastestVersion;
                                    if (frameCalibrationTargetRight is not null)
                                        frameCalibrationTargetRight.Version = frameCalibrationTargetLastestVersion;
                                }
                            }
                        }
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

            return calibrationStereoFrameSet;
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
                FrameCalibrationData? targetLeft = null;
                FrameCalibrationData? targetRight = null;

                if (matLeft is not null && !matLeft.IsEmpty)
                {
                    targetLeft = DetectAndCreateFrameCalibrationTarget(true/*leftTrueRightFalse*/,
                                                                       leftFrameIndex, matLeft);
                    if (targetLeft is not null)
                        DrawMarkersToMat(targetLeft, matLeft);
                }

                if (trueStereoFalseSoloLeft && matRight is not null && !matRight.IsEmpty)
                {
                    targetRight = DetectAndCreateFrameCalibrationTarget(false/*leftTrueRightFalse*/,
                                                                       rightFrameIndex, matRight);

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
                            FrameCalibrationData? leftFrameCalibrationTarget,
                            int rightFrameIndex,
                            Mat? rightMat,
                            FrameCalibrationData? rightFrameCalibrationTarget,
                            int correspondingCount,
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
                FrameCalibrationData? targetLeft = null;
                FrameCalibrationData? targetRight = null;

                if (matLeft is not null && !matLeft.IsEmpty)
                {
                    targetLeft = DetectAndCreateFrameCalibrationTarget(true/*leftTrueRightFalse*/, leftFrameIndex, matLeft);

                    if (targetLeft is not null)
                        DrawMarkersToMat(targetLeft, matLeft);
                }

                if (trueStereoFalseSoloLeft && matRight is not null && !matRight.IsEmpty)
                {
                    targetRight = DetectAndCreateFrameCalibrationTarget(false/*leftTrueRightFalse*/, rightFrameIndex, matRight);
                    if (targetRight is not null)
                    {
                        DrawMarkersToMat(targetRight, matRight);
                    }
                }

                // Process result
                int correspondingCount = 0;
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
                            correspondingCount = AddFrame(frameIndex, targetLeft, null);
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
                    Converters = { new TupleInt4JsonConverter(), new TupleInt2JsonConverter() }
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
            (FrameCalibrationData leftTarget, FrameCalibrationData? rightTarget, _) = Frames[stereoFrameIndex];

            // Is there a previous contiguious frame?
            if (Frames.ContainsKey(stereoFrameIndex - 1))
            {
                // Get pair for stereoFrameIndex - 1
                (FrameCalibrationData leftTargetPrevious, FrameCalibrationData? rightTargetPrevious, _) = Frames[stereoFrameIndex - 1];

                // Movement from this left frame to the previous left frame
                double leftMovement = FrameCalibrationData.CalculateCornerMovement(leftTarget, leftTargetPrevious);

                leftTarget.MovementFromPrevious = leftMovement;
                leftTargetPrevious.MovementToNext = leftMovement;

                // Check the right frame if it exists
                if (rightTarget is not null && rightTargetPrevious is not null)
                {
                    // Movement from this right frame to the previous right frame
                    double rightMovement = FrameCalibrationData.CalculateCornerMovement(rightTarget, rightTargetPrevious);
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
                (FrameCalibrationData leftTargetNext, FrameCalibrationData? rightTargetNext, _) = Frames[stereoFrameIndex + 1];

                // Movement from this left frame to the next left frame
                double leftMovement = FrameCalibrationData.CalculateCornerMovement(leftTarget, leftTargetNext);

                leftTarget.MovementFromPrevious = leftMovement;
                leftTargetNext.MovementToNext = leftMovement;

                // Check the right frame if it exists
                if (rightTarget is not null && rightTargetNext is not null)
                {
                    // Movement from this right frame to the next right frame
                    double rightMovement = FrameCalibrationData.CalculateCornerMovement(rightTarget, rightTargetNext);
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

                // Emgu: use Set with CapProp
                cap!.Set(CapProp.PosFrames, frameIndex);

                mat = new Mat();
                cap.Read(mat);
            }

            return mat;
        }


        /// <summary>
        /// Detect the Charuco calibration board in the passed image
        /// </summary>
        /// <param name="trueLeftfalseRight"></param>
        /// <param name="frameIndex"></param>
        /// <param name="frame"></param>
        /// <returns></returns>
        private FrameCalibrationData? DetectAndCreateFrameCalibrationTarget(bool trueLeftfalseRight, int frameIndex, Mat frame)
        {
            FrameCalibrationData? ret = null;

            if (charucoBoardDefinition is not null)
            {
                try
                {

                    // Convert to grayscale for detection
                    using var gray = new Mat();
                    CvInvoke.CvtColor(frame, gray, ColorConversion.Bgr2Gray);

                    // Detect ArUco markers
                    using var markerCorners = new VectorOfVectorOfPointF();
                    using var markerIds = new VectorOfInt();
                    var parameters = DetectorParameters.GetDefault();

                    ArucoInvoke.DetectMarkers(gray, charucoBoardDefinition.Dictionary, markerCorners, markerIds, parameters);


                    // Interpolate ChArUco corners
                    using var charucoCorners = new Mat();
                    using var charucoIds = new Emgu.CV.Util.VectorOfInt();

                    if (markerIds.Size > 0)
                    {
                        // Optional: Refine marker corners to subpixel accuracy
                        for (int i = 0; i < markerCorners.Size; i++)
                        {
                            using var singleMarker = markerCorners[i]; // Access each marker's corner set
                            CvInvoke.CornerSubPix(
                                gray,
                                singleMarker,
                                new System.Drawing.Size(3, 3),    // Search window size
                                new System.Drawing.Size(-1, -1),  // No dead zone
                                new MCvTermCriteria(30, 0.01)
                            );
                        }

                        // Converts detected marker corners + IDs into interpolated Charuco corners.
                        ArucoInvoke.InterpolateCornersCharuco(
                            markerCorners,
                            markerIds,
                            gray,
                            charucoBoardDefinition.Board,
                            charucoCorners,
                            charucoIds
                        );

                        //???Debug.WriteLine($"Frame:{frameIndex} Detected {charucoIds.Size} ChArUco corners");


                        // Convert detected Charuco corners to managed types
                        var managedCorners = new System.Drawing.PointF[charucoCorners.Rows];
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
        private int DrawMarkersToMat(FrameCalibrationData frameCalibrationTarget, Mat frame)
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
                System.Drawing.PointF boardCentre = frameCalibrationTarget.Center;
                int radius = 40;
                MCvScalar color = new(0, 255, 0); // Green (B, G, R)
                int thickness = 20;

                // Draw the circle on the Mat
                CvInvoke.Circle(frame, new System.Drawing.Point((int)boardCentre.X, (int)boardCentre.Y), radius, color, thickness);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DrawMarkersToMat: Error processing ChArUco board: {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// Performs a mono calibration using the best frames found in the Frames dictionary 
        /// on both the left and right side (if the right side is active)
        /// </summary>
        /// <returns></returns>
        public (MonoCalibrationCameraData? left, MonoCalibrationCameraData? right) MonoCalibrateUsingBestFrames(Size frameSize)
        {
            MonoCalibrationCameraData? monoCalibLeft = null;
            MonoCalibrationCameraData? monoCalibRight = null;

            if (charucoBoardDefinition is not null)
            {
                monoCalibLeft = MonoCalibrateUsingBestFrames(true/*trueLeftFalseRight*/, frameSize, MONO_CORNER_COUNT_THESHOLD);

                if (monoCalibLeft is not null)
                {
                    // Check if right side is active
                    if (rightCapture is not null)
                    {
                        monoCalibRight = MonoCalibrateUsingBestFrames(false/*trueLeftFalseRight*/, frameSize, MONO_CORNER_COUNT_THESHOLD);
                    }
                }
            }
            return (monoCalibLeft, monoCalibRight);
        }


        /// <summary>
        /// Performs a mono calilbration on the indicated side
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="frameSize"></param>
        /// <returns></returns>
        //private MonoCalibrationCameraData? MonoCalibrateUsingBestFrames(bool trueLeftFalseRight, Size frameSize)
        //{
        //    MonoCalibrationCameraData? monoCalibrationCameraData = null;

        //    if (charucoBoardDefinition is not null)
        //    {
        //        var allCharucoCorners = new VectorOfVectorOfPointF();
        //        var allCharucoIds = new VectorOfVectorOfInt();

        //        var allObserved = new List<System.Drawing.PointF>();
        //        var allProjected = new List<System.Drawing.PointF>();

        //        var contributingFrameIndexes = new List<int>();


        //        foreach (var frameIndex in BestFrameIndexes)
        //        {
        //            if (!Frames.TryGetValue(frameIndex, out var framePair))
        //                continue;

        //            FrameCalibrationData? calibrationData = null;
        //            if (trueLeftFalseRight) 
        //                calibrationData = framePair.frameCalibrationTargetLeft;
        //            else if (framePair.frameCalibrationTargetRight is not null)
        //                calibrationData = framePair.frameCalibrationTargetRight;

        //            if (calibrationData is null || calibrationData.CharucoCorners.Length == 0 || calibrationData.CharucoIds.Length == 0)
        //                continue;

        //            if (calibrationData.CharucoCorners.Length < 80)
        //                continue;

        //            contributingFrameIndexes.Add(frameIndex);
        //            allCharucoCorners.Push(new VectorOfPointF(calibrationData.CharucoCorners));
        //            allCharucoIds.Push(new VectorOfInt(calibrationData.CharucoIds));
        //        }


        //        if (allCharucoCorners.Size == 0)
        //        {
        //            Debug.WriteLine("No valid Charuco data found in best frames.");
        //            return null;
        //        }


        //        using var cameraMatrix = new Mat();
        //        using var distCoeffs = new Mat();
        //        using var rvecs = new VectorOfMat();
        //        using var tvecs = new VectorOfMat();

        //        System.Drawing.Size frameSizeCorrectedType = new((int)frameSize.Width, (int)frameSize.Height);
        //        var intrinsicMatrix = new Matrix<double>(3, 3);
        //        var distortionCoeffs = new Matrix<double>(1, 5);
        //        double reprojectionError;

        //        //??DEBUG limit TEST
        //        //foreach (var frameIndex in BestFrameIndexes)
        //        //{
        //        //    var (left, _) = Frames[frameIndex];
        //        //    Debug.WriteLine($"Frame {frameIndex}: {left.CharucoCorners.Length} corners");
        //        //}
        //        //??DEBUG limit TEST

        //        try
        //        {
        //            reprojectionError = ArucoInvoke.CalibrateCameraCharuco(
        //                    allCharucoCorners,
        //                    allCharucoIds,
        //                    charucoBoardDefinition.Board,
        //                    frameSizeCorrectedType,
        //                    cameraMatrix,
        //                    distCoeffs,
        //                    rvecs,
        //                    tvecs,
        //                    CalibType.Default,
        //                    new MCvTermCriteria(30, 1e-6));

        //            // Extract matrix for reprojection
        //            cameraMatrix.CopyTo(intrinsicMatrix);
        //            distCoeffs.CopyTo(distortionCoeffs);
        //        }
        //        catch (Exception ex)
        //        {
        //            Debug.WriteLine($"Mono calibration failed: {ex.Message}");
        //            return null;
        //        }

        //        // Compute projection errors
        //        for (int i = 0; i < allCharucoIds.Size; i++)
        //        {
        //            var ids = allCharucoIds[i].ToArray();
        //            var corners = allCharucoCorners[i].ToArray();
        //            if (ids.Length < 1 || corners.Length < 1) continue;

        //            // For each detected charuco corner, get the corresponding 3D object point
        //            var objectPoints = new List<MCvPoint3D32f>();
        //            var filteredCorners = new List<System.Drawing.PointF>();
        //            var charucoCorner3DMap = GetCharucoCorner3DPoints(charucoBoardDefinition);

        //            for (int j = 0; j < ids.Length; j++)
        //            {
        //                int id = ids[j];
        //                if (!charucoCorner3DMap.TryGetValue(id, out MCvPoint3D32f point3D))
        //                    continue;

        //                objectPoints.Add(point3D);
        //                filteredCorners.Add(corners[j]); // Only keep aligned 2D points
        //            }

        //            // Project those 3D points to 2D image points using estimated camera parameters
        //            var projectedPoints = CvInvoke.ProjectPoints(
        //                objectPoints.ToArray(),
        //                rvecs[i],
        //                tvecs[i],
        //                intrinsicMatrix,
        //                distortionCoeffs);

        //            allObserved.AddRange(filteredCorners);
        //            allProjected.AddRange(projectedPoints);


        //            // Add this block to compute and print per-frame error:
        //            var errors = filteredCorners.Zip(projectedPoints, (o, p) =>
        //                Math.Sqrt(Math.Pow(o.X - p.X, 2) + Math.Pow(o.Y - p.Y, 2))).ToList();

        //            double frameRms = errors.Count > 0 ? Math.Sqrt(errors.Sum(e => e * e) / errors.Count) : 0.0;
        //            double frameMax = errors.Max();

        //            Debug.WriteLine($"Frame {contributingFrameIndexes[i]}: RMS = {frameRms:F2}, Max = {frameMax:F2}");
        //        }

        //        // Compute RMS and max error from point-wise comparison
        //        double projectionRms = 0, maxError = 0;
        //        if (allObserved.Count > 0)
        //        {
        //            var errors = allObserved.Zip(allProjected, (obs, proj) =>
        //                Math.Sqrt(Math.Pow(obs.X - proj.X, 2) + Math.Pow(obs.Y - proj.Y, 2))).ToList();

        //            projectionRms = Math.Sqrt(errors.Sum(e => e * e) / errors.Count);
        //            maxError = errors.Max();
        //        }

        //        monoCalibrationCameraData = new MonoCalibrationCameraData
        //        {
        //            IntrinsicMatrix = intrinsicMatrix,
        //            DistortionCoeffs = distortionCoeffs,
        //            ReprojectionRMS = reprojectionError,
        //            ProjectionRMS = projectionRms,
        //            MaxError = maxError
        //        };

        //        //string side = trueLeftFalseRight ? "Left" : "Right";
        //        //Debug.WriteLine($"{side} mono calibration complete. RPE RMS: {reprojectionError:F4}, " +
        //        //                $"Projection RMS: {projectionRms:F4}, Max Error: {maxError:F4}");
        //    }

        //    return monoCalibrationCameraData;
        //}
        // Updated MonoCalibrateUsingBestFrames function


        // Collecting Corner Data: We iterate over each index in BestFrameIndexes and gather
        // the detected ChArUco corner positions(CharucoCorners) and IDs(CharucoIds) from
        // either the left or right frame data.We only include frames that have a sufficient
        // number of detected corners (at least 80 in this case) to ensure robust calibration data.
        // Calibrating the Camera: Using the aggregated allCharucoCorners and allCharucoIds,
        // we call ArucoInvoke.CalibrateCameraCharuco to compute the camera’s intrinsic matrix
        // and distortion coefficients. This function returns the overall reprojection error (RMS)
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
        // Per-Frame Error Metrics: We compute the reprojection error for each corner by measuring
        // the distance (Euclidean error) between the observed corner position and its projected
        // position. From these, we calculate the RMS error and maximum error for each frame.
        // These per-frame errors are output to the debug log (Debug.WriteLine) to help identify
        // if any particular frame has a high error (which could indicate an outlier or a
        // detection issue).
        // Overall Error Metrics: We aggregate all observed and projected points from every
        // frame to compute an overall RMS reprojection error(ProjectionRMS) across all points,
        // as well as the maximum reprojection error(MaxError) among all the corners in all
        // frames.This provides a global measure of calibration accuracy in addition to the
        // RMS error returned by the calibration function.
        // Output Structure: Finally, we populate the MonoCalibrationCameraData object with
        // the calibration results: the intrinsic camera matrix, distortion coefficients,
        // the calibration’s reprojection RMS (as returned by CalibrateCameraCharuco), and
        // the calculated ProjectionRMS and MaxError.This gives the calling code access to
        // both the camera parameters and the error metrics for further analysis or display.
        private MonoCalibrationCameraData? MonoCalibrateUsingBestFrames(bool trueLeftFalseRight, Windows.Foundation.Size frameSize, int monoCornerCountTheshold)
    {
        MonoCalibrationCameraData? monoCalibrationCameraData = null;
        if (charucoBoardDefinition is not null)
        {
            var allCharucoCorners = new VectorOfVectorOfPointF();
            var allCharucoIds = new VectorOfVectorOfInt();
            var allObserved = new List<System.Drawing.PointF>();
            var allProjected = new List<System.Drawing.PointF>();
            var contributingFrameIndexes = new List<int>();

            // Collect Charuco corner detections from the best frames
            foreach (var frameIndex in BestFrameIndexes)
            {
                if (!Frames.TryGetValue(frameIndex, out var framePair))
                    continue;
                FrameCalibrationData? calibrationData = null;
                if (trueLeftFalseRight)
                    calibrationData = framePair.frameCalibrationTargetLeft;
                else
                    calibrationData = framePair.frameCalibrationTargetRight;
                if (calibrationData is null || calibrationData.CharucoCorners.Length == 0 || calibrationData.CharucoIds.Length == 0)
                    continue;
                if (calibrationData.CharucoCorners.Length < monoCornerCountTheshold)
                    continue;

                    contributingFrameIndexes.Add(frameIndex);
                    allCharucoCorners.Push(new VectorOfPointF(calibrationData.CharucoCorners));
                    allCharucoIds.Push(new VectorOfInt(calibrationData.CharucoIds));
                }

                if (allCharucoCorners.Size == 0)
                {
                    Debug.WriteLine("No valid Charuco data found in best frames.");
                    return null;
                }

                using var cameraMatrix = new Mat();
                using var distCoeffs = new Mat();
                using var rvecs = new VectorOfMat();
                using var tvecs = new VectorOfMat();

                System.Drawing.Size frameSizeCv = new((int)frameSize.Width, (int)frameSize.Height);
                var intrinsicMatrix = new Matrix<double>(3, 3);
                var distortionCoeffs = new Matrix<double>(1, 5);
                double reprojectionError;

                try
                {
                    // Perform camera calibration using ChArUco corners
                    reprojectionError = ArucoInvoke.CalibrateCameraCharuco(
                        allCharucoCorners,
                        allCharucoIds,
                        charucoBoardDefinition.Board,
                        frameSizeCv,
                        cameraMatrix,
                        distCoeffs,
                        rvecs,
                        tvecs,
                        CalibType.Default,
                        new MCvTermCriteria(30, 1e-6));

                    // Copy results into matrices for easier use
                    cameraMatrix.CopyTo(intrinsicMatrix);
                    distCoeffs.CopyTo(distortionCoeffs);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Mono calibration failed: {ex.Message}");
                    return null;
                }

                // Compute projection errors for each frame
                for (int i = 0; i < allCharucoIds.Size; i++)
                {
                    var ids = allCharucoIds[i].ToArray();
                    var corners = allCharucoCorners[i].ToArray();
                    if (ids.Length < 1 || corners.Length < 1) continue;

                    // Map each detected ChArUco corner ID to its 3D object point on the board
                    var objectPoints = new List<MCvPoint3D32f>();
                    var observedCorners = new List<System.Drawing.PointF>();
                    var charucoCorner3DMap = GetCharucoCorner3DPoints(charucoBoardDefinition);

                    for (int j = 0; j < ids.Length; j++)
                    {
                        int id = ids[j];
                        if (!charucoCorner3DMap.TryGetValue(id, out MCvPoint3D32f point3D))
                            continue;
                        objectPoints.Add(point3D);
                        observedCorners.Add(corners[j]);
                    }

                    // Project the 3D points to 2D image points using the estimated camera parameters
                    System.Drawing.PointF[] projectedPoints = CvInvoke.ProjectPoints(
                        objectPoints.ToArray(),
                        rvecs[i],
                        tvecs[i],
                        intrinsicMatrix,
                        distortionCoeffs);

                    // Accumulate observed and projected points for overall error calculation
                    allObserved.AddRange(observedCorners);
                    allProjected.AddRange(projectedPoints);

                    // Calculate per-frame RMS and max error for this frame
                    var errors = observedCorners.Zip(projectedPoints, (obs, proj) =>
                                   Math.Sqrt(Math.Pow(obs.X - proj.X, 2) + Math.Pow(obs.Y - proj.Y, 2)))
                                 .ToList();
                    double frameRms = errors.Count > 0 ? Math.Sqrt(errors.Sum(e => e * e) / errors.Count) : 0.0;
                    double frameMax = errors.Count > 0 ? errors.Max() : 0.0;
                    Debug.WriteLine($"Frame {contributingFrameIndexes[i]}: RMS = {frameRms:F2}, Max = {frameMax:F2}");
                }

                // Compute overall RMS and max reprojection error across all frames
                double projectionRms = 0.0, maxError = 0.0;
                if (allObserved.Count > 0)
                {
                    var allErrors = allObserved.Zip(allProjected, (obs, proj) =>
                                     Math.Sqrt(Math.Pow(obs.X - proj.X, 2) + Math.Pow(obs.Y - proj.Y, 2)))
                                   .ToList();
                    projectionRms = Math.Sqrt(allErrors.Sum(e => e * e) / allErrors.Count);
                    maxError = allErrors.Max();
                }

                // Populate the calibration result data
                monoCalibrationCameraData = new MonoCalibrationCameraData
                {
                    IntrinsicMatrix = intrinsicMatrix,
                    DistortionCoeffs = distortionCoeffs,
                    ReprojectionRMS = reprojectionError,
                    ProjectionRMS = projectionRms,
                    MaxError = maxError
                };

                // Optionally, log final results:
                // string side = trueLeftFalseRight ? "Left" : "Right";
                // Debug.WriteLine($"{side} mono calibration complete. Reprojection RMS: {reprojectionError:F4}, Projection RMS: {projectionRms:F4}, Max Error: {maxError:F4}");
            }

            return monoCalibrationCameraData;
        }


        /// <summary>
        /// Return a text of the calibration data
        /// </summary>
        /// <param name="monoCalib"></param>
        /// <returns></returns>
        public static string CalibrationCameraDataText(MonoCalibrationCameraData? monoCalib)
        {            
            // Diplay the calibation results
            if (monoCalib is not null && monoCalib.IntrinsicMatrix is not null && monoCalib.DistortionCoeffs is not null)
            {
                StringBuilder sb = new();
                sb.AppendLine("Intrinsic Matrix:");
                for (int i = 0; i < 3; i++)
                {
                    sb.AppendLine($"{monoCalib.IntrinsicMatrix[i, 0],10:F3}  {monoCalib.IntrinsicMatrix[i, 1],10:F3}  {monoCalib.IntrinsicMatrix[i, 2],10:F3}");
                }

                sb.AppendLine("Distortion Coefficients:");
                for (int i = 0; i < monoCalib.DistortionCoeffs.Cols; i++)
                {
                    if (i > 0)
                        sb.Append("  ");

                    sb.Append($"{monoCalib.DistortionCoeffs[0, i],10:F3}");
                }
                sb.AppendLine();

                //string coeffs = string.Join(", ", Enumerable.Range(0, monoCalib.DistortionCoeffs.Cols)
                //    .Select(i => monoCalib.DistortionCoeffs[0, i].ToString("F4")));
                //sb.AppendLine(coeffs);

                // RPE RMS
                string rpeQuanlity = string.Empty;
                if (monoCalib.ReprojectionRMS <= 0.2)
                    rpeQuanlity = "(Excellent)";
                else if (monoCalib.ReprojectionRMS <= 0.5)
                    rpeQuanlity = "(Very good)";
                else if (monoCalib.ReprojectionRMS <= 1.0)
                    rpeQuanlity = "(Acceptable)";
                else if (monoCalib.ReprojectionRMS <= 1.5)
                    rpeQuanlity = "(Poor)";
                else if (monoCalib.ReprojectionRMS <= 2.0)
                    rpeQuanlity = "(Very poor)";
                // < 0.2    Excellent(usually only in studio/lab with perfect lighting and corner visibility)
                // 0.2–0.5  Very good; suitable for accurate 3D reconstructions and pose estimates
                // 0.5–1.0  Acceptable for many real-world use cases, especially underwater, drone, etc.
                // > 1.0    Often indicates blur, motion, poor corner detection, or bad coverage
                sb.AppendLine($"RPE: {monoCalib.ReprojectionRMS:F2}px {rpeQuanlity}");
                //Debug.WriteLine($"Projection RMS (point):    {monoCalib.ProjectionRMS:F4} px");
                //Debug.WriteLine($"Max Reprojection Error:    {monoCalib.MaxError:F4} px");

                // Project RMS and MAX Error
                sb.AppendLine($"Projection RMS: {monoCalib.ProjectionRMS:F2}px  Max Error:{monoCalib.MaxError:F2}px");

                return sb.ToString();
            }

            return string.Empty;
        }



        /// <summary>
        /// Manually compute the 3D corner positions based on the known board 
        /// layout (squaresX, squaresY, squareLength, markerLength) and Charuco 
        /// ID indexing.
        /// THis is needed because CharucoBoard in Emgu CV (and OpenCV) doesn't 
        /// expose a direct method like GetChessboardCorners() to retrieve the 
        /// 3D object points for individual Charuco
        /// </summary>
        /// <param name="board"></param>
        /// <returns></returns>
        private static Dictionary<int, MCvPoint3D32f> GetCharucoCorner3DPoints(CharucoBoardDefinition board)
        {
            var cornerMap = new Dictionary<int, MCvPoint3D32f>();

            int id = 0;
            for (int y = 0; y < board.SquaresY - 1; y++)
            {
                for (int x = 0; x < board.SquaresX - 1; x++)
                {
                    float fx = x * board.SquareLength;
                    float fy = y * board.SquareLength;
                    cornerMap[id++] = new MCvPoint3D32f(fx, fy, 0);
                }
            }

            return cornerMap;
        }


        /// <summary>
        /// Calculate the yaw and pitch angles for each frame in the set,
        /// </summary>
        /// <returns></returns>
        public async Task CalculateFramesYawPitchAndPopulatePoseBin(MonoCalibrationCameraData monoCalibLeft, MonoCalibrationCameraData monoCalibRight, Size frameSize)
        {
            if (charucoBoardDefinition is not null)
            {
                await Task.Run(() =>
                {
                    // Emgu uses System.Drawing
                    System.Drawing.Size frameSizeCorrectedType = new((int)frameSize.Width, (int)frameSize.Height);

                    // Parse the Frames
                    foreach (var (frameIndex, (left, right, _)) in Frames)
                    {
                        if (left is not null)
                        {
                            CalcYawAndPitcAndWhichPoseBin(left, monoCalibLeft);

                            AddToThePoseBinTotals(left, PoseBinTotalsLeft);
                        }
                        if (right is not null)
                        {
                            CalcYawAndPitcAndWhichPoseBin(right, monoCalibRight);

                            AddToThePoseBinTotals((FrameCalibrationData)right, PoseBinTotalsRight);
                        }
                    }
                });
            }

            void CalcYawAndPitcAndWhichPoseBin(FrameCalibrationData frameCalibrationData, MonoCalibrationCameraData monoCalib)
            {
                if (charucoBoardDefinition is not null)
                {
                    if (frameCalibrationData.CharucoCorners.Length > 0 && frameCalibrationData.CharucoIds.Length >= 6/*min required for DLT calc*/)
                    {
                        using var cornersVec = new VectorOfPointF(frameCalibrationData.CharucoCorners);
                        using var idsVec = new VectorOfInt(frameCalibrationData.CharucoIds);

                        var rvec = new Mat();
                        var tvec = new Mat();

                        bool success = ArucoInvoke.EstimatePoseCharucoBoard(cornersVec,
                                                                            idsVec,
                                                                            charucoBoardDefinition.Board,
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


                            double yawDeg = yawRad * 180.0 / Math.PI;
                            double pitchDeg = pitchRad * 180.0 / Math.PI;

                                // Store the angles in the left frame
                                frameCalibrationData.YawDeg = yawDeg;
                                frameCalibrationData.PitchDeg = pitchDeg;

                            int yawBin = BinFromAngle(frameCalibrationData.YawDeg, FrameCalibrationData.PoseBinThresholdYaw);
                            int pitchBin = BinFromAngle(frameCalibrationData.PitchDeg, FrameCalibrationData.PoseBinThresholdPitch);
                                frameCalibrationData.PoseBinsOccupied = [(yawBin, pitchBin)];
                        }
                    }
                    else
                    {
                            frameCalibrationData.YawDeg = 0;
                            frameCalibrationData.PitchDeg = 0;
                            frameCalibrationData.PoseBinsOccupied.Clear();
                    }

                }
            }


            // Helper
            static void AddToThePoseBinTotals(FrameCalibrationData target, Dictionary<(int binx, int biny), int> BinTotals)
            {
                foreach (var bin in target.PoseBinsOccupied)
                {
                    BinTotals[bin] = BinTotals.GetValueOrDefault(bin) + 1;
                }
            }
        }


        // Helper for binning an angle
        private static int BinFromAngle(double angle, IReadOnlyList<double> thresholds)
        {
            //???double absAngle = Math.Abs(angle);
            for (int i = 0; i < thresholds.Count; i++)
            {
                if (angle < thresholds[i])
                    return i;
            }
            return thresholds.Count - 1;
        }



        /// <summary>
        /// This would select frames that maximize coverage over both spatial and pose bins.
        /// </summary>
        public void SelectBestStereoFramesUsingSensorAndPoseBins()
        {
            BestFrameIndexes.Clear();

            // Layer dimensions
            var (gx, gy) = FrameCalibrationData.SensorBinGridLayers[0];
            var (px, py) = FrameCalibrationData.PoseBinGrid;

            var selectedBins = new HashSet<(int gx, int gy, int binx, int biny, int posex, int posey)>();

            foreach (var (frameIndex, (left, _, _)) in Frames.OrderByDescending(kv => kv.Value.frameCalibrationTargetLeft.Score))
            {
                if (left == null)
                    continue;

                foreach (var (binx, biny) in left.PoseBinsOccupied)
                {
                    foreach (var (sgx, sgy, sbx, sby) in left.SensorBinsOccupied)
                    {
                        var binKey = (sgx, sgy, sbx, sby, binx, biny);
                        if (!selectedBins.Contains(binKey))
                        {
                            selectedBins.Add(binKey);
                            BestFrameIndexes.Add(frameIndex);
                            break;
                        }
                    }
                }

                // Break early if we filled all possible pose+sensor combinations
                if (selectedBins.Count >= gx * gy * px * py)
                    break;
            }
        }


        /*** End of CalibrationStereoFrameSet ***/
    }


    public class TupleInt4JsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Dictionary<(int, int, int, int), int>);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var result = new Dictionary<(int, int, int, int), int>();
            var obj = JObject.Load(reader);

            foreach (var prop in obj.Properties())
            {
                // Parse string key: "(6, 4, 3, 0)"
                var keyString = prop.Name.Trim('(', ')');
                var parts = keyString.Split(',');

                if (parts.Length == 4 &&
                    int.TryParse(parts[0], out int a) &&
                    int.TryParse(parts[1], out int b) &&
                    int.TryParse(parts[2], out int c) &&
                    int.TryParse(parts[3], out int d))
                {
                    var key = (a, b, c, d);
                    var value = prop.Value.ToObject<int>();
                    result[key] = value;
                }
            }

            return result;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var dict = value as Dictionary<(int, int, int, int), int>;
            if (dict == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            foreach (var kvp in dict)
            {
                string key = $"({kvp.Key.Item1}, {kvp.Key.Item2}, {kvp.Key.Item3}, {kvp.Key.Item4})";
                writer.WritePropertyName(key);
                writer.WriteValue(kvp.Value);
            }
            writer.WriteEndObject();
        }

        /*** End of TupleInt4JsonConverter ***/
    }

    public class TupleInt2JsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Dictionary<(int, int), int>);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var result = new Dictionary<(int, int), int>();
            var obj = JObject.Load(reader);

            foreach (var prop in obj.Properties())
            {
                // Parse string key: "(6, 4)"
                var keyString = prop.Name.Trim('(', ')');
                var parts = keyString.Split(',');

                if (parts.Length == 4 &&
                    int.TryParse(parts[0], out int a) &&
                    int.TryParse(parts[1], out int b))
                {
                    var key = (a, b);
                    var value = prop.Value.ToObject<int>();
                    result[key] = value;
                }
            }

            return result;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var dict = value as Dictionary<(int, int), int>;
            if (dict == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            foreach (var kvp in dict)
            {
                string key = $"({kvp.Key.Item1}, {kvp.Key.Item2})";
                writer.WritePropertyName(key);
                writer.WriteValue(kvp.Value);
            }
            writer.WriteEndObject();
        }

        /*** End of TupleInt2JsonConverter ***/
    }


    /// <summary>
    /// Let or right camera calibration data.
    /// </summary>
    public class MonoCalibrationCameraData
    {
        public Matrix<double>? IntrinsicMatrix { get; set; }
        public Matrix<double>? DistortionCoeffs { get; set; }
        public double ReprojectionRMS { get; set; }     // Reprojection RMS Error (RPE RMS)
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
                                                        // Definition: Maximum Euclidean distance between an observed 2D point and its reprojection.
                                                        // ≤ 0.50px     Excellent — very tight calibration with no significant outliers.
                                                        // 0.5–1.0px    Good — minor outliers, acceptable for most applications.
                                                        // 1.0–2.0px    Acceptable — some frames may have off detections or bad coverage.
                                                        // > 2.0px      Poor — likely issues with blurred frames, incorrect detections, or too few diverse poses.
                                                        // Contextual Considerations
                                                        // High max error doesn't always mean the calibration is bad, but it does indicate a possible weak frame.
                                                        // If your Reprojection RMS error is good(~0.3–0.4 px) but your max error is >2 px, consider reviewing:
                                                        //    - Frame sharpness
                                                        //    - Angle coverage of the calibration board
                                                        //    - Board detection quality(false or partial matches)
                                                        // It’s common to filter out worst frames(e.g., >2 px error) after an initial calibration round to improve a second pass.
    }
}

