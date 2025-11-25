using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Surveyor.Calibration
{
    /// <summary>
    /// A FrameCalibrationTarget instance represents metadata on a single frame where
    /// that frame is observed to have a detectable Charuco Calibration target.
    /// The instance has a calculate of how 'still' this frame was from the previous to the 
    /// next frame (MovementFactor) and separately a BlurFactor is calculated.
    /// </summary>
    public class FrameCalibrationData
    {
        // Version of the class (use for data migrations)
        private const int version = 4;

        // Data Version
        public int Version { get; set; } = -1;      // This is so serialized records with a Version get a -1, the version is set in the main constructor

        // The frame index of the calibration target
        public int FrameIndex { get; init; }

        // Corners and IDs
        public PointF[] CharucoCorners { get; init; } = [];
        public int[] CharucoIds { get; init; } = [];


        // Movement factors
        public double MovementFromPrevious { get; set; }
        public double MovementToNext { get; set; }
        public double MovementFactor => (MovementFromPrevious < 0 || MovementToNext < 0)
                ? -1 : (MovementFromPrevious + MovementToNext) / 2.0;

        // Blur factor
        public double BlurFactor { get; init; } // Higher = sharper


        // Calculated centre point
        public PointF Center;

        // Estimated yaw and pitch
        public double YawDeg;
        public double PitchDeg;

        // The grid layers for each sensor bin (currently only one layer is used)
        public static (int x, int y)[] SensorBinGridLayers { get; } = [(10, 7)];
        public List<(int gx, int gy, int binx, int biny)> SensorBinsOccupied { get; set; } = [];

        // The grid layers pose bin 
        public static (int x, int y) PoseBinGrid { get; } = (3, 3);
        public List<(int binx, int biny)> PoseBinsOccupied { get; set; } = [];

        [JsonIgnore]
        public static IReadOnlyList<double> PoseBinThresholdYaw => [-10, 10, 90.1];
        
        [JsonIgnore]
        public static IReadOnlyList<double> PoseBinThresholdPitch => [-10, 10, 90.1 ];

        // Calibration Parameter Count
        private static readonly int calibParamCount = Enum.GetValues<CalibrationParameters>().Length;

        // Mono Frame quantily tests        
        public PointF[][] monoProjectedPoints = new PointF[calibParamCount][];
        public double[] monoFrameRms = new double[calibParamCount];
        public double[] monoFrameMaxError = new double[calibParamCount];

        // Stereo calibration specific
        public PointF[][] StereoSharedCharucoCorners = new PointF[calibParamCount][];
        public int[][] StereoSharedCharucoIDs = new int[calibParamCount][];
        public PointF[][] stereoProjectedPoints = new PointF[calibParamCount][];
        public double[] stereoFrameRms = new double[calibParamCount];
        public double[] stereoFrameMaxError = new double[calibParamCount];


        // Parameterless constructor for deserialization
        [JsonConstructor]
        public FrameCalibrationData()
        {
            // Version will remain -1 if not in the JSON
        }

        public FrameCalibrationData(int frameIndex, Mat grayFrame, PointF[] charucoCorners, int[] charucoIds, int frameWidth, int frameHeight)
        {
            // Set the Version
            Version = version;

            // Is this a meaningful records
            if (charucoCorners == null || charucoCorners.Length == 0)
                return;

            // Store static data
            FrameIndex = frameIndex;
            CharucoCorners = charucoCorners;
            CharucoIds = charucoIds;

            // One off dynamic data (because we don't store the source static data)
            BlurFactor = CalculateBlur(grayFrame);

            // Calculate dynamic data
            CalcDynamicFrameData(frameWidth, frameHeight);
        }


        /// <summary>
        /// Used to calculates or recalucate the dynamic fields
        /// </summary>
        /// <param name="resolutionX"></param>
        /// <param name="resolutionY"></param>
        public void CalcDynamicFrameData(int resolutionX, int resolutionY)
        {
            Center = CalculateCenter(CharucoCorners);            
            SensorBinsOccupied = GetBinsForCharucoCorners(CharucoCorners, resolutionX, resolutionY);
        }


        /// Calculates the average movement (Euclidean distance) between matching Charuco corners
        /// from frame `a` to frame `b`. The result is symmetric: movement from `a` to `b` equals
        /// movement from `b` to `a`.
        public static double CalculateCornerMovement(FrameCalibrationData a, FrameCalibrationData b)
        {
            var dictA = a.CharucoIds.Select((id, i) => (id, a.CharucoCorners[i])).ToDictionary(t => t.id, t => t.Item2);
            var dictB = b.CharucoIds.Select((id, i) => (id, b.CharucoCorners[i])).ToDictionary(t => t.id, t => t.Item2);

            var commonIds = dictA.Keys.Intersect(dictB.Keys).ToList();

            // If there are no common acros found between the two boards, return -1
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




        public static (double? yawProxyDeg, double? pitchProxyDeg) EstimateYawPitch(PointF[] charucoCorners,
                                                                       int[] charucoIds,
                                                                       CharucoBoard board,
                                                                       Size imageSize)
        {
            if (charucoCorners.Length == 0 || charucoIds.Length == 0)
                return (null, null);

            using var cornersVec = new VectorOfPointF(charucoCorners);
            using var idsVec = new VectorOfInt(charucoIds);

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


        //public double Score
        //{
        //    get
        //    {
        //        if (CharucoCorners == null || CharucoCorners.Length == 0 || BlurFactor <= 0)
        //            return 0;

        //        double blurScore = Math.Clamp(10.0 / BlurFactor, 0.0, 1.0);
        //        double movementScore = MovementFactor == 0 ? 1.0 : Math.Clamp(20.0 / MovementFactor, 0.0, 1.0);
        //        double cornerScore = Math.Clamp(CharucoCorners.Length / 104.0, 0.0, 1.0);

        //        return 0.4 * blurScore + 0.4 * movementScore + 0.2 * cornerScore;
        //    }
        //}
        public double Score
        {
            get
            {
                if (CharucoCorners == null || CharucoCorners.Length <= 0 || BlurFactor <= 0 || MovementFactor < 0)
                    return 0;

                // Strong preference for low movement — 5 is excellent, 10 is okay, 20+ is poor
                double movementScore = Math.Clamp(30.0 / MovementFactor, 0.0, 1.0); // Emphasizes movement more than before

                // Slightly relaxed blur weighting — 2–4 is great, 5–7 is okay, 10+ is poor
                double blurScore = Math.Clamp(10.0 / BlurFactor, 0.0, 1.0);

                // Use 104 as your actual charuco max corner count (for 14x9)
                double cornerScore = Math.Clamp(CharucoCorners.Length / 104.0, 0.0, 1.0);

                // Weighting — prioritize movement, then blur, then corners
                return 0.6 * movementScore + 0.3 * blurScore + 0.1 * cornerScore;
            }
        }


        /// <summary>
        /// Calculates which bins for the Charuco corners fit into based on grid layers.
        /// </summary>
        /// <param name="corners"></param>
        /// <param name="resolutionX"></param>
        /// <param name="resolutionY"></param>
        /// <returns></returns>
        private static List<(int gx, int gy, int binX, int binY)> GetBinsForCharucoCorners(PointF[] corners, int resolutionX, int resolutionY)
        {
            List<(int gx, int gy, int binX, int binY)> bins = [];
            foreach (var corner in corners)
            {
                foreach (var (gx, gy) in SensorBinGridLayers)
                {
                    int binX = Math.Clamp((int)(corner.X / (resolutionX / (double)gx)), 0, gx - 1);
                    int binY = Math.Clamp((int)(corner.Y / (resolutionY / (double)gy)), 0, gy - 1);

                    // Check if already there
                    if (bins.Contains((gx, gy, binX, binY)))
                        continue;

                    // If not add
                    bins.Add((gx, gy, binX, binY));
                }
            }
            return bins;
        }


        /*** End of FrameCalibrationTarget ***/
    }

}
