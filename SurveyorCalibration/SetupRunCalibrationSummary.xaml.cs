using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Surveyor
{
    public sealed class SummaryItem
    {
        public string Glyph { get; set; } = "\uEA3A"; // default info glyph
        public string ActionTitle { get; set; } = string.Empty;
        public string ActionDescription { get; set; } = string.Empty;
    }

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

            // Guard
            if (navParams is null) return;

            // Set footer buttons
            if (navParams is not null && navParams.setupRunCalibration is not null)
                navParams.setupRunCalibration.RequestFooterButtonsRefresh();

            // Summarize current calibration project
            UpdateSummary();

            // Disable the RunCalibration button if no actions requested
            if (navParams.AnyActions())
                RunCalibrationButton.IsEnabled = true;
            else
                RunCalibrationButton.IsEnabled = false;
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
            // Guard
            if (navParams is null)
                return;


            // Project Info (non-repeating)
            switch (navParams.calibProject.Data.Media.StereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    ModeText.Text = "Calibration using two Mono and and a pair of Stereo Media files";
                    ModeDescriptionText.Text = "Results in a stereo calibration result using the left and right mono media as the input into the mono calibration result and the stereo media and the mono calibration result as input into the stereo calibration result";
                    LeftMonoMediaRow.Height = GridLength.Auto;
                    RightMonoMediaRow.Height = GridLength.Auto;
                    LeftStereoMediaRow.Height = GridLength.Auto;
                    RightStereoMediaRow.Height = GridLength.Auto;
                    RightSerialNumberRow.Height = GridLength.Auto;
                    break;
                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    ModeText.Text = "Calibration using a pair of Stereo Media files";
                    ModeDescriptionText.Text = "Results in a stereo calibration result using the stereo media as input into both the mono and stereo calibration result";
                    LeftMonoMediaRow.Height = new GridLength(0, GridUnitType.Pixel);
                    RightMonoMediaRow.Height = new GridLength(0, GridUnitType.Pixel);
                    LeftStereoMediaRow.Height = GridLength.Auto;
                    RightStereoMediaRow.Height = GridLength.Auto;
                    RightSerialNumberRow.Height = GridLength.Auto;
                    break;
                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    ModeText.Text = "Calibration using two Mono and and a pair of Stereo Media files";
                    ModeDescriptionText.Text = "Results in two mono calibration result using the left and right mono media as the input. Separate left and right mono calibration results are produced";
                    LeftMonoMediaRow.Height = GridLength.Auto;
                    RightMonoMediaRow.Height = GridLength.Auto;
                    LeftStereoMediaRow.Height = new GridLength(0, GridUnitType.Pixel);
                    RightStereoMediaRow.Height = new GridLength(0, GridUnitType.Pixel);
                    RightSerialNumberRow.Height = GridLength.Auto;
                    break;
                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    ModeText.Text = "Calibration using a single Mono Media file";
                    ModeDescriptionText.Text = "Results in a mono calibration result using a single mono media file as input";
                    LeftMonoMediaRow.Height = GridLength.Auto;
                    RightMonoMediaRow.Height = new GridLength(0, GridUnitType.Pixel);
                    LeftStereoMediaRow.Height = new GridLength(0, GridUnitType.Pixel);
                    RightStereoMediaRow.Height = new GridLength(0, GridUnitType.Pixel);
                    RightSerialNumberRow.Height = new GridLength(0, GridUnitType.Pixel);
                    break;
            }


            ProjectFileNameText.Text = navParams.calibProject.Data.Info.ProjectFileName;
            LeftCamSerialNumberText.Text = navParams.calibProject.Data.Media.LeftCameraID;
            RightCamSerialNumberText.Text = navParams.calibProject.Data.Media.RightCameraID;
            LeftMonoMediaText.Text = navParams.calibProject.Data.Media.LeftMonoMP4FileName;
            RightMonoMediaText.Text = navParams.calibProject.Data.Media.RightMonoMP4FileName;
            LeftStereoMediaText.Text = navParams.calibProject.Data.Media.LeftMonoMP4FileName;
            RightStereoMediaText.Text = navParams.calibProject.Data.Media.RightMonoMP4FileName;


            // Choose glyphs. Use Segoe Fluent Icons code points.
            string glyphSave = "\uE792";        // save
            string glyphTrim = "\uE78A";        // trim/crop
            string glyphCalibration = "\uEB3C"; // calibration
            string glyphSearch = "\uE721";      // search/magnify
            string glyphStar = "\uE735";        // star/favorite

            // Display the action summary using the working values (i.e. values in the dialog)
            // If the use selects RunCalibration these will be copied into run parameters
            var items = new List<SummaryItem>
            {
                new() {
                    Glyph = glyphTrim,
                    ActionTitle  = navParams.FindCalibrationBoardZoneWorkingValue
                        ? "Find calibration board zones"
                        : "Use existing calibration board zones",
                    ActionDescription  = navParams.FindCalibrationBoardZoneWorkingValue
                        ? "Parse the media to establish the Calibration Board Zone. This is where the calibration board is first and last seen in the media"
                        : ""
                },
                new() {                
                    Glyph = glyphSearch,
                    ActionTitle  = navParams.BuildTheFrameSetsWorkingValue
                        ? "Build frame sets"
                        : "Use existing cached frame sets",
                    ActionDescription  = navParams.BuildTheFrameSetsWorkingValue
                        ? "Parse the Calibration Board Zone and extract calibration board information for each frame. This information includes the number of corners and markers detected, the movement from frame to frame and the frame blur"
                        : ""
                },
                new() {
                    Glyph = glyphStar,
                    ActionTitle  = navParams.FindBestMonoFramesWorkingValue
                        ? "Find best mono frames"
                        : "Use existing best mono frames",
                    ActionDescription  = navParams.FindBestMonoFramesWorkingValue
                        ? "From the mono frame sets select the best frames for calibration based on corner count, marker count, movement and blur"
                        : ""
                },
                new() {
                    Glyph = glyphCalibration,
                    ActionTitle  = navParams.DoCalibrationMonoCalculationsWorkingValue
                        ? "Perform mono calibration calculations"
                        : "Use existing mono calibration calculations",
                    ActionDescription  = navParams.DoCalibrationMonoCalculationsWorkingValue
                        ? "Using the best mono frames perform the mono calibration calculations"
                        : ""
                },
                new() {
                    Glyph = glyphStar,
                    ActionTitle  = navParams.FindBestStereoFramesWorkingValue
                        ? "Find best stereo frames"
                        : "Use existing best stereo frames",
                    ActionDescription  = navParams.FindBestStereoFramesWorkingValue
                        ? "From the stereo frame set select the best frames for calibration based on corner count, marker count, movement and blur"
                        : ""
                },
                new() {
                    Glyph = glyphCalibration,
                    ActionTitle  = navParams.DoCalibrationStereoCalculationsWorkingValue
                        ? "Perform stereo calibration calculations"
                        : "Use existing stereo calibration calculations",
                    ActionDescription  = navParams.DoCalibrationStereoCalculationsWorkingValue
                        ? "Using the best stereo frames and the mono calibration results perform the stereo calibration calculation" 
                        : ""
                }
            };

            if (navParams.runParams.SaveBestFrames)
            {
                items.Add(new SummaryItem
                {
                    Glyph = glyphSave,
                    ActionTitle  = "Save best frames into .png files"
                });
            }

            if (!navParams.AnyActions())
            {
                items.Add(new SummaryItem
                {
                    Glyph = "\uEA39", // warning glyph
                    ActionTitle = "No actions selected. No need to run calibration process",
                    ActionDescription = "The calibration results already exist, and no selected actions will affect them. If you need to export the results then select File → Export"
                });
            }

            SummaryList.ItemsSource = items;
        }

        private void RunCalibrationButton_Click(object sender, RoutedEventArgs e) => _ = RunCalibrationButtonClickAsync();

        private async Task RunCalibrationButtonClickAsync()
        {
            if (navParams is not null)
            {
                await Task.Delay(10); // Allow UI to update

                if (App.MainWindow is MainWindow mw)
                {
                    // Transfer dialog settings into run parameters
                    SetupRunCalibrationSettings.TransferSettingsIntoRunParams(navParams);

                    // Run the calibration process
                    _ = mw.RunCalibrationAsync(navParams.runParams);
                }

                (WindowHelper.GetWindowForElement(this) as SetupRunCalibration)?.Close();
            }
        }
    }
}
