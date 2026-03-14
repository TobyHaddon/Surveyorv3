using iText.Bouncycastle.Crypto;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using static Surveyor.CalibProject.DataClass;
            


namespace Surveyor.User_Controls
{
    public sealed partial class Export : UserControl
    {
        // Reporter
        private Reporter? report = null;

        private bool _isReady;

        private ContentDialog? ParentDialog { get; set; } = null;

        public Export()
        {
            InitializeComponent();

            Loaded += ExportUserControl_Loaded;
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

        public void SetupForContentDialog(ContentDialog dialog, CalibProject calibProject)
        {
            ParentDialog = dialog;
            DataContext = calibProject;

            // Unload an previously handle. This so we don't get multiple Save dialogs
            dialog.PrimaryButtonClick -= ExportDialog_Save_Click;
            dialog.PrimaryButtonClick += ExportDialog_Save_Click;
        }


        private void ExportUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _isReady = true;

            // Ensure a selection exists, then populate preview/code once everything is built
            if (ExportSamplesNav.SelectedItem is null && ExportSamplesNav.MenuItems.Count > 0)
                ExportSamplesNav.SelectedItem = ExportSamplesNav.MenuItems[0];

            // Find the best calibration and set that as the default model
            if (DataContext is CalibProject calibProject)
            {
                // Native is only for a stereo calibration result
                if (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                    calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet)
                {
                    FormatNative.IsEnabled = true;
                }
                else
                {
                    FormatNative.IsEnabled = false;
                    // Default export the Text
                    FormatText.IsChecked = true;
                }

                // See what results we have available
                bool IsK1K2P1P2Available = false;
                bool IsK1K2K3P1P2Available = false;
                bool IsK1K2K3K4P1P2Available = false;
                bool IsK1K2K3K4P1P2K5K6Available = false;

                // Check availability and get RMS values etc
                switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                {
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        foreach (CalibrationParameters p in Enum.GetValues(typeof(CalibrationParameters)))
                        {
                            if (calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)p] is not null &&
                                calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)p] is not null &&
                                calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)p] is not null)
                            {
                                string resultText = $"Re-projection RMS: {calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)p]!.RMS:F3}px";
                                switch (p)
                                {
                                    case CalibrationParameters.K1K2P1P2:
                                        IsK1K2P1P2Available = true;
                                        Model_K1K2P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3P1P2:
                                        IsK1K2K3P1P2Available = true;
                                        Model_K1K2K3P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3K4P1P2:
                                        IsK1K2K3K4P1P2Available = true;
                                        Model_K1K2K3K4P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3K4P1P2K5K6:
                                        IsK1K2K3K4P1P2K5K6Available = true;
                                        Model_K1K2K3K4P1P2K5K6_Result.Text = resultText;
                                        break;
                                }
                            }
                        }
                        break;
                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        foreach (CalibrationParameters p in Enum.GetValues(typeof(CalibrationParameters)))
                        {
                            if (calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)p] is not null)
                            {
                                string resultText = $"Re-projection RMS: {calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)p]!.RMS:F3}px";
                                switch (p)
                                {
                                    case CalibrationParameters.K1K2P1P2:
                                        IsK1K2P1P2Available = true;
                                        Model_K1K2P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3P1P2:
                                        IsK1K2K3P1P2Available = true;
                                        Model_K1K2K3P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3K4P1P2:
                                        IsK1K2K3K4P1P2Available = true;
                                        Model_K1K2K3K4P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3K4P1P2K5K6:
                                        IsK1K2K3K4P1P2K5K6Available = true;
                                        Model_K1K2K3K4P1P2K5K6_Result.Text = resultText;
                                        break;
                                }
                            }
                        }
                        break;
                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        foreach (CalibrationParameters p in Enum.GetValues(typeof(CalibrationParameters)))
                        {
                            if (calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)p] is not null &&
                                calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)p] is not null)
                            {
                                string resultText = $"Repro RMS: Left={calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)p]!.ReprojectionRMS:F3}px,"+
                                                    $" Right={calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)p]!.ReprojectionRMS:F3}px";
                                switch (p)
                                {
                                    case CalibrationParameters.K1K2P1P2:
                                        IsK1K2P1P2Available = true;
                                        Model_K1K2P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3P1P2:
                                        IsK1K2K3P1P2Available = true;
                                        Model_K1K2K3P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3K4P1P2:
                                        IsK1K2K3K4P1P2Available = true;
                                        Model_K1K2K3K4P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3K4P1P2K5K6:
                                        IsK1K2K3K4P1P2K5K6Available = true;
                                        Model_K1K2K3K4P1P2K5K6_Result.Text = resultText;
                                        break;
                                }
                            }
                        }
                        break;
                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        foreach (CalibrationParameters p in Enum.GetValues(typeof(CalibrationParameters)))
                        {
                            if (calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)p] is not null)
                            {
                                string resultText = $"Repro RMS: {calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)p]!.ReprojectionRMS:F3}px";
                                switch (p)
                                {
                                    case CalibrationParameters.K1K2P1P2:
                                        IsK1K2P1P2Available = true;
                                        Model_K1K2P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3P1P2:
                                        IsK1K2K3P1P2Available = true;
                                        Model_K1K2K3P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3K4P1P2:
                                        IsK1K2K3K4P1P2Available = true;
                                        Model_K1K2K3K4P1P2_Result.Text = resultText;
                                        break;
                                    case CalibrationParameters.K1K2K3K4P1P2K5K6:
                                        IsK1K2K3K4P1P2K5K6Available = true;
                                        Model_K1K2K3K4P1P2K5K6_Result.Text = resultText;
                                        break;
                                }
                            }
                        }
                        break;
                }


                // Set availability
                Model_K1K2P1P2.IsEnabled = IsK1K2P1P2Available;
                Model_K1K2K3P1P2.IsEnabled = IsK1K2K3P1P2Available;
                Model_K1K2K3K4P1P2.IsEnabled = IsK1K2K3K4P1P2Available;
                Model_K1K2K3K4P1P2K5K6.IsEnabled = IsK1K2K3K4P1P2K5K6Available;


                // Default to the best result
                CalibrationParameters? calibParams = GetBestCalibrationRersultConsideringStereoMonoMediaSetMode(calibProject);

                switch (calibParams)
                {
                    case CalibrationParameters.K1K2P1P2:
                        Model_K1K2P1P2.IsChecked = true;
                        break;
                    case CalibrationParameters.K1K2K3P1P2:
                        Model_K1K2K3P1P2.IsChecked = true;
                        break;
                    case CalibrationParameters.K1K2K3K4P1P2:
                        Model_K1K2K3K4P1P2.IsChecked = true;
                        break;
                    case CalibrationParameters.K1K2K3K4P1P2K5K6:
                        Model_K1K2K3K4P1P2K5K6.IsChecked = true;
                        break;
                    default:
                        Model_K1K2P1P2.IsChecked = true;
                        break;
                }
            }

            // Apply current format state now that visuals exist
            FormatChanged(this, new RoutedEventArgs());
            _ = LoadSampleForSelectionAsync();
        }



        ///
        /// Events
        /// 


        /// <summary>
        /// Update code sample display when selection changes.
        /// </summary>
        private void ExportSamplesNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            _ = LoadSampleForSelectionAsync();
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void ExportDialog_Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args) => _ = ExportDialogSaveAsync(args);
        private async Task ExportDialogSaveAsync(ContentDialogButtonClickEventArgs args)
        {
            if (DataContext is CalibProject calibProject)
            {                
                var picker = new FileSavePicker();
                nint hWnd;
                hWnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(picker, hWnd);

                // Get suggest file stem from project or default
                string suggestedFileStem = Path.GetFileNameWithoutExtension(calibProject.Data.Info.ProjectFileName) ?? "CameraCalibrationExport";

                // Suggested file types based on selected format
                if (FormatText.IsChecked == true)
                {
                    picker.FileTypeChoices.Add("Text", [".txt"]);
                    picker.SuggestedFileName = suggestedFileStem + ".txt";
                }
                else if (FormatOpenCV.IsChecked == true)
                {
                    picker.FileTypeChoices.Add("OpenCV Data", [".yaml", ".yml", ".json"]);
                    picker.SuggestedFileName = suggestedFileStem + ".yml";
                }
                else
                {
                    picker.FileTypeChoices.Add("Native", [".calib"]);
                    picker.SuggestedFileName = suggestedFileStem + ".calib";
                }

                var file = await picker.PickSaveFileAsync();
                if (file == null)
                {
                    args.Cancel = true; // keep dialog open if user cancels
                    return;
                }

                string payload = CreateExportPayload();

                // Write export content to 'file'                
                await FileIO.WriteTextAsync(file, payload);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CopySampleButton_Click(object sender, RoutedEventArgs e)
        {
            string textToCopy = string.Empty;
            if (FormatText.IsChecked != true)
            {
                textToCopy = CodeMarkdown.Text ?? string.Empty;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(textToCopy ?? string.Empty);
            Clipboard.SetContent(dataPackage);
        }


        /// <summary>
        /// Email support for additional export formats
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MailLinkFormat_Click(Hyperlink sender, HyperlinkClickEventArgs args) => _ = MailLinkFormatAsync();
        private static async Task MailLinkFormatAsync()
        {
            string subject = Uri.EscapeDataString("Additional Export Format Request");
            string body = Uri.EscapeDataString("Please write your request here. Include an example of the format as an attachment.");
            var uri = new Uri($"mailto:toby.solo@outlook.com?subject={subject}&body={body}");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }


        /// <summary>
        /// Email support for additional distortion model coefficient combinations
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MailLinkModel_Click(Hyperlink sender, HyperlinkClickEventArgs args) => _ = MailLinkModelAsync();
        private static async Task MailLinkModelAsync()
        {
            string subject = Uri.EscapeDataString("Additional Distortion Models Request");
            string body = Uri.EscapeDataString("Please write your request here.");
            var uri = new Uri($"mailto:toby.solo@outlook.com?subject={subject}&body={body}");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }



        ///
        /// Private
        /// 


        /// <summary>
        /// 
        /// </summary>
        /// <param name="tag"></param>
        /// <returns></returns>
        private string GetFileNameFor(string tag)
        {
            if (FormatNative.IsChecked == true)
            {
                return tag switch
                {
                    "cs" => "LoadNative.cs",
                    "cpp" => "LoadNative.cpp",
                    "py" => "LoadNative.py",
                    _ => string.Empty
                };
            }
            else if (FormatOpenCV.IsChecked == true)
            {
                return tag switch
                {
                    "cs" => "LoadOpenCV.cs",
                    "cpp" => "LoadOpenCV.cpp",
                    "py" => "LoadOpenCV.py",
                    _ => string.Empty
                };
            }
            return string.Empty;
        }

        private static async Task<string?> TryLoadTextAsync(string path)
        {
            try
            {
                if (File.Exists(path))
                    return await Task.Run(() => File.ReadAllText(path));
            }
            catch { }

            try
            {
                var uri = new Uri($"ms-appx:///{path.Replace("\\", "/")}");
                StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                return await FileIO.ReadTextAsync(file);
            }
            catch { }

            return null;
        }

        private static string GetMarkdownLang(string tag) => tag switch
        {
            "cs" => "csharp",
            "cpp" => "cpp",
            "py" => "python",
            _ => string.Empty
        };


        /// <summary>
        /// User selected a different Export format
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FormatChanged(object sender, RoutedEventArgs e)
        {
            if (!_isReady || PreviewText is null || ExportSamplesNav is null)
                return;

            LoadExportPreview();
        }



        /// <summary>
        /// User changed the distortion model to export
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ModelChanged(object sender, RoutedEventArgs e)
        {
            if (!_isReady || PreviewText is null || ExportSamplesNav is null)
                return;

            LoadExportPreview();
        }


        /// <summary>
        /// Display a preview of the export payload
        /// </summary>
        /// <returns></returns>
        private void LoadExportPreview()
        {
            string previewText = CreateExportPayload();
            PreviewText.Text = previewText;
        }


        /// <summary>
        /// Load from disk and display the code sample for the current selection
        /// </summary>
        /// <returns></returns>
        private string CreateExportPayload()
        {
            string payload = string.Empty;

            if (DataContext is CalibProject calibProject)
            {
                // Return the best stereo calibration set
                CalibrationParameters? calibrationParameters = GetSelectedModel();  //???GetBestCalibrationRersultConsideringStereoMonoMediaSetMode(calibProject);
                if (calibrationParameters is not null)
                {

                    if (FormatNative.IsChecked == true)
                    {
                        payload = CreateExportPayloadFormatNative(calibProject, (CalibrationParameters)calibrationParameters);
                    }
                    else if (FormatOpenCV.IsChecked == true)
                    {
                        payload = CreateExportPayloadFormatOpenCV(calibProject, (CalibrationParameters)calibrationParameters);
                    }
                    else if (FormatText.IsChecked == true)
                    {
                        payload = CreateExportPayloadFormatText(calibProject, (CalibrationParameters)calibrationParameters);
                    }
                }
            }

            return payload;
        }


        /// <summary>
        /// Create string with output in the native JSON format
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns></returns>
        private static string CreateExportPayloadFormatNative(CalibProject calibProject, CalibrationParameters calibrationParameters)
        {
            string payload = string.Empty;

            // Get the stereo, left mono and right mono result set
            CalibrationStereoCameraData calibrationStereoCameraData = calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)calibrationParameters]!;

            MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
            MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

            if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
            {
                // Populate the CalibrationData
                CalibrationData calibrationData = new()
                {
                    Description = $"{Path.GetFileNameWithoutExtension(calibProject.Data.Info.ProjectFileName)} / {calibrationParameters}",
                    StereoCameraCalibration = calibrationStereoCameraData
                };
                calibrationData.LeftCameraCalibration.ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/);
                calibrationData.LeftCameraCalibration.ImageSize[0, 0] = (int)calibProject.Data.Media.FrameWidth;
                calibrationData.LeftCameraCalibration.ImageSize[0, 1] = (int)calibProject.Data.Media.FrameHeight;

                calibrationData.LeftCameraCalibration.ImageTotal = leftMonoCalibrationCameraData.ImageTotal;
                calibrationData.LeftCameraCalibration.ImagesUsed = leftMonoCalibrationCameraData.ImagesUsed;
                calibrationData.LeftCameraCalibration.Intrinsic = leftMonoCalibrationCameraData.IntrinsicMatrix;
                calibrationData.LeftCameraCalibration.Distortion = leftMonoCalibrationCameraData.DistortionCoeffs;
                calibrationData.LeftCameraCalibration.RMS = leftMonoCalibrationCameraData.ReprojectionRMS;
                calibrationData.LeftCameraCalibration.ProjectionRMS = leftMonoCalibrationCameraData.ProjectionRMS;
                calibrationData.LeftCameraCalibration.MaxError = leftMonoCalibrationCameraData.MaxError;

                calibrationData.RightCameraCalibration.ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/);
                calibrationData.RightCameraCalibration.ImageSize[0, 0] = (int)calibProject.Data.Media.FrameWidth;
                calibrationData.RightCameraCalibration.ImageSize[0, 1] = (int)calibProject.Data.Media.FrameHeight;
                calibrationData.RightCameraCalibration.ImageTotal = rightMonoCalibrationCameraData.ImageTotal;
                calibrationData.RightCameraCalibration.ImagesUsed = rightMonoCalibrationCameraData.ImagesUsed;
                calibrationData.RightCameraCalibration.Intrinsic = rightMonoCalibrationCameraData.IntrinsicMatrix;
                calibrationData.RightCameraCalibration.Distortion = rightMonoCalibrationCameraData.DistortionCoeffs;
                calibrationData.RightCameraCalibration.RMS = rightMonoCalibrationCameraData.ReprojectionRMS;
                calibrationData.RightCameraCalibration.ProjectionRMS = rightMonoCalibrationCameraData.ProjectionRMS;
                calibrationData.RightCameraCalibration.MaxError = rightMonoCalibrationCameraData.MaxError;


                // Add the camera serial numbers
                calibrationData.LeftCameraCalibration.CameraID = calibProject.Data.Media.LeftCameraID;
                calibrationData.RightCameraCalibration.CameraID = calibProject.Data.Media.RightCameraID;

                calibrationData.SaveToJson(out payload, true/*pretty*/);
            }                

            return payload;
        }


        /// <summary>
        /// Create the export data
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns></returns>
        private static string CreateExportPayloadFormatOpenCV(CalibProject calibProject, CalibrationParameters calibrationParameters)
        {
            var stereo = calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)calibrationParameters];
            var left = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
            var right = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (stereo is null || left is null || right is null)
                        return string.Empty;
                    break;
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (left is null || right is null)
                        return string.Empty;
                    break;
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    if (left is null)
                        return string.Empty;
                    break;
            }


            int imgWidth  = (int)calibProject.Data.Media.FrameWidth;
            int imgHeight = (int)calibProject.Data.Media.FrameHeight;

            string FormatCvMatrix<T>(Emgu.CV.Matrix<T> m)
                where T : struct, IConvertible
            {
                // dt: f for float/double, i for int; EMGU.CV commonly backs with double
                string dt = typeof(T) == typeof(float) ? "f" :
                            typeof(T) == typeof(int)   ? "i" : "d";
                var data = new System.Text.StringBuilder();
                for (int r = 0; r < m.Rows; r++)
                {
                    for (int c = 0; c < m.Cols; c++)
                    {
                        data.Append(Convert.ToString(m[r, c], System.Globalization.CultureInfo.InvariantCulture));
                        if (!(r == m.Rows - 1 && c == m.Cols - 1))
                            data.Append(", ");
                    }
                }
                return $"!!opencv-matrix\n  rows: {m.Rows}\n  cols: {m.Cols}\n  dt: {dt}\n  data: [{data}]";
            }

            string FormatCvMatArray<T>(Emgu.CV.Matrix<T> m) where T : struct, IConvertible => FormatCvMatrix(m);
            string FormatOptional<T>(Emgu.CV.Matrix<T>? m) where T : struct, IConvertible => m is null ? "null" : FormatCvMatrix(m);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("%YAML:1.0");
            if (left is not null)
            {
                sb.AppendLine("left_camera:");
                sb.AppendLine($"  image_width: {imgWidth}");
                sb.AppendLine($"  image_height: {imgHeight}");
                if (left.IntrinsicMatrix is not null)
                    sb.AppendLine("  camera_matrix: " + FormatCvMatArray(left.IntrinsicMatrix).Replace("\n", "\n  "));
                if (left.DistortionCoeffs is not null)
                    sb.AppendLine("  distortion_coefficients: " + FormatCvMatArray(left.DistortionCoeffs).Replace("\n", "\n  "));
                sb.AppendLine($"  rms: {left.ReprojectionRMS.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                sb.AppendLine($"  camera_id: \"{calibProject.Data.Media.LeftCameraID}\"");
            }

            // Allow the modes that include right camera mono results
            if (right is not null &&
                (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                 calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet ||
                 calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoPairOnlyMediaSet))
            {
                sb.AppendLine("right_camera:");
                sb.AppendLine($"  image_width: {imgWidth}");
                sb.AppendLine($"  image_height: {imgHeight}");
                if (right.IntrinsicMatrix is not null)
                    sb.AppendLine("  camera_matrix: " + FormatCvMatArray(right.IntrinsicMatrix).Replace("\n", "\n  "));
                if (right.DistortionCoeffs is not null)
                    sb.AppendLine("  distortion_coefficients: " + FormatCvMatArray(right.DistortionCoeffs).Replace("\n", "\n  "));
                sb.AppendLine($"  rms: {right.ReprojectionRMS.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                sb.AppendLine($"  camera_id: \"{calibProject.Data.Media.RightCameraID}\"");
            }

            // Stereo terms (if available in your data type)
            if (stereo is not null &&
                (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                 calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet))
            {
                sb.AppendLine("stereo:");
                sb.AppendLine("  R: " + FormatOptional(stereo.Rotation).Replace("\n", "\n  "));
                sb.AppendLine("  T: " + FormatOptional(stereo.Translation).Replace("\n", "\n  "));
                //???      sb.AppendLine("  E: " + FormatOptional(stereo.E).Replace("\n", "\n  "));
                //???      sb.AppendLine("  F: " + FormatOptional(stereo.F).Replace("\n", "\n  "));
            }

            return sb.ToString();
        }


        /// <summary>
        /// Create a text version of the calibration data outputting all the values
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns></returns>
        private static string CreateExportPayloadFormatText(CalibProject calibProject, CalibrationParameters calibrationParameters)
        {
            StringBuilder payload = new();

            // Get the stereo, left mono and right mono result set
            CalibrationStereoCameraData calibrationStereoCameraData = calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)calibrationParameters]!;
            MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
            MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

            // Is there any mono component to the export?
            if (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoPairOnlyMediaSet)
            {
                payload.Append(GetMonoPayload("Left", leftMonoCalibrationCameraData));
                payload.Append(GetMonoPayload("Right", rightMonoCalibrationCameraData));
            }
            else if (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoSingleOnlyMediaSet)
            {
                payload.Append(GetMonoPayload("Left", leftMonoCalibrationCameraData));
            }

            // Is there a stereo component to the export?
            if (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet)
            {
                payload.Append(GetStereoPayload(calibrationStereoCameraData));
            }

            // Info Data
            payload.Append(GetInfoPayload(calibProject.Data.Info));

            // Media Data
            payload.Append(GetMediaPayload(calibProject.Data.Media));

            // Sync Data
            if (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet)
            {
                payload.Append(GetSyncPayload(calibProject.Data.Sync));
            }


            //
            // Helpers to format each section
            //

            // Format the CalibProject Info payload
            string GetInfoPayload( CalibProject.DataClass.InfoClass info)
            {
                StringBuilder sb = new();
                sb.AppendLine("=== Project Info ===");
                sb.AppendLine($"Project File: {info.ProjectFileName}");
                sb.AppendLine();
                return sb.ToString();
            }

            // Format the CalibProject Media payload
            string GetMediaPayload( CalibProject.DataClass.MediaClass media)
            {
                StringBuilder sb = new();
                sb.AppendLine("=== Media Info ===");
                sb.AppendLine($"Stereo/Mono Mode: {media.StereoMonoMediaSetMode.ToString()}");
                sb.AppendLine($"Frame size: {media.FrameWidth} x {media.FrameHeight}");
                // Any left camera data required?
                if (media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet ||
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoPairOnlyMediaSet ||
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoSingleOnlyMediaSet)
                {   
                    if (!string.IsNullOrEmpty(media.LeftCameraID))
                        sb.AppendLine($"Left Camera ID: {media.LeftCameraID}");
                    else
                        sb.AppendLine($"Left Camera ID: Unknown");
                }
                // Any right camera data required?
                if (media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet ||
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoPairOnlyMediaSet)
                {
                    if (!string.IsNullOrEmpty(media.RightCameraID))
                        sb.AppendLine($"Right Camera ID: {media.RightCameraID}");
                    else
                        sb.AppendLine($"Right Camera ID: Unknown");
                }
                // Any left mono media data?
                if (media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoPairOnlyMediaSet ||
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoSingleOnlyMediaSet)
                {
                    sb.AppendLine($"Left Mono File: {media.LeftMonoMP4Path}");
                }
                // Any right mono media data?
                if (media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoPairOnlyMediaSet)
                {
                    sb.AppendLine($"Right Mono File: {media.RightMonoMP4Path}");
                }
                // Any left stereo media data?
                if (media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||                
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet)
                {
                    sb.AppendLine($"Left Stereo File: {media.LeftStereoMP4Path}");
                }
                // Any right stereo media data?
                if (media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                    media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet)
                {
                    sb.AppendLine($"Right Stereo File: {media.RightStereoMP4Path}");
                }                
                sb.AppendLine();

                return sb.ToString();
            }

            // Format the CalibProject Sync payload
            string GetSyncPayload( CalibProject.DataClass.SyncClass sync)
            {
                StringBuilder sb = new();

                if (sync.IsSynchronized)
                {
                    sb.AppendLine("=== Sync Info ===");
                    sb.AppendLine($"Sync Point Left (frames): {sync.SyncFrameIndexLeft}");
                    sb.AppendLine($"Sync Point Right (frames): {sync.SyncFrameIndexRight}");
                    sb.AppendLine();
                }
                return sb.ToString();
            }

            // Format the CalibProject mono payload
            string GetMonoPayload(string side, MonoCalibrationCameraData? mono)
            {
                StringBuilder sb = new();
                if (mono is not null)
                {
                    sb.AppendLine($"=== Mono {side} Calibration ===");
                    sb.AppendLine($"{side} Images Used: {mono.ImagesUsed} / {mono.ImageTotal}");
                    sb.AppendLine($"{side} Re-projection RMS: {mono.ReprojectionRMS:F3}px");
                    sb.AppendLine($"{side} Projection RMS: {mono.ProjectionRMS:F3}px");
                    sb.AppendLine($"{side} Max Error: {mono.ProjectionRMS:F3}px");

                    sb.AppendLine($"{side} Intrinsic Matrix:");
                    var m = mono.IntrinsicMatrix;
                    if (m is not null)
                    {
                        if (m.Rows == 3 && m.Cols == 3)
                        {
                            sb.AppendLine($"\tfx: {m[0, 0]:F4}\tFocal length in pixel units in the x-direction");
                            sb.AppendLine($"\tfy: {m[1, 1]:F4}\tFocal length in pixel units in the y-direction");
                            sb.AppendLine($"\tcx: {m[0, 2]:F4}\tThe x-coordinate of the optical center on the image sensor");
                            sb.AppendLine($"\tcy: {m[1, 2]:F4}\tThe y-coordinate of the optical center on the image sensor");
                            sb.AppendLine($"\tskew: {m[0, 1]:F4}\tPixel skew factor - represents non-rectangular pixels");
                        }
                        else
                        {
                            for (int r = 0; r < m.Rows; r++)
                                for (int c = 0; c < m.Cols; c++)
                                    sb.AppendLine($"[{r},{c}]: {m[r, c]:F4}");
                        }                     
                    }
                    else
                    {
                        sb.AppendLine($"\tEmpty!");
                    }

                    sb.AppendLine($"{side} Distortion Coefficients:");
                    var d = mono.DistortionCoeffs;
                    if (d is not null)
                    {
                        // Flatten coefficients row-major
                        var vals = new List<double>(d.Rows * d.Cols);
                        for (int r = 0; r < d.Rows; r++)
                            for (int c = 0; c < d.Cols; c++)
                                vals.Add(Convert.ToDouble(d[r, c], System.Globalization.CultureInfo.InvariantCulture));

                        // Common OpenCV layouts:
                        // 5: k1 k2 p1 p2 k3
                        // 8: k1 k2 p1 p2 k3 k4 k5 k6
                        // 12: k1 k2 p1 p2 k3 k4 k5 k6 s1 s2 s3 s4 (thin prism)
                        // 14: + tauX tauY (tilt)
                        string[] names = vals.Count switch
                        {
                            5 => ["k1", "k2", "p1", "p2", "k3"],
                            8 => ["k1", "k2", "p1", "p2", "k3", "k4", "k5", "k6"],
                            12 => ["k1", "k2", "p1", "p2", "k3", "k4", "k5", "k6", "s1", "s2", "s3", "s4"],
                            14 => ["k1", "k2", "p1", "p2", "k3", "k4", "k5", "k6", "s1", "s2", "s3", "s4", "tauX", "tauY"],
                            _ => []
                        };

                        // Descriptions in key value pairs
                        var description = new Dictionary<string, string>
                        {
                            { "k1", "Main radial distortion (barrel/pincushion)" },
                            { "k2", "Stronger radial refinement (affects outer image)" },
                            { "p1", "Tangential (lens tilted left-right)" },
                            { "p2", "Tangential (lens tilted up-down)" },
                            { "k3", "Higher order; corrects strong wide-angle distortion" },
                            { "k4", "Extra high-order radial terms; lenses with extreme peripheral distortion, FOV > 120 degrees" },
                            { "k5", "Extra high-order radial terms; lenses with extreme peripheral distortion, FOV > 120 degrees" },
                            { "k6", "Extra high-order radial terms; lenses with extreme peripheral distortion, FOV > 120 degrees" },
                            { "s1", "Thin prism horizontal" },
                            { "s2", "Thin prism vertical" },
                            { "s3", "Higher-order prism" },
                            { "s4", "Higher-order prism" },
                            { "tauX", "Sensor tilt about x-axis" },
                            { "tauY", "Sensor tilt about y-axis" }
                        };

                        for (int i = 0; i < vals.Count; i++)
                        {
                            var label = (i < names.Length) ? names[i] : $"coeff[{i}]";
                            sb.AppendLine($"\t{label}: {vals[i],7:F4}\t{description[label]}");
                        }

                    }
                    sb.AppendLine();
                }
                return sb.ToString();
            }

            // Format the CalibProject stereo payload
            string GetStereoPayload(CalibrationStereoCameraData stereo)
            {
                StringBuilder sb = new();
                sb.AppendLine("=== Stereo Calibration ===");
                sb.AppendLine($"Re-projection RMS: {stereo.RMS:F3}px");

                sb.AppendLine("Rotation Matrix (R):");
                var r = stereo.Rotation;
                if (r is not null)
                {
                    // Descriptions in key value pairs
                    var rotationDescription = new Dictionary<string, string>
                        {
                            { "r11", "How much right-camera X points along left-camera X" },
                            { "r12", "How much right-camera X points along left-camera Y" },
                            { "r13", "How much right-camera X points along left-camera Z" },
                            { "r21", "How much right-camera Y projected on left-camera X" },
                            { "r22", "How much right-camera Y projected on left-camera Y" },
                            { "r23", "How much right-camera Y projected on left-camera Z" },
                            { "r31", "How much right-camera Z projected on left-camera X" },
                            { "r32", "How much right-camera Z projected on left-camera Y" },
                            { "r33", "How much right-camera Z projected on left-camera Z" }
                        };

                    for (int row = 0; row < r.Rows; row++)
                    {
                        for (int col = 0; col < r.Cols; col++)
                        {
                            string label = $"r{row + 1}{col + 1}";
                            sb.AppendLine($"\t{label}: {r[row, col],7:F4}\t{rotationDescription[label]}");
                        }                        
                    }
                    sb.AppendLine("or expressed as yaw-pitch-roll ");
                    double r11 = Convert.ToDouble(r[0, 0]), r12 = Convert.ToDouble(r[0, 1]), r13 = Convert.ToDouble(r[0, 2]);
                    double r21 = Convert.ToDouble(r[1, 0]), r22 = Convert.ToDouble(r[1, 1]), r23 = Convert.ToDouble(r[1, 2]);
                    double r31 = Convert.ToDouble(r[2, 0]), r32 = Convert.ToDouble(r[2, 1]), r33 = Convert.ToDouble(r[2, 2]);

                    double pitch = Math.Asin(-r31);
                    double roll = Math.Atan2(r32, r33);
                    double yaw = Math.Atan2(r21, r11);

                    sb.AppendLine($"\tYaw (Z): {yaw:F4}\tRadians");
                    sb.AppendLine($"\tPitch (Y): {pitch:F4}\tRadians");
                    sb.AppendLine($"\tRoll (X): {roll:F4}\tRadians");
                }
                else
                {
                    sb.AppendLine("\tEmpty!");
                }

                sb.AppendLine("Translation Vector (T):");
                var t = stereo.Translation;
                if (t is not null)
                {
                    // If it's a 3x1 or 1x3 vector, label components Tx, Ty, Tz and compute baseline
                    if ((t.Rows == 3 && t.Cols == 1) || (t.Rows == 1 && t.Cols == 3))
                    {
                        double tx = Convert.ToDouble(t[0, 0]);
                        double ty = Convert.ToDouble(t[1, 0]);
                        double tz = Convert.ToDouble(t[2, 0]);

                        // If row vector, read accordingly
                        if (t.Rows == 1 && t.Cols == 3)
                        {
                            tx = Convert.ToDouble(t[0, 0]);
                            ty = Convert.ToDouble(t[0, 1]);
                            tz = Convert.ToDouble(t[0, 2]);
                        }

                        double baseline = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                        sb.AppendLine($"\tTx: {tx:F4}m\tRight camera X offset from left camera");
                        sb.AppendLine($"\tTy: {ty:F4}m\tRight camera Y offset from left camera");
                        sb.AppendLine($"\tTz: {tz:F4}m\tRight camera Z offset from left camera");
                        sb.AppendLine($"\tBaseline |T|: {baseline:F4}m\tEuclidean separation");
                    }
                    else
                    {
                        for (int row = 0; row < t.Rows; row++)
                            for (int col = 0; col < t.Cols; col++)
                                sb.AppendLine($"\tT[{row},{col}]: {t[row, col]:F4}");
                    }
                }
                else
                {
                    sb.AppendLine("\tEmpty!");
                }
                sb.AppendLine();

                return sb.ToString();
            }

            return payload.ToString();
        }



        /// <summary>
        /// Load from disk and display the code sample for the current selection
        /// </summary>
        /// <returns></returns>
        private async Task LoadSampleForSelectionAsync()
        {
            // Default to copy to clipboard button as not enabled
            CopySampleButton.IsEnabled = false;

            // Disable code samples for text
            if (FormatText.IsChecked == true)
            {
                CodeMarkdown.Text = string.Empty;
                return;
            }

            string? tag = (ExportSamplesNav.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag))
            {
                // Default to first item
                if (ExportSamplesNav.MenuItems.Count > 0)
                {
                    ExportSamplesNav.SelectedItem = ExportSamplesNav.MenuItems[0];
                    tag = (ExportSamplesNav.SelectedItem as NavigationViewItem)?.Tag?.ToString();
                }
            }

            if (string.IsNullOrEmpty(tag))
            {
                CodeMarkdown.Text = string.Empty;
                return;
            }

            string fileName = GetFileNameFor(tag);
            if (string.IsNullOrEmpty(fileName))
            {
                CodeMarkdown.Text = string.Empty;
                return;
            }

            try
            {
                string path = System.IO.Path.Combine("CodeExamples", fileName);
                string codeText = await TryLoadTextAsync(path) ?? $"// Missing sample: {path}";

                if (!string.IsNullOrEmpty(codeText))
                {
                    if (tag == "cpp")
                    {
                        CodeMarkdown.Text = $"{codeText}";
                        CopySampleButton.IsEnabled = true;
                    }
                    else
                    {
                        // Show fenced markdown
                        string fenced = $"```{GetMarkdownLang(tag)}\n{codeText}\n```";                        
                        CodeMarkdown.Text = fenced;
                        CopySampleButton.IsEnabled = true;
                    }
                }
                else
                    CodeMarkdown.Text = "";
            }
            catch
            {
                Debug.WriteLine($"Failed to load code sample for tag '{tag}'");
                CodeMarkdown.Text = "";
            }
        }


        /// <summary>
        /// Find the CalibrationParameters that delivered the best quality
        /// calibration result factoring in the StereoMonoMediaSetMode
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns>null if fails</returns>
        private static CalibrationParameters? GetBestCalibrationRersultConsideringStereoMonoMediaSetMode(CalibProject calibProject)
        {
            // Default to the best result
            CalibrationParameters? calibParams = null;
            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    calibParams = calibProject.ReturnBestStereoCalibrationCameraData();
                    break;
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    calibParams = calibProject.ReturnBestMonoCalibrationCameraData(trueLeftRightFalseNullBoth: null/*best considering left and right*/);
                    break;
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    calibParams = calibProject.ReturnBestMonoCalibrationCameraData(trueLeftRightFalseNullBoth: true);
                    break;
            }

            return calibParams;
        }


        /// <summary>
        /// Returns the current selected distortion model from the radio buttons
        /// </summary>
        /// <returns></returns>
        private CalibrationParameters? GetSelectedModel()
        {
            CalibrationParameters? calibParams = null;

            if (Model_K1K2P1P2.IsChecked == true)
            {
                calibParams = CalibrationParameters.K1K2P1P2;
            }
            else if (Model_K1K2K3P1P2.IsChecked == true)
            {
                calibParams = CalibrationParameters.K1K2K3P1P2;
            }
            else if (Model_K1K2K3K4P1P2.IsChecked == true)
            {
                calibParams = CalibrationParameters.K1K2K3K4P1P2;
            }
            else if (Model_K1K2K3K4P1P2K5K6.IsChecked == true)
            {
                calibParams = CalibrationParameters.K1K2K3K4P1P2K5K6;
            }

            return calibParams;
        }
    }
}
