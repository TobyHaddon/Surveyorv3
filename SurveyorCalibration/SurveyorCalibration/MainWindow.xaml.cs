using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.Calibration;
using Surveyor.Controls;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Core;
using WinRT.Interop;
using WinUIEx;
using static Emgu.CV.Aruco.Dictionary;

namespace Surveyor
{
    public sealed partial class MainWindow : WindowEx
    {
        private string leftMonoMP4Path;
        private string rightMonoMP4Path;
        private string leftStereoMP4Path;
        private string rightStereoMP4Path;

        private Dictionary? dictionary5x5_100;
        private CharucoBoard? board5x5_100;


        public MainWindow()
        {
            // Restore the saved window state
            PersistenceId = "MainWindow";
            MinHeight = 600;
            MinWidth = 800;

            this.InitializeComponent();


            leftMonoMP4Path = @"C:\Users\tobyh\OneDrive\Docs\SVS2\SVS2 Media\125L (CEV22 Pool Left Solo Cailb).MP4";
            rightMonoMP4Path = @"C:\Users\tobyh\OneDrive\Docs\SVS2\SVS2 Media\125R (CEV22 Pool Right Solo Cailb).MP4";
            leftStereoMP4Path = @"C:\Users\tobyh\OneDrive\Docs\SVS2\SVS2 Media\126L (CEV22 Pool Stereo Calib).MP4";
            rightStereoMP4Path = @"C:\Users\tobyh\OneDrive\Docs\SVS2\SVS2 Media\126R (CEV22 Pool Stereo Calib).MP4";

            //LoadVideos(leftMonoMP4Path, rightMonoMP4Path);
                
    
            // Create the dictionary
            dictionary5x5_100 = new Dictionary(PredefinedDictionaryName.Dict5X5_100);

            // Create ChArUco board
            float squareLength = 40.0f / 1000.0f;
            float markerLength = 30.0f / 1000.0f;
            int squaresX = 14;
            int squaresY = 9;
            board5x5_100 = new CharucoBoard(squaresX, squaresY, squareLength, markerLength, dictionary5x5_100);


            //_playLeftTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            //_playLeftTimer.Tick += (s, e) => PlayLeft();

            //_playRightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            //_playRightTimer.Tick += (s, e) => PlayRight();

            //_playBothTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            //_playBothTimer.Tick += (s, e) => PlayBoth();


            // Pass the calibration board settings to the  calibration heads            
            StereoCalibrationHead.SetupCalibrationBoardType(dictionary5x5_100, board5x5_100, "5x5_100");            
            LeftMonoCalibrationHead.SetupCalibrationBoardType(dictionary5x5_100, board5x5_100, "5x5_100");            
            RightMonoCalibrationHead.SetupCalibrationBoardType(dictionary5x5_100, board5x5_100, "5x5_100");

            // Open the media
            StereoCalibrationHead.OpenMedia(leftStereoMP4Path, rightStereoMP4Path);
            LeftMonoCalibrationHead.OpenMedia(leftMonoMP4Path, string.Empty);
            RightMonoCalibrationHead.OpenMedia(rightMonoMP4Path, string.Empty);

            //??? This have break things            
            SetUIControls();
        }



        //private void LoadVideos(string leftPath, string rightPath)
        //{
        //    if (File.Exists(leftPath) && File.Exists(rightPath))
        //    {
        //        _capLeft = new Emgu.CV.VideoCapture(leftPath);
        //        _capRight = new Emgu.CV.VideoCapture(rightPath);

        //        // Emgu uses IsOpened (no parentheses)
        //        if (_capLeft.IsOpened && _capRight.IsOpened)
        //        {
        //            // Get total number of frames
        //            _totalFramesLeft = (int)_capLeft.Get(CapProp.FrameCount);
        //            _totalFramesRight = (int)_capRight.Get(CapProp.FrameCount);

        //            using var testFrame = new Emgu.CV.Mat();
        //            _capLeft.Read(testFrame);

        //            if (!testFrame.IsEmpty)
        //            {
        //                // Create WriteableBitmap with Emgu frame dimensions
        //                _wbLeft = new WriteableBitmap(testFrame.Width, testFrame.Height);
        //                _wbRight = new WriteableBitmap(testFrame.Width, testFrame.Height);

        //                // Reset to first frame — Emgu uses .Set() with CapProp
        //                _capLeft.Set(Emgu.CV.CvEnum.CapProp.PosFrames, 0);
        //                _capRight.Set(Emgu.CV.CvEnum.CapProp.PosFrames, 0);

        //                LeftImage.Source = _wbLeft;
        //                RightImage.Source = _wbRight;

        //                _currentFrameLeft = 0;
        //                _currentFrameRight = 0;
        //            }
        //            else
        //            {
        //                Debug.WriteLine("Failed to read initial frame.");
        //            }
        //        }
        //        else
        //        {
        //            Debug.WriteLine("Failed to open one or both video files.");
        //        }
        //    }

        //    SetUIControls();
        //}



        //private int DetectAndCreateFrameCalibrationTarget(bool trueLeftfalseRight, int frameIndex, Mat frame,
        //                    Dictionary arucoDictionary, CharucoBoard board,
        //                    string boardName)
        //{
        //    int ret = 0;
        //    CalibrationFrameSet calibrationFrameSet;

        //    if (trueLeftfalseRight)
        //    {
        //        calibrationFrameSet = calibrationFrameSetLeft;
        //    }
        //    else
        //    {
        //        calibrationFrameSet = calibrationFrameSetRight;
        //    }

        //    try
        //    {

        //        // Convert to grayscale for detection
        //        using var gray = new Mat();
        //        CvInvoke.CvtColor(frame, gray, ColorConversion.Bgr2Gray);

        //        // Detect ArUco markers
        //        using var markerCorners = new VectorOfVectorOfPointF();
        //        using var markerIds = new VectorOfInt();
        //        var parameters = DetectorParameters.GetDefault();

        //        ArucoInvoke.DetectMarkers(gray, arucoDictionary, markerCorners, markerIds, parameters);


        //        // Interpolate ChArUco corners
        //        using var charucoCorners = new Mat();
        //        using var charucoIds = new Emgu.CV.Util.VectorOfInt();

        //        if (markerIds.Size > 0)
        //        {
        //            ArucoInvoke.InterpolateCornersCharuco(
        //                markerCorners,
        //                markerIds,
        //                gray,
        //                board,
        //                charucoCorners,
        //                charucoIds
        //            );

        //            Debug.WriteLine($"{boardName} Detected {charucoIds.Size} ChArUco corners");
        //            ret = charucoIds.Size;


        //            // Convert detected Charuco corners to managed types
        //            var managedCorners = new PointF[charucoCorners.Rows];
        //            charucoCorners.CopyTo(managedCorners);
        //            var managedIds = charucoIds.ToArray();

