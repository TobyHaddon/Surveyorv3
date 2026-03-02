using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Surveyor;
using Surveyor.Calibration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Foundation;
using Windows.Foundation.Collections;
using static Surveyor.CalibProject.DataClass.CalibrationResultClass;


namespace Surveyor.Controls
{
    public sealed partial class CalibrationIterationViewer : UserControl
    {
        private readonly SolidColorBrush BorderBrushNormal = new(Microsoft.UI.Colors.LightGray);
        private readonly SolidColorBrush BorderBrushHighlighted = new(Microsoft.UI.Colors.IndianRed);

        private readonly SolidColorBrush CalibrationQualityExcellent = new(Microsoft.UI.Colors.Honeydew);
        private readonly SolidColorBrush CalibrationQualityVeryGood = new(Microsoft.UI.Colors.MintCream);
        private readonly SolidColorBrush CalibrationQualityAcceptable = new(Microsoft.UI.Colors.LemonChiffon);
        private readonly SolidColorBrush CalibrationQualityPoor = new(Microsoft.UI.Colors.PeachPuff);
        private readonly SolidColorBrush CalibrationQualityVeryPoor = new(Microsoft.UI.Colors.MistyRose);
        private readonly SolidColorBrush CalibrationQualityTerrible = new(Microsoft.UI.Colors.Gainsboro);


        public class CalibrationIterationViewerData
        {
            // Stereo calibration frame set
            public UniversalCalibrationHead.HeadType? headType;
            public IterationResultList? iterationResultList;

            public CalibrationIterationViewerData(UniversalCalibrationHead.HeadType _headType, IterationResultList _iterationResultList)
            {
                headType = _headType;
                iterationResultList = _iterationResultList;
            }
        }

        public CalibrationIterationViewer()
        {
            InitializeComponent();
            this.Loaded += OnLoaded;
        }



        public CalibrationIterationViewerData? Data { get; set; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SetupInputsAndCounts();
        }


        /// <summary>
        /// Set the title of this viewer - this is used as the title for Head in reality
        /// </summary>
        /// <param name="title"></param>
        public void SetTitle(string title)
        {
            TitleText.Text = title;
        }


