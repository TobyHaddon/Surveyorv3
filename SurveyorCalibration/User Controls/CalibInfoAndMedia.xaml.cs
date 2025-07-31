///
/// *** Remember when editting this User Control code that it is used from both   ***
/// *** the context of a ContentDialog (for a new Survey) and from a SettingCard  ***
/// *** from the SettingsWindow.                                                  ***  
///
// CalibInfoAndMedia  
// This is a user control is used to setup and calibration media list
// 
// Version 1.0  01 Jun 2025
//

using Surveyor; 
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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Windows.Storage;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using System.Diagnostics;
using GoProMP4MetadataExtraction;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;



namespace Surveyor.User_Controls
{
    public sealed partial class CalibInfoAndMedia : UserControl
    {        
        public IReadOnlyList<StorageFile>? mediaFilesSelected = null;

        private ContentDialog? ParentDialog { get; set; } = null;
        private ObservableCollection<MediaFileItem> LeftMonoMediaFileItemList { get; set; }
        private ObservableCollection<MediaFileItem> RightMonoMediaFileItemList { get; set; }

        private ObservableCollection<MediaFileItem> LeftStereoMediaFileItemList { get; set; }
        private ObservableCollection<MediaFileItem> RightStereoMediaFileItemList { get; set; }


        public enum StereoMonoMediaSetMode
        {
            MonoAndStereoMediaSet,  // A stereo pair plus a mono pair
            StereoOnlyMediaSet,     // A stereo pair only
            MonoPairOnlyMediaSet,   // A mono pair only
            MonoSingleOnlyMediaSet,  // A single mono file only
            None
        };

        private StereoMonoMediaSetMode stereoMonoMediaSetMode = StereoMonoMediaSetMode.MonoAndStereoMediaSet; 

        public CalibInfoAndMedia()
        {
            this.InitializeComponent();

            // Initialize the collection
            LeftMonoMediaFileItemList = [];
            RightMonoMediaFileItemList = [];
            LeftStereoMediaFileItemList = [];
            RightStereoMediaFileItemList = [];
        }



