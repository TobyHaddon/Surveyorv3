using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Surveyor.DesktopWap.Helper;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.Networking;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage;
using Emgu.CV.CvEnum;



// SurveyorTesting
// This is a user control that allows the user to run internal tests
// 
// Version 1.0  08 Apr 2025
// 

namespace Surveyor.User_Controls
{
    public sealed partial class SurveyorTesting : Window
    {
        // Copy of MainWindow
        private readonly MainWindow? mainWindow = null;

        private Reporter? Report { get; set; } = null;

        // Test controlling variables
        private bool isTestRunning = false;
        private bool isTestAbortRequest = false;
        private Stopwatch stopwatch = new Stopwatch();

        // Counts
        int testFailedCount = 0;
        int testPassCount = 0;
        int totalTestFailedCount = 0;
        int totalTestPassCount = 0;


        /// <summary>
        /// Constructor for the SurveyorTesting user control
        /// </summary>
        /// <param name="_mainWindow"></param>
        /// <param name="_Report"></param>
        public SurveyorTesting(MainWindow _mainWindow, Reporter? _Report)
        {
            // Remember main window (needed for this method)
            mainWindow = _mainWindow;

            // Remember the reporter 
            Report = _Report;

            this.InitializeComponent();
            this.Closed += SettingsWindow_Closed;

            // Remove the separate title bar from the window
            ExtendsContentIntoTitleBar = true;

            // Get the AppWindow associated with this Window
            var appWindow = GetAppWindowForCurrentWindow();
            Debug.WriteLine($"SettingsWindow() Initial Size:({appWindow.ClientSize.Width},{appWindow.ClientSize.Height})");

            // Get the scaling factor for the current display
            double scalingFactor = GetScalingFactorForWindow();
            Debug.WriteLine($"Scaling Factor: {scalingFactor}");

            // Adjust the width and height based on the scaling factor
            int adjustedWidth = (int)(800 * scalingFactor);
            int adjustedHeight = (int)(800 * scalingFactor);

            // Set the size of the window
            appWindow.Resize(new SizeInt32(adjustedWidth, adjustedHeight));
            Debug.WriteLine($"SettingsWindow() Post resize Size:({appWindow.ClientSize.Width},{appWindow.ClientSize.Height})");

            // Center the window on the screen
            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            appWindow.Move(new PointInt32(
                (workArea.Width - adjustedWidth) / 2,
                (workArea.Height - adjustedHeight) / 2
            ));

        }





        ///
        /// EVENTS
        /// 


        /// <summary>
        /// Apply the theme change to the main window when the settings window is closed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SettingsWindow_Closed(object sender, WindowEventArgs e)
        {
            // Pass focus to the main window
            mainWindow?.Activate();
        }


        /// <summary>
        /// Run the simple embedded test using the embedded survey and media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SimpleEmbeddedTest_Click(object sender, RoutedEventArgs e)
        {
            var fileSpecSurvey = new Uri($"ms-appx:///Test Data/Yoga-0m-1-2025-01-24.survey");

            StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(fileSpecSurvey);
            string realFilePath = file.Path;

            await SimpleTest(realFilePath);
        }


        /// <summary>
        /// Run a simple test but prompt the users for a survey file to use
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SimpleProvidedTest_Click(object sender, RoutedEventArgs e)
        {
            if (mainWindow is not null)
            {
                string? fileSpecSurvey = await FilePickerHelper.PickSurveyFileAsync(mainWindow);

                if (fileSpecSurvey is not null)
                {
                    // Check if the file exists
                    if (File.Exists(fileSpecSurvey))
                    {
                        // Run the test
                        await SimpleTest(fileSpecSurvey);
                    }
                    else
                    {
                        string outputText = $"SimpleProvidedTest_Click() File does not exist: {fileSpecSurvey}";
                        Report?.Warning("", outputText);
                        Debug.WriteLine(outputText);
                    }
                }
                else
                {
                    string outputText = $"SimpleProvidedTest_Click() No file selected";
                    Report?.Warning("", outputText);
                    Debug.WriteLine(outputText);
                }
            }
        }


        /// <summary>
        /// Repeatly opens and close an embedded survey 100 times to check the sync offset are applied correctly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SyncEmbeddedTest_Click(object sender, RoutedEventArgs e)
        {
            var fileSpecSurvey = new Uri($"ms-appx:///Test Data/Yoga-0m-1-2025-01-24.survey");

            StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(fileSpecSurvey);
            string realFilePath = file.Path;

            if (int.TryParse(SyncEmbeddedTestRepeat.Text, out int repeat))
            {
                await SyncTest(realFilePath, repeat);
            }

        }


