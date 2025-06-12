using Emgu.CV.Aruco;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Calibration;
using Surveyor.User_Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using WinUIEx;
using static Emgu.CV.Aruco.Dictionary;

namespace Surveyor
{
    public static class AppLaunchArgs
    {
        public static string? StereoLeft;
        public static string? StereoRight;
        public static string? MonoLeft;
        public static string? MonoRight;
        public static bool RunWithoutPrompts { get; set; } = false;
        public static bool UseCache { get; set; } = false;
        public static int? SyncFrameIndexLeft { get; set; }
        public static int? SyncFrameIndexRight { get; set; }

        public static bool? SaveBestFrames { get; set; } = null;
    }

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

                public CalibInfoAndMedia.StereoMonoMediaSetMode StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet;
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
            
            public MediaClass Media { get; set; } = new();

            public CharucoBoardDefinition CharucoBoardDefinition { get; set; } = new();
        }

        public DataClass Data = new();
    }

    public sealed partial class MainWindow : WindowEx
    {
        private CalibProject calibProject = new();

        // Add these fields to MainWindow class
        private DispatcherQueueTimer? _stereoLockCheckTimer;
        private DispatcherQueueTimer? _findCheckTimer;
        private DateTime? _findStartTime;

        private bool mediaFromCommandLine = false;
        private bool? findStatus = null;  // false started, true done
        private bool? saveStatus = null;  // None - Can't save, false - In Save, true - can save


        public MainWindow()
        {
            // Restore the saved window state
            PersistenceId = "MainWindow";
            MinHeight = 600;
            MinWidth = 800;

            this.InitializeComponent();


            // Create Charuco Board Definition
            calibProject.Data.CharucoBoardDefinition.Setup(new Dictionary(PredefinedDictionaryName.Dict5X5_100),
                                                        14/*SquareX*/, 9/*SquareY*/,
                                                        40.0f / 1000.0f/*SquareLength*/,
                                                        30.0f / 1000.0f/*MarkerLength*/);

            // Pass the calibration board settings to the  calibration heads
            StereoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.CharucoBoardDefinition);

            LeftMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.CharucoBoardDefinition);

            RightMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.CharucoBoardDefinition);

            // Get the Save Best Frame checkbox if /SaveBestFrames command line argument is set
            if (AppLaunchArgs.SaveBestFrames is not null)
            {
                SaveBestFrames.IsChecked = (bool)AppLaunchArgs.SaveBestFrames;
            }


            // Check for command line            
            bool isStereoLeft = false;
            bool isStereoRight = false;
            bool isMonoLeft = false;
            bool isMonoRight = false;

            if (!string.IsNullOrEmpty(AppLaunchArgs.StereoLeft) && File.Exists(AppLaunchArgs.StereoLeft))
            {
                calibProject.Data.Media.LeftStereoMP4Path = AppLaunchArgs.StereoLeft;
                isStereoLeft = true;
            }
            if (!string.IsNullOrEmpty(AppLaunchArgs.StereoRight) && File.Exists(AppLaunchArgs.StereoRight))
            {
                calibProject.Data.Media.RightStereoMP4Path = AppLaunchArgs.StereoRight;
                isStereoRight = true;
            }
            if (!string.IsNullOrEmpty(AppLaunchArgs.MonoLeft) && File.Exists(AppLaunchArgs.MonoLeft))
            {
                calibProject.Data.Media.LeftMonoMP4Path = AppLaunchArgs.MonoLeft;
                isMonoLeft = true;
            }
            if (!string.IsNullOrEmpty(AppLaunchArgs.MonoRight) && File.Exists(AppLaunchArgs.MonoRight))
            {
                calibProject.Data.Media.RightMonoMP4Path = AppLaunchArgs.MonoRight;
                isMonoRight = true;
            }

            if (isStereoLeft && isStereoRight && isMonoLeft && isMonoRight)
            {
                calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet;

                // Load cached results if available
                if (AppLaunchArgs.UseCache &&
                        StereoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path) &&
                        LeftMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftMonoMP4Path, string.Empty) &&
                        RightMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.RightMonoMP4Path, string.Empty))
                {
                    int? stereoFramesLoaded = StereoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                    int? leftMonoFramesLoaded = LeftMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                    int? rightMonoFramesLoaded = RightMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.RightMonoMP4Path, string.Empty);

                    if (stereoFramesLoaded is not null && leftMonoFramesLoaded is not null && rightMonoFramesLoaded is not null &&
                        stereoFramesLoaded > 0 && leftMonoFramesLoaded > 0 && rightMonoFramesLoaded > 0)
                    {
                        findStatus = true; // Frames have been extracted
                        saveStatus = true; // Can press save
                    }
                    else
                    {
                        // Added message to user here - to indicate cache failed to load/fullly load
                        Debug.WriteLine($"Tried to load Stereo cached results StereoCalibrationHead.LoadCachedResults returned={stereoFramesLoaded}, Original Stereo Left file:{calibProject.Data.Media.LeftStereoMP4Path},  Right file:{calibProject.Data.Media.RightStereoMP4Path}");
                        Debug.WriteLine($"Tried to load Mono Left/Right cached results LeftMonoCalibrationHead.LoadCachedResults returned={leftMonoFramesLoaded}, RightMonoCalibrationHead.LoadCachedResults returned={rightMonoFramesLoaded}, Original Mono Left file:{calibProject.Data.Media.LeftMonoMP4Path},  Right file:{calibProject.Data.Media.RightMonoMP4Path}");
                        findStatus = null;  // No frames loaded
                        saveStatus = null; // Can't press save
                    }
                }

                // Open Media Files
                StereoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                RightMonoCalibrationHead.OpenMedia(calibProject.Data.Media.RightMonoMP4Path, string.Empty);
                mediaFromCommandLine = true;

                if (AppLaunchArgs.SyncFrameIndexLeft is not null && AppLaunchArgs.SyncFrameIndexRight is not null)
                {
                    // Lock Media
                    StereoCalibrationHead.LockStereo((int)AppLaunchArgs.SyncFrameIndexLeft, (int)AppLaunchArgs.SyncFrameIndexRight);
                }

            }
            else if (isStereoLeft && isStereoRight)
            {
                calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet;

                // Load cached results if available
                if (AppLaunchArgs.UseCache &&
                        StereoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path))
                {
                    int? stereoFramesLoaded = StereoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                    if (stereoFramesLoaded is not null && stereoFramesLoaded > 0)
                    {
                        findStatus = true; // Frames have been extracted
                        saveStatus = true; // Can press save
                    }
                    else
                    {
                        // Added message to user here - to indicate cache failed to load/fullly load
                        Debug.WriteLine($"Tried to load Stereo cached results StereoCalibrationHead.LoadCachedResults returned={stereoFramesLoaded}, Original Stereo Left file:{calibProject.Data.Media.LeftStereoMP4Path},  Right file:{calibProject.Data.Media.RightStereoMP4Path}");
                        findStatus = null; // No frames loaded
                        saveStatus = true; // Can't press save
                    }
                }

                // Open Media Files
                StereoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                mediaFromCommandLine = true;

                if (AppLaunchArgs.SyncFrameIndexLeft is not null && AppLaunchArgs.SyncFrameIndexRight is not null)
                {
                    // Lock Media
                    StereoCalibrationHead.LockStereo((int)AppLaunchArgs.SyncFrameIndexLeft, (int)AppLaunchArgs.SyncFrameIndexRight);
                }
            }
            else if (isMonoLeft && isMonoRight)
            {
                calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet;

                // Load cached results if available
                if (AppLaunchArgs.UseCache &&
                        LeftMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftMonoMP4Path, string.Empty) &&
                        RightMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.RightMonoMP4Path, string.Empty))

                {
                    int? leftMonoFramesLoaded = LeftMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                    int? rightMonoFramesLoaded = RightMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.RightMonoMP4Path, string.Empty);
                    if (leftMonoFramesLoaded is not null && rightMonoFramesLoaded is not null &&
                        leftMonoFramesLoaded > 0 && rightMonoFramesLoaded > 0)
                    {
                        findStatus = true; // Frames have been extracted
                        saveStatus = true; // Can press save

                    }
                    else
                    {
                        // Added message to user here - to indicate cache failed to load/fullly load
                        Debug.WriteLine($"Tried to load Mono Left/Right cached results LeftMonoCalibrationHead.LoadCachedResults returned={leftMonoFramesLoaded}, RightMonoCalibrationHead.LoadCachedResults returned={rightMonoFramesLoaded}, Original Mono Left file:{calibProject.Data.Media.LeftMonoMP4Path},  Right file:{calibProject.Data.Media.RightMonoMP4Path}");
                        findStatus = null; // No frames loaded
                        saveStatus = true; // Can't press save
                    }
                }

                // Open Media Files
                LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                RightMonoCalibrationHead.OpenMedia(calibProject.Data.Media.RightMonoMP4Path, string.Empty);
                mediaFromCommandLine = true;
            }
            else if (isMonoLeft)
            {
                calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet;

                // Load cached results if available
                if (AppLaunchArgs.UseCache &&
                        LeftMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftMonoMP4Path, string.Empty))

                {
                    int? leftMonoFramesLoaded = LeftMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                    if (leftMonoFramesLoaded is not null && leftMonoFramesLoaded > 0)
                    {
                        findStatus = true; // Frames have been extracted
                        saveStatus = true; // Can press save
                    }
                    else
                    {
                        // Added message to user here - to indicate cache failed to load/fullly load
                        Debug.WriteLine($"Tried to load Mono Left cached results LeftMonoCalibrationHead.LoadCachedResults returned={leftMonoFramesLoaded}, Original Mono Left file:{calibProject.Data.Media.LeftMonoMP4Path}");
                        findStatus = null; // No frames loaded
                        saveStatus = true; // Can't press save

                    }

                }

                // Open Media Files
                LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                mediaFromCommandLine = true;
            }

            // Auto run
            if (AppLaunchArgs.RunWithoutPrompts)
            {
                if (findStatus == null && mediaFromCommandLine)
                {
                    FindAppBarButton_Click(null!, null!);
                }
            }

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

                    // Reset
                    findStatus = null;  // No frames loaded
                    saveStatus = null;  // Can't press save


                    // Check if cached results files are available
                    bool cachedResultsAvailable = false;

                    switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                    {
                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                            if (StereoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path) &&
                                LeftMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftMonoMP4Path, string.Empty) &&
                                RightMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.RightMonoMP4Path, string.Empty))
                            {
                                cachedResultsAvailable = true;
                            }
                            break;

                        case CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet:
                            if (StereoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path))
                            {
                                cachedResultsAvailable = true;
                            }
                            break;

                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                            if (LeftMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftMonoMP4Path, string.Empty) &&
                                RightMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.RightMonoMP4Path, string.Empty))
                            {
                                cachedResultsAvailable = true;
                            }
                            break;

                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                            if (LeftMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftMonoMP4Path, string.Empty))
                            {
                                cachedResultsAvailable = true;
                            }
                            break;
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
                            bool loaded = false;
                            int? stereoFramesLoaded = null;
                            int? leftMonoFramesLoaded = null;
                            int? rightMonoFramesLoaded = null;

                            // Load cached results
                            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                            {
                                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                                    try
                                    {
                                        stereoFramesLoaded = StereoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                                        leftMonoFramesLoaded = LeftMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                                        rightMonoFramesLoaded = RightMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.RightMonoMP4Path, string.Empty);

                                        if (stereoFramesLoaded is not null && leftMonoFramesLoaded is not null && rightMonoFramesLoaded is not null &&
                                            stereoFramesLoaded > 0 && leftMonoFramesLoaded > 0 && rightMonoFramesLoaded > 0)
                                        {
                                            findStatus = true;  // Frames loaded from cache
                                            saveStatus = true;  // Can press save
                                            loaded = true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error loading cached results: {ex.Message}");
                                        // Handle the error, e.g., show a message to the user
                                    }

                                    break;

                                case CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet:
                                    try
                                    {
                                        stereoFramesLoaded = StereoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);

                                        if (stereoFramesLoaded is not null && stereoFramesLoaded > 0)
                                        {
                                            findStatus = true;  // Frames loaded from cache
                                            saveStatus = true;  // Can press save
                                            loaded = true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error loading cached results: {ex.Message}");
                                        // Handle the error, e.g., show a message to the user
                                    }
                                    break;

                                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                                    try
                                    {
                                        leftMonoFramesLoaded = LeftMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                                        rightMonoFramesLoaded = RightMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.RightMonoMP4Path, string.Empty);

                                        if (leftMonoFramesLoaded is not null && rightMonoFramesLoaded is not null &&
                                            leftMonoFramesLoaded > 0 && rightMonoFramesLoaded > 0)
                                        {
                                            findStatus = true;  // Frames loaded from cache
                                            saveStatus = true;  // Can press save
                                            loaded = true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error loading cached results: {ex.Message}");
                                        // Handle the error, e.g., show a message to the user
                                    }
                                    break;

                                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                                    try
                                    {
                                        leftMonoFramesLoaded = LeftMonoCalibrationHead.LoadCachedResults(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);

                                        if (leftMonoFramesLoaded is not null && leftMonoFramesLoaded > 0)
                                        {
                                            findStatus = true;  // Frames loaded from cache
                                            saveStatus = true;  // Can press save
                                            loaded = true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error loading cached results: {ex.Message}");
                                        // Handle the error, e.g., show a message to the user
                                    }
                                    break;
                            }

                            // Check there are > 0 frames
                            if (loaded)
                            {
                                bool warn = false;
                                string contentText = string.Empty;

                                switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                                {
                                    case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                                        if (stereoFramesLoaded == 0 || leftMonoFramesLoaded == 0 || rightMonoFramesLoaded == 0)
                                        {
                                            contentText = $"The cached results not loaded of incomplete.\n\n" +
                                                $"   Stereo Frames Loaded: {stereoFramesLoaded}\n" +
                                                $"   Left Mono Frames Loaded: {leftMonoFramesLoaded}\n" +
                                                $"   Right Mono Frames Loaded: {rightMonoFramesLoaded}\n";
                                            warn = true;
                                        }
                                        break;

                                    case CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet:
                                        if (stereoFramesLoaded == 0)
                                        {
                                            contentText = $"The cached results not loaded of incomplete.\n\n" +
                                                $"   Stereo Frames Loaded: {stereoFramesLoaded}\n";
                                            warn = true;
                                        }
                                        break;

                                    case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                                        if (leftMonoFramesLoaded == 0 || rightMonoFramesLoaded == 0)
                                        {
                                            contentText = $"The cached results not loaded of incomplete.\n\n" +
                                                $"   Left Mono Frames Loaded: {leftMonoFramesLoaded}\n" +
                                                $"   Right Mono Frames Loaded: {rightMonoFramesLoaded}\n";
                                            warn = true;
                                        }
                                        break;

                                    case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                                        if (leftMonoFramesLoaded == 0)
                                        {
                                            contentText = $"The cached results not loaded of incomplete.\n\n" +
                                                $"   Left Mono Frames Loaded: {leftMonoFramesLoaded}\n";
                                            warn = true;
                                        }
                                        break;
                                }

                                if (warn)
                                {
                                    // Inform the user that the cached results could not be loaded
                                    var errorDialog = new ContentDialog
                                    {
                                        Title = "Error Loading Cached Results",
                                        Content = contentText,
                                        CloseButtonText = "OK"
                                    };
                                    errorDialog.XamlRoot = this.Content.XamlRoot; // Set the XamlRoot for proper display
                                    await errorDialog.ShowAsync();
                                }
                            }

                            // Error loading
                            if (!loaded)
                            {
                                // Inform the user that the cached results could not be loaded
                                var errorDialog = new ContentDialog
                                {
                                    Title = "Error Loading Cached Results",
                                    Content = "The cached results could not be loaded.",
                                    CloseButtonText = "OK"
                                };
                                errorDialog.XamlRoot = this.Content.XamlRoot; // Set the XamlRoot for proper display
                                await errorDialog.ShowAsync();
                            }

                            SetUIControls();
                        }
                    }


                    // Open Media Files
                    switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                    {
                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                            StereoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                            LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                            RightMonoCalibrationHead.OpenMedia(calibProject.Data.Media.RightMonoMP4Path, string.Empty);
                            break;

                        case CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet:
                            StereoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                            break;

                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                            LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                            RightMonoCalibrationHead.OpenMedia(calibProject.Data.Media.RightMonoMP4Path, string.Empty);
                            break;

                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                            LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                            break;
                    }

                    // Ask user to sync the stereo videos
                    switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                    {
                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        case CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet:

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
                            break;

                        default:
                            SetUIControls();
                            break;
                    }

                    // Jump to first frame
                    bool stereoJumpFirstFrame = false;
                    bool monoLeftJumpFirstFrame = false;
                    bool monoRightJumpFirstFrame = false;
                    switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                    {
                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                            stereoJumpFirstFrame = true;
                            monoLeftJumpFirstFrame = true;
                            monoRightJumpFirstFrame = true;
                            break;
                        case CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet:
                            stereoJumpFirstFrame = true;
                            break;
                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                            monoLeftJumpFirstFrame = true;
                            monoRightJumpFirstFrame = true;
                            break;
                        case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                            monoLeftJumpFirstFrame = true;
                            break;
                        default:
                            break;

                    }
                    if (stereoJumpFirstFrame)
                    {
                    }
                    if (monoLeftJumpFirstFrame)
                    {
                    }
                    if (monoRightJumpFirstFrame)
                    {
                    }
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
            bool started = false;

            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
            {
                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()! &&
                        LeftMonoCalibrationHead.IsOpen() &&
                        RightMonoCalibrationHead.IsOpen())
                    {
                        // Find the calibration frame in the stereo and mono calibration videos
                        StereoCalibrationHead.FindCalibrationFrame();
                        LeftMonoCalibrationHead.FindCalibrationFrame();
                        RightMonoCalibrationHead.FindCalibrationFrame();
                        started = true;
                    }
                    break;
                case CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()!)
                    {
                        // Find the calibration frame in the stereo videos
                        StereoCalibrationHead.FindCalibrationFrame();
                        started = true;
                    }
                    break;
                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (LeftMonoCalibrationHead.IsOpen() &&
                        RightMonoCalibrationHead.IsOpen())
                    {
                        // Find the calibration frame in the mono videos
                        LeftMonoCalibrationHead.FindCalibrationFrame();
                        RightMonoCalibrationHead.FindCalibrationFrame();
                        started = true;
                    }
                    break;
                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    if (LeftMonoCalibrationHead.IsOpen())
                    {
                        // Find the calibration frame in the mono video
                        LeftMonoCalibrationHead.FindCalibrationFrame();
                        started = true;
                    }
                    break;

            }

            if (started)
            {
                StartFindCheckTimer();
                findStatus = false;
                saveStatus = null;
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
        private async void SaveAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            await Save();
        }

        private async Task Save()
        {
            saveStatus = false;  // Save/Calc in progress (disable the save button
            InProgress.IsActive = true;
            SetUIControls();

            bool doStereo = false;
            bool doLeftMono = false;
            bool doRightMono = false;

            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
            {

                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()! &&
                        LeftMonoCalibrationHead.IsOpen() &&
                        RightMonoCalibrationHead.IsOpen())
                    {
                        doStereo = true;
                        doLeftMono = true;
                        doRightMono = true;
                    }
                    break;
                case CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()!)
                    {
                        doStereo = true;
                    }
                    break;
                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (LeftMonoCalibrationHead.IsOpen() &&
                        RightMonoCalibrationHead.IsOpen())
                    {
                        doLeftMono = true;
                        doRightMono = true;
                    }
                    break;
                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    if (LeftMonoCalibrationHead.IsOpen())
                    {
                        doLeftMono = true;
                    }
                    break;
            }

            // Save the frames
            if (doStereo)
            {
                await DisplayStatusText("Pre-save stereo best frames...");
                InProgress.IsActive = true;
                StereoCalibrationHead.SaveCachedResults();
                InProgress.IsActive = false;
            }
            if (doLeftMono)
            {
                await DisplayStatusText("Pre-save left mono best frames...");
                InProgress.IsActive = true;
                LeftMonoCalibrationHead.SaveCachedResults();
                InProgress.IsActive = false;
            }
            if (doRightMono)
            {
                await DisplayStatusText("Pre-save right mono best frames...");
                InProgress.IsActive = true;
                RightMonoCalibrationHead.SaveCachedResults();
                InProgress.IsActive = false;
            }



            // Find the calibration frame in the stereo videos
            bool writePngFiles = SaveBestFrames.IsChecked == true;

            if (doStereo)
            {
                await DisplayStatusText("Best frames calc stereo...");
                InProgress.IsActive = true;
                await StereoCalibrationHead.BestFramesCalc(writePngFiles);
                InProgress.IsActive = false;
            }
            if (doLeftMono)
            {
                await DisplayStatusText("Best frames calc left mono...");
                InProgress.IsActive = true;
                await LeftMonoCalibrationHead.BestFramesCalc(writePngFiles);
                InProgress.IsActive = false;
            }
            if (doRightMono)
            {
                await DisplayStatusText("Best frames calc right mono...");
                InProgress.IsActive = true;
                await RightMonoCalibrationHead.BestFramesCalc(writePngFiles);
                InProgress.IsActive = false;
            }

            // Save the frames
            if (doStereo)
            {
                await DisplayStatusText("Save stereo best frames...");
                InProgress.IsActive = true;
                StereoCalibrationHead.SaveCachedResults();
                InProgress.IsActive = false;
            }
            if (doLeftMono)
            {
                await DisplayStatusText("Save left mono best frames...");
                InProgress.IsActive = true;
                LeftMonoCalibrationHead.SaveCachedResults();
                InProgress.IsActive = false;
            }
            if (doRightMono)
            {
                await DisplayStatusText("Save right mono best frames...");
                InProgress.IsActive = true;
                RightMonoCalibrationHead.SaveCachedResults();
                InProgress.IsActive = false;
            }

            await DisplayStatusText("");
            InProgress.IsActive = false;
            saveStatus = true;  // Allowed to press the save button again
            SetUIControls();
        }

        /// <summary>
        /// Display and copy the cache folder to the clipbaord
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ShowCacheFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Get local folder path
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;

            var dialog = new ContentDialog
            {
                Title = "Cached Results Folder",
                Content = $"The cached results are stored in:\n\n{localFolder.Path}\n\nThe path has been copied to the clipboard.",
                CloseButtonText = "Cancel"
            };
            dialog.XamlRoot = this.Content.XamlRoot; // Set the XamlRoot for proper display
            await dialog.ShowAsync();

            var dataPackage = new DataPackage();
            dataPackage.SetText(localFolder.Path);
            Clipboard.SetContent(dataPackage);

        }





        ///
        /// PRIVATE
        /// 

        /// <summary>
        /// Display a status text in the StatusText TextBlock
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private async Task DisplayStatusText(TimeSpan elapsedTime)
        {
            string formatted = elapsedTime.ToString(@"hh\:mm\:ss");
            await DisplayStatusText($"Elapsed Time: {formatted}");
        }

        private async Task DisplayStatusText(string text)
        {
            StatusText.Text = text;

            await Task.Delay(50);
        }



        /// <summary>
        /// Set the UI controls to the current mode
        /// </summary>
        private void SetUIControls()
        {
            bool? isLocked = StereoCalibrationHead.IsStereoLocked();

            // Load Button
            if (mediaFromCommandLine)
            {
                OpenAppBarButton.IsEnabled = false;
            }
            else
            {
                OpenAppBarButton.IsEnabled = true;
            }

            // Find Button
            if (findStatus is null)
            {
                if ((isLocked is not null && isLocked == true) ||
                    calibProject.Data.Media.StereoMonoMediaSetMode == CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet ||
                    calibProject.Data.Media.StereoMonoMediaSetMode == CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet)
                {
                    // Stereo is locked
                    FindAppBarButton.IsEnabled = true; // Disable Find button for now
                }
                else
                {
                    // Stereo is unlocked
                    FindAppBarButton.IsEnabled = false; // Disable Find button for now
                }
            }
            else
            {
                // Stereo is unlocked
                FindAppBarButton.IsEnabled = false; // Disable Find button for now                
            }

            // Save Button
            if (saveStatus == true/*findStatus is not null && findStatus == true*/)
            {
                SaveAppBarButton.IsEnabled = true; // Enable Save button if Find is done
                SaveBestFrames.IsEnabled = true;
            }
            else
            {
                SaveAppBarButton.IsEnabled = false; // Disable Save button if Find is not done
                SaveBestFrames.IsEnabled = false;
            }

            // Cancel Find Button
            if (IsFindRunning())
            {
                FindAppBarCancel.IsEnabled = true; // Enable Cancel button if Find is running
            }
            else
            {
                FindAppBarCancel.IsEnabled = false; // Enable Cancel button if Find is running
            }

        }




        /// <summary>
        /// Start a timer to check if the stereo calibration is locked.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
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
        /// Start a timer to display the elapsed time
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>

        private void StartFindCheckTimer()
        {
            if (_findCheckTimer != null)
                return;

            // Use DispatcherQueue.GetForCurrentThread() to get an instance of DispatcherQueue
            DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue == null)
            {
                throw new InvalidOperationException("DispatcherQueue is not available on the current thread.");
            }

            _findCheckTimer = dispatcherQueue.CreateTimer();
            _findCheckTimer.Interval = TimeSpan.FromSeconds(1);
            _findCheckTimer.Tick += StereoFindCheckTimer_Tick;
            _findStartTime = DateTime.Now;
            _findCheckTimer.Start();
        }
        private void StopFindCheckTimer()
        {
            if (_findCheckTimer != null)
            {
                _findCheckTimer.Stop();
                _findCheckTimer.Tick -= StereoFindCheckTimer_Tick;
                _findCheckTimer = null;
            }
        }
        private async void StereoFindCheckTimer_Tick(object? sender, object e)
        {
            if (_findStartTime is not null)
            {
                SetUIControls();

                if (IsFindRunning())
                {
                    TimeSpan elapsed = DateTime.Now - (DateTime)_findStartTime;
                    _ = DisplayStatusText(elapsed);
                }
                else
                {
                    findStatus = true; // Frames finished loading
                    saveStatus = true; // Can't press save

                    StopFindCheckTimer();
                    _findStartTime = null; // Reset the start time
                    _ = DisplayStatusText("");

                    // Check for auto run
                    if (AppLaunchArgs.RunWithoutPrompts)
                    {
                        Debug.WriteLine("Auto run: Save results after find is done.");
                        await Save(); // Automatically save results if find is done

                        Debug.WriteLine("Auto run: Exit Aplication.");
                        //??? TODO
                    }

                    SetUIControls(); // Update UI controls after find operation completes
                }
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


    }
}

