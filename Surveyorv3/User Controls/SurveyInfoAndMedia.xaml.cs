///
/// *** Remember when editting this User Control code that it is used from both   ***
/// *** the context of a ContentDialog (for a new Suervey) and from a SettingCard ***
/// *** from the SettingsWindow.                                                  ***  
///
// SurveyInfoAndMedia  
// This is a user control is used to setup and edit the Survey information and media file list
// 
// Version 1.0
// 
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Windows.Storage;
using CommunityToolkit.WinUI.Controls;
using Newtonsoft.Json;
using System.IO;
using Surveyor.Helper;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Linq;
using Emgu.CV;
using System;
using Microsoft.UI.Xaml.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI;
using Surveyor.DesktopWap.Helper;
using System.ComponentModel.Design;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor.User_Controls
{
    public sealed partial class SurveyInfoAndMedia : UserControl
    {
        public IReadOnlyList<StorageFile>? mediaFilesSelected = null;

        public ContentDialog? ParentDialog { get;  set; } = null;
        public SettingsCard? ParentSettings { get; set; } = null;

        private ObservableCollection<MediaFileItem> LeftMediaFileItemList { get; set; }
        private ObservableCollection<MediaFileItem> RightMediaFileItemList { get; set; }


        public SurveyInfoAndMedia()
        {
            this.InitializeComponent();

            // Initialize the collection
            LeftMediaFileItemList = [];
            RightMediaFileItemList = [];

            // Bind the collection to the ListView
            LeftMediaFileNames.ItemsSource = LeftMediaFileItemList;
            RightMediaFileNames.ItemsSource = RightMediaFileItemList;
        }


        /// <summary>
        /// Called from the function that creates the ContentDailog used to setup a new survey
        /// </summary>
        /// <param name="_mediaFilesSelected"></param>
        public void SetMediaFiles(ContentDialog dialog, IReadOnlyList<StorageFile> _mediaFilesSelected)
        {
            ParentDialog = dialog;

            // Remember the selected files
            this.mediaFilesSelected = _mediaFilesSelected;

            // Get the current theme so we can figure out whether to use a dark or light default thumbnail
            BitmapImage thumbnailDefault = new();

            switch (SettingsManager.ApplicationTheme)
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


            // Loading from Dialog context. This means the users has provided a list of media files via
            // mediaFilesSelected
            if (mediaFilesSelected is not null && mediaFilesSelected.Count > 0)
            {
                // Try to figure out which is the left and which is the right media file
                (List<string>? LeftFiles, List<string>? RightFiles, double Certainty) = DetectLeftAndRightMediaFile(mediaFilesSelected);
                // If we have a high certainty, then we can populate the left and right media file fields
                if (Certainty > 0.5)
                {
                    // Load the LeftFiles into the ListView called LeftMediaFileNames
                    if (LeftFiles is not null)
                    {
                        foreach (string file in LeftFiles)
                        {
                            MediaFileItem item = new() { MediaFilePath = file, MediaFileThumbnail = thumbnailDefault };
                            try
                            {
                                // Get the file creation date
                                DateTime creationTime = File.GetCreationTime(file);
                                item.MediaFileCreateDateTime = creationTime;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("An error occurred: " + ex.Message);
                            }
                            LeftMediaFileItemList.Add(item);
                        }
                    }
                    // Load the RightFiles into the ListView called RightMediaFileNames
                    if (RightFiles is not null)
                    {
                        foreach (string file in RightFiles)
                        {
                            MediaFileItem item = new() { MediaFilePath = file };
                            try
                            {
                                // Get the file creation date
                                DateTime creationTime = File.GetCreationTime(file);
                                item.MediaFileCreateDateTime = creationTime;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("An error occurred: " + ex.Message);
                            }
                            RightMediaFileItemList.Add(item);
                        }
                    }
                }
            }


            // Get the full name from Windows if we are running in the ContextDialog (New Survey) context
            if (IsRunningFromContentDialog())
                LoadUserFullNameAsync();


            EntryFieldsValid();
        }



        /// <summary>
        /// Save the values from the survey information fields and media into the surveyClass object
        /// </summary>
        /// <param name="surveyClass"></param>
        public void Save(Survey surveyClass)
        {
            surveyClass.Data.Info.Clear();
            surveyClass.Data.Media.Clear();

            // Save the survey information
            surveyClass.Data.Info.SurveyAnalystName = SurveyAnalystName.Text;
            surveyClass.Data.Info.SurveyDepth = SurveyDepth.Text;
            surveyClass.Data.Info.SurveyCode = SurveyCode.Text;

            // Save the media files
            if (LeftMediaFileNames is not null && RightMediaFileNames is not null && (LeftMediaFileNames.Items.Count + RightMediaFileNames.Items.Count > 0))
            {
                if (LeftMediaFileNames.Items.Count > 0)
                    surveyClass.Data.Media.MediaPath = Path.GetDirectoryName(((MediaFileItem)LeftMediaFileNames.Items[0]).MediaFilePath);
                else if (RightMediaFileNames.Items.Count > 0)
                    surveyClass.Data.Media.MediaPath = Path.GetDirectoryName(((MediaFileItem)RightMediaFileNames.Items[0]).MediaFilePath);

                // Load left media
                foreach (MediaFileItem item in LeftMediaFileNames.Items)
                {
                    if (item.MediaFileName is not null)
                        surveyClass.Data.Media.LeftMediaFileNames.Add(item.MediaFileName);
                }
                // Load right media
                foreach (MediaFileItem item in RightMediaFileNames.Items)
                {
                    if (item.MediaFileName is not null)
                        surveyClass.Data.Media.RightMediaFileNames.Add(item.MediaFileName);
                }
            }

            // Remember the last used analyst name
            SettingsManager.UserName = SurveyAnalystName.Text;
        }



        /// 
        /// EVENTS
        /// 


        private void SurveyCode_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            EntryFieldsValid();
        }

        private void SurveyDepth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EntryFieldsValid();
        }

       

        private void SurveyAnalystName_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            EntryFieldsValid();
        }

        /// <summary>
        /// Move the selected item up in it's list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemUp_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMediaFileNames.SelectedItem is MediaFileItem selectedItem)
            {
                int index = LeftMediaFileItemList.IndexOf(selectedItem);
                if (index > 0)
                {
                    LeftMediaFileItemList.Move(index, index - 1);
                }
            }
            else if (RightMediaFileNames.SelectedItem is MediaFileItem rightSelectedItem)
            {
                int index = RightMediaFileItemList.IndexOf(rightSelectedItem);
                if (index > 0)
                {
                    RightMediaFileItemList.Move(index, index - 1);
                }
            }
        }


        /// <summary>
        /// Move the selected item down in it's list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemDown_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMediaFileNames.SelectedItem is MediaFileItem selectedItem)
            {
                int index = LeftMediaFileItemList.IndexOf(selectedItem);
                if (index < LeftMediaFileItemList.Count - 1)
                {
                    LeftMediaFileItemList.Move(index, index + 1);
                }
            }
            else if (RightMediaFileNames.SelectedItem is MediaFileItem rightSelectedItem)
            {
                int index = RightMediaFileItemList.IndexOf(rightSelectedItem);
                if (index < RightMediaFileItemList.Count - 1)
                {
                    RightMediaFileItemList.Move(index, index + 1);
                }
            }
        }


        /// <summary>
        /// Move the selected item in the left list to the right list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemAcrossRight_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMediaFileNames.SelectedItem is MediaFileItem selectedItem && RightMediaFileItemList.Count < 5)
            {
                LeftMediaFileItemList.Remove(selectedItem);
                RightMediaFileItemList.Add(selectedItem);
            }
        }


        /// <summary>
        /// Move the selected item in the right list to the left list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemAcrossLeft_Click(object sender, RoutedEventArgs e)
        {
            if (RightMediaFileNames.SelectedItem is MediaFileItem selectedItem && LeftMediaFileItemList.Count < 5)
            {
                RightMediaFileItemList.Remove(selectedItem);
                LeftMediaFileItemList.Add(selectedItem);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMediaFileNames.SelectedItem is MediaFileItem selectedItem)
            {
                LeftMediaFileItemList.Remove(selectedItem);
            }
            else if (RightMediaFileNames.SelectedItem is MediaFileItem rightSelectedItem)
            {
                RightMediaFileItemList.Remove(rightSelectedItem);
            }
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {

        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Try to find a suitable user name for the SurveyAnalystName field
        /// </summary>
        private async void LoadUserFullNameAsync()
        {
            // Get the user name
            string? fullName = await UserHelper.GetUserFullNameAsync();

            // Get any previously usef name from local settings
            string? previousName = SettingsManager.UserName;
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
        /// Try to figure out which is the left and which is the right media file
        /// </summary>
        /// <param name="file1"></param>
        /// <param name="file2"></param>
        /// <returns></returns>
        private static (List<string>? LeftFiles, List<string>? RightFiles, double Certainty) DetectLeftAndRightMediaFile(IReadOnlyList<StorageFile> mediaFiles)
        {
            double certainty = 1.0;
            List<string> leftFiles = [];
            List<string> rightFiles = [];

            // Regex to identify left and right
            Regex leftRegex = new("(?i)(left|l[^a-z])");
            Regex rightRegex = new("(?i)(right|r[^a-z])");

            foreach (StorageFile file in mediaFiles)
            {
                if (file is null)
                    continue;

                string fileName = file.Name;

                // Look for certain matches
                if (leftRegex.IsMatch(fileName))
                    leftFiles.Add(file.Path);
                else if (rightRegex.IsMatch(fileName))
                    rightFiles.Add(file.Path);
                else
                {
                    string fileStem = Path.GetFileNameWithoutExtension(fileName);

                    // Look for less certain matches
                    int lastIndexForL = fileStem.LastIndexOf('L');
                    int lastIndexForR = fileStem.LastIndexOf('R');

                    if (lastIndexForL != -1 && fileStem.Length - lastIndexForL >= 2)
                    {
                        leftFiles.Add(file.Path);
                        certainty = 0.6;
                    }
                    else if (lastIndexForR != -1 && fileStem.Length - lastIndexForR >= 2)
                    {
                        rightFiles.Add(file.Path);
                        certainty = 0.6;
                    }
                }
            }


            // Default case if unable to distinguish
            return (LeftFiles: leftFiles, RightFiles: rightFiles, Certainty: certainty);
        }


        /// <summary>
        /// Called when anything change to test the validity of the survey information and media
        /// This is also shows on the users control whick fields are invalid
        /// </summary>
        /// <returns></returns>
        private bool EntryFieldsValid()
        {
            bool ret = true;
            bool infoValid = true;
            bool mediaValid = true;

            // Check survey code
            string surveyCode = SurveyCode.Text;
            if (!IsFileNameValid(surveyCode))
            {
                SetValidationText(false/*invalid*/, null, SurveyCodeValidationGlyph, SurveyCodeValidationText, @"The survey code can't contain < > : \ / | ? *", "");
                infoValid = false;
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
            }
            else
                SetValidationText(null/*nothing*/, null, SurveyDepthValidationGlyph, SurveyDepthValidationText, "", "");

            // Check Analyst name
            string analystName = SurveyAnalystName.Text;
            if (string.IsNullOrWhiteSpace(analystName))
            {
                SetValidationText(false/*invalid*/, null, SurveyAnalystNameValidationGlyph, SurveyAnalystNameValidationText, "Analyst name must have a value", "");
                infoValid = false;
            }
            else
                SetValidationText(null/*nothing*/, null, SurveyAnalystNameValidationGlyph, SurveyAnalystNameValidationText, "", "");


            // Check all media from the same path
            bool mediaPathSame = CheckAllMediaPathAreTheSame();
            if (!mediaPathSame)
            {
                SetValidationText(false/*invalid*/, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, "All media files need to be in the same directory", "");
                mediaValid = false;
            }
            else
                SetValidationText(true/*valid*/, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, "All media files are in the same directory", "");


            // Check all the media is from the same date (warning only as date maybe wrong on GoPros)


            // Check all left media from the same GoPro

            // Check all right media from the same GoPro

            // Check left media is contiguous
            bool leftMediaContiguous = true;
            leftMediaContiguous = false; // TO DO

            // Check right media is contiguous
            bool rightMediaContiguous = true;
            rightMediaContiguous = true;

            // Report if media isn't contiguous
            const string contiguousTooltip = "If there are multiple media files on either the left or right side a check is perform to ensure that the start time of a media file is consistent with the stop time of the previous media file.";
            if (!leftMediaContiguous && !rightMediaContiguous)
                SetValidationText(false/*valid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "Neither the left or right media files are contiguous", contiguousTooltip);
            else if (!leftMediaContiguous)
                SetValidationText(false/*valid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "The left media files are not contiguous", contiguousTooltip);
            else if (!rightMediaContiguous)
                SetValidationText(false/*valid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "The right media files are not contiguous", contiguousTooltip);
            else
                SetValidationText(null/*nothing*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "", "");

            if (infoValid == false || mediaValid == false)
                ret = false;

            // Should we enable to OK button if we are inside a ContentDialog
            if (ParentDialog is not null)
                ParentDialog.IsPrimaryButtonEnabled = ret;


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
        /// Check the Left and Right public MediaFileItem lists to confirm all the media files are in the same directory
        /// </summary>
        /// <returns></returns>
        private bool CheckAllMediaPathAreTheSame()
        {
            bool ret = true;
           
            string? path = null;

            if (LeftMediaFileItemList.Count > 0 && LeftMediaFileItemList[0] is not null && LeftMediaFileItemList[0].MediaFilePath is not null)
            {
                MediaFileItem item = LeftMediaFileItemList[0];
                path = Path.GetDirectoryName(item.MediaFilePath);
            }
            else if (RightMediaFileItemList.Count > 0 && RightMediaFileItemList[0] is not null && RightMediaFileItemList[0].MediaFilePath is not null)
            {
                MediaFileItem item = RightMediaFileItemList[0];
                path = Path.GetDirectoryName(item.MediaFilePath);
            }
            else
                return false;

            if (ret == true)
            {
                // Check all the left media files
                foreach (MediaFileItem item in LeftMediaFileItemList)
                {
                    if (item.MediaFilePath is not null && string.Compare(Path.GetDirectoryName(item.MediaFilePath), null, true) != 0)
                    {
                        ret = false;
                        break;
                    }
                }
            }
            if (ret == true)
            {
                // Check all the right media files
                foreach (MediaFileItem item in RightMediaFileItemList)
                {
                    if (item.MediaFilePath is not null && string.Compare(Path.GetDirectoryName(item.MediaFilePath), null, true) != 0)
                    {
                        ret = false;
                        break;
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// True is the User Control has been run from inside a ContentDialog
        /// </summary>
        /// <returns></returns>
        private bool IsRunningFromContentDialog()
        {
            return ParentDialog is not null;
        }


        /// <summary>
        /// True is the User Control has been run from inside a a SettingCard in the SettingsWindow
        /// </summary>
        /// <returns></returns>
        private bool IsRunningFromSettingsCard()
        {
            return !IsRunningFromContentDialog();
        }

        // **END OF SurveyInfoAndMedia**
    }


    public class MediaFileItem : INotifyPropertyChanged
    {
        private string? _mediaFilePath = null;
        private BitmapImage? _mediaFileThumbnail = null;
        private string _goProSerialNumber = "";
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

    

    public partial class SurveyDateTimeToStringConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dateTime && parameter is string format)
            {
                return dateTime.ToString(format);
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public partial class SurveyTimeSpanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || !(value is TimeSpan))
                return "";

            if (TimeSpan.TryParse(value.ToString(), out TimeSpan timeSpan))
            {
                return timeSpan.ToString(parameter as string);
            }

            return "Invalid";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public partial class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
