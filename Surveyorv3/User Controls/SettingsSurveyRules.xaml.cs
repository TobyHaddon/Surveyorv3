using Microsoft.UI.Xaml.Controls;
using System;
using System.Text.RegularExpressions;
using static Surveyor.Survey.DataClass;


namespace Surveyor.User_Controls
{
    public sealed partial class SettingsSurveyRules : UserControl
    {
        // Reporter
        private Reporter? report = null;

        // Survey Rules
        private SurveyRulesClass? surveyRules = null;  // Bound to xaml

        public SettingsSurveyRules()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Set the Reporter, used to output messages.
        /// Call as early as possible after creating the class instance.
        /// </summary>
        /// <param name="_report"></param>
        public void SetReporter(Reporter _report)
        {
            report = _report;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="survey"></param>
        public void SetupForSurveySettingWindow(Survey survey)
        {
            // Remember the settings card for a survey (not the field trip)
            //???settingsSurveyRules = settings;
            //???settingsFieldTripRules = null;

            // Remember the survey rules
            surveyRules = survey.Data.SurveyRules;
        }


        /// <summary>
        /// Close resources
        /// </summary>
        public void Shutdown()
        {
            //settingsSurveyRules = null;
            surveyRules = null;

            report = null;
        }


        /// <summary>
        /// Control a Textbox to only allow positive decimal numbers to two decimal places
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void NumberTextBoxPositiveDecimal2DP_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            // Regex pattern to match positive numbers with up to two decimal places
            string pattern = @"^\d*\.?\d{0,2}$";

            if (!Regex.IsMatch(sender.Text, pattern))
            {
                int caretPosition = sender.SelectionStart - 1;

                // Allow only digits and one decimal point, with up to two decimal places
                sender.Text = Regex.Replace(sender.Text, @"[^0-9.]", ""); // Remove non-digit and non-dot characters

                // Ensure only one decimal point
                int firstDotIndex = sender.Text.IndexOf('.');
                if (firstDotIndex != -1)
                {
                    // Remove any extra dots
                    sender.Text = sender.Text.Substring(0, firstDotIndex + 1) + sender.Text.Substring(firstDotIndex + 1).Replace(".", "");

                    // Limit to two decimal places
                    int decimalCount = sender.Text.Length - firstDotIndex - 1;
                    if (decimalCount > 2)
                    {
                        sender.Text = sender.Text.Substring(0, firstDotIndex + 3);
                    }
                }

                // Restore cursor position
                sender.SelectionStart = Math.Max(caretPosition, 0);
            }
        }


        /// <summary>
        /// Control a Textbox to only allow positive whole numbers
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void NumberTextBoxPositiveWholeNumber_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            // Allow only digits (0-9)
            string pattern = @"^\d*$";

            if (!Regex.IsMatch(sender.Text, pattern))
            {
                int caretPosition = sender.SelectionStart - 1;

                // Remove all non-numeric characters
                sender.Text = Regex.Replace(sender.Text, @"\D", "");

                // Restore cursor position
                sender.SelectionStart = Math.Max(caretPosition, 0);
            }
        }

    }
}
