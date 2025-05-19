// Surveyor SurveyRulesData
// Holds, reads, saves the rules that allow fish to be included in the survey
// This is the size of the survey cross-section etc
// 
// Version 1.0
//


//using Emgu.CV;
//using Emgu.CV.Structure;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;


namespace Surveyor
{

    public partial class SurveyRulesData : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // SurveyRulesData class version
        public float Version { get; set; } = 1.0f;


        // Values
        private bool _rangeRuleActive = false;
        private double _rangeMin = 0.0;
        private double _rangeMax = 10.0;
        private bool _rmsRuleActive = false;
        private double _rmsMax = 0.0;
        private bool _horizontalRangeRuleActive = false;
        private double _horizontalRangeLeft = 0.0;
        private double _horizontalRangeRight = 0.0;
        private bool _verticalRangeRuleActive = false;
        private double _verticalRangeTop = 0.0;
        private double _verticalRangeBottom = 0.0;


        [JsonProperty(nameof(RangeRuleActive))]
        public bool RangeRuleActive // True if the range restriction rule is being used
        {
            get => _rangeRuleActive;
            set
            {
                if (_rangeRuleActive != value)
                {
                    _rangeRuleActive = value;
                    IsDirty = true;
                }
            }
        }

        [JsonProperty(nameof(RangeMin))]
        public double RangeMin // The minimum range a fish can be recorded at in metres e.g. 0.5m
        {
            get => _rangeMin;
            set
            {
                if (_rangeMin != value)
                {
                    _rangeMin = value;
                    IsDirty = true;
                }
            }
        }

        [JsonProperty(nameof(RangeMax))]
        public double RangeMax  // The maximum range a fish can be recorded at in metres e.g. 10.0m
        {
            get => _rangeMax;
            set
            {
                if (_rangeMax != value)
                {
                    _rangeMax = value;
                    IsDirty = true;
                }
}
        }

        [JsonProperty(nameof(RMSRuleActive))]        
        public bool RMSRuleActive  // True if the RMS restriction rule is being used
        {
            get => _rmsRuleActive;
            set
            {
                if (_rmsRuleActive != value)
                {
                    _rmsRuleActive = value;
                    IsDirty = true;
                }
            }
        }

        [JsonProperty(nameof(RMSMax))]
        public double RMSMax  // The maximum RMS error that is allowed in mm e.g. 20mm
        {
            get => _rmsMax;
            set
            {
                if (_rmsMax != value)
                {
                    _rmsMax = value;
                    IsDirty = true;
                }
            }
        }


        [JsonProperty(nameof(HorizontalRangeRuleActive))]
        public bool HorizontalRangeRuleActive  // True if the horizontal range restriction rule is being used
        {
            get => _horizontalRangeRuleActive;
            set
            {
                if (_horizontalRangeRuleActive != value)
                {
                    _horizontalRangeRuleActive = value;
                    IsDirty = true;
                }
            }
        }

        [JsonProperty(nameof(HorizontalRangeLeft))]
        public double HorizontalRangeLeft  // The size of the survey box in metres to the left of the camera centre point e.g. 2.5m
        {
            get => _horizontalRangeLeft;
            set
            {
                if (_horizontalRangeLeft != value)
                {
                    _horizontalRangeLeft = value;
                    IsDirty = true;
                }
            }
        }

        [JsonProperty(nameof(HorizontalRangeRight))]
        public double HorizontalRangeRight  // The size of the survey box in metres to the right of the camera centre point e.g. 2.5m
        {
            get => _horizontalRangeRight;
            set
            {
                if (_horizontalRangeRight != value)
                {
                    _horizontalRangeRight = value;
                    IsDirty = true;
                }
            }
        }

        [JsonProperty(nameof(VerticalRangeRuleActive))]
        public bool VerticalRangeRuleActive  // True if the vertical range restriction rule is being used
        {
            get => _verticalRangeRuleActive;
            set
            {
                if (_verticalRangeRuleActive != value)
                {
                    _verticalRangeRuleActive = value;
                    IsDirty = true;
                }
            }
        }

        [JsonProperty(nameof(VerticalRangeTop))]
        public double VerticalRangeTop  // The size of the survey box in metres above the camera centre point e.g. 2.5m
        {
            get => _verticalRangeTop;
            set
            {
                if (_verticalRangeTop != value)
                {
                    _verticalRangeTop = value;
                    IsDirty = true;
                }
            }
        }

