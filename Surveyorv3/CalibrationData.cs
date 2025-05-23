﻿// Surveyor CalibrationCameraData
// Holds, reads, saves calibration data for a camera.
// 
// Version 1.4
/// Added RMS to CalibrationCameraData
/// Moved alway from MathNET to use native Emgu types
/// Commented out the legacy classes



using Emgu.CV;
using Emgu.CV.Structure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;


namespace Surveyor
{

    public class CalibrationCameraData
    {
        [JsonProperty(nameof(RMS))]
        public double RMS { get; set; }

        [JsonProperty("CameraMatrix")]
        [JsonConverter(typeof(MatrixJsonConverter))]
        public Emgu.CV.Matrix<double>? Intrinsic { get; set; }   // Intrinsic  Camera Matrix 3x3 (K)


        [JsonProperty("DistortionCoefficients")]
        [JsonConverter(typeof(MatrixJsonConverter))]
        public Emgu.CV.Matrix<double>? Distortion { get; set; }   // Distortion coefficient Matrix 4x1

        [JsonProperty(nameof(ImageSize))]
        [JsonConverter(typeof(MatrixJsonConverter))]
        public Emgu.CV.Matrix<int>? ImageSize { get; set; }     // Width, Height of the image

        [JsonProperty(nameof(ImageTotal))]
        public int ImageTotal { get; set; }     // Total number of images supplied to the calibration process

        [JsonProperty(nameof(ImageUseable))]
        public int ImageUseable { get; set; }   // Number of images that were possible to use in the calibration process

        [JsonProperty(nameof(CameraID))]
        public string CameraID { get; set; } = "";  // Unique camera ID (i.e. the serial number) if known


        public override int GetHashCode()
        {
            return HashCode.Combine(RMS, Intrinsic, Distortion, ImageSize, ImageTotal, ImageUseable, CameraID);
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

            CalibrationCameraData other = (CalibrationCameraData)obj;

            
            return RMS == other.RMS &&
                    ((ImageSize == null && other.ImageSize == null) || ImageSize?.Equals(other.ImageSize) == true) &&
                    ImageTotal == other.ImageTotal &&
                    ImageUseable == other.ImageUseable &&
                    ((Intrinsic == null && other.Intrinsic == null) || Intrinsic?.Equals(other.Intrinsic) == true) &&
                    ((Distortion == null && other.Distortion == null) || Distortion?.Equals(other.Distortion) == true) &&
                    CameraID == other.CameraID;
        }

        public static bool operator ==(CalibrationCameraData left, CalibrationCameraData right)
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

        public static bool operator !=(CalibrationCameraData left, CalibrationCameraData right)
        {
            return !(left == right);
        }

        public void Clear()
        {
            RMS = 0;
            Intrinsic = null;
            Distortion = null;
            ImageSize = null;
            ImageTotal = 0;
            ImageUseable = 0;
            CameraID = "";
        }
    }


    public class CalibrationStereoCameraData
    {
        [JsonProperty(nameof(RMS))]
        public double RMS { get; set; } = -1;


        [JsonProperty(nameof(Rotation))]
        [JsonConverter(typeof(MatrixJsonConverter))]
        public Emgu.CV.Matrix<double>? Rotation { get; set; }       // Rotation matrix 3x3


        [JsonProperty(nameof(Translation))]
        [JsonConverter(typeof(MatrixJsonConverter))]
        public Emgu.CV.Matrix<double>? Translation { get; set; }     // Translation vector 3x1


        [JsonProperty(nameof(ImageTotal))]
        public int ImageTotal { get; set; }     // Total number of images supplied to the calibration process

        [JsonProperty(nameof(ImageUseable))]
        public int ImageUseable { get; set; }   // Number of images that were possible to use in the calibration process



        public override int GetHashCode()
        {
            return HashCode.Combine(RMS, Rotation, Translation, ImageTotal, ImageUseable);
        }

        public override bool Equals(object? obj)
        {
            if (obj is CalibrationStereoCameraData other)
            {
                return RMS == other.RMS &&
                       ImageTotal == other.ImageTotal &&
                       ImageUseable == other.ImageUseable &&
                       (Rotation == null && other.Rotation == null || Rotation?.Equals(other.Rotation) == true) &&
                       (Translation == null && other.Translation == null || Translation?.Equals(other.Translation) == true);
            }
            return false;
        }

