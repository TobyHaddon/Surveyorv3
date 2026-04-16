// Ignore Spelling: calib Uco

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Flann;
using iText.Commons.Bouncycastle.Security;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Org.BouncyCastle.Bcpg;
using Surveyor.Calibration;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WinUIEx;
using static Surveyor.Controls.UniversalCalibrationHead;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace Surveyor.Controls
{
    public sealed partial class UniversalCalibrationHead : UserControl
    {
        // HeadType enum
        public enum HeadType
        {
            Stereo,
            MonoLeft,
            MonoRight
        }

        // Store the head type as an enum
        private HeadType? headType = null;

        // Reporter
        private Reporter? report = null;

        // Constant text
        private const string ManuallyAddedText = "Add";
        private const string ManuallyIgnoredText = "Ignore";

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

        // CharUco board definition
        private CalibrationBoardDefinition? calibrationBoardDefinition;

        // Used to de-bounce sensor coverage rendering
        private bool _sensorCoverageRenderQueued;

        // Expose the play/pause button for external binding teaching tip (read-only)
        public Button LeftPlayPauseButtonElement => LeftPlayPauseButton;


        // XAML Attribute to indicate the head type (Stereo, MonoLeft, or MonoRight)
        public static readonly DependencyProperty HeadProperty =
            DependencyProperty.Register(nameof(Head), typeof(string), typeof(UniversalCalibrationHead),
                new PropertyMetadata(null, OnHeadChanged));

        public string Head
        {
            get => (string)GetValue(HeadProperty);
            set => SetValue(HeadProperty, value);
        }

        private static void OnHeadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UniversalCalibrationHead ctrl)
            {
                ctrl.ApplyHeadMode((string)e.NewValue);
            }
        }

        private void ApplyHeadMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                throw new InvalidOperationException("Head property must be set to 'Stereo', 'MonoLeft', or 'MonoRight'");
            }

            switch (mode.ToLowerInvariant())
            {
                case "monoleft":
                    // Hide column 1
                    RootGrid.ColumnDefinitions[1].Width = new GridLength(0);
                    headType = HeadType.MonoLeft;
                    break;

                case "monoright":
                    // Hide column 1
                    RootGrid.ColumnDefinitions[1].Width = new GridLength(0);
                    headType = HeadType.MonoRight;
                    break;

                case "stereo":
                    // Show Column 1
                    RootGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    headType = HeadType.Stereo;
                    break;

                default:
                    throw new InvalidOperationException($"Unknown Head mode: '{mode}'. Must be 'Stereo', 'MonoLeft', or 'MonoRight'.");
            }

            if (headType is not null)
            {
                if (IsHeadMono())
                    // Both left and right mono heads used the left side controls only
                    MediaTimeLineDisplayLeft.SetHeadType((HeadType)headType);
                else if (IsHeadStereo())
                {
                    MediaTimeLineDisplayLeft.SetHeadType((HeadType)headType);
                    MediaTimeLineDisplayRight.SetHeadType((HeadType)headType);
                }
            }
        }

        /// <summary>
        /// Check if the head is in Stereo mode
        /// </summary>
        public bool IsHeadStereo() => headType == HeadType.Stereo;

        /// <summary>
        /// Check if the head is in MonoLeft mode
        /// </summary>
        public bool IsHeadMono() => headType == HeadType.MonoLeft || headType == HeadType.MonoRight;

        /// <summary>
        /// Check if the head is in MonoLeft mode
        /// </summary>
        public bool IsHeadMonoLeft() => headType == HeadType.MonoLeft;

        /// <summary>
        /// Check if the head is in MonoRight mode
        /// </summary>
        public bool IsHeadMonoRight() => headType == HeadType.MonoRight;


        // New XAML attribute to title the head (propagates to child viewers)
        public static readonly DependencyProperty HeadTitleProperty =
            DependencyProperty.Register(nameof(HeadTitle), typeof(string), typeof(UniversalCalibrationHead),
                new PropertyMetadata(string.Empty, OnHeadTitleChanged));

        public string HeadTitle
        {
            get => (string)GetValue(HeadTitleProperty);
            set => SetValue(HeadTitleProperty, value);
        }

        private static void OnHeadTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UniversalCalibrationHead ctrl)
            {
                ctrl.ApplyHeadTitle();
            }
        }

        private void ApplyHeadTitle()
        {
            string suffix = HeadTitle ?? string.Empty;
            // Prefix with Left/Right as requested
            if (IsHeadStereo())
            {
                CalibrationFrameSetViewerLeft?.SetTitle("Left " + suffix);
                CalibrationFrameSetViewerRight?.SetTitle("Right " + suffix);
                // Only is one left side iteration viewer
                CalibrationIterationViewerLeft?.SetTitle("Left " + suffix);

            }
            else
            {
                CalibrationFrameSetViewerLeft?.SetTitle(suffix);
                CalibrationIterationViewerLeft?.SetTitle("Left " + suffix);
            }
        }

        public UniversalCalibrationHead()
        {
            // Get the DispatcherQueue for the current thread
            dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            safeUICall = new(dispatcherQueue);

            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UniversalCalibrationHead.InitializeComponent failed: {ex}");
                throw;
            }

            // Set the CalibrationStereoFrameSet
            calibrationStereoFrameSet = new();

            _playLeftTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _playLeftTimer.Tick += (s, e) => PlayLeft();

            _playRightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _playRightTimer.Tick += (s, e) => PlayRight();

            _playBothTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _playBothTimer.Tick += (s, e) => PlayBoth();


            CalibrationFrameSetViewerData dataLeft = new(_trueLeftFalseRight: true, calibrationStereoFrameSet);
            CalibrationFrameSetViewerData dataRight = new(_trueLeftFalseRight: false, calibrationStereoFrameSet);

            CalibrationFrameSetViewerLeft.Data = dataLeft;
            CalibrationFrameSetViewerRight.Data = dataRight;

            // Setup the jump frame handlers
            MediaTimeLineDisplayLeft.JumpToFrameRequested -= JumpToFrameRequestedHandlerLeft;
            MediaTimeLineDisplayLeft.JumpToFrameRequested += JumpToFrameRequestedHandlerLeft;
            MediaTimeLineDisplayRight.JumpToFrameRequested -= JumpToFrameRequestedHandlerRight;
            MediaTimeLineDisplayRight.JumpToFrameRequested += JumpToFrameRequestedHandlerRight;

            // Ensure correct layout at initialization (Note the correct Head value isn't set yet)
            this.Loaded += (_, _) =>
            {
                ApplyHeadMode(Head);
                ApplyHeadTitle();

                SetAppMode(AppMode.Close);
            };

            this.Unloaded += StereoCalibrationHeadUserControl_Unloaded;    
        }


        /// <summary>
        /// Override of ToString
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return headType switch
            {
                null => "Head type not set!",
                HeadType.Stereo => "Stereo",
                HeadType.MonoLeft => "Mono Left",
                HeadType.MonoRight => "Mono Right",
                _ => headType.ToString() ?? "Head type not set!"
            };
        }


        /// <summary>
        /// Set the Reporter, used to output messages.
        /// Call as early as possible after creating the class instance.
        /// </summary>
        /// <param name="_report"></param>
        public void SetReporter(Reporter _report)
        {
            report = _report;

            calibrationStereoFrameSet.SetReporter(report);
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
                throw new InvalidOperationException($"{ToString()} Unexpected theme value: {theme}");
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
            calibrationBoardDefinition = _chArUcoBoardDefinition;
            return calibrationStereoFrameSet.SetupCalibrationBoardType(_chArUcoBoardDefinition);
        }


        /// <summary>
        /// Reset all internal values
        /// Note see ClearResults(...) to clear the CalibrationStereoFrameSet 
        /// values and align the display accordingly
        /// </summary>
        public void Clear()
        {
            // Stop the play timers
            _playLeftTimer.Stop();
            _playRightTimer.Stop();
            _playBothTimer.Stop();
            _isLeftPlaying = false;
            _isRightPlaying = false;
            _isBothPlaying = false;


            frameSize = new(0.0, 0.0);
            wbLeft = null;
            wbRight = null;

            // Reset total frame counts
            _totalFramesLeft = -1;
            _totalFramesRight = -1;

            // Reset current frame indexes
            _currentFrameLeft = 0;
            _currentFrameRight = 0;

            // Reset current Best frame Indexes
            _currentBestFrame = 0;

            // Reset stereo lock state
            isLocked = false;

            // Reset frame set
            calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.All);

            // Workflow not running
            isFindCalibrationFrameRunning = false;

            leftMediaFileSpec = string.Empty;
            rightMediaFileSpec = string.Empty;
            capLeft = null;
            capRight = null;

            // Clear the 'Go to' TextBox
            LeftGoToFrameTextBox.Text = string.Empty;
            RightGoToFrameTextBox.Text = string.Empty;

            // New the media time line tool tip to one for a new project
            MediaTimeLineDisplayLeft.SetToolTipNewProject();
            MediaTimeLineDisplayRight.SetToolTipNewProject();
        }


        /// <summary>
        /// Clear the CalibrationStereoFrameSet 
        /// values and align the display accordingly
        /// Note see Clear() to reset all internal values (full close down)
        /// </summary>
        /// <param name=""></param>
        public void ClearResults(CalibProject calibProject, CalibrationStereoFrameSet.ClearRequest clearRequest)
        {

            switch (clearRequest)
            {
                case CalibrationStereoFrameSet.ClearRequest.StartStopCalibrationBoardZone:
                    // Clear the calibration board zone values and visualization of the timeline
                    calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.StartStopCalibrationBoardZone);
                    MediaTimeLineDisplayLeft.Clear();
                    MediaTimeLineDisplayRight.Clear();
                    break;

                case CalibrationStereoFrameSet.ClearRequest.FrameSets:
                    // Clear the frame sets and the display if necessary
                    calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.FrameSets);

                    // Clear the graphs, sensor and pose bins
                    ClearDisplay(ViewModeCurrent);

                    // Clear frame UI display data
                    DecorateClear(trueLeftFalseRight: true);
                    DecorateClear(trueLeftFalseRight: false);
                    break;

                case CalibrationStereoFrameSet.ClearRequest.BestFrames_All:
                case CalibrationStereoFrameSet.ClearRequest.BestFrames_AutoOnly:
                    // Guard
                    if ((clearRequest == CalibrationStereoFrameSet.ClearRequest.All ||
                         clearRequest == CalibrationStereoFrameSet.ClearRequest.BestFrames_All ||
                         clearRequest == CalibrationStereoFrameSet.ClearRequest.BestFrames_AutoOnly) && calibProject is null)
                    {
                        throw new ArgumentNullException(nameof(calibProject), $"'{nameof(calibProject)}' is required for ClearResultsSafeUIAsync with a ClearRequest of {clearRequest}.");
                    }
                    if ((clearRequest == CalibrationStereoFrameSet.ClearRequest.All ||
                         clearRequest == CalibrationStereoFrameSet.ClearRequest.BestFrames_All ||
                         clearRequest == CalibrationStereoFrameSet.ClearRequest.BestFrames_AutoOnly) && headType is null)
                    {
                        if (headType is not null)
                            report?.Error(ChannelConvert((HeadType)headType), $"'{nameof(headType)}' is required for ClearResultsSafeUIAsync with a ClearRequest of {clearRequest}.");
                        return;
                    }

                    // Clear the best frame list if necessary (no UI work)
                    switch (clearRequest)
                    {
                        case CalibrationStereoFrameSet.ClearRequest.BestFrames_All:
                            if (headType is not null && calibProject is not null)
                                calibProject.Data.CalibrationInputs.RemoveAllBestFrames(ConvertHeadType((HeadType)headType), trueAllFalsePreserveManual: true);
                            break;
                        case CalibrationStereoFrameSet.ClearRequest.BestFrames_AutoOnly:
                            if (headType is not null && calibProject is not null)
                                calibProject.Data.CalibrationInputs.RemoveAllBestFrames(ConvertHeadType((HeadType)headType), trueAllFalsePreserveManual: false);
                            break;
                    }

                    // Clear the graphs, sensor and pose bins
                    ClearDisplay(ViewModeCurrent);

                    // Clear frame UI display data
                    DecorateClear(trueLeftFalseRight: true);
                    DecorateClear(trueLeftFalseRight: false);

                    // Clear sensor canvas
                    LeftSensorCoverage.Children.Clear();
                    RightSensorCoverage.Children.Clear();
                    break;

            }
        }

        /// <summary>
        /// Used to clear the actual values and the UI elements that display those 
        /// values.
        /// Note calibProject is only required if BestFrames_All or BestFrames_AutoOnly.
        /// Otherwise it can be null
        /// </summary>
        /// <param name="calibProject"></param>
        /// <param name="clearRequest"></param>
        /// <returns></returns>
        public Task ClearResultsSafeUIAsync(CalibProject? calibProject, CalibrationStereoFrameSet.ClearRequest clearRequest)
        {
            // Ensure the ClearResults work runs on the UI thread and completes before returning.
            return safeUICall.CallAsync(() =>
            {
                // calibProject is check inside ClearResults for the cases where it is required,
                // so we can pass null here if not needed without causing an exception
                ClearResults(calibProject!, clearRequest);
                return Task.CompletedTask;
            });
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
            Clear();

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
                        MediaTimeLineDisplayLeft.Visibility = Visibility.Visible;
                        MediaTimeLineDisplayLeft.SetRange(0, _totalFramesLeft, clearData: true);

                        // Display first frame
                        FrameJump(trueLeftFalseRight: true, 0);

                        leftOpened = true;
                    }
                }
            }
            else
            {
                if (IsHeadStereo())
                    Debug.WriteLine($"OpenMedia: Left stereo media {_leftMediaFileSpec} does not exist.");
                else
                    Debug.WriteLine($"OpenMedia: Media {_leftMediaFileSpec} does not exist.");
            }

            // If Stereo open right side
            if (IsHeadStereo())
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
                            MediaTimeLineDisplayRight.SetRange(0, _totalFramesRight, clearData: true);
                            MediaTimeLineDisplayRight.Visibility = Visibility.Visible;

                            // Display first frame
                            FrameJump(trueLeftFalseRight: false, 0);

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
                if (IsHeadStereo())
                    calibrationStereoFrameSet.SetupMediaStereo(capLeft, capRight);
                else
                    calibrationStereoFrameSet.SetupMediaMono(capLeft);
            }

            await Task.Delay(100); // Allow UI to update

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

            // Clear and hide the media timeline
            MediaTimeLineDisplayLeft.Clear();
            MediaTimeLineDisplayLeft.Visibility = Visibility.Collapsed;
            if (IsHeadStereo())
            {
                MediaTimeLineDisplayRight.Clear();
                MediaTimeLineDisplayRight.Visibility = Visibility.Collapsed;
            }

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
            this.calibrationStereoFrameSet.ClearResults(CalibrationStereoFrameSet.ClearRequest.All);

            // Clear the graphs, sensor and pose bins
            ClearDisplay(ViewModeCurrent);

            // Clear images
            LeftImage.Source = null;
            RightImage.Source = null;

            // Clear frame UI display data
            DecorateClear(trueLeftFalseRight: true);
            if (IsHeadStereo())
                DecorateClear(trueLeftFalseRight: false);

            // Reset calibration output display
            ClearCalibrationResultsDisplay();

            // Clear internals
            Clear();

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

            if (IsHeadStereo())
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
                Debug.Assert(true, $"{ToString()} LockStero should not be called to unlock the media");
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
            if (IsHeadStereo())
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
        public double GetMaxMovement(CalibProject? calibProject, bool trueNormalFalseBestFrame)
        {
            // Guard
            if (headType is null)
                return -1;
            if (calibrationStereoFrameSet is null)
                return -1;

            if (trueNormalFalseBestFrame)
            {
                return calibrationStereoFrameSet.MaxMovementFactor;
            }
            else
            {
                if (calibProject is not null)
                {
                    // Get the appropriate best frame list
                    List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType((HeadType)headType));

                    if (bestFramesList is not null)
                        return calibrationStereoFrameSet.MaxBestMovementFactor(bestFramesList);
                    else
                        return -1;
                }
            }

            return -1;
        }


        /// <summary>
        /// Return the smallest movement in the calibration frames or the best frames.
        /// </summary>
        /// <param name="trueNormalFalseBestFrame"></param>
        /// <returns></returns>
        public double GetMinMovement(CalibProject calibProject, bool trueNormalFalseBestFrame)
        {
            // Guard
            if (headType is null)
                return -1;
            if (calibrationStereoFrameSet is null)
                return -1;

            if (trueNormalFalseBestFrame)
            {
                return calibrationStereoFrameSet.MinMovementFactor;
            }
            else
            {
                // Get the appropriate best frame list
                List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType((HeadType)headType));

                if (bestFramesList is not null)
                    return calibrationStereoFrameSet.MinBestMovementFactor(bestFramesList);
                else
                    return -1;
            }
        }


        /// <summary>
        /// Return the largest blur in the calibration frames or the best frames.
        /// </summary>
        /// <param name="trueNormalFalseBestFrame"></param>
        /// <returns></returns>
        public double GetMaxBlur(CalibProject calibProject, bool trueNormalFalseBestFrame)
        {
            // Guard
            if (headType is null)
                return -1;
            if (calibrationStereoFrameSet is null)
                return -1;

            if (trueNormalFalseBestFrame)
            {
                return calibrationStereoFrameSet.MaxBlurFactor;
            }
            else
            {
                // Get the appropriate best frame list
                List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType((HeadType)headType));

                if (bestFramesList is not null)
                    return calibrationStereoFrameSet.MaxBestBlurFactor(bestFramesList);
                else
                    return -1;
            }
        }


        /// <summary>
        /// Return the largest blur in the calibration frames or the best frames.
        /// </summary>
        /// <param name="trueNormalFalseBestFrame"></param>
        /// <returns></returns>
        public double GetMinBlur(CalibProject calibProject, bool trueNormalFalseBestFrame)
        {
            // Guard
            if (headType is null)
                return -1;
            if (calibrationStereoFrameSet is null)
                return -1;

            if (trueNormalFalseBestFrame)
            {
                return calibrationStereoFrameSet.MinBlurFactor;
            }
            else
            {
                // Get the appropriate best frame list
                List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType((HeadType)headType));

                if (bestFramesList is not null)
                    return calibrationStereoFrameSet.MinBestBlurFactor(bestFramesList);
                else
                    return -1;
            }
        }


        /// <summary>
        /// Clear calibration results from the UI
        /// </summary>
        public void ClearCalibrationResultsDisplay()
        {
            LeftCalibDataText.Text = string.Empty;
            LeftCalibDataBorder.Visibility = Visibility.Collapsed;

            // There is no right side calibration results TextBlock
        }


        /// <summary>
        /// Display the calibration information stored inside the CalibProject
        /// </summary>
        public void DisplayCalibrationInfoSafeUI(CalibProject calibProject, bool? trueLeftFalseRightNullStereo)
        {
            // Reset left calibration output display if left mono, right mono (which used the left side) or stereo
            safeUICall.Call(() =>
            {
                LeftCalibDataText.Text = string.Empty;
                LeftCalibDataBorder.Visibility = Visibility.Collapsed;
            });


            if (calibrationStereoFrameSet is not null)
            {
                // Calibration result string (start with the Image Size)
                string leftCalibationText = $"Image Size {frameSize.Width} x {frameSize.Height}\n";
                string rightCalibationText = $"Image Size {frameSize.Width} x {frameSize.Height}\n";

                bool leftNumberOfImagedUsed = false;
                bool rightNumberOfImagedUsed = false;

                // Proceed to do the stereo calibration using each calibration parameter 
                foreach (CalibrationParameters calibrationParameters in Enum.GetValues(typeof(CalibrationParameters)))
                {
                    //???Debug.WriteLine($"{ToString()} DisplayCalibrationInfoSafeUI: {calibrationParameters}");

                    // Display mono results, either left or right
                    if (trueLeftFalseRightNullStereo is not null)
                    {
                        if (trueLeftFalseRightNullStereo == true)
                        {
                            // Left mono
                            MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];

                            if (leftMonoCalibrationCameraData is not null)
                            {
                                // Indicate the number of images used in the calibration calculation
                                if (!leftNumberOfImagedUsed)
                                {
                                    leftCalibationText += $"Images Used: {leftMonoCalibrationCameraData.ImagesUsed}\n";
                                    leftNumberOfImagedUsed = true;
                                }

                                leftCalibationText += "\n" + calibrationParameters.ToString() + ":\n";
                                leftCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(leftMonoCalibrationCameraData);

                                // Set calibration output display 
                                safeUICall.Call(() =>
                                {
                                    LeftCalibDataText.Text = leftCalibationText;
                                    LeftCalibDataBorder.Visibility = Visibility.Visible;
                                    AnimateCalibrationTextUpdated();
                                });
                            }
                        }
                        else
                        {
                            // Right Mono (but display on left side control of the right mono head)
                            MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                            if (rightMonoCalibrationCameraData is not null)
                            {
                                // Indicate the number of images used in the calibration calculation
                                if (!rightNumberOfImagedUsed)
                                {
                                    rightCalibationText += $"Images Used: {rightMonoCalibrationCameraData.ImagesUsed}\n";
                                    rightNumberOfImagedUsed = true;
                                }

                                rightCalibationText += "\n" + calibrationParameters.ToString() + ":\n";
                                rightCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(rightMonoCalibrationCameraData);

                                // Note. We used the left side display control only for a right mono head
                                // even if 'trueLeftFalseRight == false'
                                safeUICall.Call(() =>
                                {
                                    LeftCalibDataText.Text = rightCalibationText;
                                    LeftCalibDataBorder.Visibility = Visibility.Visible;
                                    AnimateCalibrationTextUpdated();
                                });
                            }
                        }
                    }
                    // Display stereo results
                    else
                    {
                        // Get the stereo Calibration data for this calibration parameter set
                        CalibrationStereoCameraData? calibrationStereoCameraData = calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)calibrationParameters];

                        if (calibrationStereoCameraData is not null)
                        {
                            leftCalibationText += "\n" + calibrationParameters.ToString() + ":\n";
                            leftCalibationText += CalibrationStereoFrameSet.CalibrationCameraDataText(calibrationStereoCameraData);

                            // Set calibration output display 
                            safeUICall.Call(() =>
                            {
                                LeftCalibDataText.Text = leftCalibationText;
                                LeftCalibDataBorder.Visibility = Visibility.Visible;
                                AnimateCalibrationTextUpdated();
                            });
                        }
                    }                   
                }
            }
        }


        /// <summary>
        /// Briefly highlight the calibration text area to draw the user's attention
        /// when new calibration results are written.
        /// </summary>
        private void AnimateCalibrationTextUpdated()
        {
            // Guard – border might not be loaded yet
            if (LeftCalibDataBorder is null)
                return;

            // Reset any previous animation state
            LeftCalibDataBorder.RenderTransformOrigin = new Point(0.5, 0.0);

            if (LeftCalibDataBorder.RenderTransform is not ScaleTransform scaleTransform)
            {
                scaleTransform = new ScaleTransform
                {
                    ScaleX = 1.0,
                    ScaleY = 1.0
                };
                LeftCalibDataBorder.RenderTransform = scaleTransform;
            }

            const double scaleFrom = 1.0;
            const double scaleTo = 1.05;
            const double durationMs = 150.0;

            var scaleUpX = new DoubleAnimation
            {
                From = scaleFrom,
                To = scaleTo,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };

            var scaleUpY = new DoubleAnimation
            {
                From = scaleFrom,
                To = scaleTo,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };

            Storyboard.SetTarget(scaleUpX, scaleTransform);
            Storyboard.SetTargetProperty(scaleUpX, "ScaleX");

            Storyboard.SetTarget(scaleUpY, scaleTransform);
            Storyboard.SetTargetProperty(scaleUpY, "ScaleY");

            var storyboard = new Storyboard();
            storyboard.Children.Add(scaleUpX);
            storyboard.Children.Add(scaleUpY);

            storyboard.Begin();
        }


        /// <summary>
        /// Write the best frames on all heads out to separate .png files
        /// </summary>
        /// <param name="runCalibrationParams"></param>
        /// <returns></returns>
        public async Task<int> SaveBestFramesAsync(CalibProject calibProject)
        {
            int ret = -1;

            // Guard
            if (headType is null)
                return ret;

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
            string MakeAndCreateFramesDirectoryAndEmpty(string path, string mediaFileSpec)
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
                            Debug.WriteLine($"{ToString()} SaveBestFramesAsync: Failed to delete {file}: {ex.Message}");
                        }
                    }
                }

                return outputPath;
            }

            if (imageOutputSubFolder is not null && wbLeft is not null) // at least need the left side
            {
                // Get the appropriate best frame list
                List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType((HeadType)headType));

                // Loop through the best frames and save them (need the relative path for this)
                foreach (BestFrame bestFrame in bestFramesList)
                {
                    int frameIndex = bestFrame.FrameIndex;
                    try
                    {
                        (FrameData leftTarget, FrameData? rightTarget, _) = calibrationStereoFrameSet.Data.Frames[frameIndex];

                        // Force the frame with MoveJump (without the calibration board markup)
                        _JumpFrame(trueLeftFalseRight: true, leftTarget.FrameIndex, null, -1);

                        if (rightTarget is not null)
                            _JumpFrame(trueLeftFalseRight: false, rightTarget.FrameIndex, null, -1);

                        await Task.Delay(100);

                        // Make left image file name
                        string videoName = Path.GetFileNameWithoutExtension(leftMediaFileSpec);
                        string frameFileName = $"{videoName}_{frameIndex}_L{leftTarget.FrameIndex}.png";

                        // Save the left image file
                        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(imageOutputSubFolder);
                        StorageFile file = await folder.CreateFileAsync(frameFileName, CreationCollisionOption.ReplaceExisting);
                        await SaveWriteableBitmapToFileAsync(wbLeft, file);
                        Debug.WriteLine($"SaveBestFramesAsync: Left Frame saved: [{file.Path}], Stereo Index:{frameIndex}, Left Index:{leftTarget.FrameIndex}");

                        if (rightTarget is not null && wbRight is not null)
                        {
                            // Make right image file name
                            videoName = Path.GetFileNameWithoutExtension(rightMediaFileSpec);
                            frameFileName = $"{videoName}_{frameIndex}_R{rightTarget.FrameIndex}.png";

                            // Save the left image file
                            folder = await StorageFolder.GetFolderFromPathAsync(imageOutputSubFolder);
                            file = await folder.CreateFileAsync(frameFileName, CreationCollisionOption.ReplaceExisting);
                            await SaveWriteableBitmapToFileAsync(wbRight, file);
                            Debug.WriteLine($"SaveBestFramesAsync: Right Frame saved: [{file.Path}], Stereo Index:{frameIndex}, Left Index:{rightTarget.FrameIndex}");
                        }

                        ret = 0;// OK
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{ToString()} SaveBestFramesAsync: Error saving frame {frameIndex} to path:[{imageOutputSubFolder}], {ex.Message}");
                    }
                }
            }

            // Restore the original app mode
            SetAppMode(appModeOld);

            return ret;
        }


        /// <summary>
        /// Write the Calibration Frame Set to file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [RequiresUnreferencedCode("Calls SurveyorCalibration.CalibrationStereoFrameSet.SaveToFile(String) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        public bool SaveCachedResults(string cacheFileSpec)
        {
            // Force the version to the current 
            calibrationStereoFrameSet.Data.Version = new CalibrationStereoFrameSet.DataClass().Version;

            bool saved = calibrationStereoFrameSet.SaveToFile(cacheFileSpec);

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
        [RequiresUnreferencedCode("Calls SurveyorCalibration.CalibrationStereoFrameSet.LoadFromFileAsync(String) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        public async Task<int> LoadCachedResultsAsync(CalibProject calibProject,string cacheFileSpec)
        {
            int ret = 0;
            string messageText;

            // Guard
            if (headType is null) return -1;

            // Check if the file exists (remove any zero byte file)
            DeleteIfZeroByteFile(cacheFileSpec);

            if (File.Exists(cacheFileSpec))
            {
                // Load the calibration frame set                
                if (await calibrationStereoFrameSet.LoadFromFileAsync(cacheFileSpec))
                {
                    // Setup the left Frame Set Viewers which displays the movement/blur graphs, coverage and pose bins
                    CalibrationFrameSetViewerData dataLeft = new(true/*trueLeftFalseRight*/, calibrationStereoFrameSet);
                    CalibrationFrameSetViewerLeft.Data = dataLeft;
                    RefreshSensorBin(ViewModeCurrent, trueLeftFalseRight: true);
                    RefreshPoseBin(ViewModeCurrent, trueLeftFalseRight: true);
                    RefreshDepthBin(ViewModeCurrent, trueLeftFalseRight: true);
                    CalibrationFrameSetViewerLeft.DrawGraphs();

                    if (IsHeadStereo())
                    {
                        // Stereo only - Setup the right/left Frame Set Viewers which displays the movement/blur graphs,
                        // coverage and pose bins
                        CalibrationFrameSetViewerData dataRight = new(false/*trueLeftFalseRight*/, calibrationStereoFrameSet);
                        CalibrationFrameSetViewerRight.Data = dataRight;
                        RefreshSensorBin(ViewModeCurrent, trueLeftFalseRight: false);
                        RefreshPoseBin(ViewModeCurrent, trueLeftFalseRight: false);
                        RefreshDepthBin(ViewModeCurrent, trueLeftFalseRight: false);
                        CalibrationFrameSetViewerRight.DrawGraphs();
                    }


                    // Setup the MediaTimeLine display range and indicators that show
                    // where calibration boards have been found
                    SetupMediaTimeLineDisplay();
                    RenderMediaTimeLineDisplay();

                    // If best frames have been collected then change the
                    // MediaTimeLineDisplay tool tip
                    if (IsBestFramesSetup(calibProject))
                    {
                        MediaTimeLineDisplayLeft.SetToolTipLoadedProject();
                        if (IsHeadStereo())
                            MediaTimeLineDisplayRight.SetToolTipLoadedProject();
                    }

                    // Report
                    if (IsHeadStereo())
                    {
                        report?.Info("Stereo", $"{ToString()} Stereo calibration zone {calibrationStereoFrameSet.GetStartCalibrationBoardZone()}-{calibrationStereoFrameSet.GetStopCalibrationBoardZone()}");
                    }
                    else
                    {                           
                        report?.Info(ChannelConvert((HeadType)headType), $"{ToString()} calibration zone {calibrationStereoFrameSet.GetStartCalibrationBoardZone()}-{calibrationStereoFrameSet.GetStopCalibrationBoardZone()}");
                    }

                    ret = calibrationStereoFrameSet.Data.Frames.Count;
                }
                else
                {
                    messageText = $"{ToString()} Failed to load: {cacheFileSpec}";
                    Debug.WriteLine(messageText);

                }
            }
            else
            {
                messageText = $"{ToString()} File not found: {cacheFileSpec}";
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
        public bool IsBestFramesSetup(CalibProject calibProject)
        {
            // Guard
            if (headType is null)
                return false;

            // Get the appropriate best frame list
            List<BestFrame> bestFramesList = calibProject.Data.CalibrationInputs.GetBestFramesList(ConvertHeadType((HeadType)headType)); 

            if (bestFramesList is null)
                return false;

            return bestFramesList.Count > 0;
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
                    FrameMoveBack(trueLeftFalseRight: true);
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
                    FrameMoveForward(trueLeftFalseRight: true);
                    break;
                case ViewMode.BestFrames:
                    BestFrameMoveForward();
                    break;
            }
        }


        /// <summary>
        /// Detect left click go to start of left calibration zone
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftGotoStartButton_Click(object sender, RoutedEventArgs e)
        {
            // Jump to start of left calibration zone
            JumpToStartOrEnd(trueLeftFalseRight: true,
                             trueStartFalseEnd: true,
                             trueMediaStartEndFalseCalibrationZoneStartEnd: false);
        }


        /// <summary>
        /// Detect right click go to start of left media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftGotoStartButton_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Ignore non-mouse input if you want desktop semantics
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
                return;

            var element = (UIElement)sender;
            var pt = e.GetCurrentPoint(element);

            if (pt.Properties.IsRightButtonPressed)
            {
                // Jump to start of left media
                JumpToStartOrEnd(trueLeftFalseRight: true,
                                 trueStartFalseEnd: true,
                                 trueMediaStartEndFalseCalibrationZoneStartEnd: true);

            }
            e.Handled = true;
        }


        /// <summary>
        /// Detect left click go to end of left calibration zone
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftGotoEndButton_Click(object sender, RoutedEventArgs e)
        {
            // Jump to start of right calibration zone
            JumpToStartOrEnd(trueLeftFalseRight: true,
                             trueStartFalseEnd: false,
                             trueMediaStartEndFalseCalibrationZoneStartEnd: false);
        }


        /// <summary>
        /// Detect right click go to end of left media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftGotoEndButton_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Ignore non-mouse input if you want desktop semantics
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
                return;

            var element = (UIElement)sender;
            var pt = e.GetCurrentPoint(element);

            // Note this only works for right click you have to used the regular 'Click' handler
            // for the left click
            if (pt.Properties.IsRightButtonPressed)
            {
                // Jump to end of left media
                JumpToStartOrEnd(trueLeftFalseRight: true,
                                 trueStartFalseEnd: false,
                                 trueMediaStartEndFalseCalibrationZoneStartEnd: true);
            }
            e.Handled = true;
        }


        /// <summary>
        /// Used to jump to the start or end of the media or start or end of the calibration zone
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="trueStartFalseEnd"></param>
        /// <param name="trueMediaStartEndFalseCalibrationZoneStartEnd"></param>
        private void JumpToStartOrEnd(bool trueLeftFalseRight, bool trueStartFalseEnd, bool trueMediaStartEndFalseCalibrationZoneStartEnd)
        {
            int frameIndex = -1;

            // Guard
            if (headType is null)
                return;

            string side = trueLeftFalseRight ? "left" : "right";
            string lockState = isLocked == true ? "(locked)" : "";

            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    if (trueStartFalseEnd)
                    {
                        if (!trueMediaStartEndFalseCalibrationZoneStartEnd)
                        {
                            // Try to go to all frames calibration zone start
                            frameIndex = calibrationStereoFrameSet.GetStartCalibrationBoardZone();
                            if (frameIndex != -1)
                                Debug.WriteLine($"{ToString()} JumpToStartOrEnd: Go to calibration zone start {side} AllFrames frame:{frameIndex}");
                        }

                        // Catch all go to all frames media start
                        if (frameIndex == -1)
                        {
                            frameIndex = 0;
                            Debug.WriteLine($"{ToString()} JumpToStartOrEnd: Go to media start {side} AllFrames frame:{frameIndex}");
                        }
                    }
                    else
                    {
                        if (!trueMediaStartEndFalseCalibrationZoneStartEnd)
                        {
                            // Try to go to all frames calibration zone end
                            frameIndex = calibrationStereoFrameSet.GetStopCalibrationBoardZone();
                            if (frameIndex != -1)
                                Debug.WriteLine($"{ToString()} JumpToStartOrEnd: Go to calibration zone end {side} AllFrames frame:{frameIndex}");
                        }

                        // Catch all go to all frames media end
                        if (frameIndex == -1)
                        {
                            if (isLocked)
                            {
                                frameIndex = calibrationStereoFrameSet.GetNaturalDuration() - 1;
                            }
                            else if (trueLeftFalseRight == true)
                            {
                                frameIndex = _totalFramesLeft - 1;
                            }
                            else if (trueLeftFalseRight == false)
                            {
                                frameIndex = _totalFramesRight - 1;
                            }

                            Debug.WriteLine($"{ToString()} JumpToStartOrEnd: Go to media end {side} {lockState} AllFrames frame:{frameIndex}");
                        }
                    }

                    // Actual jump
                    if (frameIndex != -1)
                        FrameJump(trueLeftFalseRight, frameIndex);
                    break;

                case ViewMode.BestFrames:
                    if (trueStartFalseEnd)
                    {
                        // Note a best frame is always inside the calibration zone
                        frameIndex = 0;
                        Debug.WriteLine($"{ToString()} JumpToStartOrEnd: Go to media start {side} BestFrames frame:{frameIndex}");
                    }
                    else
                    {
                        // Get the best frame list for the current head type using the callback
                        List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

                        if (bestFramesList is null) return;

                        // Note a best frame is always inside the calibration zone
                        frameIndex = bestFramesList.Count - 1;
                        Debug.WriteLine($"{ToString()} JumpToStartOrEnd: Go to media end {side} BestFrames frame:{frameIndex}");
                    }

                    // Actual jump
                    if (frameIndex != -1)
                        BestFrameJump(frameIndex);
                    break;
            }
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
                    Debug.WriteLine($"{ToString()} RightFrameBackClick: FrameMoveBack");
                    FrameMoveBack(trueLeftFalseRight: false);
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
                    PlayPauseClick(trueLeftFalseRight: false);
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
                    FrameMoveForward(trueLeftFalseRight: false);
                    break;
                case ViewMode.BestFrames:
                    BestFrameMoveForward();
                    break;
            }
        }


        /// <summary>
        /// Detect left click go to start of right calibration zone
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightGotoStartButton_Click(object sender, RoutedEventArgs e)
        {
            // Jump to start of right calibration zone
            JumpToStartOrEnd(trueLeftFalseRight: false,
                             trueStartFalseEnd: true,
                             trueMediaStartEndFalseCalibrationZoneStartEnd: false);
        }


        /// <summary>
        /// Detect right click go to start of right media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightGotoStartButton_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Ignore non-mouse input if you want desktop semantics
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
                return;

            var element = (UIElement)sender;
            var pt = e.GetCurrentPoint(element);

            if (pt.Properties.IsRightButtonPressed)
            {
                // Jump to start of right media
                JumpToStartOrEnd(trueLeftFalseRight: false,
                                 trueStartFalseEnd: true,
                                 trueMediaStartEndFalseCalibrationZoneStartEnd: true);
            }
            e.Handled = true;
        }


        /// <summary>
        /// Detect left click go to end of right calibration zone
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightGotoEndButton_Click(object sender, RoutedEventArgs e)
        {
            // Jump to start of right calibration zone
            JumpToStartOrEnd(trueLeftFalseRight: false,
                             trueStartFalseEnd: false,
                             trueMediaStartEndFalseCalibrationZoneStartEnd: false);
        }


        /// <summary>
        /// Detect right click go to end of right media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightGotoEndButton_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Ignore non-mouse input if you want desktop semantics
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
                return;

            var element = (UIElement)sender;
            var pt = e.GetCurrentPoint(element);

            if (pt.Properties.IsRightButtonPressed)
            {
                // Jump to end of left media
                JumpToStartOrEnd(trueLeftFalseRight: false,
                                 trueStartFalseEnd: false,
                                 trueMediaStartEndFalseCalibrationZoneStartEnd: true);
            }

            e.Handled = true;
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
                UserGoToFrameRequest(trueLeftFalseRight: true, LeftGoToFrameTextBox);
        }


        /// <summary>
        /// User request to go to a particular right frame index
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightGotoFrameTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                UserGoToFrameRequest(trueLeftFalseRight: false, RightGoToFrameTextBox);
        }


        /// <summary>
        /// Focus lost - check if the values has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftGotoFrameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UserGoToFrameRequest(trueLeftFalseRight: true, LeftGoToFrameTextBox);
        }


        /// <summary>
        /// Focus lost - check if the values has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightGotoFrameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UserGoToFrameRequest(trueLeftFalseRight: false, RightGoToFrameTextBox);
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


        private void OverlayContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 1) Keep the timelines sized and visible (replaces Left/RightImage_SizeChanged logic)
            if (ReferenceEquals(sender, LeftOverlayContainer))
            {
                MediaTimeLineDisplayLeft.Width = LeftImage.ActualWidth;

                if (AppModeCurrent != AppMode.Close && ViewModeCurrent != ViewMode.SensorCoverage)
                {
                    if (MediaTimeLineDisplayLeft.Visibility != Visibility.Visible)
                        MediaTimeLineDisplayLeft.Visibility = Visibility.Visible;
                }
            }
            else if (ReferenceEquals(sender, RightOverlayContainer))
            {
                MediaTimeLineDisplayRight.Width = RightImage.ActualWidth;

                if (AppModeCurrent != AppMode.Close && ViewModeCurrent != ViewMode.SensorCoverage)
                {
                    if (MediaTimeLineDisplayRight.Visibility != Visibility.Visible)
                        MediaTimeLineDisplayRight.Visibility = Visibility.Visible;
                }
            }

            // 2) De-bounced re-render for sensor coverage
            if (ViewModeCurrent == ViewMode.SensorCoverage)
            {
                QueueRenderSensorCoverage();
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="frameIndex"></param>
        /// <returns></returns>
        private bool JumpToFrameRequestedHandlerLeft(int frameIndex) => JumpToFrameRequestedHandler(trueLeftFalseRight: true, frameIndex);

        private bool JumpToFrameRequestedHandlerRight(int frameIndex) => JumpToFrameRequestedHandler(trueLeftFalseRight: false, frameIndex);

        private bool JumpToFrameRequestedHandler(bool trueLeftFalseRight, int frameIndex)
        {
            // Guard
            if (headType is null)
                return false;

            if (ViewModeCurrent == ViewMode.SensorCoverage ||
                ViewModeCurrent == ViewMode.AllFrames)
            {
                // If the view mode is SensorCoverage then best frame will be available
                SetViewMode(ViewMode.BestFrames);
            }

            if (ViewModeCurrent == ViewMode.BestFrames)
            {
                // Get the best frame list for the current head type using the callback
                List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

                if (bestFramesList is not null)
                {
                    int bestFrameIndex = -1;
                    if (trueLeftFalseRight)
                    {
                        bestFrameIndex = bestFramesList
                                            .Select((bf, i) => (bf, i))
                                            .FirstOrDefault(x =>
                                                calibrationStereoFrameSet.Data.Frames.TryGetValue(x.bf.FrameIndex, out var tuple) &&
                                                tuple.frameCalibrationTargetLeft.FrameIndex == frameIndex).i;
                    }
                    else
                    {
                        bestFrameIndex = bestFramesList
                                            .Select((bf, i) => (bf, i))
                                            .FirstOrDefault(x =>
                                                calibrationStereoFrameSet.Data.Frames.TryGetValue(x.bf.FrameIndex, out var tuple) &&
                                                tuple.frameCalibrationTargetRight!.FrameIndex == frameIndex).i;

                    }
                    if (bestFrameIndex != -1)
                        BestFrameJump(bestFrameIndex);
                }
            }
            else if (ViewModeCurrent == ViewMode.AllFrames)
            {
                int? allFrameIndex;

                if (trueLeftFalseRight)
                {
                    allFrameIndex = calibrationStereoFrameSet.Data.Frames
                        .Where(kvp => kvp.Value.frameCalibrationTargetLeft is not null
                                      && kvp.Value.frameCalibrationTargetLeft.FrameIndex == frameIndex)
                        .Select(kvp => (int?)kvp.Key)
                        .FirstOrDefault();
                }
                else
                {
                    allFrameIndex = calibrationStereoFrameSet.Data.Frames
                        .Where(kvp => kvp.Value.frameCalibrationTargetRight is not null
                                      && kvp.Value.frameCalibrationTargetRight.FrameIndex == frameIndex)
                        .Select(kvp => (int?)kvp.Key)
                        .FirstOrDefault();
                }

                if (allFrameIndex is not null)
                    FrameJump(trueLeftFalseRight, (int)allFrameIndex);
            }

            return true;
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
                bitmapImage = new(new Uri($"ms-appx:///Assets/BlurCircle-Dark.png"));
                LeftBlurIconLabel.Source = bitmapImage;
                RightBlurIconLabel.Source = bitmapImage;

                bitmapImage = new(new Uri($"ms-appx:///Assets/ArucoSmall-Dark.png"));
                LeftFeatureCountIconLabel.Source = bitmapImage;
                RightFeatureCountIconLabel.Source = bitmapImage;
            }
            else if (theme == ElementTheme.Light)
            {
                bitmapImage = new(new Uri($"ms-appx:///Assets/BlurCircle-Light.png"));
                LeftBlurIconLabel.Source = bitmapImage;
                RightBlurIconLabel.Source = bitmapImage;

                bitmapImage = new(new Uri($"ms-appx:///Assets/ArucoSmall-Light.png"));
                LeftFeatureCountIconLabel.Source = bitmapImage;
                RightFeatureCountIconLabel.Source = bitmapImage;
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
                MediaTimeLineDisplayLeft.CalibrationBoardFoundAt(leftFrameIndex, trueFoundFalseNotFound);

                if (IsHeadStereo())
                {
                    if (rightMat is not null && !rightMat.IsEmpty && wbRight is not null)
                    {
                        DrawFrameToScreen(rightMat, wbRight);
                        RightFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                        RightTimeInfoLabel.Text = string.Empty;
                    }

                    trueFoundFalseNotFound = rightFrameCalibrationTarget is not null;
                    MediaTimeLineDisplayRight.CalibrationBoardFoundAt(rightFrameIndex, trueFoundFalseNotFound);
                }
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

                //???bool trueFoundFalseNotFound;

                if (leftMat is not null && !leftMat.IsEmpty && wbLeft is not null)
                {
                    DrawFrameToScreen(leftMat, wbLeft);
                    LeftFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                    LeftTimeInfoLabel.Text = string.Empty;
                }


                if (rightMat is not null && !rightMat.IsEmpty && wbRight is not null)
                {
                    DrawFrameToScreen(rightMat, wbRight);
                    RightFrameInfoLabel.Text = $"{stereoFrameIndex} / {stereoFrameTotal}";
                    RightTimeInfoLabel.Text = string.Empty;
                }

                // Add a found board to the media timeline if the FrameData is not null
                if (leftFrameCalibrationTarget is not null)
                    MediaTimeLineDisplayLeft.CalibrationBoardFoundAt(leftFrameIndex, trueFoundFalseNotFound:true );
                if (rightFrameCalibrationTarget is not null)                
                    MediaTimeLineDisplayRight.CalibrationBoardFoundAt(rightFrameIndex, trueFoundFalseNotFound: false);


                try
                {
                    // Update from Bin Layers and the graphs 
                    // Note these are fully recreated from the full list to date
                    if (leftFrameCalibrationTarget is not null)
                    {
                        // Note the Pose bin is setup during finding best frames so no need to refresh here
                        RefreshSensorBin(ViewModeCurrent, trueLeftFalseRight: true);
                        RefreshDepthBin(ViewModeCurrent, trueLeftFalseRight: true);
                        CalibrationFrameSetViewerLeft.DrawGraphs();
                    }
                    if (rightFrameCalibrationTarget is not null)
                    {
                        // Note the Pose bin is setup during finding best frames so no need to refresh here
                        RefreshSensorBin(ViewModeCurrent, trueLeftFalseRight: false);
                        RefreshDepthBin(ViewModeCurrent, trueLeftFalseRight: false);
                        CalibrationFrameSetViewerRight.DrawGraphs();
                    }


                    double movementFactor;
                    double movementFromPrevious;
                    double movementToNext;


                    if (leftFrameCalibrationTarget is not null)
                    {
                        (movementFromPrevious, movementFactor, movementToNext) = GetMovementFactors(leftFrameCalibrationTarget);

                        UpdateFrameMetaData(trueLeftFalseRight: true,
                                            movementFactor, movementFromPrevious, movementToNext,
                                            leftFrameCalibrationTarget.BlurFactor,
                                            leftFrameCalibrationTarget.DepthBinZ,
                                            leftFrameCalibrationTarget.ChArUcoCorners.Length /*Size*/,
                                            leftFrameCalibrationTarget.Score,
                                            leftFrameCalibrationTarget.YawDeg,
                                            leftFrameCalibrationTarget.PitchDeg,
                                            0, /*leftFrameCalibrationTarget.monoFrameRms[0],     //K1K2P1P2*/
                                            0, /*leftFrameCalibrationTarget.monoFrameMaxError[0],//K1K2P1P2*/
                                            leftFrameCalibrationTarget.FrameIndex,
                                            null, /*position*/
                                            BestFrameReason.None,
                                            correspondingCount);
                    }
                    else
                    {
                        ClearFrameMetaData(trueLeftFalseRight: true);
                    }

                    if (rightFrameCalibrationTarget is not null)
                    {
                        (movementFromPrevious, movementFactor, movementToNext) = GetMovementFactors(rightFrameCalibrationTarget);

                        UpdateFrameMetaData(trueLeftFalseRight: false,
                                            movementFactor, movementFromPrevious, movementToNext,
                                            rightFrameCalibrationTarget.BlurFactor,
                                            rightFrameCalibrationTarget.DepthBinZ,
                                            rightFrameCalibrationTarget.ChArUcoCorners.Length /*Size*/,
                                            rightFrameCalibrationTarget.Score,
                                            rightFrameCalibrationTarget.YawDeg,
                                            rightFrameCalibrationTarget.PitchDeg,
                                            0, /*rightFrameCalibrationTarget.monoFrameRms[0],     //K1K2P1P2*/
                                            0, /*rightFrameCalibrationTarget.monoFrameMaxError[0],//K1K2P1P2*/
                                            rightFrameCalibrationTarget.FrameIndex,
                                            null, /*position*/
                                            BestFrameReason.None,
                                            correspondingCount);

                    }
                    else
                    {
                        ClearFrameMetaData(trueLeftFalseRight: false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{ToString()} FrameProcessingCallbackFindCalibrationsFrames: Error processing ChArUco board: {ex.Message}");
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
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="movementFactor"></param>
        /// <param name="blurFactor"></param>
        /// <param name="featureCount"></param>
        /// <param name="score"></param>
        private void UpdateFrameMetaData(bool trueLeftFalseRight,
                        double movementFactor, double movementFromPrevious, double movementToNext,
                        double blurFactor, int depthIndex, int featureCount, double score,
                        double yaw, double pitch,
                        double frameRMS, double frameMaxError,
                        int frameIndex, double? position,
                        BestFrameReason? bestReason,
                        int correspondingCount)
        {
            TextBlock MovementFactor;
            TextBlock BlurFactor;
            TextBlock Depth;
            TextBlock FeatureCount;
            TextBlock Score;
            TextBlock Yaw;
            TextBlock Pitch;
            TextBlock FrameRMS;
            TextBlock FrameMaxError;
            TextBlock FrameIndex;
            TextBlock Position;

            if (trueLeftFalseRight)
            {
                MovementFactor = LeftMoveText;
                BlurFactor = LeftBlurText;
                Depth = LeftDepthText;
                Yaw = LeftYawText;
                Pitch = LeftPitchText;
                FeatureCount = LeftFeatureCountText;
                Score = LeftScoreText;
                FrameRMS = LeftFrameRMSText;
                FrameMaxError = LeftFrameMaxErrorText;
                FrameIndex = LeftFrameIndex;
                Position = LeftPosition;
            }
            else
            {
                MovementFactor = RightMoveText;
                BlurFactor = RightBlurText;
                Depth = RightDepthText;
                Yaw = RightYawText;
                Pitch = RightPitchText;
                FeatureCount = RightFeatureCountText;
                Score = RightScoreText;
                FrameRMS = RightFrameRMSText;
                FrameMaxError = RightFrameMaxErrorText;
                FrameIndex = RightFrameIndex;
                Position = RightPosition;
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

            // Depth
            if (depthIndex != -1)
            {
                Depth.Text = depthIndex switch
                {
                    0 => "Near",
                    1 => "Mid",
                    2 => "Far",
                    3 => "Deep",
                    _ => string.Empty
                };
            } 
            else
               Depth.Text = string.Empty;

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

            // Frame RMS
            if (frameRMS != 0)
                FrameRMS.Text = $"{frameRMS:F2}";
            else
                FrameRMS.Text = string.Empty;

            // Frame Max Error
            if (frameMaxError != 0)
                FrameMaxError.Text = $"{frameMaxError:F2}";
            else
                FrameMaxError.Text = string.Empty;

            // Frame Index
            if (frameIndex != -1)
                FrameIndex.Text = $"{frameIndex}";
            else
                FrameIndex.Text = string.Empty;

            // Position
            if (position is not null)
                Position.Text = $"{position:F2}";
            else
                Position.Text = string.Empty;

            // Reason (this is in a separate method so the Reason
            // can be updated independently of the other meta data
            // values. The BestFrameReason has some manually set
            // attributes.  That the users toggles via the UI
            UpdateFrameMetaDataReason(trueLeftFalseRight,
                                      bestReason is not null ? bestReason.Value : BestFrameReason.None);
        }


        /// <summary>
        /// Updates just the reason in the meta data panel
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="bestReason"></param>
        /// <param name="manualReason"></param>
        private void UpdateFrameMetaDataReason(bool trueLeftFalseRight,
                                               BestFrameReason bestReason)
        {
            TextBlock ReasonBestTextBlock;
            TextBlock ReasonManualTextBlock;

            if (trueLeftFalseRight)
            {
                ReasonBestTextBlock = LeftCalibrationFrameStatusBest;
                ReasonManualTextBlock = LeftCalibrationFrameStatusManual;
            }
            else
            {
                ReasonBestTextBlock = RightCalibrationFrameStatusBest;
                ReasonManualTextBlock = RightCalibrationFrameStatusManual;
            }

            // Split Reasons between best (automatic) and manual. 
            BestFrameReason _reasonBest = bestReason & ~(BestFrameReason.ManuallyAdded | BestFrameReason.ManuallyIgnored);
            BestFrameReason _reasonManual = bestReason & (BestFrameReason.ManuallyAdded | BestFrameReason.ManuallyIgnored);

            // Can't have both added and ignore. If that is the case take ignore
            if ((_reasonManual & (BestFrameReason.ManuallyIgnored | BestFrameReason.ManuallyAdded))
                == (BestFrameReason.ManuallyIgnored | BestFrameReason.ManuallyAdded))
            {
                // Turn off the ManuallyAdded bit
                _reasonManual &= ~BestFrameReason.ManuallyAdded;
            }

            // Update Best frame reason
            if (_reasonBest != BestFrameReason.None)
            {
                ReasonBestTextBlock.Text = string.Join(Environment.NewLine, new[]
                {
                    (_reasonBest & BestFrameReason.SensorCoverage) != 0 ? "Cover" : null,
                    (_reasonBest & BestFrameReason.PoseDiversity) != 0 ? "Pose" : null,
                    (_reasonBest & BestFrameReason.DepthDiversity) != 0 ? "Depth" : null
                }.Where(s => s is not null));
                ReasonBestTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                ReasonBestTextBlock.Text = string.Empty;
                ReasonBestTextBlock.Visibility = Visibility.Collapsed;
            }

            // Update Manual frame reason
            if (_reasonManual != BestFrameReason.None)
            {
                ReasonManualTextBlock.Text = string.Join(Environment.NewLine, new[]
                {
                    (_reasonManual & BestFrameReason.ManuallyIgnored) != 0 ? ManuallyIgnoredText : null,
                    (_reasonManual & BestFrameReason.ManuallyAdded) != 0 ? ManuallyAddedText : null,
                }.Where(s => s is not null));
                ReasonManualTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                ReasonManualTextBlock.Text = string.Empty;
                ReasonManualTextBlock.Visibility = Visibility.Collapsed;
            }
        }


        /// <summary>
        /// Clear the frame metadata on screen fields
        /// </summary>
        private void ClearFrameMetaData(bool trueLeftFalseRight)
        {
            if (trueLeftFalseRight)
            {
                LeftMoveText.Text = string.Empty;
                LeftBlurText.Text = string.Empty;
                LeftDepthText.Text = string.Empty;
                LeftFeatureCountText.Text = string.Empty;
                LeftScoreText.Text = string.Empty;
                LeftYawText.Text = string.Empty;
                LeftPitchText.Text = string.Empty;
                LeftFrameRMSText.Text = string.Empty;
                LeftFrameMaxErrorText.Text = string.Empty;
                LeftFrameIndex.Text = string.Empty;
                LeftPosition.Text = string.Empty;
                LeftCalibrationFrameStatusBest.Text = string.Empty;
                LeftCalibrationFrameStatusBest.Visibility = Visibility.Collapsed;
                LeftCalibrationFrameStatusManual.Text = string.Empty;
                LeftCalibrationFrameStatusManual.Visibility = Visibility.Collapsed;
            }
            else
            {
                RightMoveText.Text = string.Empty;
                RightBlurText.Text = string.Empty;
                RightDepthText.Text = string.Empty;
                RightFeatureCountText.Text = string.Empty;
                RightScoreText.Text = string.Empty;
                RightYawText.Text = string.Empty;
                RightPitchText.Text = string.Empty;
                RightFrameRMSText.Text = string.Empty;
                RightFrameMaxErrorText.Text = string.Empty;
                RightFrameIndex.Text = string.Empty;
                RightPosition.Text = string.Empty;
                RightCalibrationFrameStatusBest.Text = string.Empty;
                RightCalibrationFrameStatusBest.Visibility = Visibility.Collapsed;
                RightCalibrationFrameStatusManual.Text = string.Empty;
                RightCalibrationFrameStatusManual.Visibility = Visibility.Collapsed;
            }
        }


        /// <summary>
        /// Play in the context of this application is a timer based frame forward operation
        /// </summary>
        private void PlayLeft()
        {
            if (capLeft != null && wbLeft != null)
            {
                FrameMoveForward(trueLeftFalseRight: true);

            }
        }
        private void PlayRight()
        {
            if (capRight != null && wbRight != null)
            {
                FrameMoveForward(trueLeftFalseRight: false);
            }
        }
        private void PlayBoth()
        {
            if (capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                FrameMoveForward(trueLeftFalseRight: true);
                FrameMoveForward(trueLeftFalseRight: false);
            }
        }

        private void FrameMoveBack(bool trueLeftFalseRight)
        {
            int? leftIndex;
            int? rightIndex;
            int framesetIndex;
            FrameData? leftFrameData = null;
            FrameData? rightFrameData = null;
            int correpondingCount = -1;

            // If stereo and locked
            if (isLocked && capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                // Get the next index (if valid)
                leftIndex = GetNextIndex(trueLeftFalseRight: true, -1/*relative*/, null/*absolute*/);
                rightIndex = GetNextIndex(trueLeftFalseRight: false, -1/*relative*/, null/*absolute*/);

                if (leftIndex is not null && rightIndex is not null)
                {
                    framesetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftRightIndexes((int)leftIndex, (int)rightIndex);

                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue(framesetIndex, out var tuple))
                        (leftFrameData, rightFrameData, correpondingCount) = tuple;

                    // if frame data is null then there is no decoration
                    _JumpFrame(trueLeftFalseRight: true, (int)leftIndex, leftFrameData, correpondingCount);
                    _JumpFrame(trueLeftFalseRight: false, (int)rightIndex, rightFrameData, correpondingCount);
                }
            }
            // If mono left or stereo unlocked left
            else if (trueLeftFalseRight && capLeft != null && wbLeft != null)
            {
                // Get the next index (if valid)
                leftIndex = GetNextIndex(trueLeftFalseRight, -1/*relative*/, null/*absolute*/);

                if (leftIndex is not null)
                {
                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue((int)leftIndex, out var tuple))
                        (leftFrameData, _, correpondingCount) = tuple;

                    _JumpFrame(trueLeftFalseRight: true, (int)leftIndex, leftFrameData, correpondingCount);
                }
            }
            // If mono right or stereo unlocked right
            else if (!trueLeftFalseRight && capRight != null && wbRight != null)
            {
                // Get the next index (if valid)
                rightIndex = GetNextIndex(trueLeftFalseRight, -1/*relative*/, null/*absolute*/);

                if (rightIndex is not null)
                {
                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue((int)rightIndex, out var tuple))
                        (_, rightFrameData, correpondingCount) = tuple;

                    _JumpFrame(trueLeftFalseRight: false, (int)rightIndex, rightFrameData, correpondingCount);
                }
            }
        }

        private void FrameMoveForward(bool trueLeftFalseRight)
        {
            int? leftIndex = null;
            int? rightIndex = null;
            int framesetIndex;
            FrameData? leftFrameData = null;
            FrameData? rightFrameData = null;
            int correpondingCount = -1;

            if (isLocked && capLeft != null && wbLeft != null && capRight != null && wbRight != null)
            {
                // Get the next index (if valid)
                leftIndex = GetNextIndex(trueLeftFalseRight: true, 1/*relative*/, null/*absolute*/);
                rightIndex = GetNextIndex(trueLeftFalseRight: false, 1/*relative*/, null/*absolute*/);

                if (leftIndex is not null && rightIndex is not null)
                {
                    framesetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftRightIndexes((int)leftIndex, (int)rightIndex);

                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue(framesetIndex, out var tuple))
                        (leftFrameData, rightFrameData, correpondingCount) = tuple;

                    _ForwardFrame(trueLeftFalseRight: true, (int)leftIndex, leftFrameData, correpondingCount);
                    _ForwardFrame(trueLeftFalseRight: false, (int)rightIndex, rightFrameData, correpondingCount);
                }
            }
            else if (trueLeftFalseRight && capLeft != null && wbLeft != null)
            {
                // Get the next index (if valid)
                leftIndex = GetNextIndex(trueLeftFalseRight: true, 1/*relative*/, null/*absolute*/);

                if (leftIndex is not null)
                {
                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue((int)leftIndex, out var tuple))
                        (leftFrameData, _, correpondingCount) = tuple;

                    _ForwardFrame(trueLeftFalseRight: true, (int)leftIndex, leftFrameData, correpondingCount);
                }
            }
            else if (!trueLeftFalseRight && capRight != null && wbRight != null)
            {
                // Get the next index (if valid)
                rightIndex = GetNextIndex(trueLeftFalseRight: false, 1/*relative*/, null/*absolute*/);

                if (rightIndex is not null)
                {
                    if (calibrationStereoFrameSet.Data.Frames.TryGetValue((int)rightIndex, out var tuple))
                        (_, rightFrameData, correpondingCount) = tuple;

                    _ForwardFrame(trueLeftFalseRight: false, (int)rightIndex, rightFrameData, correpondingCount);
                }
            }
        }


        /// <summary>
        /// Toggle the play to pause, pause to play state
        /// Play timer is started and stopped here, icons updated
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        private void PlayPauseClick(bool trueLeftFalseRight)
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
            else if (trueLeftFalseRight)
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
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="framesetIndex"></param>
        private void FrameJump(bool trueLeftFalseRight, int? framesetIndexRequest)
        {
            int framesetIndex;
            int? leftIndex = null;
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

                // Check next index is valid
                leftIndex = GetNextIndex(trueLeftFalseRight: true, null/*relative*/, leftIndex/*absolute*/);
                rightIndex = GetNextIndex(trueLeftFalseRight: false, null/*relative*/, rightIndex/*absolute*/);

                if (leftIndex is not null && rightIndex is not null)
                {
                    _JumpFrame(trueLeftFalseRight: true, (int)leftIndex, targetLeft, correpondingCount);
                    _JumpFrame(trueLeftFalseRight: false, (int)rightIndex, targetRight, correpondingCount);
                }
            }
            else if (trueLeftFalseRight && capLeft != null && wbLeft != null)
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

                // Check next index is valid
                leftIndex = GetNextIndex(trueLeftFalseRight: true, null/*relative*/, leftIndex/*absolute*/);

                if (leftIndex is not null)
                    _JumpFrame(trueLeftFalseRight: true, (int)leftIndex, targetLeft, correpondingCount);
            }
            else if (!trueLeftFalseRight && capRight != null && wbRight != null)
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

                // Check next index is valid
                rightIndex = GetNextIndex(trueLeftFalseRight: false, null/*relative*/, rightIndex/*absolute*/);

                if (rightIndex is not null)
                    _JumpFrame(trueLeftFalseRight: false, (int)rightIndex, targetRight, correpondingCount);
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
        /// <param name="trueLeftFalseRight">left or right side</param>
        /// <param name="frameData">FrameData instance</param>
        /// <param name="frameIndex">Actual frame index number</param>
        /// <param name="bestFrameIndex">Index into the best frame array sub-set (only used in ViewMode.BestFrames)</param>
        /// <param name="time"></param>
        /// <param name="correpondingCount"></param>
        private void DecorateWithFrameInfo(bool trueLeftFalseRight, FrameData? frameData, int frameIndex, double? time, int correpondingCount)
        {
            // Set frame index / total frame and time position
            UpdateFrameLabel(trueLeftFalseRight);

            bool clearMetadataAndHighlights = false;
            if (frameData is not null)
            {
                double frameRMS;
                double frameMaxError;
                BestFrame? bestFrame = null;

                // Get the best frame list for the current head type using the callback
                List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

                if (bestFramesList is not null)
                {
                    // Check if mono or stereo head
                    if (IsHeadMono())
                    {
                        // Mono
                        frameRMS = frameData.monoFrameRms[0]/*K1K2P1P2*/;
                        frameMaxError = frameData.monoFrameMaxError[0]/*K1K2P1P2*/;

                        // Get the BestFrameReason (if any)
                        // Because it is mono search for the frameIndex in the best frame list
                        bestFrame = bestFramesList.FirstOrDefault(bf => bf.FrameIndex == frameIndex);
                    }
                    else
                    {
                        // Stereo
                        frameRMS = frameData.stereoFrameRms[0]/*K1K2P1P2*/;
                        frameMaxError = frameData.stereoFrameMaxError[0]/*K1K2P1P2*/;

                        int frameSetIndex = -1;
                        if (ViewModeCurrent == ViewMode.AllFrames)
                        {
                            if (isLocked)
                                frameSetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftIndexes(_currentFrameLeft);
                            else
                            {
                                if (trueLeftFalseRight)
                                    frameSetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftIndexes(_currentFrameLeft);
                                else
                                    frameSetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromRightIndexes(_currentFrameRight);
                            }

                            if (frameSetIndex != -1)
                                // Get the BestFrameReason (if any)
                                // Because it is mono search for the frameIndex in the best frame list
                                bestFrame = bestFramesList.FirstOrDefault(bf => bf.FrameIndex == frameSetIndex);
                            //???bestFrame = bestFramesList[frameSetIndex];
                        }
                        else
                        {
                            // Get the BestFrame instance using the _currentBestFrame
                            bestFrame = bestFramesList[_currentBestFrame];
                        }
                    }


                    //???Try to understand if frameData.FrameIndex can be different to frameIndex
                    if (frameData.FrameIndex != frameIndex)
                        Debug.WriteLine($"{ToString()} frameData.FrameIndex:{frameData.FrameIndex} != frameIndex:{frameIndex}");

                    // The frame metadata (movement, blur, yaw, pitch, features, score,
                    // frame RMS, frame max error, frame index & position)
                    UpdateFrameMetaData(trueLeftFalseRight,
                                    frameData.MovementFactor,
                                    frameData.MovementFromPrevious,
                                    frameData.MovementToNext,
                                    frameData.BlurFactor,
                                    frameData.DepthBinZ,
                                    frameData.ChArUcoCorners.Length /*Size*/,
                                    frameData.Score,
                                    frameData.YawDeg,
                                    frameData.PitchDeg,
                                    frameRMS,
                                    frameMaxError,
                                    frameIndex, /* Don't used frameData.FrameIndex */
                                    time,
                                    bestFrame?.Reason,
                                    correpondingCount);

                    // Indicate which of the bin this frame is found in
                    if (trueLeftFalseRight)
                    {
                        CalibrationFrameSetViewerLeft.HighLightActiveSensorBin(frameData);
                        CalibrationFrameSetViewerLeft.HighLightActivePoseBin(frameData);
                        CalibrationFrameSetViewerLeft.HighLightActiveDepthBin(frameData);
                    }
                    else
                    {
                        CalibrationFrameSetViewerRight.HighLightActiveSensorBin(frameData);
                        CalibrationFrameSetViewerRight.HighLightActivePoseBin(frameData);
                        CalibrationFrameSetViewerRight.HighLightActiveDepthBin(frameData);
                    }
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
                ClearFrameMetaData(trueLeftFalseRight);
                CalibrationFrameSetViewerLeft.HighLightActiveSensorBin(null);
                CalibrationFrameSetViewerRight.HighLightActiveSensorBin(null);
                CalibrationFrameSetViewerLeft.HighLightActivePoseBin(null);
                CalibrationFrameSetViewerRight.HighLightActivePoseBin(null);
                CalibrationFrameSetViewerLeft.HighLightActiveDepthBin(null);
                CalibrationFrameSetViewerRight.HighLightActiveDepthBin(null);

                // All we have to display is the frame index and time position
                TextBlock FrameIndex;
                TextBlock Position;
                if (trueLeftFalseRight == true)
                {
                    FrameIndex = LeftFrameIndex;
                    Position = LeftPosition;
                }
                else
                {
                    FrameIndex = RightFrameIndex;
                    Position = RightPosition;
                }

                // Frame Index
                if (frameIndex != -1)
                    FrameIndex.Text = $"{frameIndex}";
                else
                    FrameIndex.Text = string.Empty;

                // Position
                if (time is not null)
                    Position.Text = $"{time:F2}";
                else
                    Position.Text = string.Empty;
            }
        }


        /// <summary>
        /// Clear any frame decoration on the UI
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        private void DecorateClear(bool trueLeftFalseRight)
        {
            if (trueLeftFalseRight)
            {
                LeftFrameInfoLabel.Text = string.Empty;
                LeftTimeInfoLabel.Text = string.Empty;
            }
            else
            {
                RightFrameInfoLabel.Text = string.Empty;
                RightTimeInfoLabel.Text = string.Empty;
            }

            ClearFrameMetaData(trueLeftFalseRight);
            CalibrationFrameSetViewerLeft.HighLightActivePoseBin(null);
            CalibrationFrameSetViewerRight.HighLightActivePoseBin(null);
        }


        /// <summary>
        /// Used to frame forward in AllFrames mode.  This method reads
        /// the frame from the .MP4.  
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns>-1 if out of range (end of media normally)</returns>
        private int _ForwardFrame(bool trueLeftFalseRight, int targetIndex, FrameData? frameData, int correpondingCount)
        {
            VideoCapture? cap;
            WriteableBitmap? wb;

            if (trueLeftFalseRight)
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
                if (trueLeftFalseRight)
                {
                    // Check for end of media
                    if (_currentFrameLeft >= _totalFramesLeft)
                    {
                        PlayPauseClick(trueLeftFalseRight);
                        return -1;
                    }
                }
                else
                {
                    // Check for end of media
                    if (_currentFrameRight >= _totalFramesRight)
                    {
                        PlayPauseClick(trueLeftFalseRight);
                        return -1;
                    }
                }

                using var mat = new Mat();

                if (cap!.Read(mat) && !mat.IsEmpty)
                {

                    // Draw frame to screen
                    ProcessFrame(trueLeftFalseRight, targetIndex, mat, wb, frameData);

                    // Remember the frame index (must be set before calling DecorateWithFrameInfo)
                    if (trueLeftFalseRight)
                        _currentFrameLeft = targetIndex;
                    else
                        _currentFrameRight = targetIndex;

                    // Update metadata, frame label etc
                    double time = cap.Get(CapProp.PosMsec) / 1000.0;
                    DecorateWithFrameInfo(trueLeftFalseRight, frameData, targetIndex, time, correpondingCount);
                }
            }

            return targetIndex;
        }


        /// <summary>
        /// Used to jump to a particular frame in AllFrames mode.  
        /// This method reads the frame from the .MP4.  
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="targetIndex"></param>
        /// <param name="frameData"></param>
        /// <returns>-1 if out of range (end of media normally)</returns>
        private int _JumpFrame(bool trueLeftFalseRight, int targetIndex, FrameData? frameData, int correpondingCount)
        {
            VideoCapture? cap;
            WriteableBitmap? wb;

            if (trueLeftFalseRight)
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
                cap!.Set(CapProp.PosFrames, targetIndex);

                using var mat = new Mat();
                cap.Read(mat);

                if (!mat.IsEmpty && wb is not null)
                {
                    // Apply the calibration board markup and draw to screen
                    ProcessFrame(trueLeftFalseRight, targetIndex, mat, wb, frameData);

                    // Remember the frame index (must be set before calling DecorateWithFrameInfo)
                    if (trueLeftFalseRight)
                        _currentFrameLeft = targetIndex;
                    else
                        _currentFrameRight = targetIndex;

                    // Update metadata, frame label etc
                    double time = cap.Get(CapProp.PosMsec) / 1000.0;
                    DecorateWithFrameInfo(trueLeftFalseRight, frameData, targetIndex, time, correpondingCount);
                }
            }

            return targetIndex;
        }


        /// <summary>
        /// Calculate the new index for the given size return null is index is 
        /// out of range
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="relative"></param>
        /// <param name="absolute"></param>
        /// <returns></returns>
        private int? GetNextIndex(bool trueLeftFalseRight, int? relative, int? absolute)
        {
            // Guard
            if (relative is null && absolute is null) return null;

            int? index = null;

            if (trueLeftFalseRight)
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
        /// <param name="trueLeftFalseRight">Indicates whether the frame corresponds to the left (<see langword="true"/>) or right (<see
        /// langword="false"/>) camera or view.</param>
        /// <param name="frameIndex">The zero-based index of the frame being processed.</param>
        /// <param name="frame">The <see cref="Mat"/> object representing the video frame to process. Must not be <see langword="null"/>.</param>
        /// <param name="wb">The <see cref="WriteableBitmap"/> to which the processed frame will be rendered. Must not be <see
        /// langword="null"/>.</param>
        /// <param name="frameCalibrationData">Optional calibration data to apply to the frame. If provided, calibration markers may be drawn on the frame
        /// before rendering.</param>
        private void ProcessFrame(bool trueLeftFalseRight, int frameIndex, Mat frame, WriteableBitmap wb, FrameData? frameCalibrationData)
        {
            try
            {
                if (frameCalibrationData is not null)
                {
                    if (IsHeadStereo())
                        CalibrationStereoFrameSet.DrawMarkersToMat(frameCalibrationData, frame, headTrueIsStereoFalseIsMode: true);
                    else
                        CalibrationStereoFrameSet.DrawMarkersToMat(frameCalibrationData, frame, headTrueIsStereoFalseIsMode: false);
                }

                DrawFrameToScreen(frame, wb);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ToString()} ProcessFrame: Error processing ChArUco board, AppMode:{AppModeCurrent}, {ex.Message}");
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
                    Debug.WriteLine($"{ToString()} Warning: DrawFrameToScreen  Frame dimensions {bgraFrame.Width}x{bgraFrame.Height} " +
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
                Debug.WriteLine($"{ToString()} DrawFrameToScreen: Error drawing frame: {ex.Message}");
            }
        }


        /// <summary>
        /// Update the left or right frame label
        /// </summary>
        private void UpdateFrameLabel(bool trueLeftFalseRight)
        {
            // Guard
            if (headType is null)
                return;

            int targetIndex = -1;
            int totalFrames = -1;
            int altCurrentFrame = -1;

            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    if (isLocked)
                    {
                        targetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftIndexes(_currentFrameLeft);

                        totalFrames = calibrationStereoFrameSet.GetNaturalDuration();
                    }
                    else
                    {
                        if (trueLeftFalseRight)
                        {
                            targetIndex = _currentFrameLeft;
                            totalFrames = _totalFramesLeft;
                        }
                        else
                        {
                            targetIndex = _currentFrameRight;
                            totalFrames = _totalFramesRight;
                        }
                    }
                    break;

                case ViewMode.BestFrames:
                    targetIndex = _currentBestFrame;

                    // Get the best frame list for the current head type using the callback
                    List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

                    if (bestFramesList is not null)
                        totalFrames = bestFramesList.Count;
                    else
                        totalFrames = 0;

                    if (trueLeftFalseRight)
                        altCurrentFrame = _currentFrameLeft;
                    else
                        altCurrentFrame = _currentFrameRight;
                    break;
            }

            if (trueLeftFalseRight)
            {
                UpdateFrameAndTimeLabel(LeftFrameInfoLabel, LeftTimeInfoLabel, capLeft, targetIndex, totalFrames, altCurrentFrame);
            }
            else
            {
                UpdateFrameAndTimeLabel(RightFrameInfoLabel, RightTimeInfoLabel, capRight, targetIndex, totalFrames, altCurrentFrame);
            }
        }

        private void UpdateFrameAndTimeLabel(TextBlock frameTextBlock, TextBlock timeTextBlock, VideoCapture? cap, int currentFrame, int totalFrames, int altCurrentFrame)
        {
            if (cap is not null)
            {
                // Position portion
                if (ViewModeCurrent == ViewMode.AllFrames ||
                    ViewModeCurrent == ViewMode.BestFrames ||
                    ViewModeCurrent == ViewMode.FilterFrames)
                {
                    string timeText;
                    if (totalFrames == -1 || totalFrames == 0)
                    {
                        double time = cap.Get(CapProp.PosMsec) / 1000.0;
                        timeText = $"Time {time:F2}s";
                    }
                    else
                    {
                        double time = cap.Get(CapProp.PosMsec) / 1000.0;
                        timeText = $"Time {time:F2}s";
                    }

                    timeTextBlock.Text = timeText;
                }
                else
                {
                    timeTextBlock.Text = string.Empty;
                }

                // Frame portion
                if (ViewModeCurrent == ViewMode.AllFrames ||
                    ViewModeCurrent == ViewMode.FilterFrames)
                {
                    string frameText;
                    if (totalFrames == -1 || totalFrames == 0)
                        frameText = $"Frame {currentFrame}";
                    else
                        frameText = $"Frame {currentFrame} / {totalFrames - 1}";

                    frameTextBlock.Text = frameText;
                }
                else if (ViewModeCurrent == ViewMode.BestFrames)
                {
                    string frameText;
                    if (totalFrames == -1 || totalFrames == 0)
                        frameText = $"Best {currentFrame} ({altCurrentFrame})";
                    else
                        frameText = $"Best {currentFrame} / {totalFrames - 1} ({altCurrentFrame})";

                    frameTextBlock.Text = frameText;
                }
                else
                {
                    frameTextBlock.Text = string.Empty;
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
                        Debug.WriteLine($"DeleteIfZeroByteFile: Deleted zero-byte file: {filePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteIfZeroByteFile: Error checking/deleting file: {ex.Message}");
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
        private string MakeAndCreateFramesDirectory(string basePath, string fileSpecMP4, bool trueRelativePathFalseAbsolute)
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
                    Debug.WriteLine($"{ToString()} MakeAndCreateFramesDirectory: Error creating save frame storage folder call: [{subfolderName}] inside: [{outputFolder}], {ex.Message}");
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
        /// This renders the position of the best frames on the 
        /// media timeline in the form of colored dots on the 
        /// timeline rectangle for both the left and right (if stereo) 
        /// media windows
        /// </summary>
        private void RenderMediaTimeLineDisplay()
        {
            if (calibrationStereoFrameSet is null)
                return;

            if (frameSize.Width <= 0 || frameSize.Height <= 0)
                return;

            RenderMediaTimeLineDisplaySide(trueLeftFalseRight: true);

            if (IsHeadStereo())
            {
                RenderMediaTimeLineDisplaySide(trueLeftFalseRight: false);
            }
        }


        /// <summary>
        /// Make a single List<BestFrame> for the indicated side 
        /// i.e. for the mono head this is really the same as the full BestFrameIndexes list, 
        /// for the stereo head this is the list of BestFrame instance where frame index is
        /// actual frame index not the virtual one
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        private void RenderMediaTimeLineDisplaySide(bool trueLeftFalseRight)
        {
            // Guard
            if (headType is null)
                return;

            // Get the best frame list for the current head type using the callback
            List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

            if (bestFramesList is not null)
            {
                // Do the actual render
                if (trueLeftFalseRight)
                    MediaTimeLineDisplayLeft.RenderBestFramesOnTimeline(bestFramesList);
                else
                    MediaTimeLineDisplayRight.RenderBestFramesOnTimeline(bestFramesList);
            }
        }


        /// <summary>
        /// Configures the media timeline display to highlight the calibration board range based on the current
        /// calibration data.
        /// </summary>
        /// <remarks>This method updates the left media timeline display to indicate the calibration board
        /// range. If the current mode is stereo, it also updates the right media timeline display accordingly. This
        /// setup ensures that the calibration board zone is visually represented on the timeline for reference during
        /// media review or analysis.</remarks>
        private void SetupMediaTimeLineDisplay()
        {
            // Get the calibration board zone start/stop indexes
            int startCalibrationBoardZone = calibrationStereoFrameSet.GetStartCalibrationBoardZone();
            int stopCalibrationBoardZone = calibrationStereoFrameSet.GetStopCalibrationBoardZone();

            // Get the left/right absolute frame indexes for the calibration board zone (same values in mono)
            (int leftIndexStart, int rightIndexStart) = calibrationStereoFrameSet.GetIndexes(startCalibrationBoardZone);
            (int leftIndexStop, int rightIndexStop) = calibrationStereoFrameSet.GetIndexes(stopCalibrationBoardZone);

            MediaTimeLineDisplayLeft.CalibrationBoardRange(leftIndexStart, leftIndexStop);
            MediaTimeLineDisplayLeft.RemoveAllBestFrames();

            if (IsHeadStereo())
            {
                MediaTimeLineDisplayRight.CalibrationBoardRange(rightIndexStart, rightIndexStop);
                MediaTimeLineDisplayRight.RemoveAllBestFrames();
            }
        }


        /// <summary>
        /// Change the operational mode of the Head
        /// This means the workflow has passed particular
        /// points
        /// </summary>
        /// <param name="newAppMode"></param>
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

                SetMediaControls(trueLeftFalseRight: true, null);
                SetMediaControls(trueLeftFalseRight: false, null);
            }
            if (AppModeCurrent == AppMode.Open)
            {
                // All Flag set correct;

                // Set view mode
                SetViewMode(ViewMode.AllFrames);

                FrameJump(trueLeftFalseRight: true, 0);
                FrameJump(trueLeftFalseRight: false, 0);
            }
            else if (AppModeCurrent == AppMode.FindCalibrationsFrames)
            {
                SetMediaControls(trueLeftFalseRight: true, null);
                SetMediaControls(trueLeftFalseRight: false, null);

                // Clear frame UI display data
                DecorateClear(trueLeftFalseRight: true);
                DecorateClear(trueLeftFalseRight: false);

                // Don't change the view mode
            }
            else if (AppModeCurrent == AppMode.BestFramesCalc)
            {
                // Clear frame UI display data
                DecorateClear(trueLeftFalseRight: true);
                DecorateClear(trueLeftFalseRight: false);

                // Change the view mode so we can see the frame count build in the UI
                SetViewMode(ViewMode.SensorCoverage);

                // Because we are process disable the media controls
                SetMediaControls(trueLeftFalseRight: true, null);
                SetMediaControls(trueLeftFalseRight: false, null);
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
            ViewMode oldViewMode = ViewModeCurrent;
            ViewModeCurrent = newViewMode;

            switch (ViewModeCurrent)
            {
                case ViewMode.AllFrames:
                    LeftSensorCoverage.Visibility = Visibility.Collapsed;
                    RightSensorCoverage.Visibility = Visibility.Collapsed;
                    MediaTimeLineDisplayLeft.Visibility = Visibility.Visible;
                    MediaTimeLineDisplayRight.Visibility = Visibility.Visible;

                    SetMediaControls(trueLeftFalseRight: true, ViewMode.AllFrames);
                    SetMediaControls(trueLeftFalseRight: false, ViewMode.AllFrames);


                    FrameJump(trueLeftFalseRight: true, null);

                    if (IsHeadStereo() && !isLocked)
                    {
                        // Display last shown 'AllFrames' frame                        
                        FrameJump(trueLeftFalseRight: false, null);
                    }
                    break;

                case ViewMode.BestFrames:
                    LeftSensorCoverage.Visibility = Visibility.Collapsed;
                    RightSensorCoverage.Visibility = Visibility.Collapsed;
                    MediaTimeLineDisplayLeft.Visibility = Visibility.Visible;
                    MediaTimeLineDisplayRight.Visibility = Visibility.Visible;

                    SetMediaControls(trueLeftFalseRight: true, ViewMode.BestFrames);
                    SetMediaControls(trueLeftFalseRight: false, ViewMode.BestFrames);

                    // Display last shown 'AllFrames' frame
                    BestFrameJump(null);
                    break;

                case ViewMode.FilterFrames:
                    throw new Exception($"{ToString()} Not implemented ViewMode.FilterFrames");

                case ViewMode.SensorCoverage:
                    SetMediaControls(trueLeftFalseRight: true, ViewMode.SensorCoverage);
                    SetMediaControls(trueLeftFalseRight: false, ViewMode.SensorCoverage);

                    LeftSensorCoverage.Visibility = Visibility.Visible;
                    RightSensorCoverage.Visibility = Visibility.Visible;
                    MediaTimeLineDisplayLeft.Visibility = Visibility.Collapsed;
                    MediaTimeLineDisplayRight.Visibility = Visibility.Collapsed;

                    // Defer until after layout so the Canvas has non-zero ActualWidth/ActualHeight
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        if (ViewModeCurrent == ViewMode.SensorCoverage)
                            RenderSensorCoverage();
                    });
                    break;
            }

            // Update the totals in the sensor and pose bin displays
            RefreshSensorBin(ViewModeCurrent, trueLeftFalseRight: true);
            RefreshPoseBin(ViewModeCurrent, trueLeftFalseRight: true);
            RefreshDepthBin(ViewModeCurrent, trueLeftFalseRight: true);
            RefreshSensorBin(ViewModeCurrent, trueLeftFalseRight: false);
            RefreshPoseBin(ViewModeCurrent, trueLeftFalseRight: false);
            RefreshDepthBin(ViewModeCurrent, trueLeftFalseRight: false);
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
                    gotoStartButtonIsEnabled = false;
                    frameBackButtonIsEnabled = false;
                    playPauseButtonIsEnabled = false;
                    frameForwardButtonIsEnabled = false;
                    gotoEndButtonIsEnabled = false;
                    goToFrameTextBoxIsVisable = false;
                    break;
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
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="frameEditText"></param>
        private void UserGoToFrameRequest(bool trueLeftFalseRight, TextBox frameEditText)
        {
            if (AppModeCurrent == AppMode.Close) return; // Only allow manual jump if Open 

            int currentFrame = trueLeftFalseRight ? _currentFrameLeft : _currentFrameRight;

            if (int.TryParse(frameEditText.Text, out int targetIndex) && targetIndex != currentFrame)
            {
                FrameJump(trueLeftFalseRight, targetIndex);

                // Always clear the Go To Frame TextBox after use
                frameEditText.Text = string.Empty;
            }
        }


        /// <summary>
        /// Used to remove all the automatically found best frames from the
        /// list of frames. The manual added/ignored frames are not removed.
        /// Manual frames are normalized so their <see cref="BestFrame.Reason"/> contains
        /// only <see cref="BestFrameReason.ManuallyIgnored"/> and/or <see cref="BestFrameReason.ManuallyAdded"/>.
        /// </summary>
        /// <param name="calibProject"></param>
        // COULD USE RemoveAllReasonBitFromBestFrameIndexes
        private void RemoveNonManualFramesFromBestFramesList()
        {
            // Guard
            if (headType is null)
                throw new Exception($"{ToString()} RemoveNonManualFramesFromBestFramesList: headType is null");

            // Get the best frame list for the current head type using the callback
            List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

            if (bestFramesList is not null)
            {
                // Remove everything that isn't manual (added/ignored)
                bestFramesList.RemoveAll(bf =>
                    (bf.Reason & (BestFrameReason.ManuallyIgnored | BestFrameReason.ManuallyAdded)) == 0);

                // Normalize remaining manual entries to contain ONLY manual bits
                for (int i = 0; i < bestFramesList.Count; i++)
                {
                    BestFrame bf = bestFramesList[i];

                    BestFrameReason manualBits = bf.Reason & (BestFrameReason.ManuallyIgnored | BestFrameReason.ManuallyAdded);
                    if (manualBits != bf.Reason)
                    {
                        bestFramesList[i] = new BestFrame(bf.FrameIndex, manualBits);
                    }
                }
            }
        }


        /// <summary>
        /// Merge the passed List<BestFrame> into the CalibProject best 
        /// frames list for this Head
        /// </summary>
        /// <param name="calibProject"></param>
        /// <param name="foundBestFrames"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        //??? NOT ACTUALLY USED
        //???private (int addedCount, int updatedCount) MergeBestFramesList(CalibProject calibProject, IReadOnlyList<BestFrame> foundBestFrames)
        //{
        //    // Guard
        //    if (headType is null)
        //        throw new Exception($"{ToString()} MergeBestFramesList: headType is null");

        //    // Remove any automatically generated frame from the
        //    // best frames list
        //    RemoveNonManualFramesFromBestFramesList();

        //    BestFramesHeadType bestFramesHeadType = ConvertHeadType((HeadType)headType);

        //    // Add the new best frames to the list
        //    int addedCount = 0;
        //    int updatedCount = 0;
        //    foreach (BestFrame foundBestFrame in foundBestFrames)
        //    {
        //        bool? trueAddedFalseUpdatedNullFailed = calibProject.Data.CalibrationBestFrames.AddBestFrame(bestFramesHeadType, foundBestFrame);
        //        if (trueAddedFalseUpdatedNullFailed == true)
        //            addedCount++;
        //        else if (trueAddedFalseUpdatedNullFailed == false)
        //            updatedCount++;
        //    }

        //    // Sort the best frames list into reason order 
        //    calibProject.Data.CalibrationBestFrames.Sort(bestFramesHeadType);

        //    return (addedCount, updatedCount);
        //}


        /// <summary>
        /// Get the sensor bin counts 
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns></returns>
        public Dictionary<(int binx, int biny), int> GetSensorBinCounts(UniversalCalibrationHead.ViewMode viewModel, bool trueLeftFalseRight)
        {
            var counts = new Dictionary<(int binx, int biny), int>();


            if (viewModel == UniversalCalibrationHead.ViewMode.AllFrames)
            {
                FrameData? target;

                foreach ((FrameData leftTarget, FrameData? rightTarget, _) in calibrationStereoFrameSet.Data.Frames.Values)
                {
                    if (trueLeftFalseRight)
                        target = leftTarget;
                    else
                        target = rightTarget;

                    if (target is not null)
                        ProcessFrameData(target, counts);
                }
            }
            else if (viewModel == UniversalCalibrationHead.ViewMode.BestFrames)
            {
                // Guard
                if (headType is null)
                    throw new Exception($"{ToString()} GetSensorBinCounts: headType is null");

                FrameData leftTarget;
                FrameData? rightTarget;
                FrameData? target;

                // Get the best frame list for the current head type using the callback
                List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

                if (bestFramesList is not null)
                {

                    foreach (BestFrame bestFrame in bestFramesList!)
                    {
                        int frameIndex = bestFrame.FrameIndex;

                        if (calibrationStereoFrameSet.Data.Frames.TryGetValue(frameIndex, out var tuple))
                        {
                            (leftTarget, rightTarget, _) = tuple;

                            if (trueLeftFalseRight)
                                target = leftTarget;
                            else
                                target = rightTarget;

                            if (target is not null)
                                ProcessFrameData(target, counts);
                        }
                    }
                }
            }

            // Helper
            static void ProcessFrameData(FrameData target, Dictionary<(int binx, int biny), int> counts)
            {
                foreach (var bin in target.SensorBinsOccupied)
                {
                    // Find the this bin in the counts list, if not found create an new entry in counts
                    counts[bin] = counts.GetValueOrDefault(bin) + 1;
                }
            }

            return counts;
        }


        /// <summary>
        /// Get the pose bin counts.
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns></returns>
        public Dictionary<(int binx, int biny), int> GetPoseBinCounts(UniversalCalibrationHead.ViewMode viewModel, bool trueLeftFalseRight)
        {
            // Guard
            if (headType is null)
                throw new Exception($"{ToString()} GetPoseBinCounts: headType is null");

            var counts = new Dictionary<(int binx, int biny), int>();
            if (viewModel == UniversalCalibrationHead.ViewMode.AllFrames)
            {
                FrameData? target;

                foreach ((FrameData leftTarget, FrameData? rightTarget, _) in calibrationStereoFrameSet.Data.Frames.Values)
                {
                    if (trueLeftFalseRight)
                        target = leftTarget;
                    else
                        target = rightTarget;

                    if (target is not null)
                        ProcessFrameData(target, counts);
                }
            }
            else if (viewModel == UniversalCalibrationHead.ViewMode.BestFrames)
            {
                FrameData leftTarget;
                FrameData? rightTarget;
                FrameData? target;

                // Get the best frame list for the current head type using the callback
                List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

                if (bestFramesList is not null)
                {

                    foreach (BestFrame bestFrame in bestFramesList!)
                    {
                        int frameIndex = bestFrame.FrameIndex;

                        if (calibrationStereoFrameSet.Data.Frames.TryGetValue(frameIndex, out var tuple))
                        {
                            (leftTarget, rightTarget, _) = tuple;

                            if (trueLeftFalseRight)
                                target = leftTarget;
                            else
                                target = rightTarget;

                            if (target is not null)
                                ProcessFrameData(target, counts);
                        }
                    }
                }
            }

            // Helper
            static void ProcessFrameData(FrameData target, Dictionary<(int binx, int biny), int> counts)
            {
                if (target.PoseBinX != -1 &&
                    target.PoseBinY != -1)
                {
                    // Increase the count for this pose bin   
                    counts[(target.PoseBinX, target.PoseBinY)] = counts.GetValueOrDefault((target.PoseBinX, target.PoseBinY)) + 1;
                }
            }

            return counts;
        }


        /// <summary>
        /// Get the depth bin counts.
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns></returns>
        public Dictionary<(int binx, int biny), int> GetDepthBinCounts(UniversalCalibrationHead.ViewMode viewModel, bool trueLeftFalseRight)
        {
            // Guard
            if (headType is null)
                throw new Exception($"{ToString()} GetPoseBinCounts: headType is null");

            var counts = new Dictionary<(int binx, int biny), int>();
            if (viewModel == UniversalCalibrationHead.ViewMode.AllFrames)
            {
                FrameData? target;

                foreach ((FrameData leftTarget, FrameData? rightTarget, _) in calibrationStereoFrameSet.Data.Frames.Values)
                {
                    if (trueLeftFalseRight)
                        target = leftTarget;
                    else
                        target = rightTarget;

                    if (target is not null)
                        ProcessFrameData(target, counts);
                }
            }
            else if (viewModel == UniversalCalibrationHead.ViewMode.BestFrames)
            {
                FrameData leftTarget;
                FrameData? rightTarget;
                FrameData? target;

                // Get the best frame list for the current head type using the callback
                List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

                if (bestFramesList is not null)
                {

                    foreach (BestFrame bestFrame in bestFramesList!)
                    {
                        int frameIndex = bestFrame.FrameIndex;

                        if (calibrationStereoFrameSet.Data.Frames.TryGetValue(frameIndex, out var tuple))
                        {
                            (leftTarget, rightTarget, _) = tuple;

                            if (trueLeftFalseRight)
                                target = leftTarget;
                            else
                                target = rightTarget;

                            if (target is not null)
                                ProcessFrameData(target, counts);
                        }
                    }
                }
            }

            // Helper
            static void ProcessFrameData(FrameData target, Dictionary<(int binx, int biny), int> counts)
            {
                if (target.DepthBinZ != -1)
                {
                    // Increase the count for this pose bin   
                    counts[(0, target.DepthBinZ)] = counts.GetValueOrDefault((0, target.DepthBinZ)) + 1;
                }
            }

            return counts;
        }


        /// <summary>
        /// Called to refresh the Sensor Bin UI element
        /// This method acts as a bridge between access to calibrationStereoFrameSet
        /// which GetSensorBinCounts uses and CalibrationFrameSetViewer.RefreshSensorBin
        /// </summary>
        /// <param name="viewMode"></param>
        /// <param name="trueLeftFalseRight"></param>
        public void RefreshSensorBin(UniversalCalibrationHead.ViewMode viewMode, bool trueLeftFalseRight)
        {
            // If the viewMode is SensorCoverage then still display the best frames in the sSensor bin
            UniversalCalibrationHead.ViewMode viewModeToUse = viewMode;
            if (viewMode == UniversalCalibrationHead.ViewMode.SensorCoverage)
                viewModeToUse = UniversalCalibrationHead.ViewMode.BestFrames;

            var counts = GetSensorBinCounts(viewModeToUse, trueLeftFalseRight);

            if (trueLeftFalseRight)
                CalibrationFrameSetViewerLeft.RefreshSensorBin(counts);
            else
                CalibrationFrameSetViewerRight.RefreshSensorBin(counts);
        }


        /// <summary>
        /// Called to refresh the Pose Bin UI element
        /// This method acts as a bridge between access to calibrationStereoFrameSet
        /// which GetPoseBinCounts uses and CalibrationFrameSetViewer.RefreshPoseBin
        /// </summary>
        /// <param name="viewMode"></param>
        /// <param name="trueLeftFalseRight"></param>
        public void RefreshPoseBin(UniversalCalibrationHead.ViewMode viewMode, bool trueLeftFalseRight)
        {
            // If the viewMode is SensorCoverage then still display the best frames in the sSensor bin
            UniversalCalibrationHead.ViewMode viewModeToUse = viewMode;
            if (viewMode == UniversalCalibrationHead.ViewMode.SensorCoverage)
                viewModeToUse = UniversalCalibrationHead.ViewMode.BestFrames;

            var counts = GetPoseBinCounts(viewModeToUse, trueLeftFalseRight);


            if (trueLeftFalseRight)
                CalibrationFrameSetViewerLeft.RefreshPoseBin(counts);
            else
                CalibrationFrameSetViewerRight.RefreshPoseBin(counts);

        }


        /// <summary>
        /// Called to refresh the Depth Bin UI element
        /// This method acts as a bridge between access to calibrationStereoFrameSet
        /// which GetDepthBinCounts uses and CalibrationFrameSetViewer.RefreshDepthBin
        /// </summary>
        /// <param name="viewMode"></param>
        /// <param name="trueLeftFalseRight"></param>
        public void RefreshDepthBin(UniversalCalibrationHead.ViewMode viewMode, bool trueLeftFalseRight)
        {
            // If the viewMode is SensorCoverage then still display the best frames in the sSensor bin
            UniversalCalibrationHead.ViewMode viewModeToUse = viewMode;
            if (viewMode == UniversalCalibrationHead.ViewMode.SensorCoverage)
                viewModeToUse = UniversalCalibrationHead.ViewMode.BestFrames;

            var counts = GetDepthBinCounts(viewModeToUse, trueLeftFalseRight);


            if (trueLeftFalseRight)
                CalibrationFrameSetViewerLeft.RefreshDepthBin(counts);
            else
                CalibrationFrameSetViewerRight.RefreshDepthBin(counts);

        }


        /// <summary>
        /// Note for this to be effective a call to CalibrationStereoFrameSet.ClearResults
        /// is required for the graph to be cleared
        /// </summary>
        /// <param name="viewMode"></param>
        public void ClearDisplay(UniversalCalibrationHead.ViewMode viewMode)
        {
            CalibrationFrameSetViewerLeft.HighLightActiveSensorBin(null);
            RefreshSensorBin(viewMode, trueLeftFalseRight: true);
            CalibrationFrameSetViewerLeft.HighLightActivePoseBin(null);
            RefreshPoseBin(viewMode, trueLeftFalseRight: true);
            CalibrationFrameSetViewerLeft.HighLightActiveDepthBin(null);
            RefreshDepthBin(viewMode, trueLeftFalseRight: true);

            // The stereo right side
            CalibrationFrameSetViewerLeft.DrawGraphs();
            if (IsHeadStereo())
            {
                CalibrationFrameSetViewerRight.HighLightActiveSensorBin(null);
                RefreshSensorBin(viewMode, trueLeftFalseRight: false);
                CalibrationFrameSetViewerRight.HighLightActivePoseBin(null);
                RefreshPoseBin(viewMode, trueLeftFalseRight: false);
                CalibrationFrameSetViewerRight.HighLightActiveDepthBin(null);
                RefreshDepthBin(viewMode, trueLeftFalseRight: false);
                CalibrationFrameSetViewerRight.DrawGraphs();
            }
        }


        /// <summary>
        /// Display either the frame set viewer or the iteration viewer
        /// </summary>
        /// <param name="trueFrameSetFalseIteration"></param>
        public void DisplayFrameSetOrIterationViewer(bool trueFrameSetFalseIteration)
        {
            if (trueFrameSetFalseIteration)
            {
                CalibrationFrameSetViewerLeft.Visibility = Visibility.Visible;
                CalibrationFrameSetViewerRight.Visibility = Visibility.Visible;
                CalibrationIterationViewerLeft.Visibility = Visibility.Collapsed;
            }
            else
            {
                CalibrationFrameSetViewerLeft.Visibility = Visibility.Collapsed;
                CalibrationFrameSetViewerRight.Visibility = Visibility.Collapsed;
                CalibrationIterationViewerLeft.Visibility = Visibility.Visible;
            }
        }


        /// <summary>
        /// Convert a HeadType to a BestFramesHeadType. This is needed because the 
        /// BestFrames data structure in the CalibProject is organized by 
        /// BestFramesHeadType which has a different enum definition than the HeadType 
        /// used in the UniversalCalibrationHead. The mapping is as follows:
        /// MonoLeft -> MonoLeft
        /// MonoRight -> MonoRight
        /// Stereo -> Stereo
        /// </summary>
        /// <param name="headType"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static BestFramesHeadType ConvertHeadType(HeadType headType)
        {
            return headType switch
            {
                HeadType.MonoLeft => BestFramesHeadType.MonoLeft,
                HeadType.MonoRight => BestFramesHeadType.MonoRight,
                HeadType.Stereo => BestFramesHeadType.Stereo,
                _ => throw new Exception($"Unexpected head type {headType}")
            };
        }


        /// <summary>
        /// Convert a HeadType instance value to a Reporter
        /// channel string 
        /// </summary>
        /// <param name="headType"></param>
        /// <returns></returns>
        private static string ChannelConvert(HeadType headType)
        {
            return headType switch
            {
                HeadType.MonoLeft => "Left",
                HeadType.MonoRight => "Right",
                HeadType.Stereo => "Stereo",
                _ => throw new Exception($"Unexpected head type {headType}")
            };
        }

    }
}