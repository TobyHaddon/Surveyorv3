using ActionCameraMP4MetadataExtraction;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;
using WinRT.Surveyorv3VtableClasses;
using static Surveyor.User_Controls.SurveyInfoUserControl;

///
/// *** Remember when editing this User Control code that it is used from both    ***
/// *** the context of a ContentDialog (for a new Survey) and from a SettingCard  ***
/// *** from the SettingsWindow.                                                  ***  
///
// SurveyStereoInfoAndMedia  
// This is a user control is used to setup and edit the Survey information and media file list
// 
// Version 1.1
// 2024-12-31 Added to support to size the parent content dialog if running in that context
// Version 1.2
// 2025-01-16 Added Setup method for calling from ContentDialog and one for Settings Window
// Version 1.3
// 2025-01-17 Initial functionally complete
// Version 1.4
// 2025-10-31 Renamed to SurveyStereoInfoAndMediaUserControl 
// Version 1.5
// 2026-04-13 Moved to using SurveyInfoUserControl (which in turn depends in SurveyCodeUserControl and ReplicatesUserControl)

namespace Surveyor.User_Controls
{
    public sealed partial class SurveyStereoInfoAndMedia : UserControl
    {        
        // Reporter
        private Reporter? _report = null;

        public IReadOnlyList<StorageFile>? mediaFilesSelected = null;

        private ContentDialog? ParentDialog { get; set; } = null;
        private SettingsCard? ParentSettings { get; set; } = null;        

        private ObservableCollection<StereoMediaFileItem> LeftMediaFileItemList { get; set; }
        private ObservableCollection<StereoMediaFileItem> RightMediaFileItemList { get; set; }

        // This copy of the survey class is only used in Settings and the setup Dialog
        // It is necessary because the is no save concept with Settings. We therefore
        // need access to the survey class to update the survey information as the user
        // makes changes in the UI
        private Survey? _survey = null;

        public SurveyStereoInfoAndMedia()
        {
            this.InitializeComponent();

            // Initialize the collection
            LeftMediaFileItemList = [];
            RightMediaFileItemList = [];
        }


        /// <summary>
        /// Set the Reporter, used to output messages.
        /// Call as early as possible after creating the class instance.
        /// </summary>
        /// <param name="_report"></param>
        public void SetReporter(Reporter report)
        {
            _report = report;
        }


        /// <summary>
        /// Called from the function that creates the ContentDailog used to setup a new survey
        /// </summary>
        /// <param name="dialog"></param>
        /// <param name="_mediaFilesSelected"></param>
        public void SetupForContentDialog(ContentDialog dialog, FieldTrip? fieldTrip, IReadOnlyList<StorageFile> _mediaFilesSelected, string potentialInheritanceSurvey)
        {
            Debug.WriteLine($"SetupForContentDialog() Started");
            ParentDialog = dialog;
            ParentSettings = null; // N/A
            _survey = null;  // N/A

            // Reset Fields
            ResetDialogFields();

            // Allow all fields to be edited
            SurveyInfoUserControl.Mode = SurveyInfoMode.Setup;

            // If not under control of a field trip template and a prior Survey is available then offer the inherit option
            if (fieldTrip is null && !string.IsNullOrEmpty(potentialInheritanceSurvey))
            {
                // Show the inherit survey data checkbox
                InheritSurveyData.Visibility = Visibility.Visible;
                PotentialInheritanceSurveyName.Visibility = Visibility.Visible;

                if (SettingsManagerLocal.InheritFromLastSetting)
                    InheritSurveyData.IsChecked = true;
                else
                    InheritSurveyData.IsChecked = false;
            }
            else
            {
                // Hide the inherit survey data checkbox
                InheritSurveyData.Visibility = Visibility.Collapsed;
                PotentialInheritanceSurveyName.Visibility = Visibility.Collapsed;
            }


            // Create a exception if not running from the ContentDialog context
            if (/*!dialog.IsLoaded || */!dialog.IsEnabled)
                throw new InvalidOperationException("This function should only be called from the context of a ContentDialog");

            // Remember the selected files
            this.mediaFilesSelected = _mediaFilesSelected;

            // Run on the UI thread
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                _ = SetupForContentDialogOnUiThreadAsync(fieldTrip, potentialInheritanceSurvey);
            }); ;

