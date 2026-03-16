using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Surveyor.User_Controls
{
    public class ImageMarkUp
    {
        private WriteableBitmap? wb = null;

        /// <summary>
        /// Set the bitmap to work on
        /// </summary>
        /// <param name="_wb">The WriteableBitmap to set</param>
        public void SetImage(WriteableBitmap _wb)
        {
            wb = _wb;
        }


        /// <summary>
        /// Get the bitmap
        /// </summary>
        /// <returns></returns>
        public WriteableBitmap? GetImage()
        {
            return wb;
        }


        /// <summary>
        /// Create a display thumbnail of the indicated size from the 
        /// current image. This is used to create the thumbnail for the export dialog.
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public void CreateThumbNail(int width, int height)
        {

        }


        public void AddMarkers(Point X, Point Y)
        {
            //public static void DrawDot(Canvas canvas, Point center, double diameter, Brush brush, CanvasTag canvasTag, PointerEventHandler? pointerMoved, PointerEventHandler? pointerPressed, string? toolTip = null)
        }

        public void AddBox(Point topLeft, Point bottomRight)
        {

        }

        public void AddBox(Point topLeft, int width, int height)
        {

        }


    }
}