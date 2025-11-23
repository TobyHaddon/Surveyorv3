using Newtonsoft.Json;
using Surveyor.Calibration;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Surveyor
{
    public partial class CalibProject : INotifyPropertyChanged
    {
        // Event handler for property changed
        public event PropertyChangedEventHandler? PropertyChanged;

        public partial class DataClass
        {
            
            public partial class InfoClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                private string _projectFileName = string.Empty;
                public string ProjectFileName
                {
                    get => _projectFileName;
                    set
                    {
                        if (_projectFileName != value)
                        {
                            _projectFileName = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                private bool _isDirty;
                [JsonIgnore]
                public bool IsDirty
                {
                    get => _isDirty;
                    set
                    {
                        if (_isDirty != value)
                        {
                            _isDirty = value;
                            OnPropertyChanged();
                        }
                    }
                }

                public void Clear()
                {
                    ProjectFileName = string.Empty;
                    IsDirty = false;
                }

                private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
            }

            public partial class MediaClass : INotifyPropertyChanged
            {
                // Event handler for property changed
                public event PropertyChangedEventHandler? PropertyChanged;

                public MediaClass()
                {
                    Clear();
                }

                // Calibration mode mono+stereo, mono only, stereo only
                public CalibInfoAndMedia.StereoMonoMediaSetMode StereoMonoMediaSetMode = CalibInfoAndMedia.StereoMonoMediaSetMode.MonoAndStereoMediaSet;

                // Media Path
                private string _mediaPath = string.Empty;
                public string MediaPath
                {
                    get => _mediaPath;
                    set
                    {
                        if (_mediaPath != value)
                        {
                            _mediaPath = value;
                            IsDirty = true;
                        }
                    }
                }

                // Media file names
                private string _leftMonoMP4FileName = string.Empty;
                public string LeftMonoMP4FileName
                { 
                    get => _leftMonoMP4FileName; 
                    set
                    {
                        if (_leftMonoMP4FileName != value)
                        {
                            _leftMonoMP4FileName = value;
                            IsDirty = true;
                        }
                    }
                }

                private string _rightMonoMP4Path = string.Empty;
                public string RightMonoMP4Path
                {
                    get => _rightMonoMP4Path;
                    set
                    {
                        if (_rightMonoMP4Path != value)
                        {
                            _rightMonoMP4Path = value;
                            IsDirty = true;
                        }
                    }
                }

                private string _leftStereoMP4Path = string.Empty;
                public string LeftStereoMP4Path
                {
                    get => _leftStereoMP4Path;
                    set
                    {
                        if (_leftStereoMP4Path != value)
                        {
                            _leftStereoMP4Path = value;
                            IsDirty = true;
                        }
                    }
                }

                private string _rightStereoMP4Path = string.Empty;
                public string RightStereoMP4Path
                {
                    get => _rightStereoMP4Path;
                    set
                    {
                        if (_rightStereoMP4Path != value)
                        {
                            _rightStereoMP4Path = value;
                            IsDirty = true;
                        }
                    }
                }

                // Left camera IDs
                private string _leftCameraID = string.Empty;
                public string LeftCameraID
                {
                    get => _leftCameraID;
                    set
                    {
                        if (_leftCameraID != value)
                        {
                            _leftCameraID = value;
                            IsDirty = true;
                        }
                    }
                }

                // Right camera IDs
                private string _rightCameraID = string.Empty;
                public string RightCameraID
                {
                    get => _rightCameraID;
                    set
                    {
                        if (_rightCameraID != value)
                        {
                            _rightCameraID = value;
                            IsDirty = true;
                        }
                    }
                }

                // Frame Size
                public Windows.Foundation.Size FrameSize { get; set; } = new(0, 0);

                // Helper
                public string LeftMonoMP4Path
                {
                    get => Path.Combine(MediaPath, LeftMonoMP4FileName);
                }


                /// <summary>
                /// Clear down Media Class
                /// </summary>
                public void Clear()
                {
                    MediaPath = string.Empty;
                    LeftMonoMP4FileName = string.Empty;
                    RightMonoMP4Path = string.Empty;
                    LeftStereoMP4Path = string.Empty;
                    RightStereoMP4Path = string.Empty;
                    LeftCameraID = string.Empty;
                    RightCameraID = string.Empty;
                    FrameSize = new(0, 0);

                    _isDirty = false;
                }

                // Dirty flag
                private bool _isDirty;

                [JsonIgnore]
                public bool IsDirty
                {
                    get
                    {
                        if (_isDirty)
                            return true;

                        return false;
                    }
                    set
                    {
                        bool anyChanged = false;

                        if (_isDirty != value)
                        {
                            _isDirty = value;
                            anyChanged = true;
                        }
                        if (anyChanged)
                            OnPropertyChanged();

                    }
                }


                ///
                /// EVENTS
                /// 
                private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }

            }

            public partial class CalibrationResultClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                private string _projectFileName = string.Empty;
                public string ProjectFileName
                {
                    get => _projectFileName;
                    set
                    {
                        if (_projectFileName != value)
                        {
                            _projectFileName = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                private bool _isDirty;
                [JsonIgnore]
                public bool IsDirty
                {
                    get => _isDirty;
                    set
                    {
                        if (_isDirty != value)
                        {
                            _isDirty = value;
                            OnPropertyChanged();
                        }
                    }
                }

                public void Clear()
                {
                    ProjectFileName = string.Empty;
                    IsDirty = false;
                }

                private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
            }


            public string CalibFileSpec { get; set; } = string.Empty;

            
            public InfoClass Info { get; set; } = new();

            public MediaClass Media { get; set; } = new();
            
            public CharucoBoardDefinition CharucoBoardDefinition { get; set; } = new();

            public CalibrationResultClass CalibrationResult { get; set; } = new();

            // Left & right mono calibration result sets (different results for different calibration flags)
            public MonoCalibrationCameraData?[] LeftMonoCalibrationCameraDataArray { get; set; } = new MonoCalibrationCameraData?[Enum.GetValues<CalibrationParameters>().Length];
            public MonoCalibrationCameraData?[] RightMonoCalibrationCameraDataArray { get; set; } = new MonoCalibrationCameraData?[Enum.GetValues<CalibrationParameters>().Length];

            // Stereo result sets  (different results for different calibration flags)
            public CalibrationStereoCameraData?[] CalibrationStereoCameraDataArray { get; set; } = new CalibrationStereoCameraData?[Enum.GetValues<CalibrationParameters>().Length];
        }

        public DataClass Data = new();


        public bool IsDirty
        {
            get
            {
                if (Data.Info.IsDirty || 
                    Data.Media.IsDirty || 
                    Data.CharucoBoardDefinition.IsDirty ||
                    Data.CalibrationResult.IsDirty)
                {
                    return true;
                }
                return false;
            }
            private set
            {
                Data.Info.IsDirty = value;
                Data.CharucoBoardDefinition.IsDirty = value;
                OnPropertyChanged();
            }
        }
        public bool IsLoaded { get; private set; } = false;


        /// <summary>
        /// Save the calibration project data to a file as json.
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public async Task<bool> Save(string fileSpec)
        {
            // Remember the calib project file spec
            Data.CalibFileSpec = fileSpec;

            return await Save();
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<bool> Save()
        {
            bool ret = false;

            if (string.IsNullOrEmpty(Data.CalibFileSpec))
                return ret;

            // Delete a .bak file if it exists
            string bakFileSpec = Path.ChangeExtension(Data.CalibFileSpec, ".bak");
            if (File.Exists(bakFileSpec))
            {
                try
                {
                    File.Delete(bakFileSpec);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting backup file {bakFileSpec}: {ex.Message}");
                }
            }

            // Rename fileSpec to a .bak file
            if (File.Exists(Data.CalibFileSpec))
            {
                try
                {
                    File.Move(Data.CalibFileSpec, bakFileSpec);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error renaming file {Data.CalibFileSpec} to backup {bakFileSpec}: {ex.Message}");
                    return false; // Return false if renaming fails
                }
            }

            // Save the project data as json to the file
            try
            {
                var options = CreateJsonOptions();
                string json = JsonConvert.SerializeObject(Data, options);
                await File.WriteAllTextAsync(Data.CalibFileSpec, json);
                ret = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving calibration project data to {Data.CalibFileSpec}: {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// Load the calibration project data from a file as json.
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public bool Load(string fileSpec)
        {
            if (!File.Exists(fileSpec))
            {
                Debug.WriteLine($"Calibration project file {fileSpec} does not exist.");
                return false;
            }
            try
            {
                string json = File.ReadAllText(fileSpec);
                var options = CreateJsonOptions();
                Data = JsonConvert.DeserializeObject<DataClass>(json, options) ?? new DataClass();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading calibration project data from {fileSpec}: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Returns the stereo calibration result set with the best RMS
        /// </summary>
        /// <returns></returns>
        public CalibrationParameters? ReturnBestStereoCalibrationCameraData()
        {
            double bestScore = double.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < Data.CalibrationStereoCameraDataArray.Length; i++)
            {
                var stereoResult = Data.CalibrationStereoCameraDataArray[i];


                if (stereoResult is null)
                    continue;

                // Define weighted score (you can tune weights as needed)
                double score = stereoResult.RMS /*???+ 0.2 * stereoResult.MaxError*/;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex == -1)
                return null;

            return (CalibrationParameters?)bestIndex;
        }


        private static JsonSerializerSettings CreateJsonOptions()
        {
            var opts = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
            //???opts.Converters.Add(new Double2DArrayConverter());
            opts.Converters.Add(new MatrixJsonConverter());
            // opts.Converters.Add(new Float2DArrayConverter()); // if needed
            return opts;
        }


        ///
        /// EVENTS
        /// 
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
