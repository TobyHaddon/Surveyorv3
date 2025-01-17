using CommunityToolkit.WinUI;
using Microsoft.UI;
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
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;
using static Surveyor.MediaStereoControllerEventData;
using static Surveyor.Survey.DataClass;
#if !No_MagnifyAndMarkerDisplay
#endif

namespace Surveyor
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private AppWindow appWindow;

        // Create the Mediator
        private readonly SurveyorMediator mediator = new();

        // Declare the mediator handler for MainWindow
        private readonly MainWindowHandler mainWindowHandler;

        // Declare the MediaStereoController
        private readonly MediaStereoController mediaStereoController;

        // Title bar title elements
        private string titlebarTitle = "";
        private string titlebarCameraSide = "";
        private string titlebarSaveStatus = "";

        // Current Survey Class
        private Survey? surveyClass = null;

        // Measurement class
        private readonly StereoProjection stereoProjection = new();

        // Hidden controls to be shown dynamically
        private readonly EventsControl eventsControl = new();
        private readonly Reporter report = new();


        private const string RECENT_SURVEYS_KEY = "RecentSurveys";
        private int maxRecentSurveysDisplayed = 4;      // Will be controlled from Settings TO DO
        private const int MAX_RECENT_SURVEYS_SAVED = 10;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Closed += MainWindow_Closed;

            // Inform the Reporter of the DispatcherQueue
            report.SetDispatcherQueue(DispatcherQueue);

            // Inform the Events Control of the DispatcherQueue
            eventsControl.SetDispatcherQueue(DispatcherQueue);

            // This is used to get/adjust the theme is necessary
            ThemeHelper.Initialize();

            // Set theme
            SetTheme(SettingsManager.ApplicationTheme);

            // Add listener for theme changes
            var rootElement = (FrameworkElement)Content;
            rootElement.ActualThemeChanged += OnActualThemeChanged;

            // Restore the saved window state
            //???Disabled as it need more work.  I think the multiple monitor setting and switching between monitors is causing the issue
            //???The application can start off screen or too big for the monitor
            //???WindowStateHelper.RestoreWindowState(hWnd, this.AppWindow);

            // Setup the Handler for the MainWindow
            mainWindowHandler = new MainWindowHandler(mediator, this);

            // Create the MediaStereoController and pass it the Mediator
            mediaStereoController = new MediaStereoController(this, report,
                                                            mediator,
                                                            MediaPlayerLeft, MediaPlayerRight,
                                                            MediaControlPrimary, MediaControlSecondary,
#if !No_MagnifyAndMarkerDisplay
                                                            MagnifyAndMarkerDisplayLeft, MagnifyAndMarkerDisplayRight,
#endif
                                                            stereoProjection/*, 
                                                            MediaInfoLeft, MediaInfoRight */);

            // Inform the Events Control of the MediaStereoController
            eventsControl.SetMediaStereoController(mediaStereoController);

            // Setup and magnify and marker controls by linking that to the each media players ImageFrame
#if !No_MagnifyAndMarkerDisplay
            MagnifyAndMarkerDisplayLeft.Setup(MediaPlayerLeft.GetImageFrame(), MagnifyAndMarkerDisplay.CameraSide.Left);
            MagnifyAndMarkerDisplayRight.Setup(MediaPlayerRight.GetImageFrame(), MagnifyAndMarkerDisplay.CameraSide.Right);
#endif

            // Allows the menu bar to extend into the title bar
            // Assumes "this" is a XAML Window. In projects that don't use 
            // WinUI 3 1.3 or later, use interop APIs to get the AppWindow.
            appWindow = this.AppWindow;
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

            report.Info("", $"App Loaded Ok");

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
        public async void SaveCurrentFrame(SurveyorMediaControl.eControlType controlType)
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
                    MediaPlayerLeft.SaveCurrentFrame(framesPath);
                    MediaPlayerRight.SaveCurrentFrame(framesPath);
                }
                else if (controlType == SurveyorMediaControl.eControlType.Primary)
                    MediaPlayerLeft.SaveCurrentFrame(framesPath);
                else if (controlType == SurveyorMediaControl.eControlType.Secondary)
                    MediaPlayerRight.SaveCurrentFrame(framesPath);
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
            SettingsManager.ApplicationTheme = ElementTheme.Default;
        }


        /// <summary>
        /// Window is closing. Save the current window state
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Closed(object sender, WindowEventArgs e)
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // Save the current window state
            WindowStateHelper.SaveWindowState(hWnd, appWindow);
        }

        public void AppClosed()
        {
            // Implement your cleanup or closing logic here
            mediaStereoController.MediaClose();
        }


        /// <summary>
        /// Used to set the unsaved data indicated in the title bar
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Project_PropertyChanged(object? sender, PropertyChangedEventArgs e)
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
                // Create a new empty survey
                surveyClass = new Survey(report);
                surveyClass.PropertyChanged += Project_PropertyChanged;

                // Inform the MediaStereoController of the Events list so edits to the
                // list can be actioned by MediaStereoController
                mediaStereoController.SetEvents((ObservableCollection<Event>)surveyClass.Data.Events.EventList);

