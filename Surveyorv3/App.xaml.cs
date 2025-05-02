using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Windows.ApplicationModel;


// Top of App.xaml.cs (before any namespace or class)
// This is to allow the UnitTestApp to access internal members 
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
[assembly: InternalsVisibleTo("Surveyor3")]  


namespace Surveyor
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        // Needs to be 'internal' for Unit Testing
        static internal MainWindow? mainWindow;


        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            Microsoft.UI.Xaml.Application.Current.UnhandledException += (sender, e) =>
            {                
                mainWindow?.report.Warning("", $"Unhandled XAML exception: {e.Message}");
            };

            this.InitializeComponent();

            // Assuming m_window will be initialized later
            mainWindow = null!;                                   // TH:Added

            this.UnhandledException += App_UnhandledException;    // TH:Added
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            mainWindow = new MainWindow();
            if (mainWindow is not null)                           // TH:Added
            {                                                   // TH:Added
                mainWindow.Activate();
            }                                                   // TH:Added
            

            // Attempt to force DirectX Force Hardware Rendering
            //???var manager = Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
        }


        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                // Save crash report / persist important data
                Debug.WriteLine($"Unhandled Exception: {e.Exception?.Message}");

                mainWindow?.report.Unload(); // Unload the report to save any changes


                e.Handled = true; // Optionally prevent app from crashing immediately
            }
            catch { }
        }


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

        public static TEnum GetEnum<TEnum>(string text) where TEnum : struct
        {
            if (!typeof(TEnum).GetTypeInfo().IsEnum)
            {
                throw new InvalidOperationException("Generic parameter 'TEnum' must be an enum.");
            }
            return (TEnum)Enum.Parse(typeof(TEnum), text);
        }
    }

    //public static class TestAppHost
    //{
    //    public static MainWindow? MainWindowRef { get; private set; }

    //    public static async Task<MainWindow> EnsureMainWindowAsync()
    //    {
    //        if (MainWindowRef == null)
    //        {
    //            MainWindowRef = new MainWindow();
    //            MainWindowRef.Activate();

    //            // Wait briefly to ensure the DispatcherQueue is running
    //            await Task.Delay(250);
    //        }

    //        return MainWindowRef;
    //    }
    //}
}
