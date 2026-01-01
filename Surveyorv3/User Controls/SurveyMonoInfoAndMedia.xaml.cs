using CommunityToolkit.WinUI.Controls;
using ActionCameraMP4MetadataExtraction;
using Microsoft.UI.Dispatching;
///
/// *** Remember when editting this User Control code that it is used from both   ***
/// *** the context of a ContentDialog (for a new Survey) and from a SettingCard  ***
/// *** from the SettingsWindow.                                                  ***  
///
// SurveyMonoInfoAndMedia  
// This is a user control is used to setup and edit the Survey information and media file list
// 
// Version 1.
// Created from SurveyStereoInfoAndMedia  


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
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



namespace Surveyor.User_Controls
{
    public sealed partial class SurveyMonoInfoAndMedia : UserControl
    {        
        // Reporter
        private Reporter? report = null;

        public IReadOnlyList<StorageFile>? mediaFilesSelected = null;

        private ContentDialog? ParentDialog { get; set; } = null;
        private SettingsCard? ParentSettings { get; set; } = null;        

        private ObservableCollection<MonoMediaFileItem> LeftMediaFileItemList { get; set; }
        

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
        public void SetReporter(Reporter _report)
        {
            report = _report;
        }


        /// <summary>
        /// Called from the function that creates the ContentDailog used to setup a new survey
        /// </summary>
        /// <param name="dialog"></param>
        /// <param name="_mediaFilesSelected"></param>
        public void SetupForContentDialog(ContentDialog dialog, IReadOnlyList<StorageFile> _mediaFilesSelected)
        {
            Debug.WriteLine($"SetupForContentDialog() Started");
            ParentDialog = dialog;

            // Reset Fields
            ResetDialogFields();


            // Create a exception if not running from the ContentDialog context
            if (/*!dialog.IsLoaded || */!dialog.IsEnabled)
                throw new InvalidOperationException("This function should only be called from the context of a ContentDialog");

            // Remember the selected files
            this.mediaFilesSelected = _mediaFilesSelected;

            // Run on the UI thread
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
            {
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
                        MonoMediaFileItem item = await GetMediaFileInfo(file, thumbnailDefault);

                        LeftMediaFileItemList.Add(item);
                    }

                    // Bind the collection to the ListView
                    LeftMediaFileNames.ItemsSource = LeftMediaFileItemList;
                }

                // Get the full name from Windows if we are running in the ContextDialog (New Survey) context
                LoadUserFullNameAsync();

