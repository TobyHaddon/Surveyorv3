// Surveyor SurveyRulesData
// Holds, reads, saves the rules that allow fish to be included in the survey
// This is the size of the survey cross-section etc
// 
// Version 1.0
//


//using Emgu.CV;
//using Emgu.CV.Structure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using System.Text;
using Windows.Web.Http;


namespace Surveyor
{

    public class SurveyRulesData
    {
        [JsonProperty(nameof(RangeRuleActive))]
        public bool RangeRuleActive { get; set; }   // True if the range restriction rule is being used

        [JsonProperty(nameof(RangeMin))]
        public double RangeMin { get; set; }        // The minimum range a fish can be recorded at in metres e.g. 0.5m

        [JsonProperty(nameof(RangeMax))]
        public double RangeMax { get; set; }        // The maximum range a fish can be recorded at in metres e.g. 10.0m


        [JsonProperty(nameof(RMSRuleActive))]        
        public bool RMSRuleActive { get; set; }     // True if the RMS restriction rule is being used

        [JsonProperty(nameof(RMSMax))]
        public double RMSMax { get; set; }          // The maximum RMS error that is allowed in mm e.g. 20mm


        [JsonProperty(nameof(HorizontalRangeRuleActive))]
        public bool HorizontalRangeRuleActive { get; set; } // True if the horizontal range restriction rule is being used

        [JsonProperty(nameof(HorizontalRangeLeft))]
        public double HorizontalRangeLeft { get; set; }     // The size of the survey box in metres to the left of the camera centre point e.g. 2.5m

        [JsonProperty(nameof(HorizontalRangeRight))]
        public double HorizontalRangeRight { get; set; }    // The size of the survey box in metres to the right of the camera centre point e.g. 2.5m


        [JsonProperty(nameof(VerticalRangeRuleActive))]
        public bool VerticalRangeRuleActive { get; set; }   // True if the vertical range restriction rule is being used

        [JsonProperty(nameof(VerticalRangeTop))]
        public double VerticalRangeTop { get; set; }        // The size of the survey box in metres above the camera centre point e.g. 2.5m

        [JsonProperty(nameof(VerticalRangeBottom))]
        public double VerticalRangeBottom { get; set; }    // The size of the survey box in metres below the camera centre point e.g. 2.5m


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
            RangeMax = 0.0;
            RMSRuleActive = false;
            RMSMax = 0.0;
            HorizontalRangeRuleActive = false;
            HorizontalRangeLeft = 0.0;
            HorizontalRangeRight = 0.0;
            VerticalRangeRuleActive = false;
            VerticalRangeTop = 0.0;
            VerticalRangeBottom = 0.0;
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
            // Start by assuming the survey passes the rules and prove otherwise
            SurveyRules = true;
            SurveyRulesText = "";

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
                if (Range < surveyRulesData.RangeMin || Range > surveyRulesData.RangeMax)
                {
                    SurveyRules = false;
                    if (SurveyRulesText != "")
                        SurveyRulesText += "\n";
                    SurveyRulesText += "Range: " + Range + "m (" + surveyRulesData.RangeMin + "m - " + surveyRulesData.RangeMax + "m) ";
                }
            }   

            // Check the RMS rule
            if (surveyRulesData.RMSRuleActive == true)
            {
                if (RMS > surveyRulesData.RMSMax)
                {
                    SurveyRules = false;
                    if (SurveyRulesText != "")
                        SurveyRulesText += "\n";
                    SurveyRulesText += "RMS: " + RMS + "mm (" + surveyRulesData.RMSMax + "mm) ";
                }
            }

            // Check the horizontal range rule
            if (surveyRulesData.HorizontalRangeRuleActive == true)
            {
                if (XOffset < -surveyRulesData.HorizontalRangeLeft || XOffset > surveyRulesData.HorizontalRangeRight)
                {
                    SurveyRules = false;
                    if (SurveyRulesText != "")
                        SurveyRulesText += "\n";
                    SurveyRulesText += "Horizontal Range: " + XOffset + "m (" + -surveyRulesData.HorizontalRangeLeft + "m - " + surveyRulesData.HorizontalRangeRight + "m) ";
                }
            }

            // Check the vertical range rule
            if (surveyRulesData.VerticalRangeRuleActive == true)
            {
                if (YOffset < -surveyRulesData.VerticalRangeBottom || YOffset > surveyRulesData.VerticalRangeTop)
                {
                    SurveyRules = false;
                    if (SurveyRulesText != "")
                        SurveyRulesText += "\n";
                    SurveyRulesText += "Vertical Range: " + YOffset + "m (" + -surveyRulesData.VerticalRangeBottom + "m - " + surveyRulesData.VerticalRangeTop + "m) ";
                }
            }
        }
    }
}