        //            // Draw corners on the color frame
        //            if (charucoIds.Size > 0)
        //            {
        //                calibrationFrameSet.AddFrame(frameIndex, gray, managedCorners, managedIds, frame.Width, frame.Height);

        //            }
        //        }
        //        else
        //        {
        //            Debug.WriteLine("No markers detected.");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"DetectAndDrawMarkers: Error processing ChArUco board: {ex.Message}");
        //    }

        //    return ret;
        //}


        /// <summary>
        /// From the metadata storaged in the list for the indicated frame index draw the 
        /// markers to the frame Mat and update the screen 
        /// </summary>
        /// <param name="trueLeftfalseRight"></param>
        /// <param name="frameIndex"></param>
        /// <param name="frame"></param>
        /// <returns></returns>
        //private int DrawMarkersToMat(bool trueLeftfalseRight, int frameIndex, Mat frame)
        //{
        //    int ret = 0;

        //    CalibrationFrameSetViewer viewer;
        //    CalibrationFrameSet calibrationFrameSet;
        //    TextBlock MovementFactor;
        //    TextBlock BlurFactor;
        //    TextBlock FeatureCount;
        //    TextBlock Score;

        //    if (trueLeftfalseRight)
        //    {
        //        viewer = CalibrationFrameSetViewerLeft;
        //        calibrationFrameSet = calibrationFrameSetLeft;
        //        MovementFactor = LeftMovementFactor;
        //        BlurFactor = LeftBlurFactor;
        //        FeatureCount = LeftFeatureCount;
        //        Score = LeftScore;
        //    }
        //    else
        //    {
        //        viewer = CalibrationFrameSetViewerRight;
        //        calibrationFrameSet = calibrationFrameSetRight;
        //        MovementFactor = RightMovementFactor;
        //        BlurFactor = RightBlurFactor;
        //        FeatureCount = RightFeatureCount;
        //        Score = RightScore;
        //    }

        //    try
        //    {
        //        // Get the frameCalibrationTarget metadata for the frame index
        //        FrameCalibrationTarget frameCalibrationTarget = calibrationFrameSet.Frames[frameIndex];

        //        // Update from Bin Layers and the graphs 
        //        // Note these are fully recreated from the full list to date
        //        viewer.RefreshBinLayers();
        //        viewer.DrawGraphs();

        //        // Create a VectorOfPointF and populate it from the managed array
        //        var charucoCorners = new VectorOfPointF();
        //        charucoCorners.Push(frameCalibrationTarget.CharucoCorners);

        //        // managedIds is int[]
        //        var charucoIds = new VectorOfInt();
        //        charucoIds.Push(frameCalibrationTarget.CharucoIds); 


        //        Emgu.CV.Aruco.ArucoInvoke.DrawDetectedCornersCharuco(
        //            frame,
        //            charucoCorners,
        //            charucoIds,
        //            new MCvScalar(0, 255, 0)
        //        );

        //        // Draw the centre point
        //        PointF boardCentre = calibrationFrameSet.Frames[frameIndex].Center;
        //        int radius = 40;
        //        MCvScalar color = new(0, 255, 0); // Green (B, G, R)
        //        int thickness = 20;

        //        // Draw the circle on the Mat
        //        CvInvoke.Circle(frame, new Point((int)boardCentre.X, (int)boardCentre.Y), radius, color, thickness);

        //        // Display movement and blur factor
        //        double movementFactor = calibrationFrameSet.Frames[frameIndex].MovementFactor;
        //        double movementFromPrevious = -1;
        //        double movementToNext = -1;
        //        if (movementFactor == -1)
        //        {
        //            if (calibrationFrameSet.Frames[frameIndex].MovementFromPrevious != -1)
        //            {
        //                movementFromPrevious = calibrationFrameSet.Frames[frameIndex].MovementFromPrevious;
        //            }
        //            else if (calibrationFrameSet.Frames[frameIndex].MovementToNext != -1)
        //            {
        //                movementToNext = calibrationFrameSet.Frames[frameIndex].MovementToNext;
        //            }
        //        }

        //        double blurFactor = calibrationFrameSet.Frames[frameIndex].BlurFactor;

        //        UpdateFrameMetaData(trueLeftfalseRight,
        //            movementFactor, movementFromPrevious, movementToNext,
        //            blurFactor,
        //            charucoCorners.Size,
        //            calibrationFrameSet.Frames[frameIndex].Score);
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"DrawMarkersToMat: Error processing ChArUco board: {ex.Message}");
        //    }

        //    return ret;
        //}


        /// <summary>
        /// Update the left or right frame metadata
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        /// <param name="movementFactor"></param>
        /// <param name="blurFactor"></param>
        /// <param name="featureCount"></param>
        /// <param name="score"></param>
        //private void UpdateFrameMetaData(bool trueLeftfalseRight, double movementFactor, double movementFromPrevious, double movementToNext, double blurFactor, int featureCount, double score)
        //{
        //    TextBlock MovementFactor;
        //    TextBlock BlurFactor;
        //    TextBlock FeatureCount;
        //    TextBlock Score;

        //    if (trueLeftfalseRight)
        //    {
        //        MovementFactor = LeftMovementFactor;
        //        BlurFactor = LeftBlurFactor;
        //        FeatureCount = LeftFeatureCount;
        //        Score = LeftScore;
        //    }
        //    else
        //    {
        //        MovementFactor = RightMovementFactor;
        //        BlurFactor = RightBlurFactor;
        //        FeatureCount = RightFeatureCount;
        //        Score = RightScore;
        //    }

        //    // Display movement and blur factor
        //    if (movementFactor != -1)
        //    {
        //        MovementFactor.Text = $"Move: {movementFactor:F1}";
        //    }
        //    else if (movementFromPrevious != -1)
        //    {
        //        MovementFactor.Text = $"Move: \u2190{movementFromPrevious:F1}";
        //    }
        //    else if (movementToNext != -1)
        //    {
        //        MovementFactor.Text = $"Move: {movementToNext:F1}\u21D2";
        //    }


        //    BlurFactor.Text = $"Blur: {blurFactor:F1}";

        //    // Feature Count (number of Charuco corners)
        //    FeatureCount.Text = $"Corners: {featureCount}";

        //    // Score
        //    Score.Text = $"Score: {score:F2}";

        //}

        /// <summary>
        /// Clear the frame metadata on screen fields
        /// </summary>
        //private void ClearFrameMetaData(bool trueLeftfalseRight)
        //{
        //    if (trueLeftfalseRight)
        //    {
        //        LeftMovementFactor.Text = string.Empty;
        //        LeftBlurFactor.Text = string.Empty;
        //        LeftFeatureCount.Text = string.Empty;
        //        LeftScore.Text = string.Empty;
        //    }
        //    else
        //    {
        //        RightMovementFactor.Text = string.Empty;
        //        RightBlurFactor.Text = string.Empty;
        //        RightFeatureCount.Text = string.Empty;
        //        RightScore.Text = string.Empty;
        //    }
        //}


