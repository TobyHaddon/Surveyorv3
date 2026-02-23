//???using iText.Forms.Form.Element;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
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

        // Used to remember the previous focus control when the ControlFrameEdit is in use
        private object? controlPreviousFocus = null;


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
            navParams.calibProject.Data.CalibrationInputs.MovementFilterValue = navParams.MovementFilterWorkingValue;
            navParams.calibProject.Data.CalibrationInputs.BlurFilterValue = navParams.BlurFilterWorkingValue;
            navParams.calibProject.Data.CalibrationInputs.MonoCornersFilterValue = navParams.MonoCornersFilterWorkingValue;
            navParams.calibProject.Data.CalibrationInputs.StereoCornersFilterValue = navParams.StereoCornersFilterWorkingValue;
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
                        ProcessTextBlockValueChanged(MovementFilterValue, ref movementDragging, e.NewValue, true1DPFalseWholeNumber: true);
                        if (navParams.IsMovementFilterChanged())
                            // If the movement filter has changed then set all the
                            // actions after the BestFrameSets checkbox
                            SetActionsDownstreamOf(BuildFrameSetsCheckBox, true/*To checked on*/);
                        break;
                    case Slider s when s == BlurFilterSlider:
                        ProcessTextBlockValueChanged(BlurFilterValue, ref blurDragging, e.NewValue, true1DPFalseWholeNumber: true);
                        if (navParams.IsBlurFilterChanged())
                            // If the blur filter has changed then set all the
                            // actions after the BestFrameSets checkbox
                            SetActionsDownstreamOf(BuildFrameSetsCheckBox, true/*To checked on*/);
                        break;
                    case Slider s when s == MonoCornerFilterSlider:
                        ProcessTextBlockValueChanged(MonoCornerFilterValue, ref monoCornerDragging, e.NewValue, true1DPFalseWholeNumber: false);
                        if (navParams.IsMonoCornersFilterChanged())
                            // If the mono corner filter has changed then set all the
                            // actions after the BestFrameSets checkbox
                            SetActionsDownstreamOf(BuildFrameSetsCheckBox, true/*To checked on*/);
                        break;
                    case Slider s when s == StereoCornerFilterSlider:
                        ProcessTextBlockValueChanged(StereoCornerFilterValue, ref stereoCornerDragging, e.NewValue, true1DPFalseWholeNumber: false);
                        if (navParams.IsStereoCornersFilterChanged())
                            // If the stereo corner filter has changed then set all the
                            // actions after do mono calibration calculations checkbox
                            SetActionsDownstreamOf(DoCalibrationMonoCalculationsCheckBox, true/*To checked on*/);
                        break;
                }
            }

            static void ProcessTextBlockValueChanged(TextBlock valueTextBlock, ref bool? dragging, double newValue, bool true1DPFalseWholeNumber)
            {
                if (dragging is null)
                    dragging = false;
                else if (!(bool)dragging)
                    dragging = true;
                else
                    valueTextBlock.Visibility = Visibility.Collapsed;

                if (true1DPFalseWholeNumber)
                    valueTextBlock.Text = newValue.ToString("F1");
                else
                    valueTextBlock.Text = newValue.ToString("F0");
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
        /// This used to allow the user to manual go to a frame
        /// This method ensure only numbers are entered into the frame edit box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void TextBox_AllowPositiveTo1DP(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            string input = args.NewText;

            // Define the valid patterns:
            // Matches: 10 10.1 0.9 .9
            // Doesn’t match: -1 10. 10.12.abc

            var validPattern = @"^(?:\d+(?:\.\d)?|\.\d)$";

            args.Cancel = !Regex.IsMatch(input, validPattern, RegexOptions.IgnoreCase);
        }


        /// <summary>
        /// This used to allow the user to manual go to a frame
        /// This method ensure only numbers are entered into the frame edit box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void TextBox_AllowPositiveWholeNumber(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            string input = args.NewText;

            // Define the valid patterns:
            var validPattern = @"^(?:0|[1-9]\d*)$";

            args.Cancel = !Regex.IsMatch(input, validPattern, RegexOptions.IgnoreCase);
        }


        /// <summary>
        /// Used to allow Movement Filter value to be edited
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MovementFilterValue_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SliderTextBlockEditTapped(MovementFilterValue, MovementFilterValueEdit);
        }


        /// <summary>
        /// Used to allow Movement Filter value to be edited
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>        
        private void MovementFilterValueEdit_KeyDown(object sender, KeyRoutedEventArgs e) => _ = SliderTextBox_KeyDownAsync(e, MovementFilterSlider, MovementFilterValue, MovementFilterValueEdit, controlPreviousFocus);


        /// <summary>
        /// Used to allow Movement Filter value to be edited
        /// If the user clicks away from the text box control then the new slider value is accepted 
        /// (like pressing ENTER)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MovementFilterValueEdit_LostFocus(object sender, RoutedEventArgs e) => _ = SliderTextBox_LostFocusAsync(MovementFilterSlider, MovementFilterValue, MovementFilterValueEdit, controlPreviousFocus);

        /// <summary>
        /// Used to allow Blur Filter value to be edited
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BlurFilterValue_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SliderTextBlockEditTapped(BlurFilterValue, BlurFilterValueEdit);
        }


        /// <summary>
        /// Used to allow Blur Filter value to be edited
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>        
        private void BlurFilterValueEdit_KeyDown(object sender, KeyRoutedEventArgs e) => _ = SliderTextBox_KeyDownAsync(e, BlurFilterSlider, BlurFilterValue, BlurFilterValueEdit, controlPreviousFocus);


        /// <summary>
        /// Used to allow Blur Filter value to be edited
        /// If the user clicks away from the text box control then the new slider value is accepted 
        /// (like pressing ENTER)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BlurFilterValueEdit_LostFocus(object sender, RoutedEventArgs e) => _ = SliderTextBox_LostFocusAsync(BlurFilterSlider, BlurFilterValue, BlurFilterValueEdit, controlPreviousFocus);


        /// <summary>
        /// Used to allow Mono Corner Filter value to be edited
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MonoCornerFilterValue_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SliderTextBlockEditTapped(MonoCornerFilterValue, MonoCornerFilterValueEdit);
        }


        /// <summary>
        /// Used to allow Mono Corner Filter value to be edited
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>        
        private void MonoCornerFilterValueEdit_KeyDown(object sender, KeyRoutedEventArgs e) => _ = SliderTextBox_KeyDownAsync(e, MonoCornerFilterSlider, MonoCornerFilterValue, MonoCornerFilterValueEdit, controlPreviousFocus);


        /// <summary>
        /// Used to allow Mono Corner Filter value to be edited
        /// If the user clicks away from the text box control then the new slider value is accepted 
        /// (like pressing ENTER)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MonoCornerFilterValueEdit_LostFocus(object sender, RoutedEventArgs e) => _ = SliderTextBox_LostFocusAsync(MonoCornerFilterSlider, MonoCornerFilterValue, MonoCornerFilterValueEdit, controlPreviousFocus);


        /// <summary>
        /// Used to allow Stereo Corner Filter value to be edited
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StereoCornerFilterValue_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SliderTextBlockEditTapped(StereoCornerFilterValue, StereoCornerFilterValueEdit);
        }


        /// <summary>
        /// Used to allow Stereo Corner Filter value to be edited
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>        
        private void StereoCornerFilterValueEdit_KeyDown(object sender, KeyRoutedEventArgs e) => _ = SliderTextBox_KeyDownAsync(e, StereoCornerFilterSlider, StereoCornerFilterValue, StereoCornerFilterValueEdit, controlPreviousFocus);


        /// <summary>
        /// Used to allow Stereo Corner Filter value to be edited
        /// If the user clicks away from the text box control then the new slider value is accepted 
        /// (like pressing ENTER)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StereoCornerFilterValueEdit_LostFocus(object sender, RoutedEventArgs e) => _ = SliderTextBox_LostFocusAsync(StereoCornerFilterSlider, StereoCornerFilterValue, StereoCornerFilterValueEdit, controlPreviousFocus);


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


        /// <summary>
        /// A text block paired with a Slider control has been tapped (clicked)
        /// Which indicates the user wants to edit the slide value directly
        /// (instead of via sliding the slider)
        /// </summary>
        private void SliderTextBlockEditTapped(TextBlock controlText, TextBox controlEdit)
        {
            // Get and remember where the current focus is
            controlPreviousFocus = FocusManager.GetFocusedElement(this.Content.XamlRoot);

            // Make the frame edit box visible and the frame text box invisible
            controlText.Visibility = Visibility.Collapsed;
            controlEdit.Visibility = Visibility.Visible;


            // Load the edit box with the same frame number as the text block
            controlEdit.Text = controlText.Text;

            // Set focus to the edit box and select all the text
            controlEdit.Focus(FocusState.Programmatic);
            controlEdit.SelectAll();
        }


        /// <summary>
        /// This used to allow the user to manual go to a frame
        /// The method detect ESC to cancel the operation and ENTER to accept the new frame number
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>        
        private async Task SliderTextBox_KeyDownAsync(KeyRoutedEventArgs e, Slider slider, TextBlock controlText, TextBox controlEdit, Object? controlPreviousFocus)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                // Handle ESC key press
                controlEdit.Visibility = Visibility.Collapsed;
                e.Handled = true;

                await ControlEditCollapsedAndReturnFocusAsync(controlText, controlEdit, controlPreviousFocus);
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                // Handle ENTER key press
                e.Handled = true;

                // Set the slider value
                ProcessSliderTextBoxEdit(controlEdit, slider);

                // Hide the ControlFrameEdit control and restore the original focus
                await ControlEditCollapsedAndReturnFocusAsync(controlText, controlEdit, controlPreviousFocus);
            }
        }


        /// <summary>
        /// This used to allow the user to directly manually edit a slider value
        /// If the user clicks away from the text box control then the new slider value is accepted 
        /// (like pressing ENTER)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async Task SliderTextBox_LostFocusAsync(Slider slider, TextBlock controlText, TextBox controlEdit, Object? controlPreviousFocus)
        {
            // Check if the focus was lost programmatically or by the user clicking away
            if (controlText.Visibility == Visibility.Collapsed)
            {
                // Get the new frame number from the TextBox and request a jump to that frame
                ProcessSliderTextBoxEdit(controlEdit, slider);

                // Hide the text box control and restore the original focus
                await ControlEditCollapsedAndReturnFocusAsync(controlText, controlEdit, controlPreviousFocus);
            }
        }


        /// <summary>
        /// Used to get any value TextBox and set the slider value
        /// </summary>
        private void ProcessSliderTextBoxEdit(TextBox ControlEdit, Slider slider)
        {
            if (double.TryParse(ControlEdit.Text, out double value) == true)
            {
                slider.Value = value;

                // Guard
                if (navParams is null) return;  

                switch (ControlEdit)
                {
                    case TextBox tb when tb == MovementFilterValueEdit:
                        if (navParams.IsMovementFilterChanged())
                            // If the movement filter has changed then set all the
                            // actions after the BestFrameSets checkbox
                            SetActionsDownstreamOf(BuildFrameSetsCheckBox, true/*To checked on*/);
                        break;
                    case TextBox tb when tb == BlurFilterValueEdit:
                        if (navParams.IsBlurFilterChanged())
                            // If the blur filter has changed then set all the
                            // actions after the BestFrameSets checkbox
                            SetActionsDownstreamOf(BuildFrameSetsCheckBox, true/*To checked on*/);
                        break;
                    case TextBox tb when tb == MonoCornerFilterValueEdit:
                        if (navParams.IsMonoCornersFilterChanged())
                            // If the mono corner filter has changed then set all the
                            // actions after the BestFrameSets checkbox
                            SetActionsDownstreamOf(BuildFrameSetsCheckBox, true/*To checked on*/);
                        break;
                    case TextBox tb when tb == StereoCornerFilterValueEdit:
                        if (navParams.IsStereoCornersFilterChanged())
                            // If the stereo corner filter has changed then set all the
                            // actions after do mono calibration calculations checkbox
                            SetActionsDownstreamOf(DoCalibrationMonoCalculationsCheckBox, true/*To checked on*/);
                        break;
                }
            }
        }


        /// <summary>
        /// Called to collapsed the TextBox and return the focus to
        /// wherever it was before
        /// </summary>
        private async static Task ControlEditCollapsedAndReturnFocusAsync(TextBlock ControlText, TextBox ControlEdit, Object? controlPreviousFocus)
        {
            ControlText.Visibility = Visibility.Visible;
            ControlEdit.Visibility = Visibility.Collapsed;

            if (controlPreviousFocus is not null)
            {
                await FocusManager.TryFocusAsync((DependencyObject)controlPreviousFocus, FocusState.Programmatic);
            }
        }



    }
}
