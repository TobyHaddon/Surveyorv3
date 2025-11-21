using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationBoard : Page
    {
        private CalibProject? _calibProject;
        public SetupRunCalibrationBoard()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _calibProject = e.Parameter as CalibProject;
           
        }
    }
}
