// SettingsWindow
// This is a user control is used to adjust general, survey and (later) Field Trip settings
// 
// Version 1.0
// 
// Version 1.1
// 2025-01-17 Intregrated with new SurveyInfoAndMedia user control
// Version 1.2
// 2025-01-25 Stop the flashing on load between themes

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.Graphics;
using static Surveyor.User_Controls.SettingsWindowEventData;



namespace Surveyor.User_Controls
{


    /// <summary>
    /// A page that displays the app's settings.
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        // Copy of MainWindow
        private MainWindow? _mainWindow = null;

        // Copy of the mediator 
        private SurveyorMediator? _mediator;

        // Declare the mediator handler for MediaPlayer
        private SettingsWindowHandler? _settingsWindowHandler;

        private readonly ElementTheme? rootThemeOriginal = null;

        public string WinAppSdkRuntimeDetails => App.WinAppSdkRuntimeDetails;

        private readonly Survey? survey = null;

        public SettingsWindow(SurveyorMediator mediator, MainWindow mainWindow, Survey surveyClass)
        {
            // Remember main window (needed for this method)
            _mainWindow = mainWindow;

            this.InitializeComponent();
            this.Closed += SettingsWindow_Closed;

            // Initialize mediator handler for SurveyorMediaControl
            _mediator = mediator;
            _settingsWindowHandler = new SettingsWindowHandler(_mediator, this, mainWindow);

            // Remember the survey
            survey = surveyClass;

            // Set the current saved theme
            SetSettingsTheme(SettingsManager.ApplicationTheme);

            // Inform the SurveyInfoAndMedia user control that is is being used in the SettingsWindow
            SurveyInfoAndMedia.SetupForSettingWindow(SettingsCardSurveyInfoAndMedia, surveyClass);

            // Inform the SettingsSurveyRules user control that it is being used in the SettingsWindow for a survey (as opposed to a Field Trip)
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
            _ = SetQRCodeSelection("Standard Setup");

            // Setup the Setting page
            OnSettingsPageLoaded(SettingsManager.ApplicationTheme);
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

                TitleBarHelper.SetCaptionButtonColors(this, Colors.White);

            }
            else if (theme == ElementTheme.Light)
            {
                // Set the RequestedTheme of the root element to Dark
                rootElement.RequestedTheme = ElementTheme.Light;

                AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/Surveyor-Light.png");

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
                    AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/Surveyor-Dark.png");

                else
                    AboutAppIcon.UriSource = new Uri($"ms-appx:///Assets/Surveyor-Light.png");
            }

            // If the theme has changed, announce the change to the user
            UIHelper.AnnounceActionForAccessibility(rootElement, "Theme changed", "ThemeChangedNotificationActivityId");

        }


        ///
        /// EVENTS
        /// 


        /// <summary>
        /// User request a recalculation of the event measurements and the applying of survey rules
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ReCalculate_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow is not null)
            {
                await _mainWindow.CheckIfEventMeasurementsAreUpToDate(true/*forceReCalc*/);
            }
        }


        /// <summary>
        /// Set the combobox theme to the last saved theme
        /// </summary>
        /// <param name="theme"></param>
        private void OnSettingsPageLoaded(ElementTheme theme)
        {
            if (_mainWindow is not null)
            {
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

                // Load the current isAutoMagnify state
                MagnifierWindowAutomatic.IsOn = SettingsManager.MagnifierWindowAutomatic;

                // Load the current diags indo state
                DiagnosticInformation.IsOn = SettingsManager.DiagnosticInformation;

                // Load the teaching tip enabled state
                TeachingTips.IsOn = SettingsManager.TeachingTipsEnabled;
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
            if (rootThemeOriginal != rootElement.RequestedTheme && _mainWindow is not null)
                _mainWindow.SetTheme(rootElement.RequestedTheme);

            // Set the save theme
            SettingsManager.ApplicationTheme = rootElement.RequestedTheme;

            // Unregister the mediator handler
            if (_mediator is not null && _settingsWindowHandler is not null)
                _mediator.Unregister(_settingsWindowHandler);
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
                SettingsManager.DiagnosticInformation = true;
            }
            else
            {
                // Disable diagnostic information
                SettingsManager.DiagnosticInformation = false;
            }

            // Inform everyone of the state change
            _settingsWindowHandler?.Send(new SettingsWindowEventData(eSettingsWindowEvent.DiagnosticInformation)
            {
                diagnosticInformation = DiagnosticInformation.IsOn
            });
        }


        /// <summary>
        /// Toggle the automatic magnifier window on or off
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MagnifierWindowAutomatic_Toggled(object sender, RoutedEventArgs e)
        {
            bool settingValue;

            if (MagnifierWindowAutomatic.IsOn)
            {
                // Enable automatic magnifier window
                settingValue = true;
                
            }
            else
            {
                // Disable automatic magnifier window
                settingValue = false;
            }

            // Remember the new state
            SettingsManager.MagnifierWindowAutomatic = settingValue;

            // Inform everyone of the state change
            _settingsWindowHandler?.Send(new SettingsWindowEventData(eSettingsWindowEvent.MagnifierWindow)
            {
                magnifierWindowAutomatic = settingValue
            });
        }


        /// <summary>
        /// Any traching tipss that had been marked not to be shown again will be shown again
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReshowTeachingTips_Click(object sender, RoutedEventArgs e)
        {
            SettingsManager.RemoveAllTeachingTipShown();
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
            SettingsManager.TeachingTipsEnabled = settingValue;
        }


        /// <summary>
        /// Show the GoPro Camera Setup QR Codes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private readonly string goproScriptStandardSetup = "!MQRDR=0mVr4p30q1oR1*64BT=64000\"Shake to record. Shutter to stop\"!MBOOT=\"!Luwp\"!SAVEuwp=>a0.8<r0\"Start\"+!S+H2!R";
        private readonly string goproScriptReset = "!MBOOT=\"!Luwp\"!SAVEuwp=";

        private async void GoProQRSelectionMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem)
            {
                await SetQRCodeSelection(menuItem.Text);
            }
        }


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
        private async Task SetQRCodeSelection(string name)
        {
            GoProQRSelection.Content = name; // Update button text

            if (name == "Standard Setup")
            {
                GoProQRCode.Source = await QRCodeGeneratorHelper.GenerateQRCode(goproScriptStandardSetup);
                GoProQRScript.Text = $"Script:\n{goproScriptStandardSetup}";
            }
            else if (name == "Reset")
            {
                GoProQRCode.Source = await QRCodeGeneratorHelper.GenerateQRCode(goproScriptReset);
                GoProQRScript.Text = $"Script:\n{goproScriptReset}";
            }

        }


        // ***END OF SettingsWindow***
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