        public static bool operator ==(CalibrationStereoCameraData left, CalibrationStereoCameraData right)
        {

            return EqualityComparer<CalibrationStereoCameraData>.Default.Equals(left, right);
        }

        public static bool operator !=(CalibrationStereoCameraData left, CalibrationStereoCameraData right)
        {
            return !(left == right);
        }

        public void Clear()
        {
            RMS = 0;
            Rotation = null;
            Translation = null;
            ImageTotal = 0;
            ImageUseable = 0;
        }
    }


    public class CalibrationData
    {
        // Define a GUID property
        public Guid? CalibrationID { get; set; }

        // Optional Description of the calibration data
        public string Description { get; set; } = "";
        
        [JsonProperty("LeftCalibrationCameraData")]
        public CalibrationCameraData LeftCameraCalibration { get; set; } = new();

        [JsonProperty("RightCalibrationCameraData")]
        public CalibrationCameraData RightCameraCalibration { get; set; } = new();

        [JsonProperty("CalibrationStereoCameraData")]
        public CalibrationStereoCameraData StereoCameraCalibration { get; set; } = new();


        public CalibrationData()
        {
        }


        /// <summary>
        /// Load the calibration data from a JSON file
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public int LoadFromFile(string fileSpec)
        {
            int ret = 0;

            try
            {
                // Reset
                Clear();

                // Read the JSON from the file
                string json = System.IO.File.ReadAllText(fileSpec);

                // Load the calibration data from the JSON string
                ret = LoadFromJson(json);

                // Check for errors
                if (ret == 0)
                {
                    if (string.IsNullOrWhiteSpace(Description))
                        Description = System.IO.Path.GetFileNameWithoutExtension(fileSpec);
                }
                else
                    Debug.WriteLine($"CalibrationData.LoadFromFile Error loading calibration data from file:{fileSpec}, error = {ret}");
            }
            catch (Exception ex)
            {
                // Log the exception details
                Debug.WriteLine($"Error loading calibration data: {ex.Message}");
                return -2; // Error code for exception
            }

            return ret;
        }


        /// <summary>
        /// Load the calibration data from a JSON string
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public int LoadFromJson(string json)
        {
            int ret = 0;

            // Check if the JSON is in Calib.IO format
            if (json.Contains("CalibrationCameraData") && json.Contains("CalibrationStereoCameraData"))
            {
                ret = _LoadFromJsonSurveyorFormat(json);
            }
            else if (json.Contains("polymorphic_name"))
            {
                ret = _LoadFromJsonCalidIOFormat(json);
            }

            return ret;
        }

