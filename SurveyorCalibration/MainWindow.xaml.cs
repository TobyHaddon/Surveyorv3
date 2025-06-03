using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Microsoft.UI.Dispatching;
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
    public class CalibProject
    {
        public class DataClass
        {
            public class MediaClass
            {
                public MediaClass()
                {
                    Clear();
                }

                public bool TrueFullCalibratiobSetFalseStereoOnly = true;
                public string LeftMonoMP4Path { get; set; } = string.Empty;
                public string RightMonoMP4Path { get; set; } = string.Empty;
                public string LeftStereoMP4Path { get; set; } = string.Empty;
                public string RightStereoMP4Path { get; set; } = string.Empty;

                public string LeftCameraID { get; set; } = string.Empty;

                public string RightCameraID { get; set; } = string.Empty;

                public void Clear()
                {
                    LeftMonoMP4Path = string.Empty;
                    RightMonoMP4Path = string.Empty;
                    LeftStereoMP4Path = string.Empty;
                    RightStereoMP4Path = string.Empty;
                    LeftCameraID = string.Empty;
                    RightCameraID = string.Empty;
                }
            }

            public class CalibrationClass
            {
                public CalibrationClass()
                {
                    Clear();
                }

                public Dictionary? Dictionary5x5_100 { get; set; }
                public CharucoBoard? Board5x5_100 { get; set; }
                public string BoardName { get; set; } = string.Empty;

                public void Clear()
                {
                    Dictionary5x5_100 = null;
                    Board5x5_100 = null;
                    BoardName = string.Empty;
                }
            }

            public MediaClass Media { get; set; } = new();
            public CalibrationClass Calibration { get; set; } = new();
        }

        public DataClass Data = new();
    }

    public sealed partial class MainWindow : WindowEx
    {
        private CalibProject calibProject = new();

        // Add these fields to MainWindow class
        private DispatcherQueueTimer? _stereoLockCheckTimer;

        public MainWindow()
        {
            // Restore the saved window state
            PersistenceId = "MainWindow";
            MinHeight = 600;
            MinWidth = 800;

            this.InitializeComponent();


            calibProject.Data.Media.LeftMonoMP4Path = @"C:\Users\tobyh\OneDrive\Docs\SVS2\SVS2 Media\125L (CEV22 Pool Left Solo Cailb).MP4";
            calibProject.Data.Media.RightMonoMP4Path = @"C:\Users\tobyh\OneDrive\Docs\SVS2\SVS2 Media\125R (CEV22 Pool Right Solo Cailb).MP4";
            calibProject.Data.Media.LeftStereoMP4Path = @"C:\Users\tobyh\OneDrive\Docs\SVS2\SVS2 Media\126L (CEV22 Pool Stereo Calib).MP4";
            calibProject.Data.Media.RightStereoMP4Path = @"C:\Users\tobyh\OneDrive\Docs\SVS2\SVS2 Media\126R (CEV22 Pool Stereo Calib).MP4";

            // Create the dictionary
            calibProject.Data.Calibration.Dictionary5x5_100 = new Dictionary(PredefinedDictionaryName.Dict5X5_100);

            // Create ChArUco board
            float squareLength = 40.0f / 1000.0f;
            float markerLength = 30.0f / 1000.0f;
            int squaresX = 14;
            int squaresY = 9;
            calibProject.Data.Calibration.Board5x5_100 = new CharucoBoard(squaresX, squaresY, squareLength, markerLength, calibProject.Data.Calibration.Dictionary5x5_100);


            // Pass the calibration board settings to the  calibration heads
            StereoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.Calibration.Dictionary5x5_100, calibProject.Data.Calibration.Board5x5_100, calibProject.Data.Calibration.BoardName);
            LeftMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.Calibration.Dictionary5x5_100, calibProject.Data.Calibration.Board5x5_100, calibProject.Data.Calibration.BoardName); 
            RightMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.Calibration.Dictionary5x5_100, calibProject.Data.Calibration.Board5x5_100, calibProject.Data.Calibration.BoardName);

            
            SetUIControls();
        }





        /// 
        /// EVENTS
        /// 

        private async void OpenAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            // Load the Info and Media user control to setup the survey
            CalibrationMediaUserControl.SetupForContentDialog(CalibrationMediaContentDialog);


            try
            {
                // ** Important notes **
                // The UserControl CalibrationMediaContentDialog is displayed within a ContentDialog for 
                // the purpose of setting up a new survey (also using from a SettingsCard)
                // I stuggled to get the ContentDialog to show width necessary to fully display
                // the UserControl.  The solution was to:
                // Set <x:Double x:Key="ContentDialogMaxWidth">1200</x:Double> in the <ResourceDictionary>
                // to setup the ContentDialog in XAML in MainWindow and place it in Grid.Row=2.
                // This took a lot of trail and error. It seems to effect the title bar is left in
                // default row zero.
                ContentDialogResult result = await CalibrationMediaContentDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    CalibrationMediaUserControl.SaveForContentDialog(calibProject);

                    // Open Media Files
                    // Open the media
                    StereoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                    if (calibProject.Data.Media.TrueFullCalibratiobSetFalseStereoOnly == false)
                    {
                        LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                        RightMonoCalibrationHead.OpenMedia(calibProject.Data.Media.RightMonoMP4Path, string.Empty);
                    }

                    // Check if cached results files are available
                    bool cachedResultsAvailable = false;

                    if (calibProject.Data.Media.TrueFullCalibratiobSetFalseStereoOnly == true)
                    {
                        if (StereoCalibrationHead.ResultsFileExists() &&
                            LeftMonoCalibrationHead.ResultsFileExists() &&
                            RightMonoCalibrationHead.ResultsFileExists())
                        {
                            cachedResultsAvailable = true;
                        }
                    }
                    else
                    {
                        if (StereoCalibrationHead.ResultsFileExists())
                        {
                            cachedResultsAvailable = true;
                        }
                    }

                    // Ask the user if they want to use cached results (a full set of results is required)
                    if (cachedResultsAvailable == true)
                    {
                        var dialogUseCahceResults = new ContentDialog
                        {
                            Title = "Cached Results Available",
                            Content = "This is a set of cache results available.  Would you like to use them?",
                            PrimaryButtonText = "Yes",
                            CloseButtonText = "No"
                        };
                        dialogUseCahceResults.XamlRoot = this.Content.XamlRoot; // Set the XamlRoot for proper display
                        if (await dialogUseCahceResults.ShowAsync() == ContentDialogResult.Primary)
                        {
                            // Load cached results
                            if (calibProject.Data.Media.TrueFullCalibratiobSetFalseStereoOnly == true)
                            {
                                StereoCalibrationHead.LoadResults();
                                LeftMonoCalibrationHead.LoadResults();
                                RightMonoCalibrationHead.LoadResults();
                            }
                            else
                            {
                                StereoCalibrationHead.LoadResults();
                            }
                        }
                    }


                    // Inform that user they need to lock the stereo calibration videos
                    var dialog = new ContentDialog
                    {
                        Title = "Stereo Calibration Videos",
                        Content = "Please sync the stereo calibration media and lock the videos before proceeding.",
                        CloseButtonText = "OK"
                    };
                    dialog.XamlRoot = this.Content.XamlRoot; // Set the XamlRoot for proper display
                    await dialog.ShowAsync();


                    // Start a timer to check if the stereo calibration is locked
                    StartStereoLockCheckTimer();

                }
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                Debug.WriteLine($"Error showing SurveyInfoAndMediaContentDialog: {ex.Message}");
            }

        }


        /// <summary>
        /// If ready find the calibration frame
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FindAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (calibProject.Data.Media.TrueFullCalibratiobSetFalseStereoOnly)
            {
                if (StereoCalibrationHead.IsOpen() &&
                    (bool)StereoCalibrationHead.IsStereoLocked()! &&
                    LeftMonoCalibrationHead.IsOpen() && 
                    RightMonoCalibrationHead.IsOpen())
                {
                    // Find the calibration frame in the stereo videos
                    StereoCalibrationHead.FindCalibrationFrame();
                    LeftMonoCalibrationHead.FindCalibrationFrame();
                    RightMonoCalibrationHead.FindCalibrationFrame();
                }
            }
            else 
            {
                if (StereoCalibrationHead.IsOpen() &&
                    (bool)StereoCalibrationHead.IsStereoLocked()!)
                {
                    // Find the calibration frame in the stereo videos
                    StereoCalibrationHead.FindCalibrationFrame();
                }
            }
        }


        /// <summary>
        /// Cancel button pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FindAppBarCancel_Click(object sender, RoutedEventArgs e)
        {
            if (StereoCalibrationHead.IsFindRunning())
                StereoCalibrationHead.FindCalibrationFrameCancel();

            if (LeftMonoCalibrationHead.IsFindRunning())
                LeftMonoCalibrationHead.FindCalibrationFrameCancel();

            if (RightMonoCalibrationHead.IsFindRunning())
                RightMonoCalibrationHead.FindCalibrationFrameCancel();
        }


        /// <summary>
        /// From the calibration frame find the best frames
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (calibProject.Data.Media.TrueFullCalibratiobSetFalseStereoOnly)
            {
                // Find the calibration frame in the stereo videos
                StereoCalibrationHead.BestFramesCalc();
                LeftMonoCalibrationHead.BestFramesCalc();
                RightMonoCalibrationHead.BestFramesCalc();


                StereoCalibrationHead.SaveResults();
                LeftMonoCalibrationHead.SaveResults();
                RightMonoCalibrationHead.SaveResults();
            }
            else
            {
                // Find the calibration frame in the stereo videos
                StereoCalibrationHead.BestFramesCalc();

                StereoCalibrationHead.SaveResults();
            }
        }






        ///
        /// PRIVATE
        /// 



        /// <summary>
        /// Set the UI controls to the current mode
        /// </summary>
        private void SetUIControls()
        {
            bool? isLocked = StereoCalibrationHead.IsStereoLocked();

            if (isLocked is not null && isLocked == true)
            {
                // Stereo is locked
                FindAppBarButton.IsEnabled = true; // Disable Find button for now
            }
            else
            {
                // Stereo is unlocked
                FindAppBarButton.IsEnabled = false; // Disable Find button for now
            }

            if (IsFindRunning())
            {
                FindAppBarCancel.IsEnabled = true; // Enable Cancel button if Find is running
            }
            else
            {
                FindAppBarCancel.IsEnabled = false; // Enable Cancel button if Find is running
            }

        }





        private void StartStereoLockCheckTimer()
        {
            if (_stereoLockCheckTimer != null)
                return;

            // Use DispatcherQueue.GetForCurrentThread() to get an instance of DispatcherQueue
            DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue == null)
            {
                throw new InvalidOperationException("DispatcherQueue is not available on the current thread.");
            }

            _stereoLockCheckTimer = dispatcherQueue.CreateTimer();
            _stereoLockCheckTimer.Interval = TimeSpan.FromSeconds(1);
            _stereoLockCheckTimer.Tick += StereoLockCheckTimer_Tick;
            _stereoLockCheckTimer.Start();
        }

        private void StopStereoLockCheckTimer()
        {
            if (_stereoLockCheckTimer != null)
            {
                _stereoLockCheckTimer.Stop();
                _stereoLockCheckTimer.Tick -= StereoLockCheckTimer_Tick;
                _stereoLockCheckTimer = null;
            }
        }

        private void StereoLockCheckTimer_Tick(object? sender, object e)
        {
            bool? isLocked = StereoCalibrationHead.IsStereoLocked();
            if (isLocked is not null && isLocked == true)
            {
                StopStereoLockCheckTimer();
                SetUIControls();
                // Optionally notify user that stereo is now locked
            }
        }


        /// <summary>
        /// Check if any of the FindCalibrationBoard methods are currently running.
        /// </summary>
        /// <returns></returns>
        private bool IsFindRunning()
        {
            if (StereoCalibrationHead.IsFindRunning() ||
                LeftMonoCalibrationHead.IsFindRunning() ||
                RightMonoCalibrationHead.IsFindRunning())
            { 
                return true; // At least one find operation is running
            }
            else
            {
                return false; // No find operations are running
            }
        }


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

