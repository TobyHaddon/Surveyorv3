// Version 1.1
// Added support for COCO JSON


using ActionCameraMP4MetadataExtraction;
using CommunityToolkit.WinUI.Animations;
using CommunityToolkit.WinUI.UI.Controls;
using MathNet.Numerics.LinearAlgebra.Factorization;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.DirectX;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using OfficeOpenXml;
using Surveyor.Events;
using Surveyor.Helper;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
//using System.Drawing;

//using System.Drawing;
//using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//???using System.Xml;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;
using WinUIEx;
using static Microsoft.IO.RecyclableMemoryStreamManager;
using static Surveyor.Survey.DataClass;
using static Surveyor.User_Controls.BulkSurveyExportDialog;
using static System.Net.WebRequestMethods;



namespace Surveyor.User_Controls
{
    // COCO Image
    public class COCOImageDataObject
    {
        public string Title { get; set; } = string.Empty;
        public ImageSource? ImageSource { get; set; } = null;
    }

    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class BulkSurveyExportDialog : WindowEx
    {
        // Export Types
        public enum ExportType { Excel, COCO };
        private readonly ExportType exportType;

        // The list of survey files to export with their associated metadata
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
        public enum ExportExcelColumns { SurveyName = 1,
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

        // COCO categoryId
        private enum CategoryId
        {
            Family, Genus, Species
        }

        // COCO Image list
        public ObservableCollection<COCOImageDataObject> COCODatasetImages { get; set; } = [];

        // Image export sub-directories
        private const string folderRaw = @"\Raw";
        private const string folderCropped = @"\Cropped";
        private const string folderMarkup = @"\Markup";

        // Thumbnail display size
        private const int thumbnailWidth = 190;
        private const int thumbnailHeight = 130;

        // Cropping margin control
        private readonly CroppingMargin croppingMargin = new();

        public BulkSurveyExportDialog(ExportType _exportType, Reporter? _report, SpeciesCodeList _speciesCodeList)
        {
            // Remember the reporter & species code list
            exportType = _exportType;
            report = _report;
            speciesCodeList = _speciesCodeList;


            this.InitializeComponent();

            // Use custom title bar area
            ExtendsContentIntoTitleBar = true;                        
            SetTitleBar(CustomTitleBar);

            // Set the title based on the export type
            SurveyExportTitle.Text = exportType switch
            {
                ExportType.Excel => "Bulk Survey Excel Export",
                ExportType.COCO => "Bulk Survey COCO Export",
                _ => throw new ArgumentOutOfRangeException(nameof(exportType), exportType, "Unsupported export type")
            };

            // Grid items source
            SurveyGrid.ItemsSource = SurveyFiles;

            // Subscribe to data grid changes and keep the selected count at the bottom up-to-date
            SurveyFiles.CollectionChanged += SurveyFiles_CollectionChanged;

            // Hide/hide control based on export type
            switch (exportType)
            {
                case ExportType.Excel:
                    // Controls that are visible for Excel export
                    InstructionsExcel.Visibility = Visibility.Visible;
                    InstructionsCOCO.Visibility = Visibility.Collapsed;
                    IncludePartialIdentification.Visibility = Visibility.Visible;
                    DeriveMissingSpecies.Visibility = Visibility.Visible;
                    ApplyAverageLengths.Visibility = Visibility.Visible;
                    ExtractRawFrame.Visibility = Visibility.Collapsed;
                    ExtractCroppedImage.Visibility = Visibility.Collapsed;
                    BoxRawFrame.Visibility = Visibility.Collapsed;
                    MarkRawFrame.Visibility = Visibility.Collapsed;
                    MoreActionsButton.Visibility = Visibility.Visible;
                    break;

                case ExportType.COCO:
                    // Controls that are visible for COCO JSON export
                    InstructionsExcel.Visibility = Visibility.Collapsed;
                    InstructionsCOCO.Visibility = Visibility.Visible;
                    IncludePartialIdentification.Visibility = Visibility.Collapsed;
                    DeriveMissingSpecies.Visibility = Visibility.Collapsed;
                    ApplyAverageLengths.Visibility = Visibility.Collapsed;
                    ExtractRawFrame.Visibility = Visibility.Visible;
                    ExtractCroppedImage.Visibility = Visibility.Visible;
                    BoxRawFrame.Visibility = Visibility.Visible;
                    MarkRawFrame.Visibility = Visibility.Visible;
                    MoreActionsButton.Visibility = Visibility.Collapsed;
                    break;
            }

            // Setup hard coded cropping margin table (could be made more dynamic in the future if needed)
            croppingMargin.AddSizingTableItem(measurementGreaterThen: 0, margin: 5);
            croppingMargin.AddSizingTableItem(measurementGreaterThen: 50, margin: 10);
            croppingMargin.AddSizingTableItem(measurementGreaterThen: 150, margin: 15);

            // Show DataGrid and hide the GridView
            SetDisplayMode(trueDataFalseImages: true);

            // Initial update
            UpdateSelectAllCheckBoxState();
            UpdateButtons();
            RebuildTotalsRow(0);
        }


        /// 
        /// EVENTS
        /// 


        private void SurveyFiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (SurveyFileEntry item in e.OldItems)
                    item.PropertyChanged -= SurveyFileEntry_PropertyChanged;
            }

            if (e.NewItems is not null)
            {
                foreach (SurveyFileEntry item in e.NewItems)
                    item.PropertyChanged += SurveyFileEntry_PropertyChanged;
            }
        }


