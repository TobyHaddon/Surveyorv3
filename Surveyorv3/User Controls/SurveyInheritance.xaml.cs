using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Threading.Tasks;


namespace Surveyor.User_Controls
{
    public sealed partial class SurveyInheritance : UserControl
    {
        private MainWindow? mainWindow = null;
        private Reporter? report = null;
        Survey? survey = null;
        string surveyInheritingFromFileSpec = string.Empty;


        private ContentDialog? dialog = null;
        private Survey? surveyInheritanceSource = null;

        public SurveyInheritance()
        {
            this.InitializeComponent();
        }


        /// <summary>
        /// Allows the user in inherit Calibration data and Survey rules data from an
        /// existing survey file
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <param name="editExisting"></param>
        /// <returns>true is the speciesInfo parameter has been changed</returns>
        public async Task<bool> InheritFromSurveyAsync(MainWindow _mainWindow, Reporter _report, Survey _survey, string _surveyInheritingFromFileSpec)
        {
            bool ret = false;

            // Remember
            mainWindow = _mainWindow;
            report = _report;
            survey = _survey;
            surveyInheritingFromFileSpec = _surveyInheritingFromFileSpec;

            bool doesInheritanceSourceHaveRules = false;
            bool doesInheritanceSourceHaveCalibration = false;
            bool doesInheritanceSourceHaveSpeciesList = false;
            bool inheritanceSourceSpeciesListFound = false;

            string surveyInheritingFromFileName = Path.GetFileName(surveyInheritingFromFileSpec);

            ClearControls();

            // Load the source survey
            surveyInheritanceSource = new(report);


            int surveyInheritanceSourceLoaded = await surveyInheritanceSource.SurveyLoadAsync(surveyInheritingFromFileSpec);

            try
            {

                if (surveyInheritanceSourceLoaded == 0)
                {
                    // Check if rules exist
                    int rulesCount = CheckAtLeastOneRulePresent(surveyInheritanceSource);
                    if (surveyInheritanceSource.Data.SurveyRules.SurveyRulesActive == true &&
                        rulesCount > 0)
                    {
                        doesInheritanceSourceHaveRules = true;
                    }

                    // Check if calibration data exists
                    int countCalibration = surveyInheritanceSource.Data.Calibration.CalibrationDataList.Count;
                    if (countCalibration > 0)
                    {
                        doesInheritanceSourceHaveCalibration = true;
                    }

                    // Check if the species list exists
                    if (!string.IsNullOrEmpty(surveyInheritanceSource.Data.Info.SurveySpeciesListName))
                    {
                        doesInheritanceSourceHaveSpeciesList = true;
                        inheritanceSourceSpeciesListFound = !SpeciesCodeList.IsSpeciesListPresent(surveyInheritanceSource.Data.Info.SurveySpeciesListName);
                    }
                }

                // Create the dialog
                dialog = new()
                {
                    Content = this,
                    Title = "Inherit From Existing Survey",
                    CloseButtonText = "Cancel",
                    PrimaryButtonText = "Inherit",                   
                    XamlRoot = mainWindow.Content.XamlRoot  // Set the XamlRoot property
                };

                // Setup the dialog test
                if (!doesInheritanceSourceHaveRules && !doesInheritanceSourceHaveCalibration && !doesInheritanceSourceHaveSpeciesList)
                {
                    dialog.IsPrimaryButtonEnabled = false;

                    IneritFrom.Text = $"This are no survey rules, calibration or species list information in {surveyInheritingFromFileName}, so there is nothing to inherit!";
                    InheritRulesCheckBox.IsEnabled = false;
                    InheritCalibrationCheckBox.IsEnabled = false;
                    InheritSpeciesListCheckBox.IsEnabled = false;
                    InheritSpeciesListName.Text = "";
                }
                else
                {
                    dialog.IsPrimaryButtonEnabled = true;

                    IneritFrom.Text = $"{surveyInheritingFromFileName} has the following inheritable information:";

                    // Make the Survey Rule checkbox visible and default to checked
                    InheritRulesCheckBox.IsEnabled = doesInheritanceSourceHaveRules;
                    InheritRulesCheckBox.IsChecked = doesInheritanceSourceHaveRules;

                    // Make the Calibration Data checkbox visible and default to checked
                    InheritCalibrationCheckBox.IsEnabled = doesInheritanceSourceHaveCalibration;
                    InheritCalibrationCheckBox.IsChecked = doesInheritanceSourceHaveCalibration;


                    if (inheritanceSourceSpeciesListFound)
                    {
                        // Make the Species List checkbox visible and default to checked
                        InheritSpeciesListCheckBox.IsEnabled = doesInheritanceSourceHaveSpeciesList;
                        InheritSpeciesListCheckBox.IsChecked = doesInheritanceSourceHaveSpeciesList;
                        InheritSpeciesListName.Text = $"({surveyInheritanceSource.Data.Info.SurveySpeciesListName})";
                    }
                    else
                    {                         
                        // If the species list is not found, disable the checkbox and show a warning
                        InheritSpeciesListCheckBox.IsEnabled = false;
                        InheritSpeciesListCheckBox.IsChecked = false;
                        InheritSpeciesListName.Text = $"(Species list {surveyInheritanceSource.Data.Info.SurveySpeciesListName} not found)";
                    }
                }


                // Setup an open dialog handler
                dialog.Opened += Dialog_Opened;


                

                // Show the dialog and handle the response
                var result = await dialog.ShowAsync();

                // Check if the Add button pressed
                if (result == ContentDialogResult.Primary)
                {
                    if (InheritRulesCheckBox.IsChecked == true)
                    {
                        // Safely copies the survey rules from the source survey
                        survey.Data.SurveyRules.CopyFrom(surveyInheritanceSource.Data.SurveyRules);
                        surveyInheritanceSource.Data.SurveyRules.SurveyRulesInherited = surveyInheritingFromFileName;
                    }

                    if (InheritCalibrationCheckBox.IsChecked == true)
                    {
                        // Safely copies the calibration data from the source survey
                        survey.Data.Calibration.CopyFrom(surveyInheritanceSource.Data.Calibration);
                        survey.Data.Calibration.CalibrationInherited = surveyInheritingFromFileName;
                    }

                    if (InheritSpeciesListCheckBox.IsChecked == true)
                    {
                        // Safely copies the species list name from the source survey
                        survey.Data.Info.SurveySpeciesListName = surveyInheritanceSource.Data.Info.SurveySpeciesListName;                     
                    }

                    ret = true;
                }

                ClearControls();
                dialog.Content = null;  // Detach the content after the dialog is closed
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during the inheritance process
                report?.Error("", $"InheritFromSurvey: An error occurred while inheriting from survey {surveyInheritingFromFileSpec}: {ex.Message}");
            }
            finally
            {
                await surveyInheritanceSource.SurveyCloseAsync();
            }


            return ret;
        }


