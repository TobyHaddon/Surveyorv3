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

        private bool noSaveOptionSelected_ExportMetadataOnly = false;

        private const string DraftWarningText = "*** DRAFT DATA - CALCULATED ON THE FLY DURING THE EXPORT PROCESS ***";


        // Reporter
        private readonly Reporter? report = null;

        // Species Code List
        private readonly SpeciesCodeList speciesCodeList;
        // AllMeasurements aggregator used during processing (confined to ProcessSurveyFiles execution)
        private readonly AllMeasurements allMeasurements = new();
        // AllEvents aggregator for species derivation
        private readonly AllEvents allEvents = new();
        // Totals row summary text for Ave. Length
        private string? totalsAveLengthSummary = string.Empty;

        // Export Excel columns
        public enum ExportExcelColmns { SurveyName = 1,
                                        Depth,
                                        Transect,
                                        Analyst,
                                        Time,
                                        TimeSecs,
                                        Type,
                                        FishCount,
                                        Measurementm,
                                        Measurementmm,
                                        Range,
                                        HorizontalOffset,
                                        VerticalOffset,
                                        RMS,
                                        RMSWorst,
                                        RulesPassed,
                                        SpeciesScientific,
                                        SpeciesCommon,
                                        Genus,
                                        GenusSpeciesScientific,
                                        FamilyScientific,
                                        FamilyCommon,
                                        SpeciesCode,
                                        NoSpeciesCode,
                                        Comment,
                                        DerivedSpecies,
                                        DerivedLength
        };

        public BulkSurveyExportDialog(Reporter? _report, SpeciesCodeList _speciesCodeList)
        {
            // Remember the reporter & species code list
            report = _report;
            speciesCodeList = _speciesCodeList;


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
            RebuildTotalsRow(0);
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
                        await ProcessSurveyFiles(folder.Path, 
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

        private async Task ProcessSurveyFiles(string path, bool includeSubfolders, bool recalc, bool save, CalibrationData? calibrationData = null)
        {
            SurveyFiles.Clear();
            ItemCountTextBlock.Text = "";
            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            // Reset average lengths aggregator
            allMeasurements.Clear();
            // Reset events aggregator
            allEvents.Clear();
            totalsAveLengthSummary = string.Empty;

            StereoProjection stereoProjection = new();

            // Run the expensive work on a background thread
            var result = await Task.Run(async () =>
            {
                var entries = new List<SurveyFileEntry>();
                int totalTransects = 0;
                var files = SafeEnumerateFiles(path, "*.survey", includeSubfolders).ToList();

                var distinctSpeciesPointsBySurvey = new DistinctSpeciesListForPointEventsPerSurvey();

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
                                        // If we are saving data back to the survey file and we provided calibration data
                                        // then that calibration data needs to be added to the survey file and becomes the
                                        // preferred calibration data
                                        if (save && calibrationData is not null)
                                        {
                                            // First use the hash to confirm this calibration data is not already in the survey
                                            int newCalHash = calibrationData.GetHashCode();
                                            int foundIndex = -1;
                                            int index = 0;
                                            foreach (var calib in survey.Data.Calibration.CalibrationDataList)
                                            {
                                                if (calib.GetHashCode() == newCalHash)
                                                {
                                                    foundIndex = index;
                                                    break;
                                                }
                                                index++;
                                            }
                                            if (foundIndex == -1)
                                            {
                                                // If there will be more than one calibration data set in the survey file
                                                // then allow multiple calibration data sets
                                                if (survey.Data.Calibration.CalibrationDataList.Count > 0)
                                                    survey.Data.Calibration.AllowMultipleCalibrationData = true;

                                                survey.Data.Calibration.CalibrationDataList.Add(calibrationData);
                                                survey.Data.Calibration.PreferredCalibrationDataIndex = survey.Data.Calibration.CalibrationDataList.Count - 1;
                                                report?.Error("", $"New calibration set:{calibrationData.Description} added and set as the preferred");
                                            }
                                            else
                                            {
                                                report?.Error("", $"New calibration set:{calibrationData.Description} was already in the survey file");

                                                // Ensure it is the preferred calibration data
                                                survey.Data.Calibration.PreferredCalibrationDataIndex = foundIndex;
                                            }
                                        }

                                        // Is a save required
                                        if (save)
                                        {
                                            survey.SurveySave();
                                        }
                                    }
                                }
                            }

                            // After any recalc/save, aggregate measurements and distinct species for point events
                            allMeasurements.Add(survey.Data.Events.EventList);
                            // Aggregate all events for species derivation (needs site/depth/year)
                            allEvents.Add(
                                survey.Data.Info.SurveyFileName ?? string.Empty,
                                survey.Data.Info.SurveyDepth,
                                survey.Data.Info.SurveyCode,
                                survey.Data.Events.EventList);

                            string surveyFileNameKey = survey.Data.Info.SurveyFileName ?? string.Empty;
                            distinctSpeciesPointsBySurvey.Add(surveyFileNameKey, survey.Data.Events.EventList);

                            string fileName = Path.GetFileName(fileSpec);

                            // Check if the Survey File Name and the Survey Code are conistent
                            string surveyNameAndCodeCheck = CheckSurveyFileNameSurveyFileNameAndSurveyCodeAreConsistent(fileName, survey.Data.Info.SurveyFileName ?? "", survey.Data.Info.SurveyCode ?? "");

                            string surveyCode = survey.Data.Info.SurveyCode ?? "";

                            string leftMediaFile = survey.Data.Media.LeftMediaFileNames.Count > 0
                                ? (survey.Data.Media.LeftMediaFileNames[0] ?? "Missing")
                                : "Missing";
                            string rightMediaFile = survey.Data.Media.RightMediaFileNames.Count > 0
                                ? (survey.Data.Media.RightMediaFileNames[0] ?? "Missing")
                                : "Missing";

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
                            totalTransects += transectMarkerList.Count;

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

                            // Total the cumlative RMSWorst
                            double totalRMS = 0.0;

                            // Sum SurveyMeasurementPoints RMS
                            double sumSurveyMeasurementPointsRMS =
                                survey.Data.Events.EventList
                                    .Where(e => e.EventDataType == SurveyDataType.SurveyMeasurementPoints)
                                    .Select(e => (e.EventData as SurveyMeasurement)?.SurveyRulesCalc?.RMSWorst ?? 0.0)
                                    .Sum();

                            // Sum SurveyStereoPoint RMS
                            double sumSurveyStereoPointsRMS = survey.Data.Events.EventList
                                    .Where(e => e.EventDataType == SurveyDataType.SurveyStereoPoint)
                                    .Select(e => (e.EventData as SurveyStereoPoint)?.SurveyRulesCalc?.RMSWorst ?? 0.0)
                                    .Sum();

                            totalRMS += (sumSurveyMeasurementPointsRMS + sumSurveyStereoPointsRMS);

                            // Check the number of measurement, 3D points and single points where the species has not been set
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
                                // Clean name for key/logic
                                FileName = survey.Data.Info.SurveyFileName ?? string.Empty,
                                // Decorated display for UI
                                FileNameDisplay = (string.IsNullOrEmpty(surveyNameAndCodeCheck) ? string.Empty : "*") + fileName + surveyNameAndCodeCheck,
                                SurveyType = survey.Data.Info.SurveyType.ToString(),
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
                                TotalRMS = totalRMS,
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

                // Compute per-survey summaries now that allMeasurements and distinctPoints have been filled
                foreach (var e in entries)
                {
                    var summary = distinctSpeciesPointsBySurvey.GetSummaryText(e.FileName, allMeasurements);
                    e.AveLengthSummary = summary;
                }

                // Compute totals summary
                var totals = distinctSpeciesPointsBySurvey.GetTotalsSummary(allMeasurements);

                return (entries, totals, totalTransects);
            });

            var fileEntries = result.entries;
            totalsAveLengthSummary = result.totals;
            int totalTransects = result.totalTransects;

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
            
            RebuildTotalsRow(totalTransects);

            // Check for non-matching rules
            if (!CheckThatAllRulesMatch(fileEntries))
            {
                SetValidationText(false/*invalid*/, RulesMismatchPanel, RulesMismatchGlyph, RulesMismatchValidationText, "Not every survey has the same rules settings", "Check the different rules columns to find the survey(s) with differing rules");
            }
            else
            {
                SetValidationText(null/*hide*/, RulesMismatchPanel, RulesMismatchGlyph, RulesMismatchValidationText, "", "");
            }

            // Check for non-matching calibration data
            if (!CheckThatAllCalibrationDataMatch(fileEntries))
            {
                SetValidationText(false/*invalid*/, CalibrationDataMismatchPanel, CalibrationDataMismatchGlyph, CalibrationDataMismatchValidationText, "Not every survey is using the same calibration data", "This can happen if the camera rig needed to be recalibrated at some stage.");
            }
            else
            {
                SetValidationText(null/*hide*/, CalibrationDataMismatchPanel, CalibrationDataMismatchGlyph, CalibrationDataMismatchValidationText, "", "");
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

            // Get the default export file name
            List<SurveyFileEntry> SurveyFilesIncluded = [.. SurveyFiles.Where(f => f.Include)];
            string suggestedFileName = $"ExportedSurveys ({SurveyFilesIncluded.Count} surveys) {DateTime.Now:yyyy-MM-dd}";
            if (SurveyFilesIncluded.Count == 1)
                suggestedFileName = Path.GetFileNameWithoutExtension(SurveyFilesIncluded[0].FileName);

            // Get the export excel file spec
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Excel Workbook", [".xlsx"]);
            savePicker.SuggestedFileName = suggestedFileName;

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

                    if (!noSaveOptionSelected_ExportMetadataOnly)
                    {
                        // Write the fish by fish data
                        var worksheetData = package.Workbook.Worksheets.Add("Data");
                        await ExportDatatSheet(package, worksheetData, file.Path);
                    }

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
            // Exclude total row from export logic
            ExportButton.IsEnabled = SurveyFiles.Any(e => e.Include && !e.IsTotalRow);
        }


        private void RebuildTotalsRow(int totalTransects)
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
                FileNameDisplay = "Totals",
                Depth = $"{data.Select(d=>d.Depth).Where(d=>!string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase).Count()} depth(s)",
                TotalEntries = data.Sum(d => d.TotalEntries),
                TotalMeasurements = data.Sum(d => d.TotalMeasurements),
                Total3DPoints = data.Sum(d => d.Total3DPoints),
                TotalSinglePoints = data.Sum(d => d.TotalSinglePoints),
                TransectList = totalTransects.ToString(),
                RulesRange = AllSameOrIndicator(data.Select(d=>d.RulesRange)),
                RulesHorizontal = AllSameOrIndicator(data.Select(d=>d.RulesHorizontal)),
                RulesVertical = AllSameOrIndicator(data.Select(d=>d.RulesVertical)),
                RulesRMS = AllSameOrIndicator(data.Select(d=>d.RulesRMS)),
                RulesCalcNull = data.Sum(d => ParseLeadingInt(d.RulesCalcNull)).ToString(),
                RulesCalcFailed = data.Sum(d => ParseLeadingInt(d.RulesCalcFailed)).ToString(),
                TotalRMS = data.Sum(d => d.TotalRMS),
                Species = $"{data.Sum(d => ParseLeadingInt(d.Species))} missing", // d.Species like "3 missing" or "Ok"
                Calibration = $"{data.Count(d=> string.Equals(d.Calibration,"None",StringComparison.OrdinalIgnoreCase))} not set",
                CalibrationHash = $"{data.Select(d=>d.CalibrationHash).Where(h=>!string.IsNullOrWhiteSpace(h)).Distinct().Count()} set(s)",
                SyncPoint = $"{data.Count(d=> !string.Equals(d.SyncPoint,"Set",StringComparison.OrdinalIgnoreCase))} not set",
                Analyst = data.Select(d=>d.Analyst).Where(a=>!string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(),
                AveLengthSummary = string.IsNullOrWhiteSpace(totalsAveLengthSummary) ? string.Empty : totalsAveLengthSummary
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
            noSaveOptionSelected_ExportMetadataOnly = true;
            ExportButton.Content = "Export Metadata";

            await Recalc(false/*trueSaveFalseNoSave*/);
        }

        private async void RecalcSave_Click(object sender, RoutedEventArgs e)
        {
            // Save option selected. Export allowed
            noSaveOptionSelected_ExportMetadataOnly = false;
            ExportButton.Content = "Export";

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
                await ProcessSurveyFiles(fileSpec,
                                         IncludeSubfoldersCheckBox.IsChecked == true,
                                         true/*recalc*/,
                                         trueSaveFalseNoSave/*Save back to survey files*/);
                UpdateButtons();
            }
        }

        private async void NewCalibRecalcNoSave_Click(object sender, RoutedEventArgs e)
        {
            // No save option selected. Don't allow export to avoid confusion
            noSaveOptionSelected_ExportMetadataOnly = true;
            ExportButton.Content = "Export Metadata";

            await NewCalibRecalc(false/*save*/);
        }

        private async void NewCalibRecalcSave_Click(object sender, RoutedEventArgs e)
        {
            // Save option selected. Export allowed
            noSaveOptionSelected_ExportMetadataOnly = false;
            ExportButton.Content = "Export";

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
                await ProcessSurveyFiles(fileSpec, 
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
        private async Task ExportDatatSheet(ExcelPackage package, ExcelWorksheet worksheet, string exportFile)
        {
            int rowIndex = 1;
            bool failed = false;
            int problemCount = 0;
            int exportLineCount = 0;


            // Ensure a Hyperlink named style exists on the workbook
            var linkStyle = package.Workbook.Styles.NamedStyles
                .FirstOrDefault(s => s.Name == "Hyperlink")
                ?? package.Workbook.Styles.CreateNamedStyle("Hyperlink");

            linkStyle.Style.Font.UnderLine = true;
            linkStyle.Style.Font.Color.SetColor(System.Drawing.Color.Blue);


            // Any warning message?
            if (noSaveOptionSelected_ExportMetadataOnly)
            {
                ApplyWarningMessage(worksheet, rowIndex, 1/*colIndex*/);
                rowIndex += 2; // leave one blank row before table
            }


            // Check if we should try to derive the species if missing
            bool deriveMissingSpecies = false;
            if (DeriveMissingSpecies.IsChecked == true)
                deriveMissingSpecies = true;

            // Check if we should apply average measurement to the Single and 3d Point events
            bool applyAverageLengths = false;
            if (ApplyAverageLengths.IsChecked == true)
                applyAverageLengths = true;

            // Include failed RMS and other rules in the export
            bool includeFailedRMS = false;
            bool includeOtherFailedRules = false;
            bool includePartialIdentification = false;
            if (IncludeFailedRMS.IsChecked == true)
                includeFailedRMS = true;
            if (IncludeOtherFailedRules.IsChecked == true)
                includeOtherFailedRules = true;
            if (IncludePartialIdentification.IsChecked == true)
                includePartialIdentification = true;

                // Write headers
                worksheet.Cells[rowIndex, (int)ExportExcelColmns.SurveyName].Value = "Survey Name";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Depth].Value = "Depth";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Transect].Value = "Transect";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Analyst].Value = "Operator";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Time].Value = "Position Time";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.TimeSecs].Value = "Position Secs";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Type].Value = "Type";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.FishCount].Value = "Fish Count";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Measurementm].Value = "Measurement(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Measurementmm].Value = "Measurement(mm)";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Range].Value = "Distance (m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.HorizontalOffset].Value = "Horiontal Offset(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.VerticalOffset].Value = "Vertical Offset(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.RMS].Value = "RMS(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.RMSWorst].Value = "RMSWorst(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.RulesPassed].Value = "Rules Passed";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.SpeciesScientific].Value = "Species Scientific";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.SpeciesCommon].Value = "Species Common";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Genus].Value = "Genus";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.GenusSpeciesScientific].Value = "Genus Species Scientific";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.FamilyScientific].Value = "Family Scientific";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.FamilyCommon].Value = "Family Common";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.SpeciesCode].Value = "Species Code";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.NoSpeciesCode].Value = "Species Not Coded";
            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Comment].Value = "Comment";
            if (applyAverageLengths || deriveMissingSpecies)
            {
                worksheet.Cells[rowIndex, (int)ExportExcelColmns.DerivedSpecies].Value = "Derived Species";
                worksheet.Cells[rowIndex, (int)ExportExcelColmns.DerivedLength].Value = "Derived Length";
            }
            // Freeze panes at D2 (freeze row 1 and columns A-C)
            worksheet.View.FreezePanes(2, 4);
          
            rowIndex++;


            
            foreach (var fileEntry in SurveyFiles.Where(f => f.Include))
            {
                await Task.Delay(rowIndex % 10 == 0 ? 10 : 0); // Throttle to avoid UI freeze

                // Open the survey with no auto save
                var survey = new Survey(null!);
                if (await survey.SurveyLoad(fileEntry.FilePath, false/*autoSave*/) == 0)
                {
                    //??? Debug Line
                    //if (survey.Data.Info.SurveyCode == "STU_5m_E2-E3_2025-07-14")
                    //    row = row;
                    string transectNumber = string.Empty;

                    foreach (var evt in survey.Data.Events.EventList)
                    {
                        try
                        {
                            if (evt.EventDataType == Events.SurveyDataType.SurveyStart)
                            {
                                if (evt.EventData is TransectMarker marker)
                                    // Remember the transect we are currently in
                                    transectNumber = marker.MarkerName;
                                else
                                    // Shouldn't happen
                                    transectNumber = string.Empty;
                            }
                            else if (evt.EventDataType == Events.SurveyDataType.SurveyEnd)
                            {
                                // Leaving current transect
                                transectNumber = string.Empty;
                            }
                            else if (evt.EventDataType == Events.SurveyDataType.SurveyMeasurementPoints ||
                                     evt.EventDataType == Events.SurveyDataType.SurveyStereoPoint ||
                                     evt.EventDataType == Events.SurveyDataType.SurveyPoint)
                            {
                                SpeciesInfo? speciesInfo = null;
                                SurveyRulesCalc? surveyRulesCalc = null;
                                double? measurement = null;
                                int fishCount = 0;
                                bool derivedSpecies = false;
                                bool derivedLength = false;
                                bool includeRowinExport = true;


                                // Get the speciesInfo, surveyRulesCalc & measurement depending on the event type 
                                switch (evt.EventDataType)
                                {
                                    case Events.SurveyDataType.SurveyMeasurementPoints:
                                        worksheet.Cells[rowIndex, (int)ExportExcelColmns.Type].Value = "Measurement";
                                        if (evt.EventData is SurveyMeasurement surveyMeasurement)
                                        {
                                            speciesInfo = surveyMeasurement.SpeciesInfo;
                                            surveyRulesCalc = surveyMeasurement.SurveyRulesCalc;
                                            measurement = surveyMeasurement.Measurement;
                                            if (!int.TryParse(speciesInfo.Number, out fishCount))
                                                fishCount = 1;
                                        }
                                        break;

                                    case Events.SurveyDataType.SurveyStereoPoint:
                                    case Events.SurveyDataType.SurveyPoint:
                                        if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                                        {
                                            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Type].Value = "3D";
                                            speciesInfo = surveyStereoPoint.SpeciesInfo;
                                            surveyRulesCalc = surveyStereoPoint.SurveyRulesCalc;
                                        }
                                        else if (evt.EventData is SurveyPoint surveyPoint)
                                        {
                                            worksheet.Cells[rowIndex, (int)ExportExcelColmns.Type].Value = "Point";
                                            speciesInfo = surveyPoint.SpeciesInfo;
                                        }
                                        // Check the fish count
                                        if (speciesInfo is not null)
                                        {
                                            if (!int.TryParse(speciesInfo.Number, out fishCount))
                                                fishCount = 1;

                                            // Apply average measurement if requested
                                            if (applyAverageLengths && !string.IsNullOrWhiteSpace(speciesInfo.Genus) && !string.IsNullOrWhiteSpace(speciesInfo.Species))
                                            {
                                                measurement = allMeasurements.GetAverageLength(speciesInfo.Genus!, speciesInfo.Species!);
                                                derivedLength = true;
                                            }
                                        }
                                        break;
                                }

                                // Do we want to include failed RMS measurements and 3D Points in the export?
                                if (!includeFailedRMS && surveyRulesCalc is not null)
                                {
                                    if (surveyRulesCalc.SurveyRuleRMS.HasValue && surveyRulesCalc.SurveyRuleRMS == false)
                                        includeRowinExport = false;
                                }

                                // Do we want to include other failed rules measurements and 3D Points in the export?
                                if (!includeOtherFailedRules && surveyRulesCalc is not null)
                                {
                                    if ((surveyRulesCalc.SurveyRuleRange.HasValue && surveyRulesCalc.SurveyRuleRange == false) ||
                                        (surveyRulesCalc.SurveyRuleHoriz.HasValue && surveyRulesCalc.SurveyRuleHoriz == false) ||
                                        (surveyRulesCalc.SurveyRuleVert.HasValue && surveyRulesCalc.SurveyRuleVert == false))
                                    {
                                        includeRowinExport = false;
                                    }
                                }

                                // Do we want to include partial or unidentified fish in the export?
                                if (!includePartialIdentification && speciesInfo is not null && string.IsNullOrWhiteSpace(speciesInfo.Genus))
                                {
                                    includeRowinExport = false;
                                }

                                if (includeRowinExport)
                                {

                                    // Common data
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.SurveyName].Value = survey.Data.Info.SurveyCode ?? survey.Data.Info.SurveyFileName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.Depth].Value = survey.Data.Info.SurveyDepth;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.Analyst].Value = survey.Data.Info.SurveyAnalystName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.Time].Value = evt.TimeSpanTimelineController;

                                    // Hyperlink column
                                    var encodedPath = Uri.EscapeDataString(fileEntry.FilePath);
                                    var secs = evt.TimeSpanTimelineController.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                                    var cellTimeSecs = worksheet.Cells[rowIndex, (int)ExportExcelColmns.TimeSecs];
                                    cellTimeSecs.Value = $"{evt.TimeSpanTimelineController.TotalSeconds:F2}";
                                    cellTimeSecs.Hyperlink = new ExcelHyperLink($"underwatersurveyor://open?file={encodedPath}&start={secs}");
                                    // Apply the built-in Hyperlink style so it looks like Excel's default (blue underline)
                                    cellTimeSecs.StyleName = "Hyperlink";

                                    // Frame time
                                    var timeValue = evt.TimeSpanTimelineController;
                                    var cell = worksheet.Cells[rowIndex, (int)ExportExcelColmns.Time];
                                    cell.Value = timeValue;
                                    cell.Style.Numberformat.Format = "hh:mm:ss";


                                    // Calculated transect
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.Transect].Value = transectNumber;


                                    // Extract the species scientific name and common name
                                    // Also make the genus+species combined name
                                    string speciesScientificName = string.Empty;
                                    string speciesCommonName = string.Empty;
                                    string genusSpeciesScientific = string.Empty;
                                    string speciesCode = string.Empty;
                                    if (speciesInfo is not null)
                                    {
                                        if (!string.IsNullOrWhiteSpace(speciesInfo.Species))
                                        {
                                            int slash = speciesInfo.Species.IndexOf('/');
                                            // If there is a slash and it isn't right at the end
                                            if (slash > 0 && slash < speciesInfo.Species.Length - 1)
                                            {
                                                speciesScientificName = speciesInfo.Species[..slash].Trim();
                                                speciesCommonName = speciesInfo.Species[(slash + 1)..].Trim();
                                            }
                                            // Slash must be at the end
                                            else if (slash > 0)
                                            {
                                                // Get all but the last character
                                                speciesScientificName = speciesInfo.Species[..^1].Trim();
                                            }
                                            else
                                                speciesScientificName = speciesInfo.Species.Trim();

                                            // Name the Genus+Species scentific full name
                                            if (!string.IsNullOrEmpty(speciesInfo.Genus))
                                                genusSpeciesScientific = $"{speciesInfo.Genus} {speciesScientificName}";
                                            else
                                                genusSpeciesScientific = speciesScientificName;
                                        }
                                        else if (!string.IsNullOrWhiteSpace(speciesInfo.Genus))
                                        {
                                            genusSpeciesScientific = speciesInfo.Genus.Trim();
                                        }

                                        speciesCode = speciesInfo.Code ?? "";
                                    }

                                    // Extract the family scientific name and common name
                                    string familyScientificName = string.Empty;
                                    string familyCommonName = string.Empty;
                                    if (speciesInfo is not null)
                                    {
                                        if (!string.IsNullOrWhiteSpace(speciesInfo.Family))
                                        {
                                            int slash = speciesInfo.Family.IndexOf('/');
                                            // If there is a slash and it isn't right at the end
                                            if (slash > 0 && slash < speciesInfo.Family.Length - 1)
                                            {
                                                familyScientificName = speciesInfo.Family[..slash].Trim();
                                                familyCommonName = speciesInfo.Family[(slash + 1)..].Trim();
                                            }
                                            // Slash must be at the end
                                            else if (slash > 0)
                                            {
                                                // Get all but the last character
                                                familyScientificName = speciesInfo.Family[..^1].Trim();
                                            }
                                            else
                                                familyScientificName = speciesInfo.Family.Trim();
                                        }
                                    }

                                    // If the species is missing and we have a genus and the user request
                                    // we dervice missing species and try to derive the species
                                    if (deriveMissingSpecies &&
                                        speciesInfo is not null &&
                                        string.IsNullOrEmpty(speciesScientificName) &&
                                        !string.IsNullOrEmpty(speciesInfo.Genus))
                                    {
                                        string surveyNameForSite = survey.Data.Info.SurveyFileName ?? string.Empty;
                                        string? depthForScope = survey.Data.Info.SurveyDepth;
                                        var derived = allEvents.DeriveSpeciesScientific(surveyNameForSite, depthForScope, transectNumber, speciesInfo.Genus!);
                                        if (!string.IsNullOrEmpty(derived))
                                        {
                                            // We now know the species scientific name so store that and
                                            // make the scientific  genus/species name
                                            speciesScientificName = derived!;
                                            genusSpeciesScientific = $"{speciesInfo.Genus} {speciesScientificName}";
                                            derivedSpecies = true;

                                            // Lookup the species in the species code list to complete
                                            // the comment names and the species code
                                            string speciesSearchName = string.Empty; // This is the species name return according to the settings i.e. scientific/common or scientific 
                                            if (speciesCodeList.SearchSpecies(derived, "", ""))
                                            {
                                                if (speciesCodeList.SpeciesComboItems.Count == 1)
                                                {
                                                    SpeciesItem speciesItem = speciesCodeList.SpeciesComboItems[0];
                                                    familyScientificName = speciesItem.FamilyScientific;
                                                    familyCommonName = speciesItem.FamilyCommon;
                                                    speciesSearchName = speciesItem.Species;
                                                    speciesCommonName = speciesItem.SpeciesCommon;
                                                    speciesCode = speciesItem.Code;

                                                }
                                            }



                                            // Apply average measurement if requested
                                            if (applyAverageLengths && !string.IsNullOrWhiteSpace(speciesInfo.Genus) && !string.IsNullOrWhiteSpace(speciesSearchName))
                                            {
                                                measurement = allMeasurements.GetAverageLength(speciesInfo.Genus!, speciesSearchName);
                                                derivedLength = true;
                                            }
                                        }
                                    }

                                    // Load the fish count                               
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.FishCount].Value = fishCount;

                                    // Load measurement
                                    if (measurement is not null)
                                    {
                                        cell = worksheet.Cells[rowIndex, (int)ExportExcelColmns.Measurementm];
                                        cell.Value = measurement;
                                        cell.Style.Numberformat.Format = "0.000";

                                        cell = worksheet.Cells[rowIndex, (int)ExportExcelColmns.Measurementmm];
                                        cell.Value = measurement * 1000;
                                        cell.Style.Numberformat.Format = "0";
                                    }
                                    else
                                    {
                                        worksheet.Cells[rowIndex, (int)ExportExcelColmns.Measurementm].Value = "";
                                        worksheet.Cells[rowIndex, (int)ExportExcelColmns.Measurementmm].Value = "";
                                    }

                                    // Range value (mark in red text if rule not passed)
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColmns.Range];
                                    cell.Value = surveyRulesCalc?.Range ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";
                                    if (surveyRulesCalc?.SurveyRuleRange == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // Range Horizontal and vertical offsets and RMS
                                    // Horizontal value (mark in red text if rule not passed)
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColmns.HorizontalOffset];
                                    cell.Value = surveyRulesCalc?.XOffset ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";
                                    if (surveyRulesCalc?.SurveyRuleHoriz == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // Vertical value (mark in red text if rule not passed)
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColmns.VerticalOffset];
                                    cell.Value = surveyRulesCalc?.YOffset ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";
                                    if (surveyRulesCalc?.SurveyRuleVert == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // RMS values (mark RMSWorst in red text if rule not passed)
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColmns.RMS];
                                    cell.Value = surveyRulesCalc?.RMSMean ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";

                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColmns.RMSWorst];
                                    cell.Value = surveyRulesCalc?.RMSWorst ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";
                                    if (surveyRulesCalc?.SurveyRuleRMS == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // Rules passed summary
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColmns.RulesPassed];
                                    cell.Value = surveyRulesCalc?.SurveyRules;
                                    if (surveyRulesCalc?.SurveyRules == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // Species, Genus, Family, Code
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.SpeciesScientific].Value = speciesScientificName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.SpeciesCommon].Value = speciesCommonName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.Genus].Value = speciesInfo?.Genus ?? "";
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.GenusSpeciesScientific].Value = genusSpeciesScientific;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.FamilyScientific].Value = familyScientificName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.FamilyCommon].Value = familyCommonName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.SpeciesCode].Value = speciesCode;

                                    // Comment (if any)
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.Comment].Value = speciesInfo?.Comment ?? "";

                                    // Check if a species code was actually used or was it plan text or the species code is blank
                                    bool validSpeciesCode = true;
                                    if (speciesInfo is null ||
                                        string.IsNullOrEmpty(speciesInfo.Code) ||
                                        speciesInfo.Species is null ||
                                        speciesInfo.Species.IndexOf('/') == -1)
                                    {
                                        validSpeciesCode = false;
                                    }
                                    worksheet.Cells[rowIndex, (int)ExportExcelColmns.NoSpeciesCode].Value = !validSpeciesCode ? true : "";

                                    // Derived Species and Derived Length flags
                                    if (applyAverageLengths || deriveMissingSpecies)
                                    {
                                        worksheet.Cells[rowIndex, (int)ExportExcelColmns.DerivedSpecies].Value = derivedSpecies ? true : null;
                                        worksheet.Cells[rowIndex, (int)ExportExcelColmns.DerivedLength].Value = derivedLength ? true : null;
                                    }

                                    // Debug
                                    Debug.WriteLine($"Export:{rowIndex},{survey.Data.Info.SurveyCode ?? survey.Data.Info.SurveyFileName},{survey.Data.Info.SurveyDepth},{survey.Data.Info.SurveyAnalystName},{evt.TimeSpanTimelineController}");

                                    rowIndex++;
                                    exportLineCount++;
                                }
                            }
                        }
                        catch (Exception ex) when (ex is ObjectDisposedException)
                        {
                            report?.Error("", $"Export_Click ... {ex}");
                            failed = true;
                            break; // exits the foreach immediately
                        }
                        catch (Exception ex)
                        {
                            report?.Warning("", $"Export_Click ... {ex}");
                            problemCount++;
                        }
                    }
                }
            }

            // Apply an AutoFilter on the header row across the used data range
            int lastCol = (applyAverageLengths || deriveMissingSpecies)
                ? (int)ExportExcelColmns.DerivedLength
                : (int)ExportExcelColmns.Comment;
            int headerRow = noSaveOptionSelected_ExportMetadataOnly ? 3 : 1; // header shifts down when warning row is present
            int lastRow = Math.Max(headerRow, rowIndex - 1);
            worksheet.Cells[headerRow, 1, lastRow, lastCol].AutoFilter = true;

            // Size the excel columns nicely (limit to used range)
            worksheet.Cells[headerRow, 1, lastRow, lastCol].AutoFitColumns();


            if (failed)
            {
                report?.Error("", $"Export Failed, file:{exportFile}, partial export lines:{exportLineCount}");
            }
            else if (problemCount > 0)
            {
                report?.Warning("", $"Export Completed, file:{exportFile}, problemed lines:{problemCount}, partial export lines:{exportLineCount}");
            }
            else
            {
                report?.Info("", $"Export Completed, file:{exportFile}, export lines:{exportLineCount}");
            }
        }


        /// <summary>
        /// Write the export metadata to an Excel sheet
        /// </summary>
        /// <param name="worksheet"></param>
        private void ExportMetadatatSheet(ExcelWorksheet worksheet)
        {
            int rowIndex = 1;

            // Optional draft warning
            if (noSaveOptionSelected_ExportMetadataOnly)
            {
                ApplyWarningMessage(worksheet, rowIndex, 1);
                rowIndex += 2; // leave one blank row before table
            }

            // Use only bound columns to keep header/data aligned
            var boundColumns = SurveyGrid.Columns
                .OfType<DataGridBoundColumn>()
                .Select(col => new
                {
                    Header = col.Header?.ToString(),
                    Path = (col.Binding as Binding)?.Path?.Path
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Header) && !string.IsNullOrWhiteSpace(x.Path))
                .ToList();

            if (boundColumns.Count == 0)
                return;

            // Headers
            int colIndex = 1;
            foreach (var bc in boundColumns)
                worksheet.Cells[rowIndex, colIndex++].Value = bc.Header;
            // Freeze panes at D2 (freeze row 1 and columns A-C)
            worksheet.View.FreezePanes(2, 4);
            rowIndex++;

            // Rows
            var items = SurveyGrid.ItemsSource?.Cast<object>()?.ToList();
            if (items is null) return;

            foreach (var item in items)
            {
                // Only export rows marked Include == true
                var propInclude = item.GetType().GetProperty("Include");
                if (propInclude is not null && (bool?)propInclude.GetValue(item) == true)
                {
                    colIndex = 1; // reset per row
                    foreach (var bc in boundColumns)
                    {
                        var prop = item.GetType().GetProperty(bc.Path!);
                        var value = prop?.GetValue(item);
                        worksheet.Cells[rowIndex, colIndex++].Value = value;
                    }
                    rowIndex++;
                }
            }

            // Auto-fit visible range (headers + data)
            worksheet.Cells[1, 1, rowIndex - 1, boundColumns.Count].AutoFitColumns();
        }


        /// <summary>
        /// Apply a warning message to indicate that recalculation were done on the fly
        /// during the export (and therefore harder to reproduce)
        /// </summary>
        /// <param name="worksheet"></param>
        /// <param name="row"></param>
        /// <param name="column"></param>
        private void ApplyWarningMessage(ExcelWorksheet worksheet, int row, int column)
        {
            if (worksheet is null) return;

            var cell = worksheet.Cells[row, column];
            cell.Value = DraftWarningText;
            cell.Style.Font.Bold = true;
            cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);
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


    /// <summary>
    /// This is the case that is bound to the SurveyGrid DataGrid
    /// </summary>
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
        public string FileName { get; set; } = string.Empty; // clean metadata name
        public string FileNameDisplay { get; set; } = string.Empty; 
        public string SurveyType { get; set; } = string.Empty;
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
        public double TotalRMS { get; set; }
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
        public string AveLengthSummary { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


    /// <summary>
    /// Used to hide a UI element when the bound bool is true
    /// Specifically used to hide the Include checkbox when the row 
    /// is the totals row
    /// </summary>
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


    // Stores all measurements per (genus,species) across surveys using normalized, case-insensitive keys
    // Used to provide average length for point events that do not have a measurement
    internal sealed class AllMeasurements
    {
        // Key is normalized lower/trimmed genus/species
        private readonly Dictionary<(string genus, string species), (double sum, int count)> map = new();

        private static (string genus, string species) Key(string genus, string species)
            => ((genus ?? string.Empty).Trim().ToLowerInvariant(), (species ?? string.Empty).Trim().ToLowerInvariant());

        public void Clear() => map.Clear();


        /// <summary>
        /// Parses the events and extract all the measurement events.  Those thoses are added
        /// to a list so that a genus length for this genus and an average length for the 
        /// species can both be access later
        /// </summary>
        /// <param name="events"></param>
        public void Add(ObservableCollection<Event> events)
        {
            if (events is null) return;
            foreach (var e in events)
            {
                if (e.EventDataType != SurveyDataType.SurveyMeasurementPoints) continue;
                if (e.EventData is not SurveyMeasurement m) continue;
                if (!m.Measurement.HasValue || m.Measurement.Value <= 0) continue;

                var gi = m.SpeciesInfo?.Genus;
                if (string.IsNullOrWhiteSpace(gi) ) continue;

                // Add a genus only measurements (used for averages where only the genus ID is available)
                var kgi = Key(gi, string.Empty);
                var (sumgi, countgi) = map.TryGetValue(kgi, out var vgi) ? vgi : (0d, 0);
                map[kgi] = (sumgi + m.Measurement!.Value, countgi + 1);

                // Add genus/species measurements
                var si = m.SpeciesInfo?.Species;
                if (string.IsNullOrWhiteSpace(si)) continue;

                var k = Key(gi, si);
                var (sum, count) = map.TryGetValue(k, out var v) ? v : (0d, 0);
                map[k] = (sum + m.Measurement!.Value, count + 1);
            }
        }

        public double? GetAverageLength(string genus, string species)
        {
            var k = Key(genus, species);
            if (map.TryGetValue(k, out var v) && v.count > 0)
                return v.sum / v.count;
            return null;
        }

        /// <summary>
        /// Returns true if an average length is available for the given genus/species key
        /// (i.e. the normalized key exists in the map)
        /// </summary>
        public bool IsAveragwLengthAvailable(string genus, string species)
        {
            var k = Key(genus, species);
            return map.ContainsKey(k);
        }
    }


    // NEW: Aggregator of events for deriving most common species by scope
    internal sealed class AllEvents
    {
        // (Survey, Transect) -> Genus -> Species -> Count
        private readonly Dictionary<(string survey, string transect), Dictionary<string, Dictionary<string, int>>> bySurvey = new();
        private readonly Dictionary<(string site, string? depth), Dictionary<string, Dictionary<string, int>>> bySiteDepth = new();
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, int>>> bySite = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, int>> overall = new(StringComparer.OrdinalIgnoreCase); // genus -> (species -> count)

        private static string Norm(string? s) => (s ?? string.Empty).Trim();
        private static string NormKey(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();

        public void Clear()
        {
            bySurvey.Clear();
            bySiteDepth.Clear();
            bySite.Clear();
            overall.Clear();
        }


        public void Add(string surveyFileName, string? depth, string? surveyCode, ObservableCollection<Event> events)
        {
            if (events is null) return;
            string surveyKey = Norm(surveyFileName);
            string site = ExtractSiteFromSurveyName(surveyFileName);

            var siteDepthKey = (site, depth);
            if (!bySiteDepth.TryGetValue(siteDepthKey, out var siteDepthMap))
            {
                siteDepthMap = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                bySiteDepth[siteDepthKey] = siteDepthMap;
            }
            if (!bySite.TryGetValue(site, out var siteMap))
            {
                siteMap = new(StringComparer.OrdinalIgnoreCase);
                bySite[site] = siteMap;
            }

            string transectNumber = string.Empty; // tracks current transect segment

            foreach (var e in events)
            {
                // Maintain current transect context
                if (e.EventDataType == Events.SurveyDataType.SurveyStart)
                {
                    if (e.EventData is TransectMarker marker)
                        transectNumber = marker.MarkerName ?? string.Empty;
                    else
                        transectNumber = string.Empty;
                }
                else if (e.EventDataType == Events.SurveyDataType.SurveyEnd)
                {
                    transectNumber = string.Empty; // leaving transect
                }

                SpeciesInfo? s = e.EventData switch
                {
                    SurveyMeasurement m => m.SpeciesInfo,
                    SurveyStereoPoint s3d => s3d.SpeciesInfo,
                    SurveyPoint sp => sp.SpeciesInfo,
                    _ => null
                };
                if (s is null) continue;
                var genus = NormKey(s.Genus);
                if (string.IsNullOrWhiteSpace(genus)) continue;
                var species = NormKey(ExtractScientificFromSpeciesField(s.Species));
                if (string.IsNullOrWhiteSpace(species)) continue;

                // (survey, transect) level
                var surveyTransectKey = (surveyKey, transectNumber);
                if (!bySurvey.TryGetValue(surveyTransectKey, out var surveyTransectMap))
                {
                    surveyTransectMap = new(StringComparer.OrdinalIgnoreCase);
                    bySurvey[surveyTransectKey] = surveyTransectMap;
                }

                Increment(surveyTransectMap, genus, species);
                Increment(siteDepthMap, genus, species);
                Increment(siteMap, genus, species);
                Increment(overall, genus, species);
            }
        }

        private static void Increment(Dictionary<string, Dictionary<string, int>> target, string genus, string species)
        {
            if (!target.TryGetValue(genus, out var sp))
            {
                sp = new(StringComparer.OrdinalIgnoreCase);
                target[genus] = sp;
            }
            sp[species] = sp.TryGetValue(species, out var c) ? c + 1 : 1;
        }

        public string? MostCommonSpeciesForGenusInSurveyTransect(string surveyFileName, string transect, string genus)
        {
            var key = (Norm(surveyFileName), transect ?? string.Empty);
            if (!bySurvey.TryGetValue(key, out var map)) return null;
            return Top1(map, genus);
        }

        public string? MostCommonSpeciesForGenusInSiteDepth(string surveyFileName, string? depth, string genus)
        {
            string site = ExtractSiteFromSurveyName(surveyFileName);
            if (!bySiteDepth.TryGetValue((site, depth), out var m)) return null;
            return Top1(m, genus);
        }

        public string? MostCommonSpeciesForGenusInSite(string surveyFileName, string genus)
        {
            string site = ExtractSiteFromSurveyName(surveyFileName);
            if (!bySite.TryGetValue(site, out var m)) return null;
            return Top1(m, genus);
        }

        public string? MostCommonSpeciesForGenusOverall(string genus)
        {
            var gk = NormKey(genus);
            if (!overall.TryGetValue(gk, out var speciesMap)) return null;
            if (speciesMap.Count == 0) return null;
            return speciesMap
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => kv.Key)
                .FirstOrDefault();
        }

        public string? DeriveSpeciesScientific(string surveyFileName, string? depth, string transectNumber, string genus)
        {
            var s1 = MostCommonSpeciesForGenusInSurveyTransect(surveyFileName, transectNumber, genus);
            if (!string.IsNullOrWhiteSpace(s1)) return s1;
            var s2 = MostCommonSpeciesForGenusInSiteDepth(surveyFileName, depth, genus);
            if (!string.IsNullOrWhiteSpace(s2)) return s2;
            var s3 = MostCommonSpeciesForGenusInSite(surveyFileName, genus);
            if (!string.IsNullOrWhiteSpace(s3)) return s3;
            var s4 = MostCommonSpeciesForGenusOverall(genus);
            if (!string.IsNullOrWhiteSpace(s4)) return s4;
            return null;
        }

        private static string? Top1(Dictionary<string, Dictionary<string, int>> scopeMap, string genus)
        {
            var gk = NormKey(genus);
            if (!scopeMap.TryGetValue(gk, out var speciesMap) || speciesMap.Count == 0) return null;
            return speciesMap
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => kv.Key)
                .FirstOrDefault();
        }

        internal static string ExtractSiteFromSurveyName(string surveyFileName)
        {
            var name = Path.GetFileNameWithoutExtension(Norm(surveyFileName));
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int us = name.IndexOf('_');
            int hy = name.IndexOf('-');
            int cut = -1;
            if (us >= 0 && hy >= 0) cut = Math.Min(us, hy);
            else if (us >= 0) cut = us;
            else if (hy >= 0) cut = hy;
            return cut > 0 ? name[..cut] : name;
        }

        private static string ExtractScientificFromSpeciesField(string? field)
        {
            if (string.IsNullOrWhiteSpace(field)) return string.Empty;
            int slash = field.IndexOf('/');
            if (slash > 0) return field[..slash].Trim();
            if (slash == field.Length - 1) return field[..^1].Trim();
            return field.Trim();
        }
    }


    // Holds per-survey distinct species for point events (Single and 3D)
    // It is a list for each survey of the species (genus,species) that have
    // Single Point  3D Point events. Those point events may required a average 
    // measurement to be implied at export time
    internal sealed class DistinctSpeciesListForPointEventsPerSurvey
    {
        private readonly Dictionary<string, HashSet<(string genus, string species)>> bySurvey = new(StringComparer.OrdinalIgnoreCase);

        private static (string genus, string species) Key(string genus, string species)
            => ((genus ?? string.Empty).Trim().ToLowerInvariant(), (species ?? string.Empty).Trim().ToLowerInvariant());

        /// <summary>
        /// 
        /// 
        /// </summary>
        /// <remarks>This method processes the provided events to extract species measurementinformation from survey
        /// points and associates the resulting data with the specified survey file name. Only events of type <see
        /// cref="SurveyDataType.SurveyPoint"/> or <see cref="SurveyDataType.SurveyStereoPoint"/> are considered. If no
        /// qualifying points are found, the survey file name is removed from the collection to indicate the absence of
        /// relevant data.</remarks>
        public void Add(string surveyFileName, ObservableCollection<Event> events)
        {
            if (string.IsNullOrWhiteSpace(surveyFileName) || events is null) return;

            if (!bySurvey.TryGetValue(surveyFileName, out var set))
            {
                set = new();
                bySurvey[surveyFileName] = set;
            }

            foreach (var e in events)
            {
                if (e.EventDataType != SurveyDataType.SurveyPoint && e.EventDataType != SurveyDataType.SurveyStereoPoint) continue;
                SpeciesInfo? s = e.EventData switch
                {
                    SurveyPoint sp => sp.SpeciesInfo,
                    SurveyStereoPoint s3d => s3d.SpeciesInfo,
                    _ => null
                };
                var genus = s?.Genus;
                var species = s?.Species;
                if (string.IsNullOrWhiteSpace(genus) || string.IsNullOrWhiteSpace(species)) continue;
                set.Add(Key(genus!, species!));
            }

            if (set.Count == 0)
            {
                // if none found, remove to keep dictionary tidy and to signal "no qualifying points"
                bySurvey.Remove(surveyFileName);
            }
        }


        /// <summary>
        /// Returns the text to used in the AveLengthSummary column for the given survey
        /// </summary>
        /// <param name="surveyFileName"></param>
        /// <param name="all"></param>
        /// <returns></returns>
        public string GetSummaryText(string surveyFileName, AllMeasurements all)
        {
            if (string.IsNullOrWhiteSpace(surveyFileName)) return string.Empty;
            if (!bySurvey.TryGetValue(surveyFileName, out var set) || set.Count == 0) return string.Empty;

            int missing = 0;
            foreach (var (genus, species) in set)
            {
                var avg = all.GetAverageLength(genus, species);
                if (!avg.HasValue) missing++;
            }
            return missing == 0 ? "Available" : $"{missing} unavailable";
        }


        /// <summary>
        /// Returns the text used in the totals row AveLengthSummary column
        /// </summary>
        /// <param name="all"></param>
        /// <returns></returns>
        public string GetTotalsSummary(AllMeasurements all)
        {
            var union = new HashSet<(string genus, string species)>();
            foreach (var kvp in bySurvey)
                union.UnionWith(kvp.Value);
            if (union.Count == 0) return string.Empty; // nothing to report
            int missing = 0;
            foreach (var (genus, species) in union)
            {
                var avg = all.GetAverageLength(genus, species);
                if (!avg.HasValue) missing++;
            }
            return missing == 0 ? "All available" : $"{missing} unavailable";
        }
    }

}
