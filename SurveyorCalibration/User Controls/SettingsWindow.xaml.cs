// SettingsWindow
// This is a user control is used to adjust settings
// 
// Version 1.3
// Devived from the Surveyorv3 project


using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using WinUIEx;
using Surveyor;
using System.Threading.Tasks; // CacheManager



namespace Surveyor.User_Controls
{


    /// <summary>
    /// A page that displays the app's settings.
    /// </summary>
    public sealed partial class SettingsWindow : WindowEx
    {
        // Copy of MainWindow
        private readonly MainWindow? mainWindow = null;

        // Optional section to open
        private string sectionToScrollTo = string.Empty;

        private readonly ElementTheme? rootThemeOriginal = null;
        // To detect system-wide theme changes (like Light ↔ Dark), use this API:
        private readonly Windows.UI.ViewManagement.UISettings uiSettings = new();

        public string WinAppSdkRuntimeDetails => App.WinAppSdkRuntimeDetails;

        private readonly CalibProject? project = null;

        private bool _isInitializing = false;

        private readonly CacheManager cacheManager = new();


        public SettingsWindow(MainWindow _mainWindow, CalibProject? _project, string section = "")
        {
            // Remember main window (needed for this method)
            mainWindow = _mainWindow;

            // Remember the project
            project = _project;
            sectionToScrollTo = section;

            // Restore the saved window state
            PersistenceId = "SettingsWindow";

            InitializeComponent();

            // Track this window so WindowHelper.GetWindowForElement works.
            Surveyor.Helper.WindowHelper.TrackWindow(this);

            this.Closed += SettingsWindow_Closed;


            // React to theme changes
            uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;


            // Set the current saved theme
            SetSettingsTheme(SettingsManagerLocal.ApplicationTheme);

            // Inform the ProectStereoInfoAndMedia user control that it is being used in the SettingsWindow for a project
            ProjectStereoInfoAndMedia.SetupForSettingWindow(SettingsCardProjectStereoInfoAndMedia, project);
            SettingsCardProjectStereoInfoAndMedia.Visibility = Visibility.Visible;

            // Inform the SettingsProjectCalibrationBoard user control that it is being used in the SettingsWindow for a project            
            SettingsProjectCalibrationBoard.SetupForProjectSettingWindow(project);

            // Inform the SettingsDefaultCalibrationBoard user control that it is being used in the SettingsWindow
            // And the null meants we are only working on the adjusting the default calibration board settings
            SettingsDefaultCalibrationBoard.SetupForProjectSettingWindow(null);

            // Remove the separate title bar from the window
            ExtendsContentIntoTitleBar = true;

            // Hide the Project Settings if the CalibProject is null
            if (project is not null)
            {
                // Show the project settings section
                ProjectSettingsTitle.Visibility = Visibility.Visible;
                CalibInfoAndMediaExpander.Visibility = Visibility.Visible;
                SettingsExpanderProjectCalibrationBoard.Visibility = Visibility.Visible;
            }
            else
            {
                // Hide the project settings section
                ProjectSettingsTitle.Visibility = Visibility.Collapsed;
                CalibInfoAndMediaExpander.Visibility = Visibility.Collapsed;
                SettingsExpanderProjectCalibrationBoard.Visibility = Visibility.Collapsed;
            }

            // Setup the Setting page
            OnSettingsPageLoaded(SettingsManagerLocal.ApplicationTheme);
            UpdateCalibrationCacheUsage();
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

                AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/SurveyorCalibration-Dark.png");

                TitleBarHelper.SetCaptionButtonColors(this, Colors.White);

            }
            else if (theme == ElementTheme.Light)
            {
                // Set the RequestedTheme of the root element to Dark
                rootElement.RequestedTheme = ElementTheme.Light;

                AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/SurveyorCalibration-Light.png");

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
                    AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/SurveyorCalibration-Dark.png");
                }
                else
                {
                    AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/SurveyorCalibration-Light.png");
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

                    // Load the current diags info saved state
                    DiagnosticInformation.IsOn = SettingsManagerLocal.DiagnosticInformation;

                    // Load the teaching tip saved state
                    TeachingTips.IsOn = SettingsManagerLocal.TeachingTipsEnabled;

                    // Load the Use Internet saved state
                    UseInternet.IsOn = SettingsManagerLocal.UseInternetEnabled;

                    // Load the Telemtry setting
                    Telemetry.IsOn = SettingsManagerLocal.TelemetryEnabled;

                    // Load the Experimental setting
                    Experimental.IsOn = SettingsManagerLocal.ExperimentalEnabled;
                    ExperimentalFeatureSetA.IsChecked = SettingsManagerLocal.ExperimentalFeatureSetAEnabled;
                    ExperimentalFeatureSetB.IsChecked = SettingsManagerLocal.ExperimentalFeatureSetBEnabled;
                    ExperimentalFeatureSetC.IsChecked = SettingsManagerLocal.ExperimentalFeatureSetCEnabled;
                    ExperimentalFeatureSetA.IsEnabled = Experimental.IsOn;
                    ExperimentalFeatureSetB.IsEnabled = Experimental.IsOn;
                    ExperimentalFeatureSetC.IsEnabled = Experimental.IsOn;
                }


                // Open section if requested
                if (sectionToScrollTo.Equals("General Settings", StringComparison.OrdinalIgnoreCase))
                {
                    // Open the 'General Settings' section and bring into view
                    ExpandAndSectionIntoView(GeneralSettingsExpander);
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
            ProjectStereoInfoAndMedia.Shutdown();

            // Optionally clear controls if they’re bound to static objects
            SettingsCardProjectStereoInfoAndMedia.Content = null;

            uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
        }

