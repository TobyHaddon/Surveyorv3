// Surveyor Project
// Hold the all the survey information and results
// 
// Version 1.1
// Make Partial class to allow for the addition of ProjectEMObs
// Version 1.2
// Added the CameraID to the MediaClass
// Version 1.3
// Fixed GetHashCode()  21 Aug 2025
// Version 1.4
// Support loading SurveyMeasurement.Measurement and SurveyMeasurement.Measurment
// Version 1.5
// I've moved the version number in the SurveyRulesClass to 2.0 because I've added flags to SurveyRulesCalc
// to indicate which of the specific rules passed/failed
// Version 1.6
// Added SurveyType to InfoClass and moved it's version to 2.0


using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using Surveyor.Events;
using Surveyor.Helper;
using Surveyor.User_Controls;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SurveyorCalibrationData;
using System.Diagnostics;


namespace Surveyor
{
    public partial class Survey : INotifyPropertyChanged
    {
        // Type of survey
        public enum SurveyType
        {
            Unknown = 0,
            StereoFish,
            MonoFish,
            MonoBenthic
        }

        // Used to report info, warnings and errors to the user
        private Reporter? Report { get; set; } = null;


        // Auto Save variables
        private readonly TimeSpan autosaveInterval = TimeSpan.FromSeconds(20);
        private CancellationTokenSource? _autosaveCts;
        private Task? _autosaveTask;

        // Lock object for thread safety
        // Use to stop the auto save and save methods from being called at the same time
        private readonly object _lockObject = new();

        // Event handler for property changed
        public event PropertyChangedEventHandler? PropertyChanged;


        public partial class DataClass
        {
            /// <summary>
            /// Clear the DataClass
            /// </summary>
            public void Clear()
            {
                Info.Clear();
                Media.Clear();
                Sync.Clear();
                Events.Clear();
                Calibration.Clear();
                SurveyRules.Clear();
            }


            /// <summary>
            /// Diags dump of class information
            /// </summary>
            public void DumpAllProperties(Reporter? report)
            {
                DumpClassPropertiesHelper.DumpAllProperties(Info, report, /*ignore*/"<Version>k__BackingField,_surveyFileName,_surveyPath,_surveyCode,_surveyAnalystName,_surveyDepth,_isDirty,PropertyChanged");
                DumpClassPropertiesHelper.DumpAllProperties(Media, report, /*ignore*/"<Version>k__BackingField,_mediaPath,_leftMediaFileNames,_rightMediaFileNames,_leftCameraID,_rightCameraID,_isDirty,PropertyChanged,LeftMediaFileNames,RightMediaFileNames");
                //DumpClassPropertiesHelper.DumpAllProperties(Media.LeftMediaFileNames);
                //DumpClassPropertiesHelper.DumpAllProperties(Media.RightMediaFileNames);
                DumpClassPropertiesHelper.DumpAllProperties(Sync, report, /*ignore*/"PropertyChanged,<Version>k__BackingField,_isSynchronized,_timeSpanOffset,_actualTimeSpanOffsetLeft,_actualTimeSpanOffsetRight,_isDirty");
                DumpClassPropertiesHelper.DumpAllProperties(Events, report, /*ignore*/"PropertyChanged,<Version>k__BackingField,_eventList,_isDirty,EventList");
                DumpClassPropertiesHelper.DumpAllProperties(Calibration, report, /*ignore*/"PropertyChanged,<Version>k__BackingField,_allowMultipleCalibrationData,_preferredCalibrationDataIndex,_calibrationDataList,_isDirty,CalibrationDataList");
                DumpClassPropertiesHelper.DumpAllProperties(SurveyRules, report, /*ignore*/"PropertyChanged,<Version>k__BackingField,_surveyRulesActive,_surveyRulesInherited,_surveyRulesData,_isDirty,SurveyRulesData");
                DumpClassPropertiesHelper.DumpAllProperties(SurveyRules.SurveyRulesData, report, /*ignore*/"PropertyChanged,<Version>k__BackingField,_rangeRuleActive,_rangeMin,_rangeMax,_rmsRuleActive,_rmsMax,_horizontalRangeRuleActive,_horizontalRangeLeft,\r\n_horizontalRangeRight,_verticalRangeRuleActive,_verticalRangeTop,_verticalRangeBottom,_isDirty");
            }


