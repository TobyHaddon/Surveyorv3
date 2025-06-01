using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Cryptography.Core;

namespace Surveyor.Calibration
{
    /// <summary>
    /// A FrameCalibrationTarget instance represents metadata on a single frame where
    /// that frame is observed to have a detectable Charuco Calibration target.
    /// The instance has a calculate of how 'still' this frame was from the previous to the 
    /// next frame (MovementFactor) and separately a BlurFactor is calculated.
    /// </summary>
    public class FrameCalibrationTarget
    {
        // The frame index of the calibration target
        public int FrameIndex { get; init; }

        // Movement factors
        public double MovementFromPrevious { get; set; }
        public double MovementToNext { get; set; }
        public double MovementFactor => (MovementFromPrevious < 0 || MovementToNext < 0)
                ? -1 : (MovementFromPrevious + MovementToNext) / 2.0;

        // Blur factor
        public double BlurFactor { get; init; } // Higher = sharper


        // Calculated centre point
        public PointF Center;
        public PointF[] CharucoCorners { get; init; } = Array.Empty<PointF>();
        public int[] CharucoIds { get; init; } = [];


        // The grid layers for each bin (currently only one layer is used)
        public static (int x, int y)[] GridLayers { get; } = [(10, 7)];
        public List<(int gx, int gy, int binx, int biny)> BinsOccupied { get; init; } = [];


        public FrameCalibrationTarget(int frameIndex, Mat grayFrame, PointF[] charucoCorners, int[] charucoIds, int resolutionX, int resolutionY)
        {
            if (charucoCorners == null || charucoCorners.Length == 0)
                return;

            var bins = GetBinsForCharucoCorners(charucoCorners, resolutionX, resolutionY);

            FrameIndex = frameIndex;
            Center = CalculateCenter(charucoCorners);
            CharucoCorners = charucoCorners;
            CharucoIds = charucoIds;
            BlurFactor = CalculateBlur(grayFrame);
            BinsOccupied = bins;
        }


        /// Calculates the average movement (Euclidean distance) between matching Charuco corners
        /// from frame `a` to frame `b`. The result is symmetric: movement from `a` to `b` equals
        /// movement from `b` to `a`.
        public static double CalculateCornerMovement(FrameCalibrationTarget a, FrameCalibrationTarget b)
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


        public double Score
        {
            get => MovementFactor == -1 ? 0 : (MovementFactor + 0.01) * (CharucoCorners.Length / 10 + 0.01) * (BlurFactor + 0.01);
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
                foreach (var (gx, gy) in GridLayers)
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
