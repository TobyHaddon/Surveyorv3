using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Cryptography.Core;

namespace Surveyor.Calibration
{

    /// <summary>
    /// A CalibrationMonoFrameSet instance holds all the extracted calibration frames metadata (FrameCalibrationTarget)
    /// in a sorted directory called 'Frames'.
    /// </summary>
    public class CalibrationFrameSet
    {
        // A sorted dictionary of frames, sorted by frame index that holds the calibration
        // board corners and ids, the blur factor and the movement factor
        [JsonProperty(nameof(Frames))]
        public SortedDictionary<int, FrameCalibrationTarget> Frames { get; set; } = [];

        public List<int> BestFrames = [];

        // A dictionary of bin totals, where the key is a tuple of (gx, gy, binx, biny)
        [JsonProperty(nameof(BinTotals))]
        [TypeConverter(typeof(TupleInt4JsonConverter))]
        public Dictionary<(int gx, int gy, int binx, int biny), int> BinTotals = [];



        public const double BLUR_LARGEVALUE = 10.0;
        public const double MOVEMENT_LARGEVALUE = 400.0;


        /// <summary>
        /// Returns the maximum MovementFactor across all frames in the set.
        /// </summary>
        public double MaxMovementFactor =>
            Frames.Values
                .Select(f => f.MovementFactor)
                .Where(v => v >= 0)  // Ignore unset (-1) values
                .DefaultIfEmpty(0)
                .Max();


        /// <summary>
        /// Returns the maximum BlurFactor across all frames in the set.
        /// </summary>
        public double MaxBlurFactor =>
            Frames.Values
                .Select(f => f.BlurFactor)
                .Where(v => v != double.MaxValue)
                .DefaultIfEmpty(0)
                .Max();


        /// <summary>
        /// Returns the maximum number of corners found
        /// </summary>
        public int MaxCharucoCorners =>
            Frames.Values
                .Select(f => f.CharucoCorners.Length)
                /*.Where(v => v != double.MaxValue)*/
                .DefaultIfEmpty(0)
                .Max();


        public void AddFrame(FrameCalibrationTarget frame)
        {
            Frames[frame.FrameIndex] = frame;
        }

        public void AddFrame(int frameIndex, Mat grayFrame, PointF[] charucoCorners, int[] charucoIds, int resolutionX, int resolutionY)
        {
            if (charucoCorners == null || charucoCorners.Length == 0)
                return;


            FrameCalibrationTarget target = new(frameIndex, grayFrame, charucoCorners, charucoIds, resolutionX, resolutionY);

            Frames[frameIndex] = target;

            // If there is a prior and/or next continious frame, calculate the movement
            // from this frame to those previous frames (note values in all three frames
            // maybe updated
            CalculateCornerMovement(frameIndex);

            // Update the bin totals
            foreach (var bin in target.BinsOccupied)
            {
                BinTotals[bin] = BinTotals.GetValueOrDefault(bin) + 1;
            }
        }

        public bool RemoveFrame(int frameIndex)
        {
            bool ret = false;

            if (Frames.ContainsKey(frameIndex))
            {
                // Remove the bins from the bin totals
                foreach (var bin in Frames[frameIndex].BinsOccupied)
                {
                    BinTotals[bin] = BinTotals.GetValueOrDefault(bin) - 1;
                    if (BinTotals[bin] == 0)
                        BinTotals.Remove(bin);
                }

                // Remove the frame from the dictionary 
                ret = Frames.Remove(frameIndex);

                if (ret)
                {
                    // Is there a previous contiguious frame?
                    if (Frames.ContainsKey(frameIndex - 1))
                    {
                        // Movement from this frame to the previous frame need to be recalculated
                        Frames[frameIndex - 1].MovementToNext = -1;
                    }
                    // Is there a next contiguious frame?
                    if (Frames.ContainsKey(frameIndex + 1))
                    {
                        // Movement from this frame to the next frame need to be recalculated
                        Frames[frameIndex + 1].MovementFromPrevious = -1;
                    }
                }
            }

            return ret;
        }


