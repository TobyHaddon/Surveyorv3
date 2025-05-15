// SettingsWindow
// This is a user control is used to adjust general, survey and (later) Field Trip settings
// 
// Version 1.0
// 
// Version 1.1
// 2025-01-17 Intregrated with new SurveyInfoAndMedia user control
// Version 1.2
// 2025-01-25 Stop the flashing on load between themes


using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using WinUIEx;
using static Surveyor.InternetQueue;
using static Surveyor.SpeciesImageAndInfoCache;
using static Surveyor.User_Controls.SettingsWindowEventData;



namespace Surveyor.User_Controls
{


    /// <summary>
    /// A page that displays the app's settings.
    /// </summary>
    public sealed partial class SettingsWindow : WindowEx
    {
        // Copy of MainWindow
        private readonly MainWindow? mainWindow = null;

        // Reporter
        private Reporter? report = null;

        // Optional sectionto open
        private string sectionToScrollTo = string.Empty;

        // Copy of the mediator 
        private SurveyorMediator? mediator;

        // Declare the mediator handler for MediaPlayer
        private readonly SettingsWindowHandler? settingsWindowHandler;

        private readonly ElementTheme? rootThemeOriginal = null;
        // To detect system-wide theme changes (like Light ↔ Dark), use this API:
        private readonly Windows.UI.ViewManagement.UISettings uiSettings = new();

        public string WinAppSdkRuntimeDetails => App.WinAppSdkRuntimeDetails;

        private readonly Survey? survey = null;

        private bool _isInitializing = false;

        // This is temporary filtered version of the species code list, used if the used searches the DataGrid
        public ObservableCollection<SpeciesItem> FilteredSpeciesItems { get; } = [];

        public SettingsWindow(SurveyorMediator _mediator, MainWindow _mainWindow, Survey? surveyClass, Reporter? _report, string section = "")
        {
            // Remember main window (needed for this method)
            mainWindow = _mainWindow;

            // Remember the survey
            survey = surveyClass;

            // Remember the reporter
            report = _report;

            sectionToScrollTo = section;

            // Restore the saved window state
            PersistenceId = "SettingsWindow";

            this.InitializeComponent();
            this.Closed += SettingsWindow_Closed;

           
            // React to theme changes
            uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;

            // Initialize mediator handler for SurveyorMediaControl
            mediator = _mediator;
            settingsWindowHandler = new SettingsWindowHandler(mediator, this, mainWindow);


            // Set the current saved theme
            SetSettingsTheme(SettingsManagerLocal.ApplicationTheme);

            // Inform the SurveyInfoAndMedia user control that is is being used in the SettingsWindow
            if (surveyClass is not null)
                SurveyInfoAndMedia.SetupForSettingWindow(SettingsCardSurveyInfoAndMedia, surveyClass);

            // Inform the SettingsSurveyRules user control that it is being used in the SettingsWindow for a survey (as opposed to a Field Trip)
            if (surveyClass is not null)
                SettingsSurveyRules.SetupForSurveySettingWindow(surveyClass);

            // Remove the separate title bar from the window
            ExtendsContentIntoTitleBar = true;

            // Force QR Standard Setup
            _ = SetQRCodeSelection(null);  // null selects the first item in the list

            // Hide the Survey Settings if the Survey is null
            if (surveyClass is null)
            {
                // Hide the survey settings section
                SurveySettingsTitle.Visibility = Visibility.Collapsed;
                SurveyInfoAndMediaExpander.Visibility = Visibility.Collapsed;
                CalibrationExpander.Visibility = Visibility.Collapsed;
                SettingsSurveyRules.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Show the survey settings section
                SurveySettingsTitle.Visibility = Visibility.Visible;
                SurveyInfoAndMediaExpander.Visibility = Visibility.Visible;
                CalibrationExpander.Visibility = Visibility.Collapsed;   //???Not Implimented
                SettingsSurveyRules.Visibility = Visibility.Visible;
            }
            

            // Setup the Setting page
            OnSettingsPageLoaded(SettingsManagerLocal.ApplicationTheme);
        }


