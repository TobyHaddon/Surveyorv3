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
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using static Surveyor.User_Controls.SettingsWindowEventData;



namespace Surveyor.User_Controls
{


    /// <summary>
    /// A page that displays the app's settings.
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        // Copy of MainWindow
        private readonly MainWindow? mainWindow = null;

        // Reporter
        Reporter? report = null;

        // Copy of the mediator 
        private readonly SurveyorMediator? mediator;

        // Declare the mediator handler for MediaPlayer
        private readonly SettingsWindowHandler? settingsWindowHandler;

        private readonly ElementTheme? rootThemeOriginal = null;
        // To detect system-wide theme changes (like Light ↔ Dark), use this API:
        private readonly Windows.UI.ViewManagement.UISettings uiSettings = new();

        public string WinAppSdkRuntimeDetails => App.WinAppSdkRuntimeDetails;

        private readonly Survey? survey = null;

        public SettingsWindow(SurveyorMediator _mediator, MainWindow _mainWindow, Survey? surveyClass, Reporter? _report)
        {
            // Remember main window (needed for this method)
            mainWindow = _mainWindow;

            this.InitializeComponent();
            this.Closed += SettingsWindow_Closed;
            
            // React to theme changes
            uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;

            // Initialize mediator handler for SurveyorMediaControl
            mediator = _mediator;
            settingsWindowHandler = new SettingsWindowHandler(mediator, this, mainWindow);

            // Remember the survey
            survey = surveyClass;

            // Remember the reporter
            report = _report;

            // Set the current saved theme
            SetSettingsTheme(SettingsManagerLocal.ApplicationTheme);

            // Inform the SurveyInfoAndMedia user control that is is being used in the SettingsWindow
            if (surveyClass is not null)
                SurveyInfoAndMedia.SetupForSettingWindow(SettingsCardSurveyInfoAndMedia, surveyClass);

            // Inform the SettingsSurveyRules user control that it is being used in the SettingsWindow for a survey (as opposed to a Field Trip)
            if (surveyClass is not null)
                SettingsSurveyRules.SetupForSurveySettingWindow(SettingsExpanderSurveyRules, surveyClass);

            // Remove the separate title bar from the window
            ExtendsContentIntoTitleBar = true;

            // Get the AppWindow associated with this Window
            var appWindow = GetAppWindowForCurrentWindow();
            Debug.WriteLine($"SettingsWindow() Initial Size:({appWindow.ClientSize.Width},{appWindow.ClientSize.Height})");

            // Get the scaling factor for the current display
            double scalingFactor = GetScalingFactorForWindow();
            Debug.WriteLine($"Scaling Factor: {scalingFactor}");

            // Adjust the width and height based on the scaling factor
            int adjustedWidth = (int)(1230 * scalingFactor);
            int adjustedHeight = (int)(800 * scalingFactor);

            // Set the size of the window
            appWindow.Resize(new SizeInt32(adjustedWidth, adjustedHeight));
            Debug.WriteLine($"SettingsWindow() Post resize Size:({appWindow.ClientSize.Width},{appWindow.ClientSize.Height})");

            // Center the window on the screen
            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            appWindow.Move(new PointInt32(
                (workArea.Width - adjustedWidth) / 2,
                (workArea.Height - adjustedHeight) / 2
            ));

            // Force QR Standard Setup
            _ = SetQRCodeSelection(null);  // null selects the first item in the list

            // Hide the Survey Settings if the Survey is null
            SurveySettingsExpander.Visibility = survey is null ? Visibility.Collapsed : Visibility.Visible;

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
                SpeciesIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Dark.png");

                TitleBarHelper.SetCaptionButtonColors(this, Colors.White);

            }
            else if (theme == ElementTheme.Light)
            {
                // Set the RequestedTheme of the root element to Dark
                rootElement.RequestedTheme = ElementTheme.Light;

                AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/Surveyor-Light.png");
                SpeciesIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Light.png");

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
                    SpeciesIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Dark.png");
                }
                else
                {
                    AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/Surveyor-Light.png");
                    SpeciesIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Light.png");
                }
            }

            // If the theme has changed, announce the change to the user
            UIHelper.AnnounceActionForAccessibility(rootElement, "Theme changed", "ThemeChangedNotificationActivityId");

        }



        ///
        /// EVENTS
        /// 


        /// <summary>
        /// Toggle theauto save survey feature
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AutoSave_Toggled(object sender, RoutedEventArgs e)
        {
            bool settingValue;

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

                // Load the current diags indo state
                DiagnosticInformation.IsOn = SettingsManagerLocal.DiagnosticInformation;

                // Load the teaching tip enabled state
                TeachingTips.IsOn = SettingsManagerLocal.TeachingTipsEnabled;

                // Load the Use Internet enabled state
                UseInternet.IsOn = SettingsManagerLocal.UseInternetEnabled;

                // Refresh the Species State list
                mainWindow.mediaStereoController.speciesImageCache.RefreshView();
            }
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

            // Unregister the mediator handler
            if (mediator is not null && settingsWindowHandler is not null)
                mediator.Unregister(settingsWindowHandler);

            // Pass focus to the main window
           mainWindow?.Activate();
        }


        /// <summary>
        /// Theme selection changed by user.  Apply the new theme to the application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
        /// Toggle the diagnostic information on or off
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DiagnosticInformation_Toggled(object sender, RoutedEventArgs e)
        {
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
                diagnosticInformation = DiagnosticInformation.IsOn
            });
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
        /// Toggle the teaching tips on or off
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TeachingTips_Toggled(object sender, RoutedEventArgs e)
        {
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


        /// <summary>
        /// Toggle the allowed to use interst
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseInternet_Toggled(object sender, RoutedEventArgs e)
        {
            bool settingValue;

            if (this.UseInternet.IsOn)
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
            SettingsManagerLocal.UseInternetEnabled = settingValue;
        }


        /// <summary>
        /// Because internet can be intermittent on survey sites request for downloads and uploads can be batched up
        /// This option allows that list to be reset
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseInternetResetDownloadUploadList_Click(object sender, RoutedEventArgs e)
        {
            mainWindow?.downloadUploadManager.RemoveAll();
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
            mainWindow?.mediaStereoController.speciesImageCache.RefreshView();
        }


        /// <summary>
        /// User requested the download / upload queue is updated
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RefreshDownloadUploadView_Click(object sender, RoutedEventArgs e)
        {
            mainWindow?.downloadUploadManager?.RefreshView();
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
        private AppWindow GetAppWindowForCurrentWindow()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }


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
        /// Get the scaling factor for the display
        /// </summary>
        /// <returns></returns>
        private double GetScalingFactorForWindow()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this); // Get the native window handle
            uint dpi = GetDpiForWindow(hWnd); // Get DPI for the current window
            return dpi / 96.0; // Calculate scaling factor (96 DPI = 100%)
        }

        [DllImport("User32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);


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
                    GoProQRScript.Text = $"Script:\n{script}";
                }
            }
            else
            {
                Console.WriteLine($"No GoPro scripts loaded");
                GoProQRCode.Source = null;
                GoProQRScript.Text = "Failed!";
            }
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
            DiagnosticInformation   // Diagnostic Information has changed
        }

        public readonly eSettingsWindowEvent settingsWindowEvent;

        // Only used for settingsWindowEvent.MagnifierWindow
        public bool? magnifierWindowAutomatic;

        // Only used for settingsWindowEvent.DiagnosticInformation
        public bool? diagnosticInformation;

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

