using Emgu.CV.Ocl;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.UI.Xaml;
using Surveyor.Helper;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
// Top of App.xaml.cs (before any namespace or class)
// This is to allow the UnitTestApp to access internal members 
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using static Surveyor.Helper.TelemetryLogger;
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

        // Insights telemetry client
        private TelemetryClient _telemetryClient;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // Catch unhandled exceptions
            this.UnhandledException += App_UnhandledException;    

            this.InitializeComponent();

            // Assuming m_window will be initialized later
            mainWindow = null!;                                   


#if !DEBUG
            // Setup Application Insights
            var telemetryConfiguration = TelemetryConfiguration.CreateDefault();

            // Use your full connection string from Azure here:
            telemetryConfiguration.ConnectionString = "InstrumentationKey=7054862f-6fba-495a-b6e8-b7f2d765f679;IngestionEndpoint=https://northeurope-2.in.applicationinsights.azure.com/;LiveEndpoint=https://northeurope.livediagnostics.monitor.azure.com/;ApplicationId=85e44611-f20c-4bf1-ba80-9c05a9de8c15";

            _telemetryClient = new TelemetryClient(telemetryConfiguration);
            TelemetryLogger.Client = _telemetryClient;  // Setup the TelemetryLogger helper class to use this telemetry client

            // Check if the user has requested no telemetry
            if (SettingsManagerLocal.TelemetryEnabled == false)
            {
                telemetryConfiguration.DisableTelemetry = true;
            }

            // Track app start event
            TelemetryLogger.TrackAppStartStop(TrackAppStartStopType.AppStart);

#endif
        }


        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            mainWindow = new MainWindow();
            if (mainWindow is not null)     
            {
                mainWindow.Closed += (sender, e) =>
                {
                    _telemetryClient?.Flush();
                    System.Threading.Thread.Sleep(1000); // Give time to send
                };
                mainWindow.Activate();
            }                               
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            _telemetryClient?.Flush();
            // Allow time for flushing before shutdown
            Task.Delay(1000).Wait();
        }


        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                TelemetryLogger.TrackAppStartStop(TrackAppStartStopType.AppStopCrash);

                string message = e.Exception?.Message ?? "Unknown error";

                Debug.WriteLine($"Unhandled Exception: {message}");
                mainWindow?.report.Warning("", $"Unhandled XAML exception: {message}");

                mainWindow?.report.Unload(); // Save changes safely

                e.Handled = true; // Prevent the app from crashing immediately (optional)
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error while handling unhandled exception: {ex.Message}");
            }
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
}
