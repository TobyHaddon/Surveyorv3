// Ignore Spelling: Uco

using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Newtonsoft.Json;
using Org.BouncyCastle.Tsp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace Surveyor.Calibration
{
    /// <summary>
    /// A FrameCalibrationTarget instance represents metadata on a single frame where
    /// that frame is observed to have a detectable ChArUco Calibration target.
    /// The instance has a calculate of how 'still' this frame was from the previous to the 
    /// next frame (MovementFactor) and separately a BlurFactor is calculated.
    /// </summary>
    public class FrameData
    {
        // Version of the class (use for data migrations)
        private const int version = 6;

        // Data Version
        [JsonProperty("Ver")]
        public int Version { get; set; } = -1;      // This is so serialized records with a Version get a -1, the version is set in the main constructor

        // The frame index of the calibration target
        [JsonProperty("Index")]
        public int FrameIndex { get; init; }

        // Corners and IDs
        [JsonProperty("Corners")]
        public PointF[] ChArUcoCorners { get; init; } = [];

        [JsonProperty("Ids")]
        public int[] ChArUcoIds { get; init; } = [];


        // Movement factors
        [JsonProperty("MovePrev")]
        public double MovementFromPrevious { get; set; }
        [JsonProperty("MoveNext")]
        public double MovementToNext { get; set; }
        [JsonProperty("MoveFactor")]
        public double MovementFactor => (MovementFromPrevious < 0 || MovementToNext < 0)
                ? -1 : (MovementFromPrevious + MovementToNext) / 2.0;

        // Blur factor
        [JsonProperty(nameof(BlurFactor))]
        public double BlurFactor { get; init; } // Higher = sharper

        // Calculated center point
        [JsonProperty(nameof(Center))]
        public PointF Center;

        // Estimated yaw and pitch
        [JsonProperty("Yaw")]
        public double YawDeg;
        [JsonProperty("Pitch")]
        public double PitchDeg;

        // The grid size for the sensor bin 
        public static (int x, int y) SensorBinGrid { get; } = (10, 7);

        // Note a single frame may occupy multiple sensor bins
        [JsonProperty("SensorBin")]
        public List<(int binx, int biny)> SensorBinsOccupied { get; set; } = [];

        // The grid layers pose bin 
        public static (int x, int y) PoseBinGrid { get; } = (5, 3);

        // A frame can only occupy a single pose bin
        [JsonProperty(nameof(PoseBinX))]
        public int PoseBinX { get; set; } = -1;

        [JsonProperty(nameof(PoseBinY))]
        public int PoseBinY { get; set; } = -1;

        [JsonIgnore]
        public static IReadOnlyList<double> PoseBinThresholdYaw => [-12, -6, 6, 12];
        
        [JsonIgnore]
        public static IReadOnlyList<double> PoseBinThresholdPitch => [-10, 10];

        // The grid layers depth bin 
        public static int DepthBinGrid { get; } = (4);

        // A frame can only occupy a single pose bin
        [JsonProperty(nameof(DepthBinZ))]
        public int DepthBinZ { get; set; } = -1;

        [JsonIgnore]
        public static IReadOnlyList<double> DepthBinThreshold => [0.55, 0.25, 0.125];  // Near >= 55% > Mid >= 25% > Far >= 12.5% > Deep


        // Calibration Parameter Count
        [JsonIgnore]
        private static readonly int calibParamCount = Enum.GetValues<CalibrationParameters>().Length;

        // Mono Frame quality tests        
        [JsonProperty(nameof(monoProjectedPoints))]
        public PointF[][] monoProjectedPoints = new PointF[calibParamCount][];
        [JsonProperty(nameof(monoFrameRms))]
        public double[] monoFrameRms = new double[calibParamCount];
        [JsonProperty(nameof(monoFrameMaxError))]
        public double[] monoFrameMaxError = new double[calibParamCount];

        // Stereo calibration specific
        [JsonProperty("StereoSharedCorners")]
        public PointF[][] StereoSharedChArUcoCorners = new PointF[calibParamCount][];
        [JsonProperty("StereoSharedIds")]
        public int[][] StereoSharedChArUcoIDs = new int[calibParamCount][];

        // Stereo Frame quality tests        
        [JsonProperty(nameof(stereoProjectedPoints))]
        public PointF[][] stereoProjectedPoints = new PointF[calibParamCount][];
        [JsonProperty(nameof(stereoFrameRms))]
        public double[] stereoFrameRms = new double[calibParamCount];
        [JsonProperty(nameof(stereoFrameMaxError))]
        public double[] stereoFrameMaxError = new double[calibParamCount];


        // Parameter-less constructor for de-serialization
        [JsonConstructor]
        public FrameData()
        {
            // Version will remain -1 if not in the JSON
        }

        public FrameData(CalibrationBoardDefinition chArUcoBoardDefinition, int frameIndex, Mat grayFrame, PointF[] chArUcoCorners, int[] ChArUcoIds, int frameWidth, int frameHeight)
        {
            // Set the Version
            Version = version;

            // Is this a meaningful records
            if (chArUcoCorners == null || chArUcoCorners.Length == 0)
                return;

            // Store static data
            FrameIndex = frameIndex;
            ChArUcoCorners = chArUcoCorners;
            this.ChArUcoIds = ChArUcoIds;

            // One off dynamic data (because we don't store the source static data)
            BlurFactor = CalculateBlur(grayFrame);

            // Calculate Sensor bin coverage and center
            Center = CalculateCenter(ChArUcoCorners);
            SensorBinsOccupied = GetBinsForCharucoCorners(ChArUcoCorners, frameWidth, frameHeight);
            if ((frameIndex == 509 && chArUcoCorners.Length == 104) ||
                (frameIndex == 839 && chArUcoCorners.Length == 91) ||
                (frameIndex == 2717 && chArUcoCorners.Length == 100))
                Debug.WriteLine("break");
            // Calculate depth index
            DepthBinZ = CalculateDepthIndex(chArUcoBoardDefinition);
        }


        /// <summary>
        /// Calculate the depth index (near,med, far)
        /// by using the sensor coverage and scaling up for the 
        /// amount of the board that is detected
        /// </summary>
        /// <returns></returns>
        private int CalculateDepthIndex(CalibrationBoardDefinition chArUcoBoardDefinition)
        {
            // Guard – need sensor coverage and corner/id data
            if (SensorBinsOccupied is null ||
                SensorBinsOccupied.Count == 0 ||
                ChArUcoIds is null ||
                ChArUcoIds.Length == 0)
            {
                
                return -1;
            }

            // 1. Sensor coverage (0..1) from occupied sensor bins
            var (gx, gy) = FrameData.SensorBinGrid;
            int totalSensorBins = gx * gy;
            if (totalSensorBins <= 0)
            {
                return -1;
            }

            double sensorCoveragePercent =
                (double)SensorBinsOccupied.Count / totalSensorBins; // 0..1

            // 2. Estimate visible board fraction from IDs
            int squaresX = chArUcoBoardDefinition.SquaresX;
            int squaresY = chArUcoBoardDefinition.SquaresY;

            int totalCorners = Math.Max((squaresX - 1) * (squaresY - 1), 1);

            int minIx = int.MaxValue;
            int maxIx = int.MinValue;
            int minIy = int.MaxValue;
            int maxIy = int.MinValue;

            // Assume CharUcO IDs are laid out row-major over inner corners:
            // ix = id % (squaresX - 1), iy = id / (squaresX - 1)
            int innerWidth = squaresX - 1;
            int innerHeight = squaresY - 1;

            if (innerWidth <= 0 || innerHeight <= 0)
            {
                return -1;
            }

            foreach (int id in ChArUcoIds)
            {
                int ix = id % innerWidth;
                int iy = id / innerWidth;

                if (ix < 0 || iy < 0 || ix >= innerWidth || iy >= innerHeight)
                    continue;

                if (ix < minIx) minIx = ix;
                if (ix > maxIx) maxIx = ix;
                if (iy < minIy) minIy = iy;
                if (iy > maxIy) maxIy = iy;
            }

            double boardFraction = 1.0;

            if (minIx != int.MaxValue && minIy != int.MaxValue)
            {
                int widthCorners = maxIx - minIx + 1;
                int heightCorners = maxIy - minIy + 1;

                widthCorners = Math.Clamp(widthCorners, 1, innerWidth);
                heightCorners = Math.Clamp(heightCorners, 1, innerHeight);

                int visibleCorners = widthCorners * heightCorners;
                if (visibleCorners > 0)
                {
                    boardFraction = Math.Clamp(
                        (double)visibleCorners / totalCorners,
                        0.05,  // avoid blowing up coverage when only a tiny patch is seen
                        1.0);
                }
            }

            // 3. Adjust sensor coverage to "full-board equivalent"
            double adjustedSensorCoveragePercent = sensorCoveragePercent / boardFraction;
            adjustedSensorCoveragePercent = Math.Clamp(adjustedSensorCoveragePercent, 0.0, 1.0);

            // 4. Map adjusted coverage into depth bin index, using DepthBinThreshold / DepthBinGrid
            int depthBins = FrameData.DepthBinGrid;
            if (depthBins <= 0)
            {
                return -1;
            }

            // Single bin: everything maps to 0
            if (depthBins == 1)
            {
                return 0;
            }

            double value = adjustedSensorCoveragePercent;
            int binIndex = depthBins - 1; // default to last bin

            var thresholds = FrameData.DepthBinThreshold;
            int maxThresholdsUsed = Math.Min(thresholds.Count, depthBins - 1);

            for (int i = 0; i < maxThresholdsUsed; i++)
            {
                if (value >= thresholds[i])
                {
                    binIndex = i;
                    break;
                }
            }

            return binIndex;
        }

        /// <summary>
        /// Used to calculates or recalculate the dynamic fields
        /// </summary>
        /// <param name="resolutionX"></param>
        /// <param name="resolutionY"></param>
        //???public void CalculateDynamicFrameData(int resolutionX, int resolutionY)
        //{
        //    Center = CalculateCenter(ChArUcoCorners);            
        //    SensorBinsOccupied = GetBinsForCharucoCorners(ChArUcoCorners, resolutionX, resolutionY);
        //}


        /// Calculates the average movement (Euclidean distance) between matching ChArUco corners
        /// from frame `a` to frame `b`. The result is symmetric: movement from `a` to `b` equals
        /// movement from `b` to `a`.
        public static double CalculateCornerMovement(FrameData a, FrameData b)
        {
            var dictA = a.ChArUcoIds.Select((id, i) => (id, a.ChArUcoCorners[i])).ToDictionary(t => t.id, t => t.Item2);
            var dictB = b.ChArUcoIds.Select((id, i) => (id, b.ChArUcoCorners[i])).ToDictionary(t => t.id, t => t.Item2);

            var commonIds = dictA.Keys.Intersect(dictB.Keys).ToList();

            // If there are no common ArUco markers found between the two boards, return -1
            if (commonIds.Count == 0)
                return -1;

            double totalDist = 0;
            foreach (var id in commonIds)
            {
                var p1 = dictA[id];
                var p2 = dictB[id];
                totalDist += Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
            }

            return totalDist / commonIds.Count;
        }

        public static PointF CalculateCenter(PointF[] corners)
        {
            if (corners == null || corners.Length == 0) return new PointF(0, 0);
            float x = corners.Sum(c => c.X) / corners.Length;
            float y = corners.Sum(c => c.Y) / corners.Length;
            return new PointF(x, y);
        }

        public static double CalculateBlur(Mat grayFrame)
        {
            using var laplacian = new Mat();
            CvInvoke.Laplacian(grayFrame, laplacian, DepthType.Cv64F);
            using var mean = new Mat();
            using var stddev = new Mat();
            CvInvoke.MeanStdDev(laplacian, mean, stddev);
            return ((double[,])stddev.GetData())[0, 0];
        }




        public static (double? yawProxyDeg, double? pitchProxyDeg) EstimateYawPitch(PointF[] chArUcoCorners,
                                                                       int[] chArUcoIds,
                                                                       CharucoBoard board,
                                                                       Size imageSize)
        {
            if (chArUcoCorners.Length == 0 || chArUcoIds.Length == 0)
                return (null, null);

            using var cornersVec = new VectorOfPointF(chArUcoCorners);
            using var idsVec = new VectorOfInt(chArUcoIds);

            // Estimate a default camera matrix assuming fx=fy and center in image center
            double focalLength = 0.9 * Math.Max(imageSize.Width, imageSize.Height);
            var cameraMatrix = new Matrix<double>(3, 3);
            cameraMatrix.SetZero();
            cameraMatrix[0, 0] = focalLength;
            cameraMatrix[1, 1] = focalLength;
            cameraMatrix[0, 2] = imageSize.Width / 2.0;
            cameraMatrix[1, 2] = imageSize.Height / 2.0;
            cameraMatrix[2, 2] = 1.0;

            var distCoeffs = new Matrix<double>(1, 5);
            distCoeffs.SetZero();

            var rvec = new Mat();
            var tvec = new Mat();

            bool success = ArucoInvoke.EstimatePoseCharucoBoard(
                cornersVec, idsVec, board, cameraMatrix.Mat, distCoeffs.Mat, rvec, tvec);

            if (!success || rvec.IsEmpty)
                return (null, null);

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
            double yawRad = Math.Atan2(r10, r00);                             // Rotation around Z
            double pitchRad = Math.Atan2(-r20, Math.Sqrt(r21 * r21 + r22 * r22)); // Rotation around Y

            double yawDeg = yawRad * 180.0 / Math.PI;
            double pitchDeg = pitchRad * 180.0 / Math.PI;

            return (yawDeg, pitchDeg);
        }

        public double Score
        {
            get
            {
                if (ChArUcoCorners == null || ChArUcoCorners.Length <= 0 || BlurFactor <= 0 || MovementFactor < 0)
                    return 0;

                // Strong preference for low movement — 5 is excellent, 10 is okay, 20+ is poor
                double movementScore = Math.Clamp(30.0 / MovementFactor, 0.0, 1.0); // Emphasizes movement more than before

                // Slightly relaxed blur weighting — 2–4 is great, 5–7 is okay, 10+ is poor
                double blurScore = Math.Clamp(10.0 / BlurFactor, 0.0, 1.0);

                // Use 104 as your actual ChArUco max corner count (for 14x9)
                double cornerScore = Math.Clamp(ChArUcoCorners.Length / 104.0, 0.0, 1.0);

                // Weighting — prioritize movement, then blur, then corners
                return 0.6 * movementScore + 0.3 * blurScore + 0.1 * cornerScore;
            }
        }


        /// <summary>
        /// Calculates which bins for the ChArUco corners fit into based on grid layers.
        /// </summary>
        /// <param name="corners"></param>
        /// <param name="resolutionX"></param>
        /// <param name="resolutionY"></param>
        /// <returns></returns>
        private static List<(int binX, int binY)> GetBinsForCharucoCorners(PointF[] corners, int resolutionX, int resolutionY)
        {
            List<(int binX, int binY)> bins = [];
            foreach (var corner in corners)
            {
                var (gx, gy) = SensorBinGrid;

                int binX = Math.Clamp((int)(corner.X / (resolutionX / (double)gx)), 0, gx - 1);
                int binY = Math.Clamp((int)(corner.Y / (resolutionY / (double)gy)), 0, gy - 1);

                // Check if already there
                if (bins.Contains((binX, binY)))
                    continue;

                // If not add
                bins.Add((binX, binY));

            }
            return bins;
        }


        /*** End of FrameCalibrationTarget ***/
    }

}