        [JsonProperty(nameof(VerticalRangeBottom))]
        public double VerticalRangeBottom  // The size of the survey box in metres below the camera centre point e.g. 2.5m
        {
            get => _verticalRangeBottom;
            set
                    {
                if (_verticalRangeBottom != value)
                {
                    _verticalRangeBottom = value;
                    IsDirty = true;
                }
            }
        }


        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(RangeRuleActive);
            hash.Add(RangeMin);
            hash.Add(RangeMax);
            hash.Add(RMSRuleActive);
            hash.Add(RMSMax);
            hash.Add(HorizontalRangeRuleActive);
            hash.Add(HorizontalRangeLeft);
            hash.Add(HorizontalRangeRight);
            hash.Add(VerticalRangeRuleActive);
            hash.Add(VerticalRangeTop);
            hash.Add(VerticalRangeBottom);
            return hash.ToHashCode();
        }


        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is null || GetType() != obj.GetType())
            {
                return false;
            }

            SurveyRulesData other = (SurveyRulesData)obj;

            
            return RangeRuleActive == other.RangeRuleActive &&
                   RangeMin == other.RangeMin &&
                   RangeMax == other.RangeMax &&
                   RMSRuleActive == other.RMSRuleActive &&
                   RMSMax == other.RMSMax &&
                   HorizontalRangeRuleActive == other.HorizontalRangeRuleActive &&
                   HorizontalRangeLeft == other.HorizontalRangeLeft &&
                   HorizontalRangeRight == other.HorizontalRangeRight &&
                   VerticalRangeRuleActive == other.VerticalRangeRuleActive &&
                   VerticalRangeTop == other.VerticalRangeTop &&
                   VerticalRangeBottom == other.VerticalRangeBottom;        
        }

        public static bool operator ==(SurveyRulesData left, SurveyRulesData right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null)
            {
                return false;
            }

            return left.Equals(right);
        }

        public static bool operator !=(SurveyRulesData left, SurveyRulesData right)
        {
            return !(left == right);
        }

        public void Clear()
        {
            RangeRuleActive = false;
            RangeMin = 0.0;
            RangeMax = 10.0;
            RMSRuleActive = false;
            RMSMax = 0.0;
            HorizontalRangeRuleActive = false;
            HorizontalRangeLeft = 0.0;
            HorizontalRangeRight = 0.0;
            VerticalRangeRuleActive = false;
            VerticalRangeTop = 0.0;
            VerticalRangeBottom = 0.0;
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

    public class SurveyRulesCalc
    {
        public bool? SurveyRules { get; set; } = null;          // null = no survey rules, false = measurement failed the rules, true = measurement passed the rules
        public string SurveyRulesText { get; set; } = "";    // Info on blocken rules

        public double? Range { get; set; } = null;                // Distance from the left camera to the centre of the two points
        public double? XOffset { get; set; } = null;            // X Distance between the camera system mid-point and the measurement mid-point
        public double? YOffset { get; set; } = null;            // Y Distance between the camera system mid-point and the measurement mid-point
        
        public double? RMS { get; set; } = null;                // RMS error in mm

        public void Clear()
        {
            SurveyRules = null;
            SurveyRulesText = "";
            Range = null;
            XOffset = null;
            YOffset = null;
        }


        /// <summary>
        /// Apply the rules based on the stereo projection calculations
        /// This function should only be called if survey rules are active
        /// </summary>
        /// <param name="surveyRulesData"></param>
        /// <param name="stereoProjection"></param>
        public void ApplyRules(SurveyRulesData surveyRulesData,StereoProjection stereoProjection)
        {
            // Reset
            Clear();

            // Start by assuming the survey passes the rules and prove otherwise
            SurveyRules = true;
            SurveyRulesText = "";
            StringBuilder surveyRulesFailedText = new();
            StringBuilder surveyRulesPassedText = new();
            string ruleText = "";

            // Calculate range (distance from origin)
            Range = stereoProjection.RangeFromCameraSystemCentrePointToMeasurementCentrePoint();

            // Calculate the X & Y offset between the camera system mid-point and the measurement point mid-point
            XOffset = stereoProjection.XOffsetFromCameraSystemCentrePointToMeasurementCentrePoint();
            YOffset = stereoProjection.YOffsetFromCameraSystemCentrePointToMeasurementCentrePoint();

            // Calculate RMS
            RMS = stereoProjection.RMS(null/*TruePointAFalsePointBNullWorstCase*/);

            // Check the range rule
            if (surveyRulesData.RangeRuleActive == true)
            {
                if (Range is not null)
                {
                    ruleText = $"Range: {Math.Round((double)Range, 2)}m (Range allowed from {surveyRulesData.RangeMin}m to {surveyRulesData.RangeMax}m) ";
                    if (Range < surveyRulesData.RangeMin || Range > surveyRulesData.RangeMax)
                    {
                        SurveyRules = false;
                        if (surveyRulesFailedText.Length > 0)
                            surveyRulesFailedText.Append(", ");
                        surveyRulesFailedText.Append(ruleText);
                    }
                    else
                        SurveyRulesCalc.AppendRulesText(surveyRulesPassedText, ruleText);
                }
            }
            else
                SurveyRulesCalc.AppendRulesText(surveyRulesPassedText, "Range Rule Off");

            // Check the RMS rule
            if (surveyRulesData.RMSRuleActive == true)
            {
                if (RMS is not null)
                {
                    ruleText = $"RMS: {Math.Round((double)RMS * 1000, 1)}mm (Max allowed: {surveyRulesData.RMSMax}mm) ";
                    if (RMS/*in metres*/ * 1000 > surveyRulesData.RMSMax/*in mm*/)
                    {
                        SurveyRules = false;
                        if (surveyRulesFailedText.Length > 0)
                            surveyRulesFailedText.Append(", ");
                        surveyRulesFailedText.Append(ruleText);
                    }
                    else
                        SurveyRulesCalc.AppendRulesText(surveyRulesPassedText, ruleText);
                }
            }
            else
                SurveyRulesCalc.AppendRulesText(surveyRulesPassedText, "RMS Rule Off");

            // Check the horizontal range rule
            if (surveyRulesData.HorizontalRangeRuleActive == true)
            {
                if (XOffset is not null)
                {
                    ruleText = $"Horizontal Range: {Math.Round((double)XOffset, 2)}m (Allowed between: left {surveyRulesData.HorizontalRangeLeft}m and right {surveyRulesData.HorizontalRangeRight}m) ";
                    if (XOffset < -surveyRulesData.HorizontalRangeLeft || XOffset > surveyRulesData.HorizontalRangeRight)
                    {
                        SurveyRules = false;
                        SurveyRulesCalc.AppendRulesText(surveyRulesFailedText, ruleText);
                    }
                    else
                        SurveyRulesCalc.AppendRulesText(surveyRulesPassedText, ruleText);
                }
            }
            else
                SurveyRulesCalc.AppendRulesText(surveyRulesPassedText, "Horizontal Range Rule Off");

            // Check the vertical range rule
            if (surveyRulesData.VerticalRangeRuleActive == true)
            {
                if (YOffset is not null)
                {
                    ruleText = $"Vertical Range: {Math.Round((double)YOffset, 2)}m (Allowed between: top {-surveyRulesData.VerticalRangeBottom}m and bottom {surveyRulesData.VerticalRangeTop}m) ";
                    if (YOffset < -surveyRulesData.VerticalRangeBottom || YOffset > surveyRulesData.VerticalRangeTop)
                    {
                        SurveyRules = false;
                        if (surveyRulesFailedText.Length > 0)
                            surveyRulesFailedText.Append(", ");

                        surveyRulesFailedText.Append(ruleText);
                    }
                    else
                        SurveyRulesCalc.AppendRulesText(surveyRulesPassedText, ruleText);
                }
            }
            else
                SurveyRulesCalc.AppendRulesText(surveyRulesPassedText, "Vertical Range Rule Off");

            // Combine the passed and failed rules text            
            if (surveyRulesFailedText.Length > 0 )
                SurveyRulesText = $"FAILED [{surveyRulesFailedText}]";

            if (surveyRulesPassedText.Length > 0)
            {
                if (SurveyRulesText.Length > 0)
                    SurveyRulesText += ", ";
                SurveyRulesText += $"PASSED {surveyRulesPassedText}";
            }
            
        }


        /// <summary>
        /// Append the rules text to a string builder string adding commas if necessary
        /// </summary>
        /// <param name="sb"></param>
        /// <param name="ruleText"></param>
        private static void AppendRulesText(StringBuilder sb, string ruleText)
        {
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(ruleText);
        }


        /// <summary>
        /// Allows to SurveyRulesCalc instance to be compared
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object? obj)
        {
            if (obj is not SurveyRulesCalc other)
                return false;

            return Nullable.Equals(SurveyRules, other.SurveyRules) &&
                   string.Equals(SurveyRulesText, other.SurveyRulesText, StringComparison.Ordinal) &&
                   Nullable.Equals(Range, other.Range) &&
                   Nullable.Equals(XOffset, other.XOffset) &&
                   Nullable.Equals(YOffset, other.YOffset) &&
                   Nullable.Equals(RMS, other.RMS);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                SurveyRules,
                SurveyRulesText,
                Range,
                XOffset,
                YOffset,
                RMS
            );
        }
    }
}

