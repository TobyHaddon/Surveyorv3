using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationBoard : Page
    {
        private CalibProject? _calibProject;

        public SetupRunCalibrationBoard()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _calibProject = e.Parameter as CalibProject;

            this.CalibrationBoardSettingsControl.SetupForProjectSettingWindow(_calibProject);
        }

        private void SetupRunCalibrationBoardNext_Click(object sender, RoutedEventArgs e)
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