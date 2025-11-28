using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Surveyor.Helper;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationBoard : Page, SetupRunCalibration.IWizardPage
    {
        private NavParams? navParams;

        public SetupRunCalibrationBoard()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            navParams = e.Parameter as NavParams;

            if (navParams is not null)
            {
                // Setup CalibrationBoardSettingsControl
                CalibrationBoardSettingsControl.SetupForProjectSettingWindow(navParams.calibProject);

                // Refresh wizard buttons
                navParams.setupRunCalibration.RequestFooterButtonsRefresh();
            }
        }

        // Wizard interface
        public bool CanGoBack => navParams?.calibProject != null;
        public bool CanGoNext => navParams?.calibProject != null;

        public Task GoBackAsync() => Task.CompletedTask;

        // Go to Calibration Settings page
        public Task GoNextAsync()
        {
            navParams?.setupRunCalibration.GoToPage(typeof(SetupRunCalibrationSettings), "CalibrationSettings");

            return Task.CompletedTask;
        }
    }
}