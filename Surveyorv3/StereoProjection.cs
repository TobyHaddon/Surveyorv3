// StereoProjection
// Stereo projection maths support
// 
// Previous:
// Version 1.1  02 Feb 2025
//  Added code to calculate the RMS
// 
// Version 1.2  30 Aug 2025
//  Added optional calibrationDataIndex parameter to several public methods (except CalculateEpipolarPoints)
//   to select which CalibrationDataList item to use; defaults to PreferredCalibrationDataIndex.
// Added ResolveIndex helper and bounds checks; minor logging adjustments.
//

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Surveyor.Helper;
using Surveyor.User_Controls;
using System;
using System.Text;
using Windows.Foundation;   // We use the Point class from here
using SurveyorCalibrationData;


namespace Surveyor
{
    /// <summary>
    /// StereoProjection Version 1.3
    /// This class is used to calculate the distance between a pair of corresponding 2D points in the left and right images
    /// It intentionally uses a mixture of Emgu.CV and MathNET.Numerics types and also System.Drawing (where the System.Windows.Point and System.Drawing.Point types are not compatible)
    /// Modifed for WinUI3
    /// </summary>

    public class StereoProjection
    {
        private Reporter? report;
        private Survey.DataClass.CalibrationClass? calibrationClass = null;
        private Survey.DataClass.SurveyRulesClass? surveyRulesClass = null;

        // This string is used to check if calibrationClass has changed 
        private string calibationDataUniqueString = "";

        // Calulated variables.  These calulcated values at declared to be in parallel with the  
        // calibrationClass.CalibrationDataList. 
        private Matrix<double>?[]? essentialMatrixArray = null; /*Matrix<double>(3, 3);*/
        private Matrix<double>?[]? fundamentalMatrixArray = null;
        private MCvPoint3D64f?[]? cameraSystemCentreArray = null;

        // Remembered 2D measurement points
        private Point? LPointA = null;
        private Point? LPointB = null;
        private Point? RPointA = null;
        private Point? RPointB = null;

        // Calculated 3D versions of the 2D measurement points
        private MCvPoint3D64f?[]? vecAUndistortedArray = null;
        private MCvPoint3D64f?[]? vecBUndistortedArray = null;

        // RMS errors
        private double?[]? RMSErrorAArray = null;
        private double?[]? RMSErrorBArray = null;

        // Calculated mid-point of the 3D measurement points 
        private MCvPoint3D64f?[]? vecABMidArray = null;

        // Frame width and height
        private int frameWidth = -1;
        private int frameHeight = -1;


        private static bool InBounds(int i, int n) => i >= 0 && i < n;
        private int PreferredIndex => calibrationClass?.PreferredCalibrationDataIndex ?? -1;

        private int ResolveIndex(int calibrationDataIndex)
        {
            return calibrationDataIndex >= 0 ? calibrationDataIndex : PreferredIndex;
        }

        private double? GetPreferred(double?[]? arr)
        {
            var idx = PreferredIndex;
            if (arr is null || !InBounds(idx, arr.Length)) return null;
            return arr[idx];
        }

        private double? RMSErrorA_Current => GetPreferred(RMSErrorAArray);
        private double? RMSErrorB_Current => GetPreferred(RMSErrorBArray);


        /// <summary>
        /// Constructor
        /// </summary>
        public StereoProjection()
        {

        }