            public partial class InfoClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                /// <summary>
                /// Clear down the InfoClass
                /// </summary>
                public void Clear()
                {
                    _surveyType = SurveyType.Unknown;
                    _surveyFileName = null;
                    _surveyPath = null;
                    _isDirty = false;
                    _surveyCode = null;
                    _surveyAnalystName = null;
                    _surveyDepth = null;
                }


                // Info class version
                public float Version { get; set; } = 2.0f;

                // Values
                private SurveyType _surveyType = SurveyType.Unknown;
                private string? _surveyFileName = null;
                private string? _surveyPath = null;
                private string? _surveyCode = null;
                private string? _surveyAnalystName = null;
                private string? _surveyDepth = null;  // Normally 5,8,10,13,15 or Flat, Crest or Slope 

                // Setters and getters
                
                [JsonConverter(typeof(StringEnumConverter))]
                public SurveyType SurveyType
                {
                    get => _surveyType;
                    set
                    {
                        if (_surveyType != value)
                        {
                            _surveyType = value;
                            IsDirty = true;
                        }
                    }
                }

                public string? SurveyFileName
                {
                    get => _surveyFileName;
                    set
                    {
                        if (_surveyFileName != value)
                        {
                            _surveyFileName = value;
                            IsDirty = true;
                        }
                    }
                }

                public string? SurveyPath 
                {
                    get => _surveyPath;
                    set
                    {
                        if (_surveyPath != value)
                        {
                            _surveyPath = value;
                            IsDirty = true;
                        }
                    }
                }

                /// <summary>
                /// This is used for a string that IDs the survey i.e. [ReefCode]-[Depth]-[TransectNumber]-[YYYY-MM-DD]  e.g. CVW-10-1-2024-07-28 for Coral View , 10m depth, transect 1 on the 28th July 2024
                /// </summary>
                public string? SurveyCode
                {
                    get => _surveyCode;
                    set
                    {
                        if (_surveyCode != value)
                        {
                            _surveyCode = value;
                            IsDirty = true;
                        }
                    }
                }

                /// <summary>
                /// This is the name of the persion who analysed the survey (not the person who collected the data)
                /// </summary>
                public string? SurveyAnalystName
                {
                    get => _surveyAnalystName;
                    set
                    {
                        if (_surveyAnalystName != value)
                        {
                            _surveyAnalystName = value;
                            IsDirty = true;
                        }
                    }
                }

