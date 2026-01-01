//???using iText.Forms.Form.Element;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace Surveyor
{
    public sealed partial class SetupRunCalibrationSettings : Page, SetupRunCalibration.IWizardPage
    {
        private NavParams? navParams;
        // Slider dragging state
        private bool? movementDragging;
        private bool? blurDragging;
        private bool? monoCornerDragging;
        private bool? stereoCornerDragging;

        public SetupRunCalibrationSettings()
        {
            this.InitializeComponent();
        }


        public static void TransferSettingsIntoRunParams(NavParams navParams)
        {
            // Read current UI values and persist into runCalibrationParams
            var runParams = navParams.runParams;

            // Apply the action check boxes
            runParams.FindCalibrationBoardZone = navParams.FindCalibrationBoardZoneWorkingValue;
            runParams.BuildTheFrameSets = navParams.BuildTheFrameSetsWorkingValue;
            runParams.FindBestMonoFrames = navParams.FindBestMonoFramesWorkingValue;
            runParams.DoCalibrationMonoCalculations = navParams.DoCalibrationMonoCalculationsWorkingValue;
            // Stereo only actions
            if (navParams.calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                navParams.calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet)
            {
                runParams.FindBestStereoFrames = navParams.FindBestStereoFramesWorkingValue;
                runParams.DoCalibrationStereoCalculations = navParams.DoCalibrationStereoCalculationsWorkingValue;
            }
            else
            {
                runParams.FindBestStereoFrames = false;
                runParams.DoCalibrationStereoCalculations = false;
            }

            // Apply the slider values
            runParams.MovementFilterValue = navParams.MovementFilterWorkingValue;
            runParams.BlurFilterValue = navParams.BlurFilterWorkingValue;
            runParams.MonoCornersFilterValue = navParams.MonoCornersFilterWorkingValue;
            runParams.StereoCornersFilterValue = navParams.StereoCornersFilterWorkingValue;
        }

        /// <summary>
        /// User has navigated to the calibration run settings page
        /// </summary>
        /// <param name="e"></param>
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


                RunCalibrationParams runParams = navParams.runParams;
                // If the action flag isn't set then that action is necessary required 
                // but the user can optionally force the action to be redone by checking 
                // the checkbox
                // If the action flag is set then that action is required and the checkbox
                // is hidden
                FindCalibrationBoardZoneCheckBox.IsEnabled = !runParams.FindCalibrationBoardZone;
                FindCalibrationBoardZoneCheckBox.IsChecked = navParams.FindCalibrationBoardZoneWorkingValue;

                BuildFrameSetsCheckBox.IsEnabled = !runParams.BuildTheFrameSets;
                BuildFrameSetsCheckBox.IsChecked = navParams.BuildTheFrameSetsWorkingValue;

                FindBestMonoFramesCheckBox.IsEnabled = !runParams.FindBestMonoFrames;
                FindBestMonoFramesCheckBox.IsChecked = navParams.FindBestMonoFramesWorkingValue;

                DoCalibrationMonoCalculationsCheckBox.IsEnabled = !runParams.DoCalibrationMonoCalculations;
                DoCalibrationMonoCalculationsCheckBox.IsChecked = navParams.DoCalibrationMonoCalculationsWorkingValue;

                // Stereo specific actions
                if (navParams.calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                    navParams.calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet)
                {
                    FindBestStereoFramesCheckBox.Visibility = Visibility.Visible;
                    FindBestStereoFramesCheckBox.IsEnabled = !runParams.FindBestStereoFrames;
                    FindBestStereoFramesCheckBox.IsChecked = navParams.FindBestStereoFramesWorkingValue;

                    DoCalibrationStereoCalculationsCheckBox.Visibility = Visibility.Visible;
                    DoCalibrationStereoCalculationsCheckBox.IsEnabled = !runParams.DoCalibrationStereoCalculations;
                    DoCalibrationStereoCalculationsCheckBox.IsChecked = navParams.DoCalibrationStereoCalculationsWorkingValue;
                }
                else
                {
                    // Hide stereo actions
                    FindBestStereoFramesCheckBox.Visibility = Visibility.Collapsed;
                    FindBestStereoFramesCheckBox.IsChecked = false;
                    DoCalibrationStereoCalculationsCheckBox.Visibility = Visibility.Collapsed;
                    DoCalibrationStereoCalculationsCheckBox.IsChecked = false;
                }

                // All the action flags are set - hide the whole actions border
                if (runParams.FindCalibrationBoardZone &&
                    runParams.BuildTheFrameSets &&
                    runParams.FindBestMonoFrames &&
                    runParams.DoCalibrationMonoCalculations &&
                    runParams.FindBestStereoFrames &&
                    runParams.DoCalibrationStereoCalculations)
                {
                    BorderCache.Visibility = Visibility.Collapsed;
                }
                else
                {
                    BorderCache.Visibility = Visibility.Visible;
                }

                // Set slider values from project data
                if (navParams.calibProject.Data is not null)
                {
                    // Movement Slider Value
                    movementDragging = null; // This is so the first OnSliderValueChanged is treated differently to the others
                    //???navParams.MovementFilterWorkingValue = runParams.MovementFilterValue;   // Set the value that is bound to the dialog slider control

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
                    //???navParams.BlurFilterWorkingValue = runParams.BlurFilterValue;   // Set the value that is bound to the dialog slider control

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
                    //???navParams.MonoCornersFilterWorkingValue = runParams.MonoCornersFilterValue;
                    //???navParams.StereoCornersFilterWorkingValue = runParams.StereoCornersFilterValue;

                    // Corners min values
                    MonoCornerFilterMin.Text = "0";
                    StereoCornerFilterMin.Text = "0";

                    // Corners max values
                    CalibrationBoardDefinition charucoBoardDefinition = navParams.calibProject.Data.ChArUcoBoardDefinition;
                    int maxCorners = (charucoBoardDefinition.SquaresX - 1) * (charucoBoardDefinition.SquaresY - 1);
                    MonoCornerFilterSlider.Maximum = maxCorners;
                    MonoCornerFilterMax.Text = MonoCornerFilterSlider.Maximum.ToString();

                    // Stereo specific filter
                    if (navParams.calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.MonoAndStereoMediaSet ||
                        navParams.calibProject.Data.Media.StereoMonoMediaSetMode == StereoMonoMediaSetMode.StereoOnlyMediaSet)
                    {
                        StereoCornerFilterSlider.Maximum = maxCorners;
                        StereoCornerFilterMax.Text = StereoCornerFilterSlider.Maximum.ToString();

                        StereoCornerFilterLabel.Visibility = Visibility.Visible;
                        StereoCornerFilterMin.Visibility = Visibility.Visible;
                        StereoCornerFilterSlider.Visibility = Visibility.Visible;
                        StereoCornerFilterMax.Visibility = Visibility.Visible;
                        StereoCornerFilterValue.Visibility = Visibility.Visible;
                        StereoCornerFilterHelp.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        StereoCornerFilterLabel.Visibility = Visibility.Collapsed;
                        StereoCornerFilterMin.Visibility = Visibility.Collapsed;
                        StereoCornerFilterSlider.Visibility = Visibility.Collapsed;
                        StereoCornerFilterMax.Visibility = Visibility.Collapsed;
                        StereoCornerFilterValue.Visibility = Visibility.Collapsed;
                        StereoCornerFilterHelp.Visibility = Visibility.Collapsed;
                    }

                }
            }
        }


        /// <summary>
        /// User has navigated away from the calibration run settings page
        /// Check for changes and persist to runCalibrationParams
        /// </summary>
        /// <param name="e"></param>
        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);

            if (navParams is null) return;

            // This function may not be needed
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
                    StereoMonoMediaSetMode.MonoAndStereoMediaSet => "Media setup is for mono and stereo calibration using separate videos. Two for the stereo and one each for the left and right mono",
                    StereoMonoMediaSetMode.StereoOnlyMediaSet => "Media setup is for stereo only, mono calibration will also use the stereo videos. This is typically less accurate then having dedicated mono videos",
                    StereoMonoMediaSetMode.MonoPairOnlyMediaSet => "Media setup is for mono pair only. Stereo calibration will not calculated",
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
                // Guard
                if (navParams is null) return;


                switch (slider)
                {
                    case Slider s when s == MovementFilterSlider:
                        ProcessValueChanged(MovementFilterValue, ref movementDragging, e.NewValue);
                        if (navParams.IsMovementFilterChanged())
                            // If the movement filter has changed then set all the
                            // actions after the BestFrameSets checkbox
                            SetActionsDownstreamOf(BuildFrameSetsCheckBox, true/*To checked on*/);
                        break;
                    case Slider s when s == BlurFilterSlider:
                        ProcessValueChanged(BlurFilterValue, ref blurDragging, e.NewValue);
                        if (navParams.IsBlurFilterChanged())
                            // If the blur filter has changed then set all the
                            // actions after the BestFrameSets checkbox
                            SetActionsDownstreamOf(BuildFrameSetsCheckBox, true/*To checked on*/);
                        break;
                    case Slider s when s == MonoCornerFilterSlider:
                        ProcessValueChanged(MonoCornerFilterValue, ref monoCornerDragging, e.NewValue);
                        if (navParams.IsMonoCornersFilterChanged())
                            // If the mono corner filter has changed then set all the
                            // actions after the BestFrameSets checkbox
                            SetActionsDownstreamOf(BuildFrameSetsCheckBox, true/*To checked on*/);
                        break;
                    case Slider s when s == StereoCornerFilterSlider:
                        ProcessValueChanged(StereoCornerFilterValue, ref stereoCornerDragging, e.NewValue);
                        if (navParams.IsStereoCornersFilterChanged())
                            // If the stereo corner filter has changed then set all the
                            // actions after do mono calibration calculations checkbox
                            SetActionsDownstreamOf(DoCalibrationMonoCalculationsCheckBox, true/*To checked on*/);
                        break;
                }
            }

            static void ProcessValueChanged(TextBlock valueTextBlock, ref bool? dragging, double newValue)
            {
                if (dragging is null)
                    dragging = false;
                else if (!(bool)dragging)
                    dragging = true;
                else
                    valueTextBlock.Visibility = Visibility.Collapsed;

                valueTextBlock.Text = newValue.ToString("F1");
            }
        }

        // Not seen this fire
        private void OnSliderPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
            {
                switch (slider)
                {
                    case Slider s when s == MovementFilterSlider:
                        movementDragging = true;
                        MovementFilterValue.Visibility = Visibility.Collapsed;
                        break;
                    case Slider s when s == BlurFilterSlider:
                        blurDragging = true;
                        BlurFilterValue.Visibility = Visibility.Collapsed;
                        break;
                    case Slider s when s == MonoCornerFilterSlider:
                        monoCornerDragging = true;
                        MonoCornerFilterValue.Visibility = Visibility.Collapsed;
                        break;
                    case Slider s when s == StereoCornerFilterSlider:
                        stereoCornerDragging = true;
                        StereoCornerFilterValue.Visibility = Visibility.Collapsed;
                        break;
                }
            }
        }

        // Not seen this fire
        private void OnSliderPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
            {
                switch (slider)
                {
                    case Slider s when s == MovementFilterSlider:
                        movementDragging = false;
                        MovementFilterValue.Visibility = Visibility.Visible;
                        break;
                    case Slider s when s == BlurFilterSlider:
                        blurDragging = false;
                        BlurFilterValue.Visibility = Visibility.Visible;
                        break;
                    case Slider s when s == MonoCornerFilterSlider:
                        monoCornerDragging = false;
                        MonoCornerFilterValue.Visibility = Visibility.Visible;
                        break;
                    case Slider s when s == StereoCornerFilterSlider:
                        stereoCornerDragging = false;
                        StereoCornerFilterValue.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        // Seen this fire
        private void OnSliderPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
            {
                switch (slider)
                {
                    case Slider s when s == MovementFilterSlider:
                        movementDragging = false;
                        MovementFilterValue.Visibility = Visibility.Visible;
                        break;
                    case Slider s when s == BlurFilterSlider:
                        blurDragging = false;
                        BlurFilterValue.Visibility = Visibility.Visible;
                        break;
                    case Slider s when s == MonoCornerFilterSlider:
                        monoCornerDragging = false;
                        MonoCornerFilterValue.Visibility = Visibility.Visible;
                        break;
                    case Slider s when s == StereoCornerFilterSlider:
                        stereoCornerDragging = false;
                        StereoCornerFilterValue.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        /// <summary>
        /// Check other dependent settings
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnActionCheckBoxClick(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                // Guard
                if (checkBox.IsChecked is null)
                    return;

                if (checkBox.IsChecked == true)
                {
                    // If FindCalibrationBoardZone has been checked then
                    // all the downstream actions need to checked on
                    SetActionsDownstreamOf(checkBox, true/*To checked on*/);
                }
                else
                {
                    // If FindCalibrationBoardZone has been checked off
                    // then revert downstream actions to their original
                    // runParam values
                    SetActionsDownstreamOf(checkBox, null/*back to runParam value*/);
                }
            }
        }


        /// <summary>
        /// Set the downstream CheckBox to the value however if the value
        /// is null then set the downstream CheckBox to the original value 
        /// in navParams.runCalibrationParams
        /// </summary>
        /// <param name="checkBox"></param>
        /// <param name="value"></param>
        private void SetActionsDownstreamOf(CheckBox checkBox, bool? value)
        {
            bool buildFrameSetsCheckBoxApplyValue = false;
            bool findBestMonoFramesCheckBoxApplyValue = false;
            bool doCalibrationMonoCalculationsCheckBoxApplyValue = false;
            bool findBestStereoFramesCheckBoxApplyValue = false;
            bool doCalibrationStereoCalculationsCheckBoxApplyValue = false;

            bool? buildFrameSetsCheckBoxValue = null;
            bool? findBestMonoFramesCheckBoxValue = null;
            bool? doCalibrationMonoCalculationsCheckBoxValue = null;
            bool? findBestStereoFramesCheckBoxValue = null;
            bool? doCalibrationStereoCalculationsCheckBoxValue = null;
            if (checkBox == FindCalibrationBoardZoneCheckBox)
            {
                buildFrameSetsCheckBoxApplyValue = true;
                findBestMonoFramesCheckBoxApplyValue = true;
                doCalibrationMonoCalculationsCheckBoxApplyValue = true;
                findBestStereoFramesCheckBoxApplyValue = true;
                doCalibrationStereoCalculationsCheckBoxApplyValue = true;
            }
            else if (checkBox == BuildFrameSetsCheckBox)
            {
                findBestMonoFramesCheckBoxApplyValue = true;
                doCalibrationMonoCalculationsCheckBoxApplyValue = true;
                findBestStereoFramesCheckBoxApplyValue = true;
                doCalibrationStereoCalculationsCheckBoxApplyValue = true;
            }
            else if (checkBox == FindBestMonoFramesCheckBox)
            {
                doCalibrationMonoCalculationsCheckBoxApplyValue = true;
                findBestStereoFramesCheckBoxApplyValue = true;
                doCalibrationStereoCalculationsCheckBoxApplyValue = true;
            }
            else if (checkBox == DoCalibrationMonoCalculationsCheckBox)
            {
                findBestStereoFramesCheckBoxApplyValue = true;
                doCalibrationStereoCalculationsCheckBoxApplyValue = true;
            }
            else if (checkBox == FindBestStereoFramesCheckBox)
            {
                doCalibrationStereoCalculationsCheckBoxApplyValue = true;
            }
            else
            {
                return;
            }

            // Apply the values to the check boxes as required
            if (buildFrameSetsCheckBoxApplyValue)
            {
                if (value is null)
                    buildFrameSetsCheckBoxValue = navParams?.runParams.BuildTheFrameSets;
                else
                    buildFrameSetsCheckBoxValue = value;

                BuildFrameSetsCheckBox.IsChecked = buildFrameSetsCheckBoxValue;
            }

            if (findBestMonoFramesCheckBoxApplyValue)
            {
                if (value is null)
                    findBestMonoFramesCheckBoxValue = navParams?.runParams.FindBestMonoFrames;
                else
                    findBestMonoFramesCheckBoxValue = value;

                FindBestMonoFramesCheckBox.IsChecked = findBestMonoFramesCheckBoxValue;
            }

            if (doCalibrationMonoCalculationsCheckBoxApplyValue)
            {
                if (value is null)
                    doCalibrationMonoCalculationsCheckBoxValue = navParams?.runParams.DoCalibrationMonoCalculations;
                else
                    doCalibrationMonoCalculationsCheckBoxValue = value;

                DoCalibrationMonoCalculationsCheckBox.IsChecked = doCalibrationMonoCalculationsCheckBoxValue;
            }

            if (findBestStereoFramesCheckBoxApplyValue)
            {
                if (value is null)
                    findBestStereoFramesCheckBoxValue = navParams?.runParams.FindBestStereoFrames;
                else
                    findBestStereoFramesCheckBoxValue = value;

                FindBestStereoFramesCheckBox.IsChecked = findBestStereoFramesCheckBoxValue;
            }

            if (doCalibrationStereoCalculationsCheckBoxApplyValue)
            {
                if (value is null)
                    doCalibrationStereoCalculationsCheckBoxValue = navParams?.runParams.DoCalibrationStereoCalculations;
                else
                    doCalibrationStereoCalculationsCheckBoxValue = value;

                DoCalibrationStereoCalculationsCheckBox.IsChecked = doCalibrationStereoCalculationsCheckBoxValue;
            }
        }
    }
}
