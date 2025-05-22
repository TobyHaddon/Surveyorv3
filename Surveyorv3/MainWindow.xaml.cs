using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.DesktopWap.Helper;
using Surveyor.Events;
using Surveyor.Helper;
using Surveyor.User_Controls;
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
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;
using WinUIEx;
using static Surveyor.Helper.TelemetryLogger;
using static Surveyor.MediaStereoControllerEventData;
using static Surveyor.Survey.DataClass;
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

        // Experimental
        private bool experimentalEnabled = false;


        public MainWindow()
        {
            this.InitializeComponent();
            
            // Event first before the app window is commited to closing (i.e. can be cancelled)
            if (this.AppWindow is not null)
                this.AppWindow.Closing += AppWindow_Closing;

            // Inform the Reporter of the DispatcherQueue
            report.SetDispatcherQueue(DispatcherQueue);

            // Inform the Events Control of the DispatcherQueue
            eventsControl.SetDispatcherQueue(DispatcherQueue);

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
                if ((isOnlineRememberedStatus is null && _isOnline) ||      // If first time online state seen
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
                        // needed to keep the tooltip working as the glyph changes)
                        NetworkConnectionIndicator.Text = "    ";
                        ToolTipService.SetToolTip(NetworkConnectionIndicator, "");
                    });
                }

                isOnlineRememberedStatus = _isOnline;                                    // Remember the current state of if the internet is available
                useInternetRememberedEnabled = SettingsManagerLocal.UseInternetEnabled;  // Remember the current state of if we are allowed to use the internet
                return Task.CompletedTask;
            }, Surveyor.Priority.Normal);


            // Initialize internet download/upload mananger
            internetQueue = new(report);
            _ = internetQueue.Load(); // fire-and-forget
            networkManager.RegisterAction(async (_isOnline, _isMetered, _bars) =>
            {
                if (_isOnline)
                {
                    await internetQueue.DownloadUpload(_isMetered);
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
            // WinUI 3 1.3 or later, use interop APIs to get the AppWindow.           
            AppTitleBar.Loaded += AppTitleBar_Loaded;
            AppTitleBar.SizeChanged += AppTitleBar_SizeChanged;
            ExtendsContentIntoTitleBar = true;

            // Now the interactive regions of the titlebar has been established let 
            // remove the LockUnlockIndicator from the titlebar that was only there
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
            _SetExperimental(SettingsManagerLocal.ExperimentalEnabled);


            // Add the help documents to the Help menu
            // Fix for CS1503: Argument 1: cannot convert from 'System.Collections.Generic.IList<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase>' to 'Microsoft.UI.Xaml.Controls.ItemCollection'

            // The issue arises because `MenuHelp.Items` is of type `IList<MenuFlyoutItemBase>`,
            // but the `Initialize` method of `HelpDocuments` expects an `ItemCollection`.
            // To fix this, we need to pass the correct type to the `Initialize` method.

            helpDocuments.Initialize(MenuHelp.Items, // Pass the MenuFlyoutSubItem directly instead of its Items property
                                     HelpDocumentsPDFSection,
                                     HelpDocumentsVideosSection,
                                     HelpDocumentsDOCSection,
                                     HelpDocumentsXLSSection);
       

            // Report that the app has loaded
            
            // Debug.WriteLine($"Local Folder path:{ApplicationData.Current.LocalFolder.Path}");
            if (!SettingsManagerLocal.DiagnosticInformation)
            {
                report.Info("", $"App Loaded Ok (Local Path:{ApplicationData.Current.LocalFolder.Path})");
            }
            else
            {
                report.Info("", $"App Loaded Ok");
                report.Info("", $"Local Path:{ApplicationData.Current.LocalFolder.Path}");
                report.Info("", $"Exec Path:{Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!}");
            }
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

                // Get the background colour used by that theme
                var color = TitleBarHelper.ApplySystemThemeToCaptionButtons(this) == Colors.White ? "Dark" : "Light";

                // Based on the background colour select a suitable application icon 
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
        /// Called from the stereo controler to save the current video frame
        /// </summary>
        /// <param name="controlType"></param>
        public async Task SaveCurrentFrame(SurveyorMediaControl.eControlType controlType)
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
                    await SaveAsSurvey();
                }
                else if (result == ContentDialogResult.Secondary)
                    return;
            }

            // Recheck of the projectClass is null


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
                    await MediaPlayerLeft.SaveCurrentFrame(framesPath, positionTimelineController/*time stamp*/, true/*syncdPair*/);
                    await MediaPlayerRight.SaveCurrentFrame(framesPath, positionTimelineController/*time stamp*/, true/*syncdPair*/);
                }
                else if (controlType == SurveyorMediaControl.eControlType.Primary && MediaPlayerLeft.Position is not null)
                {
                    // Save the left media player frame, use the timestamp of the media player
                    await MediaPlayerLeft.SaveCurrentFrame(framesPath, (TimeSpan)MediaPlayerLeft.Position/*time stamp*/, false/*syncdPair*/);
                }
                else if (controlType == SurveyorMediaControl.eControlType.Secondary && MediaPlayerRight.Position is not null)
                {
                    // Save the right media player frame, use the timestamp of the media player
                    await MediaPlayerRight.SaveCurrentFrame(framesPath, (TimeSpan)MediaPlayerRight.Position/*time stamp*/, false/*syncdPair*/);
                }
            }
        }


        /// <summary>
        /// Display the passed pointer coordicates in a textbox on the navigation panel
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
        /// Returns the posiiton offsets of the left and right media players
        /// This is by the SurveryorTesting class to check the media players are correctly in sync
        /// </summary>
        /// <returns></returns>
        public (TimeSpan?, TimeSpan?) GetMediaPlayerPoisitions()
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

            if (surveyClass is not null)
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

            if (surveyClass is not null)
            {
                int countMeasurementPointsMissingSpecies = surveyClass.Data.Events.EventList.Cast<Event>()
                    .Where(e => e.EventDataType == SurveyDataType.SurveyMeasurementPoints)
                    .Select(e => e.EventData as SurveyMeasurement)
                    .Count(m => m != null && m.SpeciesInfo?.Species == null);

                if (countMeasurementPointsMissingSpecies > 0)
                {
                    showMissingSpeciesInfoInfoBar = true;
                }
                else
                {
                    int countStereoPointsMissingSpecies = surveyClass.Data.Events.EventList.Cast<Event>()
                        .Where(e => e.EventDataType == SurveyDataType.SurveyStereoPoint)
                        .Select(e => e.EventData as SurveyStereoPoint)
                        .Count(s => s != null && s.SpeciesInfo?.Species == null);
                    if (countStereoPointsMissingSpecies > 0)
                    {
                        showMissingSpeciesInfoInfoBar = true;
                    }
                    else
                    {
                        int countSinglePointsMissingSpecies = surveyClass.Data.Events.EventList.Cast<Event>()
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
                // Show the info bar
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


        ///
        /// EVENTS
        /// 


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
        /// Window close has been requested by user, check for open Surveys
        /// </summary>
        /// <returns></returns>
        private async void AppWindow_Closing(object sender, AppWindowClosingEventArgs e)
        {
            Debug.WriteLine("AppWindow_Closing Entered");

            // Cancel the close in case we don't want it
            e.Cancel = true;


            // Check if there is an unsaved survey
            if (await CheckForOpenSurveyAndClose() == true)
            {
                await internetQueue.Unload();

                await mediaStereoController.Unload();

                // Remove listener for theme changes
                var rootElement = (FrameworkElement)Content;
                rootElement.ActualThemeChanged -= OnActualThemeChanged;

                this.Close(); // This will NOT retrigger AppWindow.Closing
                Debug.WriteLine("AppWindow_Closing Exited this.Close");

                // Dump the reporter content to disk
                report.Unload();
            }
            else
            {
                // User canceled, prevent the app from closing
                e.Cancel = true;
            }

            TelemetryLogger.TrackAppStartStop(TrackAppStartStopType.AppStopOk);
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
        /// Create a new survey 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileSurveyNew_Click(object sender, RoutedEventArgs e)
        {
            // First check if an existing survey is already open
            if (await CheckForOpenSurveyAndClose() == true)
            {
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
                IReadOnlyList<StorageFile> mediaFilesSelected = await DispatcherQueue.EnqueueAsync(async () =>
                {
                    return await openPicker.PickMultipleFilesAsync();
                });

                // Proceed is files selected
                if (mediaFilesSelected.Count > 0)
                {
                    // Create a new empty survey
                    surveyClass = new Survey(report);
                    surveyClass.PropertyChanged += Survey_PropertyChanged;

                    // Inform the EventControl of the new survey events
                    eventsControl.SetEvents(surveyClass.Data.Events.EventList);

                    // Load the Info and Media user control to setup the survey
                    SurveyInfoAndMediaUserControl.SetupForContentDialog(SurveyInfoAndMediaContentDialog, mediaFilesSelected);
                    SurveyInfoAndMediaUserControl.SetReporter(report);

                    try
                    {
                        // ** Important notes **
                        // The UserControl SurveyInfoAndMedia is displayed within a ContentDialog for 
                        // the purpose of setting up a new survey (also using from a SettingsCard)
                        // I stuggled to get the ContentDialog to show width necessary to fully display
                        // the UserControl.  The solution was to:
                        // Set <x:Double x:Key="ContentDialogMaxWidth">1200</x:Double> in the <ResourceDictionary>
                        // to setup the ContentDialog in XAML in MainWindow and place it in Grid.Row=2.
                        // This took a lot of trail and error. It seems to effect the title bar is left in
                        // default row zero.
                        ContentDialogResult result = await SurveyInfoAndMediaContentDialog.ShowAsync();
                        if (result == ContentDialogResult.Primary)
                        {
                            SurveyInfoAndMediaUserControl.SaveForContentDialog(surveyClass);

                            // Open Media Files
                            await OpenSVSMediaFiles();

                        }
                    }
                    catch (Exception ex)
                    {
                        // Log or handle the exception as needed
                        report.Error("", $"Error showing SurveyInfoAndMediaContentDialog: {ex.Message}");
                    }
                }
            }
            
            SetMenuStatusBasedOnProjectState();

            // Reset Info Bar dismissed status
            infoBarCalibrationMissingDismissed = false;
            infoBarSpeciesInfoMissingDismissed = false;
        }


        /// <summary>
        /// Open an existing survey file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileSurveyOpen_Click(object sender, RoutedEventArgs e)
        {
            // First check if an existing survey is already open
            if (await CheckForOpenSurveyAndClose() == true)
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
                    await OpenSurvey(file.Path);

                    // Add to Recent Surveys
                    AddToRecentSurveys(file.Path);
                    UpdateRecentSurveysMenu();

                    // Check if the preferred calibration data is the one being using for
                    // the current event measurements calculations
                    await CheckIfEventMeasurementsAreUpToDate(false/*recalc only if necessary*/);
                }

                // Enable/Disable menu items based on the current project state
                SetMenuStatusBasedOnProjectState();
            }
        }


        /// <summary>
        /// Save the currently open survey file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSurveySave_Click(object? sender, RoutedEventArgs? e)
        {
            if (surveyClass is not null)
            {
                if (surveyClass.Data.Info.SurveyPath == null || surveyClass.Data.Info.SurveyFileName == null)
                {
                    FileSurveySaveAs_Click(sender, e);
                }
                else
                {
                    // Save
                    surveyClass.SurveySave();
                }
            }

            SetMenuStatusBasedOnProjectState();
        }

        /// <summary>
        /// Save the currently open survey file with a new name
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileSurveySaveAs_Click(object? sender, RoutedEventArgs? e)
        {
            await SaveAsSurvey();
            SetMenuStatusBasedOnProjectState();
        }


        /// <summary>
        /// Used to open a selected recent survey file from the 'Recent Surveys' sub menu
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileRecentSurvey_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuFlyoutItem;
            if (menuItem is not null)
            {
                if (menuItem.Tag is string filePath)
                {
                    // First check if an existing survey is already open
                    if (await CheckForOpenSurveyAndClose() == true)
                    {
                        // Open survey in the regular way
                        int ret = await OpenSurvey(filePath);

                        if (ret == 0)
                        {
                            // Check if the preferred calibration data is the one being using for
                            // the current event measurements calculations
                            await CheckIfEventMeasurementsAreUpToDate(false/*recalc only if necessary*/);
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

                // Enable/Disable menu items based on the current project state
                SetMenuStatusBasedOnProjectState();
            }
        }

        /// <summary>
        /// Close the currently open survey file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileSurveyClose_Click(object sender, RoutedEventArgs e)
        {
            await CheckForOpenSurveyAndClose();
        }

        /// <summary>
        /// Import calibration data into the survey
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileImportCalibration_Click(object sender, RoutedEventArgs e)
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
                            // We are only storing one calib and we already have this one so inform user we will ignore
                            // Cancel Only
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file. No action will be taken.";
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true && surveyClass.Data.Calibration.PreferredCalibrationDataIndex == index)
                        {
                            // We are storing multiple calibs and this is the preferred one so inform user we will ignore
                            // Cancel Only
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file and it is the preferred calibration so no action will be taken.";
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true && surveyClass.Data.Calibration.PreferredCalibrationDataIndex != index)
                        {
                            // We are storing multiple calibs and this is not the preferred one so ask user if they want this to be the preferred calibration
                            // Ok/Cancel
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file but it is not the preferred calibration. Do you would like to make it the preferred calibration?";
                            primaryButtonText = "Ok";
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
                            // We are only storing one calib and we already have this one but under a different Description so ask the user if the Description should be updated
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file but with a different Description. Do you you want to update the Description?";
                            primaryButtonText = "Ok";
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true && surveyClass.Data.Calibration.PreferredCalibrationDataIndex == index)
                        {
                            // We are storing multiple calibs and this is the preferred one so info user we will ignore
                            message = $"The calibration data '{calibrationData.Description}' is already in the survey file but with a different Description. Do you want to update the Description?";
                            primaryButtonText = "Ok";
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true && surveyClass.Data.Calibration.PreferredCalibrationDataIndex != index)
                        {
                            // We are storing multiple calibs and this is not the preferred one so ask user if they want this to be the preferred calibration
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
                                makeThisCalibPreferred = int.MaxValue;      // Basically if we are added a new calib any non -1 value will make it the preferred calib
                                addNewCalib = true;
                            }
                            else if (surveyClass.Data.Calibration.PreferredCalibrationDataIndex == 0 && surveyClass.Data.Calibration.CalibrationDataList.Count == 1)
                            {
                                // We are only storing one calib and we don't have this one so ask the user if they want to remove the existing one and add this one
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
                                    makeThisCalibPreferred = int.MaxValue;      // Basically if we are added a new calib any non -1 value will make it the preferred calib
                                }
                            }
                        }
                        else if (surveyClass.Data.Calibration.AllowMultipleCalibrationData == true)
                        {
                            // We are storing multiple calibs and already have at less one storage. Ask the user if they want this new one to be the preferred calibration
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
                                makeThisCalibPreferred = int.MaxValue;      // Basically if we are added a new calib any non -1 value will make it the preferred calib
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

                // Load the calibation data to the Stereo Projection class 
                stereoProjection.SetCalibrationData(surveyClass.Data.Calibration);

                // Using the left player get the current frame size (if any)
                SetCalibratedIndicator(MediaPlayerLeft.FrameWidth, MediaPlayerLeft.FrameHeight);


                // Check if the preferred calibration data is the one being using for
                // the current event measurements calculations
                await CheckIfEventMeasurementsAreUpToDate(false/*recalc only if necessary*/);


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
        private async void FileSettings_Click(object sender, RoutedEventArgs e)
        {
            await ShowSettingsWindow();
        }


        /// <summary>
        /// Exit the app
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileExit_Click(object sender, RoutedEventArgs e)
        {
            await CheckForOpenSurveyAndClose();
            //???await mediaStereoController.MediaClose();

            SetTitle("");
            SetLockUnlockIndicator(null, null);

            Application.Current.Exit();
        }


        /// <summary>
        /// Users wants to lock or unlock the media players. 
        /// i.e. synchronize or unsynchronize the media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void InsertLockUnlockMediaPlayers_Click(object sender, RoutedEventArgs e)
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
                                                    MaxWidth = 320, // Limit width to allow wrapping
                                                    HorizontalAlignment = HorizontalAlignment.Left
                                                }
                                            }
                                        }
                                    }
                                },
                                PrimaryButtonText = "Enable",
                                SecondaryButtonText = "Current Position",
                                CloseButtonText = "Cancel",
                                DefaultButton = ContentDialogButton.Primary,
                                XamlRoot = this.Content.XamlRoot // Ensure this points to the correct XamlRoot
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

                    // Lock the left and right media controlers
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

                    // Engage to the MediaTimelineController
                    if (reEnable || newPosition)
                        await mediaStereoController.MediaLockMediaPlayers();
                }
                else
                {
                    // Check user is sure they want to unlock the media players


                    // Lock the left and right media controlers
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
            else
            {
                // DON'T await this method. I don't understand why but it causes the
                // dialog to be non-modal
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                await ShowCannotSynchronizedDialog();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }

        }


        /// <summary>
        /// Insert a marker (as an Event) to indicate either the start or end of a survey within the movies
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InsertSurveyStartStopMarker_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                mediaStereoController.GetFullMediaPosition(out TimeSpan positionTimelineController, out TimeSpan leftPosition, out TimeSpan rightPosition);

                transectMarkerManager.AddMarker(eventsControl,
                                              positionTimelineController,
                                              leftPosition,
                                              rightPosition);
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
                    // Jump to frame of this event
                    mediaStereoController?.UserReqFrameJump(SurveyorMediaControl.eControlType.Primary, firstMissingSpeciesEvent.TimeSpanTimelineController);
                }
            }
        }


        /// <summary>
        /// Export data
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileExport_Click(object sender, RoutedEventArgs e)
        {

        }



        /// <summary>
        /// This method is called to check that suitable calibration data is available for the current frame size and that 
        /// it is set as the preferred calibration data.  If that isn't the case the method to see if there is any calidation data
        /// the support the current frame size but is not set to be preferred.  If that is the case the user is asked if they want to
        /// make that calibration data the preferred calibration data.
        /// </summary>
        /// <returns></returns>
        internal async Task<bool> CheckIfMeasurementSetupIsReady()
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


                    // Check if suitable preferred calibration data wwas found
                    if (!ready)
                    {
                        // Parse the calibration data to see if there is any that supports the current frame size
                        for (int i = 0; i < calibrationClass.CalibrationDataList.Count; i++)
                        {
                            // Ingore the preferred calibration data as that has been checked above
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
                        await CheckIfEventMeasurementsAreUpToDate(false/*recalc only if necessary*/);

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
            MediaControlPrimary.MouseWheelEvent(sender, e);
        }
        private void RightSubGrid_MouseWheel(object sender, PointerRoutedEventArgs e)
        {
            MediaControlSecondary.MouseWheelEvent(sender, e);
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
        /// Keyboard accelerator to testing code
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void HelpTesting_Click(object sender, RoutedEventArgs e)
        {
            // Show the testing window
            await ShowSurveyorTestingsWindow();
        }


        /// <summary>
        /// Display the Surveyor Testing window
        /// </summary>
        private int surveyorTestingEntryCount = 0;
        private async Task ShowSurveyorTestingsWindow()
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
        /// Keyboard accelerator to testing code
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void HelpAbout_Click(object sender, RoutedEventArgs e)
        {
            // Open the settings windows 'About' section
            await ShowSettingsWindow("About");
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
        /// The ViewPorts that control the area the MediaPlayer/ImageFrame/CanvasFrame 
        /// changed physical dimensions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftViewbox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Get the new size of the Viewbox
            double newWidth = e.NewSize.Width;
            double newHeight = e.NewSize.Height;

            Debug.WriteLine($"[{DateTime.Now:hh:MM:yyyy HH:mm:sd.ff}] LeftViewbox_SizeChanged:{newWidth:F1}x{newHeight:F1}");
            MediaPlayerLeft.RenderedPixelScreenSizeChanged(newWidth, newHeight);
        }

        private void RightViewbox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Get the new size of the Viewbox
            double newWidth = e.NewSize.Width;
            double newHeight = e.NewSize.Height;

            Debug.WriteLine($"[{DateTime.Now:hh:MM:yyyy HH:mm:sd.ff}] RightViewbox_SizeChanged:{newWidth:F1}x{newHeight:F1}");
            MediaPlayerRight.RenderedPixelScreenSizeChanged(newWidth, newHeight);
        }


        ///
        /// PRIVATE METHODS
        /// 


        /// <summary>
        /// Open the survey files
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <param name="surveyFileName"></param>
        /// <returns>-999 If user aborts</returns>
        internal async Task<int> OpenSurvey(string surveyFileSpec)
        {
            int ret = 0;

            if (surveyClass is null)
            {
                surveyClass ??= new Survey(report);
                surveyClass.PropertyChanged += Survey_PropertyChanged;
            }
            else
            {
//??? TOBEDELETED
//#if !No_MagnifyAndMarkerDisplay
//                MagnifyAndMarkerDisplayLeft.Close();
//                MagnifyAndMarkerDisplayRight.Close();
//#endif
                await surveyClass.SurveyClose();
            }


            ret = await surveyClass.SurveyLoad(surveyFileSpec);

            if (ret == 0 &&
                surveyClass.Data is not null && surveyClass.Data.Media is not null && surveyClass.Data.Media.MediaPath is not null)
            {
                // Check if the left media file(s) exist
                ret = await CheckIfMediaFileExists(true/*trueLeftFalseRight*/, surveyClass.Data.Media, surveyFileSpec);
                if (ret == 0)
                    ret = await CheckIfMediaFileExists(false/*trueLeftFalseRight*/, surveyClass.Data.Media, surveyFileSpec);

                if (ret == 0)
                {
                    // Setup the events
//??? TOBEDELETED
//#if !No_MagnifyAndMarkerDisplay
//                    MagnifyAndMarkerDisplayLeft.SetEvents(surveyClass.Data.Events.EventList);
//                    MagnifyAndMarkerDisplayRight.SetEvents(surveyClass.Data.Events.EventList);
//#endif
                    eventsControl.SetEvents(surveyClass.Data.Events.EventList);


                    // Create a StereoProjection instance that allows the user to add measurement points to the media images
                    // Do this before opening the media so the calibration data is available when the media is opened and the frame size established
                    stereoProjection.SetCalibrationData(surveyClass.Data.Calibration);
                    stereoProjection.SetSurveyRules(surveyClass.Data.SurveyRules);

                    // Open Media Files and bind the MediaPlayers if IsSynchronized is true
                    if (await OpenSVSMediaFiles() == true)
                    {
                        // Enable the insert survey transect marker menu item
                        MenuTransectStartStopMarker.IsEnabled = true;

                        // Remember the survey folder
                        SettingsManagerLocal.SurveyFolder = Path.GetDirectoryName(surveyFileSpec);


                        //// Inform the two media players of the MeasurementPointControl instances that allow the user to add measurement points to the media
                        //???LMediaPlayer.SetMeasurementPointControl(measurementPointControl);
                        //???RMediaPlayer.SetMeasurementPointControl(measurementPointControl);

                        // Report Project details
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

            // Display the missing species warnung InfoBar if necessary
            infoBarSpeciesInfoMissingDismissed = false;
            SetInfoBarSpeciesInfoMissing();

            return ret;
        }


        /// <summary>
        /// Save the current survey to a new file
        /// </summary>
        /// <returns></returns>
        private async Task<int> SaveAsSurvey()
        {
            int ret = 0;

            if (this.surveyClass != null)
            {
                // WinUI3 only allows 'savePicker.SuggestedStartLocation' to be one of the standard folders
                //???string? surveyFolder = SettingsManager.ProjectFolder;
                //if (string.IsNullOrEmpty(surveyFolder) == true)
                //    surveyFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

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
                    await surveyClass.SurveySaveAs(file.Path);


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
                        Debug.WriteLine("Failed to save file {file.Path}.");
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Check if there is an existing survey open and if so check if it has unsaved changes
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <returns>true is ok to proceed (i.e. no survey now open)</returns>
        internal async Task<bool> CheckForOpenSurveyAndClose()
        {
            bool ret = false;

            if (this.surveyClass is not null)
            {
                bool closeProject = false;

                if (this.surveyClass.IsDirty == true)
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
                        Title = "Close Project",
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
                        FileSurveySave_Click(null, null);
                            closeProject = true;

                    }
                    else if (result == ContentDialogResult.Secondary)
                    {
                        // "No" button clicked
                        closeProject = true;
                    }
                    // If the select Cancel the Close Survey request is cancelled
                }
                else
                    closeProject = true;


                if (closeProject == true)
                {
                    // Wait for things to settle
                    await Task.Delay(200);

                    // Closes the StereoMediaController, clears the title and the sync indicator
                    await CloseSVSMediaFiles();

                    // Close and clear the Project class (holds the survey data)
                    if (surveyClass is not null)
                    {
                        await surveyClass.SurveyClose();
                        surveyClass = null;
                    }
                   
                    // Clear the calibration data and the survey rules 
                    stereoProjection.ClearCalibrationData();
                    SetCalibratedIndicator(null, null);
                    stereoProjection.ClearSurveyRules();
                    

                    // Display both media controls
                    MediaControlsDisplayMode(false);

                    // Clear the reporter
                    report.Clear();

                    ret = true;
                }
            }
            else
                ret = true;

            SetMenuStatusBasedOnProjectState();

            // Display the missing calibration warning InfoBar if necessary
            infoBarCalibrationMissingDismissed = false;
            SetInfoBarCalibrationMissing();

            // Display the missing species warnung InfoBar if necessary
            infoBarSpeciesInfoMissingDismissed = false;
            SetInfoBarSpeciesInfoMissing();

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
        private async Task<int> CheckIfMediaFileExists(bool trueLeftFalseRight, MediaClass mediaClass, string surveyFileSpec)
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
                        message = $"The {cameraSide.ToLower()} media file {fileNumber}'{fileSpec}' does not exist. Press 'Ok' to try to find the file. Press 'Cancel' to stop loading the survey";
                    else
                        message = $"The {cameraSide.ToLower()} media file {fileNumber}'{fileName}' does not exist. Press 'Ok' to try to find the file. Press 'Cancel' to stop loading the survey";

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
        private async Task<bool> OpenSVSMediaFiles()
        {
            bool ret = true;
            int retOpen;

            if (surveyClass != null && surveyClass.Data.Media.MediaPath != null)
            {
                // Check if already Open and close if necessary
                if (mediaStereoController.MediaIsOpen())
                {
                    await mediaStereoController.MediaClose();
                    SetTitle("");
                    SetLockUnlockIndicator(null, null);
                }


                // Get the media file names
                string mediaFileLeft = "";
                string mediaFileRight = "";

                if (surveyClass.Data.Media.LeftMediaFileNames.Count > 0)
                    mediaFileLeft = Path.Combine(surveyClass.Data.Media.MediaPath, surveyClass.Data.Media.LeftMediaFileNames[0]);
                if (surveyClass.Data.Media.RightMediaFileNames.Count > 0)
                    mediaFileRight = Path.Combine(surveyClass.Data.Media.MediaPath, surveyClass.Data.Media.RightMediaFileNames[0]);
                
                // If fileSpec a relative path then use the path from the survey file spec
                if (!Path.IsPathRooted(mediaFileLeft) && surveyClass.Data.Info.SurveyPath is not null)
                {
                    // Combine the base directory with the relative fileSpec
                    mediaFileLeft = Path.GetFullPath(Path.Combine(surveyClass.Data.Info.SurveyPath, mediaFileLeft));
                }
                if (!Path.IsPathRooted(mediaFileRight) && surveyClass.Data.Info.SurveyPath is not null)
                {
                    // Combine the base directory with the relative fileSpec
                    mediaFileRight = Path.GetFullPath(Path.Combine(surveyClass.Data.Info.SurveyPath, mediaFileRight));
                }


                // Open left camera media
                if (string.IsNullOrEmpty(mediaFileLeft) == false && string.IsNullOrEmpty(mediaFileRight) == false)
                {
                    // Open the new media
                    retOpen = await mediaStereoController.MediaOpen(mediaFileLeft,
                                                                    mediaFileRight,
                                                                    surveyClass.Data.Events.EventList,
                                                                    surveyClass.Data.Sync.IsSynchronized == true ? surveyClass.Data.Sync.TimeSpanOffset : null);

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
        private async Task CloseSVSMediaFiles()
        {

            if (mediaStereoController.MediaIsOpen())
            {
                await mediaStereoController.MediaClose();

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
        private void SetMenuStatusBasedOnProjectState()
        {
            if (surveyClass is not null /*&& this.projectClass.IsLoaded == true*/)
            {
                // Survey
                MenuSurveySave.IsEnabled = true;
                MenuSurveySaveAs.IsEnabled = true;
                MenuSurveyClose.IsEnabled = true;
                // Import calibration
                MenuImportCalibration.IsEnabled = true;
                // Media Lock
                MenuLockUnlockMediaPlayers.IsEnabled = true;
                // Settings
                MenuSettings.IsEnabled = true;
                // Survey Transect Marker
                MenuTransectStartStopMarker.IsEnabled = true;
            }
            else
            {
                // Survey
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
                // needed to keep the tooltip working as the glyph changes)
                LockUnLockIndicator.Text = "    ";
                ToolTipService.SetToolTip(LockUnLockIndicator, "");
            }
            if (locked == true)
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

            if (calibrationClass is not null && (frameWidth is not null || frameHeight is not null))
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


                    // If there is other calibration data available then add it to the tooltip
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
                        CalibratedIndicator.Text = calibratedIndictorText + "!";
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
                // needed to keep the tooltip working as the glyph changes)
                CalibratedIndicator.Text = "    ";
                ToolTipService.SetToolTip(CalibratedIndicator, "");
            }
        }

        /// <summary>
        /// Make a list of calibration descriptions for the tooltip. This includes the description (if present) and the frame size (if present)
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
        /// The Mediaplayer must be open so the frame width and height is known
        /// </summary>
        /// <returns>true if anything changed</returns>
        public async Task<bool> CheckIfEventMeasurementsAreUpToDate(bool forceReCalc)
        {
            bool ret = false;

            // Get the Calibration ID from the preferred calibration data
            if (surveyClass is not null && MediaPlayerLeft.IsOpen())
            {
                CalibrationData? calibrationData = surveyClass!.Data.Calibration.GetPreferredCalibationData(MediaPlayerLeft.FrameWidth, MediaPlayerLeft.FrameHeight);

                if (calibrationData is not null)
                {
                    Guid? calibrationID = calibrationData.CalibrationID;

                    if (calibrationID is not null)
                    {
                        // Check if the preferred calibration data is the one being using for
                        // the current event measurements calculations
                        bool upToDate = true;
                        foreach (Event evt in surveyClass.Data.Events.EventList)
                        {
                            if (evt.EventDataType == SurveyDataType.SurveyMeasurementPoints && evt.EventData is not null)
                            {
                                SurveyMeasurement surveyMeasurement = (SurveyMeasurement)evt.EventData;
                                if (surveyMeasurement.CalibrationID != calibrationID || surveyMeasurement.Measurment == -1)
                                {
                                    upToDate = false;
                                    break;
                                }
                            }
                            else if (evt.EventDataType == SurveyDataType.SurveyStereoPoint && evt.EventData is not null)
                            {
                                SurveyStereoPoint surveyStereoPoint = (SurveyStereoPoint)evt.EventData;
                                if (surveyStereoPoint.CalibrationID != calibrationID)
                                {
                                    upToDate = false;
                                    break;
                                }
                            }
                        }


                        if (!upToDate && !forceReCalc)
                        {
                            // Ask the user if they want to update the event measurements
                            string message = $"The current event measurements are not up to date with the preferred calibration data. Do you want to update the event measurements?";
                            string primaryButtonText = "Yes";
                            string secondaryButtonText = "No";

                            // Ask the user
                            ContentDialog confirmationDialog = new()
                            {
                                Title = "Update Measurements",
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
                                upToDate = false;
                            } 
                            else if (result == ContentDialogResult.Secondary)
                            {
                                upToDate = true;
                            }
                        }

                        if (!upToDate || forceReCalc)
                        { 
                            // Update the event measurements if the Calibration ID is different
                            // there has been a recalibration
                            foreach (Event evt in surveyClass.Data.Events.EventList)
                            {
                                if (evt.EventData is not null)
                                {
                                    if (evt.EventDataType == SurveyDataType.SurveyMeasurementPoints)
                                    {
                                        SurveyMeasurement surveyMeasurement = (SurveyMeasurement)evt.EventData;
                                        if (surveyMeasurement.CalibrationID != calibrationID || surveyMeasurement.Measurment == -1 || forceReCalc)
                                        {
                                            // Recalculate for a measurement
                                            if (DoMeasurementAndRulesCalculations(surveyMeasurement))
                                            {
                                                // Updates were make to the event
                                                ret = true;
                                                surveyClass.Data.Events.IsDirty = true;
                                            }
                                        }
                                    }
                                    else if (evt.EventDataType == SurveyDataType.SurveyStereoPoint)
                                    {
                                        SurveyStereoPoint surveyStereoPoint = (SurveyStereoPoint)evt.EventData;
                                        if (surveyStereoPoint.CalibrationID != calibrationID || forceReCalc)
                                        {
                                            // Recalculate for a stero point
                                            if (DoRulesCalculations(surveyStereoPoint))
                                            {
                                                // Updates were make to the event
                                                ret = true;
                                                surveyClass.Data.Events.IsDirty = true;
                                            }
                                        }
                                    }
                                }
                            }
                            
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
                    }
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

            if (stereoProjection.PointsLoad(
                new Point(surveyMeasurement.LeftXA, surveyMeasurement.LeftYA),
                new Point(surveyMeasurement.LeftXB, surveyMeasurement.LeftYB),
                new Point(surveyMeasurement.RightXA, surveyMeasurement.RightYA),
                new Point(surveyMeasurement.RightXB, surveyMeasurement.RightYB)) == true)
            {

                // Calculate fish length
                double? measurement = stereoProjection.Measurement();
                surveyMeasurement.Measurment = measurement;

                SurveyRulesCalc newRules = new();

                // Apply the survey rules
                if (surveyClass is not null &&                     
                    surveyClass.Data.SurveyRules.SurveyRulesActive == true)
                {
                    newRules.ApplyRules(surveyClass.Data.SurveyRules.SurveyRulesData, stereoProjection);
                }
                else
                {
                    // No rules applied
                    newRules.Clear();
                }

                if (!newRules.Equals(surveyMeasurement.SurveyRulesCalc))
                {
                    surveyMeasurement.SurveyRulesCalc = newRules;
                    updated = true;
                }

                // Record the calidation data Guid used to calculate the measurement
                // This is used to enable recalulation of the measurement if the calibration data is changed
                Guid? newCalibrationID = stereoProjection.GetCalibrationID();
                Guid? currentCalibrationID = surveyMeasurement.CalibrationID;
                if (!Nullable.Equals(currentCalibrationID, newCalibrationID))
                {
                    surveyMeasurement.CalibrationID = newCalibrationID;
                    updated = true;
                }
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

            if (stereoProjection.PointsLoad(
                new Point(surveyStereoPoint.LeftX, surveyStereoPoint.LeftY),
                new Point(surveyStereoPoint.RightX, surveyStereoPoint.RightY)) == true)
            {
                SurveyRulesCalc newRules = new();

                // Apply the survey rules
                if (surveyClass is not null &&
                    surveyClass.Data.SurveyRules.SurveyRulesActive == true)
                {

                    newRules.ApplyRules(surveyClass.Data.SurveyRules.SurveyRulesData, stereoProjection);
                }
                else
                {
                    // No rules to apply
                    newRules.Clear();
                }

                if (!newRules.Equals(surveyStereoPoint.SurveyRulesCalc))
                {
                    surveyStereoPoint.SurveyRulesCalc = newRules;
                    updated = true;
                }

                // Record the calidation data Guid used to calculate the measurement
                // This is used to enable recalulation of the measurement if the calibration data is changed
                Guid? newCalibrationID = stereoProjection.GetCalibrationID();
                Guid? currentCalibrationID = surveyStereoPoint.CalibrationID;
                if (!Nullable.Equals(currentCalibrationID, newCalibrationID))
                {
                    surveyStereoPoint.CalibrationID = newCalibrationID;
                    updated = true;
                }
            }

            return updated;
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
            GeneralTransform transformCalibratedIndicator = CalibratedIndicator.TransformToVisual(null);
            Rect boundsCalibratedIndicator = transformCalibratedIndicator.TransformBounds(new Rect(0, 0,
                                                                                 CalibratedIndicator.ActualWidth,
                                                                                 CalibratedIndicator.ActualHeight));
            Windows.Graphics.RectInt32 CalibratedIndicatorRect = GetRect(boundsCalibratedIndicator, scaleAdjustment);


            // Create list of regions that should not be draggable
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
        /// seconardary media control and centre the primary
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
        /// Diagnostic information state has changed (or is being initially set)
        /// Setup can MainWindow thing are controlled by the diagnostic information
        /// </summary>
        /// <param name="diagnosticInformation"></param>
        internal void _SetDiagnosticInformation(bool diagnosticInformation)
        {
            if (diagnosticInformation)
            {
                // Debug Diags Dump Help>Diags Dump 
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
        internal void _SetExperimental(bool _experimentalEnabled)
        {
            experimentalEnabled = _experimentalEnabled;
        }


        /// <summary>
        /// Used to display any exceptions during the Open() function
        /// </summary>
        /// <param name="mediaFileSpec"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private async Task ShowCannotSynchronizedDialog()
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
        /// The users has selected a different tab in the tabview at bottom of the screen
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        private async void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {              
                await ShowSettingsWindow();
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
        private async Task ShowSettingsWindow(string section = "")
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

                    // Optionally add a click event handler for the menu item
                    menuItem.Click += FileRecentSurvey_Click;

                    MenuRecentSurveys.Items.Add(menuItem);
                }
            }
        }


        /// <summary>
        /// Diags dump of class information
        /// </summary>
        private void DumpAllProperties()
        {
            DumpClassPropertiesHelper.DumpAllProperties(this, report, /*ignore*/"mediator,report,mediaControllerHandler,mediaStereoController,MediaPlayerLeft,MediaPlayerRight,surveyClass,stereoProjection,eventsControl");
        }


        /// <summary>
        /// internet Downloading/uploading spinner
        /// </summary>
        private readonly string[] _brailleFrames =
        [
            "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"
        ];
        private DispatcherTimer _spinnerTimer = new();
        private int _spinnerframeIndex = 0;

        private void StartDownloadUploadSpinner()
        {
            _spinnerTimer.Interval = TimeSpan.FromMilliseconds(100);

            _spinnerTimer.Tick += (sender, args) =>
            {
                DownloadIndicator.Glyph = _brailleFrames[_spinnerframeIndex];
                _spinnerframeIndex = (_spinnerframeIndex + 1) % _brailleFrames.Length;
            };

            _spinnerTimer.Start();
        }

        private void StopDownloadUploadSpinner()
        {
            _spinnerTimer?.Stop();
            DownloadIndicator.Glyph = ""; // or a finished icon
        }





        // ** End of MainWindow **
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
                        if (data.experimentialEnabled is not null)
                        {
                            _mainWindow._SetExperimental((bool)data!.experimentialEnabled);
                        }
                        break;

                }
            }

        }
    }

}
