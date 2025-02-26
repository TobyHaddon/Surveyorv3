using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Surveyor;
using System.Diagnostics;
using Surveyor.User_Controls;
using Microsoft.UI.Dispatching;
using System.Threading.Tasks;  // Reference to your main WinUI 3 project


namespace Surveyor.Tests
{
    [TestClass]  // MSTest for WinUI 3
    public class StereoProjectionTests
    {
        private Survey? survey = null;

        [TestInitialize]  // Runs before each test
        public async Task Setup()
        {
            survey = new(null!);

            int ret = await survey.SurveyLoad("Surveyor3.Tests\\101 (CEV22 Pool Survey).survey");

            if (ret != 0)
            {
                Assert.Fail("Setup: Failed");
            }
        }

        [TestMethod]
        public void ValidateIntrinsicMatrices()
        {
            if (survey is not null)
            {
                CalibrationData cd = survey.Data.Calibration.CalibrationDataList[0];

                if (cd.LeftCameraCalibration.Intrinsic is not null)
                {
                    Emgu.CV.Matrix<double> LeftIntrinsic = (Emgu.CV.Matrix<double>)cd.LeftCameraCalibration.Intrinsic;
                    Assert.AreEqual(2457.533252328369, LeftIntrinsic[0, 0]);
                    Assert.AreEqual(1961.0459617442594, LeftIntrinsic[0, 2]);
                    Assert.AreEqual(2457.533252328369, LeftIntrinsic[1, 1]);
                    Assert.AreEqual(1066.7420961488095, LeftIntrinsic[1, 2]);
                }
                if (cd.RightCameraCalibration.Intrinsic is not null)
                {
                    Emgu.CV.Matrix<double> RightIntrinsic = (Emgu.CV.Matrix<double>)cd.RightCameraCalibration.Intrinsic;
                    Assert.AreEqual(2464.932755231347, RightIntrinsic[0, 0]);
                    Assert.AreEqual(1846.818121472416, RightIntrinsic[0, 2]);
                    Assert.AreEqual(2464.932755231347, RightIntrinsic[1, 1]);
                    Assert.AreEqual(1056.838296512517, RightIntrinsic[1, 2]);
                }
            }
            else
            {
                Assert.Fail("ValidateIntrinsicMatrices: survey returned null");
            }
        }

        [TestMethod]
        public void ValidateDistortionCoefficients()
        {
            if (survey is not null)
            {
                CalibrationData cd = survey.Data.Calibration.CalibrationDataList[0];

                if (cd.LeftCameraCalibration.Distortion is not null)
                {
                    Emgu.CV.Matrix<double> LeftDistortion = (Emgu.CV.Matrix<double>)cd.LeftCameraCalibration.Distortion;
                    Assert.AreEqual(-0.1108799674189081, LeftDistortion[0, 0]);
                    Assert.AreEqual(0.15009199756706943, LeftDistortion[0, 1]);
                }
                if (cd.RightCameraCalibration.Distortion is not null)
                {
                    Emgu.CV.Matrix<double> RightDistortion = (Emgu.CV.Matrix<double>)cd.RightCameraCalibration.Distortion;
                    Assert.AreEqual(-0.00929409571254929, RightDistortion[0, 0]);
                    Assert.AreEqual(-0.3140186182028748, RightDistortion[0, 1]);
                }
            }
            else
            {
                Assert.Fail("ValidateDistortionCoefficients: survey returned null");
            }
        }

        [TestMethod]
        public void ValidateExtrinsicMatrices()
        {
            if (survey is not null)
            {
                CalibrationData cd = survey.Data.Calibration.CalibrationDataList[0];
                if (cd.StereoCameraCalibration.Translation is not null)
                {
                    Emgu.CV.Matrix<double> Translation = cd.StereoCameraCalibration.Translation;
                    Assert.AreEqual(-0.9603758615013508, Translation[0, 0]);
                    Assert.AreEqual(0.003068960505464242, Translation[0, 1]);
                    Assert.AreEqual(0.11147148203752219, Translation[0, 2]);
                }
                if (cd.StereoCameraCalibration.Rotation is not null)
                {
                    Emgu.CV.Matrix<double> Rotation = cd.StereoCameraCalibration.Rotation;
                    Assert.AreEqual(3, Rotation.Rows);
                    Assert.AreEqual(3, Rotation.Cols);
                }
            }
            else
            {
                Assert.Fail("ValidateExtrinsicMatrices: survey returned null");
            }
        }

