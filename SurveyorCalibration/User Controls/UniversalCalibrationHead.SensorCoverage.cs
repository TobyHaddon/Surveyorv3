using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Surveyor.Calibration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;


namespace Surveyor.Controls
{
    public sealed partial class UniversalCalibrationHead : UserControl
    {
        private readonly Brush hullFillColour = new SolidColorBrush(Windows.UI.Color.FromArgb(12, 0, 120, 255));
        private readonly Brush coverageStrokeColour = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 120, 255)); // Blue 
        private readonly Brush poseStrokeColour = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 165, 0)); // Orange
        private readonly Brush borderStrokeColour = new SolidColorBrush(Colors.White);


        /// <summary>
        /// React to a change in the size of the sensor coverage canvas
        /// </summary>
        private void QueueRenderSensorCoverage()
        {
            if (_sensorCoverageRenderQueued)
                return;

            _sensorCoverageRenderQueued = true;

            // Run once at the end of the layout pass
            dispatcherQueue.TryEnqueue(() =>
            {
                _sensorCoverageRenderQueued = false;

                if (ViewModeCurrent == ViewMode.SensorCoverage)
                    RenderSensorCoverage();
            });
        }

        /// <summary>
        /// Render the best frame sensor coverage convex hulls
        /// </summary>
        private void RenderSensorCoverage()
        {
            // Guard
            if (headType is null)
                return;
            if (frameSize.Width <= 0 || frameSize.Height <= 0)
                return;

            // Get the best frame list for the current head type using the callback
            List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

            if (bestFramesList is not null)
            {
                LeftSensorCoverage.Children.Clear();
                RightSensorCoverage.Children.Clear();


                AlignCoverageCanvasToImage(LeftSensorCoverage, LeftOverlayContainer, LeftImage);
                DrawCanvasBorder(LeftSensorCoverage);

                AlignCoverageCanvasToImage(RightSensorCoverage, RightOverlayContainer, RightImage);
                DrawCanvasBorder(RightSensorCoverage);

                // Nothing to render without best frames
                if (bestFramesList.Count == 0)
                    return;

                RenderSensorCoverageSide(LeftSensorCoverage,
                                         LeftOverlayContainer,
                                         trueLeftFalseRight: true);

                if (IsHeadStereo())
                {
                    RenderSensorCoverageSide(RightSensorCoverage,
                                             RightOverlayContainer,
                                             trueLeftFalseRight: false);
                }
            }
        }


        /// <summary>
        /// Renders the convex hulls representing sensor coverage areas onto the specified canvas, overlaying them on
        /// the provided image for either the left or right sensor view.
        /// </summary>
        /// <remarks>This method overlays translucent polygons to visualize the density and extent of
        /// sensor coverage, based on calibration data. The polygons are mapped to the image control using uniform
        /// scaling to ensure correct alignment. No drawing occurs if the image has zero width or height.</remarks>
        /// <param name="canvas">The canvas on which the sensor coverage polygons are drawn.</param>
        /// <param name="image">The image control used to determine the coordinate mapping for overlaying the coverage polygons.</param>
        /// <param name="trueLeftFalseRight">If <see langword="true"/>, renders coverage for the left sensor; otherwise, renders coverage for the right
        /// sensor.</param>
        private void RenderSensorCoverageSide(Canvas canvas, FrameworkElement overlayContainer, bool trueLeftFalseRight)
        {
            // Guard
            if (headType is null)
                return;
            if (overlayContainer.ActualWidth <= 0 || overlayContainer.ActualHeight <= 0)
                return;

            // Compute how the video frame maps into the Image control (Stretch=Uniform)
            (double scale, _, _) = ComputeUniformImageMapping(frameSize.Width,
                                                              frameSize.Height,
                                                              overlayContainer.ActualWidth,
                                                              overlayContainer.ActualHeight);

            // Get the best frame list for the current head type using the callback
            List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

            if (bestFramesList is not null)
            {
                // Draw each best frame hull with low alpha (overlaps visualize coverage density)
                foreach (BestFrame bestFrame in bestFramesList)
                {
                    int frameSetIndex = bestFrame.FrameIndex;

                    if (!calibrationStereoFrameSet.Data.Frames.TryGetValue(frameSetIndex, out var tuple))
                        continue;

                    FrameData? fd = trueLeftFalseRight ? tuple.frameCalibrationTargetLeft : tuple.frameCalibrationTargetRight;
                    if (fd is null || fd.ChArUcoCorners is null || fd.ChArUcoCorners.Length < 3)
                        continue;

                    List<PointF> hull = ComputeConvexHull(fd.ChArUcoCorners);
                    if (hull.Count < 3)
                        continue;

                    PointCollection points = [];
                    foreach (PointF p in hull)
                    {
                        double x = -1;
                        double y = -1;

                        try
                        {

                            // Map from frame pixels -> Image control coordinates
                            x = p.X * scale;
                            y = p.Y * scale;
                            points.Add(new Windows.Foundation.Point(x, y));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"RenderSensorCoverageSide: Failed to add Point:({p.X},{p.Y}) scaled to:({x},{y}), {ex.Message}");
                        }
                    }

                    // Base fill (keep existing)
                    Brush fillBrush = hullFillColour;

                    bool isCoverage = (bestFrame.Reason & BestFrameReason.SensorCoverage) != 0;
                    bool isPose = (bestFrame.Reason & BestFrameReason.PoseDiversity) != 0;

                    if (isCoverage && isPose)
                    {
                        PointCollection pointsCoverage = points;
                        PointCollection pointsPose = Clone(points);

                        try
                        {
                            // Both: draw two outlines for a clear combined meaning.
                            canvas.Children.Add(new Polygon
                            {
                                Points = pointsCoverage,
                                Fill = fillBrush,
                                Stroke = coverageStrokeColour,
                                StrokeThickness = 2
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"RenderSensorCoverageSide: Failed draw Poly-line outer coverage border before drawing pose border, {ex.Message}");
                        }

                        try
                        {
                            canvas.Children.Add(new Polygon
                            {
                                Points = pointsPose,
                                Fill = null,
                                Stroke = poseStrokeColour,
                                StrokeThickness = 1
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"RenderSensorCoverageSide: Failed draw Poly-line outer pose border after drawing coverage border, {ex.Message}");
                        }
                    }
                    else if (isPose)
                    {
                        try
                        {
                            canvas.Children.Add(new Polygon
                            {
                                Points = points,
                                Fill = fillBrush,
                                Stroke = poseStrokeColour,
                                StrokeThickness = 1
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"RenderSensorCoverageSide: Failed draw Poly-line pose border, {ex.Message}");
                        }
                    }
                    else
                    {
                        try
                        {
                            // Default to SensorCoverage styling (also covers Reason=None)
                            canvas.Children.Add(new Polygon
                            {
                                Points = points,
                                Fill = fillBrush,
                                Stroke = coverageStrokeColour,
                                StrokeThickness = 1
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"RenderSensorCoverageSide: Failed draw Poly-line coverage border, {ex.Message}");
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Clone a PointCollection
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        private static PointCollection Clone(PointCollection points)
        {
            PointCollection clone = [];
            foreach (Windows.Foundation.Point p in points)
                clone.Add(p);

            return clone;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="framePixelWidth"></param>
        /// <param name="framePixelHeight"></param>
        /// <param name="controlWidth"></param>
        /// <param name="controlHeight"></param>
        /// <returns></returns>
        private static (double scale, double offsetX, double offsetY) ComputeUniformImageMapping(
            double framePixelWidth,
            double framePixelHeight,
            double controlWidth,
            double controlHeight)
        {
            // Stretch=Uniform: scale is min of width/height scales
            double scaleX = controlWidth / framePixelWidth;
            double scaleY = controlHeight / framePixelHeight;
            double scale = Math.Min(scaleX, scaleY);

            double displayedWidth = framePixelWidth * scale;
            double displayedHeight = framePixelHeight * scale;

            // Image is centered horizontally and aligned top vertically in XAML,
            // but in practice it’s inside a Grid; handle both by computing offsets.
            double offsetX = (controlWidth - displayedWidth) / 2.0;

            // Image is VerticalAlignment="Top" in XAML
            double offsetY = 0.0;
            //???double offsetY = (controlHeight - displayedHeight) / 2.0;

            return (scale, offsetX, offsetY);
        }


        /// <summary>
        /// Compute the convex hull of a set of 2D points using Andrew's monotone chain algorithm.
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        private static List<PointF> ComputeConvexHull(PointF[] points)
        {
            // Monotonic chain / Andrew’s algorithm
            var pts = points
                .DistinctBy(p => (p.X, p.Y))
                .OrderBy(p => p.X)
                .ThenBy(p => p.Y)
                .ToList();

            if (pts.Count <= 2)
                return pts;

            static float Cross(PointF o, PointF a, PointF b)
                => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

            var lower = new List<PointF>();
            foreach (var p in pts)
            {
                while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }

            var upper = new List<PointF>();
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                var p = pts[i];
                while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }

            // Concatenate without duplicating endpoints
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);

            return lower;
        }


        /// <summary>
        /// Draw a border on the edge of the Canvas
        /// </summary>
        /// <param name="canvas"></param>
        private void DrawCanvasBorder(Canvas canvas)
        {
            double w = canvas.Width;
            double h = canvas.Height;

            if (w <= 1 || h <= 1)
                return;

            double x0 = 0;
            double y0 = 0;
            double x1 = w - 1;
            double y1 = h - 1;

            var border = new Polyline
            {
                Stroke = borderStrokeColour,
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

            canvas.Children.Add(border);
        }

        private void AlignCoverageCanvasToImage(Canvas canvas, FrameworkElement overlayContainer, Image image)
        {
            if (frameSize.Width <= 0 || frameSize.Height <= 0)
                return;

            if (overlayContainer.ActualWidth <= 0 || overlayContainer.ActualHeight <= 0)
                return;

            // Compute mapping for the rendered bitmap inside the *overlayContainer*
            (double scale, double offsetX, double offsetY) = ComputeUniformImageMapping(
                framePixelWidth: frameSize.Width,
                framePixelHeight: frameSize.Height,
                controlWidth: overlayContainer.ActualWidth,
                controlHeight: overlayContainer.ActualHeight);

            // Account for the Image's margin since it shifts the rendered content
            Thickness imageMargin = image.Margin;

            double displayedWidth = frameSize.Width * scale;
            double displayedHeight = frameSize.Height * scale;

            canvas.Width = displayedWidth;
            canvas.Height = displayedHeight;

            canvas.Margin = new Thickness(
                left: offsetX + imageMargin.Left,
                top: offsetY + imageMargin.Top,
                right: 0,
                bottom: 0);
        }

        private static void TraceCanvasState(string label, Canvas canvas)
        {
            string bg = "null";
            if (canvas.Background is SolidColorBrush scb)
            {
                Windows.UI.Color c = scb.Color;
                bg = $"SolidColorBrush(A={c.A}, R={c.R}, G={c.G}, B={c.B})";
            }
            else if (canvas.Background is not null)
            {
                bg = canvas.Background.GetType().Name;
            }

            Debug.WriteLine(
                $"[{label}] " +
                $"Visibility={canvas.Visibility}, " +
                $"Opacity={canvas.Opacity:F2}, " +
                $"IsHitTestVisible={canvas.IsHitTestVisible}, " +
                $"W={canvas.Width}, H={canvas.Height}, " +
                $"AW={canvas.ActualWidth:F1}, AH={canvas.ActualHeight:F1}, " +
                $"Margin=({canvas.Margin.Left:F1},{canvas.Margin.Top:F1},{canvas.Margin.Right:F1},{canvas.Margin.Bottom:F1}), " +
                $"BG={bg}, " +
                $"Children={canvas.Children.Count}");
        }
    }
}