                /// <summary>
                /// This is the depth of the survey in metres as a number e.g. 10
                /// </summary>
                public string? SurveyDepth
                {
                    get => _surveyDepth;
                    set
                    {
                        if (_surveyDepth != value)
                        {
                            _surveyDepth = value;
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
            public InfoClass Info { get; } = new InfoClass();

            public partial class MediaClass : INotifyPropertyChanged
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
                    _leftCameraID = "";
                    _rightCameraID = "";
                    _isDirty = false;
                }


                // Media class version
                public float Version { get; set; } = 2.0f;

                [JsonIgnore]
                private string? _mediaPath = null;

                [JsonIgnore]
                private ObservableCollection<string> _leftMediaFileNames = [];

                [JsonIgnore]
                private ObservableCollection<string> _rightMediaFileNames = [];

                [JsonIgnore]
                private string _leftCameraID = "";

                [JsonIgnore]
                private string _rightCameraID = "";

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


                /// <summary>
                /// This method will be called whenever the LeftMediaFileNames or RightMediaFileNames ObservableCollection<string> collection changes
                /// </summary>
                /// <param name="sender"></param>
                /// <param name="e"></param>
                private void CollectionChangedHandler(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    // Any change to the list means data changed
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
            public MediaClass Media { get; } = new MediaClass();

            public partial class SyncClass : INotifyPropertyChanged
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
            public SyncClass Sync { get; } = new SyncClass();


            public partial class EventsClass : INotifyPropertyChanged
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
                public float Version { get; set; } = 2.0f;

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
            public EventsClass Events { get; } = new EventsClass();


            public partial class CalibrationClass : INotifyPropertyChanged
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
                    // Ensure events remain hooked after clear
                    _calibrationDataList.CollectionChanged -= CollectionChangedHandler;
                    _calibrationDataList.CollectionChanged += CollectionChangedHandler;
                    _isDirty = false;
                }

                // Calibration class version
                public float Version { get; set; } = 1.0f;

                // Values
                [JsonIgnore]
                private bool _allowMultipleCalibrationData = false;

                [JsonIgnore]
                private string? _calibrationInherited = null;

                [JsonIgnore]
                private int _preferredCalibrationDataIndex = -1;

                [JsonIgnore]
                private ObservableCollection<CalibrationData> _calibrationDataList = [];


                // Setters and getters

                [JsonProperty(nameof(AllowMultipleCalibrationData))]
                public bool AllowMultipleCalibrationData
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


                [JsonProperty(nameof(CalibrationInherited))]
                public string? CalibrationInherited
                {
                    get => _calibrationInherited;
                    set
                    {
                        if (_calibrationInherited != value)
                        {
                            _calibrationInherited = value;
                            IsDirty = true;
                        }
                    }
                }

                [JsonProperty(nameof(PreferredCalibrationDataIndex))]
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
                [JsonProperty(nameof(CalibrationDataList))]
                public ObservableCollection<CalibrationData> CalibrationDataList
                {
                    get => _calibrationDataList;
                    set
                    {
                        if (!ReferenceEquals(_calibrationDataList, value))
                        {
                            // Unhook old collection
                            if (_calibrationDataList is not null)
                                _calibrationDataList.CollectionChanged -= CollectionChangedHandler;

                            // Assign new (never allow null)
                            _calibrationDataList = value ?? new ObservableCollection<CalibrationData>();

                            // Hook new collection so add/remove sets IsDirty
                            _calibrationDataList.CollectionChanged += CollectionChangedHandler;

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
                    // Any change to the list means data changed
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


                /// <summary>
                /// Clones the CalibrationClass instance, including its collection of CalibrationData.
                /// Uses json serialization to create a deep copy of the object.
                /// </summary>
                /// <returns></returns>
                /// <exception cref="InvalidOperationException"></exception>
                public CalibrationClass Clone()
                {
                    var settings = new JsonSerializerSettings
                    {
                        Converters = [new CalibrationDataListJsonConverter()]
                    };

                    var json = JsonConvert.SerializeObject(this, settings);
                    var clone = JsonConvert.DeserializeObject<CalibrationClass>(json, settings)
                                ?? throw new InvalidOperationException("Failed to clone CalibrationClass.");

                    // Reset IsDirty
                    clone.IsDirty = false;

                    // Re-hook collection change event if needed
                    clone.CalibrationDataList.CollectionChanged += clone.CollectionChangedHandler;

                    return clone;
                }


                /// <summary>
                /// Used to copy the properties from another CalibrationClass instance.
                /// </summary>
                /// <param name="source"></param>
                /// <exception cref="ArgumentNullException"></exception>
                public void CopyFrom(CalibrationClass source)
                {
                    ArgumentNullException.ThrowIfNull(source);

                    // Clone first to ensure we're working from a stable snapshot
                    var clone = source.Clone();

                    AllowMultipleCalibrationData = clone.AllowMultipleCalibrationData;
                    CalibrationInherited = clone.CalibrationInherited;
                    PreferredCalibrationDataIndex = clone.PreferredCalibrationDataIndex;

                    // Replace CalibrationDataList with a new ObservableCollection so we preserve event wiring
                    CalibrationDataList = new ObservableCollection<CalibrationData>(clone.CalibrationDataList);

                    // Re-hook collection changed event
                    CalibrationDataList.CollectionChanged += CollectionChangedHandler;

                    IsDirty = true;
                }


                /// <summary>
                /// Get the hash code for the CalibrationClass instance. (ignores IsDirty)
                /// </summary>
                /// <returns></returns>
                public override int GetHashCode()
                {
                    return HashCode.Combine(Version, AllowMultipleCalibrationData, PreferredCalibrationDataIndex, CalibrationDataList);
                }

                /// 
                /// EVENTS
                /// 
                private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }

            }
            public CalibrationClass Calibration { get; } = new CalibrationClass();


            public partial class SurveyRulesClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public SurveyRulesClass()
                {
                }

                /// <summary>
                /// Clear down the SurveyRulesClass
                /// </summary>
                public void Clear()
                {
                    _surveyRulesActive = false;
                    _surveyRulesData.Clear();
                    _isDirty = false;
                }

                // SurveyRulesClass version
                public float Version { get; set; } = 2.0f;

                // Values
                [JsonIgnore]
                private bool _surveyRulesActive = false;

                [JsonIgnore]
                private string? _surveyRulesInherited = null;

                [JsonIgnore]
                private SurveyRulesData _surveyRulesData = new();


                // Setters and getters
                [JsonProperty(nameof(SurveyRulesActive))]
                public bool SurveyRulesActive
                {
                    get => _surveyRulesActive;
                    set
                    {
                        if (_surveyRulesActive != value)
                        {
                            _surveyRulesActive = value;
                            IsDirty = true;
                        }
                    }
                }


                [JsonProperty(nameof(SurveyRulesInherited))]
                public string? SurveyRulesInherited
                {
                    get => _surveyRulesInherited;
                    set
                    {
                        if (_surveyRulesInherited != value)
                        {
                            _surveyRulesInherited = value;
                            IsDirty = true;
                        }
                    }
                }


                [JsonProperty(nameof(SurveyRulesData))]
                public SurveyRulesData SurveyRulesData
                {
                    get => _surveyRulesData;
                    set
                    {
                        if (_surveyRulesData != value)
                        {
                            _surveyRulesData = value;
                            IsDirty = true;
                        }
                    }
                }

               
                private bool _isDirty;
                [JsonIgnore]
                public bool IsDirty
                {
                    get
                    {
                        if (_isDirty || _surveyRulesData.IsDirty)
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
                        if (_surveyRulesData.IsDirty != value)
                        {
                            _surveyRulesData.IsDirty = value;
                            anyChanged = true;
                        }
                        if (anyChanged)
                            OnPropertyChanged();
                        
                    }
                }


                /// <summary>
                /// Clones the SurveyRulesClass instance.
                /// Uses json serialization to create a deep copy of the object.
                /// </summary>
                /// <returns></returns>
                /// <exception cref="InvalidOperationException"></exception>
                public SurveyRulesClass Clone()
                {
                    var json = JsonConvert.SerializeObject(this);
                    var clone = JsonConvert.DeserializeObject<SurveyRulesClass>(json)
                                ?? throw new InvalidOperationException("Failed to clone SurveyRulesClass.");

                    // Reset IsDirty
                    clone.IsDirty = false;

                    return clone;
                }


                /// <summary>
                /// Used to copy the properties from another SurveyRulesClass instance.
                /// </summary>
                /// <param name="source"></param>
                /// <exception cref="ArgumentNullException"></exception>
                public void CopyFrom(SurveyRulesClass source)
                {
                    ArgumentNullException.ThrowIfNull(source);

                    // Clone first to ensure we're working from a stable snapshot
                    var clone = source.Clone();

                    SurveyRulesActive = clone.SurveyRulesActive;
                    SurveyRulesInherited = clone.SurveyRulesInherited;
                    SurveyRulesData = clone.SurveyRulesData;

                    IsDirty = true;
                }


                /// <summary>
                /// Get the hash code for the SurveyRulesClass instance. (ignores IsDirty and SurveyRulesInherited)
                /// </summary>
                /// <returns></returns>
                public override int GetHashCode()
                {
                    return HashCode.Combine(Version, SurveyRulesActive, SurveyRulesData);
                }


                /// 
                /// EVENTS
                /// 
                private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }

            }

            public SurveyRulesClass SurveyRules { get; } = new SurveyRulesClass();
        }
        public DataClass Data { get; set; } = new DataClass();      // Data instance is allowed to change


