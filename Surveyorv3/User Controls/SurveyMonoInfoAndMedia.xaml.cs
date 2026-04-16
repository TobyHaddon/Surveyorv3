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
using System.Threading.Tasks;
using Windows.Storage;
using static Surveyor.User_Controls.SurveyInfoUserControl;

///
/// *** Remember when editing this User Control code that it is used from both   ***
/// *** the context of a ContentDialog (for a new Survey) and from a SettingCard  ***
/// *** from the SettingsWindow.                                                  ***  
///
// SurveyMonoInfoAndMedia  
// This is a user control is used to setup and edit the Survey information and media file list
// 
// Version 1.0
// Created from SurveyStereoInfoAndMedia  
// Version 1.5
// 2026-04-13 Moved to using SurveyInfoUserControl



namespace Surveyor.User_Controls
{
    public sealed partial class SurveyMonoInfoAndMedia : UserControl
    {        
        // Reporter
        private Reporter? _report = null;

        public IReadOnlyList<StorageFile>? mediaFilesSelected = null;

        private ContentDialog? ParentDialog { get; set; } = null;
        private SettingsCard? ParentSettings { get; set; } = null;        

        // Mono so there is only left side
        private ObservableCollection<MonoMediaFileItem> LeftMediaFileItemList { get; set; }

        // This copy of the survey class is only used in Settings and the setup Dialog
        // It is necessary because the is no save concept with Settings. We therefore
        // need access to the survey class to update the survey information as the user
        // makes changes in the UI
        private Survey? _survey = null;


        public SurveyMonoInfoAndMedia()
        {
            this.InitializeComponent();

            // Initialize the collection
            LeftMediaFileItemList = [];
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
        public void SetupForContentDialog(ContentDialog dialog, FieldTrip? fieldTrip, IReadOnlyList<StorageFile> _mediaFilesSelected)
        {
            Debug.WriteLine($"SetupForContentDialog() Started");
            ParentDialog = dialog;
            ParentSettings = null;  // N/A
            _survey = null;  // N/A

            // Reset Fields
            ResetDialogFields();

            // Allow all fields to be edited
            SurveyInfoUserControl.Mode = SurveyInfoMode.Setup;

            // Create a exception if not running from the ContentDialog context
            if (/*!dialog.IsLoaded || */!dialog.IsEnabled)
                throw new InvalidOperationException("This function should only be called from the context of a ContentDialog");

            // Remember the selected files
            this.mediaFilesSelected = _mediaFilesSelected;

            // Run on the UI thread
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                _ = SetupForContentDialogOnUiThreadAsync(fieldTrip);
            });

            Debug.WriteLine($"SetMediaFiles() Complete");
        }

        private async Task SetupForContentDialogOnUiThreadAsync(FieldTrip? fieldTrip)
        {
            try
            {
                SurveyInfoUserControl.SelectionChanged += SurveyInfoUserControl_SelectionChanged;

                // Load the available species list (must be before the SetFieldTrip)
                List<string> availableSpeciesLists = SpeciesCodeList.GetAvailableSpeciesLists(SpeciesListType.Fish);
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

                // Get suitable default thumbnail based on the current theme
                BitmapImage thumbnailDefault = GetDefaultThumbnail();


                // Loading from Dialog context. This means the users has provided a list of media files via
                // mediaFilesSelected
                if (mediaFilesSelected is not null && mediaFilesSelected.Count > 0)
                {
                    // Convert storage files list to a MediaFileItem list and connect other attributes: thumbnail, creation date, GoPro serial number, Frame size, etc.
                    List<MonoMediaFileItem> mediaFileItemList = [];
                    foreach (StorageFile file in mediaFilesSelected)
                    {
                        MonoMediaFileItem item = await GetMediaFileInfoAsync(file, thumbnailDefault);

                        LeftMediaFileItemList.Add(item);
                    }

                    // Bind the collection to the ListView
                    LeftMediaFileNames.ItemsSource = LeftMediaFileItemList;
                }

                // Get the full name from Windows if we are running in the ContextDialog (New Survey) context
                _ = LoadUserFullNameAsync();

                EntryFieldsValid(false/*no reporting*/);
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

            // Used to save any replicates edit
            SurveyInfoUserControl.SelectionChanged += SurveyInfoUserControl_SelectionChanged;

            // Load the available species list (must be before the SetFieldTrip)
            List<string> availableSpeciesLists = SpeciesCodeList.GetAvailableSpeciesLists(SpeciesListType.Fish);
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
                SurveyInfoUserControl.SetSurveyCode(survey.Data.Info.SurveyCode); 
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
                        MonoMediaFileItem item = await GetMediaFileInfoAsync(file, thumbnailDefault);

                        LeftMediaFileItemList.Add(item);
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

            LeftMediaFileItemList.Clear();
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
            if (LeftMediaFileNames is not null && LeftMediaFileNames.Items.Count > 0)
            {
                if (LeftMediaFileNames.Items.Count > 0)
                    surveyClass.Data.Media.MediaPath = Path.GetDirectoryName(((MonoMediaFileItem)LeftMediaFileNames.Items[0]).MediaFilePath);

                // Load left media
                foreach (MonoMediaFileItem item in LeftMediaFileNames.Items)
                {
                    if (item.MediaFileName is not null)
                        surveyClass.Data.Media.LeftMediaFileNames.Add(item.MediaFileName);
                }
                // Get and remember left GoPro serial number
                if (surveyClass.Data.Media.LeftMediaFileNames.Count > 0) 
                    surveyClass.Data.Media.LeftCameraID = ((MonoMediaFileItem)LeftMediaFileNames.Items[0]).GoProSerialNumber;

            }

            // Remember the last used analyst name
            SettingsManagerLocal.UserName = SurveyInfoUserControl.GetAnalystName();

            // Report any issues with the data
            EntryFieldsValid(true/*report*/);

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
        /// Users changed the selected item in the left media file list view. Now adjust the control 
        /// button accordingly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftMediaFileNames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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

            // Get the status of the Survey Info User Control information
            bool? surveyInfoStatus = SurveyInfoUserControl.GetValidationStatus();

            // Return Invalid if any invalid data
            if (surveyInfoStatus is null)
                ret = EntryFieldsValidReturn.Warning;
            else if ((bool)surveyInfoStatus == false)
                ret = EntryFieldsValidReturn.Invalid;
            else
                ret = EntryFieldsValidReturn.Valid;

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
        private static async Task<MonoMediaFileItem> GetMediaFileInfoAsync(StorageFile file, BitmapImage thumbnailDefault)
        {
            MonoMediaFileItem item = new() { MediaFilePath = file.Path, MediaFileThumbnail = thumbnailDefault };

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
                        item.MediaFrameWidth = 0;
                        item.MediaFrameHeight = 0;
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
        }

        /// <summary>
        /// Enable or disables the list view control buttons based on
        /// list view control selection of viable options for moving or
        /// changing the order of media files
        /// </summary>
        private void EnableDisableControlButtons()
        {
            // There are currently no control buttons so nothing to do here
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


        // **END OF SurveyMonoInfoAndMedia**
    }


    public partial class MonoMediaFileItem: INotifyPropertyChanged
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

