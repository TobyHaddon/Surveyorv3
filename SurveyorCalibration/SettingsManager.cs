// Handles the local per user settings and the shipped application settings
// class SettingsManagerLocal is for the local settings that are stored on the user's device
// class SettingsManagerApp is for the application settings that are shipped with the application and are read-only
// Note settings from SettingsManagerApp should be remembered and not learn repeatedly from SettingsManagerApp 
// This is because the whole appSettings.json is loaded each time and it is not efficient to read it repeatedly
//
// Version 1.3  15 Nov 2025
// Derived from Surveyor version


using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        private const string ExperimentalFeatureSetAKey = "ExperimentalFeatureSetA";
        private const string ExperimentalFeatureSetBKey = "ExperimentalFeatureSetB";
        private const string ExperimentalFeatureSetCKey = "ExperimentalFeatureSetC";
        private const string ApplicationThemeKey = "ApplicationTheme";
        private const string TeachingTipsEnabledKey = "TeachingTipsEnabled";
        private const string UseInternetEnabledKey = "UseInternetEnabled";
        private const string AutoSaveEnabledKey = "AutoSaveEnabled";
        private const string DefaultCharucoBoardSquaresXKey = "DefaultCharucoBoard_SquaresX";
        private const string DefaultCharucoBoardSquaresYKey = "DefaultCharucoBoard_SquaresY";
        private const string DefaultCharucoBoardSquareLengthKey = "DefaultCharucoBoard_SquareLength";
        private const string DefaultCharucoBoardMarkerLengthKey = "DefaultCharucoBoard_MarkerLength";
        private const string DefaultCharucoBoardPredefinedDictionaryNameKey = "DefaultCharucoBoard_PredefinedDictionaryName";
        private const string DefaultBoardSizeXKey = "DefaultBoard_SizeX";
        private const string DefaultBoardSizeYKey = "DefaultBoard_SizeY";
        private const string DefaultBoardSurroundingBorderKey = "DefaultBoard_SurroundingBorder";
        private const string DefaultBoardDPIKey = "DefaultBoard_DPI";



        // Path where new media (MP4) are typically imported from
        public static string? MediaImportFolder
        {
            get => GetString(MediaImportFolderKey, string.Empty);
            set => SetString(MediaImportFolderKey, value);
        }


        // Path where new calibration files are typically imported from
        public static string? CalibrationImportFolder
        {
            get => GetString(CalibrationImportFolderKey, string.Empty);
            set => SetString(CalibrationImportFolderKey, value);
        }


        // Retrieve or set the survey folder path.  This is where the Survey files and the media files are stored.
        public static string? ProjectFolder
        {
            get => GetString(SurveyFolderKey, string.Empty);
            set => SetString(SurveyFolderKey, value);
        }

        // Path where media frames are saved to
        public static string? MediaFrameFolder
        {
            get
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values[MediaFrameFolderKey] is not string mediaFrameFolder)
                    mediaFrameFolder = ProjectFolder + "\\MediaFrames";

                return mediaFrameFolder;
            }
            set
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[MediaFrameFolderKey] = value;
            }
        }

        // Report More Diagnostic Information
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
        public static bool ExperimentalFeatureSetAEnabled
        {
            get => GetBool(ExperimentalFeatureSetAKey, false/*default*/);
            set => SetBool(ExperimentalFeatureSetAKey, value);
        }
        public static bool ExperimentalFeatureSetBEnabled
        {
            get => GetBool(ExperimentalFeatureSetBKey, false/*default*/);
            set => SetBool(ExperimentalFeatureSetBKey, value);
        }
        public static bool ExperimentalFeatureSetCEnabled
        {
            get => GetBool(ExperimentalFeatureSetCKey, false/*default*/);
            set => SetBool(ExperimentalFeatureSetCKey, value);
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
     

        // Teaching Tips Enabled
        public static bool TeachingTipsEnabled
        {
            get => GetBool(TeachingTipsEnabledKey, true/*default*/);
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
            get => GetBool(UseInternetEnabledKey, true/*default*/);
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
        /// Default ChArUco Board Squares X
        /// </summary>
        public static int DefaultChArUcoBoard_SquaresX
        {
            get => GetInt(DefaultCharucoBoardSquaresXKey, 14/*default*/);
            set => SetInt(DefaultCharucoBoardSquaresXKey, value);
        }

        /// <summary>
        /// Default ChArUco Board Squares Y
        /// </summary>
        public static int DefaultChArUcoBoard_SquaresY
        {
            get => GetInt(DefaultCharucoBoardSquaresYKey, 9/*default*/);
            set => SetInt(DefaultCharucoBoardSquaresYKey, value);
        }

        /// <summary>
        /// Size of each square in the ChArUco board in meters
        /// </summary>
        public static double DefaultChArUcoBoard_SquareLength
        {
            get => GetDouble(DefaultCharucoBoardSquareLengthKey, 0.04/*default*/);  //40mm
            set => SetDouble(DefaultCharucoBoardSquareLengthKey, value);
        }

        /// <summary>
        /// Size of each ArUco marker in the ChArUco board in meters
        /// </summary>
        public static double DefaultChArUcoBoard_MarkerLength
        {
            get => GetDouble(DefaultCharucoBoardMarkerLengthKey, 0.03/*default*/);  // 30mm
            set => SetDouble(DefaultCharucoBoardMarkerLengthKey, value);
        }

        /// <summary>
        /// Default dictionary name for the predefined ArUco dictionary used for the ChArUco board
        /// </summary>
        public static string DefaultChArUcoBoard_PredefinedDictionaryName
        {
            get => GetString(DefaultCharucoBoardPredefinedDictionaryNameKey, "DICT5x5_100"/*default*/);  
            set => SetString(DefaultCharucoBoardPredefinedDictionaryNameKey, value);
        }

        /// <summary>
        /// Default Physical Board Size X in meters
        /// </summary>
        public static double DefaultBoard_SizeX
        {
            get => GetDouble(DefaultBoardSizeXKey, 0.6/*default*/);  // 600mm
            set => SetDouble(DefaultBoardSizeXKey, value);
        }

        /// <summary>
        /// Default Physical Board Size Y in meters
        /// </summary>
        public static double DefaultBoard_SizeY
        {
            get => GetDouble(DefaultBoardSizeYKey, 0.4/*default*/);  // 400mm
            set => SetDouble(DefaultBoardSizeYKey, value);
        }

        /// <summary>
        /// Default size of the surrounding border that the PDF will be reduced by so
        /// it comfortably fits inside the physical board and the sticker doesn't peel
        /// off.  Set to 0mm if printing directly onto a board i.e. DiBond or 3mm if
        /// printing onto to sticker e.g. Plexiglas
        /// </summary>
        public static double DefaultBoard_SurroundingBorder
        {
            get => GetDouble(DefaultBoardSurroundingBorderKey, 0/*default*/);  // 0mm
            set => SetDouble(DefaultBoardSurroundingBorderKey, value);
        }


        /// <summary>
        /// Default Board Dots Per Square Inch (DPI)
        /// </summary>
        public static int DefaultBoard_DPI
        {
            get => GetInt(DefaultBoardDPIKey, 1200/*default*/);
            set => SetInt(DefaultBoardDPIKey, value);
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

        private static string GetString(string key, string defaultValue)
        {
            return _localSettings.Values[key] is string value ? value : defaultValue;
        }
        private static void SetString(string key, string? value) => _localSettings.Values[key] = value;

        private static int GetInt(string key, int defaultValue)
        {
            return _localSettings.Values[key] is int value ? value : defaultValue;
        }
        private static void SetInt(string key, int? value) => _localSettings.Values[key] = value;
        
        private static double GetDouble(string key, double defaultValue)
        {
            return _localSettings.Values[key] is double value ? value : defaultValue;
        }
        private static void SetDouble(string key, double? value) => _localSettings.Values[key] = value;
    }

    public class SettingsManagerApp
    {
        // Singleton instance
        private static SettingsManagerApp? _instance;

        [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
        public static SettingsManagerApp Instance => _instance ??= Load();

        public int RecentSurveysDisplayed { get; set; }  // Only use the 'get' the 'set' is for the JSON de-serialize


        [JsonConverter(typeof(KeyValuePairListJSonConverter))]
        public List<(string, string)> GoProScripts { get; set; } = [];  // Only use the 'get' the 'set' is for the JSON de-serialize



        ///
        /// PRIVATE
        ///


        /// <summary>
        /// Load the application settings
        /// </summary>

        private static readonly string SettingsFilePath = Path.Combine(AppContext.BaseDirectory, "appSettings.json");

        [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
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
    /// Custom JSon converter for KeyValuePairList
    /// </summary>
    public class KeyValuePairListJSonConverter : JsonConverter<List<(string, string)>>
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
