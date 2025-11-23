using Microsoft.UI;                          
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.Calibration;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WinUIEx;


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

   
    public sealed partial class MainWindow : WindowEx
    {
        private CalibProject? calibProject = null;

        // Add these fields to MainWindow class
        private DispatcherQueueTimer? _stereoLockCheckTimer;
        private DispatcherQueueTimer? _findCheckTimer;
        private DateTime? _findStartTime;

        private bool mediaFromCommandLine = false;
        private bool? findStatus = null;  // false started, true done
        private bool? saveStatus = null;  // None - Can't save, false - In Save, true - can save

        public double MovementMaxThreshold { get; set; } = 400.0;   // Set so high that these values are effectively ignored
        public double BlurMaxThreshold { get; set; } = 50;          // Set so high that these values are effectively ignored
        public int MonoCornersMinThreshold { get; set; } = CalibrationStereoFrameSet.MONO_CORNER_COUNT_THESHOLD;
        public int StereoCornersMinThreshold { get; set; } = CalibrationStereoFrameSet.STEREO_CORNER_COUNT_THESHOLD;

        // Help menu documents
        private readonly HelpDocuments helpDocuments = new();


        public MainWindow()
        {
            // Restore the saved window state
            PersistenceId = "MainWindow";
            MinHeight = 600;
            MinWidth = 800;

            InitializeComponent();
            
            // This is used to get/adjust the theme is necessary
            ThemeHelper.Initialize();

            // Set theme
            SetTheme(SettingsManagerLocal.ApplicationTheme);

            // Add listener for theme changes
            var rootElement = (FrameworkElement)Content;
            rootElement.Loaded += MainWindow_Loaded;
            rootElement.ActualThemeChanged += OnActualThemeChanged;

            // Allows the menu bar to extend into the title bar
            // Assumes "this" is a XAML Window. In projects that don't use 
            // WinUI 3 1.3 or later, use interop APIs to get the AppWindow.           
            AppTitleBar.Loaded += AppTitleBar_Loaded;
            AppTitleBar.SizeChanged += AppTitleBar_SizeChanged;
            ExtendsContentIntoTitleBar = true;


            // Create Charuco Board Definition
            //calibProject.Data.CharucoBoardDefinition.Setup(new Dictionary(PredefinedDictionaryName.Dict5X5_100),
            //                                            14/*SquareX*/, 9/*SquareY*/,
            //                                            39.92f / 1000.0f/*SquareLength*/,
            //                                            30.0f / 1000.0f/*MarkerLength*/);

            //// Pass the calibration board settings to the  calibration heads
            //StereoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.CharucoBoardDefinition);

            //LeftMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.CharucoBoardDefinition);

            //RightMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.CharucoBoardDefinition);

            // Get the Save Best Frame checkbox if /SaveBestFrames command line argument is set
            //if (AppLaunchArgs.SaveBestFrames is not null)
            //{
            //    SaveBestFrames.IsChecked = (bool)AppLaunchArgs.SaveBestFrames;
            //}                       

            // Set the sliders
            //SetMovementAndBlurSliderMax();


            // Check for command line            
            //bool isStereoLeft = false;
            //bool isStereoRight = false;
            //bool isMonoLeft = false;
            //bool isMonoRight = false;

            //if (!string.IsNullOrEmpty(AppLaunchArgs.StereoLeft) && File.Exists(AppLaunchArgs.StereoLeft))
            //{
            //    calibProject.Data.Media.LeftStereoMP4Path = AppLaunchArgs.StereoLeft;
            //    isStereoLeft = true;
            //}
            //if (!string.IsNullOrEmpty(AppLaunchArgs.StereoRight) && File.Exists(AppLaunchArgs.StereoRight))
            //{
            //    calibProject.Data.Media.RightStereoMP4Path = AppLaunchArgs.StereoRight;
            //    isStereoRight = true;
            //}
            //if (!string.IsNullOrEmpty(AppLaunchArgs.MonoLeft) && File.Exists(AppLaunchArgs.MonoLeft))
            //{
            //    calibProject.Data.Media.LeftMonoMP4Path = AppLaunchArgs.MonoLeft;
            //    isMonoLeft = true;
            //}
            //if (!string.IsNullOrEmpty(AppLaunchArgs.MonoRight) && File.Exists(AppLaunchArgs.MonoRight))
            //{
            //    calibProject.Data.Media.RightMonoMP4Path = AppLaunchArgs.MonoRight;
            //    isMonoRight = true;
            //}

            //if (isStereoLeft && isStereoRight && isMonoLeft && isMonoRight)
            //{
            //    calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet;
            //    mediaFromCommandLine = true;
            //}
            //else if (isStereoLeft && isStereoRight)
            //{
            //    calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet;
            //    mediaFromCommandLine = true;
            //}
            //else if (isMonoLeft && isMonoRight)
            //{
            //    calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet;
            //    mediaFromCommandLine = true;
            //}
            //else if (isMonoLeft)
            //{
            //    calibProject.Data.Media.StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet;
            //    mediaFromCommandLine = true;
            //}


            //if (mediaFromCommandLine)
            //    _ = OpenMedia(calibProject, true/*forceUsdCacheIfAvalable*/, AppLaunchArgs.RunWithoutPrompts/*noPrompts*/);


            // Add the help documents to the Help menu
            // Fix for CS1503: Argument 1: cannot convert from 'System.Collections.Generic.IList<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase>' to 'Microsoft.UI.Xaml.Controls.ItemCollection'

            // The issue arises because `MenuHelp.Items` is of type `IList<MenuFlyoutItemBase>`,
            // but the `Initialize` method of `HelpDocuments` expects an `ItemCollection`.
            // To fix this, we need to pass the correct type to the `Initialize` method.

            // Setup any documents on the help menu
            _ = helpDocuments.Initialize(MenuHelp.Items, // Pass the MenuFlyoutSubItem directly instead of its Items property
                                         HelpDocumentsPDFSection,
                                         HelpDocumentsVideosSection,
                                         HelpDocumentsDOCSection,
                                         HelpDocumentsXLSSection);
        }


        /// <summary>
        /// Set the theme of the application
        /// </summary>
        /// <param name="theme">Dark or Light</param>
        public void SetTheme(ElementTheme theme)
        {

            var rootElement = (FrameworkElement)(Content);

            if (theme == ElementTheme.Dark)
            {
                // Set the RequestedTheme of the root element to Dark
                rootElement.RequestedTheme = ElementTheme.Dark;

                // Use a dark theme icon
                var bitmapImage = new BitmapImage(new Uri($"ms-appx:///Assets/SurveyorCalibration-Dark.png"));
                TitleBarIcon.Source = bitmapImage;

                TitleBarHelper.SetCaptionButtonColors(this, Colors.White);
            }
            else if (theme == ElementTheme.Light)
            {
                // Set the RequestedTheme of the root element to Light
                rootElement.RequestedTheme = ElementTheme.Light;
                rootElement.RequestedTheme = ElementTheme.Light;

                // Use a light theme icon
                var bitmapImage = new BitmapImage(new Uri($"ms-appx:///Assets/SurveyorCalibration-Light.png"));
                TitleBarIcon.Source = bitmapImage;

                TitleBarHelper.SetCaptionButtonColors(this, Colors.Black);
            }
            else
            {
                // Use the default system theme
                rootElement.RequestedTheme = ElementTheme.Default;

                // Get the background colour used by that theme
                var color = TitleBarHelper.ApplySystemThemeToCaptionButtons(this) == Colors.White ? "Dark" : "Light";

                // Based on the background colour select a suitable application icon 
                if (color == "Dark")
                    TitleBarIcon.Source = new BitmapImage(new Uri($"ms-appx:///Assets/SurveyorCalibration-Dark.png"));
                else
                    TitleBarIcon.Source = new BitmapImage(new Uri($"ms-appx:///Assets/SurveyorCalibration-Light.png"));
            }

            // If the theme has changed, announce the change to the user
            UIHelper.AnnounceActionForAccessibility(rootElement, "Theme changed", "ThemeChangedNotificationActivityId");
        }



        /// 
        /// EVENTS
        /// 


        /// <summary>
        /// Called once the MainWindow is fully loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (SettingsManagerLocal.TeachingTipsEnabled &&
                !SettingsManagerLocal.HasTeachingTipBeenShown("MenuFile"))
            {
                MenuFileTeachingTip.IsOpen = true;
            }
        }


        /// <summary>
        /// Event raised when the AppTitleBar is loaded, used to set the interactive regions in 
        /// the title bar area which allowed the menubar (which is on the title bar) to operate
        /// properly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            if (ExtendsContentIntoTitleBar == true)
            {
                // Set the initial interactive regions.
                SetRegionsForCustomTitleBar();
            }
        }


        /// <summary>
        /// Event raised when the AppTitleBar size if changed, used to set the interactive regions in 
        /// the title bar area which allowed the menubar (which is on the title bar) to operate
        /// properly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ExtendsContentIntoTitleBar == true)
            {
                // Update interactive regions if the size of the window changes.
                SetRegionsForCustomTitleBar();
            }
        }


        /// <summary>
        /// Event raised when the theme is changed in Windows
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            // Handle the theme change
            var newTheme = sender.ActualTheme;
            Debug.WriteLine($"Theme changed to {newTheme}");

            // Optionally, apply additional changes
            SetTheme(newTheme);
            SettingsManagerLocal.ApplicationTheme = ElementTheme.Default;
        }


        private async Task<StorageFile?> PickCalibFileToSaveAsync(Window window)
        {
            var savePicker = new FileSavePicker();

            // Initialize with the window handle
            var hWnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(savePicker, hWnd);

            // Set file type choices and default extension
            savePicker.FileTypeChoices.Add("Calibration Project", [".calib"]);
            savePicker.DefaultFileExtension = ".calib";

            // Optional: set suggested file name
            savePicker.SuggestedFileName = "my_calibration_project";

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
            openPicker.FileTypeFilter.Add(".calproj");

            // Let user pick a single file
            var file = await openPicker.PickSingleFileAsync();

            if (file != null && calibProject is not null)
            {
                // Load the project               
                if (await calibProject.ProjectLoad(file.Path) == 0)
                {

                    // Call OpenMedia
                    await OpenMedia(calibProject, false, false);
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Load Failed",
                        Content = "Failed to load the selected calibration project.",
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
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        if (StereoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path) &&
                            LeftMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftMonoMP4Path, string.Empty) &&
                            RightMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.RightMonoMP4Path, string.Empty))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        if (StereoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        if (LeftMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.LeftMonoMP4Path, string.Empty) &&
                            RightMonoCalibrationHead.CachedResultsFileExists(calibProject.Data.Media.RightMonoMP4Path, string.Empty))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
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
                            case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
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

                            case StereoMonoMediaSetMode.StereoOnlyMediaSet:
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

                            case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
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

                            case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
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
                                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
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
                                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                                    if (stereoFramesLoaded == 0 || leftMonoFramesLoaded == 0 || rightMonoFramesLoaded == 0)
                                    {
                                        contentText = $"The cached results not loaded of incomplete.\n\n" +
                                            $"   Stereo Frames Loaded: {stereoFramesLoaded}\n" +
                                            $"   Left Mono Frames Loaded: {leftMonoFramesLoaded}\n" +
                                            $"   Right Mono Frames Loaded: {rightMonoFramesLoaded}\n";
                                        warn = true;
                                    }
                                    break;

                                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                                    if (stereoFramesLoaded == 0)
                                    {
                                        contentText = $"The cached results not loaded of incomplete.\n\n" +
                                            $"   Stereo Frames Loaded: {stereoFramesLoaded}\n";
                                        warn = true;
                                    }
                                    break;

                                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                                    if (leftMonoFramesLoaded == 0 || rightMonoFramesLoaded == 0)
                                    {
                                        contentText = $"The cached results not loaded of incomplete.\n\n" +
                                            $"   Left Mono Frames Loaded: {leftMonoFramesLoaded}\n" +
                                            $"   Right Mono Frames Loaded: {rightMonoFramesLoaded}\n";
                                        warn = true;
                                    }
                                    break;

                                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
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
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        StereoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                        LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                        RightMonoCalibrationHead.OpenMedia(calibProject.Data.Media.RightMonoMP4Path, string.Empty);
                        break;

                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        StereoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path);
                        break;

                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                        RightMonoCalibrationHead.OpenMedia(calibProject.Data.Media.RightMonoMP4Path, string.Empty);
                        break;

                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        LeftMonoCalibrationHead.OpenMedia(calibProject.Data.Media.LeftMonoMP4Path, string.Empty);
                        break;
                }

                // Ask user to sync the stereo videos
                if (StereoCalibrationHead.IsStereoLocked() == false)
                {
                    switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                    {
                        case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        case StereoMonoMediaSetMode.StereoOnlyMediaSet:

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
                Debug.WriteLine($"Error showing CalibInfoAndMediaContentDialog: {ex.Message}");
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

            if (calibProject is null)
                return;

            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
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
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()!)
                    {
                        // Find the calibration frame in the stereo videos
                        StereoCalibrationHead.FindCalibrationFrame();
                        started = true;
                    }
                    break;
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (LeftMonoCalibrationHead.IsOpen() &&
                        RightMonoCalibrationHead.IsOpen())
                    {
                        // Find the calibration frame in the mono videos
                        LeftMonoCalibrationHead.FindCalibrationFrame();
                        RightMonoCalibrationHead.FindCalibrationFrame();
                        started = true;
                    }
                    break;
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
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
            if (calibProject is not null)
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

                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
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
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()!)
                    {
                        doStereo = true;
                    }
                    break;
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (LeftMonoCalibrationHead.IsOpen() &&
                        RightMonoCalibrationHead.IsOpen())
                    {
                        doLeftMono = true;
                        doRightMono = true;
                    }
                    break;
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
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
                if (calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray.Any(item => item != null) &&
                    calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray.Any(item => item != null))
                {
                    // Get local folder path
                    StorageFolder localFolder = ApplicationData.Current.LocalFolder;

                    var dialog = new ContentDialog
                    {
                        Title = "Mono Calibration",
                        Content = $"There is an existing mono calibration set. If you wish to reuse (quick) press 'Yes' else to recalculate (slow) press 'No'?",
                        PrimaryButtonText = "Yes",
                        CloseButtonText = "No",
                        XamlRoot = this.Content.XamlRoot // Set the XamlRoot for proper display
                    };
                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        // Use the cached mono calibration results
                        useMonoCacheValues = true;
                    }
                    else 
                    {
                        // Remove cached mono calibration results
                        Array.Fill(calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray, null);
                        Array.Fill(calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray, null);
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

                if (calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray.Any(item => item != null) &&
                    calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray.Any(item => item != null))
                {
                    await StereoCalibrationHead.BestFramesCalcAndStereoCalibration(
                                                                calibProject,
                                                                MovementMaxThreshold,
                                                                BlurMaxThreshold,
                                                                StereoCornersMinThreshold,
                                                                writePngFiles);

                    // Return the best stereo calibration set
                    CalibrationParameters? calibrationParameters = calibProject.ReturnBestStereoCalibrationCameraData();
                    if (calibrationParameters is not null)
                    {
                        // Get the stereo, left mono and right mono result set
                        CalibrationStereoCameraData calibrationStereoCameraData = calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray[(int)calibrationParameters]!;

                        MonoCalibrationCameraData? leftMonoCalibrationCameraData = calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[(int)calibrationParameters];
                        MonoCalibrationCameraData? rightMonoCalibrationCameraData = calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray[(int)calibrationParameters];

                        if (leftMonoCalibrationCameraData is not null && rightMonoCalibrationCameraData is not null)
                        {
                            // Populate the CalibrationData
                            CalibrationData calibrationData = new()
                            {
                                StereoCameraCalibration = calibrationStereoCameraData,
                            };
                            calibrationData.LeftCameraCalibration.ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/);
                            calibrationData.LeftCameraCalibration.ImageSize[0, 0] = (int)calibProject.Data.Media.FrameSize.Width;
                            calibrationData.LeftCameraCalibration.ImageSize[0, 1] = (int)calibProject.Data.Media.FrameSize.Height;

                            calibrationData.LeftCameraCalibration.ImageTotal = leftMonoCalibrationCameraData.ImageTotal;
                            calibrationData.LeftCameraCalibration.ImageUseable = leftMonoCalibrationCameraData.ImageUseable;
                            calibrationData.LeftCameraCalibration.Intrinsic = leftMonoCalibrationCameraData.IntrinsicMatrix;
                            calibrationData.LeftCameraCalibration.Distortion = leftMonoCalibrationCameraData.DistortionCoeffs;
                            calibrationData.LeftCameraCalibration.RMS = leftMonoCalibrationCameraData.ReprojectionRMS;

                            calibrationData.RightCameraCalibration.ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/);
                            calibrationData.RightCameraCalibration.ImageSize[0, 0] = (int)calibProject.Data.Media.FrameSize.Width;
                            calibrationData.RightCameraCalibration.ImageSize[0, 1] = (int)calibProject.Data.Media.FrameSize.Height;
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
                            savePicker.FileTypeChoices.Add("Calibration Data", [".calib"]);
                            savePicker.SuggestedFileName = "CalibrationData";

                            Windows.Storage.StorageFile file = await savePicker.PickSaveFileAsync();
                            if (file is not null)
                            {
                                string fileSpec = file.Path;
                                calibrationData.SaveToFile(fileSpec);
                            }
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
                calibProject.ProjectSave();                   
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


        ///// <summary>
        ///// Returns the stereo calibration result set with the best RMS
        ///// </summary>
        ///// <param name="calibProject"></param>
        ///// <returns></returns>
        //private static CalibrationParameters? ReturnBestStereoCalibrationCameraData(CalibProject calibProject)
        //{
        //    double bestScore = double.MaxValue;
        //    int bestIndex = -1;

        //    for (int i = 0; i < calibProject.Data.CalibrationStereoCameraDataArray.Length; i++)
        //    {
        //        var stereoResult = calibProject.Data.CalibrationStereoCameraDataArray[i];
                

        //        if (stereoResult is null)
        //            continue;

        //        // Define weighted score (you can tune weights as needed)
        //        double score = stereoResult.RMS + /*???0.2 * stereoResult.MaxError*/;

        //        if (score < bestScore)
        //        {
        //            bestScore = score;
        //            bestIndex = i;
        //        }
        //    }

        //    if (bestIndex == -1)
        //        return null;

        //    return (CalibrationParameters?)bestIndex;
        //}


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


        /// <summary>
        /// Create a new calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileProjectNew_Click(object sender, RoutedEventArgs e)
        {
            // Load the Info and Media user control to setup the project
            CalibrationMediaUserControl.SetupForContentDialog(CalibrationMediaContentDialog);

            // ** Important notes **
            // The UserControl CalibrationMediaContentDialog is displayed within a ContentDialog for 
            // the purpose of setting up a new project (also using from a SettingsCard)
            // I stuggled to get the ContentDialog to show width necessary to fully display
            // the UserControl.  The solution was to:
            // Set <x:Double x:Key="ContentDialogMaxWidth">1200</x:Double> in the <ResourceDictionary>
            // to setup the ContentDialog in XAML in MainWindow and place it in Grid.Row=2.
            // This took a lot of trail and error. It seems to effect the title bar is left in
            // default row zero.
            ContentDialogResult result = await CalibrationMediaContentDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                calibProject = new();

                CalibrationMediaUserControl.SaveForContentDialog(calibProject);

                // Save the calib project file
                var file = await PickCalibFileToSaveAsync(this); // 'this' refers to your Window instance
                if (file != null)
                {
                    try
                    {
                        // Save the calib project data to the file
                        await calibProject.ProjectSaveAs(file.Path);
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

                // Open the mdeia
                await OpenMedia(calibProject, false/*forceUsdCacheIfAvalable*/, false/*noPrompts*/);
            }
        }


        /// <summary>
        /// Open a calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileProjectOpen_Click(object sender, RoutedEventArgs e)
        {

        }


        /// <summary>
        /// Save the open calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileProjectSave_Click(object sender, RoutedEventArgs e)
        {

        }


        /// <summary>
        /// Save the open calibration under a new file name
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileProjectSaveAs_Click(object sender, RoutedEventArgs e)
        {

        }


        /// <summary>
        /// Close the open calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileProjectClose_Click(object sender, RoutedEventArgs e)
        {

        }

        private void FileSelectMedia_Click(object sender, RoutedEventArgs e)
        {

        }

        /// <summary>
        /// Handles the click event for the "Run Calibration" menu item.
        /// </summary>
        /// <param name="sender">The source of the event, typically the menu item that was clicked.</param>
        /// <param name="e">The event data associated with the click event.</param>
        /// <returns></returns>
        private async void FileRunCalibration_Click(object sender, RoutedEventArgs e)
        {
            await ShowSetupRunCalibration();
        }


        /// <summary>
        /// Display the settings window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileSettings_Click(object sender, RoutedEventArgs e)
        {
            await ShowSettingsWindow();
        }


        /// <summary>
        /// Exit the application 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileExit_Click(object sender, RoutedEventArgs e)
        {

        }

        /// <summary>
        /// Fire up email client
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void HelpContactSupport_Click(object sender, RoutedEventArgs e)
        {
            // Get app title from resources (fallback to window title)
            string appTitle = Application.Current.Resources.TryGetValue("AppTitleName", out var titleObj)
                ? titleObj as string ?? TitleBarTextBlock.Text
                : TitleBarTextBlock.Text;

            // Build version string
            var v = Package.Current.Id.Version;
            string version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";

            string subject = Uri.EscapeDataString($"Support regarding {appTitle} Version {version}");
            string body = Uri.EscapeDataString("Please write your support email here.");

            var mailto = new Uri($"mailto:toby.solo@outlook.com?subject={subject}&body={body}");
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(mailto);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HelpContactSupport_Click: {ex.Message}");
                var dialog = new ContentDialog
                {
                    Title = "Email",
                    Content = "Unable to open the default email client.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        /// <summary>
        /// Keyboard accelerator to testing code
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void HelpAbout_Click(object sender, RoutedEventArgs e)
        {
            // Open the settings windows 'About' section
            await ShowSettingsWindow("About");
        }


        /// <summary>
        /// Handles the action button click event for the MenuFileTeachingTip.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MenuFileTeachingTip_ActionButtonClick(TeachingTip sender, object args)
        {
            MenuFileTeachingTip.IsOpen = false;
            SettingsManagerLocal.SetTeachingTipShown("MenuFile");
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Used to set the interactive regions in the title bar area which allowed the menubar
        /// to operate properly
        /// </summary>
        private void SetRegionsForCustomTitleBar()
        {
            // Specify the interactive regions of the title bar.

            double scaleAdjustment = AppTitleBar.XamlRoot.RasterizationScale;

            RightPaddingColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / scaleAdjustment);
            LeftPaddingColumn.Width = new GridLength(AppWindow.TitleBar.LeftInset / scaleAdjustment);

            // Area for the menu bar
            GeneralTransform transformMenuBar = AppMenuBar.TransformToVisual(null);
            Rect boundsMenuBar = transformMenuBar.TransformBounds(new Rect(0, 0,
                                                             AppMenuBar.ActualWidth,
                                                             AppMenuBar.ActualHeight));
            Windows.Graphics.RectInt32 MenuBarRect = GetRect(boundsMenuBar, scaleAdjustment);

            // Area for the search box
#if IF_YOU_NEED_A_SEARCHBOX
            GeneralTransform transformTitleBar = TitleBarSearchBox.TransformToVisual(null);
            Rect boundstransformTitleBar = transformTitleBar.TransformBounds(new Rect(0, 0,
                                                             TitleBarSearchBox.ActualWidth,
                                                             TitleBarSearchBox.ActualHeight));
            Windows.Graphics.RectInt32 SearchBoxRect = GetRect(boundstransformTitleBar, scaleAdjustment);
#endif // IF_YOU_NEED_A_SEARCHBOX

#if IF_YOU_NEED_A_LOGIN_INDICTOR
            transformPersonPic = PersonPic.TransformToVisual(null);
            bounds = transformPersonPic.TransformBounds(new Rect(0, 0,
                                                        PersonPic.ActualWidth,
                                                        PersonPic.ActualHeight));
            Windows.Graphics.RectInt32 PersonPicRect = GetRect(bounds, scaleAdjustment);
#endif // IF_YOU_NEED_A_LOGIN_INDICTOR

            // Area of the lock/unlock indicator
            GeneralTransform transformLockUnLockIndicator = LockUnLockIndicator.TransformToVisual(null);
            Rect boundsLockUnLockIndicator = transformLockUnLockIndicator.TransformBounds(new Rect(0, 0,
                                                                                 LockUnLockIndicator.ActualWidth,
                                                                                 LockUnLockIndicator.ActualHeight));
            Windows.Graphics.RectInt32 LockUnLockIndicatorRect = GetRect(boundsLockUnLockIndicator, scaleAdjustment);

            // Area of the Calibrated indicator
            //??? Delele
            //GeneralTransform transformCalibratedIndicator = CalibratedIndicator.TransformToVisual(null);
            //Rect boundsCalibratedIndicator = transformCalibratedIndicator.TransformBounds(new Rect(0, 0,
            //                                                                     CalibratedIndicator.ActualWidth,
            //                                                                     CalibratedIndicator.ActualHeight));
            //Windows.Graphics.RectInt32 CalibratedIndicatorRect = GetRect(boundsCalibratedIndicator, scaleAdjustment);


            // Create list of regions that should not be draggable
            var rectArray = new Windows.Graphics.RectInt32[] { MenuBarRect/*, SearchBoxRect*//*, PersonPicRect*/, LockUnLockIndicatorRect/*, CalibratedIndicatorRect*/ };

            InputNonClientPointerSource nonClientInputSrc =
                InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
            nonClientInputSrc.SetRegionRects(NonClientRegionKind.Passthrough, rectArray);
        }
        private static Windows.Graphics.RectInt32 GetRect(Rect bounds, double scale)
        {
            return new Windows.Graphics.RectInt32(
                _X: (int)Math.Round(bounds.X * scale),
                _Y: (int)Math.Round(bounds.Y * scale),
                _Width: (int)Math.Round(bounds.Width * scale),
                _Height: (int)Math.Round(bounds.Height * scale)
            );
        }


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
            if (findStatus is null && calibProject is not null)
            {
                if ((isLocked is not null && isLocked == true) ||
                    calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoPairOnlyMediaSet ||
                    calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoSingleOnlyMediaSet)
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
                        if (calibProject is not null)
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

        /// <summary>
        /// Display the settings window
        /// </summary>
        private int settingsWindowEntryCount = 0;
        private async Task ShowSettingsWindow(string section = "")
        {
            try
            {
                int entryCount = Interlocked.Increment(ref settingsWindowEntryCount);
                // Make sure we only open the settings window once.
                // This can happen if the project and movies are loaded and the user clicks the settings a few times.
                if (entryCount == 1)
                {
                    // Initialize if necessary
                    SettingsWindow settingsWindow = new(this, calibProject, section);

                    // Get the HWND (window handle) for both windows
                    IntPtr mainWindowHandle = WindowNative.GetWindowHandle(this);
                    IntPtr settingsWindowHandle = WindowNative.GetWindowHandle(settingsWindow);

                    // Get the AppWindow instances for both windows
                    AppWindow mainAppWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(mainWindowHandle));

                    // Disable the main window by setting it inactive
                    SetWindowEnabled(mainWindowHandle, false);

                    // Activate settings window
                    settingsWindow.Activate();

                    // Important not to block the UI thread.
                    // We're still waiting for the Closed event.
                    // The Closed handler runs on the UI thread, allowing WinUIEx to persist the window position.
                    var tcs = new TaskCompletionSource();

                    void OnClosed(object sender, WindowEventArgs args)
                    {
                        settingsWindow.Closed -= OnClosed;
                        tcs.SetResult();
                    }

                    settingsWindow.Closed += OnClosed;

                    await tcs.Task;

                    // Re-enable the main window after closing settings
                    SetWindowEnabled(mainWindowHandle, true);

                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during the process
                Debug.WriteLine($"MainWindow.ShowSettingsWindow Error showing settings window: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref settingsWindowEntryCount);
            }
        }


        /// <summary>
        /// Display the SetupRunCalibration window
        /// </summary>
        private int setupRunCalibrationCount = 0;
        private async Task ShowSetupRunCalibration()
        {
            try
            {
                int entryCount = Interlocked.Increment(ref setupRunCalibrationCount);
                // Make sure we only open the settings window once.
                // This can happen if the project and movies are loaded and the user clicks the settings a few times.
                if (entryCount == 1)
                {
                    // Initialize if necessary
                    SetupRunCalibration setupRunCalibration = new(calibProject);

                    // Get the HWND (window handle) for both windows
                    IntPtr mainWindowHandle = WindowNative.GetWindowHandle(this);
                    IntPtr settingsWindowHandle = WindowNative.GetWindowHandle(setupRunCalibration);

                    // Get the AppWindow instances for both windows
                    AppWindow mainAppWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(mainWindowHandle));

                    // Disable the main window by setting it inactive
                    SetWindowEnabled(mainWindowHandle, false);

                    // Activate settings window
                    setupRunCalibration.Activate();

                    // Important not to block the UI thread.
                    // We're still waiting for the Closed event.
                    // The Closed handler runs on the UI thread, allowing WinUIEx to persist the window position.
                    var tcs = new TaskCompletionSource();

                    void OnClosed(object sender, WindowEventArgs args)
                    {
                        setupRunCalibration.Closed -= OnClosed;
                        tcs.SetResult();
                    }

                    setupRunCalibration.Closed += OnClosed;

                    await tcs.Task;

                    // Re-enable the main window after closing settings
                    SetWindowEnabled(mainWindowHandle, true);
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during the process
                Debug.WriteLine($"MainWindow.ShowSetupRunCalibration Error showing settings window: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref setupRunCalibrationCount);
            }
        }



        /// <summary>
        /// Disables or enables a window in WinUI 3 using native Win32 API.
        /// </summary>
        private static void SetWindowEnabled(IntPtr hWnd, bool enabled)
        {
            const int GWL_STYLE = -16;
            const int WS_DISABLED = 0x08000000;

            int style = GetWindowLong(hWnd, GWL_STYLE);
            if (enabled)
            {
                style &= ~WS_DISABLED; // Remove the disabled flag
            }
            else
            {
                style |= WS_DISABLED; // Add the disabled flag
            }
            SetWindowLong(hWnd, GWL_STYLE, style);
        }

        /// <summary>
        /// Native Win32 API methods
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

       
    }
}

