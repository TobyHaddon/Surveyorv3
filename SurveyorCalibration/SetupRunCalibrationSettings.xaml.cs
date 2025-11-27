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
        private NavParams? navParams;

        public SetupRunCalibrationSettings()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            navParams = e.Parameter as NavParams;
            
            UpdateModeText();


            SetFrameSetsCacheAvailability();
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

        /// <summary>
        /// Set the checkbox in the SetupRunCalibrationSettings page
        /// </summary>
        private void SetFrameSetsCacheAvailability()
        {
            if (navParams?.mainWindow is null) return;

            // Check if cached results file exists and setup UI controls accordingly
            bool cacheAvailable = navParams.mainWindow.CachedResultsFileExists();
            BorderCache.Visibility = cacheAvailable ? Visibility.Visible : Visibility.Collapsed;
            ReuseCacheCheckBox.IsChecked = cacheAvailable;
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
            Frame?.Navigate(typeof(SetupRunCalibrationBoard), navParams);

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
            Frame?.Navigate(typeof(SetupRunCalibrationSummary), navParams);

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