#if !No_MagnifyAndMarkerDisplay
                MagnifyAndMarkerDisplayLeft.SetEvents(surveyClass.Data.Events.EventList);
                MagnifyAndMarkerDisplayRight.SetEvents(surveyClass.Data.Events.EventList);
#endif

                eventsControl.SetEvents(surveyClass.Data.Events.EventList);

                SetMenuStatusBasedOnProjectState();

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

            SetMenuStatusBasedOnProjectState();
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
                string surveyFolder = SettingsManager.SurveyFolder is null ? "" : SettingsManager.SurveyFolder;
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
                    await CheckIfEventMeasurementsAreUpToDate();
                }

                SetMenuStatusBasedOnProjectState();
            }
        }


        /// <summary>
        /// Save the currently open survey file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileSurveySave_Click(object sender, RoutedEventArgs e)
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
        private async void FileSurveySaveAs_Click(object sender, RoutedEventArgs e)
        {
            await SaveAsSurvey();
            SetMenuStatusBasedOnProjectState();
        }


        /// <summary>
        /// Close the currently open survey file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileSurveyClose_Click(object sender, RoutedEventArgs e)
        {
            await CheckForOpenSurveyAndClose();

//???            mediaStereoController.MediaClose();

//#if !No_MagnifyAndMarkerDisplay
//            MagnifyAndMarkerDisplayLeft.Close();
//            MagnifyAndMarkerDisplayRight.Close();
//#endif

//            projectClass?.ProjectClose();

//            SetTitle("");
//            SetLockUnlockIndicator(null, null);
//            MenuSurveyOpen.IsEnabled = true;
//            MenuSurveyClose.IsEnabled = false;
//            MenuFileLockUnlockMediaPlayers.IsEnabled = false;
//            MenuSurveySave.IsEnabled = false;

//            // Clear the measurement class by loaded an empty calibration class
//            stereoProjection.SetCalibrationData(new Project.DataClass.CalibrationClass());
//            SetCalibratedIndicator(null, null);

//            // Display both media controls
//            MediaControlsDisplayMode(false);

//            // Clear title
//            titlebarTitle = "";
//            titlebarCameraSide = "";
//            titlebarSaveStatus = "";
        }


        /// <summary>
        /// Used to open a selected recent survey file from the 'Recent Surveys' sub menu
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RecentSurvey_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuFlyoutItem;
            if (menuItem is not null)
            {
                if (menuItem.Tag is string filePath)
                {
                    // Open survey in the regular way
                    await OpenSurvey(filePath);

                    // Check if the preferred calibration data is the one being using for
                    // the current event measurements calculations
                    await CheckIfEventMeasurementsAreUpToDate();
                }

                SetMenuStatusBasedOnProjectState();

            }
        }


        /// <summary>
        /// Select the media (MP4) files to use for this survey. Both the left and right MP4 files must 
        /// be selected at the same time
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileSVSMediaOpen_Click(object sender, RoutedEventArgs e)
        {
            bool ret = true;

            // Create the file picker object
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
            IReadOnlyList<StorageFile> files = await openPicker.PickMultipleFilesAsync();

            // Check if files were picked and handle them
            if (files.Count > 0 && this.surveyClass is not null)
            {

                if (files.Count == 1)
                {
                    // Try to auto detect which is the left and right media file
                    var (leftFile, rightFile) = DetectLeftAndRightMediaFile(files[0].Path, null);

                    string message = "";

                    if (leftFile is not null)
                        message = $"Please confirm that this is the left file:\r\n    '{Path.GetFileName(leftFile)}'\r\n\r\nPress 'Yes' to confirm or 'Swap' to indicate it is the right media file.";
                    else if (rightFile is not null)
                        message = $"Please confirm that this is the right file:\r\n    '{Path.GetFileName(rightFile)}'\r\n\r\nPress 'Yes' to confirm or 'Swap' to indicate it is the left media file.";

                    ContentDialog confirmationDialog = new()
                    {
                        Title = "Confirm if left or right media file",
                        Content = message,
                        PrimaryButtonText = "Yes",
                        SecondaryButtonText = "Swap",
                        CloseButtonText = "Cancel",

                        // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                        XamlRoot = this.Content.XamlRoot
                    };

                    // Display the dialog
                    ContentDialogResult result = await confirmationDialog.ShowAsync();

                    switch (result)
                    {
                        case ContentDialogResult.Primary: // Yes
                            // No action as the file names are the correct way round
                            break;
                        case ContentDialogResult.Secondary: // Swap
                            if (leftFile is not null)
                                rightFile = leftFile;
                            else if (rightFile is not null)
                                leftFile = rightFile;
                            break;
                        case ContentDialogResult.None: // Cancel
                            ret = false;
                            break;
                    }
                    if (ret)
                    {
                        if (leftFile is not null)
                        {
                            surveyClass.AddMediaFile(leftFile, false/*FalseLeftTrueRight*/);
                        }
                        else if (rightFile is not null)
                        {
                            surveyClass.AddMediaFile(rightFile, true/*FalseLeftTrueRight*/);
                        }
                    }
                }
                else if (files.Count == 2)
                {
                    // Try to auto detect which is the left and right media file
                    var (leftFile, rightFile) = DetectLeftAndRightMediaFile(files[0].Path, files[1].Path);

                    string message = $"Please confirm that:\r\n    '{Path.GetFileName(leftFile)}'\r\n\r\nis the left media file and:\r\n    '{Path.GetFileName(rightFile)}'\r\n\r\nis the right media file. Press 'Yes' to confirm or 'Swap' to switch them off.";

                    ContentDialog confirmationDialog = new()
                    {
                        Title = "Confirm which is the left and right media files",
                        Content = message,
                        PrimaryButtonText = "Yes",
                        SecondaryButtonText = "Swap",
                        CloseButtonText = "Cancel",

                        // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                        XamlRoot = this.Content.XamlRoot
                    };

                    // Display the dialog
                    ContentDialogResult result = await confirmationDialog.ShowAsync();

                    switch (result)
                    {
                        case ContentDialogResult.Primary: // Yes
                            // No action as the file names are the correct way round
                            break;
                        case ContentDialogResult.Secondary: // Swap
                            (rightFile, leftFile) = (leftFile, rightFile);
                            break;
                        case ContentDialogResult.None: // Cancel
                            ret = false;
                            break;
                    }

                    if (ret)
                    {
                        // Clear the current media file names
                        surveyClass.Data.Media.LeftMediaFileNames.Clear();
                        surveyClass.Data.Media.RightMediaFileNames.Clear();

                        if (leftFile is not null)
                            surveyClass.AddMediaFile(leftFile, false/*FalseLeftTrueRight*/);

                        if (rightFile is not null)
                            surveyClass.AddMediaFile(rightFile, true/*FalseLeftTrueRight*/);
                    }
                }

                if ((ret == true))
                    // Open Media Files
                    await OpenSVSMediaFiles();

            }

            SetMenuStatusBasedOnProjectState();
        }


        /// <summary>
        /// Try to figure out which is the left and which is the right media file
        /// </summary>
        /// <param name="file1"></param>
        /// <param name="file2"></param>
        /// <returns></returns>
        private (string? LeftFile, string? RightFile) DetectLeftAndRightMediaFile(string? file1, string? file2)
        {
            // Regex to identify left and right
            Regex leftRegex = new Regex("(?i)(left|l[^a-z])");
            Regex rightRegex = new Regex("(?i)(right|r[^a-z])");

            bool isFile1Left = false;
            bool isFile1Right = false;
            bool isFile2Left = false;
            bool isFile2Right = false;

            if (file1 is not null)
            {
                isFile1Left = leftRegex.IsMatch(file1);
                isFile1Right = rightRegex.IsMatch(file1);
            }
            if (file2 is not null)
            {
                isFile2Left = leftRegex.IsMatch(file2);
                isFile2Right = rightRegex.IsMatch(file2);
            }

            // Determine which file is left and which is right
            if (isFile1Left && !isFile1Right)
                return (file1, file2);
            else if (isFile2Left && !isFile2Right)
                return (file2, file1);
            else if (isFile1Right && !isFile1Left)
                return (file2, file1);
            else if (isFile2Right && !isFile2Left)
                return (file1, file2);

            // Default case if unable to distinguish
            return (LeftFile: file1, RightFile: file2);
        }


        /// <summary>
        /// CLose the media files
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //??? TO BE DELETED
        //private void FileMediaSVSClose_Click(object sender, RoutedEventArgs e)
        //{
        //    // Close Media Files
        //    CloseSVSMediaFiles();
        //    SetMenuStatusBasedOnProjectState();
        //}


        /// <summary>
        /// Users wants to lock or unlock the media players. 
        /// i.e. synchronize or unsynchronize the media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FileLockUnlockMediaPlayers_Click(object sender, RoutedEventArgs e)
        {
            if (!mediaStereoController.IsPlaying())
            {
                if (MenuFileLockUnlockMediaPlayers.IsChecked)
                {
                    // Lock the left and right media controlers
                    if (surveyClass is not null && MediaPlayerLeft is not null && MediaPlayerRight is not null &&
                        MediaPlayerLeft.Position is not null && MediaPlayerRight.Position is not null)
                    {
                        surveyClass.Data.Sync.IsSynchronized = true;
                        surveyClass.Data.Sync.TimeSpanOffset = (TimeSpan)MediaPlayerRight.Position - (TimeSpan)MediaPlayerLeft.Position;
                        surveyClass.Data.Sync.ActualTimeSpanOffsetLeft = (TimeSpan)MediaPlayerLeft.Position;
                        surveyClass.Data.Sync.ActualTimeSpanOffsetRight = (TimeSpan)MediaPlayerRight.Position;
                    }

                    mediaStereoController.MediaLockMediaPlayers();
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

                // Undo the check
                MenuFileLockUnlockMediaPlayers.IsChecked = !MenuFileLockUnlockMediaPlayers.IsChecked;
            }

        }

        /// <summary>
        /// Exit the app
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileExit_Click(object sender, RoutedEventArgs e)
        {
            mediaStereoController.MediaClose();

            SetTitle("");
            SetLockUnlockIndicator(null, null);

            Application.Current.Exit();
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
                await CheckIfEventMeasurementsAreUpToDate();


                //// Inform the two media players of the MeasurementPointControl instances that allow the user to add measurement points to the media
                //LMediaPlayer.SetMeasurementPointControl(measurementPointControl);
                //RMediaPlayer.SetMeasurementPointControl(measurementPointControl);
            }
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
                        await CheckIfEventMeasurementsAreUpToDate();

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



        ///
        /// PRIVATE METHODS
        /// 


        /// <summary>
        /// Open the survey files
        /// </summary>
        /// <param name="surveyFileName"></param>
        /// <returns></returns>
        private async Task<int> OpenSurvey(string projectFileName)
        {
            int ret = 0;

            if (surveyClass is null)
            {
                surveyClass ??= new Survey(report);
                surveyClass.PropertyChanged += Project_PropertyChanged;
            }
            else
            {
#if !No_MagnifyAndMarkerDisplay
                MagnifyAndMarkerDisplayLeft.Close();
                MagnifyAndMarkerDisplayRight.Close();
#endif
                await surveyClass.SurveyClose();
            }

            ret = await surveyClass.SurveyLoad(projectFileName);

            if (ret == 0 &&
                surveyClass.Data is not null && surveyClass.Data.Media is not null && surveyClass.Data.Media.MediaPath is not null)
            {
                // Inform the MediaStereoController & MagnifyAndMarkerDisplay of the Events list so edits to the
                // list can be actioned by MediaStereoController
                mediaStereoController.SetEvents((ObservableCollection<Event>)surveyClass.Data.Events.EventList);

#if !No_MagnifyAndMarkerDisplay
                MagnifyAndMarkerDisplayLeft.SetEvents(surveyClass.Data.Events.EventList);
                MagnifyAndMarkerDisplayRight.SetEvents(surveyClass.Data.Events.EventList);
#endif
                eventsControl.SetEvents(surveyClass.Data.Events.EventList);


                // Check if the left media file(s) exist
                ret = await CheckIfMediaFileExists("L", surveyClass.Data.Media.MediaPath, surveyClass.Data.Media.LeftMediaFileNames);
                if (ret == 0)
                    ret = await CheckIfMediaFileExists("R", surveyClass.Data.Media.MediaPath, surveyClass.Data.Media.RightMediaFileNames);

                if (ret == 0)
                {
                    // Create a MeasurementPointControl instance that allows the user to add measurement points to the media
                    // Do this before opening the media so the calibration data is available when the media is opened and the frame size established
                    stereoProjection.SetCalibrationData(surveyClass.Data.Calibration);

                    // Open Media Files and bind the MediaPlayers if IsSynchronized is true
                    if (await OpenSVSMediaFiles() == true)
                    {
                        // Set the lock/unlock media files 
                        if (surveyClass.Data.Sync.IsSynchronized == true)
                            MenuFileLockUnlockMediaPlayers.IsChecked = true;


                        // Remember the survey folder
                        System.IO.FileInfo fileinfo = new(projectFileName);
                        if (fileinfo != null && fileinfo.Directory != null && SettingsManager.SurveyFolder != fileinfo.DirectoryName)
                        {
                            SettingsManager.SurveyFolder = fileinfo.DirectoryName;
                            //???Properties.Settings.Default.Save();  //???Old methed of save last used folder - do we need to do something equivalent to this?
                        }


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



                        /////////TEST CODE//////////
                        //if (projectClass is not null && projectClass.IsLoaded && projectClass.Data.Calibration.CalibrationDataList.Count > 0)
                        //{
                        //    Point pointsLRaw_A = new Point(2392.0, 2113.0);   // [2392,2113] 
                        //    Point pointsLRaw_B = new Point(3052.0, 2100.0);   // [3052,2100]
                        //    Point pointsRRaw_A = new Point(1962.0, 2171.0);   // [1962,2171]
                        //    Point pointsRRaw_B = new Point(2581.0, 2206.0);   // [2581,2206]
                        //    double distance = stereoProjection.Distance(pointsLRaw_A, pointsLRaw_B, pointsRRaw_A, pointsRRaw_B, true);    
                        //    Report.Info("", $"Distance: {distance}");
                        //}
                        ////////////////////////////
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
                report.Warning("", $"Failed to open survey file:{projectFileName}, error = {ret}");
                surveyClass = null;
            }

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
                //???string? surveyFolder = SettingsManager.ProjectFolder;
                //if (string.IsNullOrEmpty(surveyFolder) == true)
                //    surveyFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                FileSavePicker savePicker = new FileSavePicker();
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this); // 'this' should be your window or page
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd); // Link the picker with the window handle

                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("Survey", new List<string>() { ".survey" });
                savePicker.SuggestedFileName = "New Document";

                StorageFile file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    SetTitle(file.Name);
                    SetTitleSaveStatus("Saving...");

                    // Prevent updates to the remote version of the file until we finish making changes and call CompleteUpdatesAsync.
                    CachedFileManager.DeferUpdates(file);

                    // Write data to the file
                    // Save As
                    surveyClass.SurveySaveAs(file.Path);


                    // Let Windows know that we're finished changing the file so the other app can update the remote version of the file.
                    FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
                    if (status == FileUpdateStatus.Complete)
                    {
                        Debug.WriteLine($"File {file.Path} saved successfully.");

                        //??// Remember the survey folder
                        //System.IO.FileInfo fileinfo = new(saveFileDialog.FileNames[0]);
                        //if (fileinfo != null && fileinfo.Directory != null)
                        //{
                        //    SettingsManager.ProjectFolder = fileinfo.DirectoryName;
                        //    //???Properties.Settings.Default.Save();
                        //}
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
        /// </summary>
        /// <returns>true is ok to proceed (i.e. no survey now open)</returns>
        private async Task<bool> CheckForOpenSurveyAndClose()
        {
            bool ret = false;

            if (this.surveyClass is not null)
            {
                bool closeProject = false;

                if (this.surveyClass.IsDirty == true)
                {
                    // Create a FontIcon using the Segoe MDL2 Assets font
                    var warningIcon = new FontIcon
                    {
                        Glyph = "\uE814", // Unicode character for a warning icon in Segoe MDL2 Assets
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
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
                                new TextBlock { Text = "Please proceed with caution!" }
                            }
                        },
                        CloseButtonText = "OK",
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
                        if (await SaveAsSurvey() == 0)
                            closeProject = true;

                    }
                    else if (result == ContentDialogResult.Secondary)
                    {
                        // "No" button clicked
                        closeProject = true;
                    }
                }
                else
                    closeProject = true;


                if (closeProject == true)
                {
#if !No_MagnifyAndMarkerDisplay
                    MagnifyAndMarkerDisplayLeft.Close();
                    MagnifyAndMarkerDisplayRight.Close();
#endif

                    // Closes the StereoMediaController, clears the title and the sync indicator
                    CloseSVSMediaFiles();

                    // Close and clear the Project class (holds the survey data)
                    await surveyClass.SurveyClose();
                    surveyClass = null;

                                        
                    // Clear the measurement class by loaded an empty calibration class
                    stereoProjection.SetCalibrationData(new Survey.DataClass.CalibrationClass());
                    SetCalibratedIndicator(null, null);

                    // Display both media controls
                    MediaControlsDisplayMode(false);


                    ret = true;
                }
            }
            else
                ret = true;

            SetMenuStatusBasedOnProjectState();

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
        private async Task<int> CheckIfMediaFileExists(string channel/*L or R*/, string mediaPath, ObservableCollection<string> mediaFileNames)
        {
            int ret = 0;

            for (int index = 0; index < mediaFileNames.Count; index++)
            {
                string fileName = mediaFileNames[index];
                string fileSpec = Path.Combine(mediaPath, fileName);

                if (File.Exists(fileSpec) == false)
                {
                    // Media file is missing. Report to the user and ask if they would like to try to find the file
                    string cameraSide = channel == "L" ? "Left" : "Right";
                    string fileNumber = mediaFileNames.Count > 1 ? $"number {index + 1} " : "";
                    string message = $"The {cameraSide.ToLower()} media file {fileNumber}'{fileSpec}' does not exist. Press 'Ok' to try to find the file. Press 'Cancel' to stop loading the survey";

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
                            Children =
                            {
                                warningIcon, // Add the exclamation icon to the dialog content
                                new TextBlock { Text = message }
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
                        //???// "OK" button clicked
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
                            string fileNameOnly = Path.GetFileName(file.Name);
                            mediaFileNames[index] = fileNameOnly;
                        }
                        else
                        {
                            ret = -1;
                        }
                    }
                    else if (result == ContentDialogResult.Secondary)
                    {
                        // "Cancel" button clicked
                        ret = -1;
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
                    mediaStereoController.MediaClose();
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


                // Open left camera media
                if (string.IsNullOrEmpty(mediaFileLeft) == false && string.IsNullOrEmpty(mediaFileRight) == false)
                {
                    // Open the new media
                    retOpen = await mediaStereoController.MediaOpen(mediaFileLeft,
                        mediaFileRight,
                        surveyClass.Data.Sync.IsSynchronized == true ? surveyClass.Data.Sync.TimeSpanOffset : null);

                    if (retOpen == 0)
                    {
                        if (surveyClass.Data.Info.SurveyFileName is not null)
                            SetTitle(surveyClass.Data.Info.SurveyFileName);

                        //??? MenuMediaOpen.IsEnabled = false;
                        //??? MenuMediaClose.IsEnabled = true;
                        MenuFileLockUnlockMediaPlayers.IsEnabled = true;
                        //???                        MenuSurFileSave.IsEnabled = true;
                    }
                    else
                    {
                        SetTitle("");
                        SetLockUnlockIndicator(null, null);
                        //??? MenuMediaOpen.IsEnabled = true;
                        //??? MenuMediaClose.IsEnabled = false;
                        MenuFileLockUnlockMediaPlayers.IsEnabled = false;
                        //???                        MenuFileSave.IsEnabled = false;

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
        private void CloseSVSMediaFiles()
        {

            if (mediaStereoController.MediaIsOpen())
            {
                mediaStereoController.MediaClose();

                SetTitle("");
                SetTitleSaveStatus("");
                SetTitleCameraSide("");

                SetLockUnlockIndicator(null, null);
                //??? MenuMediaOpen.IsEnabled = true;
                //??? MenuMediaClose.IsEnabled = false;
                MenuFileLockUnlockMediaPlayers.IsEnabled = false;

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

                // Media
                //??? MenuMediaOpen.IsEnabled = true;
                //??? if (mediaStereoController.MediaIsOpen() == true)
                //??? MenuMediaClose.IsEnabled = true;
                //??? else
                //??? MenuMediaClose.IsEnabled = false;

                // Import calibration
                MenuImportCalibration.IsEnabled = true;
                // Media Lock
                MenuFileLockUnlockMediaPlayers.IsEnabled = true;
                //MenuItemSetFrameSettings.IsEnabled = true;
            }
            else
            {
                // Survey
                MenuSurveySave.IsEnabled = false;
                MenuSurveySaveAs.IsEnabled = false;
                MenuSurveyClose.IsEnabled = false;
                // Media
                //??? MenuMediaOpen.IsEnabled = false;
                //??? MenuMediaClose.IsEnabled = false;
                // Import calibration
                MenuImportCalibration.IsEnabled = false;
                // Media lock
                MenuFileLockUnlockMediaPlayers.IsEnabled = false;
                MenuFileLockUnlockMediaPlayers.IsChecked = false;
                //this.MenuItemSetFrameSettings.IsEnabled = false;

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
        private string? calibratedIndictor = null;

        public void SetCalibratedIndicator(int? frameWidth, int? frameHeight)
        {
            string tooltip;

            // Remember the 'Calibrated' symbol so we can reuse it later
            calibratedIndictor ??= CalibratedIndicator.Text;

            CalibrationClass? calibrationClass = surveyClass?.Data?.Calibration;
            CalibrationData? calibrationDataPreferred = calibrationClass?.GetPreferredCalibationData(frameWidth, frameHeight);

            if (calibrationClass is not null && (frameWidth is not null || frameHeight is not null))
            {
                if (calibrationDataPreferred is not null)
                {
                    // Inform about the preferred calibration data
                    if (!string.IsNullOrEmpty(calibrationDataPreferred.Description) &&
                        calibrationDataPreferred.LeftCalibrationCameraData is not null &&
                        calibrationDataPreferred.LeftCalibrationCameraData.ImageSize is not null)
                    {
                        Emgu.CV.Matrix<int> imageSize = calibrationDataPreferred.LeftCalibrationCameraData.ImageSize!;
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
                    CalibratedIndicator.Text = calibratedIndictor;

                    ToolTipService.SetToolTip(CalibratedIndicator, tooltip);
                }
                else
                {
                    if (frameWidth is not null)
                    {
                        tooltip = $"Failed to return Preferred Calibration Data for frame size ({frameWidth},{frameHeight})!\nAvailable calibration sets:\n" + MakeCalibrationDescriptionListTooltip(calibrationClass);
                        // Show the calibration icon
                        CalibratedIndicator.Text = calibratedIndictor + "!";
                    }
                    else
                    {
                        tooltip = $"Failed to return Preferred Calibration Data!\nAvailable calibration sets:\n" + MakeCalibrationDescriptionListTooltip(calibrationClass);
                        // Show the calibration icon
                        CalibratedIndicator.Text = calibratedIndictor;
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

                if (calibrationClass.CalibrationDataList[i].Description is not null && calibrationClass.CalibrationDataList[i].LeftCalibrationCameraData.ImageSize is not null)
                {
                    Emgu.CV.Matrix<int> imageSize = calibrationClass.CalibrationDataList[i].LeftCalibrationCameraData.ImageSize!;
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
        private async Task CheckIfEventMeasurementsAreUpToDate()
        {

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
                            if (evt.EventDataType == DataType.SurveyMeasurementPoints && evt.EventData is not null)
                            {
                                SurveyMeasurement surveyMeasurement = (SurveyMeasurement)evt.EventData;
                                if (surveyMeasurement.CalibrationID != calibrationID)
                                {
                                    upToDate = false;
                                    break;
                                }
                            }
                        }


                        if (!upToDate)
                        {
                            // Ask the user if they want to update the event measurements
                            string message = $"The current event measurements are not up to date with the preferred calibration data. Do you want to update the event measurements?";
                            string primaryButtonText = "Yes";
                            string secondaryButtonText = "No";

                            // Ask the user
                            ContentDialog confirmationDialog = new()
                            {
                                Title = "Update Event Measurements",
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
                                // Update the event measurements
                                foreach (Event evt in surveyClass.Data.Events.EventList)
                                {
                                    if (evt.EventDataType == DataType.SurveyMeasurementPoints && evt.EventData is not null)
                                    {
                                        SurveyMeasurement surveyMeasurement = (SurveyMeasurement)evt.EventData;
                                        if (surveyMeasurement.CalibrationID != calibrationID)
                                        {
                                            DoMeasurementCalculations(surveyMeasurement);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Populate the SurveyMeasurement with the measurement calculates from the stereo projection
        /// Note the LeftX, LeftY, RightX, RightY should have already been loaded in 
        /// SurveyMeasurement surveyMeasurement
        /// </summary>
        /// <param name="surveyMeasurement"></param>
        /// <returns></returns>
        public bool DoMeasurementCalculations(SurveyMeasurement surveyMeasurement)
        {
            if (stereoProjection.PointsLoad(
                new Point(surveyMeasurement.LeftXA, surveyMeasurement.LeftYA),
                new Point(surveyMeasurement.LeftXB, surveyMeasurement.LeftYB),
                new Point(surveyMeasurement.RightXA, surveyMeasurement.RightYA),
                new Point(surveyMeasurement.RightXB, surveyMeasurement.RightYB)) == true)
            {

                // Calculate fish length
                surveyMeasurement.Distance = stereoProjection.Measurement();

                // Calculate range (distance from origin)
                surveyMeasurement.Range = stereoProjection.RangeFromCameraSystemCentrePointToMeasurementCentrePoint();

                // Calculate the X & Y offset between the camera system mid-point and the measurement point mid-point
                surveyMeasurement.XOffset = stereoProjection.XOffsetFromCameraSystemCentrePointToMeasurementCentrePoint();
                surveyMeasurement.YOffset = stereoProjection.YOffsetFromCameraSystemCentrePointToMeasurementCentrePoint();

                // Record the calidation data Guid used to calculate the measurement
                // This is used to enable recalulation of the measurement if the calibration data is changed
                surveyMeasurement.CalibrationID = stereoProjection.GetCalibrationID();

                return true;
            }

            return false;
        }


        /// <summary>
        /// Set the title text elements of the titlebar title text
        /// </summary>
        /// <param name="titleText"></param>
        public void SetTitle(string titleText)
        {
            titlebarTitle = titleText;

            TitleBarTextBlock.Text = BuildTitleFromElements();
        }


        /// <summary>
        /// Set the save status text elements of the titlebar title text
        /// </summary>
        /// <param name="saveStatus"></param>
        public void SetTitleSaveStatus(string saveStatus)
        {
            titlebarSaveStatus = saveStatus;

            TitleBarTextBlock.Text = BuildTitleFromElements();
        }


        /// <summary>
        /// Set the camera side status text elements of the titlebar title text
        /// </summary>
        /// <param name="cameraSide"></param>
        public void SetTitleCameraSide(string cameraSide)
        {
            titlebarCameraSide = cameraSide;

            TitleBarTextBlock.Text = BuildTitleFromElements();
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

            RightPaddingColumn.Width = new GridLength(appWindow.TitleBar.RightInset / scaleAdjustment);
            LeftPaddingColumn.Width = new GridLength(appWindow.TitleBar.LeftInset / scaleAdjustment);

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
            MenuFileLockUnlockMediaPlayers.Text = "Unlock Media Players";
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
            MenuFileLockUnlockMediaPlayers.Text = "Lock Media Players";
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

        private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {              
                ShowSettingsWindow();
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
        private void ShowSettingsWindow()
        {
            if (surveyClass is not null)
            {
                SettingsWindow settingsWindow = new((Survey)surveyClass);

                settingsWindow.InitializeMediator(mediator, this);

                // Set the secondary window as modal
                settingsWindow.Activate();

                // Use P/Invoke to force the window on top
                IntPtr settingsWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(settingsWindow);
                WindowInteropHelper.SetWindowAlwaysOnTop(settingsWindowHandle, true);


                // Disable interaction with the main window (not sure this is working)
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
                    Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                        WinRT.Interop.WindowNative.GetWindowHandle(this)
                    )
                );
            }
        }



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
            var recentSurveys = (localSettings.Values[RECENT_SURVEYS_KEY] as string[]) ?? new string[0];

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
                    menuItem.Click += RecentSurvey_Click;

                    MenuRecentSurveys.Items.Add(menuItem);
                }
            }
        }
    }


    /// <summary>
    /// Sets and restore the window position and size and which monitor
    /// </summary>
    public static class WindowStateHelper
    {
        // P/Invoke to get monitor information
        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        // P/Invoke to get window monitor
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        // Save window position and size
        public static void SaveWindowState(IntPtr hWnd, AppWindow appWindow)
        {
            // Find the monitor associated with the window
            IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);

            // Retrieve DPI scaling factors
            uint dpiX, dpiY;
            DPIHelper.GetDpiForMonitor(monitor, DPIHelper.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);

            // Convert window coordinates to DIPs
            var position = appWindow.Position;
            var size = appWindow.Size;
            int dipX = (int)(position.X * 96.0 / dpiX);
            int dipY = (int)(position.Y * 96.0 / dpiY);

            // Save values in local settings
            ApplicationData.Current.LocalSettings.Values["WindowPosX"] = dipX;
            ApplicationData.Current.LocalSettings.Values["WindowPosY"] = dipY;
            ApplicationData.Current.LocalSettings.Values["WindowWidth"] = (int)(size.Width * 96.0 / dpiX);
            ApplicationData.Current.LocalSettings.Values["WindowHeight"] = (int)(size.Height * 96.0 / dpiY);
        }

        // Restore window position and size
        public static void RestoreWindowState(IntPtr hWnd, AppWindow appWindow)
        {
            // Retrieve values from local settings
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("WindowPosX", out object? posX) &&
                ApplicationData.Current.LocalSettings.Values.TryGetValue("WindowPosY", out object? posY) &&
                ApplicationData.Current.LocalSettings.Values.TryGetValue("WindowWidth", out object? width) &&
                ApplicationData.Current.LocalSettings.Values.TryGetValue("WindowHeight", out object? height))
            {
                // Find the monitor associated with the window
                IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);

                // Retrieve DPI scaling factors
                uint dpiX, dpiY;
                DPIHelper.GetDpiForMonitor(monitor, DPIHelper.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);

                // Convert DIPs back to physical pixels
                int pixelX = (int)(Convert.ToInt32(posX) * dpiX / 96.0);
                int pixelY = (int)(Convert.ToInt32(posY) * dpiY / 96.0);
                int pixelWidth = (int)(Convert.ToInt32(width) * dpiX / 96.0);
                int pixelHeight = (int)(Convert.ToInt32(height) * dpiY / 96.0);

                // Restore window size and position using Win32 API
                SetWindowPos(hWnd, IntPtr.Zero, pixelX, pixelY, pixelWidth, pixelHeight, SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }

        // P/Invoke to set window position
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }

    public class DPIHelper
    {
        // Monitor DPI retrieval function from Windows API
        [DllImport("Shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        // Constants for DPI scaling
        public const int MDT_EFFECTIVE_DPI = 0;
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
        }
    }

}
