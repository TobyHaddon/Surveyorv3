using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
    }
}
