using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Linq;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationSummary : Page
    {
        private CalibProject? _calibProject;
        public SetupRunCalibrationSummary()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _calibProject = e.Parameter as CalibProject;
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (_calibProject == null)
            {
                Summary.Text = "No calibration project loaded.";
                return;
            }
            Summary.Text = $"Project: {_calibProject.Data.Info.ProjectFileName}\nMode: {_calibProject.Data.Media.StereoMonoMediaSetMode}\nLeft Camera: {_calibProject.Data.Media.LeftCameraID}\nRight Camera: {_calibProject.Data.Media.RightCameraID}";
        }

        private void RunCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for launching calibration workflow
            Summary.Text += "\nCalibration run started...";
        }

        private void SetupRunCalibrationSummaryBack_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to settings page, passing the current CalibProject (can be null)
            Frame?.Navigate(typeof(SetupRunCalibrationSettings), _calibProject);

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