        [TestMethod]
        public void TestMeasurementLength()
        {
            // Simulated stereo points in pixel coordinates
            Point? _LPointA = new Point(965.427490234375, 336.97113037109375);  // Left image A
            Point? _LPointB = new Point(1089.427490234375, 316.97113037109375);  // Left image B
            Point? _RPointA = new Point(971.18994140625, 325.7471008300781);  // Right image A
            Point? _RPointB = new Point(1085.18994140625, 302.2471008300781);  // Right image B

            // Instantiate Measurement() (Assuming a class Measurement exists)
            StereoProjection stereoProjection = new();
            stereoProjection.SetFrameSize(3840, 2160);

            if (survey is not null)
            {
                // Load the stereo points
                stereoProjection.SetCalibrationData(survey.Data.Calibration);
                stereoProjection.PointsLoad(_LPointA, _LPointB, _RPointA, _RPointB);

                // Compute the length measurement
                double? measuredLength = stereoProjection.Measurement();

                if (measuredLength is not null)
                {
                    // Expected length in real-world units (meters)
                    double expectedLength = 0.28771946632762513; // Example assumption: 10cm separation
                    Debug.WriteLine($"TestMeasurementLength: StereoProjection.Measurement() returned:{measuredLength:F4}m, expected length~:{expectedLength}m)");

                    // Allow some margin for floating point precision errors
                    double tolerance = 0.01;

                    // Validate the measurement
                    Assert.IsTrue(Math.Abs((double)measuredLength - expectedLength) < tolerance,
                        $"Measured length {measuredLength} differs from expected {expectedLength}");
                }
                else
                {
                    Assert.Fail("TestMeasurementLength: StereoProjection.Measurement() returned null");
                }
            }
            else
            {
                Assert.Fail("TestMeasurementLength: survey returned null");
            }
        }

        [TestMethod]
        public void TestRangeLength()
        {
            // Simulated stereo points in pixel coordinates
            Point? _LPointA = new Point(965.427490234375, 336.97113037109375);  // Left image A
            Point? _LPointB = new Point(1089.427490234375, 316.97113037109375);  // Left image B
            Point? _RPointA = new Point(971.18994140625, 325.7471008300781);  // Right image A
            Point? _RPointB = new Point(1085.18994140625, 302.2471008300781);  // Right image B

            // Instantiate Measurement() (Assuming a class Measurement exists)
            StereoProjection stereoProjection = new();
            stereoProjection.SetFrameSize(3840, 2160);

            if (survey is not null)
            {
                // Load the stereo points
                stereoProjection.SetCalibrationData(survey.Data.Calibration);
                stereoProjection.PointsLoad(_LPointA, _LPointB, _RPointA, _RPointB);

                // Compute the length measurement
                double? rangeLength = stereoProjection.RangeFromCameraSystemCentrePointToMeasurementCentrePoint();

                if (rangeLength is not null)
                {
                    // Expected length in real-world units (meters)
                    double expectedLength = 6.199448992791107;
                    Debug.WriteLine($"TestRangeLength: StereoProjection.RangeFromCameraSystemCentrePointToMeasurementCentrePoint() returned:{rangeLength:F4}m, expected length~:{expectedLength}m)");

                    // Allow some margin for floating point precision errors
                    double tolerance = 0.01;

                    // Validate the measurement
                    Assert.IsTrue(Math.Abs((double)rangeLength - expectedLength) < tolerance,
                        $"Range length {rangeLength} differs from expected {expectedLength}");
                }
                else
                {
                    Assert.Fail("TestRangeLength: StereoProjection.RangeFromCameraSystemCentrePointToMeasurementCentrePoint() returned null");
                }
            }
            else
            {
                Assert.Fail("TestRangeLength: survey returned null");
            }
        }

