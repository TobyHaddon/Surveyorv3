// StereoProjection
// Stereo projection maths support
// 
// Version 1.1  02 Feb 2025
// Added code to calculate the RMS

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Surveyor.Helper;
using Surveyor.User_Controls;
using System;
using System.Text;
using Windows.Foundation;   // We use the Point class from here



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
        private double? RMSErrorA = null;
        private double? RMSErrorB = null;

        // Calculated mid-point of the 3D measurement points 
        private MCvPoint3D64f?[]? vecABMidArray = null;

        // Frame width and height
        private int frameWidth = -1;
        private int frameHeight = -1;


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
            RMSErrorA = null;
            RMSErrorB = null;
        }


        /// <summary>
        /// Calulate the distane between the two measurement points
        /// </summary>
        /// <returns></returns>
        public double? Measurement()
        {
            double? ret = null;

            if (IsReadyCalibrationData())
            {

                if (IsReadyUndistortedPoints())
                {
                    MCvPoint3D64f? vecA = vecAUndistortedArray![calibrationClass!.PreferredCalibrationDataIndex];
                    MCvPoint3D64f? vecB = vecBUndistortedArray![calibrationClass!.PreferredCalibrationDataIndex];

                    // Preferred calibration data instance measure calculation
                    if (vecA is not null && vecB is not null)
                    {
                        ret = DistanceBetween3DPoints((MCvPoint3D64f)vecA, (MCvPoint3D64f)vecB);


                        report?.Out(Reporter.WarningLevel.Info, "", $"---Length using preferred Calibration Data[{calibrationClass!.CalibrationDataList[calibrationClass!.PreferredCalibrationDataIndex].Description}] Measurement = {Math.Round((double)ret * 1000,1)}mm");
                    }

                    // Non-Preferred calibration data instance measure calculation
                    for (int i = 0; i < calibrationClass!.CalibrationDataList.Count; i++)
                    {
                        if (i != calibrationClass!.PreferredCalibrationDataIndex)
                        {
                            if (calibrationClass!.CalibrationDataList[i].FrameSizeCompare(frameWidth, frameHeight))
                            {
                                vecA = vecAUndistortedArray![i];
                                vecB = vecBUndistortedArray![i];

                                // Preferred calibration data instance measure calculation
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
            //    Console.WriteLine("Mean Reprojection Error: " + meanError);
            //}

            return ret;
        }


        /// <summary>
        /// Calulcate the distance from the centre point of the camera system to the centre point 
        /// of the measurement points
        /// </summary>
        /// <returns></returns>
        public double? RangeFromCameraSystemCentrePointToMeasurementCentrePoint()
        {
            double? ret = null;

            // Check if the calibration data and the undistored points are ready
            if (IsReadyUndistortedPoints())
            {
                MCvPoint3D64f? cameraSystemCentre = cameraSystemCentreArray![calibrationClass!.PreferredCalibrationDataIndex];
                MCvPoint3D64f? vecABMid = vecABMidArray![calibrationClass!.PreferredCalibrationDataIndex];

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
        public double? XOffsetFromCameraSystemCentrePointToMeasurementCentrePoint()
        {
            double? ret = null;

            // Check if the calibration data and the undistored points are ready
            if (IsReadyUndistortedPoints())
            { 
                MCvPoint3D64f? cameraSystemCentre = cameraSystemCentreArray![calibrationClass!.PreferredCalibrationDataIndex];
                MCvPoint3D64f? vecABMid = vecABMidArray![calibrationClass!.PreferredCalibrationDataIndex];

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
        public double? YOffsetFromCameraSystemCentrePointToMeasurementCentrePoint()
        {
            double? ret = null;

            // Check if the calibration data and the undistored points are ready
            if (IsReadyUndistortedPoints())
            {
                MCvPoint3D64f? cameraSystemCentre = cameraSystemCentreArray![calibrationClass!.PreferredCalibrationDataIndex];
                MCvPoint3D64f? vecABMid = vecABMidArray![calibrationClass!.PreferredCalibrationDataIndex];

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
        public double? RMS(bool? TruePointAFalsePointBNullWorstCase)
        {
            double? ret = null;

            if (TruePointAFalsePointBNullWorstCase == true)
            {
                ret = RMSErrorA;
            }
            else if (TruePointAFalsePointBNullWorstCase == false)
            {
                ret = RMSErrorB;
            }
            else
            {
                // WorstCase
                if (RMSErrorA is not null && RMSErrorB is not null)
                {
                    ret = Math.Max((double)RMSErrorA, (double)RMSErrorB);
                }
                else if (RMSErrorA is not null)
                {
                    ret = RMSErrorA;
                }
                else if (RMSErrorB is not null)
                {
                    ret = RMSErrorB;
                }
            }

            return ret;
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
        public bool CalculateEpipilorLine(int calibrationDataIndex, bool TrueLeftFalseRight, Point point, out double epiLine_a, out double epiLine_b, out double epiLine_c)
        {
            bool ret = false;

            // Reset
            epiLine_a = 0;
            epiLine_b = 0;
            epiLine_c = 0;


            if (IsReadyCalibrationData())
            {
                // Calculate the epipolar line for the left image
                CalibrationData? calibrationData = calibrationClass!.CalibrationDataList[calibrationDataIndex];
                if (calibrationData is not null && calibrationData.FrameSizeCompare(frameWidth, frameHeight))
                {
                    Point pointUndistorted;
                    if (TrueLeftFalseRight)
                        pointUndistorted = UndistortPoint(calibrationData.LeftCameraCalibration, point);
                    else
                        pointUndistorted = UndistortPoint(calibrationData.RightCameraCalibration, point);


                    // Convert point to homogeneous coordinates
                    Matrix<double> pointLHomogeneous = new Matrix<double>(3, 1);
                    pointLHomogeneous[0, 0] = pointUndistorted.X;
                    pointLHomogeneous[1, 0] = pointUndistorted.Y;
                    pointLHomogeneous[2, 0] = 1.0;

                    // Compute the epipolar line in the right image
                    Matrix<double> epipLine = fundamentalMatrixArray![calibrationDataIndex] * pointLHomogeneous;

                    epiLine_a = epipLine[0, 0];
                    epiLine_b = epipLine[1, 0];
                    epiLine_c = epipLine[2, 0];

                    // Indicate success
                    ret = true;
                }
            }

            return ret;
        }

        public bool CalculateEpipilorLine(bool TrueLeftFalseRight, Point point, out double epiLine_a, out double epiLine_b, out double epiLine_c, 
                                          out double focalLength, out double baseline,
                                          out double principalXLeft, out double principalYLeft, out double principalXRight, out double principalYRight)
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

            if (IsReadyCalibrationData())
            {
                ret = CalculateEpipilorLine(calibrationClass!.PreferredCalibrationDataIndex,
                                             TrueLeftFalseRight,
                                             point,
                                             out epiLine_a,
                                             out epiLine_b,
                                             out epiLine_c);
                if (ret == true)
                {
                    // Get the preferred calibration data instance
                    CalibrationData calibrationData = calibrationClass!.CalibrationDataList[calibrationClass!.PreferredCalibrationDataIndex];

                    // Extract focal length from left camera matrix
                    focalLength = calibrationData.LeftCameraCalibration.Intrinsic?[0, 0] ?? 0.0; // f = fx
                    baseline = Math.Abs(calibrationData.StereoCameraCalibration.Translation?[0, 0] ?? 0.0);

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
            CalibrationData? cd = calibrationClass!.GetPreferredCalibationData(frameWidth, frameHeight);

            if (cd is not null)
            {
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
                double middleTargetDistance = (farTargetDistance - nearTargetDistance) / 2.0;

                // Calculate the corresponding points
                pointNear = StereoProjection.ComputeCorrespondingDistortedPointByDistanceFromTarget(cd, point, nearTargetDistance, TrueLeftFalseRight);
                pointMiddle = StereoProjection.ComputeCorrespondingDistortedPointByDistanceFromTarget(cd, point, middleTargetDistance, TrueLeftFalseRight);
                pointFar = StereoProjection.ComputeCorrespondingDistortedPointByDistanceFromTarget(cd, point, farTargetDistance, TrueLeftFalseRight);

                ret = true;
            }
            else 
            {
                pointNear = new Point(-1, -1);
                pointMiddle = new Point(-1, 0);
                pointFar = new Point(-1, 0);
            }

                return ret;
        }


        ///
        /// PIRVATE METHODS
        ///


        /// <summary>
        /// This method is used to check if the CalidrationClass has changed since and that the 
        /// The preferred calibration data instance is available and support the current frame size.
        /// For this is work SetCalibrationData() and SetFrameSize() must have been called and the cilbration data
        /// must support the current frame size.
        /// </summary>
        /// <returns></returns>
        private bool IsReadyCalibrationData()
        {
            bool ret = false;
            if (calibrationClass is not null)
            {
                string newCalibationDataUniqueString = MakeCalibationDataUniqueString();

                if (string.IsNullOrEmpty(calibationDataUniqueString) || calibationDataUniqueString != newCalibationDataUniqueString)
                {

                    CalibrationData? cdp;

                    // Declare the correct size arrays
                    essentialMatrixArray = new Matrix<double>?[calibrationClass.CalibrationDataList.Count];
                    fundamentalMatrixArray = new Matrix<double>?[calibrationClass.CalibrationDataList.Count];
                    cameraSystemCentreArray = new MCvPoint3D64f?[calibrationClass.CalibrationDataList.Count];

                    // Parse the calibration data to calculate the essential and fundamental matrices for all entries of 
                    // the calibration data list matching the current resolution
                    for (int i = 0; i < calibrationClass.CalibrationDataList.Count; i++)
                    {
                        // Compute the essential matrix
                        cdp = calibrationClass.CalibrationDataList[i];

                        if (cdp is not null &&
                            cdp.FrameSizeCompare(frameWidth, frameHeight) &&
                            cdp.StereoCameraCalibration.Rotation is not null && cdp.StereoCameraCalibration.Translation is not null &&
                            cdp.LeftCameraCalibration.Intrinsic is not null && cdp.RightCameraCalibration.Intrinsic is not null)
                        {
                            // Compute the essential matrix
                            Emgu.CV.Matrix<double>? essentialMatrix = ComputeEssentialMatrix(cdp.StereoCameraCalibration.Rotation, cdp.StereoCameraCalibration.Translation);

                            // Compute the fundamental matrix
                            Emgu.CV.Matrix<double>? fundamentalMatrix = ComputeFundamentalMatrix(essentialMatrix, cdp.LeftCameraCalibration.Intrinsic/*intrinsicLeft*/, cdp.RightCameraCalibration.Intrinsic/*intrinsicRight*/);

                            // Compute the 3D centre point of the camera system
                            //OLD MCvPoint3D64f? cameraSystemCentre= new MCvPoint3D64f(cdp.StereoCameraCalibration.Translation[0, 1] / 2.0, 0.0, 0.0);
                            MCvPoint3D64f? cameraSystemCentre = new MCvPoint3D64f(cdp.StereoCameraCalibration.Translation[0, 0] / 2.0, 
                                                                                  cdp.StereoCameraCalibration.Translation[0, 1] / 2.0, 
                                                                                  cdp.StereoCameraCalibration.Translation[0, 2] / 2.0);


                            essentialMatrixArray[i] = essentialMatrix;
                            fundamentalMatrixArray[i] = fundamentalMatrix;
                            cameraSystemCentreArray[i] = cameraSystemCentre;
                        }
                    }

                    // Check the preferred calibration data instance is available and supports the current frame size
                    cdp = calibrationClass.GetPreferredCalibationData(frameWidth, frameHeight);
                    if (cdp is not null)
                    {
                        ret = true;

                        // Remember the new calibration data unique string
                        calibationDataUniqueString = newCalibationDataUniqueString;
                    }
                }
                else
                {
                    // Already setup
                    ret = true;
                }
            }

            return ret;
        }


        /// <summary>
        /// This method is used to prepare an undistorted 3D point from a 2D points set in PointsLoad().
        /// It includes a called to IsReadyCalibrationData() so no need to call that again.
        /// It works on the stereo pair of points A and B ([LPointA, RPointA] and [LPointB, RPointB]) 
        /// or a single stero point [LPointA, RPointA].
        /// </summary>
        /// <returns></returns>
        private bool IsReadyUndistortedPoints()
        {
            bool ret = false;

            if (IsReadyCalibrationData())
            {
                if (LPointA is not null && RPointA is not null && LPointB is not null && RPointB is not null)
                {
                    if (vecAUndistortedArray is null)
                    {
                        vecAUndistortedArray = new MCvPoint3D64f?[calibrationClass!.CalibrationDataList.Count];
                        vecBUndistortedArray = new MCvPoint3D64f?[calibrationClass!.CalibrationDataList.Count];
                        vecABMidArray = new MCvPoint3D64f?[calibrationClass!.CalibrationDataList.Count];

                        if (vecAUndistortedArray is not null && vecBUndistortedArray is not null && vecABMidArray is not null)
                        {
                            // Calculate the undistorted 3D points
                            for (int i = 0; i < calibrationClass!.CalibrationDataList.Count; i++)
                            {
                                CalibrationData? cdp = calibrationClass.CalibrationDataList[i];

                                if (cdp is not null &&
                                    cdp.FrameSizeCompare(frameWidth, frameHeight))
                                {
                                    MCvPoint3D64f? vecAUndistorted = Convert2DTo3D(cdp, (Point)LPointA, (Point)RPointA, true/*TrueUndistort*/, out RMSErrorA);
                                    MCvPoint3D64f? vecBUndistorted = Convert2DTo3D(cdp, (Point)LPointB, (Point)RPointB, true/*TrueUndistort*/, out RMSErrorB);

                                    if (vecAUndistorted is not null && vecBUndistorted is not null)
                                    {
                                        vecAUndistortedArray[i] = vecAUndistorted;
                                        vecBUndistortedArray[i] = vecBUndistorted;

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
                                        RMSErrorA = null;
                                        RMSErrorB = null;
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
                        vecAUndistortedArray = new MCvPoint3D64f?[calibrationClass!.CalibrationDataList.Count];
                        vecBUndistortedArray = null;
                        vecABMidArray = new MCvPoint3D64f?[calibrationClass!.CalibrationDataList.Count];

                        if (vecAUndistortedArray is not null && vecABMidArray is not null)
                        {
                            // Calculate the undistorted 3D points
                            for (int i = 0; i < calibrationClass!.CalibrationDataList.Count; i++)
                            {
                                CalibrationData? cdp = calibrationClass.CalibrationDataList[i];

                                if (cdp is not null &&
                                    cdp.FrameSizeCompare(frameWidth, frameHeight))
                                {
                                    MCvPoint3D64f? vecAUndistorted = Convert2DTo3D(cdp, (Point)LPointA, (Point)RPointA, true/*TrueUndistort*/, out RMSErrorA);

                                    if (vecAUndistorted is not null)
                                    {
                                        vecAUndistortedArray[i] = vecAUndistorted;

                                        // Single stero point so vecABMidArray[] is same as vecAUndistortedArray
                                        double midX = vecAUndistorted.Value.X;
                                        double midY = vecAUndistorted.Value.Y;
                                        double midZ = vecAUndistorted.Value.Z;

                                        vecABMidArray[i] = new MCvPoint3D64f(midX, midY, midZ);
                                    }
                                    else
                                    {
                                        vecAUndistortedArray[i] = null;
                                        vecABMidArray[i] = null;
                                        RMSErrorA = null;
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
        /// Calculate the Essential Matrix
        /// </summary>
        /// <param name="R"></param>
        /// <param name="T"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static Matrix<double> ComputeEssentialMatrix(Matrix<double> R, Matrix<double> T)
        {
            // Create the skew-symmetric matrix for the translation vector
            Matrix<double> T_skew = new Matrix<double>(3, 3);

            if (T.Rows == 3 && T.Cols == 1)
            {
                T_skew[0, 0] = 0;
                T_skew[0, 1] = -T[2, 0];
                T_skew[0, 2] = T[1, 0];
                T_skew[1, 0] = T[2, 0];
                T_skew[1, 1] = 0;
                T_skew[1, 2] = -T[0, 0];
                T_skew[2, 0] = -T[1, 0];
                T_skew[2, 1] = T[0, 0];
                T_skew[2, 2] = 0;
            }
            else if (T.Rows == 1 && T.Cols == 3)
            {
                T_skew[0, 0] = 0;
                T_skew[0, 1] = -T[0, 2];
                T_skew[0, 2] = T[0, 1];
                T_skew[1, 0] = T[0, 2];
                T_skew[1, 1] = 0;
                T_skew[1, 2] = -T[0, 0];
                T_skew[2, 0] = -T[0, 1];
                T_skew[2, 1] = T[0, 0];
                T_skew[2, 2] = 0;
            }
            else
                throw new ArgumentException("The translation vector must be a 3x1 or 1x3 matrix.");

            // Compute the essential matrix
            return T_skew * R;
        }


        /// <summary>
        /// Calcualte the FundamentalMatrix
        /// </summary>
        /// <param name="E"></param>
        /// <param name="K1"></param>
        /// <param name="K2"></param>
        /// <returns></returns>
        private static Matrix<double> ComputeFundamentalMatrix(Matrix<double> E, Matrix<double> K1, Matrix<double> K2)
        {
            Matrix<double> K1Inv = new Matrix<double>(3, 3);
            Matrix<double> K2Inv = new Matrix<double>(3, 3);

            // Invert the intrinsic matrices
            CvInvoke.Invert(K1, K1Inv, DecompMethod.Svd);
            CvInvoke.Invert(K2, K2Inv, DecompMethod.Svd);

            // Compute the fundamental matrix
            return K2Inv.Transpose() * E * K1Inv;
        }


        /// <summary>
        /// Convert a matched left and right 2D points to a real world 3D point
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
                    // Get Camera Centers                    
                    var leftCameraCentre = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray([0, 0, 0]);
                    var t = cd.StereoCameraCalibration.Translation;
                    var rightCameraCentre = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray([t[0, 0], t[0, 1], t[0, 2]]);

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
        /// This function calculates the real-world RMS error by computing the 
        /// shortest distance between two 3D rays.
        /// </summary>
        /// <param name="cameraCenterLeft"></param>
        /// <param name="rayDirectionLeft"></param>
        /// <param name="cameraCenterRight"></param>
        /// <param name="rayDirectionRight"></param>
        /// <returns></returns>







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
                (point1[1] * P1.Row(2) - P1.Row(1)).ToArray(),
                (P1.Row(0) - point1[0] * P1.Row(2)).ToArray(),
                (point2[1] * P2.Row(2) - P2.Row(1)).ToArray(),
                (P2.Row(0) - point2[0] * P2.Row(2)).ToArray()
            );

            // Singular Value Decomposition (SVD)           
            //???Older approach  var B = A.TransposeThisAndMultiply(A);
            //???var svd = B.Svd(true);
            var svd = A.Svd(true);
            var Vh = svd.VT;

            // Extract the 3D Point
            var triangulatedPoint = Vh.Row(3).SubVector(0, 3) / Vh[3, 3];

            // Debug Report
            Console.WriteLine($"Triangulated point: {triangulatedPoint}");

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
            //????var cross = CrossProduct(d1, d2); // Compute the cross product of d1 and d2
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
        public static Point ComputeCorrespondingDistortedPointByDistanceFromTarget(CalibrationData cd, Point inputPoint, double distanceToTarget, bool isLeftCamera)
        {
            // Select appropriate calibration data based on the input camera
            var sourceCamera = isLeftCamera ? cd.LeftCameraCalibration : cd.RightCameraCalibration;
            var targetCamera = isLeftCamera ? cd.RightCameraCalibration : cd.LeftCameraCalibration;

            var R = cd.StereoCameraCalibration.Rotation;
            var T = cd.StereoCameraCalibration.Translation;

            if (sourceCamera.Intrinsic == null || targetCamera.Intrinsic == null || R == null || T == null || sourceCamera.Distortion == null || targetCamera.Distortion == null)
                throw new InvalidOperationException("Calibration matrices are not initialized.");

            // Extract intrinsic parameters of the source camera
            double fx = sourceCamera.Intrinsic[0, 0];
            double fy = sourceCamera.Intrinsic[1, 1];
            double cx = sourceCamera.Intrinsic[0, 2];
            double cy = sourceCamera.Intrinsic[1, 2];

            // Extract intrinsic parameters of the target camera
            double fx_t = targetCamera.Intrinsic[0, 0];
            double fy_t = targetCamera.Intrinsic[1, 1];
            double cx_t = targetCamera.Intrinsic[0, 2];
            double cy_t = targetCamera.Intrinsic[1, 2];

            // Convert distance from system center to depth in the source camera's coordinate system
            double depthSourceCamera = distanceToTarget;

            // Undistort the input point
            Point undistortedInputPoints = UndistortPoint(sourceCamera, inputPoint);
            //???Mat inputPoints = new(1, 1, DepthType.Cv32F, 2);
            //???inputPoints.SetTo([(float)inputPoint.X, (float)inputPoint.Y]);
            //???Mat undistortedInputPoints = new();
            //???CvInvoke.UndistortPoints(inputPoints, undistortedInputPoints, sourceCamera.Intrinsic, sourceCamera.Distortion);

            // Extract undistorted input point values
            //???float[] undistortedData = new float[2];
            //???undistortedInputPoints.CopyTo(undistortedData);
            double xUndistorted = undistortedInputPoints.X;   //??? undistortedData[0];
            double yUndistorted = undistortedInputPoints.Y;   //??? undistortedData[1];

            // Convert to 3D coordinates in the source camera space
            double Xs = (xUndistorted - cx) * depthSourceCamera / fx;
            double Ys = (yUndistorted - cy) * depthSourceCamera / fy;

            // Transform to the target camera coordinate system
            Mat Ps = new(3, 1, DepthType.Cv64F, 1);
            Ps.SetTo([Xs, Ys, depthSourceCamera]);

            // Transpose the T matrix to be 3x1 (we store as 1x3)
            Emgu.CV.Matrix<double>? tT = T.Transpose();
            if (!isLeftCamera)
            {
                CvInvoke.ScaleAdd(T, -1.0, tT, tT); // negated 
            }



            //???    Emgu.CV.Matrix<double> negatedT = new Emgu.CV.Matrix<double>(T.Rows, T.Cols);
            //CvInvoke.ScaleAdd(T, -1.0, negatedT, negatedT); // negatedT = -1 * T

            // Create
            Mat Pt = new();
            CvInvoke.Gemm(R, Ps, 1.0, tT, 1.0, Pt); // Adjust translation direction based on camera order

            // Extract transformed 3D coordinates
            double[] PtData = new double[3];
            Pt.CopyTo(PtData);
            double Xt = PtData[0];
            double Yt = PtData[1];
            double Zt = PtData[2];

            // Project onto the target camera's image plane (undistorted)
            double xTargetUndistorted = (fx_t * Xt / Zt) + cx_t;
            double yTargetUndistorted = (fy_t * Yt / Zt) + cy_t;

            // Apply distortion to match the unrectified target image
            return DistortPoint(targetCamera, new Point(xTargetUndistorted, yTargetUndistorted)) ?? new Point(xTargetUndistorted, yTargetUndistorted);
        }

        /// <summary>
        /// Applies distortion to a 2D point using the given camera's distortion model.
        /// </summary>
        /// <param name="ccd">The camera calibration data (intrinsic + distortion).</param>
        /// <param name="undistortedPoint">The undistorted 2D image point.</param>
        /// <returns>The distorted 2D image point.</returns>
        public static Point? DistortPoint(CalibrationCameraData ccd, Point undistortedPoint)
        {
            if (ccd.Intrinsic == null || ccd.Distortion == null)
                return null;

            // Extract intrinsic parameters
            double fx = ccd.Intrinsic[0, 0];
            double fy = ccd.Intrinsic[1, 1];
            double cx = ccd.Intrinsic[0, 2];
            double cy = ccd.Intrinsic[1, 2];

            // Extract distortion coefficients
            double k1 = ccd.Distortion[0, 0];
            double k2 = ccd.Distortion[0, 1];
            double p1 = ccd.Distortion[0, 2];
            double p2 = ccd.Distortion[0, 3];
            double k3 = ccd.Distortion[0, 4];

            // Normalize the coordinates
            double xNorm = (undistortedPoint.X - cx) / fx;
            double yNorm = (undistortedPoint.Y - cy) / fy;

            // Compute radial distortion
            double r2 = xNorm * xNorm + yNorm * yNorm;
            double radialDistortion = 1 + k1 * r2 + k2 * r2 * r2 + k3 * r2 * r2 * r2;

            // Compute distorted coordinates
            double xDistortedNorm = xNorm * radialDistortion + 2 * p1 * xNorm * yNorm + p2 * (r2 + 2 * xNorm * xNorm);
            double yDistortedNorm = yNorm * radialDistortion + p1 * (r2 + 2 * yNorm * yNorm) + 2 * p2 * xNorm * yNorm;

            // Convert back to pixel coordinates
            double xDistorted = xDistortedNorm * fx + cx;
            double yDistorted = yDistortedNorm * fy + cy;

            return new Point(xDistorted, yDistorted);
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
