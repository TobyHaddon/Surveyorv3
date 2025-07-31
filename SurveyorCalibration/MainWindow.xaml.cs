using Emgu.CV.Aruco;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Calibration;
using SurveyorCalibrationData;
using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinUIEx;
using static Emgu.CV.Aruco.Dictionary;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Newtonsoft.Json;
using System.Drawing;
using System.Linq;


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

            public string CalibFileSpec { get; set; } = string.Empty;

            public MediaClass Media { get; set; } = new();

            [JsonIgnore]
            public CharucoBoardDefinition CharucoBoardDefinition { get; set; } = new();

            // Left & right mono calibration result sets (different results for different calibration flags)
            public MonoCalibrationCameraData?[] LeftMonoCalibrationCameraDataArray { get; set; } = new MonoCalibrationCameraData?[Enum.GetValues<CalibrationParameters>().Length];
            public MonoCalibrationCameraData?[] RightMonoCalibrationCameraDataArray { get; set; } = new MonoCalibrationCameraData?[Enum.GetValues<CalibrationParameters>().Length];

            // Stereo result sets  (different results for different calibration flags)
            public CalibrationStereoCameraData?[] CalibrationStereoCameraDataArray { get; set; } = new CalibrationStereoCameraData?[Enum.GetValues<CalibrationParameters>().Length];
        }

        public DataClass Data = new();


        /// <summary>
        /// Save the calibration project data to a file as json.
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public async Task<bool> Save(string fileSpec)
        {
            // Remember the calib project file spec
            Data.CalibFileSpec = fileSpec;

            return await Save();
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<bool> Save()
        {
            bool ret = false;

            if (string.IsNullOrEmpty(Data.CalibFileSpec))
                return ret;

            // Delete a .bak file if it exists
            string bakFileSpec = Path.ChangeExtension(Data.CalibFileSpec, ".bak");
            if (File.Exists(bakFileSpec))
            {
                try
                {
                    File.Delete(bakFileSpec);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting backup file {bakFileSpec}: {ex.Message}");
                }
            }

            // Rename fileSpec to a .bak file
            if (File.Exists(Data.CalibFileSpec))
            {
                try
                {
                    File.Move(Data.CalibFileSpec, bakFileSpec);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error renaming file {Data.CalibFileSpec} to backup {bakFileSpec}: {ex.Message}");
                    return false; // Return false if renaming fails
                }
            }

            // Save the project data as json to the file
            try
            {
                var options = CreateJsonOptions();
                string json = JsonConvert.SerializeObject(Data, options);
                await File.WriteAllTextAsync(Data.CalibFileSpec, json);
                ret = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving calibration project data to {Data.CalibFileSpec}: {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// Load the calibration project data from a file as json.
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public bool Load(string fileSpec)
        {
            if (!File.Exists(fileSpec))
            {
                Debug.WriteLine($"Calibration project file {fileSpec} does not exist.");
                return false;
            }
            try
            {
                string json = File.ReadAllText(fileSpec);
                var options = CreateJsonOptions();
                Data = JsonConvert.DeserializeObject<DataClass>(json, options) ?? new DataClass();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading calibration project data from {fileSpec}: {ex.Message}");
                return false;
            }
        }

        private static JsonSerializerSettings CreateJsonOptions()
        {
            var opts = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
            //???opts.Converters.Add(new Double2DArrayConverter());
            opts.Converters.Add(new MatrixJsonConverter());
            // opts.Converters.Add(new Float2DArrayConverter()); // if needed
            return opts;
        }
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

        public double MovementMaxThreshold { get; set; } = 20.0;
        public double BlurMaxThreshold { get; set; } = 2.5;
        public int MonoCornersMinThreshold { get; set; } = CalibrationStereoFrameSet.MONO_CORNER_COUNT_THESHOLD;
        public int StereoCornersMinThreshold { get; set; } = CalibrationStereoFrameSet.STEREO_CORNER_COUNT_THESHOLD;

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
                                                        39.92f / 1000.0f/*SquareLength*/,
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
            
            // Set the sliders
            SetMovementAndBlurSliderMax();


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
                mediaFromCommandLine = true;
            }
            else if (isStereoLeft && isStereoRight)
            {
                calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet;
                mediaFromCommandLine = true;
            }
            else if (isMonoLeft && isMonoRight)
            {
                calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet;
                mediaFromCommandLine = true;
            }
            else if (isMonoLeft)
            {
                calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet;
                mediaFromCommandLine = true;
            }


            if (mediaFromCommandLine)
                _ = OpenMedia(calibProject, true/*forceUsdCacheIfAvalable*/, AppLaunchArgs.RunWithoutPrompts/*noPrompts*/);

        }


        /// 
        /// EVENTS
        /// 

        private async void NewAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            // Load the Info and Media user control to setup the survey
            CalibrationMediaUserControl.SetupForContentDialog(CalibrationMediaContentDialog);

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

                await OpenMedia(calibProject, false/*forceUsdCacheIfAvalable*/, false/*noPrompts*/);
            }

            // Save the calib project file
            var file = await PickCalibFileToSaveAsync(this); // 'this' refers to your Window instance
            if (file != null)
            {
                try
                {
                    // Save the calib project data to the file
                    await calibProject.Save(file.Path);
                    Debug.WriteLine($"Calibration project saved to {file.Path}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error saving calibration project: {ex.Message}");
                    // Handle the error, e.g., show a message to the user
                }
            }
            else
            {
                Debug.WriteLine("No file selected for saving calibration project.");
            }
        }
        public async Task<StorageFile?> PickCalibFileToSaveAsync(Window window)
        {
            var savePicker = new FileSavePicker();

            // Initialize with the window handle
            var hWnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(savePicker, hWnd);

            // Set file type choices and default extension
            savePicker.FileTypeChoices.Add("Calibration File", new List<string>() { ".calib" });
            savePicker.DefaultFileExtension = ".calib";

            // Optional: set suggested file name
            savePicker.SuggestedFileName = "my_calibration";

            StorageFile file = await savePicker.PickSaveFileAsync();
            return file;
        }


        /// <summary>
        /// Find and open a project file and open the associated media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OpenAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            // Ensure your class has access to the current Window
            var window = this; // If this method is inside your MainWindow or a class inheriting from Window

            var openPicker = new Windows.Storage.Pickers.FileOpenPicker();

            // Initialize with the window handle (WinUI 3 requirement)
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

            // Set up picker filters
            openPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".calib");

            // Let user pick a single file
            var file = await openPicker.PickSingleFileAsync();

            if (file != null)
            {
                // Load the project               
                if (calibProject.Load(file.Path))
                {

                    // Call OpenMedia
                    await OpenMedia(calibProject, false, false);
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Load Failed",
                        Content = "Failed to load the selected calibration project file.",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }
        }


        private async Task OpenMedia(CalibProject calibProject, bool forceUsdCacheIfAvalable, bool noPrompts)
        { 

            try
            {
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
                    
                    if (forceUsdCacheIfAvalable || await dialogUseCahceResults.ShowAsync() == ContentDialogResult.Primary)
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

                        // Check if stereo lock needed
                        if (loaded)
                        {
                            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                            {
                                case CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                                case CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet:
                                    // Override the sync frame if this already exist
                                    // (they may have been set in LoadResults())
                                    if (AppLaunchArgs.SyncFrameIndexLeft is not null && AppLaunchArgs.SyncFrameIndexRight is not null)
                                    {
                                        // Lock Media
                                        StereoCalibrationHead.LockStereo((int)AppLaunchArgs.SyncFrameIndexLeft, 
                                                                         (int)AppLaunchArgs.SyncFrameIndexRight);
                                    }
                                    break;
                            }

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
                                if (noPrompts)
                                {
                                    Debug.WriteLine(contentText);
                                }
                                else
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
                        }

                        // Error loading
                        if (!loaded)
                        {
                            if (noPrompts)
                            {
                                Debug.WriteLine("The cached results could not be loaded.");
                            }
                            else
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
                if (StereoCalibrationHead.IsStereoLocked() == false)
                {
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
            await Save(calibProject);
        }

        private async Task Save(CalibProject calibProject)
        {
            saveStatus = false;  // Save/Calc in progress (disable the save button
            InProgress.IsActive = true;
            SetUIControls();

            bool doStereo = false;
            bool doLeftMono = false;
            bool doRightMono = false;
            bool useMonoCacheValues = false;

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
            // Check if mono calibration is need but there are old result that could be used
            if (doLeftMono && doRightMono)
            {
                // Check for any cached mono calibration results
                if (calibProject.Data.LeftMonoCalibrationCameraDataArray.Any(item => item != null) &&
                    calibProject.Data.RightMonoCalibrationCameraDataArray.Any(item => item != null))
                {
                    // Get local folder path
                    StorageFolder localFolder = ApplicationData.Current.LocalFolder;

                    var dialog = new ContentDialog
                    {
                        Title = "Mono Calibration",
                        Content = $"There is an existing mono calibration set. If you wish to reuse (quick) press 'Yes' else to recalculate (slow) press 'No'?",
                        PrimaryButtonText = "Yes",
                        CloseButtonText = "No"
                    };
                    dialog.XamlRoot = this.Content.XamlRoot; // Set the XamlRoot for proper display
                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        // Use the cahced mono calibration results
                        useMonoCacheValues = true;
                    }
                    else 
                    {
                        // Remove cached mono calibration results
                        Array.Fill(calibProject.Data.LeftMonoCalibrationCameraDataArray, null);
                        Array.Fill(calibProject.Data.RightMonoCalibrationCameraDataArray, null);
                    }
                }
            }

            // Save the frames
            if (doStereo)
            {
                await DisplayStatusText("Pre-save stereo best frames...");
                InProgress.IsActive = true;
                StereoCalibrationHead.SaveCachedResults();
                InProgress.IsActive = false;
            }
            if (doLeftMono && !useMonoCacheValues)
            {
                await DisplayStatusText("Pre-save left mono best frames...");
                InProgress.IsActive = true;
                LeftMonoCalibrationHead.SaveCachedResults();
                InProgress.IsActive = false;
            }
            if (doRightMono && !useMonoCacheValues)
            {
                await DisplayStatusText("Pre-save right mono best frames...");
                InProgress.IsActive = true;
                RightMonoCalibrationHead.SaveCachedResults();
                InProgress.IsActive = false;
            }



            // Find the calibration frame in the stereo videos
            bool writePngFiles = SaveBestFrames.IsChecked == true;
                       
            if (doLeftMono)
            {
                await DisplayStatusText("Best frames calc left mono...");
                InProgress.IsActive = true;
                await LeftMonoCalibrationHead.BestFramesCalcAndMonoCalibration(
                                                             calibProject,
                                                             true/*trueLeftFalseRight*/,
                                                             MovementMaxThreshold, 
                                                             BlurMaxThreshold,
                                                             MonoCornersMinThreshold,
                                                             useMonoCacheValues,
                                                             writePngFiles);
                InProgress.IsActive = false;
            }
            if (doRightMono)
            {
                await DisplayStatusText("Best frames calc right mono...");
                InProgress.IsActive = true;
                await RightMonoCalibrationHead.BestFramesCalcAndMonoCalibration(
                                                              calibProject,
                                                              false/*trueLeftFalseRight*/,
                                                              MovementMaxThreshold, 
                                                              BlurMaxThreshold,
                                                              MonoCornersMinThreshold,
                                                              useMonoCacheValues,
                                                              writePngFiles);
                InProgress.IsActive = false;
            }
            if (doStereo)
            {
                await DisplayStatusText("Best frames calc stereo...");
                InProgress.IsActive = true;

                if (calibProject.Data.LeftMonoCalibrationCameraDataArray.Any(item => item != null) &&
                    calibProject.Data.RightMonoCalibrationCameraDataArray.Any(item => item != null))
                {
                    await StereoCalibrationHead.BestFramesCalcAndStereoCalibration(
                                                                calibProject,
                                                                MovementMaxThreshold,
                                                                BlurMaxThreshold,
                                                                StereoCornersMinThreshold,
                                                                writePngFiles);

                    // Return the best stereo calibration set
                    CalibrationParameters? calibrationParameters = ReturnBestStereoCalibrationCameraData(calibProject);
                    if (calibrationParameters is not null)
                    {
                        // Get the stereo, left mono and right mono result set
                        CalibrationStereoCameraData calibrationStereoCameraData = calibProject.Data.CalibrationStereoCameraDataArray[(int)calibrationParameters];
                        ???

                        // Populate the CalibrationData
                        CalibrationData calibrationData = new()
                        {
                            StereoCameraCalibration = calibrationStereoCameraData,
                        };
                        calibrationData.LeftCameraCalibration.ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/);
                        calibrationData.LeftCameraCalibration.ImageSize[0, 0] = (int)frameSize.Width;
                        calibrationData.LeftCameraCalibration.ImageSize[0, 1] = (int)frameSize.Height;

                        calibrationData.LeftCameraCalibration.ImageTotal = leftMonoCalibrationCameraData.ImageTotal;
                        calibrationData.LeftCameraCalibration.ImageUseable = leftMonoCalibrationCameraData.ImageUseable;
                        calibrationData.LeftCameraCalibration.Intrinsic = leftMonoCalibrationCameraData.IntrinsicMatrix;
                        calibrationData.LeftCameraCalibration.Distortion = leftMonoCalibrationCameraData.DistortionCoeffs;
                        calibrationData.LeftCameraCalibration.RMS = leftMonoCalibrationCameraData.ReprojectionRMS;

                        calibrationData.RightCameraCalibration.ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/);
                        calibrationData.RightCameraCalibration.ImageSize[0, 0] = (int)frameSize.Width;
                        calibrationData.RightCameraCalibration.ImageSize[0, 1] = (int)frameSize.Height;
                        calibrationData.RightCameraCalibration.ImageTotal = rightMonoCalibrationCameraData.ImageTotal;
                        calibrationData.RightCameraCalibration.ImageUseable = rightMonoCalibrationCameraData.ImageUseable;
                        calibrationData.RightCameraCalibration.Intrinsic = rightMonoCalibrationCameraData.IntrinsicMatrix;
                        calibrationData.RightCameraCalibration.Distortion = rightMonoCalibrationCameraData.DistortionCoeffs;
                        calibrationData.RightCameraCalibration.RMS = leftMonoCalibrationCameraData.ReprojectionRMS;


                        // Add the camera serial numbers
                        calibrationData.LeftCameraCalibration.CameraID = calibProject.Data.Media.LeftCameraID;
                        calibrationData.RightCameraCalibration.CameraID = calibProject.Data.Media.RightCameraID;

                        // Get the user to save the calibration data
                        var savePicker = new Windows.Storage.Pickers.FileSavePicker();
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

                        savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                        savePicker.FileTypeChoices.Add("Calibration Data", new List<string>() { ".json" });
                        savePicker.SuggestedFileName = "CalibrationData";

                        Windows.Storage.StorageFile file = await savePicker.PickSaveFileAsync();
                        if (file is not null)
                        {
                            string fileSpec = file.Path;
                            calibrationData.SaveToFile(fileSpec);
                        }
                    }
                }

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
            if (doLeftMono || doRightMono)
            {
                await calibProject.Save();                   
            }

            await DisplayStatusText("");
            InProgress.IsActive = false;
            saveStatus = true;  // Allowed to press the save button again
            SetUIControls();
        }


        /// <summary>
        /// Return the strongest mono calibration camera data from the left and right mono 
        /// calibration camera data arrays.  The returned index is the same index for both the
        /// left and right array. Stereo calibration expects to use mono calibration data that
        /// was cresated using the same calibration parameters.
        /// </summary>
        /// <param name="leftMonoCalibrationCameraData"></param>
        /// <param name="rightMonoCalibrationCameraData"></param>
        /// <returns></returns>
        private static CalibrationParameters? ReturnBestMonoCalibrationCameraData(
                                    MonoCalibrationCameraData?[] leftMonoCalibrationCameraData,
                                    MonoCalibrationCameraData?[] rightMonoCalibrationCameraData)
        {
            double bestScore = double.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < leftMonoCalibrationCameraData.Length; i++)
            {
                var left = leftMonoCalibrationCameraData[i];
                var right = rightMonoCalibrationCameraData[i];

                if (left == null || right == null)
                    continue;

                // Combine left and right metrics
                double rmsAvg = (left.ReprojectionRMS + right.ReprojectionRMS) / 2.0;
                double maxErrAvg = (left.MaxError + right.MaxError) / 2.0;

                // Define weighted score (you can tune weights as needed)
                double score = rmsAvg + 0.2 * maxErrAvg;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex == -1)
                return null;

            return (CalibrationParameters)bestIndex;
        }


        /// <summary>
        /// Returns the stereo calibration result set with the best RMS
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns></returns>
        private static CalibrationParameters? ReturnBestStereoCalibrationCameraData(CalibProject calibProject)
        {
            double bestScore = double.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < calibProject.Data.CalibrationStereoCameraDataArray.Length; i++)
            {
                var stereoResult = calibProject.Data.CalibrationStereoCameraDataArray[i];
                

                if (stereoResult is null)
                    continue;

                // Define weighted score (you can tune weights as needed)
                double score = stereoResult.RMS + /*???0.2 * stereoResult.MaxError*/;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex == -1)
                return null;

            return (CalibrationParameters?)bestIndex;
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


        /// <summary>
        /// Update the movement threshold text when the slider value changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MovementSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            MovementMaxThresholdText.Text = $"{MovementMaxThreshold:F1}"; // Update the text to show the current value
        }


        /// <summary>
        /// Update the blur threshold text when the slider value changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BlurSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            BlurMaxThresholdText.Text = $"{BlurMaxThreshold:F1}"; // Update the text to show the current value
        }

        private void MonoCornersSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            MonoCornersMinThresholdText.Text = $"{MonoCornersMinThreshold}";
        }

        private void StereoCornersSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            StereoCornersMinThresholdText.Text = $"{StereoCornersMinThreshold}";
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

            // Set the sliders
            SetMovementAndBlurSliderMax();

        }


        private void SetMovementAndBlurSliderMax()
        {
            
            if (findStatus == true)
            {
                double minMovement;
                double maxMovement;
                double maxBlur;
                double minBlur;

                {
                    double minMovementMonoLeft = LeftMonoCalibrationHead.GetMinMovement(true/*trueNormalFalseBestFrame*/);
                    double minMovementMonoRight = LeftMonoCalibrationHead.GetMinMovement(true/*trueNormalFalseBestFrame*/);
                    double minMovementStereo = StereoCalibrationHead.GetMinMovement(true/*trueNormalFalseBestFrame*/);
                    minMovement = Math.MinMagnitude(minMovementStereo, Math.MinMagnitude(minMovementMonoLeft, minMovementMonoRight));
                }

                {
                    double maxMovementMonoLeft = LeftMonoCalibrationHead.GetMaxMovement(true/*trueNormalFalseBestFrame*/);
                    double maxMovementMonoRight = LeftMonoCalibrationHead.GetMaxMovement(true/*trueNormalFalseBestFrame*/);
                    double maxMovementStereo = StereoCalibrationHead.GetMaxMovement(true/*trueNormalFalseBestFrame*/);
                    maxMovement = Math.MaxMagnitude(maxMovementStereo, Math.MaxMagnitude(maxMovementMonoLeft, maxMovementMonoRight));
                }

                {
                    double minBlurMonoLeft = LeftMonoCalibrationHead.GetMinBlur(true/*trueNormalFalseBestFrame*/);
                    double minBlurMonoRight = LeftMonoCalibrationHead.GetMinBlur(true/*trueNormalFalseBestFrame*/);
                    double minBlurStereo = StereoCalibrationHead.GetMinBlur(true/*trueNormalFalseBestFrame*/);
                    minBlur = Math.MinMagnitude(minBlurStereo, Math.MinMagnitude(minBlurMonoLeft, minBlurMonoRight));
                }

                {
                    double maxBlurMonoLeft = LeftMonoCalibrationHead.GetMaxBlur(true/*trueNormalFalseBestFrame*/);
                    double maxBlurMonoRight = LeftMonoCalibrationHead.GetMaxBlur(true/*trueNormalFalseBestFrame*/);
                    double maxBlurStereo = StereoCalibrationHead.GetMaxBlur(true/*trueNormalFalseBestFrame*/);
                    maxBlur = Math.MaxMagnitude(maxBlurStereo, Math.MaxMagnitude(maxBlurMonoLeft, maxBlurMonoRight));
                }

                // Load the movement/blur max values into the slider for the whole set of frames
                Sliders.Visibility = Visibility.Visible;

                // Setup Movement filter max/min values
                MovementMaxThresholdSlider.Minimum = minMovement;
                MovementSliderMin.Text = $"{minMovement:F1}";
                MovementMaxThresholdSlider.Maximum = maxMovement;
                MovementSliderMax.Text = $"{maxMovement:F1}";

                // Setup Movement filter max/min values
                BlurMaxThresholdSlider.Maximum = maxBlur;
                BlurSliderMin.Text = $"{minBlur:F1}";
                BlurMaxThresholdSlider.Maximum = maxBlur;
                BlurSliderMax.Text = $"{maxBlur:F1}";
            }
            else if (saveStatus == true)
            {
                double maxMovement;
                double maxBlur;

                {
                    double maxMovementMonoLeft = LeftMonoCalibrationHead.GetMaxMovement(false/*trueNormalFalseBestFrame*/);
                    double maxMovementMonoRight = LeftMonoCalibrationHead.GetMaxMovement(false/*trueNormalFalseBestFrame*/);
                    double maxMovementStereo = StereoCalibrationHead.GetMaxMovement(false/*trueNormalFalseBestFrame*/);
                    maxMovement = Math.MaxMagnitude(maxMovementStereo, Math.MaxMagnitude(maxMovementMonoLeft, maxMovementMonoRight));
                }

                {
                    double maxBlurMonoLeft = LeftMonoCalibrationHead.GetMaxBlur(false/*trueNormalFalseBestFrame*/);
                    double maxBlurMonoRight = LeftMonoCalibrationHead.GetMaxBlur(false/*trueNormalFalseBestFrame*/);
                    double maxBlurStereo = StereoCalibrationHead.GetMaxBlur(false/*trueNormalFalseBestFrame*/);
                    maxBlur = Math.MaxMagnitude(maxBlurStereo, Math.MaxMagnitude(maxBlurMonoLeft, maxBlurMonoRight));
                }

                // Load the movement/blur max values into the slider for the best frames
                Sliders.Visibility = Visibility.Visible;
                MovementMaxThresholdSlider.Maximum = maxMovement;
                BlurMaxThresholdSlider.Maximum = maxBlur;
            }
            else
            {
                // Hide the sliders
                Sliders.Visibility = Visibility.Collapsed;
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
                        await Save(calibProject); // Automatically save results if find is done

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

