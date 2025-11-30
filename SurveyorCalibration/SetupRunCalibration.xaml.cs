using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Helper;
using System.Linq;
using System.Threading.Tasks;
using WinUIEx;
using System;

namespace Surveyor
{
    public class NavParams(MainWindow _mainWindow, CalibProject _calibProject, SetupRunCalibration _setupRunCalibration, RunCalibrationParams _runCalibrationParams)
    {
        // Used by Run Calibration wizard pages to access shared data
        public MainWindow mainWindow = _mainWindow;
        public CalibProject calibProject = _calibProject;
        public SetupRunCalibration setupRunCalibration = _setupRunCalibration;

        // Parameters for running calibration
        public RunCalibrationParams runCalibrationParams = _runCalibrationParams;
    }

    public sealed partial class SetupRunCalibration : WindowEx
    {
        // Copy of the calibration project instance being edited
        private readonly NavParams? navParams;

        public SetupRunCalibration(MainWindow _mainWindow, CalibProject _calibProject, RunCalibrationParams _runCalibrationParams)
        {
            // Prepare navigation parameters
            navParams = new(_mainWindow, _calibProject, this, _runCalibrationParams);

            // Restore the saved window state
            PersistenceId = "SetupRunCalibrationWindow";

            InitializeComponent();
            ExtendsContentIntoTitleBar = true;

            WindowHelper.TrackWindow(this);

            // Refresh after navigation completes
            ContentFrame.Navigated += (_, __) => UpdateFooterButtons();

            // Navigate first, then select the nav item
            ContentFrame.Navigate(typeof(SetupRunCalibrationBoard), navParams);
            NavView.SelectedItem = NavCalibrationTarget;

            NavView.Loaded += (_, __) =>
            {
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
            // Do not refresh here before navigation; page state isn’t ready yet
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

            // If you still want a refresh here, do it AFTER navigation
            UpdateFooterButtons();
        }

        public void GoToPage(Type pageType, string pageTag)
        {
            // Navigate the content frame with existing nav params
            ContentFrame.Navigate(pageType, navParams);

            // Update NavView selection using the provided tag
            var targetItem = NavView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(i => (i.Tag as string) == pageTag);

            if (targetItem != null)
            {
                NavView.SelectedItem = targetItem;
            }

            // Refresh footer buttons (Next/Back) after navigation
            RequestFooterButtonsRefresh();
        }

    }
}
