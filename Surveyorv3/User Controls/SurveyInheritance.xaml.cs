using MathNet.Numerics.Distributions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Surveyor.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor.User_Controls
{
    public sealed partial class SurveyInheritance : UserControl
    {
        public SurveyInheritance()
        {
            this.InitializeComponent();
        }


        /// <summary>
        /// Handle the edit or new (create) species info dialog
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <param name="editExisting"></param>
        /// <returns>true is the speciesInfo parameter has been changed</returns>
        public async Task<bool> InheritFromSurvey(MainWindow mainWindow, Reporter report, string surveyInheritingFromFileSpec)
        {
            bool ret = false;
            bool doesInheritanceSourceHaveRules = false;
            bool doesInheritanceSourceHaveCalibration = false;
            string leftCameraID = string.Empty;
            string rightCameraID = string.Empty;

            ClearControls();

            // Load the source survey
            Survey surveyInheritanceSource = new(report);

            int surveyInheritanceSourceLoaded = await surveyInheritanceSource.SurveyLoad(surveyInheritingFromFileSpec);
            if (surveyInheritanceSourceLoaded == 0)
            {
                // Get and remember left GoPro serial number
                leftCameraID = surveyInheritanceSource.Data.Media.LeftCameraID;

                // Get and remember right GoPro serial number
                rightCameraID = surveyInheritanceSource.Data.Media.RightCameraID;

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

                // Close the inheritance source survey for now
                await surveyInheritanceSource.SurveyClose();
            }

            // Create the dialog
            ContentDialog dialog = new()
            {
                Content = this,
                Title = "Inherit From Exisiting Survey",
                CloseButtonText = "Cancel",
                PrimaryButtonText = "Inherit",
                MaxWidth = 800, // <-- important!
                MinWidth = 400,
                XamlRoot = mainWindow.Content.XamlRoot  // Set the XamlRoot property
            };

            // Setup an open dialog handler
            dialog.Opened += Dialog_Opened;



            // Show the dialog and handle the response
            var result = await dialog.ShowAsync();

            // Check if the Add button pressed
            if (result == ContentDialogResult.Primary)
            {


                // surveyInheritanceSource.Data.SurveyRules.SurveyRulesInherited
            }

            ClearControls();
            dialog.Content = null;  // Detach the content after the dialog is closed

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

            // Clear environment, distrution and size (bound to xaml)            

        }


        /// <summary>
        /// Check if the 'Inherit' button should be enabled
        /// </summary>
        private void EnableButtons()
        {

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
            if (surveyRulesData.RangeRuleActive && surveyRulesData.RangeMin != 0.0 && surveyRulesData.RangeMax != 0.0)
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
    }
}
