// Extracts thumbnails from video files for display in the UI.
//
// Version 1.1 22 Nov 2025
// Rename to GetFileThumbnailAsync and set default size to 64


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
        public static async Task<BitmapImage?> GetFileThumbnailAsync(string filePath, uint size = 64)
        {
            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);

                // Try multiple modes, fall back if one returns null
                StorageItemThumbnail? thumb =
                    await file.GetThumbnailAsync(ThumbnailMode.VideosView, size, ThumbnailOptions.UseCurrentScale)
                    ?? await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.UseCurrentScale)
                    ?? await file.GetThumbnailAsync(ThumbnailMode.PicturesView, size, ThumbnailOptions.UseCurrentScale);

                if (thumb is null) return null;

                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(thumb);
                return bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetFileThumbnailAsync: Error getting thumbnail: {ex.Message}");
                return null;
            }
        }    
    }
}
