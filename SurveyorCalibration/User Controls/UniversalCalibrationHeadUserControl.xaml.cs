using Emgu.CV;
using Emgu.CV.CvEnum;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Org.BouncyCastle.Bcpg;
using Surveyor.Calibration;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using static Surveyor.Controls.UniversalCalibrationHeadUserControl;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace Surveyor.Controls
{
    public sealed partial class UniversalCalibrationHeadUserControl : UserControl
    {
        // Remembered Head property
        // Needed because background thread can't access UI elements
        // Including the Head property
        private bool? headTrueIsStereoFalseIsMode = null;

        // Reporter
        private Reporter? report = null;

        // Media file files and handles
        private string leftMediaFileSpec = string.Empty;
        private string rightMediaFileSpec = string.Empty;
        private VideoCapture? capLeft = null;
        private VideoCapture? capRight = null;

        // Writeable bitmaps for the left and right camera frames
        private Size frameSize = new(0.0, 0.0); // Size of the frames, used to create WriteableBitmaps
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
        private readonly DispatcherTimer _playLeftTimer;
        private bool _isRightPlaying = false;
        private readonly DispatcherTimer _playRightTimer;
        private bool _isBothPlaying = false;
        private readonly DispatcherTimer _playBothTimer;

        // Goto start/end button double click support
       
        // Single-click handlers become async de-bounced actions:
        private CancellationTokenSource? _leftGotoStartCts;
        private CancellationTokenSource? _leftGotoEndCts;
        private CancellationTokenSource? _rightGotoStartCts;
        private CancellationTokenSource? _rightGotoEndCts;

        // Stereo lock state
        private bool isLocked = false;

        // Frame set
        private readonly CalibrationStereoFrameSet calibrationStereoFrameSet;

        private CancellationToken cancellationToken;
        private CancellationTokenSource? cts = null;

        private bool isFindCalibrationFrameRunning = false;

        private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

        private readonly SafeUICall safeUICall;

        public enum AppMode { Close, Open, FindCalibrationsFrames, BestFramesCalc, BestFramesView, BestFramesSave };
        private AppMode _appMode = AppMode.Open;
        public AppMode AppModeCurrent
        {
            get => _appMode;
            set
            {
                if (_appMode != value)
                {
                    _appMode = value;
                }
            }
        }

        public enum ViewMode { AllFrames, BestFrames, FilterFrames, SensorCoverage };
        private ViewMode _viewMode = ViewMode.AllFrames;
        public ViewMode ViewModeCurrent
        {
            get => _viewMode;
            set
            {
                if (_viewMode != value)
                {
                    _viewMode = value;
                }
            }
        }

        // Expose the play/pause button for external binding teaching tip (read-only)
        public Button LeftPlayPauseButtonElement => LeftPlayPauseButton;


        // XAML Attribute to indicate is the head is mono or stereo
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
                ctrl.ApplyHeadMode((string)e.NewValue);
            }
        }

        private void ApplyHeadMode(string mode)
        {
            switch (mode.ToLowerInvariant())
            {
                case "mono":
                    // Hide column 1
                    RootGrid.ColumnDefinitions[1].Width = new GridLength(0);
                    headTrueIsStereoFalseIsMode = false;
                    break;

                case "stereo":
                    // Show Column 1
                    RootGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    headTrueIsStereoFalseIsMode = true;
                    break;

                default:
                    throw new InvalidOperationException($"Unknown Head mode: {mode}");
            }
        }

        // New XAML attribute to title the head (propagates to child viewers)
        public static readonly DependencyProperty HeadTitleProperty =
            DependencyProperty.Register(nameof(HeadTitle), typeof(string), typeof(UniversalCalibrationHeadUserControl),
                new PropertyMetadata(string.Empty, OnHeadTitleChanged));

        public string HeadTitle
        {
            get => (string)GetValue(HeadTitleProperty);
            set => SetValue(HeadTitleProperty, value);
        }

        private static void OnHeadTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UniversalCalibrationHeadUserControl ctrl)
            {
                ctrl.ApplyHeadTitle();
            }
        }

        private void ApplyHeadTitle()
        {
            string suffix = HeadTitle ?? string.Empty;
            // Prefix with Left/Right as requested
            if (Head.Equals("stereo", StringComparison.InvariantCultureIgnoreCase))
            {
                CalibrationFrameSetViewerLeft?.SetTitle("Left " + suffix);
                CalibrationFrameSetViewerRight?.SetTitle("Right " + suffix);
            }
            else
            {
                CalibrationFrameSetViewerLeft?.SetTitle(suffix);
            }
        }

        public UniversalCalibrationHeadUserControl()
        {
            // Get the DispatcherQueue for the current thread
            dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            safeUICall = new(dispatcherQueue);



            this.InitializeComponent();

            // After InitializeComponent(), attach DoubleTapped handlers
            LeftGotoStartButton.DoubleTapped += LeftGotoStartButton_DoubleTapped;
            LeftGotoEndButton.DoubleTapped += LeftGotoEndButton_DoubleTapped;
            RightGotoStartButton.DoubleTapped += RightGotoStartButton_DoubleTapped;
            RightGotoEndButton.DoubleTapped += RightGotoEndButton_DoubleTapped;

            // Set the CalibrationStereoFrameSet
            calibrationStereoFrameSet = new();

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
                ApplyHeadMode(Head);
                ApplyHeadTitle();
            };

            this.Unloaded += StereoCalibrationHeadUserControl_Unloaded;

            SetAppMode(AppMode.Close);
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
        /// Set the theme of the application
        /// </summary>
        /// <param name="theme">Dark or Light</param>
        public void SetTheme(ElementTheme theme)
        {

            if (theme == ElementTheme.Dark)
            {
                SetMetadataLabels(theme);
            }
            else if (theme == ElementTheme.Light)
            {
                SetMetadataLabels(theme);
            }
            else
            {
                // Throw unexpected exception
                throw new InvalidOperationException($"Unexpected theme value: {theme}");
            }
        }


        /// <summary>
        /// Set up the calibration board type for the stereo camera calibration.
        /// The board name is just for reporting
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
        public bool SetupCalibrationBoardType(CalibrationBoardDefinition _chArUcoBoardDefinition)
        {
            return calibrationStereoFrameSet.SetupCalibrationBoardType(_chArUcoBoardDefinition);
        }

        /// <summary>
        /// Open the media files for the left and right cameras.
        /// </summary>
        /// <param name="leftFileSpec"></param>
        /// <param name="rightFileSpec">Set to string.Empty is Mono</param>
        /// <returns></returns>
        public async Task<bool> OpenMediaAsync(string _leftMediaFileSpec, string _rightMediaFileSpec)
        {
            bool ret = false;
            bool leftOpened = false;
            bool? rightOpened = null;

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
                        // Create WriteableBitmap with EMGU.CV frame dimensions
                        wbLeft = new WriteableBitmap(testFrame.Width, testFrame.Height);
                        frameSize = new Size(testFrame.Width, testFrame.Height);

                        // Reset to first frame — EMGU.CV uses .Set() with CapProp
                        capLeft.Set(Emgu.CV.CvEnum.CapProp.PosFrames, 0);

                        LeftImage.Source = wbLeft;
                        _currentFrameLeft = 0;

                        // Setup the timeline ranges indicators that show where calibration
                        // boards have been found
                        CalibrationBoardTimeLineLeft.Visibility = Visibility.Visible;
                        CalibrationBoardTimeLineLeft.SetRange(0, _totalFramesLeft);

                        // Display first frame
                        FrameJump(true/*leftTrueRightFalse*/, 0);

                        leftOpened = true;
                    }
                }
            }
            else
            {
                if (headTrueIsStereoFalseIsMode == true)
                    Debug.WriteLine($"OpenMedia: Left stereo media {_leftMediaFileSpec} does not exist.");
                else
                    Debug.WriteLine($"OpenMedia: Media {_leftMediaFileSpec} does not exist.");
            }

            // If Stereo open right side
            if (headTrueIsStereoFalseIsMode == true)
            {
                rightOpened = false;

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
                            // Create WriteableBitmap with EMGU.CV frame dimensions
                            wbRight = new WriteableBitmap(testFrame.Width, testFrame.Height);

                            // Reset to first frame — EMGU.CV uses .Set() with CapProp
                            capRight.Set(Emgu.CV.CvEnum.CapProp.PosFrames, 0);

                            RightImage.Source = wbRight;
                            _currentFrameRight = 0;

                            // Setup the timeline ranges indicators that show where calibration
                            // boards have been found
                            CalibrationBoardTimeLineRight.SetRange(0, _totalFramesRight);
                            CalibrationBoardTimeLineRight.Visibility = Visibility.Visible;

                            // Display first frame
                            FrameJump(false/*leftTrueRightFalse*/, 0);

                            rightOpened = true;
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
                if (headTrueIsStereoFalseIsMode == true)
                    calibrationStereoFrameSet.SetupMediaStereo(capLeft, capRight);
                else
                    calibrationStereoFrameSet.SetupMediaMono(capLeft);
            }

            await Task.Delay(100); // Allow UI to update

            SetUIControls();

            if (leftOpened && (rightOpened is null || rightOpened == true))
            {
                ret = true;
            }

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

            // Clear frame set values
            this.calibrationStereoFrameSet.ClearResults();
            CalibrationFrameSetViewerLeft.HighLightActiveSensorBin(null);
            CalibrationFrameSetViewerLeft.RefreshSensorBin(ViewModeCurrent);
            CalibrationFrameSetViewerLeft.HighLightActivePoseBin(null);
            CalibrationFrameSetViewerLeft.RefreshPoseBin(ViewModeCurrent);
            CalibrationFrameSetViewerLeft.DrawGraphs();
            CalibrationFrameSetViewerRight.HighLightActiveSensorBin(null);
            CalibrationFrameSetViewerRight.RefreshSensorBin(ViewModeCurrent);
            CalibrationFrameSetViewerRight.HighLightActivePoseBin(null);
            CalibrationFrameSetViewerRight.RefreshPoseBin(ViewModeCurrent);
            CalibrationFrameSetViewerRight.DrawGraphs();

            // Clear images
            LeftImage.Source = null;
            _currentFrameLeft = 0;
            RightImage.Source = null;
            _currentFrameRight = 0;

            // Clear frame UI display data
            DecorateClear(true/*trueLeftfalseRight*/);
            DecorateClear(false/*trueLeftfalseRight*/);

            // Reset calibration output display 
            LeftCalibDataText.Text = string.Empty;
            LeftCalibDataBorder.Visibility = Visibility.Collapsed;
            RightCalibDataText.Text = string.Empty;
            RightCalibDataBorder.Visibility = Visibility.Collapsed;

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

            if (headTrueIsStereoFalseIsMode == true)
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
        /// Return the current frame width and height
        /// </summary>
        /// <returns></returns>
        public (int frameWidth, int frameHeight) GetFrameSize()
        {
            return ((int)frameSize.Width, (int)frameSize.Height);
        }


        /// <summary>
        /// Get the current player frame indexes. frameIndexRight will return null 
        /// if the head is Mono
        /// </summary>
        /// <returns></returns>
        public (int frameIndexLeft, int frameIndexRight) GetCurrentFrameIndexes()
        {
            return (_currentFrameLeft, _currentFrameRight);
        }


        /// <summary>
        /// Lock the stereo media lock state at the given frame indexes.
        /// </summary>
        /// <param name="syncFrameIndexLeft"></param>
        /// <param name="syncFrameIndexRight"></param>
        /// <returns></returns>
        public bool LockStereo(int syncFrameIndexLeft, int syncFrameIndexRight)
        {
            bool ret = true;

            if (!isLocked)
            {
                calibrationStereoFrameSet.SetupLockFrameIndexes(syncFrameIndexLeft, syncFrameIndexRight);
                isLocked = true;
            }
            else
            {
                // throw exception - already locked
                Debug.Assert(true, "LockStero should not be called to unlock the media");
            }

            return ret;
        }


        /// <summary>
        /// Unlock the stereo media lock state.
        /// </summary>
        public void UnlockStereo()
        {
            if (isLocked)
            {
                calibrationStereoFrameSet.SetupLockFrameIndexes(-1, -1);
                isLocked = false;
            }
        }


        /// <summary>
        /// Check if the stereo head is locked or not.
        /// </summary>
        /// <returns>Null is Mono, True is Stereo and Locked</returns>
        public bool? IsStereoLocked()
        {
            if (headTrueIsStereoFalseIsMode == true)
            {
                return isLocked;
            }
            else
            {
                return null;  // Mono
            }
        }

        /// <summary>
        /// Return the largest movement in the calibration frames or the best frames.
        /// </summary>
        /// <param name="trueNormalFalseBestFrame"></param>
        /// <returns></returns>
        public double GetMaxMovement(bool trueNormalFalseBestFrame)
        {
            if (calibrationStereoFrameSet is not null)
            {
                if (trueNormalFalseBestFrame)
                {
                    return calibrationStereoFrameSet.MaxMovementFactor;
                }
                else
                {
                    return calibrationStereoFrameSet.MaxBestMovementFactor;
                }
            }
            return -1;
        }


        /// <summary>
        /// Return the smallest movement in the calibration frames or the best frames.
        /// </summary>
        /// <param name="trueNormalFalseBestFrame"></param>
        /// <returns></returns>
        public double GetMinMovement(bool trueNormalFalseBestFrame)
        {
            if (calibrationStereoFrameSet is not null)
            {
                if (trueNormalFalseBestFrame)
                {
                    return calibrationStereoFrameSet.MinMovementFactor;
                }
                else
                {
                    return calibrationStereoFrameSet.MinBestMovementFactor;
                }
            }
            return -1;
        }


        /// <summary>
        /// Return the largest blur in the calibration frames or the best frames.
        /// </summary>
        /// <param name="trueNormalFalseBestFrame"></param>
        /// <returns></returns>
        public double GetMaxBlur(bool trueNormalFalseBestFrame)
        {
            if (calibrationStereoFrameSet is not null)
            {
                if (trueNormalFalseBestFrame)
                {
                    return calibrationStereoFrameSet.MaxBlurFactor;
                }
                else
                {
                    return calibrationStereoFrameSet.MaxBlurFactor; // calibrationStereoFrameSet.MaxBestBlurFactor;  not yet implemented
                }
            }
            return -1;
        }


        /// <summary>
        /// Return the largest blur in the calibration frames or the best frames.
        /// </summary>
        /// <param name="trueNormalFalseBestFrame"></param>
        /// <returns></returns>
        public double GetMinBlur(bool trueNormalFalseBestFrame)
        {
            if (calibrationStereoFrameSet is not null)
            {
                if (trueNormalFalseBestFrame)
                {
                    return calibrationStereoFrameSet.MinBlurFactor;
                }
                else
                {
                    return calibrationStereoFrameSet.MinBlurFactor; // calibrationStereoFrameSet.MaxBestBlurFactor;  not yet implemented
                }
            }
            return -1;
        }


        /// <summary>
        /// Find the start and end of the calibration board zone for this head
        /// This is the first and last time the calibration boards is seen in 
        /// the .MP4
        /// </summary>
        /// <returns></returns>
        public async Task<int> FindCalibrationBoardZoneAsync()
        {
            int ret = 0;

            try
            {
                isFindCalibrationFrameRunning = true;
                SetUIControls();

                cts = new CancellationTokenSource();
                cancellationToken = cts.Token;

                // Reset any previous calibration board timeline ranges
                CalibrationBoardTimeLineLeft.Clear();
                CalibrationBoardTimeLineRight.Clear();

                // Move both methods to background threads
                var (startCalibration, stopCalibration) = await Task.Run(() =>
                    calibrationStereoFrameSet.FindCalibrationBoardZoneAsync(FrameProcessingCallbackFindCalibrationTimeLineRange, 
                                                                            cancellationToken));

                if (startCalibration != -1 && stopCalibration != -1)
                {
                    // Update the timeline ranges
                    CalibrationBoardTimeLineLeft.CalibrationBoardRange(startCalibration, stopCalibration);
                    CalibrationBoardTimeLineRight.CalibrationBoardRange(startCalibration, stopCalibration);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Calibration search canceled.");
                ret = -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during calibration board zone search: {ex.Message}");
            }
            finally
            {
                SetUIControls();
            }

            return ret;
        }


        /// <summary>
        /// Search the media for the calibration boards
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public async Task<int> BuildFrameSetsAsync()
        {
            int ret = 0;

            try
            {
                isFindCalibrationFrameRunning = true;
                SetUIControls();

                cts = new CancellationTokenSource();
                cancellationToken = cts.Token;

                int startCalibrationBoardZone = calibrationStereoFrameSet.GetStartCalibrationBoardZone();
                int stopCalibrationBoardZone = calibrationStereoFrameSet.GetStopCalibrationBoardZone();

                if (startCalibrationBoardZone != -1 && stopCalibrationBoardZone != -1)
                {
                    // Update the timeline ranges
                    CalibrationBoardTimeLineLeft.CalibrationBoardRange(startCalibrationBoardZone, stopCalibrationBoardZone);
                    CalibrationBoardTimeLineRight.CalibrationBoardRange(startCalibrationBoardZone, stopCalibrationBoardZone);

                    // Next find the calibration frames with in that range
                    ret = await Task.Run(async () =>
                    {

                        try
                        {
                            return await calibrationStereoFrameSet.FindCalibrationsFramesAsync(
                                            startCalibrationBoardZone,
                                            stopCalibrationBoardZone,
                                            FrameProcessingCallbackFindCalibrationsFrames,
                                            cancellationToken);
                        }
                        finally
                        {
                            isFindCalibrationFrameRunning = false;
                        }
                    });

                    if (ret == -1)
                        Debug.WriteLine("BuildFrameSetsAsync: User canceled.");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Calibration search canceled.");
                ret = -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during calibration search: {ex.Message}");
            }
            finally
            {
                SetUIControls();
            }

            return ret;
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
        /// ** Safe to call from a background thread **
        /// ** All UI via SafeUICall **
        /// Extract the best frames and do a mono calibration.
        /// If it is called from a Stereo head both left and right are mono calibrated and the result 
        /// reported on screen.  However only the left MonoCalibrationCameraData array is returned
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public async Task<int> FindBestMonoFramesNoUIAsync(CalibProject calibProject,
                                                           bool trueLeftFalseRight,
                                                           double movementMinThreshold,
                                                           double blurMinThreshold,
                                                           int monoCornersMinThreshold,
                                                           int maxFramesFromEachSensorBin,
                                                           int maxFramesFromEachPoseBin)
        {
            int ret = 0;

            // Check we have a CalibrationStereoFrameSet and this is definitely a Mono head
            if (calibrationStereoFrameSet is not null && headTrueIsStereoFalseIsMode == false)
            {
                try
                {
                    // Create a list of the best calibration frames best on the sensor bin only
                    if (trueLeftFalseRight)
                        Debug.WriteLine($"Mono Left SelectBestStereoFramesUsingSensorBinOnly," +
                                        $" Min move={movementMinThreshold}, Min blur={blurMinThreshold}," +
                                        $" Corners threshold={monoCornersMinThreshold}:");
                    else
                        Debug.WriteLine($"Mono Right SelectBestStereoFramesUsingSensorBinOnly, " +
                                        $"Min move={movementMinThreshold}, Min blur={blurMinThreshold}, " +
                                        $"Corners threshold={monoCornersMinThreshold}:");

                    calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(movementMinThreshold,
                                                                                       blurMinThreshold,
                                                                                       monoCornersMinThreshold,
                                                                                       maxFramesFromEachSensorBin);

                    // Next top-up with pose diverse frames
                    calibrationStereoFrameSet.AddBestFramesUsingPoseBins(movementMinThreshold,
                                                                         blurMinThreshold,
                                                                         monoCornersMinThreshold,
                                                                         maxFramesFromEachPoseBin);

                    // Temp mono calibration to get yaw and pitch for each frame
                    // Calibration using the best frames (pass1 calibration using K1,K2,P1,P2)
                    // This is used to calculate the yaw and pitch of each frame and isn't
                    // reused for the ultimate mono calibration
                    MonoCalibrationCameraData? monoCalib = calibrationStereoFrameSet.MonoCalibrateUsingBestFrames(
                                                                                            false/*trueStereoFalseMono*/,
                                                                                            trueLeftFalseRight,
                                                                                            frameSize,
                                                                                            monoCornersMinThreshold,
                                                                                            CalibrationParameters.K1K2P1P2);

                    // Check we have suitable calibration data to proceed
                    if (monoCalib is not null)
                    {
                        // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                        await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(monoCalib!, null/*monoCalibRight*/, frameSize);
                        safeUICall.Call(() => CalibrationFrameSetViewerLeft.RefreshPoseBin(_viewMode));
                    }
                    else
                        ret = -1;

                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FindBestMonoFramesNoUIAsync: Error during best frames extraction: {ex.Message}");
                }
            }

            safeUICall.Call(() => BestFrameJump(0));
            safeUICall.Call(() => UpdateFrameLabel(true/*trueLeftFalseRight*/));

            return ret;
        }


        /// <summary>
        /// ** Safe to call from a background thread **
        /// ** All UI via SafeUICall **
        /// Mono calibration using the best frames already selected.
        /// If it is called from a Stereo head both left and right are mono calibrated and the result 
        /// reported on screen.  However only the left MonoCalibrationCameraData array is returned
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns>Zero is OK</returns>
        public int DoMonoCalibrationCalculationNoUI(CalibProject calibProject,
                                                    bool trueLeftFalseRight,
                                                    int monoCornersMinThreshold)
        {
            int ret = 0;

            // Check we have a CalibrationStereoFrameSet and this is definitely a Mono head
            if (calibrationStereoFrameSet is not null && headTrueIsStereoFalseIsMode == false)
            {
                try
                {
                    // Proceed to do the mono calibration using each the calibration parameter set
                    foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                    {
                        // Calibration using the best frames (pass2 calibration)                    
                        MonoCalibrationCameraData? monoCalib2 = calibrationStereoFrameSet.MonoCalibrateUsingBestFrames(
                                                                                false/*trueStereoFalseMono*/,
                                                                                trueLeftFalseRight,
                                                                                frameSize,
                                                                                monoCornersMinThreshold,
                                                                                calibrationParameters);

                        if (monoCalib2 is not null)
                        {
                            // Remember the mono calibration data
                            if (trueLeftFalseRight)
                                calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters] = monoCalib2;
                            else
                                calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters] = monoCalib2;
                        }
                        else
                            ret = -1;
                    }


                    // Display the mono calibration results
                    // Reset calibration output display 
                    // Note. We used the left side display control only for a mono head
                    // even if 'trueLeftFalseRight == false'
                    safeUICall.Call(() => LeftCalibDataText.Text = string.Empty);
                    safeUICall.Call(() => LeftCalibDataBorder.Visibility = Visibility.Collapsed);

                    string calibationText = $"Image Size {frameSize.Width} x {frameSize.Height}\n";

                    foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                    {
                        MonoCalibrationCameraData? monoCalibDisplay = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                        if (trueLeftFalseRight)
                            monoCalibDisplay = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                        else
                            monoCalibDisplay = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                        if (monoCalibDisplay is not null)
                        {
                            calibationText += "\n" + calibrationParameters.ToString() + "\n";
                            calibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(monoCalibDisplay);
                        }
                    }

                    // Set calibration output display 
                    safeUICall.Call(() => LeftCalibDataText.Text = calibationText);
                    safeUICall.Call(() => LeftCalibDataBorder.Visibility = Visibility.Visible);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during mono calibration calculation: {ex.Message}");
                }
            }

            safeUICall.Call(() => BestFrameJump(0));
            safeUICall.Call(() => UpdateFrameLabel(true/*trueLeftFalseRight*/));
            //???RightUpdateFrameLabel();

            return ret;
        }


        /// <summary>
        /// Find the best stereo frames 
        /// </summary>
        /// <param name="calibProject"></param>
        /// <param name="movementMinThreshold"></param>
        /// <param name="blurMinThreshold"></param>
        /// <param name="stereoCornersMinThreshold"></param>
        /// <param name="maxFramesFromEachSensorBin"></param>
        /// <param name="maxFramesFromEachPoseBin"></param>
        /// <returns></returns>
        public async Task<int> FindBestStereoFramesAsync(CalibProject calibProject,
                                                          double movementMinThreshold,
                                                          double blurMinThreshold,
                                                          int stereoCornersMinThreshold,
                                                          int maxFramesFromEachSensorBin,
                                                          int maxFramesFromEachPoseBin)
        {
            int ret = -1;

            SetAppMode(AppMode.BestFramesCalc);
            SetUIControls();

            if (calibrationStereoFrameSet is not null && headTrueIsStereoFalseIsMode == true)
            {
                // Proceed to do the stereo calibration using each calibration parameter 
                foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                {
                    Debug.WriteLine($"BestFramesCalcAndStereoCalibrationAsync: {calibrationParameters.ToString()}");
                    MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                    MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                    if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                    {
                        // Create a list of the best calibration frames best on the sensor bin only
                        calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(movementMinThreshold,
                                                                                           blurMinThreshold,
                                                                                           stereoCornersMinThreshold,
                                                                                           maxFramesFromEachSensorBin);

                        // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                        await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(leftMonoCalibrationCameraData,
                                                                                                       rightMonoCalibrationCameraData,
                                                                                                       frameSize);
                        // Next top-up with pose diverse frames
                        calibrationStereoFrameSet.AddBestFramesUsingPoseBins(movementMinThreshold,
                                                                             blurMinThreshold,
                                                                             stereoCornersMinThreshold,
                                                                             maxFramesFromEachPoseBin);

                        if (calibrationStereoFrameSet.Data.BestFrameIndexes.Count > 0)
                            ret = 0; // OK

                        CalibrationFrameSetViewerLeft.RefreshPoseBin(_viewMode);
                        CalibrationFrameSetViewerRight.RefreshPoseBin(_viewMode);

                    }
                }
            }

            BestFrameJump(0);
            UpdateFrameLabel(true/*trueLeftFalseRight*/);
            UpdateFrameLabel(false/*trueLeftFalseRight*/);
            SetUIControls();

            return ret;
        }


        /// <summary>
        /// Perform the stereo calibration calculation on all heads using the best frames
        /// </summary>
        /// <param name="runCalibrationParams"></param>
        /// <returns></returns>
        public int DoCalibrationStereoCalcs(CalibProject calibProject,
                                             int stereoCornersMinThreshold)
        {
            int ret = -1;

            SetAppMode(AppMode.BestFramesCalc);
            SetUIControls();

            if (calibrationStereoFrameSet is not null && headTrueIsStereoFalseIsMode == true)
            {
                // Calibration result string (start with the Image Size)
                string leftCalibationText = $"Image Size {frameSize.Width} x {frameSize.Height}\n";
                string rightCalibationText = $"Image Size {frameSize.Width} x {frameSize.Height}\n";

                // Proceed to do the stereo calibration using each calibration parameter 
                foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                {
                    Debug.WriteLine($"BestFramesCalcAndStereoCalibrationAsync: {calibrationParameters.ToString()}");
                    MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                    MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                    if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                    {

                        // Reset calibration output display 
                        LeftCalibDataText.Text = string.Empty;
                        LeftCalibDataBorder.Visibility = Visibility.Collapsed;
                        RightCalibDataText.Text = string.Empty;
                        RightCalibDataBorder.Visibility = Visibility.Collapsed;

                        // Calibration stereo calculations using the best frames
                        CalibrationStereoCameraData? calibrationStereoCameraData = calibrationStereoFrameSet.StereoCalibrateUsingBestFrames(
                                                                    frameSize,
                                                                    stereoCornersMinThreshold,
                                                                    leftMonoCalibrationCameraData,
                                                                    rightMonoCalibrationCameraData,
                                                                    calibrationParameters);


                        if (calibrationStereoCameraData is not null)
                        {
                            calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)calibrationParameters] = calibrationStereoCameraData;

                            // We need at least one working stereo calibration
                            ret = 0;
                        }

                        // Add the stereo calibration display text
                        if (calibrationStereoCameraData is not null)
                        {
                            leftCalibationText += "\n" + calibrationParameters.ToString() + ":\n";
                            leftCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(leftMonoCalibrationCameraData);
                            leftCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(calibrationStereoCameraData);
                            rightCalibationText += "\n" + calibrationParameters.ToString() + ":\n";
                            rightCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(rightMonoCalibrationCameraData);
                            rightCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(calibrationStereoCameraData);
                        }

                        // Set calibration output display 
                        LeftCalibDataText.Text = leftCalibationText;
                        LeftCalibDataBorder.Visibility = Visibility.Visible;
                        RightCalibDataText.Text = rightCalibationText;
                        RightCalibDataBorder.Visibility = Visibility.Visible;


                    }
                }
            }

            SetAppMode(AppMode.BestFramesView);
            BestFrameJump(0);
            UpdateFrameLabel(true/*trueLeftFalseRight*/);
            UpdateFrameLabel(false/*trueLeftFalseRight*/);
            SetUIControls();

            return ret;

        }


        /// <summary>
        /// Write the best frames on all heads out to separate .png files
        /// </summary>
        /// <param name="runCalibrationParams"></param>
        /// <returns></returns>
        public async Task<int> SaveBestFramesAsync()
        {
            int ret = -1;

            // Remember the current app mode
            AppMode appModeOld = AppModeCurrent;

            // Set the app mode to 
            SetAppMode(AppMode.BestFramesSave);

            // Get the app title name and make a folder in Documents
            string appTitle = AppInfo.Current.DisplayInfo.DisplayName;
            string documentsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), appTitle);

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
                            Debug.WriteLine($"Failed to delete {file}: {ex.Message}");
                        }
                    }
                }

                return outputPath;
            }

            if (imageOutputSubFolder is not null && wbLeft is not null) // at least need the left side
            {
                // Loop through the best frames and save them (need the relative path for this)
                foreach (int frameIndex in calibrationStereoFrameSet.Data.BestFrameIndexes)
                {

                    try
                    {
                        (FrameData leftTarget, FrameData? rightTarget, _) = calibrationStereoFrameSet.Data.Frames[frameIndex];

                        // Force the frame with MoveJump (without the calibration board markup)
                        _JumpFrame(true/*trueLeftFalseRight*/, leftTarget.FrameIndex, null, -1);

                        if (rightTarget is not null)
                            _JumpFrame(false/*trueLeftFalseRight*/, rightTarget.FrameIndex, null, -1);

                        await Task.Delay(100);

                        // Make left image file name
                        string videoName = Path.GetFileNameWithoutExtension(leftMediaFileSpec);
                        string frameFileName = $"{videoName}_{frameIndex}.png";

                        // Save the left image file
                        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(imageOutputSubFolder);
                        StorageFile file = await folder.CreateFileAsync(frameFileName, CreationCollisionOption.ReplaceExisting);
                        await SaveWriteableBitmapToFileAsync(wbLeft, file);
                        Debug.WriteLine($"SaveBestFiles: Left Frame saved: [{file.Path}]");

                        if (rightTarget is not null && wbRight is not null)
                        {
                            // Make right image file name
                            videoName = Path.GetFileNameWithoutExtension(rightMediaFileSpec);
                            frameFileName = $"{videoName}_{frameIndex}.png";

                            // Save the left image file
                            folder = await StorageFolder.GetFolderFromPathAsync(imageOutputSubFolder);
                            file = await folder.CreateFileAsync(frameFileName, CreationCollisionOption.ReplaceExisting);
                            await SaveWriteableBitmapToFileAsync(wbRight, file);
                            Debug.WriteLine($"SaveBestFiles: Right Frame saved: [{file.Path}]");
                        }

                        ret = 0;// OK
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SaveBestFiles: Error saving frame {frameIndex} to path:[{imageOutputSubFolder}], {ex.Message}");
                    }
                }
            }

            // Restore the original app mode
            SetAppMode(appModeOld);

            return ret;
        }


        /// <summary>
        /// Extract the best frames and do a stereo calibration.
        /// If it is called from a Stereo head both left and right are mono calibrated and the result 
        /// reported on screen.  However only the left MonoCalibrationCameraData array is returned
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public async Task<int> BestFramesCalcAndStereoCalibrationAsync(
                                         CalibProject calibProject,
                                         double movementMinThreshold,
                                         double blurMinThreshold,
                                         int stereoCornersMinThreshold,
                                         int maxFramesFromEachSensorBin,
                                         int maxFramesFromEachPoseBin,
                                         bool writeBestFramesToPng = true)
        {
            int ret = -1;

            SetAppMode(AppMode.BestFramesCalc);
            SetUIControls();

            if (calibrationStereoFrameSet is not null && headTrueIsStereoFalseIsMode == true)
            {
                // Calibration result string (start with the Image Size)
                string leftCalibationText = $"Image Size {frameSize.Width} x {frameSize.Height}\n";
                string rightCalibationText = $"Image Size {frameSize.Width} x {frameSize.Height}\n";

                // Proceed to do the stereo calibration using each calibration parameter 
                foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                {
                    Debug.WriteLine($"BestFramesCalcAndStereoCalibrationAsync: {calibrationParameters.ToString()}");
                    MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                    MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                    if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                    {
                        // Create a list of the best calibration frames best on the sensor bin only
                        calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(movementMinThreshold,
                                                                                           blurMinThreshold,
                                                                                           stereoCornersMinThreshold,
                                                                                           maxFramesFromEachSensorBin);

                        // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                        await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(leftMonoCalibrationCameraData,
                                                                                                       rightMonoCalibrationCameraData,
                                                                                                       frameSize);
                        // Next top-up with pose diverse frames
                        calibrationStereoFrameSet.AddBestFramesUsingPoseBins(movementMinThreshold,
                                                                             blurMinThreshold,
                                                                             stereoCornersMinThreshold,
                                                                             maxFramesFromEachPoseBin);

                        CalibrationFrameSetViewerLeft.RefreshPoseBin(_viewMode);
                        CalibrationFrameSetViewerRight.RefreshPoseBin(_viewMode);

                        // Reset calibration output display 
                        LeftCalibDataText.Text = string.Empty;
                        LeftCalibDataBorder.Visibility = Visibility.Collapsed;
                        RightCalibDataText.Text = string.Empty;
                        RightCalibDataBorder.Visibility = Visibility.Collapsed;

                        // Calibration stereo calculations using the best frames
                        CalibrationStereoCameraData? calibrationStereoCameraData = calibrationStereoFrameSet.StereoCalibrateUsingBestFrames(
                                                                    frameSize,
                                                                    stereoCornersMinThreshold,
                                                                    leftMonoCalibrationCameraData,
                                                                    rightMonoCalibrationCameraData,
                                                                    calibrationParameters);


                        if (calibrationStereoCameraData is not null)
                        {
                            calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)calibrationParameters] = calibrationStereoCameraData;
                        }

                        // Add the stereo calibration display text
                        if (calibrationStereoCameraData is not null)
                        {
                            leftCalibationText += "\n" + calibrationParameters.ToString() + ":\n";
                            leftCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(leftMonoCalibrationCameraData);
                            leftCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(calibrationStereoCameraData);
                            rightCalibationText += "\n" + calibrationParameters.ToString() + ":\n";
                            rightCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(rightMonoCalibrationCameraData);
                            rightCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(calibrationStereoCameraData);
                        }

                        // Set calibration output display 
                        LeftCalibDataText.Text = leftCalibationText;
                        LeftCalibDataBorder.Visibility = Visibility.Visible;
                        RightCalibDataText.Text = rightCalibationText;
                        RightCalibDataBorder.Visibility = Visibility.Visible;


                    }
                }

                // Find the best stereo calibration results set
                CalibrationParameters? calibrationParametersBest = calibProject.ReturnBestStereoCalibrationCameraData();
                if (calibrationParametersBest is not null)
                {
                    ret = 0;

                    if (writeBestFramesToPng)
                    {
                        MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParametersBest];
                        MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParametersBest];

                        if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                        {
                            // Create a list of the best calibration frames best on the sensor bin only
                            calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(movementMinThreshold,
                                                                                               blurMinThreshold,
                                                                                               stereoCornersMinThreshold,
                                                                                               maxFramesFromEachSensorBin);

                            // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                            await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(leftMonoCalibrationCameraData, rightMonoCalibrationCameraData, frameSize);

                            // Next top-up with pose diverse frames
                            calibrationStereoFrameSet.AddBestFramesUsingPoseBins(movementMinThreshold,
                                                                                 blurMinThreshold,
                                                                                 stereoCornersMinThreshold,
                                                                                 maxFramesFromEachPoseBin);


                            await SaveBestFramesAsync();
                        }
                    }
                }
            }

            SetAppMode(AppMode.BestFramesView);
            BestFrameJump(0);
            UpdateFrameLabel(true/*trueLeftFalseRight*/);
            UpdateFrameLabel(false/*trueLeftFalseRight*/);
            SetUIControls();

            return ret;
        }


        /// <summary>
        /// Write the Calibration Frame Set to file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public bool SaveCachedResults(string cacheFileSpec)
        {
            // Force the version to the current 
            calibrationStereoFrameSet.Data.Version = new CalibrationStereoFrameSet.DataClass().Version;

            bool saved = calibrationStereoFrameSet.SaveToFile(cacheFileSpec);

            SetUIControls();

            return saved;
        }


        /// <summary>
        /// Used to check is a cached result file already exists
        /// </summary>
        /// <returns></returns>
        public static bool CachedResultsFileExists(string cacheFileSpec)
        {
            bool ret = false;
            // Check if the file exists (remove any zero byte file)
            DeleteIfZeroByteFile(cacheFileSpec);

            if (File.Exists(cacheFileSpec))
            {
                ret = true;
            }

            return ret;
        }

        /// <summary>
        ///  Load a Calibration Frame Set from file and display to the screen
        /// </summary>
        /// <param name="_leftMediaFileSpec"></param>
        /// <param name="_rightMediaFileSpec"></param>
        /// <returns>null is error or the number of frames loaded</returns>
        public int LoadCachedResults(string cacheFileSpec)
        {
            int ret = 0;
            string messageText;

            // Check if the file exists (remove any zero byte file)
            DeleteIfZeroByteFile(cacheFileSpec);

            if (File.Exists(cacheFileSpec))
            {
                // Load the calibration frame set                
                if (calibrationStereoFrameSet.LoadFromFile(cacheFileSpec))
                {
                    CalibrationFrameSetViewerData dataLeft = new(true/*trueLeftFalseRight*/, calibrationStereoFrameSet);
                    CalibrationFrameSetViewerLeft.Data = dataLeft;
                    CalibrationFrameSetViewerLeft.RefreshSensorBin(ViewModeCurrent);
                    CalibrationFrameSetViewerLeft.RefreshPoseBin(ViewModeCurrent);
                    CalibrationFrameSetViewerLeft.DrawGraphs();

                    CalibrationFrameSetViewerData dataRight = new(false/*trueLeftFalseRight*/, calibrationStereoFrameSet);
                    CalibrationFrameSetViewerRight.Data = dataRight;
                    CalibrationFrameSetViewerRight.RefreshSensorBin(ViewModeCurrent);
                    CalibrationFrameSetViewerRight.RefreshPoseBin(ViewModeCurrent);
                    CalibrationFrameSetViewerRight.DrawGraphs();

                    ret = calibrationStereoFrameSet.Data.Frames.Count;
                }
                else
                {
                    messageText = $"Failed to load left: {cacheFileSpec}";
                    Debug.WriteLine(messageText);

                }
            }
            else
            {
                messageText = $"File not found left: {cacheFileSpec}";
                Debug.WriteLine(messageText);
            }

            return ret;
        }


        /// <summary>
        /// Pass through to check if the Calibration Board Zone setup is done
        /// </summary>
        /// <returns></returns>
        public bool IsCalibrationBoardZoneSetup()
        {
            if (calibrationStereoFrameSet is null)
                return false;

            return calibrationStereoFrameSet.Data.StartCalibrationBoardZone != -1 && calibrationStereoFrameSet.Data.StopCalibrationBoardZone != -1;
        }

        /// <summary>
        /// Pass through to check if the Frame Sets data has been collected
        /// </summary>
        /// <returns></returns>
        public bool IsFrameSetsSetup()
        {
            if (calibrationStereoFrameSet is null)
                return false;

            // Guard against stale/empty data if a cache load or build failed silently
            bool hasFrames = calibrationStereoFrameSet.Data is not null
                             && calibrationStereoFrameSet.Data.Frames is not null
                             && calibrationStereoFrameSet.Data.Frames.Count > 0;

            return hasFrames;
        }


        /// <summary>
        /// Check if the best frames have been setup
        /// </summary>
        /// <returns></returns>
        public bool IsBestFramesSetup()
        {
            if (calibrationStereoFrameSet is null)
                return false;

            return calibrationStereoFrameSet.Data.BestFrameIndexes.Count > 0;
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
            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    FrameMoveBack(true/*leftTrueRightFalse*/);
                    break;
                case ViewMode.BestFrames:
                    BestFrameMoveBack();
                    break;
            }
        }


        /// <summary>
        /// Left side Play button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftPlayPauseClick(object sender, RoutedEventArgs e)
        {
            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    PlayPauseClick(true);
                    break;
            }
        }


        /// <summary>
        /// Left side Frame Forward button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftFrameForwardClick(object sender, RoutedEventArgs e)
        {
            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    FrameMoveForward(true/*leftTrueRightFalse*/);
                    break;
                case ViewMode.BestFrames:
                    BestFrameMoveForward();
                    break;
            }
        }


        /// <summary>
        /// Receive the Left Goto Start button click and start the single vs double click timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void LeftGotoStartClick(object sender, RoutedEventArgs e)
        {
            _leftGotoStartCts?.Cancel();
            _leftGotoStartCts = new CancellationTokenSource();
            var token = _leftGotoStartCts.Token;

            try
            {
                // de-bounce window for double-tap
                await Task.Delay(250, token);
                // no double-tap arrived within 250ms
                LeftGotoStartSingle();
            }
            catch (TaskCanceledException) {/*canceled by double-tap*/}            
        }

        private async void LeftGotoEndClick(object sender, RoutedEventArgs e)
        {
            _leftGotoEndCts?.Cancel();
            _leftGotoEndCts = new CancellationTokenSource();
            var token = _leftGotoEndCts.Token;

            try
            {
                await Task.Delay(250, token);
                LeftGotoEndSingle();
            }
            catch (TaskCanceledException) {/*canceled by double-tap*/}
        }



        /// <summary>
        /// Right side frame back button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightFrameBackClick(object sender, RoutedEventArgs e)
        {
            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    FrameMoveBack(false/*leftTrueRightFalse*/);
                    break;
                case ViewMode.BestFrames:
                    BestFrameMoveBack();
                    break;
            }
        }


        /// <summary>
        /// Right side Play button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightPlayPauseClick(object sender, RoutedEventArgs e)
        {
            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    PlayPauseClick(false);
                    break;
            }
        }


        /// <summary>
        /// Right side Frame Forward button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightFrameForwardClick(object sender, RoutedEventArgs e)
        {
            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    FrameMoveForward(false/*leftTrueRightFalse*/);
                    break;
                case ViewMode.BestFrames:
                    BestFrameMoveForward();
                    break;
            }
        }


        /// <summary>
        /// Receive the Right Goto Start button click and start the single vs double click timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RightGotoStartClick(object sender, RoutedEventArgs e)
        {
            _rightGotoStartCts?.Cancel();
            _rightGotoStartCts = new CancellationTokenSource();
            var token = _rightGotoStartCts.Token;

            try
            {
                await Task.Delay(250, token);
                RightGotoStartSingle();
            }
            catch (TaskCanceledException) {/*canceled by double-tap*/}
        }

        private async void RightGotoEndClick(object sender, RoutedEventArgs e)
        {
            _rightGotoEndCts?.Cancel();
            _rightGotoEndCts = new CancellationTokenSource();
            var token = _rightGotoEndCts.Token;

            try
            {
                await Task.Delay(250, token);
                RightGotoEndSingle();
            }
            catch (TaskCanceledException) {/*canceled by double-tap*/}
        }



        // Goto Start/End DoubleTapped support handlers: mark pending and execute double-click action
        // DoubleTapped cancels the pending single and executes immediately:
        private void LeftGotoStartButton_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            _leftGotoStartCts?.Cancel();
            LeftGotoStartDouble();
        }

        private void LeftGotoEndButton_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            _leftGotoEndCts?.Cancel();
            LeftGotoEndDouble();
        }

        private void RightGotoStartButton_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            _rightGotoStartCts?.Cancel();
            RightGotoStartDouble();
        }

        private void RightGotoEndButton_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            _rightGotoEndCts?.Cancel();
            RightGotoEndDouble();
        }


        // Actual single vs double behaviors 

        /// <summary>
        /// Single click go to the start of the calibration board zone if known
        /// </summary>
        private void LeftGotoStartSingle()
        {
            // Guard
            if (headTrueIsStereoFalseIsMode is null) return;

            if (ViewModeCurrent == ViewMode.AllFrames)
            {
                int frameIndex = calibrationStereoFrameSet.GetStartCalibrationBoardZone();
                if (frameIndex != -1)
                {
                    FrameJump(true/*left*/, frameIndex);
                    return;
                }

                // Just go to the first frame
                LeftGotoStartDouble();
            }
        }

        /// <summary>
        /// Double click go the start of the media
        /// </summary>
        private void LeftGotoStartDouble()
        {
            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    FrameJump(true/*left*/, 0);
                    break;
                case ViewMode.BestFrames:
                    BestFrameJump(0);
                    break;
            }           
        }


        /// <summary>
        /// Single click go to the end of the calibration board zone if known
        /// </summary>
        private void LeftGotoEndSingle()
        {
            // Guard
            if (headTrueIsStereoFalseIsMode is null) return;

            if (ViewModeCurrent == ViewMode.AllFrames)
            {
                int frameIndex = calibrationStereoFrameSet.GetStopCalibrationBoardZone();
                if (frameIndex != -1)
                {
                    FrameJump(true/*left*/, frameIndex);
                    return;
                }

                // Just go to the first frame
                LeftGotoEndDouble();
            }
        }

        /// <summary>
        /// Double click go to the end of the media
        /// </summary>
        private void LeftGotoEndDouble()
        {
            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    FrameJump(true/*left*/, _totalFramesLeft - 1);
                    break;
                case ViewMode.BestFrames:
                    BestFrameJump(calibrationStereoFrameSet.Data.BestFrameIndexes.Count - 1);
                    break;
            }
        }

        private void RightGotoStartSingle()
        {
            FrameJump(false/*right*/, 0);
        }
        private void RightGotoStartDouble()
        {
            int start = calibrationStereoFrameSet.GetStartCalibrationBoardZone();
            FrameJump(false/*right*/, start >= 0 ? start : 0);
        }

        private void RightGotoEndSingle()
        {
            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    FrameMoveForward(false/*right*/);
                    break;
                case ViewMode.BestFrames:
                    BestFrameMoveForward();
                    break;
            }
        }
        private void RightGotoEndDouble()
        {
            if (ViewModeCurrent == ViewMode.AllFrames)
            {
                int target = _currentFrameRight + 10;
                FrameJump(false/*right*/, target);
            }
            else if (ViewModeCurrent == ViewMode.BestFrames)
            {
                BestFrameJump(_currentBestFrame + 5);
            }
        }


        /// <summary>
        /// Media Lock/Unlock button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LockUnlockClick(object sender, RoutedEventArgs e)
        {
            if (IsStereoLocked() == true)
            {
                // Unlock
                UnlockStereo();
            }
            else
            {
                // Lock
                LockStereo(_currentFrameLeft, _currentFrameRight);
            }
        }


        /// <summary>
        /// User request to go to a particular left frame index
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftGotoFrameTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                UserGoToFrameRequest(true/*leftTrueRightFalse*/, LeftGoToFrameTextBox);
        }


        /// <summary>
        /// User request to go to a particular right frame index
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightGotoFrameTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                UserGoToFrameRequest(false/*leftTrueRightFalse*/, RightGoToFrameTextBox);
        }


        /// <summary>
        /// Focus lost - check if the values has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftGotoFrameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UserGoToFrameRequest(true/*leftTrueRightFalse*/, LeftGoToFrameTextBox);
        }


        /// <summary>
        /// Focus lost - check if the values has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightGotoFrameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UserGoToFrameRequest(false/*leftTrueRightFalse*/, RightGoToFrameTextBox);
        }


        /// <summary>
        /// Used to cancel long running operations
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            cts?.Cancel();
        }


        /// <summary>
        /// Ensure the Calibration Timeline Left matches the Left Image width
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CalibrationBoardTimeLineLeft.Width = LeftImage.ActualWidth;
            if (CalibrationBoardTimeLineLeft.Visibility != Visibility.Visible)
                CalibrationBoardTimeLineLeft.Visibility = Visibility.Visible;
        }


        /// <summary>
        /// Ensure the Calibration Timeline Right matches the Right Image width
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CalibrationBoardTimeLineRight.Width = RightImage.ActualWidth;
            if (CalibrationBoardTimeLineRight.Visibility != Visibility.Visible)
                CalibrationBoardTimeLineRight.Visibility = Visibility.Visible;
        }



        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Sets the metadata labels for the frame metadata labels to match the theme
        /// </summary>
        /// <param name="theme"></param>
        private void SetMetadataLabels(ElementTheme theme)
        {
            BitmapImage bitmapImage;

            if (theme == ElementTheme.Dark)
            {
                bitmapImage = new (new Uri($"ms-appx:///Assets/BlurCircle-Dark.png"));
                LeftBlurIconLabel.Source = bitmapImage;

                bitmapImage = new (new Uri($"ms-appx:///Assets/ArucoSmall-Dark.png"));
                LeftFeatureCountIconLabel.Source = bitmapImage;
            }
            else if (theme == ElementTheme.Light)
            {
                bitmapImage = new(new Uri($"ms-appx:///Assets/BlurCircle-Light.png"));
                LeftBlurIconLabel.Source = bitmapImage;

                bitmapImage = new(new Uri($"ms-appx:///Assets/ArucoSmall-Light.png"));
                LeftFeatureCountIconLabel.Source = bitmapImage;
            }
        }


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
        /// <param name="correspondingCount">
        /// <param name="userData"></param>
        private void FrameProcessingCallbackFindCalibrationTimeLineRange(
                int stereoFrameIndex,
                int stereoFrameTotal,
                int leftFrameIndex,
                Mat leftMat,
                FrameData? leftFrameCalibrationTarget,
                int rightFrameIndex,
                Mat? rightMat,
                FrameData? rightFrameCalibrationTarget,
                int correspondingCount,
                object? userData)
        {
            safeUICall.Call(() =>
            {

                bool trueFoundFalseNotFound;

                if (leftMat is not null && !leftMat.IsEmpty && wbLeft is not null)
                {
                    DrawFrameToScreen(leftMat, wbLeft);
                    LeftFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                    LeftTimeInfoLabel.Text = string.Empty;
                }

                trueFoundFalseNotFound = leftFrameCalibrationTarget is not null;
                CalibrationBoardTimeLineLeft.CalibrationBoardFoundAt(leftFrameIndex, trueFoundFalseNotFound);


                if (rightMat is not null && !rightMat.IsEmpty && wbRight is not null)
                {
                    DrawFrameToScreen(rightMat, wbRight);
                    RightFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                    RightTimeInfoLabel.Text = string.Empty;
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
                FrameData? leftFrameCalibrationTarget,
                int rightFrameIndex,
                Mat? rightMat,
                FrameData? rightFrameCalibrationTarget,
                int correspondingCount,
                object? userData)
        {
            // Update the UI
            safeUICall.Call(() =>
            {

                bool trueFoundFalseNotFound;

                if (leftMat is not null && !leftMat.IsEmpty && wbLeft is not null)
                {
                    DrawFrameToScreen(leftMat, wbLeft);
                    LeftFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                    LeftTimeInfoLabel.Text = string.Empty;
                }

                trueFoundFalseNotFound = leftFrameCalibrationTarget is not null;
                CalibrationBoardTimeLineLeft.CalibrationBoardFoundAt(leftFrameIndex, trueFoundFalseNotFound);


                if (rightMat is not null && !rightMat.IsEmpty && wbRight is not null)
                {
                    DrawFrameToScreen(rightMat, wbRight);
                    RightFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                    RightTimeInfoLabel.Text = string.Empty;
                }

                trueFoundFalseNotFound = rightFrameCalibrationTarget is not null;
                CalibrationBoardTimeLineRight.CalibrationBoardFoundAt(rightFrameIndex, trueFoundFalseNotFound);


                try
                {
                    // Update from Bin Layers and the graphs 
                    // Note these are fully recreated from the full list to date
                    if (leftFrameCalibrationTarget is not null)
                    {
                        CalibrationFrameSetViewerLeft.RefreshSensorBin(ViewModeCurrent);
                        CalibrationFrameSetViewerLeft.DrawGraphs();
                    }
                    if (rightFrameCalibrationTarget is not null)
                    {
                        CalibrationFrameSetViewerRight.RefreshSensorBin(ViewModeCurrent);
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
                                            leftFrameCalibrationTarget.ChArUcoCorners.Length /*Size*/,
                                            leftFrameCalibrationTarget.Score,
                                            leftFrameCalibrationTarget.YawDeg,
                                            leftFrameCalibrationTarget.PitchDeg,
                                            correspondingCount);
                    }

                    if (rightFrameCalibrationTarget is not null)
                    {
                        (movementFromPrevious, movementFactor, movementToNext) = GetMovementFactors(rightFrameCalibrationTarget);

                        UpdateFrameMetaData(false/*trueLeftfalseRight*/,
                                            movementFactor, movementFromPrevious, movementToNext,
                                            rightFrameCalibrationTarget.BlurFactor,
                                            rightFrameCalibrationTarget.ChArUcoCorners.Length /*Size*/,
                                            rightFrameCalibrationTarget.Score,
                                            rightFrameCalibrationTarget.YawDeg,
                                            rightFrameCalibrationTarget.PitchDeg,
                                            correspondingCount);

                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FrameProcessingCallbackFindCalibrationsFrames: Error processing ChArUco board: {ex.Message}");
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
        private static (double movementFromPrevious, double movementFactor, double movementToNext) GetMovementFactors(FrameData? frameCalibrationTarget)
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
        private void UpdateFrameMetaData(bool trueLeftfalseRight,
            double movementFactor, double movementFromPrevious, double movementToNext,
            double blurFactor, int featureCount, double score,
            double yaw, double pitch,
            int correspondingCount)
        {
            TextBlock MovementFactor;
            TextBlock BlurFactor;
            TextBlock FeatureCount;
            TextBlock Score;
            TextBlock Yaw;
            TextBlock Pitch;

            if (trueLeftfalseRight)
            {                
                MovementFactor = LeftMoveText;
                BlurFactor = LeftBlurText;
                Yaw = LeftYawText;
                Pitch = LeftPitchText;
                FeatureCount = LeftFeatureCountText;
                Score = LeftScoreText;
            }
            else
            {
                MovementFactor = RightMoveText;
                BlurFactor = RightBlurText;
                FeatureCount = RightFeatureCountText;
                Score = RightScoreText;
                Yaw = RightYawText;
                Pitch = RightPitchText;
            }

            // Display movement and blur factor
            if (movementFactor != -1)
            {
                MovementFactor.Text = $"{movementFactor:F1}";
            }
            else if (movementFromPrevious != -1)
            {
                MovementFactor.Text = $"\u2190{movementFromPrevious:F1}";
            }
            else if (movementToNext != -1)
            {
                MovementFactor.Text = $"{movementToNext:F1}\u21D2";
            }

            // Blur
            BlurFactor.Text = $"{blurFactor:F1}";

            // Yaw
            if (yaw != 0.0)
                Yaw.Text = $"{yaw:F0}°";
            else
                Yaw.Text = string.Empty;

            // Pitch
            if (pitch != 0.0)
                Pitch.Text = $"{pitch:F0}°";
            else
                Pitch.Text = string.Empty;

            // Feature Count (number of ChArUco corners)
            if (correspondingCount != -1)
                // Stereo show corresponding feature count (count of matching features on both left and right)
                FeatureCount.Text = $"{correspondingCount}";
            else
                // Mono show feature count of the board
                FeatureCount.Text = $"{featureCount}";

            // Score
            if (score != 0)
                Score.Text = $"{score:F2}";
            else
                Score.Text = string.Empty;
        }

        /// <summary>
        /// Clear the frame metadata on screen fields
        /// </summary>
        private void ClearFrameMetaData(bool trueLeftfalseRight)
        {
            if (trueLeftfalseRight)
            {
                LeftMoveText.Text = string.Empty;
                LeftBlurText.Text = string.Empty;
                LeftFeatureCountText.Text = string.Empty;
                LeftScoreText.Text = string.Empty;
                LeftYawText.Text = string.Empty;
                LeftPitchText.Text = string.Empty;
            }
            else
            {
                RightMoveText.Text = string.Empty;
                RightBlurText.Text = string.Empty;
                RightFeatureCountText.Text = string.Empty;
                RightScoreText.Text = string.Empty;
                RightYawText.Text = string.Empty;
                RightPitchText.Text = string.Empty;
            }
        }


        /// <summary>
        /// Play in the context of this application is a timer based frame forward operation
        /// </summary>
        private void PlayLeft()
        {
            if (capLeft != null && wbLeft != null)
            {
                _ForwardFrame(true/*leftTrueRightFalse*/);
                UpdateFrameLabel(true/*trueLeftFalseRight*/);
            }
        }
        private void PlayRight()
        {
            if (capRight != null && wbRight != null)
            {
                _ForwardFrame(false/*leftTrueRightFalse*/);
                UpdateFrameLabel(false/*trueLeftFalseRight*/);
            }
        }
        private void PlayBoth()
        {
            if (capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                _ForwardFrame(true/*leftTrueRightFalse*/);
                _ForwardFrame(false/*leftTrueRightFalse*/);
                UpdateFrameLabel(true/*trueLeftFalseRight*/);
                UpdateFrameLabel(false/*trueLeftFalseRight*/);
            }
        }

        private void FrameMoveBack(bool leftTrueRightFalse)
        {
            int? leftIndex = null;
            int? rightIndex = null;
            int framesetIndex = -1;
            FrameData? leftFrameData = null;
            FrameData? rightFrameData = null;
            int correpondingCount = -1;

            // If stereo and locked
            if (isLocked && capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                // Get the next index (if valid)
                leftIndex = GetNextIndex(leftTrueRightFalse, -1/*relative*/, null/*absolute*/);
                rightIndex = GetNextIndex(!leftTrueRightFalse, -1/*relative*/, null/*absolute*/);

                if (leftIndex is not null && rightIndex is not null)
                {
                    framesetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftRightIndexes((int)leftIndex, (int)rightIndex);

                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue(framesetIndex, out var tuple))
                        (leftFrameData, rightFrameData, correpondingCount) = tuple;

                    // if frame data is null then there is no decoration
                    _JumpFrame(true/*leftTrueRightFalse*/, (int)leftIndex, leftFrameData, correpondingCount);
                    _JumpFrame(false/*leftTrueRightFalse*/, (int)rightIndex, rightFrameData, correpondingCount);    
                }
            }
            // If mono left or stereo unlocked left
            else if (leftTrueRightFalse && capLeft != null && wbLeft != null)
            {
                // Get the next index (if valid)
                leftIndex = GetNextIndex(leftTrueRightFalse, -1/*relative*/, null/*absolute*/);
               
                if (leftIndex is not null)
                {                  
                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue((int)leftIndex, out var tuple))
                        (leftFrameData, _, correpondingCount) = tuple;

                    _JumpFrame(true/*leftTrueRightFalse*/, (int)leftIndex, leftFrameData, correpondingCount);
                }
            }
            // If mono right or stereo unlocked right
            else if (!leftTrueRightFalse && capRight != null && wbRight != null)
            {
                // Get the next index (if valid)
                rightIndex = GetNextIndex(leftTrueRightFalse, -1/*relative*/, null/*absolute*/);

                if (rightIndex is not null)
                {
                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue((int)rightIndex, out var tuple))
                        (_, rightFrameData, correpondingCount) = tuple;

                    _JumpFrame(false/*leftTrueRightFalse*/, (int)rightIndex, rightFrameData, correpondingCount);
                }
            }

            //???
            //if (leftIndex is not null && leftIndex >= 0)
            //    DecorateWithFrameInfo(leftTrueRightFalse, framesetIndex);

            //if (rightIndex is not null && rightIndex >= 0)
            //    DecorateWithFrameInfo(leftTrueRightFalse, framesetIndex);

        }

        private void FrameMoveForward(bool leftTrueRightFalse)
        {
            int? leftIndex = null;
            int? rightIndex = null;
            int framesetIndex = -1;

            if (isLocked && capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                leftIndex = _ForwardFrame(true/*leftTrueRightFalse*/);
                rightIndex = _ForwardFrame(false/*leftTrueRightFalse*/);
                framesetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftRightIndexes((int)leftIndex, (int)rightIndex);
            }
            else if (leftTrueRightFalse && capLeft != null && wbLeft != null)
            {
                leftIndex = _ForwardFrame(true/*leftTrueRightFalse*/);
                framesetIndex = (int)leftIndex;
            }
            else if (!leftTrueRightFalse && capRight != null && wbRight != null)
            {
                rightIndex = _ForwardFrame(false/*leftTrueRightFalse*/);
                framesetIndex = (int)rightIndex;
            }

            //???
            //if (leftIndex is not null && leftIndex >= 0)
            //    DecorateWithFrameInfo(leftTrueRightFalse, framesetIndex);
            //
            //if (rightIndex is not null && rightIndex >= 0)
            //    DecorateWithFrameInfo(leftTrueRightFalse, framesetIndex);
        }


        /// <summary>
        /// Toggle the play to pause, pause to play state
        /// Play timer is started and stopped here, icons updated
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
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

        /// <summary>
        /// In All Frames view mode, jump to the requested frame set index
        /// framesetIndex = null to display/redisplay the current frame
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        /// <param name="framesetIndex"></param>
        private void FrameJump(bool leftTrueRightFalse, int? framesetIndexRequest)
        {
            int framesetIndex = 0;
            int leftIndex = -1;
            int? rightIndex = null;

            FrameData? targetLeft = null;
            FrameData? targetRight = null;
            int correpondingCount = -1;

            if (isLocked && capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                // null means jump to existing frame
                if (framesetIndexRequest is null)
                    framesetIndex = _currentFrameLeft;
                else
                    framesetIndex = (int)framesetIndexRequest;

                if (calibrationStereoFrameSet.Data.Frames.TryGetValue(framesetIndex, out var tuple))
                {
                    (targetLeft, targetRight, correpondingCount) = tuple;

                    leftIndex = targetLeft.FrameIndex;
                    rightIndex = targetRight?.FrameIndex;
                }
                else
                { 
                    (leftIndex, rightIndex) = calibrationStereoFrameSet.GetIndexes(framesetIndex);
                }

                leftIndex = _JumpFrame(true/*leftTrueRightFalse*/, leftIndex, targetLeft, correpondingCount);
                if (rightIndex is not null) // Won't be null but keep compiler happy
                    rightIndex = _JumpFrame(false/*leftTrueRightFalse*/, (int)rightIndex, targetRight, correpondingCount);
            }
            else if (leftTrueRightFalse && capLeft != null && wbLeft != null)
            {
                if (framesetIndexRequest is null)
                    framesetIndex = _currentFrameLeft;
                else
                    framesetIndex = (int)framesetIndexRequest;

                if (calibrationStereoFrameSet.Data.Frames.TryGetValue(framesetIndex, out var tuple))
                {
                    (targetLeft, _, correpondingCount) = tuple;
                    leftIndex = targetLeft.FrameIndex;                  
                }
                else
                {
                    (leftIndex, _) = calibrationStereoFrameSet.GetIndexes(framesetIndex);
                }

                leftIndex = _JumpFrame(true/*leftTrueRightFalse*/, leftIndex, targetLeft, correpondingCount);
            }
            else if (!leftTrueRightFalse && capRight != null && wbRight != null)
            {
                if (framesetIndexRequest is null)
                    framesetIndex = _currentFrameRight;
                else 
                    framesetIndex = (int)framesetIndexRequest;

                if (calibrationStereoFrameSet.Data.Frames.TryGetValue(framesetIndex, out var tuple))
                {
                    (_, targetRight, correpondingCount) = tuple;
                    if (targetRight is not null)
                        rightIndex = targetRight.FrameIndex;
                }
                else
                {
                    (_, rightIndex) = calibrationStereoFrameSet.GetIndexes(framesetIndex);
                }

                if (rightIndex is not null)
                    rightIndex = _JumpFrame(false/*leftTrueRightFalse*/, (int)rightIndex, targetRight, correpondingCount);
            }

            //???
            //if (leftIndex >= 0)
            //    DecorateWithFrameInfo(leftTrueRightFalse, framesetIndex);
            //
            //if (rightIndex is not null && rightIndex >= 0)
            //    DecorateWithFrameInfo(leftTrueRightFalse, framesetIndex);
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

        private void BestFrameJump(int? targetIndexRequest)
        {
            bool ok = false;
            int targetIndex = 0;

            if (calibrationStereoFrameSet is not null)
            {
                if (targetIndexRequest is null)
                    targetIndex = _currentBestFrame;
                else
                    targetIndex = (int)targetIndexRequest;

                // Check the request best frame index is with range
                if (targetIndex < 0)
                    targetIndex = 0;

                if (targetIndex >= calibrationStereoFrameSet.Data.BestFrameIndexes.Count)
                    targetIndex = calibrationStereoFrameSet.Data.BestFrameIndexes.Count - 1;

                try
                {
                    // Get the absolute frame index from the best frame index
                    int frameIndex = calibrationStereoFrameSet.Data.BestFrameIndexes[targetIndex];


                    // Get stereo frame pair
                    (FrameData leftTarget, FrameData? rightTarget, int correspondingCount) = calibrationStereoFrameSet.Data.Frames[frameIndex];

                    // Jump to the left side best frame
                    _JumpFrame(true/*leftTrueRightFalse*/, leftTarget.FrameIndex, leftTarget, correspondingCount);

                    //???DecorateWithFrameInfo(true/*leftTrueRightFalse*/, frameIndex);
                  

                    if (rightTarget is not null)
                    {
                        // Jump to the right side best frame
                        _JumpFrame(false/*leftTrueRightFalse*/, rightTarget.FrameIndex, rightTarget, correspondingCount);

                        //???DecorateWithFrameInfo(false/*leftTrueRightFalse*/, frameIndex);
                    }

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
            }
        }


        /// <summary>
        /// Used to update the UI for frame information:
        ///     The frame index / total frame and time position
        ///     The frame metadata (movement, blur, yaw, pitch, features, score)
        ///     Highlight the sensor coverage
        ///     Highlight the pose position
        /// Remember that in mono the frameIndex is the actual physical .MP4 frame 
        /// index and the index into CalibrationStereoFrameSet.  If stereo the 
        /// frameIndex is only the index into the CalibrationStereoFrameSet 
        /// (and inside that there are left and right physical frame indexes)
        /// framesetIndex = -1 clears the UI frame fields
        /// </summary>
        /// <param name="framesetIndex"></param>
        private void DecorateWithFrameInfo(bool leftTrueRightFalse, FrameData? frameData, int correpondingCount)
        {
            // Set frame index / total frame and time position
            UpdateFrameLabel(leftTrueRightFalse);

            bool clearMetadataAndHighlights = false;
            if (frameData is not null)
            {
                // The frame metadata (movement, blur, yaw, pitch, features, score)
                UpdateFrameMetaData(leftTrueRightFalse,
                            frameData.MovementFactor,
                            frameData.MovementFromPrevious,
                            frameData.MovementToNext,
                            frameData.BlurFactor,
                            frameData.ChArUcoCorners.Length /*Size*/,
                            frameData.Score,
                            frameData.YawDeg,
                            frameData.PitchDeg,
                            correpondingCount);
                // Indicate which of the bin this frame is found in
                if (leftTrueRightFalse)
                {
                    CalibrationFrameSetViewerLeft.HighLightActiveSensorBin(frameData);
                    CalibrationFrameSetViewerLeft.HighLightActivePoseBin(frameData);
                }
                else
                {
                    CalibrationFrameSetViewerRight.HighLightActiveSensorBin(frameData);
                    CalibrationFrameSetViewerRight.HighLightActivePoseBin(frameData);
                }            
            }
            else
            {
                clearMetadataAndHighlights = true;
            }

            // We don't use DecorateClear() here because we want the
            // Left/RightUpdateFrameLabel to remain
            if (clearMetadataAndHighlights)
            {
                ClearFrameMetaData(leftTrueRightFalse);
                CalibrationFrameSetViewerLeft.HighLightActiveSensorBin(null);
                CalibrationFrameSetViewerRight.HighLightActiveSensorBin(null);
                CalibrationFrameSetViewerLeft.HighLightActivePoseBin(null);
                CalibrationFrameSetViewerRight.HighLightActivePoseBin(null);
            }
        }


        /// <summary>
        /// Clear any frame decoration on the UI
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        private void DecorateClear(bool leftTrueRightFalse)
        {
            if (leftTrueRightFalse)
            {
                LeftFrameInfoLabel.Text = string.Empty;
                LeftTimeInfoLabel.Text = string.Empty;
            }
            else
            {
                RightFrameInfoLabel.Text = string.Empty;
                RightTimeInfoLabel.Text = string.Empty;
            }

            ClearFrameMetaData(leftTrueRightFalse);
            CalibrationFrameSetViewerLeft.HighLightActivePoseBin(null);
            CalibrationFrameSetViewerRight.HighLightActivePoseBin(null);
        }


        /// <summary>
        /// Used to frame forward in AllFrames mode.  This method reads
        /// the frame from the .MP4.  
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        /// <returns>-1 if out of range (end of media normally)</returns>
        private int _ForwardFrame(bool leftTrueRightFalse)
        {
            VideoCapture? cap;
            WriteableBitmap? wb;
            int frameIndex;
            int framesetIndex;

            if (leftTrueRightFalse)
            {
                cap = capLeft;
                wb = wbLeft;
                frameIndex = Math.Max(0, _currentFrameLeft + 1);

                // Get the frame set index
                framesetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftIndexes(frameIndex);
            }
            else
            {
                cap = capRight;
                wb = wbRight;
                frameIndex = Math.Max(0, _currentFrameRight + 1);

                // Get the frame set index
                framesetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromRightIndexes(frameIndex);
            }

            if (cap is not null && wb is not null)
            {                
                if (leftTrueRightFalse)
                {
                    // Check for end of media
                    if (_currentFrameLeft >= _totalFramesLeft)
                    {
                        PlayPauseClick(leftTrueRightFalse);
                        return -1;
                    }
                }
                else
                {
                    // Check for end of media
                    if (_currentFrameRight >= _totalFramesRight)
                    {
                        PlayPauseClick(leftTrueRightFalse);
                        return -1;
                    }
                }

                using var mat = new Mat();

                if (cap!.Read(mat) && !mat.IsEmpty)
                {
                    // Get the target calibration frame data safely using the dictionary key
                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue(framesetIndex, out var tuple))
                    {
                        (FrameData? targetLeft, FrameData? targetRight, _) = tuple;
                        FrameData? target = leftTrueRightFalse ? targetLeft : targetRight;

                        // Apply the calibration board markup and draw to screen
                        ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb, target);
                    }
                    else
                    {
                        // Draw frame to screen
                        ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb, null);

                        // No Frame set data for this frame
                        DecorateWithFrameInfo(leftTrueRightFalse, -1);
                    }
                }

                if (leftTrueRightFalse)
                    _currentFrameLeft = frameIndex;
                else
                    _currentFrameRight = frameIndex;
            }

            return frameIndex;
        }


        /// <summary>
        /// Used to frame back in AllFrames mode.  This method reads
        /// the frame from the .MP4.  
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        /// <returns>-1 if out of range (end of media normally)</returns>
        private int _BackFrame(bool leftTrueRightFalse)
        {
            VideoCapture? cap;
            WriteableBitmap? wb;
            int frameIndex;
            int framesetIndex;

            if (leftTrueRightFalse)
            {
                cap = capLeft;
                wb = wbLeft;
                frameIndex = Math.Max(0, _currentFrameLeft - 1);

                // Get the frame set index
                framesetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftIndexes(frameIndex);
            }
            else
            {
                cap = capRight;
                wb = wbRight;
                frameIndex = Math.Max(0, _currentFrameRight - 1);

                // Get the frame set index
                framesetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromRightIndexes(frameIndex);
            }

            if (cap is not null && wb is not null)
            {
                // Set frame index in EMGU.CV
                cap!.Set(CapProp.PosFrames, frameIndex);

                using var mat = new Mat();
                cap.Read(mat);

                // Check if Mat has valid data
                if (!mat.IsEmpty)
                {
                    // Get the target calibration frame data safely using the dictionary key
                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue(framesetIndex, out var tuple))
                    {
                        (FrameData? targetLeft, FrameData? targetRight, _) = tuple;
                        FrameData? target = leftTrueRightFalse ? targetLeft : targetRight;

                        // Apply the calibration board markup and draw to screen
                        ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb, target);
                    }
                    else
                    {
                        // Draw frame to screen
                        ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb, null);

                        DecorateWithFrameInfo(leftTrueRightFalse, -1);
                    }
                }

                if (leftTrueRightFalse)
                    _currentFrameLeft = frameIndex;
                else
                    _currentFrameRight = frameIndex;
            }

            return frameIndex;
        }


        /// <summary>
        /// Used to jump to a particular frame in AllFrames mode.  
        /// This method reads the frame from the .MP4.  
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        /// <param name="targetIndex"></param>
        /// <param name="frameData"></param>
        /// <returns>-1 if out of range (end of media normally)</returns>
        private int _JumpFrame(bool leftTrueRightFalse, int targetIndex, FrameData? frameData, int correpondingCount)
        {
            VideoCapture? cap;
            WriteableBitmap? wb;
            int framesetIndex;

            framesetIndex = Math.Max(0, targetIndex);

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
                // EMGU.CV: use Set with CapProp
                cap!.Set(CapProp.PosFrames, framesetIndex);

                using var mat = new Mat();
                cap.Read(mat);

                if (!mat.IsEmpty && wb is not null)
                {
                    // Get the target calibration frame data safely using the dictionary key
                    //???if (calibrationStereoFrameSet.Data.Frames.TryGetValue(framesetIndex, out var tuple))
                    //???{
                    //???    (FrameData? targetLeft, FrameData? targetRight, _) = tuple;
                    //???    FrameData? target = leftTrueRightFalse ? targetLeft : targetRight;

                        // Apply the calibration board markup and draw to screen
                        ProcessFrame(leftTrueRightFalse, framesetIndex, mat, wb, frameData);
                        DecorateWithFrameInfo(leftTrueRightFalse, frameData, correpondingCount);
                    //???}
                    //???else
                    //???{
                        // Draw frame to screen (pre-frame set being available)
                    //???    ProcessFrame(leftTrueRightFalse, framesetIndex, mat, wb, null);                       
                    //???    DecorateWithFrameInfo(leftTrueRightFalse, framesetIndex);
                    //???}
                }

                if (leftTrueRightFalse)
                {
                    _currentFrameLeft = framesetIndex;
                }
                else
                {
                    _currentFrameRight = framesetIndex;
                }
            }

            return framesetIndex;
        }


        /// <summary>
        /// Calculate the new index for the given size return null is index is 
        /// out of range
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        /// <param name="relative"></param>
        /// <param name="absolute"></param>
        /// <returns></returns>
        private int? GetNextIndex(bool leftTrueRightFalse, int? relative, int? absolute)
        {
            // Guard
            if (relative is null && absolute is null) return null;

            int? index = null;

            if (leftTrueRightFalse)
            {
                if (relative is not null)
                {
                    if (_currentFrameLeft + (int)relative >= 0 &&
                        _currentFrameLeft + (int)relative < _totalFramesLeft)                
                        index = _currentFrameLeft + (int)relative;
               
                }
                else if (absolute is not null)
                {
                    if ((int)absolute >= 0 &&
                        (int)absolute < _totalFramesLeft)                    
                        index = (int)absolute;
                    
                }
            }
            else
            {
                if (relative is not null)
                {
                    if (_currentFrameRight + (int)relative >= 0 &&
                        _currentFrameRight + (int)relative < _totalFramesRight)
                        index = _currentFrameRight + (int)relative;

                }
                else if (absolute is not null)
                {
                    if ((int)absolute >= 0 &&
                        (int)absolute < _totalFramesRight)
                        index = (int)absolute;

                }
            }

            return index;
        }


        /// <summary>
        /// Processes a video frame by applying calibration board markup and rendering it to the display.
        /// </summary>
        /// <remarks>This method applies calibration markers to the frame if calibration data is provided,
        /// then renders the frame to the specified bitmap.</remarks>
        /// <param name="leftTrueRightFalse">Indicates whether the frame corresponds to the left (<see langword="true"/>) or right (<see
        /// langword="false"/>) camera or view.</param>
        /// <param name="frameIndex">The zero-based index of the frame being processed.</param>
        /// <param name="frame">The <see cref="Mat"/> object representing the video frame to process. Must not be <see langword="null"/>.</param>
        /// <param name="wb">The <see cref="WriteableBitmap"/> to which the processed frame will be rendered. Must not be <see
        /// langword="null"/>.</param>
        /// <param name="frameCalibrationData">Optional calibration data to apply to the frame. If provided, calibration markers may be drawn on the frame
        /// before rendering.</param>
        private void ProcessFrame(bool leftTrueRightFalse, int frameIndex, Mat frame, WriteableBitmap wb, FrameData? frameCalibrationData)
        {
            try
            {
                if (frameCalibrationData is not null && headTrueIsStereoFalseIsMode is not null)
                    CalibrationStereoFrameSet.DrawMarkersToMat(frameCalibrationData, frame, (bool)headTrueIsStereoFalseIsMode);

                DrawFrameToScreen(frame, wb);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessFrame: Error processing ChArUco board, AppMode:{AppModeCurrent}, {ex.Message}");
            }
        }


        private byte[]? _frameCopyBuffer;

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
                if (_frameCopyBuffer == null || _frameCopyBuffer.Length != byteCount)
                    _frameCopyBuffer = new byte[byteCount];

                Marshal.Copy(bgraFrame.DataPointer, _frameCopyBuffer, 0, byteCount);

                using var stream = wb.PixelBuffer.AsStream();
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(_frameCopyBuffer, 0, byteCount);

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
        
        private void UpdateFrameLabel(bool leftTrueRightFalse)
        {
            int targetIndex = -1;
            int totalFrames = -1;

            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    if (leftTrueRightFalse)
                    {
                        targetIndex = _currentFrameLeft;
                        totalFrames = _totalFramesLeft;
                    }
                    else
                    {
                        targetIndex = _currentFrameRight;
                        totalFrames = _totalFramesRight;
                    }
                    break;
                case ViewMode.BestFrames:
                    targetIndex = _currentBestFrame;
                    totalFrames = calibrationStereoFrameSet.Data.BestFrameIndexes.Count;
                    break;
            }

            if (leftTrueRightFalse)
            {
                UpdateFrameAndTimeLabel(LeftFrameInfoLabel, LeftTimeInfoLabel, capLeft, targetIndex, totalFrames);
                LeftGoToFrameTextBox.Text = string.Empty;   //??? $"{_currentFrameLeft}";
            }
            else
            {
                UpdateFrameAndTimeLabel(RightFrameInfoLabel, RightTimeInfoLabel, capRight, targetIndex, totalFrames);
                RightGoToFrameTextBox.Text = string.Empty; //??? $"{_currentFrameRight}";
            }
        }
        
        private void UpdateFrameAndTimeLabel(TextBlock frameTextBlock, TextBlock timeTextBlock, VideoCapture? cap, int currentFrame, int totalFrames)
        {
            if (cap is not null)
            {
                if (ViewModeCurrent == ViewMode.AllFrames || 
                    ViewModeCurrent == ViewMode.BestFrames ||
                    ViewModeCurrent == ViewMode.FilterFrames)
                {
                    string frameText;
                    string timeText;
                    if (totalFrames == -1 || totalFrames == 0)
                    {
                        double time = cap.Get(CapProp.PosMsec) / 1000.0;
                        frameText = $"Frame {currentFrame}";
                        timeText = $"Time {time:F2}s";
                    }
                    else
                    {
                        double time = cap.Get(CapProp.PosMsec) / 1000.0;
                        frameText = $"Frame {currentFrame} / {totalFrames - 1}";
                        timeText = $"Time {time:F2}s";
                    }

                    frameTextBlock.Text = frameText;
                    timeTextBlock.Text = timeText;
                }
                else
                {
                    frameTextBlock.Text = string.Empty;
                    timeTextBlock.Text = string.Empty;
                }
            }
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
            if (calibrationStereoFrameSet.Data.Frames.Count > 0)
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
        /// Write the WriteableBitmap to file
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="file"></param>
        /// <returns></returns>
        public static async Task SaveWriteableBitmapToFileAsync(WriteableBitmap bitmap, StorageFile file)
        {
            // Get the pixel buffer from the WriteableBitmap
            using var stream = new InMemoryRandomAccessStream();
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



        /// <summary>
        /// Set the UI controls based on the current application mode and media state.
        /// </summary>
        private void SetUIControls()
        {
            //SetUISubControls(true/*trueLeftfalseRight*/);
            //SetUISubControls(false/*trueLeftfalseRight*/);

        }



        public void SetAppMode(AppMode newAppMode)
        {
            // Check is AppMode has not changed
            if (AppModeCurrent == newAppMode)
                return;

            // Remember the new AppMode
            AppModeCurrent = newAppMode;


            if (AppModeCurrent == AppMode.Close)
            {
                // No view mode really but set to All Frames
                SetViewMode(ViewMode.AllFrames);
                //???ViewModeCurrent = ViewMode.AllFrames;

                SetMediaControls(true/*trueLeftFalseRight*/, null);
                SetMediaControls(false/*trueLeftFalseRight*/, null);
            }
            if (AppModeCurrent == AppMode.Open)
            {
                // All Flag set correct;

                // Set view mode
                SetViewMode(ViewMode.AllFrames);

                FrameJump(true/*trueLeftFalseRight*/, 0);
                FrameJump(false/*trueLeftFalseRight*/, 0);
            }
            else if (AppModeCurrent == AppMode.FindCalibrationsFrames)
            {
                SetMediaControls(true/*trueLeftFalseRight*/, null);
                SetMediaControls(false/*trueLeftFalseRight*/, null);

                // Clear frame UI display data
                DecorateClear(true/*trueLeftfalseRight*/);
                DecorateClear(false/*trueLeftfalseRight*/);
                
                // Don't change the view mode
            }
            else if (AppModeCurrent == AppMode.BestFramesCalc)
            {
                // Clear frame UI display data
                DecorateClear(true/*trueLeftfalseRight*/);
                DecorateClear(false/*trueLeftfalseRight*/);

                // Change the view mode so we can see the frame count build in the UI
                SetViewMode(ViewMode.BestFrames);

                // Because we are process disable the media controls
                SetMediaControls(true/*trueLeftFalseRight*/, null);
                SetMediaControls(false/*trueLeftFalseRight*/, null);
            }
            else if (AppModeCurrent == AppMode.BestFramesView)
            {
                // Set view mode
                SetViewMode(ViewMode.BestFrames);
                BestFrameJump(0);
            }
            else if (AppModeCurrent == AppMode.BestFramesSave)
            {
              
                if (ViewModeCurrent != ViewMode.BestFrames)
                    SetViewMode(ViewMode.BestFrames);
            }
        }


        /// <summary>
        /// Used to manually change the view mode from the Menu>View menu items
        /// </summary>
        /// <param name="newViewMode"></param>
        public void SetViewMode(ViewMode newViewMode)
        {
            ViewModeCurrent = newViewMode;

            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    SetMediaControls(true/*trueLeftFalseRight*/, ViewMode.AllFrames);
                    SetMediaControls(false/*trueLeftFalseRight*/, ViewMode.AllFrames);
                    
                    // Display last shown 'AllFrames' frame
                    FrameJump(true/*trueLeftFalseRight*/, null);
                    FrameJump(false/*trueLeftFalseRight*/, null);
                    break;

                case ViewMode.BestFrames:
                    SetMediaControls(true/*trueLeftFalseRight*/, ViewMode.BestFrames);
                    SetMediaControls(false/*trueLeftFalseRight*/, ViewMode.BestFrames);

                    // Display last shown 'AllFrames' frame
                    BestFrameJump(null);
                    break;

                case ViewMode.FilterFrames:
                    throw new Exception("Not implemented");

                case ViewMode.SensorCoverage:
                    throw new Exception("Not implemented");
            }

            // Clear frame UI display data
            DecorateClear(true/*trueLeftfalseRight*/);
            DecorateClear(false/*trueLeftfalseRight*/);

            // Update the totals in the sensor and pose bin displays
            CalibrationFrameSetViewerLeft.RefreshSensorBin(ViewModeCurrent);
            CalibrationFrameSetViewerLeft.RefreshPoseBin(ViewModeCurrent);
            CalibrationFrameSetViewerRight.RefreshSensorBin(ViewModeCurrent);
            CalibrationFrameSetViewerRight.RefreshPoseBin(ViewModeCurrent);


            // Change the view mode
            SetUIControls();
        }


        /// <summary>
        /// Check if a particular view mode is possible given the current appMode
        /// </summary>
        /// <param name="queryViewMode"></param>
        /// <returns></returns>
        public bool IsViewModeAvailable(ViewMode queryViewMode)
        {
            bool ret = false;

            switch (queryViewMode)
            {
                case ViewMode.AllFrames:
                    if (AppModeCurrent != AppMode.Close)
                        ret = true;
                    break;
                case ViewMode.BestFrames:
                    if (AppModeCurrent == AppMode.BestFramesView ||
                        AppModeCurrent == AppMode.BestFramesSave)
                        ret = true;
                    break;
                case ViewMode.FilterFrames:
                    if (AppModeCurrent != AppMode.Close)
                        ret = true;
                    break;
                case ViewMode.SensorCoverage:
                    if (AppModeCurrent == AppMode.BestFramesView ||
                        AppModeCurrent == AppMode.BestFramesSave)
                        ret = true;
                    break;
            }

            return ret;
        }


        /// <summary>
        /// Set the media buttons and other UI controls to either the 
        ///     AllFrame state   (all controls enabled)
        ///     BestFrames state (all controls enabled except play buttons)
        ///     null state       (all controls disabled)
        /// </summary>
        /// <exception cref="Exception"></exception>
        private void SetMediaControls(bool trueLeftFalseRight, ViewMode? viewMode)
        {
            Button gotoStartButton;
            Button frameBackButton;
            Button playPauseButton;
            Button frameForwardButton;
            Button gotoEndButton;
            TextBox goToFrameTextBox;

            if (trueLeftFalseRight)
            {
                gotoStartButton = LeftGotoStartButton;
                frameBackButton = LeftFrameBackButton;
                playPauseButton = LeftPlayPauseButton;
                frameForwardButton = LeftFrameForwardButton;
                gotoEndButton = LeftGotoEndButton;
                goToFrameTextBox = LeftGoToFrameTextBox;
            }
            else 
            {
                gotoStartButton = RightGotoStartButton;
                frameBackButton = RightFrameBackButton;
                playPauseButton = RightPlayPauseButton;
                frameForwardButton = RightFrameForwardButton;
                gotoEndButton = RightGotoEndButton;
                goToFrameTextBox = RightGoToFrameTextBox;
            }
            
            // Media control button flags
            bool gotoStartButtonIsEnabled = false;
            bool frameBackButtonIsEnabled = false;
            bool playPauseButtonIsEnabled = false;
            bool frameForwardButtonIsEnabled = false;
            bool gotoEndButtonIsEnabled = false; 
            bool goToFrameTextBoxIsVisable = false;
            
            switch (viewMode)
            {
                case null:
                    gotoStartButtonIsEnabled = false;
                    frameBackButtonIsEnabled = false;
                    playPauseButtonIsEnabled = false;
                    frameForwardButtonIsEnabled = false;
                    gotoEndButtonIsEnabled = false;
                    goToFrameTextBoxIsVisable = false;
                    break;

                case ViewMode.AllFrames:
                    gotoStartButtonIsEnabled = true;
                    frameBackButtonIsEnabled = true;
                    playPauseButtonIsEnabled = true;
                    frameForwardButtonIsEnabled = true;
                    gotoEndButtonIsEnabled = true;
                    goToFrameTextBoxIsVisable = true;
                    break;

                case ViewMode.BestFrames:
                    gotoStartButtonIsEnabled = true;
                    frameBackButtonIsEnabled = true;
                    playPauseButtonIsEnabled = false;       // No play option in BestFrame view mode
                    frameForwardButtonIsEnabled = true;
                    gotoEndButtonIsEnabled = true;
                    goToFrameTextBoxIsVisable = true;
                    break;

                case ViewMode.FilterFrames:
                    throw new Exception("Not implemented");

                case ViewMode.SensorCoverage:
                    throw new Exception("Not implemented");
            }

            gotoStartButton.IsEnabled = gotoStartButtonIsEnabled;
            frameBackButton.IsEnabled = frameBackButtonIsEnabled;
            playPauseButton.IsEnabled = playPauseButtonIsEnabled;
            frameForwardButton.IsEnabled = frameForwardButtonIsEnabled;
            gotoEndButton.IsEnabled = gotoEndButtonIsEnabled;
            goToFrameTextBox.Visibility = goToFrameTextBoxIsVisable ? Visibility.Visible : Visibility.Collapsed;

        }


        /// <summary>
        /// Used by the user enter frame number events to jump to the request frame
        /// </summary>
        /// <param name="leftTrueRightFalse"></param>
        /// <param name="frameEditText"></param>
        private void UserGoToFrameRequest(bool leftTrueRightFalse, TextBox frameEditText)
        {
            if (AppModeCurrent == AppMode.Close) return; // Only allow manual jump if Open 

            int currentFrame = leftTrueRightFalse ? _currentFrameRight : _currentFrameRight;

            if (int.TryParse(frameEditText.Text, out int targetIndex) && targetIndex != currentFrame)
            {
                FrameJump(leftTrueRightFalse, targetIndex);
            }
        }




    }
}
