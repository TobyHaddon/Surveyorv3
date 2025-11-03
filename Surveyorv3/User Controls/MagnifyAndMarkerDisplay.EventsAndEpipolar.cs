// MagnifyAndMarkerDisplay.EventsAndEpipolar.cs
// Extension to the main class to handle the drawing of events and epipolar lines
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Surveyor.Events;
using Surveyor.Helper;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Windows.Foundation;


namespace Surveyor.User_Controls
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MagnifyAndMarkerDisplay : UserControl
    {
        private bool? hoveringOverMeasurementEnd = null;
        private bool? hoveringOverMeasurementLine = null;
        private bool? hoveringOverPoint = null;
        private bool? hoveringOverDetails = null;
        private Guid? hoveringOverGuid = null;

        // Remembered Epipolar line
        private bool epipolarLineTargetActiveA = false;
        private bool epipolarLineTargetActiveB = false;

        // Remember Epipolar Curve Distorted Points
        private List<Point>? epipolarCurveDistortedPointsTargetA = null;
        private List<Point>? epipolarCurveDistortedPointsTargetB = null;
       

        private void ClearEventsAndEpipolar()
        {
            hoveringOverMeasurementEnd = null;
            hoveringOverMeasurementLine = null;
            hoveringOverPoint = null;
            hoveringOverDetails = null;
            hoveringOverGuid = null;

        
            epipolarLineTargetActiveA = false;
            epipolarLineTargetActiveB = false;

            // Clear epipolar lines from the CanvasFrame and CanvasMag
            epipolarCurveDistortedPointsTargetA = null;
            epipolarCurveDistortedPointsTargetB = null;
        }

        /// <summary>
        /// Make the species string depending on the display layers enabled, the species name
        /// and the fish count
        /// </summary>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        private string MakeSpeciesText(SpeciesInfo speciesInfo)
        {
            string fishID = string.Empty;

            if ((layerTypesDisplayed & LayerType.EventsDetail) != 0)
            {
                if (int.TryParse(speciesInfo.Number, out int count) &&
                    count > 1)
                {
                    if (!string.IsNullOrEmpty(speciesInfo.Species))
                        fishID = $"{speciesInfo.Number} x {speciesInfo.Species}";
                    else if (!string.IsNullOrEmpty(speciesInfo.Genus))
                        fishID = $"{speciesInfo.Number} x {speciesInfo.Genus}";
                    else if (!string.IsNullOrEmpty(speciesInfo.Family))
                        fishID = $"{speciesInfo.Number} x {speciesInfo.Family}";
                }
                else
                {
                    if (!string.IsNullOrEmpty(speciesInfo.Species))
                        fishID = speciesInfo.Species;
                    else if (!string.IsNullOrEmpty(speciesInfo.Genus))
                        fishID = speciesInfo.Genus;
                    else if (!string.IsNullOrEmpty(speciesInfo.Family))
                        fishID = speciesInfo.Family;
                }
            }

            return fishID;
        }

        /// <summary>
        /// Draws a StereoMeasurementPoints event on the CanvasFrame
        /// </summary>
        /// <param name="guid"></param>
        /// <param name="pointA"></param>
        /// <param name="pointB"></param>
        /// <param name="speciesInfo"></param>
        /// <param name="distance"></param>
        private void DrawEventStereoMeasurementPoints(Guid guid, Point pointA, Point pointB, SpeciesInfo speciesInfo, double distance)
        {
            // Create CanvasTag for the event. This is so the Canvas child object can be identified
            CanvasTag canvasTagDimensionEnd = new("Event", "DimensionEnd", guid);
            CanvasTag canvasTagDimensionLine = new("Event", "DimensionLine", guid);
            CanvasTag canvasTagDetails = new("Event", "Details", guid);

            // Calculate offset for parallel lines
            Vector2 direction = new((float)(pointB.X - pointA.X), (float)(pointB.Y - pointA.Y));
            Vector2 perp = new(-direction.Y, direction.X);
            perp = Vector2.Normalize(perp);

            // Calculate the angle of the line in degrees
            double angleRadians = Math.Atan2(direction.Y, direction.X);

            // Check if the angle is 0 - 180
            bool TrueIfTextBestAboveFalseIfBelow = (angleRadians >= 0) && (angleRadians <= Math.PI);

            // Caculate the offset for the dimension line either above or below
            double offset = (20 * canvasScaleFactor) * (TrueIfTextBestAboveFalseIfBelow ? -1 : 1);

            // Parallel line 1
            Point p1Start = new(pointA.X, pointA.Y);
            Point p1End = new(pointA.X + (offset * perp.X), pointA.Y + (offset * perp.Y));

            CanvasDrawingHelper.DrawLine(CanvasFrame, p1Start, p1End, eventDimensionLineColour, canvasTagDimensionEnd, EventElement_PointerMoved, EventElement_PointerPressed);

            // Parallel line 2
            Point p2Start = new(pointB.X, pointB.Y);
            Point p2End = new(pointB.X + (offset * perp.X), pointB.Y + (offset * perp.Y));
            CanvasDrawingHelper.DrawLine(CanvasFrame, p2Start, p2End, eventDimensionLineColour, canvasTagDimensionEnd, EventElement_PointerMoved, EventElement_PointerPressed);

            // Draw dimension line
            Point dimPoint1 = new(pointA.X + (offset * perp.X * 0.80), pointA.Y + (offset * perp.Y * 0.80));
            Point dimPoint2 = new(pointB.X + (offset * perp.X * 0.80), pointB.Y + (offset * perp.Y * 0.80));
            CanvasDrawingHelper.DrawLineWithArrowHeads(CanvasFrame, dimPoint1, dimPoint2, 10/*arrow length*/, eventArrowLineColour, canvasTagDimensionLine, true/*start arrow*/, true/*end arrow*/, EventElement_PointerMoved, EventElement_PointerPressed);

            // Draw dimension text
            string speciesText = MakeSpeciesText(speciesInfo);

            // Depending on the number of rows of text we are displaying, if the text is to be
            // displayed above the line then we need to adjust the offset to ensure the text
            // does not overlap the line. 
            int rowsOfTextCount = ((layerTypesDisplayed & LayerType.EventsDetail) != 0) ? 2 : 1;
            Point textPoint1;
            Point textPoint2;

            if (TrueIfTextBestAboveFalseIfBelow)
            {
                double offsetYText = -(eventFontSize * canvasScaleFactor) * rowsOfTextCount * 1.2/*vertical padding*/;

                textPoint1 = new(pointA.X + (offset * perp.X * 0.90), pointA.Y + (offset * perp.Y) + offsetYText);
                textPoint2 = new(pointB.X + (offset * perp.X * 0.90), pointB.Y + (offset * perp.Y) + offsetYText);
            }
            else
            {
                textPoint1 = new(pointA.X + (offset * perp.X * 0.90), pointA.Y + (offset * perp.Y));
                textPoint2 = new(pointB.X + (offset * perp.X * 0.90), pointB.Y + (offset * perp.Y));
            }

            // Draw the text
            DrawDimensionAndSpecies(distance, speciesText,
                new Point((textPoint1.X + textPoint2.X) / 2, (textPoint1.Y + textPoint2.Y) / 2),
                eventDimensionTextColour, canvasTagDetails);
        }


        /// <summary>
        /// Draws a Survey point on the CanvasFrame
        /// </summary>
        /// <param name="guid"></param>
        /// <param name="point"></param>
        /// <param name="speciesInfo"></param>
        private void DrawEventPoint(Guid guid, Point point, SpeciesInfo speciesInfo)
        {
            // Create CanvasTag for the event. This is so the Canvas child object can be identified
            CanvasTag canvasTagPoint = new("Event", "Point", guid);
            CanvasTag canvasTagDetails = new("Event", "Details", guid);

            CanvasDrawingHelper.DrawDot(CanvasFrame, point, 10 * canvasScaleFactor/*diameter*/, eventDimensionLineColour, canvasTagPoint, EventElement_PointerMoved, EventElement_PointerPressed);

            // Draw species text
            string speciesText = MakeSpeciesText(speciesInfo);

            // Caculate the offset for the dimension line either above or below
            double offset = (5 * canvasScaleFactor);


            // Depending on the number of rows of text we are displaying, if the text is to be
            // displayed above the line then we need to adjust the offset to ensure the text
            // does not overlap the line. 
            int rowsOfTextCount = ((layerTypesDisplayed & LayerType.EventsDetail) != 0) ? 2 : 0;

            Point textPoint = new(point.X + (offset), point.Y + (offset));


            // Draw the text
            DrawSpecies(speciesText, textPoint, eventDimensionTextColour, canvasTagDetails);
        }


        /// <summary>
        /// Write the distance and optionally the species text on the CanvasFrame
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="species"></param>
        /// <param name="at"></param>
        /// <param name="brush"></param>
        /// <param name="tag"></param>
        private void DrawDimensionAndSpecies(double distance, string species, Point at, Brush brush, CanvasTag canvasTag)
        {
            bool addTestBlock = false;

            TextBlock textBlock = new()
            {
                Foreground = brush,
                FontSize = eventFontSize * canvasScaleFactor,
                Tag = canvasTag
            };

            // Create and configure the Run
            if (distance != -1 && !string.IsNullOrWhiteSpace(species))
            {
                // Display length and species
                textBlock.Inlines.Add(new Run() { Text = $"{Math.Round(distance * 1000, 0)}mm" });
                textBlock.Inlines.Add(new LineBreak());
                textBlock.Inlines.Add(new Italic { Inlines = { new Run { Text = species } } });

                addTestBlock = true;
            }
            else if (distance == -1 && !string.IsNullOrWhiteSpace(species))
            {
                // Display the species only
                textBlock.Inlines.Add(new Italic { Inlines = { new Run { Text = species } } });
                addTestBlock = true;
            }
            else if (distance != -1)
            {
                // Display the distance only
                textBlock.Inlines.Add(new Run() { Text = $"{Math.Round(distance * 1000, 0)}mm" });
                addTestBlock = true;
            }


            if (addTestBlock)
            {
                textBlock.PointerMoved += EventElement_PointerMoved;
                textBlock.PointerPressed += EventElement_PointerPressed;

                Canvas.SetLeft(textBlock, at.X);
                Canvas.SetTop(textBlock, at.Y);
                CanvasFrame.Children.Add(textBlock);
            }
        }


        /// <summary>
        /// Write the specifies text on the CanvasFrame
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="specifies"></param>
        /// <param name="at"></param>
        /// <param name="brush"></param>
        /// <param name="tag"></param>
        private void DrawSpecies(string specifies, Point at, Brush brush, CanvasTag canvasTag)
        {
            if (specifies != "")
            {
                TextBlock textBlock = new()
                {
                    Foreground = brush,
                    FontSize = eventFontSize * canvasScaleFactor,
                    Tag = canvasTag
                };

                // Create and configure the Run
                Run run = new();
                run.Text = specifies;

                textBlock.Text = specifies;

                textBlock.PointerMoved += EventElement_PointerMoved;
                textBlock.PointerPressed += EventElement_PointerPressed;


                Canvas.SetLeft(textBlock, at.X);
                Canvas.SetTop(textBlock, at.Y);
                CanvasFrame.Children.Add(textBlock);
            }
        }




        /// <summary>
        /// Called from mediatior to display the epipolar line on the canvas frame.
        /// **A ChannelWidth of 0 draws a simple epipolar line.
        /// **A ChannelWidth of -1 clears the epipolar line**.
        /// </summary>
        //???TO BE DELELTED Epipolar Line is obsolete
        //internal void SetCanvasFrameEpipolarLine(bool TrueEpipolarLinePointAFalseEpipolarLinePointB,
        //                                         double epiLine_a, double epiLine_b, double epiLine_c,                                                  
        //                                         double channelWidth)
        //{
        //    Rect clippingWindow = new(0, 0, CanvasFrame.Width, CanvasFrame.Height);

        //    // Draw the epipolar line and also removes any existing lines
        //    DrawEpipolarLine(TrueEpipolarLinePointAFalseEpipolarLinePointB,
        //                    clippingWindow,
        //                    epiLine_a, epiLine_b, epiLine_c,
        //                    0/*Draw line*/,
        //                    true/*trueCanvasFrameFalseMagWindow*/);

        //    if (channelWidth == 0)
        //    {
        //        // Remember the epipolar coefficients for use by the Mag Window
        //        if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
        //        {
        //            epipolarLineTargetActiveA = true;
        //            epipolarLine_aTargetA = epiLine_a;
        //            epipolarLine_bTargetA = epiLine_b;
        //            epipolarLine_cTargetA = epiLine_c;
        //        }
        //        else
        //        {
        //            epipolarLineTargetActiveB = true;
        //            epipolarLine_aTargetB = epiLine_a;
        //            epipolarLine_bTargetB = epiLine_b;
        //            epipolarLine_cTargetB = epiLine_c;
        //        }
        //    }
        //    else if (channelWidth == -1)
        //    {
        //        // Remove the epipolar line
        //        if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
        //        {
        //            epipolarLineTargetActiveA = false;
        //            epipolarLine_aTargetA = 0.0;
        //            epipolarLine_bTargetA = 0.0;
        //            epipolarLine_cTargetA = 0.0;
        //        }
        //        else
        //        {
        //            epipolarLineTargetActiveB = false;
        //            epipolarLine_aTargetB = 0.0;
        //            epipolarLine_bTargetB = 0.0;
        //            epipolarLine_cTargetB = 0.0;
        //        }
        //    }
        //}


        /// <summary>
        /// Called from MagWindow() method to display the epipolar line on the mag window.
        /// **A ChannelWidth of 0 draws a simple epipolar line.
        /// **A ChannelWidth of -1 clears the epipolar line**.
        /// </summary>
        //???TO BE DELELTED Epipolar Line is obsolete
        //private void SetMagWindowEpipolarLine(bool TrueEpipolarLinePointAFalseEpipolarLinePointB,
        //                                      Rect magWindow,                                
        //                                      double channelWidth)
        //{
        //    if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
        //    {
        //        DrawEpipolarLine(TrueEpipolarLinePointAFalseEpipolarLinePointB,
        //                        magWindow,
        //                        epipolarLine_aTargetA, epipolarLine_bTargetA, epipolarLine_cTargetA,
        //                        0/*Draw line*/,
        //                        false/*trueCanvasFrameFalseMagWindow*/);
        //    }
        //    else
        //    {
        //        DrawEpipolarLine(TrueEpipolarLinePointAFalseEpipolarLinePointB,
        //                        magWindow,
        //                        epipolarLine_aTargetB, epipolarLine_bTargetB, epipolarLine_cTargetB,
        //                        0/*Draw line*/,
        //                        false/*trueCanvasFrameFalseMagWindow*/);
        //    }

        //}

        /// <summary>
        /// Called you display the epipolar line on the canvas.
        /// If the channelWidth is 0 then the epipolar line is drawn in either red or green. If
        /// the channelWidth is not 0 then parallel lines are drawn either side of the epipolar line
        /// and hatching is draw above and below to indicate where the user sould focus. The larger
        /// the value of channelWidth the further apart the parallel lines are drawn.
        /// 
        /// **A ChannelWidth of -1 clears the epipolar line**.
        /// 
        /// Line equation:
        /// ax+by+c=0
        /// Where:
        ///     a is the coefficient of x
        ///     b is the coefficient of y
        ///     c is the constant term
        /// y = -(a/b)x - (c/b)
        /// </summary>
        /// <param name="trueEpipolarLinePointAFalseEpipolarLinePointB"></param>
        /// <param name="epiLine_a"></param>
        /// <param name="epiLine_b"></param>
        /// <param name="epiLine_c"></param>
        /// <param name="channelWidth"></param>
        //???TO BE DELELTED Epipolar Line is obsolete
        //private void DrawEpipolarLine(bool trueEpipolarLinePointAFalseEpipolarLinePointB,
        //                             Rect clippingWindow,
        //                             double epiLine_a, double epiLine_b, double epiLine_c, 
        //                             double channelWidth,
        //                             bool trueCanvasFrameFalseMagWindow)
        //{
        //    // The tagValue is used to indicate if Point A or B
        //    string tagValue = trueEpipolarLinePointAFalseEpipolarLinePointB.ToString();

        //    if (trueCanvasFrameFalseMagWindow)
        //    {
        //        // Remove any existing epipolar lines
        //        RemoveCanvasShapesByTag(CanvasFrame, new CanvasTag("EpipolarLine", "Line", tagValue));
        //    }
        //    else
        //    {
        //        // Remove any existing epipolar lines
        //        RemoveCanvasShapesByTag(CanvasMag, new CanvasTag("EpipolarLine", "Line", tagValue));
        //    }

        //    // If channelWidth is 0 then draw a simple epipolar line
        //    if (channelWidth == 0)
        //    {
        //        // Create points for the line start and end points
        //        var (start, end) = GetEpipolarLineEndpoints(epiLine_a, epiLine_b, epiLine_c,
        //                                                    clippingWindow);

        //        if (start is not null && end is not null)
        //        {
        //            // Set the brush colour
        //            Brush brush;
        //            if (trueEpipolarLinePointAFalseEpipolarLinePointB)
        //                brush = epipolarALineColour;
        //            else
        //                brush = epipolarBLineColour;

        //            // Epipolar line with an arrow head on the end with the larger depth 
        //            if (trueCanvasFrameFalseMagWindow)
        //            {
        //                CanvasDrawingHelper.DrawLineWithArrowHeads(CanvasFrame, (Point)start, (Point)end, 10f * (float)canvasScaleFactor/*arrow length*/, brush, new CanvasTag("EpipolarLine", "Line", tagValue), false, true, EventElement_PointerMoved, EventElement_PointerPressed);
        //            }
        //            else
        //            {
        //                Point magWindowEpipolarStart = new(start.Value.X - clippingWindow.X, start.Value.Y - clippingWindow.Y);
        //                Point magWindowEpipolarEnd = new(end.Value.X - clippingWindow.X, end.Value.Y - clippingWindow.Y);
        //                CanvasDrawingHelper.DrawLine(CanvasMag, (Point)magWindowEpipolarStart, (Point)magWindowEpipolarEnd, 1/*thickness*/, brush, new CanvasTag("EpipolarLine", "Line", tagValue), EventElement_PointerMoved, EventElement_PointerPressed);
        //            }
        //        }
        //    }

        //    if (trueCanvasFrameFalseMagWindow && channelWidth != -1)
        //    {
        //        // Show TeachingTip is necessary
        //        if (SettingsManagerLocal.TeachingTipsEnabled == true && !SettingsManagerLocal.HasTeachingTipBeenShown("EpipolarLineTeachingTip"))
        //        {
        //            EpipolarLineTeachingTip.IsOpen = true;
        //        }
        //    }
        //}

                                                             
        /// <summary>
        /// Called from mediatior to display the epipolar curve on the canvas frame.
        /// **A ChannelWidth of 0 draws a simple epipolar curve.
        /// **A ChannelWidth of -1 clears the epipolar curve**.
        /// </summary>
        internal void SetCanvasFrameEpipolarCurve(bool TrueEpipolarLinePointAFalseEpipolarLinePointB,
                                                  List <Point>? epipolarCurveDistortedPoints,
                                                  double channelWidth)
        {
            Rect clippingWindow = new(0, 0, CanvasFrame.Width, CanvasFrame.Height);

            // Draw the epipolar curve and also removes any existing curves
            DrawEpipolarCurve(TrueEpipolarLinePointAFalseEpipolarLinePointB,
                              clippingWindow,
                              epipolarCurveDistortedPoints,
                              true/*trueCanvasFrameFalseMagWindow*/);

            if (channelWidth == 0)
            {
                // Remember the epipolar coefficients for use by the Mag Window
                if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
                {
                    epipolarLineTargetActiveA = true;
                    epipolarCurveDistortedPointsTargetA = epipolarCurveDistortedPoints;
                }
                else
                {
                    epipolarLineTargetActiveB = true;
                    epipolarCurveDistortedPointsTargetB = epipolarCurveDistortedPoints;
                }
            }
            else if (channelWidth == -1)
            {
                // Remove the epipolar line
                if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
                {
                    epipolarLineTargetActiveA = false;
                    epipolarCurveDistortedPointsTargetA = null;
                }
                else
                {
                    epipolarLineTargetActiveB = false;
                    epipolarCurveDistortedPointsTargetB = null;
                }
            }
        }



        /// <summary>
        /// Called from MagWindow() method to display the epipolar curve on the mag window.
        /// **A ChannelWidth of 0 draws a simple epipolar line.
        /// **A ChannelWidth of -1 clears the epipolar line**.
        /// </summary>
        private void SetMagWindowEpipolarCurve(bool TrueEpipolarLinePointAFalseEpipolarLinePointB,
                                               Rect magWindow,
                                               double channelWidth)
        {
            if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
            {
                DrawEpipolarCurve(TrueEpipolarLinePointAFalseEpipolarLinePointB,
                                 magWindow,
                                 epipolarCurveDistortedPointsTargetA,
                                 false/*trueCanvasFrameFalseMagWindow*/);
            }
            else
            {
                DrawEpipolarCurve(TrueEpipolarLinePointAFalseEpipolarLinePointB,
                                 magWindow,
                                 epipolarCurveDistortedPointsTargetB,
                                 false/*trueCanvasFrameFalseMagWindow*/);
            }

        }


        /// <summary>
        /// Draw a sampled epipolar curve (distortion aware). The approach:
        /// 1. Decide sampling axis from line angle (slope).
        /// 2. Iterate in 25px steps generating distorted input samples (x or y).
        /// 3. Undistort the input sample point (identity placeholder if calibration not available).
        /// 4. Solve line in undistorted space for the missing coordinate.
        /// 5. Distort the resulting undistorted point (identity placeholder if calibration not available).
        /// 6. Collect distorted output points and render as a polyline.
        /// </summary>
        private void DrawEpipolarCurve(bool trueEpipolarLinePointAFalseEpipolarLinePointB,
                                       Rect clippingWindow,
                                       List <Point>? epipolarCurveDistortedPoints,
                                       bool trueCanvasFrameFalseMagWindow)
        {

            // Tag value encodes whether A or B
            string tagValue = trueEpipolarLinePointAFalseEpipolarLinePointB.ToString();
            Canvas targetCanvas = trueCanvasFrameFalseMagWindow ? CanvasFrame : CanvasMag;

            // Remove any existing curve for this target
            RemoveCanvasShapesByTag(targetCanvas, new CanvasTag("EpipolarLine", "Curve", tagValue));

            if (epipolarCurveDistortedPoints is not null)
            {

                Polyline polyline = new()
                {
                    StrokeThickness = 1/*thickness*/,
                    Tag = new CanvasTag("EpipolarLine", "Curve", tagValue),
                    Stroke = trueEpipolarLinePointAFalseEpipolarLinePointB ? epipolarALineColour : epipolarBLineColour
                };


                if (trueCanvasFrameFalseMagWindow)
                {
                    foreach (var p in epipolarCurveDistortedPoints)
                        polyline.Points.Add(p);
                }
                else
                {
                    // Adjust points for mag window offset
                    foreach (var p in epipolarCurveDistortedPoints)
                    {
                        Point magWindowPoint = new(p.X - clippingWindow.X, p.Y - clippingWindow.Y);
                        polyline.Points.Add(magWindowPoint);
                    }
                }

                polyline.PointerMoved += EventElement_PointerMoved;
                polyline.PointerPressed += EventElement_PointerPressed;

                targetCanvas.Children.Add(polyline);
            }
        }

        // Placeholder distortion helpers (identity). Replace with calls into StereoProjection when calibration available.
        private static Point PlaceholderUndistort(Point p) => p;
        private static Point PlaceholderDistort(Point p) => p;

        /// <summary>
        /// Compute Epipolar Line Endpoints and clip to be witin the rectClip
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="c"></param>
        /// <param name="canvasWidth"></param>
        /// <param name="canvasHeight"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static (Point? start, Point? end) GetEpipolarLineEndpoints(double a, double b, double c, Rect clippingWindow)
        {
            List<Point> intersections = [];

            double leftX = clippingWindow.X;
            double rightX = clippingWindow.X + clippingWindow.Width;
            double topY = clippingWindow.Y;
            double bottomY = clippingWindow.Y + clippingWindow.Height;

            // Left boundary (x = leftX), solve for y
            if (b != 0)
            {
                double yLeft = (-c - a * leftX) / b;
                if (yLeft >= topY && yLeft <= bottomY)
                    intersections.Add(new Point(leftX, yLeft));
            }

            // Right boundary (x = rightX), solve for y
            if (b != 0)
            {
                double yRight = (-c - a * rightX) / b;
                if (yRight >= topY && yRight <= bottomY)
                    intersections.Add(new Point(rightX, yRight));
            }

            // Top boundary (y = topY), solve for x
            if (a != 0)
            {
                double xTop = (-c - b * topY) / a;
                if (xTop >= leftX && xTop <= rightX)
                    intersections.Add(new Point(xTop, topY));
            }

            // Bottom boundary (y = bottomY), solve for x
            if (a != 0)
            {
                double xBottom = (-c - b * bottomY) / a;
                if (xBottom >= leftX && xBottom <= rightX)
                    intersections.Add(new Point(xBottom, bottomY));
            }

            // Ensure we have two valid points to define the epipolar line
            if (intersections.Count >= 2)
            {
                return (intersections[0], intersections[1]);
            }
            else
            {
                Debug.WriteLine("GetEpipolarLineEndpoints: Epipolar line does not intersect the defined rectangle correctly.");
                return (null, null);
            }
        }


        /// <summary>
        /// Check all the Event shapes (lines/TextBlock) drawn on the CanvasFrame and 
        /// unhighlight any that are hightlighted
        /// </summary>
        internal void RemoveAnyLineHightLights()
        {
            if (hoveringOverDetails is not null || 
                hoveringOverMeasurementLine is not null ||
                hoveringOverMeasurementEnd is not null ||
                hoveringOverPoint is not null)
            {
                for (int i = CanvasFrame.Children.Count - 1; i >= 0; i--)
                {
                    FrameworkElement? element = CanvasFrame.Children[i] as FrameworkElement;
                    if (element != null && element.Tag is CanvasTag canvasTag)
                    {
                        if (canvasTag.IsTagType("Event"))
                        {
                            // Set the line of textblock colour back to normal
                            if (element is Line line)
                            {
                                if (line.Stroke != eventDimensionLineColour)
                                    line.Stroke = eventDimensionLineColour;
                            }
                            else if (element is Ellipse ellipse)
                            {
                                if (ellipse.Stroke != eventDimensionLineColour)
                                {
                                    ellipse.Stroke = eventDimensionLineColour;
                                    ellipse.Fill = eventDimensionLineColour;
                                }
                            }
                            else if (element is TextBlock textBlock)
                            {
                                if (textBlock.Foreground != eventDimensionTextColour)
                                    textBlock.Foreground = eventDimensionTextColour;
                            }
                        }
                    }
                }

                hoveringOverDetails = null;
                hoveringOverMeasurementLine = null;
                hoveringOverMeasurementEnd = null;
                hoveringOverPoint = null;
                hoveringOverGuid = null;
            }
        }



        ///
        /// EVENTS
        /// 


        /// <summary>
        /// This PointerMoved handler is dynamically setup on the Event shapes
        /// If it used to highlight the shapes as the pointer moves over them to 
        /// indicate they are clickable
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EventElement_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                if (element.Tag is CanvasTag canvasTag)
                {
                    if (canvasTag.IsTagType("Event", "DimensionLine"))
                        hoveringOverMeasurementLine = true;
                    else if (canvasTag.IsTagType("Event", "DimensionEnd"))
                        hoveringOverMeasurementEnd = true;
                    else if (canvasTag.IsTagType("Event", "Point"))
                        hoveringOverPoint = true;
                    else if (canvasTag.IsTagType("Event", "Details"))
                        hoveringOverDetails = true;

                    // Hightlight the shape
                    if (sender is Line line)
                    {
                        line.Stroke = eventDimensionHighLightLineColour;
                    }
                    else if (sender is Ellipse ellipse)
                    {
                        ellipse.Stroke = eventDimensionHighLightLineColour;
                        ellipse.Fill = eventDimensionHighLightLineColour;
                    }
                    else if (sender is TextBlock textBlock)
                    {
                        textBlock.Foreground = eventDimensionHighLightLineColour;
                    }

                    // Remember the Guid 
                    hoveringOverGuid = canvasTag.ValueGuid;

                    // Handle the event
                    e.Handled = true;
                }
            }
        }


        /// <summary>
        /// This PointerPressed handler is dynamically setup on the Event shapes
        /// It is used to detect a left double click
        /// </summary>
        private DateTime lastClickTime = DateTime.MinValue;
        private void EventElement_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Temp
            Debug.WriteLine($"EventElement_PointerPressed button click press detected");

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
                            if (canvasTag.IsTagType("Event", "DimensionEnd"))
                            {
                                // Edit dimensions                                
                                // TO DO
                                Debug.WriteLine("Line_PointerPressed: ADD CODE TO EDIT THIS DIMENSION");
                            }
                            else if (canvasTag.IsTagType("Event", "Details") || canvasTag.IsTagType("Event", "DimensionLine") || canvasTag.IsTagType("Event", "Point"))
                            {
                                if (canvasTag.ValueGuid is not null)
                                {
                                    // Edit species info
                                    Guid targetGuid = (Guid)canvasTag.ValueGuid;

                                    MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.EditSpeciesInfoRequest, SurveyorMediaPlayer.eCameraSide.Left)
                                    {
                                        eventGuid = targetGuid
                                    };
                                    magnifyAndMarkerControlHandler?.Send(data);
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
    }
}