            Debug.WriteLine($"SetMediaFiles() Complete");
        }

        private async Task SetupForContentDialogOnUiThreadAsync(FieldTrip? fieldTrip, string potentialInhertanceSurvey)
        {
            try
            {
                SurveyInfoUserControl.SelectionChanged += SurveyInfoUserControl_SelectionChanged;

                // Load the available species list (must be before the SetFieldTrip)
                List<string> availableSpeciesLists = SpeciesCodeList.GetAvailableSpeciesLists();
                SurveyInfoUserControl.LoadSpeciesList(availableSpeciesLists);

                // Set the field trip if available
                if (fieldTrip is not null)
                    SurveyInfoUserControl.SetFieldTrip(fieldTrip);
                else
                {
                    SurveyInfoUserControl.ResetFieldTrip();
                    // To use the default species list if setup
                    if (!string.IsNullOrEmpty(SettingsManagerLocal.ActiveSpeciesList))
                        SurveyInfoUserControl.SetSpeciesListSelectedItem(SettingsManagerLocal.ActiveSpeciesList);
                }

                // Help the User Info control default the Survey Code fields based on the media name
                if (mediaFilesSelected is not null && mediaFilesSelected.Count > 0)
                    SurveyInfoUserControl.SetHint(mediaFilesSelected[0].Path);

                BitmapImage thumbnailDefault = GetDefaultThumbnail();

                if (mediaFilesSelected is not null && mediaFilesSelected.Count > 0)
                {
                    List<StereoMediaFileItem> mediaFileItemList = [];
                    foreach (StorageFile file in mediaFilesSelected)
                    {
                        StereoMediaFileItem item = await GetMediaFileInfoAsync(file, thumbnailDefault);
                        mediaFileItemList.Add(item);
                    }

                    (LeftMediaFileItemList, RightMediaFileItemList, _) = DetectLeftAndRightMediaFile(mediaFileItemList);

                    LeftMediaFileNames.ItemsSource = LeftMediaFileItemList;
                    RightMediaFileNames.ItemsSource = RightMediaFileItemList;
                }

                await LoadUserFullNameAsync();

                PotentialInheritanceSurveyName.Text = string.IsNullOrEmpty(potentialInhertanceSurvey)
                    ? string.Empty
                    : $"({potentialInhertanceSurvey})";

                EntryFieldsValid(false);
            }
            catch (Exception ex)
            {
                _report?.Error("", $"SetupForContentDialog failed: {ex.Message}");
            }
        }


        /// <summary>
        /// Set the parent of this control as a SettingsCard
        /// This is used when this control is used view survey settings
        /// </summary>
        /// <param name="settings"></param>
        public async Task SetupForSettingWindowAsync(SettingsCard settings, FieldTrip? fieldTrip, Survey survey)
        {
            // Remember the parent 
            ParentSettings = settings;
            ParentDialog = null;
            _survey = survey;

            // Reset Fields
            ResetDialogFields();

            // Allow no fields to be edited (there is an edit button)
            SurveyInfoUserControl.Mode = SurveyInfoMode.View;

            // Hide the inherit survey data checkbox (in-case it has been set in the ContentDialog context)
            InheritSurveyData.Visibility = Visibility.Collapsed;
            PotentialInheritanceSurveyName.Visibility = Visibility.Collapsed;

            // Used to save any replicates edit
            SurveyInfoUserControl.SelectionChanged += SurveyInfoUserControl_SelectionChanged;

            // Load the available species list (must be before the SetFieldTrip)
            List<string> availableSpeciesLists = SpeciesCodeList.GetAvailableSpeciesLists();
            SurveyInfoUserControl.LoadSpeciesList(availableSpeciesLists);

            // Set the field trip if available
            if (fieldTrip is not null)
                SurveyInfoUserControl.SetFieldTrip(fieldTrip);
            else
            {
                SurveyInfoUserControl.ResetFieldTrip();
                // To use the default species list if setup
                if (!string.IsNullOrEmpty(SettingsManagerLocal.ActiveSpeciesList))
                    SurveyInfoUserControl.SetSpeciesListSelectedItem(SettingsManagerLocal.ActiveSpeciesList);
            }


            // Load the survey code (survey name e.g. CVW-10-5-2024-07-12)
            if (!string.IsNullOrWhiteSpace(survey.Data.Info.SurveyCode))
            {
                SurveyInfoUserControl.SetSurveyCode(survey.Data.Info.SurveyCode);   // Survey Date will be extracted from this (not store in the Survey class)      
            }
            else
            {
                // If the survey code is empty then use the survey file name stem
                // This maybe because the .survey file is an old version
                if (survey.Data.Info.SurveyFileName is not null)
                {
                    string surveyCode = Path.GetFileNameWithoutExtension(survey.Data.Info.SurveyFileName);
                    SurveyInfoUserControl.SetSurveyCode(surveyCode);
                }
            }

            // Load the survey depth
            if (survey.Data.Info.SurveyDepth is not null)
                SurveyInfoUserControl.SetDepth(survey.Data.Info.SurveyDepth);       // SurveyInfoUserControl.SetSurveyCode will also extract a depth from the survey code
                                                                                    // This method should be called after SetSurveyCode() to ensure the official value is 
                                                                                    // use (but is probably the same value)

            // If Structured load the allowed replicates
            if (fieldTrip is not null)
                SurveyInfoUserControl.SetSelectedReplicateNames(survey.Data.Info.SurveyAllowedReplicates);  // SurveyInfoUserControl.SetSurveyCode will also extract the transect
                                                                                                        // name from the survey code. method should be called after SetSurveyCode()
                                                                                                        // to ensure the official values are use (but is probably the same value)

            // Load the survey analyst name
            if (survey.Data.Info.SurveyAnalystName is not null)
                SurveyInfoUserControl.SetAnalystName(survey.Data.Info.SurveyAnalystName);
            else
                SurveyInfoUserControl.SetAnalystName(string.Empty);
           


            // Get suitable default thumbnail based on the current theme
            BitmapImage thumbnailDefault = GetDefaultThumbnail();

            if (survey.Data.Media.MediaPath is not null)
            {
                // Load left the media files
                if (survey.Data.Media.LeftMediaFileNames.Count > 0)
                {
                    for (int index = 0; index < survey.Data.Media.LeftMediaFileNames.Count; index++)
                    {
                        string fileSpec = survey.GetLeftMediaFileSpec(index);

                        StorageFile file = await StorageFile.GetFileFromPathAsync(fileSpec);
                        StereoMediaFileItem item = await GetMediaFileInfoAsync(file, thumbnailDefault);

                        LeftMediaFileItemList.Add(item);
                    }
                }

                // Load right the media files
                if (survey.Data.Media.RightMediaFileNames.Count > 0)
                {
                    for (int index = 0; index < survey.Data.Media.RightMediaFileNames.Count; index++)
                    {
                        string fileSpec = survey.GetRightMediaFileSpec(index);

                        StorageFile file = await StorageFile.GetFileFromPathAsync(fileSpec);
                        StereoMediaFileItem item = await GetMediaFileInfoAsync(file, thumbnailDefault);

                        RightMediaFileItemList.Add(item);
                    }
                }
            }

            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Free resources
        /// </summary>
        public void Shutdown()
        {
            LeftMediaFileNames.ItemsSource = null;
            RightMediaFileNames.ItemsSource = null;

            LeftMediaFileItemList.Clear();
            RightMediaFileItemList.Clear();
        }

        /// <summary>
        /// Save the values from the survey information fields and media into the surveyClass 
        /// object
        /// </summary>
        /// <param name="surveyClass"></param>
        /// <returns>true is inheritance requested</returns>
        public bool SaveForContentDialog(Survey surveyClass)
        {
            bool ret = false;

            // Remember the survey type
            Survey.SurveyType surveyType = surveyClass.Data.Info.SurveyType;

            // Reset the Info and Media classes
            surveyClass.Data.Info.Clear();
            surveyClass.Data.Media.Clear();

            // Restore the Survey type
            surveyClass.Data.Info.SurveyType = surveyType;

            // Save the survey code (name of the survey)
            surveyClass.Data.Info.SurveyCode = SurveyInfoUserControl.GetSurveyCode();

            // Get the survey depth
            surveyClass.Data.Info.SurveyDepth = SurveyInfoUserControl.GetDepth();

            // Save the survey analyst name
            surveyClass.Data.Info.SurveyAnalystName = SurveyInfoUserControl.GetAnalystName();

            // Remember the allow replicate names
            surveyClass.Data.Info.SurveyAllowedReplicates = SurveyInfoUserControl.GetSelectedReplicateNames();

            // Save the media files
            if (LeftMediaFileNames is not null && RightMediaFileNames is not null && (LeftMediaFileNames.Items.Count + RightMediaFileNames.Items.Count > 0))
            {
                if (LeftMediaFileNames.Items.Count > 0)
                    surveyClass.Data.Media.MediaPath = Path.GetDirectoryName(((StereoMediaFileItem)LeftMediaFileNames.Items[0]).MediaFilePath);
                else if (RightMediaFileNames.Items.Count > 0)
                    surveyClass.Data.Media.MediaPath = Path.GetDirectoryName(((StereoMediaFileItem)RightMediaFileNames.Items[0]).MediaFilePath);

                // Load left media
                foreach (StereoMediaFileItem item in LeftMediaFileNames.Items)
                {
                    if (item.MediaFileName is not null)
                        surveyClass.Data.Media.LeftMediaFileNames.Add(item.MediaFileName);
                }
                // Get and remember left GoPro serial number
                if (surveyClass.Data.Media.LeftMediaFileNames.Count > 0) 
                    surveyClass.Data.Media.LeftCameraID = ((StereoMediaFileItem)LeftMediaFileNames.Items[0]).GoProSerialNumber;


                // Load right media
                foreach (StereoMediaFileItem item in RightMediaFileNames.Items)
                {
                    if (item.MediaFileName is not null)
                        surveyClass.Data.Media.RightMediaFileNames.Add(item.MediaFileName);
                }
                // Get and remember right GoPro serial number
                if (surveyClass.Data.Media.RightMediaFileNames.Count > 0)
                    surveyClass.Data.Media.RightCameraID = ((StereoMediaFileItem)RightMediaFileNames.Items[0]).GoProSerialNumber;

            }

            // Remember the last used analyst name
            SettingsManagerLocal.UserName = SurveyInfoUserControl.GetAnalystName();

            // Remember the inheritance check box setting
            if (IsParentContentDialog() && InheritSurveyData.IsChecked is not null)
            {
                SettingsManagerLocal.InheritFromLastSetting =  (bool)InheritSurveyData.IsChecked;
            }

            // Report any issues with the data
            EntryFieldsValid(true/*report*/);

            // Inheritance requested?
            if (IsParentContentDialog() && InheritSurveyData.IsChecked is not null)
                ret = (bool)InheritSurveyData.IsChecked;

            return ret;
        }



        /// 
        /// EVENTS
        /// 

        /// <summary>
        /// The Survey Info user control that handle structured and unstructured survey information
        /// setup is signaling a change in status
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyInfoUserControl_SelectionChanged(object sender, RoutedEventArgs e)
        {
            /// Validate the buttons if the user has edited value in the dialog
            EntryFieldsValid(false/*no reporting*/);

            // If the survey info user control there is a limited edit mode (used in Settings windows only)
            // it is possible that the user adjusted the replicates this survey covers. If so that needs
            // to be saved back to the survey
            if (ParentSettings is not null && SurveyInfoUserControl.Mode == SurveyInfoMode.LimitedEdit && _survey is not null)
            {
                // Compare the replicates elements from the survey to the settings to see if the user edited
                ObservableCollection<string> newReplicates = SurveyInfoUserControl.GetSelectedReplicateNames();
                ObservableCollection<string> currentReplicates = _survey.Data.Info.SurveyAllowedReplicates;
                if (!newReplicates.SequenceEqual(currentReplicates))
                    // Update the replicates in the survey
                    _survey.Data.Info.SurveyAllowedReplicates = newReplicates;

                // Compare the analysts name
                if (SurveyInfoUserControl.GetAnalystName() != _survey.Data.Info.SurveyAnalystName)
                    _survey.Data.Info.SurveyAnalystName = SurveyInfoUserControl.GetAnalystName();
            }
        }


        /// <summary>
        /// Move the selected item up in it's list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemUp_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMediaFileNames.SelectedItem is StereoMediaFileItem selectedItem)
            {
                int index = LeftMediaFileItemList.IndexOf(selectedItem);
                if (index > 0)
                {
                    LeftMediaFileItemList.Move(index, index - 1);
                }
            }
            else if (RightMediaFileNames.SelectedItem is StereoMediaFileItem rightSelectedItem)
            {
                int index = RightMediaFileItemList.IndexOf(rightSelectedItem);
                if (index > 0)
                {
                    RightMediaFileItemList.Move(index, index - 1);
                }
            }
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Move the selected item down in it's list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemDown_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMediaFileNames.SelectedItem is StereoMediaFileItem selectedItem)
            {
                int index = LeftMediaFileItemList.IndexOf(selectedItem);
                if (index < LeftMediaFileItemList.Count - 1)
                {
                    LeftMediaFileItemList.Move(index, index + 1);
                }
            }
            else if (RightMediaFileNames.SelectedItem is StereoMediaFileItem rightSelectedItem)
            {
                int index = RightMediaFileItemList.IndexOf(rightSelectedItem);
                if (index < RightMediaFileItemList.Count - 1)
                {
                    RightMediaFileItemList.Move(index, index + 1);
                }
            }
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Move the selected item in the left list to the right list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemAcrossRight_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMediaFileNames.SelectedItem is StereoMediaFileItem selectedItem && RightMediaFileItemList.Count < 5)
            {
                LeftMediaFileItemList.Remove(selectedItem);
                RightMediaFileItemList.Add(selectedItem);
            }
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Move the selected item in the right list to the left list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemAcrossLeft_Click(object sender, RoutedEventArgs e)
        {
            if (RightMediaFileNames.SelectedItem is StereoMediaFileItem selectedItem && LeftMediaFileItemList.Count < 5)
            {
                RightMediaFileItemList.Remove(selectedItem);
                LeftMediaFileItemList.Add(selectedItem);
            }
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Delete the selected item from the list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMediaFileNames.SelectedItem is StereoMediaFileItem selectedItem)
            {
                LeftMediaFileItemList.Remove(selectedItem);
            }
            else if (RightMediaFileNames.SelectedItem is StereoMediaFileItem rightSelectedItem)
            {
                RightMediaFileItemList.Remove(rightSelectedItem);
            }
            EntryFieldsValid(false/*no reporting*/);
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            // Add file not supported in this version (may never be)
            throw new NotImplementedException();
        }


        /// <summary>
        /// Users changed the selected item in the left media file list view. Now adjust the control 
        /// button accordingly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftMediaFileNames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Remove any existing selected item in the other (right list view)
            if (e.AddedItems.Count > 0)
                RightMediaFileNames.SelectedIndex = -1;
            
            // Setup the buttons
            EnableDisableControlButtons();
        }


        /// <summary>
        /// Users changed the selected item in the right media file list view. Now adjust the control 
        /// button accordingly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightMediaFileNames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Remove any existing selected item in the other (left list view)
            if (e.AddedItems.Count > 0)
                LeftMediaFileNames.SelectedIndex = -1;

            // Setup the buttons
            EnableDisableControlButtons();
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Called to check if this user control is running in the context of a ContentDialog
        /// </summary>
        /// <returns></returns>
        private bool IsParentContentDialog()
        {
            if (ParentDialog is not null)
                return true;
            else
                return false;
        }


        /// <summary>
        /// Called to check if this user control is running in the context of a SettingsCard
        /// </summary>
        /// <returns></returns>
        private bool IsParentSettingsCard()
        {
            if (ParentSettings is not null)
                return true;
            else
                return false;
        }


        /// <summary>
        /// Try to find a suitable user name for the SurveyAnalystName field
        /// </summary>
        private async Task LoadUserFullNameAsync()
        {
            // Get the user name
            string? fullName = await UserHelper.GetUserFullNameAsync();

            // Get any previously user name from local settings
            string? previousName = SettingsManagerLocal.UserName;
            if (string.IsNullOrEmpty(previousName))
            {
                if (!string.IsNullOrEmpty(fullName))
                    SurveyInfoUserControl.SetAnalystName(fullName);
                else
                    SurveyInfoUserControl.SetAnalystName(string.Empty);
            }
            else
            {
                SurveyInfoUserControl.SetAnalystName(previousName);
            }
        }


        /// <summary>
        /// Try to figure out which is the left and which is the right media file
        /// </summary>
        /// <param name="file1"></param>
        /// <param name="file2"></param>
        /// <returns></returns>
        private static (ObservableCollection<StereoMediaFileItem> LeftFiles, ObservableCollection<StereoMediaFileItem> RightFiles, double Certainty) DetectLeftAndRightMediaFile(List<StereoMediaFileItem> mediaFiles)
        {
            double certainty = 1.0;
            ObservableCollection<StereoMediaFileItem> leftFiles = [];
            ObservableCollection<StereoMediaFileItem> rightFiles = [];

            // Regex to identify and isolate 'L' or 'R'
            // Regex pattern explanation:
            // (?<![a-zA-Z]) - Ensures there is NO letter before 'L'
            // L             - Matches uppercase 'L'
            // (?![a-zA-Z])  - Ensures there is NO letter after 'L'
            Regex leftIsolatedRegex = new(@"(?<![a-zA-Z])L(?![a-zA-Z])");
            Regex rightIsolatedRegex = new(@"(?<![a-zA-Z])R(?![a-zA-Z])");

            // Regex to identify left and right or l or r
            Regex leftSimpleRegex = new("(?i)(left|l[^a-z])");
            Regex rightSimpleRegex = new("(?i)(right|r[^a-z])");

            foreach (StereoMediaFileItem file in mediaFiles)
            {
                if (file is null || file.MediaFileName is null)
                    continue;

                string fileName = file.MediaFileName ?? "";

                // Look for isolated L or R matches
                if (leftIsolatedRegex.IsMatch(fileName))
                    leftFiles.Add(file);
                else if (rightIsolatedRegex.IsMatch(fileName))
                    rightFiles.Add(file);
                // Look for simple matches
                else if (leftSimpleRegex.IsMatch(fileName))
                    leftFiles.Add(file);
                else if (rightSimpleRegex.IsMatch(fileName))
                    rightFiles.Add(file);
                else
                {
                    string fileStem = Path.GetFileNameWithoutExtension(fileName);

                    // Look for less certain matches
                    int lastIndexForL = fileStem.LastIndexOf('L');
                    int lastIndexForR = fileStem.LastIndexOf('R');

                    if (lastIndexForL != -1 && fileStem.Length - lastIndexForL >= 2)
                    {
                        leftFiles.Add(file);
                        certainty = 0.6;
                    }
                    else if (lastIndexForR != -1 && fileStem.Length - lastIndexForR >= 2)
                    {
                        rightFiles.Add(file);
                        certainty = 0.6;
                    }
                    // Must put the file somewhere so add to left and let the user
                    // fix left or right via to UI
                    else
                    {
                        leftFiles.Add(file);
                        certainty = 0.0;
                    }
                }
            }


            // Default case if unable to distinguish
            return (LeftFiles: leftFiles, RightFiles: rightFiles, Certainty: certainty);
        }


        /// <summary>
        /// Called when anything changes to test the validity of the survey information and media
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
            bool infoValid = true;
            bool mediaValid = true;
            bool mediaGoProSNMatch = true;
            bool mediaSameResolution;   // Set later
            bool mediaSameFrameRate;   // Set later
            bool mediaDatesMatch = true;
            bool mediaContigious = true;

            // Get the status of the Survey Info User Control information
            bool? surveyInfoStatus = SurveyInfoUserControl.GetValidationStatus();

            // Return Invalid if any invalid data
            if (surveyInfoStatus is null)
                ret = EntryFieldsValidReturn.Warning;
            else if ((bool)surveyInfoStatus == false)
                ret = EntryFieldsValidReturn.Invalid;
            else
                ret = EntryFieldsValidReturn.Valid;

            // Get survey code - used for reporting
            string surveyCode = SurveyInfoUserControl.GetSurveyCode();


            // Check all media from the same path
            bool mediaPathSame = CheckAllMediaPathAreTheSame();
            if (!mediaPathSame)
            {
                SetValidationText(false/*invalid*/, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, "All media files need to be in the same directory", "");
                mediaValid = false;

                if (reportIssues)
                    _report?.Warning("", $"The media files for survey {surveyCode} are not all from the same directory and need to be");
            }
            else
            {
                // No need to show anything if the media is all from the same path
                SetValidationText(null, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, ""/*"All media files are in the same directory"*/, "");
            }


            // Check all the media is from the same date (warning only as date maybe wrong on GoPros)
            DateTime? sameDateLeftMedia = CheckMediaDatesMatch(LeftMediaFileItemList);
            DateTime? sameDateRightMedia = CheckMediaDatesMatch(RightMediaFileItemList);

            if (sameDateLeftMedia is not null && sameDateRightMedia is not null)
            {
                if (sameDateLeftMedia.Value.Date != sameDateRightMedia.Value.Date)
                {
                    SetValidationText(false/*invalid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "The left media date is different from the right media date", "The date of the media files on the left side do not match the date of the media files on the right side.\nThis can happen if the dates on the GoPro isn't set correctly and isn't a problem. However if the dates are set correctly this is a problem.");
                    mediaDatesMatch = false;

                    if (reportIssues)
                        _report?.Warning("", $"The media files for survey {surveyCode} have different dates on the left side vs. the right side");
                }
                else
                {
                    SetValidationText(true/*valid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "The media files are all from the same date", "");
                }
            }
            else if ((sameDateLeftMedia is null && LeftMediaFileItemList.Count > 0) && sameDateRightMedia is not null)
            {
                SetValidationText(false/*invalid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "Not all the media on the left side has the same date", "You would expect all the dates on the media to be the same.");

                if (reportIssues)
                    _report?.Warning("Left", $"The media files for survey {surveyCode} on the left side don't have the same date");
            }
            else if (sameDateLeftMedia is not null && (sameDateRightMedia is null && RightMediaFileItemList.Count > 0))
            {
                SetValidationText(false/*invalid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "Not all the media on the right side has the same date", "You would expect all the dates on the media to be the same.");

                if (reportIssues)
                    _report?.Warning("Right", $"The media files for survey {surveyCode} on the right side don't have the same date");
            }
            else if ((sameDateLeftMedia is null && LeftMediaFileItemList.Count > 0) && (sameDateRightMedia is null && RightMediaFileItemList.Count > 0))
            {
                SetValidationText(false/*invalid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "Not all the media on the left side and on the right side has the same date", "You would expect all the dates on the media to be the same.");

                if (reportIssues)
                    _report?.Warning("", $"The media files for survey {surveyCode} on the left side and the right side don't have the same date");
            }
            else
            {
                SetValidationText(null/*hide*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "", "");
            }


            // Check all left & right media from the same GoPro
            bool? sameGoProLeftMedia = CheckGoProSNMatch(LeftMediaFileItemList);    // Will return Null if no GoPro serial number found or there is only one left media file
            bool? sameGoProRightMedia = CheckGoProSNMatch(RightMediaFileItemList);  // Will return Null if no GoPro serial number found or there is only one right media file
            bool? differentGoProLefAndRight = CheckGoProSNLeftAndRightAreDifferent(LeftMediaFileItemList, RightMediaFileItemList);

            // Report on the status of the GoPro serial numbers in the media set
            string mediaGoProSNMatchWarningText = "";
            string mediaGoProSNMatchWarningToolTip = "";
            
            if ((sameGoProLeftMedia is null/*No S/N*/ || (sameGoProLeftMedia is not null && (bool)sameGoProLeftMedia)) && (sameGoProRightMedia is not null && !(bool)sameGoProRightMedia))
            {
                mediaGoProSNMatchWarningText = "The right media files are not all from the same GoPro";
                mediaGoProSNMatchWarningToolTip = "No all the serial numbers embedded in the right media files match";
                mediaGoProSNMatch = false;

                if (reportIssues)
                    _report?.Warning("Right", $"The right side media files for survey {surveyCode} are not all from the same GoPro");
            }
            else if ((sameGoProLeftMedia is not null && !(bool)sameGoProLeftMedia) && (sameGoProRightMedia is null/*No S/N*/ || (sameGoProRightMedia is not null && (bool)sameGoProRightMedia)))
            {
                mediaGoProSNMatchWarningText = "The left media files are not all from the same GoPro";
                mediaGoProSNMatchWarningToolTip = "No all the serial numbers embedded in the left media files match";
                mediaGoProSNMatch = false;

                if (reportIssues)
                    _report?.Warning("Left", $"The left side media files for survey {surveyCode} are not all from the same GoPro");
            }
            else if ((sameGoProLeftMedia is not null && !(bool)sameGoProLeftMedia) && (sameGoProRightMedia is not null && !(bool)sameGoProRightMedia))
            {
                mediaGoProSNMatchWarningText = "The media files on each side need to be from their specific GoPro";
                mediaGoProSNMatchWarningToolTip = "All the media files on the left side need to be from the left GoPro and all the media files on the right side need to be from right GoPro. From the GoPro serial numbers embedded in the MP4 files this is not currently the case.";
                mediaGoProSNMatch = false;

                if (reportIssues)
                    _report?.Warning("", $"The media files for survey {surveyCode} are not all from the same GoPro, different on both the left and the right side");
            }
            else if (differentGoProLefAndRight is not null && !(bool)differentGoProLefAndRight)
            {
                mediaGoProSNMatchWarningText = "Left and Right media files need to be from different GoPros";
                mediaGoProSNMatchWarningToolTip = "Left side media files need to be from a different GoPro to the right side media files.";
                mediaGoProSNMatch = false;

                if (reportIssues)
                    _report?.Warning("", $"The media files for survey {surveyCode} the left side media and the right side media are from the same GoPro, this is not possible.");
            }


            if (!mediaGoProSNMatch)
            {
                SetValidationText(false/*invalid*/, SurveyGoProMatchPanel, SurveyGoProMatchGlyph, SurveyGoProMatchValidationText, mediaGoProSNMatchWarningText, mediaGoProSNMatchWarningToolTip);
            }
            else
            {
                // Only show the validation text if we found the GoPro serial numbers and
                // there is more than one media file on either the left or right side
                if ((sameGoProLeftMedia is not null || sameGoProRightMedia is not null) &&
                    ((sameGoProLeftMedia is not null && LeftMediaFileItemList.Count > 1) ||
                     (sameGoProRightMedia is not null && RightMediaFileItemList.Count > 1)))
                {
                    SetValidationText(true/*valid*/, SurveyGoProMatchPanel, SurveyGoProMatchGlyph, SurveyGoProMatchValidationText, "GoPro serial numbers match", "");
                }
                else
                {
                    SetValidationText(null/*hide*/, SurveyGoProMatchPanel, SurveyGoProMatchGlyph, SurveyGoProMatchValidationText, "", "");
                }
            }



            // Check media is contiguous
            bool leftMediaContiguous = CheckMediaIsContigious(LeftMediaFileItemList);
            bool rightMediaContiguous = CheckMediaIsContigious(RightMediaFileItemList);
            
            // Report if media isn't contiguous
            const string contiguousTooltip = "If there are multiple media files on either the left or right side a check is perform to ensure that the start time of a media file is consistent with the stop time of the previous media file.";
            if (!leftMediaContiguous && !rightMediaContiguous)
            {
                SetValidationText(false/*invalid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "Neither the left or right media files are contiguous", contiguousTooltip);
                mediaContigious = false;

                if (reportIssues)
                    _report?.Warning("", $"The media files for survey {surveyCode} are not contiguous on either the left or right side");
            }
            else if (!leftMediaContiguous)
            {
                SetValidationText(false/*invalid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "The left media files are not contiguous", contiguousTooltip);
                mediaContigious = false;

                if (reportIssues)
                    _report?.Warning("Left", $"The media files for survey {surveyCode} are not contiguous on the left side");
            }
            else if (!rightMediaContiguous)
            {
                SetValidationText(false/*invalid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "The right media files are not contiguous", contiguousTooltip);
                mediaContigious = false;

                if (reportIssues)
                    _report?.Warning("Right", $"The media files for survey {surveyCode} are not contiguous on the right side");
            }
            else
            {
                // Only show the validation text if we found the GoPro serial numbers and
                // there is more than one media file on either the left or right side
                if (LeftMediaFileItemList.Count > 1 || RightMediaFileItemList.Count > 1)
                {
                    SetValidationText(true/*valid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "All media is contiguous", "");
                }
                else
                {
                    SetValidationText(null/*hide*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "", "");
                }
            }


            // Check if all the media has the same resolution
            mediaSameResolution = CheckAllMediaResolutionAreTheSame();
            if (!mediaSameResolution)
            {
                SetValidationText(false/*invalid*/, SurveyResolutionMatchPanel, SurveyResolutionMatchGlyph, SurveyResolutionMatchValidationText, "All media files need have the same frame resolution", "");

                if (reportIssues)
                    _report?.Warning("", $"The media files for survey {surveyCode} are not all of the same resolution");
            }
            else
            {
                SetValidationText(true/*valid*/, SurveyResolutionMatchPanel, SurveyResolutionMatchGlyph, SurveyResolutionMatchValidationText, "All media files have the same frame resolution", "");
            }


            // Check if all the media has the same frame rate
            mediaSameFrameRate = CheckAllMediaFrameRateaAreTheSame();
            if (!mediaSameFrameRate)
            {
                SetValidationText(false/*invalid*/, SurveyFrameRateMatchPanel, SurveyFrameRateMatchGlyph, SurveyFrameRateMatchValidationText, "All media files need have the same frame rate", "");

                if (reportIssues)
                    _report?.Warning("", $"The media files for survey {surveyCode} are not all of the same frame rate");
            }
            else
            {
                SetValidationText(true/*valid*/, SurveyFrameRateMatchPanel, SurveyFrameRateMatchGlyph, SurveyFrameRateMatchValidationText, "All media files have the same frame rate", "");
            }


            // Check for warning (taking care not to downgrade the status)
            if (ret != EntryFieldsValidReturn.Invalid && (!mediaDatesMatch || !mediaContigious))
                ret = EntryFieldsValidReturn.Warning;

            // Return Invalid if any invalid data
            if (!infoValid || !mediaValid || !mediaGoProSNMatch || !mediaSameResolution || !mediaSameFrameRate)
                ret = EntryFieldsValidReturn.Invalid;


            // Should we enable to OK button if we are inside a ContentDialog
            if (IsParentContentDialog())
            {
                if (ret == EntryFieldsValidReturn.Valid || ret == EntryFieldsValidReturn.Warning)
                    ParentDialog!.IsPrimaryButtonEnabled = true;
                else
                    ParentDialog!.IsPrimaryButtonEnabled = false;
            }
                
            return ret;
        }


        /// <summary>
        /// Test if a string has characters that are valid for use in the file name
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static bool IsFileNameValid(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            // Get invalid characters from Path
            char[] invalidChars = Path.GetInvalidFileNameChars();

            // Check if fileName contains any invalid character
            return !fileName.Any(c => invalidChars.Contains(c));
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


        /// <summary>
        /// Check the Left and Right public MediaFileItem lists to confirm all the media files are in the same directory
        /// </summary>
        /// <returns></returns>
        private bool CheckAllMediaPathAreTheSame()
        {
            bool ret = true;
           
            string? path;

            if (LeftMediaFileItemList.Count > 0 && LeftMediaFileItemList[0] is not null && LeftMediaFileItemList[0].MediaFilePath is not null)
            {
                StereoMediaFileItem item = LeftMediaFileItemList[0];
                path = Path.GetDirectoryName(item.MediaFilePath);
            }
            else if (RightMediaFileItemList.Count > 0 && RightMediaFileItemList[0] is not null && RightMediaFileItemList[0].MediaFilePath is not null)
            {
                StereoMediaFileItem item = RightMediaFileItemList[0];
                path = Path.GetDirectoryName(item.MediaFilePath);
            }
            else
                return false;

            if (ret == true)
            {
                // Check all the left media files
                foreach (StereoMediaFileItem item in LeftMediaFileItemList)
                {
                    if (item.MediaFilePath is not null && string.Compare(Path.GetDirectoryName(item.MediaFilePath), path, true) != 0)
                    {
                        ret = false;
                        break;
                    }
                }
            }
            if (ret == true)
            {
                // Check all the right media files
                foreach (StereoMediaFileItem item in RightMediaFileItemList)
                {
                    if (item.MediaFilePath is not null && string.Compare(Path.GetDirectoryName(item.MediaFilePath), path, true) != 0)
                    {
                        ret = false;
                        break;
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Check if all the media files have the same date and returns that date if they all match or 
        /// null if the dates don't match
        /// </summary>
        /// <param name="mediaFileItemList"></param>
        /// <returns></returns>
        private static DateTime? CheckMediaDatesMatch(ObservableCollection<StereoMediaFileItem> mediaFileItemList)
        {
            if (mediaFileItemList.Count <= 1)
            {
                // If there's one or no item, the date is trivially the same
                return mediaFileItemList.FirstOrDefault()?.MediaFileCreateDateTime;
            }

            DateTime? firstMediaDate = mediaFileItemList[0].MediaFileCreateDateTime;

            for (int i = 1; i < mediaFileItemList.Count; i++)
            {
                DateTime? currentMediaDate = mediaFileItemList[i].MediaFileCreateDateTime;

                // Check if dates are unequal by the defined rule
                if ((firstMediaDate is null && currentMediaDate is not null) ||
                    (firstMediaDate is not null && currentMediaDate is not null &&
                     firstMediaDate.Value.Date != currentMediaDate.Value.Date))
                {
                    // If any mismatch is found, return null (indicating no match)
                    return null;
                }
            }

            // If all dates match (or are null), return the first date
            return firstMediaDate;
        }


        /// <summary>
        /// Check if all the GoPro serial number in the list match
        /// </summary>
        /// <param name="mediaFileItemList"></param>
        /// <returns></returns>
        private static bool CheckGoProSNMatch(ObservableCollection<StereoMediaFileItem> mediaFileItemList)
        {
            bool ret = true;

            if (mediaFileItemList.Count > 1)
            {
                string firstGoProSerialNumber = mediaFileItemList[0].GoProSerialNumber;

                for (int i = 1; i < mediaFileItemList.Count; i++)
                {
                    if (string.Compare(firstGoProSerialNumber, mediaFileItemList[i].GoProSerialNumber) != 0)
                    {
                        ret = false;
                        break;
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Check the left side media is from a different GoPro to the right side
        /// </summary>
        private static bool CheckGoProSNLeftAndRightAreDifferent(ObservableCollection<StereoMediaFileItem> leftMediaFileItemList, ObservableCollection<StereoMediaFileItem> rightMediaFileItemList)
        {
            // Extract unique serial numbers from both lists
            var leftSerials = new HashSet<string>(
                leftMediaFileItemList
                    .Where(item => !string.IsNullOrWhiteSpace(item.GoProSerialNumber))
                    .Select(item => item.GoProSerialNumber));

            var rightSerials = new HashSet<string>(
                rightMediaFileItemList
                    .Where(item => !string.IsNullOrWhiteSpace(item.GoProSerialNumber))
                    .Select(item => item.GoProSerialNumber));

            // Check if there is any overlap
            return !leftSerials.Overlaps(rightSerials);
        }


        /// <summary>
        /// Check the each media file follows on directly (using time) from the last media file
        /// </summary>
        /// <param name="mediaFileItemList"></param>
        /// <returns></returns>
        private static bool CheckMediaIsContigious(ObservableCollection<StereoMediaFileItem> mediaFileItemList)
        {
            bool ret = true;

            StereoMediaFileItem item;

            for (int i = 0; i < mediaFileItemList.Count - 1; i++)
            {
                item = mediaFileItemList[i];
                if (item.MediaFileCreateDateTime is not null &&
                    item.MediaFileDuration is not null &&
                    item.MediaFileCreateDateTime.HasValue &&
                    item.MediaFileDuration.HasValue)
                {
                    DateTime? endOfMediaTime = item.MediaFileCreateDateTime.Value.Add(item.MediaFileDuration.Value);
                    
                    item = mediaFileItemList[i + 1];

                    if (item.MediaFileCreateDateTime is not null && item.MediaFileCreateDateTime.HasValue)
                    {
                        if (!(endOfMediaTime <= item.MediaFileCreateDateTime && endOfMediaTime.Value.AddSeconds(1) >= item.MediaFileCreateDateTime.Value))
                        {
                            ret = false;
                            break;
                        }
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Check if all the media files have the same resolution
        /// </summary>
        /// <returns></returns>
        private bool CheckAllMediaResolutionAreTheSame()
        {
            bool ret = true;

            int? mediaFrameHeight;
            int? mediaFrameWidth;

            if (LeftMediaFileItemList.Count + RightMediaFileItemList.Count > 1)
            {
                if (LeftMediaFileItemList.Count > 0 && LeftMediaFileItemList[0] is not null)
                {
                    StereoMediaFileItem item = LeftMediaFileItemList[0];
                    mediaFrameHeight = item.MediaFrameHeight;
                    mediaFrameWidth = item.MediaFrameWidth;
                }
                else if (RightMediaFileItemList.Count > 0 && RightMediaFileItemList[0] is not null && RightMediaFileItemList[0].MediaFilePath is not null)
                {
                    StereoMediaFileItem item = RightMediaFileItemList[0];
                    mediaFrameHeight = item.MediaFrameHeight;
                    mediaFrameWidth = item.MediaFrameWidth;
                }
                else
                    return false;

                if (ret == true)
                {
                    // Check all the left media files
                    foreach (StereoMediaFileItem item in LeftMediaFileItemList)
                    {
                        if (mediaFrameHeight != item.MediaFrameHeight && mediaFrameWidth != item.MediaFrameWidth)
                        {
                            ret = false;
                            break;
                        }
                    }
                }
                if (ret == true)
                {
                    // Check all the right media files
                    foreach (StereoMediaFileItem item in RightMediaFileItemList)
                    {
                        if (mediaFrameHeight != item.MediaFrameHeight && mediaFrameWidth != item.MediaFrameWidth)
                        {
                            ret = false;
                            break;
                        }
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Check if all the media files have the same frame rate
        /// </summary>
        /// <returns></returns>
        private bool CheckAllMediaFrameRateaAreTheSame()
        {
            bool ret = true;

            double? mediaFrameRate;

            if (LeftMediaFileItemList.Count + RightMediaFileItemList.Count > 1)
            {
                if (LeftMediaFileItemList.Count > 0 && LeftMediaFileItemList[0] is not null)
                {
                    StereoMediaFileItem item = LeftMediaFileItemList[0];
                    mediaFrameRate = item.MediaFrameRate;
                }
                else if (RightMediaFileItemList.Count > 0 && RightMediaFileItemList[0] is not null && RightMediaFileItemList[0].MediaFilePath is not null)
                {
                    StereoMediaFileItem item = RightMediaFileItemList[0];
                    mediaFrameRate = item.MediaFrameRate;
                }
                else
                    return false;

                if (ret == true)
                {
                    // Check all the left media files
                    foreach (StereoMediaFileItem item in LeftMediaFileItemList)
                    {
                        if (mediaFrameRate != item.MediaFrameRate)
                        {
                            ret = false;
                            break;
                        }
                    }
                }
                if (ret == true)
                {
                    // Check all the right media files
                    foreach (StereoMediaFileItem item in RightMediaFileItemList)
                    {
                        if (mediaFrameRate != item.MediaFrameRate)
                        {
                            ret = false;
                            break;
                        }
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Get the default thumbnail for a media file. Use if a thumbnail can't be 
        /// extracted from the media file
        /// </summary>
        /// <returns></returns>
        private BitmapImage GetDefaultThumbnail()
        {
            // Get the current theme so we can figure out whether to use a dark or light default thumbnail
            BitmapImage thumbnailDefault = new();

            switch (SettingsManagerLocal.ApplicationTheme)
            {
                case ElementTheme.Dark:
                    thumbnailDefault.UriSource = new Uri($"ms-appx:///Assets/mediaDefault-dark.png");
                    break;

                case ElementTheme.Light:
                    thumbnailDefault.UriSource = new Uri($"ms-appx:///Assets/mediaDefault-light.png");
                    break;

                default:
                    var rootElement = (FrameworkElement)(Content);

                    if (rootElement.RequestedTheme == ElementTheme.Dark)
                        thumbnailDefault.UriSource = new Uri($"ms-appx:///Assets/mediaDefault-dark.png");
                    else
                        thumbnailDefault.UriSource = new Uri($"ms-appx:///Assets/mediaDefault-light.png");
                    break;
            }

            return thumbnailDefault;
        }


        /// <summary>
        /// Get the media file information from the file
        /// File properties, UTDA properties and a thumbnail
        /// </summary>
        /// <param name="file"></param>
        /// <param name="thumbnailDefault"></param>
        /// <returns></returns>
        private static async Task<StereoMediaFileItem> GetMediaFileInfoAsync(StorageFile file, BitmapImage thumbnailDefault)
        {
            StereoMediaFileItem item = new() { MediaFilePath = file.Path, MediaFileThumbnail = thumbnailDefault };

            try
            {
                // Get the file creation date
                DateTime creationTime = File.GetCreationTime(file.Path);
                item.MediaFileCreateDateTime = creationTime;

                // Get the GoPro serial number
                GpmfItemList? gpmfItemList = await GetGoProMP4StaticMetadata.ExtractPropertiesAsync(file);
                if (gpmfItemList is not null)
                {
                    GpmfItemList? gpmfItemListResult = gpmfItemList.GetItems("CASN");
                    if (gpmfItemListResult is not null && gpmfItemListResult.Count > 0)
                    {
                        GpmfItem gpmfItem = gpmfItemListResult[0];
                        if (gpmfItem is not null && gpmfItem.Payload is not null)
                            item.GoProSerialNumber = (string)gpmfItem.Payload as string;
                    }
                    else
                    {
                        item.GoProSerialNumber = "Unknown";
                    }
                }

                // Get the frame size and frame rate
                Dictionary<string, string> fileProperties = await GetMP4FileProperities.ExtractProperties(file);
                if (fileProperties.TryGetValue("Video.Width", out string? width) &&
                    fileProperties.TryGetValue("Video.Height", out string? height) &&
                    fileProperties.TryGetValue("Video.FrameRate", out string? frameRate))
                {
                    try
                    {
                        item.MediaFrameWidth = Int32.Parse(width);
                        item.MediaFrameHeight = Int32.Parse(height);
                    }
                    catch (FormatException)
                    {
                        item.MediaFrameWidth = 3840;
                        item.MediaFrameHeight = 1920;
                    }
                    try
                    {
                        item.MediaFrameRate = Double.Parse(frameRate);
                    }
                    catch (FormatException)
                    {
                        item.MediaFrameRate = 0.0;
                    }
                }


                // Get the duration
                if (fileProperties.TryGetValue("Video.Duration", out string? value))
                {
                    TimeSpan duration = TimeSpan.Parse(value);
                    item.MediaFileDuration = duration;
                }

                // Generate a thumbnail
                BitmapImage? thumbnail = await VideoThumbnailHelper.GetFileThumbnailAsync(file.Path, 128);

                if (thumbnail is not null)
                {
                    // Assign the BitmapImage to an Image control
                    item.MediaFileThumbnail = thumbnail;
                }
                else
                {
                    Console.WriteLine("Failed to retrieve thumbnail.");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
            }

            return item;
        }


        /// <summary>
        /// Clear all the dialog fields
        /// </summary>
        private void ResetDialogFields()
        {
            SurveyInfoUserControl.ResetDialogFields();

            LeftMediaFileItemList.Clear();
            RightMediaFileItemList.Clear();
        }

        /// <summary>
        /// Enable or disables the list view control buttons based on
        /// list view control selection of viable options for moving or
        /// changing the order of media files
        /// </summary>
        private void EnableDisableControlButtons()
        {

            if (LeftMediaFileNames.SelectedItem is StereoMediaFileItem selectedItem)
            {
                int index = LeftMediaFileItemList.IndexOf(selectedItem);

                // Up Button
                if (LeftMediaFileItemList.Count < 2 || index == 0)
                    MoveItemUp.IsEnabled = false;
                else
                    MoveItemUp.IsEnabled = true;

                // Down Button
                if (LeftMediaFileItemList.Count < 2 || index == LeftMediaFileItemList.Count - 1)
                    MoveItemDown.IsEnabled = false;
                else
                    MoveItemDown.IsEnabled = true;

                // Move to Right Button
                MoveItemAcrossRight.IsEnabled = true;

                // Move to Left Button (not possible)
                MoveItemAcrossLeft.IsEnabled = false;

                // Delete Button (can't delete media in the Settings window because it maybe referenced)
                if (ParentDialog is not null)
                    DeleteItem.IsEnabled = true;
                else
                    DeleteItem.IsEnabled = false;
            }
            else if (RightMediaFileNames.SelectedItem is StereoMediaFileItem rightSelectedItem)
            {
                int index = RightMediaFileItemList.IndexOf(rightSelectedItem);

                // Up Button
                if (RightMediaFileItemList.Count < 2 || index == 0)
                    MoveItemUp.IsEnabled = false;
                else
                    MoveItemUp.IsEnabled = true;

                // Down Button
                if (RightMediaFileItemList.Count < 2 || index == RightMediaFileItemList.Count - 1)
                    MoveItemDown.IsEnabled = false;
                else
                    MoveItemDown.IsEnabled = true;

                // Move to Right Button (not possible)
                MoveItemAcrossRight.IsEnabled = false;

                // Move to Left Button
                MoveItemAcrossLeft.IsEnabled = true;

                // Delete Button (can't delete media in the Settings window because it maybe referenced)
                if (ParentDialog is not null)
                    DeleteItem.IsEnabled = true;
                else
                    DeleteItem.IsEnabled = false;
            }
            else
            {
                MoveItemUp.IsEnabled = false;
                MoveItemDown.IsEnabled = false;
                MoveItemAcrossLeft.IsEnabled = false;
                MoveItemAcrossRight.IsEnabled = false;
                DeleteItem.IsEnabled = false;
            }
        }


        /// <summary>
        /// Use at the top of the function if that function is intended for use only on the 
        /// UI Thread.  This is to prevent the function being called from a non-UI thread.
        /// </summary>
        private void CheckIsUIThread()
        {
            if (!DispatcherQueue.HasThreadAccess)
                throw new InvalidOperationException("This function must be called from the UI thread");
        }


        // **END OF SurveyStereoInfoAndMedia**
    }


    public partial class StereoMediaFileItem : INotifyPropertyChanged
    {
        private string? _mediaFilePath = null;
        private BitmapImage? _mediaFileThumbnail = null;
        private string _goProSerialNumber = "";
        private int _mediaFrameWidth = 0;
        private int _mediaFrameHeight = 0;
        private double _mediaFrameRate = 0.0;
        private DateTime? _mediaFileCreateDateTime = null;
        private TimeSpan? _mediaFileDuration = null;

        public required string? MediaFilePath
        {
            get => _mediaFilePath;
            set => SetProperty(ref _mediaFilePath, value);
        }

        public BitmapImage? MediaFileThumbnail
        {
            get => _mediaFileThumbnail;
            set => SetProperty(ref _mediaFileThumbnail, value);
        }

        public string GoProSerialNumber
        {
            get => _goProSerialNumber;
            set => SetProperty(ref _goProSerialNumber, value);
        }

        public int MediaFrameWidth
        {
            get => _mediaFrameWidth;
            set => SetProperty(ref _mediaFrameWidth, value);
        }
        public int MediaFrameHeight
        {
            get => _mediaFrameHeight;
            set => SetProperty(ref _mediaFrameHeight, value);
        }
        public double MediaFrameRate
        {
            get => _mediaFrameRate;
            set => SetProperty(ref _mediaFrameRate, value);
        }
        public DateTime? MediaFileCreateDateTime
        {
            get => _mediaFileCreateDateTime;
            set => SetProperty(ref _mediaFileCreateDateTime, value);
        }

        public TimeSpan? MediaFileDuration
        {
            get => _mediaFileDuration;
            set => SetProperty(ref _mediaFileDuration, value);
        }


        // Derived property
        public string? MediaFileName => Path.GetFileName(MediaFilePath);

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}

