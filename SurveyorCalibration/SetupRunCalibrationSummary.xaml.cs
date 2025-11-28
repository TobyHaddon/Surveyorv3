using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Surveyor.Helper;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationSummary : Page, SetupRunCalibration.IWizardPage
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

            // Set footer buttons
            if (navParams is not null && navParams.setupRunCalibration is not null)
                navParams.setupRunCalibration.RequestFooterButtonsRefresh();

            // Summarize current calibration project
            UpdateSummary();
        }


        // Wizard interface
        public bool CanGoBack => navParams?.calibProject != null;
        public bool CanGoNext => false;

        // Go to Calibration Settings page
        public Task GoBackAsync()
        {
            navParams?.setupRunCalibration.GoToPage(typeof(SetupRunCalibrationSummary)/*class*/, "CalibrationSummary"/*tag*/);

            return Task.CompletedTask;
        }

        public Task GoNextAsync() => Task.CompletedTask;


        /// <summary>
        /// Build a summary of the current calibration project
        /// </summary>
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
                await Task.Delay(10); // Allow UI to update

                // Invoke MainWindow's calibration entry point
                if (App.MainWindow is MainWindow mw)
                {
                    
                    // Fire and forget
                    _ = mw.RunCalibrationAsync(navParams.runCalibrationParams);
                }

                // Close host NaView window
                (WindowHelper.GetWindowForElement(this) as SetupRunCalibration)?.Close();
            }
        }       
    }
}