        private void SurveyFileEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SurveyFileEntry.Include))
            {
                CheckSelectedItemsAreCompatible();
                UpdateItemCountText();
                UpdateButtons();
                UpdateSelectAllCheckBoxState();
            }
        }

        private void SelectFolder_Click(object sender, RoutedEventArgs e) => _ = SelectFolderClickAsync();

        /// <summary>
        /// Get the user to select the folder to export survey files from
        /// and build a list of those files and call ProcessSurveyFilesAsync
        /// to export them
        /// </summary>
        private int selectFolderEntryCount = 0;
        private async Task SelectFolderClickAsync()
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

                        List<string> files = [.. SafeEnumerateFiles(folder.Path, "*.survey", IncludeSubfoldersCheckBox.IsChecked == true)];
                        
                        await ProcessSurveyFilesAsync(files, 
                                                      IncludeSubfoldersCheckBox.IsChecked == true, 
                                                      false/*recalculate*/, 
                                                      false/*Save back to survey files*/);
                        UpdateButtons();
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during the process
                Debug.WriteLine($"BulkSurveyExportDialog.SelectFolderClickAsync Error {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref selectFolderEntryCount);
            }
        }

        private void SelectFiles_Click(object sender, RoutedEventArgs e) => _ = SelectFilesClickAsync();


        /// <summary>
        /// Get the user to select the survey files to export from
        /// and call ProcessSurveyFilesAsync to export them
        /// </summary>
        private int selectFilesEntryCount = 0;
        private async Task SelectFilesClickAsync()
        {
            try
            {
                int entryCount = Interlocked.Increment(ref selectFilesEntryCount);
                // Make sure we only open the settings window once.
                // This can happen if the survey and movies are loaded and the user clicks the settings a few times.
                if (entryCount == 1)
                {
                    FileOpenPicker openPicker = new()
                    {
                        ViewMode = PickerViewMode.List,
                        SuggestedStartLocation = PickerLocationId.DocumentsLibrary
                    };

                    // Filter: Surveys (*.survey)
                    openPicker.FileTypeFilter.Add(".survey");

                    // Associate picker with this window
                    IntPtr hWnd = WindowNative.GetWindowHandle(this);
                    InitializeWithWindow.Initialize(openPicker, hWnd);

                    IReadOnlyList<StorageFile> pickedFiles = await openPicker.PickMultipleFilesAsync();

                    if (pickedFiles is not null && pickedFiles.Count > 0)
                    {
                        List<string> files = [.. pickedFiles
                            .Select(f => f.Path)
                            .Where(p => !string.IsNullOrWhiteSpace(p))];

                        if (files.Count > 0)
                        {
                            FolderPathTextBox.Text = files.Count == 1
                                ? files[0]
                                : $"{files.Count} survey files selected";

                            await ProcessSurveyFilesAsync(
                                files,
                                false/*includeSubfolders not used for explicit file selection*/,
                                false/*recalculate*/,
                                false/*save*/);

                            UpdateButtons();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during the process
                Debug.WriteLine($"BulkSurveyExportDialog.SelectFilesClickAsync Error {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref selectFilesEntryCount);
            }
        }



        private async Task ProcessSurveyFilesAsync(List<string> files, bool includeSubfolders, bool recalc, bool save, CalibrationData? calibrationData = null)
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

                var distinctSpeciesPointsBySurvey = new DistinctSpeciesListForPointEventsPerSurvey();

                foreach (string fileSpec in files)
                {
                    try
                    {
                        // Open the survey with no auto save
                        var survey = new Survey(report!);
                        if (await survey.SurveyLoadAsync(fileSpec, false/*autoSave*/) == 0)
                        {
                            // Get the frame size
                            int frameWidth = 0;
                            int frameHeight = 0;

                            // Used the left mp4 file to get the frame size
                            string mediaFileLeft = survey.GetLeftMediaFileSpec(0);

                            // Get the frame size and frame rate
                            CalibrationData? calibrationData = survey.Data.Calibration.GetPreferredCalibrationData(null, null);

                            if (calibrationData is null)
                                continue;

                            // Try to get frame sizes
                            (frameWidth, frameHeight) = calibrationData.LeftCameraCalibration.GetFrameSize();

                            // Force a recalculate?
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

                            // After any recalculate/save, aggregate measurements and distinct species for point events
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

                            // Check if the Survey File Name and the Survey Code are consistent
                            string surveyNameAndCodeCheck = CheckSurveyFileNameSurveyFileNameAndSurveyCodeAreConsistent(fileName, survey.Data.Info.SurveyFileName ?? "", survey.Data.Info.SurveyCode ?? "");

                            string surveyCode = survey.Data.Info.SurveyCode ?? "";

                            //???string leftMediaFile = survey.Data.Media.LeftMediaFileNames.Count > 0
                            //    ? (survey.Data.Media.LeftMediaFileNames[0] ?? "Missing")
                            //    : "Missing";
                            //string rightMediaFile = survey.Data.Media.RightMediaFileNames.Count > 0
                            //    ? (survey.Data.Media.RightMediaFileNames[0] ?? "Missing")
                            //    : "Missing";

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

                            // Total SurveyMeasurementPoints and SurveyStereoPoint with blank rules calculations
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

                            // Total SurveyMeasurementPoints and SurveyStereoPoint with failed rules calculations
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

                            // Total the cumulative RMSWorst
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
                                    horizontalRule = "No horizontal";
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


                            // Get the rules hash so consistency of rules can be checked across surveys
                            int rulesHash = survey.Data.SurveyRules.GetHashCode();


                            // Check for calibration
                            string calibration = string.Empty;
                            SurveyorCalibrationData.CalibrationData? calibrationDataPreferred = survey.Data.Calibration.GetPreferredCalibrationData(frameWidth, frameHeight);
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
                                LeftMediaFiles = survey.Data.Media.LeftMediaFileNames.ToList(),
                                RightMediaFiles = survey.Data.Media.RightMediaFileNames.ToList(),
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
                    .SelectMany(entry =>
                        (entry.LeftMediaFiles ?? [])
                            .Concat(entry.RightMediaFiles ?? []))
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
                // Flag any media files used multiple times
                entry.LeftMediaFiles = [.. entry.LeftMediaFiles.Select(file =>
                            !string.IsNullOrWhiteSpace(file) && duplicatedFileSet.Contains(file)
                                ? "*" + file
                                : file)];

                entry.RightMediaFiles = [.. entry.RightMediaFiles.Select(file =>
                            !string.IsNullOrWhiteSpace(file) && duplicatedFileSet.Contains(file)
                                ? "*" + file
                                : file)];

                await Task.Delay(10); // Throttle to avoid UI freeze
                SurveyFiles.Add(entry);
            }


            RebuildTotalsRow(totalTransects);


            CheckSelectedItemsAreCompatible();
            UpdateItemCountText();
            UpdateButtons();
            UpdateSelectAllCheckBoxState();
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }


        /// <summary>
        /// Displays a warning of the survey rules differ in the selected surveys or
        /// if different calibration data used
        /// </summary>
        private void CheckSelectedItemsAreCompatible()
        {
            // Check for non-matching rules (take a snapshot of the observable collection)
            if (!CheckThatAllRulesMatch([.. SurveyFiles]))
            {
                SetValidationText(false/*invalid*/, RulesMismatchPanel, RulesMismatchGlyph, RulesMismatchValidationText, "Not every survey has the same rules settings", "Check the different rules columns to find the survey(s) with differing rules");
            }
            else
            {
                SetValidationText(null/*hide*/, RulesMismatchPanel, RulesMismatchGlyph, RulesMismatchValidationText, "", "");
            }

            // Check for non-matching calibration data  (take a snapshot of the observable collection)
            if (!CheckThatAllCalibrationDataMatch([.. SurveyFiles]))
            {
                SetValidationText(false/*invalid*/, CalibrationDataMismatchPanel, CalibrationDataMismatchGlyph, CalibrationDataMismatchValidationText, "Not every survey is using the same calibration data", "This can happen if the camera rig needed to be re-calibrated at some stage.");
            }
            else
            {
                SetValidationText(null/*hide*/, CalibrationDataMismatchPanel, CalibrationDataMismatchGlyph, CalibrationDataMismatchValidationText, "", "");
            }
        }


        /// <summary>
        /// Calculates and displays the totals of the selected surveys and the count 
        /// of how many surveys are selected vs total surveys. This is called after 
        /// loading the surveys and after any change to the include check box of any 
        /// survey file entry.
        /// </summary>
        private void UpdateItemCountText()
        {
            int total = SurveyFiles.Count(sf=>!sf.IsTotalRow);
            int selected = SurveyFiles.Count(f => f.Include && !f.IsTotalRow);
            ItemCountTextBlock.Text = $"{total} Items ({selected} selected)";
        }


        /// <summary>
        /// Create a List<> of files from the indicated root folder and pattern. 
        /// If recurse is true then all sub-folders are also searched. 
        /// Any folders that cannot be accessed are skipped with a warning in the report.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="pattern"></param>
        /// <param name="recurse"></param>
        /// <returns></returns>
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
        /// Parse the list of surveys and check all surveys that have the 
        /// include check box ticked have the sames rules applied
        /// </summary>
        /// <param name="entries"></param>
        /// <returns></returns>
        private static bool CheckThatAllRulesMatch(List<SurveyFileEntry> entries)
        {
            bool ret = true;

            // Remember there is a totals line which needs to be ignored
            if (entries.Count > 1 + 1)
            {

                int firstRulesHash = entries[0].RulesHash;

                for (int i = 1; i < entries.Count - 1; i++)
                {
                    if (entries[i].Include)
                    {
                        if (firstRulesHash != entries[i].RulesHash)
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
        /// Parse the list of surveys and check all surveys that have the 
        /// include check box ticked are using the same calibration data
        /// </summary>
        /// <param name=""></param>
        /// <returns></returns>
        private static bool CheckThatAllCalibrationDataMatch(List<SurveyFileEntry> entries)
        {
            bool ret = true;

            // Remember there is a totals line which needs to be ignored
            if (entries.Count > 1 + 1)
            {
                string firstCalibrationHash = entries[0].CalibrationHash;

                for (int i = 1; i < entries.Count - 1; i++)
                {
                    if (entries[i].Include)
                    {
                        if (firstCalibrationHash != entries[i].CalibrationHash)
                        {
                            ret = false;
                            break;
                        }
                    }
                }
            }

            return ret;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SurveyGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.DataContext is SurveyFileEntry sfe && sfe.IsTotalRow)
            {
                e.Row.Background = (Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"];
                // Font weight customization omitted due to namespace limitations
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (exportType == ExportType.Excel)
                _ = ExportExcelClickAsync();
            else if (exportType == ExportType.COCO)
                _ = ExportCOCOClickAsync();
        }
        

        private void HeaderSelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var file in SurveyFiles)
            {
                if (!file.IsTotalRow)
                    file.Include = true;
            }
            UpdateItemCountText();
        }

        private void HeaderSelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var file in SurveyFiles)
            {
                if (!file.IsTotalRow)
                    file.Include = false;
            }
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
                Include = false,
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


        private void RecalcNoSave_Click(object sender, RoutedEventArgs e) => _ = RecalcNoSaveAsync();
        private async Task RecalcNoSaveAsync()
        {
            // No save option selected. Don't allow export to avoid confusion
            noSaveOptionSelected_ExportMetadataOnly = true;
            ExportButton.Content = "Export Metadata";

            await RecalcAsync(false/*trueSaveFalseNoSave*/);
        }


        private void RecalcSave_Click(object sender, RoutedEventArgs e) => _ = RecalcSaveAsync();
        private async Task RecalcSaveAsync()
        {
            // Save option selected. Export allowed
            noSaveOptionSelected_ExportMetadataOnly = false;
            ExportButton.Content = "Export";

            await RecalcAsync(true/*trueSaveFalseNoSave*/);
        }


        /// <summary>
        /// Prompt the use for a new calibration file. Then use that
        /// calibration data to recalculate all the measurements and rules
        /// </summary>
        /// <param name="save"></param>
        private async Task RecalcAsync(bool trueSaveFalseNoSave)
        {        
            if (!string.IsNullOrEmpty(FolderPathTextBox.Text))
            {
                // Make file list from the 
                List<string> files = [.. SurveyFiles
                                        .Where(sf => !sf.IsTotalRow && !string.IsNullOrWhiteSpace(sf.FilePath))
                                        .Select(sf => sf.FilePath)];
                
                await ProcessSurveyFilesAsync(files,
                                              IncludeSubfoldersCheckBox.IsChecked == true,
                                              true/*recalculation*/,
                                              trueSaveFalseNoSave/*Save back to survey files*/);
                UpdateButtons();
            }
        }


        private void NewCalibRecalcNoSave_Click(object sender, RoutedEventArgs e) => _ = NewCalibRecalcNoSaveAsync();
        private async Task NewCalibRecalcNoSaveAsync()
        {
            // No save option selected. Don't allow export to avoid confusion
            noSaveOptionSelected_ExportMetadataOnly = true;
            ExportButton.Content = "Export Metadata";

            await NewCalibRecalcAsync(false/*save*/);
        }


        private void NewCalibRecalcSave_Click(object sender, RoutedEventArgs e) => _ = NewCalibRecalcSaveAsync();
        private async Task NewCalibRecalcSaveAsync()
        {
            // Save option selected. Export allowed
            noSaveOptionSelected_ExportMetadataOnly = false;
            ExportButton.Content = "Export";

            await NewCalibRecalcAsync(true/*save*/);
        }


        /// <summary>
        /// Prompt the use for a new calibration file. Then use that
        /// calibration data to recalculate all the measurements and rules
        /// </summary>
        /// <param name="save"></param>
        private async Task NewCalibRecalcAsync(bool trueSaveFalseNoSave)
        {
            // Get new calibration data
            CalibrationData? calibrationData = await ImportCalibrationAsync();

            if (calibrationData is not null && !string.IsNullOrEmpty(FolderPathTextBox.Text))
            {
                // Make file list from the 
                List<string> files = [.. SurveyFiles
                                        .Where(sf => !sf.IsTotalRow && !string.IsNullOrWhiteSpace(sf.FilePath))
                                        .Select(sf => sf.FilePath)];

                await ProcessSurveyFilesAsync(files, 
                                              IncludeSubfoldersCheckBox.IsChecked == true,
                                              true/*recalculate*/,
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
        private async Task<CalibrationData?> ImportCalibrationAsync()
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
        /// Excel Export
        /// </summary>
        /// <returns></returns>
        private async Task ExportExcelClickAsync()
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
                        await ExportExcelDatatSheetAsync(package, worksheetData, file.Path);
                    }

                    // Write the survey by survey metadata
                    var worksheetMetadata = package.Workbook.Worksheets.Add("Metadata");
                    ExportExcelMetadatatSheet(worksheetMetadata);

                    // write to the file
                    package.SaveAs(stream);
                    await stream.FlushAsync();

                    // Commit the updates
                    var status = await CachedFileManager.CompleteUpdatesAsync(file);
                    if (status != FileUpdateStatus.Complete)
                    {
                        report?.Warning("", $"Export completed with status: {status}");
                    }
                }
                catch (Exception ex)
                {
                    report?.Error("", $"Export failed, {ex.Message}");
                }
            }

            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;

            // Close the dialog
            this.Close();
        }


        /// <summary>
        /// Export all the data from each selected survey to an excel spreadsheet
        /// </summary>
        /// <param name="package"></param>
        /// <param name="worksheet"></param>
        /// <returns></returns>
        private async Task ExportExcelDatatSheetAsync(ExcelPackage package, ExcelWorksheet worksheet, string exportFile)
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
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.SurveyName].Value = "Survey Name";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Depth].Value = "Depth";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Transect].Value = "Transect";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Analyst].Value = "Operator";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Time].Value = "Position Time";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.TimeSecs].Value = "Position Secs";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Type].Value = "Type";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.FishCount].Value = "Fish Count";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Measurementm].Value = "Measurement(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Measurementmm].Value = "Measurement(mm)";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Range].Value = "Distance (m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.HorizontalOffset].Value = "Horizontal Offset(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.VerticalOffset].Value = "Vertical Offset(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.RMS].Value = "RMS(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.RMSWorst].Value = "RMSWorst(m)";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.RulesPassed].Value = "Rules Passed";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.SpeciesScientific].Value = "Species Scientific";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.SpeciesCommon].Value = "Species Common";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Genus].Value = "Genus";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.GenusSpeciesScientific].Value = "Genus Species Scientific";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.FamilyScientific].Value = "Family Scientific";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.FamilyCommon].Value = "Family Common";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.SpeciesCode].Value = "Species Code";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.NoSpeciesCode].Value = "Species Not Coded";
            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Comment].Value = "Comment";
            if (applyAverageLengths || deriveMissingSpecies)
            {
                worksheet.Cells[rowIndex, (int)ExportExcelColumns.DerivedSpecies].Value = "Derived Species";
                worksheet.Cells[rowIndex, (int)ExportExcelColumns.DerivedLength].Value = "Derived Length";
            }
            // Freeze panes at D2 (freeze row 1 and columns A-C)
            worksheet.View.FreezePanes(2, 4);
          
            rowIndex++;


            
            foreach (var fileEntry in SurveyFiles.Where(f => f.Include))
            {
                await Task.Delay(rowIndex % 10 == 0 ? 10 : 0); // Throttle to avoid UI freeze

                // Open the survey with no auto save
                var survey = new Survey(null!);
                if (await survey.SurveyLoadAsync(fileEntry.FilePath, false/*autoSave*/) == 0)
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

                                // Get the Rules and SpeciesInfo if possible
                                (SurveyRulesCalc? surveyRulesCalc, SpeciesInfo? speciesInfo) = GetRulesAndSpeciesInfo(evt);

                                // Is the Event eligible for the export
                                bool includeRowinExport = IncludeEventInExport(includeFailedRMS, includeOtherFailedRules, includePartialIdentification, 
                                                                surveyRulesCalc,
                                                                speciesInfo);
                                
                                if (includeRowinExport)
                                {
                                    double? measurement = null;
                                    int fishCount = 0;
                                    bool derivedSpecies = false;
                                    bool derivedLength = false;

                                    // Get the speciesInfo, surveyRulesCalc & measurement depending on the event type 
                                    switch (evt.EventDataType)
                                    {
                                        case Events.SurveyDataType.SurveyMeasurementPoints:
                                            worksheet.Cells[rowIndex, (int)ExportExcelColumns.Type].Value = "Measurement";
                                            if (evt.EventData is SurveyMeasurement surveyMeasurement && speciesInfo is not null)
                                            {
                                                measurement = surveyMeasurement.Measurement;
                                                if (!int.TryParse(speciesInfo.Number, out fishCount))
                                                    fishCount = 1;
                                            }
                                            break;

                                        case Events.SurveyDataType.SurveyStereoPoint:
                                        case Events.SurveyDataType.SurveyPoint:
                                            if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                                            {
                                                worksheet.Cells[rowIndex, (int)ExportExcelColumns.Type].Value = "3D";
                                            }
                                            else if (evt.EventData is SurveyPoint surveyPoint)
                                            {
                                                worksheet.Cells[rowIndex, (int)ExportExcelColumns.Type].Value = "Point";
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

                                    // Common data
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.SurveyName].Value = survey.Data.Info.SurveyCode ?? survey.Data.Info.SurveyFileName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.Depth].Value = survey.Data.Info.SurveyDepth;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.Analyst].Value = survey.Data.Info.SurveyAnalystName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.Time].Value = evt.TimeSpanTimelineController;

                                    // Hyperlink column
                                    var encodedPath = Uri.EscapeDataString(fileEntry.FilePath);
                                    var secs = evt.TimeSpanTimelineController.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                                    var cellTimeSecs = worksheet.Cells[rowIndex, (int)ExportExcelColumns.TimeSecs];
                                    cellTimeSecs.Value = $"{evt.TimeSpanTimelineController.TotalSeconds:F2}";
                                    cellTimeSecs.Hyperlink = new ExcelHyperLink($"underwatersurveyor://open?file={encodedPath}&start={secs}");
                                    // Apply the built-in Hyperlink style so it looks like Excel's default (blue underline)
                                    cellTimeSecs.StyleName = "Hyperlink";

                                    // Frame time
                                    var timeValue = evt.TimeSpanTimelineController;
                                    var cell = worksheet.Cells[rowIndex, (int)ExportExcelColumns.Time];
                                    cell.Value = timeValue;
                                    cell.Style.Numberformat.Format = "hh:mm:ss";


                                    // Calculated transect
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.Transect].Value = transectNumber;


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

                                            // Name the Genus+Species scientific full name
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
                                    // we derive missing species and try to derive the species
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
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.FishCount].Value = fishCount;

                                    // Load measurement
                                    if (measurement is not null)
                                    {
                                        cell = worksheet.Cells[rowIndex, (int)ExportExcelColumns.Measurementm];
                                        cell.Value = measurement;
                                        cell.Style.Numberformat.Format = "0.000";

                                        cell = worksheet.Cells[rowIndex, (int)ExportExcelColumns.Measurementmm];
                                        cell.Value = measurement * 1000;
                                        cell.Style.Numberformat.Format = "0";
                                    }
                                    else
                                    {
                                        worksheet.Cells[rowIndex, (int)ExportExcelColumns.Measurementm].Value = "";
                                        worksheet.Cells[rowIndex, (int)ExportExcelColumns.Measurementmm].Value = "";
                                    }

                                    // Range value (mark in red text if rule not passed)
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColumns.Range];
                                    cell.Value = surveyRulesCalc?.Range ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";
                                    if (surveyRulesCalc?.SurveyRuleRange == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // Range Horizontal and vertical offsets and RMS
                                    // Horizontal value (mark in red text if rule not passed)
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColumns.HorizontalOffset];
                                    cell.Value = surveyRulesCalc?.XOffset ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";
                                    if (surveyRulesCalc?.SurveyRuleHoriz == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // Vertical value (mark in red text if rule not passed)
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColumns.VerticalOffset];
                                    cell.Value = surveyRulesCalc?.YOffset ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";
                                    if (surveyRulesCalc?.SurveyRuleVert == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // RMS values (mark RMSWorst in red text if rule not passed)
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColumns.RMS];
                                    cell.Value = surveyRulesCalc?.RMSMean ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";

                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColumns.RMSWorst];
                                    cell.Value = surveyRulesCalc?.RMSWorst ?? 0;
                                    cell.Style.Numberformat.Format = "0.000";
                                    if (surveyRulesCalc?.SurveyRuleRMS == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // Rules passed summary
                                    cell = worksheet.Cells[rowIndex, (int)ExportExcelColumns.RulesPassed];
                                    cell.Value = surveyRulesCalc?.SurveyRules;
                                    if (surveyRulesCalc?.SurveyRules == false)
                                        cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);

                                    // Species, Genus, Family, Code
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.SpeciesScientific].Value = speciesScientificName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.SpeciesCommon].Value = speciesCommonName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.Genus].Value = speciesInfo?.Genus ?? "";
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.GenusSpeciesScientific].Value = genusSpeciesScientific;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.FamilyScientific].Value = familyScientificName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.FamilyCommon].Value = familyCommonName;
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.SpeciesCode].Value = speciesCode;

                                    // Comment (if any)
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.Comment].Value = speciesInfo?.Comment ?? "";

                                    // Check if a species code was actually used or was it plan text or the species code is blank
                                    bool validSpeciesCode = true;
                                    if (speciesInfo is null ||
                                        string.IsNullOrEmpty(speciesInfo.Code) ||
                                        speciesInfo.Species is null ||
                                        speciesInfo.Species.IndexOf('/') == -1)
                                    {
                                        validSpeciesCode = false;
                                    }
                                    worksheet.Cells[rowIndex, (int)ExportExcelColumns.NoSpeciesCode].Value = !validSpeciesCode ? true : "";

                                    // Derived Species and Derived Length flags
                                    if (applyAverageLengths || deriveMissingSpecies)
                                    {
                                        worksheet.Cells[rowIndex, (int)ExportExcelColumns.DerivedSpecies].Value = derivedSpecies ? true : null;
                                        worksheet.Cells[rowIndex, (int)ExportExcelColumns.DerivedLength].Value = derivedLength ? true : null;
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
                ? (int)ExportExcelColumns.DerivedLength
                : (int)ExportExcelColumns.Comment;
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
                report?.Warning("", $"Export Completed, file:{exportFile}, problem lines:{problemCount}, partial export lines:{exportLineCount}");
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
        private void ExportExcelMetadatatSheet(ExcelWorksheet worksheet)
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
        /// Extract the SurveyRules result and the Specif Info result from the 
        /// Event if possible
        /// </summary>
        /// <param name="evt"></param>
        /// <returns></returns>
        private static (SurveyRulesCalc? surveyRulesCalc, SpeciesInfo? speciesInfo) GetRulesAndSpeciesInfo(Event evt)
        {
            SurveyRulesCalc? surveyRulesCalc = null;
            SpeciesInfo? speciesInfo = null;

            // Get the speciesInfo, surveyRulesCalc & measurement depending on the event type 
            switch (evt.EventDataType)
            {
                case Events.SurveyDataType.SurveyMeasurementPoints:
                    if (evt.EventData is SurveyMeasurement surveyMeasurement)
                    {
                        speciesInfo = surveyMeasurement.SpeciesInfo;
                        surveyRulesCalc = surveyMeasurement.SurveyRulesCalc;
                    }
                    break;

                case Events.SurveyDataType.SurveyStereoPoint:
                case Events.SurveyDataType.SurveyPoint:
                    if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                    {
                        speciesInfo = surveyStereoPoint.SpeciesInfo;
                        surveyRulesCalc = surveyStereoPoint.SurveyRulesCalc;
                    }
                    else if (evt.EventData is SurveyPoint surveyPoint)
                    {
                        speciesInfo = surveyPoint.SpeciesInfo;
                    }
                    break;
            }

            return (surveyRulesCalc, speciesInfo);
        }


        /// <summary>
        /// Check to see if this Event is eligible for inclusion in the export
        /// </summary>
        /// <param name="includeFailedRMS"></param>
        /// <param name="includeOtherFailedRules"></param>
        /// <param name="includePartialIdentification"></param>
        /// <param name="surveyRulesCalc"></param>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        private static bool IncludeEventInExport(bool includeFailedRMS, bool includeOtherFailedRules, bool includePartialIdentification,
                                                 SurveyRulesCalc? surveyRulesCalc,
                                                 SpeciesInfo? speciesInfo)
        {
            bool includeRowinExport = true;

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

            return includeRowinExport;
        }


        /// <summary>
        /// Excel Export
        /// </summary>
        /// <returns></returns>
        private async Task ExportCOCOClickAsync()
        {
            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            // Get the default export file name
            List<SurveyFileEntry> SurveyFilesIncluded = [.. SurveyFiles.Where(f => f.Include)];
            string suggestedFileName = $"COCO ExportedSurveys ({SurveyFilesIncluded.Count} surveys) {DateTime.Now:yyyy-MM-dd}";
            if (SurveyFilesIncluded.Count == 1)
                suggestedFileName = Path.GetFileNameWithoutExtension(SurveyFilesIncluded[0].FileName) + "-COCO";

            // Get the export excel file spec
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("COCO JSON", [".json"]);
            savePicker.SuggestedFileName = suggestedFileName;

            StorageFile file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    // Tell Windows we're about to write
                    CachedFileManager.DeferUpdates(file);

                    using var stream = await file.OpenStreamForWriteAsync();

                    // start fresh in case file existed
                    stream.SetLength(0);

                    // Write the fish by fish data
                    await ExportCOCODatatSheetAsync(stream, file.Path);

                    // write to the file
                    await stream.FlushAsync();

                    // Commit the updates
                    var status = await CachedFileManager.CompleteUpdatesAsync(file);
                    if (status != FileUpdateStatus.Complete)
                    {
                        report?.Warning("", $"Export completed with status: {status}");
                    }
                }
                catch (Exception ex)
                {
                    report?.Error("", $"Export failed, {ex.Message}");
                }
            }

            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;

            // Close the dialog
            this.Close();
        }


        /// <summary>
        /// COCO Export
        /// https://roboflow.com/formats/coco-json#format-description
        /// </summary>
        /// <returns></returns>
        private async Task ExportCOCODatatSheetAsync(Stream stream, string exportFile)
        {
            int ret = -1;

            bool includeFailedRMS = IncludeFailedRMS.IsChecked == true;
            bool includeOtherFailedRules = IncludeOtherFailedRules.IsChecked == true;
            // Note this checkbox is not currently visible in COCO mode
            // however I've keep the functionality in case I meet datasets
            // not ID'd to species level in the future
            bool includePartialIdentification = IncludePartialIdentification.IsChecked == true;

            // Image extract
            bool extractRawFrame = ExtractRawFrame.IsChecked == true;
            bool extractCroppedImage = ExtractCroppedImage.IsChecked == true;
            bool markBoxOnFrame = BoxRawFrame.IsChecked == true;
            bool markHeadTailOnFrame = MarkRawFrame.IsChecked == true;

            // Hide DataGrid and show the GridView
            SetDisplayMode(trueDataFalseImages: false);

            // Create list of surveys to include in the export
            List<SurveyFileEntry> surveyFileEntries = SurveyFiles.Where(f => f.Include && !f.IsTotalRow).ToList();

            // This list organizes the Events by frame at the lowest level
            EventsByMediaFileByFrame eventsByMediaFileByFrame = new();

            // Make the output folder and sub-folders
            string exportBasePath = string.Empty;
            if (extractRawFrame || extractCroppedImage || markBoxOnFrame || markHeadTailOnFrame)
                ret = MakeOutputDirectories(out exportBasePath,
                                            rawFolder: true,    // Always need \Raw either in temporary or permanent sense
                                            croppedFolder: extractCroppedImage, 
                                            markupFolder: markBoxOnFrame || markHeadTailOnFrame);

            // Build the EventsByMediaFileByFrame list
            if (ret == 0)
            {
                ret = await BuildEventsByMediaFileByFrameListAsync(includeFailedRMS, includeOtherFailedRules, includePartialIdentification,
                                        surveyFileEntries, eventsByMediaFileByFrame);
            }

            // Extract the sync point frames as a test
            // Good to be able to visually see the stereo media was properly synced
            if (ret == 0) 
            {
                await ExtractSyncFramesAsync(surveyFileEntries, exportBasePath);
            }

            // Extract the frames and markup as necessary
            if (ret == 0)
            {
                // Check image extraction was requested
                if (extractRawFrame || extractCroppedImage || markBoxOnFrame || markHeadTailOnFrame)
                {
                    ret = await ExtractImagesCropAndMarkupAsync(extractRawFrame, extractCroppedImage, markBoxOnFrame, markHeadTailOnFrame,
                                                                eventsByMediaFileByFrame, exportBasePath);
                }
            }

            // Build the COCO export file
            if (ret == 0)
            {
                ret = await BuildCOCOExportFileAsync(eventsByMediaFileByFrame, includePartialIdentification, extractCroppedImage,
                                                     exportBasePath, stream);
            }

            // Remove '\Row' sub-folder if only used for temporary stuff
            if (!extractRawFrame)
            {
                Directory.Delete(exportBasePath + folderRaw);
            }

            // Show DataGrid and hide the GridView
            SetDisplayMode(trueDataFalseImages: true);

        }


        /// <summary>
        /// Inside of the Documents/Surveyor folder create sub-folders for 
        /// the raw frames, cropped frames and markup frames.  
        /// Return the base path to the export folder.
        /// </summary>
        /// <param name="exportBasePath"></param>
        /// <returns></returns>
        private int MakeOutputDirectories(out string exportBasePath, bool rawFolder, bool croppedFolder, bool markupFolder)
        {
            int ret = -1;

            // Reset
            exportBasePath = string.Empty;

            // Create the output directories
            try
            {
                string stem = $"Image Extract {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
                exportBasePath = ImageExtract.MakeAndCreateFramesDirectoryAndEmpty(stem);

                if (rawFolder)
                    ImageExtract.MakeAndCreateFramesDirectoryAndEmpty(stem + folderRaw);

                if (croppedFolder)
                    ImageExtract.MakeAndCreateFramesDirectoryAndEmpty(stem + folderCropped);

                if (markupFolder)
                    ImageExtract.MakeAndCreateFramesDirectoryAndEmpty(stem + folderMarkup);

                ret = 0;
            }
            catch (Exception ex)
            {
                report?.Warning("", $"MakeOutputDirectories: Failed to create output directories for image extraction, {ex.Message}");
                ret = -1;
            }
         
            return ret;
        }

       
        /// <summary>
        /// Organize the data so all the Events for a frame are grouped together
        /// This is ultimately so all the Events for one frame can be annotated 
        /// on the frame image file
        /// </summary>
        /// <param name="surveyFileEntries"></param>
        /// <param name="eventsByMediaFileByFrame"></param>
        /// <returns></returns>
        private static async Task<int> BuildEventsByMediaFileByFrameListAsync(bool includeFailedRMS, bool includeOtherFailedRules, bool includePartialIdentification, 
                                            List<SurveyFileEntry> surveyFileEntries, 
                                            EventsByMediaFileByFrame eventsByMediaFileByFrame)
        {
            int ret = 0;

            // Loop through each survey in the batch
            foreach (var fileEntry in surveyFileEntries)
            {
                // Open the survey with no auto save
                var survey = new Survey(null!);
                if (await survey.SurveyLoadAsync(fileEntry.FilePath, false/*autoSave*/) != 0)
                    continue;

                // Get the frame width and height
                // Get the calibration data
                CalibrationData? calibrationData = survey.Data.Calibration.GetPreferredCalibrationData(null, null);

                if (calibrationData is null)
                    continue;

                // Try to get frame sizes
                int leftWidth = 0, leftHeight = 0, rightWidth = 0, rightHeight = 0;
                (leftWidth, leftHeight) = calibrationData.LeftCameraCalibration.GetFrameSize();
                (rightWidth, rightHeight) = calibrationData.RightCameraCalibration.GetFrameSize();

                if (leftWidth == rightWidth && leftHeight == rightHeight)
                {
                    // Loop through the events for this survey
                    foreach (var evt in survey.Data.Events.EventList)
                    {
                        if (evt.EventDataType == Events.SurveyDataType.SurveyMeasurementPoints ||
                            evt.EventDataType == Events.SurveyDataType.SurveyStereoPoint ||
                            evt.EventDataType == Events.SurveyDataType.SurveyPoint)
                        {
                            // Get the Rules and SpeciesInfo if possible
                            (SurveyRulesCalc? surveyRulesCalc, SpeciesInfo? speciesInfo) = GetRulesAndSpeciesInfo(evt);

                            // Include this Event in the export
                            bool include = IncludeEventInExport(includeFailedRMS, includeOtherFailedRules, includePartialIdentification,
                                                                surveyRulesCalc, speciesInfo);
                            if (include)
                            {
                                string mediaFileSpec;

                                // Here we kind of flatten out the data 
                                switch (evt.EventDataType)
                                {
                                    case Events.SurveyDataType.SurveyMeasurementPoints:
                                    case Events.SurveyDataType.SurveyStereoPoint:
                                        mediaFileSpec = survey.Data.Media.GetMediaFileSpec(trueLeftFalseRight: true, evt.MediaLeftIndex);
                                        if (!string.IsNullOrEmpty(mediaFileSpec))
                                            eventsByMediaFileByFrame.Add(mediaFileSpec, trueLeftFalseRight: true, fileEntry.FilePath, leftWidth, leftHeight, evt.TimeSpanLeftFrame, evt);

                                        mediaFileSpec = survey.Data.Media.GetMediaFileSpec(trueLeftFalseRight: false, evt.MediaRightIndex);
                                        if (!string.IsNullOrEmpty(mediaFileSpec))
                                            eventsByMediaFileByFrame.Add(mediaFileSpec, trueLeftFalseRight: false, fileEntry.FilePath, leftWidth, leftHeight, evt.TimeSpanRightFrame, evt);
                                        break;

                                    case Events.SurveyDataType.SurveyPoint:
                                        if (evt.EventData is SurveyPoint surveyPoint)
                                        {
                                            int mediaIndex = surveyPoint.TrueLeftFalseRight == true ? evt.MediaLeftIndex : evt.MediaRightIndex;
                                            TimeSpan framePosition = surveyPoint.TrueLeftFalseRight ? evt.TimeSpanLeftFrame : evt.TimeSpanRightFrame;

                                            mediaFileSpec = survey.Data.Media.GetMediaFileSpec(surveyPoint.TrueLeftFalseRight, mediaIndex);

                                            if (!string.IsNullOrEmpty(mediaFileSpec))
                                                eventsByMediaFileByFrame.Add(mediaFileSpec, surveyPoint.TrueLeftFalseRight, fileEntry.FilePath, leftWidth, leftHeight, framePosition, evt);
                                        }
                                        break;
                                }
                            }
                        }
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Extract the image one frame before, one frame on the sync frame itself and one frame after.
        /// This is a useful for testing if the sync frame is setup correctly by visually seeing the torch
        /// </summary>
        /// <param name="surveyFileEntries"></param>
        /// <param name="exportBasePath"></param>
        /// <returns></returns>
        private async Task<int> ExtractSyncFramesAsync(List<SurveyFileEntry> surveyFileEntries, string exportBasePath)
        {
            int ret = -1;

            // Loop through each survey in the batch
            foreach (var fileEntry in surveyFileEntries)
            {
                // Open the survey with no auto save
                var survey = new Survey(null!);
                if (await survey.SurveyLoadAsync(fileEntry.FilePath, false/*autoSave*/) != 0)
                    continue;

                // Image extract from video class instances
                List<ImageExtract?> leftImageExtractList = [];
                List<ImageExtract?> rightImageExtractList = [];

                // Open the media file(s)
                ret = await OpenMediaFilesAsync(trueLeftFalseRight: true, leftImageExtractList, survey.Data.Media, exportBasePath, fileEntry.FileName);

                if (ret == 0)
                {
                    ret = await OpenMediaFilesAsync(trueLeftFalseRight: false, rightImageExtractList, survey.Data.Media, exportBasePath, fileEntry.FileName);
                    if (ret == 0)
                    {
                        // Loop through the events for this survey
                        foreach (var evt in survey.Data.Events.EventList)
                        {
                            if (evt.EventDataType == Events.SurveyDataType.StereoSyncPoint)
                            {
                                // Extract the raw frame before, on and after the sync point
                                // to help debug any potential issues with the sync point timing and the frame extraction logic
                                ret = await ExtractSyncPointImagesAsync(leftImageExtractList, evt.MediaLeftIndex, evt.TimeSpanLeftFrame, "left");
                                if (ret == 0)
                                    ret = await ExtractSyncPointImagesAsync(rightImageExtractList, evt.MediaRightIndex, evt.TimeSpanRightFrame, "right");

                                /////////////////////////////////////////////////
                                // Helper to extract images around the sync point
                                static async Task<int>  ExtractSyncPointImagesAsync(List<ImageExtract?> imageExtractList, int mediaIndex, TimeSpan position, string side)
                                {
                                    int ret = -1;

                                    ImageExtract? imageExtract = GetImageExtractInstance(imageExtractList, mediaIndex);
                                    if (imageExtract is not null)
                                    {
                                        // Image extraction
                                        (ret, List<string?> outputFileSpec) = await imageExtract.VideoExtractFramesAsync(position, -1, 1);
                                    }

                                    return ret;
                                }
                                /////////////////////////////////////////////////
                            }
                        }

                        // Close the right media file(s)
                        CloseMediaFiles(rightImageExtractList, fileEntry.FileName);
                    }
                    // Close the left media file(s)
                    CloseMediaFiles(leftImageExtractList, fileEntry.FileName);

                }
            }

            return ret;
        }


        /// <summary>
        /// Using the EventsByMediaFileByFrame instance extract images from
        /// the media files in the list. Depending on the user selection also 
        /// crop the images and markup the images with boxes and head/tail points. 
        /// raw image frame go into the \raw folder, cropped into the \cropped
        /// folder and box and/or marked head and tail into the \markup folder
        /// </summary>
        /// <param name="extractRawFrame"></param>
        /// <param name="extractCroppedImage"></param>
        /// <param name="markBoxOnFrame"></param>
        /// <param name="markHeadTailOnFrame"></param>
        /// <param name="eventsByMediaFileByFrame"></param>
        /// <param name="exportBasePath"></param>
        /// <returns></returns>
        private async Task<int> ExtractImagesCropAndMarkupAsync(bool extractRawFrame, bool extractCroppedImage, 
                                                                bool markBoxOnFrame, bool markHeadTailOnFrame,
                                                                EventsByMediaFileByFrame eventsByMediaFileByFrame, 
                                                                string exportBasePath)
        {
            int ret = -1;
            Survey? survey = null;
            string currentSurveyFilePath = string.Empty;

            string rawFolder = exportBasePath + folderRaw;
            string croppedFolder = exportBasePath + folderCropped;
            string markupFolder = exportBasePath + folderMarkup;

            // Iteration over eventsByMediaFileByFrame.mediaFilesList
            // Each entry in mediaFilesList is a media file (e.g. left or right video)
            // and contains a list of frames (TimeSpan) where there are Events, and
            // the list of Events at each of those frames
            foreach (var pair in eventsByMediaFileByFrame.mediaFilesList)
            {
                string mediaFileSpec = pair.Key;
                MediaFileExtractList mediaFileExtractList = pair.Value;
                string surveyFileName = Path.GetFileName(mediaFileExtractList.surveyFilePath);

                // Open survey if not already open
                if (currentSurveyFilePath != mediaFileExtractList.surveyFilePath)
                {
                    // Close the previous survey if needed
                    if (survey is not null)
                    {
                        await survey.SurveyCloseAsync();
                        survey = null;
                        currentSurveyFilePath = string.Empty;
                    }

                    // Open the new survey with no auto save                    
                    survey = new Survey(null!);
                    if (await survey.SurveyLoadAsync(mediaFileExtractList.surveyFilePath, false/*autoSave*/) != 0)
                    {
                        report?.Error("", $"ExtractImagesCropAndMarkupAsync: Failed to load survey:{mediaFileExtractList.surveyFilePath}");
                        continue;
                    }

                    // Remember the current open survey file path to avoid reopening the
                    // same survey multiple times for media files belonging to the same survey
                    currentSurveyFilePath = mediaFileExtractList.surveyFilePath;
                }

                if (survey is not null && mediaFileExtractList.trueLeftFalseRight is not null)
                {
                    // Clear the GridView of images
                    COCODatasetImages.Clear();

                    // Open the media file
                    ImageExtract imageExtract = new();
                    ret = await imageExtract.VideoOpenAsync(mediaFileExtractList.mediaFileSpec);

                    if (ret == 0)
                    {
                        // Extract the images for this media file
                        foreach (var evtPair in mediaFileExtractList.eventList)
                        {
                            TimeSpan position = evtPair.Key;
                            List<Event> eventsAtThisFrame = evtPair.Value;

                            // Extract the raw frame
                            string rawExportFileSpec = MakeImageFrameFileSpec(mediaFileExtractList.mediaFileSpec, position, rawFolder, "Raw", null, null, null);
                            try
                            {
                                ret = await imageExtract.VideoExtractFrameAsync(position, rawExportFileSpec);
                            }
                            catch (Exception ex)
                            {
                                report?.Warning("", $"Failed to extract raw frame for {surveyFileName}, media file {mediaFileExtractList.mediaFileSpec} at position {position}, {ex.Message}");
                                continue;
                            }

                            if (ret == 0)
                            {
                                // Get the extracted raw frame as a WriteableBitmap for potential cropping and markup
                                WriteableBitmap? wb = imageExtract.GetCurrentWriteableBitmap();

                                // Is Raw frame the image we should display to the user?                            
                                if (wb is not null && IsThisTheDisplayImage("Raw", extractRawFrame, extractCroppedImage, markBoxOnFrame, markHeadTailOnFrame) == true)
                                {
                                    string title = $"{Path.GetFileName(mediaFileExtractList.mediaFileSpec)} - {position}";
                                    await DisplayImageToGridViewAsync(wb, title);
                                }

                                // Get the bounding boxes for the Events at this frame and whether they are overlapping or not
                                List<Rect?> bBoxList = [];
                                List<bool> overlappingList = [];
                                (int frameWidth, int frameHeight) = imageExtract.GetFrameSize();

                                if (extractCroppedImage || markBoxOnFrame || markHeadTailOnFrame)
                                    ret = GenerateBBoxListForThisFrame((bool)mediaFileExtractList.trueLeftFalseRight, frameWidth, frameHeight, eventsAtThisFrame, out bBoxList, out overlappingList);

                                if (ret == 0)
                                {
                                    // Cropped image extraction 
                                    if (ret == 0 && extractCroppedImage && wb is not null)
                                    {
                                        bool displayCropped = IsThisTheDisplayImage("Cropped", extractRawFrame, extractCroppedImage, markBoxOnFrame, markHeadTailOnFrame);

                                        // Crop the image to the bounding boxes and save the cropped image
                                        ret = await CropImageToBoundingBoxesAndSaveAsync(
                                                                              wb,
                                                                              eventsAtThisFrame, bBoxList, overlappingList, 
                                                                              croppedFolder, mediaFileExtractList.mediaFileSpec, 
                                                                              position, surveyFileName, displayCropped);
                                    }
                                

                                    // Marked up image preparation
                                    if (ret == 0 && (markBoxOnFrame || markHeadTailOnFrame) && wb is not null)
                                    {
                                        bool displayMarkedUp = IsThisTheDisplayImage("Markup", extractRawFrame, extractCroppedImage, markBoxOnFrame, markHeadTailOnFrame);

                                        // Mark up the image with boxes and head/tail points and save the marked up image
                                        ret = await MarkupImageAndSaveAsync (wb, (bool)mediaFileExtractList.trueLeftFalseRight, 
                                                                             eventsAtThisFrame, bBoxList, overlappingList, 
                                                                             markBoxOnFrame, markHeadTailOnFrame, 
                                                                             markupFolder, mediaFileExtractList.mediaFileSpec, 
                                                                             position, surveyFileName, displayMarkedUp);
                                    }
                                }

                                // Clean up raw image if necessary
                                if (!extractRawFrame)
                                {
                                    System.IO.File.Delete(rawExportFileSpec);
                                }
                            }
                        }

                        // Close the media file
                        imageExtract.VideoClose();
                    }
                }
            }

            // Close the previous survey
            if (survey is not null)
            {
                await survey.SurveyCloseAsync();
            }

            return ret;
        }


        /// <summary>
        /// Display the image to the GridView for the user to see progress
        /// Note the image is scaled down if necessary
        /// </summary>
        /// <param name="wb"></param>
        /// <param name="title"></param>
        /// <returns></returns>
        private async Task DisplayImageToGridViewAsync(WriteableBitmap wb, string title)
        {
            WriteableBitmap thumbnail;

            if (wb.PixelWidth > thumbnailWidth || wb.PixelHeight > thumbnailHeight)
                thumbnail = await WriteableBitmapHelper.CreateThumbnailAsync(wb, 190, 130);
            else
                thumbnail = wb;

            COCOImageDataObject imageDataObject = new()
            {
                ImageSource = thumbnail,
                Title = title
            };
            COCODatasetImages.Add(imageDataObject);
        }

        /// <summary>
        /// Use to generate the list of bounding boxes for the Events at this frame.  
        /// This is used for both cropping and markup so we only have to loop through 
        /// the Events once to get the bounding boxes.  Also determine if any of the 
        /// bounding boxes are overlapping which is useful information to have when 
        /// deciding how to display the images in the GridView and to indicate that an
        /// image maybe compromised.
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="eventsAtThisFrame"></param>
        /// <param name="bBoxList"></param>
        /// <param name="overlappingList"></param>
        /// <returns></returns>
        private int GenerateBBoxListForThisFrame(bool trueLeftFalseRight, int frameWidth, int frameHeight, List<Event> eventsAtThisFrame, out List<Rect?> bBoxList, out List<bool> overlappingList)
        {
            int ret = 0;
            bBoxList = [];
            overlappingList = [];

            // Build an array of bounding boxes for the Events at this frame so
            // they can be drawn on the cropped image and/or used to determine
            // the cropped image dimensions
            foreach (Event evt in eventsAtThisFrame)
            {
                
                // Get the bounding box for this Event if possible
                if (TryGetBoundingBox(trueLeftFalseRight, evt, frameWidth, frameHeight, out Rect bbox))
                {
                    bBoxList.Add(bbox);
                }
                else
                    bBoxList.Add(null);
            }

            // Check if Rectangles are overlapping
            foreach (Rect? bbox in bBoxList)
            {
                bool overlapping = false;
                foreach (Rect? other in bBoxList)
                {
                    if (bbox is not null && other is not null)
                    {
                        if (bbox != other)
                        {
                            overlapping = bbox.Value.Left < other.Value.Right &&
                                          bbox.Value.Right > other.Value.Left &&
                                          bbox.Value.Top < other.Value.Bottom &&
                                          bbox.Value.Bottom > other.Value.Top;

                            if (overlapping)
                                break;
                        }
                    }
                }
                overlappingList.Add(overlapping);
            }

            return ret;
        }


        /// <summary>
        /// Attempts to retrieve the bounding box associated with the specified event.
        /// </summary>
        /// <param name="trueLeftFalseRight">Indicates whether to consider the left or right media when retrieving the bounding box.</param>
        /// <param name="evt">The event for which to obtain the bounding box. Cannot be null.</param>
        /// <param name="frameWidth">The width of the frame.</param>
        /// <param name="frameHeight">The height of the frame.  </param>
        /// <param name="bbox">When this method returns, contains the bounding box of the event if found; otherwise, contains the default
        /// value for <see cref="Rect"/>.</param>
        /// <returns>true if the bounding box was successfully retrieved; otherwise, false.</returns>
        private bool TryGetBoundingBox(bool trueLeftFalseRight, Event evt, int frameWidth, int frameHeight, out Rect bbox)
        {
            bool ret = false;

            // Reset
            bbox = new Rect(0, 0, 0, 0);

            switch (evt.EventDataType)
            {
                case Events.SurveyDataType.SurveyMeasurementPoints:
                    if (evt.EventData is SurveyMeasurement surveyMeasurement)
                    {
                        (double xA, double yA, double xB, double yB) = SurveyMeasurementGetCoordinates(surveyMeasurement, trueLeftFalseRight);

                        int xSpaceAround = croppingMargin.GetCroppingMarginMeasurement(surveyMeasurement.Measurement,
                                                                                       surveyMeasurement.SpeciesInfo.Species,
                                                                                       surveyMeasurement.SpeciesInfo.Genus,
                                                                                       surveyMeasurement.SpeciesInfo.Family);
                        bbox = MakeCropHeadTail(frameWidth, frameHeight, xA, yA, xB, yB, xSpaceAround);
                        ret = true;
                    }
                    break;

                case Events.SurveyDataType.SurveyStereoPoint:
                    if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                    {
                        (double x, double y) = SurveyStereoPointGetCoordinates(surveyStereoPoint, trueLeftFalseRight);
                        
                        (int xSpace, int ySpace) = croppingMargin.GetCroppingMarginPoint(surveyStereoPoint.SurveyRulesCalc.Range, 
                                                                                         surveyStereoPoint.SpeciesInfo.Species, 
                                                                                         surveyStereoPoint.SpeciesInfo.Genus, 
                                                                                         surveyStereoPoint.SpeciesInfo.Family);
                        bbox = MakeCropPoint(frameWidth, frameHeight, x, y, xSpace, ySpace);
                        ret = true;
                    }
                    break;

                case Events.SurveyDataType.SurveyPoint:
                    if (evt.EventData is SurveyPoint surveyPoint)
                    {
                        if (surveyPoint.TrueLeftFalseRight == trueLeftFalseRight)
                        {
                            (int xSpace, int ySpace) = croppingMargin.GetCroppingMarginPoint(null, surveyPoint.SpeciesInfo.Species, 
                                                                                                   surveyPoint.SpeciesInfo.Genus, 
                                                                                                   surveyPoint.SpeciesInfo.Family);
                            bbox = MakeCropPoint(frameWidth, frameHeight, surveyPoint.X, surveyPoint.Y, xSpace, ySpace);
                            ret = true;
                        }
                    }
                    break;
            }

            return ret;


            // Helper to calculate the bounding box for an SurveyPoint surveyPoint Event
            static Rect MakeCropPoint(int frameWidth, int frameHeight, double x1, double y1, int xSpace, int ySpace)
            {
                // Round
                int x1Rounded = (int)Math.Round(x1, MidpointRounding.AwayFromZero);
                int y1Rounded = (int)Math.Round(y1, MidpointRounding.AwayFromZero);

                // Find top-left coordinates
                int topLeftX = x1Rounded - xSpace;
                int topLeftY = y1Rounded - ySpace;

                // Ensure top-left doesn't exceed boundaries
                if (topLeftX < 0) topLeftX = 0;
                if (topLeftY < 0) topLeftY = 0;

                // Calculate width and height
                int width = 2 * xSpace;
                int height = 2 * ySpace;

                // Ensure the rectangle doesn't exceed the image dimensions
                if ((topLeftX + width) > frameWidth) width = frameWidth - topLeftX;
                if ((topLeftY + height) > frameHeight) height = frameHeight - topLeftY;

                // Return the formatted string
                return new Rect(topLeftX, topLeftY, width, height);
            }

            // Helper to calculate the bounding box for an SurveyMeasurementPoint Event
            static Rect MakeCropHeadTail(int frameWidth, int frameHeight, double hX, double hY, double tX, double tY, double spaceAround)
            {
                const double hwRatio = 0.5;

                // Calculate the angle between head and tail
                double deltaX = tX - hX;
                double deltaY = tY - hY;

                // Angle
                double angle = Math.Atan2(deltaY, deltaX);

                // Calculate the fish length
                double fishLength = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));

                // Calculate the fish height using the ratio
                double fishHeight = fishLength * hwRatio;

                // Calculate half-length and half-height for easier calculations
                double halfLength = fishLength / 2.0;
                double halfHeight = fishHeight / 2.0;

                // Expand by spaceAround on all sides
                halfLength += spaceAround;
                halfHeight += spaceAround;

                // Calculate the corners of the "fish box" in the rotated space
                double cosAngle = Math.Cos(angle);
                double sinAngle = Math.Sin(angle);

                // Center of the fish (midpoint between head and tail)
                double centerX = (hX + tX) / 2.0;
                double centerY = (hY + tY) / 2.0;

                // Calculate the four corners of the fish box
                double corner1X = centerX - (halfLength * cosAngle) - (halfHeight * sinAngle);
                double corner1Y = centerY - (halfLength * sinAngle) + (halfHeight * cosAngle);

                double corner2X = centerX + (halfLength * cosAngle) - (halfHeight * sinAngle);
                double corner2Y = centerY + (halfLength * sinAngle) + (halfHeight * cosAngle);

                double corner3X = centerX + (halfLength * cosAngle) + (halfHeight * sinAngle);
                double corner3Y = centerY + (halfLength * sinAngle) - (halfHeight * cosAngle);

                double corner4X = centerX - (halfLength * cosAngle) + (halfHeight * sinAngle);
                double corner4Y = centerY - (halfLength * sinAngle) - (halfHeight * cosAngle);

                // Determine the crop box that fully encloses the rotated fish box
                double minX = Math.Min(Math.Min(corner1X, corner2X), Math.Min(corner3X, corner4X));
                double maxX = Math.Max(Math.Max(corner1X, corner2X), Math.Max(corner3X, corner4X));
                double minY = Math.Min(Math.Min(corner1Y, corner2Y), Math.Min(corner3Y, corner4Y));
                double maxY = Math.Max(Math.Max(corner1Y, corner2Y), Math.Max(corner3Y, corner4Y));

                // Crop box dimensions
                double cropTopX = Math.Max(0, minX);
                double cropLeftY = Math.Max(0, minY);
                double cropWidth = Math.Min(frameWidth - cropTopX, maxX - minX);
                double cropHeight = Math.Min(frameHeight - cropLeftY, maxY - minY);

                // Guard against negative values
                cropWidth = Math.Max(0, cropWidth);
                cropHeight = Math.Max(0, cropHeight);

                // Ceiling(..., 1) equivalent
                int width = (int)Math.Ceiling(cropWidth);
                int height = (int)Math.Ceiling(cropHeight);
                int topX = (int)Math.Ceiling(cropTopX);
                int leftY = (int)Math.Ceiling(cropLeftY);

                // Return the formatted string
                return new Rect(topX, leftY, width, height);
            }

        }


        /// <summary>
        /// Extract the coordinates from the SurveyMeasurement class
        /// </summary>
        /// <param name="surveyMeasurement"></param>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns></returns>
        private static (double xA, double yA, double xB, double yB) SurveyMeasurementGetCoordinates(SurveyMeasurement surveyMeasurement, bool trueLeftFalseRight)
        {
            double xA;
            double xB;
            double yA;
            double yB;

            if (trueLeftFalseRight == true)
            {
                xA = surveyMeasurement.LeftXA;
                yA = surveyMeasurement.LeftYA;
                xB = surveyMeasurement.LeftXB;
                yB = surveyMeasurement.LeftYB;
            }
            else
            {
                xA = surveyMeasurement.RightXA;
                yA = surveyMeasurement.RightYA;
                xB = surveyMeasurement.RightXB;
                yB = surveyMeasurement.RightYB;
            }

            return (xA, yA, xB, yB);
        }


        /// <summary>
        ///Extract the coordinates from the SurveyStereoPoint class
        /// </summary>
        /// <param name="surveyStereoPoint"></param>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns></returns>
        private static (double x, double y) SurveyStereoPointGetCoordinates(SurveyStereoPoint surveyStereoPoint, bool trueLeftFalseRight)
        {
            double x;
            double y;

            if (trueLeftFalseRight == true)
            {
                x = surveyStereoPoint.LeftX;
                y = surveyStereoPoint.LeftY;
            }
            else
            {
                x = surveyStereoPoint.RightX;
                y = surveyStereoPoint.RightY;
            }
        
            return (x, y);
        }


        /// <summary>
        /// Extract and save the cropped images
        /// </summary>
        /// <param name="wb"></param>
        /// <param name="bBoxList"></param>
        /// <param name="overlappingList"></param>
        /// <param name="croppedFolder"></param>
        /// <param name="mediaFileSpec"></param>
        /// <param name="position"></param>
        /// <param name="surveyFileName"></param>
        /// <param name="displayCropped"></param>
        /// <returns></returns>
        private async Task<int> CropImageToBoundingBoxesAndSaveAsync(
                                                    WriteableBitmap wb,
                                                    List<Event> eventsAtThisFrame, List<Rect?> bBoxList, List<bool> overlappingList, 
                                                    string croppedFolder, string mediaFileSpec, 
                                                    TimeSpan position, string surveyFileName, bool displayCropped)
        {
            int ret = -1;

            // Guard
            if (bBoxList.Count != overlappingList.Count)
                return -1;

            // Loop over the BBox and Overlapping lists
            for (int i = 0; i < bBoxList.Count; i++)
            {
                if (bBoxList[i] is not null)
                {
                    Rect rect = (Rect)bBoxList[i]!;
                    bool overlapping = overlappingList[i];

                    // Get the SpeciesInfo if possible
                    (_, SpeciesInfo? speciesInfo) = GetRulesAndSpeciesInfo(eventsAtThisFrame[i]);
                    string species = ExtractScientificName(speciesInfo?.Species);

                    string croppedExportFileSpec = MakeImageFrameFileSpec(mediaFileSpec, position, croppedFolder, "Crop", rect, species, overlapping);

                    // Extract cropped bitmap rect from wb
                    WriteableBitmap? wbCropped;
                    try
                    {
                        wbCropped = WriteableBitmapHelper.Crop(wb, rect);

                    }
                    catch (Exception ex)
                    {
                        report?.Warning("", $"Failed to extract cropped image for {surveyFileName}, media file {mediaFileSpec} at position {position} box ({rect.X:F1},{rect.Y:F1},W={rect.Width:F1},H={rect.Height:F1}), {ex.Message}");
                        continue;
                    }

                    // Write to file
                    if (wbCropped is not null)
                    {
                        try
                        {
                            await WriteableBitmapHelper.SaveAsync(wbCropped, croppedExportFileSpec);
                            ret = 0;
                        }
                        catch (Exception ex)
                        {
                            report?.Warning("", $"Failed to save cropped image for {surveyFileName}, media file {mediaFileSpec} at position {position} box ({rect.X:F1},{rect.Y:F1},W={rect.Width:F1},H={rect.Height:F1}), {ex.Message}");
                        }
                    }

                    // Display to user if required
                    if (displayCropped)
                    {
                        string overlappingText = string.Empty;
                        if (overlapping)
                            overlappingText = "(overlap)";
                        string title = $"{Path.GetFileName(mediaFileSpec)} - {position} {overlappingText}";
                        await DisplayImageToGridViewAsync(wbCropped, title);
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Markup either the box or head/tail markers or both and save
        /// </summary>
        /// <param name="wb"></param>
        /// <param name="eventsAtThisFrame"></param>
        /// <param name="bBoxList"></param>
        /// <param name="overlappingList"></param>
        /// <param name="markBoxOnFrame"></param>
        /// <param name="markHeadTailOnFrame"></param>
        /// <param name="markupFolder"></param>
        /// <param name="mediaFileSpec"></param>
        /// <param name="position"></param>
        /// <param name="surveyFileName"></param>
        /// <param name="displayMarkedUp"></param>
        /// <returns></returns>
        private async Task<int> MarkupImageAndSaveAsync(WriteableBitmap wb, bool trueLeftFalseRight,
                                                        List<Event> eventsAtThisFrame, List<Rect?> bBoxList, List<bool> overlappingList, 
                                                        bool markBoxOnFrame, bool markHeadTailOnFrame, 
                                                        string markupFolder, string mediaFileSpec, 
                                                        TimeSpan position, string surveyFileName, bool displayMarkedUp)
        {
            int ret = -1;

            if (bBoxList.Count != overlappingList.Count || bBoxList.Count != eventsAtThisFrame.Count)
                return -1;

            // White brush for a non-overlapping box
            Windows.UI.Color nonOverlappingBoxColor = Colors.White;
            // Orange brush for an overlapping box to indicate the image maybe compromised
            Windows.UI.Color overlappingBoxColor = Colors.Orange;
            // Red marker for the Head
            Windows.UI.Color headMarkerColor = Colors.Red;
            // Green marker for the Tail
            Windows.UI.Color tailMarkerColor = Colors.Lime;

            // Convert to CanvasBitmap for markup
            CanvasBitmap canvasBitmap = await ToCanvasBitmapAsync(wb);

            // Create a CanvasRenderTarget to draw on
            CanvasRenderTarget renderTarget = new(CanvasDevice.GetSharedDevice(), 
                                                  canvasBitmap.SizeInPixels.Width, 
                                                  canvasBitmap.SizeInPixels.Height, 
                                                  canvasBitmap.Dpi);

            using (CanvasDrawingSession drawingSession = renderTarget.CreateDrawingSession())
            {
                // Draw the original image onto the render target
                drawingSession.DrawImage(canvasBitmap);

                // Loop over the Events and associated BBoxes and draw the markups
                for (int i = 0; i < eventsAtThisFrame.Count; i++)
                {
                    if (bBoxList[i] == null)
                        continue;

                    Event evt = eventsAtThisFrame[i];
                    Rect rect = (Rect)bBoxList[i]!;
                    bool overlapping = overlappingList[i];

                    // Draw the box if required and if we have a bounding box for this Event
                    if (markBoxOnFrame)
                    {
                        Windows.UI.Color color = overlapping ? overlappingBoxColor : nonOverlappingBoxColor;
                        drawingSession.DrawRectangle(
                                            (float)rect.X, (float)rect.Y,
                                            (float)rect.Width, (float)rect.Height,
                                            color,
                                            1);
                    }

                    // Draw the markers if required 
                    if (markHeadTailOnFrame)
                    {
                        switch (evt.EventDataType)
                        {
                            case Events.SurveyDataType.SurveyMeasurementPoints:
                                if (evt.EventData is SurveyMeasurement surveyMeasurement)
                                {
                                    (double xA, double yA, double xB, double yB) = SurveyMeasurementGetCoordinates(surveyMeasurement, trueLeftFalseRight);

                                    drawingSession.FillCircle((float)xA, (float)yA, 5f, headMarkerColor);
                                    drawingSession.FillCircle((float)xB, (float)yB, 5f, tailMarkerColor);
                                }
                                break;

                            case Events.SurveyDataType.SurveyStereoPoint:
                                if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                                {
                                    (double x, double y) = SurveyStereoPointGetCoordinates(surveyStereoPoint, trueLeftFalseRight);

                                    drawingSession.FillCircle((float)x, (float)y, 5f, headMarkerColor);
                                }
                                break;

                            case Events.SurveyDataType.SurveyPoint:
                                if (evt.EventData is SurveyPoint surveyPoint)
                                {
                                    if (surveyPoint.TrueLeftFalseRight == trueLeftFalseRight)
                                    {
                                        drawingSession.FillCircle((float)surveyPoint.X, (float)surveyPoint.Y, 5f, headMarkerColor);
                                    }
                                }
                                break;
                        }
                    }
                }
            }

            // Convert the render target back to a WriteableBitmap
            // Now copy pixels once and save once
            byte[] pixels = renderTarget.GetPixelBytes();
            using (Stream wbStream = wb.PixelBuffer.AsStream())
            {
                wbStream.Seek(0, SeekOrigin.Begin);
                await wbStream.WriteAsync(pixels, 0, pixels.Length);
                await wbStream.FlushAsync();
            }
            wb.Invalidate();

            string markupExportFileSpec = MakeImageFrameFileSpec(mediaFileSpec, position, markupFolder, "Markup", null, null, null);

            // Write to file
            try
            {
                await WriteableBitmapHelper.SaveAsync(wb, markupExportFileSpec);
                ret = 0;
            }
            catch (Exception ex)
            {
                report?.Warning("", $"Failed to save markup image for {surveyFileName}, media file {mediaFileSpec}, {ex.Message}");
            }


            // Display to user if required
            if (displayMarkedUp)
            {
                string title = $"{Path.GetFileName(mediaFileSpec)} - {position} (marked up)";
                await DisplayImageToGridViewAsync(wb, title);
            }

            return ret;
        }


        /// <summary>
        /// Convert a WriteableBitmap to a CanvasBitmap which is needed for the MarkUpImageAndSave method.
        /// </summary>
        /// <param name="source">The source WriteableBitmap.</param>
        /// <param name="canvasDevice">The CanvasDevice to use. If null, the shared device will be used.</param>
        /// <returns>A CanvasBitmap created from the source WriteableBitmap.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the source bitmap has invalid dimensions or if reading the pixel buffer fails.</exception>
        public static async Task<CanvasBitmap> ToCanvasBitmapAsync(WriteableBitmap source, CanvasDevice? canvasDevice = null)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
                throw new InvalidOperationException("Source bitmap has invalid dimensions.");

            canvasDevice ??= CanvasDevice.GetSharedDevice();

            byte[] pixels;
            using (Stream pixelStream = source.PixelBuffer.AsStream())
            {
                pixelStream.Seek(0, SeekOrigin.Begin);
                pixels = new byte[pixelStream.Length];

                int offset = 0;
                while (offset < pixels.Length)
                {
                    int read = await pixelStream.ReadAsync(pixels, offset, pixels.Length - offset);
                    if (read == 0)
                        break;

                    offset += read;
                }

                if (offset != pixels.Length)
                    throw new InvalidOperationException("Failed to read source pixel buffer.");
            }

            return CanvasBitmap.CreateFromBytes(
                canvasDevice,
                pixels,
                source.PixelWidth,
                source.PixelHeight,
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                96.0f);
        }


        /// <summary>
        /// Write the COCO Export file
        /// </summary>
        /// <param name="frameWidth"></param>
        /// <param name="frameHeight"></param>
        /// <param name="eventsByMediaFileByFrame"></param>
        /// <param name="stream"></param>
        /// <returns></returns>
        private async Task<int> BuildCOCOExportFileAsync(EventsByMediaFileByFrame eventsByMediaFileByFrame, bool includePartialIdentification, bool extractCroppedImage, string exportBasePath, Stream stream)
        {
            int ret = -1;

            string rawFolder = exportBasePath + folderRaw;
            string croppedFolder = exportBasePath + folderCropped;

            // Make the year string for the info section
            // Data year earliest and latest
            int? yearEarliest = null;
            int? yearLatest = null;

            IEnumerable<Event> pointAndMeasurementEvents = eventsByMediaFileByFrame.mediaFilesList.Values
                                .SelectMany(m => m.eventList.Values)          // List<Event> per frame
                                .SelectMany(eventsAtFrame => eventsAtFrame)   // Event
                                .Where(evt =>
                                    evt.EventDataType == Events.SurveyDataType.SurveyMeasurementPoints ||
                                    evt.EventDataType == Events.SurveyDataType.SurveyStereoPoint ||
                                    evt.EventDataType == Events.SurveyDataType.SurveyPoint);

            // Nullable so empty sequence is handled safely
            yearEarliest = pointAndMeasurementEvents.Select(evt => (int?)evt.DateTimeCreate.Year).Min();
            yearLatest = pointAndMeasurementEvents.Select(evt => (int?)evt.DateTimeCreate.Year).Max();

            string yearText = string.Empty;
            if (yearEarliest is not null && yearLatest is not null)
            {
                if (yearEarliest == yearLatest)
                    yearText = $"{yearEarliest}";
                else
                    yearText = $"{yearEarliest}-{yearLatest}";
            }

            // Build the categories for the COCO JSON file according to the specification at https://roboflow.com/formats/coco-json#format-description
            COCOCategory cocoCategory = new(includePartialIdentification);
            // Media file level loop
            foreach (var pair in eventsByMediaFileByFrame.mediaFilesList)
            {
                MediaFileExtractList mediaFileExtractList = pair.Value;

                if (mediaFileExtractList.trueLeftFalseRight is null)
                    continue;

                // Frame level loop
                foreach (var evtPair in mediaFileExtractList.eventList)
                {
                    List<Event> eventsAtThisFrame = evtPair.Value;
                    SpeciesInfo? speciesInfo = null;

                    // Events for a particular frame loop
                    foreach (Event evt in eventsAtThisFrame)
                    {
                        // Get the species info from the event
                        speciesInfo = GetSpeciesInfoFromEvent(evt);
                        
                        if (speciesInfo is not null)
                        {
                            string genus = speciesInfo?.Genus?.Trim() ?? string.Empty;
                            string speciesScientificName = ExtractScientificName(speciesInfo?.Species);
                            string familyScientificName = ExtractScientificName(speciesInfo?.Family);

                            // Add Species/Genus/Family to the category_id code list
                            cocoCategory.Add(familyScientificName, genus, speciesScientificName);
                        }
                    }
                }
            }


            // Iteration over eventsByMediaFileByFrame.mediaFilesList
            // Each entry in mediaFilesList is a media file (e.g. left or right video)
            // and contains a list of frames (TimeSpan) where there are Events, and
            // the list of Events at each of those frames
            // Generate the COCO JSON file according to the specification at https://roboflow.com/formats/coco-json#format-description
            // Use the same BBox generation method as we used to extract the cropped images
            ret = 0;
            int nextImageId = 1;
            int nextAnnotationId = 1;
            int currentImageId = -1;

            var images = new List<object>();
            var annotations = new List<object>();

            foreach (var pair in eventsByMediaFileByFrame.mediaFilesList)
            {
                string mediaFileSpec = pair.Key;
                MediaFileExtractList mediaFileExtractList = pair.Value;

                if (mediaFileExtractList.trueLeftFalseRight is null)
                    continue;


                // Frame level loop
                foreach (var evtPair in mediaFileExtractList.eventList)
                {
                    TimeSpan position = evtPair.Key;
                    List<Event> eventsAtThisFrame = evtPair.Value;
                    string imageFileSpec;

                    // Are we using the raw image?
                    // If so all annotations for this frame will point to this image 
                    if (!extractCroppedImage)
                    {
                        imageFileSpec = MakeImageFrameFileSpec(mediaFileExtractList.mediaFileSpec, position, rawFolder, "Raw", null, null, null);

                        // Add this frame to the images list
                        currentImageId = nextImageId++;
                        images.Add(new
                        {
                            id = currentImageId,
                            file_name = imageFileSpec,
                            width = mediaFileExtractList.frameWidth,
                            height = mediaFileExtractList.frameHeight
                        });
                    }

                    // Get the bounding boxes for the Events at this frame and whether they are overlapping or not
                    List<Rect?> bBoxList = [];
                    List<bool> overlappingList = [];
                        
                    ret = GenerateBBoxListForThisFrame((bool)mediaFileExtractList.trueLeftFalseRight, 
                                                       mediaFileExtractList.frameWidth, mediaFileExtractList.frameHeight, 
                                                       eventsAtThisFrame, out bBoxList, out overlappingList);

                    if (ret == 0)
                    {
                        // Guard
                        if (eventsAtThisFrame.Count == bBoxList.Count && bBoxList.Count == overlappingList.Count)
                        {
                            // Loop over the BBox and Overlapping lists
                            for (int i = 0; i < bBoxList.Count; i++)
                            {
                                if (bBoxList[i] is not null)
                                {

                                    Event evt = eventsAtThisFrame[i];
                                    Rect rect = bBoxList[i]!.Value;
                                    bool overlapping = overlappingList[i];


                                    // Get the species info from the event
                                    SpeciesInfo? speciesInfo = GetSpeciesInfoFromEvent(evt);

                                    if (speciesInfo is null)
                                        continue;

                                    // Are we using the cropped image?
                                    if (extractCroppedImage)
                                    {
                                        string species = ExtractScientificName(speciesInfo.Species);
                                        imageFileSpec = MakeImageFrameFileSpec(mediaFileExtractList.mediaFileSpec, position, croppedFolder, "Cropped", rect, species, overlapping);

                                        // Add this frame to the images list
                                        currentImageId = nextImageId++;
                                        images.Add(new
                                        {
                                            id = currentImageId,
                                            file_name = imageFileSpec,
                                            width = mediaFileExtractList.frameWidth,
                                            height = mediaFileExtractList.frameHeight
                                        });
                                    }

                                    // Get category id from the species
                                    int category_id;
                                    category_id = cocoCategory.GetSpeciesID(ExtractScientificName(speciesInfo.Species));

                                    if (currentImageId < 0)
                                        continue; // defensive: no image id available

                                    // Add the annotation
                                    double[] bbox = [rect.Left, rect.Top, rect.Width, rect.Height];
                                    annotations.Add(new
                                    {
                                        id = nextAnnotationId++,
                                        image_id = currentImageId,
                                        category_id,
                                        bbox,
                                        area = rect.Width * rect.Height,
                                        iscrowd = 0,
                                        segmentation = Array.Empty<object>(),
                                    });
                                }
                            }
                        }
                    }
                }
            }

            // Assemble the COCO structure in memory and then
            // serialize to a file
            if (ret == 0)
            {
                try
                {
                    // Example info section
                    //     "info": {
                    //     "year": "2020",
                    //     "version": "1",
                    //     "description": "Exported from roboflow.ai",
                    //     "contributor": "Surveyor",
                    //     "url": "https://app.roboflow.ai/datasets/hard-hat-sample/1",
                    //     "date_created": "2000-01-01T00:00:00+00:00"
                    //     },
                    // Example images section
                    //    "images": [
                    //        {
                    //          "id": 0,
                    //          "license": 1,
                    //          "file_name": "0001.jpg",
                    //          "height": 275,
                    //          "width": 490,
                    //          "date_captured": "2020-07-20T19:39:26+00:00"
                    //        }
                    //    ],
                    // Example annotations section
                    //    "annotations": [
                    //        {
                    //          "id": 0,
                    //          "image_id": 0,
                    //          "category_id": 2,
                    //          "bbox": [45,2,85,85],
                    //          "area": 7225,
                    //          "segmentation": [],
                    //          "iscrowd": 0
                    //        }
                    //    ],
                    var coco = new
                    {
                        info = new
                        {
                            year = yearText,
                            version = "1.0",
                            description = "Underwater Surveyor COCO export",
                            /*contributor = contributorText,*/
                            date_created = DateTime.UtcNow.ToString("o")
                        },
                        licenses = Array.Empty<object>(),
                        categories = cocoCategory.ToList(),
                        images,
                        annotations
                    };

                    using var streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
                    using var jsonWriter = new JsonTextWriter(streamWriter)
                    {
                        Formatting = Formatting.Indented,
                        CloseOutput = false
                    };

                    var serializer = JsonSerializer.CreateDefault();
                    serializer.Serialize(jsonWriter, coco);

                    await jsonWriter.FlushAsync();
                    await streamWriter.FlushAsync();
                }
                catch (Exception ex)
                {
                    report?.Warning("", $"BuildCOCOExportFileAsync: Error, {ex.Message} ");
                    ret = -1;
                }
            }

            return ret;
        }


        /// <summary>
        /// Get the species info from the event
        /// </summary>
        /// <param name="evt"></param>
        /// <returns></returns>
        private static SpeciesInfo? GetSpeciesInfoFromEvent(Event evt)
        {
            SpeciesInfo? speciesInfo = null;

            switch (evt.EventDataType)
            {
                case Events.SurveyDataType.SurveyMeasurementPoints:
                    {
                        if (evt.EventData is SurveyMeasurement surveyMeasurement)
                        {
                            speciesInfo = surveyMeasurement.SpeciesInfo;
                        }
                    }
                    break;

                case Events.SurveyDataType.SurveyStereoPoint:
                    if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                    {
                        speciesInfo = surveyStereoPoint.SpeciesInfo;
                    }
                    break;

                case Events.SurveyDataType.SurveyPoint:
                    if (evt.EventData is SurveyPoint surveyPoint)
                    {
                        speciesInfo = surveyPoint.SpeciesInfo;
                    }
                    break;
            }

            return speciesInfo;
        }


        /// <summary>
        /// COCO Export
        /// https://roboflow.com/formats/coco-json#format-description
        /// </summary>
        /// <returns></returns>
        //??? TO BE DELETED
//        private async Task ExportCOCODatatSheetAsync2(Stream stream, string exportFile)
//        {
//            bool failed = false;
//            int problemCount = 0;
//            int exportLineCount = 0;

//            bool includeFailedRMS = IncludeFailedRMS.IsChecked == true;
//            bool includeOtherFailedRules = IncludeOtherFailedRules.IsChecked == true;
//            // Note this checkbox is not currently visible in COCO mode
//            // however I've keep the functionality in case I meet datasets
//            // not ID'd to species level in the future
//            bool includePartialIdentification = IncludePartialIdentification.IsChecked == true;

//            // Image extract
//            bool extractRawFrame = ExtractRawFrame.IsChecked == true;
//            bool extractCroppedImage = ExtractCroppedImage.IsChecked == true;
//            bool markBoxOnFrame = BoxRawFrame.IsChecked == true;
//            bool markHeadTailOnFrame = MarkRawFrame.IsChecked == true;


//            int nextImageId = 1;
//            int nextAnnotationId = 1;

//            var images = new List<object>();
//            var annotations = new List<object>();


//            // Hide DataGrid and show the GridView
//            SetDisplayMode(trueDataFalseImages: false);

//            // Setup the species/genus/family dictionaries
//            COCOCategory cocoCategory = new(includePartialIdentification);


//            // Data year earliest and latest
//            int? yearEarliest = null;
//            int? yearLatest = null;

//            // Image extract control list
//            EventsByMediaFileByFrame allMediaFilesExtractList = new();


//            // Loop through each survey in the batch
//            foreach (var fileEntry in SurveyFiles.Where(f => f.Include && !f.IsTotalRow))
//            {
//                // Open the survey with no auto save
//                var survey = new Survey(null!);
//                if (await survey.SurveyLoadAsync(fileEntry.FilePath, false/*autoSave*/) != 0)
//                    continue;

//                // Get the calibration data
//                CalibrationData? calibrationData = survey.Data.Calibration.GetPreferredCalibrationData(null, null);

//                if (calibrationData is null)
//                    continue;

//                // Try to get frame sizes
//                int leftWidth = 0, leftHeight = 0, rightWidth = 0, rightHeight = 0;
//                (leftWidth, leftHeight) = calibrationData.LeftCameraCalibration.GetFrameSize();
//                (rightWidth, rightHeight) = calibrationData.RightCameraCalibration.GetFrameSize();

//                // Reset the transect 
//                string transectNumber = string.Empty;

//                // Image extract from video class instances
//                List<ImageExtract?> leftImageExtractList = [];
//                List<ImageExtract?> rightImageExtractList = [];
//                string baseExportPath = string.Empty;


//                if (extractRawFrame || extractCroppedImage || markHeadTailOnFrame || markHeadTailOnFrame)
//                {
//                    string stem = "";
//                    // Create the output directories
//                    try
//                    {
//                        stem = Path.GetFileNameWithoutExtension(fileEntry.FileName);
//                        baseExportPath = ImageExtract.MakeAndCreateFramesDirectoryAndEmpty(stem);
//                        ImageExtract.MakeAndCreateFramesDirectoryAndEmpty(stem + folderRaw);
//                        ImageExtract.MakeAndCreateFramesDirectoryAndEmpty(stem + folderCropped);
//                        ImageExtract.MakeAndCreateFramesDirectoryAndEmpty(stem + folderMarkup);
//                    }
//                    catch (Exception ex)
//                    {
//                        report?.Warning("", $"From Survey {fileEntry.FileName} failed to create output directories for image extraction, {ex.Message}");
//                        failed = true;
//                    }

//                    // Open the media files
//                    OpenMediaFiles(trueLeftFalseRight: true, leftImageExtractList, survey.Data.Media, stem, fileEntry.FileName);
//                    OpenMediaFiles(trueLeftFalseRight: false, rightImageExtractList, survey.Data.Media, stem, fileEntry.FileName);

//                }

//                // Loop through the events for this survey
//                foreach (var evt in survey.Data.Events.EventList)
//                {
//                    try
//                    {
//                        // Log the transect starts and stops
//                        if (evt.EventDataType == Events.SurveyDataType.SurveyStart)
//                        {
//                            if (evt.EventData is TransectMarker marker)
//                                transectNumber = marker.MarkerName ?? string.Empty;
//                            else
//                                transectNumber = string.Empty;

//                            continue;
//                        }
//                        if (evt.EventDataType == Events.SurveyDataType.SurveyEnd)
//                        {
//                            transectNumber = string.Empty;
//                            continue;
//                        }


//                        // ONLY export SurveyMeasurementPoints
//                        if (evt.EventDataType == Events.SurveyDataType.SurveyMeasurementPoints)
//                        {

//                            if (evt.EventData is not SurveyMeasurement m)
//                                continue;

//                            SpeciesInfo? speciesInfo = m.SpeciesInfo;
//                            SurveyRulesCalc? surveyRulesCalc = m.SurveyRulesCalc;

//                            // Filtering rules
//                            if (!includeFailedRMS && surveyRulesCalc is not null &&
//                                surveyRulesCalc.SurveyRuleRMS.HasValue && surveyRulesCalc.SurveyRuleRMS == false)
//                            {
//                                continue;
//                            }

//                            if (!includeOtherFailedRules && surveyRulesCalc is not null)
//                            {
//                                if ((surveyRulesCalc.SurveyRuleRange.HasValue && surveyRulesCalc.SurveyRuleRange == false) ||
//                                    (surveyRulesCalc.SurveyRuleHoriz.HasValue && surveyRulesCalc.SurveyRuleHoriz == false) ||
//                                    (surveyRulesCalc.SurveyRuleVert.HasValue && surveyRulesCalc.SurveyRuleVert == false))
//                                {
//                                    continue;
//                                }
//                            }

//                            if (!includePartialIdentification &&
//                                (speciesInfo is null || string.IsNullOrWhiteSpace(speciesInfo.Genus)))
//                            {
//                                continue;
//                            }

//                            string genus = speciesInfo?.Genus?.Trim() ?? string.Empty;
//                            string speciesScientificName = ExtractScientificName(speciesInfo?.Species);
//                            string familyScientificName = ExtractScientificName(speciesInfo?.Family);


//                            // Is the event type the min or max year?  If so update
//                            if (yearEarliest is null || yearEarliest > evt.DateTimeCreate.Year)
//                                yearEarliest = evt.DateTimeCreate.Year;
//                            if (yearLatest is null || yearLatest < evt.DateTimeCreate.Year)
//                                yearLatest = evt.DateTimeCreate.Year;


//                            // Build left/right boxes from SurveyMeasurement points
//                            double[] leftBbox = BuildBbox(m.LeftXA, m.LeftYA, m.LeftXB, m.LeftYB);
//                            double[] rightBbox = BuildBbox(m.RightXA, m.RightYA, m.RightXB, m.RightYB);

//                            // Add Species/Genus/Family to the category_id code list
//                            cocoCategory.Add(familyScientificName, genus, speciesScientificName);

//                            // Get the category IDs
//                            int category_idSpecies = cocoCategory.GetSpeciesID(speciesScientificName);
//                            int category_idGenus = cocoCategory.GetGenusID(genus);
//                            int category_idFamily = cocoCategory.GetFamilyID(familyScientificName);


//                            // Detect category id based on available taxonomic information (family/genus/species)
//                            CategoryId? category_id = null;
//                            if (category_idSpecies != -1)
//                            {
//                                // Species level annotation
//                                category_id = (CategoryId?)category_idSpecies;
//                            }
//                            else if (category_idGenus != -1)
//                            {
//                                // Genus level annotation
//                                category_id = (CategoryId?)category_idGenus;
//                            }
//                            else if (category_idFamily != -1)
//                            {
//                                // Family level annotation
//                                category_id = (CategoryId?)category_idFamily;
//                            }

//                            // Create TWO images per measurement event (left + right)
//                            int leftImageId = nextImageId++;
//                            int rightImageId = nextImageId++;
//                            bool ret = false;
//                            string targetImage;
//                            ImageExtract? leftImageExtract = null; ;
//                            ImageExtract? rightImageExtract = null; ;

//                            // Left image extraction
//                            leftImageExtract = GetImageExtractInstance(leftImageExtractList, evt.MediaLeftIndex);
//                            //if (leftImageExtract is not null)
//                            //{
//                            //    allMediaFilesExtractList.Add(leftImageExtract, trueLeftFalseRight: true, fileEntry.FilePath, evt.TimeSpanLeftFrame, evt);
//                            //}
//                            //else
//                            //    targetImage = "missing";

//                            //images.Add(new
//                            //{
//                            //    id = leftImageId,
//                            //    //file_name = $"{leftMediaFile}|t={evt.TimeSpanLeftFrame.TotalSeconds:F3}",
//                            //    file_name = targetImage,
//                            //    width = leftWidth,
//                            //    height = leftHeight
//                            //});

//                            if (extractCroppedImage)
//                            {
//                                // Cropped as top left is (0,0)
//                                leftBbox[0] = 0;
//                                leftBbox[1] = 0;
//                            }

//                            // Left annotation
//                            annotations.Add(new
//                            {
//                                id = nextAnnotationId++,
//                                image_id = leftImageId,
//                                category_id,
//                                bbox = leftBbox,
//                                area = leftBbox[2] * leftBbox[3],
//                                iscrowd = 0,
//                                segmentation = Array.Empty<object>(),
//                                Xfamily = familyScientificName,
//                                XGenus = genus,
//                                XSpecies = speciesScientificName,
//                            });


//                            // Right image extraction
//                            rightImageExtract = GetImageExtractInstance(rightImageExtractList, evt.MediaRightIndex);
//                            //if (rightImageExtract is not null)
//                            //{
//                            //    allMediaFilesExtractList.Add(rightImageExtract, trueLeftFalseRight: false, fileEntry.FilePath, evt.TimeSpanRightFrame, evt);
//                            //}
//                            //else
//                            //    targetImage = "missing";

//                            //images.Add(new
//                            //{
//                            //    id = rightImageId,
//                            //    //file_name = $"{rightMediaFile}|t={evt.TimeSpanRightFrame.TotalSeconds:F3}",
//                            //    file_name = targetImage,
//                            //    width = rightWidth,
//                            //    height = rightHeight
//                            //});

//                            if (extractCroppedImage)
//                            {
//                                // Cropped as top left is (0,0)
//                                rightBbox[0] = 0;
//                                rightBbox[1] = 0;
//                            }

//                            // Right annotation
//                            annotations.Add(new
//                            {
//                                id = nextAnnotationId++,
//                                image_id = rightImageId,
//                                category_id,
//                                bbox = rightBbox,
//                                area = rightBbox[2] * rightBbox[3],
//                                iscrowd = 0,
//                                segmentation = Array.Empty<object>(),
//                                Xfamily = familyScientificName,
//                                XGenus = genus,
//                                XSpecies = speciesScientificName,
//                            });

//                            exportLineCount += 2;
//                        }
//#if DEBUG
//                        else if (evt.EventDataType == Events.SurveyDataType.StereoSyncPoint)
//                        {

//                            // Extract the raw frame before, on and after the sync point
//                            // to help debug any potential issues with the sync point timing and the frame extraction logic
//                            ExtractSyncPointImages(leftImageExtractList, evt.MediaLeftIndex, evt.TimeSpanLeftFrame, "left");
//                            ExtractSyncPointImages(rightImageExtractList, evt.MediaRightIndex, evt.TimeSpanRightFrame, "right");


//                            // Helper to extract images around the sync point
//                            void ExtractSyncPointImages(List<ImageExtract?> imageExtractList, int mediaIndex, TimeSpan position, string side)
//                            {

//                                ImageExtract? imageExtract = GetImageExtractInstance(imageExtractList, mediaIndex);
//                                if (imageExtract is not null)
//                                {
//                                    // Image extraction
//                                    imageExtract.ImagePath = baseExportPath;
//                       //???             (int ret, List<string?> createdtImagesSpecFile) = await imageExtract.VideoExtractFramesAsync(position, extractBefore: -1, extractAfter: 1);
//                                }
//                            }
//                        }
//#endif
//                    }
//                    catch (Exception ex) when (ex is ObjectDisposedException)
//                    {
//                        report?.Error("", $"Export COCO failed, {ex}");
//                        failed = true;
//                        break;
//                    }
//                    catch (Exception ex)
//                    {
//                        report?.Warning("", $"Export COCO warning, {ex}");
//                        problemCount++;
//                    }
//                }

//                // Close the media files
//                if (extractRawFrame || extractCroppedImage || markHeadTailOnFrame || markHeadTailOnFrame)
//                {
//                    CloseMediaFiles(leftImageExtractList, fileEntry.FileName);
//                    CloseMediaFiles(rightImageExtractList, fileEntry.FileName);
//                }

//                if (failed)
//                    break;
//            }

//            if (!failed)
//            {
//                try
//                {
//                    // Make the year string for the info section
//                    string yearText = string.Empty;
//                    if (yearEarliest is not null && yearLatest is not null)
//                    {
//                        if (yearEarliest == yearLatest)
//                            yearText = $"{yearEarliest}";
//                        else
//                            yearText = $"{yearEarliest}-{yearLatest}";
//                    }

//                    // Example info section
//                    //     "info": {
//                    //     "year": "2020",
//                    //     "version": "1",
//                    //     "description": "Exported from roboflow.ai",
//                    //     "contributor": "Surveyor",
//                    //     "url": "https://app.roboflow.ai/datasets/hard-hat-sample/1",
//                    //     "date_created": "2000-01-01T00:00:00+00:00"
//                    //     },
//                    // Example images section
//                    //    "images": [
//                    //        {
//                    //          "id": 0,
//                    //          "license": 1,
//                    //          "file_name": "0001.jpg",
//                    //          "height": 275,
//                    //          "width": 490,
//                    //          "date_captured": "2020-07-20T19:39:26+00:00"
//                    //        }
//                    //    ],
//                    // Example annotations section
//                    //    "annotations": [
//                    //        {
//                    //          "id": 0,
//                    //          "image_id": 0,
//                    //          "category_id": 2,
//                    //          "bbox": [45,2,85,85],
//                    //          "area": 7225,
//                    //          "segmentation": [],
//                    //          "iscrowd": 0
//                    //        }
//                    //    ],
//                    var coco = new
//                    {
//                        info = new
//                        {
//                            year = yearText,
//                            version = "1.0",
//                            description = "Underwater Surveyor COCO export",
//                            /*contributor = contributorText,*/
//                            date_created = DateTime.UtcNow.ToString("o")
//                        },
//                        licenses = Array.Empty<object>(),
//                        categories = cocoCategory.ToList(),
//                        images,
//                        annotations

//                    };

//                    using var streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
//                    using var jsonWriter = new JsonTextWriter(streamWriter)
//                    {
//                        Formatting = Formatting.Indented,
//                        CloseOutput = false
//                    };

//                    var serializer = JsonSerializer.CreateDefault();
//                    serializer.Serialize(jsonWriter, coco);

//                    await jsonWriter.FlushAsync();
//                    await streamWriter.FlushAsync();
//                }
//                catch (Exception ex)
//                {
//                    report?.Warning("", $"ExportCOCODatatSheetAsync: Error, {ex.Message} ");
//                    failed = true;
//                }
//            }

//            if (failed)
//            {
//                report?.Error("", $"Export Failed, file:{exportFile}, partial export lines:{exportLineCount}");
//            }
//            else if (problemCount > 0)
//            {
//                report?.Warning("", $"Export Completed, file:{exportFile}, problem lines:{problemCount}, partial export lines:{exportLineCount}");
//            }
//            else
//            {
//                report?.Info("", $"Export Completed, file:{exportFile}, export lines:{exportLineCount}");
//            }

//            // Show DataGrid and hide the GridView
//            SetDisplayMode(trueDataFalseImages: true);



//            // Create BBox
//            static double[] BuildBbox(double xa, double ya, double xb, double yb)
//            {
//                double x = Math.Min(xa, xb);
//                double y = Math.Min(ya, yb);
//                double w = Math.Abs(xb - xa);
//                double h = Math.Abs(yb - ya);

//                // Keep box valid for COCO
//                if (w <= 0) w = 1;
//                if (h <= 0) h = 1;

//                // Round to nearest int, with midpoint away from zero (0.5 -> 1)
//                x = Math.Round(x, 0, MidpointRounding.AwayFromZero);
//                y = Math.Round(y, 0, MidpointRounding.AwayFromZero);
//                w = Math.Round(w, 0, MidpointRounding.AwayFromZero);
//                h = Math.Round(h, 0, MidpointRounding.AwayFromZero);

//                return [x, y, w, h];
//            }
//        }


        /// <summary>
        /// Extract the scientific name
        /// </summary>
        /// <param name="speciesField"></param>
        /// <returns></returns>
        private static string ExtractScientificName(string? speciesField)
        {
            if (string.IsNullOrWhiteSpace(speciesField))
                return string.Empty;

            int slash = speciesField.IndexOf('/');
            if (slash > 0 && slash < speciesField.Length - 1)
                return speciesField[..slash].Trim();
            if (slash > 0)
                return speciesField[..^1].Trim();

            return speciesField.Trim();
        }


        /// <summary>
        /// Open Image Extract all the media files for the indicated side (left or right)
        /// Normally there is only one media file expect if handling data from
        /// EMObs files
        /// </summary>
        /// <param name="imageExtractList"></param>
        /// <param name="mediaPath"></param>
        /// <param name="mediaFiles"></param>
        /// <param name="surveyFileName"></param>
        /// <param name="side"></param>
        /// <returns></returns>
        private async Task <int> OpenMediaFilesAsync(bool trueLeftFalseRight, List<ImageExtract?> imageExtractList, Survey.DataClass.MediaClass media, string exportBasePath, string surveyFileName)
        {
            int ret = 0;

            // Guard
            if (media.MediaPath is null)
                return -1;

            // Get left or right media file name collection           
            ObservableCollection<string> mediaFileNames = trueLeftFalseRight ? media.LeftMediaFileNames : media.RightMediaFileNames;

            // Loop through and open each media file via the ImageExtract class
            foreach (string mediaFile in mediaFileNames)
            {
                try
                {
                    // Open media file
                    string mediaFileSpec = Path.Combine(media.MediaPath!, mediaFile);
                    ImageExtract imageExtract = new();
                    ret = await imageExtract.VideoOpenAsync(mediaFileSpec);

                    if (ret == 0)
                    {
                        // Set the export path
                        imageExtract.ImagePath = exportBasePath;

                        // Add to the list of media file for this side
                        // (left or right) for this survey (normally only one)
                        imageExtractList.Add(imageExtract);
                    }
                }
                catch (Exception ex)
                {
                    string side = trueLeftFalseRight ? "left" : "right";
                    report?.Warning("", $"From Survey {surveyFileName} failed to open {side} media file {mediaFile} for image extraction, in path {media.MediaPath ?? "(blank)"}, {ex.Message}");
                    imageExtractList.Add(null);
                    ret = -1;
                }
            }
            return ret;
        }


        /// <summary>
        /// Close Image Extract media files
        /// </summary>
        /// <param name="imageExtractList"></param>
        /// <param name="surveyFileName"></param>
        private void CloseMediaFiles(List<ImageExtract?> imageExtractList, string surveyFileName)
        {
            foreach (ImageExtract? imageExtract in imageExtractList)
            {
                try
                {
                    imageExtract?.VideoClose();
                }
                catch (Exception ex)
                {
                    report?.Warning("", $"From Survey {surveyFileName} failed to close right media file {imageExtract?.GetCurrentMediaFileSpec() ?? "(no file spec)"}, {ex.Message}");                   
                }
            }
        }


        /// <summary>
        /// Return the correct ImageExtract instance to use
        /// </summary>
        /// <param name="leftImageExtractList"></param>
        /// <param name="MediaIndex"></param>
        /// <returns></returns>
        private static ImageExtract? GetImageExtractInstance(List<ImageExtract?> imageExtractList, int mediaIndex)
        {
            if (mediaIndex >= 0 && mediaIndex < imageExtractList.Count)
                return imageExtractList[mediaIndex];
            else
                return null;
        }


        /// <summary>
        /// Extract and markup frame from the indicated media file
        /// </summary>
        /// <param name="imageExtract"></param>
        /// <param name="tsFrame"></param>
        /// <param name="bbox"></param>
        /// <param name="extractRawFrame"></param>
        /// <param name="extractCroppedImage"></param>
        /// <param name="markBoxOnFrame"></param>
        /// <param name="markHeadTailOnFrame"></param>
        /// <param name="targetImage"></param>
        /// <returns></returns>
        //???private async Task<(bool ret, string targetImage)> ExtractImagesAsync(ImageExtract imageExtract, TimeSpan tsFrame, double[] bbox,
        //                                                        bool extractRawFrame, bool extractCroppedImage, bool markBoxOnFrame, bool markHeadTailOnFrame)
        //{
        //    bool ret = true;

        //    // Reset
        //    string targetImage = string.Empty;

        //    return (ret, targetImage);
        //}


        /// <summary>
        /// Change the central display area to be either the DataGrid
        /// that display the survey files to export or the GridView
        /// which can display the images being written to display
        /// </summary>
        /// <param name="trueDataFalseImages"></param>
        private void SetDisplayMode(bool trueDataFalseImages)
        {
            if (trueDataFalseImages)
            {
                SurveyGrid.Visibility = Visibility.Visible;
                HeaderSelectAllCheckBox.Visibility = Visibility.Visible;
                ItemCountTextBlock.Visibility = Visibility.Visible;
                ImageGridView.Visibility = Visibility.Collapsed;
            }
            else 
            {
                SurveyGrid.Visibility = Visibility.Collapsed;
                HeaderSelectAllCheckBox.Visibility = Visibility.Collapsed;
                ItemCountTextBlock.Visibility = Visibility.Collapsed;
                ImageGridView.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Make a standard name export file name for the extracted image frame based 
        /// on the media file spec, the time position of the frame, and the 
        /// type/species/overlapping info
        /// </summary>
        /// <param name="mediaFileSpec"></param>
        /// <param name="position"></param>
        /// <param name="rawFolder">sub-folder</param>
        /// <param name="type">Raw or Cropped or Markup</param>
        /// <param name="rect">Crop area</param>
        /// <param name="species">scientific species name</param>
        /// <param name="overlapping">did the box overlap with another box</param>
        /// <returns></returns>
        private static string MakeImageFrameFileSpec(string mediaFileSpec, TimeSpan position, string subFolder, string type, Rect? rect, string? species, bool? overlapping)
        {
            string formattedTime = "0000" + $"{Math.Round(position.TotalSeconds, 2):F2}";
            string typeText = string.Empty;
            string bboxText = string.Empty;
            string speciesText = string.Empty;
            string overlappingText = string.Empty;

            if (!string.IsNullOrWhiteSpace(type))
                typeText = $"_{type}";
            if (rect is not null)
                bboxText = $"_B({rect.Value.Left},{rect.Value.Top},{rect.Value.Width},{rect.Value.Height})";
            if (!string.IsNullOrWhiteSpace(species))
                speciesText = $"_S.{species}";
            if (overlapping.HasValue)
                overlappingText = "_overlap";

                string fileName = Path.GetFileNameWithoutExtension(mediaFileSpec) + $"_P.{formattedTime.Substring(Math.Max(0, formattedTime.Length - 12))}s{typeText}{bboxText}{speciesText}{overlappingText}.png";
            return Path.Combine(subFolder, fileName);
        }


        /// <summary>
        /// This is used to take advantage of the fact that we should use a WritbleBitamp while it is available
        /// if we are only extracting raw frames and not doing any cropping or markup, then we can just use the 
        /// raw frame as the display image in the GridView without having to extract and save a separate image 
        /// file for display. Same is true for cropped images, if we are extracting those then we can use the 
        /// cropped image as the display image without having to reload from file later. And if we are preparing
        /// marked up images than use them as the preferred GridView image
        /// </summary>
        /// <param name="caseToCheck"></param>
        /// <param name="extractRawFrame"></param>
        /// <param name="extractCroppedImage"></param>
        /// <param name="markBoxOnFrame"></param>
        /// <param name="markHeadTailOnFrame"></param>
        /// <returns></returns>
        private static bool IsThisTheDisplayImage(string caseToCheck, bool extractRawFrame, bool extractCroppedImage, bool markBoxOnFrame, bool markHeadTailOnFrame)
        {
            bool ret = false;

            if (caseToCheck == "Raw")
            {
                // Is Raw the only image we will be extracting?
                if (!extractCroppedImage && !markBoxOnFrame && !markHeadTailOnFrame)
                    ret = true;
            }
            else if (caseToCheck == "Cropped")
            {
                // Are any cropped images be extracting?
                if (extractCroppedImage && !markBoxOnFrame && !markHeadTailOnFrame)
                    ret = true;
            }
            else if (caseToCheck == "Markup")
            {
                // Are any marked images be prepared?
                if (markBoxOnFrame || markHeadTailOnFrame)
                    ret = true;
            }

            return ret;
        }

    }

    /// <summary>
    /// Hold a COCO category item
    /// </summary>
    /// <param name="Id"></param>
    /// <param name="Parent"></param>
    public record COCOCategoryItem(int Id, string Parent)
    {
        public static explicit operator COCOCategoryItem(KeyValuePair<string, COCOCategoryItem> v)
        {
            return v.Value;
        }
    }


    /// <summary>
    /// COCO Categories class
    /// Used to compile the category code list and generate a
    /// JSON output
    /// </summary>
    public class COCOCategory
    {
        private bool includePartialIdentification;
        private readonly Dictionary<string, COCOCategoryItem> familyDict = [];
        private readonly Dictionary<string, COCOCategoryItem> genusDict = [];
        private readonly Dictionary<string, COCOCategoryItem> speciesDict = [];
        private int categoryIdNext = 1;


        /// <summary>
        /// If includePartialIdentification is set to true then
        /// include Genus and Family in the COCOCategory list
        /// </summary>
        /// <param name="_includePartialIdentification"></param>
        public COCOCategory(bool _includePartialIdentification)
        {
            includePartialIdentification = _includePartialIdentification;
        }

        /// <summary>
        /// Add to the categories list
        /// </summary>
        /// <param name="family"></param>
        /// <param name="genus"></param>
        /// <param name="species"></param>
        public void Add(string family, string genus, string species)
        {
            if (!string.IsNullOrWhiteSpace(species))
            {
                if (!speciesDict.ContainsKey(species))
                {
                    // Key not found. Add the next species 
                    speciesDict.TryAdd(species, new COCOCategoryItem(categoryIdNext++, genus));
                }
            }

            if (!string.IsNullOrWhiteSpace(genus))
            {
                if (!genusDict.ContainsKey(genus))
                {
                    // Key not found. Add the next genus 
                    genusDict.TryAdd(genus, new COCOCategoryItem(categoryIdNext++, family));
                }
            }

            if (!string.IsNullOrWhiteSpace(family))
            {
                if (!familyDict.ContainsKey(family))
                {
                    // Key not found. Add the next family 
                    familyDict.TryAdd(family, new COCOCategoryItem(categoryIdNext++, "none"));
                }
            }
        }

        public int GetFamilyID(string family)
        {
            if (familyDict.TryGetValue(family, out var item))
                return item.Id;
            return -1;
        }

        public int GetGenusID(string genus)
        {
            if (genusDict.TryGetValue(genus, out var item))
                return item.Id;
            return -1;
        }

        public int GetSpeciesID(string species)
        {
            if (speciesDict.TryGetValue(species, out var item))
                return item.Id;
            return -1;
        }
        

        /// <summary>
        /// Output the categories 
        /// </summary>
        /// <returns></returns>
        public List<object> ToList()
        {
            var categories = new List<object>();

            if (includePartialIdentification)
            {
                // Make the categories
                // Category Family
                foreach (var kvpFamily in familyDict)
                {
                    categories.Add(new
                    {
                        id = ((COCOCategoryItem)kvpFamily).Id,
                        name = kvpFamily.Key,
                        supercategory = "animal",
                    });
                }
                // Category Genus
                foreach (var kvpGenus in genusDict)
                {
                    categories.Add(new
                    {
                        id = ((COCOCategoryItem)kvpGenus).Id,
                        name = kvpGenus.Key,
                        supercategory = "animal",
                    });
                }
            }

            // Category Species
            foreach (var kvpSpecies in speciesDict)
            {
                categories.Add(new
                {
                    id = ((COCOCategoryItem)kvpSpecies).Id,
                    name = kvpSpecies.Key,
                    supercategory = "animal",
                });
            }

            return categories;
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
        public List<string> LeftMediaFiles { get; set; } = [];
        public List<string> RightMediaFiles { get; set; } = [];
        public string SurveyPath { get; set; } = string.Empty;
        public string MediaPath { get; set; } = string.Empty;
        public string AveLengthSummary { get; set; } = string.Empty;

        public string LeftMediaFilesDisplay => string.Join(", ", LeftMediaFiles);
        public string RightMediaFilesDisplay => string.Join(", ", RightMediaFiles);

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
        /// Parses the events and extract all the measurement events.  Those are added
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
        /// <remarks>This method processes the provided events to extract species measurement information from survey
        /// points and associates the resulting data with the specified survey file name. Only events of type <see
        /// cref="SurveyDataType.SurveyPoint"/> or <see cref="SurveyDataType.SurveyStereoPoint"/> are considered. If no
        /// qualifying points are found, the survey file name is removed from the collection to indicate the absence of
        /// relevant data.</remarks>
        public void Add(string surveyFileName, ObservableCollection<Event> events)
        {
            if (string.IsNullOrWhiteSpace(surveyFileName) || events is null) return;

            if (!bySurvey.TryGetValue(surveyFileName, out var set))
            {
                set = [];
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


    public class EventsByMediaFileByFrame
    {
        // Sorted list of MediaFileExtractList where the media file spec is also the key
        public SortedList<string, MediaFileExtractList> mediaFilesList = [];

        /// <summary>
        /// Adds an event to the specified media file at the given position. If the media file already contains events,
        /// the new event is appended; otherwise, a new entry is created for the media file.
        /// </summary>
        /// <remarks>If the specified media file already contains events, the method appends the new event
        /// at the given position. Otherwise, it creates a new entry for the media file and associates the event. This
        /// method does not validate the existence of the media file or survey file on disk.</remarks>
        /// <param name="mediaFileSpec">The unique identifier or file specification for the media file to which the event will be added.</param>
        /// <param name="trueLeftFalseRight">A value indicating whether the event is associated with the left side (<see langword="true"/>) or the right
        /// side (<see langword="false"/>) of the media file.</param>
        /// <param name="surveyFilePath">The file path of the survey associated with the event. Cannot be null or empty.</param>
        /// <param name="surveyFileName">The name of the survey file associated with the event. Cannot be null or empty.</param>
        /// <param name="position">The position within the media file where the event should be added.</param>
        /// <param name="evt">The event to add to the media file. Cannot be null.</param>
        /// <returns>true if the event was added to a new media file entry; false if the event was appended to an existing media
        /// file entry.</returns>
        public bool Add(string mediaFileSpec, 
                        bool trueLeftFalseRight,
                        string surveyFilePath,
                        int frameWidth,
                        int frameHeight,
                        TimeSpan position, 
                        Event evt)
        {
            bool ret = true;

            if (mediaFilesList.TryGetValue(mediaFileSpec, out var mediaFileExtractList) == true)
            {
                // Update and add this Event is an existing list
                mediaFileExtractList.AddEvent(position, evt);
                ret = false;  // Update existing frame position
            }
            else
            {
                // First Event for this media file
                MediaFileExtractList mediaFileExtractListNew = new(trueLeftFalseRight,
                                                                   surveyFilePath,
                                                                   mediaFileSpec,
                                                                   frameWidth,
                                                                   frameHeight);
                mediaFilesList.Add(mediaFileSpec, mediaFileExtractListNew);
            }

            return ret;
        }

        /// <summary>
        /// Clear all values
        /// </summary>
        public void Clear()
        {
            // Parse sort list and call Clear() on each item
            foreach(var pair in mediaFilesList)
            {
                pair.Value.Clear();
            }

            mediaFilesList.Clear();
        }
    }

    /// <summary>
    /// Media level item that is used to indicate which frames need to be exacted
    /// </summary>
    public class MediaFileExtractList
    {
        public bool? trueLeftFalseRight;
        public string surveyFilePath;
        public string mediaFileSpec;
        public int frameWidth;
        public int frameHeight; 

        public readonly SortedList<TimeSpan, List<Event>> eventList = [];

        public MediaFileExtractList(bool _trueLeftFalseRight, 
                                    string _surveyFilePath,                                                                      
                                    string _mediaFileSpec,
                                    int _frameWidth,
                                    int _frameHeight)
        {
            trueLeftFalseRight = _trueLeftFalseRight;
            surveyFilePath = _surveyFilePath;
            mediaFileSpec = _mediaFileSpec;
            frameWidth = _frameWidth;
            frameHeight = _frameHeight;
        }

        /// <summary>
        /// Add an Event to the sorted list. The key to the sorted list
        /// is the position
        /// </summary>
        /// <param name="position">Frame position in the media</param>
        /// <param name="evt">Event to add</param>
        /// <returns>true if new frame</returns>
        public bool AddEvent(TimeSpan position, Event evt)
        {
            bool ret = true;  // default to new frame

            if (eventList.TryGetValue(position, out var eventsExisting) == true)
            {
                // Update and add this Event is an existing list
                eventsExisting.Add(evt);
                ret = false;  // Update existing frame position
            }
            else
            {
                // First Event for this frame position
                List<Event> eventsNew = [];
                eventsNew.Add(evt);
                eventList.Add(position, eventsNew);
            }

            return ret;
        }

        /// <summary>
        /// Clear all values
        /// </summary>
        public void Clear()
        {
            trueLeftFalseRight = null;
            surveyFilePath = string.Empty;           
            mediaFileSpec = string.Empty;
            eventList.Clear();
        }
    }
}