        /// <summary>
        /// Diags dump of class information
        /// </summary>
        public void DumpAllProperties(Reporter? report)
        {
            DumpClassPropertiesHelper.DumpAllProperties(this, report);
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


        /// <summary>
        /// Load the calilbration data.
        /// Can't to called and re-called multiple times.
        /// </summary>
        /// <param name="_calibrationClass"></param>
        public void SetCalibrationData(Survey.DataClass.CalibrationClass _calibrationClass)
        {
            // Remember the calibrtation data instance
            calibrationClass = _calibrationClass;

            // Reset            
            essentialMatrixArray = null;
            fundamentalMatrixArray = null;
            cameraSystemCentreArray = null;
            calibationDataUniqueString = "";
        }


        /// <summary>
        /// Clear the calibration data
        /// </summary>
        public void ClearCalibrationData()
        {
            calibrationClass = null;
        }
         

        /// <summary>
        /// This class has access the current survey rules instance so it can use the min and max range rule
        /// If it is setup.  
        /// </summary>
        /// <param name="_surveyRulesClass"></param>
        public void SetSurveyRules(Survey.DataClass.SurveyRulesClass _surveyRulesClass)
        {
            // Remember the survey rules instance
            surveyRulesClass = _surveyRulesClass;
        }


        /// <summary>
        /// Clear the survey rules 
        /// </summary>
        public void ClearSurveyRules()
        {
            surveyRulesClass = null;
        }


        /// <summary>
        /// Set the frame size that the current video is running at.
        /// This information is used to ensure a suitable calibration data instance is used.
        /// </summary>
        /// <param name="_frameWidth"></param>
        /// <param name="_frameHeight"></param>
        public void SetFrameSize(int _frameWidth, int _frameHeight)
        {
            // Reset
            essentialMatrixArray = null;
            fundamentalMatrixArray = null;
            cameraSystemCentreArray = null;
            calibationDataUniqueString = "";

            // Set the frame size
            frameWidth = _frameWidth;
            frameHeight = _frameHeight;

            // Check if the calibration data is ready
            // This will force a re-calculation of the essential and fundamental matrices if necessary
            IsReadyCalibrationData();
        }


        /// <summary>
        /// Reset the frame size
        /// </summary>
        public void ResetFrameSize()
        {
            // Set the frame size
            frameWidth = -1;
            frameHeight = -1;
        }


        /// <summary>
        /// Return the calibration ID (Guid) of the preferred calibration data instance
        /// </summary>
        /// <returns></returns>
        public Guid? GetCalibrationID()
        {
            Guid? ret = null;

            if (calibrationClass is not null)
            {
                // Compute the essential matrix
                CalibrationData? cdp = calibrationClass.GetPreferredCalibationData(frameWidth, frameHeight);

                if (cdp is not null)
                {
                    ret = cdp.CalibrationID;
                }
            }

            return ret;
        }


        /// <summary>
        /// Load measurement points A & B from the left camera and their corresponding points on the
        /// right camera. These are used as input for the operation method below
        /// </summary>
        /// <param name="LPointA"></param>
        /// <param name="LPointB"></param>
        /// <param name="RPointA"></param>
        /// <param name="RPointB"></param>
        /// <returns></returns>
        public bool PointsLoad(Point? _LPointA, Point? _LPointB, Point? _RPointA, Point? _RPointB)
        {
            bool ret = false;

            // Reset
            PointsClear();

            if (calibrationClass is not null)
            {
                if (_LPointA is not null && _RPointA is not null && _LPointB is not null && _RPointB is not null)
                {
                    LPointA = _LPointA;
                    LPointB = _LPointB;
                    RPointA = _RPointA;
                    RPointB = _RPointB;

                    ret = true;
                }
            }

            return ret;
        }

        /// <summary>
        /// Load measurement point A only from the left camera and the corresponding point on the
        /// right camera. 
        /// </summary>
        /// <param name="LPointA"></param>
        /// <param name="RPointA"></param>
        /// <returns></returns>
        public bool PointsLoad(Point? _LPointA, Point? _RPointA)
        {
            bool ret = false;

            // Reset
            PointsClear();

            if (calibrationClass is not null)
            {
                if (_LPointA is not null && _RPointA is not null)
                {
                    LPointA = _LPointA;
                    LPointB = null;
                    RPointA = _RPointA;
                    RPointB = null;

                    ret = true;
                }
            }

            return ret;
        }


        /// <summary>
        /// Clear the remembered 2D and 3D points
        /// </summary>
        public void PointsClear()
        {
            LPointA = null;
            LPointB = null;
            RPointA = null;
            RPointB = null;

            // Reset calulated variables
            vecAUndistortedArray = null;
            vecBUndistortedArray = null;
            vecABMidArray = null;

            // Reset RMS errors
            RMSErrorAArray = null;
            RMSErrorBArray = null;
        }


        /// <summary>
        /// Calulate the distane between the two measurement points
        /// </summary>
        /// <returns></returns>
        public double? Measurement(int calibrationDataIndex = -1)
        {
            double? ret = null;

            if (IsReadyCalibrationData())
            {

                if (IsReadyUndistortedPoints() && calibrationClass is not null)
                {
                    int idx = ResolveIndex(calibrationDataIndex);
                    if (!InBounds(idx, calibrationClass.CalibrationDataList.Count)) return null;

                    MCvPoint3D64f? vecA = vecAUndistortedArray![idx];
                    MCvPoint3D64f? vecB = vecBUndistortedArray![idx];

                    // Selected calibration data instance measure calculation
                    if (vecA is not null && vecB is not null)
                    {
                        ret = DistanceBetween3DPoints((MCvPoint3D64f)vecA, (MCvPoint3D64f)vecB);
                        report?.Out(Reporter.WarningLevel.Info, "", $"---Length using {(idx == calibrationClass.PreferredCalibrationDataIndex ? "preferred" : "selected")} Calibration Data[{calibrationClass!.CalibrationDataList[idx].Description}] Measurement = {Math.Round((double)ret * 1000,1)}mm");
                    }

                    // If default was used (preferred) and no explicit index provided, also log other available calibrations for info
                    if (calibrationDataIndex == -1)
                    {
                        for (int i = 0; i < calibrationClass!.CalibrationDataList.Count; i++)
                        {
                            if (i != calibrationClass!.PreferredCalibrationDataIndex)
                            {
                                if (calibrationClass!.CalibrationDataList[i].FrameSizeCompare(frameWidth, frameHeight))
                                {
                                    vecA = vecAUndistortedArray![i];
                                    vecB = vecBUndistortedArray![i];

                                    if (vecA is not null && vecB is not null)
                                    {
                                        double measurementAlt = DistanceBetween3DPoints((MCvPoint3D64f)vecA, (MCvPoint3D64f)vecB);
                                        report?.Out(Reporter.WarningLevel.Info, "", $"---Length using non-preferred Calibration Data[{calibrationClass!.CalibrationDataList[i].Description}] Measurement = {Math.Round(measurementAlt * 1000,1)}mm");
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Calcualte the reproject error either for:
        /// LPointA & RPointA if TRUEPointAFALSEPointBNullBoth is True
        /// LPointB & RPointB if TRUEPointAFALSEPointBNullBoth is False
        /// or the mean of LPointA & RPointA and LPointB & RPointB if TRUEPointAFALSEPointBNullBoth is null
        /// </summary>
        /// <param name="TRUEPointAFALSEPointBNullBoth"></param>
        /// <returns></returns>
        public double? ReprojectionError(bool? TRUEPointAFALSEPointBNullBoth)
        {
            double? ret = null;
           
            //if (IsReadyUndistortedPoints())
            //{

            //    // Corresponding 2D points in left and right images
            //    PointF[] points1 = new PointF[] { new PointF(LPointA.Value.X, LPointA.Value.Y), new PointF(LPointB.Value.X, LPointB.Value.Y) /*, ... */ };
            //    PointF[] points2 = new PointF[] { new PointF(RPointA.Value.X, RPointB.Value.Y), new PointF(RPointB.Value.X, RPointB.Value.Y) /*, ... */ };

            //    // Compute disparity
            //    double[] disparity = new double[points1.Length];
            //    for (int i = 0; i < points1.Length; i++)
            //    {
            //        disparity[i] = points1[i].X - points2[i].X;
            //    }

            //    // Reconstruct 3D points
            //    Matrix<double> P1 = K1 * Matrix<double>.Identity(3, 4);
            //    Matrix<double> P2 = K2 * (R.ConcateHorizontal(T));

            //    Matrix<double> points1Homogeneous = new Matrix<double>(3, points1.Length);
            //    Matrix<double> points2Homogeneous = new Matrix<double>(3, points2.Length);
            //    for (int i = 0; i < points1.Length; i++)
            //    {
            //        points1Homogeneous[0, i] = points1[i].X;
            //        points1Homogeneous[1, i] = points1[i].Y;
            //        points1Homogeneous[2, i] = 1.0;

            //        points2Homogeneous[0, i] = points2[i].X;
            //        points2Homogeneous[1, i] = points2[i].Y;
            //        points2Homogeneous[2, i] = 1.0;
            //    }

            //    Matrix<double> points4D = new Matrix<double>(4, points1.Length);
            //    CvInvoke.TriangulatePoints(P1, P2, points1Homogeneous, points2Homogeneous, points4D);

            //    // Convert from homogeneous coordinates
            //    Matrix<double> points3D = new Matrix<double>(3, points1.Length);
            //    for (int i = 0; i < points1.Length; i++)
            //    {
            //        points3D[0, i] = points4D[0, i] / points4D[3, i];
            //        points3D[1, i] = points4D[1, i] / points4D[3, i];
            //        points3D[2, i] = points4D[2, i] / points4D[3, i];
            //    }

            //    // Project 3D points back onto the image planes
            //    Matrix<double> rvec = new Matrix<double>(3, 1);
            //    Matrix<double> tvec = new Matrix<double>(3, 1);

            //    Matrix<double> reprojectedPoints1 = new Matrix<double>(points1.Length, 2);
            //    Matrix<double> reprojectedPoints2 = new Matrix<double>(points2.Length, 2);

            //    CvInvoke.ProjectPoints(points3D, rvec, tvec, K1, null, reprojectedPoints1);
            //    CvInvoke.ProjectPoints(points3D, R, T, K2, null, reprojectedPoints2);

            //    // Calculate the reprojection error
            //    double totalError = 0;
            //    for (int i = 0; i < points1.Length; i++)
            //    {
            //        double error1 = Math.Sqrt(Math.Pow(points1[i].X - reprojectedPoints1[i, 0], 2) + Math.Pow(points1[i].Y - reprojectedPoints1[i, 1], 2));
            //        double error2 = Math.Sqrt(Math.Pow(points2[i].X - reprojectedPoints2[i, 0], 2) + Math.Pow(points2[i].Y - reprojectedPoints2[i, 1], 2));
            //        totalError += (error1 + error2) / 2;
            //    }

            //    double meanError = totalError / points1.Length;
            //}

            return ret;
        }


        /// <summary>
        /// Calulcate the distance from the centre point of the camera system to the centre point 
        /// of the measurement points
        /// </summary>
        /// <returns></returns>
        public double? RangeFromCameraSystemCentrePointToMeasurementCentrePoint(int calibrationDataIndex = -1)
        {
            double? ret = null;

            // Check if the calibration data and the undistored points are ready
            if (IsReadyUndistortedPoints() && calibrationClass is not null)
            {
                int idx = ResolveIndex(calibrationDataIndex);
                if (!InBounds(idx, calibrationClass.CalibrationDataList.Count)) return null;

                MCvPoint3D64f? cameraSystemCentre = cameraSystemCentreArray![idx];
                MCvPoint3D64f? vecABMid = vecABMidArray![idx];

                if (cameraSystemCentre is not null && vecABMid is not null)
                {
                    ret = DistanceBetween3DPoints((MCvPoint3D64f)cameraSystemCentre, (MCvPoint3D64f)vecABMid);
                }
            }


            return ret;
        }


        /// <summary>
        /// Calculate the distance in the X direction between the camera system centre and the 
        /// centre of the measurement points
        /// </summary>
        /// <returns></returns>
        public double? XOffsetFromCameraSystemCentrePointToMeasurementCentrePoint(int calibrationDataIndex = -1)
        {
            double? ret = null;

            // Check if the calibration data and the undistored points are ready
            if (IsReadyUndistortedPoints() && calibrationClass is not null)
            { 
                int idx = ResolveIndex(calibrationDataIndex);
                if (!InBounds(idx, calibrationClass.CalibrationDataList.Count)) return null;

                MCvPoint3D64f? cameraSystemCentre = cameraSystemCentreArray![idx];
                MCvPoint3D64f? vecABMid = vecABMidArray![idx];

                if (cameraSystemCentre is not null && vecABMid is not null)
                {
                    ret = ((MCvPoint3D64f)vecABMid).X - ((MCvPoint3D64f)cameraSystemCentre).X;
                }
            }

            return ret;
        }


        /// <summary>
        /// Calculate the distance in the Y direction between the camera system centre and the 
        /// centre of the measurement points
        /// </summary>
        /// <returns></returns>
        public double? YOffsetFromCameraSystemCentrePointToMeasurementCentrePoint(int calibrationDataIndex = -1)
        {
            double? ret = null;

            // Check if the calibration data and the undistored points are ready
            if (IsReadyUndistortedPoints() && calibrationClass is not null)
            {
                int idx = ResolveIndex(calibrationDataIndex);
                if (!InBounds(idx, calibrationClass.CalibrationDataList.Count)) return null;

                MCvPoint3D64f? cameraSystemCentre = cameraSystemCentreArray![idx];
                MCvPoint3D64f? vecABMid = vecABMidArray![idx];

                if (cameraSystemCentre is not null && vecABMid is not null)
                {
                    ret = ((MCvPoint3D64f)vecABMid).Y - ((MCvPoint3D64f)cameraSystemCentre).Y;
                }
            }

            return ret;
        }


        /// <summary>
        /// Return the calculated RMS real world error for the measurement points
        /// Called can request either the RMS for a PointA set, PointB set or the worst case of both
        /// </summary>
        /// <param name="TruePointAFalsePointBNullWorstCase"></param>
        /// <returns></returns>
        public enum RMSMode
        {
            AOnly,
            BOnly,
            Mean,
            Quadrature,
            Worst
        }
        public double? RMS(RMSMode mode, int calibrationDataIndex = -1)
        {
            double? a, b;
            if (calibrationDataIndex == -1)
            {
                a = RMSErrorA_Current;
                b = RMSErrorB_Current;
            }
            else
            {
                if (RMSErrorAArray is null || RMSErrorBArray is null) return null;
                if (!InBounds(calibrationDataIndex, RMSErrorAArray.Length)) return null;
                a = RMSErrorAArray[calibrationDataIndex];
                b = RMSErrorBArray[calibrationDataIndex];
            }

            switch (mode)
            {
                case RMSMode.AOnly:
                    return a;
                case RMSMode.BOnly:
                    return b;

                case RMSMode.Mean:
                    if (a is null) return b;
                    if (b is null) return a;
                    return 0.5 * (a.Value + b.Value);

                case RMSMode.Quadrature:
                    if (a is null) return b;
                    if (b is null) return a;
                    return Math.Sqrt(0.5 * (a.Value * a.Value + b.Value * b.Value));

                case RMSMode.Worst:
                default:
                    if (a is null) return b;
                    if (b is null) return a;
                    return Math.Max(a.Value, b.Value);
            }
        }



        /// <summary>
        /// Get the individual RMS errors for the measurement points A & B
        /// </summary>
        /// <returns></returns>
        public (double? A, double? B) GetRMSElements(int calibrationDataIndex = -1)
        {
            if (calibrationDataIndex == -1)
            {
                return (RMSErrorA_Current, RMSErrorB_Current);
            }
            if (RMSErrorAArray is null || RMSErrorBArray is null) return (null, null);
            if (!InBounds(calibrationDataIndex, RMSErrorAArray.Length)) return (null, null);
            return (RMSErrorAArray[calibrationDataIndex], RMSErrorBArray[calibrationDataIndex]);
        }



        /// <summary>
        /// Calulcate the epipolar line for a given point (distorted point) in the left or right image
        /// </summary>
        /// <param name="TrueLeftFalseRight"></param>
        /// <param name="point"></param>
        /// <param name="epiLine_a"></param>
        /// <param name="epiLine_b"></param>
        /// <param name="epiLine_c"></param>
        /// <returns></returns>
        public bool CalculateEpipolarLine(int calibrationDataIndex,
                                          bool TrueLeftFalseRight,
                                          Point point,
                                          out double epiLine_a, out double epiLine_b, out double epiLine_c)
        {
            epiLine_a = epiLine_b = epiLine_c = 0.0;

            if (!IsReadyCalibrationData()) return false;
            var calibrationData = calibrationClass!.CalibrationDataList[calibrationDataIndex];
            if (calibrationData is null || !calibrationData.FrameSizeCompare(frameWidth, frameHeight))
                return false;

            // IMPORTANT: F was built from E and K (undistorted pinhole model).
            // So we must undistort the input pixel before using F.
            var sourceCal = TrueLeftFalseRight ? calibrationData.LeftCameraCalibration
                                               : calibrationData.RightCameraCalibration;
            var pU = UndistortPoint(sourceCal, point);

            var x = new Matrix<double>(3, 1);
            x[0, 0] = pU.X; x[1, 0] = pU.Y; x[2, 0] = 1.0;

            var F = fundamentalMatrixArray![calibrationDataIndex];
            if (F is null) return false;

            Matrix<double> line = TrueLeftFalseRight ? (F * x) : (F.Transpose() * x);

            double a = line[0, 0], b = line[1, 0], c = line[2, 0];
            double norm = Math.Sqrt(a * a + b * b);
            if (norm > 0) { a /= norm; b /= norm; c /= norm; }

            epiLine_a = a; epiLine_b = b; epiLine_c = c;
            return true;
        }

        public bool CalculateEpipolarLine(bool TrueLeftFalseRight, Point point, out double epiLine_a, out double epiLine_b, out double epiLine_c, 
                                          out double focalLength, out double baseline,
                                          out double principalXLeft, out double principalYLeft, out double principalXRight, out double principalYRight,
                                          int calibrationDataIndex = -1)
        {
            bool ret = false;

            // Reset
            epiLine_a = 0.0;
            epiLine_b = 0.0;
            epiLine_c = 0.0;
            focalLength = 0.0;
            baseline = 0.0;
            principalXLeft = 0.0;
            principalYLeft = 0.0;
            principalXRight = 0.0;
            principalYRight = 0.0;

            if (IsReadyCalibrationData() && calibrationClass is not null)
            {
                int idx = ResolveIndex(calibrationDataIndex);
                if (!InBounds(idx, calibrationClass.CalibrationDataList.Count)) return false;

                ret = CalculateEpipolarLine(idx,
                                                TrueLeftFalseRight,
                                                point,
                                                out epiLine_a,
                                                out epiLine_b,
                                                out epiLine_c);
                if (ret == true)
                {
                    // Get the selected calibration data instance
                    CalibrationData calibrationData = calibrationClass!.CalibrationDataList[idx];

                    // Extract focal length from left camera matrix
                    focalLength = calibrationData.LeftCameraCalibration.Intrinsic?[0, 0] ?? 0.0; 

                    var T = calibrationData.StereoCameraCalibration.Translation;
                    if (T is not null)
                    {
                        double tx = T[0, 0], ty = (T.Cols > 1 ? T[0, 1] : T[1, 0]), tz = (T.Cols > 2 ? T[0, 2] : T[2, 0]);
                        baseline = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                    }

                    // Extract principal point (cx, cy) from left camera matrix
                    principalXLeft = calibrationData.LeftCameraCalibration.Intrinsic?[0, 2] ?? 0.0;
                    principalYLeft = calibrationData.LeftCameraCalibration.Intrinsic?[1, 2] ?? 0.0;

                    // Extract principal point (cx, cy) from right camera matrix
                    principalXRight = calibrationData.RightCameraCalibration.Intrinsic?[0, 2] ?? 0.0;
                    principalYRight = calibrationData.RightCameraCalibration.Intrinsic?[1, 2] ?? 0.0;
                }
            }
            return ret;
        }


        /// <summary>
        /// Calculate the corresponding epipolar points for a given point in the left or right image
        /// Near, Middle and Far points are calculated. If the Range rule is active used the RangeMin and RangeMax for near and far.
        /// If the Range rule is not active then use near=0.4m, middle=(10-0.4/2)m and far=10m
        /// </summary>
        /// <param name="TrueLeftFalseRight"></param>
        /// <param name="point"></param>
        /// <param name="pointNear"></param>
        /// <param name="pointMiddle"></param>
        /// <param name="pointFar"></param>
        /// <returns></returns>
        public bool CalculateEpipolarPoints(bool TrueLeftFalseRight, Point point, out Point pointNear, out Point pointMiddle, out Point pointFar)
        {
            bool ret = false;
            pointNear = new Point(-1, -1);
            pointMiddle = new Point(-1, -1);
            pointFar = new Point(-1, -1);

            // Target distance  
            double nearTargetDistance = 0.4;
            double farTargetDistance = 10.0;

            // Check if the survey rules are active
            if (surveyRulesClass is not null && surveyRulesClass.SurveyRulesActive && surveyRulesClass.SurveyRulesData.RangeRuleActive)
            {
                nearTargetDistance = surveyRulesClass.SurveyRulesData.RangeMin;
                farTargetDistance = surveyRulesClass.SurveyRulesData.RangeMax;
            }

            // Calculate the middle target distance
            double middleTargetDistance = nearTargetDistance + (farTargetDistance - nearTargetDistance) / 2.0;

            // Calculate the corresponding points

            if (ComputeCorrespondingDistortedPointByDistanceFromTarget(TrueLeftFalseRight, point, nearTargetDistance, out Point? _pointNear))
            {
                if (ComputeCorrespondingDistortedPointByDistanceFromTarget(TrueLeftFalseRight, point, middleTargetDistance, out Point? _pointMiddle))
                {
                    if (ComputeCorrespondingDistortedPointByDistanceFromTarget(TrueLeftFalseRight, point, farTargetDistance, out Point? _pointFar))
                    {
                        // Set the output points
                        pointNear = _pointNear ?? new Point(-1, -1);
                        pointMiddle = _pointMiddle ?? new Point(-1, -1);
                        pointFar = _pointFar ?? new Point(-1, -1);

                        ret = true;
                    }
                }
            }

            return ret;
        }


        ///
        /// PRIVATE METHODS
        ///


        /// <summary>
        /// This method is used to check if the CalidrationClass has changed since and that the 
        /// The preferred calibration data instance is available and support the current frame size.
        /// For this is work SetCalibrationData() and SetFrameSize() must have been called and the cilbration data
        /// must support the current frame size.
        /// </summary>
        /// <returns></returns>
        public bool IsReadyCalibrationData()
        {
            if (calibrationClass is null ||
                calibrationClass.CalibrationDataList is null ||
                calibrationClass.CalibrationDataList.Count == 0 ||
                frameWidth <= 0 || frameHeight <= 0)
            {
                return false;
            }

            string newKey = MakeCalibationDataUniqueString();
            bool needRebuild = string.IsNullOrEmpty(calibationDataUniqueString) || calibationDataUniqueString != newKey;

            if (!needRebuild)
            {
                // We assume arrays were already built for this key+frame size
                // but still ensure preferred exists for current size.
                var preferred = calibrationClass.GetPreferredCalibationData(frameWidth, frameHeight);
                return preferred is not null;
            }

            int n = calibrationClass.CalibrationDataList.Count;

            cameraSystemCentreArray = new MCvPoint3D64f?[n];
            essentialMatrixArray = new Emgu.CV.Matrix<double>?[n];
            fundamentalMatrixArray = new Emgu.CV.Matrix<double>?[n];


            // Build for each calibration that matches current frame size
            for (int i = 0; i < n; i++)
            {
                var cdp = calibrationClass.CalibrationDataList[i];
                if (cdp is null) continue;

                // Only use calibration entries that match the current video frame size
                if (!cdp.FrameSizeCompare(frameWidth, frameHeight))
                    continue;

                // Intrinsics (must exist)
                var K_left = cdp.LeftCameraCalibration?.Intrinsic;
                var K_right = cdp.RightCameraCalibration?.Intrinsic;
                if (K_left is null || K_right is null) continue;

                // Stereo extrinsics mapping left->right: X_r = R * X_l + T
                var R = cdp.StereoCameraCalibration?.Rotation;
                var T = cdp.StereoCameraCalibration?.Translation;
                if (R is null || T is null) continue;

                // --- System center (midpoint between camera centers) ---
                // Camera centers in LEFT/world coordinates:
                //   C_left  = (0,0,0)
                //   C_right = -R^T * T
                // Midpoint: C_sys = (C_left + C_right)/2 = (-R^T * T) / 2
                // Ensure T is 3x1
                var Tcol = (T.Rows == 3 && T.Cols == 1) ? T : T.Transpose();
                var Rt = R.Transpose(); // R^T

                var C_right = new Emgu.CV.Matrix<double>(3, 1);
                CvInvoke.Gemm(Rt, Tcol, -1.0, null, 0.0, C_right);

                cameraSystemCentreArray[i] = new MCvPoint3D64f(
                    C_right[0, 0] / 2.0,
                    C_right[1, 0] / 2.0,
                    C_right[2, 0] / 2.0
                );

                // --- Essential matrix: E = [T]_x * R ---
                // Build skew-symmetric [T]_x from T (in *right* camera coords per OpenCV convention)
                double tx = Tcol[0, 0], ty = Tcol[1, 0], tz = Tcol[2, 0];
                var Tx = new Emgu.CV.Matrix<double>(3, 3);
                // [  0  -tz   ty ]
                // [  tz   0  -tx ]
                // [ -ty   tx   0 ]
                Tx[0, 0] = 0.0; Tx[0, 1] = -tz; Tx[0, 2] = ty;
                Tx[1, 0] = tz; Tx[1, 1] = 0.0; Tx[1, 2] = -tx;
                Tx[2, 0] = -ty; Tx[2, 1] = tx; Tx[2, 2] = 0.0;

                var E = new Emgu.CV.Matrix<double>(3, 3);
                CvInvoke.Gemm(Tx, R, 1.0, null, 0.0, E); // E = [T]_x * R
                essentialMatrixArray[i] = E;

                // --- Fundamental matrix: F = K_R^{-T} * E * K_L^{-1} ---
                // Inverses
                using var K_left_inv_mat = new Mat();
                using var K_right_inv_mat = new Mat();

                CvInvoke.Invert(K_left.Mat, K_left_inv_mat, DecompMethod.Svd);
                CvInvoke.Invert(K_right.Mat, K_right_inv_mat, DecompMethod.Svd);

                // (K_R)^{-T}  =  (K_R^{-1})^T
                using var K_right_inv_T = new Mat();
                CvInvoke.Transpose(K_right_inv_mat, K_right_inv_T);

                // F = K_R^{-T} * E * K_L^{-1}
                using var temp = new Mat();
                CvInvoke.Gemm(E.Mat, K_left_inv_mat, 1.0, null, 0.0, temp);

                var F = new Emgu.CV.Matrix<double>(3, 3);
                CvInvoke.Gemm(K_right_inv_T, temp, 1.0, null, 0.0, F.Mat);
                if (fundamentalMatrixArray is not null)
                    fundamentalMatrixArray[i] = F;
            }

            // Validate preferred
            var preferredCdp = calibrationClass.GetPreferredCalibationData(frameWidth, frameHeight);
            if (preferredCdp is null) return false;

            calibationDataUniqueString = newKey;
            return true;
        }


        // In IsReadyUndistortedPoints(), inside the for loop where RMSErrorAArray[i] and RMSErrorBArray[i] are set to null,
        // add null checks and initialize the arrays if they are null before assignment.

        private bool IsReadyUndistortedPoints()
        {
            bool ret = false;

            if (IsReadyCalibrationData())
            {
                if (LPointA is not null && RPointA is not null && LPointB is not null && RPointB is not null)
                {
                    if (vecAUndistortedArray is null)
                    {
                        int count = calibrationClass!.CalibrationDataList.Count;
                        vecAUndistortedArray = new MCvPoint3D64f?[count];
                        vecBUndistortedArray = new MCvPoint3D64f?[count];
                        vecABMidArray = new MCvPoint3D64f?[count];

                        // Ensure RMSErrorAArray and RMSErrorBArray are initialized
                        if (RMSErrorAArray is null || RMSErrorAArray.Length != count)
                            RMSErrorAArray = new double?[count];
                        if (RMSErrorBArray is null || RMSErrorBArray.Length != count)
                            RMSErrorBArray = new double?[count];

                        if (vecAUndistortedArray is not null && vecBUndistortedArray is not null && vecABMidArray is not null)
                        {
                            // Calculate the undistorted 3D points
                            for (int i = 0; i < count; i++)
                            {
                                CalibrationData? cdp = calibrationClass.CalibrationDataList[i];

                                if (cdp is not null &&
                                    cdp.FrameSizeCompare(frameWidth, frameHeight))
                                {
                                    MCvPoint3D64f? vecAUndistorted = Convert2DTo3D(cdp, (Point)LPointA, (Point)RPointA, true/*TrueUndistort*/, out double? RMSErrorA);
                                    MCvPoint3D64f? vecBUndistorted = Convert2DTo3D(cdp, (Point)LPointB, (Point)RPointB, true/*TrueUndistort*/, out double? RMSErrorB);

                                    if (vecAUndistorted is not null && vecBUndistorted is not null &&
                                        RMSErrorAArray is not null && RMSErrorBArray is not null)
                                    {
                                        vecAUndistortedArray[i] = vecAUndistorted;
                                        vecBUndistortedArray[i] = vecBUndistorted;
                                        RMSErrorAArray[i] = RMSErrorA;
                                        RMSErrorBArray[i] = RMSErrorB;

                                        // Calculate the mid-point
                                        double midX = (vecAUndistorted.Value.X + vecBUndistorted.Value.X) / 2.0;
                                        double midY = (vecAUndistorted.Value.Y + vecBUndistorted.Value.Y) / 2.0;
                                        double midZ = (vecAUndistorted.Value.Z + vecBUndistorted.Value.Z) / 2.0;

                                        vecABMidArray[i] = new MCvPoint3D64f(midX, midY, midZ);
                                    }
                                    else
                                    {
                                        vecAUndistortedArray[i] = null;
                                        vecBUndistortedArray[i] = null;
                                        vecABMidArray[i] = null;
                                        if (RMSErrorAArray is not null) RMSErrorAArray[i] = null;
                                        if (RMSErrorBArray is not null) RMSErrorBArray[i] = null;
                                    }
                                }
                            }

                            ret = true;
                        }
                    }
                    else
                        // Assume we are already setup
                        ret = true;
                }
                else if (LPointA is not null && RPointA is not null)
                {
                    if (vecAUndistortedArray is null)
                    {
                        int count = calibrationClass!.CalibrationDataList.Count;
                        vecAUndistortedArray = new MCvPoint3D64f?[count];
                        vecBUndistortedArray = null;
                        vecABMidArray = new MCvPoint3D64f?[count];

                        // Ensure RMSErrorAArray is initialized
                        if (RMSErrorAArray is null || RMSErrorAArray.Length != count)
                            RMSErrorAArray = new double?[count];

                        if (vecAUndistortedArray is not null && vecABMidArray is not null)
                        {
                            // Calculate the undistorted 3D points
                            for (int i = 0; i < count; i++)
                            {
                                CalibrationData? cdp = calibrationClass.CalibrationDataList[i];

                                if (cdp is not null &&
                                    cdp.FrameSizeCompare(frameWidth, frameHeight))
                                {
                                    MCvPoint3D64f? vecAUndistorted = Convert2DTo3D(cdp, (Point)LPointA, (Point)RPointA, true/*TrueUndistort*/, out double? RMSErrorA);

                                    if (vecAUndistorted is not null && RMSErrorAArray is not null)
                                    {
                                        vecAUndistortedArray[i] = vecAUndistorted;
                                        RMSErrorAArray[i] = RMSErrorA;

                                        // Single stereo point so vecABMidArray[] is same as vecAUndistortedArray
                                        double midX = vecAUndistorted.Value.X;
                                        double midY = vecAUndistorted.Value.Y;
                                        double midZ = vecAUndistorted.Value.Z;

                                        vecABMidArray[i] = new MCvPoint3D64f(midX, midY, midZ);
                                    }
                                    else
                                    {
                                        vecAUndistortedArray[i] = null;
                                        vecABMidArray[i] = null;
                                        if (RMSErrorAArray is not null) RMSErrorAArray[i] = null;
                                    }
                                }
                            }

                            ret = true;
                        }
                    }
                    else
                        // Assume we are already setup
                        ret = true;
                }

            }

            return ret;
        }


        /// <summary>
        /// A unique string is create from the calibration data set in the CalibrationClass instance.
        /// This is used to check for changes in the calibration data so the essential matrix and fundamental matrix
        /// can be re-calculated.
        /// </summary>
        /// <param name="cd"></param>
        /// <returns></returns>
        private string MakeCalibationDataUniqueString()
        {
            StringBuilder sb = new();

            // Parse the calibration data
            if (calibrationClass is not null)
            {
                for (int i = 0; i < calibrationClass.CalibrationDataList.Count; i++)
                {
                    if (i > 0)
                        sb.Append('/');

                    if (calibrationClass.CalibrationDataList[i].CalibrationID is not null)
                        sb.Append($"{i}:{calibrationClass.CalibrationDataList[i].CalibrationID}");
                    else
                        sb.Append($"{i}");
                }
            }

            return sb.ToString();
        }


        /// <summary>
        /// Convert corresponding left and right 2D points to a real world 
        /// 3D point the input points can be either raw distorted points or 
        /// already undistorted
        /// </summary>
        /// <param name="cd"></param>
        /// <param name="pL2D"></param>
        /// <param name="pR2D"></param>
        /// <returns></returns>
        public static MCvPoint3D64f? Convert2DTo3D(CalibrationData cd, Point PointL2D, Point PointR2D, bool TrueUndistortedFalseDistorted, out double? RMSRealWorld)
        {
            // Reset
            RMSRealWorld = 0; // Initialize RMS

            MathNet.Numerics.LinearAlgebra.Vector<double> L2D;
            MathNet.Numerics.LinearAlgebra.Vector<double> R2D;

            // Undort the points if necessary
            if (TrueUndistortedFalseDistorted == true)
            {
                Point _pointL2D = UndistortPoint(cd.LeftCameraCalibration, PointL2D);
                Point _pointR2D = UndistortPoint(cd.RightCameraCalibration, PointR2D);

                L2D = new MathNet.Numerics.LinearAlgebra.Double.DenseVector([_pointL2D.X, _pointL2D.Y]);
                R2D = new MathNet.Numerics.LinearAlgebra.Double.DenseVector([_pointR2D.X, _pointR2D.Y]);
            }
            else
            {
                L2D = new MathNet.Numerics.LinearAlgebra.Double.DenseVector([PointL2D.X, PointL2D.Y]);
                R2D = new MathNet.Numerics.LinearAlgebra.Double.DenseVector([PointR2D.X, PointR2D.Y]);
            }


            MathNet.Numerics.LinearAlgebra.Vector<double>? vector3D = Convert2DTo3D(cd, L2D, R2D);

            if (vector3D is not null)
            {
                MCvPoint3D64f point3D = new(vector3D[0], vector3D[1], vector3D[2]);

                // Calculate the rays from each camera so the RMS error can be calculated
                if (cd.StereoCameraCalibration.Translation is not null)
                {
                    // Get Camera Centres                    
                    var leftCameraCentre = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray([0.0, 0.0, 0.0]);

                    var R = cd.StereoCameraCalibration.Rotation!;
                    var T = cd.StereoCameraCalibration.Translation!;
                    var Tcol = (T.Rows == 3 && T.Cols == 1) ? T : T.Transpose();
                    var Rt = R.Transpose(); // R^T

                    // C_right = -R^T * T   (in LEFT/world coords)
                    var C_right = new Emgu.CV.Matrix<double>(3, 1);
                    CvInvoke.Gemm(Rt, Tcol, -1.0, null, 0.0, C_right);

                    var rightCameraCentre = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(
                        new double[] { C_right[0, 0], C_right[1, 0], C_right[2, 0] }
                    );


                    if (cd.LeftCameraCalibration.Intrinsic is not null &&
                        cd.RightCameraCalibration.Intrinsic is not null &&
                        cd.StereoCameraCalibration.Rotation is not null)
                    {
                        // Compute the ray directions
                        var rayLeftDirection = ComputeRayDirection(L2D,      // 2D pixel coordinates (u, v)
                                                                   ConvertEmguMatrixToMathNetMatrix(cd.LeftCameraCalibration.Intrinsic), // 3x3 intrinsic matrix K
                                                                   MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.CreateIdentity(3)); // Identity matrix for no rotation

                        var rayRightDirection = ComputeRayDirection(R2D,      // 2D pixel coordinates (u, v)
                                                                    ConvertEmguMatrixToMathNetMatrix(cd.RightCameraCalibration.Intrinsic), // 3x3 intrinsic matrix K
                                                                    ConvertEmguMatrixToMathNetMatrix(cd.StereoCameraCalibration.Rotation));  // Rotation matrix for the right camera


                        // Compute the RMS Distance error by calculating the shortest distance between the two rays
                        RMSRealWorld = ComputeMinimumDistance(leftCameraCentre, rayLeftDirection,
                                                              rightCameraCentre, rayRightDirection);

                    }
                }
                else
                {
                    // RMS could not be calculated
                    RMSRealWorld = null;
                }

                return point3D;
            }

            return null;
        }


        /// <summary>
        /// Convert a matched left and right 2D points to a real world 3D point
        /// Uses MathNET matrix and vector types but has Calibration data that uses EmguCV matrix types
        /// </summary>
        /// <param name="cd">Calibration data</param>
        /// <param name="L2D">Left camera 2D point (normally undistorted)</param>
        /// <param name="R2D">Right camera 2D point (normally undistorted)</param>
        /// <returns></returns>
        public static MathNet.Numerics.LinearAlgebra.Vector<double>? Convert2DTo3D(CalibrationData cd, 
                                                                                   MathNet.Numerics.LinearAlgebra.Vector<double> L2D, 
                                                                                   MathNet.Numerics.LinearAlgebra.Vector<double> R2D)
        {
            if (cd.LeftCameraCalibration.Intrinsic is not null &&
                cd.RightCameraCalibration.Intrinsic is not null &&
                cd.StereoCameraCalibration.Rotation is not null &&
                cd.StereoCameraCalibration.Translation is not null)
            {
                var RT_L = MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.CreateIdentity(3)
                    .Append(MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.Create(3, 1, 0));
                var P_L = ConvertEmguMatrixToMathNetMatrix(cd.LeftCameraCalibration.Intrinsic).Multiply(RT_L);

                var RT_R = ConvertEmguMatrixToMathNetMatrix(cd.StereoCameraCalibration.Rotation)
                    .Append(ConvertEmguMatrixToMathNetVector(cd.StereoCameraCalibration.Translation).ToColumnMatrix());
                var P_R = ConvertEmguMatrixToMathNetMatrix(cd.RightCameraCalibration.Intrinsic).Multiply(RT_R);

                return DirectLinearTransformation(P_L, P_R, L2D, R2D);
            }

            return null;
        }


        /// <summary>
        /// Performs 3D triangulation using two camera projection matrices and corresponding 2D points from stereo images
        /// </summary>
        /// <param name="P1">Projection matrix of the first (left) camera</param>
        /// <param name="P2">Projection matrix of the second (right) camera</param>
        /// <param name="point1">2D point from the first camera's image plane</param>
        /// <param name="point2"> 2D point from the second camera's image plane</param>
        /// <returns></returns>
        public static MathNet.Numerics.LinearAlgebra.Vector<double> DirectLinearTransformation(MathNet.Numerics.LinearAlgebra.Matrix<double> P1, MathNet.Numerics.LinearAlgebra.Matrix<double> P2, MathNet.Numerics.LinearAlgebra.Vector<double> point1, MathNet.Numerics.LinearAlgebra.Vector<double> point2)
        {
            // Create the matrix A based on the Direct Linear Transformation (DLT) algorithm
            var A = MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.OfRowArrays(
                [.. (point1[1] * P1.Row(2) - P1.Row(1))],
                [.. (P1.Row(0) - point1[0] * P1.Row(2))],
                [.. (point2[1] * P2.Row(2) - P2.Row(1))],
                [.. (P2.Row(0) - point2[0] * P2.Row(2))]
            );

            // Singular Value Decomposition (SVD)           
            var svd = A.Svd(true);
            var Vh = svd.VT;

            // Extract the 3D Point
            var triangulatedPoint = Vh.Row(3).SubVector(0, 3) / Vh[3, 3];

            return triangulatedPoint;
        }


        /// <summary>
        /// Compute the minimum distance between two rays in 3D space
        /// </summary>
        /// <param name="C1">Camera one centre 3D coordinate</param>
        /// <param name="d1">Camera one ray direction vector</param>
        /// <param name="C2">Camera two centre 3D coordinate</param>
        /// <param name="d2">Camera two ray direction vector</param>
        /// <returns></returns>
        public static double ComputeMinimumDistance(MathNet.Numerics.LinearAlgebra.Vector<double> C1,
                                                    MathNet.Numerics.LinearAlgebra.Vector<double> d1,
                                                    MathNet.Numerics.LinearAlgebra.Vector<double> C2,
                                                    MathNet.Numerics.LinearAlgebra.Vector<double> d2)
        {
            var cross = d1.CrossProduct(d2); // Compute the cross product of d1 and d2
            double denom = cross.L2Norm();    // Magnitude of the cross product

            if (denom < 1e-6)
            {
                // Rays are parallel or nearly parallel
                return (C2 - C1).CrossProduct(d1).L2Norm() / d1.L2Norm();
            }

            // Compute the closest points
            var C2_C1 = C2 - C1;
            double t1 = C2_C1.DotProduct(d2.CrossProduct(cross)) / cross.DotProduct(cross);
            double t2 = C2_C1.DotProduct(d1.CrossProduct(cross)) / cross.DotProduct(cross);

            var P1 = C1 + t1 * d1; // Closest point on Ray 1
            var P2 = C2 + t2 * d2; // Closest point on Ray 2

            // Compute the minimum distance
            return (P1 - P2).L2Norm();
        }


        /// <summary>
        /// Compute the ray direction from a undistorted coordinate in the image
        /// </summary>
        /// <param name="pixelCoords"></param>
        /// <param name="intrinsicMatrix"></param>
        /// <param name="rotationMatrix"></param>
        /// <param name=""></param>
        /// <returns></returns>
        public static MathNet.Numerics.LinearAlgebra.Vector<double> ComputeRayDirection(MathNet.Numerics.LinearAlgebra.Vector<double> pixelCoords,      // 2D pixel coordinates (u, v)
                                                                                        MathNet.Numerics.LinearAlgebra.Matrix<double> intrinsicMatrix, // 3x3 intrinsic matrix K
                                                                                        MathNet.Numerics.LinearAlgebra.Matrix<double> rotationMatrix)  // 3x3 rotation matrix R
        {
            // Step 1: Normalize the Image Coordinates
            double u = pixelCoords[0];
            double v = pixelCoords[1];
            double fx = intrinsicMatrix[0, 0];
            double fy = intrinsicMatrix[1, 1];
            double cx = intrinsicMatrix[0, 2];
            double cy = intrinsicMatrix[1, 2];

            double x = (u - cx) / fx;
            double y = (v - cy) / fy;

            // Create the normalized image point in homogeneous coordinates
            var normalizedImagePoint = MathNet.Numerics.LinearAlgebra.Double.DenseVector.OfArray([x, y, 1.0]);

            // Step 2: Form the Ray in Camera Coordinates
            // In camera coordinates, the ray direction is the normalized image point
            var rayCameraCoords = normalizedImagePoint.Normalize(2);

            // Step 3: Transform the Ray to World Coordinates
            // Apply the inverse of the rotation matrix to transform to world coordinates
            var rayWorldCoords = rotationMatrix.TransposeThisAndMultiply(rayCameraCoords);

            // Step 4: Normalize the Ray Direction
            var rayDirection = rayWorldCoords.Normalize(2);

            return rayDirection;
        }

        /// <summary>
        /// Used to convert an Emgu matrix to a MathNet matrix
        /// i.e. Emgu.CV.Matrix<double> to a MathNet.Numerics.LinearAlgebra.Matrix<double>
        /// </summary>
        /// <param name="emguMatrix">Emgu format matrix</param>
        /// <returns>MathNet matrix</returns>
        public static MathNet.Numerics.LinearAlgebra.Matrix<double> ConvertEmguMatrixToMathNetMatrix(Emgu.CV.Matrix<double> emguMatrix)
        {
            int rows = emguMatrix.Rows;
            int cols = emguMatrix.Cols;
            MathNet.Numerics.LinearAlgebra.Matrix<double> mathNetMatrix = MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.Create(rows, cols, 0);

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    mathNetMatrix[i, j] = emguMatrix[i, j];
                }
            }

            return mathNetMatrix;
        }


        /// <summary>
        /// Used to convert an Emgu matrix to a MathNet vector
        /// </summary>
        /// <param name="emguMatrix">Emgu format matrix</param>
        /// <returns>MathNet vector</returns>
        /// <exception cref="ArgumentException"></exception>
        public static MathNet.Numerics.LinearAlgebra.Vector<double> ConvertEmguMatrixToMathNetVector(Emgu.CV.Matrix<double> emguMatrix)
        {
            // Check if the matrix is one-dimensional
            if (emguMatrix.Rows != 1 && emguMatrix.Cols != 1)
            {
                throw new ArgumentException("The matrix is not one-dimensional and cannot be converted to a vector.");
            }

            // Determine the length of the vector
            int length = Math.Max(emguMatrix.Rows, emguMatrix.Cols);
            MathNet.Numerics.LinearAlgebra.Vector<double> mathNetVector = MathNet.Numerics.LinearAlgebra.Double.DenseVector.Create(length, 0);

            for (int i = 0; i < length; i++)
            {
                mathNetVector[i] = (emguMatrix.Rows == 1) ? emguMatrix[0, i] : emguMatrix[i, 0];
            }

            return mathNetVector;
        }


        /// <summary>
        /// Used to undistort 2D MCvPoint2D64f point using calibration data
        /// </summary>
        /// <param name="point"></param>
        /// <param name="cameraMatrix"></param>
        /// <param name="distCoeffs"></param>
        /// <returns></returns>
        public static MCvPoint2D64f UndistortPoint(CalibrationCameraData ccd, MCvPoint2D64f point)
        {
            // Convert the input point to a VectorOfPoint2D32F
            VectorOfPointF distortedPoints = new VectorOfPointF(new System.Drawing.PointF[] { new System.Drawing.PointF((float)point.X, (float)point.Y) });

            // Create a VectorOfPoint2D32F to hold the undistorted point
            VectorOfPointF undistortedPoints = new VectorOfPointF(1);

            // Perform undistortion
            CvInvoke.UndistortPoints(distortedPoints, undistortedPoints, ccd.Intrinsic, ccd.Distortion, null, ccd.Intrinsic);

            // Convert the undistorted point back to MCvPoint2D64f
            return new MCvPoint2D64f(undistortedPoints[0].X, undistortedPoints[0].Y);
        }


        /// <summary>
        /// Used to undistort 2D System.Windows.Point point using calibration data
        /// </summary>
        /// <param name="point"></param>
        /// <param name="cameraMatrix"></param>
        /// <param name="distCoeffs"></param>
        /// <returns></returns>
        public static Point UndistortPoint(CalibrationCameraData ccd, Point point)
        {
            // Convert the input point to a VectorOfPoint2D32F
            VectorOfPointF distortedPoints = new(new System.Drawing.PointF[] { new((float)point.X, (float)point.Y) });

            // Create a VectorOfPoint2D32F to hold the undistorted point
            VectorOfPointF undistortedPoints = new(1);

            // Perform undistortion
            CvInvoke.UndistortPoints(distortedPoints, undistortedPoints, ccd.Intrinsic, ccd.Distortion, null, ccd.Intrinsic);

            // Convert the undistorted point back to System.Windows.Point
            return new Point(undistortedPoints[0].X, undistortedPoints[0].Y);
        }


        /// <summary>
        /// Returns the distance between the two 3D points
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <returns></returns>
        public static double DistanceBetween3DPoints(MCvPoint3D64f point1, MCvPoint3D64f point2)
        {
            return Math.Sqrt(Math.Pow(point2.X - point1.X, 2) + Math.Pow(point2.Y - point1.Y, 2) + Math.Pow(point2.Z - point1.Z, 2));
        }


        /// <summary>
        /// Calculate the distance this 3D point is from the origin
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static double CalculateDistanceFromOrigin(MCvPoint3D64f vector)
        {
            double x = vector.X;
            double y = vector.Y;
            double z = vector.Z;

            return Math.Sqrt(x * x + y * y + z * z);
        }



        /// <summary>
        /// Computes the corresponding distorted point on the opposite camera given a selected target point.
        /// Works for both left-to-right and right-to-left correspondences.
        /// </summary>
        /// <param name="cd">The stereo camera calibration data.</param>
        /// <param name="inputPoint">The selected point in the source image.</param>
        /// <param name="distanceToTarget">The real-world distance from the system center to the target.</param>
        /// <param name="isLeftCamera">True if the input point is from the left camera, false if from the right.</param>
        /// <returns>The corresponding distorted point in the opposite camera's image.</returns>
        


        /// <summary>
        /// From a source pixel and a desired Euclidean distance from the camera system center,
        /// compute the corresponding *distorted* pixel in the other camera by:
        ///  1) building the source-camera ray,
        ///  2) solving for the ray parameter α so that the 3D point lies on a sphere of radius
        ///     'distanceToTarget' centered at C_sys = -R^T T / 2 in LEFT/world coords,
        ///  3) transforming the 3D point to the target camera coords,
        ///  4) projecting with target intrinsics,
        ///  5) re-applying distortion for display on the unrectified image.
        /// </summary>
        /// <param name="TrueLeftFalseRight">true if the SOURCE pixel is from the LEFT image; false if from RIGHT</param>
        /// <param name="sourcePixel">SOURCE pixel (distorted)</param>
        /// <param name="distanceToTarget">Euclidean distance from the camera system center (meters)</param>
        /// <param name="correspondingDistortedPixel">OUTPUT: corresponding distorted pixel in the TARGET image</param>
        /// <returns>true on success; false if no valid solution (e.g., behind camera, degenerate geometry)</returns>
        public bool ComputeCorrespondingDistortedPointByDistanceFromTarget(
            bool TrueLeftFalseRight,
            Point sourcePixel,
            double distanceToTarget,
            out Point? correspondingDistortedPixel,
            int calibrationDataIndex = -1)
        {
            correspondingDistortedPixel = default;

            if (!IsReadyCalibrationData() || calibrationClass is null)
                return false;

            int idx = ResolveIndex(calibrationDataIndex);
            if (!InBounds(idx, calibrationClass.CalibrationDataList.Count))
                return false;

            var cd = calibrationClass.CalibrationDataList[idx];
            if (cd is null || !cd.FrameSizeCompare(frameWidth, frameHeight))
                return false;

            // SOURCE / TARGET camera calibrations and stereo extrinsics
            var srcCal = TrueLeftFalseRight ? cd.LeftCameraCalibration : cd.RightCameraCalibration;
            var dstCal = TrueLeftFalseRight ? cd.RightCameraCalibration : cd.LeftCameraCalibration;

            var R = cd.StereoCameraCalibration.Rotation;     // maps LEFT -> RIGHT: X_r = R X_l + T
            var T = cd.StereoCameraCalibration.Translation;   // column or row vector (handle both below)

            if (srcCal is null || dstCal is null || R is null || T is null)
                return false;

            // Ensure T is 3x1
            Emgu.CV.Matrix<double> Tcol = (T.Rows == 3 && T.Cols == 1) ? T : T.Transpose();

            // Rt = R^T
            var Rt = R.Transpose();

            // ----- Camera system center in LEFT/world coords: C_sys = (-R^T T) / 2 -----
            var C_right = new Emgu.CV.Matrix<double>(3, 1);         // = -Rt * T
            CvInvoke.Gemm(Rt, Tcol, -1.0, null, 0.0, C_right);
            double Csys_x = C_right[0, 0] * 0.5;
            double Csys_y = C_right[1, 0] * 0.5;
            double Csys_z = C_right[2, 0] * 0.5;

            // ----- Undistort the SOURCE pixel; build the SOURCE camera ray -----
            // Undistorted SOURCE pixel (in pixel coords)
            Point pU = UndistortPoint(srcCal, sourcePixel);

            // Intrinsic parameters (SOURCE)
            var Ksrc = srcCal.Intrinsic;
            double fx_s = Ksrc[0, 0], fy_s = Ksrc[1, 1], cx_s = Ksrc[0, 2], cy_s = Ksrc[1, 2];

            // Ray direction in SOURCE camera coords (not normalized; scale is fine)
            // v_cam = [(x - cx)/fx, (y - cy)/fy, 1]
            double vx_cam = (pU.X - cx_s) / fx_s;
            double vy_cam = (pU.Y - cy_s) / fy_s;
            double vz_cam = 1.0;

            // Express this ray direction in LEFT/world coords
            // If source is LEFT: d_world = v_cam
            // If source is RIGHT: d_world = R^T * v_cam
            double dwx, dwy, dwz;
            if (TrueLeftFalseRight)
            {
                dwx = vx_cam; dwy = vy_cam; dwz = vz_cam;
            }
            else
            {
                // Multiply Rt * v_cam
                dwx = Rt[0, 0] * vx_cam + Rt[0, 1] * vy_cam + Rt[0, 2] * vz_cam;
                dwy = Rt[1, 0] * vx_cam + Rt[1, 1] * vy_cam + Rt[1, 2] * vz_cam;
                dwz = Rt[2, 0] * vx_cam + Rt[2, 1] * vy_cam + Rt[2, 2] * vz_cam;
            }

            // SOURCE camera center in LEFT/world coords
            double Csrc_x, Csrc_y, Csrc_z;
            if (TrueLeftFalseRight)
            {
                // Left camera center is origin in world
                Csrc_x = 0.0; Csrc_y = 0.0; Csrc_z = 0.0;
            }
            else
            {
                // Right camera center in LEFT/world: C_right = -R^T T
                Csrc_x = C_right[0, 0];
                Csrc_y = C_right[1, 0];
                Csrc_z = C_right[2, 0];
            }

            // ----- Solve for α so that || (C_src + α * d_world) - C_sys || = distanceToTarget -----
            // Quadratic: (d·d) α^2 + 2 ((C_src - C_sys)·d) α + ||C_src - C_sys||^2 - D^2 = 0
            double dx = Csrc_x - Csys_x;
            double dy = Csrc_y - Csys_y;
            double dz = Csrc_z - Csys_z;

            double a = dwx * dwx + dwy * dwy + dwz * dwz;
            double b = 2.0 * (dx * dwx + dy * dwy + dz * dwz);
            double c = (dx * dx + dy * dy + dz * dz) - (distanceToTarget * distanceToTarget);

            // Numerical guard
            if (a <= 0.0)
                return false;

            double disc = b * b - 4.0 * a * c;
            if (disc < 0.0) disc = 0.0; // clamp

            double sqrtDisc = Math.Sqrt(disc);
            double alpha1 = (-b - sqrtDisc) / (2.0 * a);
            double alpha2 = (-b + sqrtDisc) / (2.0 * a);

            // We want a point in front of the SOURCE camera -> choose the smallest non-negative α
            double alpha = double.PositiveInfinity;
            if (alpha1 >= 0.0) alpha = Math.Min(alpha, alpha1);
            if (alpha2 >= 0.0) alpha = Math.Min(alpha, alpha2);

            if (!double.IsFinite(alpha))
                return false; // both roots behind the camera

            // 3D point in LEFT/world coords
            double Xw = Csrc_x + alpha * dwx;
            double Yw = Csrc_y + alpha * dwy;
            double Zw = Csrc_z + alpha * dwz;

            // ----- Transform to TARGET camera coordinates -----
            // If source=LEFT (target=RIGHT): X_t = R * X_w + T
            // If source=RIGHT (target=LEFT):  X_t = R^T * X_w - R^T * T
            double Xt, Yt, Zt;

            if (TrueLeftFalseRight)
            {
                Xt = R[0, 0] * Xw + R[0, 1] * Yw + R[0, 2] * Zw + Tcol[0, 0];
                Yt = R[1, 0] * Xw + R[1, 1] * Yw + R[1, 2] * Zw + Tcol[1, 0];
                Zt = R[2, 0] * Xw + R[2, 1] * Yw + R[2, 2] * Zw + Tcol[2, 0];
            }
            else
            {
                // Left coordinates = R^T * X_right - R^T * T
                // Here Xw is already in LEFT coords; but target is LEFT, so just set Xt= Xw, etc.
                // (Equivalently: X_t = X_w since target == LEFT/world)
                Xt = Xw; Yt = Yw; Zt = Zw;
            }

            // Guard: point must be in front of TARGET camera
            if (Zt <= 1e-9) return false;

            // ----- Project with TARGET intrinsics (undistorted pixel) -----
            var Kdst = dstCal.Intrinsic;

            if (Kdst is not null)
            {
                double fx_t = Kdst[0, 0], fy_t = Kdst[1, 1], cx_t = Kdst[0, 2], cy_t = Kdst[1, 2];

                double x_u = fx_t * (Xt / Zt) + cx_t;
                double y_u = fy_t * (Yt / Zt) + cy_t;

                // ----- Re-apply distortion to draw on the unrectified image -----
                correspondingDistortedPixel = DistortPoint(dstCal, new Point(x_u, y_u));

                if (correspondingDistortedPixel is not null)
                {
                    // (Optional) bounds check; you may clamp or just return true regardless
                    if (correspondingDistortedPixel.Value.X < 0 || correspondingDistortedPixel.Value.X >= frameWidth ||
                        correspondingDistortedPixel.Value.Y < 0 || correspondingDistortedPixel.Value.Y >= frameHeight)
                        return false;
                }
                else
                    return false;
            }
            else
                return false;

            return true;
        }


        /// <summary>
        /// Applies distortion to a 2D point using the given camera's distortion model.
        /// </summary>
        /// <param name="ccd">The camera calibration data (intrinsic + distortion).</param>
        /// <param name="undistortedPoint">The undistorted 2D image point.</param>
        /// <returns>The distorted 2D image point.</returns>
        public static Point? DistortPoint(CalibrationCameraData ccd, Point undistortedPoint)
        {
            if (ccd.Intrinsic == null || ccd.Distortion == null) return null;

            double fx = ccd.Intrinsic[0, 0], fy = ccd.Intrinsic[1, 1];
            double cx = ccd.Intrinsic[0, 2], cy = ccd.Intrinsic[1, 2];

            // Gracefully read up to 8 coeffs if present
            double k1 = 0, k2 = 0, p1 = 0, p2 = 0, k3 = 0, k4 = 0, k5 = 0, k6 = 0;

            int dc = ccd.Distortion.Cols; // or Rows if 1xN vs Nx1
            double GetD(int idx)
            {
                if (ccd.Distortion.Rows == 1 && idx < ccd.Distortion.Cols) return ccd.Distortion[0, idx];
                if (ccd.Distortion.Cols == 1 && idx < ccd.Distortion.Rows) return ccd.Distortion[idx, 0];
                return 0.0;
            }
            if (dc >= 1) k1 = GetD(0);
            if (dc >= 2) k2 = GetD(1);
            if (dc >= 3) p1 = GetD(2);
            if (dc >= 4) p2 = GetD(3);
            if (dc >= 5) k3 = GetD(4);
            if (dc >= 6) k4 = GetD(5);
            if (dc >= 7) k5 = GetD(6);
            if (dc >= 8) k6 = GetD(7);

            double x = (undistortedPoint.X - cx) / fx;
            double y = (undistortedPoint.Y - cy) / fy;

            double r2 = x * x + y * y;
            double r4 = r2 * r2;
            double r6 = r4 * r2;

            // 8-coef radial (OpenCV)
            double radial = 1 + k1 * r2 + k2 * r4 + k3 * r6;
            if (dc >= 8) // “rational” model
            {
                double denom = 1 + k4 * r2 + k5 * r4 + k6 * r6;
                if (Math.Abs(denom) > 1e-12) radial /= denom;
            }

            double x_tan = 2 * p1 * x * y + p2 * (r2 + 2 * x * x);
            double y_tan = p1 * (r2 + 2 * y * y) + 2 * p2 * x * y;

            double xd = x * radial + x_tan;
            double yd = y * radial + y_tan;

            return new Point(xd * fx + cx, yd * fy + cy);
        }


        // ** End of StereoProjection**
    }


    /// <summary>
    /// This MathNetExtensions method computes the cross product of two 3-dimensional vectors, v1 and v2, using the 
    /// MathNet.Numerics library. The cross product is a vector operation in 3D space that results 
    /// in a new vector perpendicular to the plane formed by the input vectors
    /// </summary>
    /// <param name="v1">Three component input vector</param>
    /// <param name="v2">Three component input vector</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static class MathNetExtensions
    {
        /// <summary>
        /// Computes the cross product of two 3D vectors.
        /// </summary>
        public static MathNet.Numerics.LinearAlgebra.Vector<double> CrossProduct(this MathNet.Numerics.LinearAlgebra.Vector<double> v1, MathNet.Numerics.LinearAlgebra.Vector<double> v2)
        {
            if (v1.Count != 3 || v2.Count != 3)
            {
                throw new ArgumentException("Cross product is only defined for 3D vectors.");
            }

            return MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(
            [
            v1[1] * v2[2] - v1[2] * v2[1],
            v1[2] * v2[0] - v1[0] * v2[2],
            v1[0] * v2[1] - v1[1] * v2[0]
            ]);
        }
    }
}