        public void ReportOnLargeValues(bool trueLeftrightFalse, bool suppressValues)
        {
            // Return a list of frame indexes where the movement factor is large
            List<int> largeMovementList = [.. Frames.Where(f => f.Value.MovementFactor > MOVEMENT_LARGEVALUE).Select(f => f.Key)]; // Return a list of frame indexes where the movement factor is large

            if (largeMovementList.Count > 0)
            {
                string side = trueLeftrightFalse ? "Left" : "Right";
                Debug.WriteLine($"{side} side large movement frames: {string.Join(", ", largeMovementList)}");
            }
        }


        /// <summary>
        /// Calculate the movement of the corners from this frame to the previous (if any)
        /// frame and the next frame (if any). Update the movement values in all three frames
        /// </summary>
        /// <param name=""></param>
        /// <returns>true if any changes</returns>
        private bool CalculateCornerMovement(int frameIndex)
        {
            bool ret = false;
            double movementBetweenFrame;

            // Is there a previous contiguious frame?
            if (Frames.ContainsKey(frameIndex - 1))
            {
                // Movement from this frame to the previous frame
                movementBetweenFrame = FrameCalibrationTarget.CalculateCornerMovement(
                                       Frames[frameIndex], Frames[frameIndex - 1]);

                Frames[frameIndex].MovementFromPrevious = movementBetweenFrame;
                Frames[frameIndex - 1].MovementToNext = movementBetweenFrame;
                ret = true;
            }
            else
            {
                // No previous frame, we should assume a large movement value
                // this is because we are trying to ultimate detect frame with
                // the lowest movement factor. In this case we just don't know.
                // So we set the movement to a large value, so it will be ignored
                // and return false
                Frames[frameIndex].MovementFromPrevious = -1;
            }

            // Is there a next contiguious frame?
            if (Frames.ContainsKey(frameIndex + 1))
            {
                // Movement from this frame to the next frame
                movementBetweenFrame = FrameCalibrationTarget.CalculateCornerMovement(
                                       Frames[frameIndex], Frames[frameIndex + 1]);

                Frames[frameIndex].MovementToNext = movementBetweenFrame;
                Frames[frameIndex + 1].MovementFromPrevious = movementBetweenFrame;
                ret = true;
            }
            else
            {
                // No next frame, we should assume a large movement value
                // this is because we are trying to ultimate detect frame with
                // the lowest movement factor. In this case we just don't know.
                // So we set the movement to a large value, so it will be ignored
                // and return false
                Frames[frameIndex].MovementToNext = -1;
            }

            return ret;
        }


        public bool SelectBestFrames()
        {
            HashSet<int> frameIndexSet = [];

            // For the first (and only) layer parse each bin
            foreach (var layer in FrameCalibrationTarget.GridLayers)
            {
                int gx = layer.x;
                int gy = layer.y;
               
                for (int biny = 0; biny < gy; biny++)
                {
                    for (int binx = 0; binx < gx; binx++)
                    {
                        (int gx, int gy, int binx, int biny) targetBin = (gx, gy, binx, biny);

                        var frameIndexes = Frames.Values
                                     .Where(f => f.BinsOccupied.Contains(targetBin) && f.MovementFactor >= 0)
                                     .OrderBy(f => f.MovementFactor)
                                     .ThenBy(f => f.BlurFactor)
                                     .Take(2)
                                     .Select(f => f.FrameIndex);
                                     
                        foreach (var index in frameIndexes)
                            frameIndexSet.Add(index); // HashSet ensures uniqueness 
                    }

                }
                
            }

            BestFrames = frameIndexSet.ToList();

            return true;
        }