        /// <summary>
        /// Draw the calibration results chart and graphs
        /// </summary>
        public void DrawGraphs()
        {
            if (Data is null)
                return;

            double widthCalibrationResultsCanvas = CalibrationResultsCanvas.ActualWidth;
            double heightCalibrationResultCanvas = CalibrationResultsCanvas.ActualHeight - 10;  // To add a margin so the line is never above the border
            
            // Skip until measured
            if (heightCalibrationResultCanvas <= 0 || widthCalibrationResultsCanvas <= 0)
                return;

            CalibrationResultsCanvas.Children.Clear();

            var reprojectionRMSPoints = new List<Point>();
            var p95ErrorPoints = new List<Point>();


            if (Data.iterationResultList is not null)
            {

                if (Data.iterationResultList.Results.Count == 0) return;

                double xStep = widthCalibrationResultsCanvas / (double)((int)Math.Ceiling((double)Data.iterationResultList.Results.Count / 40.0) * 40);

                // Get a reprojection RMS very large value
                double largeValueReprojectionRMS;
                if (Data.headType == UniversalCalibrationHead.HeadType.Stereo)
                    largeValueReprojectionRMS = StereoCalibrationQualityClassifier.RMSVeryPoorMax * 2;
                else
                    largeValueReprojectionRMS = MonoCalibrationQualityClassifier.RMSVeryPoorMax * 2;

                // Get a p95eErrorreprojection very large value
                double largeValueP95Error;
                if (Data.headType == UniversalCalibrationHead.HeadType.Stereo)
                    largeValueP95Error = StereoCalibrationQualityClassifier.P95VeryPoorMax * 2;
                else
                    largeValueP95Error = MonoCalibrationQualityClassifier.P95VeryPoorMax * 2;


                // Get the maximum reprojection RMS
                double maxReprojectionRMS = Data.iterationResultList.Results.Max(result => result.ReprojectionRMS);
                maxReprojectionRMS = Math.Clamp(maxReprojectionRMS, 0.0, largeValueReprojectionRMS);
                if (maxReprojectionRMS <= 0) maxReprojectionRMS = 1.0; // prevent divide-by-zero

                // Get the maximum 95% error (P95Error)
                double maxP95Error = Data.iterationResultList.Results.Max(result => result.P95Error);
                maxP95Error = Math.Clamp(maxP95Error, 0.0, largeValueP95Error);
                if (maxP95Error <= 0) maxP95Error = 1.0;

                double xLine;
                double xRectStart;
                double xRectEnd;
                int i = 0;
                double y;

                IterationResult bestResult = Data.iterationResultList.GetBestResult();

                foreach (IterationResult iterationResult in Data.iterationResultList.Results)
                {
                    xLine = (i * xStep) + (xStep / 2);
                    xRectStart = i * xStep;
                    xRectEnd = xRectStart + xStep;

                    // Draw classification rectangle
                    SolidColorBrush fill;

                    if (Data.headType == UniversalCalibrationHead.HeadType.Stereo)
                    {
                        fill = iterationResult.StereoCalibrationQuality switch
                        {
                            StereoCalibrationQuality.Excellent => CalibrationQualityExcellent,
                            StereoCalibrationQuality.VeryGood => CalibrationQualityVeryGood,
                            StereoCalibrationQuality.Acceptable => CalibrationQualityAcceptable,
                            StereoCalibrationQuality.Poor => CalibrationQualityPoor,
                            StereoCalibrationQuality.VeryPoor => CalibrationQualityVeryPoor,
                            StereoCalibrationQuality.Terrible => CalibrationQualityTerrible,
                            _ => CalibrationQualityTerrible,
                        };
                    }
                    else
                    {
                        fill = iterationResult.MonoCalibrationQuality switch
                        {
                            MonoCalibrationQuality.Excellent => CalibrationQualityExcellent,
                            MonoCalibrationQuality.VeryGood => CalibrationQualityVeryGood,
                            MonoCalibrationQuality.Acceptable => CalibrationQualityAcceptable,
                            MonoCalibrationQuality.Poor => CalibrationQualityPoor,
                            MonoCalibrationQuality.VeryPoor => CalibrationQualityVeryPoor,
                            MonoCalibrationQuality.Terrible => CalibrationQualityTerrible,
                            _ => CalibrationQualityTerrible,
                        };
                    }

                    Rectangle rect = new()
                    {
                        Width = xStep,
                        Height = heightCalibrationResultCanvas,
                        Fill = fill,
                        Opacity = 0.35,
                        IsHitTestVisible = true,
                    };

                    // Put a border around the best result
                    if (bestResult == iterationResult)
                    {
                        rect.Stroke = BorderBrushHighlighted;
                        rect.StrokeThickness = 1;
                    }

                    string quality;
                    if (Data.headType == UniversalCalibrationHead.HeadType.Stereo)
                        quality = $"Stereo Quality: {iterationResult.StereoCalibrationQuality}";
                    else
                        quality = $"Mono Quality: {iterationResult.MonoCalibrationQuality}";
                            
                    var toolTip = new ToolTip
                    {
                        Content =
                            $"Frame Count: {iterationResult.BestFramesCount}\n" +
                            $"Movement: {iterationResult.MovementMinThreshold:F1}\n" +
                            $"Blur: {iterationResult.BlurMinThreshold:F1}\n" +
                            $"Corners: {iterationResult.MonoCornersMinThreshold}\n" +
                            $"Params: {iterationResult.CalibrationParameters}\n" +
                            $"RMS: {iterationResult.ReprojectionRMS:F3}\n" +
                            $"P95: {iterationResult.P95Error:F3}\n" +
                            $"Max: {iterationResult.MaxError:F3}\n" +
                            quality
                    };
                    ToolTipService.SetToolTip(rect, toolTip);

                    Canvas.SetLeft(rect, xRectStart);
                    Canvas.SetTop(rect, 0);
                    CalibrationResultsCanvas.Children.Add(rect);

                    // Draw reprojection RMS line
                    try
                    {
                        double reprojectionRMS = Math.Clamp(iterationResult.ReprojectionRMS, 0.0, largeValueReprojectionRMS);
                        y = heightCalibrationResultCanvas * (1 - reprojectionRMS / maxReprojectionRMS);
                        y = Math.Clamp(y, 0.0, heightCalibrationResultCanvas);
                        reprojectionRMSPoints.Add(new Point(xLine, y));
                    }
                    catch (Exception ex)
                    {
                        // Handle the exception as needed
                        Debug.WriteLine($"Reprojection RMS: Error processing iteration result: {ex.Message}");
                    }

                    // Draw 95% error line
                    try
                    {
                        double p95Error = Math.Clamp(iterationResult.P95Error, 0.0, largeValueP95Error);
                        y = heightCalibrationResultCanvas * (1 - p95Error / maxP95Error);
                        y = Math.Clamp(y, 0.0, heightCalibrationResultCanvas);
                        p95ErrorPoints.Add(new Point(xLine, y));
                    }
                    catch (Exception ex)
                    {
                        // Handle the exception as needed
                        Debug.WriteLine($"95% Error: Error processing iteration result: {ex.Message}");
                    }

                    i++;
                }
            }

            CalibrationResultsCanvas.Children.Add(CreatePolyline(reprojectionRMSPoints, Microsoft.UI.Colors.SkyBlue));
            CalibrationResultsCanvas.Children.Add(CreatePolyline(p95ErrorPoints, Microsoft.UI.Colors.Orange));

            // Add "Reprojection RMS" label
            var reprojectionRMSLabel = new TextBlock
            {
                Text = "Reprojection RMS",
                FontFamily = new FontFamily("Segoe UI Variable"),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.SkyBlue),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                UseLayoutRounding = true
            };
            Canvas.SetLeft(reprojectionRMSLabel, 2);
            Canvas.SetTop(reprojectionRMSLabel, heightCalibrationResultCanvas - 30); // 30 for padding from bottom
            CalibrationResultsCanvas.Children.Add(reprojectionRMSLabel);

