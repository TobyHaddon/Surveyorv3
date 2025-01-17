// SettingsWindow
// This is a user control is used to adjust general, survey and (later) Field Trip settings
// 
// Version 1.0
// 
// Version 1.1
// 2025-01-17 Intregrated with new SurveyInfoAndMedia user control

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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


        public SettingsWindow(Survey surveyClass)
        {
            this.InitializeComponent();
            this.Closed += SettingsWindow_Closed;


            // Inform the SurveyInfoAndMedia user control that is is being used in the SettingsWindow
            SurveyInfoAndMedia.SetupForSettingWindow(SettingsCardSurveyInfoAndMedia, surveyClass);

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

            // Set the current saved theme
            SetSettingsTheme(SettingsManager.ApplicationTheme);

            // Setup the Setting page
            OnSettingsPageLoaded(SettingsManager.ApplicationTheme);
        }


        /// <summary>
        /// Initialize mediator handler for SurveyorMediaControl
        /// </summary>
        /// <param name="mediator"></param>
        /// <returns></returns>
        public TListener InitializeMediator(SurveyorMediator mediator, MainWindow mainWindow)
        {
            _mediator = mediator;
            _mainWindow = mainWindow;

            _settingsWindowHandler = new SettingsWindowHandler(_mediator, this, mainWindow);

            return _settingsWindowHandler;
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
                magnifierWindowAutomatic.IsOn = SettingsManager.MagnifierWindowAutomatic;
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
            _settingsWindowHandler!.Cleanup();
        }


        /// <summary>
        /// Theme selection changed by user.  Apply the new theme to the application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void themeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
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


        private void diagnosticInformation_Toggled(object sender, RoutedEventArgs e)
        {
            if (diagnosticInformation.IsOn)
            {
                // Enable diagnostic information
                SettingsManager.DisplayPointerCoordinates = true;
            }
            else
            {
                // Disable diagnostic information
                SettingsManager.DisplayPointerCoordinates = false;
            }
            
        }

        private void magnifierWindowAutomatic_Toggled(object sender, RoutedEventArgs e)
        {
            bool settingValue;

            if (magnifierWindowAutomatic.IsOn)
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
            ///??? TO DO: survey .MP4 files
            get => "TO DO [*] survey .MP4 files";
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
            MagnifierWindow     // The Magnifier Window has been toggled
        }

        public readonly eSettingsWindowEvent settingsWindowEvent;

        // Only used for eSettingsWindowEvent.settingsWindowEvent
        public bool? magnifierWindowAutomatic;

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

