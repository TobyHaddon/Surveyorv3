using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Surveyor.Helper;
using System;
using Windows.Foundation;

namespace Surveyor.User_Controls
{
    public class MagnifyAndMarkerDisplayBookmark
    {
        // Bookmark size, thickness & frame display range
        private readonly double bookmarkSize = 120; // pixels
        private readonly double bookmarkThickness = 2; // pixels
        private readonly TimeSpan bookmakeFrameDisplayRange = new(0, 0, 5); // plus/minus 5 second

        private SurveyorMediaPlayer.eCameraSide bookmarkCameraSide = SurveyorMediaPlayer.eCameraSide.None;
        private TimeSpan bookmarkPosition = TimeSpan.Zero;
        private Point? bookmarkPoint = null;

        private readonly Brush eventBookmarkLineColor = new SolidColorBrush(Microsoft.UI.Colors.Yellow);



        /// <summary>
        /// Sets a bookmark for the specified camera side at the given frame and location.
        /// </summary>
        /// <remarks>This method clears any existing bookmark before setting the new one.  Ensure that the
        /// provided parameters are valid and meaningful for the current media context.</remarks>
        /// <param name="CameraSide">The side of the camera for which the bookmark is being set.</param>
        /// <param name="frame">The time position within the media where the bookmark is placed.</param>
        /// <param name="bookmarkPoint">The coordinates of the bookmark within the frame.</param>
        public void SetBookmark(SurveyorMediaPlayer.eCameraSide _cameraSide, TimeSpan position, Point _bookmarkPoint)
        {
            ClearBookmark();

            bookmarkCameraSide = _cameraSide;
            bookmarkPosition = position;
            bookmarkPoint = _bookmarkPoint;
        }


        /// <summary>
        /// Clears the current bookmark, resetting the camera side, frame, and bookmark point to their default values.
        /// </summary>
        /// <remarks>This method resets the state of the bookmark, including the camera side, playback
        /// frame, and bookmark point. After calling this method, no bookmark will be set.</remarks>
        public void ClearBookmark()
        {
            bookmarkCameraSide = SurveyorMediaPlayer.eCameraSide.None;
            bookmarkPosition = TimeSpan.Zero;
            bookmarkPoint = null;
        }


        /// <summary>
        /// Determines whether a bookmark is set for the specified camera side and frame.
        /// </summary>
        /// <param name="CameraSide">The camera side to check for the bookmark.</param>
        /// <param name="position">The frame time to check for the bookmark.</param>
        /// <returns><see langword="true"/> if a bookmark is set for the specified camera side and the frame is within the
        /// tolerance range; otherwise, <see langword="false"/>.</returns>
        public bool IsBookmarkSet(SurveyorMediaPlayer.eCameraSide CameraSide, TimeSpan position)
        {
            if (bookmarkCameraSide == CameraSide && bookmarkPoint.HasValue)
            {
                // Check if the frame is within the tolerance range
                if (Math.Abs((bookmarkPosition - position).TotalMilliseconds) <= bookmakeFrameDisplayRange.TotalMilliseconds)
                {
                    return true;
                }
                else
                {
                    // Out of range so clear the bookmark
                    ClearBookmark();
                }
            }
            return false;
        }


        /// <summary>
        /// Retrieves the bookmark point for the specified camera side and frame, if one is set.
        /// </summary>
        /// <remarks>The method checks if a bookmark is set for the specified camera side and frame. If
        /// the frame is within the defined tolerance range, the bookmark point is returned. Otherwise, <see
        /// langword="null"/> is returned.</remarks>
        /// <param name="CameraSide">The camera side for which the bookmark is being retrieved.</param>
        /// <param name="frame">The frame time for which the bookmark is being checked.</param>
        /// <returns>A <see cref="Point"/> representing the bookmark location if a bookmark is set and the frame is within the
        /// tolerance range; otherwise, <see langword="null"/>.</returns>
        public Point? GetBookmark(SurveyorMediaPlayer.eCameraSide CameraSide, TimeSpan frame)
        {
            if (IsBookmarkSet(CameraSide, frame))
            {
                return bookmarkPoint;
            }
            return null;
        }


        /// <summary>
        /// Draw the bookmark on the provided canvas and attach pointer event handlers.
        /// </summary>
        /// <param name="canvasFrame">Target canvas to draw on.</param>
        /// <param name="pointerMoved">Pointer moved handler (void Handler(object, PointerRoutedEventArgs)).</param>
        /// <param name="pointerPressed">Pointer pressed handler (void Handler(object, PointerRoutedEventArgs)).</param>
        public void DrawBookmark(Canvas canvasFrame, PointerEventHandler pointerMoved, PointerEventHandler pointerPressed)
        {
            CanvasTag canvasTag = new("Bookmark", "Point");

            if (bookmarkPoint.HasValue)
            {
                CanvasDrawingHelper.DrawCircle(canvasFrame,
                                            (Point)bookmarkPoint,
                                            bookmarkSize/*radius*/,
                                            eventBookmarkLineColor,
                                            bookmarkThickness/*stroke thickness*/,
                                            null,
                                            canvasTag,
                                            pointerMoved,
                                            pointerPressed);
                //CanvasDrawingHelper.DrawCircle(canvasFrame,
                //                            (Point)bookmarkPoint,
                //                            bookmarkSize + 5/*radius*/,
                //                            eventBookmarkLineColor,
                //                            bookmarkThickness/*stroke thickness*/,
                //                            null,
                //                            canvasTag,
                //                            pointerMoved,
                //                            pointerPressed);
            }
        }
    }
}
