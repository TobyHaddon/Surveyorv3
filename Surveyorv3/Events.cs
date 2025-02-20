// Version: 1.2
// 22 Mar 2024 Copied from SurveyorV1 and changed
// 22 Mar 2024 namespace change to Surveyor.Events from SurveyorV1.Events
// 26 Mar 2024 Added StereoMeasurementPoints class
// 23 Apr 2024 Added GUID the Event class
// 29 Apr 2024 Moved the species info out of StereoMeasurementPoints into SpeciesInfo
// 29 Apr 2024 Rename StereoMeasurementPoints to SurveyMeasurement and create SurveyStereoPoint and SurveyPoint
// 01 Oct 2024 Added left/right indicator to SinglePoint class
// 13 Feb 2025 Added SurveyStart and SurveyEnd SurveyDataType



using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;


namespace Surveyor.Events
{
    // https://chat.openai.com/c/e631ccac-52f1-4906-b9fe-0579dd59bdb4
    public enum SurveyDataType
    {
        SurveyPoint,
        SurveyStereoPoint,
        SurveyMeasurementPoints,
        StereoCalibrationPoints,
        StereoSyncPoint,
        SurveyStart,
        SurveyEnd
    }

    public interface IPointData
    {
        // You can define common methods or properties that all point data classes must implement.
    }

