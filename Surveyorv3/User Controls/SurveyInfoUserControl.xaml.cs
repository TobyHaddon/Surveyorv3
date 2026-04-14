using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.AppBroadcasting;

namespace Surveyor.User_Controls
{
    public sealed partial class SurveyInfoUserControl : UserControl
    {
        public event RoutedEventHandler? SelectionChanged;

        public enum SurveyInfoMode
        {
            Setup,
            LimitedEdit,
            View
        }

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(
                nameof(Mode),
                typeof(SurveyInfoMode),
                typeof(SurveyInfoUserControl),
                new PropertyMetadata(SurveyInfoMode.Setup, OnModeChanged));

        public SurveyInfoMode Mode
        {
            get => (SurveyInfoMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SurveyInfoUserControl control)
            {
                control.ApplyMode();
            }
        }

        private FieldTrip? _fieldTrip = null;

        private string _speciesListSelectorOriginalText;

        public SurveyInfoUserControl()
        {
            InitializeComponent();

            ApplyMode();

            // Get the initial text from the species list selector. These are to prompt the user
            // as to what the UI control is for. We remember these value so we do not confuse them with 
            // selected values
            _speciesListSelectorOriginalText = (string)SpeciesActiveListDropDown.Content;

            SurveyCodeUserControl.Structure = SurveyCodeUserControl.SurveyCodeStructure.Unstructured;
            SurveyCodeUserControl.SelectionChanged += SurveyCodeUserControl_SelectionChanged;

            ResetFieldTrip();
        }



        /// <summary>
        /// Call if the application is under the control of a field trip template. This limits the user 
        /// to only select from the sites, depths and replicates layouts defined in the template.
        /// </summary>
        /// <param name="layout"></param>
        public void SetFieldTrip(FieldTrip fieldTrip)
        {
            _fieldTrip = fieldTrip;

            SurveyCodeUserControl.SetFieldTrip(fieldTrip);

            // Default species list selector to enabled
            SpeciesActiveListDropDown.IsEnabled = false;  // Species list is only displayed

            // Find the species list
            string? speciesListName = null;
            if (!string.IsNullOrEmpty(fieldTrip.Data.Info.SpeciesListName))
            {
                // Get the species list from the field trip info. 
                speciesListName = fieldTrip.Data.Info.SpeciesListName;
            }
            // Fall back to the species list in the app settings if not found in the field trip info. 
            if (speciesListName is null && !string.IsNullOrEmpty(SettingsManagerLocal.ActiveSpeciesList))
            {
                speciesListName = SettingsManagerLocal.ActiveSpeciesList;
            }

            if (speciesListName is not null)
            { 
                // Find available in the species list SpeciesActiveListMenuFlyout.Items.Tag list
                MenuFlyoutItem? speciesItem = SpeciesActiveListMenuFlyout.Items
                                    .OfType<MenuFlyoutItem>()
                                    .FirstOrDefault(i => string.Equals(i.Tag as string, fieldTrip.Data.Info.SpeciesListName, StringComparison.OrdinalIgnoreCase));

                if (speciesItem is not null)
                {
                    SpeciesActiveListDropDown.Content = speciesItem.Tag as string;
                    SpeciesActiveListDropDown.IsEnabled = false;
                }
            }
        }

        public void ResetFieldTrip()
        {
            _fieldTrip = null;
            SurveyCodeUserControl.ResetFieldTrip();

            ResetDialogFields();
        }

        /// <summary>
        /// Load the species list selector with allowed values
        /// </summary>
        /// <param name="speciesList"></param>
        public void LoadSpeciesList(List<string>speciesList)
        {
            SpeciesActiveListMenuFlyout.Items.Clear();

            foreach (string text in speciesList)
            {
                MenuFlyoutItem item = new()
                {
                    Text = text,
                    Tag = text
                };

                item.Click += SpeciesListSelectorItem_Click;
                SpeciesActiveListMenuFlyout.Items.Add(item);
            }
        }

        /// <summary>
        /// Clear all the dialog fields
        /// </summary>
        public void ResetDialogFields()
        {
            SurveyCodeUserControl.ResetDialogFields();

            SpeciesActiveListDropDown.Content = _speciesListSelectorOriginalText;

            SpeciesActiveListDropDown.IsEnabled = false;

            SurveyAnalystName.Text = string.Empty;
        }