        /// <summary>
        /// Run a long run test using the embedded survey and media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void LongRun1EmbeddedTest_Click(object sender, RoutedEventArgs e)
        {
            var fileSpecSurvey = new Uri($"ms-appx:///Test Data/Yoga-0m-1-2025-01-24.survey");

            StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(fileSpecSurvey);
            string realFilePath = file.Path;

            if (int.TryParse(LongRun1EmbeddedTestRepeat.Text, out int repeat))
            {
                await LongRunTestEngine(realFilePath, longRun1Data, repeat);
            }
        }


        /// <summary>
        /// Run a long run test using the provided survey file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void LongRun1ProvidedTest_Click(object sender, RoutedEventArgs e)
        {
            if (mainWindow is not null)
            {
                string? fileSpecSurvey = await FilePickerHelper.PickSurveyFileAsync(mainWindow);

                if (fileSpecSurvey is not null)
                {
                    // Check if the file exists
                    if (File.Exists(fileSpecSurvey))
                    {
                        if (int.TryParse(LongRun1ProvidedTestRepeat.Text, out int repeat))
                        {
                            // Run the test
                            await LongRunTestEngine(fileSpecSurvey, longRun1Data, repeat);
                        }
                    }
                    else
                    {
                        string outputText = $"LongRun1ProvidedTest_Click() File does not exist: {fileSpecSurvey}";
                        Report?.Warning("", outputText);
                        Debug.WriteLine(outputText);
                    }
                }
                else
                {
                    string outputText = $"LongRun1ProvidedTest_Click() No file selected";
                    Report?.Warning("", outputText);
                    Debug.WriteLine(outputText);
                }
            }
        }




        /// <summary>
        /// Button to abort the test
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TestAbort_Click(object sender, RoutedEventArgs e)
        {
            this.isTestAbortRequest = true;
        }


        ///
        /// PRIVATE
        ///


        /// <summary>
        /// Get the AppWindow associated with the current window
        /// </summary>
        /// <returns></returns>
        private AppWindow GetAppWindowForCurrentWindow()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }


