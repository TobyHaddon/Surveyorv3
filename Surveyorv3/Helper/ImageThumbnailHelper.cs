// Used to create a reduced size thumbnail of the image for display. 
// Version 1.0  15 Mar 2026


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Surveyor.Helper
{
    using Microsoft.UI.Xaml.Media.Imaging;
    using System;
    using System.IO;
    using System.Runtime.InteropServices.WindowsRuntime;
    using System.Threading.Tasks;
    using Windows.Graphics.Imaging;
    using Windows.Storage.Streams;



    public static class BitmapThumbnailHelper
    {
        /// <summary>
        /// Used to create a reduced size thumbnail of the image for display in the export dialog. 
        /// The thumbnail is created by encoding the current WriteableBitmap to an in-memory stream 
        /// and then decoding it with scaling to the desired thumbnail size. This approach preserves 
        /// the aspect ratio and provides good quality thumbnails without needing to manually 
        /// resample the pixel data.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="maxWidth"></param>
        /// <param name="maxHeight"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static async Task<WriteableBitmap> CreateThumbnailAsync(
            WriteableBitmap source,
            int maxWidth,
            int maxHeight)
        {
            ArgumentNullException.ThrowIfNull(source);

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);

            int sourceWidth = source.PixelWidth;
            int sourceHeight = source.PixelHeight;

            if (sourceWidth <= 0 || sourceHeight <= 0)
                throw new InvalidOperationException("Source bitmap has invalid dimensions.");

            // Preserve aspect ratio
            double scale = Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight);
            scale = Math.Min(scale, 1.0); // do not upscale

            int thumbWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int thumbHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

            // Read source pixels from WriteableBitmap.PixelBuffer
            byte[] sourcePixels;
            using (Stream pixelStream = source.PixelBuffer.AsStream())
            {
                sourcePixels = new byte[pixelStream.Length];
                int bytesRead = await pixelStream.ReadAsync(sourcePixels, 0, sourcePixels.Length);
                if (bytesRead != sourcePixels.Length)
                    throw new InvalidOperationException("Failed to read all source bitmap pixels.");
            }

            // Put source pixels into an in-memory encoded bitmap
            using InMemoryRandomAccessStream encodedStream = new();

            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, encodedStream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)sourceWidth,
                (uint)sourceHeight,
                96,
                96,
                sourcePixels);

            await encoder.FlushAsync();

            // Decode with scaling
            encodedStream.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(encodedStream);

            PixelDataProvider pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform
                {
                    ScaledWidth = (uint)thumbWidth,
                    ScaledHeight = (uint)thumbHeight,
                    InterpolationMode = BitmapInterpolationMode.Fant
                },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            byte[] thumbPixels = pixelData.DetachPixelData();

            // Create the thumbnail WriteableBitmap
            WriteableBitmap thumbnail = new(thumbWidth, thumbHeight);

            using (Stream thumbStream = thumbnail.PixelBuffer.AsStream())
            {
                await thumbStream.WriteAsync(thumbPixels, 0, thumbPixels.Length);
                await thumbStream.FlushAsync();
            }

            thumbnail.Invalidate();
            return thumbnail;
        }
    }
}
