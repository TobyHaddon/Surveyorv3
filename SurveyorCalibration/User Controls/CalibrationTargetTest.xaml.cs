using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Foundation;
using Windows.Foundation.Collections;
using static Emgu.CV.Aruco.Dictionary;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor.User_Controls;

public sealed partial class CalibrationTargetTest : UserControl
{
    private VideoCapture? _capture;
    private DispatcherTimer? _timer = null;
    private WriteableBitmap? _wb;
    private CharucoBoard? _board;
    private Dictionary? _dictionary;
    private CancellationTokenSource? _cts;
    private int maxCornersDetected = 0;
    private int maxMarkersDetected = 0;
    private CalibrationBoardDefinition boardDefinition = new();

    public CalibrationTargetTest()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;        
    }


    /// <summary>
    /// Reset the test state.
    /// </summary>
    public void Reset()
    {
        maxCornersDetected = 0;
        maxMarkersDetected = 0;
        UpdateUI(false);
    }


    /// 
    /// EVENTS
    /// 
    
    public void Start()
    {
        TrySetupBoardFromSettings();
        StartCamera();
    }

    public void Stop()
    {
        StopCamera();
    }

    // Optional: avoid immediate start/stop from load/unload when expander collapses its content
    private void OnLoaded(object sender, RoutedEventArgs e) { /* intentionally left empty */ }
    private void OnUnloaded(object sender, RoutedEventArgs e) { /* intentionally left empty */ }


    /// 
    /// PRIVATE
    /// 

    private void TrySetupBoardFromSettings()
    {
        _dictionary = null;
        _board = null;

        try
        {
            boardDefinition.Clear();

            // SettingsManagerLocal provides default board parameters
            int squaresX = SettingsManagerLocal.DefaultChArUcoBoard_SquaresX;
            int squaresY = SettingsManagerLocal.DefaultChArUcoBoard_SquaresY;
            float squareLength = (float)SettingsManagerLocal.DefaultChArUcoBoard_SquareLength; // meters
            float markerLength = (float)SettingsManagerLocal.DefaultChArUcoBoard_MarkerLength; // meters

            boardDefinition.Target = CalibrationBoardDefinition.TargetType.ChArUco;
            boardDefinition.SquareLength = squareLength;
            boardDefinition.MarkerLength = markerLength;
            boardDefinition.SquaresX = squaresX;
            boardDefinition.SquaresY = squaresY;


            // Dictionary name mapping (basic)
            string dictName = SettingsManagerLocal.DefaultChArUcoBoard_PredefinedDictionaryName;

            if (Enum.TryParse(dictName, ignoreCase: true, out PredefinedDictionaryName dictEnum))
            {
                boardDefinition.PredefinedDictionaryName = dictEnum;
                _dictionary = new Dictionary(dictEnum);
                _board = new CharucoBoard(squaresX, squaresY, squareLength, markerLength, _dictionary);
            }

            // Set the calibration board description
            BoardDescription.Text = boardDefinition.Description();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalibrationTargetTest: Failed to setup board: {ex.Message}");
        }
    }

    private void StartCamera()
    {
        try
        {
            _capture = new VideoCapture(0, VideoCapture.API.DShow);
            if (!_capture.IsOpened)
            {
                Debug.WriteLine("CalibrationTargetTest: Camera not opened.");
                return;
            }

            int width = (int)_capture.Get(CapProp.FrameWidth);
            int height = (int)_capture.Get(CapProp.FrameHeight);
            _wb = new WriteableBitmap(width, height);
            CameraImage.Source = _wb;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += OnTick;
            _timer.Start();

            Debug.WriteLine($"CalibrationTargetTest: Camera opened ({width}x{height}), timer started.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalibrationTargetTest: Failed to start camera: {ex.Message}");
        }
    }

    private void StopCamera()
    {
        try
        {
            _timer?.Stop();
            _timer = null;
            _capture?.Dispose();
            _capture = null;
            _cts?.Cancel();
            _cts = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalibrationTargetTest: Error stopping camera: {ex.Message}");
        }
    }

    private void OnTick(object? sender, object e)
    {
        if (_capture is null || _wb is null) return;
        using var frame = new Mat();
        if (!_capture.Read(frame) || frame.IsEmpty) return;

        bool found = false;
        try
        {
            if (_board != null && _dictionary != null)
            {
                using var gray = new Mat();
                CvInvoke.CvtColor(frame, gray, ColorConversion.Bgr2Gray);

                using var corners = new Emgu.CV.Util.VectorOfVectorOfPointF();
                using var ids = new Emgu.CV.Util.VectorOfInt();
                using var rejected = new Emgu.CV.Util.VectorOfVectorOfPointF();
                var detectorParams = DetectorParameters.GetDefault();
                ArucoInvoke.DetectMarkers(gray, _dictionary, corners, ids, detectorParams, rejected);

                if (ids.Size > 0)
                {
                    // Remember max corners/markers detected
                    if (ids.Size > maxMarkersDetected)
                        maxMarkersDetected = ids.Size;
                    if (corners.Size > maxCornersDetected)
                        maxCornersDetected = corners.Size;

                    using var charucoCorners = new Emgu.CV.Util.VectorOfPointF();
                    using var charucoIds = new Emgu.CV.Util.VectorOfInt();
                    ArucoInvoke.InterpolateCornersCharuco(corners, ids, gray, _board, charucoCorners, charucoIds);
                    if (charucoIds.Size > 0)
                    {
                        found = true;
                        ArucoInvoke.DrawDetectedMarkers(frame, corners, ids, new MCvScalar(0, 255, 0));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalibrationTargetTest: Detection error: {ex.Message}");
        }

        DrawFrame(frame, _wb);
        UpdateUI(found);
    }

    private static byte[]? _buffer;
    private static void DrawFrame(Mat frame, WriteableBitmap wb)
    {
        try
        {
            using var bgra = new Mat();
            CvInvoke.CvtColor(frame, bgra, ColorConversion.Bgr2Bgra);
            int byteCount = bgra.Rows * bgra.Cols * bgra.ElementSize;
            if (_buffer == null || _buffer.Length != byteCount)
                _buffer = new byte[byteCount];
            Marshal.Copy(bgra.DataPointer, _buffer, 0, byteCount);
            using var stream = wb.PixelBuffer.AsStream();
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(_buffer, 0, byteCount);
            wb.Invalidate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalibrationTargetTest: Draw error {ex.Message}");
        }
    }

    private void UpdateUI(bool found)
    {
        int totalMaxCorners = boardDefinition.GetTotalSquareCount();
        int totalMaxMarkers = boardDefinition.GetTotalMarkersCount();

        // Show the maximum number of corners we have seen so far
        if (maxCornersDetected == 0)
            SquareDetection.Text = "No corners detected";
        else if (maxCornersDetected < totalMaxCorners)
            SquareDetection.Text = $"Maximum of {maxCornersDetected} corners detected of a possible {totalMaxCorners}";
        else
            SquareDetection.Text = $"All {maxCornersDetected} corners detected";

        // Show the maximum number of markers we have seen so far
        if (maxMarkersDetected == 0)
            MarkerDetection.Text = "No markers detected";
        else if (maxMarkersDetected < totalMaxMarkers)
            MarkerDetection.Text = $"Maximum of {maxMarkersDetected} markers detected of a possible {totalMaxCorners}";
        else
            MarkerDetection.Text = $"All {maxMarkersDetected} markers detected";

        if (found)
        {
            if (maxCornersDetected == totalMaxCorners && maxMarkersDetected == totalMaxMarkers)
            {
                CameraBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen);                
            }
            else
            {
                CameraBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
            }
        }
        else
        {
            CameraBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
        }

        TickOverlayText.Foreground = CameraBorder.BorderBrush;
        TickOverlayText.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
    }
}
