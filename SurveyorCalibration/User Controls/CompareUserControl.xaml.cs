using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Flann;
using Emgu.CV.Structure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Surveyor.Helper;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Pickers;
using WinRT.Interop;
using static Surveyor.User_Controls.Compare;


namespace Surveyor.User_Controls
{
    public sealed partial class Compare : UserControl
    {
        // Reporter
        private Reporter? report = null;

        // Projects
        private readonly CalibProject?[] calibProjects = [null, null, null];

        // Native Calibration Results or Calib.io Calibration Results
        private readonly string?[] calibrationResultFileSpec = [null, null, null];
        private readonly CalibrationData?[] calibrationResult = [null, null, null];

        private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;
        private readonly SafeUICall safeUICall;

        private ContentDialog? ParentDialog { get; set; } = null;

        public Compare()
        {
            // Get the DispatcherQueue for the current thread
            dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            safeUICall = new(dispatcherQueue);

            InitializeComponent();
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

        public void SetupForContentDialog(ContentDialog dialog, CalibProject? calibProject)
        {
            ParentDialog = dialog;
            DataContext = calibProject;

            if (calibProject is not null)
                calibProjects[0] = calibProject;
            else
                calibProjects[0] = null;

            calibProjects[1] = null;
            calibProjects[2] = null;

            //dialog.PrimaryButtonClick -= ExportDialog_Save_Click;
            //dialog.PrimaryButtonClick += ExportDialog_Save_Click;

            SetUIControls();
        }



        ///
        /// Events
        /// 


        /// <summary>
        /// User clicked the button to select the calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectProject1_Click(object sender, RoutedEventArgs e) => _ = SelectProjectAsync(0);

        private void SelectProject2_Click(object sender, RoutedEventArgs e) => _ = SelectProjectAsync(1);
    
        private void SelectProject3_Click(object sender, RoutedEventArgs e) => _ = SelectProjectAsync(2);



        ///
        /// Private
        /// 


        /// <summary>
        /// Set the UI elements according to the current state
        /// </summary>
        private void SetUIControls()        
        {
            SetUISelectProject(0);
            SetUISelectProject(1);
            SetUISelectProject(2);
        }


        /// <summary>
        /// Set the button state and text for the selected project index.
        /// </summary>
        /// <param name="index"></param>
        private void SetUISelectProject(int index)
        {
            Button? projectButton = null;
            TextBlock? projectTextBlock = null;

            switch (index)
            {
                case 0:
                    projectButton = SelectProject1;
                    projectTextBlock = Project1Name;
                    break;
                case 1:
                    projectButton = SelectProject2;
                    projectTextBlock = Project2Name;
                    break;
                case 2:
                    projectButton = SelectProject3;
                    projectTextBlock = Project3Name;
                    break;
            }

            if (projectButton is not null && projectTextBlock is not null)
            {
                if (calibProjects[index] is not null)
                {
                    projectButton.IsEnabled = false;
                    projectTextBlock.Text = Path.GetFileNameWithoutExtension(calibProjects[index]?.Data.Info.ProjectFileName);
                }
                else if (calibrationResultFileSpec[index] is not null)
                {
                    projectButton.IsEnabled = false;
                    projectTextBlock.Text = Path.GetFileNameWithoutExtension(calibrationResultFileSpec[index]);
                }
                else
                {
                    projectButton.IsEnabled = true;
                    projectTextBlock.Text = string.Empty;
                }
            }
        }


        /// <summary>
        /// Allow user to picks a calibration project (.calproj), calibration results 
        /// file (.calib) or calib.io (.json) to compare
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private async Task SelectProjectAsync(int index)
        {
            var picker = new FileOpenPicker();

            // Associate the picker with the window handle (required in WinUI 3)
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);

            InitializeWithWindow.Initialize(picker, hwnd);

            // File type filter
            picker.FileTypeFilter.Add(".calproj");
            picker.FileTypeFilter.Add(".calib");
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                Debug.WriteLine($"Selected calibration project or results file:{file.Path}");

                // Check file extension for project or results
                var ext = Path.GetExtension(file.Path).ToLowerInvariant();
                if (ext == ".calproj")
                {
                    // Load CalibProject
                    CalibProject calibProject = new(report!);
                    int ret = await calibProject.ProjectLoadAsync(file.Path);

                    if (ret == 0)
                    {
                        calibProjects[index] = calibProject;
                        calibrationResultFileSpec[index] = null;
                        calibrationResult[index] = null;
                    }
                }
                else if (ext == ".calib" || ext == ".json")
                {
                    // Load Calibration Results
                    CalibrationData calibrationData = new();
                    int ret = calibrationData.LoadFromFile(file.Path);
                    if (ret == 0)
                    {
                        calibProjects[index] = null;
                        calibrationResultFileSpec[index] = file.Path;
                        calibrationResult[index] = calibrationData;
                    }
                }                
            }

