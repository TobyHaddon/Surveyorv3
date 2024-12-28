using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Surveyor.User_Controls;
using System;
using System.Text;
using Windows.Foundation;




namespace Surveyor
{
    /// <summary>
    /// StereoProjection Version 1.3
    /// This class is used to calculate the distance between a pair of corresponding 2D points in the left and right images
    /// It intentionally uses a mixture of Emgu.CV and MathNET.Numerics types and also System.Drawing (where the System.Windows.Point and System.Drawing.Point types are not compatible)
    /// Modiifed for WinUI3
    /// </summary>

    public class StereoProjection
    {
        private Reporter? report;
        private Survey.DataClass.CalibrationClass? calibrationClass;

        // This string is used to check if calibrationClass has changed 
        private string calibationDataUniqueString = "";

        // Calulated variables.  These calulcated values at declared to be in parallel with the  
        // calibrationClass.CalibrationDataList. 
        private Matrix<double>?[]? essentialMatrixArray = null; /*Matrix<double>(3, 3);*/
        private Matrix<double>?[]? fundamentalMatrixArray = null;
        private MCvPoint3D64f?[]? cameraSystemCentreArray = null;

        //// Remembered 2D measurement points
        private Point? LPointA = null;
        private Point? LPointB = null;
        private Point? RPointA = null;
        private Point? RPointB = null;

        //// Calculated 3D versions of the 2D measurement points
        private MCvPoint3D64f?[]? vecAUndistortedArray = null;
        private MCvPoint3D64f?[]? vecBUndistortedArray = null;

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
            // Remember the calibrtation data
            calibrationClass = _calibrationClass;

            // Reset            
            essentialMatrixArray = null;
            fundamentalMatrixArray = null;
            cameraSystemCentreArray = null;
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
        public void RetFrameSize()
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
        }


        /// <summary>
        /// Calulate the distane between the two measurement points
        /// </summary>
        /// <returns></returns>
        public double? Measurement()
        {
            double? ret = null;

            if (IsReadyUndistortedPoints())
            {
                MCvPoint3D64f? vecA = vecAUndistortedArray![calibrationClass!.PreferredCalibrationDataIndex];
                MCvPoint3D64f? vecB = vecBUndistortedArray![calibrationClass!.PreferredCalibrationDataIndex];

                // Preferred calibration data instance measure calculation
                if (vecA is not null && vecB is not null)
                {
                    ret = DistanceBetween3DPoints((MCvPoint3D64f)vecA, (MCvPoint3D64f)vecB);

                    
                    report!.Out(Reporter.WarningLevel.Info, "", $"---Length using preferred Calibration Data[{calibrationClass!.CalibrationDataList[calibrationClass!.PreferredCalibrationDataIndex].Description}] Measurement = {(ret * 1000):F1}mm");
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

                                report!.Out(Reporter.WarningLevel.Info, "", $"---Length using non-preferred Calibration Data[{calibrationClass!.CalibrationDataList[i].Description}] Measurement = {(measurementAlt * 1000):F1}mm");
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

            if (IsReadyCalibrationData())
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

            if (IsReadyCalibrationData())
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

            if (IsReadyCalibrationData())
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
                        pointUndistorted = UndistortPoint(calibrationData.LeftCalibrationCameraData, point);
                    else
                        pointUndistorted = UndistortPoint(calibrationData.RightCalibrationCameraData, point);


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


                    //// Convert the undistorted point to a VectorOfPointF
                    //VectorOfPointF pointsVec = new VectorOfPointF(new System.Drawing.PointF[] { new System.Drawing.PointF((float)pointUndistorted.X, (float)pointUndistorted.Y) });


                    //// Create a VectorOfPoint2D32F
                    //Matrix<float> linesMat = new Matrix<float>(1, 3);


                    //CvInvoke.ComputeCorrespondEpilines(pointsVec, TrueLeftFalseRight == true ? 1 : 2, fundamentalMatrix, linesMat);

                    //epiLine_a = (double)linesMat[0, 0];
                    //epiLine_b = (double)linesMat[0, 1];
                    //epiLine_c = (double)linesMat[0, 2];

                    // Indicate success
                    ret = true;
                }
            }

            return ret;
        }

