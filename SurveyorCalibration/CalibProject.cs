using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SurveyorCalibrationData;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Printers;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Surveyor
{
    public enum StereoMonoMediaSetMode
    {
        MonoAndStereoMediaSet,  // A stereo pair plus a mono pair
        StereoOnlyMediaSet,     // A stereo pair only
        MonoPairOnlyMediaSet,   // A mono pair only
        MonoSingleOnlyMediaSet,  // A single mono file only
        None
    };

    public partial class CalibProject : INotifyPropertyChanged
    {
        // Event handler for property changed
        public event PropertyChangedEventHandler? PropertyChanged;

        readonly Reporter? Report = new();

        // Auto Save variables
        private readonly TimeSpan autosaveInterval = TimeSpan.FromSeconds(20);
        private CancellationTokenSource? _autosaveCts;
        private Task? _autosaveTask;

        // Lock object for thread safety
        // Use to stop the auto save and save methods from being called at the same time
        private readonly object _lockObject = new();



        public partial class DataClass
        {
            /// <summary>
            /// Clear the DataClass
            /// </summary>
            public void Clear()
            {
                Info.Clear();
                Media.Clear();
                CharucoBoardDefinition.Clear();
                CalibrationResults.Clear();
                Sync.Clear();
            }

            public partial class InfoClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public InfoClass() 
                {
                    Clear();
                }

                public void Clear()
                {
                    _projectFileName = string.Empty;
                    _projectPath = string.Empty;
                    IsDirty = false;
                }

                // Info class version
                public float Version { get; set; } = 1.1f;

                // Values
                private string _projectFileName = string.Empty;
                private string _projectPath = string.Empty;

                // Setters and getters

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

                public string ProjectPath
                {
                    get => _projectPath;
                    set
                    {
                        if (_projectPath != value)
                        {
                            _projectPath = value;
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


                /// 
                /// EVENTS
                ///
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

                /// <summary>
                /// Clear down Media Class
                /// </summary>
                public void Clear()
                {
                    _stereoMonoMediaSetMode = StereoMonoMediaSetMode.None;
                    _mediaPath = string.Empty;
                    _leftMonoMP4FileName = string.Empty;
                    _rightMonoMP4FileName = string.Empty;
                    _leftStereoMP4FileName = string.Empty;
                    _rightStereoMP4FileName = string.Empty;
                    _leftCameraID = string.Empty;
                    _rightCameraID = string.Empty;
                    _frameWidth = 0;
                    _frameHeight = 0;

                    _isDirty = false;
                }

                // Media class version
                public float Version { get; set; } = 1.0f;

                // Values
                private StereoMonoMediaSetMode _stereoMonoMediaSetMode = StereoMonoMediaSetMode.None;
                private string _mediaPath = string.Empty;
                private string _leftMonoMP4FileName = string.Empty;
                private string _rightMonoMP4FileName = string.Empty;
                private string _leftStereoMP4FileName = string.Empty;
                private string _rightStereoMP4FileName = string.Empty;
                private int _frameWidth = 0;
                private int _frameHeight = 0;

                // Setters and getters
                // Calibration mode mono+stereo, mono only, stereo only
                [JsonConverter(typeof(StringEnumConverter))]
                public StereoMonoMediaSetMode StereoMonoMediaSetMode
                {
                    get => _stereoMonoMediaSetMode;
                    set
                    {
                        if (_stereoMonoMediaSetMode != value)
                        {
                            _stereoMonoMediaSetMode = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                // Media Path (All media files are on the same)
                public string MediaPath
                {
                    get => _mediaPath;
                    set
                    {
                        if (_mediaPath != value)
                        {
                            _mediaPath = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                // Left Mono media file name (Use for MonoAndStereoMediaSet, MonoPairOnlyMediaSet, MonoSingleOnlyMediaSet)
                public string LeftMonoMP4FileName
                { 
                    get => _leftMonoMP4FileName; 
                    set
                    {
                        if (_leftMonoMP4FileName != value)
                        {
                            _leftMonoMP4FileName = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                // Right Mono media file name (Use for MonoAndStereoMediaSet, MonoPairOnlyMediaSet)
                public string RightMonoMP4FileName
                {
                    get => _rightMonoMP4FileName;
                    set
                    {
                        if (_rightMonoMP4FileName != value)
                        {
                            _rightMonoMP4FileName = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                // Left Stereo media file name (Use for MonoAndStereoMediaSet, StereoOnlyMediaSet)
                public string LeftStereoMP4FileName
                {
                    get => _leftStereoMP4FileName;
                    set
                    {
                        if (_leftStereoMP4FileName != value)
                        {
                            _leftStereoMP4FileName = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                // Right Stereo media file name (Use for MonoAndStereoMediaSet, StereoOnlyMediaSet)
                public string RightStereoMP4FileName
                {
                    get => _rightStereoMP4FileName;
                    set
                    {
                        if (_rightStereoMP4FileName != value)
                        {
                            _rightStereoMP4FileName = value;
                            IsDirty = true;
                            OnPropertyChanged();
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
                            OnPropertyChanged();
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
                            OnPropertyChanged();
                        }
                    }
                }

                // Frame Size
                public int FrameWidth
                { 
                    get => _frameWidth;
                    set
                    {
                         if (_frameWidth != value)
                        {
                            _frameWidth = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }
                public int FrameHeight
                {
                    get => _frameHeight;
                    set
                    {
                        if (_frameHeight != value)
                        {
                            _frameHeight = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }


                // Helpers
                [JsonIgnore]
                public string LeftMonoMP4Path
                {
                    get => Path.Combine(MediaPath, LeftMonoMP4FileName);
                }
                [JsonIgnore]
                public string RightMonoMP4Path
                {
                    get => Path.Combine(MediaPath, RightMonoMP4FileName);
                }
                [JsonIgnore]
                public string LeftStereoMP4Path
                {
                    get => Path.Combine(MediaPath, LeftStereoMP4FileName);
                }
                [JsonIgnore]
                public string RightStereoMP4Path
                {
                    get => Path.Combine(MediaPath, RightStereoMP4FileName);
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

            public partial class SyncClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public SyncClass()
                {
                    Clear();
                }

                public void Clear()
                {
                    _isSynchronized = false;
                    _syncFrameIndexLeft = 0;
                    _syncFrameIndexRight = 0;

                    IsDirty = false;
                }

                // Info class version
                public float Version { get; set; } = 1.0f;

                // Values
                private bool _isSynchronized = false;
                private int _syncFrameIndexLeft = 0;
                private int _syncFrameIndexRight = 0;


                // Setters and getters

                public bool IsSynchronized
                {
                    get => _isSynchronized;
                    set
                    {
                        if (_isSynchronized != value)
                        {
                            _isSynchronized = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                public int SyncFrameIndexLeft
                {
                    get => _syncFrameIndexLeft;
                    set
                    {
                        if (_syncFrameIndexLeft != value)
                        {
                            _syncFrameIndexLeft = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                public int SyncFrameIndexRight
                {
                    get => _syncFrameIndexRight;
                    set
                    {
                        if (_syncFrameIndexRight != value)
                        {
                            _syncFrameIndexRight = value;
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

                public CalibrationResultClass()
                {
                    Clear();
                }

                public void Clear()
                {
                    _leftMonoCalibrationCameraDataArray = new MonoCalibrationCameraData?[Enum.GetValues<CalibrationParameters>().Length];
                    _rightMonoCalibrationCameraDataArray = new MonoCalibrationCameraData?[Enum.GetValues<CalibrationParameters>().Length];
                    _calibrationStereoCameraDataArray = new CalibrationStereoCameraData?[Enum.GetValues<CalibrationParameters>().Length];

                    IsDirty = false;
                }

                // CalibrationResults class version
                public float Version { get; set; } = 1.0f;

                // Values
                private MonoCalibrationCameraData?[] _leftMonoCalibrationCameraDataArray = new MonoCalibrationCameraData?[Enum.GetValues<CalibrationParameters>().Length];
                private MonoCalibrationCameraData?[] _rightMonoCalibrationCameraDataArray = new MonoCalibrationCameraData?[Enum.GetValues<CalibrationParameters>().Length];
                private CalibrationStereoCameraData?[] _calibrationStereoCameraDataArray = new CalibrationStereoCameraData?[Enum.GetValues<CalibrationParameters>().Length];

                // Setters and getters
                // Left & right mono calibration result sets (different results for different calibration flags)
                public MonoCalibrationCameraData?[] LeftMonoCalibrationCameraDataArray 
                { 
                    get => _leftMonoCalibrationCameraDataArray;
                    set
                    {
                        if (_leftMonoCalibrationCameraDataArray != value)
                        {
                            _leftMonoCalibrationCameraDataArray = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    } 
                }

                public MonoCalibrationCameraData?[] RightMonoCalibrationCameraDataArray
                {
                    get => _rightMonoCalibrationCameraDataArray;
                    set
                    {
                        if (_rightMonoCalibrationCameraDataArray != value)
                        {
                            _rightMonoCalibrationCameraDataArray = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                // Stereo result sets  (different results for different calibration flags)
                public CalibrationStereoCameraData?[] CalibrationStereoCameraDataArray
                {
                    get => _calibrationStereoCameraDataArray;
                    set
                    {
                        if (_calibrationStereoCameraDataArray != value)
                        {
                            _calibrationStereoCameraDataArray = value;
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

                ///
                /// EVENTS
                /// 
                private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
            }

            
            public InfoClass Info { get; set; } = new();

            public MediaClass Media { get; set; } = new();
            
            public CharucoBoardDefinition CharucoBoardDefinition { get; set; } = new();

            public SyncClass Sync { get; set; } = new();

            public CalibrationResultClass CalibrationResults { get; set; } = new();

        }

        public DataClass Data = new();

        public bool IsDirty
        {
            get
            {
                if (Data.Info.IsDirty || 
                    Data.Media.IsDirty || 
                    Data.CharucoBoardDefinition.IsDirty ||
                    Data.Sync.IsDirty ||
                    Data.CalibrationResults.IsDirty)
                {
                    return true;
                }
                return false;
            }
            private set
            {
                Data.Info.IsDirty = value;
                Data.Media.IsDirty = value;
                Data.CharucoBoardDefinition.IsDirty = value;
                Data.Sync.IsDirty = value;
                Data.CalibrationResults.IsDirty = value;
                OnPropertyChanged();
            }
        }


        /// <summary>
        /// Clear down the class
        /// </summary>
        public void Clear()
        {
            Data.Clear();
        }


        /// <summary>
        /// Returns the project title for the main window title bar
        /// </summary>
        /// <returns></returns>
        public string GetProjectTitle()
        {
            string title = "Untitled Project";

            if (this.Data.Info.ProjectFileName != null)
            {
                title = Path.GetFileNameWithoutExtension(this.Data.Info.ProjectFileName);

                if (this.IsDirty)
                    title += " *";
            }

            return title;
        }


        /// <summary>
        /// Is CalibProject loaded from file
        /// </summary>
        public bool IsLoaded { get; private set; } = false;


        /// <summary>
        /// Load the calibration project data from a file as json.
        /// </summary>
        /// <param name="projectFileSpec"></param>
        /// <param name="autoSave"></param>
        /// <returns></returns>
        public async Task<int> ProjectLoadAsync(string projectFileSpec, bool autoSave = true)
        {
            int ret = -1;
            string? json = null;

            try
            {
                json = File.ReadAllText(projectFileSpec);
            }
            catch (FileNotFoundException e)
            {
                ret = -2;
                Report?.Warning("", $"Load project failed because the file couldn't be found, file:{projectFileSpec}. {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                ret = -3;
                Report?.Warning("", $"Load project failed because you do not have permission to read this file, file:{projectFileSpec}. {e.Message}");
            }
            catch (DirectoryNotFoundException e)
            {
                ret = -4;
                Report?.Warning("", $"Load project failed because the specified directory could not be found, file:{projectFileSpec}. {e.Message}");
            }
            catch (PathTooLongException e)
            {
                ret = -5;
                Report?.Warning("", $"Load project failed because the file name is too long, file:{projectFileSpec}. The specified path, file name, or both exceed the system-defined maximum length. {e.Message}");
            }
            catch (IOException e)
            {
                ret = -6;
                Report?.Warning("", $"Load project failed because an I/O error occurred, file:{projectFileSpec}. {e.Message}");
            }
            catch (Exception e)
            {
                ret = -7;
                Report?.Warning("", $"Load project failed because an unexpected error occurred, file:{projectFileSpec}. {e.Message}");
            }

            if (json != null)
            {
                var settings = CreateJsonOptions();

                DataClass? data = null;
                try
                {                    
                    data = JsonConvert.DeserializeObject<DataClass>(json, settings);
                }
                catch (Exception e)
                {
                    Report?.Info("", $"Exception JsonConvert.DeserializeObject({e.Message})");
                    ret = -1;
                }

                if (data != null)
                {
                    Data = data;

                    ret = SetProjectNameAndPath(projectFileSpec);

                    IsDirty = false;
                    IsLoaded = true;

                    // Start the autosave task in background                        
                    if (autoSave)
                    {
                        await StartAutoSaveAsync();
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Save the calibration project data to a file as json.
        /// Note this is a synchronous method to avoid reentry from the auto save task
        /// </summary>
        /// <returns></returns>
        public int ProjectSave()
        {
            int ret = -1;

            // Stop any reentry
            lock (_lockObject)
            {
                if (!string.IsNullOrEmpty(Data.Info.ProjectPath) && !string.IsNullOrEmpty(Data.Media.MediaPath))
                {
                    // Make calibration project full file spec
                    string calprojFileSpec = Path.Combine(Data.Info.ProjectPath, Data.Info.ProjectFileName);

                    // Delete a .bak file if it exists
                    string bakFileSpec = Path.ChangeExtension(calprojFileSpec, ".bak");
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
                    if (File.Exists(calprojFileSpec))
                    {
                        try
                        {
                            File.Move(calprojFileSpec, bakFileSpec);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error renaming file {calprojFileSpec} to backup {bakFileSpec}: {ex.Message}");
                        }
                    }
                }

                // Adjust MediaPath if possible
                if (!string.IsNullOrEmpty(Data.Info.ProjectPath) && !string.IsNullOrEmpty(Data.Media.MediaPath))
                {
                    var projectPathFull = Path.GetFullPath(Data.Info.ProjectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var mediaPathFull = Path.GetFullPath(Data.Media.MediaPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    if (mediaPathFull.StartsWith(projectPathFull, StringComparison.OrdinalIgnoreCase))
                    {
                        // Make relative path
                        var relativePath = Path.GetRelativePath(projectPathFull, mediaPathFull);
                        if (string.IsNullOrEmpty(relativePath) || relativePath == ".")
                        {
                            // Same directory — blank MediaPath
                            Data.Media.MediaPath = "";
                        }
                        else
                        {
                            Data.Media.MediaPath = relativePath;
                        }
                    }
                }

                // Save to .json
                if (Data.Info.ProjectPath != null && Data.Info.ProjectFileName != null)
                {
                    string projectPath = Data.Info.ProjectPath;
                    string filePath = Path.Combine(projectPath, Data.Info.ProjectFileName);


                    var settings = CreateJsonOptions();

                    // Remove the project Path so the project can be safely moved to a different folder
                    Data.Info.ProjectPath = string.Empty;

                    string json = JsonConvert.SerializeObject(Data, settings);

                    // Restore project path
                    Data.Info.ProjectPath = projectPath;

                    // Write to disk
                    try
                    {
                        File.WriteAllText(filePath, json);

                        this.IsDirty = false;
                        ret = 0;
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        ret = -1;
                        Report?.Warning("", $"Save project failed due to an unauthorized access request, file:{filePath}. You do not have permission to write to this file. {e.Message}");
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        ret = -2;
                        Report?.Warning("", $"Save project failed due to a bad directory, file:{filePath}. The specified directory could not be found. {e.Message}");
                    }
                    catch (PathTooLongException e)
                    {
                        ret = -3;
                        Report?.Warning("", $"Save project failed due to the file name too long, file:{filePath}. The specified path, file name, or both exceed the system-defined maximum length. {e.Message}");
                    }
                    catch (IOException e)
                    {
                        ret = -4;
                        Report?.Warning("", $"Save project failed due to an I/O error, file:{filePath}. {e.Message}");
                    }
                    catch (Exception e)
                    {
                        ret = -5;
                        Report?.Warning("", $"Save project failed due to an unexpected error, file:{filePath}. {e.Message}");
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Save the calibration project data to a file as json.
        /// </summary>
        /// <param name="projectFileSpec"></param>
        /// <returns></returns>
        public async Task<int> ProjectSaveAsAsync(string projectFileSpec)
        {
            // Set the project name using the stem of the file name and extract the project path
            int ret = SetProjectNameAndPath(projectFileSpec);

            if (ret == 0)
            {
                // Save the project to a json file
                ret = ProjectSave();

                if (ret == 0)
                {
                    // Reset the dirty flag
                    IsDirty = false;

                    // A Save As could be the first save of a new project to set IsLoaded to true
                    IsLoaded = true;
                    await StartAutoSaveAsync();
                }
            }

            return ret;
        }


        /// <summary>
        /// Close a project
        /// </summary>
        /// <returns></returns>
        public async Task<int> ProjectCloseAsync()
        {
            await StopAutoSaveAsync();

            Clear();

            IsLoaded = false;

            return 0;
        }


        /// <summary>
        /// Extract the project name and path from the passed file spec
        /// </summary>
        private int SetProjectNameAndPath(string projectFileSpec)
        {
            int ret = 0;
            string? directoryPath = null;
            string? fileName = null;

            // Extract the path
            try
            {
                directoryPath = Path.GetDirectoryName(projectFileSpec);
            }
            catch (ArgumentNullException e)
            {
                ret = -1;
                Report?.Warning("", $"SetProjectNameAndPath() trying to set project path base on:{projectFileSpec}, however were a null argument. {e.Message}");
            }
            catch (ArgumentException e)
            {
                ret = -2;
                Report?.Warning("", $"SetProjectNameAndPath() trying to set project path base on:{projectFileSpec}, however were was an error. {e.Message}");
            }

            // Extract the file name
            try
            {
                fileName = Path.GetFileName(projectFileSpec);
            }
            catch (ArgumentException e)
            {
                ret = -3;
                Report?.Warning("", $"SetProjectNameAndPath() trying to set project name base on:{projectFileSpec}, however were was an error. {e.Message}");
            }

            Data.Info.ProjectFileName = fileName ?? "";
            Data.Info.ProjectPath = directoryPath ?? "";

            return ret;
        }


        /// <summary>
        /// Returns the stereo calibration result set with the best RMS
        /// </summary>
        /// <returns></returns>
        public CalibrationParameters? ReturnBestStereoCalibrationCameraData()
        {
            double bestScore = double.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < Data.CalibrationResults.CalibrationStereoCameraDataArray.Length; i++)
            {
                var stereoResult = Data.CalibrationResults.CalibrationStereoCameraDataArray[i];


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


        /// <summary>
        /// Start the auto save task
        /// <summary>
        private async Task StartAutoSaveAsync()
        {
            await StopAutoSaveAsync(); // Ensure any previous autosave task is stopped

            _autosaveCts = new CancellationTokenSource();
            _autosaveTask = Task.Run(async () =>
            {
                // Task naming for debugging
                if (Thread.CurrentThread.Name == null)
                {
                    Thread.CurrentThread.Name = "CalibProject.AutosaveWork()";
                }

                var token = _autosaveCts.Token;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(autosaveInterval, token);
                        if (!token.IsCancellationRequested)
                        {
                            if (SettingsManagerLocal.AutoSaveEnabled && IsDirty)
                            {
                                ProjectSave();
                                Report?.Debug("", $"Autosave completed");
                            }
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Graceful cancellation
                        break;
                    }
                }
                Report?.Info("", $"Auto save threaded stopped on request");
            });
        }


        /// <summary>
        /// Request the autosave task to stop
        /// </summary>
        public async Task StopAutoSaveAsync()
        {
            if (_autosaveCts != null)
            {
                await _autosaveCts.CancelAsync();

                try
                {
                    if (_autosaveTask != null)
                        await _autosaveTask;
                }
                catch (TaskCanceledException)
                {
                    // Expected on cancellation
                }

                _autosaveCts.Dispose();
                _autosaveCts = null;
                _autosaveTask = null;
            }
        }


        /// <summary>
        /// Suggest and file name for the calibration project
        /// </summary>
        /// <returns></returns>
        public string SuggestProjectFileName()
        {
            // Prefer stereo names, fall back to mono
            string? left = Data?.Media?.LeftStereoMP4FileName;
            string? right = Data?.Media?.RightStereoMP4FileName;

            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            {
                left = Data?.Media?.LeftMonoMP4FileName;
                right = Data?.Media?.RightMonoMP4FileName;
            }

            // Nothing to go on
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
                return "CalibrationProject.calproj";

            // Build base stem from available names
            string stemLeft = Stem(left);
            string stemRight = Stem(right);
            string baseStem = string.Empty;

            if (!string.IsNullOrEmpty(stemLeft) && !string.IsNullOrEmpty(stemRight))
                baseStem = CommonPrefix(Clean(stemLeft), Clean(stemRight));
            else
                baseStem = Clean(!string.IsNullOrEmpty(stemLeft) ? stemLeft : stemRight);

            if (string.IsNullOrWhiteSpace(baseStem))
                baseStem = "Calibration";

            // Compose and sanitize
            string suggested = $"{baseStem}.calproj";
            suggested = SanitizeFileName(suggested);

            // Final fallback safety
            return string.IsNullOrWhiteSpace(suggested) ? "CalibrationProject.calproj" : suggested;

            // Helpers (local to keep surface small)

            static string Stem(string? fileName)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return string.Empty;
                try
                {
                    var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    return stem ?? string.Empty;
                }
                catch
                {
                    return fileName!;
                }
            }

            static string Clean(string s)
            {
                // Remove known tokens (left/right) and isolated L/R; normalize separators
                string result = s;
                result = System.Text.RegularExpressions.Regex.Replace(result, "(?i)\\bleft\\b|\\bright\\b", "");
                result = System.Text.RegularExpressions.Regex.Replace(result, "(?<![a-zA-Z])L(?![a-zA-Z])", "");
                result = System.Text.RegularExpressions.Regex.Replace(result, "(?<![a-zA-Z])R(?![a-zA-Z])", "");
                result = System.Text.RegularExpressions.Regex.Replace(result, "[\\s_\\-\\.]+", " ").Trim();
                return result;
            }

            static string CommonPrefix(string a, string b)
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                    return string.Empty;

                int len = Math.Min(a.Length, b.Length);
                int i = 0;
                for (; i < len; i++)
                {
                    if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[i]))
                        break;
                }

                string prefix = a.Substring(0, i).Trim();
                // Trim trailing separators from prefix
                prefix = System.Text.RegularExpressions.Regex.Replace(prefix, "[\\s_\\-\\.]+$", "").Trim();
                if (prefix.Length < 3)
                {
                    // If too small, prefer the longer cleaned stem as base
                    return a.Length >= b.Length ? a : b;
                }
                return prefix;
            }

            static string SanitizeFileName(string name)
            {
                var invalid = System.IO.Path.GetInvalidFileNameChars();
                var cleaned = new string(name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray());
                // Collapse consecutive underscores/spaces
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "[ _]+", " ");
                // Ensure extension
                if (!cleaned.EndsWith(".calproj", StringComparison.OrdinalIgnoreCase))
                    cleaned += ".calproj";
                return cleaned.Trim(' ');
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

    /// <summary>
    /// Reporter class for logging warnings and errors
    /// </summary>
    public class Reporter
    {
        public void Error(string channel, string message)
        {
            System.Diagnostics.Debug.WriteLine($"Error [{channel}]: {message}");
        }

        public void Warning(string channel, string message)
        {
            System.Diagnostics.Debug.WriteLine($"Warning [{channel}]: {message}");
        }
        public void Info(string channel, string message)
        {
            System.Diagnostics.Debug.WriteLine($"Info [{channel}]: {message}");
        }
        public void Debug(string channel, string message)
        {
            System.Diagnostics.Debug.WriteLine($"Debug [{channel}]: {message}");
        }

    }
}