        ///
        /// Events
        /// 


        /// <summary>
        /// Event handler called when the dialog is opened
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            // Handle the dialog being opened
            EnableButtons();

            // Display data validation text
            EntryFieldsValid(true/*report*/);
        }



        ///
        /// PRIVATE
        ///

        /// <summary>
        /// Clear the UI controls
        /// </summary>
        private void ClearControls()
        {
            // Clear control values


            // Clear ItemsSource bindings

            // Clear image list (bound to xaml)


            // Clear source credit and genus/species  (bound to xaml)

            // Clear environment, distribution and size (bound to xaml)            

        }


        /// <summary>
        /// Check if the 'Inherit' button should be enabled
        /// </summary>
        private void EnableButtons()
        {
            if (dialog is not null)
            {
                if (InheritRulesCheckBox.IsChecked == true || InheritCalibrationCheckBox.IsChecked == true)
                {
                    dialog.IsPrimaryButtonEnabled = true;
                }
            }
        }


        /// <summary>
        /// Counts the number of rules setup
        /// </summary>
        /// <param name="survey"></param>
        /// <param name="ruleCount"></param>
        /// <returns></returns>
        private static int CheckAtLeastOneRulePresent(Survey survey)
        {
            int ruleCount = 0;

            // Check if any of the rules are present
            SurveyRulesData surveyRulesData = survey.Data.SurveyRules.SurveyRulesData;

            // Check if the range rule is setup
            if (surveyRulesData.RangeRuleActive && /*surveyRulesData.RangeMin != 0.0 ignore zero&&*/ surveyRulesData.RangeMax != 0.0)
            {
                ruleCount++;
            }

            // Check if the RMS rule is setup
            if (surveyRulesData.RMSRuleActive && surveyRulesData.RMSMax != 0.0)
            {
                ruleCount++;
            }

            // Check if the horizontal range rule is setup
            if (surveyRulesData.HorizontalRangeRuleActive && surveyRulesData.HorizontalRangeLeft != 0.0 && surveyRulesData.HorizontalRangeRight != 0.0)
            {
                ruleCount++;
            }

            // Check if the vertical range rule is setup
            if (surveyRulesData.VerticalRangeRuleActive && surveyRulesData.VerticalRangeTop != 0.0 && surveyRulesData.VerticalRangeBottom != 0.0)
            {
                ruleCount++;
            }

            return ruleCount;
        }