    public class SinglePoint : IPointData
    {
        public bool TrueLeftfalseRight { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class StereoPoint : IPointData
    {
        public double LeftX { get; set; }
        public double LeftY { get; set; }

        public double RightX { get; set; }
        public double RightY { get; set; }
    }

    public class StereoPairPoints : IPointData
    {
        // Left Side Point A (X,Y)
        public double LeftXA { get; set; }
        public double LeftYA { get; set; }

        // Left Side Point B (X,Y)
        public double LeftXB { get; set; }
        public double LeftYB { get; set; }

        // Right Side Point A (X,Y)
        public double RightXA { get; set; }
        public double RightYA { get; set; }

        // Right Side Point B (X,Y)
        public double RightXB { get; set; }
        public double RightYB { get; set; }
    }


    /// <summary>
    /// SurveyMarker class used on a SurveyStart and SurveyEnd event
    /// </summary>
    public class SurveyMarker : IPointData
    {
        public string MarkerName { get; set; } = "";
    }


    // Used if the fish can only be seen in one image
    public class SurveyPoint : SinglePoint
    {
        public SpeciesInfo SpeciesInfo { get; set; } = new();
    }

    // Used if the fish can be seen in both images but a measurement can't be
    // calculated
    public class SurveyStereoPoint : StereoPoint
    {
        public SpeciesInfo SpeciesInfo { get; set; } = new();

        // Survey rules results
        public SurveyRulesCalc SurveyRulesCalc { get; set; } = new();

        // Calibration ID used to calculate the measurement
        public Guid? CalibrationID { get; set; } = null;
    }

    // Used if pairs of points in both the left and right image are set and a
    // measurement is sucessfully calulated
    public class SurveyMeasurement : StereoPairPoints
    {
        // Fish species info
        public SpeciesInfo SpeciesInfo { get; set; } = new();

        // Fish measurement
        public double? Measurment { get; set; } = -1; 

        // Survey rules result used to calculate the rules
        public SurveyRulesCalc SurveyRulesCalc { get; set; } = new();

        // Calibration ID used to calculate the measurement
        public Guid? CalibrationID { get; set; } = null;
    }

    public class SpeciesInfo
    {
        public string? Family { get; set; }
        public string? Genus { get; set; }
        public string? Species { get; set; }
        public string? Code { get; set; }
        public string? Number { get; set; }
        public string? Stage { get; set; }
        public string? Activity { get; set; }
        public string? Comment { get; set; }

        public void Clear()
        {
            Family = null;
            Genus = null;
            Species = null;
            Code = null;

            Number = null;
            Stage = null;
            Activity = null;
            Comment = null;
        }
    }


    public class StereoCalibrationPoints : IPointData
    {
        public List<(int X, int Y)>? LeftPoints { get; set; }
        public List<(int X, int Y)>? RightPoints { get; set; }
    }


    [JsonConverter(typeof(EventJsonConverter))]
    public class Event : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Guid { get; set; }
        public DateTime DateTimeCreate { get; set; }

        public TimeSpan TimeSpanTimelineController { get; set; }
        public TimeSpan TimeSpanLeftFrame { get; set; }
        public TimeSpan TimeSpanRightFrame { get; set; }
        public SurveyDataType EventDataType { get; set; }
        public IPointData? EventData { get; set; }

        // Constructor
        public Event(SurveyDataType dataType) : this()  // Chaining to the base constructor
        {
            EventDataType = dataType;
        }
        public Event()
        {
            // Generate a new GUID for the event
            Guid = Guid.NewGuid();

            // Set the creation date and time
            DateTimeCreate = DateTime.Now;
        }


        // Depending on your logic, you can have a method to initialize the Data property with the appropriate class instance.
        public void SetData(SurveyDataType dataType)
        {
            EventData = CreateDataType(dataType);
            EventDataType = dataType;
        }

        public static IPointData? CreateDataType(SurveyDataType dataType)
        {
            return dataType switch
            {                
                SurveyDataType.SurveyPoint => new SurveyPoint(),
                SurveyDataType.SurveyStereoPoint => new SurveyStereoPoint(),
                SurveyDataType.SurveyMeasurementPoints => new SurveyMeasurement(),
                SurveyDataType.StereoCalibrationPoints => new StereoCalibrationPoints(),
                SurveyDataType.StereoSyncPoint => new StereoCalibrationPoints(),
                SurveyDataType.SurveyStart => new SurveyMarker(),
                SurveyDataType.SurveyEnd => new SurveyMarker(),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        /// 
        /// EVENTS
        /// 
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    public class EventJsonConverter : JsonConverter<Event>
    {
        public override Event ReadJson(JsonReader reader, Type objectType, Event? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var jsonObject = JObject.Load(reader);
            var eventInstance = new Event();

            // Deserialize common properties
            try
            {
                eventInstance.Guid = jsonObject["Guid"]?.ToObject<Guid>() ?? Guid.Empty;
            }
            catch (Exception)
            {
                eventInstance.Guid = Guid.Empty;
            }

            eventInstance.DateTimeCreate = jsonObject["DateTimeCreate"]?.ToObject<DateTime>() ?? DateTime.MinValue;
            eventInstance.TimeSpanTimelineController = jsonObject["TimeSpanTimelineController"]?.ToObject<TimeSpan>() ?? TimeSpan.Zero;
            eventInstance.TimeSpanLeftFrame = jsonObject["TimeSpanLeftFrame"]?.ToObject<TimeSpan>() ?? TimeSpan.Zero;
            eventInstance.TimeSpanRightFrame = jsonObject["TimeSpanRightFrame"]?.ToObject<TimeSpan>() ?? TimeSpan.Zero;

            // Deserialize the EventDataType from string
            var eventDataTypeString = jsonObject["EventDataType"]?.ToString();
            if (eventDataTypeString is not null && Enum.TryParse(typeof(SurveyDataType), eventDataTypeString, out var eventDataType))
            {
                if (eventDataType is not null)
                    eventInstance.EventDataType = (SurveyDataType)eventDataType;
            }

            // Deserialize the EventData based on EventDataType
            eventInstance.EventData = Event.CreateDataType(eventInstance.EventDataType);

            if (eventInstance.EventData != null && jsonObject["EventData"] != null)
            {
                serializer.Populate(jsonObject["EventData"]!.CreateReader(), eventInstance.EventData);
            }

            return eventInstance;
        }

        public override void WriteJson(JsonWriter writer, Event? value, JsonSerializer serializer)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            var jsonObject = new JObject
            {
                { "Guid", JToken.FromObject(value.Guid) },
                { "DateTimeCreate", JToken.FromObject(value.DateTimeCreate) },
                { "TimeSpanLeftFrame", JToken.FromObject(value.TimeSpanLeftFrame) },
                { "TimeSpanTimelineController", JToken.FromObject(value.TimeSpanTimelineController) },
                { "TimeSpanRightFrame", JToken.FromObject(value.TimeSpanRightFrame) },
                { "EventDataType", JToken.FromObject(value.EventDataType.ToString()) }
            };

            if (value.EventData != null)
            {
                jsonObject["EventData"] = JToken.FromObject(value.EventData, serializer);
            }

            jsonObject.WriteTo(writer);
        }
    }

    /// <summary>
    /// Sorter class for the Event collection
    /// Ensure that event stay in TimeSpanTimelineController order
    /// </summary>
    public class SortedEventCollection : ObservableCollection<Event>
    {
        protected override void InsertItem(int index, Event item)
        {
            // Find the correct index to maintain TimeSpanTimelineController order
            //???index = Items.Take(index).TakeWhile(e => e.TimeSpanTimelineController < item.TimeSpanTimelineController).Count();
            // Find the correct index to maintain TimeSpanTimelineController and DateTimeCreate order
            index = Items.Take(index).TakeWhile(e =>
                e.TimeSpanTimelineController < item.TimeSpanTimelineController ||
                (e.TimeSpanTimelineController == item.TimeSpanTimelineController && e.DateTimeCreate < item.DateTimeCreate)).Count();

            base.InsertItem(index, item);
        }
    }
}