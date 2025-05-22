// Handles the local per user settings and the shipped application settings
// class SettingsManagerLocal is for the local settings that are stored on the user's device
// class SettingsManagerApp is for the application settings that are shipped with the application and are read-only
// Note settings from SettingsManagerApp should be remembered and not learnt repeatedly from SettingsManagerApp 
// This is because the whole appSettings.json is loaded each time and it is not efficient to read it repeatedly
//
// Version 1.0
// Verison 1.1  26 Feb 2025
// Added SettingsManagerApp 
// Version 1.2  21 May 2025
// Simplified the code 


using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace Surveyor
{
    /// <summary>
    /// Handle the application local user settings 
    /// </summary>
    public class SettingsManagerLocal
    {
        private static readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;
        private const string MediaImportFolderKey = "MediaImportFolder";
        private const string CalibrationImportFolderKey = "CalibrationImportFolder";
        private const string SurveyFolderKey = "SurveyFolder";
        private const string MediaFrameFolderKey = "MediaFrameFolder";
        private const string DiagnosticInformationKey = "DiagnosticInformation";
        private const string TelemetryKey = "Telemetry";
        private const string ExperimentalKey = "Experimental";
        private const string ApplicationThemeKey = "ApplicationTheme";
        private const string UserNameKey = "UserName";
        private const string TeachingTipsEnabledKey = "TeachingTipsEnabled";
        private const string UseInternetEnabledKey = "UseInternetEnabled";
        private const string AutoSaveEnabledKey = "AutoSaveEnabled";
        private const string SpeciesImageCacheEnabledKey = "SpeciesImageCacheEnabled";
        private const string ScientificNameOrderEnabledKey = "ScientificNameOrderEnabled";


        // Path where new media (MP4) are typically imported from
        public static string? MediaImportFolder
        {
            get => GetString(MediaImportFolderKey);
            set => SetString(MediaImportFolderKey, value);
        }


        // Path where new calibration files are typically imported from
        public static string? CalibrationImportFolder
        {
            get => GetString(CalibrationImportFolderKey);
            set => SetString(CalibrationImportFolderKey, value);
        }


        // Retrieve or set the survey folder path.  This is where the Survey files and the media files are stored.
        public static string? SurveyFolder
        {
            get => GetString(SurveyFolderKey);
            set => SetString(SurveyFolderKey, value);
        }

        // Path where media frames are saved to
        public static string? MediaFrameFolder
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values[MediaFrameFolderKey] is not string mediaFrameFolder)
                    mediaFrameFolder = SurveyFolder + "\\MediaFrames";

                return mediaFrameFolder;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[MediaFrameFolderKey] = value;
            }
        }

        // Display Pointer Coordinates on screen
        public static bool DiagnosticInformation
        {
            get => GetBool(DiagnosticInformationKey, false/*default*/);
            set => SetBool(DiagnosticInformationKey, value);
        }


        // Telemetry can be automatically uploaded
        public static bool TelemetryEnabled
        {
            get => GetBool(TelemetryKey, true/*default*/);
            set => SetBool(TelemetryKey, value);
        }


        // Experimental features can be used
        public static bool ExperimentalEnabled
        {
            get => GetBool(ExperimentalKey, false/*default*/);
            set => SetBool(ExperimentalKey, value);
        }



        // Application theme Light, Dark or Default
        public static ElementTheme ApplicationTheme
        {
            get 
            {
                ElementTheme applicationTheme = ElementTheme.Default;

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values[ApplicationThemeKey] is string)
                {
                    string applicationThemeName = (string)localSettings.Values[ApplicationThemeKey];

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
                localSettings.Values[ApplicationThemeKey] = applicationThemeName;
            }
        }

       
        // User name 
        public static string? UserName
        {
            get => GetString(UserNameKey);
            set => SetString(UserNameKey, value);
        }

        // Teaching Tips Enabled
        public static bool TeachingTipsEnabled
        {
            get => GetBool(TeachingTipsEnabledKey, false/*default*/);
            set => SetBool(TeachingTipsEnabledKey, value);
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
            get => GetBool(UseInternetEnabledKey, false/*default*/);
            set => SetBool(UseInternetEnabledKey, value);
        }


        /// <summary>
        /// Auto Save enabled flag
        /// </summary>
        public static bool AutoSaveEnabled
        {
            get => GetBool(AutoSaveEnabledKey, true/*default*/);
            set => SetBool(AutoSaveEnabledKey, value);
        }


        /// <summary>
        /// Use the species image cache to download stock fish species images to help fish ID
        /// </summary>
        public static bool SpeciesImageCacheEnabled
        {
            get => GetBool(SpeciesImageCacheEnabledKey, true/*default*/);
            set => SetBool(SpeciesImageCacheEnabledKey, value);
        }


        /// <summary>
        /// Species code list order can be by common name or scientific name
        /// </summary>
        public static bool ScientificNameOrderEnabled
        {
            get => GetBool(ScientificNameOrderEnabledKey, true/*default*/);
            set => SetBool(ScientificNameOrderEnabledKey, value);

        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        private static bool GetBool(string key, bool defaultValue)
        {
            return _localSettings.Values[key] is bool value ? value : defaultValue;
        }

        private static void SetBool(string key, bool value)
        {
            _localSettings.Values[key] = value;
        }

        private static string? GetString(string key) => _localSettings.Values[key] as string;
        private static void SetString(string key, string? value) => _localSettings.Values[key] = value;

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