        /// <summary>
        /// Called when anything change to test the validity of the survey information and media
        /// This is also shows on the users control which fields are invalid
        /// </summary>
        /// <returns></returns>
        /// 
        enum EntryFieldsValidReturn
        {
            Invalid,
            Valid,
            Warning
        }
        private EntryFieldsValidReturn EntryFieldsValid(bool reportIssues)
        {
            EntryFieldsValidReturn ret = EntryFieldsValidReturn.Valid;

            bool mediaGoProSNMatch = true;
            bool mediaSameResolution = true;   // To Do if necessary
            bool mediaSameFrameRate = true;   // To Do if necessary

            if (survey is not null && surveyInheritanceSource is not null)
            {
                // Get and remember left & right InheritanceSource GoPro serial number
                string leftInheritanceSourceCameraID = surveyInheritanceSource.Data.Media.LeftCameraID;
                string rightInheritanceSourceCameraID = surveyInheritanceSource.Data.Media.RightCameraID;


                // Get the left & right media from GoPro serial number from the target survey
                string leftTargetCameraID = survey.Data.Media.LeftCameraID;
                string rightTargetCameraID = survey.Data.Media.RightCameraID;

                // Check if the left source and target are from the same GoPro
                bool sameGoProLeftMedia = string.Compare(leftInheritanceSourceCameraID, leftTargetCameraID, true) == 0;

                // Check if the right source and target are from the same GoPro
                bool sameGoProRightMedia = string.Compare(rightInheritanceSourceCameraID, rightTargetCameraID, true) == 0;

                // Report on the status of the GoPro serial numbers in the media set
                string mediaGoProSNMatchWarningText = "";
                string mediaGoProSNMatchWarningToolTip = "";

                if (!sameGoProLeftMedia && !sameGoProRightMedia)
                {
                    mediaGoProSNMatchWarningText = "Inheritance calibration data is from a different set of GoPro Cameras";
                    mediaGoProSNMatchWarningToolTip = "The inheritance survey has calibration data generated from different GoPro Cameras to the cameras used in the new survey.";
                    mediaGoProSNMatch = false;

                    if (reportIssues)
                        report?.Warning("", $"The media files for survey {survey.Data.Info.SurveyFileName} are not from the same GoPro cameras as the inheritance survey {surveyInheritanceSource.Data.Info.SurveyFileName}, different on both the left and the right side");
                }
                else if (sameGoProLeftMedia && !sameGoProRightMedia)
                {
                    mediaGoProSNMatchWarningText = "The right camera inheritance calibration data is from a different GoPro Camera";
                    mediaGoProSNMatchWarningToolTip = "The inheritance survey has calibration data generated from different right side GoPro Camera to the camera used in the new survey.";
                    mediaGoProSNMatch = false;

                    if (reportIssues)
                        report?.Warning("Right", $"The right side media files for survey {survey.Data.Info.SurveyFileName} are not from the same GoPro as the inheritance survey {surveyInheritanceSource.Data.Info.SurveyFileName}");
                }
                else if (!sameGoProLeftMedia && sameGoProRightMedia)
                {
                    mediaGoProSNMatchWarningText = "The left camera inheritance calibration data is from a different GoPro Camera";
                    mediaGoProSNMatchWarningToolTip = "The inheritance survey has calibration data generated from different left side GoPro Camera to the camera used in the new survey.";
                    mediaGoProSNMatch = false;

                    if (reportIssues)
                        report?.Warning("Left", $"The left side media files for survey {survey.Data.Info.SurveyFileName} are not from the same GoPro as the inheritance survey {surveyInheritanceSource.Data.Info.SurveyFileName}");
                }


                if (!mediaGoProSNMatch)
                {
                    SetValidationText(false/*invalid*/, SurveyGoProMatchPanel, SurveyGoProMatchGlyph, SurveyGoProMatchValidationText, mediaGoProSNMatchWarningText, mediaGoProSNMatchWarningToolTip);
                }
                else
                {
                     SetValidationText(true/*valid*/, SurveyGoProMatchPanel, SurveyGoProMatchGlyph, SurveyGoProMatchValidationText, "GoPro serial numbers match", "");              
                }


                //// Check if all the media has the same resolution
                //mediaSameResolution = CheckAllMediaResolutionAreTheSame();
                //if (!mediaSameResolution)
                //{
                //    SetValidationText(false/*invalid*/, SurveyResolutionMatchPanel, SurveyResolutionMatchGlyph, SurveyResolutionMatchValidationText, "All media files need have the same frame resolution", "");

                //    if (reportIssues)
                //        report?.Warning("", $"The media files for survey {surveyCode} are not all of the same resolution");
                //}
                //else
                //{
                //    SetValidationText(true/*valid*/, SurveyResolutionMatchPanel, SurveyResolutionMatchGlyph, SurveyResolutionMatchValidationText, "All media files have the same frame resolution", "");
                //}



                // Return Invalid if any invalid data
                if (!mediaGoProSNMatch || !mediaSameResolution || !mediaSameFrameRate)
                    ret = EntryFieldsValidReturn.Invalid;



                // Should we enable to OK button if we are inside a ContentDialog
                if (ret == EntryFieldsValidReturn.Valid || ret == EntryFieldsValidReturn.Warning)
                    dialog!.IsPrimaryButtonEnabled = true;
                else
                    dialog!.IsPrimaryButtonEnabled = false;

            }
            return ret;
        }