        /// <summary>
        /// Called from the function that creates the ContentDailog used to setup a new survey
        /// </summary>
        /// <param name="dialog"></param>
        /// <param name="_mediaFilesSelected"></param>
        public void SetupForContentDialog(ContentDialog dialog)
        {
            ParentDialog = dialog;

            // Reset Fields
            ResetDialogFields();



            // Create a exception if not running from the ContentDialog context
            if (!dialog.IsEnabled)
                throw new InvalidOperationException("This function should only be called from the context of a ContentDialog");

            // Run on the UI thread
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                EnableDisableControlButtons();
                EntryFieldsValid(false/*no reporting*/);
            });
        }



        /// <summary>
        /// Free resources
        /// </summary>
        public void Shutdown()
        {
            LeftMonoMediaFileNames.ItemsSource = null;
            RightMonoMediaFileNames.ItemsSource = null;
            LeftStereoMediaFileNames.ItemsSource = null;
            RightStereoMediaFileNames.ItemsSource = null;

            LeftMonoMediaFileItemList.Clear();
            RightMonoMediaFileItemList.Clear();
            LeftStereoMediaFileItemList.Clear();
            RightStereoMediaFileItemList.Clear();
        }


        /// <summary>
        /// Save the values from the survey information fields and media into the surveyClass 
        /// object
        /// </summary>
        /// <param name="calibProject"></param>
        public void SaveForContentDialog(CalibProject calibProject)
        {
            calibProject.Data.Media.Clear();

            calibProject.Data.Media.StereoMonoMediaSetMode = stereoMonoMediaSetMode; 

            // Save the media files
            if (LeftMonoMediaFileNames is not null && RightMonoMediaFileNames is not null &&
                (LeftMonoMediaFileNames.Items.Count == 1 && RightMonoMediaFileNames.Items.Count == 1))
            {
                //if (LeftMediaFileNames.Items.Count > 0)
                //    calibProject.Data.Media.MediaPath = Path.GetDirectoryName(((MediaFileItem)LeftMediaFileNames.Items[0]).MediaFilePath);
                //else if (RightMediaFileNames.Items.Count > 0)
                //    calibProject.Data.Media.MediaPath = Path.GetDirectoryName(((MediaFileItem)RightMediaFileNames.Items[0]).MediaFilePath);


                MediaFileItem leftMonoMediaFileItem = (MediaFileItem)LeftMonoMediaFileNames.Items[0];
                if (leftMonoMediaFileItem is not null && leftMonoMediaFileItem.MediaFilePath is not null)
                {
                    // Load mono left media
                    calibProject.Data.Media.LeftMonoMP4Path = leftMonoMediaFileItem.MediaFilePath;

                    // Get and remember left GoPro serial number (try the left mono)
                    calibProject.Data.Media.LeftCameraID = leftMonoMediaFileItem.GoProSerialNumber;
                }


                MediaFileItem rightMonoMediaFileItem = (MediaFileItem)RightMonoMediaFileNames.Items[0];
                if (rightMonoMediaFileItem is not null && rightMonoMediaFileItem.MediaFilePath is not null)
                {
                    // Load mono right media
                    calibProject.Data.Media.RightMonoMP4Path = rightMonoMediaFileItem.MediaFilePath; ;

                    // Get and remember right GoPro serial number  (try the right mono)                 
                    calibProject.Data.Media.RightCameraID = rightMonoMediaFileItem.GoProSerialNumber;
                }
            }
            else if (LeftMonoMediaFileNames is not null && LeftMonoMediaFileNames.Items.Count == 1)
            {
                MediaFileItem leftMonoMediaFileItem = (MediaFileItem)LeftMonoMediaFileNames.Items[0];
                if (leftMonoMediaFileItem is not null && leftMonoMediaFileItem.MediaFilePath is not null)
                {
                    // Load mono left media
                    calibProject.Data.Media.LeftMonoMP4Path = leftMonoMediaFileItem.MediaFilePath;

                    // Get and remember left GoPro serial number (try the left mono)
                    calibProject.Data.Media.LeftCameraID = leftMonoMediaFileItem.GoProSerialNumber;
                }
            }

            if (LeftStereoMediaFileNames is not null && RightStereoMediaFileNames is not null &&
                (LeftStereoMediaFileNames.Items.Count == 1 && RightStereoMediaFileNames.Items.Count == 1))
            {
                // Load stereo left media
                MediaFileItem leftStereoMediaFileItem = (MediaFileItem)LeftStereoMediaFileNames.Items[0];
                if (leftStereoMediaFileItem is not null && leftStereoMediaFileItem.MediaFilePath is not null)
                {
                    calibProject.Data.Media.LeftStereoMP4Path = leftStereoMediaFileItem.MediaFilePath;

                    // Get and remember left GoPro serial number (try the left stereo)
                    calibProject.Data.Media.LeftCameraID = leftStereoMediaFileItem.GoProSerialNumber;
                }

                // Load stereo right media
                MediaFileItem rightStereoMediaFileItem = (MediaFileItem)RightStereoMediaFileNames.Items[0];
                if (rightStereoMediaFileItem is not null && rightStereoMediaFileItem.MediaFilePath is not null)
                {
                    calibProject.Data.Media.RightStereoMP4Path = rightStereoMediaFileItem.MediaFilePath;

                    // Get and remember right GoPro serial number (try the right stereo)
                    calibProject.Data.Media.RightCameraID = rightStereoMediaFileItem.GoProSerialNumber;
                }
            }


            // Just to set variables up correctly
            StereoMonoRadioButtons_SelectionChanged(null!, null!);
            // Report any issues with the data
            EntryFieldsValid(true/*report*/);
        }


        /// 
        /// EVENTS
        /// 



        /// <summary>
        /// Radio buttons for stereo or mono media selection changed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>        
        private void StereoMonoRadioButtons_SelectionChanged(object sender, RoutedEventArgs e)
        {
            StereoMonoMediaSetMode newStereoMonoMediaSetMode = StereoMonoMediaSetMode.None;

            if (MonoAndStereoMediaSetRadioButton.IsChecked == true)
            {
                // Handle mono+stereo selection
                newStereoMonoMediaSetMode = StereoMonoMediaSetMode.MonoAndStereoMediaSet;
            }
            else if (StereoOnlyMediaSetRadioButton.IsChecked == true)
            {
                // Handle stereo-only selection
                newStereoMonoMediaSetMode = StereoMonoMediaSetMode.StereoOnlyMediaSet;
            }
            else if (MonoPairOnlyMediaSetRadioButton.IsChecked == true)
            {
                // Handle mono pair selection
                newStereoMonoMediaSetMode = StereoMonoMediaSetMode.MonoPairOnlyMediaSet;
            }
            else if (MonoSingleOnlyMediaSetRadioButton.IsChecked == true)
            {
                // Handle single mono file selection
                newStereoMonoMediaSetMode = StereoMonoMediaSetMode.MonoSingleOnlyMediaSet;
            }
            else
            {
                // No valid selection
                newStereoMonoMediaSetMode = StereoMonoMediaSetMode.None;
            }


            if (newStereoMonoMediaSetMode != stereoMonoMediaSetMode)
            {
                stereoMonoMediaSetMode = newStereoMonoMediaSetMode;
                EnableDisableControlButtons();
            }
        }


        /// <summary>
        /// User clicked the button to select the calibration media files.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SelectCalibrationMedia_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();

            // Associate the picker with the window handle (required in WinUI 3)
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);

            InitializeWithWindow.Initialize(picker, hwnd);

            // File type filter
            picker.FileTypeFilter.Add(".mp4");
            

            var files = await picker.PickMultipleFilesAsync();
            if (files != null)
            {
                // Use the selected file(s)
                CalibrationMediaSelected(files);

                Debug.WriteLine($"Selected media calibration file(s): {files.Count}");
            }            
        }






        /// <summary>
        /// Move the selected item in the left list to the right list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemAcrossTopRight_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMonoMediaFileNames.SelectedItem is MediaFileItem selectedItem && RightMonoMediaFileItemList.Count < 5)
            {
                LeftMonoMediaFileItemList.Remove(selectedItem);
                RightMonoMediaFileItemList.Add(selectedItem);
            }
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Move the selected item in the right list to the left list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemAcrossTopLeft_Click(object sender, RoutedEventArgs e)
        {
            if (RightMonoMediaFileNames.SelectedItem is MediaFileItem selectedItem && 
                LeftMonoMediaFileItemList.Count < 5)
            {
                RightMonoMediaFileItemList.Remove(selectedItem);
                LeftMonoMediaFileItemList.Add(selectedItem);
            }
            EntryFieldsValid(false/*no reporting*/);
        }

        /// <summary>
        /// Move the selected item in the left list to the right list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemAcrossBottomRight_Click(object sender, RoutedEventArgs e)
        {
            if (LeftStereoMediaFileNames.SelectedItem is MediaFileItem selectedItem && RightStereoMediaFileItemList.Count < 5)
            {
                LeftStereoMediaFileItemList.Remove(selectedItem);
                RightStereoMediaFileItemList.Add(selectedItem);
            }
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Move the selected item in the right list to the left list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveItemAcrossBottomLeft_Click(object sender, RoutedEventArgs e)
        {
            if (RightStereoMediaFileNames.SelectedItem is MediaFileItem selectedItem &&
                LeftStereoMediaFileItemList.Count < 5)
            {
                RightStereoMediaFileItemList.Remove(selectedItem);
                LeftStereoMediaFileItemList.Add(selectedItem);
            }
            EntryFieldsValid(false/*no reporting*/);
        }

        /// <summary>
        /// Move the selected item up from the left stereo list view to the left mono list view
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftSideMoveItemUp_Click(object sender, RoutedEventArgs e)
        {
            if (LeftStereoMediaFileNames.SelectedItem is MediaFileItem selectedItem && 
                LeftMonoMediaFileItemList.Count < 5)
            {
                LeftStereoMediaFileItemList.Remove(selectedItem);
                LeftMonoMediaFileItemList.Add(selectedItem);
            }
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Move the selected item down from the left mono list view to the left stereo list view
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftSideMoveItemDown_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMonoMediaFileNames.SelectedItem is MediaFileItem selectedItem &&
                LeftStereoMediaFileItemList.Count < 5)
            {
                LeftMonoMediaFileItemList.Remove(selectedItem);
                LeftStereoMediaFileItemList.Add(selectedItem);
            }
            EntryFieldsValid(false/*no reporting*/);
        }


        /// <summary>
        /// Move the selected item up from the right stereo list view to the right mono list view
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightSideMoveItemUp_Click(object sender, RoutedEventArgs e)
        {

        }


        /// <summary>
        /// Move the selected item down from the right mono list view to the right stereo list view
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightSideMoveItemDown_Click(object sender, RoutedEventArgs e)
        {

        }


        /// <summary>
        /// Delete the selected item from the list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (LeftMonoMediaFileNames.SelectedItem is MediaFileItem selectedItem)
            {
                LeftMonoMediaFileItemList.Remove(selectedItem);
            }
            else if (RightMonoMediaFileNames.SelectedItem is MediaFileItem rightSelectedItem)
            {
                RightMonoMediaFileItemList.Remove(rightSelectedItem);
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
        private void LeftMonoMediaFileNames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Remove any existing seleced item in the other (right list view)
            if (e.AddedItems.Count > 0)
            {
                RightMonoMediaFileNames.SelectedIndex = -1;
                LeftStereoMediaFileNames.SelectedIndex = -1;
                RightStereoMediaFileNames.SelectedIndex = -1;
            }

            // Setup the buttons
            EnableDisableControlButtons();
        }


        /// <summary>
        /// Users changed the selected item in the right media file list view. Now adjust the control 
        /// button accordingly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightMonoMediaFileNames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Remove any existing seleced item in the other (left list view)
            if (e.AddedItems.Count > 0)
            {
                LeftMonoMediaFileNames.SelectedIndex = -1;
                LeftStereoMediaFileNames.SelectedIndex = -1;
                RightStereoMediaFileNames.SelectedIndex = -1;
            }

            // Setup the buttons
            EnableDisableControlButtons();
        }


        /// <summary>
        /// Users changed the selected item in the left media file list view. Now adjust the control 
        /// button accordingly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LeftStereoMediaFileNames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Remove any existing seleced item in the other (right list view)
            if (e.AddedItems.Count > 0)
            {
                LeftMonoMediaFileNames.SelectedIndex = -1;
                RightMonoMediaFileNames.SelectedIndex = -1;
                RightStereoMediaFileNames.SelectedIndex = -1;
            }

            // Setup the buttons
            EnableDisableControlButtons();
        }


        /// <summary>
        /// Users changed the selected item in the right media file list view. Now adjust the control 
        /// button accordingly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RightStereoMediaFileNames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Remove any existing seleced item in the other (left list view)
            if (e.AddedItems.Count > 0)
            {
                LeftMonoMediaFileNames.SelectedIndex = -1;
                RightMonoMediaFileNames.SelectedIndex = -1;
                LeftStereoMediaFileNames.SelectedIndex = -1;
            }

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


        private void CalibrationMediaSelected(IReadOnlyList<StorageFile> _mediaFilesSelected)
        {

            // Remember the selected files
            this.mediaFilesSelected = _mediaFilesSelected;

            // Run on the UI thread
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
            {
                // Get suitable default thubmnail based on the current theme
                BitmapImage thumbnailDefault = GetDefaultThumbnail();


                // Loading from Dialog context. This means the users has provided a list of media files via
                // mediaFilesSelected
                if (mediaFilesSelected is not null && mediaFilesSelected.Count > 0)
                {
                    // Convert storage files list to a MediaFileItem list and connect other attributes: thumbnail, creation date, GoPro serial number, Frame size, etc.
                    List<MediaFileItem> mediaFileItemList = [];
                    foreach (StorageFile file in mediaFilesSelected)
                    {
                        MediaFileItem item = await GetMediaFileInfo(file, thumbnailDefault);

                        mediaFileItemList.Add(item);
                    }

                    switch (stereoMonoMediaSetMode)
                    {
                        case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                            // Try to figure out which is the left and which is the right media file                        
                            (LeftStereoMediaFileItemList, RightStereoMediaFileItemList, LeftMonoMediaFileItemList, RightMonoMediaFileItemList, _) = SplitIntoStereoAndMonoChannels(mediaFileItemList);

                            // Bind the collection to the ListView
                            LeftMonoMediaFileNames.ItemsSource = LeftMonoMediaFileItemList;
                            RightMonoMediaFileNames.ItemsSource = RightMonoMediaFileItemList;
                            LeftStereoMediaFileNames.ItemsSource = LeftStereoMediaFileItemList;
                            RightStereoMediaFileNames.ItemsSource = RightStereoMediaFileItemList;
                            break;
                        case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                            (LeftStereoMediaFileItemList, RightStereoMediaFileItemList, _) = DetectLeftAndRightMediaFile(mediaFileItemList);

                            // Bind the collection to the ListView
                            LeftStereoMediaFileNames.ItemsSource = LeftStereoMediaFileItemList;
                            RightStereoMediaFileNames.ItemsSource = RightStereoMediaFileItemList;
                            break;
                        case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                            (LeftMonoMediaFileItemList, RightMonoMediaFileItemList, _) = DetectLeftAndRightMediaFile(mediaFileItemList);

                            // Bind the collection to the ListView
                            LeftMonoMediaFileNames.ItemsSource = LeftMonoMediaFileItemList;
                            RightMonoMediaFileNames.ItemsSource = RightMonoMediaFileItemList;
                            break;
                        case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                            LeftMonoMediaFileItemList.Add(mediaFileItemList[0]);
                            LeftMonoMediaFileNames.ItemsSource = LeftMonoMediaFileItemList;
                            break;
                    }
                }

                EntryFieldsValid(false/*no reporting*/);

            });

            Debug.WriteLine($"SetMediaFiles() Complete");
        }


        /// <summary>
        /// Used to split a list of file names into 1) Stereo Left, 
        /// 2) Stereo Right, 3) Mono Left and 4) Mono right
        /// </summary>
        /// <param name="allFiles"></param>
        /// <param name="stereoLeft"></param>
        /// <param name="stereoRight"></param>
        /// <param name="monoLeft"></param>
        /// <param name="monoRight"></param>
        private static (ObservableCollection<MediaFileItem> LeftStereoFiles, ObservableCollection<MediaFileItem> RightStereoFiles, ObservableCollection<MediaFileItem> LeftMonoFiles, ObservableCollection<MediaFileItem> RightMonoFiles, double Certainty) SplitIntoStereoAndMonoChannels(
                        List<MediaFileItem> allFiles)
        {
            ObservableCollection<MediaFileItem> stereoLeft = [];
            ObservableCollection<MediaFileItem> stereoRight = [];
            ObservableCollection<MediaFileItem> monoLeft = [];
            ObservableCollection<MediaFileItem> monoRight = [];
            double certainty = 0;

            // Step 1: Extract stereo files
            var stereoRegex = new Regex("stereo", RegexOptions.IgnoreCase);
            var stereoFiles = allFiles.Where(f => f?.MediaFileName is not null && stereoRegex.IsMatch(f.MediaFileName)).ToList();
            var monoCandidates = allFiles.Except(stereoFiles).ToList();

            // Step 2: Classify stereo files
            var (stereoLeftFiles, stereoRightFiles, stereoCertainty) = DetectLeftAndRightMediaFile(stereoFiles);
            foreach (var file in stereoLeftFiles) stereoLeft.Add(file);
            foreach (var file in stereoRightFiles) stereoRight.Add(file);

            // Step 3: Classify mono files
            var (monoLeftFiles, monoRightFiles, monoCertainty) = DetectLeftAndRightMediaFile(monoCandidates);
            foreach (var file in monoLeftFiles) monoLeft.Add(file);
            foreach (var file in monoRightFiles) monoRight.Add(file);

            certainty = (stereoCertainty + monoCertainty) / 2;

            return (stereoLeft, stereoRight, monoLeft, monoRight, certainty);
        }


        /// <summary>
        /// Try to figure out which is the left and which is the right media file
        /// </summary>
        /// <param name="file1"></param>
        /// <param name="file2"></param>
        /// <returns></returns>
        private static (ObservableCollection<MediaFileItem> LeftFiles, ObservableCollection<MediaFileItem> RightFiles, double Certainty) DetectLeftAndRightMediaFile(List<MediaFileItem> mediaFiles)
        {
            double certainty = 1.0;
            ObservableCollection<MediaFileItem> leftFiles = [];
            ObservableCollection<MediaFileItem> rightFiles = [];

            // Regex to identify and isolcate 'L' or 'R'
            // Regex pattern explanation:
            // (?<![a-zA-Z]) - Ensures there is NO letter before 'L'
            // L             - Matches uppercase 'L'
            // (?![a-zA-Z])  - Ensures there is NO letter after 'L'
            Regex leftIsolatedRegex = new(@"(?<![a-zA-Z])L(?![a-zA-Z])");
            Regex rightIsolatedRegex = new(@"(?<![a-zA-Z])R(?![a-zA-Z])");

            // Regex to identify left and right or l or r
            Regex leftSimpleRegex = new("(?i)(left|l[^a-z])");
            Regex rightSimpleRegex = new("(?i)(right|r[^a-z])");

            foreach (MediaFileItem file in mediaFiles)
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
            //???bool mediaSameFrameRate;   // Set later
            bool mediaDatesMatch = true;
            bool mediaContigious = true;


            //

            // Check all media from the same path
            bool mediaPathSame = CheckAllMediaPathAreTheSame();
            if (!mediaPathSame)
            {
                SetValidationText(false/*invalid*/, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, "All media files need to be in the same directory", "");
                mediaValid = false;

            }
            else
            {
                // No need to show anything if the media is all from the same path
                SetValidationText(null, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, ""/*"All media files are in the same directory"*/, "");
            }


            
            DateTime? sameDateMonoLeftMedia = CheckMediaDatesMatch(LeftMonoMediaFileItemList);
            DateTime? sameDateMonoRightMedia = CheckMediaDatesMatch(RightMonoMediaFileItemList);
            DateTime? sameDateStereoLeftMedia = CheckMediaDatesMatch(LeftStereoMediaFileItemList);
            DateTime? sameDateStereoRightMedia = CheckMediaDatesMatch(RightStereoMediaFileItemList);

            // Check if media has been selected
            switch (stereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    if (sameDateMonoLeftMedia is null ||
                        sameDateMonoRightMedia is null ||
                        sameDateStereoLeftMedia is null ||
                        sameDateStereoRightMedia is null)
                    {
                        SetValidationText(false/*invalid*/, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, "We need a calibration file for mono left & right and stereo left & right", "");
                        mediaValid = false;
                    }
                    break;

                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    if (sameDateStereoLeftMedia is null ||
                        sameDateStereoRightMedia is null)
                    {
                        SetValidationText(false/*invalid*/, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, "We need a calibration file for stereo left & right", "");
                        mediaValid = false;
                    }
                    break;

                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    if (sameDateMonoLeftMedia is null ||
                        sameDateMonoRightMedia is null)
                    {
                        SetValidationText(false/*invalid*/, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, "We need a calibration file for mono left & right", "");
                        mediaValid = false;
                    }
                    break;

                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    if (sameDateMonoLeftMedia is null)
                    {
                        SetValidationText(false/*invalid*/, SurveyMediaPathPanel, SurveyMediaPathGlyph, SurveyMediaPathValidationText, "We need a mono calibration file", "");
                        mediaValid = false;
                    }
                    break;

                default:
                    mediaValid = false;
                    break;
            }

            // Check all the media is from the same date (warning only as date maybe wrong on GoPros)
            if (sameDateMonoLeftMedia is not null && sameDateMonoRightMedia is not null && sameDateStereoLeftMedia is not null && sameDateStereoRightMedia is not null)
            {
                if (!(sameDateMonoLeftMedia.Value.Date == sameDateMonoRightMedia.Value.Date &&
                     sameDateMonoRightMedia.Value.Date == sameDateStereoLeftMedia.Value.Date &&
                     sameDateStereoLeftMedia.Value.Date == sameDateStereoRightMedia.Value.Date))
                {  
                    SetValidationText(false/*invalid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "The media file dates differ", "The date of the media files do not match each other.\nThis can happen if the dates on the GoPro isn't set correctly and isn't a problem. However if the dates are set correctly this is a problem.");
                    mediaDatesMatch = false;
                }
                else
                {
                    SetValidationText(true/*valid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "The media files are all from the same date", "");
                }
            }
            else if ((sameDateMonoLeftMedia is null && LeftMonoMediaFileItemList.Count > 0) && sameDateMonoRightMedia is not null)
            {
                SetValidationText(false/*invalid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "Not all the media on the left side has the same date", "You would expect all the dates on the media to be the same.");

            }
            else if (sameDateMonoLeftMedia is not null && (sameDateMonoRightMedia is null && RightMonoMediaFileItemList.Count > 0))
            {
                SetValidationText(false/*invalid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "Not all the media on the right side has the same date", "You would expect all the dates on the media to be the same.");
            }
            else if ((sameDateMonoLeftMedia is null && LeftMonoMediaFileItemList.Count > 0) && (sameDateMonoRightMedia is null && RightMonoMediaFileItemList.Count > 0))
            {
                SetValidationText(false/*invalid*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "Not all the media on the left side and on the right side has the same date", "You would expect all the dates on the media to be the same.");
            }
            else
            {
                SetValidationText(null/*hide*/, SurveyMediaDatePanel, SurveyMediaDateGlyph, SurveyMediaDateValidationText, "", "");
            }


            // Check all left & right media from the same GoPro
            bool? sameGoProLeftMedia = CheckGoProSNMatch(LeftMonoMediaFileItemList, LeftStereoMediaFileItemList);    // Will return Null if no GoPro serial number found or there is only one left media file
            bool? sameGoProRightMedia = CheckGoProSNMatch(RightMonoMediaFileItemList, RightStereoMediaFileItemList);  // Will return Null if no GoPro serial number found or there is only one right media file

            // Report on the status of the GoPro serial numbers in the media set
            string mediaGoProSNMatchWarningText = "";
            string mediaGoProSNMatchWarningToolTip = "";
            
            if ((sameGoProLeftMedia is null/*No S/N*/ || (sameGoProLeftMedia is not null && (bool)sameGoProLeftMedia)) && (sameGoProRightMedia is not null && !(bool)sameGoProRightMedia))
            {
                mediaGoProSNMatchWarningText = "The right media files are not all from the same GoPro";
                mediaGoProSNMatchWarningToolTip = "No all the serial numbers embedded in the right media files match";
                mediaGoProSNMatch = false;

            }
            else if ((sameGoProLeftMedia is not null && !(bool)sameGoProLeftMedia) && (sameGoProRightMedia is null/*No S/N*/ || (sameGoProRightMedia is not null && (bool)sameGoProRightMedia)))
            {
                mediaGoProSNMatchWarningText = "The left media files are not all from the same GoPro";
                mediaGoProSNMatchWarningToolTip = "No all the serial numbers embedded in the left media files match";
                mediaGoProSNMatch = false;

             }
            else if ((sameGoProLeftMedia is not null && !(bool)sameGoProLeftMedia) && (sameGoProRightMedia is not null && !(bool)sameGoProRightMedia))
            {
                mediaGoProSNMatchWarningText = "The media files on each side need to be from their specific GoPro";
                mediaGoProSNMatchWarningToolTip = "All the media files on the left side need to be from the left GoPro and all the media files on the right side need to be from right GoPro. From the GoPro serial numbers embedded in the MP4 files this is not currently the case.";
                mediaGoProSNMatch = false;

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
                    ((sameGoProLeftMedia is not null && LeftMonoMediaFileItemList.Count > 1) ||
                     (sameGoProRightMedia is not null && RightMonoMediaFileItemList.Count > 1)))
                {
                    SetValidationText(true/*valid*/, SurveyGoProMatchPanel, SurveyGoProMatchGlyph, SurveyGoProMatchValidationText, "GoPro serial numbers match", "");
                }
                else
                {
                    SetValidationText(null/*hide*/, SurveyGoProMatchPanel, SurveyGoProMatchGlyph, SurveyGoProMatchValidationText, "", "");
                }
            }



            // Check media is contiguous
            bool leftMediaContiguous = CheckMediaIsContigious(LeftMonoMediaFileItemList);
            bool rightMediaContiguous = CheckMediaIsContigious(RightMonoMediaFileItemList);
            
            // Report if media isn't contiguous
            const string contiguousTooltip = "If there are multiple media files on either the left or right side a check is perform to ensure that the start time of a media file is consistent with the stop time of the previous media file.";
            if (!leftMediaContiguous && !rightMediaContiguous)
            {
                SetValidationText(false/*invalid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "Neither the left or right media files are contiguous", contiguousTooltip);
                mediaContigious = false;

            }
            else if (!leftMediaContiguous)
            {
                SetValidationText(false/*invalid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "The left media files are not contiguous", contiguousTooltip);
                mediaContigious = false;

            }
            else if (!rightMediaContiguous)
            {
                SetValidationText(false/*invalid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "The right media files are not contiguous", contiguousTooltip);
                mediaContigious = false;

            }
            else
            {
                // Only show the validation text if we found the GoPro serial numbers and
                // there is more than one media file on either the left or right side
                if (LeftMonoMediaFileItemList.Count > 1 || RightMonoMediaFileItemList.Count > 1)
                {
                    SetValidationText(true/*valid*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "All media is contingious", "");
                }
                else
                {
                    SetValidationText(null/*hdie*/, SurveyMediaContiguousPanel, SurveyMediaContiguousGlyph, SurveyMediaContiguousValidationText, "", "");
                }
            }


            // Check if all the media has the same resolution
            if (LeftMonoMediaFileItemList.Count + RightMonoMediaFileItemList.Count + LeftStereoMediaFileItemList.Count + RightStereoMediaFileItemList.Count > 0)
            {
                mediaSameResolution = CheckAllMediaResolutionAreTheSame();
                if (!mediaSameResolution)
                {
                    SetValidationText(false/*invalid*/, SurveyResolutionMatchPanel, SurveyResolutionMatchGlyph, SurveyResolutionMatchValidationText, "All media files need have the same frame resolution", "");

                }
                else
                {
                    SetValidationText(true/*valid*/, SurveyResolutionMatchPanel, SurveyResolutionMatchGlyph, SurveyResolutionMatchValidationText, "All media files have the same frame resolution", "");
                }
            }
            else
            {
                mediaSameResolution = true;
                SetValidationText(null, SurveyResolutionMatchPanel, SurveyResolutionMatchGlyph, SurveyResolutionMatchValidationText, "", "");
            }

            // Check if all the media has the same frame rate
            //??? We don't frame rates to be the same for calibration
            //mediaSameFrameRate = CheckAllMediaFrameRateaAreTheSame();
            //if (!mediaSameFrameRate)
            //{
            //    SetValidationText(false/*invalid*/, SurveyFrameRateMatchPanel, SurveyFrameRateMatchGlyph, SurveyFrameRateMatchValidationText, "All media files need have the same frame rate", "");

            //}
            //else
            //{
            //    SetValidationText(true/*valid*/, SurveyFrameRateMatchPanel, SurveyFrameRateMatchGlyph, SurveyFrameRateMatchValidationText, "All media files have the same frame rate", "");
            //}


            // Check for warning
            if (!mediaDatesMatch || !mediaContigious)
                ret = EntryFieldsValidReturn.Warning;

            // Return Invalid if any invalid data
            if (!infoValid || !mediaValid || !mediaGoProSNMatch || !mediaSameResolution /*???|| !mediaSameFrameRate*/)
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
        /// Check the Left and Right public MediaFileItem lists to confirm all the media files are in the same directory
        /// </summary>
        /// <returns></returns>
        private bool CheckAllMediaPathAreTheSame()
        {
            bool ret = true;
           
            string? path;

            // Check that there are any files
            if (LeftMonoMediaFileItemList.Count + RightMonoMediaFileItemList.Count + LeftMonoMediaFileItemList.Count + RightMonoMediaFileItemList.Count > 1)
            {
                // Find one file to compare the other to
                if (LeftMonoMediaFileItemList.Count > 0 && LeftMonoMediaFileItemList[0] is not null && LeftMonoMediaFileItemList[0].MediaFilePath is not null)
                {
                    MediaFileItem item = LeftMonoMediaFileItemList[0];
                    path = Path.GetDirectoryName(item.MediaFilePath);
                }
                else if (RightMonoMediaFileItemList.Count > 0 && RightMonoMediaFileItemList[0] is not null && RightMonoMediaFileItemList[0].MediaFilePath is not null)
                {
                    MediaFileItem item = RightMonoMediaFileItemList[0];
                    path = Path.GetDirectoryName(item.MediaFilePath);
                }
                else if (LeftStereoMediaFileItemList.Count > 0 && LeftStereoMediaFileItemList[0] is not null && LeftStereoMediaFileItemList[0].MediaFilePath is not null)
                {
                    MediaFileItem item = LeftStereoMediaFileItemList[0];
                    path = Path.GetDirectoryName(item.MediaFilePath);
                }
                else if (RightStereoMediaFileItemList.Count > 0 && RightStereoMediaFileItemList[0] is not null && RightStereoMediaFileItemList[0].MediaFilePath is not null)
                {
                    MediaFileItem item = RightStereoMediaFileItemList[0];
                    path = Path.GetDirectoryName(item.MediaFilePath);
                }
                else
                    return false;

                // Now check the other paths
                if (ret == true)
                {
                    // Check all the left media files
                    foreach (MediaFileItem item in LeftMonoMediaFileItemList)
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
                    foreach (MediaFileItem item in RightMonoMediaFileItemList)
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
                    // Check all the left media files
                    foreach (MediaFileItem item in LeftStereoMediaFileItemList)
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
                    foreach (MediaFileItem item in RightStereoMediaFileItemList)
                    {
                        if (item.MediaFilePath is not null && string.Compare(Path.GetDirectoryName(item.MediaFilePath), path, true) != 0)
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
        /// Check if all the media files have the same date and returns that date if they all match or 
        /// null if the dates don't match
        /// </summary>
        /// <param name="mediaFileMonoItemList"></param>
        /// <returns></returns>
        private static DateTime? CheckMediaDatesMatch(ObservableCollection<MediaFileItem> mediaFileMonoItemList)
        {
            if (mediaFileMonoItemList.Count <= 1)
            {
                // If there's one or no item, the date is effectively the same
                return mediaFileMonoItemList.FirstOrDefault()?.MediaFileCreateDateTime;
            }

            DateTime? firstMediaDate = mediaFileMonoItemList[0].MediaFileCreateDateTime;

            for (int i = 1; i < mediaFileMonoItemList.Count; i++)
            {
                DateTime? currentMediaDate = mediaFileMonoItemList[i].MediaFileCreateDateTime;

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
        private static bool CheckGoProSNMatch(ObservableCollection<MediaFileItem> mediaFileMonoItemList, ObservableCollection<MediaFileItem> mediaFileStereoItemList)
        {
            bool ret = true;

            string firstGoProSerialNumber = string.Empty;

            if (mediaFileMonoItemList.Count > 1)
            {
                firstGoProSerialNumber = mediaFileMonoItemList[0].GoProSerialNumber;
            }
            if (firstGoProSerialNumber == string.Empty && mediaFileMonoItemList.Count > 1)
            {
                firstGoProSerialNumber = mediaFileMonoItemList[0].GoProSerialNumber;
            }

            for (int i = 1; i < mediaFileMonoItemList.Count; i++)
            {
                if (string.Compare(firstGoProSerialNumber, mediaFileMonoItemList[i].GoProSerialNumber) != 0)
                {
                    ret = false;
                    break;
                }
            }
            if (ret)
            {
                for (int i = 1; i < mediaFileStereoItemList.Count; i++)
                {
                    if (string.Compare(firstGoProSerialNumber, mediaFileStereoItemList[i].GoProSerialNumber) != 0)
                    {
                        ret = false;
                        break;
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Check the each media file follows on directly (using time) from the last media file
        /// </summary>
        /// <param name="mediaFileItemList"></param>
        /// <returns></returns>
        private static bool CheckMediaIsContigious(ObservableCollection<MediaFileItem> mediaFileItemList)
        {
            bool ret = true;

            MediaFileItem item;

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

            if (LeftMonoMediaFileItemList.Count + RightMonoMediaFileItemList.Count + LeftStereoMediaFileItemList.Count + RightStereoMediaFileItemList.Count > 1)
            {
                if (LeftMonoMediaFileItemList.Count > 0 && LeftMonoMediaFileItemList[0] is not null && LeftMonoMediaFileItemList[0].MediaFilePath is not null)
                {
                    MediaFileItem item = LeftMonoMediaFileItemList[0];
                    mediaFrameHeight = item.MediaFrameHeight;
                    mediaFrameWidth = item.MediaFrameWidth;
                }
                else if (RightMonoMediaFileItemList.Count > 0 && RightMonoMediaFileItemList[0] is not null && RightMonoMediaFileItemList[0].MediaFilePath is not null)
                {
                    MediaFileItem item = RightMonoMediaFileItemList[0];
                    mediaFrameHeight = item.MediaFrameHeight;
                    mediaFrameWidth = item.MediaFrameWidth;
                }
                else if (LeftStereoMediaFileItemList.Count > 0 && LeftStereoMediaFileItemList[0] is not null && LeftStereoMediaFileItemList[0].MediaFilePath is not null)
                {
                    MediaFileItem item = LeftStereoMediaFileItemList[0];
                    mediaFrameHeight = item.MediaFrameHeight;
                    mediaFrameWidth = item.MediaFrameWidth;
                }
                else if (RightStereoMediaFileItemList.Count > 0 && RightStereoMediaFileItemList[0] is not null && RightStereoMediaFileItemList[0].MediaFilePath is not null)
                {
                    MediaFileItem item = RightStereoMediaFileItemList[0];
                    mediaFrameHeight = item.MediaFrameHeight;
                    mediaFrameWidth = item.MediaFrameWidth;
                }
                else
                    return false;

                if (ret == true)
                {
                    // Check all the left mono media files
                    foreach (MediaFileItem item in LeftMonoMediaFileItemList)
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
                    // Check all the right mono media files
                    foreach (MediaFileItem item in RightMonoMediaFileItemList)
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
                    // Check all the left stereo media files
                    foreach (MediaFileItem item in LeftStereoMediaFileItemList)
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
                    // Check all the right stereo media files
                    foreach (MediaFileItem item in RightStereoMediaFileItemList)
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
        //private bool CheckAllMediaFrameRateaAreTheSame()
        //{
        //    bool ret = true;

        //    double? mediaFrameRate;

        //    if (LeftMonoMediaFileItemList.Count + RightMonoMediaFileItemList.Count > 1)
        //    {
        //        if (LeftMonoMediaFileItemList.Count > 0 && LeftMonoMediaFileItemList[0] is not null)
        //        {
        //            MediaFileItem item = LeftMonoMediaFileItemList[0];
        //            mediaFrameRate = item.MediaFrameRate;
        //        }
        //        else if (RightMonoMediaFileItemList.Count > 0 && RightMonoMediaFileItemList[0] is not null && RightMonoMediaFileItemList[0].MediaFilePath is not null)
        //        {
        //            MediaFileItem item = RightMonoMediaFileItemList[0];
        //            mediaFrameRate = item.MediaFrameRate;
        //        }
        //        else
        //            return false;

        //        if (ret == true)
        //        {
        //            // Check all the left media files
        //            foreach (MediaFileItem item in LeftMonoMediaFileItemList)
        //            {
        //                if (mediaFrameRate != item.MediaFrameRate)
        //                {
        //                    ret = false;
        //                    break;
        //                }
        //            }
        //        }
        //        if (ret == true)
        //        {
        //            // Check all the right media files
        //            foreach (MediaFileItem item in RightMonoMediaFileItemList)
        //            {
        //                if (mediaFrameRate != item.MediaFrameRate)
        //                {
        //                    ret = false;
        //                    break;
        //                }
        //            }
        //        }
        //    }

        //    return ret;
        //}


        /// <summary>
        /// Get the default thumbnail for a media file. Use if a thumbnail can't be 
        /// extracted from the media file
        /// </summary>
        /// <returns></returns>
        private BitmapImage GetDefaultThumbnail()
        {
            // Get the current theme so we can figure out whether to use a dark or light default thumbnail
            BitmapImage thumbnailDefault = new();

            switch (ElementTheme.Light/*SettingsManagerLocal.ApplicationTheme*/)
            {
                //case ElementTheme.Dark:
                //    thumbnailDefault.UriSource = new Uri($"ms-appx:///Assets/mediaDefault-dark.png");
                //    break;

                case ElementTheme.Light:
                    thumbnailDefault.UriSource = new Uri($"ms-appx:///Assets/mediaDefault-light.png");
                    break;

                default:
                    //var rootElement = (FrameworkElement)(Content);

                    //if (rootElement.RequestedTheme == ElementTheme.Dark)
                    //    thumbnailDefault.UriSource = new Uri($"ms-appx:///Assets/mediaDefault-dark.png");
                    //else
                    //    thumbnailDefault.UriSource = new Uri($"ms-appx:///Assets/mediaDefault-light.png");
//                    break;
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
        private static async Task<MediaFileItem> GetMediaFileInfo(StorageFile file, BitmapImage thumbnailDefault)
        {
            MediaFileItem item = new() { MediaFilePath = file.Path, MediaFileThumbnail = thumbnailDefault };

            try
            {
                // Get the file creation date
                DateTime creationTime = File.GetCreationTime(file.Path);
                item.MediaFileCreateDateTime = creationTime;

                // Get the GoPro serial number
                GpmfItemList? gpmfItemList = await GetMP4UtdaFileProperities.ExtractPropertiesAsync(file);
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
                BitmapImage? thumbnail = await VideoThumbnailHelper.GetBitmapImageFromVideoAsync(file.Path);

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
            // Default to Mono and Stereo Media Set
            stereoMonoMediaSetMode = StereoMonoMediaSetMode.MonoAndStereoMediaSet;
            MonoAndStereoMediaSetRadioButton.IsChecked = true;

            LeftMonoMediaFileItemList.Clear();
            RightMonoMediaFileItemList.Clear();
            LeftStereoMediaFileItemList.Clear();
            RightStereoMediaFileItemList.Clear();
        }

        /// <summary>
        /// Enable or disables the list view control buttons based on
        /// list view control selection of viable options for moving or
        /// changing the order of media files
        /// </summary>
        private GridLength monoListViewRowHeight = new GridLength(0);
        private GridLength stereoListViewRowHeight = new GridLength(0);
        private void EnableDisableControlButtons()
        {
            bool moveItemAcrossTopRightIsEnabled = false;
            bool moveItemAcrossTopLeftIsEnabled = false;
            bool leftSideMoveItemDownIsEnabled = false;
            bool leftSideMoveItemUpIsEnabled = false;
            bool moveItemAcrossBottomRightIsEnabled = false;
            bool moveItemAcrossBottomLeftIsEnabled = false;
            bool rightSideMoveItemUpIsEnabled = false;
            bool rightSideMoveItemDownIsEnabled = false;
            bool deleteItemIsEnabled = false;


            // Remember the current heights on initial load
            if (monoListViewRowHeight == new GridLength(0))
                monoListViewRowHeight = MonoListViewRow.Height;
            if (stereoListViewRowHeight == new GridLength(0))
                stereoListViewRowHeight = MonoListViewRow.Height;


            switch (stereoMonoMediaSetMode)
            {
                case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                    // Restore the mono & stereo title grid row
                    MonoTitleRow.Height = GridLength.Auto;
                    StereoTitleRow.Height = GridLength.Auto;

                    // Restore the mono listview grid row
                    MonoListViewRow.Height = monoListViewRowHeight;
                    StereoListViewRow.Height = stereoListViewRowHeight;

                    // Show right mono
                    RightMonoMediaFileNames.IsEnabled = true;

                    // Hide the up/down buttons
                    LeftSideMoveItemUp.Visibility = Visibility.Visible;
                    LeftSideMoveItemDown.Visibility = Visibility.Visible;
                    RightSideMoveItemUp.Visibility = Visibility.Visible;
                    RightSideMoveItemDown.Visibility = Visibility.Visible;
                    break;

                case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    // Ensure no mono item is selected
                    LeftMonoMediaFileNames.SelectedItem = null;
                    RightMonoMediaFileNames.SelectedItem = null;

                    // Hide the mono title grid row and restore the stereo
                    MonoTitleRow.Height = new GridLength(0);
                    StereoTitleRow.Height = GridLength.Auto;

                    // Hide the mono listview grid row and restore the stereo
                    MonoListViewRow.Height = new GridLength(0);
                    StereoListViewRow.Height = monoListViewRowHeight;

                    // Hide the up/down buttons
                    LeftSideMoveItemUp.Visibility = Visibility.Collapsed;
                    LeftSideMoveItemDown.Visibility = Visibility.Collapsed;
                    RightSideMoveItemUp.Visibility = Visibility.Collapsed;
                    RightSideMoveItemDown.Visibility = Visibility.Collapsed;
                    break;

                case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                    // Ensure no stereo item is selected
                    LeftStereoMediaFileNames.SelectedItem = null;
                    RightStereoMediaFileNames.SelectedItem = null;

                    // Restore thr mono and Hide the stereo title grid row
                    MonoTitleRow.Height = GridLength.Auto;
                    StereoTitleRow.Height = new GridLength(0);

                    // Restore the mono and hide the stereo listview grid row
                    MonoListViewRow.Height = monoListViewRowHeight;
                    StereoListViewRow.Height = new GridLength(0);


                    // Show right mono
                    RightMonoMediaFileNames.IsEnabled = true;

                    // Hide the up/down buttons
                    LeftSideMoveItemUp.Visibility = Visibility.Collapsed;
                    LeftSideMoveItemDown.Visibility = Visibility.Collapsed;
                    RightSideMoveItemUp.Visibility = Visibility.Collapsed;
                    RightSideMoveItemDown.Visibility = Visibility.Collapsed;
                    break;

                case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                    // Ensure no stereo item is selected
                    LeftStereoMediaFileNames.SelectedItem = null;
                    RightStereoMediaFileNames.SelectedItem = null;

                    // Restore the mono and hide the stereo title grid row
                    MonoTitleRow.Height = GridLength.Auto;
                    StereoTitleRow.Height = new GridLength(0);

                    // Restore the mono and hide the stereo listview grid row
                    MonoListViewRow.Height = monoListViewRowHeight;
                    StereoListViewRow.Height = new GridLength(0);

                    // Hide right mono
                    RightMonoMediaFileNames.IsEnabled = false;

                    // Hide the up/down buttons
                    LeftSideMoveItemUp.Visibility = Visibility.Collapsed;
                    LeftSideMoveItemDown.Visibility = Visibility.Collapsed;
                    RightSideMoveItemUp.Visibility = Visibility.Collapsed;
                    RightSideMoveItemDown.Visibility = Visibility.Collapsed;
                    break;
            }



            if (LeftMonoMediaFileNames.SelectedItem is MediaFileItem)
            {
                // Move to Right top listview button
                moveItemAcrossTopRightIsEnabled = true;

                // Move to Left bottom listview button
                leftSideMoveItemDownIsEnabled = true;

                // Delete enabled
                deleteItemIsEnabled = true;
            }
            else if (RightMonoMediaFileNames.SelectedItem is MediaFileItem)
            {
                // Move to Right top listview button
                moveItemAcrossTopLeftIsEnabled = true;

                // Move to Left bottom listview button
                rightSideMoveItemDownIsEnabled = true;

                // Delete enabled
                deleteItemIsEnabled = true;
            }
            else if (LeftStereoMediaFileNames.SelectedItem is MediaFileItem)
            {
                // Move to Right bottom listview button
                moveItemAcrossBottomRightIsEnabled = true;

                // Move to Left top listview button
                leftSideMoveItemUpIsEnabled = true;

                // Delete enabled
                deleteItemIsEnabled = true;
            }
            else if (RightStereoMediaFileNames.SelectedItem is MediaFileItem)
            {
                // Move to Right bottom listview button
                moveItemAcrossBottomLeftIsEnabled = true;

                // Move to Left top listview button
                rightSideMoveItemUpIsEnabled = true;

                // Delete enabled
                deleteItemIsEnabled = true;
            }
          

            MoveItemAcrossTopRight.IsEnabled = moveItemAcrossTopRightIsEnabled;
            MoveItemAcrossTopLeft.IsEnabled = moveItemAcrossTopLeftIsEnabled;
            LeftSideMoveItemDown.IsEnabled = leftSideMoveItemDownIsEnabled;
            LeftSideMoveItemUp.IsEnabled = leftSideMoveItemUpIsEnabled;
            MoveItemAcrossBottomRight.IsEnabled = moveItemAcrossBottomRightIsEnabled;
            MoveItemAcrossBottomLeft.IsEnabled = moveItemAcrossBottomLeftIsEnabled;
            RightSideMoveItemUp.IsEnabled = rightSideMoveItemUpIsEnabled;
            RightSideMoveItemDown.IsEnabled = rightSideMoveItemDownIsEnabled;
            DeleteItem.IsEnabled = deleteItemIsEnabled;

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






        // **END OF CalibSurveyInfoAndMedia**
    }


    public partial class MediaFileItem : INotifyPropertyChanged
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


    /// <summary>
    /// This converter is used by the XAML to convert a DateTime to a string
    /// </summary>
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


    /// <summary>
    /// This converter is used by the XAML to convert a TimeSpan to a string
    /// </summary>
    public partial class SurveyTimeSpanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || value is not TimeSpan)
                return "";

            if (TimeSpan.TryParse(value.ToString(), out TimeSpan timeSpan))
            {
                string format = parameter as string ?? @"hh\:mm\:ss";
                return timeSpan.ToString(format);
            }

            return "Invalid";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// This converter is used by the XAML to hide a whole StackPanel if a string null in one of it's elements is blank or null
    /// </summary>
    public partial class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string valueString)
                return string.IsNullOrWhiteSpace(valueString) == true ? Visibility.Collapsed : Visibility.Visible;
            else
                return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}