            // Add "95% Error" label
            var p95ErrorLabel = new TextBlock
            {
                Text = "95% Error",
                FontFamily = new FontFamily("Segoe UI Variable"),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                UseLayoutRounding = true
            };
            Canvas.SetLeft(p95ErrorLabel, 2);
            Canvas.SetTop(p95ErrorLabel, heightCalibrationResultCanvas - 14); // 14 for padding from bottom
            CalibrationResultsCanvas.Children.Add(p95ErrorLabel);
        }

        private static Polyline CreatePolyline(List<Point> points, Windows.UI.Color color)
        {
            var pointCollection = new Microsoft.UI.Xaml.Media.PointCollection();
            foreach (var pt in points)
            {
                pointCollection.Add(pt);
            }

            return new Polyline
            {
                Points = pointCollection,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2
            };
        }


        /// <summary>
        /// Setup the sensor bin grid
        /// </summary>
        private void SetupInputsAndCounts()
        {
            if (Data is null) return;

        }


        /// <summary>
        /// Display the current calibration inputs and best frame count
        /// </summary>
        public void RefreshInputsAndCounts(double movementThreshold, double movementThresholdStart, double movementThresholdStop,
                                           int cornersThreshold, int cornersThresholdStart, int cornersThresholdStop, 
                                           double blurThreshold,
                                           int bestFrameCount, int iterationNumber)
        {
            StringBuilder sb = new();

            // Titles
            sb.AppendLine($"            Current      Range");
            sb.AppendLine($" Movement   {movementThreshold,5:F1}    {movementThresholdStart,5:F1} > {movementThresholdStop:F1} ");
            sb.AppendLine($" Corners    {cornersThreshold,5}    {cornersThresholdStart,5} > {cornersThresholdStop} ");
            sb.AppendLine($" Blur       {blurThreshold,5:F1} ");
            sb.AppendLine($" ");
            sb.AppendLine($" Iteration number:{iterationNumber,3} ");
            if (bestFrameCount != 0)
                sb.AppendLine($" Best frame count:{bestFrameCount,3} ");

               
            InputsAndCountsText.Text = sb.ToString();
        }
    }
}
