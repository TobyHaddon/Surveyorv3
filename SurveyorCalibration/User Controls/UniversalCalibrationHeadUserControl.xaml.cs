using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.Calibration;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;



namespace Surveyor.Controls
{
    public sealed partial class UniversalCalibrationHeadUserControl : UserControl
    {
        // Media file files and handles
        private string leftMediaFileSpec = string.Empty;
        private string rightMediaFileSpec = string.Empty;
        private VideoCapture? capLeft = null;
        private VideoCapture? capRight = null;

        // Target calibration board setup
        private Dictionary? arucoDictionary;
        private CharucoBoard? board;
        private string boardName = string.Empty;

        // Writeable bitmaps for the left and right camera frames
        private WriteableBitmap? wbLeft = null;
        private WriteableBitmap? wbRight = null;

        // Total frame count
        private int _totalFramesLeft = -1;
        private int _totalFramesRight = -1;

        // Current frame indexes
        private int _currentFrameLeft = 0;
        private int _currentFrameRight = 0;

        // Current Best frame Indexes
        private int _currentBestFrame = 0;


        // Play timers
        private bool _isLeftPlaying = false;
        private DispatcherTimer _playLeftTimer;
        private bool _isRightPlaying = false;
        private DispatcherTimer _playRightTimer;
        private bool _isBothPlaying = false;
        private DispatcherTimer _playBothTimer;

        private bool isLocked = false;

        private CalibrationStereoFrameSet calibrationStereoFrameSet = new();

        private CancellationToken cancellationToken;
        private CancellationTokenSource? cts = null;

        private bool isFindCalibrationFrameRunning = false;

        private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

        private enum AppMode { Open, FindCalibrationsFrames, BestFramesCalc, BestFramesView };
        private AppMode appMode = AppMode.Open;


        public static readonly DependencyProperty HeadProperty =
                DependencyProperty.Register(nameof(Head), typeof(string), typeof(UniversalCalibrationHeadUserControl),
        new PropertyMetadata("Stereo", OnHeadChanged));

        public string Head
        {
            get => (string)GetValue(HeadProperty);
            set => SetValue(HeadProperty, value);
        }