        /// <summary>
        /// Called to set the validation test and icon status
        /// </summary>
        /// <param name="validTRUEInvalidFALSE"></param>
        /// <param name="glyph"></param>
        /// <param name="validationText"></param>
        /// <param name="text"></param>
        private static void SetValidationText(bool? validTRUEInvalidFALSE, StackPanel? panel, FontIcon glyph, TextBlock validationText, string text, string tooltip)
        {
            if (validTRUEInvalidFALSE is null)
            {
                if (panel is not null)
                    panel.Visibility = Visibility.Collapsed;

                glyph.Glyph = "";
                validationText.Text = "";
            }
            else if ((bool)validTRUEInvalidFALSE == true)
            {
                // Get the brush from the application resources
                var themeBrush = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];

                if (panel is not null)
                    panel.Visibility = Visibility.Visible;

                glyph.Glyph = "\uE73E";     // Tick
                glyph.Foreground = themeBrush;
                validationText.Text = text;
            }
            else
            {
                // Get the brush from the application resources
                var themeBrush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

                if (panel is not null)
                    panel.Visibility = Visibility.Visible;

                glyph.Glyph = "\uE783";    // Information 
                glyph.Foreground = themeBrush;
                validationText.Text = text;
            }

            // Retrieve the tool tip programmatically
            bool applyTooltip = false;

            if (ToolTipService.GetToolTip(validationText) is not ToolTip existingToolTip)
            {
                applyTooltip = true;
            }
            else if ((string)existingToolTip.Content != tooltip)
            {
                // Update tool tip
                existingToolTip.Content = tooltip;
            }

            // Change the tool tip
            if (applyTooltip)
            {
                ToolTip toolTip = new() { Content = tooltip };
                ToolTipService.SetToolTip(validationText, toolTip);
            }
        }

    }
}
