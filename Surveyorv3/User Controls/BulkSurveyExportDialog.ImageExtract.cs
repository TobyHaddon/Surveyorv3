using Emgu.CV;
using Emgu.CV.CvEnum;
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

namespace Surveyor.User_Controls
{
    public class ImageExtract
    {
        public string ImagePath { get; set; } = string.Empty;

        // Media file files and handles
        private string mediaFileSpec = string.Empty;
        private VideoCapture? cap = null;

        // Writeable bitmaps for the left and right camera frames
        private Size frameSize = new(0.0, 0.0); // Size of the frames, used to create WriteableBitmaps
        private WriteableBitmap? wb = null;

        // Total frame count
        private TimeSpan totalFrames = TimeSpan.Zero;

        // Current frame indexes
        private TimeSpan currentFrame = TimeSpan.Zero;


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
            bool mediaOpened = false;

            // Reset
            Clear();

            // Open Left side
            if (File.Exists(fileSpec))
            {
                mediaFileSpec = fileSpec;

                // Open Left
                cap = new Emgu.CV.VideoCapture(mediaFileSpec);

                if (cap.IsOpened)
                {
                    // Get total number of frames
                    // Get media duration as TimeSpan (stored in totalFrames)
                    double frameCount = cap.Get(CapProp.FrameCount);
                    double fps = cap.Get(CapProp.Fps);

                    if (double.IsFinite(frameCount) && double.IsFinite(fps) && frameCount > 0 && fps > 0)
                    {
                        totalFrames = TimeSpan.FromSeconds(frameCount / fps);
                    }
                    else
                    {
                        totalFrames = TimeSpan.Zero;
                    }

                    using var testFrame = new Emgu.CV.Mat();
                    cap.Read(testFrame);

                    if (!testFrame.IsEmpty)
                    {
                        // Create WriteableBitmap with EMGU.CV frame dimensions
                        wb = new WriteableBitmap(testFrame.Width, testFrame.Height);
                        frameSize = new Size(testFrame.Width, testFrame.Height);

                        // Reset to first frame — EMGU.CV uses .Set() with CapProp
                        cap.Set(Emgu.CV.CvEnum.CapProp.PosFrames, 0);

                        currentFrame = TimeSpan.Zero;

                        mediaOpened = true;
                    }
                }
            }

            //???await Task.Delay(100); // Allow UI to update

            if (mediaOpened)
            {
                ret = 0;
            }

            return ret;
        }


        /// <summary>
        /// Close the media file
        /// </summary>
        /// <returns></returns>
        public int VideoClose()
        {
            int ret = -1;

            if (cap is not null && cap.IsOpened)
            {
                cap.Dispose();
                cap = null;
            }

            // Clear internals
            Clear();

            return ret;
        }

        /// <summary>
        /// Extract the frame from the indicated position in the video and save 
        /// it to an image file. The file spec of the saved image is returned 
        /// in the out parameter.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="imageFileSpec"></param>
        /// <returns></returns>
        public int VideoExtractFrame(TimeSpan position, out string imageFileSpec)
        {
            int ret = -1;
            imageFileSpec = string.Empty;

            if (string.IsNullOrWhiteSpace(ImagePath))
                return -1;

            if (cap is null || !cap.IsOpened || wb is null)
                return -1;

            try
            {
                // Ensure output folder exists
                Directory.CreateDirectory(ImagePath);

                // Clamp position to valid media range when known
                if (position < TimeSpan.Zero)
                    position = TimeSpan.Zero;

                if (totalFrames > TimeSpan.Zero && position > totalFrames)
                    position = totalFrames;

                // Seek using time (milliseconds)
                cap.Set(CapProp.PosMsec, position.TotalMilliseconds);

                using var mat = new Mat();
                cap.Read(mat);

                if (mat.IsEmpty)
                    return -1;

                // Optional UI preview
                DrawFrameToScreen(mat, wb);

                // Track current position
                currentFrame = position;

                // Build output filename
                string stem = Path.GetFileNameWithoutExtension(mediaFileSpec);
                string msToken = Math.Round(position.TotalMilliseconds, MidpointRounding.AwayFromZero).ToString("F0");
                imageFileSpec = Path.Combine(ImagePath, $"{stem}_{msToken}ms.png");

                // Save frame
                CvInvoke.Imwrite(imageFileSpec, mat);

                ret = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ToString()} VideoExtractFrame: {ex.Message}");
                ret = -1;
            }

