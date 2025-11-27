using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Surveyor.Helper;
using System.Linq;
using System.Threading.Tasks;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationSummary : Page
    {
        private NavParams? navParams;
        public SetupRunCalibrationSummary()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            navParams = e.Parameter as NavParams;
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (navParams is not null)
            {
                if (navParams.calibProject is null)
                {
                    Summary.Text = "No calibration project loaded.";
                    return;
                }
                Summary.Text = $"Project: {navParams.calibProject.Data.Info.ProjectFileName}\n"+
                    $"Mode: {navParams.calibProject.Data.Media.StereoMonoMediaSetMode}\n"+
                    $"Left Camera: {navParams.calibProject.Data.Media.LeftCameraID}\n"+
                    $"Right Camera: {navParams.calibProject.Data.Media.RightCameraID}";
            }
        }

        private void RunCalibrationButton_Click(object sender, RoutedEventArgs e) => _ = RunCalibrationButtonClickAsync();
        private async Task RunCalibrationButtonClickAsync()
        {
            if (navParams is not null)
            {
                // Invoke MainWindow's calibration entry point
                if (App.MainWindow is MainWindow mw)
                {
                    // Fire and forget
                    _ = mw.RunCalibrationAsync();
                }

                // Close host window
                (WindowHelper.GetWindowForElement(this) as SetupRunCalibration)?.Close();
            }
        }

        private void SetupRunCalibrationSummaryBack_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to settings page, passing the current CalibProject (can be null)
            Frame?.Navigate(typeof(SetupRunCalibrationSettings), navParams);

            // Update NavView selection to "Calibration Settings"
            var navView = FindParentNavigationView();
            if (navView != null)
            {
                var targetItem = navView.MenuItems
                                        .OfType<NavigationViewItem>()
                                        .FirstOrDefault(i => (i.Tag as string) == "CalibrationSettings");
                if (targetItem != null && (NavigationViewItem)navView.SelectedItem != targetItem)
                {
                    navView.SelectedItem = targetItem;
                }
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
