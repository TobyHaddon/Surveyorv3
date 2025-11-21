using Microsoft.UI.Xaml.Controls;
using WinUIEx;

namespace Surveyor
{
    public sealed partial class SetupRunCalibration : WindowEx
    {
        // Copy of the calibration project instance being edited
        private readonly CalibProject? _calibProject;
        public SetupRunCalibration(CalibProject? calibProject = null)
        {
            _calibProject = calibProject;
            InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;

            // Navigate to default page
            ContentFrame.Navigate(typeof(SetupRunCalibrationSettings), _calibProject);
            NavView.SelectedItem = NavCalibrationTarget;

            // Show teaching tip once NavigationView (and its items) are loaded
            NavView.Loaded += (_, __) =>
            {
                // If teaching tips are enabled and this tip hasn't been shown yet
                if (SettingsManagerLocal.TeachingTipsEnabled && 
                    !SettingsManagerLocal.HasTeachingTipBeenShown("CalibrationSummaryTeachingTip"))
                {
                    CalibrationSummaryTeachingTip.Target = NavCalibrationSummary;
                    DispatcherQueue.TryEnqueue(() => CalibrationSummaryTeachingTip.IsOpen = true);
                }
            };
        }
            
        private void CalibrationSummaryTeachingTip_CloseButtonClick(TeachingTip sender, object args)
        {
            SettingsManagerLocal.SetTeachingTipShown("CalibrationSummaryTeachingTip");
            sender.IsOpen = false;
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            // Close the tip when the user interacts with the nav items
            if (CalibrationSummaryTeachingTip.IsOpen)
                CalibrationSummaryTeachingTip.IsOpen = false;

            if (args.SelectedItem is NavigationViewItem item)
            {
                switch (item.Tag as string)
                {
                    case "CalibrationSettings":
                        ContentFrame.Navigate(typeof(SetupRunCalibrationSettings), _calibProject);
                        break;
                    case "CalibrationTarget":
                        
                        ContentFrame.Navigate(typeof(SetupRunCalibrationBoard), _calibProject);
                        break;
                    case "CalibrationSummary":
                        ContentFrame.Navigate(typeof(SetupRunCalibrationSummary), _calibProject);
                        break;
                }
            }
        }
    }
}
