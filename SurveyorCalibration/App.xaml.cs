using Microsoft.UI.Xaml;
using Surveyor.Helper;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Windows.ApplicationModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Load the command line Args
            GetArgs.GetArg("/StereoLeft", out string stereoLeft, true/*remove quotes*/);
            GetArgs.GetArg("/StereoRight", out string stereoRight, true/*remove quotes*/);
            GetArgs.GetArg("/MonoLeft", out string monoLeft, true/*remove quotes*/);
            GetArgs.GetArg("/MonoRight", out string monoRight, true/*remove quotes*/);
            GetArgs.GetArg("/Run", out bool? run);
            GetArgs.GetArg("/UseCache", out bool? cache);
            GetArgs.GetArg("/LeftSync", out int? leftSync);
            GetArgs.GetArg("/RightSync", out int? rightSync);
            GetArgs.GetArg("/SaveBestFrames", out bool? saveBestFrames);
            

            AppLaunchArgs.StereoLeft = stereoLeft;
            AppLaunchArgs.StereoRight = stereoRight;
            AppLaunchArgs.MonoLeft = monoLeft;
            AppLaunchArgs.MonoRight = monoRight;
            AppLaunchArgs.RunWithoutPrompts = run ?? false;
            AppLaunchArgs.UseCache = cache ?? false;
            AppLaunchArgs.SyncFrameIndexLeft = leftSync;
            AppLaunchArgs.SyncFrameIndexRight = rightSync;
            AppLaunchArgs.SaveBestFrames = saveBestFrames;

            Debug.WriteLine($"Run: {AppLaunchArgs.RunWithoutPrompts}");
            Debug.WriteLine($"StereoLeft: {AppLaunchArgs.StereoLeft}");
            Debug.WriteLine($"StereoLRight: {AppLaunchArgs.StereoRight}");
            Debug.WriteLine($"MonoLeft: {AppLaunchArgs.MonoLeft}");
            Debug.WriteLine($"MonoRight: {AppLaunchArgs.MonoRight}");
            Debug.WriteLine($"Use Cached Results: {AppLaunchArgs.UseCache}");
            Debug.WriteLine($"Left Sync: {AppLaunchArgs.SyncFrameIndexLeft}");
            Debug.WriteLine($"Right Sync: {AppLaunchArgs.SyncFrameIndexRight}");
            Debug.WriteLine($"Save Best Frames: {AppLaunchArgs.SaveBestFrames}");

            m_window = MainWindow = new MainWindow();
            m_window.Activate();
        }

        private Window? m_window;


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