        [TestMethod]
        public void TestXOffsetLength()
        {
            // Simulated stereo points in pixel coordinates
            Point? _LPointA = new Point(965.427490234375, 336.97113037109375);  // Left image A
            Point? _LPointB = new Point(1089.427490234375, 316.97113037109375);  // Left image B
            Point? _RPointA = new Point(971.18994140625, 325.7471008300781);  // Right image A
            Point? _RPointB = new Point(1085.18994140625, 302.2471008300781);  // Right image B

            // Instantiate Measurement() (Assuming a class Measurement exists)
            StereoProjection stereoProjection = new();
            stereoProjection.SetFrameSize(3840, 2160);

            if (survey is not null)
            {
                // Load the stereo points
                stereoProjection.SetCalibrationData(survey.Data.Calibration);
                stereoProjection.PointsLoad(_LPointA, _LPointB, _RPointA, _RPointB);

                // Compute the length measurement
                double? xOffsetLength = stereoProjection.XOffsetFromCameraSystemCentrePointToMeasurementCentrePoint();

                if (xOffsetLength is not null)
                {
                    // Expected length in real-world units (meters)
                    double expectedLength = -2.1307496675307753;
                    Debug.WriteLine($"TestXOffsetLength: StereoProjection.XOffsetFromCameraSystemCentrePointToMeasurementCentrePoint() returned:{xOffsetLength:F4}m, expected length~:{expectedLength}m)");

                    // Allow some margin for floating point precision errors
                    double tolerance = 0.01;

                    // Validate the measurement
                    Assert.IsTrue(Math.Abs((double)xOffsetLength - expectedLength) < tolerance,
                        $"Range length {xOffsetLength} differs from expected {expectedLength}");
                }
                else
                {
                    Assert.Fail("TestXOffsetLength: StereoProjection.XOffsetFromCameraSystemCentrePointToMeasurementCentrePoint() returned null");
                }
            }
            else
            {
                Assert.Fail("TestXOffsetLength: survey returned null");
            }
        }


        [TestMethod]
        public void TestYOffsetLength()
        {
            // Simulated stereo points in pixel coordinates
            Point? _LPointA = new Point(965.427490234375, 336.97113037109375);  // Left image A
            Point? _LPointB = new Point(1089.427490234375, 316.97113037109375);  // Left image B
            Point? _RPointA = new Point(971.18994140625, 325.7471008300781);  // Right image A
            Point? _RPointB = new Point(1085.18994140625, 302.2471008300781);  // Right image B

            // Instantiate Measurement() (Assuming a class Measurement exists)
            StereoProjection stereoProjection = new();
            stereoProjection.SetFrameSize(3840, 2160);

            if (survey is not null)
            {
                // Load the stereo points
                stereoProjection.SetCalibrationData(survey.Data.Calibration);
                stereoProjection.PointsLoad(_LPointA, _LPointB, _RPointA, _RPointB);

                // Compute the length measurement
                double? yOffsetLength = stereoProjection.YOffsetFromCameraSystemCentrePointToMeasurementCentrePoint();

                if (yOffsetLength is not null)
                {
                    // Expected length in real-world units (meters)
                    double expectedLength = -1.8691431416686615;
                    Debug.WriteLine($"TestYOffsetLength: StereoProjection.YOffsetFromCameraSystemCentrePointToMeasurementCentrePoint() returned:{yOffsetLength:F4}m, expected length~:{expectedLength}m)");

                    // Allow some margin for floating point precision errors
                    double tolerance = 0.01;

                    // Validate the measurement
                    Assert.IsTrue(Math.Abs((double)yOffsetLength - expectedLength) < tolerance,
                        $"Range length {yOffsetLength} differs from expected {expectedLength}");
                }
                else
                {
                    Assert.Fail("TestYOffsetLength: StereoProjection.YOffsetFromCameraSystemCentrePointToMeasurementCentrePoint() returned null");
                }
            }
            else
            {
                Assert.Fail("TestYOffsetLength: survey returned null");
            }
        }