        /// <summary>
        /// Returns the survey title for the main window title bar
        /// </summary>
        /// <returns></returns>
        public string GetSurveyTitle()
        {
            string title = "Untitled Survey";

            if (this.Data.Info.SurveyFileName != null)
            {
                title = Path.GetFileNameWithoutExtension(this.Data.Info.SurveyFileName);

                if (this.IsDirty)
                    title += " *";
            }                                

            return title;
        }


        public bool IsDirty 
        {   get
            {
                if (Data.Info.IsDirty || Data.Media.IsDirty || Data.Sync.IsDirty || Data.Events.IsDirty || Data.Calibration.IsDirty || Data.SurveyRules.IsDirty)
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
                Data.SurveyRules.IsDirty = value;
                OnPropertyChanged();
            }
        }
        public bool IsLoaded { get; private set; } = false;


        public Survey(Reporter _report)
        {
            Report = _report;                                            
        }


        /// <summary>
        /// Clear down the survey class
        /// </summary>
        public void Clear()
        {
            Data.Clear();
        }


        /// <summary>
        /// Load a survey from a json file
        /// </summary>
        /// <param name="SurveyFileSpec"></param>
        /// <returns></returns>
        public async Task<int> SurveyLoad(string SurveyFileSpec, bool autoSave = true)
        {
            int ret = -1;
            string? json = null;
            var stopwatch = Stopwatch.StartNew();


            if (Path.GetExtension(SurveyFileSpec).Equals(".Survey", StringComparison.OrdinalIgnoreCase))
            {
                try
                {                   
                    json = File.ReadAllText(SurveyFileSpec);
                }
                catch (FileNotFoundException e)
                {
                    ret = -2;
                    Report?.Warning("", $"Load survey failed because the survey file couldn't be found, file:{SurveyFileSpec}. {e.Message}");
                }
                catch (UnauthorizedAccessException e)
                {
                    ret = -3;
                    Report?.Warning("", $"Load survey failed because you do not have permission to read this file, file:{SurveyFileSpec}. {e.Message}");
                }
                catch (DirectoryNotFoundException e)
                {
                    ret = -4;
                    Report?.Warning("", $"Load survey failed because the specified directory could not be found, file:{SurveyFileSpec}. {e.Message}");
                }
                catch (PathTooLongException e)
                {
                    ret = -5;
                    Report?.Warning("", $"Load survey failed because the file name is too long, file:{SurveyFileSpec}. The specified path, file name, or both exceed the system-defined maximum length. {e.Message}");
                }
                catch (IOException e)
                {
                    ret = -6;
                    Report?.Warning("", $"Load survey failed because an I/O error occurred, file:{SurveyFileSpec}. {e.Message}");
                }
                catch (Exception e)
                {
                    ret = -7;
                    Report?.Warning("", $"Load survey failed because an unexpected error occurred, file:{SurveyFileSpec}. {e.Message}");
                }

                if (json != null)
                {
                    var settings = new JsonSerializerSettings
                    {
                        Converters =
                        [
                            new EventJsonConverter(),
                            new CalibrationDataListJsonConverter() // Explicitly add the converter  
                        ]
                    };

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

                        ret = SetSurveyNameAndPath(SurveyFileSpec);                     

                        IsDirty = false;
                        IsLoaded = true;

                        // Start the autosave task in background                        
                        if (autoSave)
                        {
                            await StartAutoSave();
                        }
                    }
                }
            }
            else if (Path.GetExtension(SurveyFileSpec).Equals(".EMObs", StringComparison.OrdinalIgnoreCase))
            {

                var (result, errorMessage) = await ProjectLoadEMObs(SurveyFileSpec);

                if (result != 0)
                {
                    ret = result;
                    Report?.Warning("", $"Load survey failed, file:{SurveyFileSpec}. {errorMessage}");
                }
                else
                    ret = 0;
            }
            else
            {
                ret = -8;
                Report?.Warning("", $"Load survey failed because the survey has an unsupported extension type, file:{SurveyFileSpec}.");
            }

