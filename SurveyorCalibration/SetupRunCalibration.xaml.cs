using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Helper;
using System.Threading.Tasks;
using WinUIEx;

namespace Surveyor
{
    public class NavParams(MainWindow? _mainWindow, CalibProject? _calibProject)
    {
        public MainWindow? mainWindow = _mainWindow;
        public CalibProject? calibProject = _calibProject;
    }

    public sealed partial class SetupRunCalibration : WindowEx
    {
        // Copy of the calibration project instance being edited
        private readonly NavParams? navParams;

        public SetupRunCalibration(MainWindow _mainWindow, CalibProject? _calibProject = null)
        {
            navParams = new(_mainWindow, _calibProject);

            InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;

            WindowHelper.TrackWindow(this);

            // Hook navigated to keep footer buttons in sync
            ContentFrame.Navigated += (_, __) => UpdateFooterButtons();


            // Navigate to default page
            ContentFrame.Navigate(typeof(SetupRunCalibrationBoard), navParams);
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

        // Allow pages to request a refresh when internal state changes
        internal void RequestFooterButtonsRefresh() => UpdateFooterButtons();

        private void CalibrationSummaryTeachingTip_CloseButtonClick(TeachingTip sender, object args)
        {
            SettingsManagerLocal.SetTeachingTipShown("CalibrationSummaryTeachingTip");
            sender.IsOpen = false;
        }

        public interface IWizardPage
        {
            bool CanGoBack { get; }
            bool CanGoNext { get; }
            Task GoBackAsync();
            Task GoNextAsync();
        }

        private void UpdateFooterButtons()
        {
            if (ContentFrame.Content is IWizardPage wiz)
            {
                BackBtn.IsEnabled = wiz.CanGoBack;
                NextBtn.IsEnabled = wiz.CanGoNext;
            }
            else
            {
                BackBtn.IsEnabled = false;
                NextBtn.IsEnabled = false;
            }
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e) => _ = BackBtnClickAsync();
        private async Task BackBtnClickAsync()
        {
            if (ContentFrame.Content is IWizardPage wiz && wiz.CanGoBack)
                await wiz.GoBackAsync();
        }


        private void NextBtn_Click(object sender, RoutedEventArgs e) => _ = NextBtnClickAsync();
        private async Task NextBtnClickAsync()
        {
            if (ContentFrame.Content is IWizardPage wiz && wiz.CanGoNext)
                await wiz.GoNextAsync();
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            UpdateFooterButtons();

            // Close the tip when the user interacts with the nav items
            if (CalibrationSummaryTeachingTip.IsOpen)
                CalibrationSummaryTeachingTip.IsOpen = false;

            if (args.SelectedItem is NavigationViewItem item)
            {
                switch (item.Tag as string)
                {
                    case "CalibrationTarget":
                        ContentFrame.Navigate(typeof(SetupRunCalibrationBoard), navParams);
                        break;
                    case "CalibrationSettings":
                        ContentFrame.Navigate(typeof(SetupRunCalibrationSettings), navParams);
                        break;
                    case "CalibrationSummary":
                        ContentFrame.Navigate(typeof(SetupRunCalibrationSummary), navParams);
                        break;
                }
            }
        }
    }
}