            return ret;
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
        /// <param name="mediaFileSpec"></param>
        /// <returns></returns>
        public string MakeAndCreateFramesDirectoryAndEmpty(string mediaFileSpec)
        {
            string appTitle = AppInfo.Current.DisplayInfo.DisplayName;
            string documentsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), appTitle);

            string outputPath = MakeAndCreateFramesDirectory(documentsFolder, mediaFileSpec, false);

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
                        Debug.WriteLine($"{ToString()} MakeAndCreateFramesDirectoryAndEmpty: Failed to delete {file}: {ex.Message}");
                    }
                }
            }

            return outputPath;
        }


        /// <summary>
        /// Make the folder name to save the frames to, create the folder if necessary)
        /// </summary>
        /// <param name="fileSpecMP4"></param>
        /// <returns></returns>
        public string MakeAndCreateFramesDirectory(string basePath, string fileSpecMP4, bool trueRelativePathFalseAbsolute)
        {
            string outputFolder;

            // Make an output folder in the local folder (if necessary) based on the video name 
            string subfolderName = Path.GetFileNameWithoutExtension(fileSpecMP4);

            outputFolder = Path.Combine(basePath, subfolderName);

            if (!Directory.Exists(outputFolder))
            {
                // Create a folder
                try
                {
                    Directory.CreateDirectory(outputFolder);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{ToString()} MakeAndCreateFramesDirectory: Error creating save frame storage folder call: [{subfolderName}] inside: [{outputFolder}], {ex.Message}");
                }
            }

            return outputFolder;
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Clear internal data and state
        /// </summary>
        private void Clear()
        {
            frameSize = new(0.0, 0.0);
            wb = null;

            // Reset total frame counts
            totalFrames = TimeSpan.Zero;

            // Reset current frame indexes
            currentFrame = TimeSpan.Zero;

            mediaFileSpec = string.Empty;
            cap = null;

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
        private int drawFrameToScreenEntryCount = 0;
        private byte[]? _frameCopyBuffer;
        private void DrawFrameToScreen(Mat frame, WriteableBitmap wb)
        {
            int entryCount = Interlocked.Increment(ref drawFrameToScreenEntryCount);
            if (entryCount != 1)
            {
                Interlocked.Decrement(ref drawFrameToScreenEntryCount);
                return;
            }

            if (frame.IsEmpty || wb == null) return;

            try
            {
                using var bgraFrame = new Mat();
                CvInvoke.CvtColor(frame, bgraFrame, Emgu.CV.CvEnum.ColorConversion.Bgr2Bgra);

                if (wb.PixelWidth != bgraFrame.Width || wb.PixelHeight != bgraFrame.Height)
                {
                    Debug.WriteLine($"{ToString()} Warning: DrawFrameToScreen  Frame dimensions {bgraFrame.Width}x{bgraFrame.Height} " +
                                    $"don't match WriteableBitmap {wb.PixelWidth}x{wb.PixelHeight}");
                    return;
                }

                int byteCount = bgraFrame.Rows * bgraFrame.Cols * bgraFrame.ElementSize;
                if (_frameCopyBuffer == null || _frameCopyBuffer.Length != byteCount)
                    _frameCopyBuffer = new byte[byteCount];

                Marshal.Copy(bgraFrame.DataPointer, _frameCopyBuffer, 0, byteCount);

                using var stream = wb.PixelBuffer.AsStream();
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(_frameCopyBuffer, 0, byteCount);

                wb.Invalidate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ToString()} DrawFrameToScreen: Error drawing frame: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref drawFrameToScreenEntryCount);
            }
        }
    }
}
