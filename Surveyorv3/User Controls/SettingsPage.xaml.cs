using System;
//???using WinUIGallery.Helper;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
//???using WinUIGallery.DesktopWap.Helper;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor.User_Controls
{
    /// <summary>
    /// A page that displays the app's settings.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        public string Version
        {
            get
            {
                var version = System.Reflection.Assembly.GetEntryAssembly()!.GetName().Version;
                if (version is not null)
                    return string.Format("{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision);
                else
                    return "Not available";
            }
        }

        public string WinAppSdkRuntimeDetails => App.WinAppSdkRuntimeDetails;
        //???private int lastNavigationSelectionMode = 0;

        public SettingsPage()
        {
            this.InitializeComponent();
            Loaded += OnSettingsPageLoaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }

        private void OnSettingsPageLoaded(object sender, RoutedEventArgs e)
        {
            //???var currentTheme = ThemeHelper.RootTheme;
            //switch (currentTheme)
            //{
            //    case ElementTheme.Light:
            //        themeMode.SelectedIndex = 0;
            //        break;
            //    case ElementTheme.Dark:
            //        themeMode.SelectedIndex = 1;
            //        break;
            //    case ElementTheme.Default:
            //        themeMode.SelectedIndex = 2;
            //        break;
            //}
        }
    }
}

