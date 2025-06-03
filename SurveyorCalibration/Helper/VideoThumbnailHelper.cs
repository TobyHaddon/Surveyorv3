using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Surveyor.Helper
{
    public static class VideoThumbnailHelper
    {
        public static async Task<BitmapImage?> GetBitmapImageFromVideoAsync(string filePath)
        {
            try
            {
                // Get the StorageFile from the file path
                var file = await StorageFile.GetFileFromPathAsync(filePath);

                // Request a thumbnail of the video
                var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem);

                if (thumbnail != null)
                {
                    // Create a BitmapImage from the thumbnail stream
                    BitmapImage bitmapImage = new BitmapImage();
                    using (var stream = thumbnail.AsStream())
                    {
                        await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                    }

                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file not found, no thumbnail available)
                Console.WriteLine($"Error getting thumbnail: {ex.Message}");
            }

            return null;
        }


    }
}