                EntryFieldsValid(false/*no reporting*/);

            });

            Debug.WriteLine($"SetMediaFiles() Complete");
        }


        /// <summary>
        /// Set the parent of this control as a SettingsCard
        /// This is used when this control is used view survey settings
        /// </summary>
        /// <param name="settings"></param>
        public async void SetupForSettingWindow(SettingsCard settings, Survey survey)
        {
            // Remember the parent 
            ParentSettings = settings;
            ParentDialog = null;

            // Reset Fields
            ResetDialogFields();

            // Disable UI elements not used by the SettingsCard
            SurveyCode.IsEnabled = false;       // Survey code is the name of the survey e.g. CVW-10-5-2024-07-12.
                                                // It is used as the file name and therefore can't be changed in the Setting window

            // Because the depth is also in the file name it can't be changed in the Setting window
            // The only exception is if the depth has never been set (i.e. an old .survey file)
            if (survey.Data.Info.SurveyDepth is null || (survey.Data.Info.SurveyDepth is not null && string.IsNullOrWhiteSpace(survey.Data.Info.SurveyDepth)))
            {
                SurveyDepth.IsEnabled = true;
            }
            else
            {
                SurveyDepth.IsEnabled = false;
            }


            // Load the survey code (survey name e.g. CVW-10-5-2024-07-12)
            if (!string.IsNullOrWhiteSpace(survey.Data.Info.SurveyCode))
                SurveyCode.Text = survey.Data.Info.SurveyCode;
            else
                // If the survey code is empty then use the survey file name stem
                // This maybe because the .survey file is an old verison
                SurveyCode.Text = Path.GetFileNameWithoutExtension(survey.Data.Info.SurveyFileName);


            // Load the survey depth
            // Iterate through the Survey Depth ComboBox items to find a match
            bool depthMatchFound = false;
            foreach (var item in SurveyDepth.Items)
            {
                if (item is ComboBoxItem comboBoxItem && comboBoxItem.Content.ToString() == survey.Data.Info.SurveyDepth)
                {
                    SurveyDepth.SelectedItem = comboBoxItem; // Set the matching item as selected
                    depthMatchFound = true;
                    break;
                }
            }
            // If no match was found, set the text directly
            if (!depthMatchFound)
                SurveyDepth.Text = survey.Data.Info.SurveyDepth; // Set the text property with the value


            // Load the survey analyst name
            SurveyAnalystName.Text = survey.Data.Info.SurveyAnalystName;


            // Get suitable default thubmnail based on the current theme
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
                        MonoMediaFileItem item = await GetMediaFileInfo(file, thumbnailDefault);

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
            surveyClass.Data.Info.SurveyCode = SurveyCode.Text;

            // Extract the value from the ComboBox
            if (SurveyDepth.SelectedItem is ComboBoxItem selectedItem)
            {
                surveyClass.Data.Info.SurveyDepth = selectedItem.Content.ToString();
            }
            else
            {
                // Use the typed text if no item is selected
                surveyClass.Data.Info.SurveyDepth = SurveyDepth.Text;
            }

            // Save the survey analyst name
            surveyClass.Data.Info.SurveyAnalystName = SurveyAnalystName.Text;

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
            SettingsManagerLocal.UserName = SurveyAnalystName.Text;

            // Report any issues with the data
            EntryFieldsValid(true/*report*/);

            return ret;
        }



        /// 
        /// EVENTS
        /// 

        /// <summary>
        /// Validate the buttons if the user has editted value in the dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyCode_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Validate the buttons if the user has editted value in the dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyDepth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Validate the buttons if the user has editted value in the dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyDepth_TextSubmitted(object sender, Microsoft.UI.Xaml.Controls.ComboBoxTextSubmittedEventArgs e)
        {
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Validate the buttons if the user has editted value in the dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyAnalystName_TextChanged(object sender, TextChangedEventArgs e)
        {
            EntryFieldsValid(false/*no reporting*/);
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


        /// <summary>
        /// Enter has been pressed on the combo after text entry, update the Ok button is necessary
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void SurveyDepth_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
        {
            // Setup the buttons
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Wire up the TextChanged on the child TextBox. 
        /// Note. I tried to use the Loaded event but SurveyDepthTextBox_TextChanged 
        /// was never called so I switch to GotFocus
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyDepth_GettingFocus(object sender, RoutedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            var textBox = UIHelper.FindDescendant<TextBox>(SurveyDepth);
            if (textBox is not null)
            {
                textBox.TextChanged -= SurveyDepthTextBox_TextChanged;
                textBox.TextChanged += SurveyDepthTextBox_TextChanged;
            }
        }


        /// <summary>
        /// This had to be manually wired up because there is currently no TextChanged event on a Combo UIControl
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyDepthTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Setup the buttons
            EntryFieldsValid(false/*no reporting*/);
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
        private async void LoadUserFullNameAsync()
        {
            // Get the user name
            string? fullName = await UserHelper.GetUserFullNameAsync();

            // Get any previously usef name from local settings
            string? previousName = SettingsManagerLocal.UserName;
            if (string.IsNullOrEmpty(previousName))
            {
                if (!string.IsNullOrEmpty(fullName))
                    SurveyAnalystName.Text = fullName;
                else
                    SurveyAnalystName.Text = "";
            }
            else
            {
                SurveyAnalystName.Text = previousName;
            }
        }




        /// <summary>
        /// Called when anything change to test the validity of the survey information and media
        /// This is also shows on the users control whick fields are invalid
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

            // Check survey code
            string surveyCode = SurveyCode.Text;
            if (!IsFileNameValid(surveyCode))
            {
                SetValidationText(false/*invalid*/, null, SurveyCodeValidationGlyph, SurveyCodeValidationText, @"The survey code can't contain < > : \ / | ? *", "");
                infoValid = false;

                if (reportIssues)
                    report?.Warning("", $"The survey code:{surveyCode} contains invalid characters");
            }
            else
                SetValidationText(null/*nothing*/, null, SurveyCodeValidationGlyph, SurveyCodeValidationText, "", "");


            // Check survey depth
            string? surveyDepth;

            if (SurveyDepth.SelectedItem is ComboBoxItem selectedItem && selectedItem.Content != null)
            {
                surveyDepth = selectedItem.Content.ToString();
            }
            else
            {
                // For custom user input
                surveyDepth = SurveyDepth.Text;
            }
                
            if (string.IsNullOrWhiteSpace(surveyDepth))
            {
                SetValidationText(false/*invalid*/, null, SurveyDepthValidationGlyph, SurveyDepthValidationText, "Survey depth must have a value", "");
                infoValid = false;

                if (reportIssues)
                    report?.Warning("", $"The survey depth for survey {surveyCode} is missing");
            }
            else
                SetValidationText(null/*nothing*/, null, SurveyDepthValidationGlyph, SurveyDepthValidationText, "", "");


            // Check Analyst name
            string analystName = SurveyAnalystName.Text;
            if (string.IsNullOrWhiteSpace(analystName))
            {
                SetValidationText(false/*invalid*/, null, SurveyAnalystNameValidationGlyph, SurveyAnalystNameValidationText, "Analyst name must have a value", "");
                infoValid = false;

                if (reportIssues)
                    report?.Warning("", $"The analyst name for survey {surveyCode} is missing");
            }
            else
                SetValidationText(null/*nothing*/, null, SurveyAnalystNameValidationGlyph, SurveyAnalystNameValidationText, "", "");

            // Return Invalid if any invalid data
            if (!infoValid)
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

            // Retrieve the tooltip programmatically
            bool applyTooltip = false;

            if (ToolTipService.GetToolTip(validationText) is not ToolTip existingToolTip)
            {
                applyTooltip = true;
            }
            else if ((string)existingToolTip.Content != tooltip)
            {
                // Update tooltip
                existingToolTip.Content = tooltip;
            }

            // Change the tooltip
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
        private static async Task<MonoMediaFileItem> GetMediaFileInfo(StorageFile file, BitmapImage thumbnailDefault)
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
            SurveyCode.Text = "";
            SurveyDepth.Text = string.Empty;
            SurveyDepth.SelectedItem = null;  // Clear the selected item
            SurveyDepth.SelectedIndex = -1;  // Clear the selected index
            SurveyAnalystName.Text = "";

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
        /// Use at the top of the function if that function is intended for use use only on the 
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

