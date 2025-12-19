using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Surveyor.Helper;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationSettings : Page, SetupRunCalibration.IWizardPage
    {
        private NavParams? navParams;
        private bool? movementDragging; // flag for movement slider drag state

        public SetupRunCalibrationSettings()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            navParams = e.Parameter as NavParams;

            if (navParams is not null)
            {
                // Set footer buttons            
                navParams.setupRunCalibration.RequestFooterButtonsRefresh();

                // Set if the calibration is mono or stereo
                UpdateModeText();

                // Hide the border (and hence the checkbox) if no cache is available
                // Note the checkbox value is handled by the binding
                RunCalibrationParams runParams = navParams.runCalibrationParams;
                FindCalibrationBoardZoneCheckBox.Visibility = !runParams.FindCalibrationBoardZone ? Visibility.Visible : Visibility.Collapsed;
                BuildFrameSetsCheckBox.Visibility = !runParams.BuildTheFrameSets ? Visibility.Visible : Visibility.Collapsed;
                FindBestMonoFramesCheckBox.Visibility = !runParams.FindBestMonoFrames ? Visibility.Visible : Visibility.Collapsed;

                //??if (navParams.runCalibrationParams.FindCalibrationBoardZone)
                //??BorderCache.Visibility = navParams.runCalibrationParams.UseFrameSetCache ? Visibility.Visible : Visibility.Collapsed;

                // Set slider values from project data
                if (navParams.calibProject.Data is not null)
                {
                    // Movement Slider Value
                    movementDragging = null; // This is so the first OnSliderValueChanged is treated differently to the others
                    MovementFilterSlider.Value = runParams.MovementFilterValue;

                    // Movement Slider Min
                    if (runParams.MovementFilterMin is null)
                        MovementFilterSlider.Minimum = 0;
                    else
                        MovementFilterSlider.Minimum = runParams.MovementFilterMin.Value;
                    
                    MovementFilterMin.Text = MovementFilterSlider.Minimum.ToString("F1");

                    // Movement Slider Max
                    if (runParams.MovementFilterMax is null)
                        MovementFilterSlider.Maximum = RunCalibrationParams.MovementFilterMaxDefault;
                    else
                        MovementFilterSlider.Maximum = runParams.MovementFilterMax.Value;
                    
                    MovementFilterMax.Text = MovementFilterSlider.Maximum.ToString("F1");

                    // Blur Slider value
                    BlurFilterSlider.Value = runParams.BlurFilterValue;

                    // Blur Slider Min
                    if (runParams.BlurFilterMin is null)
                        BlurFilterSlider.Minimum = 0;
                    else
                        BlurFilterSlider.Minimum = runParams.BlurFilterMin.Value;
                    BlurFilterMin.Text = BlurFilterSlider.Minimum.ToString("F1");

                    // Blur Slider Max
                    if (runParams.BlurFilterMax is null)
                        BlurFilterSlider.Maximum = RunCalibrationParams.BlurMaxFilterMaxDefault;
                    else
                        BlurFilterSlider.Maximum = runParams.BlurFilterMax.Value;
                    BlurFilterMax.Text = BlurFilterSlider.Maximum.ToString("F1");

                    // Corner Filters values
                    MonoCornerFilterSlider.Value = runParams.MonoCornersFilterValue;
                    StereoCornerFilterSlider.Value = runParams.StereoCornersFilterValue;

                    // Corners min values
                    MonoCornerFilterMin.Text = "0";
                    StereoCornerFilterMin.Text = "0";

                    // Corners max values
                    CalibrationBoardDefinition charucoBoardDefinition = navParams.calibProject.Data.CharucoBoardDefinition;
                    int maxCorners = (charucoBoardDefinition.SquaresX - 1) * (charucoBoardDefinition.SquaresY - 1);
                    MonoCornerFilterSlider.Maximum = maxCorners;
                    StereoCornerFilterSlider.Maximum = maxCorners;
                    MonoCornerFilterMax.Text = MonoCornerFilterSlider.Maximum.ToString();
                    StereoCornerFilterMax.Text = StereoCornerFilterSlider.Maximum.ToString();


                }
            }
        }

        // Wizard interface
        public bool CanGoBack => navParams?.calibProject != null;
        public bool CanGoNext => navParams?.calibProject != null;

        // Go to Calibration Board Settings page
        public Task GoBackAsync()
        {
            navParams?.setupRunCalibration.GoToPage(typeof(SetupRunCalibrationBoard)/*class*/, "CalibrationTarget"/*tag*/);

            return Task.CompletedTask;
        }

        // Go to Calibration Summary page
        public Task GoNextAsync()
        {
            navParams?.setupRunCalibration.GoToPage(typeof(SetupRunCalibrationSummary)/*class*/, "CalibrationSummary"/*tag*/);

            return Task.CompletedTask;
        }

        private void UpdateModeText()
        {
            if (navParams?.calibProject?.Data is not null)
            {
                var mode = navParams.calibProject.Data.Media.StereoMonoMediaSetMode;

                // Mode
                string text = mode switch
                {
                    StereoMonoMediaSetMode.MonoAndStereoMediaSet => "Mono and Stereo calibration",
                    StereoMonoMediaSetMode.StereoOnlyMediaSet => "Stereo only",
                    StereoMonoMediaSetMode.MonoPairOnlyMediaSet => "Mono Pair Only",
                    StereoMonoMediaSetMode.MonoSingleOnlyMediaSet => "Mono Single Only",
                    _ => "Mode: Not Set"
                };
                ModeSelected.Text = text;

                // Mode description
                text = mode switch
                {
                    StereoMonoMediaSetMode.MonoAndStereoMediaSet => "Media setup is for mono and stereo calibration using separate videos. Two for the stereo and one each for the left and right mono.",
                    StereoMonoMediaSetMode.StereoOnlyMediaSet => "Media setup is for stereo only, mono calibration will use the stereo videos. This is typically less accurate then having dedicated mono videos (one for left and one for the right camera).",
                    StereoMonoMediaSetMode.MonoPairOnlyMediaSet => "Media setup is for mono pair only. Stereo calibration will not calculated.",
                    StereoMonoMediaSetMode.MonoSingleOnlyMediaSet => "Media setup is for a single mono camera",
                    _ => string.Empty
                };
                ModeSelectedDescription.Text = text;
            }
            else
            {
                ModeSelected.Text = "Mode: Not Set";
            }
        }


        private void OnSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider)
            {
                if (slider == MovementFilterSlider)
                {
                    if (movementDragging is null)
                        movementDragging = false;
                    else if (!(bool)movementDragging)
                        movementDragging = true;
                    else
                        MovementFilterValue.Visibility = Visibility.Collapsed;

                    MovementFilterValue.Text = e.NewValue.ToString("F1");
                }
                else if (slider == BlurFilterSlider) BlurFilterValue.Text = e.NewValue.ToString("F1");
                else if (slider == MonoCornerFilterSlider) MonoCornerFilterValue.Text = e.NewValue.ToString("F0");
                else if (slider == StereoCornerFilterSlider) StereoCornerFilterValue.Text = e.NewValue.ToString("F0");
            }
        }

        // Not seen this fire
        private void MovementFilterSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            movementDragging = true;
            MovementFilterValue.Visibility = Visibility.Collapsed;
        }

        // Not seen this fire
        private void MovementFilterSlider_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            movementDragging = false;
            MovementFilterValue.Visibility = Visibility.Visible;
        }

        // Seen this fire
        private void MovementFilterSlider_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            movementDragging = false;
            MovementFilterValue.Visibility = Visibility.Visible;
        }
    }
}