        private static void OnHeadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UniversalCalibrationHeadUserControl ctrl)
            {
                ctrl.ApplyMode((string)e.NewValue);
            }
        }

        private void ApplyMode(string mode)
        {
            switch (mode.ToLowerInvariant())
            {
                case "mono":
                    // Hide column 2
                    RootGrid.ColumnDefinitions[2].Width = new GridLength(0);
                    LockUnlockButton.IsEnabled = false;
                    LockUnlockButton.Visibility = Visibility.Collapsed;
                    break;

                case "stereo":
                    // Show Column 2
                    RootGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                    LockUnlockButton.IsEnabled = true;
                    LockUnlockButton.Visibility = Visibility.Visible;

                    break;

                default:
                    throw new InvalidOperationException($"Unknown Head mode: {mode}");
            }
        }

        public UniversalCalibrationHeadUserControl()
        {
            // Get the DispatcherQueue for the current thread
            dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();


            this.InitializeComponent();

            _playLeftTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _playLeftTimer.Tick += (s, e) => PlayLeft();

            _playRightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _playRightTimer.Tick += (s, e) => PlayRight();

            _playBothTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _playBothTimer.Tick += (s, e) => PlayBoth();

            CalibrationFrameSetViewerData dataLeft = new(true/*trueLeftFalseRight*/, calibrationStereoFrameSet);
            CalibrationFrameSetViewerData dataRight = new(false/*trueLeftFalseRight*/, calibrationStereoFrameSet);

            CalibrationFrameSetViewerLeft.Data = dataLeft;
            CalibrationFrameSetViewerRight.Data = dataRight;

            // Ensure correct layout at initialization (Note the correct Head value isn't set yet)
            this.Loaded += (_, _) =>
            {
                ApplyMode(Head);
            };

            this.Unloaded += StereoCalibrationHeadUserControl_Unloaded;
        }


        /// <summary>
        /// Clean up timers, event handlers, disposable resources etc.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StereoCalibrationHeadUserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _playLeftTimer.Stop();
            _playRightTimer.Stop();
            _playBothTimer.Stop();
        }


        /// <summary>
        /// Set up the calibration board type for the stereo camera calibration.
        /// The boardname is just for reporting
        /// Example setup:
        /// 
        ///         // Create dictionary
        ///         dictionary5x5_100 = new Dictionary(PredefinedDictionaryName.Dict5X5_100);
        ///
        ///         // Create ChArUco board
        ///         float squareLength = 40.0f / 1000.0f;
        ///         float markerLength = 30.0f / 1000.0f;
        ///         int squaresX = 14;
        ///         int squaresY = 9;
        ///         board5x5_100 = new CharucoBoard(squaresX, squaresY, squareLength, markerLength, dictionary5x5_100);
        ///
        /// </summary>
        /// <param name="arucoDictionary"></param>
        /// <param name="boardName"></param>
        /// <returns></returns>
        public bool SetupCalibrationBoardType(Dictionary _arucoDictionary, CharucoBoard _board, string _boardName)
        {
            arucoDictionary = _arucoDictionary;
            board = _board;
            boardName = _boardName;

            return calibrationStereoFrameSet.SetupCalibrationBoardType(arucoDictionary, board, boardName);
        }

        /// <summary>
        /// Open the media files for the left and right cameras.
        /// </summary>
        /// <param name="leftFileSpec"></param>
        /// <param name="rightFileSpec">Set to string.Empty is Mono</param>
        /// <returns></returns>
        public bool OpenMedia(string _leftMediaFileSpec, string _rightMediaFileSpec)
        {
            bool ret = false;
            bool leftOpened = false;
            //???bool rightOpened = false;

            // Reset
            leftMediaFileSpec = string.Empty;
            rightMediaFileSpec = string.Empty;
            capLeft = null;
            capRight = null;

            // Open Left side
            if (File.Exists(_leftMediaFileSpec))
            {
                leftMediaFileSpec = _leftMediaFileSpec;

                // Open Left
                capLeft = new Emgu.CV.VideoCapture(leftMediaFileSpec);

                if (capLeft.IsOpened)
                {
                    // Get total number of frames
                    _totalFramesLeft = (int)capLeft.Get(CapProp.FrameCount);

                    using var testFrame = new Emgu.CV.Mat();
                    capLeft.Read(testFrame);

                    if (!testFrame.IsEmpty)
                    {
                        // Create WriteableBitmap with Emgu frame dimensions
                        wbLeft = new WriteableBitmap(testFrame.Width, testFrame.Height);

                        // Reset to first frame — Emgu uses .Set() with CapProp
                        capLeft.Set(Emgu.CV.CvEnum.CapProp.PosFrames, 0);                        

                        LeftImage.Source = wbLeft;
                        _currentFrameLeft = 0;

                        // Setup the timeline ranges indicators that show where calibration
                        // boards have been found
                        CalibrationBoardTimeLineLeft.Visibility = Visibility.Visible;
                        CalibrationBoardTimeLineLeft.SetRange(0, _totalFramesLeft);

                        leftOpened = true;
                    }
                }
            }
            else
            {
                if (Head.Equals("stereo", StringComparison.InvariantCultureIgnoreCase))
                    Debug.WriteLine($"OpenMedia: Left stereo media {_leftMediaFileSpec} does not exist.");
                else
                    Debug.WriteLine($"OpenMedia: Media {_leftMediaFileSpec} does not exist.");
            }

            // If Stereo open right side
            if (Head.Equals("stereo", StringComparison.InvariantCultureIgnoreCase))
            {
                if (File.Exists(_rightMediaFileSpec))
                {
                    rightMediaFileSpec = _rightMediaFileSpec;

                    // Open Right if stereo
                    capRight = new Emgu.CV.VideoCapture(rightMediaFileSpec);

                    if (capRight.IsOpened)
                    {
                        // Get total number of frames
                        _totalFramesRight = (int)capRight.Get(CapProp.FrameCount);

                        using var testFrame = new Emgu.CV.Mat();
                        capRight.Read(testFrame);

                        if (!testFrame.IsEmpty)
                        {
                            // Create WriteableBitmap with Emgu frame dimensions
                            wbRight = new WriteableBitmap(testFrame.Width, testFrame.Height);

                            // Reset to first frame — Emgu uses .Set() with CapProp
                            capRight.Set(Emgu.CV.CvEnum.CapProp.PosFrames, 0);

                            RightImage.Source = wbRight;
                            _currentFrameRight = 0;

                            // Setup the timeline ranges indicators that show where calibration
                            // boards have been found
                            CalibrationBoardTimeLineRight.SetRange(0, _totalFramesRight);
                            CalibrationBoardTimeLineRight.Visibility = Visibility.Visible;

                            //???rightOpened = true;
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"OpenMedia: Right stereo media {_rightMediaFileSpec} does not exist.");
                }
            }


            // Give CalibrationStereoFrameSet access to the video capture handles
            if (leftOpened && capLeft is not null)
            {
                calibrationStereoFrameSet.SetupMedia(capLeft, capRight);
            }

            SetUIControls();

            return ret;
        }


        /// <summary>
        /// Close the media files for the left and right cameras.
        /// </summary>
        /// <param name="leftFileSpec"></param>
        /// <param name="rightFileSpec"></param>
        /// <returns></returns>
        public bool CloseMedia()
        {
            bool ret = false;

            calibrationStereoFrameSet.ShutDownMedia();

            CalibrationBoardTimeLineLeft.Visibility = Visibility.Collapsed;
            CalibrationBoardTimeLineRight.Visibility = Visibility.Collapsed;

            if (capLeft is not null && capLeft.IsOpened)
            {
                capLeft.Dispose();
                capLeft = null;
            }

            if (capRight is not null && capRight.IsOpened)
            {
                capRight.Dispose();
                capRight = null;
            }

            SetUIControls();

            return ret;
        }

        /// <summary>
        /// Check media is open and ready
        /// </summary>
        /// <returns></returns>
        public bool IsOpen()
        {
            // Check if the media files are open
            bool leftOpen = capLeft is not null && capLeft.IsOpened;

            if (Head.Equals("stereo", StringComparison.InvariantCultureIgnoreCase))
            {
                bool rightOpen = capRight is not null && capRight.IsOpened;
                return leftOpen && rightOpen;
            }
            else
            {
                return leftOpen;
            }                           
        }


        /// <summary>
        /// Check if the stereo head is locked or not.
        /// </summary>
        /// <returns>Null is Mono, True is Stereo and Locked</returns>
        public bool? IsStereoLocked()
        {
            if (Head.Equals("stereo", StringComparison.InvariantCultureIgnoreCase))
            {
                return isLocked;
            }
            else
            {
                return null;  // Mono
            }
        }


        /// <summary>
        /// Search the media for the calibration boards
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public async void FindCalibrationFrame()
        {
            try
            {
                appMode = AppMode.FindCalibrationsFrames;
                SetUIControls();

                cts = new CancellationTokenSource();
                cancellationToken = cts.Token;

                // Move both methods to background threads
                var (startCalibration, stopCalibration) = await Task.Run(() =>
                    calibrationStereoFrameSet.FindCalibrationTimeLineRange(FrameProcessingCallbackFindCalibrationTimeLineRange, cancellationToken));

                if (startCalibration != -1 && stopCalibration != -1)
                {
                    // Update the timeline ranges
                    CalibrationBoardTimeLineLeft.CalibrationBoardRange(startCalibration, stopCalibration);
                    CalibrationBoardTimeLineRight.CalibrationBoardRange(startCalibration, stopCalibration);

                    // Next find the calibration frames with in that range
                    int framesCount = await Task.Run(() =>
                    {
                        isFindCalibrationFrameRunning = true;
                        try
                        {
                            return calibrationStereoFrameSet.FindCalibrationsFrames(
                                startCalibration,
                                stopCalibration,
                                FrameProcessingCallbackFindCalibrationsFrames,
                                cancellationToken);
                        }
                        finally
                        {
                            isFindCalibrationFrameRunning = false;
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Calibration search cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during calibration search: {ex.Message}");
            }
            finally
            {
                appMode = AppMode.Open;
                SetUIControls();
            }
        }


        /// <summary>
        /// Cancel the search for calibration frames.
        /// </summary>
        public void FindCalibrationFrameCancel()
        {
            cts?.Cancel();
        }


        /// <summary>
        /// Check if the Find Calibration Frames is running or not
        /// </summary>
        /// <returns></returns>
        public bool IsFindRunning()
        {
            return isFindCalibrationFrameRunning;
        }


        /// <summary>
        /// Extract the best frames
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public async void BestFramesCalc(bool writeBestFramesToPng = true)
        {
            appMode = AppMode.BestFramesCalc;
            SetUIControls();

            if (calibrationStereoFrameSet is not null)
            {
                // Create a list of the best calibation frames from the left side
                calibrationStereoFrameSet.SelectBestStereoFrames();

                if (writeBestFramesToPng)
                {
                    if (await SaveBestFiles())
                    {
                        // Update the left image viewers with the saved frames
                        string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        string outputPathLeft = MakeAndCreateFramesDirectory(documentsFolder, leftMediaFileSpec, false);

                        string searchPath = Path.Combine(outputPathLeft, "*.png");
                        LeftImageViewer.Data = searchPath;

                        // Update the right image viewers with the saved frames                    
                        string outputPathRight = MakeAndCreateFramesDirectory(documentsFolder, rightMediaFileSpec, false);

                        searchPath = Path.Combine(outputPathRight, "*.png");
                        RightImageViewer.Data = searchPath;
                    }
                }
            }

            appMode = AppMode.BestFramesView;
            BestFrameJump(0);
            LeftUpdateFrameLabel();
            RightUpdateFrameLabel();
            SetUIControls();
        }


        /// <summary>
        /// Write the Calibration Frame Set to file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public bool SaveResults()
        {
            // Make the left calibration frame set path
            string calibrationFrameSetPath = MakeCalibrationStereoFrameSetPath(leftMediaFileSpec, rightMediaFileSpec);

            bool saved = calibrationStereoFrameSet.SaveToFile(calibrationFrameSetPath);
        
            SetUIControls();

            return saved;
        }


        /// <summary>
        /// Used to check is a cached result file already exists
        /// </summary>
        /// <returns></returns>
        public bool ResultsFileExists()
        {
            bool ret = false;

            // Make the left calibration frame set path
            string calibrationFrameSetPath = MakeCalibrationStereoFrameSetPath(leftMediaFileSpec, rightMediaFileSpec);

            // Check if the file exists (remove any zero byte file)
            DeleteIfZeroByteFile(calibrationFrameSetPath);

            if (File.Exists(calibrationFrameSetPath))
            {
                ret = true;
            }

            return ret;
        }


        /// <summary>
        ///  Load a Calibration Frame Set from file and display to the screen
        /// </summary>
        /// <param name="MP4Path"></param>
        /// <param name="calibrationFrameSet"></param>
        /// <param name="calibrationFrameSetViewer"></param>
        public bool LoadResults()
        {
            bool ret = false;
            string messageText;

            // Make the left calibration frame set path
            string calibrationFrameSetPath = MakeCalibrationStereoFrameSetPath(leftMediaFileSpec, rightMediaFileSpec);

            // Check if the file exists (remove any zero byte file)
            DeleteIfZeroByteFile(calibrationFrameSetPath);

            if (File.Exists(calibrationFrameSetPath))
            {
                // Load the calibration frame set
                var json = CalibrationStereoFrameSet.LoadFromFile(calibrationFrameSetPath);
                if (json is not null)
                {
                    calibrationStereoFrameSet = json;

                    CalibrationFrameSetViewerData dataLeft = new(true/*trueLeftFalseRight*/, calibrationStereoFrameSet);
                    CalibrationFrameSetViewerLeft.Data = dataLeft;
                    CalibrationFrameSetViewerLeft.RefreshBinLayers();
                    CalibrationFrameSetViewerLeft.DrawGraphs();

                    CalibrationFrameSetViewerData dataRight = new(false/*trueLeftFalseRight*/, calibrationStereoFrameSet);
                    CalibrationFrameSetViewerRight.Data = dataRight;
                    CalibrationFrameSetViewerRight.RefreshBinLayers();
                    CalibrationFrameSetViewerRight.DrawGraphs();

                    ret = true;
                }
                else
                {
                    messageText = $"Failed to load left: {calibrationFrameSetPath}";
                    Debug.WriteLine(messageText);

                }
            }
            else
            {
                messageText = $"File not found left: {calibrationFrameSetPath}";
                Debug.WriteLine(messageText);
            }

            return ret;
        }




        ///
        /// EVENTS
        /// 

        /// <summary>
        /// Left side Frame Back button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftFrameBackClick(object sender, RoutedEventArgs e)
        {
            if (appMode == AppMode.Open)
                FrameMoveBack(true/*leftTrueRightFalse*/);
            else if (appMode == AppMode.BestFramesView)
                BestFrameMoveBack();
        }

        /// <summary>
        /// Left side Play button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (appMode == AppMode.Open)
                PlayPauseClick(true);
        }

        /// <summary>
        /// Left side Frame Forward button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        private void LeftFrameForwardClick(object sender, RoutedEventArgs e)
        {
            if (appMode == AppMode.Open)
                FrameMoveForward(true/*leftTrueRightFalse*/);
            else if (appMode == AppMode.BestFramesView)
                BestFrameMoveForward();
        }


        /// <summary>
        /// Right side Frame Backbutton pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightFrameBackClick(object sender, RoutedEventArgs e)
        {
            if (appMode == AppMode.Open)
                FrameMoveBack(false/*leftTrueRightFalse*/);
            else if (appMode == AppMode.BestFramesView)
                BestFrameMoveBack();
        }


        /// <summary>
        /// Right side Play button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (appMode == AppMode.Open)
                PlayPauseClick(false);
        }


        /// <summary>
        /// Right side Frame Forward button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightFrameForwardClick(object sender, RoutedEventArgs e)
        {
            if (appMode == AppMode.Open)
                FrameMoveForward(false/*leftTrueRightFalse*/);
            else if (appMode == AppMode.BestFramesView)
                BestFrameMoveForward();
        }


        /// <summary>
        /// Media Lock/Unlock button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LockUnlockClick(object sender, RoutedEventArgs e)
        {
            isLocked = !isLocked;
            LockUnlockIcon.Glyph = isLocked ? "\uE72E" : "\uE785";
            if (isLocked)
            {
                calibrationStereoFrameSet.SetupLockFrameIndexes(_currentFrameLeft, _currentFrameRight);
            }
            else
            {
                calibrationStereoFrameSet.SetupLockFrameIndexes(-1, -1);
            }
        }


        /// <summary>
        /// User request to go to a particular left frame index
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftFrameInfoTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (int.TryParse(LeftFrameInfoTextBox.Text, out int targetIndex))
                {
                    FrameJump(true/*left*/, targetIndex);
                }
            }
        }


        /// <summary>
        /// User request to go to a particular right frame index
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightFrameInfoTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (int.TryParse(RightFrameInfoTextBox.Text, out int targetIndex))
                {
                    FrameJump(false/*right*/, targetIndex);
                }
            }
        }


        /// <summary>
        /// Load a calibration frame set file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            string messageText = string.Empty;

            // Check if you will be overwriting data that is already loaded
            // i.e. maybe the user pressed the wrong button
            bool load = false;
            if (calibrationStereoFrameSet.Frames.Count > 0)
            {
                messageText = $"There is existing data already loaded. Are you sure you want to continue?";
            
                // Check with the user
                var dialog = new ContentDialog
                {
                    Title = "Calibration Frame Set Open",
                    Content = messageText,
                    PrimaryButtonText = "Ok",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot  // 'this' is the MainWindow
                };
                // Show the dialog
                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                    load = true;

                messageText = string.Empty;
            }
            else
                // Nothing yet loaded so ok to load from file
                load = true;

            if (load)
            {
                // Load and display the left calibration frame set file
                bool loaded = LoadResults();



                if (loaded)
                {
                    messageText = $"Stereo Calibration Frame Set loaded ok";
                }

                ReportOnLargeValues();

                if (!string.IsNullOrEmpty(messageText))
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Calibration Frame Set Open",
                        Content = messageText,
                        CloseButtonText = "Ok",
                        XamlRoot = this.Content.XamlRoot  // 'this' is the MainWindow
                    };
                    // Show the dialog
                    var result = dialog.ShowAsync();
                }
            }

            SetUIControls();
        }


        /// <summary>
        /// Search the media for the calibration boards
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //???TOBEDELETED
        //private async void SearchCalibrationBoard_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        appMode = AppMode.FindCalibrationsFrames;
        //        SetUIControls();

        //        cts = new CancellationTokenSource();
        //        cancellationToken = cts.Token;

        //        // Move both methods to background threads
        //        var (startCalibration, stopCalibration) = await Task.Run(() =>
        //            calibrationStereoFrameSet.FindCalibrationTimeLineRange(FrameProcessingCallbackFindCalibrationTimeLineRange, cancellationToken));

        //        if (startCalibration != -1 && stopCalibration != -1)
        //        {
        //            // Update the timeline ranges
        //            CalibrationBoardTimeLineLeft.CalibrationBoardRange(startCalibration, stopCalibration);
        //            CalibrationBoardTimeLineRight.CalibrationBoardRange(startCalibration, stopCalibration);

        //            // Next find the calibration frames with in that range
        //            int framesCount = await Task.Run(() =>
        //                calibrationStereoFrameSet.FindCalibrationsFrames(startCalibration, stopCalibration,
        //                                                                 FrameProcessingCallbackFindCalibrationsFrames, 
        //                                                                 cancellationToken));
        //        }
        //    }
        //    catch (OperationCanceledException)
        //    {
        //        Debug.WriteLine("Calibration search cancelled.");
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"Error during calibration search: {ex.Message}");
        //    }
        //    finally
        //    {
        //        appMode = AppMode.Open;
        //        SetUIControls();
        //    }
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
        //    string calibrationFrameSetPath = MakeCalibrationStereoFrameSetPath(leftMediaFileSpec, rightMediaFileSpec);

        //    bool saved = calibrationStereoFrameSet.SaveToFile(calibrationFrameSetPath);

        //    string messageText = string.Empty;
        //    if (saved)
        //    {
        //        messageText = $"Stereo calibration Frame Set saved ok";
        //    }
        //    else
        //    {
        //        messageText = $"Failed to save stereo calibration Frame Set";
        //    }

        //    var dialog = new ContentDialog
        //    {
        //        Title = "Calibration Stereo Frame Set Save",
        //        Content = messageText,
        //        CloseButtonText = "Ok",
        //        XamlRoot = this.Content.XamlRoot  // 'this' is the MainWindow
        //    };
        //    // Show the dialog
        //    var result = dialog.ShowAsync();

        //    SetUIControls();
        //}


        /// <summary>
        /// User required the best frames to be calculated and saved to a folder.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private async void BestFrames_Click(object sender, RoutedEventArgs e)
        //{
        //    appMode = AppMode.BestFramesCalc;
        //    SetUIControls();

        //    if (calibrationStereoFrameSet is not null)
        //    {
        //        // Create a list of the best calibation frames from the left side
        //        calibrationStereoFrameSet.SelectBestStereoFrames();

        //        if (await SaveBestFiles())
        //        {
        //            // Update the left image viewers with the saved frames
        //            string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        //            string outputPathLeft = MakeAndCreateFramesDirectory(documentsFolder, leftMediaFileSpec, false);
                    
        //            string searchPath = Path.Combine(outputPathLeft, "*.png");
        //            LeftImageViewer.Data = searchPath;

        //            // Update the right image viewers with the saved frames                    
        //            string outputPathRight = MakeAndCreateFramesDirectory(documentsFolder, rightMediaFileSpec, false);

        //            searchPath = Path.Combine(outputPathRight, "*.png");
        //            RightImageViewer.Data = searchPath;
        //        }
        //    }

        //    appMode = AppMode.BestFramesView;
        //    BestFrameJump(0);
        //    LeftUpdateFrameLabel();
        //    RightUpdateFrameLabel();
        //    SetUIControls();
        //}

        /// <summary>
        /// Used to cancel long running operations
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            cts?.Cancel();
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// This is the progress callback used for the FindCalibrationTimeLineRange() method
        /// </summary>
        /// <param name="stereoFrameIndex"></param>
        /// <param name="stereoFrameTotal"></param>
        /// <param name="leftFrameIndex"></param>
        /// <param name="leftMat"></param>
        /// <param name="leftFrameCalibrationTarget"></param>
        /// <param name="rightFrameIndex"></param>
        /// <param name="rightMat"></param>
        /// <param name="rightFrameCalibrationTarget"></param>
        private void FrameProcessingCallbackFindCalibrationTimeLineRange(
                int stereoFrameIndex,
                int stereoFrameTotal,
                int leftFrameIndex,
                Mat leftMat,
                FrameCalibrationTarget? leftFrameCalibrationTarget,
                int rightFrameIndex,
                Mat? rightMat,
                FrameCalibrationTarget? rightFrameCalibrationTarget,
                object? userData)
        {
            dispatcherQueue.TryEnqueue(() => {

                bool trueFoundFalseNotFound;

                if (leftMat is not null && !leftMat.IsEmpty && wbLeft is not null)
                {
                    DrawFrameToScreen(leftMat, wbLeft);
                    LeftFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                }

                trueFoundFalseNotFound = leftFrameCalibrationTarget is not null;
                CalibrationBoardTimeLineLeft.CalibrationBoardFoundAt(leftFrameIndex, trueFoundFalseNotFound);


                if (rightMat is not null && !rightMat.IsEmpty && wbRight is not null)
                {
                    DrawFrameToScreen(rightMat, wbRight);
                    RightFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                }

                trueFoundFalseNotFound = rightFrameCalibrationTarget is not null;
                CalibrationBoardTimeLineRight.CalibrationBoardFoundAt(rightFrameIndex, trueFoundFalseNotFound);

            });
        }


        /// <summary>
        /// This is the progress callback used for the FindCalibrationsFrames() method
        /// </summary>
        /// <param name="stereoFrameIndex"></param>
        /// <param name="stereoFrameTotal"></param>
        /// <param name="leftFrameIndex"></param>
        /// <param name="leftMat"></param>
        /// <param name="leftFrameCalibrationTarget"></param>
        /// <param name="rightFrameIndex"></param>
        /// <param name="rightMat"></param>
        /// <param name="rightFrameCalibrationTarget"></param>
        /// <param name="userData"></param>
        private void FrameProcessingCallbackFindCalibrationsFrames(int stereoFrameIndex,
                int stereoFrameTotal,
                int leftFrameIndex,
                Mat leftMat,
                FrameCalibrationTarget? leftFrameCalibrationTarget,
                int rightFrameIndex,
                Mat? rightMat,
                FrameCalibrationTarget? rightFrameCalibrationTarget,
                object? userData)
        {
            dispatcherQueue.TryEnqueue(() => {

                bool trueFoundFalseNotFound;

                if (leftMat is not null && !leftMat.IsEmpty && wbLeft is not null)
                {
                    DrawFrameToScreen(leftMat, wbLeft);
                    LeftFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                }

                trueFoundFalseNotFound = leftFrameCalibrationTarget is not null;
                CalibrationBoardTimeLineLeft.CalibrationBoardFoundAt(leftFrameIndex, trueFoundFalseNotFound);


                if (rightMat is not null && !rightMat.IsEmpty && wbRight is not null)
                {
                    DrawFrameToScreen(rightMat, wbRight);
                    RightFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                }

                trueFoundFalseNotFound = rightFrameCalibrationTarget is not null;
                CalibrationBoardTimeLineRight.CalibrationBoardFoundAt(rightFrameIndex, trueFoundFalseNotFound);


                try
                {
                    // Update from Bin Layers and the graphs 
                    // Note these are fully recreated from the full list to date
                    if (leftFrameCalibrationTarget is not null)
                    {
                        CalibrationFrameSetViewerLeft.RefreshBinLayers();
                        CalibrationFrameSetViewerLeft.DrawGraphs();
                    }
                    if (rightFrameCalibrationTarget is not null)
                    {
                        CalibrationFrameSetViewerRight.RefreshBinLayers();
                        CalibrationFrameSetViewerRight.DrawGraphs();
                    }


                    double movementFactor;
                    double movementFromPrevious;
                    double movementToNext;


                    if (leftFrameCalibrationTarget is not null)
                    {
                        (movementFromPrevious, movementFactor, movementToNext) = GetMovementFactors(leftFrameCalibrationTarget);

                        UpdateFrameMetaData(true/*trueLeftfalseRight*/,
                                            movementFactor, movementFromPrevious, movementToNext,
                                            leftFrameCalibrationTarget.BlurFactor,
                                            leftFrameCalibrationTarget.CharucoCorners.Length /*Size*/,
                                            leftFrameCalibrationTarget.Score);
                    }

                    if (rightFrameCalibrationTarget is not null)
                    {
                        (movementFromPrevious, movementFactor, movementToNext) = GetMovementFactors(rightFrameCalibrationTarget);

                        UpdateFrameMetaData(false/*trueLeftfalseRight*/,
                        movementFactor, movementFromPrevious, movementToNext,
                        rightFrameCalibrationTarget.BlurFactor,
                        rightFrameCalibrationTarget.CharucoCorners.Length /*Size*/,
                        rightFrameCalibrationTarget.Score);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DrawMarkersToMat: Error processing ChArUco board: {ex.Message}");
                }

            });
        }


        /// <summary>
        /// Extract the movement factors from the frameCalibrationTarget.
        /// We want to display the movement factor, maybe the movement of the current
        /// frame isn't available yet, so we will display the movement from the previous
        /// </summary>
        /// <param name="frameCalibrationTarget"></param>
        /// <param name="frameIndex"></param>
        /// <param name="calibrationFrameSet"></param>
        /// <returns></returns>
        private static (double movementFromPrevious, double movementFactor, double movementToNext) GetMovementFactors(FrameCalibrationTarget? frameCalibrationTarget)
        {
            if (frameCalibrationTarget is null)
                return (-1, -1, -1);

            double movementFactor = frameCalibrationTarget.MovementFactor;
            double movementFromPrevious = frameCalibrationTarget.MovementFromPrevious;
            double movementToNext = frameCalibrationTarget.MovementToNext;

            if (movementFactor != -1)
                return (-1, movementFactor, -1);

            return (movementFromPrevious != -1 ? movementFromPrevious : -1,
                    -1,
                    movementToNext != -1 ? movementToNext : -1);
        }


        /// <summary>
        /// Update the left or right frame metadata
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        /// <param name="movementFactor"></param>
        /// <param name="blurFactor"></param>
        /// <param name="featureCount"></param>
        /// <param name="score"></param>
        private void UpdateFrameMetaData(bool trueLeftfalseRight, double movementFactor, double movementFromPrevious, double movementToNext, double blurFactor, int featureCount, double score)
        {
            TextBlock MovementFactor;
            TextBlock BlurFactor;
            TextBlock FeatureCount;
            TextBlock Score;

            if (trueLeftfalseRight)
            {
                MovementFactor = LeftMovementFactor;
                BlurFactor = LeftBlurFactor;
                FeatureCount = LeftFeatureCount;
                Score = LeftScore;
            }
            else
            {
                MovementFactor = RightMovementFactor;
                BlurFactor = RightBlurFactor;
                FeatureCount = RightFeatureCount;
                Score = RightScore;
            }

            // Display movement and blur factor
            if (movementFactor != -1)
            {
                MovementFactor.Text = $"Move: {movementFactor:F1}";
            }
            else if (movementFromPrevious != -1)
            {
                MovementFactor.Text = $"Move: \u2190{movementFromPrevious:F1}";
            }
            else if (movementToNext != -1)
            {
                MovementFactor.Text = $"Move: {movementToNext:F1}\u21D2";
            }


            BlurFactor.Text = $"Blur: {blurFactor:F1}";

            // Feature Count (number of Charuco corners)
            FeatureCount.Text = $"Corners: {featureCount}";

            // Score
            Score.Text = $"Score: {score:F2}";

        }

        /// <summary>
        /// Clear the frame metadata on screen fields
        /// </summary>
        private void ClearFrameMetaData(bool trueLeftfalseRight)
        {
            if (trueLeftfalseRight)
            {
                LeftMovementFactor.Text = string.Empty;
                LeftBlurFactor.Text = string.Empty;
                LeftFeatureCount.Text = string.Empty;
                LeftScore.Text = string.Empty;
            }
            else
            {
                RightMovementFactor.Text = string.Empty;
                RightBlurFactor.Text = string.Empty;
                RightFeatureCount.Text = string.Empty;
                RightScore.Text = string.Empty;
            }
        }


        private void PlayLeft()
        {
            if (capLeft != null && wbLeft != null)
            {
                _ForwardFrame(true/*leftTrueRightFalse*/);
                LeftUpdateFrameLabel();
            }
        }
        private void PlayRight()
        {
            if (capRight != null && wbRight != null)
            {
                _ForwardFrame(false/*leftTrueRightFalse*/);
                RightUpdateFrameLabel();
            }
        }
        private void PlayBoth()
        {
            if (capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                _ForwardFrame(true/*leftTrueRightFalse*/);
                _ForwardFrame(false/*leftTrueRightFalse*/);
                LeftUpdateFrameLabel();
                RightUpdateFrameLabel();
            }
        }

        private void FrameMoveBack(bool leftTrueRightFalse)
        {
            if (isLocked && capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                _BackFrame(true/*leftTrueRightFalse*/);
                _BackFrame(false/*leftTrueRightFalse*/);
                LeftUpdateFrameLabel();
                RightUpdateFrameLabel();
            }
            else if (leftTrueRightFalse && capLeft != null && wbLeft != null)
            {
                _BackFrame(true/*leftTrueRightFalse*/);
                LeftUpdateFrameLabel();
            }
            else if (!leftTrueRightFalse && capRight != null && wbRight != null)
            {
                _BackFrame(false/*leftTrueRightFalse*/);
                RightUpdateFrameLabel();
            }
        }

        private void FrameMoveForward(bool leftTrueRightFalse)
        {
            if (isLocked && capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                _ForwardFrame(true/*leftTrueRightFalse*/);
                _ForwardFrame(false/*leftTrueRightFalse*/);
                LeftUpdateFrameLabel();
                RightUpdateFrameLabel();
            }
            else if (leftTrueRightFalse && capLeft != null && wbLeft != null)
            {
                _ForwardFrame(true/*leftTrueRightFalse*/);
                LeftUpdateFrameLabel();
            }
            else if (!leftTrueRightFalse && capRight != null && wbRight != null)
            {
                _ForwardFrame(false/*leftTrueRightFalse*/);
                RightUpdateFrameLabel();
            }
        }

        private void PlayPauseClick(bool leftTrueRightFalse)
        {
            if (isLocked)
            {
                _isBothPlaying = !_isBothPlaying;
                if (_isBothPlaying)
                {
                    _playBothTimer.Start();
                    LeftPlayPauseIcon.Glyph = "\uF8AE";
                    RightPlayPauseIcon.Glyph = "\uF8AE";
                }
                else
                {
                    _playBothTimer.Stop();
                    LeftPlayPauseIcon.Glyph = "\uF5B0";
                    RightPlayPauseIcon.Glyph = "\uF5B0";
                }
            }
            else if (leftTrueRightFalse)
            {
                _isLeftPlaying = !_isLeftPlaying;
                if (_isLeftPlaying)
                {
                    _playLeftTimer.Start();
                    LeftPlayPauseIcon.Glyph = "\uF8AE";
                }
                else
                {
                    _playLeftTimer.Stop();
                    LeftPlayPauseIcon.Glyph = "\uF5B0";
                }
            }
            else
            {
                _isRightPlaying = !_isRightPlaying;
                if (_isRightPlaying)
                {
                    _playRightTimer.Start();
                    RightPlayPauseIcon.Glyph = "\uF8AE";
                }
                else
                {
                    _playRightTimer.Stop();
                    RightPlayPauseIcon.Glyph = "\uF5B0";
                }
            }
        }

        private void FrameJump(bool leftTrueRightFalse, int targetIndex)
        {
            if (isLocked && capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                (int leftFrame, int rightFrame) = calibrationStereoFrameSet.GetIndexes(targetIndex);

                _JumpFrame(true/*leftTrueRightFalse*/, leftFrame);
                _JumpFrame(false/*leftTrueRightFalse*/, rightFrame);
                LeftUpdateFrameLabel();
                RightUpdateFrameLabel();
            }
            else if (leftTrueRightFalse && capLeft != null && wbLeft != null)
            {
                _JumpFrame(true/*leftTrueRightFalse*/, targetIndex);
                LeftUpdateFrameLabel();
            }
            else if (!leftTrueRightFalse && capRight != null && wbRight != null)
            {
                _JumpFrame(false/*leftTrueRightFalse*/, targetIndex);
                RightUpdateFrameLabel();
            }
        }


        /// <summary>
        /// Move back in the best frame list
        /// This moves both the left and right frame
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        private void BestFrameMoveBack()
        {
            int targetIndex;

            // BestFrameJump does the out of bounds check
            targetIndex = _currentBestFrame - 1;
            BestFrameJump(targetIndex);
        }


        private void BestFrameMoveForward()
        {
            int targetIndex;

            // BestFrameJump does the out of bounds check
            targetIndex = _currentBestFrame + 1;
            BestFrameJump(targetIndex);
        }

        private void BestFrameJump(int targetIndex)
        {
            bool ok = false;

            if (calibrationStereoFrameSet is not null)
            {
                if (targetIndex < 0)
                    targetIndex = 0;

                if (targetIndex >= calibrationStereoFrameSet.Frames.Count)
                    targetIndex = calibrationStereoFrameSet.Frames.Count;

                try
                {
                    int frameIndex = calibrationStereoFrameSet.BestFrameIndexes[targetIndex];

                    // Get stereo frame pair
                    (FrameCalibrationTarget leftTarget, FrameCalibrationTarget? rightTarget) = calibrationStereoFrameSet.Frames[frameIndex];

                    _JumpFrame(true/*leftTrueRightFalse*/, leftTarget.FrameIndex);
                    if (rightTarget is not null)
                        _JumpFrame(false/*leftTrueRightFalse*/, rightTarget.FrameIndex);
                    ok = true;
                }
                catch (Exception ex)
                {                    
                    Debug.WriteLine($"Failed to display best frames index:{targetIndex}, {ex.Message}");
                }
            }

            if (ok)
            {
                _currentBestFrame = targetIndex;
                LeftUpdateFrameLabel();
                RightUpdateFrameLabel();
            }
        }


        private void _ForwardFrame(bool leftTrueRightFalse)
        {
            VideoCapture? cap;
            WriteableBitmap? wb;
            int frameIndex;

            if (leftTrueRightFalse)
            {
                cap = capLeft;
                wb = wbLeft;
                frameIndex = Math.Max(0, _currentFrameLeft + 1);
            }
            else
            {
                cap = capRight;
                wb = wbRight;
                frameIndex = Math.Max(0, _currentFrameRight + 1);
            }

            if (cap is not null && wb is not null)
            {
                if (leftTrueRightFalse)
                {
                    // Check for end of media
                    if (_currentFrameLeft >= _totalFramesLeft)
                    {
                        PlayPauseClick(leftTrueRightFalse);
                        return;
                    }
                }
                else
                {
                    // Check for end of media
                    if (_currentFrameRight >= _totalFramesRight)
                    {
                        PlayPauseClick(leftTrueRightFalse);
                        return;
                    }
                }

                using var mat = new Mat();

                if (cap!.Read(mat) && !mat.IsEmpty)
                {
                    ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb);
                }

                if (leftTrueRightFalse)
                {
                    _currentFrameLeft = frameIndex;
                }
                else
                {
                    _currentFrameRight = frameIndex;
                }
            }
        }


        private void _BackFrame(bool leftTrueRightFalse)
        {
            VideoCapture? cap;
            WriteableBitmap? wb;
            int frameIndex;

            if (leftTrueRightFalse)
            {
                cap = capLeft;
                wb = wbLeft;
                frameIndex = Math.Max(0, _currentFrameLeft - 1);
            }
            else
            {
                cap = capRight;
                wb = wbRight;
                frameIndex = Math.Max(0, _currentFrameRight - 1);
            }

            if (cap is not null && wb is not null)
            {

                // Set frame index in Emgu
                cap!.Set(CapProp.PosFrames, frameIndex);

                using var mat = new Mat();
                cap.Read(mat);

                // Check if Mat has valid data
                if (!mat.IsEmpty)
                {
                    ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb);
                }

                if (leftTrueRightFalse)
                {
                    _currentFrameLeft = frameIndex;
                }
                else
                {
                    _currentFrameRight = frameIndex;
                }
            }
        }

        private void _JumpFrame(bool leftTrueRightFalse, int targetIndex)
        {
            VideoCapture? cap;
            WriteableBitmap? wb;
            int frameIndex;

            frameIndex = Math.Max(0, targetIndex);

            if (leftTrueRightFalse)
            {
                cap = capLeft;
                wb = wbLeft;
            }
            else
            {
                cap = capRight;
                wb = wbRight;
            }

            if (cap is not null && wb is not null)
            {
                // Emgu: use Set with CapProp
                cap!.Set(CapProp.PosFrames, frameIndex);

                using var mat = new Mat();
                cap.Read(mat);

                if (!mat.IsEmpty && wb is not null)
                {
                    ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb);
                }

                if (leftTrueRightFalse)
                {
                    _currentFrameLeft = frameIndex;
                }
                else
                {
                    _currentFrameRight = frameIndex;
                }
            }
        }

        private void ProcessFrame(bool leftTrueRightFalse, int frameIndex, Mat frame, WriteableBitmap wb)
        {
            if (appMode == AppMode.Open)
            {
                try
                {
                    //???DetectAndCreateFrameCalibrationTarget(leftTrueRightFalse, frameIndex, frame, dictionary5x5_100!, board5x5_100!, "5x5_100");

                    //???DrawMarkersToMat(leftTrueRightFalse, frameIndex, frame);

                    DrawFrameToScreen(frame, wb);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ProcessFrame: Error processing ChArUco board, appMode:{appMode}, {ex.Message}");
                }

                SetUIControls();
            }
            else if (appMode == AppMode.FindCalibrationsFrames)
            {
                DrawFrameToScreen(frame, wb);
            }
            else if (appMode == AppMode.BestFramesView)
            {
                try
                {
                    //???DrawMarkersToMat(leftTrueRightFalse, frameIndex, frame);

                    DrawFrameToScreen(frame, wb);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ProcessFrame: Error processing ChArUco board, appMode:{appMode}, {ex.Message}");
                }

                SetUIControls();
            }
            else if (appMode == AppMode.BestFramesCalc)

            {
                try
                {
                    //???DrawMarkersToMat(leftTrueRightFalse, frameIndex, frame);

                    DrawFrameToScreen(frame, wb);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ProcessFrame: Error processing ChArUco board, appMode:{appMode}, {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Draw an Emgu Mat into a WriteableBitmap (which is the Source for an Image element)
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="wb"></param>
        private void DrawFrameToScreen(Mat frame, WriteableBitmap wb)
        {
            if (frame.IsEmpty || wb == null) return;

            try
            {
                using var bgraFrame = new Mat();
                CvInvoke.CvtColor(frame, bgraFrame, Emgu.CV.CvEnum.ColorConversion.Bgr2Bgra);

                if (wb.PixelWidth != bgraFrame.Width || wb.PixelHeight != bgraFrame.Height)
                {
                    Debug.WriteLine($"Warning: Frame dimensions {bgraFrame.Width}x{bgraFrame.Height} " +
                                    $"don't match WriteableBitmap {wb.PixelWidth}x{wb.PixelHeight}");
                    return;
                }

                int byteCount = bgraFrame.Rows * bgraFrame.Cols * bgraFrame.ElementSize;
                byte[] buffer = new byte[byteCount];

                // Copy from native memory to managed buffer
                Marshal.Copy(bgraFrame.DataPointer, buffer, 0, buffer.Length);

                using var stream = wb.PixelBuffer.AsStream();
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(buffer, 0, buffer.Length);
                wb.Invalidate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DrawFrame: Error drawing frame: {ex.Message}");
            }
        }


        /// <summary>
        /// Update the left or right frame label
        /// </summary>
        private void LeftUpdateFrameLabel()
        {
            int targetIndex = -1;
            int totalFrames = -1;
            if (appMode == AppMode.Open)
            {
                targetIndex = _currentFrameLeft;
                totalFrames = _totalFramesLeft;
            }
            else if (appMode == AppMode.BestFramesView)
            {
                targetIndex = _currentBestFrame;
                totalFrames = calibrationStereoFrameSet.BestFrameIndexes.Count;
            }

            _UpdateFrameLabel(LeftFrameInfoLabel, capLeft, targetIndex, totalFrames);
            LeftFrameInfoTextBox.Text = $"{_currentFrameLeft}";
        }
        private void RightUpdateFrameLabel()
        {
            int targetIndex = -1;
            int totalFrames = -1;
            if (appMode == AppMode.Open)
            {
                targetIndex = _currentFrameRight;
                totalFrames = _totalFramesLeft;
            }
            else if (appMode == AppMode.BestFramesView)
            {
                targetIndex = _currentBestFrame;
                totalFrames = calibrationStereoFrameSet.BestFrameIndexes.Count;
            }

            _UpdateFrameLabel(RightFrameInfoLabel, capRight, targetIndex, totalFrames);
            RightFrameInfoTextBox.Text = $"{_currentFrameRight}";
        }
        private void _UpdateFrameLabel(TextBlock textBlock, VideoCapture? cap, int currentFrame, int totalFrames)
        {
            if (cap is not null)
            {
                if (appMode == AppMode.Open || appMode == AppMode.BestFramesView)
                {
                    string frameText = string.Empty;

                    if (totalFrames == -1 || totalFrames == 0)
                    {
                        double time = cap.Get(CapProp.PosMsec) / 1000.0;
                        frameText = $"Frame {currentFrame}, Time {time:F2}s";
                    }
                    else
                    {
                        double time = cap.Get(CapProp.PosMsec) / 1000.0;
                        frameText = $"Frame {currentFrame} / {totalFrames}, Time {time:F2}s";
                    }

                    textBlock.Text = frameText;
                }
                else
                    textBlock.Text = string.Empty;
            }
        }





        /// <summary>
        /// Get the path to save the calibration frame set file
        /// </summary>
        /// <param name="originalPath"></param>
        /// <returns></returns>
        public static string MakeCalibrationStereoFrameSetPath(string leftMediaFileSpec, string rightMediaFileSpec)
        {
            // Extract the filename without extension
            string baseName = string.Empty;
            if (leftMediaFileSpec != string.Empty && rightMediaFileSpec != string.Empty)
            {
                baseName = Path.GetFileNameWithoutExtension(leftMediaFileSpec) + "_" + Path.GetFileNameWithoutExtension(rightMediaFileSpec);
            }
            else if (leftMediaFileSpec != string.Empty)
            {
                baseName = Path.GetFileNameWithoutExtension(leftMediaFileSpec);
            }

            if (!string.IsNullOrEmpty(baseName))
            {
                // Build new filename
                string filename = $"{baseName}-CalibrationStereoFrameSet.json";

                // Get local folder path
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;

                // Combine into full path
                string fullPath = Path.Combine(localFolder.Path, filename);

                return fullPath;
            }
            else
                return string.Empty;
        }


        /// <summary>
        /// Check and delete zero byte files
        /// </summary>
        /// <param name="filePath"></param>
        private static void DeleteIfZeroByteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length == 0)
                    {
                        File.Delete(filePath);
                        Debug.WriteLine($"Deleted zero-byte file: {filePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking/deleting file: {ex.Message}");
            }
        }


        public void ReportOnLargeValues()
        {
            // Check for large values in the left calibration frame set
            if (calibrationStereoFrameSet.Frames.Count > 0)
            {
                calibrationStereoFrameSet.ReportOnLargeValues(true/*left*/, true/*suppress value*/);
            }
        }



        /// <summary>
        /// Make the folder name to save the frames to, create the folder if necessary)
        /// </summary>
        /// <param name="fileSpecMP4"></param>
        /// <returns></returns>
        private static string MakeAndCreateFramesDirectory(string basePath, string fileSpecMP4, bool trueRelativePathFalseAbsolute)
        {
            string outputFolder;

            // Make an output folder in the local folder (if necessary) based on the video name 
            string subfolderName = Path.GetFileNameWithoutExtension(fileSpecMP4);

            outputFolder = Path.Combine(basePath, subfolderName);

            if (!Directory.Exists(outputFolder))
            {
                // Create a folder
                try
                {
                    Directory.CreateDirectory(outputFolder);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MakeAndCreateFramesDirectory: Error creating save frame storage folder call: [{subfolderName}] inside: [{outputFolder}], {ex.Message}");
                }
            }

            return outputFolder;
        }


        /// <summary>
        /// Save the best frames to a folder
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="bestFrames"></param>
        /// <param name="calibrationFrameSet"></param>
        /// <param name="wb"></param>
        /// <param name="fileSpecMP4"></param>
        /// <returns></returns>
        private async Task<bool> SaveBestFiles()
        {
            bool ret = false;

            string documentsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SurveyorCalibration");

            string baseName = string.Empty;
            if (leftMediaFileSpec != string.Empty && rightMediaFileSpec != string.Empty)
            {
                baseName = Path.GetFileNameWithoutExtension(leftMediaFileSpec) + "_" + Path.GetFileNameWithoutExtension(rightMediaFileSpec);
            }
            else if (leftMediaFileSpec != string.Empty)
            {
                baseName = Path.GetFileNameWithoutExtension(leftMediaFileSpec);
            }

            // Create a folder (need the full path for this)
            string imageOutputSubFolder = MakeAndCreateFramesDirectoryAndEmpty(documentsFolder, baseName);


            // Local helper to create/empty folder
            static string MakeAndCreateFramesDirectoryAndEmpty(string path, string mediaFileSpec)
            {
                string outputPath = MakeAndCreateFramesDirectory(path, mediaFileSpec, false);

                if (!string.IsNullOrEmpty(outputPath))
                {
                    // Ensure those folder are empty
                    foreach (var file in Directory.GetFiles(outputPath))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            // Optional: handle or log the exception
                            Console.WriteLine($"Failed to delete {file}: {ex.Message}");
                        }
                    }
                }

                return outputPath;
            }

            if (imageOutputSubFolder is not null && wbLeft is not null) // at least need the left side
            {
                // Loop through the best frames and save them (need the relative path for this)
                foreach (int frameIndex in calibrationStereoFrameSet.BestFrameIndexes)
                {

                    try
                    {
                        (FrameCalibrationTarget leftTarget, FrameCalibrationTarget? rightTarget) = calibrationStereoFrameSet.Frames[frameIndex];

                        // Force the frame with MoveJump
                        _JumpFrame(true/*trueLeftFalseRight*/, leftTarget.FrameIndex);

                        if (rightTarget is not null)
                            _JumpFrame(false/*trueLeftFalseRight*/, rightTarget.FrameIndex);

                        await Task.Delay(100);

                        // Make left image file name
                        string videoName = Path.GetFileNameWithoutExtension(leftMediaFileSpec);
                        string frameFileName = $"{videoName}_{frameIndex}.png";

                        // Save the left image file
                        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(imageOutputSubFolder);
                        StorageFile file = await folder.CreateFileAsync(frameFileName, CreationCollisionOption.ReplaceExisting);
                        await SaveWriteableBitmapToFile(wbLeft, file);
                        Debug.WriteLine($"SaveBestFiles: Left Frame saved: [{file.Path}]");

                        if (rightTarget is not null && wbRight is not null)
                        {
                            // Make right image file name
                            videoName = Path.GetFileNameWithoutExtension(rightMediaFileSpec);
                            frameFileName = $"{videoName}_{frameIndex}.png";

                            // Save the left image file
                            folder = await StorageFolder.GetFolderFromPathAsync(imageOutputSubFolder);
                            file = await folder.CreateFileAsync(frameFileName, CreationCollisionOption.ReplaceExisting);
                            await SaveWriteableBitmapToFile(wbRight, file);
                            Debug.WriteLine($"SaveBestFiles: Right Frame saved: [{file.Path}]");
                        }

                        ret = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SaveBestFiles: Error saving frame {frameIndex} to path:[{imageOutputSubFolder}], {ex.Message}");
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Write the WriteableBitmap to file
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="file"></param>
        /// <returns></returns>
        public static async Task SaveWriteableBitmapToFile(WriteableBitmap bitmap, StorageFile file)
        {
            // Get the pixel buffer from the WriteableBitmap
            using (var stream = new InMemoryRandomAccessStream())
            {
                // Encode the WriteableBitmap to a stream
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                var pixelStream = bitmap.PixelBuffer.AsStream();
                var pixels = new byte[pixelStream.Length];
                await pixelStream.ReadAsync(pixels, 0, pixels.Length);

                // Set the pixel data to the encoder
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth,
                    (uint)bitmap.PixelHeight,
                    96.0,  // Default DPI for WinUI
                    96.0,
                    pixels);

                await encoder.FlushAsync();

                // Save the stream to a file
                using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    await RandomAccessStream.CopyAndCloseAsync(stream.GetInputStreamAt(0), fileStream.GetOutputStreamAt(0));
                }
            }
        }




        /// <summary>
        /// Set the UI controls based on the current application mode and media state.
        /// </summary>
        private void SetUIControls()
        {
            if (appMode == AppMode.Open || appMode == AppMode.BestFramesView)
            {
                int totalRecordCount = calibrationStereoFrameSet.Frames.Count;

                // Load Button - Always enabled

                // Save Button
                //???SaveButton.IsEnabled = totalRecordCount > 0 ? true : false;

                // Lock/Unlock Button            
                LockUnlockButton.IsEnabled = (capLeft?.IsOpened == true) && (capRight?.IsOpened == true);

                // Best Frames
                //???BestFrames.IsEnabled = totalRecordCount > 0 ? true : false;


                //???OpenButton.IsEnabled = true;
                //???SaveButton.IsEnabled = true;
                LockUnlockButton.IsEnabled = true;
                //???SearchCalibrationBoard.IsEnabled = true;
                //???BestFrames.IsEnabled = true;
                //???Cancel.IsEnabled = false;
            }
            else if (appMode == AppMode.FindCalibrationsFrames || appMode == AppMode.BestFramesCalc)
            {
                //???OpenButton.IsEnabled = false;
                //???SaveButton.IsEnabled = false;
                LockUnlockButton.IsEnabled = false;
                //???BestFrames.IsEnabled = false;
                //???SearchCalibrationBoard.IsEnabled = false;
                //???Cancel.IsEnabled = true;
            }

            SetUISubControls(true/*trueLeftfalseRight*/);
            SetUISubControls(false/*trueLeftfalseRight*/);

        }

        private void SetUISubControls(bool trueLeftfalseRight)
        {
            bool leftFrameBackButtonIsEnabled = true;
            bool leftPlayPauseButtonIsEnabled = true;
            bool leftFrameForwardButtonIsEnabled = true;
            bool rightFrameBackButtonIsEnabled = true;
            bool rightPlayPauseButtonIsEnabled = true;
            bool rightFrameForwardButtonsEnabled = true;
            bool leftFrameInfoTextBoxIsVisable = true;
            bool rightFrameInfoTextBoxIsVisable = true;

            if (appMode == AppMode.Open || appMode == AppMode.BestFramesView)
            {


            }
            else if (appMode == AppMode.FindCalibrationsFrames || appMode == AppMode.BestFramesCalc)
            {
                if (trueLeftfalseRight)
                {
                    leftFrameBackButtonIsEnabled = false;
                    leftPlayPauseButtonIsEnabled = false;
                    leftFrameForwardButtonIsEnabled = false;
                    leftFrameInfoTextBoxIsVisable = false;
                }
                else
                {
                    rightFrameBackButtonIsEnabled = false;
                    rightPlayPauseButtonIsEnabled = false;
                    rightFrameForwardButtonsEnabled = false;
                    rightFrameInfoTextBoxIsVisable = false;
                }

                // Clear the metadata display fields
                if (trueLeftfalseRight)
                {
                    LeftMovementFactor.Text = string.Empty;
                    LeftBlurFactor.Text = string.Empty;
                    LeftFeatureCount.Text = string.Empty;
                    LeftScore.Text = string.Empty;
                }
                else
                {
                    RightMovementFactor.Text = string.Empty;
                    RightBlurFactor.Text = string.Empty;
                    RightFeatureCount.Text = string.Empty;
                    RightScore.Text = string.Empty;
                }

            }

            if (trueLeftfalseRight)
            {
                LeftFrameBackButton.IsEnabled = leftFrameBackButtonIsEnabled;
                LeftPlayPauseButton.IsEnabled = leftPlayPauseButtonIsEnabled;
                LeftFrameForwardButton.IsEnabled = leftFrameForwardButtonIsEnabled;
                LeftFrameInfoTextBox.Visibility = leftFrameInfoTextBoxIsVisable ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                RightFrameBackButton.IsEnabled = rightFrameBackButtonIsEnabled;
                RightPlayPauseButton.IsEnabled = rightPlayPauseButtonIsEnabled;
                RightFrameForwardButton.IsEnabled = rightFrameForwardButtonsEnabled;
                RightFrameInfoTextBox.Visibility = rightFrameInfoTextBoxIsVisable ? Visibility.Visible : Visibility.Collapsed;
            }

        }

    }
}