            SetUIControls();
            CheckIfCompareNecessary();
        }

        /// <summary>
        /// Check if two of more projects/results have been selected to enable comparison
        /// </summary>
        /// <returns></returns>
        private bool CheckIfCompareNecessary()
        {   
            int selectedCount = 0;
            for (int i = 0; i < calibProjects.Length; i++)
            {
                if (calibProjects[i] is not null || calibrationResultFileSpec[i] is not null)
                    selectedCount++;
            }
            bool canCompare = selectedCount >= 2;

            if (canCompare)
            {
                DoCompare();
            }

            return canCompare;
        }


        /// <summary>
        /// Compare all that are ready
        /// </summary>
        private void DoCompare()
        {
            // Check the primary we are compare against is ready
            if (!isCompareIndexReady(0))
                return;

            // Compare to the other selected projects/results
            for (int i = 1; i < calibProjects.Length; i++)
            {
                if (isCompareIndexReady(i))
                {
                    DoCompareTo(i);
                }
            }
        }


        /// <summary>
        /// Compare the primary project/result to the indicated index
        /// </summary>
        /// <param name="index"></param>
        private void DoCompareTo(int index)
        {
            StringBuilder sb = new();

            // Check the primary we are compare against is ready
            if (!isCompareIndexReady(0))
                return;
            // Check the index we are compare to is ready
            if (!isCompareIndexReady(index))
                return;

            int againstSetCount = GetCalibrationSetCount(0/*index 0*/);

            for (int i = 0; i < againstSetCount; i++)
            {
                // Get Mono or Stereo calibration data for primary
                CalibrationCameraData? againstLeft = null;
                CalibrationCameraData? againstRight = null;                
                CalibrationStereoCameraData? againstStereo = null;
                CalibrationParameters? againstCalibParams = null;
                (againstCalibParams, againstLeft, againstRight, againstStereo) = GetCalibrationDataAtIndex(0/*index 0*/, i/*set*/);

                if (againstCalibParams is not null)
                {
                    // Find a calibration set with matching calibration parameters in the compare-to index
                    int toSetCount = GetCalibrationSetCount(index);
                    bool foundMatch = false;
                    CompareMonoCalibrationResult? compareLeftResult = null;
                    CompareMonoCalibrationResult? compareRightResult = null;
                    CompareStereoCalibrationResult? compareStereoResult = null;

                    for (int j = 0; j < toSetCount; j++)
                    {
                        CalibrationCameraData? toLeft = null;
                        CalibrationCameraData? toRight = null;
                        CalibrationStereoCameraData? toStereo = null;
                        CalibrationParameters? toCalibParams = null;
                        (toCalibParams, toLeft, toRight, toStereo) = GetCalibrationDataAtIndex(index, j/*set*/);

                        if (toCalibParams is not null && toCalibParams == againstCalibParams)
                        {
                            // Found a calibration set in the 'to' index with matching calibration parameters to the 'against'                            
                            foundMatch = true;

                            // Do Mono compare if we have left camera data
                            if (againstLeft is not null && toLeft is not null)
                            {
                                compareLeftResult = CalibrationCompare.DoCompareMonoCalibration(againstLeft, 
                                                                                                toLeft,
                                                                                                new System.Drawing.Size(againstLeft.ImageSize![0, 0], againstLeft.ImageSize![0, 1]));
                            }
                            // Do Mono compare if we have right camera data
                            if (againstRight is not null && toRight is not null)
                            {
                                compareRightResult = CalibrationCompare.DoCompareMonoCalibration(againstRight, 
                                                                                                 toRight,
                                                                                                 new System.Drawing.Size(againstRight.ImageSize![0, 0], againstRight.ImageSize![0, 1]));
                            }
                            // Do Stereo compare if we have stereo camera data
                            if (againstStereo is not null && toStereo is not null)
                            {
                                compareStereoResult = CalibrationCompare.DoCompareStereoCalibration(againstStereo, toStereo);
                            }
                        }
                    }

                    string titleLine1 = $"Compare {GetCalibrationDataTitleAtIndex(0)}";
                    string titleLine2 = $"to {GetCalibrationDataTitleAtIndex(index)}";
                    string titleUnderLine = new('-', Math.Max(titleLine1.Length, titleLine2.Length));
                    sb.AppendLine(titleLine1);
                    sb.AppendLine(titleLine2);
                    sb.AppendLine(titleUnderLine);

                    if (foundMatch)
                    {
                        // Display left compare result (if any)
                        if (compareLeftResult is not null)
                        {
                            StringBuilder sbResult = DisplayMonoCompareResult(compareLeftResult);
                            sb.AppendLine(sbResult.ToString());
                        }

                        // Display right compare result (if any)
                        if (compareRightResult is not null)
                        {
                            StringBuilder sbResult = DisplayMonoCompareResult(compareRightResult);
                            sb.AppendLine(sbResult.ToString());
                        }

                        // Display stereo compare result (if any)
                        if (compareStereoResult is not null)
                        {
                            StringBuilder sbResult = DisplayStereoCompareResult(compareStereoResult);
                            sb.AppendLine(sbResult.ToString());                            
                        }
                    }
                    else
                    {
                        sb.AppendLine($"No set with matching calibration parameters {againstCalibParams} found.");
                    }

                    safeUICall.Call(() => Results.Text = sb.ToString());
                    sb.AppendLine("");
                }
            }
        }

