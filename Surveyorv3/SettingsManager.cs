using Microsoft.UI.Xaml;
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
                if (localSettings.Values["MediaFrameFolder"] is not string mediaFrameFolder)
                    mediaFrameFolder = ProjectFolder + "\\MediaFrames";

                return mediaFrameFolder;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["MediaFrameFolder"] = value;
            }
        }

        // Display Pointer Coordinates on screen
        public static bool DisplayPointerCoordinates
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["DisplayPointerCoordinates"] is not bool displayPointerCoordinates)
                    displayPointerCoordinates = false;

                return displayPointerCoordinates;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["DisplayPointerCoordinates"] = value;
            }
        }

        // Application theme Light, Dark or Default
        public static ElementTheme ApplicationTheme
        {
            get 
            {
                ElementTheme applicationTheme = ElementTheme.Default;

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["ApplicationTheme"] is string)
                {
                    string applicationThemeName = (string)localSettings.Values["ApplicationTheme"];

                    if (applicationThemeName == "Dark")
                        applicationTheme = ElementTheme.Dark;
                    else if (applicationThemeName == "Light")
                        applicationTheme = ElementTheme.Light;
                }

                return applicationTheme;
            }
            set
            {
                string applicationThemeName = "Default";

                if (value == ElementTheme.Dark)
                    applicationThemeName = "Dark";
                else if (value == ElementTheme.Light)
                    applicationThemeName = "Light";

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["ApplicationTheme"] = applicationThemeName;
            }
        }
    }
}
