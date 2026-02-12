using Emgu.CV;
using Microsoft.UI.Composition;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

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

    [Flags]
    public enum BestFrameReason
    {
        None = 0,
        SensorCoverage = 1 << 0,
        PoseDiversity = 1 << 1,
        ManuallyIgnored = 1 << 2,
        ManuallyAdded = 1 << 3,
    }

    public sealed record BestFrame(int FrameIndex, BestFrameReason Reason);


    public partial class CalibProject : INotifyPropertyChanged
    {
        // Event handler for property changed
        public event PropertyChangedEventHandler? PropertyChanged;

        readonly Reporter Report;

        // Auto Save variables
        private readonly TimeSpan autosaveInterval = TimeSpan.FromSeconds(20);
        private CancellationTokenSource? _autosaveCts;
        private Task? _autosaveTask;

        // Lock object for thread safety
        // Use to stop the auto save and save methods from being called at the same time
        private readonly object _lockObject = new();

        // Generates an instance ID only used for debugging
        private static int _instanceCounter = 0;
        private readonly int _instanceId = Interlocked.Increment(ref _instanceCounter);
        [JsonIgnore]
        public string DebugInstanceTag => $"CalibProject#{_instanceId} (idHash={RuntimeHelpers.GetHashCode(this)}, gen={GC.GetGeneration(this)})";


        // The DynamicDependency are there to stop the Linker in Release mode from trimming 
        // so the public properties which stopped the NewtonSoft JSon method working because
        // they rely on reflection.  The problem was observed on the DataClass.CacheClass
        // on the three Guid properties and on the mono and stereo results arrays in
        // DataClass.CalibrationResultClass. 
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(CalibProject))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(CalibProject.DataClass))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(CalibProject.DataClass.CalibrationResultClass))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(CalibProject.DataClass.CacheClass))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(CalibProject.DataClass.InfoClass))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(CalibProject.DataClass.MediaClass))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(CalibProject.DataClass.SyncClass))]
        public CalibProject(Reporter _report)
        {
            Report = _report;
            Clear();
        }

        public partial class DataClass
        {
            /// <summary>
            /// Clear the DataClass
            /// </summary>
            public void Clear()
            {
                Info.Clear();
                Media.Clear();
                ChArUcoBoardDefinition.Clear();
                Sync.Clear();
                CalibrationResults.Clear();
                Cache.Clear();
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
                    _leftMonoBestFrames = new ListTrackDirty<BestFrame>(() => MarkDirtyAndRaise(nameof(LeftMonoBestFrames)));
                    _rightMonoBestFrames = new ListTrackDirty<BestFrame>(() => MarkDirtyAndRaise(nameof(RightMonoBestFrames)));
                    _stereoBestFrames = new ListTrackDirty<BestFrame>(() => MarkDirtyAndRaise(nameof(StereoBestFrames)));

                    _isDirty = false;
                }

                // Media class version
                public float Version { get; set; } = 2.0f;

                // Values
                private StereoMonoMediaSetMode _stereoMonoMediaSetMode = StereoMonoMediaSetMode.None;
                private string _mediaPath = string.Empty;
                private string _leftMonoMP4FileName = string.Empty;
                private string _rightMonoMP4FileName = string.Empty;
                private string _leftStereoMP4FileName = string.Empty;
                private string _rightStereoMP4FileName = string.Empty;
                private int _frameWidth = 0;
                private int _frameHeight = 0;
                private ListTrackDirty<BestFrame> _leftMonoBestFrames = new(() => { });
                private ListTrackDirty<BestFrame> _rightMonoBestFrames = new(() => { });
                private ListTrackDirty<BestFrame> _stereoBestFrames = new(() => { });

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

                public List<BestFrame> LeftMonoBestFrames
                {
                    get => _leftMonoBestFrames;
                    set
                    {
                        if (!ReferenceEquals(_leftMonoBestFrames, value))
                        {
                            _leftMonoBestFrames = Wrap(value, nameof(LeftMonoBestFrames));
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                public List<BestFrame> RightMonoBestFrames
                {
                    get => _rightMonoBestFrames;
                    set
                    {
                        if (!ReferenceEquals(_rightMonoBestFrames, value))
                        {
                            _rightMonoBestFrames = Wrap(value, nameof(RightMonoBestFrames));
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                public List<BestFrame> StereoBestFrames
                {
                    get => _stereoBestFrames;
                    set
                    {
                        if (!ReferenceEquals(_stereoBestFrames, value))
                        {
                            _stereoBestFrames = Wrap(value, nameof(StereoBestFrames));
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

                private ListTrackDirty<BestFrame> Wrap(List<BestFrame>? value, string propertyName)
                {
                    var list = new ListTrackDirty<BestFrame>(() => MarkDirtyAndRaise(propertyName));
                    if (value is not null && value.Count > 0)
                        list.AddRange(value); // will trigger dirty; we immediately neutralize it below

                    // When setting from deserialization / assignment, you usually don't want this to mark dirty.
                    // So reset IsDirty after populating.
                    _isDirty = false;
                    return list;
                }

                private void MarkDirtyAndRaise(string propertyName)
                {
                    IsDirty = true;
                    OnPropertyChanged(propertyName);
                }

                // Derived list<> to enable a dirty flag to be set
                private sealed class ListTrackDirty<T>(Action changed) : List<T>
                {
                    private readonly Action _changed = changed ?? throw new ArgumentNullException(nameof(changed));

                    public new void Add(T item)
                    {
                        base.Add(item);
                        _changed();
                    }

                    public new bool Remove(T item)
                    {
                        bool removed = base.Remove(item);
                        if (removed)
                            _changed();
                        return removed;
                    }

                    public new void RemoveAt(int index)
                    {
                        base.RemoveAt(index);
                        _changed();
                    }

                    public new int RemoveAll(Predicate<T> match)
                    {
                        int removed = base.RemoveAll(match);
                        if (removed > 0)
                            _changed();
                        return removed;
                    }
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
                public float Version { get; set; } = 2.0f;

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

            public partial class CacheClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public CacheClass()
                {       
                }

                public void Clear()
                {
                    _projectFileNameSavedUnder = string.Empty;

                    // Load Guid in case new project (may get over written by a de-serialization)
                    // Cache should always have a valid Guid's setup
                    _leftMonoFrameSetCacheGuid = Guid.NewGuid().ToString();
                    _rightMonoFrameSetCacheGuid = Guid.NewGuid().ToString();
                    _stereoFrameSetCacheGuid = Guid.NewGuid().ToString();

                    IsDirty = false;
                }


                // Info class version
                public float Version { get; set; } = 1.0f;

                // Values
                private string _projectFileNameSavedUnder = string.Empty;
                private string _leftMonoFrameSetCacheGuid = string.Empty;
                private string _rightMonoFrameSetCacheGuid = string.Empty;
                private string _stereoFrameSetCacheGuid = string.Empty;


                // Setters and getters

                // The project file name the cache files were saved under
                // This is separate from the Info.ProjectFileName in
                // case the project gets renamed and the cache files
                // need to be renamed to match

                public string ProjectFileNameSavedUnder
                {
                    get => _projectFileNameSavedUnder;
                    set
                    {
                        if (_projectFileNameSavedUnder != value)
                        {
                            _projectFileNameSavedUnder = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

  
                public string LeftMonoFrameSetCacheGuid
                {
                    get => _leftMonoFrameSetCacheGuid;
                    set
                    {
                        if (_leftMonoFrameSetCacheGuid != value)
                        {
                            _leftMonoFrameSetCacheGuid = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

    
                public string RightMonoFrameSetCacheGuid
                {
                    get => _rightMonoFrameSetCacheGuid;
                    set
                    {
                        if (_rightMonoFrameSetCacheGuid != value)
                        {
                            _rightMonoFrameSetCacheGuid = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }


                public string StereoFrameSetCacheGuid
                {
                    get => _stereoFrameSetCacheGuid;
                    set
                    {
                        if (_stereoFrameSetCacheGuid != value)
                        {
                            _stereoFrameSetCacheGuid = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                // Helpers
                [JsonIgnore]
                public string LeftMonoFrameSetCacheFileName
                {
                    get => $"{Path.GetFileNameWithoutExtension(ProjectFileNameSavedUnder)}_LMono_{LeftMonoFrameSetCacheGuid}_FrameSet.json";
                }
                [JsonIgnore]
                public string RightMonoFrameSetCacheFileName
                {
                    get => $"{Path.GetFileNameWithoutExtension(ProjectFileNameSavedUnder)}_RMono_{RightMonoFrameSetCacheGuid}_FrameSet.json";
                }
                [JsonIgnore]
                public string StereoFrameSetCacheFileName
                {
                    get => $"{Path.GetFileNameWithoutExtension(ProjectFileNameSavedUnder)}_Stereo_{StereoFrameSetCacheGuid}_FrameSet.json";
                }

                [JsonIgnore]
                public string LeftMonoFrameSetCacheFileSpec
                {
                    get => Path.Combine(ApplicationData.Current.LocalFolder.Path, LeftMonoFrameSetCacheFileName);
                }
                [JsonIgnore]
                public string RightMonoFrameSetCacheFileSpec
                {
                    get => Path.Combine(ApplicationData.Current.LocalFolder.Path, RightMonoFrameSetCacheFileName);
                }
                [JsonIgnore]
                public string StereoFrameSetCacheFileSpec
                {
                    get => Path.Combine(ApplicationData.Current.LocalFolder.Path, StereoFrameSetCacheFileName);
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

            public CalibrationBoardDefinition ChArUcoBoardDefinition { get; set; } = new();

            public SyncClass Sync { get; set; } = new();

            public CalibrationResultClass CalibrationResults { get; set; } = new();

            public CacheClass Cache { get; set; } = new();
        }

        public DataClass Data = new();

        public bool IsDirty
        {
            get
            {
                if (Data.Info.IsDirty || 
                    Data.Media.IsDirty || 
                    Data.ChArUcoBoardDefinition.IsDirty ||
                    Data.Sync.IsDirty ||
                    Data.CalibrationResults.IsDirty ||
                    Data.Cache.IsDirty)
                {
                    return true;
                }
                return false;
            }
            private set
            {
                Data.Info.IsDirty = value;
                Data.Media.IsDirty = value;
                Data.ChArUcoBoardDefinition.IsDirty = value;
                Data.Sync.IsDirty = value;
                Data.CalibrationResults.IsDirty = value;
                Data.Cache.IsDirty = value;
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
        /// Load the calibration project data from a file as JSON.
        /// </summary>
        /// <param name="projectFileSpec"></param>
        /// <param name="autoSave"></param>
        /// <returns></returns>
        [RequiresUnreferencedCode("ProjectLoadAsync uses Json.NET serialization which may not be compatible with trimming.")]
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

                    // New load so not dirty
                    IsDirty = false;

                    // Ensure the project name in the project JSON matches the actually file name
                    ret = SetProjectNameAndPath(projectFileSpec);

                    
                    IsLoaded = true;

                    if (autoSave)
                    {
                        // Start the auto save task in background                        
                        // The AutoSaveEnable flag is checked at the point the save is about to be made
                        // The advantage with always having the timer running an checking if auto save is
                        // enabled last is that the Auto Save settings can be changed and the application
                        // doesn't need to be restarted.
                        await StartAutoSaveAsync();
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Save the calibration project data to a file as JSON.
        /// Note this is a synchronous method to avoid reentry from the auto save task
        /// </summary>
        /// <returns></returns>
        [RequiresUnreferencedCode("ProjectSave uses Json.NET serialization which may not be compatible with trimming.")]
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

                // Adjust MediaPath if possible to be relative to project path
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

                // If the ProjectFileName changed vs. the cache file naming
                // then rename the cache files
                if (Data.Info.ProjectPath is not null)
                {
                    string expectedCacheProjectFileName = Data.Cache.ProjectFileNameSavedUnder;

                    if (string.IsNullOrEmpty(expectedCacheProjectFileName))
                    {
                        // First Save so nothing will need renaming
                        // Just setup the project file name in the cache class
                        // So cache file name/ file spec can be created
                        Data.Cache.ProjectFileNameSavedUnder = Data.Info.ProjectFileName ?? "";
                    }
                    else if (Data.Info.ProjectFileName != expectedCacheProjectFileName)
                    {
                        string oldLeftMonoCacheFileSpec = string.Empty;
                        string oldRightMonoCacheFileSpec = string.Empty;
                        string oldStereoCacheFileSpec = string.Empty;

                        // Get the current cache file names
                        if (!string.IsNullOrEmpty(Data.Cache.LeftMonoFrameSetCacheFileName))
                            oldLeftMonoCacheFileSpec = Data.Cache.LeftMonoFrameSetCacheFileSpec;

                        if (!string.IsNullOrEmpty(Data.Cache.RightMonoFrameSetCacheFileName))
                            oldRightMonoCacheFileSpec = Data.Cache.RightMonoFrameSetCacheFileSpec;

                        if (!string.IsNullOrEmpty(Data.Cache.StereoFrameSetCacheFileName))
                            oldStereoCacheFileSpec = Data.Cache.StereoFrameSetCacheFileSpec;

                        // Apply the new project file name to the cache class
                        // This is to enable the generation of new cache file names
                        Data.Cache.ProjectFileNameSavedUnder = Data.Info.ProjectFileName ?? "";

                        string newLeftMonoCacheFileSpec = Data.Cache.LeftMonoFrameSetCacheFileSpec;
                        string newRightMonoCacheFileSpec = Data.Cache.RightMonoFrameSetCacheFileSpec;
                        string newStereoCacheFileSpec = Data.Cache.StereoFrameSetCacheFileSpec;

                        // Rename left mono cache file (if any)
                        if (!string.IsNullOrEmpty(Data.Cache.LeftMonoFrameSetCacheFileName) &&
                            File.Exists(oldLeftMonoCacheFileSpec))
                        {
                            try
                            {
                                File.Move(oldLeftMonoCacheFileSpec, newLeftMonoCacheFileSpec, true/*overwrite*/);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error renaming left mono cache file {oldLeftMonoCacheFileSpec} to {newLeftMonoCacheFileSpec}: {ex.Message}");
                            }
                        }
                        // Rename right mono cache file (if any)
                        if (!string.IsNullOrEmpty(Data.Cache.RightMonoFrameSetCacheFileName) &&
                            File.Exists(oldRightMonoCacheFileSpec))
                        {
                            try
                            {
                                File.Move(oldRightMonoCacheFileSpec, newRightMonoCacheFileSpec, true/*overwrite*/);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error renaming right mono cache file {oldRightMonoCacheFileSpec} to {newRightMonoCacheFileSpec}: {ex.Message}");
                            }
                        }
                        // Rename stereo cache file (if any)
                        if (!string.IsNullOrEmpty(Data.Cache.StereoFrameSetCacheFileName) &&
                            File.Exists(oldStereoCacheFileSpec))
                        {
                            try
                            {
                                File.Move(oldStereoCacheFileSpec, newStereoCacheFileSpec, true/*overwrite*/);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error renaming stereo cache file {oldStereoCacheFileSpec} to {newStereoCacheFileSpec}: {ex.Message}");
                            }
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

                    // Convert to JSon
                    string json = string.Empty;

                    try
                    {
                        json = JsonConvert.SerializeObject(Data, settings);
                    }
                    catch (Exception ex)
                    {
                        Report?.Error(
                            "",
                            $"Failed to save project file, failed to convert to JSon. {ex.Message}. Inner: {ex.InnerException?.Message}"
                        );
                        ret = -6;
                    }

                    // Restore project path
                    Data.Info.ProjectPath = projectPath;

                    if (!string.IsNullOrEmpty(json))
                    {
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
            }

            return ret;
        }


        /// <summary>
        /// Save the calibration project data to a file as JSON.
        /// </summary>
        /// <param name="projectFileSpec"></param>
        /// <returns></returns>
        [RequiresUnreferencedCode("ProjectSaveAsAsync uses Json.NET serialization which may not be compatible with trimming.")]
        public async Task<int> ProjectSaveAsAsync(string projectFileSpec)
        {
            // Set the project name using the stem of the file name and extract the project path
            int ret = SetProjectNameAndPath(projectFileSpec);

            if (ret == 0)
            {
                // Save the project to a JSON file
                ret = ProjectSave();

                if (ret == 0)
                {
                    // Reset the dirty flag
                    IsDirty = false;

                    // A Save As could be the first save of a new project to set IsLoaded to true
                    IsLoaded = true;

                    // Auto save 
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

            // Force the ProjectFileName to match the actual file name (in case the file was renamed for example)
            Data.Info.ProjectFileName = fileName ?? "";

            // Project path is only set while the project is open so don't let this set the IsDirty flag
            bool isDirtyRemembered = Data.Info.IsDirty;            
            Data.Info.ProjectPath = directoryPath ?? "";
            Data.Info.IsDirty = isDirtyRemembered;

            return ret;
        }


        /// <summary>
        /// Find the best mono calibration result for either:
        /// Both (pass null) - best average Re-projection RMS score
        /// Left only (pass true) - best left side Re-projection RMS score
        /// Right only (pass true) - best right side Re-projection RMS score
        /// </summary>
        /// <param name="trueLeftRightFalseNullBoth"></param>
        /// <returns>null if fails</returns>
        public CalibrationParameters? ReturnBestMonoCalibrationCameraData(bool? trueLeftRightFalseNullBoth)
        {
            double bestScore = double.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < Data.CalibrationResults.CalibrationStereoCameraDataArray.Length; i++)
            {
                var leftMonoResult = Data.CalibrationResults.LeftMonoCalibrationCameraDataArray[i];
                var rightMonoResult = Data.CalibrationResults.RightMonoCalibrationCameraDataArray[i];

                // Check suitable results exist
                if (trueLeftRightFalseNullBoth is null)
                {
                    if (leftMonoResult is null || rightMonoResult is null)
                        continue;
                }
                else if (trueLeftRightFalseNullBoth == true)
                {
                    if (leftMonoResult is null)
                        continue;

                }
                else
                {
                    if (rightMonoResult is null)
                        continue;
                }


                // Define weighted score (you can tune weights as needed)
                double score = 0;
                if (trueLeftRightFalseNullBoth is null)
                {
                    if (leftMonoResult is not null && rightMonoResult is not null)
                        score = (leftMonoResult.ReprojectionRMS /*???+ 0.2 * stereoResult.MaxError*/ + rightMonoResult.ReprojectionRMS) / 2;
                }
                else if (trueLeftRightFalseNullBoth == true)
                {
                    if (leftMonoResult is not null)
                        score = leftMonoResult.ReprojectionRMS /*???+ 0.2 * stereoResult.MaxError*/;
                }
                else
                {
                    if (rightMonoResult is not null)
                        score = rightMonoResult.ReprojectionRMS;
                }

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

        
        /// <summary>
        /// Given the StereoMonoMediaSetMode check if the calibration is complete
        /// </summary>
        /// <returns></returns>
        public bool IsCalibrationReady
        {            
            get 
            {
                bool ret = false;

                switch (Data.Media.StereoMonoMediaSetMode)
                {
                    case StereoMonoMediaSetMode.StereoOnlyMediaSet:
                    case StereoMonoMediaSetMode.MonoAndStereoMediaSet:
                        {
                            var bestStereo = ReturnBestStereoCalibrationCameraData();
                            ret = bestStereo != null;
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoPairOnlyMediaSet:
                        {
                            if (Data.CalibrationResults.LeftMonoCalibrationCameraDataArray.Any(item => item != null) &&
                                Data.CalibrationResults.RightMonoCalibrationCameraDataArray.Any(item => item != null))
                            {
                                ret = true;
                            }
                        }
                        break;

                    case StereoMonoMediaSetMode.MonoSingleOnlyMediaSet:
                        {
                            if (Data.CalibrationResults.LeftMonoCalibrationCameraDataArray.Any(item => item != null))
                            {
                                ret = true;
                            }
                        }
                        break;

                    default:
                        return false;
                }

                return ret;
            }
        }

        [RequiresUnreferencedCode("Calls Newtonsoft.Json.Converters.StringEnumConverter.StringEnumConverter()")]
        private static JsonSerializerSettings CreateJsonOptions()
        {
            var opts = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };

            opts.Converters.Add(new MatrixJsonConverter());
            opts.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter()); // explicit to try to fix release mode linker trimming issues

            return opts;
        }


        /// <summary>
        /// Start the auto save task
        /// <summary>
        [RequiresUnreferencedCode("StartAutoSaveAsync uses Json.NET serialization which may not be compatible with trimming.")]
        private async Task StartAutoSaveAsync()
        {
            Debug.WriteLine($"{DebugInstanceTag}: StartAutoSaveAsync entered. IsDirty={IsDirty}");

            await StopAutoSaveAsync(); // Ensure any previous auto save task is stopped

            _autosaveCts = new CancellationTokenSource();
            _autosaveTask = Task.Run(async () =>
            {
                // Task naming for debugging
                if (Thread.CurrentThread.Name == null)
                {
                    Thread.CurrentThread.Name = "CalibProject.AutosaveWork()";
                }

                Report?.Info("", $"Auto save thread started on request");

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
                                Report?.Debug("", $"Auto-save Saved completed");
                            }
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Graceful cancellation
                        break;
                    }
                }
                Report?.Info("", $"Auto-save thread stopped on request");
            });
        }


        /// <summary>
        /// Request the auto save task to stop
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

                Debug.WriteLine($"{DebugInstanceTag}: StopAutoSaveAsync exited.");
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
}