        public bool CalculateEpipilorLine(bool TrueLeftFalseRight, Point point, out double epiLine_a, out double epiLine_b, out double epiLine_c)
        {
            // Reset
            epiLine_a = 0;
            epiLine_b = 0;
            epiLine_c = 0;

            if (IsReadyCalibrationData())
                return CalculateEpipilorLine(calibrationClass!.PreferredCalibrationDataIndex,
                                             TrueLeftFalseRight,
                                             point,
                                             out epiLine_a,
                                             out epiLine_b,
                                             out epiLine_c);

            return false;
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
                            cdp.CalibrationStereoCameraData.Rotation is not null && cdp.CalibrationStereoCameraData.Translation is not null &&
                            cdp.LeftCalibrationCameraData.Mtx is not null && cdp.RightCalibrationCameraData.Mtx is not null)
                        {
                            // Compute the essential matrix
                            Emgu.CV.Matrix<double>? essentialMatrix = ComputeEssentialMatrix(cdp.CalibrationStereoCameraData.Rotation, cdp.CalibrationStereoCameraData.Translation);

                            // Compute the fundamental matrix
                            Emgu.CV.Matrix<double>? fundamentalMatrix = ComputeFundamentalMatrix(essentialMatrix, cdp.LeftCalibrationCameraData.Mtx/*intrinsicLeft*/, cdp.RightCalibrationCameraData.Mtx/*intrinsicRight*/);

                            // Compute the 3D centre point of the camera system
                            MCvPoint3D64f? cameraSystemCentre= new MCvPoint3D64f(cdp.CalibrationStereoCameraData.Translation[0, 1] / 2.0, 0.0, 0.0);


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
        /// This method is used to prepare an undistorted 3D point from a 2D points set in PointsLoad()
        /// It includes a called to IsReadyCalibrationData() so no need to call that again
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
                                    MCvPoint3D64f? vecAUndistorted = Convert2DTo3D(cdp, (Point)LPointA, (Point)RPointA, true/*TrueUndistort*/);
                                    MCvPoint3D64f? vecBUndistorted = Convert2DTo3D(cdp, (Point)LPointB, (Point)RPointB, true/*TrueUndistort*/);

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
                                    }
                                }
                            }

                            ret = true;
                        }
                    }
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
        /// *** THIS IS LEGACY CODE ***
        /// Calculate the distance between a pair of corresponding 2D points in the left and right images
        /// If there are no calibrartion data instance available, then return -1
        /// If there are multiple calibrartion data instances available then only return calculations based of the preferred calbration data instance, 
        /// the calculations from any other calibration data instance will be returned in the output window.
        /// </summary>
        /// <param name="LPointA"></param>
        /// <param name="LPointB"></param>
        /// <param name="RPointA"></param>
        /// <param name="RPointB"></param>
        /// <returns></returns>
        //public double Distance(Point? LPointA, Point? LPointB, Point? RPointA, Point? RPointB, bool ReportDepth)
        //{
        //    double distance = 0;

        //    if (calibrationClass is not null)
        //    {
        //        if (LPointA is not null && RPointA is not null && LPointB is not null && RPointB is not null)
        //        {
        //            // Calculate the distance between the two 3D points using the preferred calibration data instance
        //            CalibrationData? calibrationDataPreferred = calibrationClass.GetPreferredCalibationData();
        //            if (calibrationDataPreferred is not null)
        //            {
        //                MCvPoint3D64f? vecA = Convert2DTo3D(calibrationDataPreferred, (Point)LPointA, (Point)RPointA, true/*TrueUndistort*/);
        //                MCvPoint3D64f? vecB = Convert2DTo3D(calibrationDataPreferred, (Point)LPointB, (Point)RPointB, true/*TrueUndistort*/);

        //                if (vecA is not null && vecB is not null)
        //                {
        //                    distance = DistanceBetween3DPoints((MCvPoint3D64f)vecA, (MCvPoint3D64f)vecB);
        //                }
        //            }

        //            // Calculate the distance and report only using the non-preferred calibration data instance (if any)
        //            if (calibrationClass.CalibrationDataList.Count > 1)
        //            {
        //                foreach (CalibrationData calibrationData in calibrationClass.CalibrationDataList)
        //                {
        //                    // Ignore the preferred calibration data instance
        //                    if (calibrationData != calibrationDataPreferred)
        //                    {
        //                        MCvPoint3D64f? vecA2 = Convert2DTo3D(calibrationData, (Point)LPointA, (Point)RPointA, true/*TrueUndistort*/);
        //                        MCvPoint3D64f? vecB2 = Convert2DTo3D(calibrationData, (Point)LPointB, (Point)RPointB, true/*TrueUndistort*/);

        //                        if (vecA2 is not null && vecB2 is not null)
        //                        {
        //                            double distance2 = DistanceBetween3DPoints((MCvPoint3D64f)vecA2, (MCvPoint3D64f)vecB2);
        //                            double depthA = CalculateDistanceFromOrigin((MCvPoint3D64f)vecA2);
        //                            double depthB = CalculateDistanceFromOrigin((MCvPoint3D64f)vecB2);

        //                            report!.Out(Reporter.WarningLevel.Info, "", $"---Length using non-preferred Calibration Data[{calibrationData.Description}] Length = {distance2:F1}mm, Distance from camera red point:{depthA / 1000:F2}m, green point:{depthB / 1000:F2}m");

        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    return distance;
        //}


        /// <summary>
        /// *** THIS IS LEGACY CODE ***
        /// Calculate the distance from the cameras to corresponding 2D points in the left and right images
        /// </summary>
        /// <param name="LPoint"></param>
        /// <param name="LPoint"></param>
        /// <returns></returns>
        //public double Depth(Point? LPoint, Point? RPoint)
        //{
        //    double depth = 0;
        //    if (calibrationClass is not null && calibrationClass.CalibrationDataList is not null)
        //    {
        //        if (LPoint is not null && RPoint is not null)
        //        {
        //            // Calculate the distance between the two 3D points using the preferred calibration data instance
        //            CalibrationData? calibrationDataPreferred = calibrationClass.CalibrationDataList[calibrationClass.PreferredCalibrationDataIndex];
        //            if (calibrationDataPreferred is not null)
        //            {
        //                MCvPoint3D64f? vec = Convert2DTo3D(calibrationDataPreferred, (Point)LPoint, (Point)RPoint, true/*TrueUndistort*/);

        //                if (vec is not null)
        //                {
        //                    depth = CalculateDistanceFromOrigin((MCvPoint3D64f)vec);
        //                    report!.Info("", $"*Depth calcs using preferred Calibration Data[{calibrationDataPreferred.Description}] Depth = {depth / 1000:F2}m");
        //                }


        //                // Calculate the distance and report only using the non-preferred calibration data instance (if any)
        //                if (calibrationClass.CalibrationDataList.Count > 1)
        //                {
        //                    foreach (CalibrationData calibrationData in calibrationClass.CalibrationDataList)
        //                    {
        //                        // Ignore the preferred calibration data instance
        //                        if (calibrationData != calibrationDataPreferred)
        //                        {
        //                            MCvPoint3D64f? vec2 = Convert2DTo3D(calibrationData, (Point)LPoint, (Point)RPoint, true/*TrueUndistort*/);

        //                            if (vec2 is not null)
        //                            {
        //                                double depth2 = CalculateDistanceFromOrigin((MCvPoint3D64f)vec2);

        //                                report!.Info("", $"Depth calcs using non-preferred Calibration Data[{calibrationData.Description}] Depth = {depth2 / 1000:F2}m");
        //                            }
        //                        }
        //                    }
        //                }

        //            }
        //        }
        //    }

        //    return depth;
        //}





        /// <summary>
        /// Convert a matched left and right 2D points to a real world 3D point
        /// </summary>
        /// <param name="cd"></param>
        /// <param name="pL2D"></param>
        /// <param name="pR2D"></param>
        /// <returns></returns>
        public static MCvPoint3D64f? Convert2DTo3D(CalibrationData cd, Point PointL2D, Point PointR2D, bool TrueUndistortedFalseDistorted)
        {
            //MCvPoint2D64f L2D = new MCvPoint2D64f((double)pL2D.X, (double)pL2D.Y);
            //MCvPoint2D64f R2D = new MCvPoint2D64f((double)pR2D.X, (double)pR2D.Y);
            MathNet.Numerics.LinearAlgebra.Vector<double> L2D;
            MathNet.Numerics.LinearAlgebra.Vector<double> R2D;

            // Undort the points if necessary
            if (TrueUndistortedFalseDistorted == true)
            {
                Point _pointL2D = UndistortPoint(cd.LeftCalibrationCameraData, PointL2D);
                Point _pointR2D = UndistortPoint(cd.RightCalibrationCameraData, PointR2D);

                L2D = new MathNet.Numerics.LinearAlgebra.Double.DenseVector(new double[] { _pointL2D.X, _pointL2D.Y });
                R2D = new MathNet.Numerics.LinearAlgebra.Double.DenseVector(new double[] { _pointR2D.X, _pointR2D.Y });
            }
            else
            {
                L2D = new MathNet.Numerics.LinearAlgebra.Double.DenseVector(new double[] { PointL2D.X, PointL2D.Y });
                R2D = new MathNet.Numerics.LinearAlgebra.Double.DenseVector(new double[] { PointR2D.X, PointR2D.Y });
            }


            MathNet.Numerics.LinearAlgebra.Vector<double>? vector = Convert2DTo3D(cd, L2D, R2D);

            if (vector is not null)
                return new MCvPoint3D64f(vector[0], vector[1], vector[2]);
            else
                return null;
        }



        /// <summary>
        /// Convert a matched left and right 2D points to a real world 3D point
        /// Uses MathNET matrix and vector types but has Calibration data that uses EmguCV matrix types
        /// </summary>
        /// <param name="cd"></param>
        /// <param name="L2D"></param>
        /// <param name="R2D"></param>
        /// <returns></returns>
        public static MathNet.Numerics.LinearAlgebra.Vector<double>? Convert2DTo3D(CalibrationData cd, MathNet.Numerics.LinearAlgebra.Vector<double> L2D, MathNet.Numerics.LinearAlgebra.Vector<double> R2D)
        {
            if (cd.LeftCalibrationCameraData.Mtx is not null &&
                cd.RightCalibrationCameraData.Mtx is not null &&
                cd.CalibrationStereoCameraData.Rotation is not null &&
                cd.CalibrationStereoCameraData.Translation is not null)
            {
                var RT_L = MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.CreateIdentity(3).Append(MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.Create(3, 1, 0));
                var P_L = ConvertEmguMatrixToMathNetMatrix(cd.LeftCalibrationCameraData.Mtx).Multiply(RT_L);

                var RT_R = ConvertEmguMatrixToMathNetMatrix(cd.CalibrationStereoCameraData.Rotation).Append(ConvertEmguMatrixToMathNetVector(cd.CalibrationStereoCameraData.Translation).ToColumnMatrix());
                var P_R = ConvertEmguMatrixToMathNetMatrix(cd.RightCalibrationCameraData.Mtx).Multiply(RT_R);

                return DirectLinearTransformation(P_L, P_R, L2D, R2D);
            }

            return null;
        }


        public static MathNet.Numerics.LinearAlgebra.Vector<double> DirectLinearTransformation(MathNet.Numerics.LinearAlgebra.Matrix<double> P1, MathNet.Numerics.LinearAlgebra.Matrix<double> P2, MathNet.Numerics.LinearAlgebra.Vector<double> point1, MathNet.Numerics.LinearAlgebra.Vector<double> point2)
        {
            var A = MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.OfRowArrays(
                (point1[1] * P1.Row(2) - P1.Row(1)).ToArray(),
                (P1.Row(0) - point1[0] * P1.Row(2)).ToArray(),
                (point2[1] * P2.Row(2) - P2.Row(1)).ToArray(),
                (P2.Row(0) - point2[0] * P2.Row(2)).ToArray()
            );

            var B = A.TransposeThisAndMultiply(A);
            var svd = B.Svd(true);
            var Vh = svd.VT;
            var triangulatedPoint = Vh.Row(3).SubVector(0, 3) / Vh[3, 3];

            Console.WriteLine("Triangulated point: ");
            Console.WriteLine(triangulatedPoint);
            return triangulatedPoint;
        }


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
            CvInvoke.UndistortPoints(distortedPoints, undistortedPoints, ccd.Mtx, ccd.Dist, null, ccd.Mtx);

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
            VectorOfPointF distortedPoints = new(new System.Drawing.PointF[] { new System.Drawing.PointF((float)point.X, (float)point.Y) });

            // Create a VectorOfPoint2D32F to hold the undistorted point
            VectorOfPointF undistortedPoints = new(1);

            // Perform undistortion
            CvInvoke.UndistortPoints(distortedPoints, undistortedPoints, ccd.Mtx, ccd.Dist, null, ccd.Mtx);

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



    }
}
