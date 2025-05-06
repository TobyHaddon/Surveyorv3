//
// Version 1.0  03 Jan 2025
//
// Version 1.1 12 Feb 2025
// Added ExtractProperties(...)
// Added ExtractPropertiesDuration(...)

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace GoProMP4MetadataExtraction
{
    public static class GetMP4FileProperities
    {
        /// <summary>
        /// Retreive the file properties of a video file used StorageFile
        /// </summary>
        /// <param name="videoFile"></param>
        /// <returns></returns>
        public static async Task<Dictionary<string, string>> ExtractProperties(StorageFile videoFile)
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


        /// <summary>
        /// Retreive the file properties of a video file using a file path
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public static async Task<Dictionary<string, string>> ExtractProperties(string fileSpec)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(fileSpec);

            // You can now use the `file` object to read or manipulate the file
            return await GetMP4FileProperities.ExtractProperties(file);
        }


        /// <summary>
        /// Extract the duration of a video file
        /// </summary>
        /// <param name="videoFile"></param>
        /// <returns></returns>
        public static async Task<TimeSpan?> ExtractPropertiesDuration(StorageFile videoFile)
        {
            TimeSpan? duration = null;

            Dictionary<string, string> metadata = await ExtractProperties(videoFile);

            if (metadata is not null)
            {
                string durationString = metadata["Video.Duration"];

                if (TimeSpan.TryParse(durationString, out TimeSpan durationValue))
                {
                    duration = durationValue;
                }
            }

            return duration;
        }


        /// <summary>
        /// Extract the duration of a video file
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public static async Task<TimeSpan?> ExtractPropertiesDuration(string fileSpec)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(fileSpec);

            // You can now use the `file` object to read or manipulate the file
            return await GetMP4FileProperities.ExtractPropertiesDuration(file);
        }
    }
}
