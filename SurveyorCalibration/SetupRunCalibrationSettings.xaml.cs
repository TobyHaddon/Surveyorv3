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
                ReuseCacheCheckBox.Visibility = navParams.runCalibrationParams.UseFrameSetCache ? Visibility.Visible : Visibility.Collapsed;
                BorderCache.Visibility = navParams.runCalibrationParams.UseFrameSetCache ? Visibility.Visible : Visibility.Collapsed;

                // Set slider values from project data
                if (navParams.calibProject.Data is not null)
                {
                    var settings = navParams.runCalibrationParams;
                    MovementFilterSlider.Value = settings.MovementFilterValue;
                    BlurFilterSlider.Value = settings.BlurFilterValue;
                    MonoCornerFilterSlider.Value = settings.MonoCornersFilterValue;
                    StereoCornerFilterSlider.Value = settings.StereoCornersFilterValue;
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
                if (slider == MovementFilterSlider) MovementFilterValue.Text = e.NewValue.ToString("F0");
                else if (slider == BlurFilterSlider) BlurFilterValue.Text = e.NewValue.ToString("F0");
                else if (slider == MonoCornerFilterSlider) MonoCornerFilterValue.Text = e.NewValue.ToString("F0");
                else if (slider == StereoCornerFilterSlider) StereoCornerFilterValue.Text = e.NewValue.ToString("F0");
            }
        }
    }
}
