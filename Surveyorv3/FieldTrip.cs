// Hold the survey control information for a particular research base
// - Research Base or Area information: Country, Area, Notes, Fish species list name
// - Survey: ReefCode, Reef Name, Coordinates, Depths, Replicate count, Required surveys (SVS,3D,Benthic)
// - Survey Rules: SVS rules, transect length
// - Replicate Layout: 1,2,3,BuoyLine,4,5,6
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Surveyor.Events;
using Surveyor.Helper;
using Surveyor.User_Controls;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Surveyor
{
    public partial class FieldTrip : INotifyPropertyChanged
    {

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
                ReplicateLayout.Clear();
                Surveys.Clear();
                SurveyRules.Clear();
            }


            /// <summary>
            /// Diagnostics dump of class information
            /// </summary>
            public void DumpAllProperties(Reporter? report)
            {
                DumpClassPropertiesHelper.DumpAllProperties(Info, report, /*ignore*/"<Version>k__BackingField,_fieldTripFileName,_fieldTripPath,_countryName,_countryCode,_researchBaseName,_notes,PropertyChanged");
                DumpClassPropertiesHelper.DumpAllProperties(ReplicateLayout, report, /*ignore*/"PropertyChanged,<Version>k__BackingField,_isSynchronized,_timeSpanOffset,_actualTimeSpanOffsetLeft,_actualTimeSpanOffsetRight,_isDirty");
                DumpClassPropertiesHelper.DumpAllProperties(Surveys, report, /*ignore*/"PropertyChanged,<Version>k__BackingField,_eventList,_isDirty,EventList");
                DumpClassPropertiesHelper.DumpAllProperties(SurveyRules, report, /*ignore*/"PropertyChanged,<Version>k__BackingField,_surveyRulesActive,_surveyRulesInherited,_surveyRulesData,_isDirty,SurveyRulesData");
                DumpClassPropertiesHelper.DumpAllProperties(SurveyRules.SurveyRulesData, report, /*ignore*/"PropertyChanged,<Version>k__BackingField,_rangeRuleActive,_rangeMin,_rangeMax,_rmsRuleActive,_rmsMax,_horizontalRangeRuleActive,_horizontalRangeLeft,\r\n_horizontalRangeRight,_verticalRangeRuleActive,_verticalRangeTop,_verticalRangeBottom,_isDirty");
            }

            // InfoClass - DataClass Child
            public partial class InfoClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                /// <summary>
                /// Clear down the InfoClass
                /// </summary>
                public void Clear()
                {
                    _fieldTripFileName = null;
                    _fieldTripPath = null;
                    _countryName = null;
                    _countryCode = null;  // ISO2
                    _areaName = null;
                    _areaCode = null;
                    _notes = null;
                }



                // Info class version
                public float Version { get; set; } = 1.0f;

                // Values
                private string? _fieldTripFileName = null;
                private string? _fieldTripPath = null;
                private string? _countryName = null;
                private string? _countryCode = null;  // ISO2
                private string? _areaName = null;
                private string? _areaCode = null; // can be same as research base name is that is only one word like 'Hoga' or 'Uitla'
                private string? _notes = null;

                // Setters and getters

                public string? FieldTripFileName
                {
                    get => _fieldTripFileName;
                    set
                    {
                        if (_fieldTripFileName != value)
                        {
                            _fieldTripFileName = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                public string? FieldTripPath
                {
                    get => _fieldTripPath;
                    set
                    {
                        if (_fieldTripPath != value)
                        {
                            _fieldTripPath = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                /// <summary>
                /// Country Name e.g. Honduras, Indonesia, Madagascar or Mexico
                /// </summary>
                public string? CountryName
                {
                    get => _countryName;
                    set
                    {
                        if (_countryName != value)
                        {
                            _countryName = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                /// <summary>
                /// ISO2 Country Code e.g. HN, ID, MG, MX for Honduras, Indonesia, Madagascar and Mexico
                /// </summary>
                public string? CountryCode
                {
                    get => _countryCode;
                    set
                    {
                        if (_countryCode != value)
                        {
                            _countryCode = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                /// <summary>
                /// Area name or research base name e.g. Utila, Hoga, Nosy Be
                /// </summary>
                public string? AreaName
                {
                    get => _areaName;
                    set
                    {
                        if (_areaName != value)
                        {
                            _areaName = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                /// <summary>
                /// Area code e.g. Utila, Hoga, Nosy
                /// </summary>
                public string? AreaCode
                {
                    get => _areaCode;
                    set
                    {
                        if (_areaCode != value)
                        {
                            _areaCode = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                /// <summary>
                /// Any notes or comments
                /// </summary>
                public string? Notes
                {
                    get => _notes;
                    set
                    {
                        if (_notes != value)
                        {
                            _notes = value;
                            IsDirty = true;
                            OnPropertyChanged();
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


            public enum ReplicateItemType
            {
                Replicate,
                BuoyLine,
                Other
            }

            // Used by ReplicateLayoutClass
            public partial class ReplicateItem : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                /// <summary>
                /// Clear down the SyncClass
                /// </summary>
                public void Clear()
                {
                    _replicateItemType = null;
                    _replicateName = null;
                    _isDirty = false;
                }


                // ReplicateItem class version
                public float Version { get; set; } = 1.0f;

                
                private ReplicateItemType? _replicateItemType = null;
                private string? _replicateName = null;

                public ReplicateItemType? ReplicateItemType
                {
                    get => _replicateItemType;
                    set
                    {
                        if (_replicateItemType != value)
                        {
                            _replicateItemType = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                public string? ReplicateName
                {
                    get => _replicateName;
                    set
                    {
                        if (_replicateName != value)
                        {
                            _replicateName = value;
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

            // ReplicateLayoutClass - DataClass Child
            public partial class ReplicateLayoutClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public ReplicateLayoutClass()
                {
                    // Subscribe to the collection's CollectionChanged event
                    _layout.CollectionChanged += CollectionChangedHandler;
                }

                /// <summary>
                /// Clear down the SyncClass
                /// </summary>
                public void Clear()
                {
                    _layout.Clear();
                    _isDirty = false;
                }


                // ReplicateLayoutClass class version
                public float Version { get; set; } = 1.0f;


                private ObservableCollection<ReplicateItem> _layout = [];       // If synchronized is switched to false the frame offset isn't removed in case it needs to be recovered 

                public ObservableCollection<ReplicateItem> Layout
                {
                    get => _layout;
                    set
                    {
                        if (_layout != value)
                        {
                            _layout = value;

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

                /// <summary>
                /// This method will be called whenever the collection changes
                /// </summary>
                /// <param name="sender"></param>
                /// <param name="e"></param>
                private void CollectionChangedHandler(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    // Any change to the list means data changed
                    IsDirty = true;
                }

                /// 
                /// EVENTS
                /// 
                private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
            }
            public ReplicateLayoutClass ReplicateLayout { get; } = new ReplicateLayoutClass();


            // Used by SurveysClass
            public partial class SurveyItem : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public SurveyItem()
                {
                    // Subscribe to the collection's CollectionChanged event
                    _depths.CollectionChanged += CollectionChangedHandler;
                }

                /// <summary>
                /// Clear down the SurveyItem
                /// </summary>
                public void Clear()
                {
                    _active = false;
                    _areaCode = null;
                    _siteCode = null;
                    _siteName = null;
                    _coordinatesLatitude = null;
                    _coordinatesLongitude = null;
                    _depths.Clear();
                    _isDirty = false;
                }


                // SurveyItem class version
                public float Version { get; set; } = 1.0f;

                private bool _active = false;   
                private string? _areaCode = null;
                private string? _siteCode = null;
                private string? _siteName = null;
                private double? _coordinatesLatitude = null;
                private double? _coordinatesLongitude = null;
                private ObservableCollection<string> _depths = [];  // Depth in m or Flat, Crest and Slope


                /// <summary>
                /// Is this survey active and should be surveyed as part of the field trip
                /// </summary>
                public bool Active
                {
                    get => _active;
                    set
                    {
                        if (_active != value)
                        {
                            _active = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                /// <summary>
                /// As set-up in the InfoClass 
                /// </summary>
                public string? AreaCode
                {
                    get => _areaCode;
                    set
                    {
                        if (_areaCode != value)
                        {
                            _areaCode = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }


                /// <summary>
                /// Site code or reef code
                /// </summary>
                public string? SiteCode
                {
                    get => _siteCode;
                    set
                    {
                        if (_siteCode != value)
                        {
                            _siteCode = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                /// <summary>
                /// Site name or reef name 
                /// </summary>
                public string? SiteName
                {
                    get => _siteName;
                    set
                    {
                        if (_siteName != value)
                        {
                            _siteName = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                /// <summary>
                /// East positive, West negative
                /// </summary>
                public double? CoordinatesLatitude
                {
                    get => _coordinatesLatitude;
                    set
                    {
                        if (_coordinatesLatitude != value)
                        {
                            _coordinatesLatitude = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                /// <summary>
                /// North positive, South negative
                /// </summary>
                public double? CoordinatesLongitude
                {
                    get => _coordinatesLongitude;
                    set
                    {
                        if (_coordinatesLongitude != value)
                        {
                            _coordinatesLongitude = value;
                            IsDirty = true;
                            OnPropertyChanged();
                        }
                    }
                }

                public ObservableCollection<string> Depths
                {
                    get => _depths;
                    set
                    {
                        if (_depths != value)
                        {
                            _depths = value;

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

                /// <summary>
                /// This method will be called whenever the collection changes
                /// </summary>
                /// <param name="sender"></param>
                /// <param name="e"></param>
                private void CollectionChangedHandler(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    // Any change to the list means data changed
                    IsDirty = true;
                }

                /// 
                /// EVENTS
                /// 
                private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
            }


            // SurveysClass - DataClass Child
            public partial class SurveysClass : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                public SurveysClass()
                {
                    // Subscribe to the collection's CollectionChanged event
                    _surveyItemList.CollectionChanged += CollectionChangedHandler;

                }


                /// <summary>
                /// Clear down the SurveysClass
                /// </summary>
                public void Clear()
                {
                    _surveyItemList.Clear();
                    _isDirty = false;
                }

                // SurveysClass class version
                public float Version { get; set; } = 2.0f;

                [JsonIgnore]
                private ObservableCollection<SurveyItem> _surveyItemList = [];

                public ObservableCollection<SurveyItem> SurveyItemList
                {
                    get => _surveyItemList;
                    set
                    {
                        if (_surveyItemList != value)
                        {
                            _surveyItemList = value;
                            IsDirty = true;
                            OnPropertyChanged();
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
            public SurveysClass Surveys { get; } = new SurveysClass();



            // SurveyRulesClass - DataClass Child
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
                    _transectLength = 0;     // units m
                    _isDirty = false;
                }

                // SurveyRulesClass version
                public float Version { get; set; } = 2.0f;

                // Values
                private bool _surveyRulesActive = false;
                private SurveyRulesData _surveyRulesData = new();
                private double _transectLength = 0;


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
                            OnPropertyChanged();
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
                            OnPropertyChanged();
                        }
                    }
                }
                
                public double TransectLength
                {
                    get => _transectLength;
                    set
                    {
                        if (_transectLength != value)
                        {
                            _transectLength = value;
                            IsDirty = true;
                            OnPropertyChanged();
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
                    SurveyRulesData = clone.SurveyRulesData;
                    TransectLength = clone.TransectLength;

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


        public bool IsDirty
        {
            get
            {
                if (Data.Info.IsDirty || Data.ReplicateLayout.IsDirty || Data.Surveys.IsDirty || Data.SurveyRules.IsDirty)
                {
                    return true;
                }
                return false;
            }
            private set
            {
                Data.Info.IsDirty = value;
                Data.ReplicateLayout.IsDirty = value;
                Data.Surveys.IsDirty = value;
                Data.SurveyRules.IsDirty = value;
                OnPropertyChanged();
            }
        }
        public bool IsLoaded { get; private set; } = false;


        public FieldTrip(Reporter _report)
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
        /// Returns the survey title for the main window title bar
        /// </summary>
        /// <returns></returns>
        public string GetFieldTripTitle()
        {
            string title = "Untitled Field Trip";

            if (this.Data.Info.FieldTripFileName != null)
            {
                title = Path.GetFileNameWithoutExtension(this.Data.Info.FieldTripFileName);

                if (this.IsDirty)
                    title += " *";
            }

            return title;
        }


        /// <summary>
        /// Load a Field Trip from a json file
        /// </summary>
        /// <param name="fieldTripFileSpec"></param>
        /// <param name="autoSave"></param>
        /// <returns></returns>
        public async Task<int> FieldTripLoadAsync(string fieldTripFileSpec, bool autoSave = true)
        {
            int ret = -1;
            string? json = null;
            var stopwatch = Stopwatch.StartNew();


            if (Path.GetExtension(fieldTripFileSpec).Equals(".Trip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    json = File.ReadAllText(fieldTripFileSpec);
                }
                catch (FileNotFoundException e)
                {
                    ret = -2;
                    Report?.Warning("", $"Load field trip failed because the survey file couldn't be found, file:{fieldTripFileSpec}. {e.Message}");
                }
                catch (UnauthorizedAccessException e)
                {
                    ret = -3;
                    Report?.Warning("", $"Load field trip failed because you do not have permission to read this file, file:{fieldTripFileSpec}. {e.Message}");
                }
                catch (DirectoryNotFoundException e)
                {
                    ret = -4;
                    Report?.Warning("", $"Load field trip failed because the specified directory could not be found, file:{fieldTripFileSpec}. {e.Message}");
                }
                catch (PathTooLongException e)
                {
                    ret = -5;
                    Report?.Warning("", $"Load field trip failed because the file name is too long, file:{fieldTripFileSpec}. The specified path, file name, or both exceed the system-defined maximum length. {e.Message}");
                }
                catch (IOException e)
                {
                    ret = -6;
                    Report?.Warning("", $"Load field trip failed because an I/O error occurred, file:{fieldTripFileSpec}. {e.Message}");
                }
                catch (Exception e)
                {
                    ret = -7;
                    Report?.Warning("", $"Load field trip failed because an unexpected error occurred, file:{fieldTripFileSpec}. {e.Message}");
                }

                if (json != null)
                {
                    var settings = new JsonSerializerSettings
                    {
                        Converters =
                        [
                            new EventJsonConverter(),
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

                        ret = SetFieldTripNameAndPath(fieldTripFileSpec);

                        IsDirty = false;
                        IsLoaded = true;

                        // Start the auto save task in background
                        // The AutoSaveEnable flag is checked at the point the save is about to be made
                        // The advantage with always having the timer running an checking if auto save is
                        // enabled last is that the Auto Save settings can be changed and the application
                        //doesn't need to be restarted.

                        if (autoSave)
                        {
                            await StartAutoSaveAsync();
                        }
                    }
                }
            }
            else
            {
                ret = -8;
                Report?.Warning("", $"Load field trip failed because the survey has an unsupported extension type, file:{fieldTripFileSpec}.");
            }


            stopwatch.Stop();
            Debug.WriteLine($"FieldTrip.FieldTripLoadAsync {fieldTripFileSpec} Return code:{ret}, Elapsed time: {stopwatch.ElapsedMilliseconds} ms");


            return ret;
        }


        /// <summary>
        /// Save a Field Trip to a json file using the survey current name and path
        /// </summary>
        /// <returns>0 if OK</returns>
        public int FieldTripSave()
        {
            int ret = -1;

            // Stop any reentry
            lock (_lockObject)
            {
                // Save to .json
                if (Data.Info.FieldTripPath != null && Data.Info.FieldTripFileName != null)
                {
                    string surveyPath = Data.Info.FieldTripPath;
                    string filePath = Path.Combine(surveyPath, Data.Info.FieldTripFileName);

                    var settings = new JsonSerializerSettings
                    {
                        Formatting = Formatting.Indented,  // For pretty-printing the JSON
                        Converters = [new EventJsonConverter()]
                    };

                    // Remove the Survey Path so the survey can be safely moved to a different folder
                    Data.Info.FieldTripPath = string.Empty;

                    string json = JsonConvert.SerializeObject(Data, settings);

                    // Restore survey path
                    Data.Info.FieldTripPath = surveyPath;

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
                        Report?.Warning("", $"Save Field Trip failed due to an unauthorized access request, file:{filePath}. You do not have permission to write to this file. {e.Message}");
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        ret = -2;
                        Report?.Warning("", $"Save Field Trip failed due to a bad directory, file:{filePath}. The specified directory could not be found. {e.Message}");
                    }
                    catch (PathTooLongException e)
                    {
                        ret = -3;
                        Report?.Warning("", $"Save Field Trip failed due to the file name too long, file:{filePath}. The specified path, file name, or both exceed the system-defined maximum length. {e.Message}");
                    }
                    catch (IOException e)
                    {
                        ret = -4;
                        Report?.Warning("", $"Save Field Trip failed due to an I/O error, file:{filePath}. {e.Message}");
                    }
                    catch (Exception e)
                    {
                        ret = -5;
                        Report?.Warning("", $"Save Field Trip failed due to an unexpected error, file:{filePath}. {e.Message}");
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Save a field trip to a json file using passed file spec
        /// Start an auto save task
        /// </summary>
        /// <param name="fieldTripFileSpec"></param>
        /// <returns></returns>
        public async Task<int> FieldTripSaveAsAsync(string fieldTripFileSpec)
        {

            // Set the survey name using the stem of the file name and extract the survey path
            int ret = SetFieldTripNameAndPath(fieldTripFileSpec);

            if (ret == 0)
            {
                // Save the survey to a json file
                ret = FieldTripSave();

                if (ret == 0)
                {
                    // Reset the dirty flag
                    IsDirty = false;

                    // A Save As could be the first save of a new survey to set IsLoaded to true
                    IsLoaded = true;
                    await StartAutoSaveAsync();
                }
            }

            return ret;
        }


        /// <summary>
        /// Close a survey 
        /// </summary>
        /// <returns></returns>
        public async Task<int> SurveyCloseAsync()
        {
            await StopAutoSaveAsync();

            Clear();

            IsLoaded = false;

            return 0;
        }


        /// <summary>
        /// Extracts a list of distinct site/reef names from the Field Trip Surveys list
        /// The list appears in the order the sites appear in the survey list, so if there
        /// are multiple surveys for the same site the site name appears in the list in the
        /// order of the first survey for that site. 
        /// </summary>
        /// <returns></returns>
        public List<string> GetSiteNameList()
        {
            return [.. Data.Surveys.SurveyItemList
                            .Select(s => s.SiteName)
                            .OfType<string>()
                            .Distinct()];
        }


        /// <summary>
        /// Extract a list of distinct site/reef codes from the Field Trip Surveys list
        /// The list appears in the order the sites appear in the survey list, so if there
        /// are multiple surveys for the same site the site code appears in the list in the
        /// order of the first survey for that site. 
        /// </summary>
        /// <returns></returns>
        public List<string> GetSiteCodeList()
        {            
            return [.. Data.Surveys.SurveyItemList
                            .Select(s => s.SiteCode)
                            .OfType<string>()
                            .Distinct()];
        }


        /// <summary>
        /// Return the site code for a given site name by searching the Field Trip Surveys list
        /// </summary>
        /// <param name="siteName"></param>
        /// <returns></returns>
        public string GetSiteCodeFromName(string siteName)
        {
            // Build SiteName, SiteCode dictionary
            var siteDictionary = Data.Surveys.SurveyItemList
                .Where(s => s.SiteName != null && s.SiteCode != null)
                .ToDictionary(s => s.SiteName!, s => s.SiteCode!);

            // Search for the site code using the site name
            if (siteDictionary.TryGetValue(siteName, out string siteCode))
            {
                return siteCode;
            }
            else
            {
                return string.Empty; // or throw an exception, or return null, depending on your needs
            }
        }


        /// <summary>
        /// Return the site name for a given site code by searching the Field Trip Surveys list
        /// </summary>
        /// <param name="siteCode"></param>
        /// <returns></returns>
        public string GetSiteNameFromCode(string siteCode)
        {
            // Build SiteCode, SiteName dictionary
            var siteDictionary = Data.Surveys.SurveyItemList
                .Where(s => s.SiteName != null && s.SiteCode != null)
                .ToDictionary(s => s.SiteCode!, s => s.SiteName!);
            // Search for the site name using the site code
            if (siteDictionary.TryGetValue(siteCode, out string siteName))
            {
                return siteName;
            }
            else
            {
                return string.Empty; // or throw an exception, or return null, depending on your needs
            }
        }


        /// <summary>
        /// Return of list of all the depths used in the Field Trip surveys list
        /// </summary>
        /// <returns></returns>
        public List<string> GetDepthList()
        {
            // From the Surveys list extract the Depths list, flatten it and return the distinct values
            return Data.Surveys.SurveyItemList
                .SelectMany(s => s.Depths)
                .Where(d => d != null)
                .Distinct()
                .ToList();
        }


        /// <summary>
        /// Extract the field trip name and path from the passed file spec
        /// </summary>
        private int SetFieldTripNameAndPath(string surveyFileSpec)
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
                Report?.Warning("", $"SetFieldTripNameAndPath: trying to set survey path base on:{surveyFileSpec}, however were a null argument. {e.Message}");
            }
            catch (ArgumentException e)
            {
                ret = -2;
                Report?.Warning("", $"SetFieldTripNameAndPath: trying to set survey path base on:{surveyFileSpec}, however were was an error. {e.Message}");
            }

            // Extract the file name
            try
            {
                fileName = Path.GetFileName(surveyFileSpec);
            }
            catch (ArgumentException e)
            {
                ret = -3;
                Report?.Warning("", $"SetFieldTripNameAndPath: trying to set survey name base on:{surveyFileSpec}, however were was an error. {e.Message}");
            }

            Data.Info.FieldTripFileName = fileName;
            Data.Info.FieldTripPath = directoryPath;

            return ret;
        }


        



        /// <summary>
        /// Start the auto save task
        /// <summary>
        private async Task StartAutoSaveAsync()
        {
            await StopAutoSaveAsync(); // Ensure any previous auto save task is stopped

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
                                FieldTripSave();
                                Report?.Debug("", $"Auto save completed");
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
