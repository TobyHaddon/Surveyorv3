
//using MathNet.Numerics;
using ActionCameraMP4MetadataExtraction;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using Surveyor.Helper;
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
        private readonly SemaphoreSlim mediaGate = new(1, 1);

        // Add these fields in ImageExtract (near other private fields)
        private readonly object frameWaitLock = new();
        private TaskCompletionSource<TimeSpan>? nextFrameTcs;

        private static bool IsPositionWithinTolerance(TimeSpan requested, TimeSpan actual, TimeSpan tolerance)
                        => Math.Abs((actual - requested).Ticks) <= tolerance.Ticks;

        public ImageExtract()
        {

        }


        /// <summary>
        /// Open the media file
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        // Replace VideoOpen with this version

        public async Task<int> VideoOpenAsync(string fileSpec)
        {
            int ret = -1;

            await mediaGate.WaitAsync();
            try
            {
                // Ensure previous resources/handlers are cleaned up before opening new media.
                _ = CloseInternalNoLock();

                if (!File.Exists(fileSpec))
                    return -1;

                mediaFileSpec = fileSpec;

                try
                {
                    Dictionary<string, string> fileProperties = await GetMP4FileProperities.ExtractProperties(fileSpec);
                    if (fileProperties.TryGetValue("Video.FrameRate", out string? frameRate))
                    {
                        frameStep = TimeSpan.FromMilliseconds(Double.Parse(frameRate));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{ToString()} VideoOpen: Failed to extract frame rate, using default. {ex.Message}");
                    return -1;
                }

                // Create MediaPlayer with video frame server mode enabled
                MediaPlayer mp = new()
                {
                    AutoPlay = false,
                    IsMuted = true,
                    IsVideoFrameServerEnabled = false,
                    Source = null
                };

                // Wait for MediaOpened or MediaFailed event to ensure media is ready before
                // we query properties or attempt to capture frames
                using ManualResetEventSlim openEvent = new(false);
                Exception? openException = null;

                // Handlers to capture MediaOpened and MediaFailed events.
                // These will signal the openEvent when either event is raised, allowing
                // us to wait for the media to be ready or fail before proceeding.
                void OnMediaOpened(MediaPlayer sender, object args) => openEvent.Set();

                void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
                {
                    openException = new InvalidOperationException(args.ErrorMessage);
                    openEvent.Set();
                }

                // Wire up the event handlers, set the source to start opening the media,
                // and wait for the result.
                mp.MediaOpened += OnMediaOpened;
                mp.MediaFailed += OnMediaFailed;

                // Setting the source will trigger the MediaPlayer to start opening the media,
                // which will in turn trigger either the MediaOpened or MediaFailed event when
                // it completes.
                mp.Source = MediaSource.CreateFromUri(new Uri(mediaFileSpec));

                // Wait for either MediaOpened or MediaFailed to be raised, with a timeout to
                // avoid hanging indefinitely.
                bool opened = openEvent.Wait(TimeSpan.FromSeconds(10));

                // Unwire the event handlers as they are no longer needed after this point.
                // The MediaPlayer will be disposed if opening failed, so we want to avoid
                // any chance of these handlers being called after disposal.
                mp.MediaOpened -= OnMediaOpened;
                mp.MediaFailed -= OnMediaFailed;

                if (!opened || openException is not null)
                {
                    mp.Dispose();
                    return -1;
                }

                // At this point the media is opened and we can query properties and capture frames.
                int retry = 0;
                while ((mp.PlaybackSession.NaturalVideoWidth == 0 || mp.PlaybackSession.NaturalVideoHeight == 0) && retry < 40)
                {
                    Thread.Sleep(50);
                    retry++;
                }

                // If we still don't have valid frame dimensions, something is wrong with
                // the media or playback session, so we should clean up and return failure.
                uint frameWidth = mp.PlaybackSession.NaturalVideoWidth;
                uint frameHeight = mp.PlaybackSession.NaturalVideoHeight;
                if (frameWidth == 0 || frameHeight == 0)
                {
                    mp.Dispose();
                    return -1;
                }

                // At this point we have valid frame dimensions and can proceed with setting up the frame extraction.
                totalFrames = mp.PlaybackSession.NaturalDuration;
                if (totalFrames < TimeSpan.Zero)
                    totalFrames = TimeSpan.Zero;

                // Create the CanvasDevice, SoftwareBitmap, and CanvasBitmap that will
                // be used as the destination for the MediaPlayer's CopyFrameToVideoSurface calls.
                canvasDevice = CanvasDevice.GetSharedDevice();
                frameServerDest = new SoftwareBitmap(BitmapPixelFormat.Bgra8, (int)frameWidth, (int)frameHeight, BitmapAlphaMode.Premultiplied);
                inputBitmap = CanvasBitmap.CreateFromSoftwareBitmap(canvasDevice, frameServerDest);

                // Create the WriteableBitmap that will be used for display and extraction.
                // The frame data will be copied
                wb = new WriteableBitmap((int)frameWidth, (int)frameHeight);
                frameSize = new Size(frameWidth, frameHeight);
                currentFrame = TimeSpan.Zero;

                // At this point we have a valid media player and can start extracting frames.
                mediaPlayer = mp;

                // Wire once for lifetime of this open media session.
                mediaPlayer.VideoFrameAvailable += MediaPlayer_VideoFrameAvailable;
                mediaPlayer.IsVideoFrameServerEnabled = true;

                // Capture the first frame to ensure everything is working and we have a
                // valid current frame.
                (bool retb, TimeSpan actual) = await TryCaptureFrameAtPosition(TimeSpan.Zero);
                if (!retb)
                {
                    CloseInternalNoLock();
                    return -1;
                }

                // Set the current frame to the actual captured frame, which may be
                // different from the requested position
                currentFrame = actual;
                ret = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ToString()} VideoOpen: {ex.Message}");
                ret = -1;
            }
            finally
            {
                mediaGate.Release();
            }

            return ret;
        }


        /// <summary>
        /// Close the media file
        /// </summary>
        /// <returns></returns>
        public int VideoClose()
        {
            mediaGate.Wait();

            try
            {
                return CloseInternalNoLock();
            }
            finally
            {
                mediaGate.Release();
            }
        }


        /// <summary>
        /// Extract the frames from the indicated position in the video and save 
        /// them to image files. The file specs of the saved images are returned 
        /// in the out parameter.
        /// Set the ImagePath property to indicate where the extracted images should be saved.
        /// extractBefore and extractAfter should be set to non-zero values to allow 
        /// extraction of frames before and after the indicated position. If used
        /// extractBefore will always be a small negative number and extractAfter 
        /// will always be a small positive number.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="imageFileSpec"></param>
        /// <param name="extractBefore"></param>
        /// <param name="extractAfter"></param>
        /// <param name="exportFileSpec">Optional file specification for the exported image, only allowed is when extractBefore and extractAfter are both zero.</param>
        /// <returns></returns>       
        public async Task<(int ret, List<string?> imageFileSpecList)> VideoExtractFramesAsync(TimeSpan position, int extractBefore, int extractAfter, string? exportFileSpec = null)
        {
            int ret = -1;
            List<string?> imageFileSpecList = [];
            
            // Guard           
            //if (string.IsNullOrWhiteSpace(ImagePath) && string.IsNullOrEmpty(exportFileSpec))
            //    return (-1, imageFileSpecList);

            if (extractBefore > 0 || extractAfter < 0)
                throw new ArgumentException("VideoExtractFramesAsync: extractBefore must be <= 0 and extractAfter must be >= 0");
            
            if (exportFileSpec is not null && (extractBefore != 0 || extractAfter != 0))
                throw new ArgumentException("VideoExtractFramesAsync: exportFileSpec can only be specified when extractBefore and extractAfter are both zero.");

            await mediaGate.WaitAsync();
            try
            {
                if (mediaPlayer is null || inputBitmap is null || wb is null)
                    return (-1, imageFileSpecList);
              
                if (extractBefore == 0 && extractAfter == 0)
                {
                    string? one = await ExtractAtAsync(position, exportFileSpec);
                    imageFileSpecList.Add(one);
                }
                else
                {
                    for (int i = extractBefore; i <= extractAfter; i++)
                    {
                        TimeSpan target = position + TimeSpan.FromTicks(frameStep.Ticks * i);
                        string? fileSpec = await ExtractAtAsync(target, exportFileSpec);
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
            finally
            {
                mediaGate.Release();
            }           

            return (ret, imageFileSpecList);

            // Extract Frame at target position, return saved file spec or null on failure
            async Task<string?> ExtractAtAsync(TimeSpan target, string? exportFileSpec)
            {
                TimeSpan clamped = ClampToMediaRange(target);

                (bool retb, TimeSpan actualPosition) = await TryCaptureFrameAtPosition(clamped);
                if (!retb)
                    return null;

                currentFrame = actualPosition;
                DrawFrameToScreen(wb);

                string imageFileSpec = string.Empty;

                if (!string.IsNullOrEmpty(ImagePath) || exportFileSpec is not null)
                {
                    string formattedTime = "0000" + $"{Math.Round(position.TotalSeconds, 2):F2}";
                    string fileName = Path.GetFileNameWithoutExtension(mediaFileSpec) + $"_P.{formattedTime[Math.Max(0, formattedTime.Length - 12)..]}s.png";
                    imageFileSpec = exportFileSpec ?? Path.Combine(ImagePath, fileName);

                    await inputBitmap!.SaveAsync(imageFileSpec, CanvasBitmapFileFormat.Png);
                }

                return imageFileSpec;
            }
        }     

        /// <summary>
        /// Extract a frame from the indicated position in the video and save 
        /// it to an image file. The file spec of the saved image is returned 
        /// in the out parameter.
        /// Set the ImagePath property to indicate where the extracted images should be saved.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="imageFileSpecList"></param>
        /// <returns></returns>
        public async Task<(int ret, string outputFileSpec)> VideoExtractFrameAsync(TimeSpan position)
        {
            (int ret, List<string?> imageFileSpecList) = await VideoExtractFramesAsync(position, extractBefore: 0, extractAfter: 0);

            if (ret == 0 && imageFileSpecList.Count == 1 && (!string.IsNullOrEmpty(imageFileSpecList[0]) || ImagePath == ""))
                return (0, imageFileSpecList[0]!);
            else
                return (-1, string.Empty);  
        }


        /// <summary>
        /// Extract a frame from the indicated position in the video and save
        /// it to an export file spec provided. 
        /// Note the ImagePage property is ignored in this case. 
        /// </summary>
        /// <param name="position"></param>
        /// <param name="exportFileSpec"></param>
        /// <returns></returns>
        public async Task<int> VideoExtractFrameAsync(TimeSpan position, string exportFileSpec)
        {
            (int ret, List<string?> imageFileSpecList) = await VideoExtractFramesAsync(position, extractBefore: 0, extractAfter: 0, exportFileSpec);

            if (ret == 0 && imageFileSpecList.Count == 1 && !string.IsNullOrEmpty(imageFileSpecList[0]))
                return 0;
            else
                return -1;
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


        // Replace CloseInternalNoLock with this version

        private int CloseInternalNoLock()
        {
            try
            {
                if (mediaPlayer is not null)
                {
                    mediaPlayer.VideoFrameAvailable -= MediaPlayer_VideoFrameAvailable;
                    mediaPlayer.IsVideoFrameServerEnabled = false;
                    mediaPlayer.Source = null;
                    mediaPlayer.Dispose();
                    mediaPlayer = null;
                }

                lock (frameWaitLock)
                {
                    nextFrameTcs?.TrySetCanceled();
                    nextFrameTcs = null;
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

        private TimeSpan ClampToMediaRange(TimeSpan position)
        {
            if (position < TimeSpan.Zero)
                return TimeSpan.Zero;

            if (totalFrames > TimeSpan.Zero && position > totalFrames)
                return totalFrames;

            return position;
        }

        // Replace TryCaptureFrameAtPosition with this version

        private async Task<(bool ret, TimeSpan actualPosition)> TryCaptureFrameAtPosition(TimeSpan requestedPosition)
        {
            TimeSpan actualPosition = requestedPosition;

            if (mediaPlayer is null || inputBitmap is null)
                return (false, TimeSpan.Zero);

            TimeSpan capturedPosition = requestedPosition;

            try
            {
                // If the media is currently playing, pause it before attempting to
                // capture a frame. This is necessary because the MediaPlayer will not
                // raise the VideoFrameAvailable event while it is playing, which means
                // we won't be able to capture a frame.
                if (mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                {
                    mediaPlayer.Pause();
                    await Task.Delay(100);
                }

                // Check if we are already at the requested position (within frame step tolerance).
                // If so, step forward and back to trigger frame availability.
                if (TimePositionHelper.IsExactFrameMatch(mediaPlayer.PlaybackSession.Position, requestedPosition, frameStep))
                {
                    Debug.WriteLine($"Extract at: {requestedPosition.TotalSeconds:F2}, already at position so step forward and back.");

                    mediaPlayer.StepForwardOneFrame();
                    (bool okFwd, TimeSpan posFwd) = await WaitForNextFrameAsync(
                        $"Extract at: {requestedPosition.TotalSeconds:F2}), step forward",
                        TimeSpan.FromSeconds(2));

                    if (!okFwd)
                        return (false, mediaPlayer.PlaybackSession.Position);

                    mediaPlayer.StepBackwardOneFrame();
                    (bool okBack, TimeSpan posBack) = await WaitForNextFrameAsync(
                        $"Extract at: {requestedPosition.TotalSeconds:F2}), step back",
                        TimeSpan.FromSeconds(2));

                    if (!okBack)
                    {
                        // Fallback: explicit seek if backward frame callback does not arrive.
                        mediaPlayer.PlaybackSession.Position = requestedPosition;

                        (bool okSeek, TimeSpan posSeek) = await WaitForNextFrameAsync(
                            $"Extract at: {requestedPosition.TotalSeconds:F2}), fallback seek",
                            TimeSpan.FromSeconds(2));

                        if (!okSeek)
                            return (false, mediaPlayer.PlaybackSession.Position);

                        capturedPosition = posSeek;
                    }
                    else
                    {
                        capturedPosition = posBack;
                    }
                }
                else
                {
                    Debug.WriteLine($"Extract at: {requestedPosition.TotalSeconds:F2}), seeking to position.");
                    mediaPlayer.PlaybackSession.Position = requestedPosition;

                    (bool okSeek, TimeSpan posSeek) = await WaitForNextFrameAsync(
                        $"Extract at: {requestedPosition.TotalSeconds:F2}), seek",
                        TimeSpan.FromSeconds(2));

                    if (!okSeek)
                        return (false, mediaPlayer.PlaybackSession.Position);

                    capturedPosition = posSeek;
                }

                // If after the initial seek and potential step forward/back we are not on an exact
                // frame match for the requested position, step forward or backward as needed until
                // we find an exact frame match, or exhaust our max tries.
                int maxTries = 10;
                while (!TimePositionHelper.IsExactFrameMatch(mediaPlayer.PlaybackSession.Position, requestedPosition, frameStep))
                {
                    if (mediaPlayer.PlaybackSession.Position < requestedPosition)
                    {
                        Debug.WriteLine($"Extract at: {requestedPosition.TotalSeconds:F2}), current {mediaPlayer.PlaybackSession.Position.TotalSeconds:F2} before requested, stepping forward.");
                        mediaPlayer.StepForwardOneFrame();
                    }
                    else
                    {
                        Debug.WriteLine($"Extract at: {requestedPosition.TotalSeconds:F2}), current {mediaPlayer.PlaybackSession.Position.TotalSeconds:F2} after requested, stepping backward.");
                        mediaPlayer.StepBackwardOneFrame();
                    }

                    (bool okStep, TimeSpan posStep) = await WaitForNextFrameAsync(
                        $"Extract at: {requestedPosition.TotalSeconds:F2}), step adjust",
                        TimeSpan.FromSeconds(2));

                    if (!okStep)
                        return (false, mediaPlayer.PlaybackSession.Position);

                    capturedPosition = posStep;

                    maxTries--;
                    if (maxTries == 0)
                        return (false, mediaPlayer.PlaybackSession.Position);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ToString()} TryCaptureFrameAtPosition: {ex.Message}");
                return (false, mediaPlayer.PlaybackSession.Position);
            }

            actualPosition = capturedPosition;
            return (true, actualPosition);
        }


        // Add these helper methods + event handler in ImageExtract (private section)
        /// <summary>
        /// Called each time the MediaPlayer has a frame to deliver
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MediaPlayer_VideoFrameAvailable(MediaPlayer sender, object args)
        {
            if (inputBitmap is null)
                return;

            try
            {
                // Get the current frame into a CanvasBitmap and record the current media position
                sender.CopyFrameToVideoSurface(inputBitmap);
                TimeSpan position = sender.PlaybackSession.Position;

                Debug.WriteLine($"OnVideoFrameAvailable: {position.TotalSeconds:F2}");

                // Clear the current wait for frame availability and set the result to the position of this delivered frame.
                TaskCompletionSource<TimeSpan>? tcs = null;
                lock (frameWaitLock)
                {
                    tcs = nextFrameTcs;
                    nextFrameTcs = null;
                }

                // Complete the wait for frame availability with the position of the delivered frame.
                tcs?.TrySetResult(position);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnVideoFrameAvailable failed CopyFrameToVideoSurface: {ex.Message}");

                // If there was an error during frame copy, complete any pending wait for frame availability
                // with an error to unblock the waiting code and allow it to handle the failure.
                TaskCompletionSource<TimeSpan>? tcs = null;
                lock (frameWaitLock)
                {
                    tcs = nextFrameTcs;
                    nextFrameTcs = null;
                }

                // Complete the wait for frame availability with the exception.
                tcs?.TrySetException(ex);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private TaskCompletionSource<TimeSpan> ArmNextFrameWait()
        {
            TaskCompletionSource<TimeSpan> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (frameWaitLock)
            {
                nextFrameTcs = tcs;
            }

            return tcs;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        private async Task<(bool ok, TimeSpan position)> WaitForNextFrameAsync(string context, TimeSpan timeout)
        {
            TaskCompletionSource<TimeSpan> tcs = ArmNextFrameWait();

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            if (completed != tcs.Task)
            {
                Debug.WriteLine($"{context} did not trigger frame availability within timeout.");
                return (false, TimeSpan.Zero);
            }

            TimeSpan framePosition = await tcs.Task;
            Debug.WriteLine($"{context} triggered frame at: {framePosition.TotalSeconds:F2}");
            return (true, framePosition);
        }


        /// <summary>
        /// Copies pixel data from the current <see cref="CanvasBitmap"/> frame buffer and writes it
        /// into the provided <see cref="WriteableBitmap"/> for preview/display.
        /// The frame source is populated via <see cref="MediaPlayer.CopyFrameToVideoSurface"/>.
        /// </summary>
        /// <param name="writeableBitmap"></param>
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
    }



    public class CroppingMargin
    {
        // Size around crop for a surveyPoint
        private int xSpaceAroundPoint = 60;
        private int ySpaceAroundPoint = 60;

        // Fish sizing margin table
        private sealed record CroppingMarginSizingItem(double measurementGreaterThan, int margin);

        private readonly SortedList<double, CroppingMarginSizingItem> sizingTable = [];

        // Fish species margin table
        private sealed record CroppingMarginScientificItem(string scientificName, int margin);

        private readonly SortedList<string, CroppingMarginScientificItem> speciesTable = [];

        // Fish genus margin table
        private readonly SortedList<string, CroppingMarginScientificItem> genusTable = [];

        // Fish family margin table
        private readonly SortedList<string, CroppingMarginScientificItem> familyTable = [];


        public CroppingMargin()
        {

        }


        /// <summary>
        /// Get the margin around a survey point when cropping an image. 
        /// This margin is used to ensure that there is enough space around 
        /// the survey point in the cropped image for display purposes. The 
        /// xSpace parameter is used to determine how much space to leave 
        /// on the x axis when cropping the image, while the ySpace parameter 
        /// is used to determine how much space to leave on the y axis when 
        /// cropping the image.
        /// </summary>
        /// <param name="xSpace"></param>
        /// <param name="ySpace"></param>
        public void AddPointSpacing(int xSpace, int ySpace)
        {
            xSpaceAroundPoint = xSpace;
            ySpaceAroundPoint = ySpace;
        }

        /// <summary>
        /// Add a sizing item to the cropping margin sizing table. The measurementGreaterThan parameter is 
        /// used to determine which sizing item to use based on the size of the image being cropped. 
        /// The xSpace and ySpace parameters are used to determine how much space to leave on the x and y
        /// axes when cropping the image. When determining which sizing item to use, the table will be 
        /// searched for the largest measurementGreaterThan value that is less than or equal to the size 
        /// of the image being cropped. The corresponding xSpace and ySpace values will then be used for cropping.
        /// </summary>
        /// <param name="measurementGreaterThen"></param>
        /// <param name="margin"></param>
        public void AddSizingTableItem(double measurementGreaterThen, int margin)
        {
            sizingTable.Add(measurementGreaterThen, new CroppingMarginSizingItem(measurementGreaterThen, margin));
        }


        /// <summary>
        /// Add and margin required around a particular species. The scientificName parameter 
        /// is used to determine which species item to use based on the species of the image 
        /// being cropped.
        /// </summary>
        /// <param name="scientificSpeciesName"></param>
        /// <param name="xSpace"></param>
        /// <param name="ySpace"></param>
        public void AddSpeciesTableItem(string scientificSpeciesName, int spaceAround)
        {
            speciesTable.Add(scientificSpeciesName, new CroppingMarginScientificItem(scientificSpeciesName, spaceAround));
        }


        /// <summary>
        /// Add and margin required around a particular genus. The scientificName parameter 
        /// is used to determine which species item to use based on the species of the image 
        /// being cropped.
        /// </summary>
        /// <param name="scientificGenusName"></param>
        /// <param name="xSpace"></param>
        /// <param name="ySpace"></param>
        public void AddGenusTableItem(string scientificGenusName, int spaceAround)
        {
            genusTable.Add(scientificGenusName, new CroppingMarginScientificItem(scientificGenusName, spaceAround));
        }


        /// <summary>
        /// Add and margin required around a particular family. The scientificName parameter 
        /// is used to determine which species item to use based on the species of the image 
        /// being cropped.
        /// </summary>
        /// <param name="scientificFamilyName"></param>
        /// <param name="xSpace"></param>
        /// <param name="ySpace"></param>
        public void AddFamilyTableItem(string scientificFamilyName, int spaceAround)
        {
            familyTable.Add(scientificFamilyName, new CroppingMarginScientificItem(scientificFamilyName, spaceAround));
        }


        /// <summary>
        /// Return the cropping margin for the given measurement and/or scientific names. 
        /// The measurement parameter is used to determine which sizing item to use from 
        /// the sizing table. The familyScientific, genus, and speciesScientific parameters
        /// are used to determine which scientific item to use from the family, genus, and 
        /// species tables respectively. The function will first check the species table for 
        /// a matching scientific name, then the genus table, then the family table, and finally
        /// the sizing table if no matches are found in the scientific tables. If no matches are
        /// found in any of the tables, a default margin of 60 pixels on both axes will be returned.
        /// </summary>
        /// <param name="measurement"></param>
        /// <param name="familyScientific"></param>
        /// <param name="genus"></param>
        /// <param name="speciesScientific"></param>
        /// <returns>space around box</returns>
        public int GetCroppingMarginMeasurement(double? measurement, string? familyScientific, string? genus, string? speciesScientific)
        {
            // Match on the species table first
            if (speciesScientific is not null && speciesTable.Count > 0)
            {
                if (speciesTable.TryGetValue(speciesScientific, out CroppingMarginScientificItem? speciesItem))
                {
                    return speciesItem.margin;
                }
            }

            // Match on the genus table next
            if (genus is not null && genusTable.Count > 0)
            {
                if (genusTable.TryGetValue(genus, out CroppingMarginScientificItem? genusItem))
                {
                    return genusItem.margin;
                }
            }

            // Match on the family table next
            if (familyScientific is not null && familyTable.Count > 0)
            {
                if (familyTable.TryGetValue(familyScientific, out CroppingMarginScientificItem? familyItem))
                {
                    return familyItem.margin;
                }
            }

            // Match on the sizing table last (based on the length of the fish in the
            // image). The sizing table is searched for the largest measurementGreaterThan
            // value that is less than or equal to the measurement parameter.
            // The corresponding xSpace and ySpace values are then returned.
            if (measurement is not null && sizingTable.Count > 0)
            {
                foreach (var item in sizingTable.Values)
                {
                    if (measurement <= item.measurementGreaterThan)
                        return item.margin;
                }
            }

            // Default to the margin used for a surveyPoint
            return xSpaceAroundPoint;
        }


        /// <summary>
        /// Used to return margin for the survey point or SurveyStereoPoint
        /// The distance is not currently supported but could be used to adjust the
        /// margin based on the distance the point is away from the camera, with points 
        /// further away potentially requiring a smaller margin because the fish will be smaller.
        /// </summary>
        /// <param name="distance"></param>
        /// <returns></returns>
        public (int xSpace, int ySpace) GetCroppingMarginPoint(double? distance, string? familyScientific, string? genus, string? speciesScientific)
        {
            return (xSpaceAroundPoint, ySpaceAroundPoint);
        }


        ///
        /// PRIVATE
        /// 
    }

}
