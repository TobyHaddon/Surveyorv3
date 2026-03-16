using Emgu.CV;
using Emgu.CV.CvEnum;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Surveyor.User_Controls
{
    public class ImageExtract
    {
        public string ImagePath { get; set; } = string.Empty;

        // Media file files and handles
        private string mediaFileSpec = string.Empty;
        //???private VideoCapture? cap = null;
        private MediaPlayer? mediaPlayer = null;

        // Frame capture resources
        private CanvasDevice? canvasDevice = null;
        private SoftwareBitmap? frameServerDest = null;
        private CanvasBitmap? inputBitmap = null;

        // Writeable bitmaps for the left and right camera frames
        private Size frameSize = new(0.0, 0.0); // Size of the frames, used to create WriteableBitmaps
        private WriteableBitmap? wb = null;

        // Total frame count
        private TimeSpan totalFrames = TimeSpan.Zero;

        // Current frame indexes
        private TimeSpan currentFrame = TimeSpan.Zero;

        // Default frame step when extracting +/- frame offsets
        private TimeSpan frameStep = TimeSpan.FromMilliseconds(33.333); // ~30fps fallback

        // Thread safety
        private readonly object mediaLock = new();


        public ImageExtract()
        {

        }


        /// <summary>
        /// Open the media file
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public int VideoOpen(string fileSpec)
        {
            int ret = -1;

            lock (mediaLock)
            {
                try
                {
                    // Reset
                    Clear();

                    if (!File.Exists(fileSpec))
                        return -1;

                    mediaFileSpec = fileSpec;

                    MediaPlayer mp = new()
                    {
                        AutoPlay = false,
                        IsMuted = true,
                        IsVideoFrameServerEnabled = true,
                        Source = null
                    };

                    using ManualResetEventSlim openEvent = new(false);
                    Exception? openException = null;

                    void OnMediaOpened(MediaPlayer sender, object args)
                    {
                        openEvent.Set();
                    }

                    void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
                    {
                        openException = new InvalidOperationException(args.ErrorMessage);
                        openEvent.Set();
                    }

                    mp.MediaOpened += OnMediaOpened;
                    mp.MediaFailed += OnMediaFailed;

                    mp.Source = MediaSource.CreateFromUri(new Uri(mediaFileSpec));

                    bool opened = openEvent.Wait(TimeSpan.FromSeconds(10));

                    mp.MediaOpened -= OnMediaOpened;
                    mp.MediaFailed -= OnMediaFailed;

                    if (!opened || openException is not null)
                    {
                        mp.Dispose();
                        return -1;
                    }

                    // Wait briefly for natural dimensions if needed
                    int retry = 0;
                    while ((mp.PlaybackSession.NaturalVideoWidth == 0 || mp.PlaybackSession.NaturalVideoHeight == 0) && retry < 40)
                    {
                        Thread.Sleep(50);
                        retry++;
                    }

                    uint width = mp.PlaybackSession.NaturalVideoWidth;
                    uint height = mp.PlaybackSession.NaturalVideoHeight;
                    if (width == 0 || height == 0)
                    {
                        mp.Dispose();
                        return -1;
                    }

                    totalFrames = mp.PlaybackSession.NaturalDuration;
                    if (totalFrames < TimeSpan.Zero)
                        totalFrames = TimeSpan.Zero;

                    canvasDevice = CanvasDevice.GetSharedDevice();
                    frameServerDest = new SoftwareBitmap(BitmapPixelFormat.Bgra8, (int)width, (int)height, BitmapAlphaMode.Premultiplied);
                    inputBitmap = CanvasBitmap.CreateFromSoftwareBitmap(canvasDevice, frameServerDest);

                    wb = new WriteableBitmap((int)width, (int)height);
                    frameSize = new Size(width, height);
                    currentFrame = TimeSpan.Zero;

                    mediaPlayer = mp;

                    // Prime first frame
                    if (!TryCaptureFrameAtPosition(TimeSpan.Zero, out TimeSpan actual))
                    {
                        VideoClose();
                        return -1;
                    }

                    currentFrame = actual;
                    ret = 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{ToString()} VideoOpen: {ex.Message}");
                    ret = -1;
                }
            }

            return ret;
        }


        /// <summary>
        /// Close the media file
        /// </summary>
        /// <returns></returns>
        public int VideoClose()
        {
            lock (mediaLock)
            {
                try
                {
                    if (mediaPlayer is not null)
                    {
                        mediaPlayer.Source = null;
                        mediaPlayer.Dispose();
                        mediaPlayer = null;
                    }

                    if (inputBitmap is not null)
                    {
                        inputBitmap.Dispose();
                        inputBitmap = null;
                    }

                    if (frameServerDest is not null)
                    {
                        frameServerDest.Dispose();
                        frameServerDest = null;
                    }

                    canvasDevice = null;

                    Clear();
                    return 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{ToString()} VideoClose: {ex.Message}");
                    Clear();
                    return -1;
                }
            }
        }


        /// <summary>
        /// Extract the frame from the indicated position in the video and save 
        /// it to an image file. The file spec of the saved image is returned 
        /// in the out parameter.
        /// extractBefore and extractAfter can be set to non-zero values to allow 
        /// extraction of frames before and after the indicated position. If used
        /// extractBefore will always be a small negative number and extractAfter 
        /// will always be a small positive number.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="imageFileSpec"></param>
        /// <param name="extractBefore"></param>
        /// <param name="extractAfter"></param>
        /// <returns></returns>       
        public int VideoExtractFrame(TimeSpan position, out List<string?> imageFileSpecList, int extractBefore = 0, int extractAfter = 0)
        {
            imageFileSpecList = [];
            int ret = -1;

            if (string.IsNullOrWhiteSpace(ImagePath))
                return -1;

            if (extractBefore > 0 || extractAfter < 0)
                throw new ArgumentException("extractBefore must be <= 0 and extractAfter must be >= 0");

            lock (mediaLock)
            {
                if (mediaPlayer is null || inputBitmap is null || wb is null)
                    return -1;

                try
                {
                    Directory.CreateDirectory(ImagePath);

                    if (extractBefore == 0 && extractAfter == 0)
                    {
                        string? one = ExtractAt(position);
                        imageFileSpecList.Add(one);
                    }
                    else
                    {
                        for (int i = extractBefore; i <= extractAfter; i++)
                        {
                            TimeSpan target = position + TimeSpan.FromTicks(frameStep.Ticks * i);
                            string? fileSpec = ExtractAt(target);
                            imageFileSpecList.Add(fileSpec);
                        }
                    }

                    ret = 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{ToString()} VideoExtractFrame: {ex.Message}");
                    ret = -1;
                }
            }

            return ret;

            string? ExtractAt(TimeSpan target)
            {
                TimeSpan clamped = ClampToMediaRange(target);

                if (!TryCaptureFrameAtPosition(clamped, out TimeSpan actualPosition))
                    return null;

                currentFrame = actualPosition;
                DrawFrameToScreen(wb);

                string stem = Path.GetFileNameWithoutExtension(mediaFileSpec);
                string msToken = Math.Round(actualPosition.TotalMilliseconds, MidpointRounding.AwayFromZero).ToString("F0");
                string imageFileSpec = Path.Combine(ImagePath, $"{stem}_{msToken}ms.png");

                inputBitmap!.SaveAsync(imageFileSpec, CanvasBitmapFileFormat.Png).AsTask().GetAwaiter().GetResult();

                return imageFileSpec;
            }
        }

      

        /// <summary>
        /// Return the frame size
        /// </summary>
        /// <returns></returns>
        public (int width, int height) GetFrameSize()
        {
            return ((int)frameSize.Width, (int)frameSize.Height);
        }


        /// <summary>
        /// Create/empty folder
        /// Note the function can only delete .png/.jpeg/.jpg files as a save measure
        /// </summary>
        /// <param name="path"></param>
        /// <param name="subFolder"></param>
        /// <returns></returns>
        public static string MakeAndCreateFramesDirectoryAndEmpty(string subFolder)
        {
            string appTitle = AppInfo.Current.DisplayInfo.DisplayName;
            string documentsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), appTitle);

            string outputPath = MakeAndCreateFramesDirectory(documentsFolder, subFolder, false);

            if (!string.IsNullOrEmpty(outputPath))
            {
                // Ensure those folder are empty
                foreach (var file in Directory.GetFiles(outputPath))
                {
                    try
                    {
                        if (Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetExtension(file).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetExtension(file).Equals(".jpg", StringComparison.OrdinalIgnoreCase))
                            File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        // Optional: handle or log the exception
                        Debug.WriteLine($"MakeAndCreateFramesDirectoryAndEmpty: Failed to delete {file}: {ex.Message}");
                    }
                }
            }

            return outputPath;
        }


        /// <summary>
        /// Make the folder name to save the frames to, create the folder if necessary)
        /// </summary>
        /// <param name="subFolder"></param>
        /// <returns></returns>
        public static string MakeAndCreateFramesDirectory(string basePath, string subFolder, bool trueRelativePathFalseAbsolute)
        {
            string outputFolder;

            // Guard again base path being empty (throw exception)
            if (string.IsNullOrWhiteSpace(basePath))
                throw new ArgumentException("Base path cannot be null or whitespace.", nameof(basePath));

            outputFolder = Path.Combine(basePath, subFolder);

            if (!Directory.Exists(outputFolder))
            {
                // Create a folder
                try
                {
                    Directory.CreateDirectory(outputFolder);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MakeAndCreateFramesDirectory: Error creating save frame storage folder call: [{outputFolder}] , {ex.Message}");
                }
            }

            return outputFolder;
        }


        /// <summary>
        /// Returns the current opened media full path
        /// </summary>
        /// <returns></returns>
        public string GetCurrentMediaFileSpec()
        {
            return mediaFileSpec;
        }

        /// <summary>
        /// Returns the current bitmap.  This is useful if you call
        /// VideoExtractFrame and then want to access the bitmap for 
        /// display purposes without having to read it back from disk
        /// </summary>
        /// <returns></returns>
        public WriteableBitmap? GetCurrentWriteableBitmap()
        {
            return wb;
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Clear internal data and state
        /// </summary>
        private void Clear()
        {
            frameSize = new (0.0, 0.0);
            wb = null;
            totalFrames = TimeSpan.Zero;
            currentFrame = TimeSpan.Zero;
            mediaFileSpec = string.Empty;
            mediaPlayer = null;
            inputBitmap = null;
            frameServerDest = null;
            canvasDevice = null;
            ImagePath = string.Empty;
        }
        //???private void Clear()
        //{
        //    frameSize = new(0.0, 0.0);
        //    wb = null;

        //    // Reset total frame counts
        //    totalFrames = TimeSpan.Zero;

        //    // Reset current frame indexes
        //    currentFrame = TimeSpan.Zero;

        //    mediaFileSpec = string.Empty;
        //    cap = null;

        //    ImagePath = string.Empty;
        //}


        private TimeSpan ClampToMediaRange(TimeSpan position)
        {
            if (position < TimeSpan.Zero)
                return TimeSpan.Zero;

            if (totalFrames > TimeSpan.Zero && position > totalFrames)
                return totalFrames;

            return position;
        }

        private bool TryCaptureFrameAtPosition(TimeSpan requestedPosition, out TimeSpan actualPosition)
        {
            actualPosition = requestedPosition;

            if (mediaPlayer is null || inputBitmap is null)
                return false;

            TimeSpan capturedPosition = requestedPosition;
            int copied = 0;

            using ManualResetEventSlim frameEvent = new(false);

            void OnVideoFrameAvailable(MediaPlayer sender, object args)
            {
                if (Interlocked.Exchange(ref copied, 1) != 0)
                    return;

                try
                {
                    sender.CopyFrameToVideoSurface(inputBitmap);
                    capturedPosition = sender.PlaybackSession.Position;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{ToString()} TryCaptureFrameAtPosition CopyFrameToVideoSurface: {ex.Message}");
                }
                finally
                {
                    frameEvent.Set();
                }
            }

            mediaPlayer.VideoFrameAvailable += OnVideoFrameAvailable;

            try
            {
                mediaPlayer.IsVideoFrameServerEnabled = true;
                mediaPlayer.Pause();

                mediaPlayer.PlaybackSession.Position = requestedPosition;
                mediaPlayer.StepForwardOneFrame();

                if (!frameEvent.Wait(TimeSpan.FromSeconds(2)))
                {
                    mediaPlayer.StepForwardOneFrame();
                    frameEvent.Wait(TimeSpan.FromSeconds(2));
                }
            }
            finally
            {
                mediaPlayer.VideoFrameAvailable -= OnVideoFrameAvailable;
                mediaPlayer.IsVideoFrameServerEnabled = false;
            }

            if (copied == 0)
                return false;

            actualPosition = capturedPosition;
            return true;
        }


        /// <summary>
        /// Convert the EMGU.CV Mat frame to BGRA format, copy the pixel data to a buffer, 
        /// and then write that buffer to the WriteableBitmap's PixelBuffer. This method 
        /// is designed to be thread-safe and prevent concurrent access issues by using 
        /// an entry count mechanism. If multiple calls to this method occur simultaneously, 
        /// only the first call will proceed while others will return immediately, 
        /// ensuring that the WriteableBitmap is not accessed concurrently from multiple threads.
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="wb"></param>
        private void DrawFrameToScreen(WriteableBitmap writeableBitmap)
        {
            if (inputBitmap is null)
                return;

            try
            {
                byte[] pixels = inputBitmap.GetPixelBytes();

                if (pixels.Length == 0)
                    return;

                using var stream = writeableBitmap.PixelBuffer.AsStream();
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(pixels, 0, pixels.Length);

                writeableBitmap.Invalidate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ToString()} DrawFrameToScreen: Error drawing frame: {ex.Message}");
            }
        }

        //???private int drawFrameToScreenEntryCount = 0;
        //???private byte[]? _frameCopyBuffer;
        //???private void DrawFrameToScreen(Mat frame, WriteableBitmap wb)
        //{
        //    int entryCount = Interlocked.Increment(ref drawFrameToScreenEntryCount);
        //    if (entryCount != 1)
        //    {
        //        Interlocked.Decrement(ref drawFrameToScreenEntryCount);
        //        return;
        //    }

        //    if (frame.IsEmpty || wb == null) return;

        //    try
        //    {
        //        using var bgraFrame = new Mat();
        //        CvInvoke.CvtColor(frame, bgraFrame, Emgu.CV.CvEnum.ColorConversion.Bgr2Bgra);

        //        if (wb.PixelWidth != bgraFrame.Width || wb.PixelHeight != bgraFrame.Height)
        //        {
        //            Debug.WriteLine($"{ToString()} Warning: DrawFrameToScreen  Frame dimensions {bgraFrame.Width}x{bgraFrame.Height} " +
        //                            $"don't match WriteableBitmap {wb.PixelWidth}x{wb.PixelHeight}");
        //            return;
        //        }

        //        int byteCount = bgraFrame.Rows * bgraFrame.Cols * bgraFrame.ElementSize;
        //        if (_frameCopyBuffer == null || _frameCopyBuffer.Length != byteCount)
        //            _frameCopyBuffer = new byte[byteCount];

        //        Marshal.Copy(bgraFrame.DataPointer, _frameCopyBuffer, 0, byteCount);

        //        using var stream = wb.PixelBuffer.AsStream();
        //        stream.Seek(0, SeekOrigin.Begin);
        //        stream.Write(_frameCopyBuffer, 0, byteCount);

        //        wb.Invalidate();
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine($"{ToString()} DrawFrameToScreen: Error drawing frame: {ex.Message}");
        //    }
        //    finally
        //    {
        //        Interlocked.Decrement(ref drawFrameToScreenEntryCount);
        //    }
        //}
    }
}
