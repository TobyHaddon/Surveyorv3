// Surveyor Project
// Hold the all the survey information and results
// 
// Version 1.1
// Make Partial class to allow for the addition of ProjectEMObs

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Surveyor.Events;
using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Surveyor
{
    public partial class Project : INotifyPropertyChanged
    {
        // Used to report info, warnings and errors to the user
        private Reporter? Report { get; set; } = null;


        // Auto Save variables
        private bool _isAutoSaveRunning = true;
        private bool _autoSaveStopped = false;
        private TimeSpan _autosaveInterval = TimeSpan.FromMinutes(5); // Example: save every 5 minutes

        // Lock object for thread safety
        // Use to stop the auto save and save methods from being called at the same time
        private readonly object _lockObject = new();

        // Event handler for property changed
        public event PropertyChangedEventHandler? PropertyChanged;


        public class DataClass
        {
            /// <summary>
            /// Clear the DataClass
            /// </summary>
            public void Clear()
            {
                this.Info.Clear();
                this.Media.Clear();
                this.Sync.Clear();
                this.Events.Clear();
                this.Calibration.Clear();
            }


            public class InfoClass : INotifyPropertyChanged
            {                
                public event PropertyChangedEventHandler? PropertyChanged;

                /// <summary>
                /// Clear down the InfoClass
                /// </summary>
                public void Clear()
                {
                    _projectFileName = null;
                    _projectPath = null;
                    _isDirty = false;
                }


                // Info class version
                public float Version { get; set; } = 1.0f;

                // Values
                private string? _projectFileName = null;
                private string? _projectPath = null;

                // Setters and getters
                public string? ProjectFileName
                {
                    get => _projectFileName;
                    set
                    {
                        if (_projectFileName != value)
                        {
                            _projectFileName = value;
                            IsDirty = true;
                        }
                    }
                }
                public string? ProjectPath 
                {
                    get => _projectPath;
                    set
                    {
                        if (_projectPath != value)
                        {
                            _projectPath = value;
                            IsDirty = true;
                        }
                    }
                }


                [JsonIgnore]
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
            public InfoClass Info { get; set; } = new InfoClass();

            public class MediaClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public MediaClass()
                {
                    // Subscribe to the collection's CollectionChanged event
                    _leftMediaFileNames.CollectionChanged += CollectionChangedHandler;
                    _rightMediaFileNames.CollectionChanged += CollectionChangedHandler;
                }

                /// <summary>
                /// Clear down the MediaClass
                /// </summary>
                public void Clear()
                {
                    _mediaPath = null;
                    _leftMediaFileNames.Clear();
                    _rightMediaFileNames.Clear();
                    _isDirty = false;
                }


                // Media class version
                public float Version { get; set; } = 1.0f;

                [JsonIgnore]
                private string? _mediaPath = null;

                [JsonIgnore]
                private ObservableCollection<string> _leftMediaFileNames = new();

                [JsonIgnore]
                private ObservableCollection<string> _rightMediaFileNames = new();

                public string? MediaPath
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

          
                public ObservableCollection<string> LeftMediaFileNames
                {
                    get => _leftMediaFileNames;
                    set
                    {
                        if (_leftMediaFileNames != value)
                        {
                            _leftMediaFileNames = value;

                            IsDirty = true;
                        }
                    }
                }


                public ObservableCollection<string> RightMediaFileNames
                {
                    get => _rightMediaFileNames;
                    set
                    {
                        if (_rightMediaFileNames != value)
                        {
                            _rightMediaFileNames = value;

                            IsDirty = true;
                        }
                    }
                }


                /// <summary>
                /// This method will be called whenever the LeftMediaFileNames or RightMediaFileNames ObservableCollection<string> collection changes
                /// </summary>
                /// <param name="sender"></param>
                /// <param name="e"></param>
                private void CollectionChangedHandler(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    // This code will be executed when the collection is changed
                    IsDirty = true;
                }


                [JsonIgnore]
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
            public MediaClass Media { get; set; } = new MediaClass();

            public class SyncClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;


                /// <summary>
                /// Clear down the SyncClass
                /// </summary>
                public void Clear()
                {
                    _isSynchronized = false;
                    _timeSpanOffset = TimeSpan.Zero;
                    _actualTimeSpanOffsetLeft = TimeSpan.Zero;
                    _actualTimeSpanOffsetRight = TimeSpan.Zero;
                    _isDirty = false;
                }


                // Sync class version
                public float Version { get; set; } = 1.3f;

                // Expand this class to include all the syning points like the lock on multiple media files
                // maybe support for multiple sync points
                // maybe support for period start/stop
                // but start with a single offset between the first left media fist and the first right media file

                [JsonIgnore]
                private bool _isSynchronized = false;       // If synchronized is switched to false the frame offset isn't removed incase it needs to be recovered 

                [JsonIgnore]
                private TimeSpan _timeSpanOffset = TimeSpan.Zero;   // This is right - left

                [JsonIgnore]
                private TimeSpan _actualTimeSpanOffsetLeft = TimeSpan.Zero;  // The actual sync timespan offset in the left media file, normally a torch flash

                [JsonIgnore]
                private TimeSpan _actualTimeSpanOffsetRight = TimeSpan.Zero; // The actual sync timespan offset in the right media file, normally a torch flash

                public bool IsSynchronized
                {
                    get => _isSynchronized;
                    set
                    {
                        if (_isSynchronized != value)
                        {
                            _isSynchronized = value;
                            IsDirty = true;
                        }
                    }
                }

                public TimeSpan TimeSpanOffset
                {
                    get => _timeSpanOffset;
                    set
                    {
                        if (_timeSpanOffset != value)
                        {
                            _timeSpanOffset = value;
                            IsDirty = true;
                        }
                    }
                }

                public TimeSpan ActualTimeSpanOffsetLeft
                {
                    get => _actualTimeSpanOffsetLeft;
                    set
                    {
                        if (_actualTimeSpanOffsetLeft != value)
                        {
                            _actualTimeSpanOffsetLeft = value;
                            IsDirty = true;
                        }
                    }
                }

                public TimeSpan ActualTimeSpanOffsetRight
                {
                    get => _actualTimeSpanOffsetRight;
                    set
                    {
                        if (_actualTimeSpanOffsetRight != value)
                        {
                            _actualTimeSpanOffsetRight = value;
                            IsDirty = true;
                        }
                    }
                }

                [JsonIgnore]
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
            public SyncClass Sync { get; set; } = new SyncClass();


            public class EventsClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public EventsClass()
                {
                    // Subscribe to the collection's CollectionChanged event
                    _eventList.CollectionChanged += CollectionChangedHandler;
                    
                }


                /// <summary>
                /// Clear down the EventsClass
                /// </summary>
                public void Clear()
                {
                    _eventList.Clear();
                    _isDirty = false;
                }

                // Events class version
                public float Version { get; set; } = 1.0f;

                [JsonIgnore]
                private SortedEventCollection _eventList = new();

                public SortedEventCollection EventList
                {
                    get => _eventList;
                    set
                    {
                        if (_eventList != value)
                        {
                            _eventList = value;
                            IsDirty = true;
                        }
                    }
                }

                /// <summary>
                /// This method will be called whenever the Events ObservableCollection<string> collection changes
                /// </summary>
                /// <param name="sender"></param>
                /// <param name="e"></param>
                private void CollectionChangedHandler(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    // This code will be executed when the collection is changed
                    IsDirty = true;
                }


                [JsonIgnore]
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
            public EventsClass Events { get; set; } = new EventsClass();


            public class CalibrationClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public CalibrationClass()
                {
                    // Subscribe to the collection's CollectionChanged event
                    _calibrationDataList.CollectionChanged += CollectionChangedHandler;

                }

                /// <summary>
                /// Clear down the CalibrationClass
                /// </summary>
                public void Clear()
                {
                    _allowMultipleCalibrationData = false;
                    _preferredCalibrationDataIndex = -1;
                    _calibrationDataList.Clear();
                    _isDirty = false;
                }

                // Calibration class version
                public float Version { get; set; } = 1.0f;

                // Values
                [JsonIgnore]
                private bool? _allowMultipleCalibrationData = false;

                [JsonIgnore]
                private int _preferredCalibrationDataIndex = -1;

                [JsonIgnore]
                private ObservableCollection<CalibrationData> _calibrationDataList = new();


                // Setters and getters
                public bool? AllowMultipleCalibrationData
                {
                    get => _allowMultipleCalibrationData;
                    set
                    {
                        if (_allowMultipleCalibrationData != value)
                        {
                            _allowMultipleCalibrationData = value;
                            IsDirty = true;
                        }
                    }
                }

                public int PreferredCalibrationDataIndex
                {
                    get => _preferredCalibrationDataIndex;
                    set
                    {
                        if (_preferredCalibrationDataIndex != value)
                        {
                            _preferredCalibrationDataIndex = value;
                            IsDirty = true;
                        }
                    }
                }

                [JsonConverter(typeof(CalibrationDataListJsonConverter))]
                [JsonProperty("CalibrationDataList")]
                public ObservableCollection<CalibrationData> CalibrationDataList
                {
                    get => _calibrationDataList;
                    set
                    {
                        if (_calibrationDataList != value)
                        {
                            _calibrationDataList = value;
                            IsDirty = true;
                        }
                    }
                }


                /// <summary>
                /// This method will be called whenever the LeftMediaFileNames or RightMediaFileNames ObservableCollection<string> collection changes
                /// </summary>
                /// <param name="sender"></param>
                /// <param name="e"></param>
                private void CollectionChangedHandler(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    // This code will be executed when the collection is changed
                    IsDirty = true;
                }


                [JsonIgnore]
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


                /// <summary>
                /// Returns the preferred calibration data
                /// </summary>
                /// <returns></returns>
                public CalibrationData? GetPreferredCalibationData(int? frameWidth, int? frameHeight)
                {
                    CalibrationData? ret;

                    if (_calibrationDataList is not null)
                    {
                        if (_preferredCalibrationDataIndex >= 0 && _preferredCalibrationDataIndex < _calibrationDataList.Count)
                        {
                            ret = _calibrationDataList[_preferredCalibrationDataIndex];

                            if (frameWidth is not null && frameHeight is not null)
                            {
                                if (ret.FrameSizeCompare((int)frameWidth, (int)frameHeight))
                                    return ret;
                            }
                        }
                    }
                    return null;
                }


                /// 
                /// EVENTS
                /// 
                private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }

            }
            public CalibrationClass Calibration { get; set; } = new CalibrationClass();

        }
        public DataClass Data { get; set; } = new DataClass();


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


        public bool IsDirty 
        {   get
            {
                if (Data.Info.IsDirty || Data.Media.IsDirty || Data.Sync.IsDirty || Data.Events.IsDirty || Data.Calibration.IsDirty)
                {
                    return  true;
                }
                return false;                
            }
            private set
            {                
                Data.Info.IsDirty = value;
                Data.Media.IsDirty = value;
                Data.Sync.IsDirty = value;
                Data.Events.IsDirty = value;
                Data.Calibration.IsDirty = value;
                OnPropertyChanged();
            }
        }
        public bool IsLoaded { get; private set; } = false;


        public Project(Reporter _report)
        {
            Report = _report;                                            
        }


        /// <summary>
        /// Clear down the Project class
        /// </summary>
        public void Clear()
        {
            Data.Clear();
        }


        /// <summary>
        /// Load a project from a json file
        /// </summary>
        /// <param name="projectFileSpec"></param>
        /// <returns></returns>
        public async Task<int> ProjectLoad(string projectFileSpec)
        {
            int ret = -1;
            string? json = null;

            if (Path.GetExtension(projectFileSpec).Equals(".Survey", StringComparison.OrdinalIgnoreCase))
            {

                try
                {
                    json = File.ReadAllText(projectFileSpec);
                }
                catch (FileNotFoundException e)
                {
                    ret = -2;
                    Report?.Warning("", $"Load project failed because the project file couldn't be found, project file:{projectFileSpec}. {e.Message}");
                }
                catch (UnauthorizedAccessException e)
                {
                    ret = -3;
                    Report?.Warning("", $"Load project failed because you do not have permission to read this file, project file:{projectFileSpec}. {e.Message}");
                }
                catch (DirectoryNotFoundException e)
                {
                    ret = -4;
                    Report?.Warning("", $"Load project failed because the specified directory could not be found, project file:{projectFileSpec}. {e.Message}");
                }
                catch (PathTooLongException e)
                {
                    ret = -5;
                    Report?.Warning("", $"Load project failed because the file name is too long, project file:{projectFileSpec}. The specified path, file name, or both exceed the system-defined maximum length. {e.Message}");
                }
                catch (IOException e)
                {
                    ret = -6;
                    Report?.Warning("", $"Load project failed because an I/O error occurred, project file:{projectFileSpec}. {e.Message}");
                }
                catch (Exception e)
                {
                    ret = -7;
                    Report?.Warning("", $"Load project failed because an unexpected error occurred, project file:{projectFileSpec}. {e.Message}");
                }

                if (json != null)
                {
                    var settings = new JsonSerializerSettings();
                    settings.Converters.Add(new EventJsonConverter());

                    DataClass? data = JsonConvert.DeserializeObject<DataClass>(json, settings);

                    if (data != null)
                    {
                        Data = data;
                        ret = SetProjectNameAndPath(projectFileSpec);

                        IsDirty = false;
                        IsLoaded = true;

                        // Start the autosave task in background
                        _ = Task.Run(() => AutosaveWorkAsync());
                    }
                }
            }
            else if (Path.GetExtension(projectFileSpec).Equals(".EMObs", StringComparison.OrdinalIgnoreCase))
            {

                var (result, errorMessage) = await ProjectLoadEMObs(projectFileSpec);

                if (result != 0)
                {
                    ret = result;
                    Report?.Warning("", $"Load project failed, project file:{projectFileSpec}. {errorMessage}");
                }
                else
                    ret = 0;
            }
            else
            {
                ret = -8;
                Report?.Warning("", $"Load project failed because the survey has an unsupported extension type, project file:{projectFileSpec}.");
            }

            return ret;
        }


        /// <summary>
        /// Save a project to a json file using the project current name and path
        /// </summary>
        /// <returns></returns>
        public int ProjectSave()
        {
            int ret = -1;

            // Stop any reentry
            lock (_lockObject)
            {
                if (Data.Info.ProjectPath != null && Data.Info.ProjectFileName != null)
                {
                    var settings = new JsonSerializerSettings
                    {
                        Formatting = Formatting.Indented,  // For pretty-printing the JSON
                        Converters = new List<JsonConverter> { new EventJsonConverter() }
                    };

                    string json = JsonConvert.SerializeObject(Data, settings);


                    string filePath = Path.Combine(Data.Info.ProjectPath, Data.Info.ProjectFileName);

                    try
                    {
                        File.WriteAllText(filePath, json);

                        this.IsDirty = false;
                        ret = 0;
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        ret = -1;
                        Report?.Warning("", $"Save project failed due to an unauthorized access request, project file:{filePath}. You do not have permission to write to this file. {e.Message}");
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        ret = -2;
                        Report?.Warning("", $"Save project failed due to a bad directory, project file:{filePath}. The specified directory could not be found. {e.Message}");
                    }
                    catch (PathTooLongException e)
                    {
                        ret = -3;
                        Report?.Warning("", $"Save project failed due to the file name too long, project file:{filePath}. The specified path, file name, or both exceed the system-defined maximum length. {e.Message}");
                    }
                    catch (IOException e)
                    {
                        ret = -4;
                        Report?.Warning("", $"Save project failed due to an I/O error, project file:{filePath}. {e.Message}");
                    }
                    catch (Exception e)
                    {
                        ret = -5;
                        Report?.Warning("", $"Save project failed due to an unexpected error, project file:{filePath}. {e.Message}");
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Save a project to a json file using passed file spec
        /// </summary>
        /// <param name="projectFileSpec"></param>
        /// <returns></returns>
        public int ProjectSaveAs(string projectFileSpec)
        {
            int ret = -1;

            // Set the project name using the stem of the file name and extract the project path
            ret = SetProjectNameAndPath(projectFileSpec);

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
                }
            }

            return ret;
        }


        /// <summary>
        /// Close a project 
        /// </summary>
        /// <returns></returns>
        public int ProjectClose()
        {
            StopAutosave();

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

            Data.Info.ProjectFileName = fileName;
            Data.Info.ProjectPath = directoryPath;

            return ret;
        }


        /// <summary>
        /// Add the media file to the list of eith left or right media files
        /// Only add the file name and not the path but check if the path is the same as the media file path
        /// </summary>
        /// <param name="mediaFileSpec"></param>
        /// <param name="FalseLeftTrueRight"></param>
        /// <returns>0 Ok</returns>
        /// <returns>-1 if the media file is in a different path to the other media files</returns>
        public int AddMediaFile(string mediaFileSpec, bool FalseLeftTrueRight)
        {
            int ret = 0;

            string? mediaFilePath = Path.GetDirectoryName(mediaFileSpec);

            if (mediaFilePath != null)
            {

                // Check the media file to be added is in the same path as the media path
                if (Data.Media.MediaPath == null)
                { 
                    Data.Media.MediaPath = mediaFilePath;
                    Report?.Out(Reporter.WarningLevel.Debug, FalseLeftTrueRight == true ? "L" : "R", $"Setting media path to: {mediaFilePath}");
                }

                mediaFilePath = mediaFilePath.ToLower().Trim();

                // Check if the media file is in the same path as the media path
                if (mediaFilePath.ToLower() == Data.Media.MediaPath.ToLower().Trim())
                {
                    if (FalseLeftTrueRight == false)
                    {
                        // Check if the media file is already in the list of left media files
                        if (!Data.Media.LeftMediaFileNames.Contains(mediaFileSpec))
                        {
                            Data.Media.LeftMediaFileNames.Add(Path.GetFileName(mediaFileSpec));
                            Report?.Out(Reporter.WarningLevel.Debug, "L", $"Adding media file:{Path.GetFileName(mediaFileSpec)} to the list of left media files");
                        }
                        else
                        {
                            ret = -3;
                            Report?.Warning("L", $"Media file:{Path.GetFileName(mediaFileSpec)} is already in the list of left media files, ignoring request to add.");
                        }
                    }
                    else
                    {
                        // Check if the media file is already in the list of right media files
                        if (!Data.Media.RightMediaFileNames.Contains(mediaFileSpec))
                        {
                            Data.Media.RightMediaFileNames.Add(Path.GetFileName(mediaFileSpec));
                            Report?.Out(Reporter.WarningLevel.Debug, "R", $"Adding media file:{Path.GetFileName(mediaFileSpec)} to the list of right media files");

                        }
                        else
                        {
                            ret = -3;
                            Report?.Warning("R", $"Media file:{Path.GetFileName(mediaFileSpec)} is already in the list of right media files, ignoring request to add.");
                        }
                    }
                }
                else
                {
                    ret = -1;
                    Report?.Warning(FalseLeftTrueRight == true ? "L" : "R", $"Media file:{mediaFileSpec} is not in the same path as the media path:{Data.Media.MediaPath}, ignoring request to add.");
                }
            }
            else
            {
                ret = -2;
                Report?.Warning(FalseLeftTrueRight == true ? "L" : "R", $"Media file:{mediaFileSpec} required a path, ignoring request to add.");
            }

            return ret;
        }


        // None, Found, NotFound, FoundButDescriptionDiffer
        public enum CalibrationDataListResult
        {
            None,
            Found, 
            NotFound, 
            FoundButDescriptionDiffer
        }

        public CalibrationDataListResult IsInCalibrationDataList(CalibrationData calibrationData, out int index)
        {
            CalibrationDataListResult result = CalibrationDataListResult.NotFound;

            // Reset
            index = -1;

            if (this.Data.Calibration.CalibrationDataList is not null)
            {
                for (int i = 0; i < this.Data.Calibration.CalibrationDataList.Count; i++)
                {
                    CalibrationData calibrationDataItem = Data.Calibration.CalibrationDataList[i];

                    if (calibrationData.Compare(calibrationDataItem) == true)
                    {
                        index = i;
                        result = CalibrationDataListResult.Found;
                        break;
                    }
                    else if (calibrationData.LeftCalibrationCameraData == calibrationDataItem.LeftCalibrationCameraData &&
                             calibrationData.RightCalibrationCameraData == calibrationDataItem.RightCalibrationCameraData &&
                             calibrationData.CalibrationStereoCameraData == calibrationDataItem.CalibrationStereoCameraData)
                    {
                        index = i;
                        result = CalibrationDataListResult.FoundButDescriptionDiffer;
                        break;
                    }
                }
            }

            return result;
        }


        /// <summary>
        /// Start the auto save task
        /// </summary>
        /// <returns></returns>
        public async Task AutosaveWorkAsync()
        {
            _isAutoSaveRunning = true;
            _autoSaveStopped = false;

            while (_isAutoSaveRunning)
            {
                try
                {
                    if (IsDirty)
                    {
                        // Your logic to save the current work state
                        ProjectSave();
                        Report?.Out(Reporter.WarningLevel.Debug, "", $"Autosave completed");
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception using your preferred logging approach
                    Report?.Warning("", $"Error during autosave: {ex.Message}");
                }
                await Task.Delay(_autosaveInterval);
            }

            _autoSaveStopped = true;
        }


        /// <summary>
        /// Request the autosave task to stop
        /// </summary>
        public void StopAutosave()
        {
            if (_isAutoSaveRunning)
            {
                _isAutoSaveRunning = false;

                int maxTryCount = 100*5; // 5 seconds

                // Wait for the autosave task to stop
                while (!_autoSaveStopped && maxTryCount > 0)
                {
                    Task.Delay(100);
                    maxTryCount--;
                }

                if (!_autoSaveStopped)
                    Report?.Warning("", "Autosave task failed to stop.");

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


    public class CalibrationDataListJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ObservableCollection<CalibrationData>);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            ObservableCollection<CalibrationData>? calibrationDataList = new ObservableCollection<CalibrationData>();

            if (reader.TokenType != JsonToken.Null)
            {
                calibrationDataList = new();

                var array = JArray.Load(reader);

                if (array is not null)
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        CalibrationData calibrationData = new();
                        int ret = calibrationData.LoadFromJson(array[i].ToString());
                        if (ret == 0)
                        {
                            calibrationDataList.Add(calibrationData);
                        }
                    }
                }
            }

            return calibrationDataList;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ObservableCollection<CalibrationData>? calibrationDataList = (ObservableCollection<CalibrationData>?)value;

            if (calibrationDataList is not null && calibrationDataList.Count > 0)
            {
                writer.WriteStartArray();

                foreach (CalibrationData item in calibrationDataList)
                {
                    string jsonItem;
                    int ret = item.SaveToJson(out jsonItem);

                    if (ret == 0)
                    {
                        writer.WriteValue(jsonItem);
                    }
                }
                writer.WriteEndArray();

            }
        }
    }
}