        /// <summary>
        /// Get the version of the Application from the Package
        /// </summary>
        public string Version
        {
            get
            {
                var version = Package.Current.Id.Version;

                return string.Format("{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision);
            }
        }


        /// <summary>
        /// Set the theme of the application
        /// </summary>
        /// <param name="theme">Dark or Light</param>
        public void SetSettingsTheme(ElementTheme theme)
        {
            var rootElement = (FrameworkElement)(Content);

            if (theme == ElementTheme.Dark)
            {
                // Set the RequestedTheme of the root element to Dark
                rootElement.RequestedTheme = ElementTheme.Dark;

                AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/Surveyor-Dark.png");
                SpeciesCodeListIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Dark.png");

                TitleBarHelper.SetCaptionButtonColors(this, Colors.White);

            }
            else if (theme == ElementTheme.Light)
            {
                // Set the RequestedTheme of the root element to Dark
                rootElement.RequestedTheme = ElementTheme.Light;

                AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/Surveyor-Light.png");
                SpeciesCodeListIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Light.png");

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
                {
                    AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/Surveyor-Dark.png");
                    SpeciesCodeListIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Dark.png");
                }
                else
                {
                    AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/Surveyor-Light.png");
                    SpeciesCodeListIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Light.png");
                }
            }

            // If the theme has changed, announce the change to the user
            UIHelper.AnnounceActionForAccessibility(rootElement, "Theme changed", "ThemeChangedNotificationActivityId");

        }



        ///
        /// EVENTS
        /// 


        /// <summary>
        /// Used to detect if the system theme has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void UiSettings_ColorValuesChanged(Windows.UI.ViewManagement.UISettings sender, object args)
        {
            // Dispatch back to UI thread
            _ = DispatcherQueue.EnqueueAsync(() =>
            {
                if (ThemeHelper.RootTheme == ElementTheme.Default)
                {
                    Debug.WriteLine("System theme changed — refreshing icons...");
                    SetSettingsTheme(ElementTheme.Default);
                }
            });
        }


        /// <summary>
        /// User request a recalculation of the event measurements and the applying of survey rules
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ReCalculate_Click(object sender, RoutedEventArgs e)
        {
            if (mainWindow is not null)
            {
                await mainWindow.CheckIfEventMeasurementsAreUpToDate(true/*forceReCalc*/);
            }
        }


        /// <summary>
        /// Set the combobox theme to the last saved theme
        /// </summary>
        /// <param name="theme"></param>
        private void OnSettingsPageLoaded(ElementTheme theme)
        {         
            _isInitializing = true;

            try
            {
                if (mainWindow is not null)
                {
                    // Auto Save
                    AutoSave.IsOn = SettingsManagerLocal.AutoSaveEnabled;

                    // Load the current theme
                    switch (theme)
                    {
                        case ElementTheme.Light:
                            themeMode.SelectedIndex = 0;
                            break;
                        case ElementTheme.Dark:
                            themeMode.SelectedIndex = 1;
                            break;
                        case ElementTheme.Default:
                            themeMode.SelectedIndex = 2;
                            break;
                    }

                    // Load the current diags info state
                    DiagnosticInformation.IsOn = SettingsManagerLocal.DiagnosticInformation;

                    // Load the SpeciesImageCache state
                    UseSpeciesImageCache.IsOn = SettingsManagerLocal.SpeciesImageCacheEnabled;

                    // Load the teaching tip enabled state
                    TeachingTips.IsOn = SettingsManagerLocal.TeachingTipsEnabled;

                    // Load the Use Internet enabled state
                    UseInternet.IsOn = SettingsManagerLocal.UseInternetEnabled;

                    // Set the tooltip on the information icon on the Species image and information cache expander
                    ToolTip tooltipSpeciesImageCacheFolder = new();
                    try
                    {
                        string localFolderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                        tooltipSpeciesImageCacheFolder.Content = $"Species image and information folder: {localFolderPath}";
                        ToolTipService.SetToolTip(SpeciesImageCacheFolder, tooltipSpeciesImageCacheFolder);
                    }
                    catch { }                

                    // Hide the Edit/Delete buttons until an SpeciesCodeList item is selected
                    SpeciesCodeListEditButton.IsEnabled = false;
                    SpeciesCodeListDeleteButton.IsEnabled = false;

                    // Load the Scientific Name Order enabled state
                    SpeciesCodeListScientificNameOrder.IsOn = SettingsManagerLocal.ScientificNameOrderEnabled;

                    // Set the tooltip on the information icon on the Species List expander
                    ToolTip tooltipSpeciesCodeListFileSpec = new();
                    try
                    {
                        tooltipSpeciesCodeListFileSpec.Content = $"Species file location: {mainWindow?.mediaStereoController.speciesSelector.speciesCodeList.SpeciesCodeListFileSpec}";
                        ToolTipService.SetToolTip(SpeciesCodeListFileSpec, tooltipSpeciesCodeListFileSpec);
                    }
                    catch { }
                    
                    // Refresh the Species State list
                    mainWindow?.mediaStereoController.speciesImageCache.RefreshView();

                    // Refresh the internet queue
                    mainWindow?.internetQueue.RefreshView();

                    // Load the Telemtry setting
                    Telemetry.IsOn = SettingsManagerLocal.TelemetryEnabled;

                    // Load the Experimental setting
                    Experimental.IsOn = SettingsManagerLocal.ExperimentalEnabled;
                }


                // Open section if requested
                if (sectionToScrollTo.Equals("General Settings", StringComparison.OrdinalIgnoreCase))
                {
                    // Open the 'General Settings' section and bring into view
                    ExpandAndSectionIntoView(GeneralSettingsExpander);
                }
                else if (sectionToScrollTo.Equals("Species List", StringComparison.OrdinalIgnoreCase))
                {
                    // Open the 'General Settings' section and bring into view
                    ExpandAndSectionIntoView(SpeciesCodeListExpander);
                }
                else if (sectionToScrollTo.Equals("Camera", StringComparison.OrdinalIgnoreCase))
                {
                    // Open the 'General Settings' section and bring into view
                    ExpandAndSectionIntoView(CameraExpander);
                }
                else if (sectionToScrollTo.Equals("About", StringComparison.OrdinalIgnoreCase))
                {
                    // Open the 'About' section and bring into view
                    ExpandAndSectionIntoView(SettingsExpanderAbout);
                }

            }
            finally
            {
                _isInitializing = false;
            }           
        }


        /// <summary>
        /// Unloaded event for the root grid.  This is used to clean up the UI and close any open dialogs
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RootGrid_Unloaded(object sender, RoutedEventArgs e)
        {
            // Close UI things
            GoProQRCode.Source = null;
            SurveyInfoAndMedia.Shutdown();
            SettingsSurveyRules.Shutdown();
            mainWindow?.mediaStereoController.speciesImageCache.RefreshView(true/*reset*/);
            mainWindow?.internetQueue.RefreshView(true/*reset*/);

            // Optionally clear controls if they’re bound to static objects
            SettingsCardSurveyInfoAndMedia.Content = null;

            uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
        }

        /// <summary>
        /// Apply the theme change to the main window when the settings window is closed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SettingsWindow_Closed(object sender, WindowEventArgs e)
        {
            //  But: don't await anything directly here!


            // Check if the theme has changed
            var rootElement = (FrameworkElement)(this.Content);
            if (rootThemeOriginal != rootElement.RequestedTheme && mainWindow is not null)
                mainWindow.SetTheme(rootElement.RequestedTheme);

            // Set the save theme
            SettingsManagerLocal.ApplicationTheme = rootElement.RequestedTheme;
          
            // Unregister the mediator handler
            if (mediator is not null && settingsWindowHandler is not null)
            {
                mediator.Unregister(settingsWindowHandler);
                mediator = null;
            }

            report = null;

            // Pass focus to the main window
            mainWindow?.Activate();
        }


        /// <summary>
        /// Toggle theauto save survey feature
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AutoSave_Toggled(object sender, RoutedEventArgs e)
        {
            bool settingValue;

            if (_isInitializing) return;

            if (this.AutoSave.IsOn)
            {
                // Enable auto save
                settingValue = true;

                report?.Info("", $"Auto save threaded enabled");
            }
            else
            {
                // Disable auto save
                settingValue = false;

                report?.Info("", $"Auto save threaded disabled");
            }

            // Remember the new state
            SettingsManagerLocal.AutoSaveEnabled = settingValue;
        }


        /// <summary>
        /// Theme selection changed by user.  Apply the new theme to the application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            var selectedTheme = ((ComboBoxItem)themeMode.SelectedItem)?.Tag?.ToString();
            
            if (selectedTheme != null)
            {
                // Get the root element of your application

                var rootElement = (FrameworkElement)(this.Content);

                if (rootElement is null)
                    return;

                ThemeHelper.RootTheme = App.GetEnum<ElementTheme>(selectedTheme);

                if (selectedTheme == "Dark")
                    SetSettingsTheme(ElementTheme.Dark);
                else if (selectedTheme == "Light")
                    SetSettingsTheme(ElementTheme.Light);
                else
                    SetSettingsTheme(ElementTheme.Default);
            }
        }


        
        /// <summary>
        /// Any traching tips that had been marked not to be shown again will be shown again
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReshowTeachingTips_Click(object sender, RoutedEventArgs e)
        {
            SettingsManagerLocal.RemoveAllTeachingTipShown();
        }

        
        /// <summary>
        /// Toggle the allowed to use interst
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseInternet_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool settingValue;

