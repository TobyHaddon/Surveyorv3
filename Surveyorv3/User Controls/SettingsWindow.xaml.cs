using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Surveyor.Helper;
using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Surveyor.DesktopWap.Helper;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI.ViewManagement;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor.User_Controls
{
    

    /// <summary>
    /// A page that displays the app's settings.
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        private readonly MainWindow? mainWindow = null;
        private readonly ElementTheme? rootThemeOriginal = null;

        public string WinAppSdkRuntimeDetails => App.WinAppSdkRuntimeDetails;


        public SettingsWindow(MainWindow _mainWindow)
        {
            mainWindow = _mainWindow;

            this.InitializeComponent();
            this.Closed += SettingsWindow_Closed;

            ExtendsContentIntoTitleBar = true;

            // Get the AppWindow associated with this Window
            var appWindow = GetAppWindowForCurrentWindow();

            // Set the size of the window
            appWindow.Resize(new SizeInt32(800, 600));

            // Center the window on the screen
            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            appWindow.Move(new PointInt32(
                (workArea.Width - 800) / 2,
                (workArea.Height - 600) / 2
            ));

            // Remember the theme as the SettingWindow opens
            //???var rootElement = (FrameworkElement)(mainWindow.Content);
            //???rootThemeOriginal = rootElement.RequestedTheme;
            SetSettingsTheme(SettingsManager.ApplicationTheme);

            OnSettingsPageLoaded(SettingsManager.ApplicationTheme);
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
            SettingsManager.ApplicationTheme = rootElement.RequestedTheme;
        }


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
        /// Set the combobox theme to the last saved theme
        /// </summary>
        /// <param name="theme"></param>
        private void OnSettingsPageLoaded(ElementTheme theme)
        {
            if (mainWindow is not null)
            {
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
            }
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
    }
}

