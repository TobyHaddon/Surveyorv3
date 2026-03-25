using ActionCameraMP4MetadataExtraction;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
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


        public ImageExtract()
        {

        }


        /// <summary>
        /// Open the media file
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public async Task<int> VideoOpenAsync(string fileSpec)
        {
            int ret = -1;

            await mediaGate.WaitAsync();
            try
            {
                // Reset
                Clear();

                if (!File.Exists(fileSpec))
                    return -1;

                mediaFileSpec = fileSpec;


                // Get the .MP4 file properties to determine the frame rate.
                // If we fail to get the properties or parse the frame rate, we will use the default frame step value.
                Dictionary<string, string> fileProperties = await GetMP4FileProperities.ExtractProperties(fileSpec);

                if (fileProperties.TryGetValue("Video.FrameRate", out string? frameRate))
                {
                    frameStep = TimeSpan.FromMilliseconds(Double.Parse(frameRate));
                }


                // Create a dedicated MediaPlayer instance configured for frame extraction (not playback).
                MediaPlayer mp = new()
                {
                    AutoPlay = false,
                    IsMuted = true,
                    IsVideoFrameServerEnabled = true,
                    Source = null
                };

                // We block until MediaOpened/MediaFailed is raised (with timeout).
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

                // Start opening media.
                mp.Source = MediaSource.CreateFromUri(new Uri(mediaFileSpec));

                // Wait up to 10s for open result.
                bool opened = openEvent.Wait(TimeSpan.FromSeconds(10));

                // Always unhook temporary handlers.
                mp.MediaOpened -= OnMediaOpened;
                mp.MediaFailed -= OnMediaFailed;

                // Open failed or timed out.
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

                uint frameWidth = mp.PlaybackSession.NaturalVideoWidth;
                uint frameHeight = mp.PlaybackSession.NaturalVideoHeight;
                if (frameWidth == 0 || frameHeight == 0)
                {
                    mp.Dispose();
                    return -1;
                }

                // Cache media duration (named totalFrames in existing code).
                totalFrames = mp.PlaybackSession.NaturalDuration;
                if (totalFrames < TimeSpan.Zero)
                    totalFrames = TimeSpan.Zero;

                // Allocate frame capture/render resources for BGRA frame copies.
                canvasDevice = CanvasDevice.GetSharedDevice();
                frameServerDest = new SoftwareBitmap(BitmapPixelFormat.Bgra8, (int)frameWidth, (int)frameHeight, BitmapAlphaMode.Premultiplied);
                inputBitmap = CanvasBitmap.CreateFromSoftwareBitmap(canvasDevice, frameServerDest);

                wb = new WriteableBitmap((int)frameWidth, (int)frameHeight);
                frameSize = new Size(frameWidth, frameHeight);
                currentFrame = TimeSpan.Zero;

                // Promote local player to field only after successful setup.
                mediaPlayer = mp;

                // Prime by capturing first frame at time zero so downstream callers have valid buffers.
                if (!TryCaptureFrameAtPosition(TimeSpan.Zero, out TimeSpan actual))
                {
                    CloseInternalNoLock();
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
            if (string.IsNullOrWhiteSpace(ImagePath) && string.IsNullOrEmpty(exportFileSpec))
                return (-1, imageFileSpecList);

            if (extractBefore > 0 || extractAfter < 0)
                throw new ArgumentException("VideoExtractFramesAsync: extractBefore must be <= 0 and extractAfter must be >= 0");
            
            if (exportFileSpec is not null && (extractBefore != 0 || extractAfter != 0))
                throw new ArgumentException("VideoExtractFramesAsync: exportFileSpec can only be specified when extractBefore and extractAfter are both zero.");

            await mediaGate.WaitAsync();
            try
            {
                if (mediaPlayer is null || inputBitmap is null || wb is null)
                    return (-1, imageFileSpecList);
              
                //???Directory.CreateDirectory(ImagePath);

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

                if (!TryCaptureFrameAtPosition(clamped, out TimeSpan actualPosition))
                    return null;

                currentFrame = actualPosition;
                DrawFrameToScreen(wb);

                //???string stem = Path.GetFileNameWithoutExtension(mediaFileSpec);
                //string msToken = Math.Round(actualPosition.TotalMilliseconds, MidpointRounding.AwayFromZero).ToString("F0");
                //string imageFileSpec = exportFileSpec ?? Path.Combine(ImagePath, $"{stem}_{msToken}ms.png");

                string formattedTime = "0000" + $"{Math.Round(position.TotalSeconds, 2):F2}";
                string fileName = Path.GetFileNameWithoutExtension(mediaFileSpec) + $"_P.{formattedTime[Math.Max(0, formattedTime.Length - 12)..]}s.png";
                string imageFileSpec = exportFileSpec ?? Path.Combine(ImagePath, fileName);

                await inputBitmap!.SaveAsync(imageFileSpec, CanvasBitmapFileFormat.Png); 

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

            if (ret == 0 && imageFileSpecList.Count == 1 && !string.IsNullOrEmpty(imageFileSpecList[0]))
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


        private int CloseInternalNoLock()
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
