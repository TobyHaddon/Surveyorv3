// Display a timeline of the media that indicates the range the calibration board
// has detected in and can display a dot for each of the 'best frame' these are
// frames used in the calibration calculation
// The user control also allows for a delegate to set to be called is the user
// double clicks on any of the frame dots
//
// Version 1.0
// Version 1.1 BestFrameFoundAt and RenderBestFramesOnTimeline added
// Version 1.2 04 Feb 2026  Tidied up

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
        private const int timeLineHeight = 9;  // Should be same as the Height of the Canvas in XAML
        private const int timeLineMargin = 2;
        
        private const int boardFoundAtWidth = 1;
        private const double dotRadius = timeLineHeight / 2.0;

        // Head type (used to help make the debug messages make sense
        private UniversalCalibrationHead.HeadType? headType = null;

        // Media range
        private int _startMediaFrameIndex = -1;
        private int _endMediaFrameIndex = -1;

        // Calibration Board observed at range
        private int _calibrationBoardStartFrameIndex = -1;
        private int _calibrationBoardEndFrameIndex = -1;

        // Calibration Board found at
        public sealed record BoardFoundAt(int FrameIndex, bool trueFoundFalseNotFound);
        private List<BoardFoundAt> _CalibrationBoardFoundAt = [];

        // Best frames
        private List<BestFrame> _bestFrames = [];

        // Colors
        private readonly Brush timeLimeBackgroundStrokeColour = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        private readonly Brush timeLimeBackgroundFillColour = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        private readonly Brush timeLimeBackground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        private readonly Brush manuallyAddedDotColour = new SolidColorBrush(Microsoft.UI.Colors.Green);
        private readonly Brush manuallyIgnoredDotColour = new SolidColorBrush(Microsoft.UI.Colors.Red);
        private readonly Brush coverageDotColour = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 71, 163)); // solid darker blue
        private readonly Brush poseDotColour = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 199, 106, 0)); // darker orange
        private readonly Brush depthDotColour = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 0, 128)); // purple
        private readonly Brush blackDotColour = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)); // black
        //???private readonly Brush coverageAndPoseCircleStrokeColour = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 71, 163)); // solid darker blue 
        private readonly Brush coverageAndPoseCircelFillColour = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 199, 106, 0)); // darker orange

        // Expose a callback for requesting a jump to a specific frame index.
        // Return true if the request was handled.
        public delegate bool JumpToFrameRequestedHandler(int frameIndex);

        public event JumpToFrameRequestedHandler? JumpToFrameRequested;

        public MediaTimeLineDisplayUserControl()
        {
            this.InitializeComponent();
        }


        /// <summary>
        /// Set the head type that owns this control
        /// Used to help give clarity to the debug messages
        /// </summary>
        /// <param name="_headType"></param>
        public void SetHeadType(UniversalCalibrationHead.HeadType _headType)
        {
            headType = _headType;
        }

        public void Clear()
        {
            Debug.WriteLine($"{headType} MediaTimeLineDisplayUserControl.Clear");
            _startMediaFrameIndex = -1;
            _endMediaFrameIndex = -1;
        
            _calibrationBoardStartFrameIndex = -1;
            _calibrationBoardEndFrameIndex = -1;

            _CalibrationBoardFoundAt = [];

            _bestFrames = [];

            MediaTimeLineDisplay.Children.Clear();
            DrawTimeLineBackground();
        }


        /// <summary>
        /// Set the media frame range for the timeline. This will clear any existing timeline 
        /// and set up a new one with the specified range. The range is defined by the start 
        /// and end media frame indexes. The timeline will be drawn to represent this range, 
        /// and any subsequent calls to indicate calibration board detection or best frames 
        /// should be within this range.
        /// The range is the first and last frame index i.e. the first is normally zero
        /// </summary>
        /// <param name="_startMediaFrameIndex"></param>
        /// <param name="_endMediaFrameIndex"></param>
        public void SetRange(int startMediaFrameIndex, int endMediaFrameIndex, bool clearData)
        {
            Debug.WriteLine($"{headType} MediaTimeLineDisplayUserControl.SetRange({startMediaFrameIndex}, {endMediaFrameIndex}, clearData={clearData})");
            if (clearData)
                Clear();

            _startMediaFrameIndex = startMediaFrameIndex;
            _endMediaFrameIndex = endMediaFrameIndex;            
        }


        /// <summary>
        /// This method is used to show that a calibration boards was detected 
        /// in the indicate frame and is drawn on the timeline is in green.
        /// This is used in the process that is discovering the calibration board 
        /// real-time to display progress.
        /// This is normal before the calibration board range is fully known and 
        /// typically overlays the CalibrationBoardRange() when it is known.
        /// </summary>
        /// <param name="frameIndex"></param>
        public void CalibrationBoardFoundAt(int frameIndex, bool trueFoundFalseNotFound)
        {
            if (frameIndex < _startMediaFrameIndex || frameIndex > _endMediaFrameIndex)
            {
                Debug.WriteLine($"{headType} MediaTimeLineDisplayUserControl.CalibrationBoardFoundAt({frameIndex}, trueFoundFalseNotFound={trueFoundFalseNotFound}), out of range, media Start={_startMediaFrameIndex}, end={_endMediaFrameIndex}");
                return; // Ignore out-of-range
            }

            // Remember in-case of a resizing/redraw
            _CalibrationBoardFoundAt.Add(new BoardFoundAt(frameIndex, trueFoundFalseNotFound));

            DrawBoardFoundAt(frameIndex, trueFoundFalseNotFound);
        }


        /// <summary>
        /// This method is called when the calibration board range is fully known.
        /// </summary>
        /// <param name="calilbrationBoardStartframeIndex"></param>
        /// <param name="calilbrationBoardEndframeIndex"></param>
        public void CalibrationBoardRange(int calibrationBoardStartFrameIndex, int calibrationBoardEndFrameIndex)
        {
            // Guard
            if (calibrationBoardStartFrameIndex < _startMediaFrameIndex || calibrationBoardEndFrameIndex > _endMediaFrameIndex)
                return; // Ignore out-of-range

            // Clear all and setup the background again
            SetRange(_startMediaFrameIndex, _endMediaFrameIndex, clearData: false);

            // Remember the range
            _calibrationBoardStartFrameIndex = calibrationBoardStartFrameIndex;
            _calibrationBoardEndFrameIndex = calibrationBoardEndFrameIndex;

            double startX = MapFrameIndexToTimelineX(calibrationBoardStartFrameIndex);
            double endX = MapFrameIndexToTimelineX(calibrationBoardEndFrameIndex);

            double w = Math.Max(0, endX - startX);
            if (w <= 0)
            {
                return;
            }

            // Inset inside the background rectangle stroke.
            // Background stroke is 1px and drawn inside the canvas; the red fill spans y=3..5.
            // Use an inset of 1px from the interior edges.
            const double inset = 1.0;

            double top = timeLineMargin + inset; // 2 + 1 = 3
            double height = (timeLineHeight - (timeLineMargin * 2)) - (inset * 2); // 5 - 2 = 3
            if (height <= 0)
            {
                return;
            }

            // Also inset horizontally by 1px so it stays off the left/right stroke.
            double left = startX + inset;
            double width = Math.Max(0, w - (inset * 2));
            if (width <= 0)
            {
                return;
            }

            var rectangle = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
            };

            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);
            MediaTimeLineDisplay.Children.Add(rectangle);
        }


        /// <summary>
        /// This method is called to indicate a best calibration board frame (it is called once
        /// for each best frame). It typically overlays the CalibrationBoardRange().
        /// This method will safely remove any existing BestFrame instance for the
        /// same FrameIndex
        /// </summary>
        /// <param name="frameIndex"></param>
        public void RenderBestFrameFoundAtOnTimeline(BestFrame bestFrame)
        {
            // Guard
            if (bestFrame.FrameIndex < _startMediaFrameIndex || bestFrame.FrameIndex > _endMediaFrameIndex)
                return; // Ignore out-of-range

            // Remove any instance of bestFrame.FrameIndex from the
            // media timeline display
            RemoveBestFrameFoundAt(bestFrame.FrameIndex);

            // Remove any instance of bestFrame.FrameIndex from the
            // _bestFrame List<>
            _bestFrames.RemoveAll(bf => bf.FrameIndex == bestFrame.FrameIndex);

            // Add the new BestFrame to the master list
            _bestFrames.Add(new (bestFrame.FrameIndex, bestFrame.Reason));

            // Update the UI
            DrawBestFrameDot(bestFrame.FrameIndex, bestFrame.Reason);            
        }


        /// <summary>
        /// Draw all the best frames on the timeline
        /// </summary>
        /// <param name="BestFrameIndexes"></param>
        public void RenderBestFramesOnTimeline(List<BestFrame> BestFrameIndexes)
        {
            //???Debug.WriteLine($"{headType} RenderBestFramesOnTimeline, count={BestFrameIndexes.Count}");

            // Remember the best frame in case of resize/redraw
            foreach (BestFrame bestFrame in BestFrameIndexes)
            {
                _bestFrames.Add(new(bestFrame.FrameIndex, bestFrame.Reason));
            }

            DrawBestFrames();
        }


        /// <summary>
        /// Remove (if found) the BestFrame instance for the
        /// passed FrameIndex from the media timeline display
        /// Note. Any record in the _bestFrame List<> needs to 
        /// be removed separately
        /// </summary>
        /// <param name="FrameIndex"></param>
        public void RemoveBestFrameFoundAt(int frameIndex)
        {
            bool exists = _bestFrames.Any(bf => bf.FrameIndex == frameIndex);
            if (exists)
            {
                CanvasTag canvasTag = new("Best", $"F:{frameIndex}", "");

                CanvasDrawingHelper.RemoveCanvasShapesByTag(MediaTimeLineDisplay, canvasTag);
            }
        }

        /// <summary>
        /// Remove all the best frame indicator dots from the timeline
        /// </summary>
        public void RemoveAllBestFrames()
        {
            //???Debug.WriteLine($"{headType} RemoveAllBestFrames");
            CanvasDrawingHelper.RemoveCanvasShapesByTag(MediaTimeLineDisplay, "Best");
        }


        /// <summary>
        /// Set the tool tip help for a new empty project
        /// </summary>
        public void SetToolTipNewProject()
        {
            ToolTipService.SetToolTip(MediaTimeLineDisplay, (string)Resources["MediaTimeLineToolTipNewProject"]);
        }


        /// <summary>
        /// Set the tool tip help for a project where the best frames have already been found
        /// </summary>
        public void SetToolTipLoadedProject()
        {
            ToolTipService.SetToolTip(MediaTimeLineDisplay, (string)Resources["MediaTimeLineToolTipLoadedProject"]);
        }



        ///
        /// Events
        ///

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
        /// Size of the MediaTimeLineDisplay has change. We need to completely redraw
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaTimeLineDisplay_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //???Debug.WriteLine($"MediaTimeLineDisplay_SizeChanged");
            if (e.PreviousSize.Width == e.NewSize.Width)
            {
                return;
            }

            if (_startMediaFrameIndex < 0 || _endMediaFrameIndex < _startMediaFrameIndex)
            {
                return;
            }

            if (this.Visibility == Visibility.Visible)
            {
                MediaTimeLineDisplay.Children.Clear();
                DrawTimeLineBackground();

                if (_calibrationBoardStartFrameIndex >= 0 && _calibrationBoardEndFrameIndex >= _calibrationBoardStartFrameIndex)
                {
                    CalibrationBoardRange(_calibrationBoardStartFrameIndex, _calibrationBoardEndFrameIndex);
                }

                if (_CalibrationBoardFoundAt.Count > 0)
                {
                    foreach (BoardFoundAt boardFoundAt in _CalibrationBoardFoundAt)
                        DrawBoardFoundAt(boardFoundAt.FrameIndex, boardFoundAt.trueFoundFalseNotFound);
                }

                if (_bestFrames.Count > 0)
                {
                    DrawBestFrames();
                }
            }
        }


        /// <summary>
        /// User clicked on a dot on the timeline
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private DateTime lastClickTime = DateTime.MinValue;
        private void CalibrationBoardTimeLineDot_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
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
                            if (canvasTag.IsTagType("Best") && canvasTag.ValueString is not null)
                            {
                                // Safely extract the frame index from the tag  format is "F:n"
                                if (int.TryParse(canvasTag.TagSubType.AsSpan(2), out int frameIndex))
                                {
                                    Debug.WriteLine($"{headType} CalibrationBoardTimeLineDot press implement code to go to frame:{frameIndex}");

                                    // Is setup request jump to the frame
                                    bool handled = JumpToFrameRequested?.Invoke(frameIndex) == true;
                                }
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
        /// <param name="trueFoundFalseNotFound"></param>
        private void DrawBoardFoundAt(int frameIndex, bool trueFoundFalseNotFound)
        {
            double x = MapFrameIndexToTimelineX(frameIndex);

            Windows.UI.Color color;

            if (trueFoundFalseNotFound)
                color = Microsoft.UI.Colors.LightGreen;
            else
                color = Microsoft.UI.Colors.Black;

            var line = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = boardFoundAtWidth,
                Height = timeLineHeight - (timeLineMargin * 2),
                Fill = new SolidColorBrush(color), // or use Colors.Green
            };

            // Position it on the Canvas
            Canvas.SetLeft(line, x);
            Canvas.SetTop(line, timeLineMargin);

            MediaTimeLineDisplay.Children.Add(line);
        }


        /// <summary>
        /// Draw the best frames dots on the timeline from the cached
        /// best frames list
        /// </summary>
        public void DrawBestFrames()
        {
            // Remove any existing indicator dots
            RemoveAllBestFrames();

            if (_bestFrames.Count > 100)
                Debug.WriteLine(">100");

            // Iterate over BestFrameIndexes
            foreach (BestFrame bestFrame in _bestFrames)
            {
                DrawBestFrameDot(bestFrame.FrameIndex, bestFrame.Reason);
            }
        }

        /// <summary>
        /// Draw a best frame dot on the timeline
        /// </summary>
        /// <param name="frameIndex"></param>
        /// <param name="reason"></param>
        private void DrawBestFrameDot(int frameIndex, BestFrameReason reason)
        {
            string toolTip;
            double x = MapFrameIndexToTimelineX(frameIndex);
            
            Point center = new(x, timeLineMargin + ((timeLineHeight - (timeLineMargin * 2)) / 2.0));
            
            CanvasTag canvasTag = new("Best", $"F:{frameIndex}", "");

            // Check for SensorCoverage, PoseDiversity & DepthDiversity bits set
            if ((reason & BestFrameReason.SensorCoverage) != 0 &&
                (reason & BestFrameReason.PoseDiversity) != 0 &&
                (reason & BestFrameReason.DepthDiversity) != 0)
            {
                toolTip = $"Frame added for calibration due to the calibration board's position in the image, it's pose an depth. \nThe frame index is {frameIndex}. Double click to go to this frame.";
                CanvasDrawingHelper.DrawCircle(MediaTimeLineDisplay,
                                            center,
                                            dotRadius,
                                            coverageDotColour,  // Used as the outline color
                                            2.0/*strokeThickness*/,
                                            blackDotColour,      // Used as the fill color
                                            canvasTag,
                                            null/*pointerMoved*/,
                                            CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/,
                                            toolTip);
            }
            // Check for SensorCoverage and PoseDiversity bits set
            else if ((reason & BestFrameReason.SensorCoverage) != 0 && 
                (reason & BestFrameReason.PoseDiversity) != 0)
            {
                toolTip = $"Frame added for calibration due to the calibration board's position in the image and it's pose. \nThe frame index is {frameIndex}. Double click to go to this frame.";
                CanvasDrawingHelper.DrawCircle(MediaTimeLineDisplay,
                                            center,
                                            dotRadius,
                                            coverageDotColour,  // Used as the outline color
                                            2.0/*strokeThickness*/,
                                            poseDotColour,      // Used as the fill color
                                            canvasTag,
                                            null/*pointerMoved*/,
                                            CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/,
                                            toolTip);
            }
            // Check for SensorCoverage and DepthDiversity bits set
            else if ((reason & BestFrameReason.SensorCoverage) != 0 &&
                     (reason & BestFrameReason.DepthDiversity) != 0)
            {
                toolTip = $"Frame added for calibration due to the calibration board's position in the image and it's depth. \nThe frame index is {frameIndex}. Double click to go to this frame.";
                CanvasDrawingHelper.DrawCircle(MediaTimeLineDisplay,
                                            center,
                                            dotRadius,
                                            coverageDotColour,  // Used as the outline color
                                            2.0/*strokeThickness*/,
                                            depthDotColour,     // Used as the fill color
                                            canvasTag,
                                            null/*pointerMoved*/,
                                            CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/,
                                            toolTip);
            }
            // Check for PoseDiversity and DepthDiversity bits set
            else if ((reason & BestFrameReason.PoseDiversity) != 0 &&
                     (reason & BestFrameReason.DepthDiversity) != 0)
            {
                toolTip = $"Frame added for calibration due to the calibration board's pose in the image and it's depth. \nThe frame index is {frameIndex}. Double click to go to this frame.";
                CanvasDrawingHelper.DrawCircle(MediaTimeLineDisplay,
                                            center,
                                            dotRadius,
                                            poseDotColour,  // Used as the outline color
                                            2.0/*strokeThickness*/,
                                            depthDotColour,     // Used as the fill color
                                            canvasTag,
                                            null/*pointerMoved*/,
                                            CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/,
                                            toolTip);
            }
            else
            {
                // Check for SensorCoverage bit set
                if ((reason & BestFrameReason.SensorCoverage) != 0)
                {
                    toolTip = $"Frame added for calibration due to the calibration board position in the image. \nThe frame index is {frameIndex}. Double click to go to this frame.";
                    CanvasDrawingHelper.DrawDot(MediaTimeLineDisplay,
                                                center, dotRadius * 2, coverageDotColour, canvasTag,
                                                null/*pointerMoved*/,
                                                CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/,
                                                toolTip);
                }
                // Check for PoseDiversity bit set
                else if ((reason & BestFrameReason.PoseDiversity) != 0)
                {
                    toolTip = $"Frame added for calibration due to the calibration board pose in the image. \nThe frame index is {frameIndex}. Double click to go to this frame.";
                    CanvasDrawingHelper.DrawDot(MediaTimeLineDisplay,
                                                center, dotRadius * 2, poseDotColour, canvasTag,
                                                null/*pointerMoved*/,
                                                CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/,
                                                toolTip);
                }
                // Check for DepthDiversity bit set
                else if ((reason & BestFrameReason.DepthDiversity) != 0)
                {
                    toolTip = $"Frame added for calibration due to the calibration board depth in the image. \nThe frame index is {frameIndex}. Double click to go to this frame.";
                    CanvasDrawingHelper.DrawDot(MediaTimeLineDisplay,
                                                center, dotRadius * 2, depthDotColour, canvasTag,
                                                null/*pointerMoved*/,
                                                CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/,
                                                toolTip);
                }
            }

            // Check for Ignore frame bit set
            if ((reason & BestFrameReason.ManuallyIgnored) != 0)
            {
                toolTip = $"Frame manually ignored. \nThe frame index is {frameIndex}. Double click to go to this frame.";

                CanvasDrawingHelper.DrawDiamond(MediaTimeLineDisplay,
                                               center, dotRadius * 2, manuallyIgnoredDotColour, canvasTag,
                                               null/*pointerMoved*/,
                                               CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/, toolTip);
            }
            // Check for Added frame bit set
            else if ((reason & BestFrameReason.ManuallyAdded) != 0)
            {
                toolTip = $"Frame manually added. \nThe frame index is {frameIndex}. Double click to go to this frame.";

                CanvasDrawingHelper.DrawDiamond(MediaTimeLineDisplay,
                                                center, dotRadius * 2, manuallyAddedDotColour, canvasTag,
                                                null/*pointerMoved*/,
                                                CalibrationBoardTimeLineDot_PointerPressed /*pointerPressed*/, toolTip);
            }
        }


        /// <summary>
        /// Draw a filled rectangle 
        /// </summary>
        /// <param name="canvas"></param>
        private void DrawTimeLineBackground()
        {
            double w = double.IsNaN(MediaTimeLineDisplay.Width) ? MediaTimeLineDisplay.ActualWidth : MediaTimeLineDisplay.Width;
            double h = double.IsNaN(MediaTimeLineDisplay.Height) ? MediaTimeLineDisplay.ActualHeight : MediaTimeLineDisplay.Height;

            if (w <= 1 || h <= 1)
            {
                return;
            }

            // Draw a 1px stroke fully inside the canvas using half-pixel alignment.
            const double strokeThickness = 1.0;

            double left = 0.5;
            double top = timeLineMargin + 0.5;
            double width = Math.Max(0, w - strokeThickness);
            double height = Math.Max(0, h - (2 * timeLineMargin) - strokeThickness);

            var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Stroke = timeLimeBackgroundStrokeColour,
                Fill = timeLimeBackgroundFillColour,
                StrokeThickness = strokeThickness,
            };

            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);

            MediaTimeLineDisplay.Children.Add(rect);
        }


        /// <summary>
        /// Maps a media frame index to its corresponding X coordinate on the timeline display.
        /// </summary>
        /// <remarks>The returned X coordinate corresponds to the left-most pixel of the timeline's
        /// background fill area. If the timeline width is less than or equal to 2, or if the media frame range is
        /// invalid, the method returns 0.0.</remarks>
        /// <param name="frameIndex">The index of the media frame to map. Values outside the valid media frame range are clamped to the nearest
        /// boundary.</param>
        /// <returns>The X coordinate, in device-independent pixels, representing the position of the specified frame on the
        /// timeline. Returns 0.0 if the timeline or frame range is invalid.</returns>
        private double MapFrameIndexToTimelineX(int frameIndex)
        {
            if (_startMediaFrameIndex < 0 || _endMediaFrameIndex < _startMediaFrameIndex)
            {
                return 0.0;
            }

            // Clamp to the known media range.
            int clampedFrameIndex = Math.Clamp(frameIndex, _startMediaFrameIndex, _endMediaFrameIndex);

            // X coordinate of the red background fill's left-most pixel.
            // Background rectangle is positioned at x=0.5 with width=(w - 1) and StrokeThickness=1,
            // so the fill starts at x=1 and ends at x=(w - 2) inclusive.
            double w = double.IsNaN(MediaTimeLineDisplay.Width) ? MediaTimeLineDisplay.ActualWidth : MediaTimeLineDisplay.Width;
            if (w <= 2)
            {
                return 0.0;
            }

            double xLeftPixel = 1.0;
            double xRightPixel = w - 2.0;

            int range = _endMediaFrameIndex - _startMediaFrameIndex;
            if (range == 0)
            {
                return xLeftPixel;
            }

            double t = (double)(clampedFrameIndex - _startMediaFrameIndex) / range;
            return xLeftPixel + (t * (xRightPixel - xLeftPixel));
        }
    }
}