            if (this.UseInternet.IsOn)
            {
                // Enable 
                settingValue = true;

            }
            else
            {
                // Disable
                settingValue = false;
            }

            // Remember the new state
            SettingsManagerLocal.UseInternetEnabled = settingValue;
        }


        /// <summary>
        /// Toggle the teaching tips on or off
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TeachingTips_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool settingValue;

            if (this.TeachingTips.IsOn)
            {
                // Enable teaching tips
                settingValue = true;

            }
            else
            {
                // Disable teaching tips
                settingValue = false;
            }

            // Remember the new state
            SettingsManagerLocal.TeachingTipsEnabled = settingValue;
        }


        private void Telemetry_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool settingValue;

            if (this.Telemetry.IsOn)
            {
                // Enable Microsoft Insights Telemetry
                settingValue = true;

            }
            else
            {
                // Disable Microsoft Insights Telemetry
                settingValue = false;
            }


            var telemetryConfiguration = TelemetryConfiguration.CreateDefault();

            // Report the change to Insights (before if switching off)
            if (telemetryConfiguration.DisableTelemetry != !settingValue/*Check it really changed before reporting*/ && 
                settingValue == false/*switching off*/)
            {
                TelemetryLogger.TrackTrace("User switching telemetry off");
                TelemetryLogger.TrackSettingTelemetry(settingValue);
            }

            // Switch on/off the Insights telemetry as required by the user                    
            telemetryConfiguration.DisableTelemetry = !settingValue;

            // Report the change to Insights (after if switching on)
            if (telemetryConfiguration.DisableTelemetry != !settingValue/*Check it really changed before reporting*/ &&
                settingValue == true/*switching on*/)
            {
                TelemetryLogger.TrackTrace("User switching telemetry on");
                TelemetryLogger.TrackSettingTelemetry(settingValue);
            }

            // Remember the new state
            SettingsManagerLocal.TelemetryEnabled = settingValue;
        }


        /// <summary>
        /// Toggle the diagnostic information on or off
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DiagnosticInformation_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (DiagnosticInformation.IsOn)
            {
                // Enable diagnostic information
                SettingsManagerLocal.DiagnosticInformation = true;
            }
            else
            {
                // Disable diagnostic information
                SettingsManagerLocal.DiagnosticInformation = false;
            }

            // Inform everyone of the state change
            settingsWindowHandler?.Send(new SettingsWindowEventData(eSettingsWindowEvent.DiagnosticInformation)
            {
                diagnosticInformation = SettingsManagerLocal.DiagnosticInformation
            });
        }


        /// <summary>
        /// Eanbled or disable beat release code
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Experimental_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (Experimental.IsOn)
            {
                // Enable diagnostic information
                SettingsManagerLocal.ExperimentalEnabled = true;
            }
            else
            {
                // Disable diagnostic information
                SettingsManagerLocal.ExperimentalEnabled = false;
            }

            // Inform everyone of the state change
            settingsWindowHandler?.Send(new SettingsWindowEventData(eSettingsWindowEvent.Experimental)
            {
                experimentialEnabled = SettingsManagerLocal.ExperimentalEnabled
            });
        }


        /// <summary>
        /// Show the GoPro Camera Setup QR Codes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void GoProQRSelectionMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem)
            {
                await SetQRCodeSelection(menuItem.Text);
            }
        }


        /// <summary>
        /// User requested the species image cache view is updated
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RefreshSpeciesStateView_Click(object sender, RoutedEventArgs e)
        {
            if (mainWindow is not null)
            {
                // Save the selected species name
                string? selectedSpeciesName = (SpeciesImageCacheListView.SelectedItem as SpeciesCacheViewItem)?.SpeciesItem.Species;

                // Refresh the cache view                
                mainWindow.mediaStereoController.speciesImageCache.RefreshView();

                // Try to find the matching item based on Species name
                if (!string.IsNullOrEmpty(selectedSpeciesName))
                {
                    var newSelectedItem = mainWindow.mediaStereoController.speciesImageCache.SpeciesStateView
                        .FirstOrDefault(item => string.Equals(item.SpeciesItem.Species, selectedSpeciesName, StringComparison.OrdinalIgnoreCase));

                    if (newSelectedItem is not null)
                    {
                        SpeciesImageCacheListView.SelectedItem = newSelectedItem;
                        SpeciesImageCacheListView.ScrollIntoView(newSelectedItem, ScrollIntoViewAlignment.Default);
                    }
                }

                SpeciesImageCacheItemImageCount.Text = $"{mainWindow.mediaStereoController.speciesImageCache.TotalImagesAvailable()} images of {mainWindow.mediaStereoController.speciesImageCache.TotalImagesRequired()} available";
            }
            else
                SpeciesImageCacheItemImageCount.Text = string.Empty;
        }


        /// <summary>
        /// User requested the download / upload queue is updated
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RefreshInternetQueueView_Click(object sender, RoutedEventArgs e)
        {
            if (mainWindow is not null)
            {
                // Save the selected url
                string? selectedURL = (InternetQueueListView.SelectedItem as InternetQueueViewItem)?.URL;

                // Refresh the internet queue view
                mainWindow?.internetQueue?.RefreshView();

                // Restore selection (if the item still exists in the refreshed collection)
                if (!string.IsNullOrEmpty(selectedURL))
                {
                    var newSelectedItem = mainWindow?.internetQueue.InternetQueueView
                        .FirstOrDefault(item => string.Equals(item.URL, selectedURL, StringComparison.OrdinalIgnoreCase));

                    if (newSelectedItem is not null)
                    {
                        InternetQueueListView.SelectedItem = newSelectedItem;
                        InternetQueueListView.ScrollIntoView(newSelectedItem, ScrollIntoViewAlignment.Default);
                    }
                }
            }
        }


        /// <summary>
        /// Remove the downadloed and the uploaded items in the internet queue and leave the other
        /// states 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RemoveInternetQueue_Click(object sender, RoutedEventArgs e)
        {
            bool somethingToDo = false;
            int downloadedCount = mainWindow?.internetQueue.InternetQueueView.Count(item => item.Status == Status.Downloaded) ?? 0;
            int uploadedCount = mainWindow?.internetQueue.InternetQueueView.Count(item => item.Status == Status.Uploaded) ?? 0;

            string puralDownloaded = downloadedCount == 1 ? "" : "s";
            string puralUploaded = uploadedCount == 1 ? "" : "s";

            string contentString = "";

            if (downloadedCount == 0 && uploadedCount == 0)
            {
                // do nothing
                somethingToDo = false;
            }
            else if (downloadedCount > 0 && uploadedCount == 0)
            {
                contentString = $"There are {uploadedCount} uploaded record{puralUploaded}. Are you sure you want to remove them from the internet request queue?";
                somethingToDo = true;
            }
            else if (downloadedCount == 0 && uploadedCount > 0)
            {
                contentString = $"There are {uploadedCount} uploaded record{puralUploaded}. Are you sure you want to remove them from the internet request queue?";
                somethingToDo = true;
            }
            else
            {
                contentString = $"There are {downloadedCount} downloaded record{puralDownloaded} and {uploadedCount} uploaded record{puralUploaded}. Are you sure you want to remove them from the internet request queue?";
                somethingToDo = true;
            }

            if (somethingToDo)
            {
                var dialog = new ContentDialog
                {
                    Title = "Confirm Remove",
                    Content = contentString,
                    PrimaryButtonText = "Yes",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot // Required in WinUI 3 for non-windowed dialogs
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    mainWindow?.internetQueue?.RemoveAll(Status.Downloaded);
                    mainWindow?.internetQueue?.RemoveAll(Status.Uploaded);
                }
            }
        }


        /// <summary>
        /// Remove all the records from the Internet Queue
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RemoveAllInternetQueue_Click(object sender, RoutedEventArgs e)
        {
            int count = mainWindow?.internetQueue?.InternetQueueView.Count ?? 0;
            string pural = count == 1 ? "" : "s";

            var dialog = new ContentDialog
            {
                Title = "Confirm Remove",
                Content = $"Are you sure you want to delete all {count} record{pural} from the internet request queue?",
                PrimaryButtonText = "Yes",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot // Required in WinUI 3 for non-windowed dialogs
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                mainWindow?.internetQueue?.RemoveAll();
                mainWindow?.internetQueue?.RefreshView();
            }
        }


        /// <summary>
        /// Remove the currently selected record from the Internet Queue
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RemoveRecordInternetQueue_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = InternetQueueListView.SelectedItem as InternetQueueViewItem;

            if (selectedItem is not null)
            {
                if (!string.IsNullOrEmpty(selectedItem.URL))
                {
                    InternetQueueItem? item = mainWindow?.internetQueue?.Find(selectedItem.URL);

                    if (item is not null)
                    {
                        mainWindow?.internetQueue?.Remove(item);
                        mainWindow?.internetQueue?.RefreshView();
                    }
                }
            }
        }


        /// <summary>
        /// Show the folder where the species image and information cahce can be found
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SpeciesImageCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            string localFolderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;

            if (!string.IsNullOrEmpty(localFolderPath))
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(localFolderPath);
                Clipboard.SetContent(dataPackage);
            }
        }


        /// <summary>
        /// Remove all the records from the species cache
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RemoveAllSpeciesCache_Click(object sender, RoutedEventArgs e)
        {
            int count = mainWindow?.mediaStereoController.speciesImageCache?.SpeciesStateView.Count ?? 0;
            string pural = count == 1 ? "" : "s";

            var dialog = new ContentDialog
            {
                Title = "Confirm Remove",
                Content = $"Are you sure you want to delete all {count} record{pural} from the species image cache. These are the images that help with fish ID?",
                PrimaryButtonText = "Yes",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot // Required in WinUI 3 for non-windowed dialogs
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                mainWindow?.mediaStereoController.speciesImageCache?.RemoveAll();
                mainWindow?.mediaStereoController.speciesImageCache?.RefreshView();
            }
        }


        /// <summary>
        /// Removes a specific record by index from the species image cache
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RemoveRecordSpeciesCache_Click(object sender, RoutedEventArgs e)
        {
            int index = SpeciesImageCacheListView.SelectedIndex;

            if (index != -1)
            {
                try
                {
                    var item = mainWindow?.mediaStereoController.speciesImageCache?.SpeciesStateView[index];
                    if (item is not null)
                    {
                        mainWindow?.mediaStereoController.speciesImageCache?.Remove(item.Code);
                        mainWindow?.mediaStereoController.speciesImageCache?.RefreshView();
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Species Image Cache Enabled
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseSpeciesImageCache_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool settingValue;

            if (this.UseSpeciesImageCache.IsOn)
            {
                // Enable
                settingValue = true;

            }
            else
            {
                // Disable
                settingValue = false;
            }

            // Remember the new state
            SettingsManagerLocal.SpeciesImageCacheEnabled = settingValue;
            mainWindow?.mediaStereoController.speciesImageCache.Enable(settingValue);
        }

        /// <summary>
        /// Display the cached image photos
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        ///        
        private void SpeciesImageCacheDataGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            // If we need an image viewer
        }


        /// <summary>
        /// User can click the information icon to copy the species code list file path to the clipboard
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SpeciesCodeListFileSpec_Click(object sender, RoutedEventArgs e)
        {
            string filePath = mainWindow?.mediaStereoController?.speciesSelector?.speciesCodeList?.SpeciesCodeListFileSpec ?? "";

            if (!string.IsNullOrEmpty(filePath))
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(filePath);
                Clipboard.SetContent(dataPackage);
            }
        }



        /// <summary>
        /// Add a new species code to the list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SpeciesCodeListAdd_Click(object sender, RoutedEventArgs e)
        {
            SpeciesItem speciesItemNew = new();

            if (mainWindow is not null)
            {
                SpeciesRecordEditDialog dialog = new(report);

                SpeciesCodeList speciesCodeList = mainWindow.mediaStereoController.speciesSelector.speciesCodeList;

                if (await dialog.SpeciesRecordNew(this, speciesItemNew, speciesCodeList) == true)
                {
                    // Add the new species code to the list
                    bool ret = mainWindow?.mediaStereoController.speciesSelector.speciesCodeList.AddItem(speciesItemNew, SettingsManagerLocal.ScientificNameOrderEnabled) ?? false;

                    if (!ret)
                    {
                        // Are you sure?
                        var dialog2 = new ContentDialog
                        {
                            Title = "Add Species Code List Record",
                            Content = "Failed to add a new species to species code list.",
                            CloseButtonText = "Cancel",
                            DefaultButton = ContentDialogButton.Close,
                            XamlRoot = this.Content.XamlRoot
                        };

                        await dialog2.ShowAsync();
                    }
                }
            }
        }


        /// <summary>
        /// Edit the selected species code
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SpeciesCodeListEdit_Click(object sender, RoutedEventArgs e)
        {
            if (SpeciesCodeListDataGrid.SelectedItem is not SpeciesItem speciesItem)
                return;

            await SpeciesCodeListEdit((SpeciesItem)speciesItem);
        }


        /// <summary>
        /// Delete the selected species code
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SpeciesCodeListDelete_Click(object sender, RoutedEventArgs e)
        {
            SpeciesItem? speciesItem = SpeciesCodeListDataGrid.SelectedItem as SpeciesItem;

            if (speciesItem is not null)
            {
                // Are you sure?
                var dialog = new ContentDialog
                {
                    Title = "Delete Species Code List Record",
                    Content = "Are you sure you want to delete the selected species code list record?",
                    PrimaryButtonText = "OK",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    mainWindow?.mediaStereoController.speciesSelector.speciesCodeList.DeleteItem(speciesItem);
                }
            }                
        }


        /// <summary>
        /// An item in the species code list data grid has been selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SpeciesCodeListDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeciesCodeListDataGrid.SelectedItem is SpeciesItem)
            {
                SpeciesCodeListEditButton.IsEnabled = true;
                SpeciesCodeListDeleteButton.IsEnabled = true;
            }
        }


        /// <summary>
        /// User double tapped on a species code in the list to also edit the entry
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SpeciesCodeListDataGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (SpeciesCodeListDataGrid.SelectedItem is not SpeciesItem speciesItem)
                return;

            await SpeciesCodeListEdit((SpeciesItem)speciesItem);
        }


        /// <summary>
        /// Toggle sorting on the common name instead of the latin name
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SpeciesCodeListSort_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool settingValue;

            if (this.SpeciesCodeListScientificNameOrder.IsOn)
            {
                // Enable 
                settingValue = true;

            }
            else
            {
                // Disable
                settingValue = false;
            }

            // Remember the new state
            SettingsManagerLocal.ScientificNameOrderEnabled = settingValue;

            // Re-sort
            if (mainWindow is not null)
            {
                if (mainWindow.mediaStereoController.speciesSelector.speciesCodeList.Sort(settingValue))
                    mainWindow.mediaStereoController.speciesSelector.speciesCodeList.Save();
            }
        }


        /// <summary>
        /// Text has changed in the SpeciesCodeList AutoSuggestBox search box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void SpeciesCodeListSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            string searchText = SpeciesCodeListSearchBox.Text;

            if (searchText.Length > 2 || searchText == string.Empty)
                FilterSpeciesList(searchText);

        }


        /// <summary>
        /// User selected a suggestion from the dropdown in SpeciesCodeList AutoSuggestBox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void SpeciesCodeListSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            // Not used
        }


        /// <summary>
        /// Enter pressed in the SpeciesCodeList AutoSuggestBox search box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void SpeciesCodeListSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {

        }


        ///
        /// MEDIATOR METHODS (Called by the TListener, always marked as internal)
        ///



        ///
        /// PRIVATE
        ///


        /// <summary>
        /// Get the AppWindow associated with the current window
        /// </summary>
        /// <returns></returns>
        //???private AppWindow GetAppWindowForCurrentWindow()
        //{
        //    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        //    var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        //    return AppWindow.GetFromWindowId(windowId);
        //}


        /// <summary>
        /// String that appears in the Settings Expander Info Text
        /// </summary>        
        private string SettingsExpanderInfoText
        {
            get
            {
                StringBuilder sb = new();

                if (survey is not null)
                {
                    if (survey.Data.Info.SurveyCode is not null)
                    {
                        sb.Append(survey.Data.Info.SurveyCode);

                        if (!string.IsNullOrEmpty(survey.Data.Info.SurveyDepth))
                        {
                            sb.Append($"/{survey.Data.Info.SurveyDepth}");
                        }
                    }
                }

                return sb.ToString();
            }
        }


        /// <summary>
        /// Set the toolkit:SettingsCard for the GoPro setup
        /// </summary>
        /// <param name="name"></param>
        private async Task SetQRCodeSelection(string? name)
        {
            List<(string, string)> GoProScriptsList = SettingsManagerApp.Instance.GoProScripts;

            if (name is null && GoProScriptsList.Count > 0)
            {
                name = GoProScriptsList[0].Item1;
            }

            if (name is not null)
            {
                // Does the DropDownButton need loading?
                int scriptCount = SettingsManagerApp.Instance.GoProScripts.Count;
                int dropDownCount = GoProQRSelectionFlyout.Items.Count;
                if (scriptCount != dropDownCount)
                {
                    GoProQRSelectionFlyout.Items.Clear();
                    foreach (var (key, value) in GoProScriptsList)
                    {
                        MenuFlyoutItem menuItem = new()
                        {
                            Text = key
                        };
                        menuItem.Click += GoProQRSelectionMenuFlyoutItem_Click;
                        GoProQRSelectionFlyout.Items.Add(menuItem);
                    }
                }

                // Update button text
                GoProQRSelection.Content = name;

                // Find the script name (key) in the list and get the value
                string? script = GoProScriptsList.Find(x => x.Item1 == name).Item2;
                if (script is not null)
                {
                    // Update the QR Code and script
                    GoProQRCode.Source = await QRCodeGeneratorHelper.GenerateQRCode(script);
                    GoProQRScript.Text = $"{script}";
                }
            }
            else
            {
                Console.WriteLine($"No GoPro scripts loaded");
                GoProQRCode.Source = null;
                GoProQRScript.Text = "Failed!";
            }
        }


        /// <summary>
        /// Handle the edit species info dialog
        /// </summary>
        /// <param name="speciesItem"></param>
        /// <returns></returns>
        private async Task SpeciesCodeListEdit(SpeciesItem speciesItem)
        {
            if (speciesItem is not null && mainWindow is not null )
            {
                SpeciesRecordEditDialog dialog = new(report);

                SpeciesCodeList speciesCodeList = mainWindow.mediaStereoController.speciesSelector.speciesCodeList;

                if (await dialog.SpeciesRecordEdit(this, speciesItem, speciesCodeList) == true)
                {
                    // Update the species code to the list
                    mainWindow?.mediaStereoController.speciesSelector.speciesCodeList.UpdateItem(speciesItem, SettingsManagerLocal.ScientificNameOrderEnabled);

                    // Because it is an existing item that has been editted we need to force an update
                    // Note This is because INotifyPropertyChanged/OnPropertyChanged() isn't implemented
                    // on the SpeciesItem class
                    SpeciesCodeListDataGrid.UpdateLayout();
                    SpeciesCodeListDataGrid.ScrollIntoView(speciesItem, null);
                }
            }
        }


        /// <summary>
        /// Filter the species code list based on the search text
        /// </summary>
        /// <param name="searchText"></param>
        private void FilterSpeciesList(string searchText)
        {
            if (mainWindow is not null)
            {

                FilteredSpeciesItems.Clear();

                if (!string.IsNullOrEmpty(searchText))
                {
                    var lower = searchText?.ToLowerInvariant() ?? "";

                    foreach (var item in mainWindow.mediaStereoController.speciesSelector.speciesCodeList.SpeciesItems)
                    {
                        if (string.IsNullOrWhiteSpace(searchText) ||
                            item.Genus?.ToLowerInvariant().Contains(lower, StringComparison.InvariantCultureIgnoreCase) == true ||
                            item.Species?.ToLowerInvariant().Contains(lower, StringComparison.InvariantCultureIgnoreCase) == true ||
                            item.Family?.ToLowerInvariant().Contains(lower, StringComparison.InvariantCultureIgnoreCase) == true ||
                            item.Code?.ToLowerInvariant().Contains(lower, StringComparison.InvariantCultureIgnoreCase) == true)
                        {
                            FilteredSpeciesItems.Add(item);
                        }
                    }

                    // Bind the DataGrid to the filtered list if necessary
                    if (SpeciesCodeListDataGrid.ItemsSource != FilteredSpeciesItems)
                        SpeciesCodeListDataGrid.ItemsSource = FilteredSpeciesItems;
                }
                else
                {
                    // No search text, show all items. Bind back to the full list if necessary
                    if (SpeciesCodeListDataGrid.ItemsSource != mainWindow.mediaStereoController.speciesSelector.speciesCodeList.SpeciesItems)
                        SpeciesCodeListDataGrid.ItemsSource = mainWindow.mediaStereoController.speciesSelector.speciesCodeList.SpeciesItems;

                }
            }        
        }


        /// <summary>
        /// Expand the expander and bring it into view
        /// </summary>
        /// <param name="expander"></param>
        private void ExpandAndSectionIntoView(SettingsExpander expander)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                expander.IsExpanded = true;

                // Get position of the expander relative to the ScrollViewer
                var transform = expander.TransformToVisual(contentSV);
                var point = transform.TransformPoint(new Point(0, 0));
                double expanderTop = point.Y;
                double expanderHeight = expander.ActualHeight;

                // Get the height of the visible viewport of the ScrollViewer
                double viewportHeight = contentSV.ViewportHeight;

                // Scroll so that the whole expander is visible if possible
                double targetOffset = expanderTop + expanderHeight - viewportHeight;

                // Clamp to 0 in case the expander fits already
                double scrollTo = Math.Max(0, targetOffset);

                contentSV.ChangeView(null, scrollTo, null);
            });
        }




        // ***END OF SettingsWindow***
    }


    /// <summary>
    /// XAML Converter 
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }


    /// <summary>
    /// Used by the SettingsWindow User Control to inform other components of settings changes
    /// </summary>
    public class SettingsWindowEventData
    {
        public SettingsWindowEventData(eSettingsWindowEvent e)
        {
            settingsWindowEvent = e;
        }

        public enum eSettingsWindowEvent
        {
            MagnifierWindow,        // The Magnifier Window has been toggled
            DiagnosticInformation,  // Diagnostic Information has changed
            Experimental            // Allow experimental features setting has changed
        }

        public readonly eSettingsWindowEvent settingsWindowEvent;

        // Only used for settingsWindowEvent.MagnifierWindow
        public bool? magnifierWindowAutomatic;

        // Only used for settingsWindowEvent.DiagnosticInformation
        public bool? diagnosticInformation;

        // Only used for settingsWindowEvent.Experimental
        public bool? experimentialEnabled;
    }



    public class SettingsWindowHandler : TListener
    {
        private readonly SettingsWindow _settingsWindow;

        public SettingsWindowHandler(IMediator mediator, SettingsWindow settingsWindow, MainWindow mainWindow) : base(mediator, mainWindow)
        {
            _settingsWindow = settingsWindow;
        }

        public override void Receive(TListener listenerFrom, object message)
        {
            // In case we need later
        }
    }
}

