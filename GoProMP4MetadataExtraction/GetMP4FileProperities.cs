//
// Version 1.0  03 Jan 2025
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.FileProperties;

using static GoProMP4MetadataExtraction.GetMP4FileProperities;

namespace GoProMP4MetadataExtraction
{
    public static class GetMP4FileProperities
    {
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
            }
            catch (Exception ex)
            {
                metadata["Error"] = ex.Message;
            }

            return metadata;
        }
    }
}
