using GoProMP4MetadataExtraction;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using OfficeOpenXml;
using Surveyor.Events;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Surveyor.User_Controls
{
    public sealed partial class SettingsCalibrationTest : UserControl
    {
        // Reporter
        private Reporter? report = null;

        public double KnownLength { get; set; } = 0.0;

        // Stores the selected survey file full path
        public string SurveyFileSpec { get; private set; } = string.Empty;

        // For ResultText binding
        private string _resultText = string.Empty;
        public string ResultText
        {
            get => _resultText;
            set { _resultText = value; }
        }

        // For IsBusy binding
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; }
        }

        // Used by the file picker
        public Window? ParentWindow { get; set; } = null;

        // Export Excel columns for calibration test
        public enum ExportExcelColmns
        {
            SurveyName = 1,
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
            CalibrationDataIndex,
            PreferredCalibrationDataIndex, // "Y" or blank
            DiffToKnownLength // percentage
        }

        public SettingsCalibrationTest()
        {
            this.InitializeComponent();
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
        /// Sets the hosting window for the current instance.
        /// </summary>
        /// <param name="parentWindow">The parent <see cref="Window"/> to be used as the hosting window. Cannot be null.</param>
        public void SetHostingWindow(Window parentWindow)
        {
            ParentWindow = parentWindow;
        }


        /// <summary>
        /// Find the survey to run the calibration test on
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BrowseSurveyFileButton_Click(object sender, RoutedEventArgs e) => _ = BrowseSurveyFileButtonClickAsync();
        private async Task BrowseSurveyFileButtonClickAsync()
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    ViewMode = PickerViewMode.List
                };
                picker.FileTypeFilter.Add(".survey");

                // WinUI 3: initialize with window handle
                var hwnd = WindowNative.GetWindowHandle(ParentWindow);
                InitializeWithWindow.Initialize(picker, hwnd);

                StorageFile? file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    SurveyFileSpec = file.Path;
                    SurveyFilePathTextBox.Text = SurveyFileSpec;
                }
                else
                {
                    // User canceled; do not clear existing selection
                }
            }
            catch (Exception ex)
            {
                report?.Warning("", $"Browse failed: {ex.Message}");
            }

            UpdateButtonState();
        }


        private void UpdateButtonState()
        {
            var hasFile = !string.IsNullOrWhiteSpace(SurveyFilePathTextBox.Text);
            var hasLength = KnownLength > 0.0;
            RunCalibrationTestButton.IsEnabled = hasFile && hasLength;
        }

        /// <summary>
        /// Control a text box to only allow positive whole numbers
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void KnownLengthNumberTextBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            // Allow only digits (0-9)
            string pattern = @"^\d*$";

            if (!Regex.IsMatch(sender.Text, pattern))
            {
                int caretPosition = sender.SelectionStart - 1;

                // Remove all non-numeric characters
                sender.Text = Regex.Replace(sender.Text, @"\D", "");

                // Restore cursor position
                sender.SelectionStart = Math.Max(caretPosition, 0);
            }

            // update KnownLength backing value if possible
            if (double.TryParse(sender.Text, out double val))
            {
                KnownLength = val;
            }
            else
            {
                KnownLength = 0.0;
            }

            UpdateButtonState();
        }


        /// <summary>
        /// For each measurement event in the selected survey file, calculate using each of the calibration sets the measurement, range, RMS etc.
        /// Export results to Documents folder as an .xlsx named after the survey file stem.
        /// </summary>
        private void RunCalculations_Button(object sender, RoutedEventArgs e) => _ = RunCalculationsButtonAsync();
        private async Task RunCalculationsButtonAsync()
        {
            ResultTextBox.Text = string.Empty;
            ResultTextBox.Visibility = Visibility.Collapsed;
            ResultTextBoxNote1.Visibility = Visibility.Collapsed;
            ResultTextBoxNote2.Visibility = Visibility.Collapsed;

            try
            {
                IsBusy = true;            // swap to spinner + disable button
                SetRunCalculationsButtonState(IsBusy);

                await Task.Run(async () =>
                {
                    // Long-running work here (CPU/IO). Keep UI thread free.
                    await RunCalculationsAsync();
                });

                ResultTextBox.Text = ResultText;
                ResultTextBox.Visibility = Visibility.Visible;
                ResultTextBoxNote1.Visibility = Visibility.Visible;
                ResultTextBoxNote2.Visibility = Visibility.Visible;
            }
            finally
            {
                IsBusy = false;           // restore text + re-enable
                SetRunCalculationsButtonState(IsBusy);
            }
        }

        private async Task RunCalculationsAsync()
        {
            // Reset result text
            ResultText = string.Empty;


            if (string.IsNullOrWhiteSpace(SurveyFileSpec))
            {
                report?.Warning("", "No survey file selected.");
                return;
            }

            Survey? survey = null;

            try
            {
                // EPPlus license
                ExcelPackage.License.SetNonCommercialPersonal("TobySolo");

                // Load the survey
                survey = new Surveyor.Survey(report!);
                int loadRet = await survey.SurveyLoadAsync(SurveyFileSpec, false/*autoSave*/);
                if (loadRet != 0)
                {
                    report?.Warning("", $"Failed to load survey: {SurveyFileSpec}");
                    return;
                }

                // Determine frame size via left media file
                int frameWidth = 0, frameHeight = 0;
                try
                {
                    string mediaFileLeft = survey.GetLeftMediaFileSpec(0);
                    var props = await GetMP4FileProperities.ExtractProperties(mediaFileLeft);
                    if (props.TryGetValue("Video.Width", out string? w) && int.TryParse(w, out int iw)) frameWidth = iw;
                    if (props.TryGetValue("Video.Height", out string? h) && int.TryParse(h, out int ih)) frameHeight = ih;
                }
                catch (Exception ex)
                {
                    report?.Warning("", $"Unable to read media frame size: {ex.Message}");
                }

                // Setup stereo projection
                var stereo = new StereoProjection();
                stereo.SetReporter(report!);
                stereo.SetCalibrationData(survey.Data.Calibration);
                stereo.SetSurveyRules(survey.Data.SurveyRules);
                if (frameWidth > 0 && frameHeight > 0)
                    stereo.SetFrameSize(frameWidth, frameHeight);

                // Prepare Excel package
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string stem = Path.GetFileNameWithoutExtension(SurveyFileSpec);
                string outPath = Path.Combine(docs, stem + ".xlsx");

                using var fs = new FileStream(outPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                using var package = new ExcelPackage();
                var ws = package.Workbook.Worksheets.Add("Data");

                // Headers
                ws.Cells[1, (int)ExportExcelColmns.SurveyName].Value = "Survey Name";
                ws.Cells[1, (int)ExportExcelColmns.Depth].Value = "Depth";
                ws.Cells[1, (int)ExportExcelColmns.Transect].Value = "Transect";
                ws.Cells[1, (int)ExportExcelColmns.Analyst].Value = "Operator";
                ws.Cells[1, (int)ExportExcelColmns.Time].Value = "Position Time";
                ws.Cells[1, (int)ExportExcelColmns.TimeSecs].Value = "Position Secs";
                ws.Cells[1, (int)ExportExcelColmns.Type].Value = "Type";
                ws.Cells[1, (int)ExportExcelColmns.Measurement].Value = "Measurement";
                ws.Cells[1, (int)ExportExcelColmns.Range].Value = "Distance";
                ws.Cells[1, (int)ExportExcelColmns.HorizontalOffset].Value = "Horizontal Offset";
                ws.Cells[1, (int)ExportExcelColmns.VerticalOffset].Value = "Vertical Offset";
                ws.Cells[1, (int)ExportExcelColmns.RMS].Value = "RMS";
                ws.Cells[1, (int)ExportExcelColmns.RulesPassed].Value = "Rules Passed";
                ws.Cells[1, (int)ExportExcelColmns.CalibrationDataIndex].Value = "Calib Index";
                ws.Cells[1, (int)ExportExcelColmns.PreferredCalibrationDataIndex].Value = "Preferred?";
                ws.Cells[1, (int)ExportExcelColmns.DiffToKnownLength].Value = "Diff to Known %";

                int row = 2;
                var calList = survey.Data.Calibration.CalibrationDataList;
                int preferredIdx = survey.Data.Calibration.PreferredCalibrationDataIndex;
                int measurementCount = 0;

                // Cumlative Divergence from the known length for each calibration set
                double[] knownLengthCumulativeDivergenceArray = new double[calList.Count];
                double[] knownLengthCumulativeRMSArray = new double[calList.Count];


                foreach (var evt in survey.Data.Events.EventList)
                {
                    if (evt.EventDataType != Events.SurveyDataType.SurveyMeasurementPoints) continue;
                    if (evt.EventData is not Surveyor.Events.SurveyMeasurement sm) continue;

                    // Load points into stereo
                    if (!stereo.PointsLoad(
                        new Windows.Foundation.Point(sm.LeftXA, sm.LeftYA),
                        new Windows.Foundation.Point(sm.LeftXB, sm.LeftYB),
                        new Windows.Foundation.Point(sm.RightXA, sm.RightYA),
                        new Windows.Foundation.Point(sm.RightXB, sm.RightYB)))
                    {
                        continue;
                    }

                    // Increment the measurement count
                    measurementCount++;

                    // Loop through each calibration set and re-calculate this measurement use one
                    for (int i = 0; i < calList.Count; i++)
                    {
                        var cd = calList[i];
                        if (cd is null) continue;
                        if (frameWidth > 0 && frameHeight > 0 && !cd.FrameSizeCompare(frameWidth, frameHeight))
                            continue;

                        double? measurement = stereo.Measurement(i);

                        // Use SurveyRulesCalc for calculate and rules per calibration index
                        var newRules = new Surveyor.SurveyRulesCalc();
                        newRules.ApplyCalcs(stereo, i);
                        if (survey.Data.SurveyRules.SurveyRulesActive)
                        {
                            newRules.ApplyRules(survey.Data.SurveyRules.SurveyRulesData);
                        }

                        // Diff to known length (measurement meters -> mm)
                        double? diffPct = null;
                        if (measurement.HasValue && KnownLength > 0)
                        {
                            double measMm = measurement.Value * 1000.0;
                            diffPct = ((measMm - KnownLength) / KnownLength) * 100.0;
                        }

                        // Common data per row
                        ws.Cells[row, (int)ExportExcelColmns.SurveyName].Value = survey.Data.Info.SurveyCode ?? survey.Data.Info.SurveyFileName;
                        ws.Cells[row, (int)ExportExcelColmns.Depth].Value = survey.Data.Info.SurveyDepth;
                        string transect = EventsControl.GetTransectMarkerNameForEvent(survey.Data.Events.EventList, evt) ?? string.Empty;
                        ws.Cells[row, (int)ExportExcelColmns.Transect].Value = transect;
                        ws.Cells[row, (int)ExportExcelColmns.Analyst].Value = survey.Data.Info.SurveyAnalystName;

                        var t = evt.TimeSpanTimelineController;
                        var cellTime = ws.Cells[row, (int)ExportExcelColmns.Time];
                        cellTime.Value = t;
                        cellTime.Style.Numberformat.Format = "hh:mm:ss";
                        ws.Cells[row, (int)ExportExcelColmns.TimeSecs].Value = t.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                        ws.Cells[row, (int)ExportExcelColmns.Type].Value = "Measurement";

                        ws.Cells[row, (int)ExportExcelColmns.Measurement].Value = measurement ?? (object)string.Empty;
                        ws.Cells[row, (int)ExportExcelColmns.Range].Value = newRules.Range ?? (object)string.Empty;
                        ws.Cells[row, (int)ExportExcelColmns.HorizontalOffset].Value = newRules.XOffset ?? (object)string.Empty;
                        ws.Cells[row, (int)ExportExcelColmns.VerticalOffset].Value = newRules.YOffset ?? (object)string.Empty;
                        ws.Cells[row, (int)ExportExcelColmns.RMS].Value = newRules.RMSMean ?? (object)string.Empty;
                        ws.Cells[row, (int)ExportExcelColmns.RulesPassed].Value = newRules.SurveyRules?.ToString() ?? string.Empty;
                        ws.Cells[row, (int)ExportExcelColmns.CalibrationDataIndex].Value = i;
                        ws.Cells[row, (int)ExportExcelColmns.PreferredCalibrationDataIndex].Value = (i == preferredIdx) ? "Y" : string.Empty;
                        if (diffPct.HasValue)
                            ws.Cells[row, (int)ExportExcelColmns.DiffToKnownLength].Value = diffPct.Value;

                        if (measurement is not null)
                        {
                            knownLengthCumulativeDivergenceArray[i] += Math.Abs((double)(measurement * 1000) - KnownLength);
                        }
                        if (newRules.RMSMean is not null)
                        {
                            knownLengthCumulativeRMSArray[i] += Math.Abs((double)(newRules.RMSMean * 1000));
                        }

                        row++;
                    }
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                // Save
                package.SaveAs(fs);
                await fs.FlushAsync();

                report?.Info("", $"Export complete: {outPath}");

                // Find the index of knownLengthCumulativeDivergenceArray[] with the smallest value
                int smallestCumulativeDivergenceIndex = knownLengthCumulativeDivergenceArray
                                                                .Select((value, index) => (value, index))
                                                                .Where(t => !double.IsNaN(t.value))
                                                                .MinBy(t => t.value).index;

                // Find the index of knownLengthCumulativeRMSArray[] with the smallest value
                int smallestCumulativeRMSIndex = knownLengthCumulativeRMSArray
                                                                .Select((value, index) => (value, index))
                                                                .Where(t => !double.IsNaN(t.value))
                                                                .MinBy(t => t.value).index;

                // Analyze result
                ResultText = "Cumulative divergence from known length per calibration set:\r\n";
                for (int i = 0; i < calList.Count; i++)
                {
                    // Indicate which calibration set is the preferred one
                    if (i == preferredIdx)
                        ResultText += "*";

                    // Best Known length indicator
                    string bestKnownLenth = string.Empty;
                    if (i == smallestCumulativeDivergenceIndex)
                    {
                        bestKnownLenth = "(best)";
                        ResultText += "+";
                    }

                    // Best RMS indicator
                    string bestRMS = string.Empty;
                    if (i == smallestCumulativeRMSIndex)
                    {
                        bestRMS = "(best)";
                    }

                    // Append to the result text
                    double aveError = knownLengthCumulativeDivergenceArray[i] / measurementCount;
                    double aveErrorRatio = KnownLength > 0 ? aveError / KnownLength : 0.0;
                    double aveRMS = knownLengthCumulativeRMSArray[i] / measurementCount;
                    ResultText += $"Calib set:{i}: Average error:{aveError:F0}mm({aveErrorRatio:P1}){bestKnownLenth} Ave RMS:{aveRMS:F0}{bestRMS}  {calList[i].Description}\r\n";
                }
            }
            catch (Exception ex)
            {
                report?.Error("", $"RunCalculations failed: {ex.Message}");
            }
            finally
            {
                if (survey is not null && survey.IsLoaded)
                {
                    await survey.SurveyCloseAsync();
                }
            }
        }

        private void SetRunCalculationsButtonState(bool isBusy)
        {
            if (isBusy)
            {
                RunCalibrationTestButton.IsEnabled = false;
                RunCalcsButtonText.Visibility = Visibility.Collapsed;
                RunCalcsButtonProgressRing.Visibility = Visibility.Visible;
                RunCalcsButtonProgressRing.IsActive = true;
            }
            else
            {
                RunCalibrationTestButton.IsEnabled = true;
                RunCalcsButtonText.Visibility = Visibility.Visible;
                RunCalcsButtonProgressRing.Visibility = Visibility.Collapsed;
                RunCalcsButtonProgressRing.IsActive = false;
            }
        }
    }
}