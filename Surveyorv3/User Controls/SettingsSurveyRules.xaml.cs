using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.Foundation.Collections;


namespace Surveyor.User_Controls
{
    public sealed partial class SettingsSurveyRules : UserControl
    {
        // Reporter
        private Reporter? report = null;

        // Survey Rules
        //private Survey.DataClass.SurveyRulesClass? surveyRulesClass = null;     // This is the container class in Survey.DataClass for SurveyRulesData
        private SurveyRulesData? surveyRules = null;

        // Called from
        SettingsExpander? settingsSurveyRules = null;
        //SettingsExpander? settingsFieldTripRules = null;  // To be implemented once Field Trip are supported


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
        public void SetupForSurveySettingWindow(SettingsExpander settings, Survey survey)
        {
            // Remember the settings card for a survey (not the field trip)
            settingsSurveyRules = settings;

            // Remember the survey rules
            surveyRules = survey.Data.SurveyRules.SurveyRulesData;

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
