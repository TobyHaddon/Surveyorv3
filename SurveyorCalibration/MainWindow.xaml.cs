using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;
using WinUIEx;
using static Surveyor.Controls.UniversalCalibrationHeadUserControl;


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

    public class RunCalibrationParams
    {
        // Defaults
        public static double MovementFilterDefaultValue { get; set; } = 400.0;   // Set so high that these values are effectively ignored
        public static double BlurMaxFilterDefaultValue { get; set; } = 50;       // Set so high that these values are effectively ignored
        public static double MovementFilterMaxDefault { get; set; } = 400.0;   // Set so high that these values are effectively ignored
        public static double BlurMaxFilterMaxDefault { get; set; } = 50;       // Set so high that these values are effectively ignored


        // Limits 
        // If there is cache framesets these values get set to the max in those framesets 
        public double? MovementFilterMin { get; set; } = null;      
        public double? MovementFilterMax { get; set; } = null;
        public double? BlurFilterMin { get; set; } = null;    
        public double? BlurFilterMax { get; set; } = null;    

        // Used by the RunCalibration process
        public bool UseFrameSetCache { get; set; } = true;
        public double MovementFilterValue { get; set; } = MovementFilterDefaultValue;
        public double BlurFilterValue { get; set; } = BlurMaxFilterDefaultValue;
        public int MonoCornersFilterValue { get; set; } = CalibrationStereoFrameSet.MONO_CORNER_COUNT_THESHOLD;
        public int StereoCornersFilterValue { get; set; } = CalibrationStereoFrameSet.STEREO_CORNER_COUNT_THESHOLD;
        public bool SaveBestFrames { get; set; } = false;
    }

    public sealed partial class MainWindow : WindowEx
    {
        // Title bar title elements
        private string titlebarTitle = "";
        private string titlebarCameraSide = "";
        private string titlebarSaveStatus = "";

        private CalibProject? calibProject = null;

        // Recent projects management
        private const string RECENT_PROJECTS_KEY = "RecentProjects";
        private readonly int maxRecentProjectsDisplayed = 6;
        private const int MAX_RECENT_PROJECTS_SAVED = 20;

        // Add these fields to MainWindow class
        private DispatcherQueueTimer? _stereoLockCheckTimer;
        private DispatcherQueueTimer? _findCheckTimer;
        private DateTime? _findStartTime;

        
        private bool mediaFromCommandLine = false; // no sure to support this or not

        // Help menu documents
        private readonly HelpDocuments helpDocuments = new();


        public MainWindow()
        {
            // Restore the saved window state
            PersistenceId = "MainWindow";
            MinHeight = 600;
            MinWidth = 800;

            InitializeComponent();

            // Event first before the app window is commited to closing (i.e. can be cancelled)
            if (this.AppWindow is not null)
                this.AppWindow.Closing += AppWindow_Closing;

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

            // Wire up to the ProcessingInfoBar the external TextBlock and ProgressRing controls in the title bar
            InfoBarProcessing.WireUpElapsedTimeUIControl(ElapsedProcessingTime, TitleProgressRing);

            // Update the Recent open surveys sub menu
            UpdateRecentProjectsMenu();


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


            // Setup any documents on the help menu
            _ = helpDocuments.InitializeAsync(MenuHelp.Items, // Pass the MenuFlyoutSubItem directly instead of its Items property
                                              HelpDocumentsPDFSection,
                                              HelpDocumentsVideosSection,
                                              HelpDocumentsDOCSection,
                                              HelpDocumentsXLSSection);

            SetUIControls();
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


        /// <summary>
        /// Check if cached results file exists for the current media set
        /// </summary>
        /// <returns></returns>
        public bool CachedResultsFileExists()
        {
            bool cachedResultsAvailable = false;

            if (calibProject is not null)
            {
                // Check if cached results files are available
                CalibProject.DataClass.CacheClass cache = calibProject.Data.Cache;

                switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                {
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        if (StereoCalibrationHead.CachedResultsFileExists(cache.StereoFrameSetCacheFileSpec) &&
                            LeftMonoCalibrationHead.CachedResultsFileExists(cache.LeftMonoFrameSetCacheFileSpec) &&
                            RightMonoCalibrationHead.CachedResultsFileExists(cache.RightMonoFrameSetCacheFileSpec))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        if (StereoCalibrationHead.CachedResultsFileExists(cache.StereoFrameSetCacheFileSpec))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        if (LeftMonoCalibrationHead.CachedResultsFileExists(cache.LeftMonoFrameSetCacheFileSpec) &&
                            RightMonoCalibrationHead.CachedResultsFileExists(cache.RightMonoFrameSetCacheFileSpec))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        if (LeftMonoCalibrationHead.CachedResultsFileExists(cache.LeftMonoFrameSetCacheFileSpec))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;
                }
            }

            return cachedResultsAvailable;
        }


        /// <summary>
        /// Run the calibration as defined in the SetupRunCalibration pages
        /// This method is called once the user presses the Run Calibration button
        /// in the SetupRunCalibrationSummary page. Hence why it is public
        /// </summary>
        /// <returns></returns>
        public async Task RunCalibrationAsync(RunCalibrationParams runCalibrationParams)
        {
            int ret = 0;

            if (calibProject is not null)
            {
                // Prime the board for EMGU Api use
                if (calibProject.Data.CharucoBoardDefinition.Setup())
                {
                    // Load the calibration board type
                    StereoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.CharucoBoardDefinition);
                    LeftMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.CharucoBoardDefinition);
                    RightMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.CharucoBoardDefinition);
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Board Setup Failed",
                        Content = "Failed to setup the calibration board definition.",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }

            // Load caches if requested (quicker)
            if (runCalibrationParams.UseFrameSetCache)
            {
                if (await LoadFrameDataCachesAsync(false/*noPrompts*/) == true)
                    Debug.WriteLine("Frame Set Caches Loaded");
                else
                    // Failed - switch cache off
                    runCalibrationParams.UseFrameSetCache = false;
            }
            
            if (runCalibrationParams.UseFrameSetCache == false)
            {
                // Build the frame sets by finding calibration targets in all frames
                ret = await BuildFrameSetsAsync();
            }

            if (ret == 0)
            {
                // Find the best frames and do the calibration calculation
                await IdentifyBestFrameaAndDoCalibrationCalcAsync(runCalibrationParams);
            }

            SetUIControls();
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
        /// Window close has been requested by user, check for open Surveys
        /// </summary>
        /// <returns></returns>
        private void AppWindow_Closing(object sender, AppWindowClosingEventArgs e) => _ = AppWindowClosingAsync(e);
        private async Task AppWindowClosingAsync(AppWindowClosingEventArgs e)
        {
            Debug.WriteLine("AppWindow_Closing Entered");

            // First: check unsaved survey (may show dialog)
            bool canClose = await CheckForOpenProjectAndCloseAsync(true/*existing*/);
            if (!canClose)
            {
                e.Cancel = true;
                Debug.WriteLine("AppWindow_Closing canceled by user");
                return;
            }

            // Perform unified shutdown
            //???Left over from Surveyor
            //await ShutdownAsync();

            e.Cancel = false;
            Debug.WriteLine("AppWindow_Closing Exit");
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


        /// <summary>
        /// Create a new calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileProjectNew_Click(object sender, RoutedEventArgs e) => _ = FileProjectNewAsync();
        private async Task FileProjectNewAsync()
        {
            // Reset Title
            SetTitle("");

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
                if (await SaveAsProjectAsync() == 0)
                {
                    // Open the mdeia
                    await OpenMediaSetsAsync(calibProject, false/*forceUsdCacheIfAvalable*/, false/*noPrompts*/);
                }
            }
        }


        /// <summary>
        /// Open a calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileProjectOpen_Click(object sender, RoutedEventArgs e) => _ = FileProjectOpenClickAsync();
        private async Task FileProjectOpenClickAsync()
        {
            // First check if an existing survey is already open
            if (await CheckForOpenProjectAndCloseAsync() == true)
            {
                // Create the file picker object
                FileOpenPicker openPicker = new()
                {
                    ViewMode = PickerViewMode.Thumbnail, // Can be List or Thumbnail
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary
                };

                // Add file type filters
                openPicker.FileTypeFilter.Add(".calproj");

                // Associate the file picker with the current window
                IntPtr hWnd = WindowNative.GetWindowHandle(this);
                InitializeWithWindow.Initialize(openPicker, hWnd);

                // Show the picker to the user
                StorageFile file = await openPicker.PickSingleFileAsync();

                // If a file was picked, handle it
                if (file is not null)
                {
                    int ret = await OpenProjectAsync(file.Path);

                    if (ret == 0)
                    {
                        // Add to Recent Projects list
                        AddToRecentProjects(file.Path);
                        UpdateRecentProjectsMenu();
                    }
                    else
                    {
                        Debug.WriteLine($"FileProjectOpenClickAsync: OpenProjectAsync() failed, survey path:{file.Path}, ret = {ret}");
                    }
                }

                // Enable/Disable menu items based on the current survey state
                SetUIControls();
            }
        }



        /// <summary>
        /// Save the open calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileProjectSave_Click(object sender, RoutedEventArgs e) => _ = FileProjectSaveClickAsync();
        private async Task FileProjectSaveClickAsync()
        {
            await FileProjectSaveOrSaveAsAsync();
        }


        /// <summary>
        /// Save the open calibration under a new file name
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileProjectSaveAs_Click(object sender, RoutedEventArgs e) => _ = FileProjectSaveAsClickAsync();
        private async Task FileProjectSaveAsClickAsync()
        {
            await FileProjectSaveOrSaveAsAsync();
        }


        /// <summary>
        /// Close the open calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileProjectClose_Click(object sender, RoutedEventArgs e) => _ = FileProjectCloseClickAsync();
        private async Task FileProjectCloseClickAsync()
        {
            await CheckForOpenProjectAndCloseAsync();
        }


        /// <summary>
        /// Used to open a selected recent project file from the 'Recent Projects' sub menu
        /// Note this method is dynamically connected to the menu items created in
        /// the UpdateRecentProjectsMenu method.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileRecentProject_Click(object sender, RoutedEventArgs e) => _ = FileRecentProjectClickAsync(sender);
        private async Task FileRecentProjectClickAsync(object sender)
        {
            var menuItem = sender as MenuFlyoutItem;
            if (menuItem is not null)
            {
                if (menuItem.Tag is string filePath)
                {
                    // First check if an existing project is already open
                    if (await CheckForOpenProjectAndCloseAsync() == true)
                    {
                        // Open project in the regular way
                        int ret = await OpenProjectAsync(filePath);

                        if (ret == 0)
                        {
                            // Force to the top of the recent projects list
                            // Note this project is definately in the recent project list
                            // but may be the top item. As the new last opened project it
                            // should be top
                            AddToRecentProjects(filePath);
                            UpdateRecentProjectsMenu();
                        }
                        else if (ret != -999/*User aborted*/)
                        {
                            // Report the missing survey file
                            // Survey needs to be saved before a frame can be saved
                            var warningIcon = new SymbolIcon(Symbol.Important); // Symbol.Important represents an exclamation

                            // Create the ContentDialog instance
                            var dialog = new ContentDialog
                            {
                                Title = $"Project file missing",
                                Content = new StackPanel
                                {
                                    Orientation = Orientation.Horizontal,
                                    Spacing = 10,
                                    Children =
                                    {
                                        warningIcon, // Add the exclamation icon to the dialog content
                                        new TextBlock
                                        {
                                            Text = $"{filePath}",
                                            TextWrapping = TextWrapping.Wrap,
                                            MaxWidth = 400 // Adjust based on your app's layout
                                        }
                                    }
                                },

                                CloseButtonText = "Cancel",
                                DefaultButton = ContentDialogButton.Close, // Set "Cancel" as the default button

                                // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                                XamlRoot = this.Content.XamlRoot
                            };

                            // Show the dialog and await the result
                            await dialog.ShowAsync();

                            // Recent survey file is missing, remove from the recent file list
                            RemoveToRecentSurveys(filePath);
                        }
                    }
                }

                // Enable/Disable menu items based on the current survey state
                SetUIControls();
            }
        }


        /// <summary>
        /// User requested to either lock or unlock the media players
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileLockUnlockMediaPlayers_Click(object sender, RoutedEventArgs e) => _ = FileLockUnlockMediaPlayersAsync();
        private async Task FileLockUnlockMediaPlayersAsync()
        {
            if (calibProject is not null)
            {
                if (calibProject.Data.Sync.IsSynchronized)
                {
                    await LockUnlockMediaPlayersAsync(false/*lockTrueUnLockFalse*/);
                }
                else
                {
                    await LockUnlockMediaPlayersAsync(true/*lockTrueUnLockFalse*/);
                }
            }

            SetUIControls();
        }


        /// <summary>
        /// Handles the click event for the "Run Calibration" menu item.
        /// </summary>
        /// <param name="sender">The source of the event, typically the menu item that was clicked.</param>
        /// <param name="e">The event data associated with the click event.</param>
        /// <returns></returns>
        private void FileRunCalibration_Click(object sender, RoutedEventArgs e) => _ = FileRunCalibrationAsync();
        private async Task FileRunCalibrationAsync()
        {
            await ShowSetupRunCalibrationAsync();
        }


        /// <summary>
        /// Export the calibration results to a file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileExport_Click(object sender, RoutedEventArgs e)
        {
            //???TODO
        }


        /// <summary>
        /// Display the settings window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSettings_Click(object sender, RoutedEventArgs e) => _ = FileSettingsAsync();
        private async Task FileSettingsAsync()
        {
            await ShowSettingsWindowAsync();
        }


        /// <summary>
        /// Exit the application 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileExit_Click(object sender, RoutedEventArgs e)
        {
            // Closing project/media is handled in App Closing override

            SetTitle("");
            SetLockUnlockIndicator(null, null);

            Application.Current.Exit();
        }



        /// <summary>
        /// Fire up email client
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HelpContactSupport_Click(object sender, RoutedEventArgs e) => _ = HelpContactSupportAsync();
        private async Task HelpContactSupportAsync()
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
        private void HelpAbout_Click(object sender, RoutedEventArgs e) => _ = HelpAboutAsync();
        private async Task HelpAboutAsync()
        {
            // Open the settings windows 'About' section
            await ShowSettingsWindowAsync("About");
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


        /// <summary>
        /// Handles the click event for the InfoBar "Lock Media" button.
        /// </summary>
        /// <param name="sender">The source of the event, typically the button that was clicked.</param>
        /// <param name="e">The event data associated with the click event.</param>
        private void InfoBarLockMediaButton_Click(object sender, RoutedEventArgs e) => _ = InfoBarLockMediaButtonAsync();
        private async Task InfoBarLockMediaButtonAsync()
        {
            await LockUnlockMediaPlayersAsync(true/*lockTrueUnLockFalse*/);

            SetUIControls();
        }


        /// <summary>
        /// Stop any processing in progress
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InfoBarCancelProcessingButton_Click(object sender, RoutedEventArgs e)
        {
            if (StereoCalibrationHead.IsFindRunning())
                StereoCalibrationHead.FindCalibrationFrameCancel();

            if (LeftMonoCalibrationHead.IsFindRunning())
                LeftMonoCalibrationHead.FindCalibrationFrameCancel();

            if (RightMonoCalibrationHead.IsFindRunning())
                RightMonoCalibrationHead.FindCalibrationFrameCancel();

            SetUIControls();
        }




        /// <summary>
        /// Used to set the unsaved data indicated in the title bar
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Project_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CalibProject.IsDirty))
            {
                if (calibProject is not null)
                {
                    if (calibProject.IsDirty)
                        SetTitleSaveStatus("Unsaved");
                    else
                        SetTitleSaveStatus("");
                }
            }
        }

        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Open the calibration projec files
        /// </summary>
        /// <param name="surveyFileName"></param>
        /// <returns>-999 If user aborts</returns>
        private async Task<int> OpenProjectAsync(string projectFileSpec)
        {
            int ret = 0;

            if (calibProject is null)
            {
                calibProject ??= new CalibProject();
                calibProject.PropertyChanged += Project_PropertyChanged;
            }

            ret = await calibProject.ProjectLoadAsync(projectFileSpec);

            if (ret == 0 &&
                calibProject.Data is not null && calibProject.Data.Media is not null && calibProject.Data.Media.MediaPath is not null)
            {
                // Check if the left media file(s) exist
                ret = await CheckIfMediaFileExistsAsync();

                if (ret == 0)
                {
                    // Open Media Files and bind the MediaPlayers if IsSynchronized is true
                    if (await OpenMediaSetsAsync(calibProject, false/*force cache use*/, false/*no prompts*/) == true)
                    {
                        // Remember the survey folder
                        SettingsManagerLocal.ProjectFolder = System.IO.Path.GetDirectoryName(projectFileSpec);

                        // Set the title
                        SetTitle(System.IO.Path.GetFileNameWithoutExtension(projectFileSpec));

                        Debug.WriteLine($"Project Loaded");
                    }
                    else
                        // Failed to open media files
                        calibProject = null;
                }
                else
                    // Failed to open media files
                    calibProject = null;
            }
            else
            {
                Debug.WriteLine($"Failed to open survey file:{projectFileSpec}, error = {ret}");
                calibProject = null;
            }

            SetUIControls();

            return ret;
        }

        /// <summary>
        /// Check that media files list exist incase they have been renamed, moved or deleted.
        /// Allow the user to try to find the missing media file(s) or cancel loading the survey
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="mediaPath"></param>
        /// <param name="mediaFileNames"></param>
        /// <returns></returns>
        private async Task<int> CheckIfMediaFileExistsAsync()
        {
            int ret = 0;

            if (calibProject is null)
                return -1;

            CalibProject.DataClass.MediaClass media = calibProject.Data.Media;
            
            string fileName;
            for (int index = 0; index < 4; index++)
            {
                bool promptForMediaFile = false;

                fileName = index switch
                {
                    0 => media.LeftMonoMP4FileName,
                    1 => media.RightMonoMP4FileName,
                    2 => media.LeftStereoMP4FileName,
                    3 => media.RightStereoMP4FileName,
                    _ => "",
                };

                if (!string.IsNullOrEmpty(fileName))
                {
                    string fileSpec = "";

                    if (media.MediaPath is not null)
                    {
                        fileSpec = System.IO.Path.Combine(media.MediaPath, fileName);

                        // If fileSpec a relative path then use the path from the survey file spec
                        if (!System.IO.Path.IsPathRooted(fileSpec))
                        {
                            // Get the directory portion of the fully qualified projectFileSpec
                            string baseDirectory = calibProject.Data.Info.ProjectPath;

                            // Combine the base directory with the relative fileSpec
                            fileSpec = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, fileSpec));
                        }

                        if (System.IO.File.Exists(fileSpec) == false)
                            promptForMediaFile = true;
                    }
                    else
                        promptForMediaFile = true;

                    if (promptForMediaFile)
                    {
                        // Media file is missing. Report to the user and ask if they would like to try to find the file
                        string mediaType = fileName = index switch
                        {
                            0 => "left mono",
                            1 => "right mono",
                            2 => "left stereo",
                            3 => "right stereo",
                            _ => "",
                        };

                        string message;

                        if (media.MediaPath is not null)
                            message = $"The {mediaType} media file '{fileSpec}' does not exist." +
                                " Press 'Ok' to try to find the file. Press 'Cancel' to stop loading the project";
                        else
                            message = $"The {mediaType} media file '{fileName}' does not exist." +
                                " Press 'Ok' to try to find the file. Press 'Cancel' to stop loading the project";

                        // Create a SymbolIcon with an exclamation mark
                        var warningIcon = new SymbolIcon(Symbol.Important); // Symbol.Important represents an exclamation

                        // Create the ContentDialog instance
                        var dialog = new ContentDialog
                        {
                            Title = $"{mediaType} media file missing",
                            Content = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 10,
                                MaxWidth = 500,
                                Children =
                                {
                                    warningIcon,
                                    new TextBlock
                                    {
                                        Text = message,
                                        TextWrapping = TextWrapping.Wrap,
                                        MaxWidth = 400
                                    }
                                }
                            },
                            PrimaryButtonText = "OK",
                            SecondaryButtonText = "Cancel",
                            DefaultButton = ContentDialogButton.Primary, // Set "OK" as the default button

                            // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                            XamlRoot = this.Content.XamlRoot
                        };

                        // Show the dialog and await the result
                        var result = await dialog.ShowAsync();

                        // Handle the dialog result
                        if (result == ContentDialogResult.Primary)
                        {
                            FileOpenPicker openPicker = new();
                            IntPtr hwnd = WindowNative.GetWindowHandle(this); // Assuming 'this' is your current window.
                            InitializeWithWindow.Initialize(openPicker, hwnd);

                            openPicker.ViewMode = PickerViewMode.Thumbnail; // Makes it easier for users to find their files visually.
                            openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary; // Suggest starting in the Pictures library.
                            openPicker.FileTypeFilter.Add(".mp4");


                            var file = await openPicker.PickSingleFileAsync();
                            if (file is not null)
                            {
                                // Adjust media file name
                                string fileNameOnly = System.IO.Path.GetFileName(file.Name);
                                switch (index)
                                {
                                    case 0:
                                        media.LeftMonoMP4FileName = fileNameOnly;
                                        break;
                                    case 1:
                                        media.RightMonoMP4FileName = fileNameOnly;
                                        break;
                                    case 2:
                                        media.LeftStereoMP4FileName = fileNameOnly;
                                        break;
                                    case 3:
                                        media.RightStereoMP4FileName = fileNameOnly;
                                        break;
                                    default:
                                        break;
                                };

                                string extractedMediaPath = System.IO.Path.GetDirectoryName(file.Path) ?? "";

                                // Check if the media path needs to change
                                if (media.MediaPath is not null)
                                {
                                    if (media.MediaPath != extractedMediaPath)
                                        media.MediaPath = extractedMediaPath;
                                }
                                else
                                {
                                    // Media is missing so just apply new path
                                    media.MediaPath = extractedMediaPath;
                                }
                            }
                            else
                            {
                                ret = -1;
                            }
                        }
                        else if (result == ContentDialogResult.Secondary)
                        {
                            // "Cancel" button clicked
                            ret = -999;
                        }
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Centralized project save
        /// </summary>
        /// <returns></returns>
        private async Task<int> FileProjectSaveOrSaveAsAsync()
        {
            int ret = -1;

            if (calibProject is not null)
            {
                if (calibProject.Data.Info.ProjectPath == null || calibProject.Data.Info.ProjectFileName == null)
                {
                    // Not saved yet so use 'Save As'
                    ret = await SaveAsProjectAsync();
                }
                else
                {
                    // Save
                    ret = calibProject.ProjectSave();
                }
            }

            SetUIControls();

            return ret;
        }


        /// <summary>
        /// Save the current calibration project to a new file
        /// </summary>
        /// <returns></returns>
        private async Task<int> SaveAsProjectAsync()
        {
            int ret = 0;

            if (calibProject is not null)
            {
                string suggestedFileName = calibProject.SuggestProjectFileName();

                var file = await PickCalProjFileToSaveAsync(this, suggestedFileName); // 'this' refers to your Window instance
                if (file != null)
                {
                    SetTitle(System.IO.Path.GetFileNameWithoutExtension(file.Name));
                    SetTitleSaveStatus("Saving...");

                    // Prevent updates to the remote version of the file until we finish making changes and call CompleteUpdatesAsync.
                    CachedFileManager.DeferUpdates(file);

                    try
                    {
                        // Save the calib project data to the file
                        ret = await calibProject.ProjectSaveAsAsync(file.Path);

                        // Let Windows know that we're finished changing the file so the other app can update the remote version of the file.
                        FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
                        if (status == FileUpdateStatus.Complete)
                        {
                            Debug.WriteLine($"File {file.Path} saved successfully.");

                            // Add to Recent Surveys
                            AddToRecentProjects(file.Path);
                            UpdateRecentProjectsMenu();

                            Debug.WriteLine($"Calibration project saved to {file.Path}");
                            SetTitle(System.IO.Path.GetFileNameWithoutExtension(calibProject.Data.Info.ProjectFileName));

                            // Remember the survey folder                        
                            SettingsManagerLocal.ProjectFolder = System.IO.Path.GetDirectoryName(file.Path);
                        }
                        else
                        {
                            ret = -1;
                            Debug.WriteLine($"Failed to save file {file.Path}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error saving calibration project: {ex.Message}");
                        // Handle the error, e.g., show a message to the user
                    }
                    finally
                    {
                        SetTitleSaveStatus("");
                    }
                }
                else
                {
                    Debug.WriteLine("No file selected for saving calibration project.");
                }
            }

            return ret;
        }


        /// <summary>
        /// Used to pick a .calib file to save the calibration project
        /// </summary>
        /// <param name="window"></param>
        /// <returns></returns>
        private static async Task<StorageFile?> PickCalProjFileToSaveAsync(Window window, string suggestedFileName)
        {
            var savePicker = new FileSavePicker();

            // Initialize with the window handle
            var hWnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(savePicker, hWnd);

            // Set file type choices and default extension
            savePicker.FileTypeChoices.Add("Calibration Project", [".calproj"]);
            savePicker.DefaultFileExtension = ".calproj";

            // Optional: set suggested file name
            savePicker.SuggestedFileName = suggestedFileName;

            StorageFile file = await savePicker.PickSaveFileAsync();
            return file;
        }


        /// <summary>
        /// Open all the media files for the calibration project
        /// </summary>
        /// <param name="calibProject"></param>
        /// <param name="forceUsdCacheIfAvalable"></param>
        /// <param name="noPrompts"></param>
        /// <returns></returns>
        private async Task<bool> OpenMediaSetsAsync(CalibProject calibProject, bool forceUsdCacheIfAvalable, bool noPrompts)
        {
            bool ret = false;

            try
            {
                // Open Media Files
                var tasks = new List<Task>();

                InfoBarProcessing.ShowProcessing("Open Media...");
                SetDisplayModeOnAllHeads(AppMode.Open);

                bool openStereo = false;
                bool openLeftMono = false;
                bool openRightMono = false;

                switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                {
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        openStereo = true;
                        openLeftMono = true;
                        openRightMono = true;
                        break;

                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        openStereo = true;
                        break;

                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        openLeftMono = true;
                        openRightMono = true;
                        break;

                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        openLeftMono = true;
                        break;
                }

                int taskIndex = 0;
                int taskStereoOpenIndex = -1;
                int taskLeftMonoOpenIndex = -1;
                int taskRightMonoOpenIndex = -1;

                if (openStereo)
                {
                    tasks.Add(StereoCalibrationHead.OpenMediaAsync(calibProject.Data.Media.LeftStereoMP4Path, calibProject.Data.Media.RightStereoMP4Path));
                    taskStereoOpenIndex = taskIndex++;
                }
                if (openLeftMono)
                {
                    tasks.Add(LeftMonoCalibrationHead.OpenMediaAsync(calibProject.Data.Media.LeftMonoMP4Path, string.Empty));
                    taskLeftMonoOpenIndex = taskIndex++;
                }
                if (openRightMono)
                {
                    tasks.Add(RightMonoCalibrationHead.OpenMediaAsync(calibProject.Data.Media.RightMonoMP4Path, string.Empty));
                    taskRightMonoOpenIndex = taskIndex++;
                }

                try
                {
                    // Run all finds in parallel, but still observe completion and exceptions
                    await Task.WhenAll(tasks);

                    // Get result of each task
                    bool openStereoResult = true;
                    bool openLeftMonoResult = true;
                    bool openRightMonoResult = true;

                    if (openStereo)
                        openStereoResult = await ((Task<bool>)tasks[taskStereoOpenIndex]);
                    if (openLeftMono)
                        openLeftMonoResult = await ((Task<bool>)tasks[taskLeftMonoOpenIndex]);
                    if (openRightMono)
                        openRightMonoResult = await ((Task<bool>)tasks[taskRightMonoOpenIndex]);

                    // Check if any failed
                    if ((openStereo && !openStereoResult) ||
                        (openLeftMono && !openLeftMonoResult) ||
                        (openRightMono && !openRightMonoResult))
                    {
                        Debug.WriteLine($"Error opening media files: Stereo Result={openStereoResult}, Left Mono Result={openLeftMonoResult}, Right Mono Result={openRightMonoResult}");
                    }
                    else
                    {
                        // Get the frame size
                        (int stereoFrameWidth, int stereoFrameHeight) = StereoCalibrationHead.GetFrameSize();
                        (int leftMonoFrameWidth, int leftMonoFrameHeight) = LeftMonoCalibrationHead.GetFrameSize();
                        (int rightMonoFrameWidth, int rightMonoFrameHeight) = RightMonoCalibrationHead.GetFrameSize();

                        bool frameSizeOk = false;
                        int frameWidth = -1;
                        int frameHeight = -1;
                        switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                        {
                            case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                                if (stereoFrameWidth != 0 && stereoFrameHeight != 0 &&
                                    stereoFrameWidth == leftMonoFrameWidth &&
                                    leftMonoFrameWidth == rightMonoFrameWidth &&
                                    stereoFrameHeight == leftMonoFrameHeight &&
                                    leftMonoFrameHeight == rightMonoFrameHeight)
                                {
                                    frameWidth = stereoFrameWidth;
                                    frameHeight = stereoFrameHeight;
                                    frameSizeOk = true;
                                }
                                break;

                            case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                                if (stereoFrameWidth != 0 && stereoFrameHeight != 0)
                                {
                                    frameWidth = stereoFrameWidth;
                                    frameHeight = stereoFrameHeight;
                                    frameSizeOk = true;
                                }
                                break;

                            case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                                if (leftMonoFrameWidth != 0 && leftMonoFrameHeight != 0 &&
                                    leftMonoFrameWidth == rightMonoFrameWidth &&
                                    leftMonoFrameHeight == rightMonoFrameHeight)
                                {
                                    frameWidth = stereoFrameWidth;
                                    frameHeight = stereoFrameHeight;
                                    frameSizeOk = true;
                                }
                                break;

                            case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                                if (leftMonoFrameWidth != 0 && leftMonoFrameHeight != 0)
                                {
                                    frameWidth = stereoFrameWidth;
                                    frameHeight = stereoFrameHeight;
                                    frameSizeOk = true;
                                }
                                break;
                        }

                        if (frameSizeOk)
                        {
                            calibProject.Data.Media.FrameWidth = frameWidth;
                            calibProject.Data.Media.FrameHeight = frameHeight;

                            // Check if stereo lock needed
                            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                            {
                                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                                    if (calibProject.Data.Sync.IsSynchronized)
                                    {
                                        // Lock Media
                                        StereoCalibrationHead.LockStereo(calibProject.Data.Sync.SyncFrameIndexLeft,
                                                                         calibProject.Data.Sync.SyncFrameIndexLeft);
                                    }
                                    break;
                            }

                            // Success
                            ret = true;

                        }
                    }
                }
                catch (Exception ex)
                {
                    // Handle/log properly
                    Debug.WriteLine($"Error in OpenMediaSetsAsync: {ex}");
                }

                InfoBarProcessing.HideProcessing();

                SetUIControls();                
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                Debug.WriteLine($"Error showing CalibInfoAndMediaContentDialog: {ex.Message}");
            }

            return ret;
        }

        /// <summary>
        /// Close Media Files
        /// </summary>
        private async Task CloseMediaSetsAsync(bool isExisting = false)
        {
            if (calibProject is not null)
            {
                // Settle
                await Task.Delay(50);

                // Close Media Files
                bool closeStereo = false;
                bool closeLeftMono = false;
                bool closeRightMono = false;

                switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                {
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        closeStereo = true;
                        closeLeftMono = true;
                        closeRightMono = true;
                        break;

                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        closeStereo = true;
                        break;

                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        closeLeftMono = true;
                        closeRightMono = true;
                        break;

                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        closeLeftMono = true;
                        break;
                }

                try
                {
                    if (closeStereo)
                        StereoCalibrationHead.CloseMedia();
                    if (closeLeftMono)
                        LeftMonoCalibrationHead.CloseMedia();
                    if (closeRightMono)
                        RightMonoCalibrationHead.CloseMedia();

                    SetDisplayModeOnAllHeads(AppMode.Close);
                }
                catch (Exception ex)
                {
                    // Handle/log properly
                    Debug.WriteLine($"Error in CloseMediaSetsAsync: {ex}");
                }

                // No UI work if existing
                if (!isExisting)
                {
                    SetTitle("");
                    SetTitleSaveStatus("");
                    SetTitleCameraSide("");

                    SetUIControls();
                }
            }
        }


        /// <summary>
        /// Check if there is an existing project open and if so check if it has unsaved changes
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <returns>true is ok to proceed (i.e. no project now open)</returns>
        internal async Task<bool> CheckForOpenProjectAndCloseAsync(bool isExiting = false)
        {
            bool ret = false;

            if (calibProject is not null)
            {
                bool closeSurvey = false;

                if (calibProject.IsDirty == true)
                {
                    try
                    {
                        // Create a FontIcon using the Segoe Fluent Icons font
                        var warningIcon = new FontIcon
                        {
                            Glyph = "\uE814", // Unicode character for a warning icon in Segoe Fluent Icons
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            Width = 24,
                            Height = 24
                        };

                        ContentDialog confirmationDialog = new()
                        {
                            Title = "Close Survey",
                            Content = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 10,
                                Children =
                            {
                                warningIcon, // Add the warning icon to the dialog content
                                new TextBlock
                                {
                                    Text = "Before you close this survey do you want to save the changes you have made?\n\nPress 'Yes' to save the existing survey, 'No' to close without saving",
                                    TextWrapping = TextWrapping.Wrap, // Enables text wrapping
                                    MaxWidth = 300 // Prevents text from stretching too wide
                                }
                            }
                            },
                            CloseButtonText = "Cancel",
                            PrimaryButtonText = "Yes",
                            SecondaryButtonText = "No",
                            DefaultButton = ContentDialogButton.Primary, // Set the default focused button to "Yes"

                            // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                            XamlRoot = this.Content.XamlRoot
                        };

                        // Display the dialog
                        var result = await confirmationDialog.ShowAsync();

                        // Handle the dialog result
                        if (result == ContentDialogResult.Primary)
                        {
                            // "Yes" button clicked
                            await FileProjectSaveOrSaveAsAsync();
                            closeSurvey = true;

                        }
                        else if (result == ContentDialogResult.Secondary)
                        {
                            // "No" button clicked
                            closeSurvey = true;
                        }
                        // If the select Cancel the Close Survey request is cancelled
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"CheckForOpenSurveyAndClose (confirm phase): {ex.Message}");
                    }
                }
                else
                    closeSurvey = true;


                if (closeSurvey == true)
                {
                    try
                    {
                        // Wait for things to settle
                        await Task.Delay(100);

                        // Closes the StereoMediaController, clears the title and the sync indicator
                        await CloseMediaSetsAsync(isExiting);

                        // Close and clear the Survey class (holds the survey data)
                        if (calibProject is not null)
                        {

                            await calibProject.ProjectCloseAsync();
                            calibProject = null;
                        }

                        ret = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"CheckForOpenSurveyAndClose (close phase): {ex.Message}");
                    }
                }
            }
            else
                ret = true;

            if (!isExiting)
                SetUIControls();

            return ret;
        }


        /// <summary>
        /// Openn and load all the framesset caches
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns></returns>
        private async Task<bool> LoadFrameDataCachesAsync(bool noPrompts)
        {
            bool ret = false;

            if (calibProject is null)
                return ret;

            try
            {        
                // Check if cached results files are available
                bool cachedResultsAvailable = CachedResultsFileExists();

                // Ask the user if they want to use cached results (a full set of results is required)
                if (cachedResultsAvailable == true)
                {
                    InfoBarProcessing.ShowProcessing("Load Cached Results...", true/*show elapsed time*/);

                    bool loaded = false;
                    int? stereoFramesLoaded = null;
                    int? leftMonoFramesLoaded = null;
                    int? rightMonoFramesLoaded = null;

                    // Load cached results
                    CalibProject.DataClass.CacheClass cache = calibProject.Data.Cache;

                    switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                    {
                        case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                            try
                            {
                                stereoFramesLoaded = StereoCalibrationHead.LoadCachedResults(cache.StereoFrameSetCacheFileSpec);
                                leftMonoFramesLoaded = LeftMonoCalibrationHead.LoadCachedResults(cache.LeftMonoFrameSetCacheFileSpec);
                                rightMonoFramesLoaded = RightMonoCalibrationHead.LoadCachedResults(cache.RightMonoFrameSetCacheFileSpec);

                                if (stereoFramesLoaded is not null && leftMonoFramesLoaded is not null && rightMonoFramesLoaded is not null &&
                                    stereoFramesLoaded > 0 && leftMonoFramesLoaded > 0 && rightMonoFramesLoaded > 0)
                                {
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
                                stereoFramesLoaded = StereoCalibrationHead.LoadCachedResults(cache.StereoFrameSetCacheFileSpec);

                                if (stereoFramesLoaded is not null && stereoFramesLoaded > 0)
                                {
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
                                leftMonoFramesLoaded = LeftMonoCalibrationHead.LoadCachedResults(cache.LeftMonoFrameSetCacheFileSpec);
                                rightMonoFramesLoaded = RightMonoCalibrationHead.LoadCachedResults(cache.RightMonoFrameSetCacheFileSpec);

                                if (leftMonoFramesLoaded is not null && rightMonoFramesLoaded is not null &&
                                    leftMonoFramesLoaded > 0 && rightMonoFramesLoaded > 0)
                                {
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
                                leftMonoFramesLoaded = LeftMonoCalibrationHead.LoadCachedResults(cache.LeftMonoFrameSetCacheFileSpec);

                                if (leftMonoFramesLoaded is not null && leftMonoFramesLoaded > 0)
                                {
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
                    if (loaded)
                    {
                        // Sucess
                        ret = true;
                    }
                    else
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

                    
                }
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                Debug.WriteLine($"LoadFrameDataCaches: {ex.Message}");
            }
            finally
            {
                InfoBarProcessing.HideProcessing();
                SetUIControls();
            }

            return ret;
        }

        /// <summary>
        /// Save all the media files for the calibration project
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns></returns>
        private async Task<bool> SaveFrameDataCachesAsync(bool noPrompts)
        {
            bool ret = false;

            if (calibProject is null)
                return ret;

            try
            {
                InfoBarProcessing.ShowProcessing("Save Cached Results...", true/*show elapsed time*/);
                SetUIControls();

                bool doStereo = false;
                bool doLeftMono = false;
                bool doRightMono = false;

                switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                {

                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        if (StereoCalibrationHead.IsOpen() &&
                            LeftMonoCalibrationHead.IsOpen() &&
                            RightMonoCalibrationHead.IsOpen())
                        {
                            doStereo = true;
                            doLeftMono = true;
                            doRightMono = true;
                        }
                        break;
                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        if (StereoCalibrationHead.IsOpen())
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

                // Run SaveCachedResults in parallel where applicable
                CalibProject.DataClass.CacheClass cache = calibProject.Data.Cache;

                var saveTasks = new List<Task>();
                if (doStereo)
                    saveTasks.Add(Task.Run(() => StereoCalibrationHead.SaveCachedResults(cache.StereoFrameSetCacheFileSpec)));
                if (doLeftMono)
                    saveTasks.Add(Task.Run(() => LeftMonoCalibrationHead.SaveCachedResults(cache.LeftMonoFrameSetCacheFileSpec)));
                if (doRightMono)
                    saveTasks.Add(Task.Run(() => RightMonoCalibrationHead.SaveCachedResults(cache.RightMonoFrameSetCacheFileSpec)));

                if (saveTasks.Count > 0)
                {
                    try { await Task.WhenAll(saveTasks); }
                    catch (Exception ex) { Debug.WriteLine($"Error post saving cached results: {ex}"); }
                }
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                Debug.WriteLine($"LoadFrameDataCaches: {ex.Message}");
            }
            finally
            {
                InfoBarProcessing.HideProcessing();
                SetUIControls();
            }

            return ret;
        }


        /// <summary>
        /// This is core function.  It orchestrates the finding of calibration 
        /// targets by the different UniversalCalibrationHeadUserControls.
        /// This is data on every frame in the media set. This can be time consuming
        /// and it is cached for future use.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async Task<int> BuildFrameSetsAsync()
        {
            int ret = 0;

            if (calibProject is null)
                return -2;

            try
            {
                var tasks = new List<Task>();
                InfoBarProcessing.ShowProcessing("Finding calibration frames...");
                SetDisplayModeOnAllHeads(AppMode.FindCalibrationsFrames);

                bool doLeftMono = false;
                bool doRightMono = false;
                bool doStereo = false;
                bool okToProceed = false;

                switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                {
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        if (StereoCalibrationHead.IsOpen() &&
                            (bool)StereoCalibrationHead.IsStereoLocked()! &&
                            LeftMonoCalibrationHead.IsOpen() &&
                            RightMonoCalibrationHead.IsOpen())
                        {
                            doLeftMono = true;
                            doRightMono = true;
                            doStereo = true;
                            okToProceed = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        if (StereoCalibrationHead.IsOpen() &&
                            (bool)StereoCalibrationHead.IsStereoLocked()!)
                        {
                            doStereo = true;
                            okToProceed = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        if (LeftMonoCalibrationHead.IsOpen() &&
                            RightMonoCalibrationHead.IsOpen())
                        {
                            doLeftMono = true;
                            doRightMono = true;
                            okToProceed = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        if (LeftMonoCalibrationHead.IsOpen())
                        {
                            doLeftMono = true;
                            okToProceed = true;
                        }
                        break;
                }

                if (okToProceed)
                {
                    int taskIndex = 0;
                    int taskLeftMonoIndex = -1;
                    int taskRightMonoIndex = -1;
                    int taskStereoIndex = -1;

                    if (doLeftMono)
                    {
                        tasks.Add(LeftMonoCalibrationHead.FindCalibrationFrameAsync());
                        taskLeftMonoIndex = taskIndex++;
                    }
                    if (doRightMono)
                    {
                        tasks.Add(RightMonoCalibrationHead.FindCalibrationFrameAsync());
                        taskRightMonoIndex = taskIndex++;
                    }
                    if (doStereo)
                    {
                        tasks.Add(StereoCalibrationHead.FindCalibrationFrameAsync());
                        taskStereoIndex = taskIndex++;
                    }

                    if (tasks.Count == 0)
                        return 0;

                    // Your existing flags + timer
                    StartFindCheckTimer();

                    try
                    {
                        // Run all finds in parallel, but still observe completion and exceptions
                        await Task.WhenAll(tasks);

                        // Get result of each task
                        int stereoResult = 0;
                        int leftMonoResult = 0;
                        int rightMonoResult = 0;

                        if (doLeftMono)
                            leftMonoResult = await ((Task<int>)tasks[taskLeftMonoIndex]);
                        if (doRightMono)
                            rightMonoResult = await ((Task<int>)tasks[taskRightMonoIndex]);
                        if (doStereo)
                            stereoResult = await ((Task<int>)tasks[taskStereoIndex]);

                        // Check if any failed
                        if ((doLeftMono && leftMonoResult != 0) ||
                            (doRightMono && rightMonoResult != 0) ||
                            (doStereo && stereoResult != 0))
                        {
                            Debug.WriteLine($"Error FindCalibrationFrameAsync: Left Mono Result={leftMonoResult}, Right Mono Result={rightMonoResult},Stereo Result={stereoResult}");

                            if (doLeftMono && leftMonoResult != 0)
                                ret = leftMonoResult;
                            else if (doRightMono && rightMonoResult != 0)
                                ret = rightMonoResult;
                            else if (doStereo && stereoResult != 0)
                                ret = stereoResult;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle/log properly
                        Debug.WriteLine($"Error in FindAppBarButtonAsync: {ex}");
                    }
                }
                else
                    ret = -3;

                    // Save the frames dataset
                InfoBarProcessing.UpdateMessage("Saving stereo best frames...");
                await SaveFrameDataCachesAsync(false/*no prompts*/);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FindAppBarButtonAsync: Exception:{ex.Message}");
            }
            finally
            {
                InfoBarProcessing.HideProcessing();
            }

            return ret;
        }



        /// <summary>
        /// From the extract frame information find the best calibration frames
        /// and run the calibration calculation. Optionally the best frames can be 
        /// saved to the 'Documents/Camera Calibration' folder
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async Task IdentifyBestFrameaAndDoCalibrationCalcAsync(RunCalibrationParams runParams)
        {
            if (calibProject is null)
                return;

            //???InfoBarProcessing.ShowProcessing("Saving...");
            SetDisplayModeOnAllHeads(AppMode.BestFramesCalc);

            SetUIControls();

            bool doStereo = false;
            bool doLeftMono = false;
            bool doRightMono = false;
            bool useExistingMonoBestFrames = false;

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
                        useExistingMonoBestFrames = true;
                    }
                    else
                    {
                        // Remove cached mono calibration results
                        Array.Fill(calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray, null);
                        Array.Fill(calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray, null);
                    }
                }
            }

            // Find the best frames from the mono calibration frames 
            InfoBarProcessing.UpdateMessage("Best frames calc mono...");

            var monoPhaseTasks = new List<Task>();

            if (!useExistingMonoBestFrames)
            {
                if (doLeftMono)
                {
                    monoPhaseTasks.Add(Task.Run(() => LeftMonoCalibrationHead.FindBestFramesNoUIAsync(
                                                                 calibProject,
                                                                 true/*trueLeftFalseRight*/,
                                                                 runParams.MovementFilterValue,
                                                                 runParams.BlurFilterValue,
                                                                 runParams.MonoCornersFilterValue,
                                                                 runParams.SaveBestFrames)));
                }
                if (doRightMono)
                {
                    monoPhaseTasks.Add(Task.Run(() => RightMonoCalibrationHead.FindBestFramesNoUIAsync(
                                                                 calibProject,
                                                                 false/*trueLeftFalseRight*/,
                                                                 runParams.MovementFilterValue,
                                                                 runParams.BlurFilterValue,
                                                                 runParams.MonoCornersFilterValue,
                                                                 runParams.SaveBestFrames)));
                }

                // Find the best frames in parallel
                if (monoPhaseTasks.Count > 0)
                {
                    try { await Task.WhenAll(monoPhaseTasks); }
                    catch (Exception ex) { Debug.WriteLine($"Error find best mono frames: {ex}"); }
                }
            }

            // Do the mono calibration calculations
            monoPhaseTasks.Clear();

            if (doLeftMono)
            {
                monoPhaseTasks.Add(Task.Run(() => LeftMonoCalibrationHead.DoMonoCalibrationCalculationNoUI(
                                                             calibProject,
                                                             true/*trueLeftFalseRight*/,
                                                             runParams.MonoCornersFilterValue)));
            }
            if (doRightMono)
            {
                monoPhaseTasks.Add(Task.Run(() => RightMonoCalibrationHead.DoMonoCalibrationCalculationNoUI(
                                                             calibProject,
                                                             false/*trueLeftFalseRight*/,
                                                             runParams.MonoCornersFilterValue)));
            }

            if (monoPhaseTasks.Count > 0)
            {
                try { await Task.WhenAll(monoPhaseTasks); }
                catch (Exception ex) { Debug.WriteLine($"Error find best mono frames: {ex}"); }
            }


            // Save the frames dataset
            InfoBarProcessing.UpdateMessage("Saving aftero mono phase...");
            await SaveFrameDataCachesAsync(false/*no prompts*/);


            // Find the best frames from the stereo calibration frames
            if (doStereo)
            {
                InfoBarProcessing.UpdateMessage("Best frames calc stereo...");

                if (calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray.Any(item => item != null) &&
                    calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray.Any(item => item != null))
                {
                    await StereoCalibrationHead.BestFramesCalcAndStereoCalibrationAsync(
                                                                calibProject,
                                                                runParams.MovementFilterValue,
                                                                runParams.BlurFilterValue,
                                                                runParams.MonoCornersFilterValue,
                                                                runParams.SaveBestFrames);

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
                            calibrationData.LeftCameraCalibration.ImageSize[0, 0] = (int)calibProject.Data.Media.FrameWidth;
                            calibrationData.LeftCameraCalibration.ImageSize[0, 1] = (int)calibProject.Data.Media.FrameHeight;

                            calibrationData.LeftCameraCalibration.ImageTotal = leftMonoCalibrationCameraData.ImageTotal;
                            calibrationData.LeftCameraCalibration.ImageUseable = leftMonoCalibrationCameraData.ImageUseable;
                            calibrationData.LeftCameraCalibration.Intrinsic = leftMonoCalibrationCameraData.IntrinsicMatrix;
                            calibrationData.LeftCameraCalibration.Distortion = leftMonoCalibrationCameraData.DistortionCoeffs;
                            calibrationData.LeftCameraCalibration.RMS = leftMonoCalibrationCameraData.ReprojectionRMS;

                            calibrationData.RightCameraCalibration.ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/);
                            calibrationData.RightCameraCalibration.ImageSize[0, 0] = (int)calibProject.Data.Media.FrameWidth;
                            calibrationData.RightCameraCalibration.ImageSize[0, 1] = (int)calibProject.Data.Media.FrameHeight;
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

                // Save the frames dataset
                InfoBarProcessing.UpdateMessage("Saving after stereo phase...");
                await SaveFrameDataCachesAsync(false/*no prompts*/);
            }


            // Save the calibration project file
            if (doLeftMono || doRightMono || doStereo)
            {
                if (calibProject.IsLoaded)
                    calibProject.ProjectSave();
            }

            InfoBarProcessing.HideProcessing();

            // Allow the user to browse the best frames
            SetDisplayModeOnAllHeads(AppMode.BestFramesView);  

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
        //[Obsolete]
        //private static CalibrationParameters? ReturnBestMonoCalibrationCameraData(
        //                            MonoCalibrationCameraData?[] leftMonoCalibrationCameraData,
        //                            MonoCalibrationCameraData?[] rightMonoCalibrationCameraData)
        //{
        //    double bestScore = double.MaxValue;
        //    int bestIndex = -1;

        //    for (int i = 0; i < leftMonoCalibrationCameraData.Length; i++)
        //    {
        //        var left = leftMonoCalibrationCameraData[i];
        //        var right = rightMonoCalibrationCameraData[i];

        //        if (left == null || right == null)
        //            continue;

        //        // Combine left and right metrics
        //        double rmsAvg = (left.ReprojectionRMS + right.ReprojectionRMS) / 2.0;
        //        double maxErrAvg = (left.MaxError + right.MaxError) / 2.0;

        //        // Define weighted score (you can tune weights as needed)
        //        double score = rmsAvg + 0.2 * maxErrAvg;

        //        if (score < bestScore)
        //        {
        //            bestScore = score;
        //            bestIndex = i;
        //        }
        //    }

        //    if (bestIndex == -1)
        //        return null;

        //    return (CalibrationParameters)bestIndex;
        //}




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
        /// Set the UI controls to the current mode
        /// </summary>
        private void SetUIControls()
        {
            //bool? isLocked = StereoCalibrationHead.IsStereoLocked();
            bool isCalibProjectOpen = calibProject is not null;
            bool isMediaLocked = false;
            bool isProcessingHappening = IsFindRunning();
            bool isStereo = false;

            // Calc isMediaLocked
            if (isCalibProjectOpen)
            {
                if (calibProject?.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet ||
                    calibProject?.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet)
                {

                    if (calibProject?.Data.Sync.IsSynchronized ?? false)
                        isMediaLocked = true;
                    else
                        isMediaLocked = false;

                    isStereo = true;
                }
                else
                {
                    isStereo = false;
                }
            }

            // File>New Project menu item
            MenuProjectNew.IsEnabled = !isCalibProjectOpen;

            // File>Open Project menu item
            MenuProjectOpen.IsEnabled = !isCalibProjectOpen;

            // File>Save/SaveAs Project menu item
            MenuProjectSave.IsEnabled = isCalibProjectOpen && !isProcessingHappening && (calibProject?.IsDirty ?? false);
            MenuProjectSaveAs.IsEnabled = isCalibProjectOpen && !isProcessingHappening;

            // File>Close Project menu item
            MenuProjectClose.IsEnabled = isCalibProjectOpen && !isProcessingHappening;

            // File>Lock/Unlock Media menu item & Lock/Unlock Titlebar Icon
            MenuLockUnlockMediaPlayers.IsEnabled = isStereo;

            if (isCalibProjectOpen)
            {
                if (isMediaLocked)
                {
                    // Media is currently locked and we are going to unlock it

                    // Set the menu text so the users can unlock it again in the future
                    MenuLockUnlockMediaPlayers.Text = "Unlock Media Players";
                    MenuLockUnlockMediaPlayersIcon.Glyph = "\uE1F7"; // Unlock icon

                    // Indicate the media is unlocked on the title bar
                    SetLockUnlockIndicator(true/*locked*/, null);
                }
                else
                {
                    // Media is currently unlocked and we are going to lock it

                    // Set the menu text so the users can lock it again in the future
                    MenuLockUnlockMediaPlayers.Text = "Lock Media Players";
                    MenuLockUnlockMediaPlayersIcon.Glyph = "\uE1F6"; // Lock icon

                    // Indicate the media is locked on the title bar
                    SetLockUnlockIndicator(false/*unlocked*/, null);
                }
            }
            else
            {
                SetLockUnlockIndicator(null, null);
            }

            // File>Run Calibration menu item
            if (isCalibProjectOpen)
            {
                if (isStereo)
                    MenuRunCalibration.IsEnabled = !isProcessingHappening && isMediaLocked;
                else
                    MenuRunCalibration.IsEnabled = !isProcessingHappening;
            }
            else
                MenuRunCalibration.IsEnabled = false;

            // Show/Hide Lock Media InfoBar
            if (isStereo && !isMediaLocked)
                InfoBarLockMedia.IsOpen = true;
            else
                InfoBarLockMedia.IsOpen = false;


            // Set the sliders
            //???SetMovementAndBlurSliderMax();

        }


        //private void SetMovementAndBlurSliderMax()
        //{
            
        //    if (findStatus == true)
        //    {
        //        double minMovement;
        //        double maxMovement;
        //        double maxBlur;
        //        double minBlur;

        //        {
        //            double minMovementMonoLeft = LeftMonoCalibrationHead.GetMinMovement(true/*trueNormalFalseBestFrame*/);
        //            double minMovementMonoRight = LeftMonoCalibrationHead.GetMinMovement(true/*trueNormalFalseBestFrame*/);
        //            double minMovementStereo = StereoCalibrationHead.GetMinMovement(true/*trueNormalFalseBestFrame*/);
        //            minMovement = Math.MinMagnitude(minMovementStereo, Math.MinMagnitude(minMovementMonoLeft, minMovementMonoRight));
        //        }

        //        {
        //            double maxMovementMonoLeft = LeftMonoCalibrationHead.GetMaxMovement(true/*trueNormalFalseBestFrame*/);
        //            double maxMovementMonoRight = LeftMonoCalibrationHead.GetMaxMovement(true/*trueNormalFalseBestFrame*/);
        //            double maxMovementStereo = StereoCalibrationHead.GetMaxMovement(true/*trueNormalFalseBestFrame*/);
        //            maxMovement = Math.MaxMagnitude(maxMovementStereo, Math.MaxMagnitude(maxMovementMonoLeft, maxMovementMonoRight));
        //        }

        //        {
        //            double minBlurMonoLeft = LeftMonoCalibrationHead.GetMinBlur(true/*trueNormalFalseBestFrame*/);
        //            double minBlurMonoRight = LeftMonoCalibrationHead.GetMinBlur(true/*trueNormalFalseBestFrame*/);
        //            double minBlurStereo = StereoCalibrationHead.GetMinBlur(true/*trueNormalFalseBestFrame*/);
        //            minBlur = Math.MinMagnitude(minBlurStereo, Math.MinMagnitude(minBlurMonoLeft, minBlurMonoRight));
        //        }

        //        {
        //            double maxBlurMonoLeft = LeftMonoCalibrationHead.GetMaxBlur(true/*trueNormalFalseBestFrame*/);
        //            double maxBlurMonoRight = LeftMonoCalibrationHead.GetMaxBlur(true/*trueNormalFalseBestFrame*/);
        //            double maxBlurStereo = StereoCalibrationHead.GetMaxBlur(true/*trueNormalFalseBestFrame*/);
        //            maxBlur = Math.MaxMagnitude(maxBlurStereo, Math.MaxMagnitude(maxBlurMonoLeft, maxBlurMonoRight));
        //        }

        //        // Load the movement/blur max values into the slider for the whole set of frames
        //        Sliders.Visibility = Visibility.Visible;

        //        // Setup Movement filter max/min values
        //        MovementMaxThresholdSlider.Minimum = minMovement;
        //        MovementSliderMin.Text = $"{minMovement:F1}";
        //        MovementMaxThresholdSlider.Maximum = maxMovement;
        //        MovementSliderMax.Text = $"{maxMovement:F1}";

        //        // Setup Movement filter max/min values
        //        BlurMaxThresholdSlider.Maximum = maxBlur;
        //        BlurSliderMin.Text = $"{minBlur:F1}";
        //        BlurMaxThresholdSlider.Maximum = maxBlur;
        //        BlurSliderMax.Text = $"{maxBlur:F1}";
        //    }
        //    else if (saveStatus == true)
        //    {
        //        double maxMovement;
        //        double maxBlur;

        //        {
        //            double maxMovementMonoLeft = LeftMonoCalibrationHead.GetMaxMovement(false/*trueNormalFalseBestFrame*/);
        //            double maxMovementMonoRight = LeftMonoCalibrationHead.GetMaxMovement(false/*trueNormalFalseBestFrame*/);
        //            double maxMovementStereo = StereoCalibrationHead.GetMaxMovement(false/*trueNormalFalseBestFrame*/);
        //            maxMovement = Math.MaxMagnitude(maxMovementStereo, Math.MaxMagnitude(maxMovementMonoLeft, maxMovementMonoRight));
        //        }

        //        {
        //            double maxBlurMonoLeft = LeftMonoCalibrationHead.GetMaxBlur(false/*trueNormalFalseBestFrame*/);
        //            double maxBlurMonoRight = LeftMonoCalibrationHead.GetMaxBlur(false/*trueNormalFalseBestFrame*/);
        //            double maxBlurStereo = StereoCalibrationHead.GetMaxBlur(false/*trueNormalFalseBestFrame*/);
        //            maxBlur = Math.MaxMagnitude(maxBlurStereo, Math.MaxMagnitude(maxBlurMonoLeft, maxBlurMonoRight));
        //        }

        //        // Load the movement/blur max values into the slider for the best frames
        //        Sliders.Visibility = Visibility.Visible;
        //        MovementMaxThresholdSlider.Maximum = maxMovement;
        //        BlurMaxThresholdSlider.Maximum = maxBlur;
        //    }
        //    else
        //    {
        //        // Hide the sliders
        //        Sliders.Visibility = Visibility.Collapsed;
        //    }
        //}

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

        private void StereoFindCheckTimer_Tick(object? sender, object e) => _ = StereoFindCheckTimerAsync();
        private async Task StereoFindCheckTimerAsync()
        {
            if (_findStartTime is not null)
            {
                await Task.Delay(1); // Allow UI to update

                SetUIControls();

                if (!IsFindRunning())
                {
                    StopFindCheckTimer();
                    _findStartTime = null; // Reset the start time

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
        private async Task ShowSettingsWindowAsync(string section = "")
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
        private async Task ShowSetupRunCalibrationAsync()
        {
            if (calibProject is null) return;

            try
            {
                int entryCount = Interlocked.Increment(ref setupRunCalibrationCount);
                // Make sure we only open the settings window once.
                // This can happen if the project and movies are loaded and the user clicks the settings a few times.
                if (entryCount == 1)
                {
                    // These are the paramters setup in the SetupRunCalibration window
                    // and used by the RunCalibration process
                    RunCalibrationParams runCalibrationParams = new()
                    {
                        // Check if cached results file exists and setup UI controls accordingly
                        UseFrameSetCache = CachedResultsFileExists(),

                        // If a cache is available that get the min movement/blur values (used by the sliders)
                        MovementFilterMin = GetMinMovementFromCachedResults(true/*from all frames*/),
                        BlurFilterMin = GetMinBlurFromCachedResults(true/*from all frames*/),

                        // If a cache is available that get the max movement/blur values (used by the sliders)
                        MovementFilterMax = GetMaxMovementFromCachedResults(true/*from all frames*/),
                        BlurFilterMax = GetMaxBlurFromCachedResults(true /*from all frames*/)
                    };


                    // Initialize if necessary
                    SetupRunCalibration setupRunCalibration = new(this, calibProject, runCalibrationParams);

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
        /// Find the largest movement value from the cached results
        /// </summary>
        /// <returns></returns>
        private double? GetMaxMovementFromCachedResults(bool trueNormalFalseBestFrames)
        {
            double? ret = null;

            double leftMonoMax = 0;
            double rightMonoMax = 0;
            double stereoMax = 0;

            if (LeftMonoCalibrationHead.IsOpen())
                leftMonoMax = LeftMonoCalibrationHead.GetMaxMovement(trueNormalFalseBestFrames);

            if (RightMonoCalibrationHead.IsOpen())
                rightMonoMax = RightMonoCalibrationHead.GetMaxMovement(trueNormalFalseBestFrames);

            if (StereoCalibrationHead.IsOpen())
                stereoMax = StereoCalibrationHead.GetMaxMovement(trueNormalFalseBestFrames);

            switch (calibProject?.Data.Media.StereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    if (leftMonoMax != 0)
                        ret = Math.Max(leftMonoMax, rightMonoMax);
                    break;
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (leftMonoMax != 0 || rightMonoMax != 0)
                        ret = Math.Max(leftMonoMax, rightMonoMax);
                    break;
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (stereoMax != 0)
                        ret = stereoMax;
                    break;
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    if (leftMonoMax != 0 || rightMonoMax != 0 || stereoMax != 0)
                        ret = Math.Max(leftMonoMax, Math.Max(rightMonoMax, stereoMax));
                    break;
                default:
                    break;
            }

            return ret;
        }


        /// <summary>
        /// Find the smallest value from the cached results
        /// </summary>
        /// <returns></returns>
        private double? GetMinMovementFromCachedResults(bool trueNormalFalseBestFrames)
        {
            double? ret = null;

            double leftMonoMin = double.MaxValue;
            double rightMonoMin = double.MaxValue;
            double stereoMin = double.MaxValue;

            if (LeftMonoCalibrationHead.IsOpen())
                leftMonoMin = LeftMonoCalibrationHead.GetMinMovement(trueNormalFalseBestFrames);

            if (RightMonoCalibrationHead.IsOpen())
                rightMonoMin = RightMonoCalibrationHead.GetMinMovement(true/*from all frames*/);

            if (StereoCalibrationHead.IsOpen())
                stereoMin = StereoCalibrationHead.GetMinMovement(true/*from all frames*/);

            switch (calibProject?.Data.Media.StereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    if (leftMonoMin != double.MaxValue)
                        ret = Math.Min(leftMonoMin, rightMonoMin);
                    break;
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (leftMonoMin != double.MaxValue || rightMonoMin != double.MaxValue)
                        ret = Math.Min(leftMonoMin, rightMonoMin);
                    break;
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (stereoMin != double.MaxValue)
                        ret = stereoMin;
                    break;
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    if (leftMonoMin != double.MaxValue || rightMonoMin != double.MaxValue || stereoMin != double.MaxValue)
                        ret = Math.Min(leftMonoMin, Math.Min(rightMonoMin, stereoMin));
                    break;
                default:
                    break;
            }

            return ret;
        }


        /// <summary>
        /// Find the largest blur value from the cached results
        /// </summary>
        /// <returns></returns>
        private double? GetMaxBlurFromCachedResults(bool trueNormalFalseBestFrames)
        {
            double? ret = null;

            double leftMonoMax = 0;
            double rightMonoMax = 0;
            double stereoMax = 0;

            if (LeftMonoCalibrationHead.IsOpen())
                leftMonoMax = LeftMonoCalibrationHead.GetMaxBlur(trueNormalFalseBestFrames);

            if (RightMonoCalibrationHead.IsOpen())
                rightMonoMax = RightMonoCalibrationHead.GetMaxBlur(trueNormalFalseBestFrames);

            if (StereoCalibrationHead.IsOpen())
                stereoMax = StereoCalibrationHead.GetMaxBlur(trueNormalFalseBestFrames);

            switch (calibProject?.Data.Media.StereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    if (leftMonoMax != 0)
                        ret = Math.Max(leftMonoMax, rightMonoMax);
                    break;
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (leftMonoMax != 0 || rightMonoMax != 0)
                        ret = Math.Max(leftMonoMax, rightMonoMax);
                    break;
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (stereoMax != 0)
                        ret = stereoMax;
                    break;
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    if (leftMonoMax != 0 || rightMonoMax != 0 || stereoMax != 0)
                        ret = Math.Max(leftMonoMax, Math.Max(rightMonoMax, stereoMax));
                    break;
                default:
                    break;
            }

            return ret;
        }


        /// <summary>
        /// Find the lasmallest blur value from the cached results
        /// </summary>
        /// <returns></returns>
        private double? GetMinBlurFromCachedResults(bool trueNormalFalseBestFrames)
        {
            double? ret = null;

            double leftMonoMin = double.MaxValue;
            double rightMonoMin = double.MaxValue;
            double stereoMin = double.MaxValue;

            if (LeftMonoCalibrationHead.IsOpen())
                leftMonoMin = LeftMonoCalibrationHead.GetMinBlur(trueNormalFalseBestFrames);

            if (RightMonoCalibrationHead.IsOpen())
                rightMonoMin = RightMonoCalibrationHead.GetMinBlur(trueNormalFalseBestFrames);

            if (StereoCalibrationHead.IsOpen())
                stereoMin = StereoCalibrationHead.GetMinBlur(trueNormalFalseBestFrames);

            switch (calibProject?.Data.Media.StereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    if (leftMonoMin != double.MaxValue)
                        ret = Math.Min(leftMonoMin, rightMonoMin);
                    break;
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (leftMonoMin != double.MaxValue || rightMonoMin != double.MaxValue)
                        ret = Math.Min(leftMonoMin, rightMonoMin);
                    break;
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (stereoMin != double.MaxValue)
                        ret = stereoMin;
                    break;
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    if (leftMonoMin != double.MaxValue || rightMonoMin != double.MaxValue || stereoMin != double.MaxValue)
                        ret = Math.Min(leftMonoMin, Math.Min(rightMonoMin, stereoMin));
                    break;
                default:
                    break;
            }

            return ret;
        }


        /// <summary>
        /// Set the lock or unlock indicator in the title bar
        /// </summary>
        /// <param name="locked">true = locked, false = unlock, null is blank</param>
        private void SetLockUnlockIndicator(bool? locked, TimeSpan? offset)
        {
            if (locked is null)
            {
                // Show nothing (normally if no media is open)
                // Four spaces to make it invisible and approximately the same width
                // as the lock/unlock icons (do not change, not fully understood but
                // needed to keep the tooltip working as the glyph changes)
                LockUnLockIndicator.Text = "    ";
                ToolTipService.SetToolTip(LockUnLockIndicator, "");
            }
            else if (locked == true)
            {
                // Show the lock icon
                LockUnLockIndicator.Text = "\uE1F6";
                if (offset is null)
                    ToolTipService.SetToolTip(LockUnLockIndicator, "The media is synchronized");
                else
                {
                    if (offset == TimeSpan.Zero)
                        ToolTipService.SetToolTip(LockUnLockIndicator, "The media is synchronized with both media set to start from their respective beginnings");
                    else if (offset > TimeSpan.Zero)
                        ToolTipService.SetToolTip(LockUnLockIndicator, "The media is synchronized and the right media is " + offset.Value.ToString(@"hh\:mm\:ss\.ff") + " ahead");
                    else
                        ToolTipService.SetToolTip(LockUnLockIndicator, "The media is synchronized and the left media is " + offset.Value.ToString(@"hh\:mm\:ss\.ff") + " ahead");
                }
            }
            else
            {
                // Show the unlock icon
                LockUnLockIndicator.Text = "\uE1F7";
                ToolTipService.SetToolTip(LockUnLockIndicator, "The media is not synchronized and either player can be played independently");

            }
        }


        /// <summary>
        /// Set the title text elements of the titlebar title text
        /// </summary>
        /// <param name="titleText"></param>
        public void SetTitle(string titleText)
        {
            titlebarTitle = titleText;

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                TitleBarTextBlock.Text = BuildTitleFromElements();
            });
        }


        /// <summary>
        /// Set the save status text elements of the titlebar title text
        /// </summary>
        /// <param name="saveStatus"></param>
        public void SetTitleSaveStatus(string saveStatus)
        {
            titlebarSaveStatus = saveStatus;

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                TitleBarTextBlock.Text = BuildTitleFromElements();
            });
        }


        /// <summary>
        /// Set the camera side status text elements of the titlebar title text
        /// </summary>
        /// <param name="cameraSide"></param>
        public void SetTitleCameraSide(string cameraSide)
        {
            titlebarCameraSide = cameraSide;

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                TitleBarTextBlock.Text = BuildTitleFromElements();
            });
        }


        /// <summary>
        /// Build the title from the elements
        /// </summary>
        /// <returns></returns>
        private string BuildTitleFromElements()
        {
            string title;

            string appTitle = Application.Current.Resources.TryGetValue("AppTitleName", out var titleObj)
                ? titleObj as string ?? TitleBarTextBlock.Text
                : TitleBarTextBlock.Text;

            if (!string.IsNullOrEmpty(titlebarTitle))
            {
                title = $"{appTitle}: ";

                title += titlebarTitle;

                if (!string.IsNullOrEmpty(titlebarSaveStatus))
                {
                    title += " (" + titlebarSaveStatus + ")";
                }

                if (!string.IsNullOrEmpty(titlebarCameraSide))
                {
                    title += " - " + titlebarCameraSide;
                }
            }
            else
            {

                title = appTitle;
            }

            return title;
        }


        /// <summary>
        /// If Locking:
        /// Get the current media positions and record in the Data.Sync class
        /// Inform the Stereo Head to lock
        /// 
        /// If Unlocking:
        /// Mark as unsynced in the Data.Sync class
        /// Inform the Stereo Head to unlock
        /// 
        /// </summary>
        /// <param name="lockTrueUnLockFalse"></param>
        private async Task LockUnlockMediaPlayersAsync(bool lockTrueUnLockFalse)
        {
            if (calibProject is null)
                return;

            // Check if request to lock of unlock
            if (lockTrueUnLockFalse)
            {
                // LOCK THE STEREO MEDIA PLAYERS

                // Action flags
                bool reEnable = false;
                bool newPosition = false;

                // Check if sync offset is already present and just needs enabling
                if (calibProject.Data.Sync.IsSynchronized == false &&                    
                    calibProject.Data.Sync.SyncFrameIndexLeft != 0 &&
                    calibProject.Data.Sync.SyncFrameIndexRight != 0)
                {
                    // Create a SymbolIcon with an exclamation mark
                    var warningIcon = new SymbolIcon(Symbol.Important); // Symbol.Important represents an exclamation

                    var dialog = new ContentDialog
                    {
                        Title = "Lock Media Players",
                        Content = new Grid
                        {
                            Width = 400, // Set the width of the dialog content
                            Children =
                            {
                                new StackPanel
                                {
                                    Orientation = Orientation.Horizontal,
                                    Spacing = 10,
                                    Children =
                                    {
                                        warningIcon, // Add the exclamation icon to the dialog content
                                        new TextBlock
                                        {
                                            Text = "There is synchronization information already in this survey that is currently disabled. Do you want to re-enable it or do you want to lock the players at their current position?",
                                            TextWrapping = TextWrapping.Wrap,
                                            MaxWidth = 320 // Limit width to allow wrapping
                                        }
                                    }
                                }
                            }
                        },
                        PrimaryButtonText = "Enable",
                        SecondaryButtonText = "Current Position",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary, // Set "OK" as the default button

                        // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                        XamlRoot = this.Content.XamlRoot
                    };

                    var result = await dialog.ShowAsync();

                    switch (result)
                    {
                        case ContentDialogResult.Primary:
                            // Handle Enable action
                            reEnable = true;
                            break;

                        case ContentDialogResult.Secondary:
                            // Handle Current Position action
                            newPosition = true;
                            break;
                    }
                }
                else
                {
                    // No sync information present so use the current position
                    newPosition = true;
                }

                // Lock the left and right media controlers
                if (calibProject is not null)
                {
                    if (reEnable)
                    {
                        calibProject.Data.Sync.IsSynchronized = true;
                    }
                    else if (newPosition)
                    {
                        // Get the current frame indexes and lock at that point
                        (int frameIndexLeft, int frameIndexRight) = StereoCalibrationHead.GetCurrentFrameIndexes();
                        if (frameIndexLeft != -1 && frameIndexRight != -1)
                        {
                            calibProject.Data.Sync.IsSynchronized = true;
                            calibProject.Data.Sync.SyncFrameIndexLeft = frameIndexLeft;
                            calibProject.Data.Sync.SyncFrameIndexRight = (int)frameIndexRight;
                        }
                    }
                }

                // Engage to the MediaTimelineController
                if (calibProject is not null && (reEnable || newPosition))
                {
                    StereoCalibrationHead.LockStereo(calibProject.Data.Sync.SyncFrameIndexLeft,
                                                     calibProject.Data.Sync.SyncFrameIndexRight);
                }
            }
            else
            {
                // UNLOCK THE STEREO MEDIA PLAYERS

                // Check user is sure they want to unlock the media players
                var dialog = new ContentDialog
                {
                    Title = "Unlock Media Players",
                    PrimaryButtonText = "Unlock",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                // Add a warning icon + text
                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = "\uE7BA", // Warning icon from Segoe MDL2 Assets
                            FontFamily = new FontFamily("Segoe MDL2 Assets"),
                            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange),
                            Width = 32,
                            Height = 32,
                            VerticalAlignment = VerticalAlignment.Top
                        },
                        new TextBlock
                        {
                            Text = "Are you sure you want to unlock the media players?",
                            TextWrapping = TextWrapping.Wrap,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                };

                dialog.Content = panel;

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // Unlock the left and right media 
                    if (calibProject is not null)
                    {
                        calibProject.Data.Sync.IsSynchronized = false;

                        // Don't remove the SyncFrameIndexLeft, SyncFrameIndexRight
                        // in case the user wants to sync again
                    }

                    StereoCalibrationHead.UnlockStereo();
                }
            }
        }


        /// <summary>
        /// Add the selected survey to the recent surveys list
        /// </summary>
        /// <param name="filePath"></param>
        private void AddToRecentProjects(string filePath)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var recentSurveys = (localSettings.Values[RECENT_PROJECTS_KEY] as string[]) ?? [];

            // Remove if already exists
            var list = new List<string>(recentSurveys);
            list.Remove(filePath);

            // Add to beginning
            list.Insert(0, filePath);

            // Keep only MAX_RECENT_PROJECTS items
            if (list.Count > MAX_RECENT_PROJECTS_SAVED)
                list.RemoveRange(MAX_RECENT_PROJECTS_SAVED, list.Count - MAX_RECENT_PROJECTS_SAVED);

            // Save back to settings
            localSettings.Values[RECENT_PROJECTS_KEY] = list.ToArray();

            UpdateRecentProjectsMenu();
        }


        /// <summary>
        /// Remove the selected survey to the recent surveys list
        /// </summary>
        /// <param name="filePath"></param>
        private void RemoveToRecentSurveys(string filePath)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var recentSurveys = (localSettings.Values[RECENT_PROJECTS_KEY] as string[]) ?? [];

            // Remove if already exists
            var list = new List<string>(recentSurveys);
            list.Remove(filePath);

            // Save back to settings
            localSettings.Values[RECENT_PROJECTS_KEY] = list.ToArray();

            UpdateRecentProjectsMenu();
        }


        /// <summary>
        /// Update the recent surveys menu from localSettings
        /// </summary>
        private void UpdateRecentProjectsMenu()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            string[]? recentSurveys = localSettings.Values[RECENT_PROJECTS_KEY] as string[];

            // Clear existing items in the MenuFlyoutSubItem
            MenuRecentProjects.Items.Clear();

            if (recentSurveys == null || recentSurveys.Length == 0)
            {
                // Add a single "Empty" menu item if no recent surveys exist
                var emptyItem = new MenuFlyoutItem
                {
                    Text = "(Empty)",
                    IsEnabled = false
                };
                MenuRecentProjects.Items.Add(emptyItem);
                return;
            }

            // Add new items from the recentSurveys array
            foreach (var surveyPath in recentSurveys)
            {
                if (MenuRecentProjects.Items.Count >= maxRecentProjectsDisplayed)
                    break;

                if (!string.IsNullOrEmpty(surveyPath))
                {
                    var menuItem = new MenuFlyoutItem
                    {
                        Text = System.IO.Path.GetFileName(surveyPath), // Use the file name as the menu item text
                        Tag = surveyPath // Store the full path in the Tag property
                    };

                    // Add tooltip to show the full file specification
                    ToolTipService.SetToolTip(menuItem, surveyPath);

                    // Optionally add a click event handler for the menu item
                    menuItem.Click += FileRecentProject_Click;

                    MenuRecentProjects.Items.Add(menuItem);
                }
            }
        }


        /// <summary>
        /// Tell all heads to set their display mode
        /// This controls the visibility of various UI elements like
        /// enabling/disabling the frame back/forward buttons etc.
        /// </summary>
        /// <param name="appMode"></param>
        private void SetDisplayModeOnAllHeads(AppMode appMode)
        {
            StereoCalibrationHead.SetDisplayMode(appMode);
            LeftMonoCalibrationHead.SetDisplayMode(appMode);
            RightMonoCalibrationHead.SetDisplayMode(appMode);
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

