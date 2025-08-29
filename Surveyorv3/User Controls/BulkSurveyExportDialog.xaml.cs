using CommunityToolkit.WinUI.UI.Controls;
using GoProMP4MetadataExtraction;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using OfficeOpenXml;
using Surveyor.Events;
using Surveyor.Helper;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;
using WinUIEx;
using static Surveyor.Survey.DataClass;



namespace Surveyor.User_Controls
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class BulkSurveyExportDialog : WindowEx
    {
        public ObservableCollection<SurveyFileEntry> SurveyFiles { get; set; } = [];

        private bool noSaveOptionSelected_NoExportAllowed = false;

        // Reporter
        private readonly Reporter? report = null;

        // Export Excel columns
        public enum ExportExcelColmns { SurveyName = 1,
                                        Depth,
                                        Transect,
                                        Analyst,
                                        Time,
                                        TimeSecs,
                                        Type,
                                        FishCount,
                                        Measurement,
                                        Range,
                                        HorizontalOffset,
                                        VerticalOffset,
                                        RMS,
                                        RMSWorst,
                                        RulesPassed,
                                        Species,
                                        Genus,
                                        Family,
                                        SpeciesCode,
                                        NoSpeciesCode,
                                        Comment };

        public BulkSurveyExportDialog(Reporter? _report)
        {
            // Remember the reporter
            report = _report;

            // Remove the separate title bar from the window
            ExtendsContentIntoTitleBar = true;

            this.InitializeComponent();
            SurveyGrid.ItemsSource = SurveyFiles;

            // Subscribe to datagrid changes and keep the selected count at the bottom up-to-date
            SurveyFiles.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (SurveyFileEntry item in e.NewItems)
                    {
                        item.PropertyChanged += (s2, e2) =>
                        {
                            if (e2.PropertyName == nameof(SurveyFileEntry.Include))
                            {
                                UpdateItemCountText();
                                UpdateButtons();
                                UpdateSelectAllCheckBoxState();
                            }
                        };
                    }
                }               
            };

            // Initial update
            UpdateSelectAllCheckBoxState();
            UpdateButtons();
            RebuildTotalsRow();
        }

        private int selectFolderEntryCount = 0;
        private async void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int entryCount = Interlocked.Increment(ref selectFolderEntryCount);
                // Make sure we only open the settings window once.
                // This can happen if the survey and movies are loaded and the user clicks the settings a few times.
                if (entryCount == 1)
                { 
                    var folderPicker = new FolderPicker();
                    folderPicker.FileTypeFilter.Add("*");
                    WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    StorageFolder folder = await folderPicker.PickSingleFolderAsync();
                    if (folder != null)
                    {
                        FolderPathTextBox.Text = folder.Path;
                        await LoadSurveyFiles(folder.Path, 
                                              IncludeSubfoldersCheckBox.IsChecked == true, 
                                              false/*recalce*/, 
                                              false/*Save back to survey files*/);
                        UpdateButtons();
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during the process
                Debug.WriteLine($"BulkSurveyExportDialog.SelectFolder_Click Error {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref selectFolderEntryCount);
            }

        }

        private async Task LoadSurveyFiles(string path, bool includeSubfolders, bool recalc, bool save, CalibrationData? calibrationData = null)
        {
            SurveyFiles.Clear();
            ItemCountTextBlock.Text = "";
            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            StereoProjection stereoProjection = new();

            // Run the expensive work on a background thread
            var fileEntries = await Task.Run(async () =>
            {
                var entries = new List<SurveyFileEntry>();
                var files = SafeEnumerateFiles(path, "*.survey", includeSubfolders).ToList();

                foreach (var fileSpec in files)
                {
                    try
                    {
                        // Open the survey with no auto save
                        var survey = new Survey(report!);
                        if (await survey.SurveyLoad(fileSpec, false/*autoSave*/) == 0)
                        {
                            // Get the frame size
                            int frameWidth = 0;
                            int frameHeight = 0;

                            // Used the left mp4 file to get the frame size
                            string mediaFileLeft = survey.GetLeftMediaFileSpec(0);

                            // Get the frame size and frame rate
                            Dictionary<string, string> fileProperties = await GetMP4FileProperities.ExtractProperties(mediaFileLeft);
                            if (fileProperties.TryGetValue("Video.Width", out string? width) &&
                                fileProperties.TryGetValue("Video.Height", out string? height))
                            {
                                try
                                {
                                    frameWidth = Int32.Parse(width);
                                    frameHeight = Int32.Parse(height);
                                }
                                catch (Exception ex)
                                {
                                    report?.Error("", $"Failed to parse video frame size from {mediaFileLeft}: {ex.Message}");
                                }
                            }

                            // Force a recalc?
                            if (recalc == true)
                            {
                                // Set the calibration data for stereo projection
                                if (calibrationData is not null)
                                {
                                    CalibrationClass Calibration = new();
                                    Calibration.CalibrationDataList.Add(calibrationData);
                                    Calibration.PreferredCalibrationDataIndex = 0;

                                    stereoProjection.SetCalibrationData(Calibration);
                                }
                                else
                                    stereoProjection.SetCalibrationData(survey.Data.Calibration);

                                if (frameWidth != 0 && frameHeight != 0)
                                {
                                    stereoProjection.SetFrameSize(frameWidth, frameHeight);

                                    bool ret = await SurveyMeasurementHelper.CheckIfEventMeasurementsAreUpToDate(
                                                            stereoProjection,
                                                            survey,
                                                            frameWidth,
                                                            frameHeight,
                                                            null/*no UI this.Content.XamlRoot*/,
                                                            true/*forceReCalc*/);

                                    if (ret)
                                    {
                                        // Is a save required
                                        if (save)
                                        {
                                            survey.SurveySave();
                                        }
                                    }
                                }
                            }


                            string fileName = Path.GetFileName(fileSpec);

                            // Check if the Survey File Name and the Survey Code are conistent
                            string surveyNameAndCodeCheck = CheckSurveyFileNameSurveyFileNameAndSurveyCodeAreConsistent(fileName, survey.Data.Info.SurveyFileName ?? "", survey.Data.Info.SurveyCode ?? "");

                            string surveyCode = survey.Data.Info.SurveyCode ?? "";
                            string leftMediaFile = survey.Data.Media.LeftMediaFileNames[0] ?? "missing";
                            string rightMediaFile = survey.Data.Media.RightMediaFileNames[0] ?? "missing";
                            string surveyPath = survey.Data.Info.SurveyPath ?? "missing";
                            string mediaPath = survey.Data.Media.MediaPath ?? "missing";
                            string depth = survey.Data.Info.SurveyDepth ?? "missing";

                            int totalMeasurements = survey.Data.Events.EventList.Count(e => e.EventDataType == Events.SurveyDataType.SurveyMeasurementPoints);
                            int total3DPoints = survey.Data.Events.EventList.Count(e => e.EventDataType == Events.SurveyDataType.SurveyStereoPoint);
                            int totalSinglePoints = survey.Data.Events.EventList.Count(e => e.EventDataType == Events.SurveyDataType.SurveyPoint);
                            int totalEntries = totalMeasurements + total3DPoints + totalSinglePoints;

                            // Count SurveyMeasurementPoints with null SurveyRules
                            int countSurveyMeasurementPointsWithNullRules = survey.Data.Events.EventList
                                .Where(e => e.EventDataType == SurveyDataType.SurveyMeasurementPoints)
                                .Select(e => e.EventData as SurveyMeasurement)
                                .Count(data => data?.SurveyRulesCalc?.SurveyRules == null);

                            // Count SurveyStereoPoint with null SurveyRules
                            int countSurveyStereoPointsWithNullRules = survey.Data.Events.EventList
                                .Where(e => e.EventDataType == SurveyDataType.SurveyStereoPoint)
                                .Select(e => e.EventData as SurveyStereoPoint)
                                .Count(data => data?.SurveyRulesCalc?.SurveyRules == null);

                            // Total SurveyMeasurementPoints and SurveyStereoPoint with blank rules calcs
                            int totalCountWithNullRules = countSurveyMeasurementPointsWithNullRules + countSurveyStereoPointsWithNullRules;

                            // Count SurveyMeasurementPoints with failed SurveyRules
                            int countSurveyMeasurementPointsWithFailedRules = survey.Data.Events.EventList
                                .Where(e => e.EventDataType == SurveyDataType.SurveyMeasurementPoints)
                                .Select(e => e.EventData as SurveyMeasurement)
                                .Count(data => data?.SurveyRulesCalc?.SurveyRules == false);

                            // Count SurveyStereoPoint with failed SurveyRules
                            int countSurveyStereoPointsWithFailedRules = survey.Data.Events.EventList
                                .Where(e => e.EventDataType == SurveyDataType.SurveyStereoPoint)
                                .Select(e => e.EventData as SurveyStereoPoint)
                                .Count(data => data?.SurveyRulesCalc?.SurveyRules == false);

                            // Total SurveyMeasurementPoints and SurveyStereoPoint with failed rules calcs
                            int totalCountWithFailedRules = countSurveyMeasurementPointsWithFailedRules + countSurveyStereoPointsWithFailedRules;

                            int countSpeciesNull = survey.Data.Events.EventList.Count(e =>
                                (e.EventDataType == SurveyDataType.SurveyMeasurementPoints &&
                                string.IsNullOrEmpty((e.EventData as SurveyMeasurement)?.SpeciesInfo?.Species)) ||

                                (e.EventDataType == SurveyDataType.SurveyStereoPoint &&
                                string.IsNullOrEmpty((e.EventData as SurveyStereoPoint)?.SpeciesInfo?.Species)) ||

                                (e.EventDataType == SurveyDataType.SurveyPoint &&
                                string.IsNullOrEmpty((e.EventData as SurveyPoint)?.SpeciesInfo?.Species))
                            );



                            // Create a List<string> of all the different transect used in the survey
                            List<string> transectMarkerList = [.. survey.Data.Events.EventList
                                                .Where(e => e.EventDataType == SurveyDataType.SurveyStart)
                                                .Select(e => (e.EventData as TransectMarker)?.MarkerName ?? "")
                                                .Where(name => !string.IsNullOrWhiteSpace(name))
                                                .Distinct()];
                            string transectList = string.Join(", ", transectMarkerList);

                            // Check for the range rule
                            string rangeRule = string.Empty;
                            if (survey.Data.SurveyRules.SurveyRulesActive)
                            {
                                if (survey.Data.SurveyRules.SurveyRulesData.RangeRuleActive)
                                {
                                    rangeRule = $"{survey.Data.SurveyRules.SurveyRulesData.RangeMin:F1}m - {survey.Data.SurveyRules.SurveyRulesData.RangeMax:F1}m";
                                }
                                else
                                {
                                    rangeRule = "No range";
                                }
                            }
                            else
                                rangeRule = "No rules";

                            // Check for the RMS rule
                            string rmsRule = string.Empty;
                            if (survey.Data.SurveyRules.SurveyRulesActive)
                            {
                                if (survey.Data.SurveyRules.SurveyRulesData.RMSRuleActive)
                                {
                                    rmsRule = $"RMS<{Math.Round(survey.Data.SurveyRules.SurveyRulesData.RMSMax, MidpointRounding.AwayFromZero):F0}mm";
                                }
                                else
                                {
                                    rmsRule = "No RMS";
                                }
                            }
                            else
                                rmsRule = "No rules";


                            // Check the number of measurement and 3D points where the rules have not been applied
                            string rulesCalcNull = string.Empty;
                            if (totalCountWithNullRules > 0)
                            {
                                rulesCalcNull = $"{totalCountWithNullRules} missing";
                            }
                            else
                            {
                                rulesCalcNull = "Ok";
                            }

                            // Check the number of measurement and 3D points where the rules have failed
                            string rulesCalcFailed = string.Empty;
                            if (totalCountWithFailedRules > 0)
                            {
                                rulesCalcFailed = $"{totalCountWithFailedRules} failed";
                            }
                            else
                            {
                                rulesCalcFailed = "All passed";
                            }

                            // Check the number of measurment, 3D points and single points where the species has not been set
                            string species = string.Empty;
                            if (countSpeciesNull > 0)
                            {
                                species = $"{countSpeciesNull} missing";
                            }
                            else
                            {
                                species = "Ok";
                            }

                            // Check for the horizontal range rule
                            string horizontalRule = string.Empty;
                            if (survey.Data.SurveyRules.SurveyRulesActive)
                            {
                                if (survey.Data.SurveyRules.SurveyRulesData.HorizontalRangeRuleActive)
                                {
                                    horizontalRule = $"{survey.Data.SurveyRules.SurveyRulesData.HorizontalRangeLeft:F2}m ← → {survey.Data.SurveyRules.SurveyRulesData.HorizontalRangeRight:F2}m";
                                }
                                else
                                {
                                    horizontalRule = "No hortizontal";
                                }
                            }
                            else
                                horizontalRule = "No rules";

                            // Check for the horizontal range rule
                            string verticalRule = string.Empty;
                            if (survey.Data.SurveyRules.SurveyRulesActive)
                            {
                                if (survey.Data.SurveyRules.SurveyRulesData.VerticalRangeRuleActive)
                                {
                                    verticalRule = $"{survey.Data.SurveyRules.SurveyRulesData.VerticalRangeTop:F2}m ↑ ↓ {survey.Data.SurveyRules.SurveyRulesData.VerticalRangeBottom:F2}m";
                                }
                                else
                                {
                                    verticalRule = "No vertical";
                                }
                            }
                            else
                                verticalRule = "No rules";


                            // Get the rules hash so consistancy of rules can be checked across surveys
                            int rulesHash = survey.Data.SurveyRules.GetHashCode();


                            // Check for calibration
                            string calibration = string.Empty;
                            SurveyorCalibrationData.CalibrationData? calibrationDataPreferred = survey.Data.Calibration.GetPreferredCalibationData(frameWidth, frameHeight);
                            if (calibrationDataPreferred is not null)
                            {
                                if (calibrationDataPreferred.StereoCameraCalibration.RMS != 0)
                                {
                                    calibration = $"RMS:{calibrationDataPreferred.StereoCameraCalibration.RMS * 1000:F2}";
                                }
                                else
                                {
                                    calibration = "Set";
                                }
                            }
                            else 
                            {
                                calibration = "None";
                            }

                            // Get calibration Hash
                            string calibrationHash = string.Empty;
                            if (calibrationDataPreferred is not null)
                            {
                                calibrationDataPreferred = survey.Data.Calibration.CalibrationDataList[survey.Data.Calibration.PreferredCalibrationDataIndex];
                                calibrationHash = $"{calibrationDataPreferred.GetHashCode():x8}";
                            }
                            else
                            {
                                calibration = "None";
                            }


                            // Check a sync point was setup (should only be one per survey video)
                            string syncPoint = string.Empty;
                            int totalSyncPoints = survey.Data.Events.EventList.Count(e => e.EventDataType == Events.SurveyDataType.StereoSyncPoint);
                            if (totalSyncPoints == 1)
                            {
                                syncPoint = "Set";
                            }
                            else if (totalSyncPoints == 0)
                            {
                                syncPoint = "None";
                            }
                            else
                            {
                                syncPoint = $"{totalSyncPoints}!";
                            }

                            // Analyst
                            string analyst = survey.Data.Info.SurveyAnalystName ?? "";

                            // Create the SurveyFileEntry object
                            entries.Add(new SurveyFileEntry
                            {
                                Include = true,
                                FilePath = fileSpec,
                                FileName = (string.IsNullOrEmpty(surveyNameAndCodeCheck) ? "" : "*") + fileName + surveyNameAndCodeCheck,
                                Depth = depth,
                                TotalMeasurements = totalMeasurements,
                                Total3DPoints = total3DPoints,
                                TotalSinglePoints = totalSinglePoints,
                                TotalEntries = totalEntries,
                                TransectList = transectList,
                                RulesRange = rangeRule,
                                RulesHorizontal = horizontalRule,
                                RulesVertical = verticalRule,
                                RulesHash = rulesHash,
                                RulesRMS = rmsRule,
                                RulesCalcNull = rulesCalcNull,          // Count of where the rules actually applied
                                RulesCalcFailed = rulesCalcFailed,      // Count of where the rules have failed
                                Species = species,
                                Calibration = calibration,
                                CalibrationHash = calibrationHash,
                                SyncPoint = syncPoint,
                                Analyst = analyst,
                                SurveyCode = surveyCode,
                                LeftMediaFile = leftMediaFile,
                                RightMediaFile = rightMediaFile,
                                SurveyPath = surveyPath,
                                MediaPath = mediaPath

                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        report?.Warning("", $"Failed to load {fileSpec}: {ex.Message}");
                    }
                }

                return entries;
            });

            // Check if the media files have been used more than once
            var duplicateMediaFiles = fileEntries
                    .SelectMany(entry => new[] { entry.LeftMediaFile, entry.RightMediaFile })
                    .Where(file => !string.IsNullOrWhiteSpace(file))
                    .GroupBy(file => file, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => new { MediaFile = g.Key, Count = g.Count() })
                    .ToList();

            var duplicatedFileSet = new HashSet<string>(
                    duplicateMediaFiles.Select(d => d.MediaFile),
                    StringComparer.OrdinalIgnoreCase
            );

            // Back on UI thread: add to ObservableCollection
            foreach (var entry in fileEntries)
            {
                // Flag any media files used multiple time
                if (duplicatedFileSet.Contains(entry.LeftMediaFile))
                {
                    entry.LeftMediaFile = "*" + entry.LeftMediaFile;
                }
                if (duplicatedFileSet.Contains(entry.RightMediaFile))
                {
                    entry.RightMediaFile = "*" + entry.RightMediaFile;
                }

                await Task.Delay(10); // Throttle to avoid UI freeze
                SurveyFiles.Add(entry);
            }
            
            RebuildTotalsRow();

            // Check for non-matching rules
            if (!CheckThatAllRulesMatch(fileEntries))
            {
                SetValidationText(false/*invalid*/, RulesMismatchPanel, RulesMismatchGlyph, RulesMismatchValidationText, "Not every survey has the same rules settings", "Check the different rules columns to find the survey(s) with differing rules");
            }

            // Check for non-matching calibration data
            if (!CheckThatAllCalibrationDataMatch(fileEntries))
            {
                SetValidationText(false/*invalid*/, CalibrationDataMismatchPanel, CalibrationDataMismatchGlyph, CalibrationDataMismatchValidationText, "Not every survey is using the same calibration data", "This can happen if the camera rig needed to be recalibrated at some stage.");
            }

            UpdateItemCountText();
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }

        private void UpdateItemCountText()
        {
            int total = SurveyFiles.Count(sf=>!sf.IsTotalRow);
            int selected = SurveyFiles.Count(f => f.Include && !f.IsTotalRow);
            ItemCountTextBlock.Text = $"{total} Items ({selected} selected)";
        }
        private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, bool recurse)
        {
            Queue<string> pending = new();
            pending.Enqueue(root);

            while (pending.Count > 0)
            {
                var path = pending.Dequeue();

                // Enumerate files in current folder
                string[]? files = null;
                try
                {
                    files = Directory.GetFiles(path, pattern);
                }
                catch (UnauthorizedAccessException) { }
                catch (PathTooLongException) { }

                if (files != null)
                {
                    foreach (var file in files)
                        yield return file;
                }

                // Add subdirectories to queue
                if (recurse)
                {
                    string[]? subDirs = null;
                    try
                    {
                        subDirs = Directory.GetDirectories(path);
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (PathTooLongException) { }

                    if (subDirs != null)
                    {
                        foreach (var dir in subDirs)
                            pending.Enqueue(dir);
                    }
                }
            }
        }

        /// <summary>
        /// Parse the list of surveys and check all surveys have the sames rules applied
        /// </summary>
        /// <param name="entries"></param>
        /// <returns></returns>
        private static bool CheckThatAllRulesMatch(List<SurveyFileEntry> entries)
        {
            bool ret = true;

            int firstRulesHash = entries[0].RulesHash;

            for (int i = 1; i < entries.Count; i++)
            {
                if (firstRulesHash != entries[i].RulesHash)
                {
                    ret = false;
                    break;
                }
            }

            return ret;
        }


        /// <summary>
        /// Parse the list of surveys and check all surveys are using the same calibration data
        /// </summary>
        /// <param name=""></param>
        /// <returns></returns>
        private static bool CheckThatAllCalibrationDataMatch(List<SurveyFileEntry> entries)
        {
            bool ret = true;

            if (entries.Count > 1)
            {
                string firstCalibrationHash = entries[0].CalibrationHash;

                for (int i = 1; i < entries.Count; i++)
                {
                    if (firstCalibrationHash != entries[i].CalibrationHash)
                    {
                        ret = false;
                        break;
                    }
                }
            }

            return ret;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            // If you use EPPlus in a noncommercial context
            // according to the Polyform Noncommercial license:
            ExcelPackage.License.SetNonCommercialPersonal("TobySolo");


            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Excel Workbook", new List<string>() { ".xlsx" });
            savePicker.SuggestedFileName = "ExportedSurveys";

            StorageFile file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    // Tell Windows we're about to write
                    CachedFileManager.DeferUpdates(file);

                    using var stream = await file.OpenStreamForWriteAsync();
                    using var package = new OfficeOpenXml.ExcelPackage();

                    // start fresh in case file existed
                    stream.SetLength(0);

                    // Write the fish by fish data
                    var worksheetData = package.Workbook.Worksheets.Add("Data");
                    await ExportDatatSheet(package, worksheetData);

                    // Write the survey by survey metadata
                    var worksheetMetadata = package.Workbook.Worksheets.Add("Metadata");
                    ExportMetadatatSheet(worksheetMetadata);

                    // write to the file
                    package.SaveAs(stream);
                    stream.Flush();

                    // Commit the updates
                    var status = await CachedFileManager.CompleteUpdatesAsync(file);
                    if (status != FileUpdateStatus.Complete)
                    {
                        report?.Warning("", $"Export completed with status: {status}");
                    }
                }
                catch(Exception ex)
                {
                    report?.Error("", $"Export failed, {ex.Message}");
                }
            }

            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;

            // Close the dialog
            this.Close();
        }

        private void HeaderSelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var file in SurveyFiles)
                file.Include = true;
            UpdateItemCountText();
        }

        private void HeaderSelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var file in SurveyFiles)
                file.Include = false;
            UpdateItemCountText();
        }

        private void HeaderSelectAllCheckBox_Indeterminate(object sender, RoutedEventArgs e)
        {
            // Optional: Add logic here if needed.
            // Currently, we leave this empty because indeterminate is just visual feedback.
        }


        private void UpdateSelectAllCheckBoxState()
        {
            var dataRows = SurveyFiles.Where(sf=>!sf.IsTotalRow).ToList();
            if (dataRows.Count == 0)
            {
                HeaderSelectAllCheckBox.IsChecked = false;
                return;
            }
            int selectedCount = dataRows.Count(f => f.Include);
            if (selectedCount == 0)
                HeaderSelectAllCheckBox.IsChecked = false;
            else if (selectedCount == dataRows.Count)
                HeaderSelectAllCheckBox.IsChecked = true;
            else
                HeaderSelectAllCheckBox.IsChecked = null; // indeterminate
        }


        private void UpdateButtons()
        {
            if (noSaveOptionSelected_NoExportAllowed)
            {
                ExportButton.IsEnabled = false;
            }
            else
            {
                // Exclude total row from export logic
                ExportButton.IsEnabled = SurveyFiles.Any(e => e.Include && !e.IsTotalRow);
            }
        }


        private void RebuildTotalsRow()
        {
            // Remove existing total row if present
            var existing = SurveyFiles.FirstOrDefault(f => f.IsTotalRow);
            if (existing != null)
            {
                SurveyFiles.CollectionChanged -= SurveyFiles_CollectionChangedSuppress; // ensure not double
                SurveyFiles.Remove(existing);
            }
            var data = SurveyFiles.Where(f => !f.IsTotalRow).ToList();
            if (data.Count == 0)
                return;

            var totalRow = new SurveyFileEntry
            {
                IsTotalRow = true,
                FileName = "Totals",
                Depth = $"{data.Select(d=>d.Depth).Where(d=>!string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase).Count()} depth(s)",
                TotalEntries = data.Sum(d => d.TotalEntries),
                TotalMeasurements = data.Sum(d => d.TotalMeasurements),
                Total3DPoints = data.Sum(d => d.Total3DPoints),
                TotalSinglePoints = data.Sum(d => d.TotalSinglePoints),
                RulesRange = AllSameOrIndicator(data.Select(d=>d.RulesRange)),
                RulesHorizontal = AllSameOrIndicator(data.Select(d=>d.RulesHorizontal)),
                RulesVertical = AllSameOrIndicator(data.Select(d=>d.RulesVertical)),
                RulesRMS = AllSameOrIndicator(data.Select(d=>d.RulesRMS)),
                RulesCalcNull = data.Sum(d => ParseLeadingInt(d.RulesCalcNull)).ToString(),
                RulesCalcFailed = data.Sum(d => ParseLeadingInt(d.RulesCalcFailed)).ToString(),
                Species = $"{data.Sum(d => ParseLeadingInt(d.Species))} missing", // d.Species like "3 missing" or "Ok"
                Calibration = $"{data.Count(d=> string.Equals(d.Calibration,"None",StringComparison.OrdinalIgnoreCase))} not set",
                CalibrationHash = $"{data.Select(d=>d.CalibrationHash).Where(h=>!string.IsNullOrWhiteSpace(h)).Distinct().Count()} set(s)",
                SyncPoint = $"{data.Count(d=> !string.Equals(d.SyncPoint,"Set",StringComparison.OrdinalIgnoreCase))} not set",
                Analyst = data.Select(d=>d.Analyst).Where(a=>!string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString()
            };

            // Convert numeric-only fields for consistency
            if (int.TryParse(totalRow.RulesCalcNull, out int rcnull) && rcnull == 0)
                totalRow.RulesCalcNull = "0";
            if (int.TryParse(totalRow.RulesCalcFailed, out int rcfail) && rcfail == 0)
                totalRow.RulesCalcFailed = "0";

            SurveyFiles.Add(totalRow);
        }

        private void SurveyFiles_CollectionChangedSuppress(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { }

        private static string AllSameOrIndicator(IEnumerable<string> values)
        {
            var filtered = values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (filtered.Count <= 1)
                return "Consistent";
            return "Inconsistent";
        }

        private static int ParseLeadingInt(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int space = text.IndexOf(' ');
            string first = space > 0 ? text[..space] : text;
            if (int.TryParse(first, out int val)) return val;
            return 0;
        }

        private async void RecalcNoSave_Click(object sender, RoutedEventArgs e)
        {
            // No save option selected. Don't allow export to avoid confusion
            noSaveOptionSelected_NoExportAllowed = true;

            await Recalc(false/*trueSaveFalseNoSave*/);
        }

        private async void RecalcSave_Click(object sender, RoutedEventArgs e)
        {
            // Save option selected. Export allowed
            noSaveOptionSelected_NoExportAllowed = false;

            await Recalc(true/*trueSaveFalseNoSave*/);
        }


        /// <summary>
        /// Prompt the use for a new calibration file. Then use that
        /// calibrition data to recalculate all the measurements and rules
        /// </summary>
        /// <param name="save"></param>
        private async Task Recalc(bool trueSaveFalseNoSave)
        {        
            if (!string.IsNullOrEmpty(FolderPathTextBox.Text))
            {
                string fileSpec = FolderPathTextBox.Text;
                await LoadSurveyFiles(fileSpec,
                                      IncludeSubfoldersCheckBox.IsChecked == true,
                                      true/*recalc*/,
                                      trueSaveFalseNoSave/*Save back to survey files*/);
                UpdateButtons();
            }
        }

        private async void NewCalibRecalcNoSave_Click(object sender, RoutedEventArgs e)
        {
            // No save option selected. Don't allow export to avoid confusion
            noSaveOptionSelected_NoExportAllowed = true;

            await NewCalibRecalc(false/*save*/);
        }

        private async void NewCalibRecalcSave_Click(object sender, RoutedEventArgs e)
        {
            // Save option selected. Export allowed
            noSaveOptionSelected_NoExportAllowed = false;

            await NewCalibRecalc(true/*save*/);
        }


        /// <summary>
        /// Prompt the use for a new calibration file. Then use that
        /// calibrition data to recalculate all the measurements and rules
        /// </summary>
        /// <param name="save"></param>
        private async Task NewCalibRecalc(bool trueSaveFalseNoSave)
        {
            // Get new calibration data
            CalibrationData? calibrationData = await ImportCalibration();

            if (calibrationData is not null && !string.IsNullOrEmpty(FolderPathTextBox.Text))
            {
                string fileSpec = FolderPathTextBox.Text;
                await LoadSurveyFiles(fileSpec, 
                                      IncludeSubfoldersCheckBox.IsChecked == true,
                                      true/*recalc*/,
                                      trueSaveFalseNoSave/*Save back to survey files*/, 
                                      calibrationData);
                UpdateButtons();
            }
        }


        /// <summary>
        /// Import calibration data into the survey
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async Task<CalibrationData?> ImportCalibration()
        {
            CalibrationData? calibrationData = null;

            // Create the file picker object
            FileOpenPicker openPicker = new()
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };

            // Add file type filters
            openPicker.FileTypeFilter.Add(".calib");
            openPicker.FileTypeFilter.Add(".json");
            openPicker.FileTypeFilter.Add(".jsn");

            // Associate the file picker with the current window
            IntPtr hWnd = WindowNative.GetWindowHandle(this/*App.MainWindow*/);
            InitializeWithWindow.Initialize(openPicker, hWnd);

            // Show the picker and allow multiple file selection
            StorageFile file = await openPicker.PickSingleFileAsync();

            // Check if files were picked and handle them
            if (file is not null)
            {
                string? calibrationFileSpec = file.Path;

                // Load the calibration file
                calibrationData = new();
                int ret = calibrationData.LoadFromFile(calibrationFileSpec);

                if (ret != 0)
                {
                    report?.Warning("", $"Failed to read from calibration file: {calibrationFileSpec}, return = {ret}");
                    calibrationData = null;
                }
            }

            return calibrationData;
        }


        /// <summary>
        /// Export all the data from each selected survey to an excel spreadsheet
        /// </summary>
        /// <param name="package"></param>
        /// <param name="worksheet"></param>
        /// <returns></returns>
        private async Task ExportDatatSheet(ExcelPackage package, ExcelWorksheet worksheet)
        {
            // Ensure a Hyperlink named style exists on the workbook
            var linkStyle = package.Workbook.Styles.NamedStyles
                .FirstOrDefault(s => s.Name == "Hyperlink")
                ?? package.Workbook.Styles.CreateNamedStyle("Hyperlink");

            linkStyle.Style.Font.UnderLine = true;
            linkStyle.Style.Font.Color.SetColor(System.Drawing.Color.Blue);

            // Write headers
            worksheet.Cells[1, (int)ExportExcelColmns.SurveyName].Value = "Survey Name";
            worksheet.Cells[1, (int)ExportExcelColmns.Depth].Value = "Depth";
            worksheet.Cells[1, (int)ExportExcelColmns.Transect].Value = "Transect";
            worksheet.Cells[1, (int)ExportExcelColmns.Analyst].Value = "Operator";
            worksheet.Cells[1, (int)ExportExcelColmns.Time].Value = "Position Time";
            worksheet.Cells[1, (int)ExportExcelColmns.TimeSecs].Value = "Position Secs";
            worksheet.Cells[1, (int)ExportExcelColmns.Type].Value = "Type";
            worksheet.Cells[1, (int)ExportExcelColmns.FishCount].Value = "Fish Count";
            worksheet.Cells[1, (int)ExportExcelColmns.Measurement].Value = "Measurement";
            worksheet.Cells[1, (int)ExportExcelColmns.Range].Value = "Distance";
            worksheet.Cells[1, (int)ExportExcelColmns.HorizontalOffset].Value = "Horiontal Offset";
            worksheet.Cells[1, (int)ExportExcelColmns.VerticalOffset].Value = "Vertical Offset";
            worksheet.Cells[1, (int)ExportExcelColmns.RMS].Value = "RMS";
            worksheet.Cells[1, (int)ExportExcelColmns.RMSWorst].Value = "RMSWorst";
            worksheet.Cells[1, (int)ExportExcelColmns.RulesPassed].Value = "Rules Passed";
            worksheet.Cells[1, (int)ExportExcelColmns.Species].Value = "Species";
            worksheet.Cells[1, (int)ExportExcelColmns.Genus].Value = "Genus";
            worksheet.Cells[1, (int)ExportExcelColmns.Family].Value = "Family";
            worksheet.Cells[1, (int)ExportExcelColmns.SpeciesCode].Value = "Species Code";
            worksheet.Cells[1, (int)ExportExcelColmns.NoSpeciesCode].Value = "Species Not Coded";
            worksheet.Cells[1, (int)ExportExcelColmns.Comment].Value = "Comment";
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();


            int row = 2;
            foreach (var fileEntry in SurveyFiles.Where(f => f.Include))
            {
                await Task.Delay(row % 10 == 0 ? 10 : 0); // Throttle to avoid UI freeze

                // Open the survey with no auto save
                var survey = new Survey(null!);
                if (await survey.SurveyLoad(fileEntry.FilePath, false/*autoSave*/) == 0)
                {
                    //??? Debug Line
                    //if (survey.Data.Info.SurveyCode == "STU_5m_E2-E3_2025-07-14")
                    //    row = row;

                    foreach (var evt in survey.Data.Events.EventList)
                    {
                        try
                        {
                            if (evt.EventDataType == Events.SurveyDataType.SurveyMeasurementPoints ||
                                evt.EventDataType == Events.SurveyDataType.SurveyStereoPoint ||
                                evt.EventDataType == Events.SurveyDataType.SurveyPoint)
                            {
                                SpeciesInfo? speciesInfo = null;
                                SurveyRulesCalc? surveyRulesCalc = null;
                                double? measurement = null;
                                int fishCount = 0;

                                // Common data
                                worksheet.Cells[row, (int)ExportExcelColmns.SurveyName].Value = survey.Data.Info.SurveyCode ?? survey.Data.Info.SurveyFileName;
                                worksheet.Cells[row, (int)ExportExcelColmns.Depth].Value = survey.Data.Info.SurveyDepth;
                                worksheet.Cells[row, (int)ExportExcelColmns.Analyst].Value = survey.Data.Info.SurveyAnalystName;
                                worksheet.Cells[row, (int)ExportExcelColmns.Time].Value = evt.TimeSpanTimelineController;

                                // Hyperlink column
                                var encodedPath = Uri.EscapeDataString(fileEntry.FilePath);
                                var secs = evt.TimeSpanTimelineController.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                                var cellTimeSecs = worksheet.Cells[row, (int)ExportExcelColmns.TimeSecs];
                                cellTimeSecs.Value = $"{evt.TimeSpanTimelineController.TotalSeconds:F2}";
                                cellTimeSecs.Hyperlink = new ExcelHyperLink($"underwatersurveyor://open?file={encodedPath}&start={secs}");
                                // Apply the built-in Hyperlink style so it looks like Excel's default (blue underline)
                                cellTimeSecs.StyleName = "Hyperlink";

                                // Frame time
                                var timeValue = evt.TimeSpanTimelineController;
                                var cell = worksheet.Cells[row, (int)ExportExcelColmns.Time];
                                cell.Value = timeValue;
                                cell.Style.Numberformat.Format = "hh:mm:ss";


                                // Calculated transect
                                string transectNumber = EventsControl.GetTransectMarkerNameForEvent(survey.Data.Events.EventList, evt) ?? string.Empty;
                                worksheet.Cells[row, (int)ExportExcelColmns.Transect].Value = transectNumber;

                                // Type
                                switch (evt.EventDataType)
                                {
                                    case Events.SurveyDataType.SurveyMeasurementPoints:
                                        worksheet.Cells[row, (int)ExportExcelColmns.Type].Value = "Measurement";
                                        if (evt.EventData is SurveyMeasurement surveyMeasurement)
                                        {
                                            speciesInfo = surveyMeasurement.SpeciesInfo;
                                            surveyRulesCalc = surveyMeasurement.SurveyRulesCalc;
                                            measurement = surveyMeasurement.Measurment;
                                            if (!int.TryParse(speciesInfo.Number, out fishCount))
                                                fishCount = 1;
                                        }
                                        break;

                                    case Events.SurveyDataType.SurveyStereoPoint:
                                        worksheet.Cells[row, (int)ExportExcelColmns.Type].Value = "3D";
                                        if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                                        {
                                            speciesInfo = surveyStereoPoint.SpeciesInfo;
                                            surveyRulesCalc = surveyStereoPoint.SurveyRulesCalc;
                                            if (!int.TryParse(speciesInfo.Number, out fishCount))
                                                fishCount = 1;
                                        }
                                        break;

                                    case Events.SurveyDataType.SurveyPoint:
                                        worksheet.Cells[row, (int)ExportExcelColmns.Type].Value = "Point";
                                        if (evt.EventData is SurveyPoint surveyPoint)
                                        {
                                            speciesInfo = surveyPoint.SpeciesInfo;
                                            if (!int.TryParse(speciesInfo.Number, out fishCount))
                                                fishCount = 1;
                                        }
                                        break;
                                }

                                // Load the fish count
                                worksheet.Cells[row, (int)ExportExcelColmns.FishCount].Value = fishCount;

                                // Load measurement
                                if (measurement is not null)
                                    worksheet.Cells[row, (int)ExportExcelColmns.Measurement].Value = measurement;
                                else
                                    worksheet.Cells[row, (int)ExportExcelColmns.Measurement].Value = "";

                                // Range Horizontal and vertical offsets and RMS
                                worksheet.Cells[row, (int)ExportExcelColmns.Range].Value = surveyRulesCalc?.Range ?? 0;
                                worksheet.Cells[row, (int)ExportExcelColmns.HorizontalOffset].Value = surveyRulesCalc?.XOffset ?? 0;
                                worksheet.Cells[row, (int)ExportExcelColmns.VerticalOffset].Value = surveyRulesCalc?.YOffset ?? 0;
                                worksheet.Cells[row, (int)ExportExcelColmns.RMS].Value = surveyRulesCalc?.RMSMean ?? 0;
                                worksheet.Cells[row, (int)ExportExcelColmns.RMSWorst].Value = surveyRulesCalc?.RMSWorst ?? 0;
                                worksheet.Cells[row, (int)ExportExcelColmns.RulesPassed].Value = surveyRulesCalc?.SurveyRules;

                                // Species, Genus, Family, Code
                                worksheet.Cells[row, (int)ExportExcelColmns.Species].Value = speciesInfo?.Species ?? "";
                                worksheet.Cells[row, (int)ExportExcelColmns.Genus].Value = speciesInfo?.Genus ?? "";
                                worksheet.Cells[row, (int)ExportExcelColmns.Family].Value = speciesInfo?.Family ?? "";
                                worksheet.Cells[row, (int)ExportExcelColmns.SpeciesCode].Value = speciesInfo?.Code ?? "";
                                worksheet.Cells[row, (int)ExportExcelColmns.Comment].Value = speciesInfo?.Comment ?? "";

                                // Check if a species code was actually used or was it plan text or the species code is blank
                                bool validSpeciesCode = true;
                                if (speciesInfo is null || 
                                    string.IsNullOrEmpty(speciesInfo.Code) ||
                                    speciesInfo.Species is null ||
                                    speciesInfo.Species.IndexOf('/') == -1)
                                {
                                    validSpeciesCode = false;
                                }
                                worksheet.Cells[row, (int)ExportExcelColmns.NoSpeciesCode].Value = !validSpeciesCode ? true : ""; 

                                // Debug
                                Debug.WriteLine($"Export:{row},{survey.Data.Info.SurveyCode ?? survey.Data.Info.SurveyFileName},{survey.Data.Info.SurveyDepth},{survey.Data.Info.SurveyAnalystName},{evt.TimeSpanTimelineController}");

                                row++;
                            }
                        }
                        catch (Exception ex) when (ex is ObjectDisposedException)
                        {
                            report?.Error("", $"Export_Click ... {ex}");
                            break; // exits the foreach immediately
                        }
                        catch (Exception ex)
                        {
                            report?.Warning("", $"Export_Click ... {ex}");
                        }
                    }
                }
            }

        }


        private void ExportMetadatatSheet(ExcelWorksheet worksheet)
        {
            // Write headers
            int colIndex = 1;
            foreach (var col in SurveyGrid.Columns)
            {
                // Use Header if it's text; otherwise use index
                string headerText = col.Header?.ToString() ?? $"Column {colIndex}";
                worksheet.Cells[1, colIndex].Value = headerText;
                colIndex++;
            }

            // Write rows
            var items = SurveyGrid.ItemsSource?.Cast<object>()?.ToList();
            if (items is null)
                return;

            for (int rowIndex = 0; rowIndex < items.Count; rowIndex++)
            {
                var item = items[rowIndex];
                for (int c = 0; c < SurveyGrid.Columns.Count; c++)
                {
                    var column = SurveyGrid.Columns[c];

                    // Extract binding path
                    string? path = (column as DataGridBoundColumn)?.Binding is Binding binding
                        ? binding.Path?.Path
                        : null;

                    // Fallback for template columns: skip them or use reflection on known types
                    if (path == null)
                        continue;

                    var prop = item.GetType().GetProperty(path);
                    if (prop != null)
                    {
                        var value = prop.GetValue(item);
                        worksheet.Cells[rowIndex + 2, c + 1].Value = value;
                    }
                }
            }
        }

        private void SurveyGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.DataContext is SurveyFileEntry sfe && sfe.IsTotalRow)
            {
                e.Row.Background = (Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"];
                // Font weight customization omitted due to namespace limitations
            }
        }

        /// <summary>
        /// Check if the survey file name, survey code and actual file name are consistent.
        /// </summary>
        /// <param name="actualFileName"></param>
        /// <param name="surveyFileName"></param>
        /// <param name="surveyCode"></param>
        /// <param name="status"></param>
        /// <returns></returns>
        private static string CheckSurveyFileNameSurveyFileNameAndSurveyCodeAreConsistent(string actualFileName, string surveyFileName, string surveyCode)
        {
            // Reset
            string status = string.Empty;
            StringBuilder sb = new();
            
            // Check the survey name in the meta data
            if (string.IsNullOrWhiteSpace(surveyFileName))
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append("Missing survey file name");
            }

            // Check the survey code in the meta data
            if (string.IsNullOrWhiteSpace(surveyCode))
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append("Missing survey Code");
            }

            // Check if the Survey File Name and the actual file name match
            if (string.Compare(actualFileName, surveyFileName, true) != 0)
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append($"Survey File Name differ:{surveyFileName}");
            }

            // Check if the Survey File Name and the Survey Code match
            string actualFileNameWithoutExtension = Path.GetFileNameWithoutExtension(actualFileName);

            if (string.Compare(actualFileNameWithoutExtension, surveyCode, true) != 0)
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append($"Survey code inconsistent:{surveyCode}");
            }

            if (sb.Length > 0)
                status = $" ({sb})";

            return status;
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

    }
    public partial class SurveyFileEntry : INotifyPropertyChanged
    {
        public bool IsTotalRow { get; set; } = false; // flag for totals row

        private bool _include;

        public bool Include
        {
            get => _include;
            set
            {
                if (_include != value)
                {
                    _include = value;
                    OnPropertyChanged(nameof(Include));
                }
            }
        }

        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Depth { get; set; } = string.Empty;
        public int TotalEntries { get; set; }
        public int TotalMeasurements { get; set; }
        public int Total3DPoints { get; set; }
        public int TotalSinglePoints { get; set; }
        public string TransectList { get; set; } = string.Empty;
        public string RulesRange { get; set; } = string.Empty;
        public string RulesRMS { get; set; } = string.Empty;
        public string RulesCalcNull { get; set; } = string.Empty;
        public string RulesCalcFailed { get; set; } = string.Empty;
        public int  RulesHash { get; set; } = -1;           // Not displayed
        public string Species { get; set; } = string.Empty;
        public string RulesHorizontal { get; set; } = string.Empty;
        public string RulesVertical { get; set; } = string.Empty;
        public string Calibration { get; set; } = string.Empty;
        public string CalibrationHash { get; set; } = string.Empty;
        public string SyncPoint { get; set; } = string.Empty;
        public string Analyst { get; set; } = string.Empty;
        public string SurveyCode { get; set; } = string.Empty;
        public string LeftMediaFile { get; set; } = string.Empty;
        public string RightMediaFile { get; set; } = string.Empty;
        public string SurveyPath { get; set; } = string.Empty;
        public string MediaPath { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class BoolHideWhenTrueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b)
                return Visibility.Collapsed;
            return Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

}
