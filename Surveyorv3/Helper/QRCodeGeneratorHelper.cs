using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace Surveyor.Helper
{
    public class QRCodeGeneratorHelper
    {
        public static async Task<SoftwareBitmapSource?> GenerateQRCodeAsync(string qrText)
        {
            SoftwareBitmapSource? ret = null;

            using (QRCodeGenerator qrGenerator = new())
            {
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrText, QRCodeGenerator.ECCLevel.Q);
                using QRCode qrCode = new(qrCodeData);
                using System.Drawing.Bitmap qrCodeImage = qrCode.GetGraphic(20);
                ret = await ConvertBitmapToImageSourceAsync(qrCodeImage);
            }

            return ret; 
        }


        private static async Task<SoftwareBitmapSource> ConvertBitmapToImageSourceAsync(Bitmap bitmap)
        {
            using MemoryStream memoryStream = new();
            bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
            memoryStream.Position = 0;

            // Decode the image
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(memoryStream.AsRandomAccessStream());
            SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,  // Ensure correct format
                BitmapAlphaMode.Premultiplied // Ensure correct alpha mode
            );

            if (softwareBitmap == null || softwareBitmap.PixelWidth == 0 || softwareBitmap.PixelHeight == 0)
            {
                throw new ArgumentException("Invalid image data: The software bitmap is null or has zero dimensions.");
            }

            SoftwareBitmapSource bitmapSource = new();
            await bitmapSource.SetBitmapAsync(softwareBitmap);

            return bitmapSource;
        }

    }
}
