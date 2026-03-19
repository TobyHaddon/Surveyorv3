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
    using Windows.Foundation;
    using Windows.Graphics.Imaging;
    using Windows.Storage.Streams;



    public static class WriteableBitmapHelper
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


        /// <summary>
        /// Save to disk the current WriteableBitmap as a PNG file. 
        /// This method encodes the bitmap's pixel data
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static async Task SaveAsync(this WriteableBitmap bitmap, string filePath)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));

            string? folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            byte[] pixels;
            using (Stream pixelStream = bitmap.PixelBuffer.AsStream())
            {
                pixelStream.Seek(0, SeekOrigin.Begin);
                pixels = new byte[pixelStream.Length];

                int offset = 0;
                while (offset < pixels.Length)
                {
                    int read = await pixelStream.ReadAsync(pixels, offset, pixels.Length - offset);
                    if (read == 0)
                        break;
                    offset += read;
                }

                if (offset != pixels.Length)
                    throw new InvalidOperationException("Failed to read full pixel buffer.");
            }

            using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var ras = fs.AsRandomAccessStream();

            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth,
                (uint)bitmap.PixelHeight,
                96,
                96,
                pixels);

            await encoder.FlushAsync();
            await fs.FlushAsync();
        }

        public static WriteableBitmap Crop(WriteableBitmap source, Rect rect)
        {
            ArgumentNullException.ThrowIfNull(source);

            int sourceWidth = source.PixelWidth;
            int sourceHeight = source.PixelHeight;

            if (sourceWidth <= 0 || sourceHeight <= 0)
                throw new InvalidOperationException("Source bitmap has invalid dimensions.");

            // Clamp requested crop rect to source bounds
            int x = Math.Max(0, (int)Math.Floor(rect.X));
            int y = Math.Max(0, (int)Math.Floor(rect.Y));
            int right = Math.Min(sourceWidth, (int)Math.Ceiling(rect.X + rect.Width));
            int bottom = Math.Min(sourceHeight, (int)Math.Ceiling(rect.Y + rect.Height));

            int cropWidth = right - x;
            int cropHeight = bottom - y;

            if (cropWidth <= 0 || cropHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(rect), "Crop rectangle is outside source bitmap bounds.");

            int sourceStride = sourceWidth * 4; // BGRA8
            int cropStride = cropWidth * 4;

            byte[] sourcePixels;
            using (Stream sourceStream = source.PixelBuffer.AsStream())
            {
                sourcePixels = new byte[sourceStream.Length];
                sourceStream.Seek(0, SeekOrigin.Begin);

                int offset = 0;
                while (offset < sourcePixels.Length)
                {
                    int read = sourceStream.Read(sourcePixels, offset, sourcePixels.Length - offset);
                    if (read == 0)
                        break;

                    offset += read;
                }

                if (offset != sourcePixels.Length)
                    throw new InvalidOperationException("Failed to read source bitmap pixels.");
            }

            byte[] cropPixels = new byte[cropHeight * cropStride];

            for (int row = 0; row < cropHeight; row++)
            {
                int sourceOffset = ((y + row) * sourceStride) + (x * 4);
                int cropOffset = row * cropStride;

                System.Buffer.BlockCopy(sourcePixels, sourceOffset, cropPixels, cropOffset, cropStride);
            }

            WriteableBitmap cropped = new(cropWidth, cropHeight);

            using (Stream cropStream = cropped.PixelBuffer.AsStream())
            {
                cropStream.Seek(0, SeekOrigin.Begin);
                cropStream.Write(cropPixels, 0, cropPixels.Length);
                cropStream.Flush();
            }

            cropped.Invalidate();
            return cropped;
        }
    }
}
