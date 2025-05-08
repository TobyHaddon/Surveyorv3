// MagnifyAndMarkerDisplay.EventsAndEpipolar.cs
// Extension to the main class to handle the drawing of events and epipolar lines
using MathNet.Numerics.LinearAlgebra.Factorization;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Windows.Foundation;

using Surveyor.Events;
using Surveyor.Helper;


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
        //???private Point? epipolarStartCanvasFrameTargetA = null;
        //???private Point? epipolarEndCanvasFrameTargetA = null;
        //???private Point? epipolarStartCanvasFrameTargetB = null;
        //???private Point? epipolarEndCanvasFrameTargetB = null;
        private double epipolarLine_aTargetA = 0.0;
        private double epipolarLine_bTargetA = 0.0;
        private double epipolarLine_cTargetA = 0.0;
        private double epipolarLine_aTargetB = 0.0;
        private double epipolarLine_bTargetB = 0.0;
        private double epipolarLine_cTargetB = 0.0;

        // Remember Epipolar Points
        private bool epipolarPointsActiveA = false;
        private Point? epipolarPointNearA = null;
        private Point? epipolarPointMiddleA = null;
        private Point? epipolarPointFarA = null;
        private bool epipolarPointsActiveB = false;
        private Point? epipolarPointNearB = null;
        private Point? epipolarPointMiddleB = null;
        private Point? epipolarPointFarB = null;


        private void ClearEventsAndEpipolar()
        {
            hoveringOverMeasurementEnd = null;
            hoveringOverMeasurementLine = null;
            hoveringOverPoint = null;
            hoveringOverDetails = null;
            hoveringOverGuid = null;

        
            epipolarLineTargetActiveA = false;
            epipolarLineTargetActiveB = false;
            //???epipolarStartCanvasFrameTargetA = null;
            //???epipolarEndCanvasFrameTargetA = null;
            //???epipolarStartCanvasFrameTargetB = null;
            //???epipolarEndCanvasFrameTargetB = null;
            epipolarLine_aTargetA = 0.0;
            epipolarLine_bTargetA = 0.0;
            epipolarLine_cTargetA = 0.0;
            epipolarLine_aTargetB = 0.0;
            epipolarLine_bTargetB = 0.0;
            epipolarLine_cTargetB = 0.0;
        
            epipolarPointsActiveA = false;
            epipolarPointNearA = null;
            epipolarPointMiddleA = null;
            epipolarPointFarA = null;
            epipolarPointsActiveB = false;
            epipolarPointNearB = null;
            epipolarPointMiddleB = null;
            epipolarPointFarB = null;
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
            Vector2 direction = new Vector2((float)(pointB.X - pointA.X), (float)(pointB.Y - pointA.Y));
            Vector2 perp = new Vector2(-direction.Y, direction.X);
            perp = Vector2.Normalize(perp);

            // Calculate the angle of the line in degrees
            double angleRadians = Math.Atan2(direction.Y, direction.X);

            // Check if the angle is 0 - 180
            bool TrueIfTextBestAboveFalseIfBelow = (angleRadians >= 0) && (angleRadians <= Math.PI);

            // Caculate the offset for the dimension line either above or below
            double offset = (20 / canvasFrameScaleX) * (TrueIfTextBestAboveFalseIfBelow ? -1 : 1);

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
            string fishID = "";
            if ((layerTypesDisplayed & LayerType.EventsDetail) != 0)
            {
                if (!string.IsNullOrEmpty(speciesInfo.Species))
                    fishID = speciesInfo.Species;
                else if (!string.IsNullOrEmpty(speciesInfo.Genus))
                    fishID = speciesInfo.Genus;
                else if (!string.IsNullOrEmpty(speciesInfo.Family))
                    fishID = speciesInfo.Family;
            }

            // Depending on the number of rows of text we are displaying, if the text is to be
            // displayed above the line then we need to adjust the offset to ensure the text
            // does not overlap the line. 
            int rowsOfTextCount = ((layerTypesDisplayed & LayerType.EventsDetail) != 0) ? 2 : 1;
            Point textPoint1;
            Point textPoint2;

            if (TrueIfTextBestAboveFalseIfBelow)
            {
                double offsetYText = -(eventFontSize / canvasFrameScaleX) * rowsOfTextCount * 1.2/*vertical padding*/;

                textPoint1 = new(pointA.X + (offset * perp.X * 0.90), pointA.Y + (offset * perp.Y) + offsetYText);
                textPoint2 = new(pointB.X + (offset * perp.X * 0.90), pointB.Y + (offset * perp.Y) + offsetYText);
            }
            else
            {
                textPoint1 = new(pointA.X + (offset * perp.X * 0.90), pointA.Y + (offset * perp.Y));
                textPoint2 = new(pointB.X + (offset * perp.X * 0.90), pointB.Y + (offset * perp.Y));
            }

            // Draw the text
            DrawDimensionAndSpecies(distance, fishID,
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

            CanvasDrawingHelper.DrawDot(CanvasFrame, point, 10 / canvasFrameScaleX/*diameter*/, eventDimensionLineColour, canvasTagPoint, EventElement_PointerMoved, EventElement_PointerPressed);

            // Draw species text
            string fishID = "";
            if ((layerTypesDisplayed & LayerType.EventsDetail) != 0)
            {
                if (!string.IsNullOrEmpty(speciesInfo.Species))
                    fishID = speciesInfo.Species;
                else if (!string.IsNullOrEmpty(speciesInfo.Genus))
                    fishID = speciesInfo.Genus;
                else if (!string.IsNullOrEmpty(speciesInfo.Family))
                    fishID = speciesInfo.Family;
            }

            // Caculate the offset for the dimension line either above or below
            double offset = (5 / canvasFrameScaleX);


            // Depending on the number of rows of text we are displaying, if the text is to be
            // displayed above the line then we need to adjust the offset to ensure the text
            // does not overlap the line. 
            int rowsOfTextCount = ((layerTypesDisplayed & LayerType.EventsDetail) != 0) ? 2 : 0;

            Point textPoint = new(point.X + (offset), point.Y + (offset));


            // Draw the text
            DrawSpecifies(fishID, textPoint, eventDimensionTextColour, canvasTagDetails);
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
                FontSize = eventFontSize / canvasFrameScaleX,
                Tag = canvasTag
            };

            //// Create and configure the Run
            //Run run = new()
            //{
            //    Text = $"{Math.Round(distance * 1000, 0)}mm"
            //};

            //if (species != "")
            //{
            //    // Create a LineBreak
            //    LineBreak lineBreak = new();

            //    // Create and configure the Span
            //    Span span = new();
            //    span.Inlines.Add(new Italic { Inlines = { new Run { Text = species } } });

            //    // Add inlines to TextBlock
            //    textBlock.Inlines.Add(run);
            //    textBlock.Inlines.Add(lineBreak);
            //    textBlock.Inlines.Add(span);
            //}
            //else
            //    textBlock.Inlines.Add(run);

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
        private void DrawSpecifies(string specifies, Point at, Brush brush, CanvasTag canvasTag)
        {
            if (specifies != "")
            {
                TextBlock textBlock = new()
                {
                    Foreground = brush,
                    FontSize = eventFontSize / canvasFrameScaleX,
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
        internal void SetCanvasFrameEpipolarLine(bool TrueEpipolarLinePointAFalseEpipolarLinePointB,
                                                 double epiLine_a, double epiLine_b, double epiLine_c,                                                  
                                                 double channelWidth)
        {
            Rect clippingWindow = new(0, 0, CanvasFrame.Width, CanvasFrame.Height);

            SetEpipolarLine(TrueEpipolarLinePointAFalseEpipolarLinePointB,
                            clippingWindow,
                            epiLine_a, epiLine_b, epiLine_c,
                            0.0/*focalLength*/, 0.0/*baseline*/, 0.0/*principalXLeft*/, 0.0/*principalYLeft*/, 0.0/*principalXRight*/, 0.0/*principalYRight*/, // Experimental parameters
                            0/*Draw line*/,
                            true/*trueCanvasFrameFalseMagWindow*/);

            if (channelWidth == 0)
            {
                // Remember the epipolar coefficients for use by the Mag Window
                if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
                {
                    epipolarLineTargetActiveA = true;
                    epipolarLine_aTargetA = epiLine_a;
                    epipolarLine_bTargetA = epiLine_b;
                    epipolarLine_cTargetA = epiLine_c;
                }
                else
                {
                    epipolarLineTargetActiveB = true;
                    epipolarLine_aTargetB = epiLine_a;
                    epipolarLine_bTargetB = epiLine_b;
                    epipolarLine_cTargetB = epiLine_c;
                }
            }
            else if (channelWidth != -1)
            {
                // Remove the epipolar line
                if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
                {
                    epipolarLineTargetActiveA = false;
                    epipolarLine_aTargetA = 0.0;
                    epipolarLine_bTargetA = 0.0;
                    epipolarLine_cTargetA = 0.0;
                }
                else
                {
                    epipolarLineTargetActiveB = false;
                    epipolarLine_aTargetB = 0.0;
                    epipolarLine_bTargetB = 0.0;
                    epipolarLine_cTargetB = 0.0;
                }
            }
        }


        /// <summary>
        /// Called from MagWindow() method to display the epipolar line on the mag window.
        /// **A ChannelWidth of 0 draws a simple epipolar line.
        /// **A ChannelWidth of -1 clears the epipolar line**.
        /// </summary>

        private void SetMagWindowEpipolarLine(bool TrueEpipolarLinePointAFalseEpipolarLinePointB,
                                               Rect magWindow,                                
                                               double channelWidth)
        {
            if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
            {
                SetEpipolarLine(TrueEpipolarLinePointAFalseEpipolarLinePointB,
                                magWindow,
                                epipolarLine_aTargetA, epipolarLine_bTargetA, epipolarLine_cTargetA,
                                0.0/*focalLength*/, 0.0/*baseline*/, 0.0/*principalXLeft*/, 0.0/*principalYLeft*/, 0.0/*principalXRight*/, 0.0/*principalYRight*/, // Experimental parameters
                                0/*Draw line*/,
                                false/*trueCanvasFrameFalseMagWindow*/);
            }
            else
            {
                SetEpipolarLine(TrueEpipolarLinePointAFalseEpipolarLinePointB,
                                magWindow,
                                epipolarLine_aTargetB, epipolarLine_bTargetB, epipolarLine_cTargetB,
                                0.0/*focalLength*/, 0.0/*baseline*/, 0.0/*principalXLeft*/, 0.0/*principalYLeft*/, 0.0/*principalXRight*/, 0.0/*principalYRight*/, // Experimental parameters
                                0/*Draw line*/,
                                false/*trueCanvasFrameFalseMagWindow*/);
            }

        }

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
        private void SetEpipolarLine(bool trueEpipolarLinePointAFalseEpipolarLinePointB,
                                     Rect clippingWindow,
                                     double epiLine_a, double epiLine_b, double epiLine_c, 
                                     double focalLength, double baseline, double principalXLeft, double principalYLeft, double principalXRight, double principalYRight, // Experimental parameters
                                     double channelWidth,
                                     bool trueCanvasFrameFalseMagWindow)
        {
            // The tagValue is used to indicate if Point A or B
            string tagValue = trueEpipolarLinePointAFalseEpipolarLinePointB.ToString();

            if (trueCanvasFrameFalseMagWindow)
            {
                // Remove any existing epipolar lines
                RemoveCanvasShapesByTag(CanvasFrame, new CanvasTag("EpipolarLine", "Polygon1", tagValue));
                RemoveCanvasShapesByTag(CanvasFrame, new CanvasTag("EpipolarLine", "Polygon1", tagValue));
                RemoveCanvasShapesByTag(CanvasFrame, new CanvasTag("EpipolarLine", "Line", tagValue));
            }
            else
            {
                // Remove any existing epipolar lines
                RemoveCanvasShapesByTag(CanvasMag, new CanvasTag("EpipolarLine", "Polygon1", tagValue));
                RemoveCanvasShapesByTag(CanvasMag, new CanvasTag("EpipolarLine", "Polygon1", tagValue));
                RemoveCanvasShapesByTag(CanvasMag, new CanvasTag("EpipolarLine", "Line", tagValue));
            }

            // If channelWidth is 0 then draw a simple epipolar line
            if (channelWidth == 0)
            {
                // Create points for the line start and end points
                var (start, end) = GetEpipolarLineEndpoints(epiLine_a, epiLine_b, epiLine_c,
                                                            clippingWindow);

                if (start is not null && end is not null)
                {
                    // Set the brush colour
                    Brush brush;
                    if (trueEpipolarLinePointAFalseEpipolarLinePointB)
                        brush = epipolarALineColour;
                    else
                        brush = epipolarBLineColour;

                    //??? Plain Epiploar line
                    //???DrawLine(start, end, brush, new CanvasTag("EpipolarLine", "Line", tagValue));

                    // Epipolar line with an arrow head on the end with the larger depth 
                    if (trueCanvasFrameFalseMagWindow)
                    {
                        CanvasDrawingHelper.DrawLineWithArrowHeads(CanvasFrame, (Point)start, (Point)end, 10 / (float)canvasFrameScaleX/*arrow length*/, brush, new CanvasTag("EpipolarLine", "Line", tagValue), false, true, EventElement_PointerMoved, EventElement_PointerPressed);
                    }
                    else
                    {
                        Point magWindowEpipolarStart = new(start.Value.X - clippingWindow.X, start.Value.Y - clippingWindow.Y);
                        Point magWindowEpipolarEnd = new(end.Value.X - clippingWindow.X, end.Value.Y - clippingWindow.Y);
                        CanvasDrawingHelper.DrawLine(CanvasMag, (Point)magWindowEpipolarStart, (Point)magWindowEpipolarEnd, 1/*thickness*/, brush, new CanvasTag("EpipolarLine", "Line", tagValue), EventElement_PointerMoved, EventElement_PointerPressed);
                    }
                }
            }

            if (trueCanvasFrameFalseMagWindow && channelWidth != -1)
            {
                // Show TeachingTip is necessary
                if (SettingsManagerLocal.TeachingTipsEnabled == true && !SettingsManagerLocal.HasTeachingTipBeenShown("EpipolarLineTeachingTip"))
                {
                    EpipolarLineTeachingTip.IsOpen = true;
                }
            }
        }


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
        //??? older version (keep for a while 02 Feb 2025)
        //public static (Point? start, Point? end) GetEpipolarLineEndpoints(double a, double b, double c, double canvasWidth, double canvasHeight)
        //{
        //    List<Point> intersections = [];

        //    // Left boundary (x = 0), solve for y
        //    if (b != 0)
        //    {
        //        double yLeft = (-c - a * 0) / b;
        //        if (yLeft >= 0 && yLeft <= canvasHeight)
        //            intersections.Add(new Point(0, yLeft));
        //    }

        //    // Right boundary (x = canvasWidth), solve for y
        //    if (b != 0)
        //    {
        //        double yRight = (-c - a * canvasWidth) / b;
        //        if (yRight >= 0 && yRight <= canvasHeight)
        //            intersections.Add(new Point(canvasWidth, yRight));
        //    }

        //    // Top boundary (y = 0), solve for x
        //    if (a != 0)
        //    {
        //        double xTop = (-c - b * 0) / a;
        //        if (xTop >= 0 && xTop <= canvasWidth)
        //            intersections.Add(new Point(xTop, 0));
        //    }

        //    // Bottom boundary (y = canvasHeight), solve for x
        //    if (a != 0)
        //    {
        //        double xBottom = (-c - b * canvasHeight) / a;
        //        if (xBottom >= 0 && xBottom <= canvasWidth)
        //            intersections.Add(new Point(xBottom, canvasHeight));
        //    }

        //    // Ensure we have two valid points to draw the line
        //    if (intersections.Count >= 2)
        //    {
        //        return (intersections[0], intersections[1]);
        //    }
        //    else
        //    {
        //        Debug.WriteLine("GetEpipolarLineEndpoints: Epipolar line does not intersect the canvas correctly.");
        //        return (null, null);
        //    }
        //}



        /// <summary>
        /// Failed
        /// Calculate the epipolar line for unrectified stereo images that is between the min and max depth
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="c"></param>
        /// <param name="canvasWidth"></param>
        /// <param name="canvasHeight"></param>
        /// <param name="focalLength"></param>
        /// <param name="baseline"></param>
        /// <param name="principalXLeft"></param>
        /// <param name="principalYLeft"></param>
        /// <param name="principalXRight"></param>
        /// <param name="principalYRight"></param>
        /// <param name="minDepth"></param>
        /// <param name="maxDepth"></param>
        /// <returns></returns>
        //??? NOT CURRENT USED
        //public static (Point start, Point end) GetEpipolarLineForUnrectifiedStereo(double a, double b, double c, double canvasWidth, double canvasHeight,
        //                                            double focalLength, double baseline, 
        //                                            double principalXLeft, double principalYLeft, double principalXRight, double principalYRight,
        //                                            double minDepth, double maxDepth)                                                    
        //{
        //    // Ensure valid depth range
        //    if (minDepth == 0) minDepth = 0.01;  // Prevent division by zero
        //    if (maxDepth <= minDepth) maxDepth = minDepth + 0.1;

        //    // Compute disparity range
        //    double maxDisparity = (focalLength * baseline) / minDepth; // Closest object (largest disparity)
        //    double minDisparity = (focalLength * baseline) / maxDepth; // Farthest object (smallest disparity)

        //    // Convert disparity to right image coordinates
        //    double xMinRight = principalXLeft - minDisparity + (principalXRight - principalXLeft);
        //    double xMaxRight = principalXLeft - maxDisparity + (principalXRight - principalXLeft);

        //    // Adjust for principalY difference (if right image is shifted)
        //    double yMinRight = (-c - a * xMinRight) / b + (principalYRight - principalYLeft);
        //    double yMaxRight = (-c - a * xMaxRight) / b + (principalYRight - principalYLeft);

        //    // Ensure the epipolar line is clipped within the canvas bounds
        //    var (canvasStart, canvasEnd) = GetEpipolarLineEndpoints(a, b, c, canvasWidth, canvasHeight);

        //    if (canvasStart is not null && canvasEnd is not null)
        //    {
        //        xMinRight = Math.Clamp(xMinRight, Math.Min(canvasStart.Value.X, canvasEnd.Value.X), Math.Max(canvasStart.Value.X, canvasEnd.Value.X));
        //        yMinRight = Math.Clamp(yMinRight, Math.Min(canvasStart.Value.Y, canvasEnd.Value.Y), Math.Max(canvasStart.Value.Y, canvasEnd.Value.Y));

        //        xMaxRight = Math.Clamp(xMaxRight, Math.Min(canvasStart.Value.X, canvasEnd.Value.X), Math.Max(canvasStart.Value.X, canvasEnd.Value.X));
        //        yMaxRight = Math.Clamp(yMaxRight, Math.Min(canvasStart.Value.Y, canvasEnd.Value.Y), Math.Max(canvasStart.Value.Y, canvasEnd.Value.Y));
        //    }

        //    return (new Point(xMinRight, yMinRight), new Point(xMaxRight, yMaxRight));
        //}


        /// <summary>
        /// Used to applied an epipolar curved line to the canvas and rememeber the line points for later use
        /// by the mag window.
        /// </summary>
        /// <param name="TrueEpipolarLinePointAFalseEpipolarLinePointB"></param>
        /// <param name="pointNear"></param>
        /// <param name="pointMiddle"></param>
        /// <param name="pointFar"></param>
        /// <param name="channelWidth"></param>
        internal void SetEpipolarPoints(bool TrueEpipolarLinePointAFalseEpipolarLinePointB, Point pointNear, Point pointMiddle, Point pointFar, int channelWidth)
        {
            // The tagValue is used to indicate if Point A or B
            string tagValue = TrueEpipolarLinePointAFalseEpipolarLinePointB.ToString();

            // Remove any existing epipolar lines
            RemoveCanvasShapesByTag(CanvasFrame, new CanvasTag("EpipolarPoints", "Curve", tagValue));


            // If channelWidth is 0 then draw a simple epipolar line
            if (channelWidth == 0)
            {
                // Set the brush colour
                Brush brush;
                if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
                    brush = epipolarALineColour;
                else
                    brush = epipolarBLineColour;

                // Epipolar line with an arrow head on the end with the larger depth 
                CanvasDrawingHelper.DrawLine(CanvasFrame, pointNear, pointMiddle, brush, new CanvasTag("EpipolarPoints", "Curve", tagValue), EventElement_PointerMoved, EventElement_PointerPressed);
                CanvasDrawingHelper.DrawLineWithArrowHeads(CanvasFrame, (Point)pointMiddle, (Point)pointFar, 10/*arrow length*/, brush, new CanvasTag("EpipolarPoints", "Curve", tagValue), false, true, EventElement_PointerMoved, EventElement_PointerPressed);
                CanvasDrawingHelper.DrawDot(CanvasFrame, pointMiddle, 10/*diameter*/, brush, new CanvasTag("EpipolarPoints", "Curve", tagValue), EventElement_PointerMoved, EventElement_PointerPressed);

                // Remember the epipolar line
                if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
                {
                    epipolarPointsActiveA = true;
                    epipolarPointNearA = pointNear;
                    epipolarPointMiddleA = pointMiddle;
                    epipolarPointFarA = pointFar;
                }
                else
                {
                    epipolarPointsActiveB = true;
                    epipolarPointNearB = pointNear;
                    epipolarPointMiddleB = pointMiddle;
                    epipolarPointFarB = pointFar;
                }

            }
            else if (channelWidth != -1)
            {
                // Remove the epipolar line
                if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
                {
                    epipolarPointsActiveA = true;
                    epipolarPointNearA = null;
                    epipolarPointMiddleA = null;
                    epipolarPointFarA = null;
                }
                else
                {
                    epipolarPointsActiveB = true;
                    epipolarPointNearB = null;
                    epipolarPointMiddleB = null;
                    epipolarPointFarB = null;
                }
            }

            if (channelWidth != -1)
            {
                // Show TeachingTip is necessary
                if (SettingsManagerLocal.TeachingTipsEnabled == true && !SettingsManagerLocal.HasTeachingTipBeenShown("EpipolarPointsTeachingTip"))
                {
                    EpipolarPointsTeachingTip.IsOpen = true;
                }
            }
        }


        /// <summary>
        /// Solve line for Y
        /// </summary>
        /// <param name="x"></param>
        /// <param name="A"></param>
        /// <param name="B"></param>
        /// <param name="C"></param>
        /// <param name="channelWidth"></param>
        /// <returns></returns>
        private double LineSolveForY(double x, double A, double B, double C, double channelWidth)
        {
            double sqrtTerm = Math.Sqrt(A * A + B * B);
            double D1 = C + channelWidth * sqrtTerm;

            double y = (-A / B) * x - (D1 / B);

            return y;
        }

        /// <summary>
        /// Solve line for X
        /// </summary>
        /// <param name="x"></param>
        /// <param name="epiLine_a"></param>
        /// <param name="epiLine_b"></param>
        /// <param name="epiLine_c"></param>
        /// <param name="channelWidth"></param>
        /// <returns></returns>
        private double LineSolveForX(double y, double A, double B, double C, double channelWidth)
        {
            double sqrtTerm = Math.Sqrt(A * A + B * B);
            double D1 = C + channelWidth * sqrtTerm;

            double x = (-B / A) * y - (D1 / A);
            return x;
        }


        /// <summary>
        ///  ax+by+(c+channelWidth)=0
        ///  y = -(a/b)x - (c/b)
        ///  x = -(b/a)y - (c/a)
        /// </summary>
        /// <param name="epiLine_a"></param>
        /// <param name="epiLine_b"></param>
        /// <param name="epiLine_c"></param>
        /// <param name="channelWidth"></param>
        /// <param name="canvasWidth"></param>
        /// <param name="canvasHeight"></param>
        /// <param name="polyLineLeftIntersect"></param>
        /// <param name="polyLineRightIntersect"></param>
        /// <param name="polyLineTopIntersect"></param>
        /// <param name="polyLineBottomIntersect"></param>
        ///??? Not Used 02 Feb 2025 
        //private void XXCalculateCanvasIntersectPointForEpipolarLine(double A, double B, double C,
        //    double channelWidth,
        //    double canvasWidth, double canvasHeight,
        //    out double? polyLineLeftIntersect,
        //    out double? polyLineRightIntersect,
        //    out double? polyLineTopIntersect,
        //    out double? polyLineBottomIntersect)
        //{
        //    // Reset
        //    polyLineLeftIntersect = null;
        //    polyLineRightIntersect = null;
        //    polyLineTopIntersect = null;
        //    polyLineBottomIntersect = null;

        //    // Does polygon1 line intersect left canvas boundary
        //    // Range is between 0 and (canvasHeight - 2) inclusive, (canvasHeight - 1) is classified as the bottom boundary
        //    // y = ((-a * x) - (c + cw)) / b so if x = 0 then y = -(c + cw) / b
        //    if (B != 0)
        //    {
        //        polyLineLeftIntersect = LineSolveForY(0 /*x=0*/, A, B, C, channelWidth);

        //        // Check if the intersect is out of bounds
        //        if (polyLineLeftIntersect < 0 || polyLineLeftIntersect >= (canvasHeight - 1))
        //            polyLineLeftIntersect = null;
        //    }

        //    // Does polygon line intersect right canvas boundary
        //    // Range is greater than 0 and (canvasHeight - 1) inclusive, 0 is classified as the top boundary
        //    // y = ((-a * x) - (c + cw)) / b  so if x=(canvasWidth - 1) then y = ((-a * (canvasWidth - 1)) - (c + cw)) / b
        //    if (B != 0)
        //    {
        //        polyLineRightIntersect = LineSolveForY(canvasWidth - 1 /*x=canvasWidth - 1*/, A, B, C, channelWidth);

        //        // Check if the intersect is out of bounds
        //        if (polyLineRightIntersect <= 0 || polyLineRightIntersect > canvasHeight - 1)
        //            polyLineRightIntersect = null;
        //    }

        //    // Does polygon1 line intersect top canvas boundary
        //    // Range is greater than 0 and (canvasWidth - 1) inclusive, 0 is classified as the lef boundary
        //    // x = ((-b * y) - (c + cw)) / a  so if y = 0  then x = (-(c + cw) / a
        //    if (A != 0)
        //    {
        //        polyLineTopIntersect = LineSolveForX(0/*y=0*/, A, B, C, channelWidth);

        //        // Check if the intersect is out of bounds
        //        if (polyLineTopIntersect <= 0 || polyLineTopIntersect > canvasWidth - 1)
        //            polyLineTopIntersect = null;
        //    }

        //    // Does polygon1 line intersect bottom canvas boundary
        //    // Range is between 0 and least than (canvasWidth - 1) inclusive, (canvasWidth - 1) is classified as the right boundary
        //    // x = ((-b * y) - (c + cw)) / a  so if y = (canvasHeight - 1)  then x = ((-b * (canvasHeight - 1)) - (c + cw)) / a
        //    if (A != 0)
        //    {
        //        polyLineBottomIntersect = LineSolveForX(canvasHeight - 1/*y=canvasHeight - 1*/, A, B, C, channelWidth);

        //        // Check if the intersect is out of bounds
        //        if (polyLineBottomIntersect <= 0 || polyLineBottomIntersect > canvasWidth - 1)
        //            polyLineBottomIntersect = null;
        //    }
        //}


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
                        }

                        // Handle the event
                        e.Handled = true;

                        // Update the last click time
                        lastClickTime = DateTime.Now;
                    }
                }
            }           
        }
    }
}