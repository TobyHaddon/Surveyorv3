using iText.Layout;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using static iText.Svg.SvgConstants;
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
        // If there is cached frame set these values get set to the max in those frame sets 
        public double? MovementFilterMin { get; set; } = null;      
        public double? MovementFilterMax { get; set; } = null;
        public double? BlurFilterMin { get; set; } = null;    
        public double? BlurFilterMax { get; set; } = null;    

        // Used by the RunCalibration process        
        public double MovementFilterValue { get; set; } = MovementFilterDefaultValue;
        public double BlurFilterValue { get; set; } = BlurMaxFilterDefaultValue;
        public int MonoCornersFilterValue { get; set; } = CalibrationStereoFrameSet.MONO_CORNER_COUNT_THRESHOLD;
        public int StereoCornersFilterValue { get; set; } = CalibrationStereoFrameSet.STEREO_CORNER_COUNT_THRESHOLD;
        public int MaxFramesFromEachSensorBin { get; set; } = 2;  // Take top 2 frames from each sensor bin
        public int MaxFramesFromEachPoseBin { get; set; } = 4;    // Take top 4 frames from each pose bin
        // Action FLags
        public bool FindCalibrationBoardZone { get; set; } = true;
        public bool BuildTheFrameSets { get; set; } = true;
        public bool FindBestMonoFrames { get; set; } = true;
        public bool DoCalibrationMonoCalculations { get; set; } = true;
        public bool FindBestStereoFrames { get; set; } = true;
        public bool DoCalibrationStereoCalculations { get; set; } = true;
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

        // Output display (debug, info, warning, error messages)
        private bool displayOutput = false;


        private bool mediaFromCommandLine = false; // not sure whether to support this

        // Help menu documents
        private readonly HelpDocuments helpDocuments = new();

        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.CheckForOpenProjectAndCloseAsync(Boolean) which uses Json.NET serialization which may not be compatible with trimming.")]
        public MainWindow()
        {
            // Restore the saved window state
            PersistenceId = "MainWindow";
            MinHeight = 600;
            MinWidth = 800;

            InitializeComponent();

            // Event first before the app window is committed to closing (i.e. can be canceled)
            if (this.AppWindow is not null)
                this.AppWindow.Closing += AppWindow_Closing;

            // This is used to get/adjust the theme is necessary
            ThemeHelper.Initialize();

            // Inform the Reporter of the DispatcherQueue
            Report.SetDispatcherQueue(DispatcherQueue);

            // Set the Reporter to use for output messages
            LeftMonoCalibrationHead.SetReporter(Report);
            RightMonoCalibrationHead.SetReporter(Report);
            StereoCalibrationHead.SetReporter(Report);

            // Set theme (ThemeChanged calls SetTheme but performs some preparation first)
            var rootElement = (FrameworkElement)Content;
            if (SettingsManagerLocal.ApplicationTheme != ElementTheme.Default)
                rootElement.RequestedTheme = SettingsManagerLocal.ApplicationTheme;
            ApplyTheme(SettingsManagerLocal.ApplicationTheme);

            // Add listener for theme changes            
            rootElement.Loaded += MainWindow_Loaded;
            rootElement.ActualThemeChanged += OnActualThemeChanged;

            // Allows the menu bar to extend into the title bar
            // Assumes "this" is a XAML Window. In projects that don't use 
            // WinUI 3 1.3 or later, use inter-op APIs to get the AppWindow.           
            AppTitleBar.Loaded += AppTitleBar_Loaded;
            AppTitleBar.SizeChanged += AppTitleBar_SizeChanged;
            ExtendsContentIntoTitleBar = true;

            // Wire up to the ProcessingInfoBar the external TextBlock and ProgressRing controls in the title bar
            InfoBarProcessing.WireUpElapsedTimeUIControl(ElapsedProcessingTime, TitleProgressRing);

            // Update the Recent open surveys sub menu
            UpdateRecentProjectsMenu();

            /////////////////////////////////////
            ///May want this code
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
            //
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
            //
            //if (mediaFromCommandLine)
            //    _ = OpenMedia(calibProject, true/*forceUsdCacheIfAvalable*/, AppLaunchArgs.RunWithoutPrompts/*noPrompts*/);


            // Setup any documents on the help menu
            _ = helpDocuments.InitializeAsync(MenuHelp.Items, // Pass the MenuFlyoutSubItem directly instead of its Items property
                                              HelpDocumentsPDFSection,
                                              HelpDocumentsVideosSection,
                                              HelpDocumentsDOCSection,
                                              HelpDocumentsXLSSection);

            SetUIControls();

            Report.Info("", "App Started");
        }


        /// <summary>
        /// Set the theme of the application
        /// </summary>
        /// <param name="theme">Dark or Light</param>
        public void SetTheme(ElementTheme theme)
        {
            var rootElement = (FrameworkElement)(Content);

            // Always set RequestedTheme to reflect the chosen mode
            rootElement.RequestedTheme = theme;

            // Derive a concrete theme to choose icons/caption colors
            ElementTheme themeToApply = theme;
            if (theme == ElementTheme.Default)
            {
                var colorMode = TitleBarHelper.ApplySystemThemeToCaptionButtons(this) == Colors.White ? "Dark" : "Light";
                themeToApply = colorMode == "Dark" ? ElementTheme.Dark : ElementTheme.Light;
            }

            // Update title bar icon/caption buttons
            if (themeToApply == ElementTheme.Dark)
            {
                TitleBarIcon.Source = new BitmapImage(new Uri($"ms-appx:///Assets/SurveyorCalibration-Dark.png"));
                TitleBarHelper.SetCaptionButtonColors(this, Colors.White);
            }
            else
            {
                TitleBarIcon.Source = new BitmapImage(new Uri($"ms-appx:///Assets/SurveyorCalibration-Light.png"));
                TitleBarHelper.SetCaptionButtonColors(this, Colors.Black);
            }

            // Propagate theme to child heads
            LeftMonoCalibrationHead.SetTheme(themeToApply);
            RightMonoCalibrationHead.SetTheme(themeToApply);
            StereoCalibrationHead.SetTheme(themeToApply);

            UIHelper.AnnounceActionForAccessibility(rootElement, "Theme changed", "ThemeChangedNotificationActivityId");
        }
        //public void SetTheme(ElementTheme theme)
        //{
        //    ElementTheme themeToApply = ElementTheme.Default;

        //    var rootElement = (FrameworkElement)(Content);

        //    // If the app settings are controlling the theme 
        //    // then set the theme directly
        //    if (theme == ElementTheme.Dark)
        //    {
        //        // Set the RequestedTheme of the root element to Dark
        //        rootElement.RequestedTheme = ElementTheme.Dark;

        //        themeToApply = ElementTheme.Dark;
        //    }
        //    else if (theme == ElementTheme.Light)
        //    {
        //        // Set the RequestedTheme of the root element to Light
        //        rootElement.RequestedTheme = ElementTheme.Light;

        //        themeToApply = ElementTheme.Light;
        //    }

        //    // If we are using the system theme, determine what that is
        //    // so we can set our app icon and caption button colors appropriately
        //    if (theme == ElementTheme.Default)
        //    {
        //        // Get the background color used by that theme
        //        var color = TitleBarHelper.ApplySystemThemeToCaptionButtons(this) == Colors.White ? "Dark" : "Light";

        //        // Based on the background color, select a suitable application icon
        //        if (color == "Dark")
        //            themeToApply = ElementTheme.Dark;
        //        else
        //            themeToApply = ElementTheme.Light;
        //    }

        //    if (themeToApply == ElementTheme.Dark)
        //    {
        //        // Use a dark theme icon
        //        var bitmapImage = new BitmapImage(new Uri($"ms-appx:///Assets/SurveyorCalibration-Dark.png"));
        //        TitleBarIcon.Source = bitmapImage;

        //        TitleBarHelper.SetCaptionButtonColors(this, Colors.White);
        //    }
        //    else if (themeToApply == ElementTheme.Light)
        //    {
        //        // Use a light theme icon
        //        var bitmapImage = new BitmapImage(new Uri($"ms-appx:///Assets/SurveyorCalibration-Light.png"));
        //        TitleBarIcon.Source = bitmapImage;

        //        TitleBarHelper.SetCaptionButtonColors(this, Colors.Black);
        //    }

        //    // Inform child controls
        //    LeftMonoCalibrationHead.SetTheme(themeToApply);
        //    RightMonoCalibrationHead.SetTheme(themeToApply);
        //    StereoCalibrationHead.SetTheme(themeToApply);

        //    // If the theme has changed, announce the change to the user
        //    UIHelper.AnnounceActionForAccessibility(rootElement, "Theme changed", "ThemeChangedNotificationActivityId");
        //}


        /// <summary>
        /// Check if a cached results file exists for the current media set
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
                        if (Controls.UniversalCalibrationHeadUserControl.CachedResultsFileExists(cache.StereoFrameSetCacheFileSpec) &&
                            Controls.UniversalCalibrationHeadUserControl.CachedResultsFileExists(cache.LeftMonoFrameSetCacheFileSpec) &&
                            Controls.UniversalCalibrationHeadUserControl.CachedResultsFileExists(cache.RightMonoFrameSetCacheFileSpec))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        if (Controls.UniversalCalibrationHeadUserControl.CachedResultsFileExists(cache.StereoFrameSetCacheFileSpec) &&
                            Controls.UniversalCalibrationHeadUserControl.CachedResultsFileExists(cache.LeftMonoFrameSetCacheFileSpec) &&
                            Controls.UniversalCalibrationHeadUserControl.CachedResultsFileExists(cache.RightMonoFrameSetCacheFileSpec))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        if (Controls.UniversalCalibrationHeadUserControl.CachedResultsFileExists(cache.LeftMonoFrameSetCacheFileSpec) &&
                            Controls.UniversalCalibrationHeadUserControl.CachedResultsFileExists(cache.RightMonoFrameSetCacheFileSpec))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        if (Controls.UniversalCalibrationHeadUserControl.CachedResultsFileExists(cache.LeftMonoFrameSetCacheFileSpec))
                        {
                            cachedResultsAvailable = true;
                        }
                        break;
                }
            }

            return cachedResultsAvailable;
        }

        enum CachedResultCheckType
        {
            CalibrationBoardZone,
            FrameSets,
            BestMonoFrames,
            MonoCalibrationCalcs,
            BestStereoFrames,
            StereoCalibrationCalcs
        }
        /// <summary>
        /// Check whether the loaded cached results are available for
        /// the requested type
        /// </summary>
        /// <param name="cachedResultCheckType"></param>
        /// <returns></returns>
        private bool CheckIfCacheResultAvailable(CachedResultCheckType cachedResultCheckType)
        {
            bool ret = false;

            if (calibProject is not null)
            {
                bool doLeftMono = false;
                bool doRightMono = false;
                bool doStereo = false;

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
                        }
                        break;

                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        if (StereoCalibrationHead.IsOpen() &&
                            (bool)StereoCalibrationHead.IsStereoLocked()! &&
                            LeftMonoCalibrationHead.IsOpen() &&
                            RightMonoCalibrationHead.IsOpen())
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

                CalibProject.DataClass.CacheClass cache = calibProject.Data.Cache;
                switch (cachedResultCheckType)
                {
                    case CachedResultCheckType.CalibrationBoardZone:
                        ret = true;
                        if (doLeftMono && !LeftMonoCalibrationHead.IsCalibrationBoardZoneSetup())
                            ret = false;
                        if (doRightMono && !RightMonoCalibrationHead.IsCalibrationBoardZoneSetup())
                            ret = false;
                        if (doStereo && !StereoCalibrationHead.IsCalibrationBoardZoneSetup())
                            ret = false;
                        break;
                    case CachedResultCheckType.FrameSets:
                        ret = true;
                        if (doLeftMono && !LeftMonoCalibrationHead.IsFrameSetsSetup())
                            ret = false;
                        if (doRightMono && !RightMonoCalibrationHead.IsFrameSetsSetup())
                            ret = false;
                        if (doStereo && !StereoCalibrationHead.IsFrameSetsSetup())
                            ret = false;
                        break;
                    case CachedResultCheckType.BestMonoFrames:
                        ret = true;
                        if (doLeftMono && !LeftMonoCalibrationHead.IsBestFramesSetup())
                            ret = false;
                        if (doRightMono && !RightMonoCalibrationHead.IsBestFramesSetup())
                            ret = false;
                        break;
                    case CachedResultCheckType.MonoCalibrationCalcs:
                        ret = true;
                        if (doLeftMono && !IsMonoCalibrationCalculationsSetup(calibProject, true/*left*/))
                            ret = false;
                        if (doRightMono && !IsMonoCalibrationCalculationsSetup(calibProject, false/*right*/))
                            ret = false;
                        break;
                    case CachedResultCheckType.BestStereoFrames:
                        ret = true;
                        if (doStereo && !StereoCalibrationHead.IsBestFramesSetup())
                            ret = false;
                        break;
                    case CachedResultCheckType.StereoCalibrationCalcs:
                        ret = true;
                        if (doStereo && !IsStereoCalibrationCalculationsSetup(calibProject))
                            ret = false;
                        break;
                }
            }

            return ret;
        }


        /// <summary>
        /// Run the calibration as defined in the SetupRunCalibration pages
        /// This method is called once the user presses the Run Calibration button
        /// in the SetupRunCalibrationSummary page. This is why it is public
        /// </summary>
        /// <returns></returns>


        [RequiresUnreferencedCode("ProjectSave uses Json.NET serialization which may not be compatible with trimming.")]
        public async Task RunCalibrationAsync(RunCalibrationParams runCalibrationParams)
        {
            int ret = 0;

            if (calibProject is not null)
            {
                // Prime the board for EMGU.CV API use
                if (calibProject.Data.ChArUcoBoardDefinition.Setup())
                {
                    // Load the calibration board type
                    StereoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.ChArUcoBoardDefinition);
                    LeftMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.ChArUcoBoardDefinition);
                    RightMonoCalibrationHead.SetupCalibrationBoardType(calibProject.Data.ChArUcoBoardDefinition);
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Board Setup Failed",
                        Content = "Failed to setup the calibration board definition",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }

                // Clear all the calibration results
                StereoCalibrationHead.ClearCalibrationResultsDisplay();
                LeftMonoCalibrationHead.ClearCalibrationResultsDisplay();
                RightMonoCalibrationHead.ClearCalibrationResultsDisplay();

                // Find where in the .MP4 the calibration board starts and stops
                if (runCalibrationParams.FindCalibrationBoardZone)
                {
                    ret = await FindCalibrationBoardZoneAllHeadsAsync();
                }

                // Build the frame sets by finding calibration targets in all frames
                if (ret == 0 && runCalibrationParams.BuildTheFrameSets)
                {
                    SetAppModeOnAllHeads(AppMode.FindCalibrationsFrames);

                    ret = await BuildFrameSetsAllHeadsAsync();
                }

                // Identify the best mono frames from the frame sets 
                if (ret == 0 && runCalibrationParams.FindBestMonoFrames)
                {
                    SetAppModeOnAllHeads(AppMode.BestFramesCalc);

                    ret = await FindBestMonoFramesAllHeadsAsync(runCalibrationParams);
                }

                // Do the calibration mono calculations
                if (ret == 0 && runCalibrationParams.DoCalibrationMonoCalculations)
                {
                    SetAppModeOnAllHeads(AppMode.BestFramesCalc);

                    ret = await DoCalibrationMonoCalcsAllHeadsAsync(runCalibrationParams);

                    if (ret == 0)
                    {
                        if (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoPairOnlyMediaSet ||
                            calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoSingleOnlyMediaSet)
                        {
                            // Mono calibration only so swap to best frames view 
                            SetAppModeOnAllHeads(AppMode.BestFramesView);
                        }
                    }
                    else
                        SetAppModeOnAllHeads(AppMode.Open); // Problem
                }

                // Identify the best stereo frames from the frame sets 
                if (ret == 0 && runCalibrationParams.FindBestStereoFrames)
                {
                    SetAppModeOnAllHeads(AppMode.BestFramesCalc);

                    ret = await FindBestStereoFramesAsync(runCalibrationParams);
                }

                // Do the calibration calculations
                if (ret == 0 && runCalibrationParams.DoCalibrationStereoCalculations)
                {
                    SetAppModeOnAllHeads(AppMode.BestFramesCalc);

                    ret = await DoCalibrationStereoCalcsAsync(runCalibrationParams);

                    if (ret == 0)
                        SetAppModeOnAllHeads(AppMode.BestFramesView);
                    else
                        SetAppModeOnAllHeads(AppMode.Open); // Problem
                }

                // Save the best frames images to disk if requested
                if (ret == 0 && runCalibrationParams.SaveBestFrames)
                {
                    // Find the best frames and do the calibration calculation
                    ret = await SaveBestFramesAllHeadsAsync();
                }

                if (ret == 0)
                {
                    if (calibProject.IsCalibrationReady &&
                        SettingsManagerLocal.TeachingTipsEnabled &&
                        !SettingsManagerLocal.HasTeachingTipBeenShown("MenuExport"))
                    {
                        MenuExportTeachingTip.IsOpen = true;
                    }
                }

                // Make sure the appMode/viewMode are up-to-update (they exist
                // at both the MainWindow level and in each Head
                // Switch view mode to best frames if possible
                if (IsViewModeAvailableOnAllHeads(ViewMode.BestFrames))
                {
                    // If the calibration worked
                    SetViewModeOnAllHeads(ViewMode.BestFrames);
                }
                else if (IsViewModeAvailableOnAllHeads(ViewMode.AllFrames))
                {
                    // If the calibration didn't work
                    SetViewModeOnAllHeads(ViewMode.AllFrames);
                }

                // Save project if necessary
                if (calibProject.IsDirty)
                {
                    calibProject.ProjectSave();
                }
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
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.AppWindowClosingAsync(AppWindowClosingEventArgs) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private void AppWindow_Closing(object sender, AppWindowClosingEventArgs e) => _ = AppWindowClosingAsync(e);

        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.CheckForOpenProjectAndCloseAsync(Boolean) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
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
        /// the title bar area which allowed the menu bar (which is on the title bar) to operate
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
        /// the title bar area which allowed the menu bar (which is on the title bar) to operate
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
            ApplyTheme(sender.ActualTheme);
        }

        private void ApplyTheme(ElementTheme newTheme)
        {
            Debug.WriteLine($"Theme changed to {newTheme}");

            // Apply additional changes
            SetTheme(newTheme);

            // Persist exactly what was requested so settings are consistent
            SettingsManagerLocal.ApplicationTheme = newTheme;
        }


        /// <summary>
        /// Create a new calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileProjectNewAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private void FileProjectNew_Click(object sender, RoutedEventArgs e) => _ = FileProjectNewAsync();

        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.SaveAsProjectAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task FileProjectNewAsync()
        {
            // Reset Title
            SetTitle("");

            // Load the Info and Media user control to setup the project
            CalibrationMediaUserControl.SetupForContentDialog(CalibrationMediaContentDialog);

            // ** Important notes **
            // The UserControl CalibrationMediaContentDialog is displayed within a ContentDialog for 
            // the purpose of setting up a new project (also using from a SettingsCard)
            // I struggled to get the ContentDialog to show width necessary to fully display
            // the UserControl.  The solution was to:
            // Set <x:Double x:Key="ContentDialogMaxWidth">1200</x:Double> in the <ResourceDictionary>
            // to setup the ContentDialog in XAML in MainWindow and place it in Grid.Row=2.
            // This took a lot of trial and error. It seems to effect the title bar is left in
            // default row zero.
            ContentDialogResult result = await CalibrationMediaContentDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                calibProject = new(Report);

                CalibrationMediaUserControl.SaveForContentDialog(calibProject);

                // Setup display for selected StereoMonoMediaSetMode
                SetupMainWindowForStereoMonoMediaSetMode(calibProject.Data.Media.StereoMonoMediaSetMode);

               
                // Save the calibration project file
                if (await SaveAsProjectAsync() == 0)
                {
                    // Open the media
                    bool ret = await OpenMediaSetsAsync(calibProject, false/*forceUsdCacheIfAvalable*/, false/*noPrompts*/);

                    if (ret)
                    {
                        // If it is a mono calibration show 'Run Calibration' teaching tip next set if required
                        if (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoPairOnlyMediaSet ||
                            calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoSingleOnlyMediaSet)
                        {
                            if (SettingsManagerLocal.TeachingTipsEnabled &&
                                !SettingsManagerLocal.HasTeachingTipBeenShown("MenuRunCalibration"))
                            {
                                MenuRunCalibrationTeachingTip.IsOpen = true;
                            }
                        }
                        // If it is a stereo calibration show 'Sync stereo videos' teaching tip next set if required
                        else if (calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                                 calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet)
                        {
                            if (SettingsManagerLocal.TeachingTipsEnabled &&
                                !SettingsManagerLocal.HasTeachingTipBeenShown("SyncStereoVideos"))
                            {
                                SyncStereoVideosTeachingTip.IsOpen = true;
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Open a calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileProjectOpenClickAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private void FileProjectOpen_Click(object sender, RoutedEventArgs e) => _ = FileProjectOpenClickAsync();

        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.OpenProjectAsync(String) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
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
                    InfoBarProcessing.ShowProcessing("Opening calibration project...", true/*show elapsed time*/);

                    int ret = await OpenProjectAsync(file.Path);

                    InfoBarProcessing.HideProcessing();

                    if (ret == 0)
                    {
                        // Setup display for selected StereoMonoMediaSetMode
                        if (calibProject is not null)
                            SetupMainWindowForStereoMonoMediaSetMode(calibProject.Data.Media.StereoMonoMediaSetMode);

                        // Add to Recent Projects list
                        AddToRecentProjects(file.Path);
                        UpdateRecentProjectsMenu();
                    }
                    else
                    {
                        Debug.WriteLine($"FileProjectOpenClickAsync: OpenProjectAsync() failed, survey path:{file.Path}, return code = {ret}");
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
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileProjectSaveClickAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private void FileProjectSave_Click(object sender, RoutedEventArgs e) => _ = FileProjectSaveClickAsync();

        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileProjectSaveOrSaveAsAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task FileProjectSaveClickAsync()
        {
            await FileProjectSaveOrSaveAsAsync();
        }


        /// <summary>
        /// Save the open calibration under a new file name
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileProjectSaveAsClickAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private void FileProjectSaveAs_Click(object sender, RoutedEventArgs e) => _ = FileProjectSaveAsClickAsync();

        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileProjectSaveOrSaveAsAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task FileProjectSaveAsClickAsync()
        {
            await FileProjectSaveOrSaveAsAsync();
        }


        /// <summary>
        /// Close the open calibration project
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileProjectCloseClickAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private void FileProjectClose_Click(object sender, RoutedEventArgs e) => _ = FileProjectCloseClickAsync();

        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.CheckForOpenProjectAndCloseAsync(Boolean) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
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
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileRecentProjectClickAsync(Object) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private void FileRecentProject_Click(object sender, RoutedEventArgs e) => _ = FileRecentProjectClickAsync(sender);

        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.OpenProjectAsync(String) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
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
                        InfoBarProcessing.ShowProcessing("Opening calibration project...", true/*show elapsed time*/);

                        int ret = await OpenProjectAsync(filePath);

                        InfoBarProcessing.HideProcessing();

                        if (ret == 0)
                        {
                            // Setup display for selected StereoMonoMediaSetMode
                            if (calibProject is not null)
                                SetupMainWindowForStereoMonoMediaSetMode(calibProject.Data.Media.StereoMonoMediaSetMode);

                            // Force to the top of the recent projects list
                            // Note this project is definitely in the recent project list
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
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileLockUnlockMediaPlayersAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private void FileLockUnlockMediaPlayers_Click(object sender, RoutedEventArgs e) => _ = FileLockUnlockMediaPlayersAsync();

        [RequiresUnreferencedCode("ProjectSave uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task FileLockUnlockMediaPlayersAsync()
        {
            if (calibProject is not null)
            {
                if (calibProject.Data.Sync.IsSynchronized)
                {
                    await LockUnlockMediaPlayersAsync(false/*lockTrueUnLockFalse*/);

                    // Save the locked state
                    calibProject.ProjectSave();
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
        private void FileExport_Click(object sender, RoutedEventArgs e) => _ = FileExportAsync();
        private async Task FileExportAsync()
        {
            if (calibProject is not null)
            {
                // Load the Info and Media user control to setup the project
                ExportUserControl.SetupForContentDialog(ExportUserControlDialog, calibProject);

                // ** Important notes **
                // The UserControl ExportUserControlDialog is displayed within a ContentDialog for 
                // the purpose of setting up a new project (also using from a SettingsCard)
                // I struggled to get the ContentDialog to show width necessary to fully display
                // the UserControl.  The solution was to:
                // Set <x:Double x:Key="ContentDialogMaxWidth">1200</x:Double> in the <ResourceDictionary>
                // to setup the ContentDialog in XAML in MainWindow and place it in Grid.Row=2.
                // This took a lot of trial and error. It seems to effect the title bar is left in
                // default row zero.
                ContentDialogResult result = await ExportUserControlDialog.ShowAsync();

                //???await ExportUserControl.ShowExportDialogAsync(calibProject, this.Content.XamlRoot);
            }
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
        /// Keyboard accelerator for testing code
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
        /// Handles the action button click event for the SyncStereoVideosTeachingTip.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void SyncStereoVideosTeachingTip_ActionButtonClick(TeachingTip sender, object args)
        {
            SyncStereoVideosTeachingTip.IsOpen = false;
            SettingsManagerLocal.SetTeachingTipShown("SyncStereoVideos");
        }


        /// <summary>
        /// Handles the action button click event for the MenuRunCalibrationTeachingTip.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MenuRunCalibrationTeachingTip_ActionButtonClick(TeachingTip sender, object args)
        {
            MenuRunCalibrationTeachingTip.IsOpen = false;
            SettingsManagerLocal.SetTeachingTipShown("MenuRunCalibration");
        }

        /// <summary>
        /// Handles the action button click event for the MenuExportTeachingTip.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MenuExportTeachingTip_ActionButtonClick(TeachingTip sender, object args)
        {
            MenuExportTeachingTip.IsOpen = false;
            SettingsManagerLocal.SetTeachingTipShown("MenuExport");
        }

        /// <summary>
        /// Handles the click event for the InfoBar "Lock Media" button.
        /// </summary>
        /// <param name="sender">The source of the event, typically the button that was clicked.</param>
        /// <param name="e">The event data associated with the click event.</param>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.InfoBarLockMediaButtonAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private void InfoBarLockMediaButton_Click(object sender, RoutedEventArgs e) => _ = InfoBarLockMediaButtonAsync();
        [RequiresUnreferencedCode("ProjectSave uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task InfoBarLockMediaButtonAsync()
        {
            await LockUnlockMediaPlayersAsync(true/*lockTrueUnLockFalse*/);

            // Save the locked state
            calibProject?.ProjectSave();

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


        /// <summary>
        /// Set view mode to seeing all the frames in the video(s)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileViewAllFrames_Click(object sender, RoutedEventArgs e)
        {
            bool usedInfoBarProcessing = false;
            // This can be a slow change to AllFrame so display 
            if (!InfoBarProcessing.IsOpen)
            {
                InfoBarProcessing.ShowProcessing("Swapping to view all frames...", true);
                usedInfoBarProcessing = true;
            }

            SetViewModeOnAllHeads(ViewMode.AllFrames);
            
            if (usedInfoBarProcessing)
                InfoBarProcessing.HideProcessing();

            SetUIControls();
        }


        /// <summary>
        /// Set view mode to seeing the best frames in the video(s)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileViewBestFrames_Click(object sender, RoutedEventArgs e)
        {
            SetViewModeOnAllHeads(ViewMode.BestFrames);
            SetUIControls();
        }


        /// <summary>
        /// Set view mode to seeing the filtered frames in the video(s)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileViewFilterFrames_Click(object sender, RoutedEventArgs e)
        {
            SetViewModeOnAllHeads(ViewMode.FilterFrames);
            SetUIControls();
        }


        /// <summary>
        /// Set view mode to see the sensor coverage in each video(s)
        /// This is a single image showing an outline of all the calibration
        /// markers detected in each frame overlaid on each other to give an indication
        /// of the sensor coverage.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileViewSensorCoverage_Click(object sender, RoutedEventArgs e)
        {
            SetViewModeOnAllHeads(ViewMode.SensorCoverage);
            SetUIControls();
        }

        /// <summary>
        /// Toggle the output pane visibility
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ViewOutput_Click(object sender, RoutedEventArgs e)
        {
            if (displayOutput)
            {
                // Switch off display Output

                // Take the tick off the View>Output menu item
                MenuViewOutput.IsChecked = false;

                // Hide the Reporter panel and it's splitter
                Report.Visibility = Visibility.Collapsed;
                OutputSplitter.Visibility = Visibility.Collapsed;

                // Hide output area
                OutputSplitterRow.Height = new GridLength(0);
                OutputRow.Height = new GridLength(0);

                // Mark display output as off
                displayOutput = false;
            }
            else
            {
                // Switch on display Output

                // Show the Reporter panel and it's splitter
                Report.Visibility = Visibility.Visible;
                OutputSplitter.Visibility = Visibility.Visible;

                // Show output area: Row6 gets 14% of the variable space, Row5 is the splitter thickness
                OutputSplitterRow.Height = new GridLength(8);                          // splitter thickness
                OutputRow.Height = new GridLength(14, GridUnitType.Star);      // reporter

                // Put a tick on the View>Output menu item
                MenuViewOutput.IsChecked = true;

                // Mark display output as on
                displayOutput = true;
            }
        }






        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Open the calibration project files
        /// </summary>
        /// <param name="surveyFileName"></param>
        /// <returns>-999 If user aborts</returns>
        [RequiresUnreferencedCode("ProjectLoadAsync uses Json.NET serialization which may not be compatible with trimming.")]     
        private async Task<int> OpenProjectAsync(string projectFileSpec)
        {
            int ret = 0;

            if (calibProject is null)
            {
                calibProject ??= new CalibProject(Report);
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

                        // Load caches if available
                        if (await LoadFrameDataCachesAsync(false/*noPrompts*/) == true)
                        {
                            Debug.WriteLine("Frame Set Caches Loaded");
                        }

                        Debug.WriteLine($"Project {projectFileSpec} Loaded");
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
        /// Check that media files list exist in case they have been renamed, moved or deleted.
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
                                " Press 'OK' to try to find the file. Press 'Cancel' to stop loading the project";
                        else
                            message = $"The {mediaType} media file '{fileName}' does not exist." +
                                " Press 'OK' to try to find the file. Press 'Cancel' to stop loading the project";

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
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.SaveAsProjectAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
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
        [RequiresUnreferencedCode("Calls Surveyor.CalibProject.ProjectSaveAsAsync(String)")]
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
                        // Save the calibration project data to the file
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
        /// Used to pick a .calproj file to save the calibration project
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

                InfoBarProcessing.ShowProcessing("Opening Media...", true/*display elapsed time*/);
                SetAppModeOnAllHeads(AppMode.Open);

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
                        openLeftMono = true;
                        openRightMono = true;
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
                                                                         calibProject.Data.Sync.SyncFrameIndexRight);
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
                // Allow operations to settle
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
                        closeLeftMono = true;
                        closeRightMono = true;
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

                    SetAppModeOnAllHeads(AppMode.Close);
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
        /// <returns>true is OK to proceed (i.e. no project now open)</returns>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileProjectSaveOrSaveAsAsync() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
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
                        // Create a FontIcon using the Fluent Icons font
                        var warningIcon = new FontIcon
                        {
                            Glyph = "\uE814", // Unicode character for a warning icon in Fluent Icons
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
                                    Text = "Before you close this survey, do you want to save your changes?\n\nPress 'Yes' to save the existing survey, 'No' to close without saving",
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
                        // If the select Cancel the Close Survey request is canceled
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

                        // Close the StereoMediaController, clears the title and the sync indicator
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
        /// Open and load all the frames set caches
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns></returns>
        [RequiresUnreferencedCode("Calls Surveyor.Controls.UniversalCalibrationHeadUserControl.LoadCachedResultsAsync(String) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
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
                    bool loaded = false;
                    bool stereoFramesLoad = false;
                    bool leftMonoFramesLoad = false;
                    bool rightMonoFramesLoad = false;

                    // Load cached results
                    CalibProject.DataClass.CacheClass cache = calibProject.Data.Cache;

                    switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                    {
                        case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                            stereoFramesLoad = true;
                            leftMonoFramesLoad = true;
                            rightMonoFramesLoad = true;
                            break;

                        case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                            stereoFramesLoad = true;
                            leftMonoFramesLoad = true;
                            rightMonoFramesLoad = true;
                            break;

                        case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                            leftMonoFramesLoad = true;
                            rightMonoFramesLoad = true;
                            break;

                        case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                            leftMonoFramesLoad = true;
                            break;
                    }

                    var tasks = new List<Task>();
                    int taskIndex = 0;
                    int taskStereoIndex = -1;
                    int taskLeftMonoIndex = -1;
                    int taskRightMonoIndex = -1;
                    Stopwatch sw = new();
                    sw.Start();

                    if (stereoFramesLoad)
                    {
                        tasks.Add(StereoCalibrationHead.LoadCachedResultsAsync(cache.StereoFrameSetCacheFileSpec));
                        taskStereoIndex = taskIndex++;
                    }
                    if (leftMonoFramesLoad)
                    {
                        tasks.Add(LeftMonoCalibrationHead.LoadCachedResultsAsync(cache.LeftMonoFrameSetCacheFileSpec));
                        taskLeftMonoIndex = taskIndex++;
                    }
                    if (rightMonoFramesLoad)
                    {
                        tasks.Add(RightMonoCalibrationHead.LoadCachedResultsAsync(cache.RightMonoFrameSetCacheFileSpec));
                        taskRightMonoIndex = taskIndex++;
                    }

                    int stereoFramesLoaded = 0;
                    int leftMonoFramesLoaded = 0;
                    int rightMonoFramesLoaded = 0;

                    try
                    {
                        // Run all finds in parallel, but still observe completion and exceptions                       
                        await Task.WhenAll(tasks);
                        sw.Stop();

                        // Timings
                        Debug.WriteLine($"Cache load time total:{sw.ElapsedMilliseconds}m/s");

                        // Get result of each task
                        if (stereoFramesLoad)
                            stereoFramesLoaded = await ((Task<int>)tasks[taskStereoIndex]);
                        if (leftMonoFramesLoad)
                            leftMonoFramesLoaded = await ((Task<int>)tasks[taskLeftMonoIndex]);
                        if (rightMonoFramesLoad)
                            rightMonoFramesLoaded = await ((Task<int>)tasks[taskRightMonoIndex]);

                        // Check if any failed
                        if ((stereoFramesLoad && stereoFramesLoaded == 0) ||
                            (leftMonoFramesLoad && leftMonoFramesLoaded == 0) ||
                            (rightMonoFramesLoad && rightMonoFramesLoaded == 0))
                        {
                            Debug.WriteLine($"LoadFrameDataCachesAsync: Error reading files: Stereo Result={stereoFramesLoaded}, Left Mono Result={leftMonoFramesLoaded}, Right Mono Result={rightMonoFramesLoaded}");
                        }
                        else
                        {
                            // Success
                            loaded = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle/log properly
                        Debug.WriteLine($"Error in OpenMediaSetsAsync: {ex}");
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
                                    contentText = $"The cached results were not loaded or are incomplete\n\n" +
                                        $"   Stereo Frames Loaded: {stereoFramesLoaded}\n" +
                                        $"   Left Mono Frames Loaded: {leftMonoFramesLoaded}\n" +
                                        $"   Right Mono Frames Loaded: {rightMonoFramesLoaded}\n";
                                    warn = true;
                                }
                                break;

                            case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                                if (stereoFramesLoaded == 0)
                                {
                                    contentText = $"The cached results were not loaded or are incomplete\n\n" +
                                        $"   Stereo Frames Loaded: {stereoFramesLoaded}\n" +
                                        $"   Left Mono Frames Loaded: {leftMonoFramesLoaded}\n" +
                                        $"   Right Mono Frames Loaded: {rightMonoFramesLoaded}\n";
                                    warn = true;
                                }
                                break;

                            case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                                if (leftMonoFramesLoaded == 0 || rightMonoFramesLoaded == 0)
                                {
                                    contentText = $"The cached results were not loaded or are incomplete\n\n" +
                                        $"   Left Mono Frames Loaded: {leftMonoFramesLoaded}\n" +
                                        $"   Right Mono Frames Loaded: {rightMonoFramesLoaded}\n";
                                    warn = true;
                                }
                                break;

                            case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                                if (leftMonoFramesLoaded == 0)
                                {
                                    contentText = $"The cached results were not loaded or are incomplete\n\n" +
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
                                    Title = "Error loading cached results. However, all results can be recreated via File > Run Calibration",
                                    Content = contentText,
                                    CloseButtonText = "OK",
                                    XamlRoot = this.Content.XamlRoot // Set the XamlRoot for proper display
                                };
                                await errorDialog.ShowAsync();
                            }
                        }
                    }

                    // Display calibration result
                    switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                    {
                        case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                            StereoCalibrationHead.DisplayCalibrationInfoSafeUI(calibProject, null);
                            LeftMonoCalibrationHead.DisplayCalibrationInfoSafeUI(calibProject, true/*trueLeftFalseRightNullStereo*/);
                            RightMonoCalibrationHead.DisplayCalibrationInfoSafeUI(calibProject, false/*trueLeftFalseRightNullStereo*/);
                            break;
                        case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                            StereoCalibrationHead.DisplayCalibrationInfoSafeUI(calibProject, null);
                            break;
                        case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                            LeftMonoCalibrationHead.DisplayCalibrationInfoSafeUI(calibProject, true/*trueLeftFalseRightNullStereo*/);
                            RightMonoCalibrationHead.DisplayCalibrationInfoSafeUI(calibProject, false/*trueLeftFalseRightNullStereo*/);
                            break;
                        case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                            LeftMonoCalibrationHead.DisplayCalibrationInfoSafeUI(calibProject, true/*trueLeftFalseRightNullStereo*/);
                            break;
                    }

                    // Check if error loading
                    if (loaded)
                    {
                        // We have best frame available
                        SetAppModeOnAllHeads(AppMode.BestFramesView);

                        // Switch view mode to best frames if possible
                        if (IsViewModeAvailableOnAllHeads(ViewMode.BestFrames))
                        {
                            SetViewModeOnAllHeads(ViewMode.BestFrames);
                        }

                        // Success
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
                                Content = "The cached results could not be loaded. However, all results can be recreated via File > Run Calibration",
                                CloseButtonText = "OK",
                                XamlRoot = this.Content.XamlRoot // Set the XamlRoot for proper display
                            };
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
                SetUIControls();
            }

            return ret;
        }

        /// <summary>
        /// Save all the media files for the calibration project
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns></returns>
        [RequiresUnreferencedCode("Calls Surveyor.Controls.UniversalCalibrationHeadUserControl.SaveCachedResults(String) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]

        private async Task<bool> SaveFrameDataCachesAsync(bool noPrompts)
        {
            bool ret = false;

            if (calibProject is null)
                return ret;

            try
            {
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
                            doLeftMono = true;
                            doRightMono = true;
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
                SetUIControls();
            }

            return ret;
        }


        /// <summary>
        /// First and last occurrence of the Calibration board in the .MP4
        /// This is so detailed (slow) analysis of the frames only occurs
        /// in the frames that really have a calibration board displayed
        /// </summary>
        /// <returns></returns>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.SaveFrameDataCachesAsync(Boolean) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task<int> FindCalibrationBoardZoneAllHeadsAsync()
        {
            int ret = 0;

            if (calibProject is null)
                return -2;

            try
            {
                var tasks = new List<Task>();
                InfoBarProcessing.ShowProcessing("Finding calibration board zone...");
                SetAppModeOnAllHeads(AppMode.FindCalibrationsFrames);

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
                            doLeftMono = true;
                            doRightMono = true;
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
                        tasks.Add(LeftMonoCalibrationHead.FindCalibrationBoardZoneAsync());
                        taskLeftMonoIndex = taskIndex++;
                    }
                    if (doRightMono)
                    {
                        tasks.Add(RightMonoCalibrationHead.FindCalibrationBoardZoneAsync());
                        taskRightMonoIndex = taskIndex++;
                    }
                    if (doStereo)
                    {
                        tasks.Add(StereoCalibrationHead.FindCalibrationBoardZoneAsync());
                        taskStereoIndex = taskIndex++;
                    }

                    if (tasks.Count == 0)
                        return 0;

                    // Your existing flags + timer
                    //???StartFindCheckTimer();

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
                        Debug.WriteLine($"Error in FindCalibrationBoardZoneAsync: {ex}");
                    }
                }
                else
                    ret = -3;

                // If there is an error then clear any collected
                // values and reset the display
                if (ret != 0)
                {
                    if (doLeftMono)
                    {
                        LeftMonoCalibrationHead.ClearResults(CalibrationStereoFrameSet.ClearRequest.StartStopCalibrationBoardZone);
                    }
                    if (doRightMono)
                    {
                        RightMonoCalibrationHead.ClearResults(CalibrationStereoFrameSet.ClearRequest.StartStopCalibrationBoardZone);
                    }
                    if (doStereo)
                    {
                        StereoCalibrationHead.ClearResults(CalibrationStereoFrameSet.ClearRequest.StartStopCalibrationBoardZone);
                    }
                }

                // Save the calibration board start/end zones
                InfoBarProcessing.UpdateMessage("Saving calibration board zone...");
                await SaveFrameDataCachesAsync(false/*no prompts*/);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FindCalibrationBoardZoneAllHeadsAsync: Exception:{ex.Message}");
            }
            finally
            {
                InfoBarProcessing.HideProcessing();
            }

            Debug.WriteLine($"FindCalibrationBoardZoneAllHeadsAsync Run Status  Left:{LeftMonoCalibrationHead.IsFindRunning()}  Right:{RightMonoCalibrationHead.IsFindRunning()}  Stereo:{StereoCalibrationHead.IsFindRunning()}");

            return ret;
        }


        /// <summary>
        /// This is core function. It orchestrates the finding of calibration 
        /// targets by the different UniversalCalibrationHeadUserControls.
        /// This is data on every frame in the media set. This can be time consuming
        /// and it is cached for future use.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.SaveFrameDataCachesAsync(Boolean) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task<int> BuildFrameSetsAllHeadsAsync()
        {
            int ret = 0;

            if (calibProject is null)
                return -2;

            try
            {
                var tasks = new List<Task>();
                InfoBarProcessing.ShowProcessing("Finding calibration frames...");              

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
                            doLeftMono = true;  // This is the same video as used in the left stereo
                            doRightMono = true; // This is the same video as used in the right stereo
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
                        tasks.Add(LeftMonoCalibrationHead.BuildFrameSetsAsync());
                        taskLeftMonoIndex = taskIndex++;
                    }
                    if (doRightMono)
                    {
                        tasks.Add(RightMonoCalibrationHead.BuildFrameSetsAsync());
                        taskRightMonoIndex = taskIndex++;
                    }
                    if (doStereo)
                    {
                        tasks.Add(StereoCalibrationHead.BuildFrameSetsAsync());
                        taskStereoIndex = taskIndex++;
                    }

                    if (tasks.Count == 0)
                        return 0;

                    // Your existing flags + timer
                    //???StartFindCheckTimer();

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
                            Debug.WriteLine($"Error BuildFrameSetsAsync: Left Mono Result={leftMonoResult}, Right Mono Result={rightMonoResult},Stereo Result={stereoResult}");

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
                        Debug.WriteLine($"Error in BuildFrameSetsAsync: {ex}");
                    }
                }
                else
                    ret = -3;


                // If there is an error then clear any collected
                // values and reset the display
                if (ret != 0)
                {
                    if (doLeftMono)
                    {
                        LeftMonoCalibrationHead.ClearResults(CalibrationStereoFrameSet.ClearRequest.FrameSets);
                    }
                    if (doRightMono)
                    {
                        RightMonoCalibrationHead.ClearResults(CalibrationStereoFrameSet.ClearRequest.FrameSets);
                    }
                    if (doStereo)
                    {
                        StereoCalibrationHead.ClearResults(CalibrationStereoFrameSet.ClearRequest.FrameSets);
                    }
                }

                // Save the frames dataset
                InfoBarProcessing.UpdateMessage("Saving frame sets...");
                await SaveFrameDataCachesAsync(false/*no prompts*/);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BuildFrameSetsAllHeadsAsync: Exception:{ex.Message}");
            }
            finally
            {
                InfoBarProcessing.HideProcessing();
            }

            Debug.WriteLine($"BuildFrameSetsAllHeadsAsync Run Status  Left:{LeftMonoCalibrationHead.IsFindRunning()}  Right:{RightMonoCalibrationHead.IsFindRunning()}  Stereo:{StereoCalibrationHead.IsFindRunning()}");

            return ret;
        }


        /// <summary>
        /// Asynchronously finds the best mono frames for all detected heads based on the specified calibration
        /// parameters.
        /// </summary>
        /// <param name="runParams">The calibration parameters to use when evaluating and selecting the best mono frames for each head. Cannot
        /// be <c>null</c>.</param>
        /// <returns>The task result contains the number of heads for which a
        /// best mono frame was successfully found.</returns>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.SaveFrameDataCachesAsync(Boolean) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task<int> FindBestMonoFramesAllHeadsAsync(RunCalibrationParams runParams)
        {
            int ret = 0;

            if (calibProject is null)
                return -1;

            InfoBarProcessing.ShowProcessing("Finding best mono frames...");
           
            bool doLeftMono = false;
            bool doRightMono = false;

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
                    }
                    break;
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()!)
                    {
                        doLeftMono = true;
                        doRightMono = true;
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

            var monoPhaseTasks = new List<Task<int>>();
            int taskIndex = 0;
            int taskLeftMonoIndex = -1;
            int taskRightMonoIndex = -1;

            // Find the best frames from the mono calibration frames 
            if (doLeftMono)
            {
                monoPhaseTasks.Add(Task.Run(() => LeftMonoCalibrationHead.FindBestMonoFramesSafeUIAsync(
                                                                calibProject,
                                                                true/*trueLeftFalseRight*/,
                                                                runParams.MovementFilterValue,
                                                                runParams.BlurFilterValue,
                                                                runParams.MonoCornersFilterValue,
                                                                runParams.MaxFramesFromEachSensorBin,
                                                                runParams.MaxFramesFromEachPoseBin)));
                taskLeftMonoIndex = taskIndex++;
            }

            if (doRightMono)
            {
                monoPhaseTasks.Add(Task.Run(() => RightMonoCalibrationHead.FindBestMonoFramesSafeUIAsync(
                                                                calibProject,
                                                                false/*trueLeftFalseRight*/,
                                                                runParams.MovementFilterValue,
                                                                runParams.BlurFilterValue,
                                                                runParams.MonoCornersFilterValue,
                                                                runParams.MaxFramesFromEachSensorBin,
                                                                runParams.MaxFramesFromEachPoseBin)));
                taskRightMonoIndex = taskIndex++;
            }

            // Find the best frames in parallel
            if (monoPhaseTasks.Count > 0)
            {
                try
                {
                    int[] results = await Task.WhenAll(monoPhaseTasks);

                    if (doLeftMono)
                    {
                        if (results[taskLeftMonoIndex] != 0)
                        {
                            ret = results[taskLeftMonoIndex];
                            Debug.WriteLine($"FindBestMonoFramesAllHeadsAsync: Error from FindBestFramesSafeUIAsync: Left Mono Result={ret}");
                        }
                    }
                    if (doRightMono)
                    {
                        if (results[taskRightMonoIndex] != 0)
                        {
                            ret = results[taskRightMonoIndex];
                            Debug.WriteLine($"FindBestMonoFramesAllHeadsAsync: Error from FindBestFramesSafeUIAsync: Right Mono Result={ret}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FindBestMonoFramesAllHeadsAsync: Error running FindBestFramesSafeUIAsync tasks, {ex}");
                    ret = -2;
                }


                // If there is an error then clear any collected
                // values and reset the display
                if (ret != 0)
                {
                    if (doLeftMono)
                    {
                        LeftMonoCalibrationHead.ClearResults(CalibrationStereoFrameSet.ClearRequest.BestFrames);
                    }
                    if (doRightMono)
                    {
                        RightMonoCalibrationHead.ClearResults(CalibrationStereoFrameSet.ClearRequest.BestFrames);
                    }
                }

                // Save the frames dataset
                InfoBarProcessing.UpdateMessage("Saving after stereo phase...");
                await SaveFrameDataCachesAsync(false/*no prompts*/);
            }

            InfoBarProcessing.HideProcessing();

            SetUIControls();

            Debug.WriteLine($"FindBestMonoFramesAllHeadsAsync Run Status  Left:{LeftMonoCalibrationHead.IsFindRunning()}  Right:{RightMonoCalibrationHead.IsFindRunning()}");

            return ret;
        }


        /// <summary>
        /// Using the best mono frames, perform the mono calibration calculations on all heads
        /// </summary>
        /// <param name="runParams"></param>
        /// <returns></returns>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.SaveFrameDataCachesAsync(Boolean) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task<int> DoCalibrationMonoCalcsAllHeadsAsync(RunCalibrationParams runParams)
        {
            int ret = 0;

            if (calibProject is null)
                return -1;

            InfoBarProcessing.ShowProcessing("Do mono calibration calculations...");
                      
            bool doLeftMono = false;
            bool doRightMono = false;

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
                    }
                    break;
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()! &&
                        LeftMonoCalibrationHead.IsOpen() &&
                        RightMonoCalibrationHead.IsOpen())
                    {
                        doLeftMono = true;
                        doRightMono = true;
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


            var monoPhaseTasks = new List<Task<int>>();
            int taskIndex = 0;
            int taskLeftMonoIndex = -1;
            int taskRightMonoIndex = -1;


            if (ret == 0)
            {
                // Find the best frames from the mono calibration frames 
                InfoBarProcessing.UpdateMessage("Mono calibration calculations...");

                // Do the mono calibration calculations
                monoPhaseTasks.Clear();
                taskIndex = 0;
                taskLeftMonoIndex = -1;
                taskRightMonoIndex = -1;

                if (doLeftMono)
                {
                    monoPhaseTasks.Add(Task.Run(() => LeftMonoCalibrationHead.DoMonoCalibrationCalculationSafeUI(
                                                                 calibProject,
                                                                 true/*trueLeftFalseRight*/,
                                                                 runParams.MonoCornersFilterValue)));
                    taskLeftMonoIndex = taskIndex++;
                }

                if (doRightMono)
                {
                    monoPhaseTasks.Add(Task.Run(() => RightMonoCalibrationHead.DoMonoCalibrationCalculationSafeUI(
                                                                 calibProject,
                                                                 false/*trueLeftFalseRight*/,
                                                                 runParams.MonoCornersFilterValue)));
                    taskRightMonoIndex = taskIndex++;
                }


                if (monoPhaseTasks.Count > 0)
                {
                    try
                    {
                        int[] results = await Task.WhenAll(monoPhaseTasks);

                        if (doLeftMono)
                        {
                            if (results[taskLeftMonoIndex] != 0)
                            {
                                ret = results[taskLeftMonoIndex];
                                Debug.WriteLine($"DoCalibrationMonoCalcsAllHeadsAsync: Error from DoMonoCalibrationCalculationSafeUI: Left Mono Result={ret}");
                            }
                        }
                        if (doRightMono)
                        {
                            if (results[taskRightMonoIndex] != 0)
                            {
                                ret = results[taskRightMonoIndex];
                                Debug.WriteLine($"DoCalibrationMonoCalcsAllHeadsAsync: Error from DoMonoCalibrationCalculationSafeUI: Right Mono Result={ret}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"DoCalibrationMonoCalcsAllHeadsAsync: Error running DoMonoCalibrationCalculationSafeUI tasks, {ex}");
                        ret = -2;
                    }
                }
            }

            if (ret != 0)
            {
                calibProject.Data.CalibrationResults.Clear();  // This actually clears all calibration results left, right and stereo

                if (doLeftMono)
                {
                    calibProject.Data.CalibrationResults.Clear();
                    LeftMonoCalibrationHead.ClearCalibrationResultsDisplay();
                }
                if (doRightMono)
                {
                    RightMonoCalibrationHead.ClearCalibrationResultsDisplay();
                }
            }

            if (ret == 0)
            {
                // Save the frames dataset
                InfoBarProcessing.UpdateMessage("Saving after mono phase...");
                await SaveFrameDataCachesAsync(false/*no prompts*/);
            }

            InfoBarProcessing.HideProcessing();
            SetUIControls();

            Debug.WriteLine($"DoCalibrationMonoCalcsAllHeadsAsync Run Status  Left:{LeftMonoCalibrationHead.IsFindRunning()}  Right:{RightMonoCalibrationHead.IsFindRunning()}");

            return ret;
        }


        /// <summary>
        /// Find the best stereo frames
        /// </summary>
        /// <param name="runCalibrationParams"></param>
        /// <returns></returns>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.SaveFrameDataCachesAsync(Boolean) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task<int> FindBestStereoFramesAsync(RunCalibrationParams runParams)
        {
            int ret = 0;

            if (calibProject is null)
                return -1;

            InfoBarProcessing.ShowProcessing("Finding best stereo frames...");
           
            SetUIControls();


            bool doStereo = false;  

            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
            {

                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()!)
                    {
                        doStereo = true;
                    }
                    break;
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()!)
                    {
                        doStereo = true;
                    }
                    break;
            }


            if (doStereo)
            {
                ret = await StereoCalibrationHead.FindBestStereoFramesAsync(calibProject, 
                                                                            runParams.MovementFilterValue,
                                                                            runParams.BlurFilterValue,
                                                                            runParams.MonoCornersFilterValue,
                                                                            runParams.MaxFramesFromEachSensorBin,
                                                                            runParams.MaxFramesFromEachPoseBin);
            }


            // If there is an error then clear any collected
            // values and reset the display
            if (ret != 0)
            {
                if (doStereo)
                {
                    StereoCalibrationHead.ClearResults(CalibrationStereoFrameSet.ClearRequest.BestFrames);
                }
            }


            if (ret == 0)
            {
                // Save the frames dataset
                InfoBarProcessing.UpdateMessage("Saving...");
                await SaveFrameDataCachesAsync(false/*no prompts*/);
            }

            InfoBarProcessing.HideProcessing();
            SetUIControls();

            Debug.WriteLine($"FindBestStereoFramesAsync Run Status  Stereo:{StereoCalibrationHead.IsFindRunning()}");

            return ret;
        }


        /// <summary>
        /// Perform the stereo calibration calculation on all heads using the best frames
        /// </summary>
        /// <param name="runCalibrationParams"></param>
        /// <returns></returns>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.SaveFrameDataCachesAsync(Boolean) which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task<int> DoCalibrationStereoCalcsAsync(RunCalibrationParams runParams)
        {
            int ret = 0;

            if (calibProject is null)
                return -1;

            InfoBarProcessing.ShowProcessing("Finding best stereo frames...");            

            SetUIControls();


            bool doStereo = false;

            switch (calibProject.Data.Media.StereoMonoMediaSetMode)
            {

                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()!)
                    {
                        doStereo = true;
                    }
                    break;
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (StereoCalibrationHead.IsOpen() &&
                        (bool)StereoCalibrationHead.IsStereoLocked()!)
                    {
                        doStereo = true;
                    }
                    break;
            }


            if (doStereo)
            {
                ret = StereoCalibrationHead.DoCalibrationStereoCalculations(calibProject, runParams.StereoCornersFilterValue);
            }

            if (ret != 0)
            {
                if (doStereo)
                {
                    calibProject.Data.CalibrationResults.Clear();  // This actually clears all calibration results left, right and stereo
                    StereoCalibrationHead.ClearCalibrationResultsDisplay();
                }
            }

            if (ret == 0)
            {
                // Save the frames dataset
                InfoBarProcessing.UpdateMessage("Saving...");
                await SaveFrameDataCachesAsync(false/*no prompts*/);
            }

            InfoBarProcessing.HideProcessing();
            SetUIControls();

            Debug.WriteLine($"DoCalibrationStereoCalcsAsync Run Status  Stereo:{StereoCalibrationHead.IsFindRunning()}");

            return ret;                                 
        }


        /// <summary>
        /// Write the best frames on all heads out to separate .png files
        /// </summary>
        /// <param name="runCalibrationParams"></param>
        /// <returns></returns>
        private async Task<int> SaveBestFramesAllHeadsAsync()
        {
            int ret = 0;

            if (calibProject is null)
                return -1;

            InfoBarProcessing.ShowProcessing("Save the best frame to files...");
            //???SetAppModeOnAllHeads(AppMode.BestFramesCalc);

            SetUIControls();

            bool doStereo = false;
            bool doLeftMono = false;
            bool doRightMono = false;

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
                        doLeftMono = true;
                        doRightMono = true;
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

#if MULTITASKVERSION
            var monoPhaseTasks = new List<Task<int>>();
            int taskIndex = 0;
            int taskLeftMonoIndex = -1;
            int taskRightMonoIndex = -1;
            int taskStereoIndex = -1;
#endif

            if (ret == 0)
            {
#if MULTITASKVERSION
                //if (doLeftMono)
                //{
                //    monoPhaseTasks.Add(Task.Run(() => LeftMonoCalibrationHead.SaveBestFramesAsync()));
                //    taskLeftMonoIndex = taskIndex++;
                //}
                //if (doRightMono)
                //{
                //    monoPhaseTasks.Add(Task.Run(() => RightMonoCalibrationHead.SaveBestFramesAsync()));
                //    taskRightMonoIndex = taskIndex++;
                //}
                //if (doStereo)
                //{
                //    monoPhaseTasks.Add(Task.Run(() => StereoCalibrationHead.SaveBestFramesAsync()));
                //    taskStereoIndex = taskIndex++;
                //}

                //if (monoPhaseTasks.Count > 0)
                //{
                //    try
                //    {
                //        int[] results = await Task.WhenAll(monoPhaseTasks);

                //        if (doLeftMono)
                //        {
                //            if (results[taskLeftMonoIndex] != 0)
                //            {
                //                ret = results[taskLeftMonoIndex];
                //                Debug.WriteLine($"SaveBestFramesAllHeadsAsync: Error from SaveBestFramesAsync: Left Mono Result={ret}");
                //            }
                //        }
                //        if (doRightMono)
                //        {
                //            if (results[taskRightMonoIndex] != 0)
                //            {
                //                ret = results[taskRightMonoIndex];
                //                Debug.WriteLine($"SaveBestFramesAllHeadsAsync: Error from SaveBestFramesAsync: Right Mono Result={ret}");
                //            }
                //        }
                //        if (doStereo)
                //        {
                //            if (results[taskStereoIndex] != 0)
                //            {
                //                ret = results[taskStereoIndex];
                //                Debug.WriteLine($"SaveBestFramesAllHeadsAsync: Error from SaveBestFramesAsync: Stereo Result={ret}");
                //            }
                //        }
                //    }
                //    catch (Exception ex)
                //    {
                //        Debug.WriteLine($"SaveBestFramesAllHeadsAsync: Error running DoMonoCalibrationCalculationSafeUI tasks, {ex}");
                //        ret = -2;
                //    }
                //}
#else
                if (doLeftMono)
                {
                    ret = await LeftMonoCalibrationHead.SaveBestFramesAsync();
                    if (ret != 0)
                    {
                        Debug.WriteLine($"SaveBestFramesAllHeadsAsync: Error from SaveBestFramesAsync: Left Mono Result={ret}");
                    }                    
                }
                if (doRightMono)
                {
                    ret = await RightMonoCalibrationHead.SaveBestFramesAsync();
                    if (ret != 0)
                    {
                        Debug.WriteLine($"SaveBestFramesAllHeadsAsync: Error from SaveBestFramesAsync: Right Mono Result={ret}");
                    }
                }
                if (doStereo)
                {
                    ret = await StereoCalibrationHead.SaveBestFramesAsync();
                    if (ret != 0)
                    {
                        Debug.WriteLine($"SaveBestFramesAllHeadsAsync: Error from SaveBestFramesAsync: Stereo Result={ret}");
                    }                
                }
#endif
            }

            InfoBarProcessing.HideProcessing();
            SetUIControls();

            return ret;
        }


        /// <summary>
        /// Used to set the interactive regions in the title bar area, allowing the menu bar
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

            // Create list of regions that should not be drag-able
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
        /// Check if the mono calibration calculations been done
        /// </summary>
        /// <returns></returns>
        public static bool IsMonoCalibrationCalculationsSetup(CalibProject calibProject, bool trueLeftFalseRight)
        {
            if (trueLeftFalseRight)
            {
                if (calibProject.Data.CalibrationResults.LeftMonoCalibrationCameraDataArray.Any(item => item is not null))
                    return true;
                else
                    return false;
            }
            else
            {
                if (calibProject.Data.CalibrationResults.RightMonoCalibrationCameraDataArray.Any(item => item is not null))
                    return true;
                else
                    return false;
            }
        }


        /// <summary>
        /// Check if the stereo calibration has been done
        /// </summary>
        /// <param name="calibProject"></param>
        /// <returns></returns>
        public static bool IsStereoCalibrationCalculationsSetup(CalibProject calibProject)
        {
            if (calibProject.Data.CalibrationResults.CalibrationStereoCameraDataArray.Any(item => item is not null))
                return true;
            else
                return false;   
        }


        /// <summary>
        /// Set the UI controls to the current mode
        /// </summary>
        private void SetUIControls()
        {
            bool isCalibProjectOpen = calibProject is not null;
            bool isMediaLocked = false;
            bool isProcessingHappening = IsFindRunning();
            bool isStereo = false;

            // Calculate isMediaLocked
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

            // File>Lock/Unlock Media menu item & Lock/Unlock title bar Icon
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

            // File>Export menu item
            if (isCalibProjectOpen && !isProcessingHappening && 
                (calibProject is not null && calibProject.IsCalibrationReady))
                MenuExport.IsEnabled = true;
            else
                MenuExport.IsEnabled = false;


            // View>All Frames menu items
            MenuViewAllFrames.IsEnabled = IsViewModeAvailableOnAllHeads(ViewMode.AllFrames);

            // View>Best Frames menu items
            MenuViewBestFrames.IsEnabled = IsViewModeAvailableOnAllHeads(ViewMode.BestFrames);

            // View>Filter Frames menu item
            MenuViewFilterFrames.IsEnabled = false; /*??? Disable until implemented IsViewModeAvailableOnAllHeads(ViewMode.FilterFrames);*/

            // View>Sensor Coverage menu item
            MenuViewSensorCoverage.IsEnabled = false; /*??? Disable until implemented IsViewModeAvailableOnAllHeads(ViewMode.SensorCoverage);*/

            // Set the current view mode menu checks
            
            if (isCalibProjectOpen)
            {
                ViewMode currentViewMode = GetCurrentViewModeOnAllHeads();
                MenuViewAllFrames.IsChecked = (currentViewMode == ViewMode.AllFrames);
                MenuViewBestFrames.IsChecked = (currentViewMode == ViewMode.BestFrames);
                MenuViewFilterFrames.IsChecked = (currentViewMode == ViewMode.FilterFrames);
                MenuViewSensorCoverage.IsChecked = (currentViewMode == ViewMode.SensorCoverage);

                // Set the view mode on the title bar
                string viewMode = currentViewMode switch
                {
                    ViewMode.AllFrames      => "All Frames",
                    ViewMode.BestFrames     => "Best Frames",
                    ViewMode.FilterFrames   => "Filter Frames",
                    ViewMode.SensorCoverage => "Sensor Coverage",
                    _                       => "All Frames"
                };
                // Set the view mode text with animation to draw users attention
                if (TitleBarViewMode.Text != viewMode)
                {
                    TitleBarViewMode.Text = viewMode;
                    TitleBarViewMode.Visibility = Visibility.Visible;

                    // Restart the animation each time
                    TitleBarViewModeChangeStoryboard.Stop();
                    TitleBarViewModeChangeStoryboard.Begin();
                }
            }
            else
            {
                MenuViewAllFrames.IsChecked = false;
                MenuViewBestFrames.IsChecked = false;
                MenuViewFilterFrames.IsChecked = false;
                MenuViewSensorCoverage.IsChecked = false;

                TitleBarViewMode.Text = string.Empty;
                TitleBarViewMode.Visibility = Visibility.Collapsed;
            }


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
            DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("DispatcherQueue is not available on the current thread.");
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
                    bool calibrationBoardZoneAvailable = CheckIfCacheResultAvailable(CachedResultCheckType.CalibrationBoardZone);
                    bool frameSetsAvailable = calibrationBoardZoneAvailable && CheckIfCacheResultAvailable(CachedResultCheckType.FrameSets);
                    bool bestMonoFramesAvailable = frameSetsAvailable && CheckIfCacheResultAvailable(CachedResultCheckType.BestMonoFrames);
                    bool calibrationMonoCalculationsAvailable = bestMonoFramesAvailable && CheckIfCacheResultAvailable(CachedResultCheckType.MonoCalibrationCalcs);
                    bool bestStereoFramesAvailable = calibrationMonoCalculationsAvailable && CheckIfCacheResultAvailable(CachedResultCheckType.BestStereoFrames);
                    bool calibrationStereoCalculationsAvailable = bestStereoFramesAvailable && CheckIfCacheResultAvailable(CachedResultCheckType.StereoCalibrationCalcs);


                    // These are the parameters setup in the SetupRunCalibration window
                    // and used by the RunCalibration process
                    RunCalibrationParams runParams = new()
                    {
                        // Setup the action flags according to what is found
                        // in the cache
                        FindCalibrationBoardZone = !calibrationBoardZoneAvailable,
                        BuildTheFrameSets = !frameSetsAvailable,
                        FindBestMonoFrames = !bestMonoFramesAvailable,
                        DoCalibrationMonoCalculations = !calibrationMonoCalculationsAvailable,
                        FindBestStereoFrames = !bestStereoFramesAvailable,
                        DoCalibrationStereoCalculations = !calibrationStereoCalculationsAvailable,

                        // If a cache is available that get the min movement/blur values (used by the sliders)
                        MovementFilterMin = GetMinMovementFromCachedResults(true/*from all frames*/),
                        BlurFilterMin = GetMinBlurFromCachedResults(true/*from all frames*/),

                        // If a cache is available that get the max movement/blur values (used by the sliders)
                        MovementFilterMax = GetMaxMovementFromCachedResults(true/*from all frames*/),
                        BlurFilterMax = GetMaxBlurFromCachedResults(true /*from all frames*/)
                    };


                    // Initialize if necessary
                    SetupRunCalibration setupRunCalibration = new(this, calibProject, runParams);

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
                    if (leftMonoMax != 0 || rightMonoMax != 0 || stereoMax != 0)
                        ret = Math.Max(leftMonoMax, Math.Max(rightMonoMax, stereoMax));
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
                    if (leftMonoMin != double.MaxValue || rightMonoMin != double.MaxValue || stereoMin != double.MaxValue)
                        ret = Math.Min(leftMonoMin, Math.Min(rightMonoMin, stereoMin));
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
                    if (leftMonoMax != 0 || rightMonoMax != 0 || stereoMax != 0)
                        ret = Math.Max(leftMonoMax, Math.Max(rightMonoMax, stereoMax));
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
        /// Find the smallest blur value from the cached results
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
                    if (leftMonoMin != double.MaxValue || rightMonoMin != double.MaxValue || stereoMin != double.MaxValue)
                        ret = Math.Min(leftMonoMin, Math.Min(rightMonoMin, stereoMin));
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
                // Use four spaces to keep layout width similar to the lock/unlock
                // icons and preserve tool tip behavior as the glyph changes
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
        /// Set the title text elements of the title bar title text
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
        /// Set the save status text elements of the title bar title text
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
        /// Set the camera side status text elements of the title bar title text
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
        /// Get the current media positions and record them in the Data.Sync class
        /// Inform the Stereo Head to lock
        /// 
        /// If Unlocking:
        /// Mark as unsynchronized in the Data.Sync class
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
                                            Text = "Synchronization information already exists in this calibration project but is disabled. Do you want to re-enable it, or lock the players at their current positions?",
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

                // Lock the left and right media controllers
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

                // Engage the MediaTimelineController
                if (calibProject is not null && (reEnable || newPosition))
                {
                    StereoCalibrationHead.LockStereo(calibProject.Data.Sync.SyncFrameIndexLeft,
                                                     calibProject.Data.Sync.SyncFrameIndexRight);
                }

                // Show the next Run Calibration teaching tip if enabled and not shown before
                if (SettingsManagerLocal.TeachingTipsEnabled &&
                    !SettingsManagerLocal.HasTeachingTipBeenShown("MenuRunCalibration"))
                {
                    MenuRunCalibrationTeachingTip.IsOpen = true;
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

                        // Do not remove SyncFrameIndexLeft or SyncFrameIndexRight in
                        // case the user wants to synchronize again
                    }

                    StereoCalibrationHead.UnlockStereo();
                }
            }
        }


        /// <summary>
        /// Add the selected survey to the recent surveys list
        /// </summary>
        /// <param name="filePath"></param>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.UpdateRecentProjectsMenu() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
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
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.UpdateRecentProjectsMenu() which ultimately uses Json.NET serialization which may not be compatible with trimming.")]
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
        /// Update the recent projects menu from localSettings
        /// </summary>
        [RequiresUnreferencedCode("Calls Surveyor.MainWindow.FileRecentProject_Click(Object, RoutedEventArgs)")]
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

                    // Add tool tip to show the full file specification
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
        private void SetAppModeOnAllHeads(AppMode appMode)
        {
            StereoCalibrationHead.SetAppMode(appMode);
            LeftMonoCalibrationHead.SetAppMode(appMode);
            RightMonoCalibrationHead.SetAppMode(appMode);

            SetUIControls();
        }


        /// <summary>
        /// Tell all heads to set their display mode        
        /// This controls the view mode of the video display like
        /// showing all frames, best frames, filtered frames, or sensor coverage.
        /// </summary>
        /// <param name="appView"></param>
        private void SetViewModeOnAllHeads(ViewMode appView)
        {
            Stopwatch sw = new();//???
            Stopwatch swAll = new();//???
            sw.Start();//???
            swAll.Start();//???
            StereoCalibrationHead.SetViewMode(appView);
            Debug.WriteLine($"Swap to {appView} Stereo Head {sw.ElapsedMilliseconds}m/s");
            sw.Reset();//???
            sw.Start();//???
            LeftMonoCalibrationHead.SetViewMode(appView);
            Debug.WriteLine($"Swap to {appView} LeftMono Head {sw.ElapsedMilliseconds}m/s");
            sw.Reset();//???
            sw.Start();//???
            RightMonoCalibrationHead.SetViewMode(appView);
            Debug.WriteLine($"Swap to {appView} RightMono Head {sw.ElapsedMilliseconds}m/s");

            Debug.WriteLine($"Swap to {appView} Total {swAll.ElapsedMilliseconds}m/s");

            // Called so the ViewMode on the title bar changes
            SetUIControls();
        }


        /// <summary>
        /// Check if a particular view is available on all heads
        /// </summary>
        /// <param name="viewMode"></param>
        /// <returns></returns>
        private bool IsViewModeAvailableOnAllHeads(ViewMode viewMode)
        {
            bool ret = false;

            bool stereo = StereoCalibrationHead.IsViewModeAvailable(viewMode);
            bool left = LeftMonoCalibrationHead.IsViewModeAvailable(viewMode);
            bool right = RightMonoCalibrationHead.IsViewModeAvailable(viewMode);

            if (calibProject is not null)
            {
                switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                {
                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        ret = left;
                        break;
                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        ret = left && right;
                        break;
                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        ret = stereo && left && right;
                        break;
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        ret = stereo && left && right;
                        break;
                    default:
                        ret = false;
                        break;
                }
            }

            return ret;
        }


        /// <summary>
        /// Get the current view mode on all heads
        /// </summary>
        /// <returns></returns>
        private ViewMode GetCurrentViewModeOnAllHeads()
        {
            ViewMode ret = ViewMode.AllFrames;

            ViewMode stereo = StereoCalibrationHead.ViewModeCurrent;
            ViewMode left = LeftMonoCalibrationHead.ViewModeCurrent;
            ViewMode right = RightMonoCalibrationHead.ViewModeCurrent;

            if (calibProject is not null)
            {
                // Return the more 'restrictive view' i.e. if one head is on BestFrames and
                // another on AllFrames the return should be AllFrames because BestFrames
                // is not available on all heads but AllFrames is.
                switch (calibProject.Data.Media.StereoMonoMediaSetMode)
                {
                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        ret = left;
                        break;
                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        ret = (ViewMode)Math.Min((int)left, (int)right);
                        break;
                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                        ret = (ViewMode)Math.Min((int)stereo, Math.Min((int)left, (int)right));
                        break;
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        ret = (ViewMode)Math.Min((int)stereo, Math.Min((int)left, (int)right));
                        break;
                    default:
                        ret = ViewMode.AllFrames;
                        break;
                }
            }

            return ret;
        }


        /// <summary>
        /// Configures the main window layout and visibility of calibration controls based on the specified stereo or
        /// mono media set mode.
        /// It shows only the calibration heads required for the given StereoMonoMediaSetMode
        /// </summary>
        /// <remarks>This method updates the visibility and arrangement of UI elements to match the
        /// requirements of the selected media set mode. It should be called whenever the media set mode changes to
        /// ensure the main window reflects the current calibration scenario.</remarks>
        /// <param name="stereoMonoMediaSetMode">The media set mode that determines which calibration controls and layout elements are displayed. Must be a
        /// valid value of the StereoMonoMediaSetMode enumeration.</param>
        private void SetupMainWindowForStereoMonoMediaSetMode(StereoMonoMediaSetMode stereoMonoMediaSetMode)
        {
            bool showStereoCalibrationHead = false;
            bool showMonoStereoSplitter = false;
            bool showMonoCalibrationHeads = false;
            bool showLeftRightSplitter = false;
            bool showRightMonoCalibrationHead = false;

            switch (stereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    LeftMonoCalibrationHead.HeadTitle = "Left Mono Calibration Video";

                    // Show the both mono heads and the stereo head and their splitters
                    showStereoCalibrationHead = true;
                    showMonoStereoSplitter = true;
                    showMonoCalibrationHeads = true;
                    showLeftRightSplitter = true;
                    showRightMonoCalibrationHead = true;
                    break;

                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    LeftMonoCalibrationHead.HeadTitle = "Left Mono Calibration Video";

                    // Show both mono and stereo heads but the mono head are using the stay videos as the stereo head 
                    showStereoCalibrationHead = true;
                    showMonoStereoSplitter = true;
                    showMonoCalibrationHeads = true;
                    showLeftRightSplitter = true;
                    showRightMonoCalibrationHead = true;
                    break;

                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    LeftMonoCalibrationHead.HeadTitle = "Left Mono Calibration Video";

                    // Show the both mono heads only
                    showStereoCalibrationHead = false;
                    showMonoStereoSplitter = false;
                    showMonoCalibrationHeads = true;
                    showLeftRightSplitter = true;
                    showRightMonoCalibrationHead = true;
                    break;
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    LeftMonoCalibrationHead.HeadTitle = "Mono Calibration Video";

                    // Show the only the left mono head
                    showStereoCalibrationHead = false;
                    showMonoStereoSplitter = false;
                    showMonoCalibrationHeads = true;
                    showLeftRightSplitter = false;
                    showRightMonoCalibrationHead = false;
                    break;
            }

 
            // Stereo Calibration Head
            if (showStereoCalibrationHead)
            {
                // Show the stereo calibration head
                StereoCalibrationHead.Visibility = Visibility.Visible;
                StereoHeadRow.Height = new GridLength(42, GridUnitType.Star);
            }
            else
            {
                // Hide the stereo calibration head
                StereoCalibrationHead.Visibility = Visibility.Collapsed;
                StereoHeadRow.Height = new GridLength(0);
            }

            // Splitter between mono and stereo heads
            if (showMonoStereoSplitter)
            {
                // Show the stereo/mono splitter
                MonoStereoSplitter.Visibility = Visibility.Visible;
                MonoStereoSplitterRow.Height = new GridLength(8);
            }
            else
            {
                // Show the stereo/mono splitter
                MonoStereoSplitter.Visibility = Visibility.Collapsed;
                MonoStereoSplitterRow.Height = new GridLength(0);
            }

            // Mono Calibration Heads (the grid that holds them)
            if (showMonoCalibrationHeads)
            {
                // Show both mono heads and the splitter below
                MonoCalibrationHeads.Visibility = Visibility.Visible;
                MonoHeadRow.Height = new GridLength(42, GridUnitType.Star);
            }
            else
            {
                // Hide both mono heads and the splitter below
                MonoCalibrationHeads.Visibility = Visibility.Collapsed;
                MonoHeadRow.Height = new GridLength(0);
            }

            // Splitter between the mono calibration heads
            if (showLeftRightSplitter)
            {
                LeftRightMonoSplitter.Visibility = Visibility.Visible;
                LeftRightMonoSplitterColumn.Width = new GridLength(8);
            }
            else
            {
                LeftRightMonoSplitter.Visibility = Visibility.Collapsed;
                LeftRightMonoSplitterColumn.Width = new GridLength(0);
            }

            if (showRightMonoCalibrationHead)
            {
                // Ensure the right mono head and the left/right splitter was visible
                RightMonoCalibrationHead.Visibility = Visibility.Visible;
                RightMonoHeadColumn.Width = new GridLength(1, GridUnitType.Star);
            }
            else 
            {
                // Hide the right mono head and the left/right splitter was visible
                RightMonoCalibrationHead.Visibility = Visibility.Collapsed;
                RightMonoHeadColumn.Width = new GridLength(0);
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