        /// <summary>
        /// Draw an Emgu Mat into a WriteableBitmap (which is the Source for an Image element)
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="wb"></param>
        //private void DrawFrameToScreen(Mat frame, WriteableBitmap wb)
        //{
        //    if (frame.IsEmpty || wb == null) return;

        //    try
        //    {
        //        using var bgraFrame = new Mat();
        //        CvInvoke.CvtColor(frame, bgraFrame, Emgu.CV.CvEnum.ColorConversion.Bgr2Bgra);

        //        if (wb.PixelWidth != bgraFrame.Width || wb.PixelHeight != bgraFrame.Height)
        //        {
        //            Debug.WriteLine($"Warning: Frame dimensions {bgraFrame.Width}x{bgraFrame.Height} " +
        //                            $"don't match WriteableBitmap {wb.PixelWidth}x{wb.PixelHeight}");
        //            return;
        //        }

        //        int byteCount = bgraFrame.Rows * bgraFrame.Cols * bgraFrame.ElementSize;
        //        byte[] buffer = new byte[byteCount];

        //        // Copy from native memory to managed buffer
        //        Marshal.Copy(bgraFrame.DataPointer, buffer, 0, buffer.Length);

        //        using var stream = wb.PixelBuffer.AsStream();
        //        stream.Seek(0, SeekOrigin.Begin);
        //        stream.Write(buffer, 0, buffer.Length);
        //        wb.Invalidate();
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"DrawFrame: Error drawing frame: {ex.Message}");
        //    }
        //}



        /// 
        /// EVENTS
        /// 


        /// <summary>
        /// Left side Frame Back button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private void LeftFrameBackClick(object sender, RoutedEventArgs e)
        //{
        //    if (appMode == AppMode.Open)
        //        FrameMoveBack(true/*leftTrueRightFalse*/);
        //    else if (appMode == AppMode.BestFramesView)
        //        BestFrameMoveBack(true/*leftTrueRightFalse*/);
        //}

        /// <summary>
        /// Left side Play button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private void LeftPlayPauseClick(object sender, RoutedEventArgs e)
        //{
        //    if (appMode == AppMode.Open)
        //        PlayPauseClick(true);
        //}

        /// <summary>
        /// Left side Frame Forward button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        //private void LeftFrameForwardClick(object sender, RoutedEventArgs e)
        //{
        //    if (appMode == AppMode.Open)
        //        FrameMoveForward(true/*leftTrueRightFalse*/);
        //    else if (appMode == AppMode.BestFramesView)
        //        BestFrameMoveForward(true/*leftTrueRightFalse*/);
        //}

        /// <summary>
        /// Right side Frame Backbutton pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        //private void RightFrameBackClick(object sender, RoutedEventArgs e)
        //{
        //    if (appMode == AppMode.Open)
        //        FrameMoveBack(false/*leftTrueRightFalse*/);
        //    else if (appMode == AppMode.BestFramesView)
        //        BestFrameMoveBack(false/*leftTrueRightFalse*/);
        //}

        /// <summary>
        /// Right side Play button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private void RightPlayPauseClick(object sender, RoutedEventArgs e)
        //{
        //    if (appMode == AppMode.Open)
        //        PlayPauseClick(false);
        //}

        /// <summary>
        /// Right side Frame Forward button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        //private void RightFrameForwardClick(object sender, RoutedEventArgs e)
        //{
        //    if (appMode == AppMode.Open)
        //        FrameMoveForward(false/*leftTrueRightFalse*/);
        //    else if (appMode == AppMode.BestFramesView)
        //        BestFrameMoveForward(false/*leftTrueRightFalse*/);
        //}

        /// <summary>
        /// Media Lock/Unlock button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private void LockUnlockClick(object sender, RoutedEventArgs e)
        //{
        //    isLocked = !isLocked;
        //    LockUnlockIcon.Glyph = isLocked ? "\uE72E" : "\uE785";
        //    if (isLocked) lockOffset = _currentFrameRight - _currentFrameLeft;
        //}

        //private void LeftFrameInfoTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        //{
        //    if (e.Key == Windows.System.VirtualKey.Enter)
        //    {
        //        if (int.TryParse(LeftFrameInfoTextBox.Text, out int targetIndex))
        //        {
        //            FrameJump(true/*left*/, targetIndex);
        //        }
        //    }
        //}

        //private void RightFrameInfoTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        //{
        //    if (e.Key == Windows.System.VirtualKey.Enter)
        //    {
        //        if (int.TryParse(RightFrameInfoTextBox.Text, out int targetIndex))
        //        {
        //            FrameJump(false/*right*/, targetIndex);
        //        }
        //    }
        //}


        /// <summary>
        /// Load a calibration frame set file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private async void OpenButton_Click(object sender, RoutedEventArgs e)
        //{
        //    bool leftLoaded  = false;
        //    bool rightLoaded = false;
        //    string messageText = string.Empty;

        //    // Check if you will be overwriting data that is already loaded
        //    // i.e. maybe the user pressed the wrong button
        //    bool load = false;
        //    if (calibrationFrameSetLeft.Frames.Count + calibrationFrameSetRight.Frames.Count > 0)
        //    {
        //        messageText = $"There is existing data already loaded. Are you sure you want to continue?\n\n";
        //        if (calibrationFrameSetLeft.Frames.Count > 0 && calibrationFrameSetRight.Frames.Count > 0)
        //        {
        //            messageText += $"Left side has {calibrationFrameSetLeft.Frames.Count} item(s) and the right side {calibrationFrameSetRight.Frames.Count} item(s)";
        //        }
        //        else if (calibrationFrameSetLeft.Frames.Count > 0)
        //        {
        //            messageText += $"Left side has {calibrationFrameSetLeft.Frames.Count} item(s)";
        //        }
        //        else
        //        {
        //            messageText += $"Right side has {calibrationFrameSetRight.Frames.Count} item(s)";
        //        }

        //        // Check with the user
        //        var dialog = new ContentDialog
        //        {
        //            Title = "Calibration Frame Set Open",
        //            Content = messageText,
        //            PrimaryButtonText = "Ok",
        //            CloseButtonText = "Cancel",
        //            XamlRoot = this.Content.XamlRoot  // 'this' is the MainWindow
        //        };
        //        // Show the dialog
        //        var result = await dialog.ShowAsync();

        //        if (result == ContentDialogResult.Primary)
        //            load = true;

        //        messageText = string.Empty;
        //    }
        //    else
        //        // Nothing yet loaded so ok to load from file
        //        load = true;

        //    if (load)
        //    {
        //        // Load and display the left calibration frame set file
        //        leftLoaded = LoadAndDisplayCalibrationFrameSetFile(leftMonoMP4Path, calibrationFrameSetLeft, CalibrationFrameSetViewerLeft);

        //        // Load and display the right calibration frame set file
        //        rightLoaded = LoadAndDisplayCalibrationFrameSetFile(rightMonoMP4Path, calibrationFrameSetRight, CalibrationFrameSetViewerRight);


        //        if (leftLoaded && rightLoaded)
        //        {
        //            messageText = $"Both the left and right Calibration Frame Set loaded ok";
        //        }

        //        ReportOnLargeValues();

        //        if (!string.IsNullOrEmpty(messageText))
        //        {
        //            var dialog = new ContentDialog
        //            {
        //                Title = "Calibration Frame Set Open",
        //                Content = messageText,
        //                CloseButtonText = "Ok",
        //                XamlRoot = this.Content.XamlRoot  // 'this' is the MainWindow
        //            };
        //            // Show the dialog
        //            var result = dialog.ShowAsync();
        //        }
        //    }

        //    SetUIControls();
        //}




        /// <summary>
        /// Save a calibration frame set file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private void SaveButton_Click(object sender, RoutedEventArgs e)
        //{
        //    LeftImageViewer.Data = string.Empty;
        //    RightImageViewer.Data = string.Empty;

        //    // Make the left calibration frame set path
        //    string leftCalibrationFrameSetPath = MakeCalibrationFrameSetPath(leftMonoMP4Path);

        //    bool leftSaved = calibrationFrameSetLeft.SaveToFile(leftCalibrationFrameSetPath);

        //    // Make the right calibration frame set path
        //    string rightCalibrationFrameSetPath = MakeCalibrationFrameSetPath(rightMonoMP4Path);

        //    bool rightSaved = calibrationFrameSetRight.SaveToFile(rightCalibrationFrameSetPath);

        //    string messageText = string.Empty;
        //    if (leftSaved && rightSaved)
        //    {
        //        messageText = $"Both the left and right Calibration Frame Set saved ok";
        //    }
        //    else if (leftSaved)
        //    {
        //        messageText = $"Left Calibration Frame Set saved ok, but the right failed";
        //    }
        //    else if (rightSaved)
        //    {
        //        messageText = $"Right Calibration Frame Set saved ok, but the left failed";
        //    }
        //    else
        //    {
        //        messageText = $"Failed to save both the left and right Calibration Frame Set";
        //    }

        //    var dialog = new ContentDialog
        //    {
        //        Title = "Calibration Frame Set Save",
        //        Content = messageText,
        //        CloseButtonText = "Ok",
        //        XamlRoot = this.Content.XamlRoot  // 'this' is the MainWindow
        //    };
        //    // Show the dialog
        //    var result = dialog.ShowAsync();

        //    SetUIControls();
        //}

        //private async void BestFrames_Click(object sender, RoutedEventArgs e)
        //{
        //    appMode = AppMode.BestFramesCalc;
        //    SetUIControls();

        //    StorageFolder? imageOutputFolder = await PickOutputFolderAsync(this);

        //    if (imageOutputFolder is not null)
        //    {
        //        if (calibrationFrameSetLeft is not null && _wbLeft is not null)
        //        {
        //            // Create a list of the best calibation frames from the left side
        //            calibrationFrameSetLeft.SelectBestFrames();

        //            if (await SaveBestFiles(true/*trueLeftFalseRight*/, imageOutputFolder, calibrationFrameSetLeft, _wbLeft, leftMonoMP4Path))
        //            {
        //                StorageFolder? imageOutputSubFolder = await MakeAndCreateFramesDirectory((StorageFolder)imageOutputFolder, leftMonoMP4Path, false/*trueRelativePathFalseAbsolute*/);
        //                if (imageOutputSubFolder is not null)
        //                {
        //                    string searchPath = Path.Combine(imageOutputSubFolder.Path, "*.png");
        //                    LeftImageViewer.Data = searchPath;
        //                }
        //            }
        //        }

        //        if (calibrationFrameSetRight is not null && _wbRight is not null)
        //        {
        //            // Create a list of the best calibation frames from the right side
        //            calibrationFrameSetRight.SelectBestFrames();

        //            if (await SaveBestFiles(false/*trueLeftFalseRight*/, imageOutputFolder, calibrationFrameSetRight, _wbRight, rightMonoMP4Path))
        //            {
        //                StorageFolder? imageOutputSubFolder = await MakeAndCreateFramesDirectory((StorageFolder)imageOutputFolder, rightMonoMP4Path, false/*trueRelativePathFalseAbsolute*/);
        //                if (imageOutputSubFolder is not null)
        //                {
        //                    string searchPath = Path.Combine(imageOutputSubFolder.Path, "*.png");
        //                    RightImageViewer.Data = searchPath;
        //                }
        //            }
        //        }
        //    }

        //    appMode = AppMode.BestFramesView;
        //    BestFrameJump(true/*leftTrueRightFalse*/, 0);
        //    BestFrameJump(false/*leftTrueRightFalse*/, 0);
        //    LeftUpdateFrameLabel();
        //    RightUpdateFrameLabel();
        //    SetUIControls();
        //}


        /// <summary>
        /// Make the folder name to save the frames to, create the folder if necessary)
        /// </summary>
        /// <param name="fileSpecMP4"></param>
        /// <returns></returns>
        //private async Task<StorageFolder?> MakeAndCreateFramesDirectory(StorageFolder imageOutputFolder, string fileSpecMP4, bool trueRelativePathFalseAbsolute)
        //{
        //    StorageFolder? outputFolder = null;

        //    // Make an output folder in the local folder (if necessary) based on the video name 
        //    string subfolderName = Path.GetFileNameWithoutExtension(fileSpecMP4);

        //    // Create a folder
        //    try
        //    {
        //        outputFolder = await imageOutputFolder.CreateFolderAsync(subfolderName,
        //                                    CreationCollisionOption.OpenIfExists); // Ensures it won't throw if already exists
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"MakeAndCreateFramesDirectory: Error creating save frame storage folder call: [{subfolderName}] inside: [{imageOutputFolder.Path}], {ex.Message}");
        //    }

        //    return outputFolder;
        //}


        /// <summary>
        /// Save the best frames to a folder
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="bestFrames"></param>
        /// <param name="calibrationFrameSet"></param>
        /// <param name="wb"></param>
        /// <param name="fileSpecMP4"></param>
        /// <returns></returns>
        //private async Task<bool> SaveBestFiles(bool trueLeftFalseRight, StorageFolder imageOutputFolder, CalibrationFrameSet calibrationFrameSet, WriteableBitmap wb, string fileSpecMP4)
        //{
        //    bool ret = false;

        //    // Create a folder (need the full path for this)
        //    StorageFolder? imageOutputSubFolder = await MakeAndCreateFramesDirectory(imageOutputFolder, fileSpecMP4, false/*trueRelativePathFalseAbsolute*/);

        //    if (imageOutputSubFolder is not null)
        //    {
        //        // Ensure those folder are empty
        //        var files = await imageOutputSubFolder.GetFilesAsync();
        //        foreach (StorageFile file in files)
        //        {
        //            await file.DeleteAsync();
        //        }

        //        // Loop through the best frames and save them (need the relative path for this)
        //        foreach (int frameIndex in calibrationFrameSet.BestFrames)
        //        {
        //            // Make image file name
        //            string videoName = Path.GetFileNameWithoutExtension(fileSpecMP4);
        //            string frameFileName = $"{videoName}_{frameIndex}.png";

        //            try
        //            {
        //                // Force the frame with MoveJump
        //                _JumpFrame(trueLeftFalseRight, frameIndex);

        //                await Task.Delay(100);

        //                // Save the image                        
        //                StorageFile file = await imageOutputSubFolder.CreateFileAsync(frameFileName,
        //                                                CreationCollisionOption.ReplaceExisting);

        //                await SaveWriteableBitmapToFile(wb, file);

        //                Debug.WriteLine($"SaveBestFiles: Frame saved: [{file.Path}]");
        //                ret = true;
        //            }
        //            catch (Exception ex)
        //            {
        //                Debug.WriteLine($"SaveBestFiles: Error saving frame {frameIndex} to path:[{imageOutputSubFolder.Path}] as:[{frameFileName}]: {ex.Message}");
        //            }
        //        }            
        //    }

        //    return ret;
        //}



        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Write the WriteableBitmap to file
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="file"></param>
        /// <returns></returns>
        //public static async Task SaveWriteableBitmapToFile(WriteableBitmap bitmap, StorageFile file)
        //{
        //    // Get the pixel buffer from the WriteableBitmap
        //    using (var stream = new InMemoryRandomAccessStream())
        //    {
        //        // Encode the WriteableBitmap to a stream
        //        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        //        var pixelStream = bitmap.PixelBuffer.AsStream();
        //        var pixels = new byte[pixelStream.Length];
        //        await pixelStream.ReadAsync(pixels, 0, pixels.Length);

        //        // Set the pixel data to the encoder
        //        encoder.SetPixelData(
        //            BitmapPixelFormat.Bgra8,
        //            BitmapAlphaMode.Premultiplied,
        //            (uint)bitmap.PixelWidth,
        //            (uint)bitmap.PixelHeight,
        //            96.0,  // Default DPI for WinUI
        //            96.0,
        //            pixels);

        //        await encoder.FlushAsync();

        //        // Save the stream to a file
        //        using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
        //        {
        //            await RandomAccessStream.CopyAndCloseAsync(stream.GetInputStreamAt(0), fileStream.GetOutputStreamAt(0));
        //        }
        //    }
        //}


        /// <summary>
        ///  Load a Calibration Frame Set from file and display to the screen
        /// </summary>
        /// <param name="MP4Path"></param>
        /// <param name="calibrationFrameSet"></param>
        /// <param name="calibrationFrameSetViewer"></param>
        //private static bool LoadAndDisplayCalibrationFrameSetFile(string MP4Path, CalibrationFrameSet calibrationFrameSet, CalibrationFrameSetViewer calibrationFrameSetViewer)
        //{
        //    bool ret = false;
        //    string messageText;

        //    // Make the left calibration frame set path
        //    string leftCalibrationFrameSetPath = MakeCalibrationFrameSetPath(MP4Path);

        //    // Check if the file exists (remove any zero byte file)
        //    DeleteIfZeroByteFile(leftCalibrationFrameSetPath);

        //    if (File.Exists(leftCalibrationFrameSetPath))
        //    {
        //        // Load the calibration frame set
        //        var json = CalibrationFrameSet.LoadFromFile(leftCalibrationFrameSetPath);
        //        if (json is not null)
        //        {
        //            calibrationFrameSet = json;

        //            CalibrationFrameSetViewerData data = new(calibrationFrameSet);
        //            calibrationFrameSetViewer.Data = data;
        //            calibrationFrameSetViewer.RefreshBinLayers();
        //            calibrationFrameSetViewer.DrawGraphs();
        //            ret = true;
        //        }
        //        else
        //        {
        //            messageText = $"Failed to load left: {leftCalibrationFrameSetPath}";
        //            Debug.WriteLine(messageText);

        //        }
        //    }
        //    else
        //    {
        //        messageText = $"File not found left: {leftCalibrationFrameSetPath}";
        //        Debug.WriteLine(messageText);
        //    }

        //    return ret;
        //}


        /// <summary>
        /// 
        /// </summary>
        //private void LeftUpdateFrameLabel()
        //{
        //    int targetIndex = -1;
        //    int totalFrames = -1;
        //    if (appMode == AppMode.Open)
        //    {
        //        targetIndex = _currentFrameLeft;
        //        totalFrames = _totalFramesLeft;
        //    }
        //    else if (appMode == AppMode.BestFramesView)
        //    {
        //        targetIndex = _currentBestFrameLeft;
        //        totalFrames = calibrationFrameSetLeft.BestFrames.Count;
        //    }

        //    _UpdateFrameLabel(LeftFrameInfoLabel, _capLeft, targetIndex, totalFrames);
        //    LeftFrameInfoTextBox.Text = $"{_currentFrameLeft}";
        //}
        //private void RightUpdateFrameLabel()
        //{
        //    int targetIndex = -1;
        //    int totalFrames = -1;
        //    if (appMode == AppMode.Open)
        //    {
        //        targetIndex = _currentFrameRight;
        //        totalFrames = _totalFramesLeft;
        //    }
        //    else if (appMode == AppMode.BestFramesView)
        //    {
        //        targetIndex = _currentBestFrameRight;
        //        totalFrames = calibrationFrameSetRight.BestFrames.Count;
        //    }

        //    _UpdateFrameLabel(RightFrameInfoLabel, _capRight, targetIndex, totalFrames);
        //    RightFrameInfoTextBox.Text = $"{_currentFrameRight}";
        //}
        //private void _UpdateFrameLabel(TextBlock textBlock, VideoCapture? cap, int currentFrame, int totalFrames)
        //{
        //    if (cap is not null)
        //    {
        //        if (appMode == AppMode.Open || appMode == AppMode.BestFramesView)
        //        {
        //            string frameText = string.Empty;

        //            if (totalFrames == -1 || totalFrames == 0)
        //            {
        //                double time = cap.Get(CapProp.PosMsec) / 1000.0;
        //                frameText = $"Frame {currentFrame}, Time {time:F2}s";
        //            }
        //            else
        //            {
        //                double time = cap.Get(CapProp.PosMsec) / 1000.0;
        //                frameText = $"Frame {currentFrame} / {totalFrames}, Time {time:F2}s";
        //            }

        //            textBlock.Text = frameText;
        //        }
        //        else
        //            textBlock.Text = string.Empty;
        //    }
        //}


        //private void FrameMoveBack(bool leftTrueRightFalse)
        //{
        //    if (isLocked && _capLeft != null && _wbLeft != null && _capRight != null && _wbRight != null)
        //    {
        //        _BackFrame(true/*leftTrueRightFalse*/);
        //        _BackFrame(false/*leftTrueRightFalse*/);
        //        LeftUpdateFrameLabel();
        //        RightUpdateFrameLabel();
        //    }
        //    else if (leftTrueRightFalse && _capLeft != null && _wbLeft != null)
        //    {
        //        _BackFrame(true/*leftTrueRightFalse*/);
        //        LeftUpdateFrameLabel();
        //    }
        //    else if (!leftTrueRightFalse && _capRight != null && _wbRight != null)
        //    {
        //        _BackFrame(false/*leftTrueRightFalse*/);
        //        RightUpdateFrameLabel();
        //    }
        //}

        //private void FrameMoveForward(bool leftTrueRightFalse)
        //{
        //    if (isLocked && _capLeft != null && _wbLeft != null && _capRight != null && _wbRight != null)
        //    {
        //        _ForwardFrame(true/*leftTrueRightFalse*/);
        //        _ForwardFrame(false/*leftTrueRightFalse*/);
        //        LeftUpdateFrameLabel();
        //        RightUpdateFrameLabel();
        //    }
        //    else if (leftTrueRightFalse && _capLeft != null && _wbLeft != null)
        //    {
        //        _ForwardFrame(true/*leftTrueRightFalse*/);
        //        LeftUpdateFrameLabel();
        //    }
        //    else if (!leftTrueRightFalse && _capRight != null && _wbRight != null)
        //    {
        //        _ForwardFrame(false/*leftTrueRightFalse*/);
        //        RightUpdateFrameLabel();
        //    }
        //}

        //private void PlayPauseClick(bool leftTrueRightFalse)
        //{
        //    if (isLocked)
        //    {
        //        _isBothPlaying = !_isBothPlaying;
        //        if (_isBothPlaying)
        //        {
        //            _playBothTimer.Start();
        //            LeftPlayPauseIcon.Glyph = "\uF8AE";
        //            RightPlayPauseIcon.Glyph = "\uF8AE";
        //        }
        //        else
        //        {
        //            _playBothTimer.Stop();
        //            LeftPlayPauseIcon.Glyph = "\uF5B0";
        //            RightPlayPauseIcon.Glyph = "\uF5B0";
        //        }
        //    }
        //    else if (leftTrueRightFalse)
        //    {
        //        _isLeftPlaying = !_isLeftPlaying;
        //        if (_isLeftPlaying)
        //        {
        //            _playLeftTimer.Start();
        //            LeftPlayPauseIcon.Glyph = "\uF8AE";
        //        }
        //        else
        //        {
        //            _playLeftTimer.Stop();
        //            LeftPlayPauseIcon.Glyph = "\uF5B0";
        //        }
        //    }
        //    else
        //    {
        //        _isRightPlaying = !_isRightPlaying;
        //        if (_isRightPlaying)
        //        {
        //            _playRightTimer.Start();
        //            RightPlayPauseIcon.Glyph = "\uF8AE";
        //        }
        //        else
        //        {
        //            _playRightTimer.Stop();
        //            RightPlayPauseIcon.Glyph = "\uF5B0";
        //        }
        //    }
        //}

        //private void FrameJump(bool leftTrueRightFalse, int targetIndex)
        //{
        //    if (isLocked && _capLeft != null && _wbLeft != null && _capRight != null && _wbRight != null)
        //    {
        //        _JumpFrame(true/*leftTrueRightFalse*/, targetIndex);
        //        _JumpFrame(false/*leftTrueRightFalse*/, targetIndex + lockOffset);
        //        LeftUpdateFrameLabel();
        //        RightUpdateFrameLabel();
        //    }
        //    else if (leftTrueRightFalse && _capLeft != null && _wbLeft != null)
        //    {
        //        _JumpFrame(true/*leftTrueRightFalse*/, targetIndex);
        //        LeftUpdateFrameLabel();
        //    }
        //    else if (!leftTrueRightFalse && _capRight != null && _wbRight != null)
        //    {
        //        _JumpFrame(false/*leftTrueRightFalse*/, targetIndex);
        //        RightUpdateFrameLabel();
        //    }
        //}



        //private void PlayLeft()
        //{
        //    if (_capLeft != null && _wbLeft != null)
        //    {
        //        _ForwardFrame(true/*leftTrueRightFalse*/);
        //        LeftUpdateFrameLabel();
        //    }
        //}
        //private void PlayRight()
        //{
        //    if (_capRight != null && _wbRight != null)
        //    {
        //        _ForwardFrame(false/*leftTrueRightFalse*/);
        //        RightUpdateFrameLabel();
        //    }
        //}
        //private void PlayBoth()
        //{
        //    if (_capLeft != null && _wbLeft != null && _capRight != null && _wbRight != null)
        //    {
        //        _ForwardFrame(true/*leftTrueRightFalse*/);
        //        _ForwardFrame(false/*leftTrueRightFalse*/);
        //        LeftUpdateFrameLabel();
        //        RightUpdateFrameLabel();
        //    }
        //}


        //private void BestFrameMoveBack(bool leftTrueRightFalse)
        //{
        //    int targetIndex;

        //    CalibrationFrameSet calibrationFrameSet;
        //    if (leftTrueRightFalse)
        //    {
        //        calibrationFrameSet = calibrationFrameSetLeft;
        //        targetIndex = _currentBestFrameLeft - 1;
        //    }
        //    else
        //    {
        //        calibrationFrameSet = calibrationFrameSetRight;
        //        targetIndex = _currentBestFrameRight - 1;
        //    }

        //    // BestFrameJump does the out of bounds check
        //    BestFrameJump(leftTrueRightFalse, targetIndex);
        //}


        //private void BestFrameMoveForward(bool leftTrueRightFalse)
        //{
        //    int targetIndex;

        //    CalibrationFrameSet calibrationFrameSet;

        //    if (leftTrueRightFalse)
        //    {
        //        calibrationFrameSet = calibrationFrameSetLeft;
        //        targetIndex = _currentBestFrameLeft + 1;
        //    }
        //    else
        //    {
        //        calibrationFrameSet = calibrationFrameSetRight;
        //        targetIndex = _currentBestFrameRight + 1;
        //    }

        //    // BestFrameJump does the out of bounds check
        //    BestFrameJump(leftTrueRightFalse, targetIndex);
        //}

        //private void BestFrameJump(bool leftTrueRightFalse, int targetIndex)
        //{
        //    bool ok = false;
        //    CalibrationFrameSet? calibrationFrameSet = null;

        //    if (leftTrueRightFalse)
        //    {
        //        calibrationFrameSet = calibrationFrameSetLeft;
        //    }
        //    else 
        //    {
        //        calibrationFrameSet = calibrationFrameSetRight;
        //    }

        //    // 
        //    if (calibrationFrameSet is not null)
        //    {
        //        if (targetIndex < 0)
        //            targetIndex = 0;
        //        if (targetIndex >= calibrationFrameSet.Frames.Count)
        //            targetIndex = calibrationFrameSet.Frames.Count;

        //        try
        //        {
        //            int frameIndex = calibrationFrameSet.BestFrames[targetIndex];

        //            FrameCalibrationTarget frameCalibrationTarget = calibrationFrameSet.Frames[frameIndex];
        //            _JumpFrame(leftTrueRightFalse, frameCalibrationTarget.FrameIndex);
        //            ok = true;
        //        }
        //        catch (Exception ex)
        //        {
        //            string side = leftTrueRightFalse ? "left" : "right";
        //            Debug.WriteLine($"Failed to berst frames index:{targetIndex} for the {side} side, {ex.Message}");
        //        }
        //    }

        //    if (ok)
        //    {
        //        if (leftTrueRightFalse)
        //        {
        //            _currentBestFrameLeft = targetIndex;
        //            LeftUpdateFrameLabel();
        //        }
        //        else
        //        {
        //            _currentBestFrameRight = targetIndex;
        //            RightUpdateFrameLabel();
        //        }
        //    }
        //}



        /// <summary>
        /// Get the path to save the calibration frame set file
        /// </summary>
        /// <param name="originalPath"></param>
        /// <returns></returns>
        //public static string MakeCalibrationFrameSetPath(string originalPath)
        //{
        //    // Extract the filename without extension
        //    string baseName = Path.GetFileNameWithoutExtension(originalPath);

        //    // Build new filename
        //    string filename = $"{baseName}-CalibrationFrameSet.json";

        //    // Get local folder path
        //    StorageFolder localFolder = ApplicationData.Current.LocalFolder;

        //    // Combine into full path
        //    string fullPath = Path.Combine(localFolder.Path, filename);

        //    return fullPath;
        //}



        //public static void DeleteIfZeroByteFile(string filePath)
        //{
        //    try
        //    {
        //        if (File.Exists(filePath))
        //        {
        //            var fileInfo = new FileInfo(filePath);
        //            if (fileInfo.Length == 0)
        //            {
        //                File.Delete(filePath);
        //                Debug.WriteLine($"Deleted zero-byte file: {filePath}");
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"Error checking/deleting file: {ex.Message}");
        //    }
        //}


        //public void ReportOnLargeValues()
        //{
        //    // Check for large values in the left calibration frame set
        //    if (calibrationFrameSetLeft.Frames.Count > 0)
        //    {
        //        calibrationFrameSetLeft.ReportOnLargeValues(true/*left*/, true/*supress value*/);
        //    }
        //    // Check for large values in the right calibration frame set
        //    if (calibrationFrameSetRight.Frames.Count > 0)
        //    {
        //        calibrationFrameSetRight.ReportOnLargeValues(false/*right*/, true/*supress value*/);
        //    }
        //}

        //private void _ForwardFrame(bool leftTrueRightFalse)
        //{
        //    VideoCapture? cap;
        //    WriteableBitmap? wb;
        //    int frameIndex;

        //    if (leftTrueRightFalse)
        //    {
        //        cap = _capLeft;
        //        wb = _wbLeft;
        //        frameIndex = Math.Max(0, _currentFrameLeft + 1);
        //    }
        //    else
        //    {
        //        cap = _capRight;
        //        wb = _wbRight;
        //        frameIndex = Math.Max(0, _currentFrameRight + 1);
        //    }

        //    if (cap is not null && wb is not null)
        //    {
        //        if (leftTrueRightFalse)
        //        {
        //            // Check for end of media
        //            if (_currentFrameLeft >= _totalFramesLeft)
        //            {
        //                PlayPauseClick(leftTrueRightFalse);
        //                return;
        //            }
        //        }
        //        else
        //        {
        //            // Check for end of media
        //            if (_currentFrameRight >= _totalFramesRight)
        //            { 
        //                PlayPauseClick(leftTrueRightFalse);
        //                return;
        //            }
        //        }

        //        using var mat = new Mat();

        //        if (cap!.Read(mat) && !mat.IsEmpty)
        //        {
        //            ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb);
        //        }

        //        if (leftTrueRightFalse)
        //        {
        //            _currentFrameLeft = frameIndex;
        //        }
        //        else
        //        {
        //            _currentFrameRight = frameIndex;
        //        }
        //    }
        //}


        //private void _BackFrame(bool leftTrueRightFalse)
        //{
        //    VideoCapture? cap;
        //    WriteableBitmap? wb;
        //    int frameIndex;

        //    if (leftTrueRightFalse)
        //    {
        //        cap = _capLeft;
        //        wb = _wbLeft;
        //        frameIndex = Math.Max(0, _currentFrameLeft - 1);
        //    }
        //    else
        //    {
        //        cap = _capRight;
        //        wb = _wbRight;
        //        frameIndex = Math.Max(0, _currentFrameRight - 1);
        //    }

        //    if (cap is not null && wb is not null)
        //    {

        //        // Set frame index in Emgu
        //        cap!.Set(CapProp.PosFrames, frameIndex);

        //        using var mat = new Mat();
        //        cap.Read(mat);

        //        // Check if Mat has valid data
        //        if (!mat.IsEmpty)
        //        {
        //            ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb);
        //        }

        //        if (leftTrueRightFalse)
        //        {
        //            _currentFrameLeft = frameIndex;
        //        }
        //        else
        //        {
        //            _currentFrameRight = frameIndex;
        //        }
        //    }
        //}

        //private void _JumpFrame(bool leftTrueRightFalse, int targetIndex)
        //{
        //    VideoCapture? cap;
        //    WriteableBitmap? wb;
        //    int frameIndex;

        //    frameIndex = Math.Max(0, targetIndex);

        //    if (leftTrueRightFalse)
        //    {
        //        cap = _capLeft;
        //        wb = _wbLeft;
        //    }
        //    else
        //    {
        //        cap = _capRight;
        //        wb = _wbRight;
        //    }

        //    if (cap is not null && wb is not null)
        //    {
        //        // Emgu: use Set with CapProp
        //        cap!.Set(CapProp.PosFrames, frameIndex);

        //        using var mat = new Mat();
        //        cap.Read(mat);

        //        if (!mat.IsEmpty && wb is not null)
        //        {
        //            ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb);
        //        }

        //        if (leftTrueRightFalse)
        //        {
        //            _currentFrameLeft = frameIndex;
        //        }
        //        else
        //        {
        //            _currentFrameRight = frameIndex;
        //        }
        //    }
        //}

        //private void ProcessFrame(bool leftTrueRightFalse, int frameIndex, Mat frame, WriteableBitmap wb)
        //{
        //    if (appMode == AppMode.Open)
        //    {
        //        try
        //        {
        //            DetectAndCreateFrameCalibrationTarget(leftTrueRightFalse, frameIndex, frame, dictionary5x5_100!, board5x5_100!, "5x5_100");

        //            DrawMarkersToMat(leftTrueRightFalse, frameIndex, frame);

        //            DrawFrameToScreen(frame, wb);
        //        }
        //        catch (Exception ex)
        //        {
        //            Debug.WriteLine($"ProcessFrame: Error processing ChArUco board, appMode:{appMode}, {ex.Message}");
        //        }

        //        SetUIControls();
        //    }
        //    else if (appMode == AppMode.BestFramesFind)
        //    {
        //        DrawFrameToScreen(frame, wb);
        //    }
        //    else if (appMode == AppMode.BestFramesView)
        //    {
        //        try
        //        {
        //            DrawMarkersToMat(leftTrueRightFalse, frameIndex, frame);

        //            DrawFrameToScreen(frame, wb);
        //        }
        //        catch (Exception ex)
        //        {
        //            Debug.WriteLine($"ProcessFrame: Error processing ChArUco board, appMode:{appMode}, {ex.Message}");
        //        }

        //        SetUIControls();
        //    }
        //    else if (appMode == AppMode.BestFramesCalc)

        //    {
        //        try
        //        {
        //            DrawMarkersToMat(leftTrueRightFalse, frameIndex, frame);

        //            DrawFrameToScreen(frame, wb);
        //        }
        //        catch (Exception ex)
        //        {
        //            Debug.WriteLine($"ProcessFrame: Error processing ChArUco board, appMode:{appMode}, {ex.Message}");
        //        }
        //    }
        //}

        /// <summary>
        /// Set the UI controls to the current mode
        /// </summary>
        private void SetUIControls()
        {
            //if (appMode == AppMode.Open || appMode == AppMode.BestFramesView)
            //{
            //    int totalRecordCount = calibrationFrameSetLeft.Frames.Count + calibrationFrameSetRight.Frames.Count;

            //    // Load Button - Always enabled

            //    // Save Button
            //    SaveButton.IsEnabled = totalRecordCount > 0 ? true : false;

            //    // Lock/Unlock Button            
            //    LockUnlockButton.IsEnabled = (_capLeft?.IsOpened == true) && (_capRight?.IsOpened == true);

            //    // Best Frames
            //    BestFrames.IsEnabled = totalRecordCount > 0 ? true : false;


            //    OpenButton.IsEnabled = true;
            //    SaveButton.IsEnabled = true;
            //    LockUnlockButton.IsEnabled = true;
            //    BestFrames.IsEnabled = true;

            //}
            //else if (appMode == AppMode.BestFramesFind || appMode == AppMode.BestFramesCalc)
            //{
            //    OpenButton.IsEnabled = false;
            //    SaveButton.IsEnabled = false;
            //    LockUnlockButton.IsEnabled = false;
            //    BestFrames.IsEnabled = false;
            //}

            //SetUIControls(true/*trueLeftfalseRight*/, calibrationFrameSetLeft);
            //SetUIControls(false/*trueLeftfalseRight*/, calibrationFrameSetRight);
        }

        //private void SetUIControls(bool trueLeftfalseRight, CalibrationFrameSet calibrationFrameSet)
        //{
        //    bool LeftFrameBackButtonIsEnabled = true;
        //    bool LeftPlayPauseButtonIsEnabled = true;
        //    bool LeftFrameForwardButtonIsEnabled = true;
        //    bool RightFrameBackButtonIsEnabled = true;
        //    bool RightPlayPauseButtonIsEnabled = true;
        //    bool RightFrameForwardButtonsEnabled = true;
        //    bool LeftFrameInfoTextBoxIsVisable = true;
        //    bool RightFrameInfoTextBoxIsVisable = true;


        //    if (appMode == AppMode.Open || appMode == AppMode.BestFramesView)
        //    {


        //    }
        //    else if (appMode == AppMode.BestFramesFind || appMode == AppMode.BestFramesCalc)
        //    {
        //        if (trueLeftfalseRight)
        //        {
        //            LeftFrameBackButtonIsEnabled = false;
        //            LeftPlayPauseButtonIsEnabled = false;
        //            LeftFrameForwardButtonIsEnabled = false;
        //        }
        //        else
        //        {
        //            RightFrameBackButtonIsEnabled = false;
        //            RightPlayPauseButtonIsEnabled = false;
        //            RightFrameForwardButtonsEnabled = false;
        //        }

        //        // Clear the metadata display fields
        //        ClearFrameMetaData(trueLeftfalseRight);
        //    }

        //    if (trueLeftfalseRight)
        //    {
        //        LeftFrameBackButton.IsEnabled = LeftFrameBackButtonIsEnabled;
        //        LeftPlayPauseButton.IsEnabled = LeftPlayPauseButtonIsEnabled;
        //        LeftFrameForwardButton.IsEnabled = LeftFrameForwardButtonIsEnabled;
        //        LeftFrameInfoTextBox.Visibility = LeftFrameInfoTextBoxIsVisable ? Visibility.Visible : Visibility.Collapsed;                
        //    }
        //    else
        //    {
        //        RightFrameBackButton.IsEnabled = RightFrameBackButtonIsEnabled;
        //        RightPlayPauseButton.IsEnabled = RightPlayPauseButtonIsEnabled;
        //        RightFrameForwardButton.IsEnabled = RightFrameForwardButtonsEnabled;
        //        RightFrameInfoTextBox.Visibility = RightFrameInfoTextBoxIsVisable ? Visibility.Visible : Visibility.Collapsed;
        //    }

        //}


    //    public async Task<StorageFolder?> PickOutputFolderAsync(Window window)
    //    {
    //        FolderPicker picker = new FolderPicker();
    
    //        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
    //        picker.FileTypeFilter.Add("*"); // Required even if picking a folder

    //        // Initialize the picker with the window handle (required in WinUI 3)
    //        IntPtr hWnd = WindowNative.GetWindowHandle(window);
    //        InitializeWithWindow.Initialize(picker, hWnd);

    //        StorageFolder folder = await picker.PickSingleFolderAsync();
    //        return folder; // returns null if user cancels
    //    }
    }
}