        /// <summary>
        /// Check that the indicated compare is setup and ready
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private bool isCompareIndexReady(int index)
        {
            return calibProjects[index] is not null || (calibrationResultFileSpec[index] is not null && calibrationResult[index] is not null);
        }


        /// <summary>
        /// Return the number of calibration sets in the indicated project/result
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private int GetCalibrationSetCount(int index)
        {
            int count = 0;

            if (calibProjects[index] is not null)
            {
                int countLeftMono = calibProjects[index]!.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray.Length;
                int countRightMono = calibProjects[index]!.Data.CalibrationResults.RightMonoCalibrationCameraDataArray.Length;
                int countStereo = calibProjects[index]!.Data.CalibrationResults.CalibrationStereoCameraDataArray.Length;
                count = Math.Max(countLeftMono, Math.Max(countRightMono, countStereo));
            }
            else if (calibrationResultFileSpec[index] is not null && calibrationResult[index] is not null)
            {
                count = 1;
            }

            return count;
        }


        /// <summary>
        /// Get the calibration data at the indicated index
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>        
        private (CalibrationParameters? calibParams, CalibrationCameraData? left, CalibrationCameraData? right, CalibrationStereoCameraData? stereo) GetCalibrationDataAtIndex(int index, int set)
        {
            CalibrationParameters? calibParams = null;
            CalibrationCameraData? left = null;
            CalibrationCameraData? right = null;
            CalibrationStereoCameraData? stereo = null;

            if (calibProjects[index] is not null)
            {
                if (set < 0 || set >= GetCalibrationSetCount(index))
                    throw new ArgumentException("Invalid calibration set index.");

                CalibProject.DataClass.CalibrationResultClass calibrationResult = (CalibProject.DataClass.CalibrationResultClass)calibProjects[index]!.Data.CalibrationResults;
                if (calibrationResult.LeftMonoCalibrationCameraDataArray[set] is not null)
                {
                    int frameWidth = (int)calibProjects[index]!.Data.Media.FrameWidth;
                    int frameHeight = (int)calibProjects[index]!.Data.Media.FrameHeight;
                    calibParams = calibrationResult.LeftMonoCalibrationCameraDataArray[set]!.CalibrationParameters;
                    left = ConvertMonoCalibrationCameraData(calibrationResult.LeftMonoCalibrationCameraDataArray[set]!, frameWidth, frameHeight);
                    right = ConvertMonoCalibrationCameraData(calibrationResult.RightMonoCalibrationCameraDataArray[set]!, frameWidth, frameHeight);
                    stereo = calibrationResult.CalibrationStereoCameraDataArray[set];
                }
            }
            else if (calibrationResult[index] is not null)
            {
                // throw parameter exception is set != 0
                if (set != 0)
                    throw new ArgumentException("Calibration result files only contain a single calibration set.");

                CalibrationData calibrationData = (CalibrationData)calibrationResult[index]!;
                calibParams = EstimateCalibrationParameters(calibrationData.LeftCameraCalibration);
                left = calibrationData.LeftCameraCalibration;
                right = calibrationData.RightCameraCalibration;
                stereo = calibrationData.StereoCameraCalibration;
            }

            return (calibParams, left, right, stereo);
        }


        /// <summary>
        /// Get the title for the indicated index
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private string GetCalibrationDataTitleAtIndex(int index)
        {
            if (calibProjects[index] is not null)
            {
                return Path.GetFileNameWithoutExtension(calibProjects[index]!.Data.Info.ProjectFileName);
            }
            else if (calibrationResult[index] is not null)
            {
                return Path.GetFileNameWithoutExtension(calibrationResultFileSpec[index]!);
            }
            else
                return "(No name)";
        }


        /// <summary>
        /// Convert a MonoCalibrationCameraData to CalibrationCameraData
        /// </summary>
        /// <param name="monoCalibrationCameraData"></param>
        /// <returns></returns>
        CalibrationCameraData? ConvertMonoCalibrationCameraData(MonoCalibrationCameraData monoCalibrationCameraData, int frameWidth, int frameHeight)
        {
            CalibrationCameraData calibrationCameraData = new()
            {
                ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/)
            };
            calibrationCameraData.ImageSize[0, 0] = frameWidth;
            calibrationCameraData.ImageSize[0, 1] = frameHeight;

