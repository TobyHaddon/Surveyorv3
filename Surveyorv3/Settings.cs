using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace Surveyor
{
    internal class SettingsManager
    {
        // Path where new media (MP4) are typically imported from
        public static string? MediaImportFolder
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                return localSettings.Values["MediaImportFolder"] as string;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["MediaImportFolder"] = value;
            }
        }


        // Path where new calibration files are typically imported from
        public static string? CalibrationImportFolder
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                return localSettings.Values["CalibrationImportFolder"] as string;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["CalibrationImportFolder"] = value;
            }
        }


        // Retrieve or set the project folder path.  This is where the Survey files and the media files are stored.
        public static string? ProjectFolder
        {
            get
            {
                // Accessing the local settings storage
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

                // Retrieve the setting, or provide a default value if not present
                return localSettings.Values["ProjectFolder"] as string;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

                // Save the value in the settings container
                localSettings.Values["ProjectFolder"] = value;
            }
        }

        // Path where media frames are saved to
        public static string? MediaFrameFolder
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                string? mediaFrameFolder = localSettings.Values["MediaFrameFolder"] as string;
                if (mediaFrameFolder is null)
                    mediaFrameFolder = ProjectFolder + "\\MediaFrames";

                return mediaFrameFolder;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["MediaFrameFolder"] = value;
            }
        }
    }
}
