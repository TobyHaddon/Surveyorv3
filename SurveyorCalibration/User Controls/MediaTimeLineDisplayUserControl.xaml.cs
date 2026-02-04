using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinUIEx;


namespace Surveyor.Controls
{
    public sealed partial class MediaTimeLineDisplayUserControl : UserControl
    {
        private const int timeLineMargin = 2;
        private const int timeLineHeight = 4;
        private const int boardFoundAtWidth = 1;
        private const double dotRadius = 4;

        private int startMediaFrameIndex = -1;
        private int endMediaFrameIndex = -1;

        // Colors
        private readonly Brush timeLimeBackground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        private readonly Brush manuallyAddedDotColour = new SolidColorBrush(Microsoft.UI.Colors.Green);
        private readonly Brush manuallyIgnoredDotColour = new SolidColorBrush(Microsoft.UI.Colors.Red);
        private readonly Brush coverageDotColour = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 120, 255)); // Blue 
        private readonly Brush poseDotColour = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 165, 0)); // Orange
        private readonly Brush coverageAndPoseCircleStrokeColour = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 120, 255)); // Blue 
        private readonly Brush coverageAndPoseCircelFillColour = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 165, 0)); // Orange



        public MediaTimeLineDisplayUserControl()
        {
            this.InitializeComponent();
        }

        public void Clear()
        {
            MediaTimeLineDisplay.Children.Clear();
            DrawTimeLineBackground();
        }

        public void SetRange(int _startMediaFrameIndex, int _endMediaFrameIndex)
        {
            startMediaFrameIndex = _startMediaFrameIndex;
            endMediaFrameIndex = _endMediaFrameIndex;

            // Setup the canvas width
            double width = (endMediaFrameIndex - startMediaFrameIndex + 1); // Assuming each frame is represented by 100 pixels
            if (width < 0)
            {
                width = 0;
            }
            MediaTimeLineDisplay.Width = width;
            Clear();
        }


        /// <summary>
        /// This method is used to show that a calibration boards was detected 
        /// in the indicate frame and is drawn on the timeline is in green
        /// </summary>
        /// <param name="frameIndex"></param>
        public void CalibrationBoardFoundAt(int frameIndex, bool trueFoundFalseNotFound)
        {
            if (frameIndex < startMediaFrameIndex || frameIndex > endMediaFrameIndex)
                return; // Ignore out-of-range

            double x = frameIndex - startMediaFrameIndex;

            Windows.UI.Color color;

            if (trueFoundFalseNotFound)
                color = Microsoft.UI.Colors.LightGreen;
            else
                color = Microsoft.UI.Colors.Black;

            var line = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = boardFoundAtWidth,
                Height = timeLineHeight,
                Fill = new SolidColorBrush(color), // or use Colors.Green
            };

            // Position it on the Canvas
            Canvas.SetLeft(line, x);
            Canvas.SetTop(line, timeLineMargin);

            MediaTimeLineDisplay.Children.Add(line);
        }


        /// <summary>
        /// This method is called when the calibration board range is fully known.
        /// </summary>
        /// <param name="calilbrationBoardStartframeIndex"></param>
        /// <param name="calilbrationBoardEndframeIndex"></param>
        public void CalibrationBoardRange(int calibrationBoardStartFrameIndex, int calibrationBoardEndFrameIndex)
        {
            MediaTimeLineDisplay.Children.Clear();

            if (calibrationBoardStartFrameIndex < startMediaFrameIndex || calibrationBoardEndFrameIndex > endMediaFrameIndex)
                return; // Ignore out-of-range

            double startX = calibrationBoardStartFrameIndex - startMediaFrameIndex;
            double endX = calibrationBoardEndFrameIndex - startMediaFrameIndex;
            double width = endX - startX;
            if (width < 0)
            {
                width = 0;
            }         

            var rectangle = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = width,
                Height = timeLineHeight,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.LightBlue), // or use Colors.Blue
            };

            // Position it on the Canvas
            Canvas.SetLeft(rectangle, startX);
            Canvas.SetTop(rectangle, timeLineMargin);
            MediaTimeLineDisplay.Children.Add(rectangle);
        }

        /// <summary>
        /// This method is called to indicate a best calibration board frame (it is called once
        /// for each best frame). It typically overlays the CalibrationBoardRange()
        /// </summary>
        /// <param name="frameIndex"></param>
        public void BestFrameFoundAt(BestFrame bestFrame)
        {
            if (bestFrame.FrameIndex < startMediaFrameIndex || bestFrame.FrameIndex > endMediaFrameIndex)
                return; // Ignore out-of-range

            double x = bestFrame.FrameIndex - startMediaFrameIndex;

            DrawBestFrameDot(x, bestFrame.FrameIndex, bestFrame.Reason);            
        }


        /// <summary>
        /// Draw all the best frames on the timeline
        /// </summary>
        /// <param name="BestFrameIndexes"></param>
        public void RenderBestFramesOnTimeline(List<BestFrame> BestFrameIndexes)
        {
            // Remove any existing indicator dots
            RemoveAllBestFrames();

            // Iterate over BestFrameIndexes
            foreach (BestFrame bestFrame in BestFrameIndexes)
            {
                BestFrameFoundAt(bestFrame);
            }
        }


        /// <summary>
        /// Remove all the best frame indicator dots from the timeline
        /// </summary>
        public void RemoveAllBestFrames()
        {
            CanvasDrawingHelper.RemoveCanvasShapesByTag(MediaTimeLineDisplay, "Best");
        }


        ///
        /// Events
        ///

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Viewbox_SizeChanged(object sender, SizeChangedEventArgs e)
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CalibrationBoardTimeLine_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            Debug.WriteLine("CalibrationBoardTimeLine_PointerPressed");
        }


        /// <summary>
        /// User clicked on a dot on the timeline
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private DateTime lastClickTime = DateTime.MinValue;
        private void CalibrationBoardTimeLineDot_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Temp
            Debug.WriteLine($"CalibrationBoardTimeLineDot_PointerPressed button click press detected");

            if (sender is FrameworkElement element)
            {
                PointerPoint? pointerPoint = e.GetCurrentPoint(element);

                if (element.Tag is CanvasTag canvasTag && pointerPoint is not null)
                {
                    if (pointerPoint.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
                    {
                        // Calculate time difference from last click
                        TimeSpan timeSinceLastClick = DateTime.Now - lastClickTime;

                        // Check if it's a double-click (within a certain time threshold)
                        if (timeSinceLastClick.TotalMilliseconds < 500)
                        {
                            // It's a double-click
                            // Your double-click handling logic goes here
                            // Get the tag
                            if (canvasTag.IsTagType("Best"))
                            {
                                Debug.WriteLine($"CalibrationBoardTimeLineDot press implement code to go to frame:{canvasTag.ValueString}");
                            }

                            // Handle the event
                            e.Handled = true;
                        }
                        else
                        {
                            // Single click let other handlers pick it up
                            e.Handled = false;
                        }

                        // Update the last click time
                        lastClickTime = DateTime.Now;
                    }
                }
            }
        }



        ///
        /// Private
        ///

        /// <summary>
        /// 
        /// </summary>
        /// <param name="frameIndex"></param>
        /// <param name="reason"></param>
        private void DrawBestFrameDot(double x, int frameIndex, BestFrameReason reason)
        {
            Point center = new(x, 3);

            CanvasTag canvasTag = new("Best", "", $"F:{frameIndex}");


            // Check for SensorCoverage and PoseDiversity bits set
            if ((reason & BestFrameReason.SensorCoverage) != 0 && 
                (reason & BestFrameReason.PoseDiversity) != 0)
            {
                CanvasDrawingHelper.DrawCircle(MediaTimeLineDisplay,
                                            center,
                                            dotRadius,
                                            coverageAndPoseCircleStrokeColour,
                                            2.0/*strokeThickness*/,
                                            coverageAndPoseCircelFillColour,
                                            canvasTag,
                                            null/*pointerMoved*/,
                                            CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/);
            }
            else
            {
                // Check for SensorCoverage bit set
                if ((reason & BestFrameReason.SensorCoverage) != 0)
                {
                    CanvasDrawingHelper.DrawDot(MediaTimeLineDisplay,
                                                center, dotRadius * 2, coverageDotColour, canvasTag,
                                                null/*pointerMoved*/,
                                                CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/);
                }
                // Check for PoseDiversity bit set
                if ((reason & BestFrameReason.PoseDiversity) != 0)
                {
                    CanvasDrawingHelper.DrawDot(MediaTimeLineDisplay,
                                                center, dotRadius * 2, poseDotColour, canvasTag,
                                                null/*pointerMoved*/,
                                                CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/);
                }
            }
            // Check for Ignore frame bit set
            if ((reason & BestFrameReason.ManuallyIgnored) != 0)
            {
                CanvasDrawingHelper.DrawDot(MediaTimeLineDisplay,
                                            center, dotRadius * 2, manuallyIgnoredDotColour, canvasTag,
                                            null/*pointerMoved*/,
                                            CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/);
            }
            // Check for Added frame bit set
            if ((reason & BestFrameReason.ManuallyAdded) != 0)
            {
                CanvasDrawingHelper.DrawDot(MediaTimeLineDisplay,
                                            center, dotRadius * 2, manuallyAddedDotColour, canvasTag,
                                            null/*pointerMoved*/,
                                            CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/);
            }
        }

        /// <summary>
        /// Draw a filled rectangle 
        /// </summary>
        /// <param name="canvas"></param>
        private void DrawTimeLineBackground()
        {
            double w = MediaTimeLineDisplay.Width;
            double h = MediaTimeLineDisplay.Height;

            if (w <= 1 || h <= 1)
                return;

            double x0 = 0;
            double y0 = timeLineMargin;
            double x1 = w - 1;
            double y1 = h - timeLineMargin - 1;

            var border = new Polyline
            {
                Stroke = timeLimeBackground,
                Fill = timeLimeBackground,
                StrokeThickness = 1,
                Points =
                [
                    new Windows.Foundation.Point(x0, y0),
                    new Windows.Foundation.Point(x1, y0),
                    new Windows.Foundation.Point(x1, y1),
                    new Windows.Foundation.Point(x0, y1),
                    new Windows.Foundation.Point(x0, y0),
                ]
            };

            MediaTimeLineDisplay.Children.Add(border);
        }
    }
}
