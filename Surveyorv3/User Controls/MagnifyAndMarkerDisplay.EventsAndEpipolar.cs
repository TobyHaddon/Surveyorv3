//using ExampleMagnifierWinUI;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Surveyor.Events;
using System;
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

            DrawLine(p1Start, p1End, eventDimensionLineColour, canvasTagDimensionEnd);

            // Parallel line 2
            Point p2Start = new(pointB.X, pointB.Y);
            Point p2End = new(pointB.X + (offset * perp.X), pointB.Y + (offset * perp.Y));
            DrawLine(p2Start, p2End, eventDimensionLineColour, canvasTagDimensionEnd);

            // Draw dimension line
            Point dimPoint1 = new(pointA.X + (offset * perp.X * 0.80), pointA.Y + (offset * perp.Y * 0.80));
            Point dimPoint2 = new(pointB.X + (offset * perp.X * 0.80), pointB.Y + (offset * perp.Y * 0.80));
            DrawLineWithArrowHeads(dimPoint1, dimPoint2, eventArrowLineColour, canvasTagDimensionLine, true/*start arrow*/, true/*end arrow*/);

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
            DrawDimensionAndSpecifies(distance, fishID,
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

            DrawDot(point, 10/*diameter*/, eventDimensionLineColour, canvasTagPoint);

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
        /// Draw aline on the CanvasFrame using the indicated brush and tag
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="brush"></param>
        /// <param name="canvasTag"></param>
        private void DrawLine(Point start, Point end, Brush brush, CanvasTag canvasTag)
        {
            try
            {
                Microsoft.UI.Xaml.Shapes.Line line = new()
                {
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = end.X,
                    Y2 = end.Y,
                    StrokeThickness = 2,
                    Stroke = brush,
                    Tag = canvasTag
                };

                line.PointerMoved += EventElement_PointerMoved;
                line.PointerPressed += EventElement_PointerPressed;

                CanvasFrame.Children.Add(line);
            }
            catch (Exception ex) 
            {
                Debug.WriteLine($"MagnifyAndMarkerDisplay.DrawLine: Exception raised, start ({start.X},{start.Y}). end ({end.X},{end.Y}), {canvasTag.TagType}/{canvasTag.TagSubType}, {ex.Message}");
            }
        }


        /// <summary>
        /// Draw a dot on the canvas
        /// </summary>
        /// <param name="centre"></param>
        /// <param name="brush"></param>
        /// <param name="canvasTag"></param>
        /// <param name="canvas"></param>
        private void DrawDot(Point centre, double diameter, Brush brush, object canvasTag)
        {
            double scaledDiameter = diameter / canvasFrameScaleX;

            // Create an ellipse (circle) with a diameter of 6
            Ellipse ellipse = new()
            {
                Width = scaledDiameter,
                Height = scaledDiameter,
                Fill = brush, // Set the fill color
                Stroke = brush, // Set the outline color
                StrokeThickness = 1, // Set the thickness of the outline
                Tag = canvasTag
            };
            ellipse.PointerMoved += EventElement_PointerMoved;
            ellipse.PointerPressed += EventElement_PointerPressed;

            // Set the position of the ellipse on the canvas
            Canvas.SetLeft(ellipse, centre.X - (scaledDiameter / 2)); // Subtract half the width to center
            Canvas.SetTop(ellipse, centre.Y - (scaledDiameter / 2)); // Subtract half the height to center


            // Add the ellipse to the canvas
            CanvasFrame.Children.Add(ellipse);
        }



        /// <summary>
        /// Draw aline on the CanvasFrame with arrow heads using the indicated brush and tag
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="brush"></param>
        /// <param name="tag"></param>
        /// <param name="arrowStart"></param>
        /// <param name="arrowEnd"></param>
        private void DrawLineWithArrowHeads(Point start, Point end, Brush brush, CanvasTag canvasTag, bool arrowStart, bool arrowEnd)
        {
            // First draw the line
            DrawLine(start, end, brush, canvasTag);

            // Calculate the direction vector
            Vector2 lineDirection = new Vector2((float)(end.X - start.X), (float)(end.Y - start.Y));

            // Draw the arrow heads as required
            if (arrowStart)
                DrawArrowHead(start, -lineDirection, brush, canvasTag);
            if (arrowEnd)
                DrawArrowHead(end, lineDirection, brush, canvasTag);
        }


        /// <summary>
        /// Draw an arrow head on the Canvas Frame
        /// </summary>
        /// <param name="end"></param>
        /// <param name="direction"></param>
        /// <param name="brush"></param>
        /// <param name="tag"></param>
        private void DrawArrowHead(Point end, Vector2 direction, Brush brush, CanvasTag canvasTag)
        {
            const float arrowLength = 10f;  // Length of the arrow lines
            const float arrowAngle = 30f;   // Angle of the arrow lines

            // Normalize and scale the direction vector
            direction = Vector2.Normalize(direction) * arrowLength;

            // Calculate the two points that form the arrow lines
            Point arrowEnd1 = new(
                end.X - (direction.X * Math.Cos(arrowAngle * Math.PI / 180) - direction.Y * Math.Sin(arrowAngle * Math.PI / 180)),
                end.Y - (direction.X * Math.Sin(arrowAngle * Math.PI / 180) + direction.Y * Math.Cos(arrowAngle * Math.PI / 180))
            );

            Point arrowEnd2 = new(
                end.X - (direction.X * Math.Cos(-arrowAngle * Math.PI / 180) - direction.Y * Math.Sin(-arrowAngle * Math.PI / 180)),
                end.Y - (direction.X * Math.Sin(-arrowAngle * Math.PI / 180) + direction.Y * Math.Cos(-arrowAngle * Math.PI / 180))
            );

            // Draw the arrow lines
            DrawLine(end, arrowEnd1, brush, canvasTag);
            DrawLine(end, arrowEnd2, brush, canvasTag);
        }


        /// <summary>
        /// Write the distance and optionally the specifies text on the CanvasFrame
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="specifies"></param>
        /// <param name="at"></param>
        /// <param name="brush"></param>
        /// <param name="tag"></param>
        private void DrawDimensionAndSpecifies(double distance, string specifies, Point at, Brush brush, CanvasTag canvasTag)
        {
            TextBlock textBlock = new()
            {
                Foreground = brush,
                FontSize = eventFontSize / canvasFrameScaleX,
                Tag = canvasTag
            };

            // Create and configure the Run
            Run run = new()
            {
                Text = $"{Math.Round(distance * 1000, 0)}mm"
            };

            if (specifies != "")
            {
                // Create a LineBreak
                LineBreak lineBreak = new();

                // Create and configure the Span
                Span span = new();
                span.Inlines.Add(new Italic { Inlines = { new Run { Text = specifies } } });

                // Add inlines to TextBlock
                textBlock.Inlines.Add(run);
                textBlock.Inlines.Add(lineBreak);
                textBlock.Inlines.Add(span);
            }
            else
                textBlock.Inlines.Add(run);

            textBlock.PointerMoved += EventElement_PointerMoved;
            textBlock.PointerPressed += EventElement_PointerPressed;


            Canvas.SetLeft(textBlock, at.X);
            Canvas.SetTop(textBlock, at.Y);
            CanvasFrame.Children.Add(textBlock);
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
        /// Blur a polygen
        /// </summary>
        /// <param name="points"></param>
        /// <param name="brush"></param>
        /// <param name="canvasTag"></param>
        private void DrawPolygonAcrylic(PointCollection points, Brush brush, CanvasTag canvasTag)
        {
            Windows.UI.Color tintColor;

            // Extract from brush if SolidColorBrush
            if (brush is SolidColorBrush solidColorBrush)
                tintColor = solidColorBrush.Color;
            else
                tintColor = Colors.Black;

            // Create a Polygon
            Polygon polygon = new() 
            {
                Points = points,
                Tag = canvasTag
            };


            // Define the AcrylicBrush
            AcrylicBrush acrylicBrush = new()
            {
                TintColor = tintColor,
                TintOpacity = 0.1,
                FallbackColor = Colors.White
            };

            // Set the Fill of the Polygon
            polygon.Fill = acrylicBrush;

            // Add the Polygon to the Canvas
            CanvasFrame.Children.Add(polygon);
        }


        /// <summary>
        /// Called you display the epipolar line on the canvas.
        /// If the channelWidth is 0 then the epipolar line is drawn in either red or green. If
        /// the channelWidth is not 0 then parallel lines are drawn either side of the epipolar line
        /// and hatching is draw above and below to indicate where the user sould focus. The larger
        /// the value of channelWidth the further apart the parallel lines are drawn.
        /// **A ChannelWidth of -1 clears the epipolar line**.
        /// Line equation:
        /// ax+by+c=0
        /// Where:
        ///     a is the coefficient of x
        ///     b is the coefficient of y
        ///     c is the constant term
        /// y = -(a/b)x - (c/b)
        /// </summary>
        /// <param name="TrueEpipolarLinePointAFalseEpipolarLinePointB"></param>
        /// <param name="epiLine_a"></param>
        /// <param name="epiLine_b"></param>
        /// <param name="epiLine_c"></param>
        /// <param name="channelWidth"></param>
        internal void SetEpipolarLine(bool TrueEpipolarLinePointAFalseEpipolarLinePointB,
            double epiLine_a, double epiLine_b, double epiLine_c, double channelWidth)
        {
            // The tagValue is used to indicate if Point A or B
            string tagValue = TrueEpipolarLinePointAFalseEpipolarLinePointB.ToString();

            // Remove any existing epipolar lines
            RemoveCanvasShapesByTag(new CanvasTag("EpipolarLine", "Polygon1", tagValue));
            RemoveCanvasShapesByTag(new CanvasTag("EpipolarLine", "Polygon2", tagValue));


            // If channelWidth is 0 then draw a simple epipolar line
            if (channelWidth == 0)
            {
                // Create points for the line (start and end)
                Point start = new(0, CanvasFrame.Height - epiLine_c / epiLine_b);
                Point end = new(CanvasFrame.Width, CanvasFrame.Height - (epiLine_c + epiLine_a * CanvasFrame.Height) / epiLine_b);

                // Set the brush colour
                Brush brush;
                if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
                    brush = epipolarALineColour;
                else
                    brush = epipolarBLineColour;

                DrawLine(start, end, brush, new CanvasTag("EpipolarLine", "", tagValue));
            }
            else if (channelWidth > 0) // If channelWidth is not 0 then draw parallel lines to epipolar line
                                       // with blurring above and below to indicate where the user should focus
            {
                // Caculate the offset for the dimension line either above or below
                double offset = (channelWidth / canvasFrameScaleX);

                // Calculate the out of bounds polygon for line 1 and 2
                CalculateOutOfBoundsPolygons(TrueEpipolarLinePointAFalseEpipolarLinePointB,
                    epiLine_a, epiLine_b, epiLine_c, 
                    offset, 
                    CanvasFrame.Width, CanvasFrame.Height,
                    out PointCollection points1, out PointCollection points2);

                // Set the brush colour
                Brush brush;
                if (TrueEpipolarLinePointAFalseEpipolarLinePointB)
                    brush = epipolarALineColour;
                else
                    brush = epipolarBLineColour;

                // Parallel line1
                DrawPolygonAcrylic(points1, brush, new CanvasTag("EpipolarLine", "Polygon1", tagValue));
                DrawPolygonAcrylic(points2, brush, new CanvasTag("EpipolarLine", "Polygon2", tagValue));
            }
        }

        /// <summary>
        /// 
        /// polygon1 line is: ax+by+(c+channelWidth)=0
        /// polygon2 line is: ax+by+(c-channelWidth)=0
        /// </summary>
        /// <param name="TrueEpipolarLinePointAFalseEpipolarLinePointB"
        /// <param name="epiLine_a"></param>
        /// <param name="epiLine_b"></param>
        /// <param name="epiLine_c"></param>
        /// <param name="channelWidth"></param>
        /// <param name="canvasWidth"></param>
        /// <param name="canvasHeight"></param>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        private void CalculateOutOfBoundsPolygons(bool TrueEpipolarLinePointAFalseEpipolarLinePointB,
            double epiLine_a, double epiLine_b, double epiLine_c,
            double channelWidth,
            double canvasWidth, double canvasHeight,
            out PointCollection points1, out PointCollection points2)
        {

            // The tagValue is used to indicate if Point A or B
            string tagValue = TrueEpipolarLinePointAFalseEpipolarLinePointB.ToString();

             
            // Get the intersect points of the epipolar line with the canvas
            // boundaries for polygon line 1
            CalculateCanvasIntersectPointForEpipolarLine(epiLine_a, epiLine_b, epiLine_c,
                channelWidth,
                canvasWidth, canvasHeight,
                out double? polyLineLeftIntersect1,
                out double? polyLineRightIntersect1,
                out double? polyLineTopIntersect1,
                out double? polyLineBottomIntersect1);


            // Get the intersect points of the epipolar line with the canvas
            // boundaries for polygon line 2
            CalculateCanvasIntersectPointForEpipolarLine(epiLine_a, epiLine_b, epiLine_c,
                -channelWidth,
                canvasWidth, canvasHeight,
                out double? polyLineLeftIntersect2,
                out double? polyLineRightIntersect2,
                out double? polyLineTopIntersect2,
                out double? polyLineBottomIntersect2);


            // points 1 is typically the upper out of bounds area
            points1 = new();

            // Did we intersect the left canvas boundary?
            if (polyLineLeftIntersect1 is not null)
                points1.Add(new Point(0, (double)polyLineLeftIntersect1));

            // Did we intersect the top canvas boundary?
            if (polyLineTopIntersect1 is not null)
                points1.Add(new Point((double)polyLineTopIntersect1, 0));

            // Did we intersect the right canvas boundary?
            if (polyLineRightIntersect1 is not null)
                points1.Add(new Point(canvasWidth - 1, (double)polyLineRightIntersect1));

            // Did we interest the bottom canvas boundary?
            if (polyLineBottomIntersect1 is not null)
                points1.Add(new Point((double)polyLineBottomIntersect1, canvasHeight - 1));



            // If intersect is Left and Top then add(0, 0)
            if (polyLineLeftIntersect1 is not null && polyLineTopIntersect1 is not null)
                points1.Add(new Point(0, 0));

            // If intersect is Top and Right then add(w, 0)
            if (polyLineTopIntersect1 is not null && polyLineRightIntersect1 is not null)
                points1.Add(new Point(canvasWidth, 0));

            // If intersect is Left and Right then add(w, 0) & (0, 0)
            if (polyLineLeftIntersect1 is not null && polyLineRightIntersect1 is not null)
            {
                points1.Add(new Point(canvasWidth, 0));
                points1.Add(new Point(0, 0));
            }

            // If intersect is Left and Bottom then add(w, h), (w, 0) & (0, 0)
            if (polyLineLeftIntersect1 is not null && polyLineBottomIntersect1 is not null)
            {
                points1.Add(new Point(canvasWidth, canvasHeight));
                points1.Add(new Point(canvasWidth, 0));
                points1.Add(new Point(0, 0));
            }

            // If intersect is Top and Bottom then add(w, h) & (w, 0)
            if (polyLineTopIntersect1 is not null && polyLineBottomIntersect1 is not null)
            {
//                if (polyLineTopIntersect1 > polyLineBottomIntersect1)
//                {
                    points1.Add(new Point(0, canvasHeight));
                    points1.Add(new Point(0, 0));
                //}
                //else
                //{
                //    points1.Add(new Point(canvasWidth, canvasHeight));
                //    points1.Add(new Point(canvasWidth, 0));
                //}
            }

            // If intersect is Right and Bottom then add(0, h), (0, 0) & (w, 0)
            if (polyLineRightIntersect1 is not null && polyLineBottomIntersect1 is not null)
            {
                points1.Add(new Point(0, canvasHeight));
                points1.Add(new Point(0, 0));
                points1.Add(new Point(canvasWidth, 0));
            }
        


            // points 2 is typically the upper out of bounds area
            points2 = new();

            // Did we intersect the left canvas boundary?
            if (polyLineLeftIntersect2 is not null)
                points2.Add(new Point(0, (double)polyLineLeftIntersect2));

            // Did we intersect the top canvas boundary?
            if (polyLineTopIntersect2 is not null)
                points2.Add(new Point((double)polyLineTopIntersect2, 0));

            // Did we intersect the right canvas boundary?
            if (polyLineRightIntersect2 is not null)
                points2.Add(new Point(canvasWidth - 1, (double)polyLineRightIntersect2));

            // Did we interest the bottom canvas boundary?
            if (polyLineBottomIntersect2 is not null)
                points2.Add(new Point((double)polyLineBottomIntersect2, canvasHeight - 1));


            // If intersect is Left and Top then add(w, 0), (w, h) & (0, h)
            if (polyLineLeftIntersect2 is not null && polyLineTopIntersect2 is not null)
            {
                points2.Add(new Point(canvasWidth, 0));                
                points2.Add(new Point(canvasWidth, canvasHeight));
                points2.Add(new Point(0, canvasHeight));
            }

            // If intersect is Top and Right then  add(w, h), (0, h) & (0, 0)
            if (polyLineTopIntersect2 is not null && polyLineRightIntersect2 is not null)
            {
                points2.Add(new Point(canvasWidth, canvasHeight));
                points2.Add(new Point(0, canvasHeight));                
                points2.Add(new Point(0, 0));
            }

            // If intersect is Left and Right then  add(w, h) & (0, h)
            if (polyLineLeftIntersect2 is not null && polyLineRightIntersect2 is not null)
            {
                points2.Add(new Point(canvasWidth, canvasHeight));
                points2.Add(new Point(0, canvasHeight));            
            }

            // If intersect is Left and Bottom then  add(0, h)
            if (polyLineLeftIntersect2 is not null && polyLineBottomIntersect2 is not null)
                points2.Add(new Point(0, canvasHeight));

            // If intersect is Top and Bottom then  add(0, h) & (0, 0)
            if (polyLineTopIntersect2 is not null && polyLineBottomIntersect2 is not null)
            {
//                if (polyLineTopIntersect2 > polyLineBottomIntersect2)
//                {
                    points2.Add(new Point(canvasWidth, canvasHeight));
                    points2.Add(new Point(canvasWidth, 0));
                //}
                //else
                //{
                //    points2.Add(new Point(0, canvasHeight));
                //    points2.Add(new Point(0, 0));
                //}
            }

            // If intersect is Right and Bottom then  add(w, h)
            if (polyLineRightIntersect2 is not null && polyLineBottomIntersect2 is not null)
                points2.Add(new Point(canvasWidth, canvasHeight));
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
        private void CalculateCanvasIntersectPointForEpipolarLine(double A, double B, double C,
            double channelWidth,
            double canvasWidth, double canvasHeight,
            out double? polyLineLeftIntersect,
            out double? polyLineRightIntersect,
            out double? polyLineTopIntersect,
            out double? polyLineBottomIntersect)
        {
            // Reset
            polyLineLeftIntersect = null;
            polyLineRightIntersect = null;
            polyLineTopIntersect = null;
            polyLineBottomIntersect = null;

            // Does polygon1 line intersect left canvas boundary
            // Range is between 0 and (canvasHeight - 2) inclusive, (canvasHeight - 1) is classified as the bottom boundary
            // y = ((-a * x) - (c + cw)) / b so if x = 0 then y = -(c + cw) / b
            if (B != 0)
            {
                polyLineLeftIntersect = LineSolveForY(0 /*x=0*/, A, B, C, channelWidth);

                // Check if the intersect is out of bounds
                if (polyLineLeftIntersect < 0 || polyLineLeftIntersect >= (canvasHeight - 1))
                    polyLineLeftIntersect = null;
            }

            // Does polygon line intersect right canvas boundary
            // Range is greater than 0 and (canvasHeight - 1) inclusive, 0 is classified as the top boundary
            // y = ((-a * x) - (c + cw)) / b  so if x=(canvasWidth - 1) then y = ((-a * (canvasWidth - 1)) - (c + cw)) / b
            if (B != 0)
            {
                polyLineRightIntersect = LineSolveForY(canvasWidth - 1 /*x=canvasWidth - 1*/, A, B, C, channelWidth);

                // Check if the intersect is out of bounds
                if (polyLineRightIntersect <= 0 || polyLineRightIntersect > canvasHeight - 1)
                    polyLineRightIntersect = null;
            }

            // Does polygon1 line intersect top canvas boundary
            // Range is greater than 0 and (canvasWidth - 1) inclusive, 0 is classified as the lef boundary
            // x = ((-b * y) - (c + cw)) / a  so if y = 0  then x = (-(c + cw) / a
            if (A != 0)
            {
                polyLineTopIntersect = LineSolveForX(0/*y=0*/, A, B, C, channelWidth);

                // Check if the intersect is out of bounds
                if (polyLineTopIntersect <= 0 || polyLineTopIntersect > canvasWidth - 1)
                    polyLineTopIntersect = null;
            }

            // Does polygon1 line intersect bottom canvas boundary
            // Range is between 0 and least than (canvasWidth - 1) inclusive, (canvasWidth - 1) is classified as the right boundary
            // x = ((-b * y) - (c + cw)) / a  so if y = (canvasHeight - 1)  then x = ((-b * (canvasHeight - 1)) - (c + cw)) / a
            if (A != 0)
            {
                polyLineBottomIntersect = LineSolveForX(canvasHeight - 1/*y=canvasHeight - 1*/, A, B, C, channelWidth);

                // Check if the intersect is out of bounds
                if (polyLineBottomIntersect <= 0 || polyLineBottomIntersect > canvasWidth - 1)
                    polyLineBottomIntersect = null;
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
                                if (textBlock.Foreground != eventDimensionLineColour)
                                    textBlock.Foreground = eventDimensionLineColour;
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
                                    _magnifyAndMarkerControlHandler?.Send(data);
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