            // Data adjustments
            if (Data.Info.SurveyType == SurveyType.Unknown && Data.Info.Version < 2.0)
            {
                // Default survey type to Stereo Fish (SVS) for Survey.Data.Info.Version < 2.0
                Data.Info.SurveyType = SurveyType.StereoFish;
            }

            stopwatch.Stop();
            Debug.WriteLine($"Survey.SurveyLoad {SurveyFileSpec} Return code:{ret}, Elapsed time: {stopwatch.ElapsedMilliseconds} ms");


            return ret;
        }


        /// <summary>
        /// Save a survey to a json file using the survey current name and path
        /// </summary>
        /// <returns>0 if OK</returns>
        public int SurveySave()
        {
            int ret = -1;

            // Stop any reentry
            lock (_lockObject)
            {
                // Adjust MediaPath if possible
                if (!string.IsNullOrEmpty(Data.Info.SurveyPath) && !string.IsNullOrEmpty(Data.Media.MediaPath))
                {
                    var surveyPathFull = Path.GetFullPath(Data.Info.SurveyPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var mediaPathFull = Path.GetFullPath(Data.Media.MediaPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    if (mediaPathFull.StartsWith(surveyPathFull, StringComparison.OrdinalIgnoreCase))
                    {
                        // Make relative path
                        var relativePath = Path.GetRelativePath(surveyPathFull, mediaPathFull);
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
                if (Data.Info.SurveyPath != null && Data.Info.SurveyFileName != null)
                {
                    string surveyPath = Data.Info.SurveyPath;
                    string filePath = Path.Combine(surveyPath, Data.Info.SurveyFileName);

                    var settings = new JsonSerializerSettings
                    {
                        Formatting = Formatting.Indented,  // For pretty-printing the JSON
                        Converters = [new EventJsonConverter()]
                    };

                    // Remove the Survey Path so the survey can be safely moved to a different folder
                    Data.Info.SurveyPath = string.Empty;

                    string json = JsonConvert.SerializeObject(Data, settings);

                    // Restore survey path
                    Data.Info.SurveyPath = surveyPath;

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
                        Report?.Warning("", $"Save survey failed due to an unauthorized access request, file:{filePath}. You do not have permission to write to this file. {e.Message}");
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        ret = -2;
                        Report?.Warning("", $"Save survey failed due to a bad directory, file:{filePath}. The specified directory could not be found. {e.Message}");
                    }
                    catch (PathTooLongException e)
                    {
                        ret = -3;
                        Report?.Warning("", $"Save survey failed due to the file name too long, file:{filePath}. The specified path, file name, or both exceed the system-defined maximum length. {e.Message}");
                    }
                    catch (IOException e)
                    {
                        ret = -4;
                        Report?.Warning("", $"Save survey failed due to an I/O error, file:{filePath}. {e.Message}");
                    }
                    catch (Exception e)
                    {
                        ret = -5;
                        Report?.Warning("", $"Save survey failed due to an unexpected error, file:{filePath}. {e.Message}");
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Save a survey to a json file using passed file spec
        /// Start an autosave task
        /// </summary>
        /// <param name="surveyFileSpec"></param>
        /// <returns></returns>
        public async Task<int> SurveySaveAs(string surveyFileSpec)
        {

            // Set the survey name using the stem of the file name and extract the survey path
            int ret = SetSurveyNameAndPath(surveyFileSpec);

            if (ret == 0)
            {
                // Save the survey to a json file
                ret = SurveySave();

                if (ret == 0)
                {
                    // Reset the dirty flag
                    IsDirty = false;

                    // A Save As could be the first save of a new survey to set IsLoaded to true
                    IsLoaded = true;
                    await StartAutoSave();
                }
            }

            return ret;
        }


        /// <summary>
        /// Close a survey 
        /// </summary>
        /// <returns></returns>
        public async Task<int> SurveyClose()
        {
            await StopAutoSave();

            Clear();

            IsLoaded = false;

            return 0;
        }


        /// <summary>
        /// Extract the survey name and path from the passed file spec
        /// </summary>
        private int SetSurveyNameAndPath(string surveyFileSpec)
        {
            int ret = 0;
            string? directoryPath = null;
            string? fileName = null;

            // Extract the path
            try
            {
                directoryPath = Path.GetDirectoryName(surveyFileSpec);
            }
            catch (ArgumentNullException e)
            {
                ret = -1;
                Report?.Warning("", $"SetSurveyNameAndPath() trying to set survey path base on:{surveyFileSpec}, however were a null argument. {e.Message}");
            }
            catch (ArgumentException e)
            {
                ret = -2;
                Report?.Warning("", $"SetSurveyNameAndPath() trying to set survey path base on:{surveyFileSpec}, however were was an error. {e.Message}");
            }

            // Extract the file name
            try
            {
                fileName = Path.GetFileName(surveyFileSpec);
            }
            catch (ArgumentException e)
            {
                ret = -3;
                Report?.Warning("", $"SetSurveyNameAndPath() trying to set survey name base on:{surveyFileSpec}, however were was an error. {e.Message}");
            }

            Data.Info.SurveyFileName = fileName;
            Data.Info.SurveyPath = directoryPath;

            return ret;
        }


        /// <summary>
        /// Add the media file to the list of either left or right media files
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


        /// <summary>
        /// Return a full media file spec
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public string GetLeftMediaFileSpec(int index)
        {
            return GetMediaFileSpec(true/*trueLeftFaleRight*/, index);
        }
        public string GetRightMediaFileSpec(int index)
        {
            return GetMediaFileSpec(false/*trueLeftFaleRight*/, index);
        }
        private string GetMediaFileSpec(bool trueLeftFaleRight, int index)
        {
            ObservableCollection<string> mediaFileNames;
            if (trueLeftFaleRight)
            {
                mediaFileNames = Data.Media.LeftMediaFileNames;
            }
            else
            {
                mediaFileNames = Data.Media.RightMediaFileNames;
            }

            string mediaFile = string.Empty;
            if (mediaFileNames.Count > 0 &&
                index >= 0 && index < mediaFileNames.Count)
            {
                if (Data.Media.MediaPath is not null)
                {
                    mediaFile = Path.Combine(Data.Media.MediaPath, mediaFileNames[index]);
                }
                else
                {
                    mediaFile = mediaFileNames[index];
                }
            }

            // If fileSpec a relative path then use the path from the survey file spec
            if (!Path.IsPathRooted(mediaFile) && Data.Info.SurveyPath is not null)
            {
                // Combine the base directory with the relative fileSpec
                mediaFile = Path.GetFullPath(Path.Combine(Data.Info.SurveyPath, mediaFile));
            }

            return mediaFile;
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
                    else if (calibrationData.LeftCameraCalibration == calibrationDataItem.LeftCameraCalibration &&
                             calibrationData.RightCameraCalibration == calibrationDataItem.RightCameraCalibration &&
                             calibrationData.StereoCameraCalibration == calibrationDataItem.StereoCameraCalibration)
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
        /// <summary>
        private async Task StartAutoSave()
        {
            await StopAutoSave(); // Ensure any previous autosave task is stopped

            _autosaveCts = new CancellationTokenSource();
            _autosaveTask = Task.Run(async () =>
            {
                // Task naming for debugging
                if (Thread.CurrentThread.Name == null)
                {
                        Thread.CurrentThread.Name = "Survey.AutosaveWork()";
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
                                SurveySave();
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
        public async Task StopAutoSave()
        {
            if (_autosaveCts != null)
            {
                _autosaveCts.Cancel();

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
        public CalibrationDataListJsonConverter() { } // Ensure parameterless constructor exists

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ObservableCollection<CalibrationData>);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            ObservableCollection<CalibrationData>? calibrationDataList = [];

            if (reader.TokenType != JsonToken.Null)
            {
                calibrationDataList = [];

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
