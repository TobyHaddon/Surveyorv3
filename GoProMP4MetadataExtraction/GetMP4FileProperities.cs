//
// Version 1.0  03 Jan 2025
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Media.MediaProperties;


using static GoProMP4MetadataExtraction.GetMP4FileProperities;

namespace GoProMP4MetadataExtraction
{
    public static class GetMP4FileProperities
    {
        // ...

        public static async Task<Dictionary<string, string>> ExtractPropertiesAsync(StorageFile videoFile)
        {
            ArgumentNullException.ThrowIfNull(videoFile);

            var metadata = new Dictionary<string, string>();

            try
            {
                // Retrieve system-level metadata
                var properties = await videoFile.Properties.RetrievePropertiesAsync(null);
                foreach (var prop in properties)
                {
                    metadata[prop.Key] = prop.Value?.ToString() ?? "N/A";
                }

                // Retrieve VideoProperties for additional details
                VideoProperties videoProperties = await videoFile.Properties.GetVideoPropertiesAsync();

                metadata["Video.Title"] = videoProperties.Title ?? "N/A";
                metadata["Video.Duration"] = videoProperties.Duration.ToString();
                metadata["Video.Bitrate"] = videoProperties.Bitrate.ToString();
                metadata["Video.Width"] = videoProperties.Width.ToString();
                metadata["Video.Height"] = videoProperties.Height.ToString();

                // Retrieve GPS or custom metadata for GoPro-specific MP4 files
                if (videoProperties.Keywords != null && videoProperties.Keywords.Count > 0)
                {
                    metadata["Keywords"] = string.Join(", ", videoProperties.Keywords);
                }

                // Retrieve Frame Rate using extended properties
                var propertiesExtended = await videoFile.Properties.RetrievePropertiesAsync(new string[]
                {
                    "System.Video.FrameRate" // Frame rate is stored in 100-nanosecond units
                });

                if (propertiesExtended.TryGetValue("System.Video.FrameRate", out object? frameRateObj) && frameRateObj is uint frameRate)
                {
                    metadata["Video.FrameRate"] = (frameRate / 1000.0).ToString("0.00"); // Convert to FPS
                }
            }
            catch (Exception ex)
            {
                metadata["Error"] = ex.Message;
            }

            return metadata;
        }
    }
}
