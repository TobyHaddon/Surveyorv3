using Emgu.CV;
using Emgu.CV.CvEnum;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.Calibration;
using Surveyor.Helper;
using SurveyorCalibrationData;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;


namespace Surveyor.Controls
{
    public sealed partial class UniversalCalibrationHeadUserControl : UserControl
    {
        // Remembered Head property
        // Needed because background thread can't access UI elements
        // Including the Head property
        bool? headTrueIsStereoFalseIsMode = null;

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
        private DispatcherTimer _playLeftTimer;
        private bool _isRightPlaying = false;
        private DispatcherTimer _playRightTimer;
        private bool _isBothPlaying = false;
        private DispatcherTimer _playBothTimer;

        private bool isLocked = false;

        private CalibrationStereoFrameSet calibrationStereoFrameSet;

        private CancellationToken cancellationToken;
        private CancellationTokenSource? cts = null;

        private bool isFindCalibrationFrameRunning = false;

        private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

        private readonly SafeUICall safeUICall;

        public enum AppMode { Close, Open, FindCalibrationsFrames, BestFramesCalc, BestFramesView, BestFramesSave };
        private AppMode appMode = AppMode.Open;

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

            SetDisplayMode(AppMode.Close);
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
        public bool SetupCalibrationBoardType(CalibrationBoardDefinition _charucoBoardDefinition) 
        {
            return calibrationStereoFrameSet.SetupCalibrationBoardType(_charucoBoardDefinition);
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
                        // Create WriteableBitmap with Emgu frame dimensions
                        wbLeft = new WriteableBitmap(testFrame.Width, testFrame.Height);
                        frameSize = new Size(testFrame.Width, testFrame.Height);

                        // Reset to first frame — Emgu uses .Set() with CapProp
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

            // Clear frameset values
            this.calibrationStereoFrameSet.ClearResults();
            CalibrationFrameSetViewerLeft.RefreshSensorBinLayers();
            CalibrationFrameSetViewerLeft.DrawGraphs();
            CalibrationFrameSetViewerRight.RefreshSensorBinLayers();
            CalibrationFrameSetViewerRight.DrawGraphs();

            // Clear images
            LeftImage.Source = null;
            _currentFrameLeft = 0;
            RightImage.Source = null;
            _currentFrameRight = 0;

            // Clear frame label
            LeftUpdateFrameLabel();
            RightUpdateFrameLabel();

            // Clear Metadata
            ClearFrameMetaData(true/*trueLeftfalseRight*/);
            ClearFrameMetaData(false/*trueLeftfalseRight*/);

            // Reset calibration output display display 
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
           // TODO
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
        /// Search the media for the calibration boards
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public async Task<int> FindCalibrationFrameAsync()
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
                    calibrationStereoFrameSet.FindCalibrationTimeLineRangeAsync(FrameProcessingCallbackFindCalibrationTimeLineRange, cancellationToken));