        /// <summary>
        /// Pass on the hint
        /// </summary>
        /// <param name="mediaFileName"></param>
        public void SetHint(string mediaFileName) => SurveyCodeUserControl.SetHint(mediaFileName);


        /// <summary>
        /// Set the site code
        /// </summary>
        /// <param name="siteCode"></param>
        public void SetSiteCode(string siteCode)
        {
            SurveyCodeUserControl.SetSiteNameOrCode(siteCode);
        }


        /// <summary>
        /// Get the site code
        /// </summary>
        /// <returns></returns>
        public string GetSiteCode() => SurveyCodeUserControl.GetSiteCode();


        /// <summary>
        /// Set the survey depth
        /// </summary>
        /// <param name="siteCode"></param>
        public void SetDepth(string depth) => SurveyCodeUserControl.SetDepth(depth);


        /// <summary>
        /// Get the site code
        /// </summary>
        /// <returns></returns>
        public string GetDepth() => SurveyCodeUserControl.GetDepth();
        

        /// <summary>
        /// Set the survey date
        /// </summary>
        /// <param name="surveyDate"></param>
        public void SetSurveyDate(DateTime surveyDate) => SurveyCodeUserControl.SetSurveyDate(surveyDate);


        /// <summary>
        /// Get survey date
        /// </summary>
        /// <returns></returns>
        public DateTime GetSurveyDate() => SurveyCodeUserControl.GetSurveyDate();
        

        /// <summary>
        /// Set the selected replicate names
        /// </summary>
        /// <param name="replicateNames"></param>
        public void SetSelectedReplicateNames(ObservableCollection<string> replicateNames) => SurveyCodeUserControl.SetSelectedReplicateNames(replicateNames);  


        /// <summary>
        /// Get the selected replicate names
        /// </summary>
        /// <returns></returns>
        public ObservableCollection<string> GetSelectedReplicateNames() => SurveyCodeUserControl.GetSelectedReplicateNames();
        

        /// <summary>
        /// Set the analyst name field
        /// </summary>
        /// <param name="analystName"></param>
        public void SetAnalystName(string analystName)
        {
            SurveyAnalystName.Text = analystName;
        }


        /// <summary>
        /// Get the analyst name field
        /// </summary>
        /// <returns></returns>
        public string GetAnalystName()
        {
            return SurveyAnalystName.Text;
        }


        /// <summary>
        /// Set the selected species list
        /// </summary>
        /// <param name="selectedSpeciesList"></param>
        public void SetSpeciesListSelectedItem(string selectedSpeciesList)
        {               
            SpeciesActiveListDropDown.Content = selectedSpeciesList;
        }


        /// <summary>
        /// Get the selected species list
        /// </summary>
        /// <returns></returns>
        public string GetSpeciesListSelectedItem()
        {
            if ((string)SpeciesActiveListDropDown.Content != _speciesListSelectorOriginalText)
                return (string)SpeciesActiveListDropDown.Content;
            else
                return string.Empty;
        }


        /// <summary>
        /// Returns the current validation status of the fields in the SurveyInfo user control
        /// </summary>
        /// <returns>true=valid, false=invalid, null=warnings</returns>
        public bool? GetValidationStatus()
        {
            bool? ret = false;
            EntryFieldsValidReturn entryFieldsValidReturn = EntryFieldsValid(reportIssues: false);

            switch (entryFieldsValidReturn)
            {
                case EntryFieldsValidReturn.Valid:
                    ret = true;
                    break;
                case EntryFieldsValidReturn.Invalid:
                    ret = false;
                    break;
                case EntryFieldsValidReturn.Warning:
                    ret = null;
                    break;
            }

            return ret;
        }


        public void SetSurveyCode(string surveyCode)
        {
            SurveyCodeUserControl.SetHint(surveyCode);
        }

        /// <summary>
        /// Get the survey code. 
        /// </summary>
        /// <returns></returns>
        public string GetSurveyCode()
        {
            (_, string surveyCode) = SurveyCodeUserControl.GetSurveyCode();

            return surveyCode;
        }


        ///
        /// EVENTS
        /// 


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyCodeUserControl_SelectionChanged(object sender, RoutedEventArgs e)
        {
            _ = EntryFieldsValid(reportIssues: false);
            SelectionChanged?.Invoke(this, e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyAnalystName_TextChanged(object sender, TextChangedEventArgs e)
        {
            _ = EntryFieldsValid(reportIssues: false);
            SelectionChanged?.Invoke(this, e);
        }


        /// <summary>
        /// Handles the click event for a species list selector item.
        /// This handler is attached in code behind to each item in the species list selector menu flyout, 
        /// and the Tag property of each menu item is used to identify which item was clicked.  It doesn't
        /// appear in the .XAML
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SpeciesListSelectorItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string newSpeciesListName)
            {
                SpeciesActiveListDropDown.Content = newSpeciesListName;
                _ = EntryFieldsValid(reportIssues: false);
                SelectionChanged?.Invoke(this, e);
            }
        }


