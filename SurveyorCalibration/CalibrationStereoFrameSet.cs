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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SurveyorCalibrationData;
using Windows.Devices.Bluetooth.Advertisement;

namespace Surveyor.Calibration
{

    public enum CalibrationParameters
    {
        K1K2P1P2,              // 4 coefficients: k1, k2, p1, p2
        K1K2K3P1P2,            // 5 coefficients: k1, k2, k3, p1, p2
        K1K2K3K4P1P2          // 6 coefficients: k1–k4, p1, p2
    }

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

                                return new[] { pair.frameCalibrationTargetLeft!, pair.frameCalibrationTargetRight! };
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
        private async Task FindCalibration(bool trueStereoFalseMonoLeft,
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
                FrameCalibrationData? targetLeft = null;
                FrameCalibrationData? targetRight = null;

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
                    await FindCalibration(trueStereoFalseMonoLeft, newStartFrame, newEndFrame, frameStep2,
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
        public static int DrawMarkersToMat(FrameCalibrationData frameCalibrationTarget, Mat frame, bool trueMonoHeadfalseStereoHead)
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
                    new MCvScalar(0, 255, 0)  // Green for Charuco IDs
                );

                // Draw the centre point
                System.Drawing.PointF boardCentre = frameCalibrationTarget.Center;
                int radius = 40;
                MCvScalar color = new(0, 255, 0); // Green for Centre 
                int thickness = 20;

                // Draw the circle on the Mat
                CvInvoke.Circle(frame, new System.Drawing.Point((int)boardCentre.X, (int)boardCentre.Y), radius, color, thickness);

                if (trueMonoHeadfalseStereoHead)
                { 
                    // If there are reprojected points then draw them
                    int index = frameCalibrationTarget.monoProjectedPoints?
                            .Select((arr, i) => new { arr, i })
                            .FirstOrDefault(x => x.arr != null && x.arr.Length > 0)?.i ?? -1;

                    if (index != -1 && frameCalibrationTarget?.monoProjectedPoints is not null)
                    {
                        foreach (var pt in frameCalibrationTarget.monoProjectedPoints[index])
                        {
                            CvInvoke.Circle(
                                frame,
                                new System.Drawing.Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)),
                                10,
                                new MCvScalar(0, 0, 255),  // Red for reprojected
                                3  // Filled circle
                            );
                        }
                    }

