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
                }
                else
                {
                    string findCalibrationBoardZone = navParams.runCalibrationParams.FindCalibrationBoardZone ? "Find calibration board zones\n" : "Use existing calibration board zones\n";
                    string buildTheFrameSets = navParams.runCalibrationParams.BuildTheFrameSets ? "Build frame sets\n" : "Use existing cached frame sets\n";
                    string findBestMonoFrames = navParams.runCalibrationParams.FindBestMonoFrames ? "Find best mono frames\n" : "Use existing best mono frames\n";
                    string doCalibrationMonoCalculations = navParams.runCalibrationParams.DoCalibrationMonoCalculations ? "Perform mono calibration calculations\n" : "Use existing mono calibration calculations\n";
                    string findBestStereoFrames = navParams.runCalibrationParams.FindBestStereoFrames ? "Find best stereo frames\n" : "Use existing best stereo frames\n";
                    string doCalibrationStereoCalculations = navParams.runCalibrationParams.DoCalibrationStereoCalculations ? "Perform stereo calibration calculations\n" : "Use existing stereo calibration calculations\n";
                    string saveBestFrames = navParams.runCalibrationParams.SaveBestFrames ? "Save best frames into .png files\n" : "";

                    Summary.Text = $"Project: {navParams.calibProject.Data.Info.ProjectFileName}\n" +
                        $"Mode: {navParams.calibProject.Data.Media.StereoMonoMediaSetMode}\n\n" +
                        $"Left Camera: {navParams.calibProject.Data.Media.LeftCameraID}\n" +
                        $"Right Camera: {navParams.calibProject.Data.Media.RightCameraID}\n" +
                        "\n" +
                        findCalibrationBoardZone + "\n" +
                        buildTheFrameSets + "\n" +
                        findBestMonoFrames + "\n" +
                        doCalibrationMonoCalculations + "\n" +
                        findBestStereoFrames + "\n" +
                        doCalibrationStereoCalculations + "\n" +
                        saveBestFrames;
                }
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
