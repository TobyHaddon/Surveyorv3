using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Surveyor.Calibration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Windows.Foundation;

namespace Surveyor.Controls
{

    public class CalibrationFrameSetViewerData
    {
        public CalibrationFrameSetViewerData(CalibrationFrameSet _calibrationFrameSet)
        {
            calibrationFrameSet = _calibrationFrameSet;
        }
        public CalibrationFrameSetViewerData(bool _trueLeftFalseRight, CalibrationStereoFrameSet _calibrationStereoFrameSet)
        {
            trueLeftFalseRight = _trueLeftFalseRight;
            calibrationStereoFrameSet = _calibrationStereoFrameSet;
        }

        // Solo calibration frame set
        public CalibrationFrameSet? calibrationFrameSet = null;

        // Stereo calibration frame set
        public bool trueLeftFalseRight = true;
        public CalibrationStereoFrameSet? calibrationStereoFrameSet = null;        
    }

    public sealed partial class CalibrationFrameSetViewer : UserControl
    {

        private (int gx, int gy)[] layers;

        public CalibrationFrameSetViewer()
        {
            layers = FrameCalibrationTarget.GridLayers;

            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }


        public CalibrationFrameSetViewerData? Data { get; set; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SetupBinLayers();
        }

        public void DrawGraphs()
        {
            if (Data is null)
                return;

            double widthMovementCanvas = MovementCanvas.ActualWidth;
            double heightMovementCanvas = MovementCanvas.ActualHeight;
            double widthBlurCanvas = BlurCanvas.ActualWidth;
            double heightBlurCanvas = BlurCanvas.ActualHeight;

            MovementCanvas.Children.Clear();
            BlurCanvas.Children.Clear();

            var movementPoints = new List<Point>();
            var blurPoints = new List<Point>();


            if (Data.calibrationFrameSet is not null)
            {
                var frames = Data.calibrationFrameSet.Frames;
                if (frames.Count == 0) return;

                double xStep = widthMovementCanvas / (double)((int)Math.Ceiling((double)frames.Count / 100.0) * 100);

                double maxMovement = Math.Clamp(Data.calibrationFrameSet.MaxMovementFactor, 0.0, CalibrationFrameSet.MOVEMENT_LARGEVALUE);
                double maxBlur = Data.calibrationFrameSet.MaxBlurFactor * 1.1;


                double x;
                int i = 0;
                foreach (var frame in frames.Values)
                {
                    x = i * xStep;

                    try
                    {
                        if (frame.MovementFactor != -1)
                        {
                            double movementFactor = frame.MovementFactor;
                            movementFactor = Math.Clamp(movementFactor, 0.0, CalibrationFrameSet.MOVEMENT_LARGEVALUE);

                            double yMovement = heightMovementCanvas * (1 - movementFactor / maxMovement);

                            movementPoints.Add(new Point(x, yMovement));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle the exception as needed
                        System.Diagnostics.Debug.WriteLine($"Movement Error processing frame {i}: {ex.Message}");
                    }

                    try
                    {
                        double yBlur = heightBlurCanvas * (1 - frame.BlurFactor / maxBlur);
                        blurPoints.Add(new Point(x, yBlur));
                    }
                    catch (Exception ex)
                    {
                        // Handle the exception as needed
                        System.Diagnostics.Debug.WriteLine($"Blur Error processing frame {i}: {ex.Message}");
                    }

                    i++;
                }
            }
            else if (Data.calibrationStereoFrameSet is not null)
            {
                var frames = Data.calibrationStereoFrameSet.Frames;
                if (frames.Count == 0) return;

                double xStep = widthMovementCanvas / (double)((int)Math.Ceiling((double)frames.Count / 100.0) * 100);

                double maxMovement = Math.Clamp(Data.calibrationStereoFrameSet.MaxMovementFactor, 0.0, CalibrationFrameSet.MOVEMENT_LARGEVALUE);
                double maxBlur = Data.calibrationStereoFrameSet.MaxBlurFactor * 1.1;


                double x;
                int i = 0;
                foreach ((FrameCalibrationTarget leftTarget, FrameCalibrationTarget? rightTarget) in frames.Values)
                {
                    x = i * xStep;

                    FrameCalibrationTarget? frame = Data.trueLeftFalseRight ? leftTarget : rightTarget;

                    try
                    {                        
                        if (frame is not null && frame.MovementFactor != -1)
                        {
                            double movementFactor = frame.MovementFactor;
                            movementFactor = Math.Clamp(movementFactor, 0.0, CalibrationFrameSet.MOVEMENT_LARGEVALUE);

                            double yMovement = heightMovementCanvas * (1 - movementFactor / maxMovement);

                            movementPoints.Add(new Point(x, yMovement));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle the exception as needed
                        System.Diagnostics.Debug.WriteLine($"Movement Error processing frame {i}: {ex.Message}");
                    }

                    try
                    {
                        if (frame is not null)
                        {
                            double yBlur = heightBlurCanvas * (1 - frame.BlurFactor / maxBlur);
                            blurPoints.Add(new Point(x, yBlur));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle the exception as needed
                        System.Diagnostics.Debug.WriteLine($"Blur Error processing frame {i}: {ex.Message}");
                    }

                    i++;
                }
            }

            MovementCanvas.Children.Add(CreatePolyline(movementPoints, Microsoft.UI.Colors.SkyBlue));
            BlurCanvas.Children.Add(CreatePolyline(blurPoints, Microsoft.UI.Colors.Orange));

            // Add "Movement" label
            var movementLabel = new TextBlock
            {
                Text = "Movement",
                FontFamily = new FontFamily("Segoe UI Variable"),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.SkyBlue),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            movementLabel.UseLayoutRounding = true;
            Canvas.SetLeft(movementLabel, 2);
            Canvas.SetTop(movementLabel, 2);
            MovementCanvas.Children.Add(movementLabel);

            // Add "Blur" label
            var blurLabel = new TextBlock
            {
                Text = "Blur",
                FontFamily = new FontFamily("Segoe UI Variable"),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            blurLabel.UseLayoutRounding = true;
            Canvas.SetLeft(blurLabel, 2);
            Canvas.SetTop(blurLabel, heightBlurCanvas - 14); // 14 for padding from bottom
            BlurCanvas.Children.Add(blurLabel);
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


        
        private void SetupBinLayers()
        {
            if (Data is null) return;

            BinGridItemsControl.Items.Clear();

            foreach (var (gx, gy) in layers)
            {
                var grid = new Grid
                {
                    Width = 180,
                    Height = 110,
                    Margin = new Thickness(8),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Gray)
                };

                // Create columns and rows
                for (int c = 0; c < gx; c++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                for (int r = 0; r < gy; r++)
                    grid.RowDefinitions.Add(new RowDefinition());

                // Add cell borders and content
                for (int r = 0; r < gy; r++)
                {
                    for (int c = 0; c < gx; c++)
                    {
                        var cell = new Border
                        {
                            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                            BorderThickness = new Thickness(0.5),
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        };

                        var label = new TextBlock
                        {
                            Text = "0", // Default value
                            FontFamily = new FontFamily("Segoe UI Variable"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            //Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"]
                            FontSize = 10,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Margin = new Thickness(0),
                            Padding = new Thickness(0),
                            UseLayoutRounding = true
                        };

                        Grid.SetRow(cell, r);
                        Grid.SetColumn(cell, c);
                        cell.Child = label;
                        grid.Children.Add(cell);
                    }
                }

                BinGridItemsControl.Items.Add(grid);
            }
        }
        public void RefreshBinLayers()
        {
            if (Data is null)
                return;

            int gridIndex = 0;


            if (Data.calibrationFrameSet is not null)
            {

                foreach (var (gx, gy) in layers)
                {
                    // Safety check
                    if (gridIndex >= BinGridItemsControl.Items.Count)
                        break;

                    if (BinGridItemsControl.Items[gridIndex] is Grid grid)
                    {
                        var counts = Data.calibrationFrameSet.GetBinCounts(gx, gy);

                        foreach (var child in grid.Children)
                        {
                            if (child is Border border && border.Child is TextBlock textBlock)
                            {
                                int column = Grid.GetColumn(border);
                                int row = Grid.GetRow(border);

                                // Updated to use just column/row since gx/gy are implicit in the grid structure
                                textBlock.Text = counts.TryGetValue((gx, gy, column, row), out int v) ? v.ToString() : "0";
                            }
                        }
                    }
                    gridIndex++;
                }
            }
            else if (Data.calibrationStereoFrameSet is not null)
            {
                foreach (var (gx, gy) in layers)
                {
                    // Safety check
                    if (gridIndex >= BinGridItemsControl.Items.Count)
                        break;

                    if (BinGridItemsControl.Items[gridIndex] is Grid grid)
                    {
                        var counts = Data.calibrationStereoFrameSet.GetBinCounts(Data.trueLeftFalseRight, gx, gy);

                        foreach (var child in grid.Children)
                        {
                            if (child is Border border && border.Child is TextBlock textBlock)
                            {
                                int column = Grid.GetColumn(border);
                                int row = Grid.GetRow(border);

                                // Updated to use just column/row since gx/gy are implicit in the grid structure
                                textBlock.Text = counts.TryGetValue((gx, gy, column, row), out int v) ? v.ToString() : "0";
                            }
                        }
                    }
                    gridIndex++;
                }
            }
        }

    }
}