                    // Draw any ChArUco corners if they exist
                    if (frameCalibrationTarget is not null &&
                        frameCalibrationTarget.CharucoCorners is not null &&
                        frameCalibrationTarget.CharucoCorners.Length > 0)
                    {
                        foreach (var pt in frameCalibrationTarget.CharucoCorners)
                        {
                            CvInvoke.Circle(
                                frame,
                                new System.Drawing.Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)),
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
                        frameCalibrationTarget.StereoSharedCharucoCorners is not null)
                    {
                        int indexStereoSharedCharucoCorners = frameCalibrationTarget.StereoSharedCharucoCorners?
                                            .Select((arr, i) => new { arr, i })
                                            .FirstOrDefault(x => x.arr != null && x.arr.Length > 0)?.i ?? -1;

                        if (indexStereoSharedCharucoCorners != -1 && 
                            frameCalibrationTarget.StereoSharedCharucoCorners[indexStereoSharedCharucoCorners] is not null)
                        {
                            foreach (var pt in frameCalibrationTarget.StereoSharedCharucoCorners[indexStereoSharedCharucoCorners])
                            {
                                CvInvoke.Circle(
                                    frame,
                                    new System.Drawing.Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)),
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

            if (charucoBoardDefinition is not null)
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


        public MonoCalibrationCameraData? MonoCalibrateUsingBestFrames(
                                                    bool trueStereoFalseMono,
                                                    bool trueLeftFalseRight,
                                                    Windows.Foundation.Size frameSize,
                                                    int monoCornerCountThreshold,
                                                    CalibrationParameters calibrationParameters)
        {
            MonoCalibrationCameraData? monoCalibrationCameraData = null;
            double reprojectionError = -1;
            double rmsUpper = 3.0; // Set high
            double maxUpper = 5.0; // Set high
            int imageUsable = 0; // Count of usable images

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

            if (charucoBoardDefinition is null)
            {
                Debug.WriteLine($"{side} Charuco board definition is null.");
                return null;
            }

            int passCount = 1;

            for (int pass = 0; pass < passCount; pass++)
            {
                var allCharucoCorners = new VectorOfVectorOfPointF();
                var allCharucoIds = new VectorOfVectorOfInt();
                List<FrameCalibrationData> allFrameData = [];

                // Collect Charuco corner detections from the best frames
                int frameRemovedFromRMSOrMaxError = 0;
                foreach (var frameIndex in BestFrameIndexes)
                {
                    if (!Frames.TryGetValue(frameIndex, out var framePair))
                        continue;

                    var calibrationData = trueTargetLeftFalseUseTargetRight
                        ? framePair.frameCalibrationTargetLeft
                        : framePair.frameCalibrationTargetRight;

                    // The first pass (pass zero) is used to gather all frames with sufficient corners.
                    if (pass == 0)
                    {
                        if (calibrationData is null ||
                            calibrationData.CharucoCorners.Length == 0 ||
                            calibrationData.CharucoIds.Length == 0 ||
                            calibrationData.CharucoCorners.Length < monoCornerCountThreshold)
                        {
                            // Ensure the projected RMS and Max Error are reset (may have been previously set)
                            if (calibrationData is not null)
                            {
                                calibrationData.monoFrameRms[(int)calibrationParameters] = -1; // Mark as invalid
                                calibrationData.monoFrameMaxError[(int)calibrationParameters] = -1; // Mark as invalid
                            }
                            continue;
                        }
                    }
                    // Second pass (pass one) is used to gather all frames with sufficient corners and
                    // the projected RMS and Max Error are with thesholds.
                    // Note the projected RMS and Max Error were calculated in the first pass
                    else if (pass == 1)
                    {
                        if (calibrationData is null ||
                            calibrationData.CharucoCorners.Length == 0 ||
                            calibrationData.CharucoIds.Length == 0 ||
                            calibrationData.CharucoCorners.Length < monoCornerCountThreshold )
                        {
                            continue;
                        }
                        if (calibrationData is not null &&
                            (calibrationData.monoFrameRms[(int)calibrationParameters] > rmsUpper || 
                             calibrationData.monoFrameMaxError[(int)calibrationParameters] > maxUpper))
                        {
                            frameRemovedFromRMSOrMaxError++;
                            continue;
                        }
                    }


                    if (calibrationData is not null)
                    {
                        imageUsable++;
                        allCharucoCorners.Push(new VectorOfPointF(calibrationData.CharucoCorners));
                        allCharucoIds.Push(new VectorOfInt(calibrationData.CharucoIds));
                        allFrameData.Add(calibrationData);
                    }
                }

                if (allCharucoCorners.Size == 0)
                {
                    Debug.WriteLine($"{side} No valid Charuco data found in best frames.");
                    return null;
                }
                if (pass == 1 && frameRemovedFromRMSOrMaxError > 0)
                {
                    Debug.WriteLine($"{side} Mono calibration second pass (pass 1) removed {frameRemovedFromRMSOrMaxError} frames due to RMS or Max Error thresholds.");
                }

                System.Drawing.Size frameSizeCv = new((int)frameSize.Width, (int)frameSize.Height);
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
                    reprojectionError = ArucoInvoke.CalibrateCameraCharuco(
                                                    allCharucoCorners,
                                                    allCharucoIds,
                                                    charucoBoardDefinition.Board,
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
                var charucoCorner3DMap = GetCharucoCorner3DPoints(charucoBoardDefinition);
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
                    ImageTotal = BestFrameIndexes.Count,
                    ImageUseable = imageUsable,
                    IntrinsicMatrix = intrinsicMatrix,
                    DistortionCoeffs = distortionCoeffs,
                    ReprojectionRMS = reprojectionError,
                    ProjectionRMS = projectionRms,
                    MaxError = maxError
                };

                Debug.WriteLine($"{side} mono calibration first pass (pass 0) complete. Reprojection RMS: {reprojectionError:F4}, Projection RMS: {projectionRms:F4}, Max Error: {maxError:F4}");

                // Check if frames can be improved and if so re-run the calibration
                // Select relevant FrameCalibrationData from BestFrameIndexes
                var selectedFrames = BestFrameIndexes
                    .Select(index => Frames.TryGetValue(index, out var tuple)
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
                    Debug.WriteLine($"{side} mono calibration second pass (pass 1), projection RMS theshold: {rmsUpper:F2}, max error theshold: {maxUpper:F2}");
                    passCount = 2;
                }
            }


            return monoCalibrationCameraData;
        }


        /// <summary>
        /// Compute the projection errors for the given Charuco corners and IDs
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
                                                    List<FrameCalibrationData> allFrameData,
                                                    VectorOfMat rvecs,
                                                    VectorOfMat tvecs,
                                                    Matrix<double> intrinsicMatrix,
                                                    Matrix<double> distortionCoeffs,
                                                    Dictionary<int, MCvPoint3D32f> charucoCorner3DMap,
                                                    CalibrationParameters calibrationParameters)
        {
            var allObserved = new List<System.Drawing.PointF>();
            var allProjected = new List<System.Drawing.PointF>();

            for (int i = 0; i < allCharucoIds.Size; i++)
            {
                var ids = allCharucoIds[i].ToArray();
                var corners = allCharucoCorners[i].ToArray();

                if (ids.Length == 0 || corners.Length == 0)
                    continue;

                var objectPoints = new List<MCvPoint3D32f>();
                var observedCorners = new List<System.Drawing.PointF>();

                for (int j = 0; j < ids.Length; j++)
                {
                    if (charucoCorner3DMap.TryGetValue(ids[j], out var point3D))
                    {
                        objectPoints.Add(point3D);
                        observedCorners.Add(corners[j]);
                    }
                    else
                    {
                        Debug.WriteLine($"Missing 3D point for Charuco ID {ids[j]}");
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

                // Save the frame quanlity tests
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
        private double GetUpperFence(List<double> values)
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
        /// <param name="monoCalib"></param>
        /// <returns></returns>
        public static string CalibrationCameraDataText(MonoCalibrationCameraData? monoCalib)
        {
            try
            {
                // Diplay the calibation results
                if (monoCalib is not null && monoCalib.IntrinsicMatrix is not null && monoCalib.DistortionCoeffs is not null)
                {
                    StringBuilder sb = new();
                    sb.AppendLine("Intrinsic Matrix:");
                    for (int i = 0; i < 3; i++)
                    {
                        sb.AppendLine($"{monoCalib.IntrinsicMatrix[i, 0],9:F3}  {monoCalib.IntrinsicMatrix[i, 1],9:F3}  {monoCalib.IntrinsicMatrix[i, 2],9:F3}");
                    }

                    sb.AppendLine($"Distortion Coefficients [{monoCalib.CalibrationParameters.ToString()}]:");
                    (_, int DistRowCount) = GetCalibrationFlags(monoCalib.CalibrationParameters);

                    for (int i = 0; i < Math.Min(DistRowCount, 8); i++)
                    {
                        if (i > 0)
                            sb.Append("  ");

                        sb.Append($"{monoCalib.DistortionCoeffs[0, i],9:F3}");
                    }
                    sb.AppendLine();


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
                    sb.AppendLine($"Projection RMS: {monoCalib.ProjectionRMS:F2}px  Max Error: {monoCalib.MaxError:F2}px");

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
        /// Return text for the stereo calibration data
        /// </summary>
        /// <param name="monoCalib"></param>
        /// <returns></returns>
        public static string CalibrationCameraDataText(CalibrationStereoCameraData? stereoCalib)
        {
            try
            {
                // Diplay the calibation results
                if (stereoCalib is not null && stereoCalib.Rotation is not null && stereoCalib.Translation is not null)
                {
                    StringBuilder sb = new();
                    sb.AppendLine("Rotation:");
                    for (int i = 0; i < 3; i++)
                    {
                        sb.AppendLine($"{stereoCalib.Rotation[i, 0],9:F3}  {stereoCalib.Rotation[i, 1],9:F3}  {stereoCalib.Rotation[i, 2],9:F3}");
                    }

                    sb.AppendLine($"Translation:");

                    for (int i = 0; i < 3; i++)
                    {
                        if (i > 0)
                            sb.Append("  ");

                        sb.Append($"{stereoCalib.Translation[i, 0],9:F3}");
                    }
                    sb.AppendLine();


                    // RPE RMS
                    string rpeQuanlity = string.Empty;
                    if (stereoCalib.RMS <= 0.2)
                        rpeQuanlity = "(Excellent)";
                    else if (stereoCalib.RMS <= 0.5)
                        rpeQuanlity = "(Very good)";
                    else if (stereoCalib.RMS <= 1.0)
                        rpeQuanlity = "(Acceptable)";
                    else if (stereoCalib.RMS <= 1.5)
                        rpeQuanlity = "(Poor)";
                    else if (stereoCalib.RMS <= 2.0)
                        rpeQuanlity = "(Very poor)";
                    // < 0.2    Excellent(usually only in studio/lab with perfect lighting and corner visibility)
                    // 0.2–0.5  Very good; suitable for accurate 3D reconstructions and pose estimates
                    // 0.5–1.0  Acceptable for many real-world use cases, especially underwater, drone, etc.
                    // > 1.0    Often indicates blur, motion, poor corner detection, or bad coverage
                    sb.AppendLine($"RPE: {stereoCalib.RMS:F2}px {rpeQuanlity}");
                    //Debug.WriteLine($"Projection RMS (point):    {monoCalib.ProjectionRMS:F4} px");
                    //Debug.WriteLine($"Max Reprojection Error:    {monoCalib.MaxError:F4} px");

                    // Project RMS and MAX Error
                    //???sb.AppendLine($"Projection RMS: {monoCalib.ProjectionRMS:F2}px  Max Error: {monoCalib.MaxError:F2}px");

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
            if (charucoBoardDefinition is null)
                return null;

            // Define output containers
            var objectPoints = new List<MCvPoint3D32f[]>();
            var imagePointsLeft = new List<PointF[]>();
            var imagePointsRight = new List<PointF[]>();

            var charucoCorner3DMap = GetCharucoCorner3DPoints(charucoBoardDefinition);

            foreach (var frameIndex in BestFrameIndexes)
            {
                if (!Frames.TryGetValue(frameIndex, out var framePair))
                    continue;

                var left = framePair.frameCalibrationTargetLeft;
                var right = framePair.frameCalibrationTargetRight;

                if (left == null || right == null || left.CharucoIds.Length < minCharucoIdsCount || right.CharucoIds.Length < minCharucoIdsCount)
                    continue;

                // Match corners by ID
                var leftDict = new Dictionary<int, PointF>();
                for (int i = 0; i < left.CharucoIds.Length; i++)
                    leftDict[left.CharucoIds[i]] = left.CharucoCorners[i];

                var rightDict = new Dictionary<int, PointF>();
                for (int i = 0; i < right.CharucoIds.Length; i++)
                    rightDict[right.CharucoIds[i]] = right.CharucoCorners[i];

                var sharedIds = leftDict.Keys.Intersect(rightDict.Keys).ToList();
                if (sharedIds.Count < stereoCornerCountThreshold)
                    continue;

                var objPts = new List<MCvPoint3D32f>();
                var imgPtsLeft = new List<PointF>();
                var imgPtsRight = new List<PointF>();

                foreach (var id in sharedIds)
                {
                    if (!charucoCorner3DMap.TryGetValue(id, out var pt3D))
                        continue;

                    objPts.Add(pt3D);
                    imgPtsLeft.Add(leftDict[id]);
                    imgPtsRight.Add(rightDict[id]);

                }

                // And store of displaying later
                left.StereoSharedCharucoCorners[(int)calibrationParameters] = [.. imgPtsLeft];
                right.StereoSharedCharucoCorners[(int)calibrationParameters] = [.. imgPtsRight];
                left.StereoSharedCharucoIDs[(int)calibrationParameters] = [.. sharedIds];  // Only need on the left side as both left and right are the same


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

            if (leftMonoCalibrationCameraData.IntrinsicMatrix is not null &&
                rightMonoCalibrationCameraData.IntrinsicMatrix is not null &&
                leftMonoCalibrationCameraData.DistortionCoeffs is not null &&
                rightMonoCalibrationCameraData.DistortionCoeffs is not null)
            {
                var camMatL = leftMonoCalibrationCameraData.IntrinsicMatrix.Mat.Clone();
                var camMatR = rightMonoCalibrationCameraData.IntrinsicMatrix.Mat.Clone();
                var distL = leftMonoCalibrationCameraData.DistortionCoeffs.Mat.Clone();
                var distR = rightMonoCalibrationCameraData.DistortionCoeffs.Mat.Clone();

                var R = new Mat();
                var T = new Mat();
                var E = new Mat();
                var F = new Mat();

                System.Drawing.Size imgSize = new((int)frameSize.Width, (int)frameSize.Height);

                double error = CvInvoke.StereoCalibrate(
                    [.. objectPoints],
                    [.. imagePointsLeft],
                    [.. imagePointsRight],
                    camMatL, distL,
                    camMatR, distR,
                    imgSize,
                    R, T, E, F,
                    CalibType.FixIntrinsic,
                    new MCvTermCriteria(30, 1e-6)
                );


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
                    ImageTotal = BestFrameIndexes.Count,
                    ImageUseable = imageUseable,
                    RMS = error
                };


                //???***TEMP***
                return calibrationStereoCameraData;

                // Reprojection test
                int index = 0;

                foreach (var frameIndex in BestFrameIndexes)
                {
                    if (!Frames.TryGetValue(frameIndex, out var framePair))
                        continue;

                    var left = framePair.frameCalibrationTargetLeft;
                    var right = framePair.frameCalibrationTargetRight;

                    if (left == null || right == null || 
                        left.CharucoIds.Length < minCharucoIdsCount || right.CharucoIds.Length < minCharucoIdsCount)
                        continue;

                    var objPts = objectPoints[index];
                    var imgPtsLeft = imagePointsLeft[index];
                    var imgPtsRight = imagePointsRight[index];

                    // Re-Setup distL & distR because CvInvoke.StereoCalibrate will change them
                    distL = leftMonoCalibrationCameraData.DistortionCoeffs.Mat.Clone();
                    distR = rightMonoCalibrationCameraData.DistortionCoeffs.Mat.Clone();

                    // Calc the reprojection errors for this frame
                    (PointF[] leftProjected, PointF[] rightProjected) = ValidateStereoProjectionReprojectionError(
                                frameIndex,
                                objPts, imgPtsLeft, imgPtsRight,
                                camMatL, distL,
                                camMatR, distR,
                                Rmat, Tmat);

                    // Check if leftProjected is not empty
                    if (leftProjected.Length > 0 && leftProjected.Length == rightProjected.Length)
                    {
                        left.stereoProjectedPoints[(int)calibrationParameters] = leftProjected;
                        right.stereoProjectedPoints[(int)calibrationParameters] = rightProjected;
                        //???left.stereoFrameRms[(int)calibrationParameters]
                        //???left.stereoFrameMaxError[(int)calibrationParameters]
                        //???right.stereoFrameRms[(int)calibrationParameters]
                        //???right.stereoFrameMaxError[(int)calibrationParameters]
                    }
                }
            }


            return calibrationStereoCameraData;
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
            PointF[] leftProjected = [];
            PointF[] rightProjected = [];

            Matrix<double> R1 = new(3, 3);
            R.CopyTo(R1);
            Matrix<double> T1 = new(3, 1);
            T.CopyTo(T1);


            using var vecLeft = new VectorOfPointF(imgPtsLeft);
            using var vecRight = new VectorOfPointF(imgPtsRight);
            using var undistLeft = new VectorOfPointF();
            using var undistRight = new VectorOfPointF();

            CvInvoke.UndistortPoints(vecLeft, undistLeft, intrLeft, distLeft, null, intrLeft);
            CvInvoke.UndistortPoints(vecRight, undistRight, intrRight, distRight, null, intrRight);

            var points4D = new Mat();
            CvInvoke.TriangulatePoints(intrLeft, intrRight, undistLeft, undistRight, points4D);

            var totalErrorLeft = 0.0;
            var totalErrorRight = 0.0;
            Matrix<float> ptsMat = new(points4D.Rows, points4D.Cols);
            points4D.CopyTo(ptsMat);

            var objectPointsUnDist = new List<MCvPoint3D32f>();

            // Now access like: ptsMat[0, j], ptsMat[1, j], etc.
            for (int j = 0; j < ptsMat.Cols; j++)
            {
                float w = ptsMat[3, j];
                var point3D = new MCvPoint3D32f(
                    ptsMat[0, j] / w,
                    ptsMat[1, j] / w,
                    ptsMat[2, j] / w
                );

                objectPointsUnDist.Add(point3D);
            }


            using var projectedLeft = new Emgu.CV.Util.VectorOfPointF();
            using var projectedRight = new Emgu.CV.Util.VectorOfPointF();

            Matrix<double> zeroRotation = new(3, 1); // all zeros by default
            Matrix<double> zeroTranslation = new(3, 1); // all zeros by default

            CvInvoke.ProjectPoints([.. objectPointsUnDist], zeroRotation, zeroTranslation, intrLeft, distLeft, projectedLeft);
            CvInvoke.ProjectPoints([.. objectPointsUnDist], R1, T1, intrRight, distRight, projectedRight);


            for (int j = 0; j < ptsMat.Cols; j++)
            {
                double errL = Math.Sqrt(Math.Pow(imgPtsLeft[j].X - projectedLeft[0].X, 2) + Math.Pow(imgPtsLeft[j].Y - projectedLeft[0].Y, 2));
                double errR = Math.Sqrt(Math.Pow(imgPtsRight[j].X - projectedRight[0].X, 2) + Math.Pow(imgPtsRight[j].Y - projectedRight[0].Y, 2));

                Debug.WriteLine($"[Stereo Validation] Frame {frameIndex} Point {j}: Reprojection error L={errL:F3}px R={errR:F3}px");
                totalErrorLeft += errL;
                totalErrorRight += errR;
            }

            Debug.WriteLine($"[Stereo Validation] Frame {frameIndex}: Avg reprojection error L={totalErrorLeft / imgPtsLeft.Length:F3}px R={totalErrorRight / imgPtsRight.Length:F3}px");
          
            //??? Return left and right RMS

            return (leftProjected, rightProjected);
        }

        /// <summary>
        /// Calculate the yaw and pitch angles for each frame in the set,
        /// </summary>
        /// <returns></returns>
        public async Task CalculateFramesYawPitchAndPopulatePoseBin(MonoCalibrationCameraData monoCalibLeft, MonoCalibrationCameraData? monoCalibRight, Windows.Foundation.Size frameSize)
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
                        if (right is not null && monoCalibRight is not null)
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
        /// Add to the existing best frame array 2 frames from each of the 
        /// pose bins to ensure pose diversity
        /// </summary>
        public bool AddBestStereoFramesUsingPoseBins(double maxMovementFactor, double maxBlurFactor)
        {
            int poseFramesAdded = 0;
            int poseFramesTotal = 0;
            HashSet<int> frameIndexSet = [.. BestFrameIndexes];

            // Layer dimensions
            var (px, py) = FrameCalibrationData.PoseBinGrid;

            bool mono = false;
            List<int>? frameIndexes = null;

            // Check if mono or stereo by checking for null right values
            mono = !Frames.TryGetValue(Frames.Keys.FirstOrDefault(), out var tuple) || tuple.Item2 is null;


            for (int biny = 0; biny < py; biny++)
            {
                for (int binx = 0; binx < px; binx++)
                {
                    var targetBin = (binx, biny);

                    if (mono)
                    {
                        frameIndexes = Frames
                                         .Where(kvp =>
                                         {
                                             var (left, _, _) = kvp.Value;
                                             return left.CharucoCorners.Length > 50 &&
                                                    left.PoseBinsOccupied.Contains(targetBin) &&
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
                                         .Take(4)
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
                                                   left.PoseBinsOccupied.Contains(targetBin) &&
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
                    {
                        poseFramesTotal++;
                        if (frameIndexSet.Add(index))
                        {
                            poseFramesAdded++;
                        }
                    }
                }
            }

            if (frameIndexes is not null)
            {
                Debug.WriteLine($"AddBestStereoFramesUsingPoseBins: {poseFramesAdded} unique pose diverse frames added from {poseFramesTotal} diverse frames found (any different is because the frames already existed in the best frames list ");

                BestFrameIndexes = [.. frameIndexSet];
            }

            return true;
        }


        private static (CalibType, int distRowCount) GetCalibrationFlags(CalibrationParameters calibrationParameters)
        {
            CalibType flags = CalibType.Default;
            int distRowCount = 0;

            switch (calibrationParameters)
            {
                case CalibrationParameters.K1K2P1P2:
                    flags = CalibType.FixK3;
                    distRowCount = 5;
                    break;

                case CalibrationParameters.K1K2K3P1P2:
                    flags = CalibType.Default;
                    distRowCount = 5;
                    break;

                case CalibrationParameters.K1K2K3K4P1P2:
                    flags = CalibType.RationalModel | CalibType.FixK5 | CalibType.FixK6;
                    distRowCount = 14;
                    break;
            }

            return (flags, distRowCount);
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
        public CalibrationParameters CalibrationParameters { get; set; }
        public Matrix<double>? IntrinsicMatrix { get; set; }
        public Matrix<double>? DistortionCoeffs { get; set; }
        public int ImageTotal { get; set; }
        public int ImageUseable { get; set; }
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