        /// <summary>
        /// Get the scaling factor for the display
        /// </summary>
        /// <returns></returns>
        private double GetScalingFactorForWindow()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this); // Get the native window handle
            uint dpi = GetDpiForWindow(hWnd); // Get DPI for the current window
            return dpi / 96.0; // Calculate scaling factor (96 DPI = 100%)
        }

        [DllImport("User32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);


        /// <summary>
        /// Run a simple test using the provided survey file
        /// Open
        /// Play
        /// Pause
        /// FrameMove +1 (x10)
        /// Play
        /// Pause
        /// Close
        /// </summary>
        /// <param name="fileSpecSurvey"></param>
        /// <returns></returns>
        private async Task<bool> SimpleTest(string fileSpecSurvey)
        {
            bool ret = true;

            InTest(true/*inTest*/);

            if (mainWindow is not null && Report is not null)
            {
                SetProgressBarMax(16);

                /// Open Step 0
                SetProgressBarAndStatus(0, "Survey open");
                int retOpen = await mainWindow.OpenSurvey(fileSpecSurvey);

                if (retOpen == 0)
                {
                    // Open a local copy of the survey because survey member from mainWndow is private
                    Survey survey = new((Reporter)Report);
                    retOpen = await survey.SurveyLoad(fileSpecSurvey);

                    if (retOpen != 0)
                    {
                        string outputText = $"SimpleTest() SurveyLoad({fileSpecSurvey}) failed with error {retOpen}";
                        Report?.Warning("", outputText);
                        Debug.WriteLine(outputText);

                        ret = false;
                    }

                    if (ret == true && AbortRequestCheck(fileSpecSurvey) == true)
                        ret = false;

                    if (ret == true)
                    {
                        if (survey.Data.Sync.IsSynchronized == true)
                        {
                            // Testing a sync'd nmedia set

                            // Play Step 1
                            SetProgressBarAndStatus(1, "Play");
                            mainWindow.mediaStereoController.Play(SurveyorMediaPlayer.eCameraSide.None);

                            if (ret == true && AbortRequestCheck(fileSpecSurvey) == true)
                                ret = false;

                            if (ret == true)
                            {
                                await Task.Delay(2000);

                                // Pause Step 2
                                SetProgressBarAndStatus(2, "Pause");
                                await mainWindow.mediaStereoController.Pause(SurveyorMediaPlayer.eCameraSide.None);
                            }

                            if (ret == true && AbortRequestCheck(fileSpecSurvey) == true)
                                ret = false;

                            if (ret == true)
                            {
                                await Task.Delay(500);

                                // FrameMove +1 (x10) Step 3 - 12
                                for (int i = 0; i < 10 && !AbortRequestCheck(fileSpecSurvey); i++)
                                {
                                    SetProgressBarAndStatus(3 + i, "Frame forward");

                                    await mainWindow.mediaStereoController.FrameMove(SurveyorMediaPlayer.eCameraSide.None, 1);

                                    await Task.Delay(100);
                                }
                            }

                            if (ret == true)
                            {
                                // Play Step 13
                                SetProgressBarAndStatus(13, "Play");
                                mainWindow.mediaStereoController.Play(SurveyorMediaPlayer.eCameraSide.None);
                            }

                            if (ret == true && AbortRequestCheck(fileSpecSurvey) == true)
                                ret = false;

                            if (ret == true)
                            {
                                await Task.Delay(1000);

                                // Pause Step 14
                                SetProgressBarAndStatus(14, "Pause");
                                await mainWindow.mediaStereoController.Pause(SurveyorMediaPlayer.eCameraSide.None);
                            }

                            if (ret == true && AbortRequestCheck(fileSpecSurvey) == true)
                                ret = false;

                            if (ret == true)
                            {
                                await Task.Delay(500);
                            }
                        }
                        else
                        {
                            // Testing an un-sync'd nmedia set

                            // Not implemented
                            SetStatusText("Only designed to work with surveys were the media is locked");
                            await Task.Delay(5000);
                        }
                    }

                    // Close the survey Step 15
                    SetProgressBarAndStatus(15, "Survey close");
                    ret = await mainWindow.CheckForOpenSurveyAndClose();
                }
                else
                {
                    string outputText = $"SimpleTest() OpenSurvey({fileSpecSurvey}) failed with error {retOpen}";
                    Report?.Warning("", outputText);
                    Debug.WriteLine(outputText);
                    ret = false;
                }
            }
            else
            {
                Debug.WriteLine("SimpleTest() MainWindow is null or Report is null");
                ret = false;
            }

            TestCountIncrement(ret/*isPass*/);

            InTest(false/*inTest*/);

            return ret;
        }


        /// <summary>
        /// This test repeatedly opens and closes a survey file
        /// </summary>
        /// <param name="fileSpecSurvey"></param>
        /// <param name="repeat"></param>
        /// <returns></returns>
        private async Task<bool> SyncTest(string fileSpecSurvey, int repeat)
        {
            bool ret = true;
            int retOpen;

            InTest(true/*inTest*/);

            if (mainWindow is not null && Report is not null)
            {
                // Open the survey so we can get the sync offset
                Survey survey = new((Reporter)Report);
                retOpen = await survey.SurveyLoad(fileSpecSurvey);

                if (retOpen != 0)
                {
                    string outputText = $"SimpleTest() SurveyLoad({fileSpecSurvey}) failed with error {retOpen}";
                    Report?.Warning("", outputText);
                    Debug.WriteLine(outputText);

                    return false;
                }


                SetProgressBarMax(repeat);

                for (int i = 0; i < repeat; i++)
                {
                    if (AbortRequestCheck(fileSpecSurvey) == true)
                        return false;

                    /// Open Step i
                    SetProgressBarAndStatus(i, "Survey open");
                    retOpen = await mainWindow.OpenSurvey(fileSpecSurvey);

                    if (retOpen == 0)
                    {
                        await Task.Delay(1000);
                        (TimeSpan? leftOffset, TimeSpan? rightOffset) = mainWindow.GetMediaPlayerPoisitions();

                        if (leftOffset is not null && rightOffset is not null)
                        {
                            TimeSpan actualOffset = (TimeSpan)rightOffset - (TimeSpan)leftOffset;
                            if (actualOffset == survey.Data.Sync.TimeSpanOffset)
                            {
                                TestCountIncrement(true/*isPass*/);
                            }
                            else
                            {
                                TestCountIncrement(false/*isPass*/);

                                string outputText = $"SyncTest() OpenSurvey({fileSpecSurvey}) Required sync offset:{survey.Data.Sync.TimeSpanOffset.TotalSeconds:F2}, Acutal offset:{actualOffset.TotalSeconds:F2}, (Left Position:{((TimeSpan)leftOffset).TotalSeconds:F2}, Right Position:{((TimeSpan)rightOffset).TotalSeconds:F2}";
                                Report?.Warning("", outputText);
                                Debug.WriteLine(outputText);
                            }
                        }

                        // Close the survey Step i
                        SetProgressBarAndStatus(i, "Survey close");
                        await mainWindow.CheckForOpenSurveyAndClose();
                    }
                }
            }
            else
            {
                Debug.WriteLine("SimpleTest() MainWindow is null or Report is null");
                ret = false;
            }

            InTest(false/*inTest*/);

            return ret;
        }


        public enum MAction { None, Play, Pause, FrameMove, FrameJump, StepForward, StepBackward, Wait };
        public class DItem(SurveyorTesting.MAction action,
            int? frameIndex = null,
            TimeSpan? framePosition = null,
            TimeSpan? duration = null)
        {
            public MAction Action { get; set; } = action;

            // FrameIndex & FramePosition used for FrameMove (relative), FrameJump (absolute),
            // and optionally for Play (null means Play from current position)
            public int? FrameIndex { get; set; } = frameIndex;
            public TimeSpan? FramePosition { get; set; } = framePosition;

            // TimeSpan used for Play, Pause & Wait
            public TimeSpan? Duration { get; set; } = duration;
        }

        private List<DItem> longRun1Data = [
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.Play, null, null, TimeSpan.FromSeconds(5)),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.FrameMove, -1, null, null),
                            new(MAction.Wait, null, null, TimeSpan.FromSeconds(5)),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.StepForward, null, null, null),
                            new(MAction.StepForward, null, null, null),
                            new(MAction.StepForward, null, null, null),
                            new(MAction.Play, null, null, TimeSpan.FromSeconds(5)),
                            new(MAction.FrameJump, null, TimeSpan.FromSeconds(12), null),
                            new(MAction.FrameJump, null, TimeSpan.FromSeconds(13), null),
                            new(MAction.FrameJump, null, TimeSpan.FromSeconds(14), null),
                            new(MAction.Play, null, null, TimeSpan.FromSeconds(5)),
                            new(MAction.Pause, null, null, null),
                            new(MAction.Wait, null, null, TimeSpan.FromSeconds(2)),
                            new(MAction.StepForward, null, null, null),
                            new(MAction.StepForward, null, null, null),
                            new(MAction.StepForward, null, null, null),
                            new(MAction.FrameJump, null, TimeSpan.FromSeconds(5), null),
                            new(MAction.FrameJump, null, TimeSpan.FromSeconds(7), null),
                            new(MAction.FrameJump, null, TimeSpan.FromSeconds(9), null),
                            new(MAction.FrameJump, null, TimeSpan.FromSeconds(15), null),
                            new(MAction.FrameJump, null, TimeSpan.FromSeconds(17), null),
                            new(MAction.FrameJump, null, TimeSpan.FromSeconds(19), null),
                            new(MAction.Play, null, null, TimeSpan.FromSeconds(5)),
                            new(MAction.Pause, null, null, null),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.FrameMove, 1, null, null),
                            new(MAction.FrameMove, -1, null, null),
                            new(MAction.FrameMove, -1, null, null),
                            new(MAction.FrameMove, -1, null, null),
                            new(MAction.FrameMove, -1, null, null),
                            new(MAction.FrameMove, -1, null, null)
                            ];

        private async Task<bool> LongRunTestEngine(string fileSpecSurvey, List<DItem> runData, int repeat)
        {
            bool ret = true;
            int retOpen;

            InTest(true/*inTest*/);

            if (mainWindow is not null && Report is not null)
            {
                // Open the survey so we can get the sync offset
                Survey survey = new((Reporter)Report);
                retOpen = await survey.SurveyLoad(fileSpecSurvey);

                if (retOpen != 0)
                {
                    string outputText = $"SimpleTest() SurveyLoad({fileSpecSurvey}) failed with error {retOpen}";
                    Report?.Warning("", outputText);
                    Debug.WriteLine(outputText);

                    ret = false;
                }

                if (ret == true)
                {
                    SetProgressBarMax((runData.Count * repeat) + 2); // Add in the Open and Close

                    /// Open Step 0
                    int stepIndex = 0;
                    SetProgressBarAndStatus(stepIndex++, "Survey open");
                    retOpen = await mainWindow.OpenSurvey(fileSpecSurvey);

                    if (retOpen == 0)
                    {
                        for (int repeats = 0; repeats < repeat && !AbortRequestCheck(fileSpecSurvey); repeats++)
                        {
                            for (int i = 0; i < runData.Count && !AbortRequestCheck(fileSpecSurvey); i++)
                            {
                                DItem item = runData[i];

                                if (!AbortRequestCheck(fileSpecSurvey) == true)
                                {

                                    TimeSpan duration = item.Duration ?? TimeSpan.Zero;
                                    int frameIndex = item.FrameIndex ?? 0;
                                    TimeSpan framePosition = item.FramePosition ?? TimeSpan.Zero;

                                    switch (item.Action)
                                    {
                                        case MAction.Play:
                                            if (item.Duration is not null)
                                                SetProgressBarAndStatus(stepIndex++, $"{repeats + 1}/{i + 1}:Play for {duration.TotalSeconds:F1}secs");
                                            else
                                                SetProgressBarAndStatus(stepIndex++, $"{repeats + 1}/{i + 1}:Play");

                                            mainWindow.mediaStereoController.Play(SurveyorMediaPlayer.eCameraSide.None);

                                            if (item.Duration is not null)
                                                await Task.Delay(duration.Milliseconds);
                                            break;

                                        case MAction.Pause:
                                            SetProgressBarAndStatus(stepIndex++, $"{repeats + 1}/{i + 1}:Pause");
                                            await mainWindow.mediaStereoController.Pause(SurveyorMediaPlayer.eCameraSide.None);
                                            break;

                                        case MAction.FrameMove:
                                            if (frameIndex != 0)
                                            {
                                                SetProgressBarAndStatus(stepIndex++, $"{repeats + 1}/{i + 1}:Frame move {frameIndex} frames");
                                                await mainWindow.mediaStereoController.FrameMove(SurveyorMediaPlayer.eCameraSide.None, frameIndex);
                                            }
                                            else
                                            {
                                                SetProgressBarAndStatus(stepIndex++, $"{repeats + 1}/{i + 1}:Frame move relative {framePosition:hh\\:mm\\:ss\\.ff}");
                                                await mainWindow.mediaStereoController.FrameMove(SurveyorMediaPlayer.eCameraSide.None, framePosition);
                                            }
                                            break;

                                        case MAction.FrameJump:
                                            SetProgressBarAndStatus(stepIndex++, $"{repeats + 1}/{i + 1}:Frame jump to position {framePosition:hh\\:mm\\:ss\\.ff}");
                                            mainWindow.mediaStereoController.FrameJump(SurveyorMediaPlayer.eCameraSide.None, framePosition);
                                            break;

                                        case MAction.StepForward:
                                            SetProgressBarAndStatus(stepIndex++, $"{repeats + 1}/{i + 1}:Step forward 30 frames"); // 30 frames
                                            await mainWindow.mediaStereoController.FrameMove(SurveyorMediaPlayer.eCameraSide.None, 30);
                                            break;

                                        case MAction.StepBackward:
                                            SetProgressBarAndStatus(stepIndex++, $"{repeats + 1}/{i + 1}:Step backward 10 frames");  // 10 frames
                                            await mainWindow.mediaStereoController.FrameMove(SurveyorMediaPlayer.eCameraSide.None, -10);
                                            break;

                                        case MAction.Wait:
                                            if (item.Duration is null)
                                                duration = TimeSpan.FromSeconds(1);
                                            SetProgressBarAndStatus(stepIndex++, $"{repeats + 1}/{i + 1}:Wait {duration.TotalSeconds:F1}");
                                            await Task.Delay(duration.Milliseconds);
                                            break;

                                        default:
                                            Debug.WriteLine($"LongRunTestEngine() Unknown action {item.Action}");
                                            break;
                                    }
                                }

                                await Task.Delay(5);
                            }
                        }

                        // Close the survey Step n + 1
                        SetProgressBarAndStatus(stepIndex++, "Survey close");
                        await mainWindow.CheckForOpenSurveyAndClose();
                    }
                }
            }
            else
            {
                Debug.WriteLine("SimpleTest() MainWindow is null or Report is null");
                ret = false;
            }

            InTest(false/*inTest*/);

            return ret;
        }


        /// <summary>
        /// Called to test is if the user requested the test be aborted
        /// </summary>
        /// <param name="testName"></param>
        /// <param name="fileSpecSurvey"></param>
        /// <returns></returns>
        private bool AbortRequestCheck(string fileSpecSurvey, [CallerMemberName] string? caller = null)
        {
            // Check if the test is running
            if (isTestAbortRequest)
            {
                string outputText = $"{caller}() OpenSurvey({fileSpecSurvey}) aborting test...";
                Report?.Warning("", outputText);
                Debug.WriteLine(outputText);

                isTestAbortRequest = false;
                return true;
            }
            else
            {
                return false;
            }
        }


        /// <summary>
        /// Called to move the user control into or out of test mode
        /// </summary>
        /// <param name="inTest"></param>
        private void InTest(bool inTest)
        {
            if (inTest)
            {
                // Reset the test counts (but not the totals)
                TestCountReset();

                isTestRunning = true;
                isTestAbortRequest = false;

                // In test
                TestingProgressBar.Value = 0;
                TestingProgressBar.Visibility = Visibility.Visible;
                TestAbort.Visibility = Visibility.Visible;

                // Disable tests
                SimpleEmbeddedTest.IsEnabled = false;
                SimpleProvidedTest.IsEnabled = false;
                SyncEmbeddedTest.IsEnabled = false;
                LongRun1EmbeddedTest.IsEnabled = false;
                LongRun1ProvidedTest.IsEnabled = false;

                TestStatus.Text = "";

                stopwatch.Start();
            }
            else
            {
                stopwatch.Stop();

                isTestRunning = false;
                isTestAbortRequest = false;

                // Out of test
                TestingProgressBar.Visibility = Visibility.Collapsed;
                TestAbort.Visibility = Visibility.Collapsed;

                // Enable tests
                SimpleEmbeddedTest.IsEnabled = true;
                SimpleProvidedTest.IsEnabled = true;
                SyncEmbeddedTest.IsEnabled = true;
                LongRun1EmbeddedTest.IsEnabled = true;
                LongRun1ProvidedTest.IsEnabled = true;

                // Make show the progress bar stopped (it maybe 'sIndeterminate')
                TestingProgressBar.IsIndeterminate = false;

                TestStatus.Text = "";
            }
        }


        /// <summary>
        /// Set the progress bar to the maximum value
        /// </summary>
        /// <param name="max"></param>
        private void SetProgressBarMax(int max)
        {
            if (max > -1)
            {
                TestingProgressBar.IsIndeterminate = false;
                TestingProgressBar.Maximum = max;                
            }
            else
                TestingProgressBar.IsIndeterminate = true;

        }


        /// <summary>
        /// Set the progress bar to the value
        /// </summary>
        /// <param name="value"></param>
        private void SetProgressBarValue(int value)
        {
            TestingProgressBar.Value = value;
        }


        /// <summary>
        /// Set the status text
        /// </summary>
        /// <param name="text"></param>
        private void SetStatusText(string text)
        {
            double elapsedSecs = stopwatch.ElapsedMilliseconds / 1000.0;

            string statusText = $"{elapsedSecs:F1}secs  {text}";

            TestStatus.Text = statusText;
        }


        /// <summary>
        /// Set the progress bar and the test
        /// </summary>
        /// <param name="value"></param>
        /// <param name="text"></param>
        private void SetProgressBarAndStatus(int value, string text)
        {
            SetProgressBarValue(value);
            SetStatusText(text);
        }


        /// <summary>
        /// Used to increment the test pass/fail counts
        /// </summary>
        /// <param name="isPass"></param>
        private void TestCountIncrement(bool isPass)
        {
            if (isPass)
            {
                testPassCount++;
                totalTestPassCount++;
            }
            else
            {
                testFailedCount++;
                totalTestFailedCount++;
            }

            // Update the UI
            TestPassedCount.Text = testPassCount.ToString();
            TestFailedCount.Text = testFailedCount.ToString();
        }

        /// <summary>
        /// Reset the test count (but not the totals)
        /// </summary>
        private void TestCountReset()
        {
            testPassCount = 0;
            testFailedCount = 0;
            // Update the UI
            TestPassedCount.Text = testPassCount.ToString();
            TestFailedCount.Text = testFailedCount.ToString();
        }

    }

    /// <summary>
    /// Helper class to pick a survey file
    /// </summary>
    public static class FilePickerHelper
    {
        public static async Task<string?> PickSurveyFileAsync(Window window)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add(".survey");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.ViewMode = PickerViewMode.List;
            picker.CommitButtonText = "Select";

            StorageFile file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
    }
}