        /// <summary>
        /// Apply the theme change to the main window when the settings window is closed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SettingsWindow_Closed(object sender, WindowEventArgs e)
        {

            // Check if the theme has changed
            var rootElement = (FrameworkElement)(this.Content);
            if (rootThemeOriginal != rootElement.RequestedTheme && mainWindow is not null)
                mainWindow.SetTheme(rootElement.RequestedTheme);

            // Set the save theme
            SettingsManagerLocal.ApplicationTheme = rootElement.RequestedTheme;


            // Pass focus to the main window
            mainWindow?.Activate();
        }


        /// <summary>
        /// Toggle the auto save project feature
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
            }
            else
            {
                // Disable auto save
                settingValue = false;
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
        }


        /// <summary>
        /// Copy the path to the local folder where the report files are stored to the clipboard
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReporterFolder_Click(object sender, RoutedEventArgs e)
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

            ExperimentalFeatureSetA.IsEnabled = Experimental.IsOn;
            ExperimentalFeatureSetB.IsEnabled = Experimental.IsOn;
            ExperimentalFeatureSetC.IsEnabled = Experimental.IsOn;
        }


        /// <summary>
        /// Check feature sets
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ExperimentalFeatureSetA_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (ExperimentalFeatureSetA.IsChecked is not null && (bool)ExperimentalFeatureSetA.IsChecked)
            {
                // Enable diagnostic information
                SettingsManagerLocal.ExperimentalFeatureSetAEnabled = true;
            }
            else
            {
                // Disable diagnostic information
                SettingsManagerLocal.ExperimentalFeatureSetAEnabled = false;
            }
        }
        private void ExperimentalFeatureSetB_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (ExperimentalFeatureSetB.IsChecked is not null && (bool)ExperimentalFeatureSetB.IsChecked)
            {
                // Enable diagnostic information
                SettingsManagerLocal.ExperimentalFeatureSetBEnabled = true;
            }
            else
            {
                // Disable diagnostic information
                SettingsManagerLocal.ExperimentalFeatureSetBEnabled = false;
            }
        }
        private void ExperimentalFeatureSetC_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (ExperimentalFeatureSetC.IsChecked is not null && (bool)ExperimentalFeatureSetC.IsChecked)
            {
                // Enable diagnostic information
                SettingsManagerLocal.ExperimentalFeatureSetCEnabled = true;
            }
            else
            {
                // Disable diagnostic information
                SettingsManagerLocal.ExperimentalFeatureSetCEnabled = false;
            }
        }






        ///
        /// MEDIATOR METHODS (Called by the TListener, always marked as internal)
        ///



        ///
        /// PRIVATE
        ///


        /// <summary>
        /// String that appears in the Settings Expander Info Text
        /// </summary>        
        private string SettingsExpanderInfoText
        {
            get
            {
                StringBuilder sb = new();

                if (project is not null)
                {
                    sb.Append(Path.GetFileNameWithoutExtension(project.Data.Info.ProjectFileName));
                }

                return sb.ToString();
            }
        }



        /// <summary>
        /// Used to records the status of the experimental features on entry
        /// </summary>
        private bool onEntryExperimentalEnabled = false;
        private bool onEntryExperimentalFeatureSetAEnabled = false;
        private bool onEntryExperimentalFeatureSetBEnabled = false;
        private bool onEntryExperimentalFeatureSetCEnabled = false;
        private void RememberExperimentalStatus()
        {
            onEntryExperimentalEnabled = SettingsManagerLocal.ExperimentalEnabled;
            onEntryExperimentalFeatureSetAEnabled = SettingsManagerLocal.ExperimentalFeatureSetAEnabled;
            onEntryExperimentalFeatureSetBEnabled = SettingsManagerLocal.ExperimentalFeatureSetBEnabled;
            onEntryExperimentalFeatureSetCEnabled = SettingsManagerLocal.ExperimentalFeatureSetCEnabled;
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

        /// <summary>
        /// Copy the cache folder path to the clipboard
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CacheFolderInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string localFolderPath = cacheManager.GetCachePath();
                if (!string.IsNullOrEmpty(localFolderPath))
                {
                    var dataPackage = new DataPackage();
                    dataPackage.SetText(localFolderPath);
                    Clipboard.SetContent(dataPackage);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CacheFolderInfo_Click: {ex.Message}");
            }
        }


        /// <summary>
        /// Deletes old cache files
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ClearCache_Click(object sender, RoutedEventArgs e) => _ = ClearCacheAsync();
        private async Task ClearCacheAsync()
        {
            bool result = cacheManager.ClearCacheOlderItems();
            var dialog = new ContentDialog
            {
                Title = "Clear Cache",
                Content = result ? "Older cache items cleared." : "Failed to clear cache items.",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
            UpdateCalibrationCacheUsage();
        }

        private string _calibrationCacheUsageText = string.Empty;
        public string CalibrationCacheUsageText => _calibrationCacheUsageText;

        private void UpdateCalibrationCacheUsage()
        {
            try
            {
                long bytes = cacheManager.GetCacheTotalDiskSpaceUsed();
                // Format to human readable
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = bytes;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len /= 1024;
                }
                _calibrationCacheUsageText = string.Format("{0:0.##} {1}", len, sizes[order]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateCalibrationCacheUsage: {ex.Message}");
                _calibrationCacheUsageText = "N/A";
            }
            // Force UI refresh for x:Bind OneWay
            CalibrationCacheUsage.Text = _calibrationCacheUsageText;
        }
        // ***END OF SettingsWindow***
    }
}