        [TestMethod]
        public void TestRMS()
        {
            // Simulated stereo points in pixel coordinates
            Point? _LPointA = new Point(965.427490234375, 336.97113037109375);  // Left image A
            Point? _LPointB = new Point(1089.427490234375, 316.97113037109375);  // Left image B
            Point? _RPointA = new Point(971.18994140625, 325.7471008300781);  // Right image A
            Point? _RPointB = new Point(1085.18994140625, 302.2471008300781);  // Right image B

            // Instantiate Measurement() (Assuming a class Measurement exists)
            StereoProjection stereoProjection = new();
            stereoProjection.SetFrameSize(3840, 2160);

            if (survey is not null)
            {
                // Load the stereo points
                stereoProjection.SetCalibrationData(survey.Data.Calibration);
                stereoProjection.PointsLoad(_LPointA, _LPointB, _RPointA, _RPointB);

                // First Compute the length measurement because this is where the RMS are also calculated
                double? measuredLength = stereoProjection.Measurement();

                if (measuredLength is not null)
                {
                    // This function just returns the calculated RMS distance error average of the two points
                    double? rms = stereoProjection.RMS(null);

                    if (rms is not null)
                    {
                        // Expected length in real-world units (meters)
                        double expectedLength = 0.35535873616877278;
                        Debug.WriteLine($"TestRMS: StereoProjection.RMS() returned:{rms:F4}m, expected length~:{expectedLength}m)");

                        // Allow some margin for floating point precision errors
                        double tolerance = 0.01;

                        // Validate the measurement
                        Assert.IsTrue(Math.Abs((double)rms - expectedLength) < tolerance,
                            $"Range length {rms} differs from expected {expectedLength}");
                    }
                    else
                    {
                        Assert.Fail("TestRMS: StereoProjection.RMS() returned null");
                    }
                }
                else
                {
                    Assert.Fail("TestRMS: StereoProjection.Measurement() returned null");
                }
            }
            else
            {
                Assert.Fail("TestRMS: survey returned null");
            }
        }

        [TestMethod]
        public void TestUndistortAndDistort()
        {
            // Simulated stereo points in pixel coordinates
            Point? _LPointADist = new Point(965.427490234375, 336.97113037109375);  // Left image A

            if (survey is not null)
            {
                CalibrationData? cd = survey.Data.Calibration.GetPreferredCalibationData(3840, 2160);
                if (cd is not null)
                {
                    MCvPoint2D64f _LPointA64Dist = new(_LPointADist.Value.X, _LPointADist.Value.Y);
                    MCvPoint2D64f _LPointA64UnDist = StereoProjection.UndistortPoint(cd.LeftCameraCalibration, _LPointA64Dist);

                    Point _LPointAUnDist = new(_LPointA64UnDist.X, _LPointA64UnDist.Y);
                    Point? _LPointAReDist = StereoProjection.DistortPoint(cd.LeftCameraCalibration, _LPointAUnDist);

                    if (_LPointAReDist is not null)
                    {

                        // Allow some margin for floating point precision errors
                        double tolerance = 0.01;
                        Debug.WriteLine($"TestUndistortAndDistort: Distort:({_LPointADist.Value.X:F4}, {_LPointADist.Value.Y:F4}) > Undistort:({_LPointAUnDist.X:F4}, {_LPointAUnDist.Y:F4}) > Distort:({_LPointAReDist.Value.X:F4}, {_LPointAReDist.Value.Y:F4})");

                        // Validate the measurement
                        Assert.IsTrue(Math.Abs(_LPointADist.Value.X - _LPointAReDist.Value.X) < tolerance,
                            $"X coordinate {_LPointADist.Value.X} differs from expected {_LPointAReDist.Value.X}");
                        Assert.IsTrue(Math.Abs(_LPointADist.Value.Y - _LPointAReDist.Value.Y) < tolerance,
                            $"Y coordinate {_LPointADist.Value.Y} differs from expected {_LPointAReDist.Value.Y}");
                    }
                    else
                    {
                        Assert.Fail("TestUndistortAndDistort: _LPointAReDist returned null");
                    }
                }
                else
                {
                    Assert.Fail("TestUndistortAndDistort: cd returned null");
                }
            }
            else
            {
                Assert.Fail("TestUndistortAndDistort: survey returned null");
            }
        }
    }
}