            calibrationCameraData.ImageTotal = monoCalibrationCameraData.ImageTotal;
            calibrationCameraData.ImagesUsed = monoCalibrationCameraData.ImagesUsed;
            calibrationCameraData.Intrinsic = monoCalibrationCameraData.IntrinsicMatrix;
            calibrationCameraData.Distortion = monoCalibrationCameraData.DistortionCoeffs;
            calibrationCameraData.RMS = monoCalibrationCameraData.ReprojectionRMS;
            calibrationCameraData.ProjectionRMS = monoCalibrationCameraData.ProjectionRMS;
            calibrationCameraData.MaxError = monoCalibrationCameraData.MaxError;

            return calibrationCameraData;
        }


        /// <summary>
        /// By analyzing DistortionCoeffs inside MonoCalibrationCameraData, estimate the CalibrationParameters used
        /// </summary>
        /// <param name=""></param>
        /// <returns></returns>
        private CalibrationParameters? EstimateCalibrationParameters(MonoCalibrationCameraData monoCalibrationCameraData)
        {
            CalibrationParameters? calibrationParameters = null;
            
            if (monoCalibrationCameraData.DistortionCoeffs is not null)
                calibrationParameters = EstimateCalibrationParameters(monoCalibrationCameraData.DistortionCoeffs);

            return calibrationParameters;
        }


        /// <summary>
        /// By analyzing DistortionCoeffs inside CalibrationCameraData, estimate the CalibrationParameters used
        /// </summary>
        /// <param name="calibrationCameraData"></param>
        /// <returns></returns>
        private CalibrationParameters? EstimateCalibrationParameters(CalibrationCameraData calibrationCameraData)
        {
            CalibrationParameters? calibrationParameters = null;

            if (calibrationCameraData.Distortion is not null)
                calibrationParameters = EstimateCalibrationParameters(calibrationCameraData.Distortion);

            return calibrationParameters;
        }


        /// <summary>
        /// By analyzing distortionCoeffs matrix, estimate the CalibrationParameters used
        /// </summary>
        /// <param name="distortionCoeffs"></param>
        /// <returns></returns>
        private static CalibrationParameters? EstimateCalibrationParameters(Matrix<double> distortionCoeffs)
        {
            CalibrationParameters? calibrationParameters = null;

            // Flatten coefficients row-major
            var vals = new List<double>(distortionCoeffs.Rows * distortionCoeffs.Cols);
            for (int r = 0; r < distortionCoeffs.Rows; r++)
                for (int c = 0; c < distortionCoeffs.Cols; c++)
                    vals.Add(Convert.ToDouble(distortionCoeffs[r, c], System.Globalization.CultureInfo.InvariantCulture));

            if (vals.Count == 5)
            {
                if (vals[4] != 0)  // K3 value
                    calibrationParameters = CalibrationParameters.K1K2K3P1P2;
                else
                    calibrationParameters = CalibrationParameters.K1K2P1P2;
            }
            else if (vals.Count == 8)
            {
                if (vals[6] != 0 || vals[7] != 0) // K5,K6 values
                    calibrationParameters = CalibrationParameters.K1K2K3K4P1P2K5K6;
                else
                    calibrationParameters = CalibrationParameters.K1K2K3K4P1P2;
            }

            return calibrationParameters;
        }

        /// <summary>
        /// Display the mono compare result (human readable)
        /// </summary>
        private StringBuilder DisplayMonoCompareResult(CompareMonoCalibrationResult r)
        {
            var sb = new StringBuilder();

            // Helper locals
            static string F(double v, string fmt = "0.###") =>
                (double.IsNaN(v) || double.IsInfinity(v)) ? "n/a" : v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

            static string Pct(double frac) =>
                (double.IsNaN(frac) || double.IsInfinity(frac)) ? "n/a" : (frac * 100.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%";

            static string RatingFxFy(double relDelta)
            {
                if (double.IsNaN(relDelta) || double.IsInfinity(relDelta)) return "n/a";
                // fx/fy relative stability (rule-of-thumb)
                if (relDelta < 0.002) return "Excellent";
                if (relDelta < 0.005) return "Good";
                if (relDelta < 0.01) return "OK";
                return "Large change";
            }

            static string RatingPrincipalPoint(double shiftPx)
            {
                if (double.IsNaN(shiftPx) || double.IsInfinity(shiftPx)) return "n/a";
                if (shiftPx < 0.5) return "Excellent";
                if (shiftPx < 1.0) return "Good";
                if (shiftPx < 2.0) return "OK";
                return "Large shift";
            }

            static string RatingDistortionDelta(double meanPx)
            {
                if (double.IsNaN(meanPx) || double.IsInfinity(meanPx)) return "n/a";
                if (meanPx < 0.2) return "Essentially identical";
                if (meanPx < 0.5) return "Minor difference";
                if (meanPx < 1.0) return "Noticeable difference";
                return "Significant mismatch";
            }

            if (!r.IsComparable)
            {
                sb.AppendLine("MONO: Not comparable.");
                if (!string.IsNullOrWhiteSpace(r.Notes))
                    sb.AppendLine($"  Notes: {r.Notes}");
                return sb;
            }

            sb.AppendLine("MONO (intrinsics + distortion impact):");
            sb.AppendLine($"  Images used: A {r.ImagesUsedA}/{r.ImageTotalA}   |   B {r.ImagesUsedB}/{r.ImageTotalB}");
            sb.AppendLine($"  Reprojection RMS:   A {F(r.ReprojectionRmsA)} px   |   B {F(r.ReprojectionRmsB)} px   |   Δ {F(r.ReprojectionRmsB - r.ReprojectionRmsA)} px");
            sb.AppendLine($"  Projection RMS:     A {F(r.ProjectionRmsA)} px     |   B {F(r.ProjectionRmsB)} px     |   Δ {F(r.ProjectionRmsB - r.ProjectionRmsA)} px");
            sb.AppendLine($"  Max error:          A {F(r.MaxErrorA)} px          |   B {F(r.MaxErrorB)} px          |   Δ {F(r.MaxErrorB - r.MaxErrorA)} px");

            sb.AppendLine("  Intrinsics:");
            sb.AppendLine($"    fx: A {F(r.FxA)}   B {F(r.FxB)}   |   Δ {F(r.FxB - r.FxA)}   ({Pct(r.FxRelativeDelta)})  [{RatingFxFy(r.FxRelativeDelta)}]");
            sb.AppendLine($"    fy: A {F(r.FyA)}   B {F(r.FyB)}   |   Δ {F(r.FyB - r.FyA)}   ({Pct(r.FyRelativeDelta)})  [{RatingFxFy(r.FyRelativeDelta)}]");
            sb.AppendLine($"    c : A ({F(r.CxA)}, {F(r.CyA)})   B ({F(r.CxB)}, {F(r.CyB)})   |   shift {F(r.PrincipalPointShiftPx)} px  [{RatingPrincipalPoint(r.PrincipalPointShiftPx)}]");

            sb.AppendLine("  Distortion model impact (pixel-space, via undistort grid):");
            sb.AppendLine($"    mean Δ: {F(r.DistortionDeltaMeanPx)} px   |   p95 Δ: {F(r.DistortionDeltaP95Px)} px   |   max Δ: {F(r.DistortionDeltaMaxPx)} px");
            sb.AppendLine($"    Interpretation: {RatingDistortionDelta(r.DistortionDeltaMeanPx)}");

            if (!string.IsNullOrWhiteSpace(r.Notes))
                sb.AppendLine($"  Notes: {r.Notes}");

            return sb;
        }

        /// <summary>
        /// Display the stereo compare result (human readable)
        /// </summary>
        private StringBuilder DisplayStereoCompareResult(CompareStereoCalibrationResult r)
        {
            var sb = new StringBuilder();

            // Helper locals
            static string F(double v, string fmt = "0.###") =>
                (double.IsNaN(v) || double.IsInfinity(v)) ? "n/a" : v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

            static string Pct(double frac) =>
                (double.IsNaN(frac) || double.IsInfinity(frac)) ? "n/a" : (frac * 100.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%";

            static string RatingRotation(double deg)
            {
                if (double.IsNaN(deg) || double.IsInfinity(deg)) return "n/a";
                if (deg < 0.05) return "Excellent";
                if (deg < 0.10) return "Good";
                if (deg < 0.30) return "OK";
                return "Large change";
            }

            static string RatingBaselineRel(double rel)
            {
                if (double.IsNaN(rel) || double.IsInfinity(rel)) return "n/a";
                if (rel < 0.001) return "Excellent";
                if (rel < 0.003) return "Good";
                if (rel < 0.01) return "OK";
                return "Large change";
            }

            static string RatingDirection(double deg)
            {
                if (double.IsNaN(deg) || double.IsInfinity(deg)) return "n/a";
                if (deg < 0.05) return "Excellent";
                if (deg < 0.10) return "Good";
                if (deg < 0.30) return "OK";
                return "Large change";
            }

            static string Opt(double? v, string fmt = "0.###") =>
                (v is null || double.IsNaN(v.Value) || double.IsInfinity(v.Value)) ? "n/a" : v.Value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

            if (!r.IsComparable)
            {
                sb.AppendLine("STEREO: Not comparable.");
                if (!string.IsNullOrWhiteSpace(r.Notes))
                    sb.AppendLine($"  Notes: {r.Notes}");
                return sb;
            }

            sb.AppendLine("STEREO (camera-to-camera pose + baseline):");
            sb.AppendLine($"  Images used: A {r.ImagesUsedA}/{r.ImageTotalA}   |   B {r.ImagesUsedB}/{r.ImageTotalB}");
            sb.AppendLine($"  Stereo RMS:         A {F(r.RmsA)} px             |   B {F(r.RmsB)} px             |   Δ {F(r.RmsB - r.RmsA)} px");
            sb.AppendLine($"  Projection RMS:     A {F(r.ProjectionRmsA)} px   |   B {F(r.ProjectionRmsB)} px   |   Δ {F(r.ProjectionRmsB - r.ProjectionRmsA)} px");
            sb.AppendLine($"  Max error:          A {F(r.MaxErrorA)} px        |   B {F(r.MaxErrorB)} px        |   Δ {F(r.MaxErrorB - r.MaxErrorA)} px");

            sb.AppendLine("  Relative pose (Right wrt Left):");
            sb.AppendLine($"    Rotation diff: {F(r.RotationDiffAngleDeg)}°  [{RatingRotation(r.RotationDiffAngleDeg)}]");

            sb.AppendLine("  Baseline (translation vector):");
            sb.AppendLine($"    |t|: A {F(r.BaselineA)}   |   B {F(r.BaselineB)}   |   Δ {F(r.BaselineAbsDelta)}   ({Pct(r.BaselineRelativeDelta)})  [{RatingBaselineRel(r.BaselineRelativeDelta)}]");
            sb.AppendLine($"    Direction diff: {F(r.BaselineDirectionDiffDeg)}°  [{RatingDirection(r.BaselineDirectionDiffDeg)}]");

            // Optional diagnostics (only if supplied)
            if (r.MeanEpipolarErrorPx is not null ||
                r.RectifiedYDisparityStdPx is not null ||
                r.RectifiedYDisparityMaxAbsPx is not null)
            {
                sb.AppendLine("  Stereo diagnostics:");
                sb.AppendLine($"    Mean epipolar error: {Opt(r.MeanEpipolarErrorPx)} px");
                sb.AppendLine($"    Rectified y-disparity: std {Opt(r.RectifiedYDisparityStdPx)} px   |   max |Δy| {Opt(r.RectifiedYDisparityMaxAbsPx)} px");
            }

            if (!string.IsNullOrWhiteSpace(r.Notes))
                sb.AppendLine($"  Notes: {r.Notes}");

            return sb;
        }



        /// <summary>
        /// Mono compare result class. The result of calling 
        /// DoCompareMonoCalibration is stored here
        /// </summary>
        #pragma warning disable VSSpell001 // Spell Check
        public sealed class CompareMonoCalibrationResult
        {
            public bool IsComparable { get; set; }
            public string? Notes { get; set; }

            // Basic counts / meta
            public int ImageTotalA { get; set; }
            public int ImagesUsedA { get; set; }
            public int ImageTotalB { get; set; }
            public int ImagesUsedB { get; set; }

            // Existing quality metrics (so you can see if a “better” calibration is also “different”)
            public double ReprojectionRmsA { get; set; }
            public double ReprojectionRmsB { get; set; }
            public double ProjectionRmsA { get; set; }
            public double ProjectionRmsB { get; set; }
            public double MaxErrorA { get; set; }
            public double MaxErrorB { get; set; }

            // Intrinsic parameter deltas

            public double FxA { get; set; }
            public double FxB { get; set; }
            public double FyA { get; set; }
            public double FyB { get; set; }
            public double CxA { get; set; }
            public double CxB { get; set; }
            public double CyA { get; set; }
            public double CyB { get; set; }

            public double FxRelativeDelta { get; set; }   // |fxB - fxA| / fxA

            public double FyRelativeDelta { get; set; }
            public double PrincipalPointShiftPx { get; set; }

            // Distortion impact comparison in pixel space
            public double DistortionDeltaMeanPx { get; set; }
            public double DistortionDeltaP95Px { get; set; }
            public double DistortionDeltaMaxPx { get; set; }
        }
#pragma warning restore VSSpell001 // Spell Check

        /// <summary>
        /// Stereo compare result class. The result of calling 
        /// DoCompareStereoCalibration is stored here
        /// </summary>

        public sealed class CompareStereoCalibrationResult
        {
            public bool IsComparable { get; set; }
            public string? Notes { get; set; }

            public int ImageTotalA { get; set; }
            public int ImagesUsedA { get; set; }
            public int ImageTotalB { get; set; }
            public int ImagesUsedB { get; set; }

            public double RmsA { get; set; }
            public double RmsB { get; set; }
            public double ProjectionRmsA { get; set; }
            public double ProjectionRmsB { get; set; }
            public double MaxErrorA { get; set; }
            public double MaxErrorB { get; set; }

            // Rotation comparison
            public double RotationDiffAngleDeg { get; set; }

            // Translation comparison
            public double BaselineA { get; set; }
            public double BaselineB { get; set; }
            public double BaselineAbsDelta { get; set; }       // |baselineB - baselineA|
            public double BaselineRelativeDelta { get; set; }  // |...|/baselineA
            public double BaselineDirectionDiffDeg { get; set; }

            // Optional stereo diagnostics (only if you pass point pairs / rectified pairs)
            public double? MeanEpipolarErrorPx { get; set; }
            public double? RectifiedYDisparityStdPx { get; set; }
            public double? RectifiedYDisparityMaxAbsPx { get; set; }
        }

    }

    public static class CalibrationCompare
    {
        public static CompareMonoCalibrationResult DoCompareMonoCalibration(
                                                        CalibrationCameraData a,
                                                        CalibrationCameraData b,
                                                        System.Drawing.Size imageSize,
                                                        int gridCols = 32,
                                                        int gridRows = 24)
        {
            var result = new CompareMonoCalibrationResult
            {
                ImageTotalA = a.ImageTotal,
                ImagesUsedA = a.ImagesUsed,
                ImageTotalB = b.ImageTotal,
                ImagesUsedB = b.ImagesUsed,
                ReprojectionRmsA = a.RMS,
                ReprojectionRmsB = b.RMS,
                ProjectionRmsA = a.ProjectionRMS,
                ProjectionRmsB = b.ProjectionRMS,
                MaxErrorA = a.MaxError,
                MaxErrorB = b.MaxError,
            };

            if (a.Intrinsic is null || b.Intrinsic is null ||
                a.Distortion is null || b.Distortion is null)
            {
                result.IsComparable = false;
                result.Notes = "Missing IntrinsicMatrix or DistortionCoeffs in one/both calibrations.";
                return result;
            }

            // Extract fx,fy,cx,cy from K
            double fxA = a.Intrinsic[0, 0];
            double fyA = a.Intrinsic[1, 1];
            double cxA = a.Intrinsic[0, 2];
            double cyA = a.Intrinsic[1, 2];

            double fxB = b.Intrinsic[0, 0];
            double fyB = b.Intrinsic[1, 1];
            double cxB = b.Intrinsic[0, 2];
            double cyB = b.Intrinsic[1, 2];

            result.FxA = fxA; result.FxB = fxB;
            result.FyA = fyA; result.FyB = fyB;
            result.CxA = cxA; result.CxB = cxB;
            result.CyA = cyA; result.CyB = cyB;

            result.FxRelativeDelta = SafeRelDelta(fxA, fxB);
            result.FyRelativeDelta = SafeRelDelta(fyA, fyB);
            result.PrincipalPointShiftPx = Math.Sqrt(Sq(cxB - cxA) + Sq(cyB - cyA));

            // Distortion "impact" delta in pixel space: compare undistorted locations for a grid of pixels
            var pixels = BuildPixelGrid(imageSize, gridCols, gridRows);
            var disp = ComputeUndistortDisplacementDeltasPx(
                pixels,
                a.Intrinsic, a.Distortion,
                b.Intrinsic, b.Distortion);

            if (disp.Count > 0)
            {
                disp.Sort();
                result.DistortionDeltaMeanPx = disp.Average();
                result.DistortionDeltaMaxPx = disp[^1];
                result.DistortionDeltaP95Px = disp[(int)Math.Floor(0.95 * (disp.Count - 1))];
            }

            result.IsComparable = true;
            return result;
        }

        private static double SafeRelDelta(double a, double b)
            => Math.Abs(a) < 1e-12 ? double.NaN : Math.Abs(b - a) / Math.Abs(a);

        private static double Sq(double x) => x * x;

        private static List<System.Drawing.PointF> BuildPixelGrid(System.Drawing.Size size, int cols, int rows)
        {
            cols = Math.Max(cols, 2);
            rows = Math.Max(rows, 2);

            var pts = new List<System.Drawing.PointF>(cols * rows);

            // Keep off extreme edges slightly
            float marginX = Math.Max(1, size.Width * 0.02f);
            float marginY = Math.Max(1, size.Height * 0.02f);

            for (int r = 0; r < rows; r++)
            {
                float y = marginY + (size.Height - 2 * marginY) * (r / (float)(rows - 1));
                for (int c = 0; c < cols; c++)
                {
                    float x = marginX + (size.Width - 2 * marginX) * (c / (float)(cols - 1));
                    pts.Add(new System.Drawing.PointF(x, y));
                }
            }

            return pts;
        }

        private static List<double> ComputeUndistortDisplacementDeltasPx(
                                        List<System.Drawing.PointF> pixelPoints,
                                        Emgu.CV.Matrix<double> kA, Emgu.CV.Matrix<double> dA,
                                        Emgu.CV.Matrix<double> kB, Emgu.CV.Matrix<double> dB)
        {
            int n = pixelPoints.Count;
            if (n == 0)
                return [];

            // Pack as CV_32FC2: [x0, y0, x1, y1, ...]
            var srcData = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                srcData[(i * 2) + 0] = pixelPoints[i].X;
                srcData[(i * 2) + 1] = pixelPoints[i].Y;
            }

            using var src = new Mat(n, 1, DepthType.Cv32F, 2);
            Marshal.Copy(srcData, 0, src.DataPointer, srcData.Length);

            using var undA = new Mat();
            using var undB = new Mat();

            // If P is provided, output is in pixel coordinates (not normalized).
            CvInvoke.UndistortPoints(src, undA, kA, dA, null, kA);
            CvInvoke.UndistortPoints(src, undB, kB, dB, null, kB);

            var undAData = new float[n * 2];
            var undBData = new float[n * 2];

            Marshal.Copy(undA.DataPointer, undAData, 0, undAData.Length);
            Marshal.Copy(undB.DataPointer, undBData, 0, undBData.Length);

            var deltas = new List<double>(n);
            for (int i = 0; i < n; i++)
            {
                double ax = undAData[(i * 2) + 0];
                double ay = undAData[(i * 2) + 1];

                double bx = undBData[(i * 2) + 0];
                double by = undBData[(i * 2) + 1];

                double dx = bx - ax;
                double dy = by - ay;
                deltas.Add(Math.Sqrt(dx * dx + dy * dy));
            }

            return deltas;
        }

        public static CompareStereoCalibrationResult DoCompareStereoCalibration(
                            CalibrationStereoCameraData a,
                            CalibrationStereoCameraData b)
        {
            var result = new CompareStereoCalibrationResult
            {
                ImageTotalA = a.ImageTotal,
                ImagesUsedA = a.ImagesUsed,
                ImageTotalB = b.ImageTotal,
                ImagesUsedB = b.ImagesUsed,
                RmsA = a.RMS,
                RmsB = b.RMS,
                ProjectionRmsA = a.ProjectionRMS,
                ProjectionRmsB = b.ProjectionRMS,
                MaxErrorA = a.MaxError,
                MaxErrorB = b.MaxError,
            };

            if (a.Rotation is null || b.Rotation is null || a.Translation is null || b.Translation is null)
            {
                result.IsComparable = false;
                result.Notes = "Missing Rotation or Translation in one/both stereo calibrations.";
                return result;
            }

            // Rotation diff: Rdiff = Rb * Ra^T
            var Ra = a.Rotation;
            var Rb = b.Rotation;

            using var RaT = Ra.Transpose();
            using var Rdiff = new Matrix<double>(3, 3);
            CvInvoke.Gemm(Rb, RaT, 1.0, null, 0.0, Rdiff); // Rdiff = Rb * RaT

            result.RotationDiffAngleDeg = RotationAngleDeg(Rdiff);

            // Baseline lengths
            var ta = ToVec3(a.Translation);
            var tb = ToVec3(b.Translation);

            double ba = Norm(ta);
            double bb = Norm(tb);

            result.BaselineA = ba;
            result.BaselineB = bb;
            result.BaselineAbsDelta = Math.Abs(bb - ba);
            result.BaselineRelativeDelta = (Math.Abs(ba) < 1e-12) ? double.NaN : Math.Abs(bb - ba) / Math.Abs(ba);

            // Baseline direction angle
            result.BaselineDirectionDiffDeg = AngleBetweenDeg(ta, tb);

            result.IsComparable = true;
            return result;
        }

        private static double RotationAngleDeg(Matrix<double> R)
        {
            // θ = acos((trace(R)-1)/2)
            double trace = R[0, 0] + R[1, 1] + R[2, 2];
            double cos = (trace - 1.0) * 0.5;

            // Clamp for numerical safety
            cos = Math.Max(-1.0, Math.Min(1.0, cos));

            double thetaRad = Math.Acos(cos);
            return thetaRad * (180.0 / Math.PI);
        }

        private static (double x, double y, double z) ToVec3(Matrix<double> t)
        {
            // Accept 3x1 or 1x3
            if (t.Rows == 3 && t.Cols == 1) return (t[0, 0], t[1, 0], t[2, 0]);
            if (t.Rows == 1 && t.Cols == 3) return (t[0, 0], t[0, 1], t[0, 2]);
            throw new ArgumentException("Translation must be 3x1 or 1x3.");
        }

        private static double Norm((double x, double y, double z) v)
            => Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);

        private static double Dot((double x, double y, double z) a, (double x, double y, double z) b)
            => a.x * b.x + a.y * b.y + a.z * b.z;

        private static double AngleBetweenDeg((double x, double y, double z) a, (double x, double y, double z) b)
        {
            double na = Norm(a);
            double nb = Norm(b);
            if (na < 1e-12 || nb < 1e-12) return double.NaN;

            double cos = Dot(a, b) / (na * nb);
            cos = Math.Max(-1.0, Math.Min(1.0, cos));
            return Math.Acos(cos) * (180.0 / Math.PI);
        }
    }
}
