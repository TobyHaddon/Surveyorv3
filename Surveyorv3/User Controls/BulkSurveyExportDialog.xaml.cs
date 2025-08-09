using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using OfficeOpenXml;
using Surveyor.Events;
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
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinUIEx;



namespace Surveyor.User_Controls
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class BulkSurveyExportDialog : WindowEx
    {
        public ObservableCollection<SurveyFileEntry> SurveyFiles { get; set; } = [];

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
                                        Measurement,
                                        Range,
                                        HorizontalOffset,
                                        VerticalOffset,
                                        RMS,
                                        RulesPassed,
                                        Species,
                                        Genus,
                                        Family,
                                        Count,
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
                        await LoadSurveyFiles(folder.Path, IncludeSubfoldersCheckBox.IsChecked == true);
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

        private async Task LoadSurveyFiles(string path, bool includeSubfolders)
        {
            SurveyFiles.Clear();
            ItemCountTextBlock.Text = "";
            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            // Run the expensive work on a background thread
            var fileEntries = await Task.Run(async () =>
            {
                var entries = new List<SurveyFileEntry>();
                var files = SafeEnumerateFiles(path, "*.survey", includeSubfolders).ToList();

                foreach (var fileSpec in files)
                {
                    try
                    {
                        var survey = new Survey(null!);
                        if (await survey.SurveyLoad(fileSpec) == 0)
                        {
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
                                    rmsRule = $"RMS<{Math.Round(survey.Data.SurveyRules.SurveyRulesData.RMSMax / 1000, MidpointRounding.AwayFromZero):F0}mm";
                                }
                                else
                                {
                                    rmsRule = "No RMS";
                                }
                            }
                            else
                                rmsRule = "No rules";


                            // Check the number of measurement and 3D points where the rules have not been applied
                            string rulesCalc = string.Empty;
                            if (totalCountWithNullRules > 0)
                            {
                                rulesCalc = $"{totalCountWithNullRules} missing";
                            }
                            else
                            {
                                rulesCalc = "Ok";
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

                            // Check for calibration
                            string calibration = string.Empty;
                            SurveyorCalibrationData.CalibrationData? calibrationData = survey.Data.Calibration.GetPreferredCalibationData(null, null);
                            if (survey.Data.Calibration.CalibrationDataList.Count > 0 &&
                                survey.Data.Calibration.PreferredCalibrationDataIndex >= 0 && 
                                survey.Data.Calibration.PreferredCalibrationDataIndex < survey.Data.Calibration.CalibrationDataList.Count)
                            {
                                calibrationData = survey.Data.Calibration.CalibrationDataList[survey.Data.Calibration.PreferredCalibrationDataIndex];
                                if (calibrationData.StereoCameraCalibration.RMS != 0)
                                {
                                    calibration = $"RMS:{calibrationData.StereoCameraCalibration.RMS:F2}";
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
                                RulesRMS = rmsRule,
                                RulesCalc = rulesCalc,          // Where the rules actually applied
                                Species = species,
                                Calibration = calibration,
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
                        System.Diagnostics.Debug.WriteLine($"Failed to load {fileSpec}: {ex.Message}");
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
            

            UpdateItemCountText();
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }

        private void UpdateItemCountText()
        {
            int total = SurveyFiles.Count;
            int selected = SurveyFiles.Count(f => f.Include);
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
            if (SurveyFiles.Count == 0)
            {
                HeaderSelectAllCheckBox.IsChecked = false;
                return;
            }

            int selectedCount = SurveyFiles.Count(f => f.Include);
            if (selectedCount == 0)
                HeaderSelectAllCheckBox.IsChecked = false;
            else if (selectedCount == SurveyFiles.Count)
                HeaderSelectAllCheckBox.IsChecked = true;
            else
                HeaderSelectAllCheckBox.IsChecked = null; // indeterminate
        }


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
            worksheet.Cells[1, (int)ExportExcelColmns.Measurement].Value = "Measurement";
            worksheet.Cells[1, (int)ExportExcelColmns.Range].Value = "Distance";
            worksheet.Cells[1, (int)ExportExcelColmns.HorizontalOffset].Value = "Horiontal Offset";
            worksheet.Cells[1, (int)ExportExcelColmns.VerticalOffset].Value = "Vertical Offset";
            worksheet.Cells[1, (int)ExportExcelColmns.RMS].Value = "RMS";
            worksheet.Cells[1, (int)ExportExcelColmns.RulesPassed].Value = "Rules Passed";
            worksheet.Cells[1, (int)ExportExcelColmns.Species].Value = "Species";
            worksheet.Cells[1, (int)ExportExcelColmns.Genus].Value = "Genus";
            worksheet.Cells[1, (int)ExportExcelColmns.Family].Value = "Family";
            worksheet.Cells[1, (int)ExportExcelColmns.Count].Value = "Count";
            worksheet.Cells[1, (int)ExportExcelColmns.Comment].Value = "Comment";

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();


            int row = 2;
            foreach (var fileEntry in SurveyFiles.Where(f => f.Include))
            {
                await Task.Delay(row % 10 == 0 ? 10 : 0); // Throttle to avoid UI freeze

                var survey = new Survey(null!);
                if (await survey.SurveyLoad(fileEntry.FilePath) == 0)
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

                                // Common data
                                worksheet.Cells[row, (int)ExportExcelColmns.SurveyName].Value = survey.Data.Info.SurveyCode ?? survey.Data.Info.SurveyFileName;
                                worksheet.Cells[row, (int)ExportExcelColmns.Depth].Value = survey.Data.Info.SurveyDepth;
                                worksheet.Cells[row, (int)ExportExcelColmns.Analyst].Value = survey.Data.Info.SurveyAnalystName;
                                worksheet.Cells[row, (int)ExportExcelColmns.Time].Value = evt.TimeSpanTimelineController;
                                //???worksheet.Cells[row, (int)ExportExcelColmns.TimeSecs].Value = evt.TimeSpanTimelineController.TotalSeconds;

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
                                        }
                                        break;

                                    case Events.SurveyDataType.SurveyStereoPoint:
                                        worksheet.Cells[row, (int)ExportExcelColmns.Type].Value = "3D";
                                        if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                                        {
                                            speciesInfo = surveyStereoPoint.SpeciesInfo;
                                            surveyRulesCalc = surveyStereoPoint.SurveyRulesCalc;
                                        }
                                        break;

                                    case Events.SurveyDataType.SurveyPoint:
                                        worksheet.Cells[row, (int)ExportExcelColmns.Type].Value = "Point";
                                        if (evt.EventData is SurveyPoint surveyPoint)
                                        {
                                            speciesInfo = surveyPoint.SpeciesInfo;
                                        }
                                        break;
                                }

                                // Load measurement
                                if (measurement is not null)
                                    worksheet.Cells[row, (int)ExportExcelColmns.Measurement].Value = measurement;
                                else
                                    worksheet.Cells[row, (int)ExportExcelColmns.Measurement].Value = "";

                                // Range Horizontal and vertical offsets and RMS
                                worksheet.Cells[row, (int)ExportExcelColmns.Range].Value = surveyRulesCalc?.Range ?? 0;
                                worksheet.Cells[row, (int)ExportExcelColmns.HorizontalOffset].Value = surveyRulesCalc?.XOffset ?? 0;
                                worksheet.Cells[row, (int)ExportExcelColmns.VerticalOffset].Value = surveyRulesCalc?.YOffset ?? 0;
                                worksheet.Cells[row, (int)ExportExcelColmns.RMS].Value = surveyRulesCalc?.RMS ?? 0;
                                worksheet.Cells[row, (int)ExportExcelColmns.RulesPassed].Value = surveyRulesCalc?.SurveyRules;

                                // Species, Genus, Family
                                worksheet.Cells[row, (int)ExportExcelColmns.Species].Value = speciesInfo?.Species ?? "";
                                worksheet.Cells[row, (int)ExportExcelColmns.Genus].Value = speciesInfo?.Genus ?? "";
                                worksheet.Cells[row, (int)ExportExcelColmns.Family].Value = speciesInfo?.Family ?? "";
                                worksheet.Cells[row, (int)ExportExcelColmns.Count].Value = speciesInfo?.Number ?? "";
                                worksheet.Cells[row, (int)ExportExcelColmns.Comment].Value = speciesInfo?.Comment ?? "";


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

        private void UpdateButtons()
        {
            // Ensure SurveyGrid.ItemsSource is accessible
            if (SurveyGrid.ItemsSource is IEnumerable<object> items)
            {
                // Convert to list for reuse
                var itemList = items.Cast<object>().ToList();

                // Enable ExportDataButton if any item has Include == true
                ExportButton.IsEnabled = itemList
                    .OfType<SurveyFileEntry>()
                    .Any(entry => entry.Include);
            }
            else
            {
                // Fallback: disable both buttons
                ExportButton.IsEnabled = false;
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


    }
    public partial class SurveyFileEntry : INotifyPropertyChanged
    {
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
        public string RulesCalc { get; set; } = string.Empty;
        public string Species { get; set; } = string.Empty;
        public string RulesHorizontal { get; set; } = string.Empty;
        public string RulesVertical { get; set; } = string.Empty;
        public string Calibration { get; set; } = string.Empty;
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

}
