using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Surveyor.Helper;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationBoard : Page, SetupRunCalibration.IWizardPage
    {
        private NavParams? navParams;

        public SetupRunCalibrationBoard()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            navParams = e.Parameter as NavParams;
            if (navParams is not null)
                CalibrationBoardSettingsControl.SetupForProjectSettingWindow(navParams.calibProject);

            (WindowHelper.GetWindowForElement(this) as SetupRunCalibration)?.RequestFooterButtonsRefresh();
        }

        // Wizard interface
        //public bool CanGoBack => false;
        public bool CanGoBack
        {
            get
            {
                return false;
            }
        }
        //public bool CanGoNext => navParams?.calibProject != null;
        public bool CanGoNext
        {
            get
            {
                bool ret = navParams?.calibProject != null;
                Debug.WriteLine($"SetupRunCalibrationBoard: CanGoNext={ret}, {navParams}, {navParams?.calibProject}");
                return navParams?.calibProject != null;
            }
        }

        public Task GoBackAsync() => Task.CompletedTask;

        public Task GoNextAsync()
        {
            Frame?.Navigate(typeof(SetupRunCalibrationSettings), navParams);

            var host = WindowHelper.GetWindowForElement(this) as SetupRunCalibration;
            if (host != null)
            {
                // Prefer robust lookup by Tag instead of relying on public fields
                var settingsItem = host.NavView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(i => (i.Tag as string) == "CalibrationSettings");

                if (settingsItem != null)
                    host.NavView.SelectedItem = settingsItem;

                host.RequestFooterButtonsRefresh();
            }
            return Task.CompletedTask;
        }

        private void SetupRunCalibrationBoardNext_Click(object sender, RoutedEventArgs e)
        {
            Frame?.Navigate(typeof(SetupRunCalibrationSettings), navParams);

            var navView = FindParentNavigationView();
            if (navView != null)
            {
                var targetItem = navView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(i => (i.Tag as string) == "CalibrationSettings");
                if (targetItem != null && navView.SelectedItem != targetItem)
                    navView.SelectedItem = targetItem;
            }
        }

        private NavigationView? FindParentNavigationView()
        {
            DependencyObject? parent = this;
            while (parent != null)
            {
                if (parent is NavigationView nv) return nv;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}