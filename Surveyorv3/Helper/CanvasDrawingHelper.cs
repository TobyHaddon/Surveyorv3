// Canvas Drawing Function
//
// Version 1.0  03 Mar 2025

using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Diagnostics;
using System.Numerics;
using Windows.Foundation;

namespace Surveyor.Helper
{
    static class CanvasDrawingHelper
    {
        /// <summary>
        /// Draw aline on the CanvasFrame using the indicated brush and tag
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="brush"></param>
        /// <param name="canvasTag"></param>
        public static void DrawLine(Canvas canvas, Point start, Point end, Brush brush, CanvasTag canvasTag, PointerEventHandler? pointerMoved, PointerEventHandler? pointerPressed)
        {
            CanvasDrawingHelper.DrawLine(canvas, start, end, 2/*strokeThickness*/, brush, canvasTag, pointerMoved, pointerPressed);
        }
        public static void DrawLine(Canvas canvas, Point start, Point end, double strokeThickness, Brush brush, CanvasTag canvasTag, PointerEventHandler? pointerMoved, PointerEventHandler? pointerPressed)
        {
            try
            {
                Microsoft.UI.Xaml.Shapes.Line line = new()
                {
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = end.X,
                    Y2 = end.Y,
                    StrokeThickness = strokeThickness,
                    Stroke = brush,
                    Tag = canvasTag
                };

                // Set event handlers if necessary
                if (pointerMoved is not null)
                    line.PointerMoved += pointerMoved;
                if (pointerPressed is not null)
                    line.PointerPressed += pointerPressed;

                canvas.Children.Add(line);
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
        public static void DrawDot(Canvas canvas, Point centre, double diameter, Brush brush, object canvasTag, PointerEventHandler? pointerMoved, PointerEventHandler? pointerPressed)
        {
            // Create an ellipse (circle) with a diameter of 6
            Ellipse ellipse = new()
            {
                Width = diameter,
                Height = diameter,
                Fill = brush, // Set the fill color
                Stroke = brush, // Set the outline color
                StrokeThickness = 1, // Set the thickness of the outline
                Tag = canvasTag
            };

            // Set event handlers if necessary
            if (pointerMoved is not null)
                ellipse.PointerMoved += pointerMoved;
            if (pointerPressed is not null)
                ellipse.PointerPressed += pointerPressed;

            // Set the position of the ellipse on the canvas
            Canvas.SetLeft(ellipse, centre.X - (diameter / 2)); // Subtract half the width to center
            Canvas.SetTop(ellipse, centre.Y - (diameter / 2)); // Subtract half the height to center


            // Add the ellipse to the canvas
            canvas.Children.Add(ellipse);
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
        public static void DrawLineWithArrowHeads(Canvas canvas, Point start, Point end, float arrowLength, Brush brush, CanvasTag canvasTag, bool arrowStart, bool arrowEnd, PointerEventHandler? pointerMoved, PointerEventHandler? pointerPressed)
        {
            // First draw the line
            DrawLine(canvas, start, end, brush, canvasTag, pointerMoved, pointerPressed);

            // Calculate the direction vector
            Vector2 lineDirection = new((float)(end.X - start.X), (float)(end.Y - start.Y));

            // Draw the arrow heads as required
            if (arrowStart)
                DrawArrowHead(canvas, start, -lineDirection, arrowLength, brush, canvasTag, pointerMoved, pointerPressed);
            if (arrowEnd)
                DrawArrowHead(canvas, end, lineDirection, arrowLength, brush, canvasTag, pointerMoved, pointerPressed);
        }


        /// <summary>
        /// Draw an arrow head on the Canvas Frame
        /// </summary>
        /// <param name="end"></param>
        /// <param name="direction"></param>
        /// <param name="brush"></param>
        /// <param name="tag"></param>
        public static void DrawArrowHead(Canvas canvas, Point end, Vector2 direction, float arrowLength, Brush brush, CanvasTag canvasTag, PointerEventHandler? pointerMoved, PointerEventHandler? pointerPressed)
        {
            //???const float arrowLength = 10f;  // Length of the arrow lines
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
            DrawLine(canvas, end, arrowEnd1, brush, canvasTag, pointerMoved, pointerPressed);
            DrawLine(canvas, end, arrowEnd2, brush, canvasTag, pointerMoved, pointerPressed);
        }


        /// <summary>
        /// Blur a polygen
        /// </summary>
        /// <param name="points"></param>
        /// <param name="brush"></param>
        /// <param name="canvasTag"></param>
        public static void DrawPolygonAcrylic(Canvas canvas, PointCollection points, Brush brush, CanvasTag canvasTag)
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
            canvas.Children.Add(polygon);
        }
    }


    /// <summary>
    /// Class of imbedding information in a Canvas child element
    /// Tag format:
    /// [TagType]:[TagSubType]:[Value]
    /// The TagType is the type of tag e.g. 'Event' or 'EpipolarLine'
    /// The TagSubType is the sub type of the tag e.g. 'DimensionEnd' or 'DimensionLine'
    /// The Value is the value of the tag e.g. '12345678-1234-1234-1234-1234567890AB' or 'SomeText'
    /// </summary>
    public class CanvasTag
    {
        public enum ValueType
        {
            vtNone,
            vtGuid,
            vtString
        }

        // TagType e.g. 'Event' or 'EpipolarLine'
        public string TagType { get; set; }
        // TagSubType e.g. 'DimensionEnd' or 'DimensionLine'
        public string TagSubType { get; set; }

        // Value type e.g. GUID or String
        public ValueType VType;
        public Guid? ValueGuid { get; set; }
        public string? ValueString { get; set; }

        public CanvasTag(string tagType, string tagSubType)
        {
            TagType = tagType;
            TagSubType = tagSubType;

            VType = ValueType.vtNone;
            ValueGuid = null;
            ValueString = null;

        }
        public CanvasTag(string tagType, string tagSubType, Guid guid) : this(tagType, tagSubType)
        {
            VType = ValueType.vtGuid;
            ValueGuid = guid;
        }
        public CanvasTag(string tagType, string tagSubType, string valueString) : this(tagType, tagSubType)
        {
            VType = ValueType.vtString;
            ValueString = valueString;
        }

        /// <summary>
        /// Check if the tag is of the indicated values and ignore the sub type
        /// </summary>
        /// <param name="tagType"></param>
        /// <returns></returns>
        public bool IsTagType(string tagType)
        {
            return TagType == tagType;
        }


        /// <summary>
        /// Check if the tag andn sub tag is of the indicated values 
        /// </summary>
        /// <param name="tagType"></param>
        /// <param name="tagSubType"></param>
        /// <returns></returns>
        public bool IsTagType(string tagType, string tagSubType)
        {
            return TagType == tagType && TagSubType == tagSubType;
        }

        public bool IsTag(CanvasTag tagOther)
        {
            return (TagType == tagOther.TagType && TagSubType == tagOther.TagSubType && IsValue(tagOther));
        }

        public bool IsValue(CanvasTag tagOther)
        {
            switch (VType)
            {
                case ValueType.vtNone:
                    return false;

                case ValueType.vtGuid:
                    if (ValueGuid is null || tagOther.ValueGuid is null)
                        return false;
                    else
                        return (tagOther.VType == ValueType.vtGuid) && (ValueGuid == tagOther.ValueGuid);

                case ValueType.vtString:
                    if (ValueString is null || tagOther.ValueString is null)
                        return false;
                    else
                        return (tagOther.VType == ValueType.vtString) && (ValueString == tagOther.ValueString);

                default:
                    return false;
            }
        }
    }


}
