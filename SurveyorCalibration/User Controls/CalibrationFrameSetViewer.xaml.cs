using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Surveyor.Calibration;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace Surveyor.Controls
{

    public class CalibrationFrameSetViewerData(bool _trueLeftFalseRight, CalibrationStereoFrameSet _calibrationStereoFrameSet)
    {
        // Stereo calibration frame set
        public bool trueLeftFalseRight = _trueLeftFalseRight;
        public CalibrationStereoFrameSet? calibrationStereoFrameSet = _calibrationStereoFrameSet;        
    }

    public sealed partial class CalibrationFrameSetViewer : UserControl
    {

        private (int gx, int gy)[] sensorBinLayers;

        public CalibrationFrameSetViewer()
        {
            sensorBinLayers = FrameCalibrationData.SensorBinGridLayers;

            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }


        public CalibrationFrameSetViewerData? Data { get; set; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SetupSensorBinLayers();
            SetupPoseBinLayers();
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
        /// Draw the movement and blur graphs based on the current data.
        /// </summary>
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


            if (Data.calibrationStereoFrameSet is not null)
            {
                var frames = Data.calibrationStereoFrameSet.Frames;
                if (frames.Count == 0) return;

                double xStep = widthMovementCanvas / (double)((int)Math.Ceiling((double)frames.Count / 100.0) * 100);

                double maxMovement = Math.Clamp(Data.calibrationStereoFrameSet.MaxMovementFactor, 0.0, CalibrationStereoFrameSet.MOVEMENT_LARGEVALUE);
                double maxBlur = Data.calibrationStereoFrameSet.MaxBlurFactor * 1.1;


                double x;
                int i = 0;
                foreach ((FrameCalibrationData leftTarget, FrameCalibrationData? rightTarget, _) in frames.Values)
                {
                    x = i * xStep;

                    FrameCalibrationData? frame = Data.trueLeftFalseRight ? leftTarget : rightTarget;

                    try
                    {                        
                        if (frame is not null && frame.MovementFactor != -1)
                        {
                            double movementFactor = frame.MovementFactor;
                            movementFactor = Math.Clamp(movementFactor, 0.0, CalibrationStereoFrameSet.MOVEMENT_LARGEVALUE);

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
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                UseLayoutRounding = true
            };
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


        /// <summary>
        /// Setup the sensor bin grid
        /// </summary>
        private void SetupSensorBinLayers()
        {
            if (Data is null) return;

            // Reset the rows and columns
            SensorBinGridItemsControl.Children.Clear();

            // Only support the first sensor bin layer 
            var (gx, gy) = sensorBinLayers.FirstOrDefault();

            // Create columns and rows
            for (int c = 0; c < gx; c++)
                SensorBinGridItemsControl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int r = 0; r < gy; r++)
                SensorBinGridItemsControl.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

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
                        //Width = 50,
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Margin = new Thickness(0),
                        Padding = new Thickness(0),
                        UseLayoutRounding = true
                    };

                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    cell.Child = label;
                    SensorBinGridItemsControl.Children.Add(cell);
                }
            }
        }

        /// <summary>
        /// Setup the pose bin grid
        /// </summary>
        private void SetupPoseBinLayers()
        {
            if (Data is null) return;

            // Reset the rows and columns
            PoseBinGridItemsControl.Children.Clear();

            // Get the number of columns and rows for the pose bin grid
            (int gx, int gy) = FrameCalibrationData.PoseBinGrid;

            // Create columns and rows
            for (int c = 0; c < gx; c++)
                PoseBinGridItemsControl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int r = 0; r < gy; r++)
                PoseBinGridItemsControl.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
          
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
                        //Width = 50,
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Margin = new Thickness(0),
                        Padding = new Thickness(0),
                        UseLayoutRounding = true
                    };

                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    cell.Child = label;
                    PoseBinGridItemsControl.Children.Add(cell);
                }
            }
        }


        /// <summary>
        /// Refresh the sensor bin layers with the current data.
        /// </summary>
        public void RefreshSensorBinLayers()
        {
            if (Data is null)
                return;

            if (Data.calibrationStereoFrameSet is not null)
            {
                // Only support the first sensor bin layer 
                var (gx, gy) = sensorBinLayers.FirstOrDefault();

                var counts = Data.calibrationStereoFrameSet.GetSensorBinCounts(Data.trueLeftFalseRight, gx, gy);

                foreach (var child in SensorBinGridItemsControl.Children)
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
        }


        /// <summary>
        /// Refresh the sensor bin layers with the current data.
        /// </summary>
        public void RefreshPoseBinLayers()
        {
            if (Data is null)
                return;

            if (Data.calibrationStereoFrameSet is not null)
            {
                (int gx, int gy) = FrameCalibrationData.PoseBinGrid;

                var counts = Data.calibrationStereoFrameSet.GetPoseBinCounts(Data.trueLeftFalseRight);

                foreach (var child in PoseBinGridItemsControl.Children)
                {
                    if (child is Border border && border.Child is TextBlock textBlock)
                    {
                        int column = Grid.GetColumn(border);
                        int row = Grid.GetRow(border);

                        // Updated to use just column/row since gx/gy are implicit in the grid structure
                        textBlock.Text = counts.TryGetValue((column, row), out int v) ? v.ToString() : "0";
                    }
                }

            }
        }


        /// <summary>
        /// Highlight on the screen the used sensor bins for this frame
        /// </summary>
        /// <param name="frameCalibrationData"></param>
        public void HighLightActiveSensorBinLayers(FrameCalibrationData? frameCalibrationData)
        {
            if (Data is null)
                return;

            if (Data.calibrationStereoFrameSet is not null)
            {
                // Only support the first sensor bin layer 
                var (gx, gy) = sensorBinLayers.FirstOrDefault();

                {
                    if (frameCalibrationData is null)
                    {
                        // Clear colour of the the bins
                        foreach (var child in SensorBinGridItemsControl.Children)
                        {
                            if (child is Border border &&
                                border.Child is TextBlock textBlock)
                            {
                                border.Background = null;   //??? new SolidColorBrush(color);
                            }
                        }
                    }
                    else
                    {
                        foreach (var child in SensorBinGridItemsControl.Children)
                        {
                            if (child is Border border &&
                                border.Child is TextBlock textBlock)
                            {
                                int row = Grid.GetRow(border);
                                int column = Grid.GetColumn(border);


                                bool colourCell = frameCalibrationData.SensorBinsOccupied
                                                            .Any(entry => entry.gx == gx && entry.gy == gy && entry.binx == column && entry.biny == row);

                                if (colourCell)
                                    border.Background = new SolidColorBrush(Colors.LightBlue);
                                else
                                    border.Background = null;
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Highlight on the screen the used pose bins for this frame
        /// </summary>
        /// <param name="frameCalibrationData"></param>
        public void HighLightActivePoseBinLayers(FrameCalibrationData? frameCalibrationData)
        {
            if (Data is null)
                return;

            
            if (Data.calibrationStereoFrameSet is not null)
            {
                if (frameCalibrationData is null)
                {
                    // Clear colour of the the bins
                    foreach (var child in PoseBinGridItemsControl.Children)
                    {
                        if (child is Border border &&
                            border.Child is TextBlock textBlock)
                        {
                            border.Background = null;   //??? new SolidColorBrush(color);
                        }
                    }
                }
                else
                {
                    foreach (var child in PoseBinGridItemsControl.Children)
                    {
                        if (child is Border border &&
                            border.Child is TextBlock textBlock)
                        {
                            int row = Grid.GetRow(border);
                            int column = Grid.GetColumn(border);

                            bool colourCell = frameCalibrationData.PoseBinsOccupied
                                                        .Any(entry => entry.binx == column && entry.biny == row);

                            if (colourCell)
                                border.Background = new SolidColorBrush(Colors.LightBlue);
                            else
                                border.Background = null;
                        }
                    }
                }
            }
        }
    }
}
