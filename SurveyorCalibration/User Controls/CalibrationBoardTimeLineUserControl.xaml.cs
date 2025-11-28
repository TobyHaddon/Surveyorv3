using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor.Controls
{
    public sealed partial class CalibrationBoardTimeLineUserControl : UserControl
    {
        private int startMediaFrameIndex = -1;
        private int endMediaFrameIndex = -1;
        public CalibrationBoardTimeLineUserControl()
        {
            this.InitializeComponent();
        }

        public void Clear()
        {
            CalibrationBoardTimeLine.Children.Clear();
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
            CalibrationBoardTimeLine.Width = width;
            Clear();
        }


        /// <summary>
        /// This method is used as the calibration boards range in the timeline is
        /// being detected
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
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(color), // or use Colors.Green
            };

            // Position it on the Canvas
            Canvas.SetLeft(line, x);
            Canvas.SetTop(line, 0);

            CalibrationBoardTimeLine.Children.Add(line);
        }


        /// <summary>
        /// This method is called when the calibration board range is fully known.
        /// </summary>
        /// <param name="calilbrationBoardStartframeIndex"></param>
        /// <param name="calilbrationBoardEndframeIndex"></param>
        public void CalibrationBoardRange(int calilbrationBoardStartframeIndex, int calilbrationBoardEndframeIndex)
        {
            CalibrationBoardTimeLine.Children.Clear();

            if (calilbrationBoardStartframeIndex < startMediaFrameIndex || calilbrationBoardEndframeIndex > endMediaFrameIndex)
                return; // Ignore out-of-range

            double startX = calilbrationBoardStartframeIndex - startMediaFrameIndex;
            double endX = calilbrationBoardEndframeIndex - startMediaFrameIndex;
            double width = endX - startX;
            if (width < 0)
            {
                width = 0;
            }         

            var rectangle = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = width,
                Height = 5,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.LightBlue), // or use Colors.Blue
            };

            // Position it on the Canvas
            Canvas.SetLeft(rectangle, startX);
            Canvas.SetTop(rectangle, 0);
            CalibrationBoardTimeLine.Children.Add(rectangle);
        }

        /// <summary>
        /// This method is called to indicate a best calibration board frame (it is called once
        /// for each best frame). It typically overlays the CalibrationBoardRange()
        /// </summary>
        /// <param name="frameIndex"></param>
        public void BestCalibrationBoardFoundAt(int frameIndex)
        {
            if (frameIndex < startMediaFrameIndex || frameIndex > endMediaFrameIndex)
                return; // Ignore out-of-range

            double x = frameIndex - startMediaFrameIndex;

            var line = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.Green), // or use Colors.Green
            };

            // Position it on the Canvas
            Canvas.SetLeft(line, x);
            Canvas.SetTop(line, 0);

            CalibrationBoardTimeLine.Children.Add(line);
        }



        private void Viewbox_SizeChanged(object sender, SizeChangedEventArgs e)
        {

        }
    }
}