        private static List<(int gx, int gy, int binX, int binY)> GetBinsForCharucoCorners(PointF[] corners, int resolutionX, int resolutionY)
        {
            List<(int gx, int gy, int binX, int binY)> bins = [];
            foreach (var corner in corners)
            {
                foreach (var (gx, gy) in FrameCalibrationTarget.GridLayers)
                {
                    int binX = Math.Clamp((int)(corner.X / (resolutionX / (double)gx)), 0, gx - 1);
                    int binY = Math.Clamp((int)(corner.Y / (resolutionY / (double)gy)), 0, gy - 1);

                    // Check if already there
                    if (bins.Contains((gx, gy, binX, binY)))
                        continue;

                    // If not add
                    bins.Add((gx, gy, binX, binY));
                }
            }
            return bins;
        }


        /// <summary>
        /// Get the bin counts for a given grid layer (gx, gy) and bin (binx, biny).
        /// </summary>
        /// <param name="gx"></param>
        /// <param name="gy"></param>
        /// <returns></returns>
        public Dictionary<(int gx, int gy, int binx, int biny), int> GetBinCounts(int gx, int gy)
        {
            var counts = new Dictionary<(int gx, int gy, int binx, int biny), int>();

            foreach (var f in Frames.Values)
            {
                foreach (var bin in f.BinsOccupied)
                {
                    // Find the this bin in the counts list, if not found create an new entry in counts
                    counts[bin] = counts.GetValueOrDefault(bin) + 1;
                }
            }

            return counts;
        }


        /// <summary>
        /// Load a CalibrationFrameSet from a JSON file.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static CalibrationFrameSet? LoadFromFile(string path)
        {
            CalibrationFrameSet? ret = null;

            try
            {
                var json = File.ReadAllText(path);
                if (json is not null)
                {
                    try
                    {
                        var settings = new JsonSerializerSettings
                        {
                            Converters = { new TupleInt4JsonConverter() }
                        };


                        ret = JsonConvert.DeserializeObject<CalibrationFrameSet>(json, settings);
                    }
                    catch (JsonSerializationException jsex)
                    {
                        Debug.WriteLine($"JSON Serialization Error: {jsex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file access issues)
                Debug.WriteLine($"Error loading from file: {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// Save the CalibrationFrameSet to a JSON file.
        /// </summary>
        /// <param name="path"></param>
        public bool SaveToFile(string path)
        {
            bool ret = false;
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.None,
                    Converters = { new TupleInt4JsonConverter() }
                };

                var json = JsonConvert.SerializeObject(this, settings);
                File.WriteAllText(path, json);
                ret = true;
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file access issues)
                Debug.WriteLine($"Error saving to file: {ex.Message}");
            }

            return ret;
        }

        /*** End of CalibrationFrameSet ***/
    }

    public class TupleInt4JsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Dictionary<(int, int, int, int), int>);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var result = new Dictionary<(int, int, int, int), int>();
            var obj = JObject.Load(reader);

            foreach (var prop in obj.Properties())
            {
                // Parse string key: "(6, 4, 3, 0)"
                var keyString = prop.Name.Trim('(', ')');
                var parts = keyString.Split(',');

                if (parts.Length == 4 &&
                    int.TryParse(parts[0], out int a) &&
                    int.TryParse(parts[1], out int b) &&
                    int.TryParse(parts[2], out int c) &&
                    int.TryParse(parts[3], out int d))
                {
                    var key = (a, b, c, d);
                    var value = prop.Value.ToObject<int>();
                    result[key] = value;
                }
            }

            return result;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var dict = value as Dictionary<(int, int, int, int), int>;
            if (dict == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            foreach (var kvp in dict)
            {
                string key = $"({kvp.Key.Item1}, {kvp.Key.Item2}, {kvp.Key.Item3}, {kvp.Key.Item4})";
                writer.WritePropertyName(key);
                writer.WriteValue(kvp.Value);
            }
            writer.WriteEndObject();
        }

        /*** End of TupleInt4JsonConverter ***/
    }
}