                if (startCalibration != -1 && stopCalibration != -1)
                {
                    // Update the timeline ranges
                    CalibrationBoardTimeLineLeft.CalibrationBoardRange(startCalibration, stopCalibration);
                    CalibrationBoardTimeLineRight.CalibrationBoardRange(startCalibration, stopCalibration);

                    // Next find the calibration frames with in that range
                    ret = await Task.Run(async () =>
                    {

                        try
                        {
                            return await calibrationStereoFrameSet.FindCalibrationsFramesAsync(
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

                    if (ret == -1)
                        Debug.WriteLine("FindCalibrationFrameAsync: User cancelled.");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Calibration search cancelled.");
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
        public async Task FindBestFramesNoUIAsync(CalibProject calibProject,
                                              bool trueLeftFalseRight,
                                              double movementMinThreshold, 
                                              double blurMinThreshold, 
                                              int monoCornersMinThreshold, 
                                              bool writeBestFramesToPng = true)
        {
            // Check we have a CalibrationStereoFrameSet and this is definately a Mono head
            if (calibrationStereoFrameSet is not null && headTrueIsStereoFalseIsMode == false)
            {
                try
                {
                    // Create a list of the best calibation frames best on the sensor bin only
                    if (trueLeftFalseRight)
                        Debug.WriteLine($"Mono Left SelectBestStereoFramesUsingSensorBinOnly,"+
                                        $" Min move={movementMinThreshold}, Min blur={blurMinThreshold},"+
                                        $" Corners threshold={monoCornersMinThreshold}:");
                    else
                        Debug.WriteLine($"Mono Right SelectBestStereoFramesUsingSensorBinOnly, "+
                                        $"Min move={movementMinThreshold}, Min blur={blurMinThreshold}, "+
                                        $"Corners threshold={monoCornersMinThreshold}:");

                    calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(movementMinThreshold, blurMinThreshold, monoCornersMinThreshold);

                    // Next top-up with pose diverse frames
                    calibrationStereoFrameSet.AddBestStereoFramesUsingPoseBins(movementMinThreshold, blurMinThreshold);

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
                    }


                    // Write frames to .png if requested
                    if (writeBestFramesToPng)
                    {
                        await SaveBestFilesAsync();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during best frames extraction: {ex.Message}");
                }
            }

            safeUICall.Call(() => BestFrameJump(0));
            safeUICall.Call(() => RightUpdateFrameLabel());

            return;
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
        public bool DoMonoCalibrationCalculationNoUI(CalibProject calibProject, 
                                                     bool trueLeftFalseRight,
                                                     int monoCornersMinThreshold)
        {
            bool ret = false;

            // Check we have a CalibrationStereoFrameSet and this is definately a Mono head
            if (calibrationStereoFrameSet is not null && headTrueIsStereoFalseIsMode == false)
            {
                try
                {
                    // Proceed to do the mono calibration using each the calibration paraemter set
                    foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                    {
                        // Calibration using the best frames (pass2 calibration)                    
                        MonoCalibrationCameraData? monoCalib2 = calibrationStereoFrameSet.MonoCalibrateUsingBestFrames(
                                                                                false/*trueStereoFalseMono*/,
                                                                                trueLeftFalseRight,
                                                                                frameSize,
                                                                                monoCornersMinThreshold,
                                                                                calibrationParameters);

                        // Remember the mono calibration data
                        if (trueLeftFalseRight)
                            calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters] = monoCalib2;
                        else
                            calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters] = monoCalib2;
                    }


                    // Display the mono calibration results
                    // Reset calibration output display display 
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
                            calibationText += "\n" + calibrationParameters.ToString() + ":\n";
                            calibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(monoCalibDisplay);
                        }
                    }

                    // Set calibration output display display 
                    safeUICall.Call(() => LeftCalibDataText.Text = calibationText);
                    safeUICall.Call(() => LeftCalibDataBorder.Visibility = Visibility.Visible);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during mono calibration calculation: {ex.Message}");
                }
            }

            safeUICall.Call(() => BestFrameJump(0));
            safeUICall.Call(() => LeftUpdateFrameLabel());
            //???RightUpdateFrameLabel();
            
            return ret;
        }


        /// <summary>
        /// Extract the best frames and do a stereo calibration.
        /// If it is called from a Stereo head both left and right are mono calibrated and the result 
        /// reported on screen.  However only the left MonoCalibrationCameraData array is returned
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public async Task<bool> BestFramesCalcAndStereoCalibrationAsync(
                                         CalibProject calibProject,
                                         double movementMinThreshold,
                                         double blurMinThreshold,
                                         int stereoCornersMinThreshold,
                                         bool writeBestFramesToPng = true)
        {
            bool ret = false;

            appMode = AppMode.BestFramesCalc;
            SetUIControls();

            if (calibrationStereoFrameSet is not null && headTrueIsStereoFalseIsMode == true)
            {
                // Proceed to do the stero  calibration using each the calibration paraemter set
                foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                {
                    MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                    MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                    if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                    {
                        // Create a list of the best calibation frames best on the sensor bin only
                        calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(movementMinThreshold, blurMinThreshold, stereoCornersMinThreshold);

                        // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                        await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(leftMonoCalibrationCameraData, rightMonoCalibrationCameraData, frameSize);

                        // Next top-up with pose diverse frames
                        calibrationStereoFrameSet.AddBestStereoFramesUsingPoseBins(movementMinThreshold, blurMinThreshold);

                        // Reset calibration output display display 
                        LeftCalibDataText.Text = string.Empty;
                        LeftCalibDataBorder.Visibility = Visibility.Collapsed;
                        RightCalibDataText.Text = string.Empty;
                        RightCalibDataBorder.Visibility = Visibility.Collapsed;

                        // DO STEREO CALBRATION 
                        string leftCalibationText = $"Image Size {frameSize.Width} x {frameSize.Height}\n";
                        string rightCalibationText = $"Image Size {frameSize.Width} x {frameSize.Height}\n";



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
                            leftCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(calibrationStereoCameraData);
                            rightCalibationText += "\n" + calibrationParameters.ToString() + ":\n";
                            rightCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(calibrationStereoCameraData);
                        }

                        // Set calibration output display display 
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
                    ret = true;

                    if (writeBestFramesToPng)
                    {
                        MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParametersBest];
                        MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParametersBest];

                        if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                        {
                            // Create a list of the best calibation frames best on the sensor bin only
                            calibrationStereoFrameSet.SelectBestStereoFramesUsingSensorBinOnly(movementMinThreshold, blurMinThreshold, stereoCornersMinThreshold);

                            // Parse the Frames and calculate the yaw and pitch for each frame using the pass1 calibration
                            await calibrationStereoFrameSet.CalculateFramesYawPitchAndPopulatePoseBinAsync(leftMonoCalibrationCameraData, rightMonoCalibrationCameraData, frameSize);

                            // Next top-up with pose diverse frames
                            calibrationStereoFrameSet.AddBestStereoFramesUsingPoseBins(movementMinThreshold, blurMinThreshold);


                            await SaveBestFilesAsync();
                        }
                    }
                }
            }

            appMode = AppMode.BestFramesView;
            BestFrameJump(0);
            LeftUpdateFrameLabel();
            RightUpdateFrameLabel();
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
        public int? LoadCachedResults(string cacheFileSpec)
        {
            int? ret = null;
            string messageText;

            // Check if the file exists (remove any zero byte file)
            DeleteIfZeroByteFile(cacheFileSpec);

            if (File.Exists(cacheFileSpec))
            {
                // Load the calibration frame set
                var json = CalibrationStereoFrameSet.LoadFromFile(cacheFileSpec);
                if (json is not null)
                {
                    calibrationStereoFrameSet = json;

                    CalibrationFrameSetViewerData dataLeft = new(true/*trueLeftFalseRight*/, calibrationStereoFrameSet);
                    CalibrationFrameSetViewerLeft.Data = dataLeft;
                    CalibrationFrameSetViewerLeft.RefreshSensorBinLayers();
                    CalibrationFrameSetViewerLeft.RefreshPoseBinLayers();
                    CalibrationFrameSetViewerLeft.DrawGraphs();

                    CalibrationFrameSetViewerData dataRight = new(false/*trueLeftFalseRight*/, calibrationStereoFrameSet);
                    CalibrationFrameSetViewerRight.Data = dataRight;
                    CalibrationFrameSetViewerRight.RefreshSensorBinLayers();
                    CalibrationFrameSetViewerRight.RefreshPoseBinLayers();
                    CalibrationFrameSetViewerRight.DrawGraphs();

                    ret = calibrationStereoFrameSet.Frames.Count;
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
        /// Focus lost - check if the values has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftFrameInfoTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (appMode != AppMode.Open) return; // Only allow manual jump in Open mode
            if (int.TryParse(LeftFrameInfoTextBox.Text, out int targetIndex) && targetIndex != _currentFrameLeft)
            {
                FrameJump(true/*left*/, targetIndex);
            }
        }


        /// <summary>
        /// Focus lost - check if the values has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightFrameInfoTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (appMode != AppMode.Open) return; // Only allow manual jump in Open mode
            if (int.TryParse(RightFrameInfoTextBox.Text, out int targetIndex) && targetIndex != _currentFrameRight)
            {
                FrameJump(false/*right*/, targetIndex);
            }
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
                FrameCalibrationData? leftFrameCalibrationTarget,
                int rightFrameIndex,
                Mat? rightMat,
                FrameCalibrationData? rightFrameCalibrationTarget,
                int correspondingCount,
                object? userData)
        {
            safeUICall.Call(() => {

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
                FrameCalibrationData? leftFrameCalibrationTarget,
                int rightFrameIndex,
                Mat? rightMat,
                FrameCalibrationData? rightFrameCalibrationTarget,
                int correspondingCount,
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
                        CalibrationFrameSetViewerLeft.RefreshSensorBinLayers();
                        CalibrationFrameSetViewerLeft.DrawGraphs();
                    }
                    if (rightFrameCalibrationTarget is not null)
                    {
                        CalibrationFrameSetViewerRight.RefreshSensorBinLayers();
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
                                            rightFrameCalibrationTarget.CharucoCorners.Length /*Size*/,
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
        private static (double movementFromPrevious, double movementFactor, double movementToNext) GetMovementFactors(FrameCalibrationData? frameCalibrationTarget)
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
                MovementFactor = LeftMovementFactor;
                BlurFactor = LeftBlurFactor;
                FeatureCount = LeftFeatureCount;
                Score = LeftScore;
                Yaw = LeftYaw;
                Pitch = LeftPitch;
            }
            else
            {
                MovementFactor = RightMovementFactor;
                BlurFactor = RightBlurFactor;
                FeatureCount = RightFeatureCount;
                Score = RightScore;
                Yaw = RightYaw;
                Pitch = RightPitch;
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
            if (correspondingCount != -1)
                FeatureCount.Text = $"Corners: {featureCount}({correspondingCount})";
            else
                FeatureCount.Text = $"Corners: {featureCount}";

            // Score
            Score.Text = $"Score: {score:F2}";

            // Yaw
            if (yaw != 0.0)
                Yaw.Text = $"Yaw: {yaw:F2}";
            else
                Yaw.Text = string.Empty;

            // Pitch
            if (pitch != 0.0)
                Pitch.Text = $"Pitch: {pitch:F2}";
            else
                Pitch.Text = string.Empty;
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
                LeftYaw.Text = string.Empty;
                LeftPitch.Text = string.Empty;
            }
            else
            {
                RightMovementFactor.Text = string.Empty;
                RightBlurFactor.Text = string.Empty;
                RightFeatureCount.Text = string.Empty;
                RightScore.Text = string.Empty;
                RightYaw.Text = string.Empty;
                RightPitch.Text = string.Empty;
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

                FrameCalibrationData? leftTarget = null;
                FrameCalibrationData? rightTarget = null;

                try
                {
                    // Get stereo frame pair
                    (leftTarget, rightTarget, int correspondingCount) = calibrationStereoFrameSet.Frames[targetIndex];

                    UpdateFrameMetaData(true/*leftTrueRightFalse*/,
                                leftTarget.MovementFactor,
                                leftTarget.MovementFromPrevious,
                                leftTarget.MovementToNext,
                                leftTarget.BlurFactor,
                                leftTarget.CharucoCorners.Length /*Size*/,
                                leftTarget.Score,
                                leftTarget.YawDeg,
                                leftTarget.PitchDeg,
                                0);

                    if (rightTarget is not null)
                        UpdateFrameMetaData(false/*leftTrueRightFalse*/,
                                    rightTarget.MovementFactor,
                                    rightTarget.MovementFromPrevious,
                                    rightTarget.MovementToNext,
                                    rightTarget.BlurFactor,
                                    rightTarget.CharucoCorners.Length /*Size*/,
                                    rightTarget.Score,
                                    rightTarget.YawDeg,
                                    rightTarget.PitchDeg,
                                    0);
                }
                catch
                {
                    ClearFrameMetaData(false/*leftTrueRightFalse*/);
                }

                _JumpFrame(true/*leftTrueRightFalse*/, leftFrame, leftTarget);
                _JumpFrame(false/*leftTrueRightFalse*/, rightFrame, rightTarget);
                LeftUpdateFrameLabel();
                RightUpdateFrameLabel();

            }
            else if (leftTrueRightFalse && capLeft != null && wbLeft != null)
            {

                FrameCalibrationData? leftTarget = null;

                try
                {
                    // Get left mono frame data
                    (leftTarget, _, _) = calibrationStereoFrameSet.Frames[targetIndex];

                    UpdateFrameMetaData(true/*leftTrueRightFalse*/,
                                leftTarget.MovementFactor,
                                leftTarget.MovementFromPrevious,
                                leftTarget.MovementToNext,
                                leftTarget.BlurFactor,
                                leftTarget.CharucoCorners.Length /*Size*/,
                                leftTarget.Score,
                                leftTarget.YawDeg,
                                leftTarget.PitchDeg,
                                0);
                }
                catch
                {
                    ClearFrameMetaData(false/*leftTrueRightFalse*/);
                }

                _JumpFrame(true/*leftTrueRightFalse*/, targetIndex, leftTarget);
                LeftUpdateFrameLabel();

            }
            else if (!leftTrueRightFalse && capRight != null && wbRight != null)
            {

                FrameCalibrationData? rightTarget = null;

                try
                {
                    // Get left mono frame data
                    (rightTarget, _, _) = calibrationStereoFrameSet.Frames[targetIndex];

                    UpdateFrameMetaData(false/*leftTrueRightFalse*/,
                                rightTarget.MovementFactor,
                                rightTarget.MovementFromPrevious,
                                rightTarget.MovementToNext,
                                rightTarget.BlurFactor,
                                rightTarget.CharucoCorners.Length /*Size*/,
                                rightTarget.Score,
                                rightTarget.YawDeg,
                                rightTarget.PitchDeg,
                                0);
                }
                catch
                {
                    ClearFrameMetaData(false/*leftTrueRightFalse*/);
                }

                _JumpFrame(false/*leftTrueRightFalse*/, targetIndex, rightTarget);
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
                // Check the request best frame index is with range
                if (targetIndex < 0)
                    targetIndex = 0;

                if (targetIndex >= calibrationStereoFrameSet.BestFrameIndexes.Count)
                    targetIndex = calibrationStereoFrameSet.BestFrameIndexes.Count - 1;

                try
                {
                    // Get the absolute frame index from the best frame index
                    int frameIndex = calibrationStereoFrameSet.BestFrameIndexes[targetIndex];

                    // Get stereo frame pair
                    (FrameCalibrationData leftTarget, FrameCalibrationData? rightTarget, int correspondingCount) = calibrationStereoFrameSet.Frames[frameIndex];

                    // Jump to the left side best frame
                    _JumpFrame(true/*leftTrueRightFalse*/, leftTarget.FrameIndex, leftTarget);

                    // Update left side the information fields
                    UpdateFrameMetaData(true/*leftTrueRightFalse*/,
                                            leftTarget.MovementFactor, leftTarget.MovementFromPrevious, leftTarget.MovementToNext,
                                            leftTarget.BlurFactor,
                                            leftTarget.CharucoCorners.Length /*Size*/,
                                            leftTarget.Score,
                                            leftTarget.YawDeg,
                                            leftTarget.PitchDeg,
                                            correspondingCount);

                    // Indicate which of the bin this frame is found in
                    CalibrationFrameSetViewerLeft.HighLightActiveSensorBinLayers(leftTarget);
                    CalibrationFrameSetViewerLeft.HighLightActivePoseBinLayers(leftTarget);

                    if (rightTarget is not null)
                    {
                        // Jump to the right side best frame
                        _JumpFrame(false/*leftTrueRightFalse*/, rightTarget.FrameIndex, rightTarget);

                        // Update right side the information fields
                        UpdateFrameMetaData(false/*leftTrueRightFalse*/,
                                                rightTarget.MovementFactor,
                                                rightTarget.MovementFromPrevious,
                                                rightTarget.MovementToNext,
                                                rightTarget.BlurFactor,
                                                rightTarget.CharucoCorners.Length /*Size*/,
                                                rightTarget.Score,
                                                rightTarget.YawDeg,
                                                rightTarget.PitchDeg,
                                                correspondingCount);

                        // Indicate which of the bin this frame is found in
                        CalibrationFrameSetViewerRight.HighLightActiveSensorBinLayers(rightTarget);
                        CalibrationFrameSetViewerRight.HighLightActivePoseBinLayers(leftTarget);

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
                    ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb, null);
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
                    ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb, null);
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

        private void _JumpFrame(bool leftTrueRightFalse, int targetIndex, FrameCalibrationData? frameCalibrationData)
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
                    ProcessFrame(leftTrueRightFalse, frameIndex, mat, wb, frameCalibrationData);
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

        private void ProcessFrame(bool leftTrueRightFalse, int frameIndex, Mat frame, WriteableBitmap wb, FrameCalibrationData? frameCalibrationData)
        {
            switch (appMode)
            {
                case AppMode.Open:
                case AppMode.BestFramesView:
                case AppMode.BestFramesCalc:
                    try
                    {
                        if (frameCalibrationData is not null && headTrueIsStereoFalseIsMode is not null)
                            CalibrationStereoFrameSet.DrawMarkersToMat(frameCalibrationData, frame, (bool)headTrueIsStereoFalseIsMode);

                        DrawFrameToScreen(frame, wb);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ProcessFrame: Error processing ChArUco board, appMode:{appMode}, {ex.Message}");
                    }

                    SetUIControls();
                    break;

                case AppMode.FindCalibrationsFrames:
                case AppMode.BestFramesSave:
                    DrawFrameToScreen(frame, wb);
                    break;
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
                        frameText = $"Frame {currentFrame} / {totalFrames - 1}, Time {time:F2}s";
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
        //public static string MakeCalibrationStereoFrameSetPath(string leftMediaFileSpec, string rightMediaFileSpec)
        //{
        //    // Extract the filename without extension
        //    string baseName = string.Empty;
        //    if (leftMediaFileSpec != string.Empty && rightMediaFileSpec != string.Empty)
        //    {
        //        baseName = Path.GetFileNameWithoutExtension(leftMediaFileSpec) + "_" + Path.GetFileNameWithoutExtension(rightMediaFileSpec);
        //    }
        //    else if (leftMediaFileSpec != string.Empty)
        //    {
        //        baseName = Path.GetFileNameWithoutExtension(leftMediaFileSpec);
        //    }

        //    if (!string.IsNullOrEmpty(baseName))
        //    {
        //        // Build new filename
        //        string filename = $"{baseName}-CalibrationStereoFrameSet.json";

        //        // Get local folder path
        //        StorageFolder localFolder = ApplicationData.Current.LocalFolder;

        //        // Combine into full path
        //        string fullPath = Path.Combine(localFolder.Path, filename);

        //        return fullPath;
        //    }
        //    else
        //        return string.Empty;
        //}


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
        private async Task<bool> SaveBestFilesAsync()
        {
            bool ret = false;

            // Remember the current app mode
            AppMode appModeOld = appMode;

            // Set the app mode to 
            appMode = AppMode.BestFramesSave;

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
                        (FrameCalibrationData leftTarget, FrameCalibrationData? rightTarget, _) = calibrationStereoFrameSet.Frames[frameIndex];

                        // Force the frame with MoveJump
                        _JumpFrame(true/*trueLeftFalseRight*/, leftTarget.FrameIndex, leftTarget);

                        if (rightTarget is not null)
                            _JumpFrame(false/*trueLeftFalseRight*/, rightTarget.FrameIndex, rightTarget);

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

                        ret = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SaveBestFiles: Error saving frame {frameIndex} to path:[{imageOutputSubFolder}], {ex.Message}");
                    }
                }
            }

            // Restore the original app mode
            appMode = appModeOld;


            return ret;
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

 

        public void SetDisplayMode(AppMode newAppMode)
        {
            appMode = newAppMode;
                
            bool leftFrameBackButtonIsEnabled = true;
            bool leftPlayPauseButtonIsEnabled = true;
            bool leftFrameForwardButtonIsEnabled = true;
            bool rightFrameBackButtonIsEnabled = true;
            bool rightPlayPauseButtonIsEnabled = true;
            bool rightFrameForwardButtonsEnabled = true;
            bool leftFrameInfoTextBoxIsVisable = true;
            bool rightFrameInfoTextBoxIsVisable = true;

            if (appMode == AppMode.Close)
            {
                leftFrameBackButtonIsEnabled = false;
                leftPlayPauseButtonIsEnabled = false;
                leftFrameForwardButtonIsEnabled = false;
                rightFrameBackButtonIsEnabled = false;
                rightPlayPauseButtonIsEnabled = false;
                rightFrameForwardButtonsEnabled = false;
                leftFrameInfoTextBoxIsVisable = false;
                rightFrameInfoTextBoxIsVisable = false;
            }
            if (appMode == AppMode.Open || appMode == AppMode.BestFramesView)
            {
                // All Flag set correct;y

            }
            else if (appMode == AppMode.FindCalibrationsFrames)
            {
                    leftFrameBackButtonIsEnabled = false;
                    leftPlayPauseButtonIsEnabled = false;
                    leftFrameForwardButtonIsEnabled = false;
                    leftFrameInfoTextBoxIsVisable = false;

                    rightFrameBackButtonIsEnabled = false;
                    rightPlayPauseButtonIsEnabled = false;
                    rightFrameForwardButtonsEnabled = false;
                    rightFrameInfoTextBoxIsVisable = false;

                // Clear the metadata display fields
                ClearFrameMetaData(true/*trueLeftfalseRight*/);
                ClearFrameMetaData(false/*trueLeftfalseRight*/);

            }
            else if (appMode == AppMode.BestFramesCalc)
            {
                leftFrameBackButtonIsEnabled = false;
                leftPlayPauseButtonIsEnabled = false;
                leftFrameForwardButtonIsEnabled = false;
                leftFrameInfoTextBoxIsVisable = false;
                rightPlayPauseButtonIsEnabled = false;   // No play - only frame forward/back

                rightFrameBackButtonIsEnabled = false;
                rightPlayPauseButtonIsEnabled = false;
                rightFrameForwardButtonsEnabled = false;
                rightFrameInfoTextBoxIsVisable = false;
                leftPlayPauseButtonIsEnabled = false;   // No play - only frame forward/back
            }

            LeftFrameBackButton.IsEnabled = leftFrameBackButtonIsEnabled;
            LeftPlayPauseButton.IsEnabled = leftPlayPauseButtonIsEnabled;
            LeftFrameForwardButton.IsEnabled = leftFrameForwardButtonIsEnabled;
            LeftFrameInfoTextBox.Visibility = leftFrameInfoTextBoxIsVisable ? Visibility.Visible : Visibility.Collapsed;

            RightFrameBackButton.IsEnabled = rightFrameBackButtonIsEnabled;
            RightPlayPauseButton.IsEnabled = rightPlayPauseButtonIsEnabled;
            RightFrameForwardButton.IsEnabled = rightFrameForwardButtonsEnabled;
            RightFrameInfoTextBox.Visibility = rightFrameInfoTextBoxIsVisable ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
