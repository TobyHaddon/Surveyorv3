using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationSettings : Page
    {
        private CalibProject? _calibProject;

        public SetupRunCalibrationSettings()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _calibProject = e.Parameter as CalibProject;
            UpdateModeText();
        }

        private void UpdateModeText()
        {
            if (_calibProject?.Data != null)
            {
                var mode = _calibProject.Data.Media.StereoMonoMediaSetMode;

                // Mode
                string text = mode switch
                {
                    User_Controls.CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet => "Mono and Stereo calibration",
                    User_Controls.CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet => "Stereo only",
                    User_Controls.CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet => "Mono Pair Only",
                    User_Controls.CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet => "Mono Single Only",
                    _ => "Mode: Not Set"
                };
                ModeSelected.Text = text;

                // Mode description
                text = mode switch
                {
                    User_Controls.CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet => "Media setup is for mono and stereo calibration using separate videos. Two for the stereo and one each for the left and right mono.",
                    User_Controls.CalibInfoAndMedia.StereoMonoMediaSetMode.StereoOnlyMediaSet => "Media setup is for stereo only, mono calibration will use the stereo videos. This is typically less accurate then having dedicated mono videos (one for left and one for the right camera).",
                    User_Controls.CalibInfoAndMedia.StereoMonoMediaSetMode.MonoPairOnlyMediaSet => "Media setup is for mono pair only. Stereo calibration will not calculated.",
                    User_Controls.CalibInfoAndMedia.StereoMonoMediaSetMode.MonoSingleOnlyMediaSet => "Media setup is for a single mono camera",
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

        private void SetupRunCalibrationSettingsBack_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to settings page, passing the current CalibProject (can be null)
            Frame?.Navigate(typeof(SetupRunCalibrationBoard), _calibProject);

            // Update NavView selection to "Calibration Settings"
            var navView = FindParentNavigationView();
            if (navView != null)
            {
                var targetItem = navView.MenuItems
                                        .OfType<NavigationViewItem>()
                                        .FirstOrDefault(i => (i.Tag as string) == "CalibrationTarget");
                if (targetItem != null && (NavigationViewItem)navView.SelectedItem != targetItem)
                {
                    navView.SelectedItem = targetItem;
                }
            }
        }

        private void SetupRunCalibrationSettingsNext_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to settings page, passing the current CalibProject (can be null)
            Frame?.Navigate(typeof(SetupRunCalibrationSummary), _calibProject);

            // Update NavView selection to "Calibration Settings"
            var navView = FindParentNavigationView();
            if (navView != null)
            {
                var targetItem = navView.MenuItems
                                        .OfType<NavigationViewItem>()
                                        .FirstOrDefault(i => (i.Tag as string) == "CalibrationSummary");
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