        /// <summary>
        /// Load the calibration data from a JSON string
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        private int _LoadFromJsonSurveyorFormat(string json)
        {
            int ret = 0;

            try
            {
                // Reset
                Clear();

                var settings = new JsonSerializerSettings();
                settings.Converters.Add(new MatrixJsonConverter());
                settings.Converters.Add(new VectorJsonConverter());

                var calibrationData = JsonConvert.DeserializeObject<CalibrationData>(json, settings);

#if DEBUG
                if (calibrationData is not null)
                {
                    string Description = string.IsNullOrEmpty(calibrationData.Description) == false ? calibrationData.Description : "None";
                    string GuidString = calibrationData.CalibrationID?.ToString() ?? "Emtry";

                    Debug.WriteLine($"Description:{Description},  Guid:{GuidString}");
                    Debug.WriteLine($"Left:");
                    if (calibrationData.LeftCameraCalibration.Intrinsic is not null)
                        Debug.WriteLine($"   Camera Matrix: {FormatMatrixToString(calibrationData.LeftCameraCalibration.Intrinsic, 18/*indent*/)}");
                    if (calibrationData.LeftCameraCalibration.Distortion is not null)
                        Debug.WriteLine($"   Distortion Coefficients: {FormatMatrixToString(calibrationData.LeftCameraCalibration.Distortion, 28/*indent*/)}");
                    if (calibrationData.LeftCameraCalibration.ImageSize is not null)
                        Debug.WriteLine($"   Image Size: {FormatMatrixToString(calibrationData.LeftCameraCalibration.ImageSize, 28/*indent*/)}");
                    if (!string.IsNullOrEmpty(calibrationData.LeftCameraCalibration.CameraID))
                        Debug.WriteLine($"   Camera ID: {calibrationData.LeftCameraCalibration.CameraID}");

                    Debug.WriteLine($"Right:");
                    if (calibrationData.RightCameraCalibration.Intrinsic is not null)
                        Debug.WriteLine($"   Camera Matrix: {FormatMatrixToString(calibrationData.RightCameraCalibration.Intrinsic, 18/*indent*/)}");
                    if (calibrationData.RightCameraCalibration.Distortion is not null)
                        Debug.WriteLine($"   Distortion Coefficients: {FormatMatrixToString(calibrationData.RightCameraCalibration.Distortion, 28/*indent*/)}");
                    if (calibrationData.RightCameraCalibration.ImageSize is not null)
                        Debug.WriteLine($"   Image Size: {FormatMatrixToString(calibrationData.RightCameraCalibration.ImageSize, 28/*indent*/)}");
                    if (!string.IsNullOrEmpty(calibrationData.RightCameraCalibration.CameraID))
                        Debug.WriteLine($"   Camera ID: {calibrationData.RightCameraCalibration.CameraID}");

                    Debug.WriteLine("Stereo:");
                    Debug.WriteLine($"   RMS: {calibrationData.StereoCameraCalibration.RMS}");
                    if (calibrationData.StereoCameraCalibration.Rotation is not null)
                        Debug.WriteLine($"   Rotation: {FormatMatrixToString(calibrationData.StereoCameraCalibration.Rotation, 13/*indent*/)}");
                    if (calibrationData.StereoCameraCalibration.Translation is not null)
                        Debug.WriteLine($"   Translation: {FormatMatrixToString(calibrationData.StereoCameraCalibration.Translation, 16/*indent*/)}");
                }
#endif

                // Load this class
                if (calibrationData is not null)
                {
                    CalibrationID = calibrationData.CalibrationID;
                    Description = calibrationData.Description;

                    if (ret == 0)
                    {
                        if (calibrationData.LeftCameraCalibration.Intrinsic is not null)
                            LeftCameraCalibration.Intrinsic = calibrationData.LeftCameraCalibration.Intrinsic;
                        else
                        {
                            Debug.WriteLine("Calibration.Load()   LeftCalibrationCameraData.Mtx is null");
                            ret = -4;
                        }

                        if (calibrationData.LeftCameraCalibration.Distortion is not null)
                            LeftCameraCalibration.Distortion = calibrationData.LeftCameraCalibration.Distortion;
                        else
                        {
                            Debug.WriteLine("Calibration.Load()   LeftCalibrationCameraData.Dist is null");
                            ret = -5;
                        }

                        LeftCameraCalibration.RMS = calibrationData.LeftCameraCalibration.RMS;
                        LeftCameraCalibration.ImageTotal = calibrationData.LeftCameraCalibration.ImageTotal;
                        LeftCameraCalibration.ImageUseable = calibrationData.LeftCameraCalibration.ImageUseable;
                        LeftCameraCalibration.ImageSize = calibrationData.LeftCameraCalibration.ImageSize;
                        LeftCameraCalibration.CameraID = calibrationData.LeftCameraCalibration.CameraID;
                    }


                    if (ret == 0)
                    {
                        if (calibrationData.RightCameraCalibration.Intrinsic is not null)
                            RightCameraCalibration.Intrinsic = calibrationData.RightCameraCalibration.Intrinsic;
                        else
                        {
                            Debug.WriteLine("Calibration.Load()   RightCalibrationCameraData.Mtx is null");
                            ret = -8;
                        }

                        if (calibrationData.RightCameraCalibration.Distortion is not null)
                            RightCameraCalibration.Distortion = calibrationData.RightCameraCalibration.Distortion;
                        else
                        {
                            Debug.WriteLine("Calibration.Load()   RightCalibrationCameraData.Dist is null");
                            ret = -9;
                        }

                        RightCameraCalibration.RMS = calibrationData.RightCameraCalibration.RMS;
                        RightCameraCalibration.ImageTotal = calibrationData.RightCameraCalibration.ImageTotal;
                        RightCameraCalibration.ImageUseable = calibrationData.RightCameraCalibration.ImageUseable;
                        RightCameraCalibration.ImageSize = calibrationData.RightCameraCalibration.ImageSize;
                        RightCameraCalibration.CameraID = calibrationData.RightCameraCalibration.CameraID;
                    }

                    if (ret == 0)
                    {
                        StereoCameraCalibration.RMS = calibrationData.StereoCameraCalibration.RMS;
                        StereoCameraCalibration.ImageTotal = calibrationData.StereoCameraCalibration.ImageTotal;
                        StereoCameraCalibration.ImageUseable = calibrationData.StereoCameraCalibration.ImageUseable;

                        if (calibrationData.StereoCameraCalibration.Rotation is not null)
                            StereoCameraCalibration.Rotation = calibrationData.StereoCameraCalibration.Rotation;
                        else
                        {
                            Debug.WriteLine("Calibration.Load()   CalibrationStereoCameraData.Rotation is null");
                            ret = -12;
                        }

                        if (calibrationData.StereoCameraCalibration.Translation is not null)
                            StereoCameraCalibration.Translation = calibrationData.StereoCameraCalibration.Translation;
                        else
                        {
                            Debug.WriteLine("Calibration.Load()   CalibrationStereoCameraData.Translation is null");
                            ret = -13;
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("Calibration.Load()   calibrationData is null");
                    ret = -1;
                }

            }
            catch (Exception ex)
            {
                // Log the exception details
                Debug.WriteLine($"Error loading calibration data: {ex.Message}");
                return -2; // Error code for exception
            }

            return ret;
        }


        /// <summary>
        /// Load the Calid.IO calibration data from a JSON string and
        /// load into the CalibrationData object
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        private int _LoadFromJsonCalidIOFormat(string json)
        {
            int ret = 0;

            // Parse the JSON string into a JObject
            JObject jsonStruct = JObject.Parse(json);

            JToken? token = jsonStruct["Calibration"]?["cameras"]?[0]?["model"]?["polymorphic_name"];

            if (token is not null && (string?)token == "libCalib::CameraModelOpenCV")
            {
                // Get the number of cameras
                JArray? camerasArray = (JArray?)jsonStruct["Calibration"]?["cameras"];
                int nCameras = camerasArray?.Count ?? 0;

                if (nCameras == 2)
                {
                    for (int i = 0; i < nCameras; ++i)
                    {
                        var imageSize = jsonStruct["Calibration"]?["cameras"]?[i]?["model"]?["ptr_wrapper"]?["data"]?["CameraModelCRT"]?["CameraModelBase"]?["imageSize"];

                        if (imageSize is not null)
                        {
                            int? width = (int?)imageSize["width"];
                            int? height = (int?)imageSize["height"];

                            if (width is not null && height is not null)
                            {
                                // Image Size
                                if (i == 0/*left camera*/)
                                {
                                    LeftCameraCalibration.ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/);
                                    LeftCameraCalibration.ImageSize[0, 0] = (int)width;
                                    LeftCameraCalibration.ImageSize[0, 1] = (int)height;
                                }
                                else if (i == 1/*right camera*/)
                                {
                                    RightCameraCalibration.ImageSize = new Emgu.CV.Matrix<int>(1/*rows*/, 2/*cols*/);
                                    RightCameraCalibration.ImageSize[0, 0] = (int)width;
                                    RightCameraCalibration.ImageSize[0, 1] = (int)height;
                                }
                            }
                        }


                        var intrinsics = jsonStruct["Calibration"]?["cameras"]?[i]?["model"]?["ptr_wrapper"]?["data"]?["parameters"];

                        if (intrinsics is not null)
                        {
                            double? f = (double?)intrinsics["f"]?["val"];
                            double? ar = (double?)intrinsics["ar"]?["val"];
                            double? cx = (double?)intrinsics["cx"]?["val"];
                            double? cy = (double?)intrinsics["cy"]?["val"];
                            double? k1 = (double?)intrinsics["k1"]?["val"];
                            double? k2 = (double?)intrinsics["k2"]?["val"];
                            double? k3 = (double?)intrinsics["k3"]?["val"];
                            double? k4 = (double?)intrinsics["k4"]?["val"];
                            double? k5 = (double?)intrinsics["k5"]?["val"];
                            double? k6 = (double?)intrinsics["k6"]?["val"];
                            double? p1 = (double?)intrinsics["p1"]?["val"];
                            double? p2 = (double?)intrinsics["p2"]?["val"];
                            double? s1 = (double?)intrinsics["s1"]?["val"];
                            double? s2 = (double?)intrinsics["s2"]?["val"];
                            double? s3 = (double?)intrinsics["s3"]?["val"];
                            double? s4 = (double?)intrinsics["s4"]?["val"];
                            double? tauX = (double?)intrinsics["tauX"]?["val"];
                            double? tauY = (double?)intrinsics["tauY"]?["val"];

                            if (i == 0/*left camera*/)
                            {
                                // Left Camera Matrix
                                LeftCameraCalibration.Intrinsic = new Emgu.CV.Matrix<double>(3/*rows*/, 3/*cols*/);
                                LeftCameraCalibration.Intrinsic[0, 0] = f ?? 0.0;
                                LeftCameraCalibration.Intrinsic[0, 1] = 0.0;
                                LeftCameraCalibration.Intrinsic[0, 2] = cx ?? 0.0;
                                LeftCameraCalibration.Intrinsic[1, 0] = 0.0;
                                LeftCameraCalibration.Intrinsic[1, 1] = f * ar ?? 0.0;
                                LeftCameraCalibration.Intrinsic[1, 2] = cy ?? 0.0;
                                LeftCameraCalibration.Intrinsic[2, 0] = 0.0;
                                LeftCameraCalibration.Intrinsic[2, 1] = 0.0;
                                LeftCameraCalibration.Intrinsic[2, 2] = 1.0;

                                // Left Distortion Coefficients (we use the 5 element model)
                                LeftCameraCalibration.Distortion = new Emgu.CV.Matrix<double>(1/*rows*/, 5/*cols*/);
                                LeftCameraCalibration.Distortion[0, 0] = k1 ?? 0.0;
                                LeftCameraCalibration.Distortion[0, 1] = k2 ?? 0.0;
                                LeftCameraCalibration.Distortion[0, 2] = p1 ?? 0.0;
                                LeftCameraCalibration.Distortion[0, 3] = p2 ?? 0.0;
                                LeftCameraCalibration.Distortion[0, 4] = k3 ?? 0.0;
                            }
                            else if (i == 1/*right camera*/)
                            {
                                // Right Camera Matrix
                                RightCameraCalibration.Intrinsic = new Emgu.CV.Matrix<double>(3/*rows*/, 3/*cols*/);
                                RightCameraCalibration.Intrinsic[0, 0] = f ?? 0.0;
                                RightCameraCalibration.Intrinsic[0, 1] = 0.0;
                                RightCameraCalibration.Intrinsic[0, 2] = cx ?? 0.0;
                                RightCameraCalibration.Intrinsic[1, 0] = 0.0;
                                RightCameraCalibration.Intrinsic[1, 1] = f * ar ?? 0.0;
                                RightCameraCalibration.Intrinsic[1, 2] = cy ?? 0.0;
                                RightCameraCalibration.Intrinsic[2, 0] = 0.0;
                                RightCameraCalibration.Intrinsic[2, 1] = 0.0;
                                RightCameraCalibration.Intrinsic[2, 2] = 1.0;

                                // Right Distortion Coefficients (we use the 5 element model)
                                RightCameraCalibration.Distortion = new Emgu.CV.Matrix<double>(1/*rows*/, 5/*cols*/);
                                RightCameraCalibration.Distortion[0, 0] = k1 ?? 0.0;
                                RightCameraCalibration.Distortion[0, 1] = k2 ?? 0.0;
                                RightCameraCalibration.Distortion[0, 2] = p1 ?? 0.0;
                                RightCameraCalibration.Distortion[0, 3] = p2 ?? 0.0;
                                RightCameraCalibration.Distortion[0, 4] = k3 ?? 0.0;


                                // Stereo Calibration Data
                                var transform = jsonStruct["Calibration"]?["cameras"]?[i]?["transform"];
                                if (transform is not null)
                                {
                                    var rot = transform["rotation"];

                                    if (rot is not null)
                                    {
                                        Emgu.CV.Matrix<double> rotationVector = new(3, 1);
                                        rotationVector[0, 0] = (double?)rot["rx"] ?? 0.0;
                                        rotationVector[1, 0] = (double?)rot["ry"] ?? 0.0;
                                        rotationVector[2, 0] = (double?)rot["rz"] ?? 0.0;

                                        StereoCameraCalibration.Rotation = new Emgu.CV.Matrix<double>(3/*rows*/, 3/*cols*/);

                                        // Convert the rotation vector to a rotation matrix
                                        CvInvoke.Rodrigues(rotationVector, StereoCameraCalibration.Rotation);
                                    }

                                    var t = transform["translation"];
                                    if (t is not null)
                                    {
                                        // Right Translation
                                        StereoCameraCalibration.Translation = new Emgu.CV.Matrix<double>(1/*rows*/, 3/*cols*/);
                                        StereoCameraCalibration.Translation[0, 0] = (double?)t["x"] ?? 0.0;
                                        StereoCameraCalibration.Translation[0, 1] = (double?)t["y"] ?? 0.0;
                                        StereoCameraCalibration.Translation[0, 2] = (double?)t["z"] ?? 0.0;
                                    }

                                    if (rot is not null && t is not null && CalibrationID is null)
                                    {
                                        CalibrationID = Guid.NewGuid();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                return -4; // Error code for invalid Calib.IO format
            }

            return ret;
        }


        /// <summary>
        /// Save the calibration data to a JSON file
        /// </summary>
        /// <param name="fileSpec"></param>
        /// <returns></returns>
        public int SaveToFile(string fileSpec)
        {
            int ret = 0;

            try
            {
                string json;

                ret = SaveToJson(out json);

                if (ret == 0)
                    System.IO.File.WriteAllText(fileSpec, json);
            }
            catch (Exception ex)
            {
                // Log the exception details
                Console.WriteLine($"Error saving calibration data: {ex.Message}");
                return -2; // Error code for exception
            }

            return ret;
        }


        /// <summary>
        /// Save the calibration data to a JSON string
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public int SaveToJson(out string json)
        {
            int ret = 0;

            // Reset
            json = "";

            // Add a unique Guid to identify this calibration set of data id missing
            this.CalibrationID ??= Guid.NewGuid();

            try
            {
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.None,      //.Indented, // For pretty-printing the JSON
                    Converters = new List<JsonConverter> { new MatrixJsonConverter(), new VectorJsonConverter() }
                };

                json = JsonConvert.SerializeObject(this, settings);
            }
            catch (Exception ex)
            {
                // Log the exception details
                Console.WriteLine($"Error saving calibration data: {ex.Message}");
                return -2; // Error code for exception
            }

            return ret;
        }


        /// <summary>
        /// Check this CalibrationData object is suitable for the supplied frame size
        /// </summary>
        /// <param name="frameWidth"></param>
        /// <param name="frameHeight"></param>
        /// <returns></returns>
        public bool FrameSizeCompare(int frameWidth, int frameHeight)
        {
            bool ret = false;

            // Use the left camera data
            if (LeftCameraCalibration is not null && LeftCameraCalibration.ImageSize is not null)
            {
                if (LeftCameraCalibration.ImageSize[0, 0] == frameWidth &&
                    LeftCameraCalibration.ImageSize[0, 1] == frameHeight)
                {
                    ret = true;
                }
            }

            return ret;
        }


        /// <summary>
        /// Clear all values in the CalibrationData object
        /// </summary>
        public void Clear()
        {
            CalibrationID = null;
            Description = "";
            //??? Create Clear() function in CalibrationCameraData() and CalibrationStereoCameraData() classes
            LeftCameraCalibration.Clear();
            RightCameraCalibration.Clear();
            StereoCameraCalibration.Clear();
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(CalibrationID,
                                    Description,
                                    LeftCameraCalibration,
                                    RightCameraCalibration,
                                    StereoCameraCalibration);
        }



        /// <summary>
        /// Used to compare for a extact match of the calibration data
        /// Use CalibrationData.Compare() to compare if the values match ignoring the CalibrationID Guid
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(CalibrationData left, CalibrationData right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
            {
                return false;
            }

            return left.Equals(right);
        }


        /// <summary>
        /// Used to compare for a extact non-match of the calibration data
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(CalibrationData left, CalibrationData right)
        {
            return !(left == right);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (ReferenceEquals(obj, null))
            {
                return false;
            }

            if (obj is CalibrationData other)
            {
                return CalibrationID == other.CalibrationID &&
                       Compare(other);
            }

            return false;
        }


        /// <summary>
        /// Compare if the values match ignoring the CalibrationID Guid
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Compare(CalibrationData other)
        {
            return Description == other.Description &&
                    LeftCameraCalibration.Equals(other.LeftCameraCalibration) &&
                    RightCameraCalibration.Equals(other.RightCameraCalibration) &&
                    StereoCameraCalibration.Equals(other.StereoCameraCalibration);
        }



        /// <summary>
        /// Convert a Emgu.CV.Matrix into a readable string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="matrix"></param>
        /// <param name="indent"></param>
        /// <returns></returns>
        private static string FormatMatrixToString<T>(Emgu.CV.Matrix<T>? matrix, int indent) where T : struct
        {
            var sb = new StringBuilder();

            if (matrix is not null)
            {
                for (int row = 0; row < matrix.Rows; row++)
                {
                    if (row > 0 && indent > 0)
                        sb.Append(new string(' ', indent));

                    for (int col = 0; col < matrix.Cols; col++)
                    {
                        sb.Append(matrix[row, col].ToString());

                        if (col < matrix.Cols - 1)
                            sb.Append(", ");
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }



    public class MatrixJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Emgu.CV.Matrix<double>) || objectType == typeof(Emgu.CV.Matrix<int>);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var array = JArray.Load(reader);
            if (array == null || array.Count == 0 || array[0].HasValues == false)
            {
                return null;
            }

            int rows = array.Count;
            int cols = array[0].Count();

            if (objectType == typeof(Emgu.CV.Matrix<double>))
            {
                var matrix = new Emgu.CV.Matrix<double>(rows, cols);
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        matrix[i, j] = (double)array[i][j]!;
                    }
                }
                return matrix;
            }
            else if (objectType == typeof(Emgu.CV.Matrix<int>))
            {
                var matrix = new Emgu.CV.Matrix<int>(rows, cols);
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        matrix[i, j] = (int)array[i][j]!;
                    }
                }
                return matrix;
            }
            else
            {
                throw new JsonSerializationException($"Unexpected matrix type: {objectType}");
            }
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (value is Emgu.CV.Matrix<double> matrixDouble)
            {
                writer.WriteStartArray();
                for (int i = 0; i < matrixDouble.Rows; i++)
                {
                    writer.WriteStartArray();
                    for (int j = 0; j < matrixDouble.Cols; j++)
                    {
                        writer.WriteValue(matrixDouble[i, j]);
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
            }
            else if (value is Emgu.CV.Matrix<int> matrixInt)
            {
                writer.WriteStartArray();
                for (int i = 0; i < matrixInt.Rows; i++)
                {
                    writer.WriteStartArray();
                    for (int j = 0; j < matrixInt.Cols; j++)
                    {
                        writer.WriteValue(matrixInt[i, j]);
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
            }
            else
            {
                throw new JsonSerializationException($"Unexpected matrix type: {value.GetType()}");
            }
        }
    }


    public class VectorJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(MCvPoint3D32f);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var array = JArray.Load(reader);

            if (array != null && array.Count == 3)
            {
                return new MCvPoint3D32f(
                    (float)array[0],
                    (float)array[1],
                    (float)array[2]);
            }
            else
            {
                throw new JsonSerializationException("Unexpected JSON structure for MCvPoint3D64f deserialization.");
            }
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var vector = value as MCvPoint3D32f?;

            if (vector != null)
            {
                writer.WriteStartArray();
                writer.WriteValue(vector.Value.X);
                writer.WriteValue(vector.Value.Y);
                writer.WriteValue(vector.Value.Z);
                writer.WriteEndArray();
            }
        }
    }
}

