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

        private (int gx, int gy) sensorBinGrid;

        public CalibrationFrameSetViewer()
        {
            sensorBinGrid = FrameData.SensorBinGrid;

            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }


        public CalibrationFrameSetViewerData? Data { get; set; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SetupSensorBin();
            SetupPoseBin();
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
                var frames = Data.calibrationStereoFrameSet.Data.Frames;
                if (frames.Count == 0) return;

                double xStep = widthMovementCanvas / (double)((int)Math.Ceiling((double)frames.Count / 100.0) * 100);

                double maxMovement = Math.Clamp(Data.calibrationStereoFrameSet.MaxMovementFactor, 0.0, CalibrationStereoFrameSet.MOVEMENT_LARGEVALUE);
                double maxBlur = Data.calibrationStereoFrameSet.MaxBlurFactor * 1.1;


                double x;
                int i = 0;
                foreach ((FrameData leftTarget, FrameData? rightTarget, _) in frames.Values)
                {
                    x = i * xStep;

                    FrameData? frame = Data.trueLeftFalseRight ? leftTarget : rightTarget;

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
        private void SetupSensorBin()
        {
            if (Data is null) return;

            // Reset the rows and columns
            SensorBinGridItemsControl.Children.Clear();

            // Get the size of the sensor bin grid
            var (gx, gy) = sensorBinGrid;

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
                        Text = "", // Default value
                        FontFamily = new FontFamily("Segoe UI Variable"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
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
        void SetupPoseBin()
        {
            if (Data is null)
                return;

            // Reset the rows, columns and children
            PoseBinGridItemsControl.Children.Clear();
            PoseBinGridItemsControl.RowDefinitions.Clear();
            PoseBinGridItemsControl.ColumnDefinitions.Clear();

            // 3×3, but uses whatever FrameCalibrationData says
            (int gx, int gy) = FrameData.PoseBinGrid;

            for (int c = 0; c < gx; c++)
                PoseBinGridItemsControl.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int r = 0; r < gy; r++)
                PoseBinGridItemsControl.RowDefinitions.Add(
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Build each cell: border → inner Grid → [board icon + count label]
            for (int r = 0; r < gy; r++)          // pitch index
            {
                for (int c = 0; c < gx; c++)      // yaw index
                {
                    var cellBorder = new Border
                    {
                        BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                        BorderThickness = new Thickness(0.5),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    var innerGrid = new Grid
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    // --- Pose icon (small “board” rectangle) ---
                    var boardRect = new Rectangle
                    {
                        Width = 20,   // fixed icon size works fine at your current scale
                        Height = 14,
                        Fill = new SolidColorBrush(Microsoft.UI.Colors.DarkSlateGray),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        RenderTransformOrigin = new Point(0.5, 0.5)
                    };

                    // Representative yaw/pitch per bin (switch expressions)
                    double yawDeg = c switch
                    {
                        0 => -30,
                        1 => 30,
                        _ => 0
                    };

                    double pitchDeg = r switch
                    {
                        0 => -30,
                        1 => 30,
                        _ => 0
                    };

                    // Map yaw → Z rotation (screen) and pitch → squashing
                    double zRotation = yawDeg * 0.4; // tweak factor if you want more/less tilt
                    double pitchFactor = 1.0 - (Math.Abs(pitchDeg) / 90.0) * 0.4;

                    var transformGroup = new TransformGroup();
                    transformGroup.Children.Add(new ScaleTransform
                    {
                        ScaleX = 1.0,
                        ScaleY = pitchFactor
                    });
                    transformGroup.Children.Add(new RotateTransform
                    {
                        Angle = zRotation
                    });
                    boardRect.RenderTransform = transformGroup;

                    innerGrid.Children.Add(boardRect);

                    // --- Count label (bottom-right) ---
                    var countLabel = new TextBlock
                    {
                        Text = "", // will be filled in RefreshPoseBinLayers
                        FontFamily = new FontFamily("Segoe UI Variable"),
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(1),
                        Padding = new Thickness(0),
                        UseLayoutRounding = true
                    };

                    innerGrid.Children.Add(countLabel);

                    cellBorder.Child = innerGrid;

                    Grid.SetRow(cellBorder, r);
                    Grid.SetColumn(cellBorder, c);

                    PoseBinGridItemsControl.Children.Add(cellBorder);
                }
            }

            // Immediately populate counts + tooltips if data is present
            RefreshPoseBin();
        }

        //???private void SetupPoseBin()
        //{
        //    if (Data is null) return;

        //    // Reset the rows and columns
        //    PoseBinGridItemsControl.Children.Clear();

        //    // Get the number of columns and rows for the pose bin grid
        //    (int gx, int gy) = FrameCalibrationData.PoseBinGrid;

        //    // Create columns and rows
        //    for (int c = 0; c < gx; c++)
        //        PoseBinGridItemsControl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        //    for (int r = 0; r < gy; r++)
        //        PoseBinGridItemsControl.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        //    // Add cell borders and content
        //    for (int r = 0; r < gy; r++)
        //    {
        //        for (int c = 0; c < gx; c++)
        //        {
        //            var cell = new Border
        //            {
        //                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
        //                BorderThickness = new Thickness(0.5),
        //                HorizontalAlignment = HorizontalAlignment.Stretch,
        //                VerticalAlignment = VerticalAlignment.Stretch
        //            };

        //            var label = new TextBlock
        //            {
        //                Text = "", // Default value
        //                FontFamily = new FontFamily("Segoe UI Variable"),
        //                HorizontalAlignment = HorizontalAlignment.Center,
        //                VerticalAlignment = VerticalAlignment.Center,
        //                FontSize = 10,
        //                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        //                Margin = new Thickness(0),
        //                Padding = new Thickness(0),
        //                UseLayoutRounding = true
        //            };

        //            Grid.SetRow(cell, r);
        //            Grid.SetColumn(cell, c);
        //            cell.Child = label;
        //            PoseBinGridItemsControl.Children.Add(cell);
        //        }
        //    }
        //}


        /// <summary>
        /// Refresh the sensor bin layers with the current data.
        /// </summary>
        public void RefreshSensorBin()
        {
            if (Data is null)
                return;

            if (Data.calibrationStereoFrameSet is not null)
            {
                // Get the size of the sensor bin grid
                var (gx, gy) = sensorBinGrid;

                var counts = Data.calibrationStereoFrameSet.GetSensorBinCounts(Data.trueLeftFalseRight);

                foreach (var child in SensorBinGridItemsControl.Children)
                {
                    if (child is Border border && border.Child is TextBlock textBlock)
                    {
                        int column = Grid.GetColumn(border);
                        int row = Grid.GetRow(border);

                        if (counts.TryGetValue((column, row), out int count))
                        {
                            textBlock.Text = count != 0 ? count.ToString() : "";

                            byte b = (byte)Math.Min(count, 255);
                            border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, b));
                        }
                        else
                        {
                            textBlock.Text = "";
                            border.Background = null;
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Refresh the sensor bin layers with the current data.
        /// </summary>
        public void RefreshPoseBin()
        {
            if (Data is null || Data.calibrationStereoFrameSet is null)
                return;

            var counts = Data.calibrationStereoFrameSet.GetPoseBinCounts(Data.trueLeftFalseRight);

            int maxCount = counts.Count > 0 ? counts.Values.Max() : 0;

            foreach (var child in PoseBinGridItemsControl.Children)
            {
                if (child is not Border border)
                    continue;

                int column = Grid.GetColumn(border); // yaw index
                int row = Grid.GetRow(border);    // pitch index

                counts.TryGetValue((column, row), out int count);

                // Background shading based on occupancy
                if (maxCount > 0 && count > 0)
                {
                    double t = (double)count / maxCount; // 0..1
                    t = 0.15 + 0.75 * t;                 // avoid totally transparent bins
                    byte a = (byte)(t * 255);
                    border.Background = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(a, 0, 120, 255));
                }
                else
                {
                    border.Background = null;
                }

                // Update the text label inside the cell
                if (border.Child is Grid innerGrid)
                {
                    foreach (var inner in innerGrid.Children)
                    {
                        if (inner is TextBlock tb)
                        {
                            tb.Text = count > 0 ? count.ToString() : "";
                            break;
                        }
                    }
                }

                // Tooltip with yaw/pitch ranges + count
                var tooltip = new ToolTip
                {
                    Content = GetPoseBinTooltipText(column, row, count)
                };
                ToolTipService.SetToolTip(border, tooltip);
            }
        }
        //???public void RefreshPoseBin()
        //{
        //    if (Data is null)
        //        return;

        //    if (Data.calibrationStereoFrameSet is not null)
        //    {
        //        (int gx, int gy) = FrameCalibrationData.PoseBinGrid;

        //        var counts = Data.calibrationStereoFrameSet.GetPoseBinCounts(Data.trueLeftFalseRight);

        //        foreach (var child in PoseBinGridItemsControl.Children)
        //        {
        //            if (child is Border border && border.Child is TextBlock textBlock)
        //            {
        //                int column = Grid.GetColumn(border);
        //                int row = Grid.GetRow(border);

        //                // Updated to use just column/row since gx/gy are implicit in the grid structure
        //                textBlock.Text = counts.TryGetValue((column, row), out int v) ? v != 0 ? v.ToString() : "" : "";
        //            }
        //        }

        //    }
        //}


        /// <summary>
        /// Highlight on the screen the used sensor bins for this frame
        /// Set clear the highlight frameCalibrationData = null
        /// </summary>
        /// <param name="frameCalibrationData"></param>
        public void HighLightActiveSensorBin(FrameData? frameCalibrationData)
        {
            if (Data is null)
                return;

            if (Data.calibrationStereoFrameSet is not null)
            {
                // Get the size of the sensor bin grid
                var (gx, gy) = sensorBinGrid;

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


        /// <summary>
        /// Highlight on the screen the used pose bins for this frame
        /// Set clear the highlight frameCalibrationData = null
        /// </summary>
        /// <param name="frameCalibrationData"></param>
        public void HighLightActivePoseBin(FrameData? frameCalibrationData)
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
                    // Parse each pose bin cell and highlight it if
                    // the current frame occupies it
                    foreach (var child in PoseBinGridItemsControl.Children)
                    {
                        if (child is Border border &&
                            border.Child is TextBlock textBlock)
                        {
                            int row = Grid.GetRow(border);
                            int column = Grid.GetColumn(border);

                            //???bool colourCell = frameCalibrationData.PoseBinsOccupied
                            //???                            .Any(entry => entry.binx == column && entry.biny == row);

                            if (frameCalibrationData.PoseBinX == column && 
                                frameCalibrationData.PoseBinY == row)
                                border.Background = new SolidColorBrush(Colors.LightBlue);
                            else
                                border.Background = null;
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Used to create the tooltip text for a pose bin cell
        /// </summary>
        /// <param name="yawIndex"></param>
        /// <param name="pitchIndex"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private static string GetPoseBinTooltipText(int yawIndex, int pitchIndex, int count)
        {
            string yawText;
            string pitchText;

            try
            {
                // X axis = yaw (columns)
                yawText = yawIndex switch
                {
                    0 => $"Yaw < {FrameData.PoseBinThresholdYaw[0]:F1}°",
                    1 => $"{FrameData.PoseBinThresholdYaw[0]:F1}° ≤ Yaw < {FrameData.PoseBinThresholdYaw[1]:F1}°",
                    2 => $"{FrameData.PoseBinThresholdYaw[1]:F1}° ≤ Yaw < {FrameData.PoseBinThresholdYaw[2]:F1}°",
                    _ => $"Yaw bin {yawIndex}"
                };
            }
            catch (Exception ex)
            {
                // Defensive coding in case of index error
                yawText = $"GetPoseBinTooltipText: {ex.Message}";
            }

            try
            { 
                // Y axis = pitch (rows)
                pitchText = pitchIndex switch
                {
                    0 => $"Pitch < {FrameData.PoseBinThresholdPitch[0]:F1}°",
                    1 => $"{FrameData.PoseBinThresholdPitch[0]:F1}° ≤ Pitch < {FrameData.PoseBinThresholdPitch[1]:F1}°",
                    2 => $"{FrameData.PoseBinThresholdPitch[1]:F1}° ≤ Pitch < {FrameData.PoseBinThresholdPitch[2]:F1}°",
                    _ => $"Pitch bin {pitchIndex}"
                };
            }
            catch (Exception ex)
            {
                // Defensive coding in case of index error
                pitchText = $"GetPoseBinTooltipText: {ex.Message}";
            }

            return $"{yawText}\n{pitchText}\nFrames: {count}";
        }
    }
}