        /// <summary>
        /// Edit button push. Change the SurveyInfoUserControl mode and disable the button
        /// (one time use only)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            EditButton.IsEnabled = false;

            Mode = SurveyInfoMode.LimitedEdit;
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Check if the fields collectively are valid, invalid or if there are issues
        /// </summary>
        /// <param name="reportIssues"></param>
        /// <returns></returns>

        enum EntryFieldsValidReturn
        {
            Invalid,
            Valid,
            Warning
        }
        private EntryFieldsValidReturn EntryFieldsValid(bool reportIssues)
        {
            EntryFieldsValidReturn ret = EntryFieldsValidReturn.Valid;

            bool? surveyCodeReady;
            bool analystNameReady;
            bool speciesListNameReady;

            (surveyCodeReady, _) = SurveyCodeUserControl.GetSurveyCode();

            // Get and check the analyst name
            analystNameReady = !string.IsNullOrEmpty(GetAnalystName());

            if (!analystNameReady)
                SetValidationText(false/*invalid*/, null, SurveyAnalystNameValidationGlyph, SurveyAnalystNameValidationText,
                    "Survey must have an analyst name", "");
            else
                SetValidationText(null/*nothing*/, null, SurveyAnalystNameValidationGlyph, SurveyAnalystNameValidationText, "", "");


            // Get and check the species list
            speciesListNameReady = !string.IsNullOrEmpty(GetSpeciesListSelectedItem());

            if (!speciesListNameReady)
                SetValidationText(false/*invalid*/, null, SpeciesActiveListValidationGlyph, SpeciesActiveListValidationText,
                    "Set in Settings or via a field trip template",
                    "The species list can't be set in this survey setup screen. It either comes from a field trip template or the Settings screen. If you have a field trip template, it is loaded via File>'Load Field Trip Template' "+
                    "or if you don't have a field trip template go to File>'Settings' and in the 'Species List' section, select the required species list.");
            else
                SetValidationText(null/*nothing*/, null, SpeciesActiveListValidationGlyph, SpeciesActiveListValidationText, "", "");


            // Establish the overall status
            if ((surveyCodeReady is not null) && surveyCodeReady == true && analystNameReady && speciesListNameReady)
                ret = EntryFieldsValidReturn.Valid;
            else
                ret = EntryFieldsValidReturn.Invalid;

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

        private void ApplyMode()
        {
            // Named controls exist after InitializeComponent; null-guard for safety.
            if (SurveyCodeUserControl is null || SurveyAnalystName is null || SpeciesActiveListDropDown is null || EditButton is null)
                return;

            // Species list must be either set by the field trip or come from the app settings,
            // so disable the selector in setup mode
            SpeciesActiveListDropDown.IsEnabled = false;

            switch (Mode)
            {
                case SurveyInfoMode.Setup:                    
                    SurveyCodeUserControl.Mode = SurveyCodeUserControl.SurveyCodeMode.Setup;
                    SurveyAnalystName.IsEnabled = true;
                    EditButton.Visibility = Visibility.Collapsed;
                    EditButton.IsEnabled = false;
                    break;

                case SurveyInfoMode.LimitedEdit:
                    // In limited edit allow the replicates to be edited
                    SurveyCodeUserControl.Mode = SurveyCodeUserControl.SurveyCodeMode.LimitedEdit;
                    // In limited edit allow the analyst name to be edited
                    SurveyAnalystName.IsEnabled = true;
                    // Button must have been pressed to get here, so disable it to prevent multiple
                    // presses and confusion about the mode
                    EditButton.Visibility = Visibility.Collapsed;
                    EditButton.IsEnabled = false;
                    break;

                case SurveyInfoMode.View:
                    SurveyCodeUserControl.Mode = SurveyCodeUserControl.SurveyCodeMode.View;
                    SurveyAnalystName.IsEnabled = false;
                    EditButton.Visibility = Visibility.Visible;
                    EditButton.IsEnabled = true;
                    break;
            }
        }

    }
}
