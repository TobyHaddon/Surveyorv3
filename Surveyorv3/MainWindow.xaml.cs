using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;
using Surveyor;
using Surveyor.DesktopWap.Helper;
using Surveyor.Events;
using Surveyor.Helper;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.UI;
using WinRT.Interop;
using WinUIEx;
using static Surveyor.Helper.TelemetryLogger;
using static Surveyor.MediaStereoControllerEventData;
using static Surveyor.Survey;
using static Surveyor.Survey.DataClass;
using static Surveyor.User_Controls.MediaPlayerEventData;
using static Surveyor.User_Controls.SettingsWindowEventData;


namespace Surveyor
{

    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : WindowEx
    {
        // Create the Mediator
        private readonly SurveyorMediator mediator;

        // Declare the mediator handler for MainWindow
        private readonly MainWindowHandler mainWindowHandler;

        // Declare the MediaStereoController
        // Uses 'Internal' so it can be used in the unit test project
        internal readonly MediaStereoController mediaStereoController;

        // Title bar title elements
        private string titlebarTitle = "";
        private string titlebarCameraSide = "";
        private string titlebarSaveStatus = "";

        // Current Survey Class
        private Survey? surveyClass = null;

        // StereoProjection class
        private readonly StereoProjection stereoProjection = new();

        // Hidden controls to be shown dynamically
        private readonly EventsControl eventsControl = new();

        // Uses 'internal' so App class can access it to dump report in case of a crash
        internal readonly Reporter report = new();

        private readonly TransectMarkerManager transectMarkerManager = new();

        // Recent surveys management
        private const string RECENT_SURVEYS_KEY = "RecentSurveys";
        private readonly int maxRecentSurveysDisplayed = 6;      
        private const int MAX_RECENT_SURVEYS_SAVED = 20;

        // Internet connection status and management
        internal NetworkManager networkManager;
        private bool? isOnlineRememberedStatus = null;
        private bool? useInternetRememberedEnabled = null;

        // Internet Download/Upload manager
        internal InternetQueue internetQueue;

        // Help menu documents
        private readonly HelpDocuments helpDocuments = new();

        // InfoBar Dismissed Status
        private bool infoBarCalibrationMissingDismissed = false;
        private bool infoBarSpeciesInfoMissingDismissed = false;
        private bool infoBarRMSRuleViolationDismissed = false;

        // Experimental
        private bool experimentalEnabled = false;
        private bool experimentalFeatureSetAEnabled = false;
        private bool experimentalFeatureSetBEnabled = false;
        private bool experimentalFeatureSetCEnabled = false;


        public MainWindow()
        {
            this.InitializeComponent();
            
            // Event first before the app window is committed to closing (i.e. can be canceled)
            if (this.AppWindow is not null)
                this.AppWindow.Closing += AppWindow_Closing;

            // Inform the Reporter of the DispatcherQueue
            report.SetDispatcherQueue(DispatcherQueue);

            // Inform the Events Control of the DispatcherQueue
            eventsControl.SetDispatcherQueue(DispatcherQueue);

            // Intercept keys like Space from EventsControl (used play/pause the media players via MediaStereoCOntroller)
            eventsControl.SpaceKeyPressed += EventsControl_SpaceKeyPressed;

            // This is used to get/adjust the theme is necessary
            ThemeHelper.Initialize();

            // Set theme
            SetTheme(SettingsManagerLocal.ApplicationTheme);

            // Add listener for theme changes
            var rootElement = (FrameworkElement)Content;
            rootElement.ActualThemeChanged += OnActualThemeChanged;

            // Load app settings (these are read-only)
            try
            {
                // Load settings from JSON (happens automatically)
                var settings = SettingsManagerApp.Instance;
            }
            catch (Exception ex)
            {
                report.Error("", $"App Settings failed to load, {ex.Message}");
            }

            // Set the number of recently opened surveys that are displaying in the File menu
            maxRecentSurveysDisplayed = Math.Min(SettingsManagerApp.Instance.RecentSurveysDisplayed, MAX_RECENT_SURVEYS_SAVED);

            // Set setup Mediator
            mediator = new();
            mediator.SetReporter(report);

            // Restore the saved window state
            PersistenceId = "MainWindow";
            MinHeight = 600;
            MinWidth = 800;

            // Setup the Handler for the MainWindow
            mainWindowHandler = new MainWindowHandler(mediator, this);

            // Initialize the internet network manager
            networkManager = new(report);

            // Set the Network Connection title bar icon
            NetworkConnectionIndicator.Text = "    ";
            networkManager.RegisterAction((_isOnline, _isMetered, _bars) =>
            {
                if ((isOnlineRememberedStatus is null && _isOnline) ||      // If first time online status seen
                    (_isOnline && !(bool)isOnlineRememberedStatus!) ||      // If online status has changed
                    /*(useInternetRememberedEnabled is null && _isOnline) ||  // If online and */
                    (useInternetRememberedEnabled is not null && _isOnline && useInternetRememberedEnabled != SettingsManagerLocal.UseInternetEnabled))  // If online and the Use Internet option has changed
                {
                    _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                    {
                        if (SettingsManagerLocal.UseInternetEnabled)
                        {
                            // Signal change in network to online
                            Debug.WriteLine($"{DateTime.Now}  Application is connection to the internet and application is allowed to use the internet");

                            if (_isMetered)
                            {
                                if (_bars <= 0 )
                                    NetworkConnectionIndicator.Text = "\uE1E5"; // Zero bars icon
                                else if (_bars == 1)
                                    NetworkConnectionIndicator.Text = "\uE1E6"; // One bar icon
                                else if (_bars == 2)
                                    NetworkConnectionIndicator.Text = "\uE1E7"; // Two bars icon
                                else if (_bars == 3)
                                    NetworkConnectionIndicator.Text = "\uE1E8"; // Three bars icon
                                else // 4 or more bars
                                    NetworkConnectionIndicator.Text = "\uE1E9"; // Four bars icon
                            }
                            else
                                NetworkConnectionIndicator.Text = "\uE701";  // Normal WiFi

                            if (_isMetered)
                                ToolTipService.SetToolTip(NetworkConnectionIndicator, "Connected to metered internet");
                            else
                                ToolTipService.SetToolTip(NetworkConnectionIndicator, "Connected to the internet");
                        }
                        else
                        {
                            // Signal change in network to online but not allowed to use
                            Debug.WriteLine($"{DateTime.Now}  Application is connection to the internet but application is not allowed to use the internet");
                            NetworkConnectionIndicator.Text = "\uEB5E";
                            ToolTipService.SetToolTip(NetworkConnectionIndicator, "Connected to the internet but application not allowed to access. Go to Settings to allow access.");
                        }
                    });
                }
                else if ((isOnlineRememberedStatus is null && !_isOnline) || (!_isOnline && (bool)isOnlineRememberedStatus!))
                {
                    _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                    {
                        // Internet state change in network to offline
                        Debug.WriteLine($"{DateTime.Now}  Application is disconnection to the internet");

                        // Show nothing (normally if no calibration is available)
                        // Four spaces to make it invisible and approximately the same width
                        // as the lock/unlock icons (do not change, not fully understood but
                        // needed to keep the tool tip working as the glyph changes)
                        NetworkConnectionIndicator.Text = "    ";
                        ToolTipService.SetToolTip(NetworkConnectionIndicator, "");
                    });
                }

                isOnlineRememberedStatus = _isOnline;                                    // Remember the current state of if the internet is available
                useInternetRememberedEnabled = SettingsManagerLocal.UseInternetEnabled;  // Remember the current state of if we are allowed to use the internet
                return Task.CompletedTask;
            }, Surveyor.Priority.Normal);


            // Initialize internet download/upload manager
            internetQueue = new(report);
            _ = internetQueue.LoadAsync(); // fire-and-forget
            networkManager.RegisterAction(async (_isOnline, _isMetered, _bars) =>
            {
                if (_isOnline)
                {
                    await internetQueue.DownloadUploadAsync(_isMetered);
                }

                return;
            }, Surveyor.Priority.Normal);

            // Setup event to indicate if downloading/uploading
            internetQueue.InternetActivityChanged += (sender, isActive) =>
            {
                if (isActive)
                {
                    UIHelper.SafeUICall(this, StartDownloadUploadSpinner);
                    Debug.WriteLine("Internet activity started (event)...");
                }
                else
                {
                    UIHelper.SafeUICall(this, StopDownloadUploadSpinner);
                    Debug.WriteLine("Internet activity stopped (event)...");
                }
            };


            // Create the MediaStereoController and pass it the Mediator
            mediaStereoController = new MediaStereoController(this, report,
                                                              mediator,
                                                              MediaPlayerLeft, MediaPlayerRight,
                                                              MediaControlPrimary, MediaControlSecondary,
                                                              eventsControl,                                                              
                                                              stereoProjection                                                            
                                                              /*MediaInfoLeft, MediaInfoRight */);

            // Inform the Events Control of MainWindow and MediaStereoController
            eventsControl.SetMainWindow(this);
            eventsControl.SetMediaStereoController(mediaStereoController);

            // Allows the menu bar to extend into the title bar
            // Assumes "this" is a XAML Window. In projects that don't use 
            // WinUI 3 1.3 or later, use inter-op APIs to get the AppWindow.           
            AppTitleBar.Loaded += AppTitleBar_Loaded;
            AppTitleBar.SizeChanged += AppTitleBar_SizeChanged;
            ExtendsContentIntoTitleBar = true;

            // Now the interactive regions of the title bar has been established let 
            // remove the LockUnlockIndicator from the title bar that was only there
            // so the regions could be calculated correctly
            SetLockUnlockIndicator(null, null);

            // Set the default tab view visibility
            UpdateNavigationViewVisibility();

            // Show calibration status (i.e. not calibrated)
            SetCalibratedIndicator(null, null);

            // Update the Recent open surveys sub menu
            UpdateRecentSurveysMenu();


            // Set-up any controls that depend on the diagnostic information state 
            _SetDiagnosticInformation(SettingsManagerLocal.DiagnosticInformation);

            // Load the experimental settings
            _SetExperimental(SettingsManagerLocal.ExperimentalEnabled, 
                SettingsManagerLocal.ExperimentalFeatureSetAEnabled, SettingsManagerLocal.ExperimentalFeatureSetBEnabled, SettingsManagerLocal.ExperimentalFeatureSetCEnabled);


            // Add the help documents to the Help menu
            // Fix for CS1503: Argument 1: cannot convert from 'System.Collections.Generic.IList<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase>' to 'Microsoft.UI.Xaml.Controls.ItemCollection'

            // The issue arises because `MenuHelp.Items` is of type `IList<MenuFlyoutItemBase>`,
            // but the `Initialize` method of `HelpDocuments` expects an `ItemCollection`.
            // To fix this, we need to pass the correct type to the `Initialize` method.

            _ = helpDocuments.InitializeAsync(MenuHelp.Items, // Pass the MenuFlyoutSubItem directly instead of its Items property
                                              HelpDocumentsPDFSection,
                                              HelpDocumentsVideosSection,
                                              HelpDocumentsDOCSection,
                                              HelpDocumentsXLSSection);
       

            // Report that the app has loaded
            
            // Debug.WriteLine($"Local Folder path:{ApplicationData.Current.LocalFolder.Path}");
            if (!SettingsManagerLocal.DiagnosticInformation)
            {
                report.Info("", $"App Loaded OK (Local Path:{ApplicationData.Current.LocalFolder.Path})");
            }
            else
            {
                report.Info("", $"App Loaded OK");
                report.Info("", $"Local Path:{ApplicationData.Current.LocalFolder.Path}");
                report.Info("", $"Exec Path:{Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!}");
            }

