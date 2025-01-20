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
}

