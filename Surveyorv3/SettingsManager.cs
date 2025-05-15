// Handles the local per user settings and the shipped application settings
// class SettingsManagerLocal is for the local settings that are stored on the user's device
// class SettingsManagerApp is for the application settings that are shipped with the application and are read-only
// Note settings from SettingsManagerApp should be remembered and not learnt repeatedly from SettingsManagerApp 
// This is because the whole appSettings.json is loaded each time and it is not efficient to read it repeatedly
//
// Version 1.0
// Verison 1.1  26 Feb 2025
// Added SettingsManagerApp 

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using Windows.Storage;
using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.IO;

namespace Surveyor
{
    /// <summary>
    /// Handle the application local user settings 
    /// </summary>
    public class SettingsManagerLocal
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


        // Retrieve or set the survey folder path.  This is where the Survey files and the media files are stored.
        public static string? SurveyFolder
        {
            get
            {
                // Accessing the local settings storage
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

                // Retrieve the setting, or provide a default value if not present
                return localSettings.Values["SurveyFolder"] as string;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

                // Save the value in the settings container
                localSettings.Values["SurveyFolder"] = value;
            }
        }

        // Path where media frames are saved to
        public static string? MediaFrameFolder
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["MediaFrameFolder"] is not string mediaFrameFolder)
                    mediaFrameFolder = SurveyFolder + "\\MediaFrames";

                return mediaFrameFolder;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["MediaFrameFolder"] = value;
            }
        }

        // Display Pointer Coordinates on screen
        public static bool DiagnosticInformation
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["DiagnosticInformation"] is not bool displayPointerCoordinates)
                    displayPointerCoordinates = false;          // Default telemetry to off

                return displayPointerCoordinates;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["DiagnosticInformation"] = value;
            }
        }


        // Telemetry can be automatically uploaded
        public static bool TelemetryEnabled
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["Telemetry"] is not bool telemetryEnabled)
                    telemetryEnabled = true;        // Default telemetry to on

                return telemetryEnabled;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["Telemetry"] = value;
            }
        }


        // Experimental features can be used
        public static bool ExperimentalEnabled
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["Experimental"] is not bool experimentalEnabled)
                    experimentalEnabled = false;        // Default experimental codeto off

                return experimentalEnabled;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["Experimental"] = value;
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

       
        // User name 
        public static string? UserName
        {
            get
            {
                // Accessing the local settings storage
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

                // Retrieve the setting, or provide a default value if not present
                return localSettings.Values["UserName"] as string;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

                // Save the value in the settings container
                localSettings.Values["UserName"] = value;
            }
        }

        // Teaching Tips Enabled
        public static bool TeachingTipsEnabled
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["TeachingTipsEnabled"] is not bool teachingTipsEnabled)
                    teachingTipsEnabled = false;     // Default teaching tip to disabled

                return teachingTipsEnabled;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["TeachingTipsEnabled"] = value;
            }
        }


        /// <summary>
        /// Teaching tip control
        /// </summary>
        private const string TeachingTipShownKey = "TeachingTipShown";
        public static bool HasTeachingTipBeenShown(string teachingTipName)
        {
            // Retrieve the flag from local settings
            var localSettings = ApplicationData.Current.LocalSettings;
            string key = TeachingTipShownKey + teachingTipName;
            return localSettings.Values.ContainsKey(key) &&
                   (bool)localSettings.Values[key];
        }

        public static void SetTeachingTipShown(string teachingTipName)
        {
            // Save the flag in local settings
            var localSettings = ApplicationData.Current.LocalSettings;
            string key = TeachingTipShownKey + teachingTipName;
            localSettings.Values[key] = true;
        }



        /// <summary>
        /// Used to remove all the TeachingTipShownXXXX values so the teaching tip are shown again
        /// </summary>
        public static void RemoveAllTeachingTipShown()
        {
            // Get the local settings container
            var localSettings = ApplicationData.Current.LocalSettings;

            // Create a list to store keys that need to be removed
            List<string> keysToRemove = [];

            // Iterate through all settings
            foreach (var key in localSettings.Values.Keys)
            {
                // Check if the key starts with "TeachingTipShown"
                if (key.StartsWith("TeachingTipShown"))
                {
                    // Add the key to the removal list
                    keysToRemove.Add(key);
                }
            }

            // Remove the settings with the identified keys
            foreach (var key in keysToRemove)
            {
                localSettings.Values.Remove(key);
            }
        }

        /// <summary>
        /// Internet enable flag
        /// </summary>
        public static bool UseInternetEnabled
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["UseInternetEnabled"] is not bool useInternetEnabled)
                    useInternetEnabled = false;         // Default telemetry to off

                return useInternetEnabled;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["UseInternetEnabled"] = value;
            }
        }


        /// <summary>
        /// Auto Save enabled flag
        /// </summary>
        public static bool AutoSaveEnabled
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["AutoSaveEnabled"] is not bool autoSaveEnabled)
                    autoSaveEnabled = true;             // Default telemetry to on

                return autoSaveEnabled;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["AutoSaveEnabled"] = value;
            }
        }


        /// <summary>
        /// Use the species image cache to download stock fish species images to help fish ID
        /// </summary>
        public static bool SpeciesImageCacheEnabled
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["SpeciesImageCacheEnabled"] is not bool speciesImageCacheEnabled)
                    speciesImageCacheEnabled = true;            // Default telemetry to on

                return speciesImageCacheEnabled;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["SpeciesImageCacheEnabled"] = value;
            }
        }


        /// <summary>
        /// Species code list order can be by common name or scientific name
        /// </summary>
        public static bool ScientificNameOrderEnabled
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values["ScientificNameOrderEnabled"] is not bool scientificNameOrderEnabled)
                    scientificNameOrderEnabled = true;
                return scientificNameOrderEnabled;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["ScientificNameOrderEnabled"] = value;
            }
        }

    }

    public class SettingsManagerApp
    {
        // Singleton instance
        private static SettingsManagerApp? _instance;
        public static SettingsManagerApp Instance => _instance ??= Load();

        public int RecentSurveysDisplayed { get; set; }  // Only use the 'get' the 'set' is for the Json deserializer


        [JsonConverter(typeof(KeyValuePairListJsonConverter))]
        public List<(string, string)> GoProScripts { get; set; } = [];  // Only use the 'get' the 'set' is for the Json deserializer



        ///
        /// PRIVATE
        ///


        /// <summary>
        /// Load the application settings
        /// </summary>

        private static readonly string SettingsFilePath = Path.Combine(AppContext.BaseDirectory, "appSettings.json");
        private static SettingsManagerApp Load()
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<SettingsManagerApp>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SettingsManagerApp();
            }
            else
            {
                throw new FileNotFoundException($"Settings file not found: {SettingsFilePath}");
            }
        }
    }


    /// <summary>
    /// Custom JSON converter for KeyValuePairList
    /// </summary>
    public class KeyValuePairListJsonConverter : JsonConverter<List<(string, string)>>
    {
        public override List<(string, string)> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var list = new List<(string, string)>();
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() == 2)
                    {
                        string key = element[0].GetString() ?? string.Empty;
                        string value = element[1].GetString() ?? string.Empty;
                        list.Add((key, value));
                    }
                }
            }
            return list;
        }

        public override void Write(Utf8JsonWriter writer, List<(string, string)> value, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Settings are read-only. Writing to JSON is not allowed.");
        }
    }

}