            // Load a startup survey if the application was started via a file association
            // Now check for startup file
            LoadSurveyIfRequestedAndJumpToPoistionIfNeeded();
           
        }


        /// <summary>
        /// Expand the either the left or right media player to full screen
        /// </summary>
        /// <param name="TrueLeftFalseRight"></param>
        public void MediaFullScreen(bool TrueLeftFalseRight)
        {
            if (TrueLeftFalseRight)
            {
                GridColumnLeftMedia.Width = new GridLength(50, GridUnitType.Star);
                GridColumnRightMedia.Width = new GridLength(0);
            }
            else
            {
                GridColumnLeftMedia.Width = new GridLength(0);
                GridColumnRightMedia.Width = new GridLength(50, GridUnitType.Star);
            }
            GridColumnMediaSeparator.Width = new GridLength(0);
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
                var bitmapImage = new BitmapImage(new Uri($"ms-appx:///Assets/Surveyor-Dark.png"));
                TitleBarIcon.Source = bitmapImage;

                TitleBarHelper.SetCaptionButtonColors(this, Colors.White);                
            }
            else if (theme == ElementTheme.Light)
            {
                // Set the RequestedTheme of the root element to Light
                rootElement.RequestedTheme = ElementTheme.Light;
                rootElement.RequestedTheme = ElementTheme.Light;

                // Use a light theme icon
                var bitmapImage = new BitmapImage(new Uri($"ms-appx:///Assets/Surveyor-Light.png"));
                TitleBarIcon.Source = bitmapImage;

                TitleBarHelper.SetCaptionButtonColors(this, Colors.Black);                
            }
            else
            {
                // Use the default system theme
                rootElement.RequestedTheme = ElementTheme.Default;

                // Get the background color used by that theme
                var color = TitleBarHelper.ApplySystemThemeToCaptionButtons(this) == Colors.White ? "Dark" : "Light";

                // Based on the background color, select a suitable application icon
                if (color == "Dark")
                    TitleBarIcon.Source = new BitmapImage(new Uri($"ms-appx:///Assets/Surveyor-Dark.png"));
                else
                    TitleBarIcon.Source = new BitmapImage(new Uri($"ms-appx:///Assets/Surveyor-Light.png"));
            }

            // If the theme has changed, announce the change to the user
            UIHelper.AnnounceActionForAccessibility(rootElement, "Theme changed", "ThemeChangedNotificationActivityId");
        }


        /// <summary>
        /// Restore the media players to their original size
        /// </summary>
        public void MediaBackToWindow()
        {
            GridColumnLeftMedia.Width = new GridLength(50, GridUnitType.Star);
            GridColumnMediaSeparator.Width = new GridLength(1);
            GridColumnRightMedia.Width = new GridLength(50, GridUnitType.Star);
        }


        /// <summary>
        /// Called from the stereo controller to save the current video frame
        /// </summary>
        /// <param name="controlType"></param>
        public async Task SaveCurrentFrameAsync(SurveyorMediaControl.eControlType controlType)
        {
            if (surveyClass is null ||
                surveyClass.IsLoaded == false ||
                string.IsNullOrEmpty(surveyClass.Data.Info.SurveyPath))
            {

                // Survey needs to be saved before a frame can be saved
                var warningIcon = new SymbolIcon(Symbol.Important); // Symbol.Important represents an exclamation

                // Create the ContentDialog instance
                var dialog = new ContentDialog
                {
                    Title = $"Can't save the current frame",
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                            {
                                warningIcon, // Add the exclamation icon to the dialog content
                                new TextBlock { Text = "Survey needs to be saved before a frame can be saved" }
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
                    // Allow the user to save the survey                    
                    await SaveAsSurveyAsync();
                }
                else if (result == ContentDialogResult.Secondary)
                    return;
            }

            // Recheck if the SurveyClass is null
            if (surveyClass is not null &&
                surveyClass.IsLoaded &&
                !string.IsNullOrEmpty(surveyClass.Data.Info.SurveyPath))
            {
                string framesPath = surveyClass.Data.Info.SurveyPath + @"\Frames";

                // Create the folder if it does not exist
                if (!Directory.Exists(framesPath))
                    Directory.CreateDirectory(framesPath);


                if (controlType == SurveyorMediaControl.eControlType.Both)
                {
                    // Write a pair of stereo frames, used the timestamp of the media timeline controller
                    mediaStereoController.GetFullMediaPosition(out TimeSpan positionTimelineController, out _, out _);
                    await MediaPlayerLeft.SaveCurrentFrameAsync(framesPath, positionTimelineController/*time stamp*/, true/*syncdPair*/);
                    await MediaPlayerRight.SaveCurrentFrameAsync(framesPath, positionTimelineController/*time stamp*/, true/*syncdPair*/);
                }
                else if (controlType == SurveyorMediaControl.eControlType.Primary && MediaPlayerLeft.Position is not null)
                {
                    // Save the left media player frame, use the timestamp of the media player
                    await MediaPlayerLeft.SaveCurrentFrameAsync(framesPath, (TimeSpan)MediaPlayerLeft.Position/*time stamp*/, false/*syncdPair*/);
                }
                else if (controlType == SurveyorMediaControl.eControlType.Secondary && MediaPlayerRight.Position is not null)
                {
                    // Save the right media player frame, use the timestamp of the media player
                    await MediaPlayerRight.SaveCurrentFrameAsync(framesPath, (TimeSpan)MediaPlayerRight.Position/*time stamp*/, false/*syncdPair*/);
                }
            }
        }


        /// <summary>
        /// Display the passed pointer coordinates in a <TextBox> on the navigation panel
        /// Pass x=-1 to clear the display
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void DisplayPointerCoordinates(double x, double y)
        {
            if (x != -1)
            {
                PointerCoordinates.Text = $"{Math.Round(x, 1)}, {Math.Round(y, 1)}";
                PointerCoordinatesIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                PointerCoordinates.Text = "";
                PointerCoordinatesIndicator.Visibility = Visibility.Collapsed;
            }
        }


        /// <summary>
        /// Returns the position offsets of the left and right media players
        /// This is by the SurveryorTesting class to check the media players are correctly in sync
        /// </summary>
        /// <returns></returns>
        public (TimeSpan?, TimeSpan?) GetMediaPlayerPositions()
        {
            return (MediaPlayerLeft.Position, MediaPlayerRight.Position);
        }


        /// <summary>
        /// The method is called to display the 'Calibration missing' info bar if necessary
        /// i.e. survey is open and has Measurement events and calibration is not ready
        /// </summary>
        public void SetInfoBarCalibrationMissing()
        {
            bool showMissingCalibrationInfoBar = false;

            // Only show if a survey is open and is StereoFish (SVS) type
            // This is the only survey type that uses calibration
            if (surveyClass is not null && surveyClass.Data.Info.SurveyType == Survey.SurveyType.StereoFish)
            {
                CalibrationClass calibrationClass = surveyClass.Data.Calibration;
                int frameWidth = MediaPlayerLeft.FrameWidth;
                int frameHeight = MediaPlayerLeft.FrameHeight;

                // Get the preferred calibration data ensuring it is for this frame size
                CalibrationData? calibrationDataPreferred = calibrationClass.GetPreferredCalibationData(frameWidth, frameHeight);
                if (calibrationDataPreferred is null)
                    showMissingCalibrationInfoBar = true;
            }

            if (!infoBarCalibrationMissingDismissed/*User Dismissed Already*/ && showMissingCalibrationInfoBar)
            {
                // Show the info bar
                InfoBarCalibrationMissing.IsOpen = true;
                InfoBarCalibrationMissing.Visibility = Visibility.Visible;
            }
            else
            {
                // Hide the info bar
                InfoBarCalibrationMissing.IsOpen = false;
                InfoBarCalibrationMissing.Visibility = Visibility.Collapsed;
            }
        }


        /// <summary>
        /// The method is called to display the 'Species Info missing' info bar if necessary
        /// i.e. one of more Measurement, 3d Point or Single Point events are missing species
        /// information
        /// </summary>
        public void SetInfoBarSpeciesInfoMissing()
        {
            bool showMissingSpeciesInfoInfoBar = false;
            int countMeasurementPointsMissingSpecies = 0;
            int countStereoPointsMissingSpecies = 0;
            int countSinglePointsMissingSpecies = 0;

            if (surveyClass is not null)
            {
                countMeasurementPointsMissingSpecies = surveyClass.Data.Events.EventList.Cast<Event>()
                    .Where(e => e.EventDataType == SurveyDataType.SurveyMeasurementPoints)
                    .Select(e => e.EventData as SurveyMeasurement)
                    .Count(m => m != null && m.SpeciesInfo?.Species == null);

                if (countMeasurementPointsMissingSpecies > 0)
                {
                    showMissingSpeciesInfoInfoBar = true;
                }
                else
                {
                    countStereoPointsMissingSpecies = surveyClass.Data.Events.EventList.Cast<Event>()
                        .Where(e => e.EventDataType == SurveyDataType.SurveyStereoPoint)
                        .Select(e => e.EventData as SurveyStereoPoint)
                        .Count(s => s != null && s.SpeciesInfo?.Species == null);
                    if (countStereoPointsMissingSpecies > 0)
                    {
                        showMissingSpeciesInfoInfoBar = true;
                    }
                    else
                    {
                        countSinglePointsMissingSpecies = surveyClass.Data.Events.EventList.Cast<Event>()
                            .Where(e => e.EventDataType == SurveyDataType.SurveyPoint)
                            .Select(e => e.EventData as SurveyPoint)
                            .Count(p => p != null && p.SpeciesInfo?.Species == null);
                        if (countSinglePointsMissingSpecies > 0)
                        {
                            showMissingSpeciesInfoInfoBar = true;
                        }
                    }
                }
            }

            if (!infoBarSpeciesInfoMissingDismissed/*User Dismissed Already*/  && showMissingSpeciesInfoInfoBar)
            {
                // Set the InfoBar message text
                // One or more Measurements, 3D Points or Single Point are missing their species information.
                int total = countMeasurementPointsMissingSpecies + countStereoPointsMissingSpecies + countSinglePointsMissingSpecies;
                string message;

                StringBuilder sb = new();
                if (countMeasurementPointsMissingSpecies > 1)
                    sb.Append($"{countMeasurementPointsMissingSpecies} Measurements");
                else
                    sb.Append($"{countMeasurementPointsMissingSpecies} Measurement");

                if (countStereoPointsMissingSpecies > 0)
                {
                    if (sb.Length > 0)
                        sb.Append(", ");

                    if (countStereoPointsMissingSpecies > 1)
                        sb.Append($"{countStereoPointsMissingSpecies} 3D Points");
                    else
                        sb.Append($"{countStereoPointsMissingSpecies} 3D Point");
                }
                if (countSinglePointsMissingSpecies > 0)
                {
                    if (sb.Length > 0)
                        sb.Append(", ");

                    if (countSinglePointsMissingSpecies > 1)
                        sb.Append($"{countSinglePointsMissingSpecies} Single Points");
                    else
                        sb.Append($"{countSinglePointsMissingSpecies} Single Point");
                }

                if (total >  1)
                    message = sb.ToString() + $" are missing their species information.";
                else
                    message = sb.ToString() + $" is missing it's species information.";

                // Show the info bar
                InfoBarSpeciesInfoMissing.Message = message;
                InfoBarSpeciesInfoMissing.IsOpen = true;
                InfoBarSpeciesInfoMissing.Visibility = Visibility.Visible;
            }
            else
            {
                // Hide the info bar
                InfoBarSpeciesInfoMissing.IsOpen = false;
                InfoBarSpeciesInfoMissing.Visibility = Visibility.Collapsed;
            }
        }


        /// <summary>
        /// The method is called to display the 'RMS Rule Violation' info bar if necessary
        /// i.e. one of more Measurement or 3D Point events have violated the RMS rule
        /// information
        /// </summary>
        public void SetInfoBarRMSRuleViolation()
        {
            bool showRMSRuleViolationInfoInfoBar = false;
            int countMeasurementPointsMissingSpecies = 0;
            int countStereoPointsMissingSpecies = 0;

            if (surveyClass is not null)
            {
                countMeasurementPointsMissingSpecies = surveyClass.Data.Events.EventList.Cast<Event>()
                    .Where(e => e.EventDataType == SurveyDataType.SurveyMeasurementPoints)
                    .Select(e => e.EventData as SurveyMeasurement)
                    .Count(m => m != null && m.SurveyRulesCalc.SurveyRuleRMS is false);

                if (countMeasurementPointsMissingSpecies > 0)
                {
                    showRMSRuleViolationInfoInfoBar = true;
                }
                else
                {
                    countStereoPointsMissingSpecies = surveyClass.Data.Events.EventList.Cast<Event>()
                        .Where(e => e.EventDataType == SurveyDataType.SurveyStereoPoint)
                        .Select(e => e.EventData as SurveyStereoPoint)
                        .Count(s => s != null && s.SurveyRulesCalc.SurveyRuleRMS is false);
                    if (countStereoPointsMissingSpecies > 0)
                    {
                        showRMSRuleViolationInfoInfoBar = true;
                    }
                }
            }

            if (!infoBarRMSRuleViolationDismissed/*User Dismissed Already*/  && showRMSRuleViolationInfoInfoBar)
            {
                // Set the InfoBar message Text
                // One or more Measurements or 3D Points have RMS rule violations.
                int total = countMeasurementPointsMissingSpecies + countStereoPointsMissingSpecies;
                string message;

                if (total > 1)
                {
                    message = $"{total} Measurements or 3D Points have RMS rule violations.";
                }
                else
                {
                    if (countMeasurementPointsMissingSpecies == 1)
                        message = $"1 Measurement has a RMS rule violation.";
                    else
                        message = $"1 3D Points has a RMS rule violation.";
                }

                // Show the info bar
                InfoBarRMSRuleViolation.Message = message;
                InfoBarRMSRuleViolation.IsOpen = true;
                InfoBarRMSRuleViolation.Visibility = Visibility.Visible;
            }
            else
            {
                // Hide the info bar
                InfoBarRMSRuleViolation.IsOpen = false;
                InfoBarRMSRuleViolation.Visibility = Visibility.Collapsed;
            }
        }


        /// <summary>
        /// This function is used to open a survey from a file path. It handles a request 
        /// from app.xaml.cs generated by the user activating the app with a file type
        /// </summary>
        /// <param name="surveyPath"></param>
        public async Task OpenSurveyFromFileAsync(string surveyPath)
        { 
            // First check if an existing survey is already open
            if (await CheckForOpenSurveyAndCloseAsync() == true)
            {
                int ret = await OpenSurveyAsync(surveyPath);

                if (ret == 0)
                {
                    // Add to Recent Surveys
                    AddToRecentSurveys(surveyPath);
                    UpdateRecentSurveysMenu();

                    // Check if the preferred calibration data is the one being using for
                    // the current event measurements calculations
                    await CheckIfEventMeasurementsAreUpToDateAsync(false/*recalculate only if necessary*/);
                }
                else
                { 
                    report.Warning("", $"OpenSurveyFromFile: OpenSurvey() failed, survey path:{surveyPath}, return = {ret}");
                }
            
                // Enable/Disable menu items based on the current survey state
                SetMenuStatusBasedOnSurveyState();
            }
        }


        /// <summary>
        /// Return the SurveyRulesClass from the current open survey
        /// </summary>
        /// <returns>null is no survey</returns>
        public SurveyRulesClass? GetSurveyRulesClass()
        {
            return surveyClass!.Data.SurveyRules;
        }


        ///
        /// EVENTS
        /// 


        /// <summary>
        /// Event raised when the AppTitleBar is loaded, used to set the interactive regions in 
        /// the title bar area which allowed the <MenuBar> (which is on the title bar) to operate
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
        /// the title bar area which allowed the <MenuBar> (which is on the title bar) to operate
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
        /// Window close has been requested by user, check for open Surveys
        /// </summary>
        /// <returns></returns>
        private void AppWindow_Closing(object sender, AppWindowClosingEventArgs e) => _ = AppWindowClosingAsync(e);
        private async Task AppWindowClosingAsync(AppWindowClosingEventArgs e)
        {
            Debug.WriteLine("AppWindow_Closing Entered");

            // First: check unsaved survey (may show dialog)
            bool canClose = await CheckForOpenSurveyAndCloseAsync();
            if (!canClose)
            {
                e.Cancel = true;
                Debug.WriteLine("AppWindow_Closing canceled by user");
                return;
            }

            // Perform unified shutdown
            await ShutdownAsync();

            e.Cancel = false;
            Debug.WriteLine("AppWindow_Closing Exit");
        }
        

        /// <summary>
        /// Used to set the unsaved data indicated in the title bar
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Survey_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Survey.IsDirty))
            {
                if (surveyClass is not null)
                {
                    if (surveyClass.IsDirty)
                        SetTitleSaveStatus("Unsaved");
                    else
                        SetTitleSaveStatus("");
                }
            }
        }


        /// <summary>
        /// Create a new stereo fish survey 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSurveyNewStereo_Click(object sender, RoutedEventArgs e) => _ = FileSurveyNewStereoClickAsync();
        private async Task FileSurveyNewStereoClickAsync()
        {
            bool ret;

            if (surveyClass is null)
            {
                ret = await SurveyCreateAllTypesAsync(Survey.SurveyType.StereoFish);

                if (ret == false)
                {
                    report.Warning("", $"FileSurveyNewStereo_Click: SurveyCreateAllTypes() failed");
                }
            }
        }


        /// <summary>
        /// Create a new mono fish survey 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSurveyNewMono_Click(object sender, RoutedEventArgs e) => _ = FileSurveyNewMonoClickAsync();
        private async Task FileSurveyNewMonoClickAsync()
        {
            bool ret;

            if (surveyClass is null)
            {
                ret = await SurveyCreateAllTypesAsync(Survey.SurveyType.MonoFish);

                if (ret == false)
                {
                    report.Warning("", $"FileSurveyNewMono_Click: SurveyCreateAllTypes() failed");
                }
            }
        }


        /// <summary>
        /// Create a new benthic survey
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSurveyNewBenthic_Click(object sender, RoutedEventArgs e) => _ = FileSurveyNewBenthicClickAsync();
        private async Task FileSurveyNewBenthicClickAsync()
        {
            bool ret;

            if (surveyClass is null)
            {
                ret = await SurveyCreateAllTypesAsync(Survey.SurveyType.MonoBenthic);

                if (ret == false)
                {
                    report.Warning("", $"FileSurveyNewBenthic_Click: SurveyCreateAllTypes() failed");
                }
            }
        }


        /// <summary>
        /// Open an existing survey file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSurveyOpen_Click(object sender, RoutedEventArgs e) => _ = FileSurveyOpenClickAsync();
        private async Task FileSurveyOpenClickAsync()
        {
            // First check if an existing survey is already open
            if (await CheckForOpenSurveyAndCloseAsync() == true)
            {
                // Show dialog to find the survey file to open
                string surveyFolder = SettingsManagerLocal.SurveyFolder is null ? "" : SettingsManagerLocal.SurveyFolder;
                if (string.IsNullOrEmpty(surveyFolder) == true)
                    surveyFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);


                // Create the file picker object
                FileOpenPicker openPicker = new()
                {
                    ViewMode = PickerViewMode.Thumbnail, // Can be List or Thumbnail
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary
                };

                // Add file type filters
                openPicker.FileTypeFilter.Add(".survey");

                // Associate the file picker with the current window
                IntPtr hWnd = WindowNative.GetWindowHandle(this/*???App.MainWindow*/);
                InitializeWithWindow.Initialize(openPicker, hWnd);

                // Show the picker to the user
                StorageFile file = await openPicker.PickSingleFileAsync();

                // If a file was picked, handle it
                if (file is not null)
                {
                    int ret = await OpenSurveyAsync(file.Path);

                    if (ret == 0)
                    {
                        // Add to Recent Surveys
                        AddToRecentSurveys(file.Path);
                        UpdateRecentSurveysMenu();

                        // Check if the preferred calibration data is the one being using for
                        // the current event measurements calculations
                        await CheckIfEventMeasurementsAreUpToDateAsync(false/*recalculate only if necessary*/);
                    }
                    else
                    { 
                        report.Warning("", $"FileSurveyOpen_Click: OpenSurvey() failed, survey path:{file.Path}, return = {ret}");
                    }
                }

                // Enable/Disable menu items based on the current survey state
                SetMenuStatusBasedOnSurveyState();
            }
        }


        /// <summary>
        /// Save the currently open survey file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSurveySave_Click(object? sender, RoutedEventArgs? e) => _ = FileSurveySaveClickAsync();
        private async Task FileSurveySaveClickAsync()
        {
            int ret;

            ret = await FileSurveySaveOrSaveAsAsync();

            if (ret != 0)
            {
                report.Warning("", $"FileSurveySave_Click: FileSurveySaveOrSaveAs() failed, return = {ret}");
            }
        }


        /// <summary>
        /// Save the currently open survey file with a new name
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSurveySaveAs_Click(object? sender, RoutedEventArgs? e) => _ = FileSurveySaveAsAsync();
        private async Task FileSurveySaveAsAsync()
        {
            int ret;

            ret = await SaveAsSurveyAsync();

            if (ret != 0)
            {
                report.Warning("", $"FileSurveySave_Click: FileSurveySaveOrSaveAs() failed, return = {ret}");
            }

            report.Save();

            SetMenuStatusBasedOnSurveyState();
        }


        /// <summary>
        /// Used to open a selected recent survey file from the 'Recent Surveys' sub menu
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileRecentSurvey_Click(object sender, RoutedEventArgs e) => _ = FileRecentSurveyClickAsync(sender);
        private async Task FileRecentSurveyClickAsync(object sender)
        {
            var menuItem = sender as MenuFlyoutItem;
            if (menuItem is not null)
            {
                if (menuItem.Tag is string filePath)
                {
                    // First check if an existing survey is already open
                    if (await CheckForOpenSurveyAndCloseAsync() == true)
                    {
                        // Open survey in the regular way
                        int ret = await OpenSurveyAsync(filePath);

                        if (ret == 0)
                        {
                            // Force to the top of the recent surveys list
                            // Note this survey is definitely in the recent survey list
                            // but may be the top item. As the new last opened survey it
                            // should be top
                            AddToRecentSurveys(filePath);
                            UpdateRecentSurveysMenu();

                            // Check if the preferred calibration data is the one being using for
                            // the current event measurements calculations
                            await CheckIfEventMeasurementsAreUpToDateAsync(false/*recalculate only if necessary*/);
                        }
                        else if (ret != -999/*User aborted*/)
                        {
                            // Report the missing survey file
                            // Survey needs to be saved before a frame can be saved
                            var warningIcon = new SymbolIcon(Symbol.Important); // Symbol.Important represents an exclamation

                            // Create the ContentDialog instance
                            var dialog = new ContentDialog
                            {
                                Title = $"Survey file missing",
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
                SetMenuStatusBasedOnSurveyState();
            }
        }

        /// <summary>
        /// Close the currently open survey file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSurveyClose_Click(object sender, RoutedEventArgs e) => _ = FileSurveyCloseClickAsync();
        private async Task FileSurveyCloseClickAsync()
        {
            await CheckForOpenSurveyAndCloseAsync();
        }


        /// <summary>
        /// Import calibration data into the survey
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileImportCalibration_Click(object sender, RoutedEventArgs e) => _ = FileImportCalibrationClickAsync();
        private async Task FileImportCalibrationClickAsync()
        {
            // Create the file picker object
            FileOpenPicker openPicker = new()
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };

            // Add file type filters
            openPicker.FileTypeFilter.Add(".calib");
            openPicker.FileTypeFilter.Add(".json");
            openPicker.FileTypeFilter.Add(".jsn");

            // Associate the file picker with the current window
            IntPtr hWnd = WindowNative.GetWindowHandle(this/*App.MainWindow*/);
            InitializeWithWindow.Initialize(openPicker, hWnd);

            // Show the picker and allow multiple file selection
            StorageFile file = await openPicker.PickSingleFileAsync();

            // Check if files were picked and handle them
            if (file is not null && this.surveyClass is not null)
            {
                string? calibrationFileSpec = file.Path;

                // Load the calibration file
                CalibrationData calibrationData = new();
                int ret = calibrationData.LoadFromFile(calibrationFileSpec);


                if (ret == 0)
                {
                    bool? leftCameraIDMatch = null;
                    bool? rightCameraIDMatch = null;
                    bool cameraWrongWayRound = false;

                    // Check for GoPro serial numbers in the calibration data and the media class
                    if (!string.IsNullOrEmpty(calibrationData.LeftCameraCalibration.CameraID) &&
                        !string.IsNullOrEmpty(surveyClass.Data.Media.LeftCameraID))
                    {
                        if (calibrationData.LeftCameraCalibration.CameraID == surveyClass.Data.Media.LeftCameraID)
                            leftCameraIDMatch = true;
                        else
                            leftCameraIDMatch = false;
                    }
                    if (!string.IsNullOrEmpty(calibrationData.RightCameraCalibration.CameraID) &&
                        !string.IsNullOrEmpty(surveyClass.Data.Media.RightCameraID))
                    {
                        if (calibrationData.RightCameraCalibration.CameraID == surveyClass.Data.Media.RightCameraID)
                            rightCameraIDMatch = true;
                        else
                            rightCameraIDMatch = false;
                    }
                    // Check for camera wrong way round
                    if (leftCameraIDMatch is not null && rightCameraIDMatch is not null &&
                        !(bool)leftCameraIDMatch && !(bool)rightCameraIDMatch)
                    {
                        if (calibrationData.LeftCameraCalibration.CameraID == surveyClass.Data.Media.RightCameraID &&
                            calibrationData.RightCameraCalibration.CameraID == surveyClass.Data.Media.LeftCameraID)
                        {
                            cameraWrongWayRound = true;
                        }
                    }

                    string text = "";
                    bool warnUser = false;
                    if (leftCameraIDMatch is not null && rightCameraIDMatch is not null &&
                        !(bool)leftCameraIDMatch && !(bool)rightCameraIDMatch)
                    {
                        // Check if camera are the wrong way round
                        if (!cameraWrongWayRound)
                        {
                            text = $"The GoPros used in the survey are different to those used for calibration. " +
                                   $"Any measurement results will be invalid. You need to either use the cameras " +
                                   $"used for the calibration or re-calibrate using the cameras used for this survey.\n\n" +
                                   $"The calibration cameras had the following serial numbers:\n\n" +
                                   $"Left: {calibrationData.LeftCameraCalibration.CameraID}\n" +
                                   $"Right: {calibrationData.RightCameraCalibration.CameraID}";
                            warnUser = true;
                        }
                        else
                        {
                            text = $"The GoPros used in the survey are the wrong way round (left camera on right side, right camera on left side) vs. the way they were used during calibration. " +
                                   $"Any measurement results will be invalid. You need to either redo the survey with the cameras swapped around " +
                                   $"or re-calibrate using the cameras the same way round that you had for this survey.\n\n" +
                                   $"The calibration cameras had the following serial numbers:\n\n" +
                                   $"Left: {calibrationData.LeftCameraCalibration.CameraID}\n" +
                                   $"Right: {calibrationData.RightCameraCalibration.CameraID}";
                            warnUser = true;
                        }
                    }
                    else if (leftCameraIDMatch is not null && rightCameraIDMatch is not null &&
                        (bool)leftCameraIDMatch && !(bool)rightCameraIDMatch)
                    {
                        text = $"The right GoPro isn't the same as the right GoPro used for Calibration (left GoPro was the same). " +
                               $"Any measurement results will be invalid. You need to either use the cameras " +
                                   $"used for the calibration or re-calibrate using the cameras used for this survey.\n\n" +
                                   $"The calibration cameras had the following serial numbers:\n\n" +
                                   $"Left: {calibrationData.LeftCameraCalibration.CameraID}\n" +
                                   $"Right: {calibrationData.RightCameraCalibration.CameraID}";
                        warnUser = true;
                    }
                    else if (leftCameraIDMatch is not null && rightCameraIDMatch is not null &&
                             !(bool)leftCameraIDMatch && (bool)rightCameraIDMatch)
                    {
                        text = $"The left GoPro isn't the same as the right GoPro used for Calibration (right GoPro was the same). " +
                               $"Any measurement results will be invalid. You need to either use the cameras " +
                                   $"used for the calibration or re-calibrate using the cameras used for this survey.\n\n" +
                                   $"The calibration cameras had the following serial numbers:\n\n" +
                                   $"Left: {calibrationData.LeftCameraCalibration.CameraID}\n" +
                                   $"Right: {calibrationData.RightCameraCalibration.CameraID}";
                        warnUser = true;
                    }

                    if (warnUser == true)
                    {
                        // The GoPro serial number in the calibration data does not match the one in the survey
                        // Cancel Only
                        ContentDialog confirmationDialog = new()
                        {
                            Title = "Survey & Calibration Camera Mismatch",
                            Content = text,
                            CloseButtonText = "Cancel",
                            // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                            XamlRoot = this.Content.XamlRoot
                        };
                        // Display the dialog
                        ContentDialogResult resultDlg = await confirmationDialog.ShowAsync();

                        if (resultDlg == ContentDialogResult.None)
                        {
                            ret = -1;
                        }
                    }
                }


                if (ret == 0)
                {
                    bool removeAllCalibs = false;
                    int removeThisCalibIndex = -1;
                    int makeThisCalibPreferred = -1;
                    bool addNewCalib = false;
                    string? primaryButtonText = null;
                    string? secondaryButtonText = null;
                    string message = "";

                    // Check if we are already storing this calibration date in the survey file                        
                    var result = surveyClass.IsInCalibrationDataList(calibrationData, out int index);
                    if (result == Survey.CalibrationDataListResult.Found)
                    {
                        if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == false)
                        {
                            // We are only storing one calibration result and we already have this one so inform user we will ignore
                            // Cancel Only
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file. No action will be taken.";
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true && surveyClass.Data.Calibration.PreferredCalibrationDataIndex == index)
                        {
                            // We are storing multiple calibration results and this is the preferred one so inform user we will ignore
                            // Cancel Only
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file and it is the preferred calibration so no action will be taken.";
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true && surveyClass.Data.Calibration.PreferredCalibrationDataIndex != index)
                        {
                            // We are storing multiple calibration results and this is not the preferred one so ask user if they want this to be the preferred calibration
                            // OK/Cancel
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file but it is not the preferred calibration. Do you would like to make it the preferred calibration?";
                            primaryButtonText = "OK";
                        }

                        // Ask the user
                        ContentDialog confirmationDialog = new()
                        {
                            Title = "Import Calibration Data",
                            Content = message,
                            PrimaryButtonText = primaryButtonText,
                            CloseButtonText = "Cancel",

                            // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                            XamlRoot = this.Content.XamlRoot
                        };

                        // Display the dialog
                        ContentDialogResult resultDlg = await confirmationDialog.ShowAsync();


                        if (resultDlg == ContentDialogResult.Primary)
                        {
                            // Make preferred calibration
                            makeThisCalibPreferred = index;
                        }
                    }
                    else if (result == Survey.CalibrationDataListResult.FoundButDescriptionDiffer)
                    {
                        if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == false)
                        {
                            // We are only storing one calibration result and we already have this one but under a different Description so ask the user if the Description should be updated
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file but with a different Description. Do you want to update the Description?";
                            primaryButtonText = "OK";
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true && surveyClass.Data.Calibration.PreferredCalibrationDataIndex == index)
                        {
                            // We are storing multiple calibration results and this is the preferred one so info user we will ignore
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file but with a different Description. Do you want to update the Description?";
                            primaryButtonText = "OK";
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true && surveyClass.Data.Calibration.PreferredCalibrationDataIndex != index)
                        {
                            // We are storing multiple calibration results and this is not the preferred one so ask user if they want this to be the preferred calibration
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file but with a different Description and is not the preferred calibration. Press 'Yes' to update the Description and make it the preferred calibration or 'No' to just update the Description?";
                            primaryButtonText = "Yes";
                            secondaryButtonText = "No";
                        }

                        // Ask the user
                        ContentDialog confirmationDialog = new()
                        {
                            Title = "Import Calibration Data",
                            Content = message,
                            PrimaryButtonText = primaryButtonText,
                            SecondaryButtonText = secondaryButtonText,
                            CloseButtonText = "Cancel",

                            // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                            XamlRoot = this.Content.XamlRoot
                        };

                        // Display the dialog
                        ContentDialogResult resultDlg = await confirmationDialog.ShowAsync();

                        if (secondaryButtonText is null && resultDlg == ContentDialogResult.Primary)
                        {
                            // Make preferred calibration
                            makeThisCalibPreferred = index;
                        }
                        else if (secondaryButtonText is not null && resultDlg == ContentDialogResult.Primary)
                        {
                            // Update Description and make preferred calibration
                            surveyClass.Data.Calibration.CalibrationDataList[(int)surveyClass.Data.Calibration.PreferredCalibrationDataIndex].Description = calibrationData.Description;
                            makeThisCalibPreferred = index;
                        }
                        else if (secondaryButtonText is not null && resultDlg == ContentDialogResult.Secondary)
                        {
                            // Update Description only
                            surveyClass.Data.Calibration.CalibrationDataList[(int)surveyClass.Data.Calibration.PreferredCalibrationDataIndex].Description = calibrationData.Description;
                        }
                    }
                    else if (result == Survey.CalibrationDataListResult.NotFound)
                    {
                        if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == false)
                        {
                            if (surveyClass.Data.Calibration.CalibrationDataList.Count == 0)
                            {
                                makeThisCalibPreferred = int.MaxValue;      // Basically if we are added a new calibration any non -1 value will make it the preferred calibration
                                addNewCalib = true;
                            }
                            else if (surveyClass.Data.Calibration.PreferredCalibrationDataIndex == 0 && surveyClass.Data.Calibration.CalibrationDataList.Count == 1)
                            {
                                // We are only storing one calibration and we don't have this one so ask the user if they want to remove the existing one and add this one
                                message = $"Are you sure you want to replace '{surveyClass.Data.Calibration.CalibrationDataList[0].Description}' with '{calibrationData.Description}'?";
                                primaryButtonText = "OK";

                                // Ask the user
                                ContentDialog confirmationDialog = new()
                                {
                                    Title = "Import Calibration Data",
                                    Content = message,
                                    PrimaryButtonText = primaryButtonText,
                                    SecondaryButtonText = secondaryButtonText,
                                    CloseButtonText = "Cancel",

                                    // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                                    XamlRoot = this.Content.XamlRoot
                                };

                                // Display the dialog
                                ContentDialogResult resultDlg = await confirmationDialog.ShowAsync();

                                if (resultDlg == ContentDialogResult.Primary)
                                {
                                    // Remove the existing calibration
                                    removeAllCalibs = true;
                                    addNewCalib = true;
                                    makeThisCalibPreferred = int.MaxValue;      // Basically if we are added a new calibration any non -1 value will make it the preferred calibration
                                }
                            }
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true)
                        {
                            // We are storing multiple calibrations and already have at less one storage. Ask the user if they want this new one to be the preferred calibration
                            message = $"Do you want this new calibration data '{calibrationData.Description}' to be the preferred calibration?";
                            primaryButtonText = "Yes";
                            secondaryButtonText = "No";


                            // Ask the user
                            ContentDialog confirmationDialog = new()
                            {
                                Title = "Import Calibration Data",
                                Content = message,
                                PrimaryButtonText = primaryButtonText,
                                SecondaryButtonText = secondaryButtonText,
                                CloseButtonText = "Cancel",

                                // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                                XamlRoot = this.Content.XamlRoot
                            };

                            // Display the dialog
                            ContentDialogResult resultDlg = await confirmationDialog.ShowAsync();


                            if (resultDlg == ContentDialogResult.Primary)
                            {
                                makeThisCalibPreferred = int.MaxValue;      // Basically if we are added a new calibration any non -1 value will make it the preferred calibration
                                addNewCalib = true;
                            }
                            else if (resultDlg == ContentDialogResult.Secondary)
                                addNewCalib = true;
                        }
                    }


                    // Do the action
                    if (removeAllCalibs == true)
                    {
                        // Remove all calibrations
                        surveyClass.Data.Calibration.CalibrationDataList?.Clear();
                        surveyClass.Data.Calibration.PreferredCalibrationDataIndex = -1;
                    }
                    else if (removeThisCalibIndex >= 0)
                    {
                        // Remove this calibration
                        surveyClass.Data.Calibration.CalibrationDataList!.RemoveAt(removeThisCalibIndex);
                        if (surveyClass.Data.Calibration.PreferredCalibrationDataIndex == removeThisCalibIndex)
                            surveyClass.Data.Calibration.PreferredCalibrationDataIndex = -1;
                    }

                    if (addNewCalib == true)
                    {
                        if (surveyClass.Data.Calibration.CalibrationDataList is not null && calibrationData is not null)
                        {
                            // Add the new calibration
                            surveyClass.Data.Calibration.CalibrationDataList.Add(calibrationData);
                            if (makeThisCalibPreferred != -1)
                                // Make this the preferred calibration
                                surveyClass.Data.Calibration.PreferredCalibrationDataIndex = surveyClass.Data.Calibration.CalibrationDataList.Count - 1;
                        }
                    }
                    else if (makeThisCalibPreferred != -1)
                    {
                        // Make this the preferred calibration
                        surveyClass.Data.Calibration.PreferredCalibrationDataIndex = makeThisCalibPreferred;
                    }
                }

                // Load the calibration data to the Stereo Projection class 
                stereoProjection.SetCalibrationData(surveyClass.Data.Calibration);

                // Using the left player get the current frame size (if any)
                SetCalibratedIndicator(MediaPlayerLeft.FrameWidth, MediaPlayerLeft.FrameHeight);


                // Check if the preferred calibration data is the one being using for
                // the current event measurements calculations
                await CheckIfEventMeasurementsAreUpToDateAsync(false/*recalculate only if necessary*/);


                // Display the missing calibration warning InfoBar if necessary
                SetInfoBarCalibrationMissing();

                //// Inform the two media players of the MeasurementPointControl instances that allow the user to add measurement points to the media
                //LMediaPlayer.SetMeasurementPointControl(measurementPointControl);
                //RMediaPlayer.SetMeasurementPointControl(measurementPointControl);
            }
        }


        /// <summary>
        /// Display the settings windows
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSettings_Click(object sender, RoutedEventArgs e) => _ = FileSettingsClickAsync();
        private async Task FileSettingsClickAsync()
        {
            await ShowSettingsWindowAsync();
        }


        /// <summary>
        /// Exit the app
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileExit_Click(object sender, RoutedEventArgs e) => _ = FileExitClickAsync();
        private async Task FileExitClickAsync()
        {
            await CheckForOpenSurveyAndCloseAsync();

            SetTitle("");
            SetLockUnlockIndicator(null, null);

            Application.Current.Exit();
        }


        /// <summary>
        /// Users wants to lock or unlock the media players. 
        /// i.e. synchronize or unlock the synchronization of the media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InsertLockUnlockMediaPlayers_Click(object sender, RoutedEventArgs e) => _ = InsertLockUnlockMediaPlayersClickAsync();
        private async Task InsertLockUnlockMediaPlayersClickAsync()
        {
            if (!mediaStereoController.IsPlaying())
            {
                if (surveyClass is not null && surveyClass.Data.Sync.IsSynchronized == false)
                {
                    // Wait for things to settle i.e. any pending MediPlayer directed plays or pauses to have completed
                    // note. calling MediaPlayer.Play or Pause with the MediaTimelineController engaged will cause an
                    // exception
                    await Task.Delay(500);

                    // Action flags
                    bool reEnable = false;
                    bool newPosition = false;

                    // Check if sync offset is already present and just needs enabling
                    if (surveyClass is not null)
                    {
                        if (surveyClass.Data.Sync.ActualTimeSpanOffsetLeft != TimeSpan.Zero || 
                            surveyClass.Data.Sync.ActualTimeSpanOffsetRight != TimeSpan.Zero)
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

                    }

                    // Lock the left and right media controllers
                    if (surveyClass is not null && MediaPlayerLeft is not null && MediaPlayerRight is not null &&
                        MediaPlayerLeft.Position is not null && MediaPlayerRight.Position is not null)
                    {
                        if (reEnable)
                        {
                            surveyClass.Data.Sync.IsSynchronized = true;
                        }
                        else if (newPosition)
                        {
                            surveyClass.Data.Sync.IsSynchronized = true;
                            surveyClass.Data.Sync.TimeSpanOffset = (TimeSpan)MediaPlayerRight.Position - (TimeSpan)MediaPlayerLeft.Position;
                            surveyClass.Data.Sync.ActualTimeSpanOffsetLeft = (TimeSpan)MediaPlayerLeft.Position;
                            surveyClass.Data.Sync.ActualTimeSpanOffsetRight = (TimeSpan)MediaPlayerRight.Position;

                        }
                    }

                    // Engage the MediaTimelineController
                    if (surveyClass is not null && (reEnable || newPosition))
                    {
                        await mediaStereoController.MediaLockMediaPlayersAsync(null/*current media positions*/, surveyClass.Data.Events.EventList);
                    }
                }
                else
                {
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
                        // Lock the left and right media controllers
                        if (surveyClass is not null && MediaPlayerLeft is not null && MediaPlayerRight is not null &&
                        MediaPlayerLeft.Position is not null && MediaPlayerRight.Position is not null)
                        {
                            surveyClass.Data.Sync.IsSynchronized = false;

                            // Don't remove the TimeSpanOffset, ActualTimeSpanOffsetLeft & ActualTimeSpanOffsetRight
                            // in case the user wants to sync again
                        }

                        mediaStereoController.MediaUnlockMediaPlayers();
                    }
                }
            }
            else
            {
                // DON'T await this method. I don't understand why but it causes the
                // dialog to be non-modal
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                await ShowCannotSynchronizedDialogAsync();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }

        }


        /// <summary>
        /// Insert a marker (as an Event) to indicate either the start or end of a survey within the movies
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InsertSurveyStartStopMarker_Click(object sender, RoutedEventArgs e) => _ = InsertSurveyStartStopMarkerClickAsync();
        private async Task InsertSurveyStartStopMarkerClickAsync()
        {
            try
            {
                mediaStereoController.GetFullMediaPosition(out TimeSpan positionTimelineController, out TimeSpan leftPosition, out TimeSpan rightPosition);

                await transectMarkerManager.AddMarkerAsync(eventsControl,
                                                           positionTimelineController,
                                                           leftPosition, rightPosition);
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"InsertSurveyStartStopMarker_Click(): Failed to insert a survey transect marker, {ex.Message}");
            }
        }


        /// <summary>
        /// InfoBar import calibration button click event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ImportCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            FileImportCalibration_Click(null!, null!);
        }


        /// <summary>
        /// InfoBar Go To first event with missing species info
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GoToFirstMissingSpeciesEvent_Click(object sender, RoutedEventArgs e)
        {
            if (surveyClass is not null)
            {
                Event? firstMissingSpeciesEvent = surveyClass.Data.Events.EventList
                                .Cast<Event>() // or .AsEnumerable()/.ToList() depending on your context
                                .FirstOrDefault(e =>
                                    (e.EventDataType == SurveyDataType.SurveyMeasurementPoints && (e.EventData as SurveyMeasurement)?.SpeciesInfo?.Species == null) ||
                                    (e.EventDataType == SurveyDataType.SurveyStereoPoint && (e.EventData as SurveyStereoPoint)?.SpeciesInfo?.Species == null) ||
                                    (e.EventDataType == SurveyDataType.SurveyPoint && (e.EventData as SurveyPoint)?.SpeciesInfo?.Species == null));

                if (firstMissingSpeciesEvent is not null)
                {
                    // Go to the frame in the media player and scroll to the event in the events list
                    eventsControl?.GoToEvent(firstMissingSpeciesEvent);
                }
            }
        }


        /// <summary>
        /// InfoBar Go To first event with RMS Rule Violation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GoToFirstRMSRuleViolationEvent_Click(object sender, RoutedEventArgs e)
        {
            if (surveyClass is not null)
            {
                Event? firstRMSRuleViolationEvent = surveyClass.Data.Events.EventList
                                .Cast<Event>() // or .AsEnumerable()/.ToList() depending on your context
                                .FirstOrDefault(e =>
                                    (e.EventDataType == SurveyDataType.SurveyMeasurementPoints && (e.EventData as SurveyMeasurement)?.SurveyRulesCalc.SurveyRuleRMS == false) ||
                                    (e.EventDataType == SurveyDataType.SurveyStereoPoint && (e.EventData as SurveyStereoPoint)?.SurveyRulesCalc.SurveyRuleRMS == false));

                if (firstRMSRuleViolationEvent is not null)
                {
                    // Go to the frame in the media player and scroll to the event in the events list
                    eventsControl?.GoToEvent(firstRMSRuleViolationEvent);
                }
            }
        }


        /// <summary>
        /// Export data
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileExport_Click(object sender, RoutedEventArgs e) => _ = FileExportAsync();
        
        private int exportWindowEntryCount = 0;
        private async Task FileExportAsync()
        {

            try
            {
                int entryCount = Interlocked.Increment(ref exportWindowEntryCount);
                // Make sure we only open the settings window once.
                // This can happen if the survey and movies are loaded and the user clicks the settings a few times.
                if (entryCount == 1)
                {
                    // Initialize if necessary
                    var dialog = new BulkSurveyExportDialog(report, mediaStereoController.speciesSelector.speciesCodeList);

                    // Get the HWND (window handle) for both windows
                    IntPtr mainWindowHandle = WindowNative.GetWindowHandle(this);
                    IntPtr settingsWindowHandle = WindowNative.GetWindowHandle(dialog);

                    // Get the AppWindow instances for both windows
                    AppWindow mainAppWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(mainWindowHandle));

                    // Disable the main window by setting it inactive
                    SetWindowEnabled(mainWindowHandle, false);

                    // Activate export window
                    dialog.Activate(); // Shows the window non-modally

                    // Important not to block the UI thread.
                    // We're still waiting for the Closed event.
                    // The Closed handler runs on the UI thread, allowing WinUIEx to persist the window position.
                    var tcs = new TaskCompletionSource();

                    void OnClosed(object sender, WindowEventArgs args)
                    {
                        dialog.Closed -= OnClosed;
                        tcs.SetResult();
                    }

                    dialog.Closed += OnClosed;

                    await tcs.Task;


                    // Re-enable the main window after closing settings
                    SetWindowEnabled(mainWindowHandle, true);
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during the process
                Debug.WriteLine($"MainWindow.FileExport_Click Error showing bulk export window: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref exportWindowEntryCount);
            }
        }


        /// <summary>
        /// This method is called to check that suitable calibration data is available for the current frame size and that 
        /// it is set as the preferred calibration data.  If that isn't the case the method to see if there is any calibration data
        /// the support the current frame size but is not set to be preferred.  If that is the case the user is asked if they want to
        /// make that calibration data the preferred calibration data.
        /// </summary>
        /// <returns></returns>
        internal async Task<bool> CheckIfMeasurementSetupIsReadyAsync()
        {
            bool ready = false;

            if (surveyClass is not null)
            {
                CalibrationClass calibrationClass = surveyClass.Data.Calibration;
                if (calibrationClass is not null)
                {
                    int frameWidth = MediaPlayerLeft.FrameWidth;
                    int frameHeight = MediaPlayerLeft.FrameHeight;

                    // Get the preferred calibration data ensuring it is for this frame size
                    CalibrationData? calibrationDataPreferred = calibrationClass.GetPreferredCalibationData(frameWidth, frameHeight);
                    if (calibrationDataPreferred is not null)
                        ready = true;


                    // Check if suitable preferred calibration data was found
                    if (!ready)
                    {
                        // Parse the calibration data to see if there is any that supports the current frame size
                        for (int i = 0; i < calibrationClass.CalibrationDataList.Count; i++)
                        {
                            // Ignore the preferred calibration data as that has been checked above
                            if (i == calibrationClass.PreferredCalibrationDataIndex)
                                continue;

                            CalibrationData calibrationData = calibrationClass.CalibrationDataList[i];
                            if (calibrationData.FrameSizeCompare(frameWidth, frameHeight))
                            {
                                // Ask the user if they want to make this the preferred calibration data
                                string message = $"The calibration data '{calibrationData.Description}' supports the current frame size. Do you want to make it the preferred calibration data?";
                                string primaryButtonText = "Yes";
                                string secondaryButtonText = "No";

                                // Ask the user
                                ContentDialog confirmationDialog = new()
                                {
                                    Title = "Preferred Calibration Data",
                                    Content = message,
                                    PrimaryButtonText = primaryButtonText,
                                    SecondaryButtonText = secondaryButtonText,
                                    CloseButtonText = "Cancel",

                                    // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                                    XamlRoot = this.Content.XamlRoot
                                };

                                // Display the dialog
                                ContentDialogResult result = await confirmationDialog.ShowAsync();

                                if (result == ContentDialogResult.Primary)
                                {
                                    // Make this the preferred calibration
                                    surveyClass.Data.Calibration.PreferredCalibrationDataIndex = i;
                                    ready = true;
                                    SetCalibratedIndicator(frameWidth, frameHeight);
                                }
                            }
                        }

                        // Check if the preferred calibration data is the one being using for
                        // the current event measurements calculations
                        await CheckIfEventMeasurementsAreUpToDateAsync(false/*recalculate only if necessary*/);

                    }
                }
            }

            return ready;
        }


        /// <summary>
        /// Left/Right MediaPlayer/MediaControls grid area mouse wheel event
        /// We want to capture the mouse wheel event if the mouse if over the media player or the 
        /// media controls area
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>        
        private void LeftSubGrid_MouseWheel(object sender, PointerRoutedEventArgs e)
        {
            // Manage the mag window size/zoom
            MediaPlayerLeft.MouseWheelEvent(sender, e);
            if (e.Handled == true)
                return;

            if (SettingsManagerLocal.MouseWheelFrameMoveEnabled)
            {
                // Move the frame
                MediaControlPrimary.MouseWheelEvent(sender, e);
            }
        }
        private void RightSubGrid_MouseWheel(object sender, PointerRoutedEventArgs e)
        {
            // Manage the mag window size/zoom
            MediaPlayerRight.MouseWheelEvent(sender, e);
            if (e.Handled == true)
                return;

            if (SettingsManagerLocal.MouseWheelFrameMoveEnabled)
            {
                // Move the frame
                MediaControlSecondary.MouseWheelEvent(sender, e);
            }
        }


        /// <summary>
        /// Keyboard accelerator to dump all the properties 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void HelpDiagsDump_Click(object sender, RoutedEventArgs e)
        {
            this.DumpAllProperties();
            mediaStereoController.DumpAllProperties();
            MediaPlayerLeft.DumpAllProperties();
            MediaPlayerRight.DumpAllProperties();
            surveyClass?.Data.DumpAllProperties(report);

            // To Be Completed            
            //???eventsControl.DumpAllProperties();
            //???measurementPointControl.DumpAllProperties();
            //???stereoProjection.DumpAllProperties();

            report?.Save();
        }


        /// <summary>
        /// Keyboard accelerator for testing code
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void HelpTesting_Click(object sender, RoutedEventArgs e) => _ = HelpTestingAsync();
        private async Task HelpTestingAsync()
        {
            // Show the testing window
            await ShowSurveyorTestingsWindowAsync();
        }


        /// <summary>
        /// Display the Surveyor Testing window
        /// </summary>
        private int surveyorTestingEntryCount = 0;
        private async Task ShowSurveyorTestingsWindowAsync()
        {
            try
            {
                // Atomic
                int entryCount = Interlocked.Increment(ref surveyorTestingEntryCount);

                // Make sure we only open the window once.
                if (entryCount == 1)
                {
                    SurveyorTesting testingWindow = new(mediator, this, report);

                    // Get the HWND (window handle) for both windows
                    IntPtr mainWindowHandle = WindowNative.GetWindowHandle(this);
                    IntPtr testingWindowHandle = WindowNative.GetWindowHandle(testingWindow);

                    // Get the AppWindow instances for both windows
                    AppWindow mainAppWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(mainWindowHandle));
                   
                    // Disable the main window by setting it inactive
                    SetWindowEnabled(mainWindowHandle, false);

                    // Activate settings window
                    testingWindow.Activate();


                    // Important not to block the UI thread.
                    // We're still waiting for the Closed event.
                    // The Closed handler runs on the UI thread, allowing WinUIEx to persist the window position.
                    var tcs = new TaskCompletionSource();

                    void OnClosed(object sender, WindowEventArgs args)
                    {
                        testingWindow.Closed -= OnClosed;
                        tcs.SetResult();
                    }

                    testingWindow.Closed += OnClosed;

                    await tcs.Task;

                    // Re-enable the main window after closing settings
                    SetWindowEnabled(mainWindowHandle, true);
                }
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                report.Error("", $"Error showing SurveyorTesting.RunTestingDialog: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref surveyorTestingEntryCount);
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


        private void DownloadIndicator_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            int? itemRequiredDownload = internetQueue?.GetCount(Direction.Download, null, Status.Required);
            int? itemRequiredUpload = internetQueue?.GetCount(Direction.Upload, null, Status.Required);

            if (itemRequiredDownload is null || itemRequiredUpload is null)
            {
                DownloadIndicatorToolTip.Content = $"Downloading/Uploading from internet";
            }
            else
            {
                string downloadText = itemRequiredDownload == 1 ? "1 download item" : $"{itemRequiredDownload} download items";
                string uploadText = itemRequiredUpload == 1 ? "1 upload item" : $"{itemRequiredUpload} upload items";

                if (itemRequiredDownload != 0 && itemRequiredUpload != 0)
                {
                    DownloadIndicatorToolTip.Content = $"{downloadText}/{uploadText} remaining";
                }
                else if (itemRequiredDownload != 0)
                {
                    DownloadIndicatorToolTip.Content = $"{downloadText} remaining";
                }
                else
                {
                    // Therefore itemRequiredUpload != 0
                    DownloadIndicatorToolTip.Content = $"{uploadText} remaining";
                }                
            }
        }


        /// <summary>
        /// User dismissed the InfoBar warning about missing calibration data
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void InfoBarCalibrationMissing_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            // Remember the user dismissed the warning
            if (args.Reason == InfoBarCloseReason.CloseButton)
            {
                infoBarCalibrationMissingDismissed = true;
            }
        }


        /// <summary>
        /// User dismissed the InfoBar warning about missing species info
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void InfoBarSpeciesInfoMissing_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            // Remember the user dismissed the warning
            if (args.Reason == InfoBarCloseReason.CloseButton)
            {
                infoBarSpeciesInfoMissingDismissed = true;
            }
        }


        /// <summary>
        /// User dismissed the InfoBar warning about RMS Rule violations info
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void InfoBarRMSRuleViolation_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            // Remember the user dismissed the warning
            if (args.Reason == InfoBarCloseReason.CloseButton)
            {
                infoBarRMSRuleViolationDismissed = true;
            }
        }

        /// <summary>
        /// The ViewPorts that control the area the MediaPlayer/ImageFrame/CanvasFrame 
        /// changed physical dimensions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftViewbox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Get the new size of the <ViewBox>
            double newWidth = e.NewSize.Width;
            double newHeight = e.NewSize.Height;

            Debug.WriteLine($"[{DateTime.Now:hh:MM:yyyy HH:mm:sd.ff}] LeftViewbox_SizeChanged:{newWidth:F1}x{newHeight:F1}");
            MediaPlayerLeft.RenderedPixelScreenSizeChanged(newWidth, newHeight);
        }

        private void RightViewbox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Get the new size of the <ViewBox>
            double newWidth = e.NewSize.Width;
            double newHeight = e.NewSize.Height;

            Debug.WriteLine($"[{DateTime.Now:hh:MM:yyyy HH:mm:sd.ff}] RightViewbox_SizeChanged:{newWidth:F1}x{newHeight:F1}");
            MediaPlayerRight.RenderedPixelScreenSizeChanged(newWidth, newHeight);
        }


        /// <summary>
        /// MainWindow level keyboard handler.  Ensure MediaControl keys are always
        /// directed at the PrimaryMediaControl via MediaStereoController
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e) => _ = RootGridKeyDownAsync(e);
        private async Task RootGridKeyDownAsync(KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Space)
            {
                // Passed Space bar to MediaStereoController
                await mediaStereoController.SpaceKeyPressedAsync();
            }
        }


        /// <summary>
        /// EventsControl level keyboard handler.  Ensure MediaControl keys are always
        /// directed at the PrimaryMediaControl via MediaStereoController
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EventsControl_SpaceKeyPressed(object? sender, RoutedEventArgs e) => _ = EventsControlSpaceKeyPressedAsync();
        private async Task EventsControlSpaceKeyPressedAsync()
        {
            // Passed Space bar to MediaStereoController
            await mediaStereoController.SpaceKeyPressedAsync();
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Initiates the shutdown process for the application.
        /// </summary>
        /// <returns></returns>
        private int _shutdownStarted = 0;
        private async Task ShutdownAsync()
        {
            // Idempotent guard
            if (Interlocked.CompareExchange(ref _shutdownStarted, 1, 0) != 0)
                return;

            try
            {
                Debug.WriteLine("ShutdownAsync: begin");

                // Stop download spinner timer
                try
                {
                    if (DispatcherQueue.HasThreadAccess)
                        StopDownloadUploadSpinner();
                    else
                        _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, StopDownloadUploadSpinner);
                }
                catch { }

                // Unhook theme change (prevents callbacks on torn-down XAML)
                try
                {
                    if (Content is FrameworkElement fe)
                        fe.ActualThemeChanged -= OnActualThemeChanged;
                }
                catch (Exception ex) { Debug.WriteLine($"ShutdownAsync: theme unhook error {ex.Message}"); }

                // Flush/save report early (in case later steps fault)
                try { report.Save(); } catch (Exception ex) { Debug.WriteLine($"ShutdownAsync: report.Save error {ex.Message}"); }

                // Close open media & survey state (already prompts earlier)
                try
                {
                    // We do not prompt here; closing handler handled user confirmation.
                    await CloseSVSMediaFilesAsync();
                }
                catch (Exception ex) { Debug.WriteLine($"ShutdownAsync: CloseSVSMediaFiles error {ex.Message}"); }

                // Stereo controller full unload (distinct from MediaClose)
                try { await mediaStereoController.UnloadAsync(); } catch (Exception ex) { Debug.WriteLine($"ShutdownAsync: mediaStereoController.Unload error {ex.Message}"); }

                // Internet queue unload
                try { await internetQueue.UnloadAsync(); } catch (Exception ex) { Debug.WriteLine($"ShutdownAsync: internetQueue.Unload error {ex.Message}"); }

                // Persist final reporter content and release resources
                try { report.Unload(); } catch (Exception ex) { Debug.WriteLine($"ShutdownAsync: report.Unload error {ex.Message}"); }

                // Telemetry (if integrated through TelemetryLogger)
                try { TelemetryLogger.TrackAppStartStop(TrackAppStartStopType.AppStopOk); } catch { }

                Debug.WriteLine("ShutdownAsync: complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShutdownAsync: fatal {ex}");
            }
        }


        /// <summary>
        /// Called by the file new survey menu events for stereo fish survey
        /// mono fish survey and benthic surveys
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async Task<bool> SurveyCreateAllTypesAsync(Survey.SurveyType surveyType)
        {
            bool ret = false;

            // First check if an existing survey is already open
            if (await CheckForOpenSurveyAndCloseAsync() == true)
            {
                switch (surveyType)
                {
                    case Survey.SurveyType.StereoFish:
                        ret = await SurveyCreateStereoFishAsync();

                        break;
                    case Survey.SurveyType.MonoFish:
                    case Survey.SurveyType.MonoBenthic:
                        ret = await SurveyCreateMonoSurveyAsync(surveyType);
                        break;
                    default:
                        report.Warning("", $"SurveyCreateAllTypes: Unsupported survey type {surveyType}");
                        return false;
                }

                if (ret)
                {
                    int ret2;

                    // Force a Save
                    ret2 = await FileSurveySaveOrSaveAsAsync();

                    if (ret2 == 0 && surveyClass is not null)
                    {
                        if (surveyClass.Data.Info.SurveyPath is not null && surveyClass.Data.Info.SurveyFileName is not null)
                        {
                            // Make survey path
                            string surveyPath = Path.Combine(surveyClass.Data.Info.SurveyPath, surveyClass.Data.Info.SurveyFileName);

                            // Close the Survey
                            await CheckForOpenSurveyAndCloseAsync();

                            // Re-Open in a standard way (so everyone gets hooked up and initialized correctly)
                            ret2 = await OpenSurveyAsync(surveyPath);

                            if (ret2 == 0)
                            {
                                ret = true;
                            }
                            else
                            {
                                report.Warning("", $"SurveyCreateAllTypes failed, survey path:{surveyPath}, return = {ret2}");
                            }
                        }
                        else
                        {
                            ret2 = -1;
                            report.Warning("", $"SurveyCreateAllTypes: Missing survey path.");
                        }
                    }
                    else
                    {
                        report.Warning("", $"SurveyCreateAllTypes failed, return = {ret2}");
                    }

                    if (ret2 != 0)
                    {
                        // Report the missing survey file
                        // Survey needs to be saved before a frame can be saved
                        var warningIcon = new SymbolIcon(Symbol.Important); // Symbol.Important represents an exclamation

                        // Add a content dialog to report the survey save error
                        // and display the user to look at the Output tab for more information
                        var dialog = new ContentDialog
                        {
                            Title = $"Failed to save Survey file",
                            Content = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 10,
                                Children =
                                        {
                                            warningIcon, // Add the exclamation icon to the dialog content
                                            new TextBlock
                                            {
                                                Text = $"Please check the Output tab for more information on the failure",
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
                    }
                }
            }

            if (!ret)
                surveyClass = null;

            SetMenuStatusBasedOnSurveyState();

            // Reset Info Bar dismissed status
            infoBarCalibrationMissingDismissed = false;
            infoBarSpeciesInfoMissingDismissed = false;

            return ret;
        }


        /// <summary>
        /// Used to select the media for a stereo fish survey and setup the survey info
        /// </summary>
        /// <returns></returns>
        private async Task<bool> SurveyCreateStereoFishAsync()
        {
            bool ret = false;

            // Get to use to select media files for the survey
            FileOpenPicker openPicker = new()
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };

            // Add file type filters
            openPicker.FileTypeFilter.Add(".mp4");

            // Associate the file picker with the current window
            IntPtr hWnd = WindowNative.GetWindowHandle(this/*App.MainWindow*/);
            InitializeWithWindow.Initialize(openPicker, hWnd);


            // Show the picker and allow multiple file selection            
            // The DispatcherQueue is used to ensure the file picker returned objects are created on the UI thread (ChapGPT)
            IReadOnlyList<StorageFile> mediaFilesSelected = mediaFilesSelected = await DispatcherQueue.EnqueueAsync(async () =>
            {
                openPicker.CommitButtonText = $"Select 2 videos";
                return await openPicker.PickMultipleFilesAsync();
            });

            // Proceed is files selected
            if (mediaFilesSelected.Count == 2)
            {
                // Create a new empty survey
                surveyClass = new Survey(report);
                surveyClass.PropertyChanged += Survey_PropertyChanged;

                // Set the survey type stereo fish (SVS)
                surveyClass.Data.Info.SurveyType = SurveyType.StereoFish;

                // Inform the EventControl of the new survey events
                eventsControl.SetEvents(surveyClass.Data.Events.EventList);

                // Get the name (if any) of a potential survey to inherit information from
                string potentialSurveyToInheritFrom = string.Empty;
                string[]? recentSurveys = ApplicationData.Current.LocalSettings.Values[RECENT_SURVEYS_KEY] as string[];
                if (recentSurveys is not null && recentSurveys.Length > 0)
                {
                    potentialSurveyToInheritFrom = recentSurveys[0];
                    if (File.Exists(potentialSurveyToInheritFrom) == false)
                        potentialSurveyToInheritFrom = string.Empty;
                }

                // Load the Info and Media user control to setup the survey
                SurveyStereoInfoAndMediaUserControl.SetupForContentDialog(SurveyStereoInfoAndMediaContentDialog,
                                                                    mediaFilesSelected,
                                                                    Path.GetFileName(potentialSurveyToInheritFrom)/*used to display the name of the inheritance survey only*/);
                SurveyStereoInfoAndMediaUserControl.SetReporter(report);

                try
                {
                    // ** Important notes **
                    // The UserControl SurveyInfoAndMedia is displayed within a ContentDialog for 
                    // the purpose of setting up a new survey (also using from a SettingsCard)
                    // I struggled to get the ContentDialog to show width necessary to fully display
                    // the UserControl.  The solution was to:
                    // Set <x:Double x:key="ContentDialogMaxWidth">1200</x:Double> in the <ResourceDictionary>
                    // to setup the ContentDialog in XAML in MainWindow and place it in Grid.Row=2.
                    // This took a lot of trial and error. It seems to effect the title bar is left in
                    // default row zero.
                    ContentDialogResult result = await SurveyStereoInfoAndMediaContentDialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        // Copy the survey info and media info setup in the dialog into the survey class
                        bool inheritanceRequested = SurveyStereoInfoAndMediaUserControl.SaveForContentDialog(surveyClass);

                        // Inherit information from recent survey if user requested                        
                        if (inheritanceRequested == true && !string.IsNullOrEmpty(potentialSurveyToInheritFrom))
                        {
                            // Copies select information (calibration and/or rules)
                            SurveyInheritance surveyInheritance = new();
                            ret = await surveyInheritance.InheritFromSurveyAsync(this, report, surveyClass, potentialSurveyToInheritFrom);
                        }
                        else
                            // All good
                            ret = true;
                    }
                    else
                    {
                        // User canceled creating a new survey
                        ret = false;
                    }
                }
                catch (Exception ex)
                {
                    // Log or handle the exception as needed
                    report.Error("", $"SurveyCreateStereoFish: {ex.Message}");
                    ret = false;
                }
            }
            else
            {
                // Report two media files are required               
                var warningIcon = new SymbolIcon(Symbol.Important); // Symbol.Important represents an exclamation

                // Add a content dialog to report the survey save error
                // and display the user to look at the Output tab for more information
                var dialog = new ContentDialog
                {
                    Title = $"Two media files are required",
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                                        {
                                            warningIcon, // Add the exclamation icon to the dialog content
                                            new TextBlock
                                            {
                                                Text = $"For a stereo fish survey two media files are required",
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
            }


            return ret;
        }


        /// <summary>
        /// Used to select the media for a mono fish or benthic survey and setup the survey info
        /// </summary>
        /// <param name="surveyType"></param>
        /// <param name=""></param>
        /// <returns></returns>
        private async Task<bool> SurveyCreateMonoSurveyAsync(Survey.SurveyType surveyType)
        {
            bool ret = false;

            // Get to use to select media files for the survey
            FileOpenPicker openPicker = new()
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };

            // Add file type filters
            openPicker.FileTypeFilter.Add(".mp4");

            // Associate the file picker with the current window
            IntPtr hWnd = WindowNative.GetWindowHandle(this/*App.MainWindow*/);
            InitializeWithWindow.Initialize(openPicker, hWnd);


            // Show the picker and allow multiple file selection            
            // The DispatcherQueue is used to ensure the file picker returned objects are created on the UI thread (ChapGPT)
            StorageFile mediaFileSelected = await DispatcherQueue.EnqueueAsync(async () =>
            {
                return await openPicker.PickSingleFileAsync();
            });

            // Proceed is files selected
            if (mediaFileSelected is not null)
            {
                // Create a new empty survey
                surveyClass = new Survey(report);
                surveyClass.PropertyChanged += Survey_PropertyChanged;

                // Set the survey type Mono fish or Benthic
                surveyClass.Data.Info.SurveyType = surveyType;

                // Inform the EventControl of the new survey events
                eventsControl.SetEvents(surveyClass.Data.Events.EventList);

                // Load the Info and Media user control to setup the survey
                List<StorageFile> mediaFilesSelected = [];
                mediaFilesSelected.Add(mediaFileSelected);

                SurveyMonoInfoAndMediaUserControl.SetupForContentDialog(SurveyMonoInfoAndMediaContentDialog,
                                                                    mediaFilesSelected);
                SurveyMonoInfoAndMediaUserControl.SetReporter(report);

                try
                {
                    // ** Important notes **
                    // The UserControl SurveyInfoAndMedia is displayed within a ContentDialog for 
                    // the purpose of setting up a new survey (also using from a SettingsCard)
                    // I struggled to get the ContentDialog to show width necessary to fully display
                    // the UserControl.  The solution was to:
                    // Set <x:Double x:key="ContentDialogMaxWidth">1200</x:Double> in the <ResourceDictionary>
                    // to setup the ContentDialog in XAML in MainWindow and place it in Grid.Row=2.
                    // This took a lot of trial and error. It seems to effect the title bar is left in
                    // default row zero.
                    ContentDialogResult result = await SurveyMonoInfoAndMediaContentDialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        // Copy the survey info and media info setup in the dialog into the survey class                        
                        _ = SurveyMonoInfoAndMediaUserControl.SaveForContentDialog(surveyClass);

                        ret = true;
                    }
                    else
                    {
                        // User canceled creating a new survey
                        ret = false;
                    }
                }
                catch (Exception ex)
                {
                    // Log or handle the exception as needed
                    report.Error("", $"SurveyCreateMonoSurvey: {ex.Message}");
                    ret = false;
                }
            }

            return ret;
        }


        /// <summary>
        /// Open the survey files
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <param name="surveyFileName"></param>
        /// <returns>-999 If user aborts</returns>
        internal async Task<int> OpenSurveyAsync(string surveyFileSpec)
        {
            int ret = 0;

            if (surveyClass is null)
            {
                surveyClass ??= new Survey(report);
                surveyClass.PropertyChanged += Survey_PropertyChanged;
            }
            else
            {
                await surveyClass.SurveyCloseAsync();
            }


            ret = await surveyClass.SurveyLoadAsync(surveyFileSpec);

            if (ret == 0 &&
                surveyClass.Data is not null && surveyClass.Data.Media is not null && surveyClass.Data.Media.MediaPath is not null)
            {
                // Check if the left media file(s) exist
                ret = await CheckIfMediaFileExistsAsync(true/*trueLeftFalseRight*/, surveyClass.Data.Media, surveyFileSpec);
                if (ret == 0)
                    ret = await CheckIfMediaFileExistsAsync(false/*trueLeftFalseRight*/, surveyClass.Data.Media, surveyFileSpec);

                if (ret == 0)
                {
                    // Setup the events
                    eventsControl.SetEvents(surveyClass.Data.Events.EventList);


                    // Create a StereoProjection instance that allows the user to add measurement points to the media images
                    // Do this before opening the media so the calibration data is available when the media is opened and the frame size established
                    stereoProjection.SetCalibrationData(surveyClass.Data.Calibration);
                    stereoProjection.SetSurveyRules(surveyClass.Data.SurveyRules);

                    // Open Media Files and bind the MediaPlayers if IsSynchronized is true
                    if (await OpenSVSMediaFilesAsync() == true)
                    {
                        // Enable the insert survey transect marker menu item
                        //???MenuTransectStartStopMarker.IsEnabled = true;  // This is probably not need here (handled elsewhere)

                        // Remember the survey folder
                        SettingsManagerLocal.SurveyFolder = Path.GetDirectoryName(surveyFileSpec);


                        //// Inform the two media players of the MeasurementPointControl instances that allow the user to add measurement points to the media
                        //???LMediaPlayer.SetMeasurementPointControl(measurementPointControl);
                        //???RMediaPlayer.SetMeasurementPointControl(measurementPointControl);

                        // Report Survey details
                        string calibrationStatus;
                        if (surveyClass.Data.Calibration.CalibrationDataList.Count == 0)
                            calibrationStatus = "No Calibration Data";
                        else if (surveyClass.Data.Calibration.CalibrationDataList.Count == 1)
                            calibrationStatus = "Calibrated";
                        else
                            calibrationStatus = "Multiple Calibrations";

                        string eventsStatus;
                        if (surveyClass.Data.Events.EventList.Count == 0)
                            eventsStatus = "No Events";
                        else if (surveyClass.Data.Events.EventList.Count == 1)
                            eventsStatus = "1 Event";
                        else
                            eventsStatus = $"{surveyClass.Data.Events.EventList.Count} Events";

                        report.Info("", $"Survey Loaded: '{surveyClass.GetSurveyTitle()}', {calibrationStatus}, {eventsStatus}");
                    }
                    else
                        // Failed to open media files
                        surveyClass = null;
                }
                else
                    // Failed to open media files
                    surveyClass = null;
            }
            else
            {
                report.Warning("", $"Failed to open survey file:{surveyFileSpec}, error = {ret}");
                surveyClass = null;
            }

            // Display the missing calibration warning InfoBar if necessary
            infoBarCalibrationMissingDismissed = false;
            SetInfoBarCalibrationMissing();

            // Display the missing species warning InfoBar if necessary
            infoBarSpeciesInfoMissingDismissed = false;
            SetInfoBarSpeciesInfoMissing();

            // Display the missing RMS Rule Violation InfoBar if necessary
            infoBarRMSRuleViolationDismissed = false;
            SetInfoBarRMSRuleViolation();

            return ret;
        }


        /// <summary>
        /// Save the current survey to a new file
        /// </summary>
        /// <returns></returns>
        private async Task<int> SaveAsSurveyAsync()
        {
            int ret = 0;

            if (surveyClass is not null)
            {
                FileSavePicker savePicker = new();
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this); // 'this' should be your window or page
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd); // Link the picker with the window handle

                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("Survey", [".survey"]);
                savePicker.SuggestedFileName = string.IsNullOrWhiteSpace(surveyClass.Data.Info.SurveyCode) ? "New Document" : surveyClass.Data.Info.SurveyCode;
        

                StorageFile file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    SetTitle(file.Name);
                    SetTitleSaveStatus("Saving...");

                    // Prevent updates to the remote version of the file until we finish making changes and call CompleteUpdatesAsync.
                    CachedFileManager.DeferUpdates(file);

                    // Write data to the file
                    // Save As
                    ret = await surveyClass.SurveySaveAsAsync(file.Path);


                    // Let Windows know that we're finished changing the file so the other app can update the remote version of the file.
                    FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
                    if (status == FileUpdateStatus.Complete)
                    {
                        Debug.WriteLine($"File {file.Path} saved successfully.");

                        // Add to Recent Surveys
                        AddToRecentSurveys(file.Path);
                        UpdateRecentSurveysMenu();

                        // Remember the survey folder                        
                        SettingsManagerLocal.SurveyFolder = Path.GetDirectoryName(file.Path);
                    }                   
                    else
                    {
                        ret = -1;
                        Debug.WriteLine($"Failed to save file {file.Path}.");
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Save the currently open survey file or 'Save As' if not saved yet
        /// Used by the FileSurveySave_Click and FileSurveySaveAs_Click and
        /// also to save a new survey after creation
        /// </summary>
        /// <returns></returns>
        private async Task<int> FileSurveySaveOrSaveAsAsync()
        {
            int ret = -1;

            if (surveyClass is not null)
            {
                if (surveyClass.Data.Info.SurveyPath == null || surveyClass.Data.Info.SurveyFileName == null)
                {
                    // Not saved yet so use 'Save As'
                    ret = await SaveAsSurveyAsync();
                }
                else
                {
                    // Save
                    ret = surveyClass.SurveySave();
                }
            }

            report.Save();

            SetMenuStatusBasedOnSurveyState();

            return ret;
        }


        /// <summary>
        /// Check if there is an existing survey open and if so check if it has unsaved changes
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <returns>true is OK to proceed (i.e. no survey now open)</returns>
        internal async Task<bool> CheckForOpenSurveyAndCloseAsync()
        {
            bool ret = false;

            if (this.surveyClass is not null)
            {
                bool closeSurvey = false;

                if (this.surveyClass.IsDirty == true)
                {
                    Debug.WriteLine("CheckForOpenSurveyAndClose Dirty Path");//???
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
                            await FileSurveySaveOrSaveAsAsync();
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
                        report.Error("", $"CheckForOpenSurveyAndClose (confirm phase): {ex.Message}");
                    }
                }
                else
                    closeSurvey = true;


                if (closeSurvey == true)
                {
                    Debug.WriteLine("CheckForOpenSurveyAndClose Close Survey Path");//???
                    try
                    {
                        // Wait for things to settle
                        await Task.Delay(200);
                        Debug.WriteLine("CheckForOpenSurveyAndClose Before CloseSVSMediaFiles");//???
                        // Closes the StereoMediaController, clears the title and the sync indicator
                        await CloseSVSMediaFilesAsync();

                        // Close and clear the Survey class (holds the survey data)
                        if (surveyClass is not null)
                        {
                            Debug.WriteLine("CheckForOpenSurveyAndClose Before SurveyClose");//???
                            await surveyClass.SurveyCloseAsync();
                            surveyClass = null;
                        }

                        // Clear the calibration data and the survey rules 
                        Debug.WriteLine("CheckForOpenSurveyAndClose Before ClearCalibrationData");//???
                        stereoProjection.ClearCalibrationData();
                        Debug.WriteLine("CheckForOpenSurveyAndClose Before SetCalibratedIndicator");//???
                        SetCalibratedIndicator(null, null);
                        Debug.WriteLine("CheckForOpenSurveyAndClose Before ClearSurveyRules");//???
                        stereoProjection.ClearSurveyRules();


                        // Display both media controls
                        Debug.WriteLine("CheckForOpenSurveyAndClose Before MediaControlsDisplayMode");//???
                        MediaControlsDisplayMode(false);

                        // Clear the reporter
                        Debug.WriteLine("CheckForOpenSurveyAndClose Before report.Clear");//???
                        report.Clear();

                        ret = true;
                    }
                    catch (Exception ex)
                    {
                        report.Error("", $"CheckForOpenSurveyAndClose (close phase): {ex.Message}");
                    }
                }
            }
            else
                ret = true;

            Debug.WriteLine("CheckForOpenSurveyAndClose Before SetMenuStatusBasedOnSurveyState");//???
            SetMenuStatusBasedOnSurveyState();

            // Display the missing calibration warning InfoBar if necessary
            infoBarCalibrationMissingDismissed = false;
            SetInfoBarCalibrationMissing();

            // Display the missing species warning InfoBar if necessary
            infoBarSpeciesInfoMissingDismissed = false;
            SetInfoBarSpeciesInfoMissing();

            // Display the missing RMS Rule Violation InfoBar if necessary
            Debug.WriteLine("CheckForOpenSurveyAndClose Before SetInfoBarRMSRuleViolation");//???
            infoBarRMSRuleViolationDismissed = false;
            SetInfoBarRMSRuleViolation();

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
        private async Task<int> CheckIfMediaFileExistsAsync(bool trueLeftFalseRight, MediaClass mediaClass, string surveyFileSpec)
        {
            int ret = 0;
            ObservableCollection<string> mediaFileNames = trueLeftFalseRight ? mediaClass.LeftMediaFileNames : mediaClass.RightMediaFileNames;

            for (int index = 0; index < mediaFileNames.Count; index++)
            {
                bool promptForMediaFile = false;

                string fileName = mediaFileNames[index];
                string fileSpec = "";

                if (mediaClass.MediaPath is not null)
                {
                    fileSpec = Path.Combine(mediaClass.MediaPath, fileName);

                    // If fileSpec a relative path then use the path from the survey file spec
                    if (!Path.IsPathRooted(fileSpec))
                    {
                        // Get the directory portion of the fully qualified surveyFileSpec
                        string baseDirectory = Path.GetDirectoryName(surveyFileSpec) ?? "";

                        // Combine the base directory with the relative fileSpec
                        fileSpec = Path.GetFullPath(Path.Combine(baseDirectory, fileSpec));
                    }

                    if (File.Exists(fileSpec) == false)
                        promptForMediaFile = true;
                }
                else
                    promptForMediaFile = true;

                if (promptForMediaFile)
                {
                    // Media file is missing. Report to the user and ask if they would like to try to find the file
                    string cameraSide = trueLeftFalseRight ? "Left" : "Right";
                    string fileNumber = mediaFileNames.Count > 1 ? $"number {index + 1} " : "";
                    string message;

                    if (mediaClass.MediaPath is not null)
                        message = $"The {cameraSide.ToLower()} media file {fileNumber}'{fileSpec}' does not exist. Press 'OK' to try to find the file. Press 'Cancel' to stop loading the survey";
                    else
                        message = $"The {cameraSide.ToLower()} media file {fileNumber}'{fileName}' does not exist. Press 'OK' to try to find the file. Press 'Cancel' to stop loading the survey";

                    // Create a SymbolIcon with an exclamation mark
                    var warningIcon = new SymbolIcon(Symbol.Important); // Symbol.Important represents an exclamation

                    // Create the ContentDialog instance
                    var dialog = new ContentDialog
                    {
                        Title = $"{cameraSide} media file missing",
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
                        // "OK" button clicked
                        // WinUI3 only allows 'openPicker.SuggestedStartLocation' to be one of the standard folders
                        //string? mediaFolder = SettingsManager.MediaImportFolder;
                        //if (string.IsNullOrEmpty(mediaFolder) == true)
                        //    mediaFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                        FileOpenPicker openPicker = new FileOpenPicker();
                        IntPtr hwnd = WindowNative.GetWindowHandle(this); // Assuming 'this' is your current window.
                        InitializeWithWindow.Initialize(openPicker, hwnd);

                        openPicker.ViewMode = PickerViewMode.Thumbnail; // Makes it easier for users to find their files visually.
                        openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary; // Suggest starting in the Pictures library.
                        openPicker.FileTypeFilter.Add(".mp4");


                        var file = await openPicker.PickSingleFileAsync();
                        if (file is not null)
                        {
                            // Adjust media file name
                            string fileNameOnly = Path.GetFileName(file.Name);
                            mediaFileNames[index] = fileNameOnly;

                            string extractedMediaPath = Path.GetDirectoryName(file.Path) ?? "";

                            // Check if the media path needs to change
                            if (mediaClass.MediaPath is not null)
                            {
                                if (mediaClass.MediaPath != extractedMediaPath)
                                    mediaClass.MediaPath = extractedMediaPath;
                            }
                            else
                            {
                                // Media is missing so just apply new path
                                mediaClass.MediaPath = extractedMediaPath;
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

            return ret;
        }



        /// <summary>
        /// OpenMediaFiles
        /// Open the left and right media files and if the media files are locked together then bind both MediaPlayers to the same MediaControl instance
        /// </summary>
        private async Task<bool> OpenSVSMediaFilesAsync()
        {
            bool ret = true;
            int retOpen;

            if (surveyClass != null && surveyClass.Data.Media.MediaPath != null)
            {
                // Check if already Open and close if necessary
                if (mediaStereoController.MediaIsOpen())
                {
                    await mediaStereoController.MediaCloseAsync();
                    SetTitle("");
                    SetLockUnlockIndicator(null, null);
                }


                // Get the media file names
                string mediaFileLeft = surveyClass.GetLeftMediaFileSpec(0);
                string mediaFileRight = surveyClass.GetRightMediaFileSpec(0);

                //???TOBEDELETED
                //if (surveyClass.Data.Media.LeftMediaFileNames.Count > 0)
                //    mediaFileLeft = Path.Combine(surveyClass.Data.Media.MediaPath, surveyClass.Data.Media.LeftMediaFileNames[0]);
                //if (surveyClass.Data.Media.RightMediaFileNames.Count > 0)
                //    mediaFileRight = Path.Combine(surveyClass.Data.Media.MediaPath, surveyClass.Data.Media.RightMediaFileNames[0]);
                
                //// If fileSpec a relative path then use the path from the survey file spec
                //if (!Path.IsPathRooted(mediaFileLeft) && surveyClass.Data.Info.SurveyPath is not null)
                //{
                //    // Combine the base directory with the relative fileSpec
                //    mediaFileLeft = Path.GetFullPath(Path.Combine(surveyClass.Data.Info.SurveyPath, mediaFileLeft));
                //}
                //if (!Path.IsPathRooted(mediaFileRight) && surveyClass.Data.Info.SurveyPath is not null)
                //{
                //    // Combine the base directory with the relative fileSpec
                //    mediaFileRight = Path.GetFullPath(Path.Combine(surveyClass.Data.Info.SurveyPath, mediaFileRight));
                //}


                // Open left camera media
                if (string.IsNullOrEmpty(mediaFileLeft) == false && string.IsNullOrEmpty(mediaFileRight) == false)
                {
                    // Extract depth underwater for color correction
                    if (uint.TryParse(surveyClass.Data.Info.SurveyDepth, out uint depthUnderwater) == false)
                        depthUnderwater = 0;

                    // Open the new media
                    retOpen = await mediaStereoController.MediaOpenAsync(surveyClass.Data.Info.SurveyType,
                                                                        mediaFileLeft,
                                                                        mediaFileRight,
                                                                        surveyClass.Data.Events.EventList,
                                                                        surveyClass.Data.Sync.IsSynchronized == true ? surveyClass.Data.Sync.TimeSpanOffset : null,
                                                                        depthUnderwater);

                    if (retOpen == 0)
                    {
                        if (surveyClass.Data.Info.SurveyFileName is not null)
                            SetTitle(surveyClass.Data.Info.SurveyFileName);

                        MenuLockUnlockMediaPlayers.IsEnabled = true;
                        MenuTransectStartStopMarker.IsEnabled = true;
                    }
                    else
                    {
                        SetTitle("");
                        SetLockUnlockIndicator(null, null);
                        MenuLockUnlockMediaPlayers.IsEnabled = false;
                        MenuTransectStartStopMarker.IsEnabled = false;

                        // Display both media controls
                        MediaControlsDisplayMode(false);

                        ret = false;
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// CloseMediaFiles
        /// </summary>
        private async Task CloseSVSMediaFilesAsync()
        {

            if (mediaStereoController.MediaIsOpen())
            {
                await mediaStereoController.MediaCloseAsync();

                SetTitle("");
                SetTitleSaveStatus("");
                SetTitleCameraSide("");

                SetLockUnlockIndicator(null, null);

                MenuLockUnlockMediaPlayers.IsEnabled = false;
                MenuTransectStartStopMarker.IsEnabled = false;
            }
        }



        /// <summary>
        /// Used to set the status of the menu options based on the state of the survey
        /// </summary>
        private void SetMenuStatusBasedOnSurveyState()
        {
            if (surveyClass is not null /*&& this.projectClass.IsLoaded == true*/)
            {
                // Survey
                MenuSurveyNew.IsEnabled = false;
                MenuSurveySave.IsEnabled = true;
                MenuSurveySaveAs.IsEnabled = true;
                MenuSurveyClose.IsEnabled = true;

                // Import calibration
                if (surveyClass.Data.Info.SurveyType == Survey.SurveyType.StereoFish)
                    MenuImportCalibration.IsEnabled = true;
                else
                    MenuImportCalibration.IsEnabled = false;

                // Media Lock
                if (surveyClass.Data.Info.SurveyType == Survey.SurveyType.StereoFish)
                    MenuLockUnlockMediaPlayers.IsEnabled = true;
                else
                    MenuLockUnlockMediaPlayers.IsEnabled = false;

                // Settings
                MenuSettings.IsEnabled = true;
                // Survey Transect Marker
                MenuTransectStartStopMarker.IsEnabled = true;
            }
            else
            {
                // Survey
                MenuSurveyNew.IsEnabled = true;
                MenuSurveySave.IsEnabled = false;
                MenuSurveySaveAs.IsEnabled = false;
                MenuSurveyClose.IsEnabled = false;
                // Import calibration
                MenuImportCalibration.IsEnabled = false;
                // Media lock
                MenuLockUnlockMediaPlayers.IsEnabled = false;
                // Settings
                MenuSettings.IsEnabled = true;      // Always allow settings and setting will adjust of no survey is open
                // Survey Transect Marker
                MenuTransectStartStopMarker.IsEnabled = false;
                }
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
                // needed to keep the <ToolTip> working as the glyph changes)
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
        /// Set the calibrated indicator in the title bar
        /// </summary>
        /// <param name="locked">true = locked, false = unlock, null is blank</param>
        private string? calibratedIndictorText = null;

        public void SetCalibratedIndicator(int? frameWidth, int? frameHeight)
        {
            string tooltip;

            // Remember the 'Calibrated' symbol so we can reuse it later
            calibratedIndictorText ??= CalibratedIndicator.Text;

            CalibrationClass? calibrationClass = surveyClass?.Data?.Calibration;
            CalibrationData? calibrationDataPreferred = calibrationClass?.GetPreferredCalibationData(frameWidth, frameHeight);

            if (calibrationClass is not null && 
                calibrationClass.CalibrationDataList.Count > 0 && 
                (frameWidth is not null || frameHeight is not null))
            {
                if (calibrationDataPreferred is not null)
                {
                    // Inform about the preferred calibration data
                    if (!string.IsNullOrEmpty(calibrationDataPreferred.Description) &&
                        calibrationDataPreferred.LeftCameraCalibration is not null &&
                        calibrationDataPreferred.LeftCameraCalibration.ImageSize is not null)
                    {
                        Emgu.CV.Matrix<int> imageSize = calibrationDataPreferred.LeftCameraCalibration.ImageSize!;
                        tooltip = $"Calibration Data Description: {calibrationDataPreferred.Description}, frame size:({imageSize[0, 0]},{imageSize[0, 1]})";
                    }
                    else if (!string.IsNullOrEmpty(calibrationDataPreferred.Description))
                        tooltip = $"Calibration Data Description: {calibrationDataPreferred.Description}, frame size missing";
                    else
                        tooltip = "Calibration Data Setup";


                    // If there is other calibration data available then add it to the <ToolTip>
                    if (calibrationClass.CalibrationDataList.Count > 1)
                        tooltip += "\n\nAvailable Calibration:" + MakeCalibrationDescriptionListTooltip(calibrationClass);


                    // Show the calibration icon
                    CalibratedIndicator.Text = calibratedIndictorText;

                    ToolTipService.SetToolTip(CalibratedIndicator, tooltip);
                }
                else
                {
                    if (frameWidth is not null)
                    {
                        tooltip = $"Failed to return Preferred Calibration Data for frame size ({frameWidth},{frameHeight})!\nAvailable calibration sets:\n" + MakeCalibrationDescriptionListTooltip(calibrationClass);
                        // Show the calibration icon
                        CalibratedIndicator.Text = calibratedIndictorText + "\uE814";
                    }
                    else
                    {
                        tooltip = $"Failed to return Preferred Calibration Data!\nAvailable calibration sets:\n" + MakeCalibrationDescriptionListTooltip(calibrationClass);
                        // Show the calibration icon
                        CalibratedIndicator.Text = calibratedIndictorText;
                    }

                    ToolTipService.SetToolTip(CalibratedIndicator, tooltip);
                }
            }
            else
            {
                // Show nothing (normally if no calibration is available)
                // Four spaces to make it invisible and approximately the same width
                // as the lock/unlock icons (do not change, not fully understood but
                // needed to keep the <ToolTip> working as the glyph changes)
                CalibratedIndicator.Text = "    ";
                ToolTipService.SetToolTip(CalibratedIndicator, "");
            }
        }

        /// <summary>
        /// Make a list of calibration descriptions for the <ToolTip>. This includes the description (if present) and the frame size (if present)
        /// </summary>
        /// <param name=""></param>
        /// <returns></returns>
        private static string MakeCalibrationDescriptionListTooltip(CalibrationClass calibrationClass)
        {
            StringBuilder sb = new();

            for (int i = 0; i < calibrationClass.CalibrationDataList.Count; i++)
            {
                if (calibrationClass.PreferredCalibrationDataIndex == i)
                    sb.Append("  *");
                else
                    sb.Append("   ");

                if (calibrationClass.CalibrationDataList[i].Description is not null && calibrationClass.CalibrationDataList[i].LeftCameraCalibration.ImageSize is not null)
                {
                    Emgu.CV.Matrix<int> imageSize = calibrationClass.CalibrationDataList[i].LeftCameraCalibration.ImageSize!;
                    sb.AppendLine($"{i + 1}. {calibrationClass.CalibrationDataList[i].Description}, frame size:({imageSize[0, 0]},{imageSize[0, 1]})");
                }
                else if (calibrationClass.CalibrationDataList[i].Description is not null)
                    sb.AppendLine($"{i + 1}. {calibrationClass.CalibrationDataList[i].Description}, frame size missing");
                else
                    sb.AppendLine($"{i + 1}. description missing");
            }

            if (calibrationClass.PreferredCalibrationDataIndex != -1)
                sb.AppendLine($"* Preferred Calibration Data");

            return sb.ToString();
        }


        /// <summary>
        /// Get the Calibration ID from the preferred calibration data and check if was used for
        /// all the event EventMeasurements.  If not then ask the user if they want to update the
        /// calculation.
        /// The MediaPlayer must be open so the frame width and height is known
        /// </summary>
        /// <returns>true if anything changed</returns>
        public async Task<bool> CheckIfEventMeasurementsAreUpToDateAsync(bool forceReCalc)
        {
            bool ret = false;

            // Get the Calibration ID from the preferred calibration data
            if (surveyClass is not null && MediaPlayerLeft.IsOpen())
            {
                ret = await SurveyMeasurementHelper.CheckIfEventMeasurementsAreUpToDate(
                                                            stereoProjection,
                                                            surveyClass,
                                                            MediaPlayerLeft.FrameWidth,
                                                            MediaPlayerLeft.FrameHeight,
                                                            this.Content.XamlRoot, 
                                                            forceReCalc);
                            
                if (ret == true && surveyClass is not null)
                {
                    // Reset the event list
                    eventsControl.SetEvents([]);

                    // Need to wait for the events to be updated otherwise the reset is missed
                    await Task.Delay(500);

                    // Refresh the event list (it will not automatic detect changes within existing events)
                    eventsControl.SetEvents(surveyClass.Data.Events.EventList);
                }
            }

            return ret;
        }


        /// <summary>
        /// Populate the SurveyMeasurement with the measurement calculates from the stereo projection
        /// Note the LeftX, LeftY, RightX, RightY should have already been loaded in 
        /// SurveyMeasurement surveyMeasurement
        /// Survey rules are also calculated
        /// </summary>
        /// <param name="surveyMeasurement"></param>
        /// <returns></returns>
        public bool DoMeasurementAndRulesCalculations(SurveyMeasurement surveyMeasurement)
        {
            bool updated = false;

            if (surveyClass is not null)
            {
                updated = SurveyMeasurementHelper.DoMeasurementAndRulesCalculations(
                                    stereoProjection,
                                    surveyClass,
                                    surveyMeasurement);
            }

            return updated;
        }


        /// <summary>
        /// Populate the SurveyMeasurement with the measurement calculates from the stereo projection
        /// Note the LeftX, LeftY, RightX, RightY should have already been loaded in 
        /// SurveyMeasurement surveyMeasurement
        /// Survey rules are also calculated
        /// </summary>
        /// <param name="surveyMeasurement"></param>
        /// <returns></returns>
        public bool DoRulesCalculations(SurveyStereoPoint surveyStereoPoint)
        {
            bool updated = false;

            if (surveyClass is not null)
            {
                updated = SurveyMeasurementHelper.DoRulesCalculations(
                                    stereoProjection,
                                    surveyClass, 
                                    surveyStereoPoint);
            }

            return updated;
        }


        /// <summary>
        /// Set the title text elements of the <TitleBar> title text
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
        /// Set the save status text elements of the <TitleBar> title text
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
        /// Set the camera side status text elements of the <TitleBar> title text
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

            if (!string.IsNullOrEmpty(titlebarTitle))
            {
                title = $"Surveyor: ";

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
                title = $"Surveyor";

            return title;
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

            // Area of the Calibrated indicator
            GeneralTransform transformCalibratedIndicator = CalibratedIndicator.TransformToVisual(null);
            Rect boundsCalibratedIndicator = transformCalibratedIndicator.TransformBounds(new Rect(0, 0,
                                                                                 CalibratedIndicator.ActualWidth,
                                                                                 CalibratedIndicator.ActualHeight));
            Windows.Graphics.RectInt32 CalibratedIndicatorRect = GetRect(boundsCalibratedIndicator, scaleAdjustment);


            // Create list of regions that should not be drag-able
            var rectArray = new Windows.Graphics.RectInt32[] { MenuBarRect/*, SearchBoxRect*//*, PersonPicRect*/, LockUnLockIndicatorRect, CalibratedIndicatorRect };

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
        /// If the media is synchronized then only display the primary (left) media control. Hide the 
        /// secondary media control and center the primary
        /// </summary>
        /// <param name="mediaSycronized"></param>
        private void MediaControlsDisplayMode(bool mediaSycronized)
        {
            if (mediaSycronized)
            {
                Grid.SetColumnSpan(MediaControlsLeftGrid, 3);
                MediaControlSecondary.Visibility = Visibility.Collapsed;
            }
            else
            {
                Grid.SetColumnSpan(MediaControlsLeftGrid, 1);
                MediaControlSecondary.Visibility = Visibility.Visible;
            }
        }


        /// <summary>
        /// Updates the main window to show that the media is synchronized
        /// </summary>
        /// <param name="positionOffset"></param>
        internal void MediaSynchronized(TimeSpan? positionOffset)
        {
            // Indicate the media is locked
            SetLockUnlockIndicator(true, positionOffset);

            // Display only the primary media control (Adjust grid and hide secondary media control)
            MediaControlsDisplayMode(true);

            // Adjust the File menu item text 
            MenuLockUnlockMediaPlayers.Text = "Unlock Media Players";
            MenuLockUnlockMediaPlayersIcon.Glyph = "\uE1F7"; // Unlock icon
        }


        /// <summary>
        /// Updates the main window to show that the media is unsynchronized
        /// </summary>
        internal void MediaUnsynchronized()
        {
            // Indicate the media is unlocked
            SetLockUnlockIndicator(false, null);

            // Display both media controls
            MediaControlsDisplayMode(false);

            // Adjust the File menu item text 
            MenuLockUnlockMediaPlayers.Text = "Lock Media Players";
            MenuLockUnlockMediaPlayersIcon.Glyph = "\uE1F6"; // Lock icon
        }


        /// <summary>
        /// Updates the main windows to show any dynamic measurement information
        /// </summary>
        internal void DisplayDynamicMeasurement(bool showRMSCombinedOnly, double? measurement, double? range, double? rmsCombined, double? rmsTargetA, double? rmsTargetB)
        {
            // Measurement dynamic display
            string measurementText = string.Empty;
            if (measurement is not null)
            {
                measurementText = $"{measurement*1000:F0}mm";
                MeasurementIndictor.Visibility = Visibility.Visible;
            }
            else
                MeasurementIndictor.Visibility = Visibility.Collapsed;

            Measurement.Text = measurementText;

            // Range dynamic display
            string rangeText = string.Empty;
            if (range is not null)
            {
                rangeText = $"range:{range:F2}m";
            }
            Range.Text = rangeText;

            if (showRMSCombinedOnly)
            {
                // RMS dynamic display
                string rmsText = string.Empty;
                if (rmsCombined is not null)
                {
                    rmsText = $"rms:{rmsCombined * 1000:F1}mm";
                }
                RMS.Text = rmsText;
            }
            else
            {
                RMS.Inlines.Clear();

                if (rmsCombined is null) return;

                var (redBrush, greenBrush) = ThemeAwareRmsBrushes(RMS);

                RMS.Inlines.Add(new Run { Text = $"rms:{rmsCombined * 1000:F1}(" });

                if (rmsTargetA is not null)
                    RMS.Inlines.Add(new Run { Text = $"{rmsTargetA * 1000:F0}", Foreground = redBrush });
                else
                    RMS.Inlines.Add(new Run { Text = "—" });

                RMS.Inlines.Add(new Run { Text = "/" });

                if (rmsTargetB is not null)
                    RMS.Inlines.Add(new Run { Text = $"{rmsTargetB * 1000:F0}", Foreground = greenBrush });
                else
                    RMS.Inlines.Add(new Run { Text = "—" });

                RMS.Inlines.Add(new Run { Text = ")mm" });
            }
        }

        static SolidColorBrush MakeBrush(byte a, byte r, byte g, byte b) =>
                        new(new Color() { A = a, R = r, G = g, B = b });

        static (Brush red, Brush green) ThemeAwareRmsBrushes(FrameworkElement fe)
        {
            // Use ActualTheme so it reflects app/system + per-element overrides
            bool isDark = fe.ActualTheme == ElementTheme.Dark;

            // Tuned for readability:
            // - In light theme, use deeper tones.
            // - In dark theme, use lighter tones.
            var red = isDark ? MakeBrush(255, 255, 160, 160) : MakeBrush(255, 178, 34, 34);   // Light red vs Firebrick
            var green = isDark ? MakeBrush(255, 160, 245, 160) : MakeBrush(255, 34, 139, 34);   // Light green vs ForestGreen
            return (red, green);
        }


        /// <summary>
        /// Diagnostic information state has changed (or is being initially set)
        /// Setup can MainWindow thing are controlled by the diagnostic information
        /// </summary>
        /// <param name="diagnosticInformation"></param>
        internal void _SetDiagnosticInformation(bool diagnosticInformation)
        {
            if (diagnosticInformation)
            {
                // Debug Diagnostics Dump Help>Diagnostics Dump 
                MenuDiagsDump.IsEnabled = true;

                // Testing Help>Testing
                MenuTesting.IsEnabled = true;
            }

            // Inform everyone of the state change
            // Use eSettingsWindowEvent.DiagnosticInformation
            mainWindowHandler?.Send(new SettingsWindowEventData(eSettingsWindowEvent.DiagnosticInformation)
            {
                diagnosticInformation = SettingsManagerLocal.DiagnosticInformation
            });

        }


        /// <summary>
        /// Experimental setting has changed (or is being initially set)
        /// </summary>
        /// <param name="_experimentalEnabled"></param>
        internal void _SetExperimental(bool _experimentalEnabled, 
                                       bool _experimentalFeatureSetAEnabled, 
                                       bool _experimentalFeatureSetBEnabled, 
                                       bool _experimentalFeatureSetCEnabled)
        {
            experimentalEnabled = _experimentalEnabled;
            experimentalFeatureSetAEnabled = _experimentalFeatureSetAEnabled;
            experimentalFeatureSetBEnabled = _experimentalFeatureSetBEnabled;
            experimentalFeatureSetCEnabled = _experimentalFeatureSetCEnabled;            
        }


        /// <summary>
        /// Used to display any exceptions during the Open() function
        /// </summary>
        /// <param name="mediaFileSpec"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private async Task ShowCannotSynchronizedDialogAsync()
        {
            ContentDialog confirmationDialog = new()
            {
                Title = "Failed to Synchronize media",
                Content = $"You must pause the media before it can be locked",
                CloseButtonText = "OK",

                // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                XamlRoot = this.Content.XamlRoot
            };

            // Display the dialog
            await confirmationDialog.ShowAsync();
        }


        /// <summary>
        /// The users has selected a different tab in the <TabView> at bottom of the screen
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) => _ = OnNavigationViewSelectionChangedAsync(args);
        private async Task OnNavigationViewSelectionChangedAsync(NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {              
                await ShowSettingsWindowAsync();
                NavigationView.SelectedItem = (NavigationViewItem)NavigationView.MenuItems[0];  // Assuming Events is the first item
            }
            else
            {
                UpdateNavigationViewVisibility();
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
                // This can happen if the survey and movies are loaded and the user clicks the settings a few times.
                if (entryCount == 1)
                {
                    // Initialize if necessary
                    SettingsWindow settingsWindow = new(mediator, this, surveyClass, report, section);

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

                    // Check if the settings windows indicated a recalculation is required
                    if (settingsWindow.RecalculationRequired())
                    {
                        InfoBarReCalc.IsOpen = true;
                        await Task.Delay(500); // Give the user a chance to see the info bar open

                        // User has changed something that requires a recalculation of all event measurements
                        await CheckIfEventMeasurementsAreUpToDateAsync(true);


                        InfoBarReCalc.IsOpen = false;
                    }
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

        /// <summary>
        /// Used to swap between the Events/Results/Output pages
        /// </summary>
        private void UpdateNavigationViewVisibility()
        {
            var selectedItem = (NavigationViewItem)NavigationView.SelectedItem;


            // Check if no view is selected
            if (selectedItem is null)
            {
                selectedItem = (NavigationViewItem)NavigationView.MenuItems[0];  // Assuming Events is the first item

                // Set the initial selected item to the "EventsPage"
                NavigationView.SelectedItem = selectedItem; 
                ContentFrame.Content = eventsControl;  // Load EventsControl into the Frame
            }

            var tag = selectedItem.Tag.ToString();

            switch (tag)
            {
                case "EventsPage":
                    ContentFrame.Content = eventsControl;  // Assuming EventsControl is already defined
                    break;
                case "ResultsPage":
                   //??? ContentFrame.Content = new Results(); // Replace with your Results control
                    break;
                case "OutputPage":
                    ContentFrame.Content = report;  // Assuming Report is already defined
                    report.Visibility = Visibility.Visible;
                    break;
            }
        }


        /// <summary>
        /// Add the selected survey to the recent surveys list
        /// </summary>
        /// <param name="filePath"></param>
        private void AddToRecentSurveys(string filePath)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var recentSurveys = (localSettings.Values[RECENT_SURVEYS_KEY] as string[]) ?? [];

            // Remove if already exists
            var list = new List<string>(recentSurveys);
            list.Remove(filePath);

            // Add to beginning
            list.Insert(0, filePath);

            // Keep only MAX_RECENT_SURVEYS items
            if (list.Count > MAX_RECENT_SURVEYS_SAVED)
                list.RemoveRange(MAX_RECENT_SURVEYS_SAVED, list.Count - MAX_RECENT_SURVEYS_SAVED);

            // Save back to settings
            localSettings.Values[RECENT_SURVEYS_KEY] = list.ToArray();

            UpdateRecentSurveysMenu();
        }


        /// <summary>
        /// Remove the selected survey to the recent surveys list
        /// </summary>
        /// <param name="filePath"></param>
        private void RemoveToRecentSurveys(string filePath)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var recentSurveys = (localSettings.Values[RECENT_SURVEYS_KEY] as string[]) ?? [];

            // Remove if already exists
            var list = new List<string>(recentSurveys);
            list.Remove(filePath);

            // Save back to settings
            localSettings.Values[RECENT_SURVEYS_KEY] = list.ToArray();

            UpdateRecentSurveysMenu();
        }


        /// <summary>
        /// Update the recent surveys menu from localSettings
        /// </summary>
        private void UpdateRecentSurveysMenu()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            string[]? recentSurveys = localSettings.Values[RECENT_SURVEYS_KEY] as string[];

            // Clear existing items in the MenuFlyoutSubItem
            MenuRecentSurveys.Items.Clear();

            if (recentSurveys == null || recentSurveys.Length == 0)
            {
                // Add a single "Empty" menu item if no recent surveys exist
                var emptyItem = new MenuFlyoutItem
                {
                    Text = "(Empty)",
                    IsEnabled = false
                };
                MenuRecentSurveys.Items.Add(emptyItem);
                return;
            }

            // Add new items from the recentSurveys array
            foreach (var surveyPath in recentSurveys)
            {
                if (MenuRecentSurveys.Items.Count >= maxRecentSurveysDisplayed)
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
                    menuItem.Click += FileRecentSurvey_Click;

                    MenuRecentSurveys.Items.Add(menuItem);
                }
            }
        }


        /// <summary>
        /// Diagnostics dump of class information
        /// </summary>
        private void DumpAllProperties()
        {
            DumpClassPropertiesHelper.DumpAllProperties(this, report, /*ignore*/"mediator,report,mediaControllerHandler,mediaStereoController,MediaPlayerLeft,MediaPlayerRight,surveyClass,stereoProjection,eventsControl");
        }


        /// <summary>
        /// internet Downloading/uploading spinner
        /// </summary>
        private readonly string[] brailleFrames =
        [
            "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"
        ];
        private DispatcherTimer? spinnerTimer;
        private int spinnerframeIndex = 0;

        private void SpinnerTimer_Tick(object? sender, object e)
        {
            if (DownloadIndicator is null) return;
            DownloadIndicator.Glyph = brailleFrames[spinnerframeIndex];
            spinnerframeIndex = (spinnerframeIndex + 1) % brailleFrames.Length;
        }

        private void StartDownloadUploadSpinner()
        {
            spinnerTimer ??= new DispatcherTimer();
            spinnerTimer.Interval = TimeSpan.FromMilliseconds(100);
            // Ensure only one handler
            spinnerTimer.Tick -= SpinnerTimer_Tick;
            spinnerTimer.Tick += SpinnerTimer_Tick;
            spinnerTimer.Start();
        }

        private void StopDownloadUploadSpinner()
        {
            if (spinnerTimer is not null)
            {
                spinnerTimer.Tick -= SpinnerTimer_Tick;
                spinnerTimer.Stop();
            }
            if (DownloadIndicator is not null)
                DownloadIndicator.Glyph = "";
        }



        /// <summary>
        /// Loads the survey if requested and jumps to the specified position if needed.
        /// ExtendedActivationKind.File
        ///     File association activation (double-click a .survey in Explorer, “Open with” 
        ///     using a registered handler, drag a .survey onto the app’s shortcut).
        /// ExtendedActivationKind.Launch
        ///     “Normal” app starts (Start menu, taskbar, debug F5), or launching the EXE 
        ///     with plain command-line args.
        /// ExtendedActivationKind.Protocol
        ///     Custom URI scheme activation (e.g., UnderwaterSurveyor://open?file=...&start=... 
        ///     from a browser, Run dialog, or hyperlink).
        /// </summary>
        private void LoadSurveyIfRequestedAndJumpToPoistionIfNeeded()
        {
            var activationArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            var launchArgs = activationArgs.Data as ILaunchActivatedEventArgs;

            string surveyFileSpec = string.Empty;
            double? startSeconds = null;
            if (activationArgs.Kind == ExtendedActivationKind.File)
            {
                var fileArgs = activationArgs.Data as IFileActivatedEventArgs;
                if (fileArgs?.Files.Count > 0 &&
                    fileArgs.Files[0] is StorageFile file &&
                    file.FileType == ".survey")
                {
                    surveyFileSpec = file.Path;
                }
            }
            else if (activationArgs.Kind == ExtendedActivationKind.Launch)
            {
                var args = Environment.GetCommandLineArgs();
                if (args.Length >= 2)
                {
                    surveyFileSpec = args[1];

                    // Get for 2nd parameter for a start position (in seconds)
                    GetArgs.GetArg("/Start", out double? startPosition);
                }
            }
            else if (activationArgs.Kind == ExtendedActivationKind.Protocol)
            {
                var protocolArgs = activationArgs.Data as IProtocolActivatedEventArgs;
                var uri = protocolArgs?.Uri;

                if (uri is not null)
                {
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

                    if (query is not null)
                    {
                        surveyFileSpec = query["file"] ?? string.Empty;
                        string startParam = query["start"] ?? string.Empty;

                        if (startParam != string.Empty &&
                            double.TryParse(startParam, System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            out var secs))
                        {
                            startSeconds = secs;
                        }
                    }
                }
            }

            if (surveyFileSpec != string.Empty)
            {
                Debug.WriteLine($"Activated with file: {surveyFileSpec}, move to position:{startSeconds} activation kind: {activationArgs.Kind}");

                // Small dispatcher delay to ensure UI is fully rendered
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(100); // optional but helps UI be ready
                    report.Info("", $"App activated with associated file: {surveyFileSpec}");

                    await OpenSurveyFromFileAsync(surveyFileSpec);

                    // Go to the start position in the video if requested
                    if (startSeconds is not null)
                    {
                        // Go to the start position in the video (only if stereo set and synced)
                        if (surveyClass?.Data.Info.SurveyType == Survey.SurveyType.StereoFish &&
                            surveyClass?.Data.Sync.IsSynchronized == true)
                        {
                            TimeSpan startPositionTS = TimeSpan.FromSeconds((double)startSeconds);

                            report.Info("", $"Start position: {startPositionTS:hh\\:mm\\:ss\\.ff} ({startPositionTS.TotalSeconds:F2})");
                            await Task.Delay(150); // optional but helps UI be ready
                            await mediaStereoController.FrameMoveAsync(SurveyorMediaPlayer.eCameraSide.None/*Stereo*/, 1);
                            await Task.Delay(400); // optional but helps UI be_ready
                            mediaStereoController.FrameJump(SurveyorMediaPlayer.eCameraSide.None/*Stereo*/, startPositionTS);

                            // Find the event at or before this position
                            Event? evt = eventsControl.FindEventFromTimeSpanTimelineController(startPositionTS);
                            if (evt is not null)
                            {
                                // Jump to the correct event in the EventControl and display the correct frame
                                report.Info("", $"Start event: {evt.TimeSpanTimelineController:hh\\:mm\\:ss\\.ff} ({evt.TimeSpanTimelineController.TotalSeconds:F2})");
                                eventsControl.GoToEvent(evt);
                            }
                            else
                            {
                                report.Info("", $"No event found at or before start position");
                            }
                        }
                        // Go to the start position in the video (only if mono set)
                        else if (surveyClass?.Data.Info.SurveyType == Survey.SurveyType.MonoFish ||
                                 surveyClass?.Data.Info.SurveyType == Survey.SurveyType.MonoBenthic)
                        {
                            TimeSpan startPositionTS = TimeSpan.FromSeconds((double)startSeconds);
                            report.Info("", $"Start position: {startPositionTS:hh\\:mm\\:ss\\.ff} ({startPositionTS.TotalSeconds:F2})");
                            await Task.Delay(150); // optional but helps UI be ready
                            MediaPlayerLeft.FrameMove(1);
                            await Task.Delay(400); // optional but helps UI be ready
                            MediaPlayerLeft.FrameJump(startPositionTS);
                            // Find the event at or before this position
                            Event? evt = eventsControl.FindEventFromTimeSpanTimelineController(startPositionTS);
                            if (evt is not null)
                            {
                                // Jump to the correct event in the EventControl and display the correct frame
                                report.Info("", $"Start event: {evt.TimeSpanTimelineController:hh\\:mm\\:ss\\.ff} ({evt.TimeSpanTimelineController.TotalSeconds:F2})");
                                eventsControl.GoToEvent(evt);
                            }
                            else
                            {
                                report.Info("", $"No event found at or before start position");
                            }
                        }
                    }

                });
            }
        }


        // ** End of MainWindow **

        // Placeholder methods to satisfy handler references if not already defined (guard against duplication)
        internal void MediaSynchronizedPlaceholder(TimeSpan? positionOffset) { }
        internal void MediaUnsynchronizedPlaceholder() { }
        internal void DisplayDynamicMeasurementPlaceholder(bool showRMSCombinedOnly, double? measurement, double? range, double? rmsCombined, double? rmsTargetA, double? rmsTargetB) { }
        internal void _SetDiagnosticInformationPlaceholder(bool diag) { }
        internal void _SetExperimentalPlaceholder(bool a, bool b, bool c, bool d) { }

    }


    /// <summary>
    /// Mediator Handler for MainWindow
    /// </summary>
    public class MainWindowHandler : TListener
    {
        private readonly MainWindow _mainWindow;

        public MainWindowHandler(IMediator mediator, MainWindow mainWindow) : base(mediator, mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public override void Receive(TListener listenerFrom, object? message)
        {
            if (message is MediaStereoControllerEventData)
            {
                MediaStereoControllerEventData data = (MediaStereoControllerEventData)message;

                switch (data.mediaStereoControllerEvent)
                {
                    case eMediaStereoControllerEvent.MediaSynchronized:
                        SafeUICall(() => _mainWindow.MediaSynchronized(data.positionOffset));
                        break;

                    case eMediaStereoControllerEvent.MediaUnsynchronized:
                        SafeUICall(() => _mainWindow.MediaUnsynchronized());
                        break;

                    case eMediaStereoControllerEvent.DisplayDynamicMeasurement:
                        if (data.showRMSCombinedOnly is not null)
                        {
                            SafeUICall(() => _mainWindow.DisplayDynamicMeasurement((bool)data.showRMSCombinedOnly,
                                                                                   data.measurement,
                                                                                   data.range,
                                                                                   data.rmsCombined,
                                                                                   data.rmsTargetA,
                                                                                   data.rmsTargetB));
                        }
                        break;

                }
            }
            else if (message is MediaPlayerEventData)
            {
                MediaPlayerEventData data = (MediaPlayerEventData)message;

                // If a new frame is being rendered then clear any dynamic measurements
                // We are depending on the FrameRendered message from the left camera
                // The dynamic measurements are only from stereo calculates, we only need to clear
                // the measurement once (no left frame/right frame concept)
                if (data.cameraSide == SurveyorMediaPlayer.eCameraSide.Left &&
                    data.mediaPlayerEvent == eMediaPlayerEvent.FrameRendered)
                {
                    SafeUICall(() => _mainWindow.DisplayDynamicMeasurement(true, null, null, null, null, null));
                }
            }
            else if (message is SettingsWindowEventData)
            {
                SettingsWindowEventData data = (SettingsWindowEventData)message;

                switch (data.settingsWindowEvent)
                {
                    // The user has changed the Diagnostic Information settings
                    case eSettingsWindowEvent.DiagnosticInformation:
                        if (data.diagnosticInformation is not null)
                        {
                            _mainWindow._SetDiagnosticInformation((bool)data!.diagnosticInformation);
                        }
                        break;
                    // The user has changed the Experimental settings
                    case eSettingsWindowEvent.Experimental:
                        if (data.experimentalEnabled is not null &&
                            data.experimentalFeatureSetAEnabled is not null &&
                            data.experimentalFeatureSetBEnabled is not null &&
                            data.experimentalFeatureSetCEnabled is not null)
                        {
                            _mainWindow._SetExperimental((bool)data!.experimentalEnabled,
                                                         (bool)data.experimentalFeatureSetAEnabled,
                                                         (bool)data.experimentalFeatureSetBEnabled,
                                                         (bool)data.experimentalFeatureSetCEnabled);
                        }
                        break;

                }
            }

        }
    }

}


