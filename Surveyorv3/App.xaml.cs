using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Linq;
using Windows.ApplicationModel;
//???using WASDK = Microsoft.WindowsAppSDK;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            // Assuming m_window will be initialized later
            m_window = null!;                                   // TH:Added
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            if (m_window is not null)                           // TH:Added
            {                                                   // TH:Added
                m_window.Activate();

                // Subscribe to the Closed event
                m_window.Closed += MainWindow_Closed;
            }                                                   // TH:Added
        }
        private void MainWindow_Closed(object sender, WindowEventArgs e)
        {
            // Handle the window closed event here
            if (m_window is MainWindow mainWindow)
            {
                mainWindow.AppClosed(); // Call the method on your MainWindow instance
            }
        }

        private Window m_window;


        public static string WinAppSdkDetails
        {
            get
            {
                var version = Package.Current.Id.Version;
                return string.Format("Windows App SDK {0}.{1}.{2}.{3}",
                    version.Major, version.Minor, version.Build, version.Revision);
            }
        }

        public static string WinAppSdkRuntimeDetails
        {
            get
            {
                try
                {
                    // Retrieve Windows App Runtime version info dynamically
                    var runtimeVersion =
                        (from module in Process.GetCurrentProcess().Modules.OfType<ProcessModule>()
                         where module.FileName.EndsWith("Microsoft.WindowsAppRuntime.Insights.Resource.dll")
                         select FileVersionInfo.GetVersionInfo(module.FileName)).FirstOrDefault();

                    if (runtimeVersion != null)
                    {
                        return WinAppSdkDetails + ", Windows App Runtime " + runtimeVersion.FileVersion;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to retrieve Windows App Runtime details: {ex.Message}");
                }

                // Fallback
                return WinAppSdkDetails + ", Windows App Runtime Unknown";
            }
        }

    }
}
