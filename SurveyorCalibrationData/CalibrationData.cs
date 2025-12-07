// Surveyor CalibrationCameraData
// Holds, reads, saves calibration data for a camera.
// 
// Version 1.4
// Added RMS to CalibrationCameraData
// Moved alway from MathNET to use native Emgu types
// Commented out the legacy classes
//
// Version 1.5
// Moved into it's own library so it can be shared between
// Surveyorv3 and SurveyorCalibration


using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;


namespace SurveyorCalibrationData
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
        public Emgu.CV.Matrix<double>? Distortion { get; set; }   // Distortion coefficient Matrix 1x4, 1x5 or 1x8 (D)

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
            var hash = new HashCode();
            hash.Add(RMS);
            hash.Add(ImageTotal);
            hash.Add(ImageUseable);
            hash.Add(CameraID, StringComparer.Ordinal);
            hash.Add(HashMatrix(Intrinsic));   // content hash
            hash.Add(HashMatrix(Distortion));  // content hash
            hash.Add(HashMatrix(ImageSize));   // content hash
            return hash.ToHashCode();
        }

        private static int HashMatrix(Emgu.CV.Matrix<double>? m)
        {
            if (m is null) return 0;
            var h = new HashCode();
            h.Add(m.Rows);
            h.Add(m.Cols);
            for (int r = 0; r < m.Rows; r++)
                for (int c = 0; c < m.Cols; c++)
                    h.Add(m[r, c]);
            return h.ToHashCode();
        }

        private static int HashMatrix(Emgu.CV.Matrix<int>? m)
        {
            if (m is null) return 0;
            var h = new HashCode();
            h.Add(m.Rows);
            h.Add(m.Cols);
            for (int r = 0; r < m.Rows; r++)
                for (int c = 0; c < m.Cols; c++)
                    h.Add(m[r, c]);
            return h.ToHashCode();
        }


        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj is not CalibrationCameraData other) return false;

            return RMS == other.RMS &&
                   ImageTotal == other.ImageTotal &&
                   ImageUseable == other.ImageUseable &&
                   string.Equals(CameraID, other.CameraID, StringComparison.Ordinal) &&
                   MatEqual(Intrinsic, other.Intrinsic) &&
                   MatEqual(Distortion, other.Distortion) &&
                   MatEqual(ImageSize, other.ImageSize);
        }

        private static bool MatEqual(Emgu.CV.Matrix<double>? a, Emgu.CV.Matrix<double>? b, double tol = 0.0)
        {
            if (a is null || b is null) return a is null && b is null;
            if (a.Rows != b.Rows || a.Cols != b.Cols) return false;
            for (int r = 0; r < a.Rows; r++)
                for (int c = 0; c < a.Cols; c++)
                    if (Math.Abs(a[r, c] - b[r, c]) > tol) return false; // set tol if you want tolerance
            return true;
        }

        private static bool MatEqual(Emgu.CV.Matrix<int>? a, Emgu.CV.Matrix<int>? b)
        {
            if (a is null || b is null) return a is null && b is null;
            if (a.Rows != b.Rows || a.Cols != b.Cols) return false;
            for (int r = 0; r < a.Rows; r++)
                for (int c = 0; c < a.Cols; c++)
                    if (a[r, c] != b[r, c]) return false;
            return true;
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


        /// <summary>
        /// Clone this CalibrationCameraData object
        /// Uses a json serialization to create a deep copy of the object.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public CalibrationCameraData Clone()
        {
            var settings = new JsonSerializerSettings
            {
                Converters = [new MatrixJsonConverter()]
            };

            var json = JsonConvert.SerializeObject(this, settings);
            var clone = JsonConvert.DeserializeObject<CalibrationCameraData>(json, settings)
                        ?? throw new InvalidOperationException("Failed to clone CalibrationCameraData.");

            return clone;
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
            var hash = new HashCode();
            hash.Add(RMS);
            hash.Add(ImageTotal);
            hash.Add(ImageUseable);
            hash.Add(HashMatrix(Rotation));
            hash.Add(HashMatrix(Translation));
            return hash.ToHashCode();
        }

        private static int HashMatrix(Emgu.CV.Matrix<double>? m)
        {
            if (m is null) return 0;
            var h = new HashCode();
            h.Add(m.Rows);
            h.Add(m.Cols);
            for (int r = 0; r < m.Rows; r++)
                for (int c = 0; c < m.Cols; c++)
                    h.Add(m[r, c]);
            return h.ToHashCode();
        }

        private static int HashMatrix(Emgu.CV.Matrix<int>? m)
        {
            if (m is null) return 0;
            var h = new HashCode();
            h.Add(m.Rows);
            h.Add(m.Cols);
            for (int r = 0; r < m.Rows; r++)
                for (int c = 0; c < m.Cols; c++)
                    h.Add(m[r, c]);
            return h.ToHashCode();
        }
        public override bool Equals(object? obj)
        {
            if (obj is not CalibrationStereoCameraData other) return false;

            return RMS == other.RMS &&
                   ImageTotal == other.ImageTotal &&
                   ImageUseable == other.ImageUseable &&
                   MatEqual(Rotation, other.Rotation) &&
                   MatEqual(Translation, other.Translation);
        }

        private static bool MatEqual(Emgu.CV.Matrix<double>? a, Emgu.CV.Matrix<double>? b, double tol = 0.0)
        {
            if (a is null || b is null) return a is null && b is null;
            if (a.Rows != b.Rows || a.Cols != b.Cols) return false;
            for (int r = 0; r < a.Rows; r++)
                for (int c = 0; c < a.Cols; c++)
                    if (Math.Abs(a[r, c] - b[r, c]) > tol) return false; // set tol if you want tolerance
            return true;
        }

        private static bool MatEqual(Emgu.CV.Matrix<int>? a, Emgu.CV.Matrix<int>? b)
        {
            if (a is null || b is null) return a is null && b is null;
            if (a.Rows != b.Rows || a.Cols != b.Cols) return false;
            for (int r = 0; r < a.Rows; r++)
                for (int c = 0; c < a.Cols; c++)
                    if (a[r, c] != b[r, c]) return false;
            return true;
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


        /// <summary>
        /// Clone this CalibrationStereoCameraData object
        /// Uses a json serialization to create a deep copy of the object.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public CalibrationStereoCameraData Clone()
        {
            var settings = new JsonSerializerSettings
            {
                Converters = [new MatrixJsonConverter()]
            };

            var json = JsonConvert.SerializeObject(this, settings);
            var clone = JsonConvert.DeserializeObject<CalibrationStereoCameraData>(json, settings)
                        ?? throw new InvalidOperationException("Failed to clone CalibrationStereoCameraData.");

            return clone;
        }


        /// <summary>
        /// Calculate the Essential Matrix
        /// </summary>
        /// <param name="R"></param>
        /// <param name="T"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public Matrix<double>? ComputeEssentialMatrix()
        {
            Matrix<double>? essentialMatrix = null;

            if (Translation is not null && Rotation is not null)
            {
                // Create the skew-symmetric matrix for the translation vector
                Matrix<double> T_skew = new(3, 3);

                if (Translation.Rows == 3 && Translation.Cols == 1)
                {
                    T_skew[0, 0] = 0;
                    T_skew[0, 1] = -Translation[2, 0];
                    T_skew[0, 2] = Translation[1, 0];
                    T_skew[1, 0] = Translation[2, 0];
                    T_skew[1, 1] = 0;
                    T_skew[1, 2] = -Translation[0, 0];
                    T_skew[2, 0] = -Translation[1, 0];
                    T_skew[2, 1] = Translation[0, 0];
                    T_skew[2, 2] = 0;
                }
                else if (Translation.Rows == 1 && Translation.Cols == 3)
                {
                    T_skew[0, 0] = 0;
                    T_skew[0, 1] = -Translation[0, 2];
                    T_skew[0, 2] = Translation[0, 1];
                    T_skew[1, 0] = Translation[0, 2];
                    T_skew[1, 1] = 0;
                    T_skew[1, 2] = -Translation[0, 0];
                    T_skew[2, 0] = -Translation[0, 1];
                    T_skew[2, 1] = Translation[0, 0];
                    T_skew[2, 2] = 0;
                }
                else
                    throw new ArgumentException("The translation vector must be a 3x1 or 1x3 matrix.");

                // Compute the essential matrix
                essentialMatrix = T_skew * Rotation;
            }

            return essentialMatrix;
        }


        // ** End of CalibrationStereoCameraData **
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
            var stopwatch = Stopwatch.StartNew();

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
            finally
            {
                stopwatch.Stop();
                Debug.WriteLine($"CalibrationCameraData.LoadFromFile {fileSpec} Return code:{ret}, RMS={this.StereoCameraCalibration.RMS}, Elapsed time: {stopwatch.ElapsedMilliseconds} ms");
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
        /// Clone this CalibrationData object
        /// Uses json serialization to create a deep copy of the object.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public CalibrationData Clone()
        {
            var settings = new JsonSerializerSettings
            {
                Converters = [new MatrixJsonConverter(), new VectorJsonConverter()]
            };

            var json = JsonConvert.SerializeObject(this, settings);
            var clone = JsonConvert.DeserializeObject<CalibrationData>(json, settings)
                        ?? throw new InvalidOperationException("Failed to clone CalibrationData.");

            return clone;
        }


        /// <summary>
        /// Calcualte the FundamentalMatrix
        /// </summary>
        /// <param name="E"></param>
        /// <param name="K1"></param>
        /// <param name="K2"></param>
        /// <returns></returns>
        public Matrix<double>? ComputeFundamentalMatrix()
        {
            Matrix<double>? fundamentalMatrix = null;

            if (LeftCameraCalibration?.Intrinsic is not null &&
                RightCameraCalibration?.Intrinsic is not null)
            {
                Matrix<double> K1Inv = new(3, 3);
                Matrix<double> K2Inv = new(3, 3);

                // Invert the intrinsic matrices
                CvInvoke.Invert(LeftCameraCalibration.Intrinsic, K1Inv, DecompMethod.Svd);
                CvInvoke.Invert(RightCameraCalibration.Intrinsic, K2Inv, DecompMethod.Svd);

                // Compute the fundamental matrix
                Matrix<double>? essentialMatrix = StereoCameraCalibration.ComputeEssentialMatrix();
                if (essentialMatrix is not null)
                {
                    fundamentalMatrix = K2Inv.Transpose() * essentialMatrix * K1Inv;
                }
            }

            return fundamentalMatrix;
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

//#if DEBUG
//                if (calibrationData is not null)
//                {
//                    string Description = string.IsNullOrEmpty(calibrationData.Description) == false ? calibrationData.Description : "None";
//                    string GuidString = calibrationData.CalibrationID?.ToString() ?? "Emtry";

//                    Debug.WriteLine($"Description:{Description},  Guid:{GuidString}");
//                    Debug.WriteLine($"Left:");
//                    if (calibrationData.LeftCameraCalibration.Intrinsic is not null)
//                        Debug.WriteLine($"   Camera Matrix: {FormatMatrixToString(calibrationData.LeftCameraCalibration.Intrinsic, 18/*indent*/)}");
//                    if (calibrationData.LeftCameraCalibration.Distortion is not null)
//                        Debug.WriteLine($"   Distortion Coefficients: {FormatMatrixToString(calibrationData.LeftCameraCalibration.Distortion, 28/*indent*/)}");
//                    if (calibrationData.LeftCameraCalibration.ImageSize is not null)
//                        Debug.WriteLine($"   Image Size: {FormatMatrixToString(calibrationData.LeftCameraCalibration.ImageSize, 28/*indent*/)}");
//                    if (!string.IsNullOrEmpty(calibrationData.LeftCameraCalibration.CameraID))
//                        Debug.WriteLine($"   Camera ID: {calibrationData.LeftCameraCalibration.CameraID}");

//                    Debug.WriteLine($"Right:");
//                    if (calibrationData.RightCameraCalibration.Intrinsic is not null)
//                        Debug.WriteLine($"   Camera Matrix: {FormatMatrixToString(calibrationData.RightCameraCalibration.Intrinsic, 18/*indent*/)}");
//                    if (calibrationData.RightCameraCalibration.Distortion is not null)
//                        Debug.WriteLine($"   Distortion Coefficients: {FormatMatrixToString(calibrationData.RightCameraCalibration.Distortion, 28/*indent*/)}");
//                    if (calibrationData.RightCameraCalibration.ImageSize is not null)
//                        Debug.WriteLine($"   Image Size: {FormatMatrixToString(calibrationData.RightCameraCalibration.ImageSize, 28/*indent*/)}");
//                    if (!string.IsNullOrEmpty(calibrationData.RightCameraCalibration.CameraID))
//                        Debug.WriteLine($"   Camera ID: {calibrationData.RightCameraCalibration.CameraID}");

//                    Debug.WriteLine("Stereo:");
//                    Debug.WriteLine($"   RMS: {calibrationData.StereoCameraCalibration.RMS}");
//                    if (calibrationData.StereoCameraCalibration.Rotation is not null)
//                        Debug.WriteLine($"   Rotation: {FormatMatrixToString(calibrationData.StereoCameraCalibration.Rotation, 13/*indent*/)}");
//                    if (calibrationData.StereoCameraCalibration.Translation is not null)
//                        Debug.WriteLine($"   Translation: {FormatMatrixToString(calibrationData.StereoCameraCalibration.Translation, 16/*indent*/)}");
//                }
//#endif

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

                            // Focal Length
                            int? f_state = (int?)intrinsics["f"]?["state"];
                            double f = (f_state ?? 2) != 2 ? (double?)intrinsics["f"]?["val"] ?? 0.0 : 0.0;
                            // Sensor aspect ratio (normally square for GoPro and therefore not estimated)
                            double ar = (double?)intrinsics["ar"]?["val"] ?? 1.0;
                            // Principal Point X - X-coordinate of the principal point (usually near the image center), in pixels.
                            int? cx_state = (int?)intrinsics["cx"]?["state"];
                            double cx = (cx_state ?? 2) != 2 ? (double?)intrinsics["cx"]?["val"] ?? 0.0 : 0.0;
                            // Principal Point Y - Y-coordinate of the principal point (usually near the image center), in pixels.
                            int? cy_state = (int?)intrinsics["cy"]?["state"];
                            double cy = (cy_state ?? 2) != 2 ? (double?)intrinsics["cy"]?["val"] ?? 0.0 : 0.0;
                            // Radial Distortion Coefficient 1 - Corrects for barrel/pincushion distortion, primary term.
                            int? k1_state = (int?)intrinsics["k1"]?["state"];
                            double? k1 = (k1_state ?? 2) != 2 ? (double?)intrinsics["k1"]?["val"] ?? 0.0 : 0.0;
                            // Radial Distortion Coefficient 2 - Secondary radial term for stronger or complex distortion correction.
                            int? k2_state = (int?)intrinsics["k2"]?["state"];
                            double? k2 = (k2_state ?? 2) != 2 ? (double?)intrinsics["k2"]?["val"] ?? 0.0 : 0.0;
                            // Radial Distortion Coefficient 3 - Higher-order radial distortion term for strong lenses or wide FOV.
                            int? k3_state = (int?)intrinsics["k3"]?["state"];
                            double? k3 = (k3_state ?? 2) != 2 ? (double?)intrinsics["k3"]?["val"] ?? 0.0 : 0.0;
                            // Radial Distortion Coefficient 4 - Used in extended distortion models (rarely needed for standard lenses).
                            int? k4_state = (int?)intrinsics["k4"]?["state"];
                            double? k4 = (k4_state ?? 2) != 2 ? (double?)intrinsics["k4"]?["val"] ?? 0.0 : 0.0;
                            // Radial Distortion Coefficient 5 - Even higher-order radial term; supports very precise modeling.
                            int? k5_state = (int?)intrinsics["k5"]?["state"];
                            double? k5 = (k5_state ?? 2) != 2 ? (double?)intrinsics["k5"]?["val"] ?? 0.0 : 0.0;
                            // Radial Distortion Coefficient 6 - Highest-order radial distortion parameter (used when fine precision is needed).
                            int? k6_state = (int?)intrinsics["k6"]?["state"];
                            double? k6 = (k6_state ?? 2) != 2 ? (double?)intrinsics["k6"]?["val"] ?? 0.0 : 0.0;
                            // Tangential Distortion Coefficient 1 - Accounts for lens misalignment causing image to skew tangentially.
                            int? p1_state = (int?)intrinsics["p1"]?["state"];
                            double? p1 = (p1_state ?? 2) != 2 ? (double?)intrinsics["p1"]?["val"] ?? 0.0 : 0.0;
                            // Tangential Distortion Coefficient 2 - Works with p1 to correct asymmetric distortion.
                            int? p2_state = (int?)intrinsics["p2"]?["state"];
                            double? p2 = (p2_state ?? 2) != 2 ? (double?)intrinsics["p2"]?["val"] ?? 0.0 : 0.0;
                            // These coeeffients are not currently used
                            double? s1 = (double?)intrinsics["s1"]?["val"];
                            double? s2 = (double?)intrinsics["s2"]?["val"];
                            double? s3 = (double?)intrinsics["s3"]?["val"];
                            double? s4 = (double?)intrinsics["s4"]?["val"];
                            double? tauX = (double?)intrinsics["tauX"]?["val"];
                            double? tauY = (double?)intrinsics["tauY"]?["val"];

                            // Calculate the distortion matrix row count
                            int distortionRowCount;
                            if ((k4_state ?? 2) != 2 || (k5_state ?? 2) != 2 || (k6_state ?? 2) != 2)
                                distortionRowCount = 8;
                            else if ((k3_state ?? 2) != 2)
                                distortionRowCount = 5;
                            else
                                distortionRowCount = 4;

                            CalibrationCameraData cameraCalibrationData;

                            if (i == 0/*left camera*/)
                            {
                                cameraCalibrationData = LeftCameraCalibration;
                            }
                            else /*right camera*/
                            {
                                cameraCalibrationData = RightCameraCalibration;
                            }
                            
                            // Camera Matrix
                            cameraCalibrationData.Intrinsic = new Emgu.CV.Matrix<double>(3/*rows*/, 3/*cols*/);
                            cameraCalibrationData.Intrinsic[0, 0] = f;
                            cameraCalibrationData.Intrinsic[0, 1] = 0.0;
                            cameraCalibrationData.Intrinsic[0, 2] = cx;
                            cameraCalibrationData.Intrinsic[1, 0] = 0.0;
                            cameraCalibrationData.Intrinsic[1, 1] = f * ar;
                            cameraCalibrationData.Intrinsic[1, 2] = cy;
                            cameraCalibrationData.Intrinsic[2, 0] = 0.0;
                            cameraCalibrationData.Intrinsic[2, 1] = 0.0;
                            cameraCalibrationData.Intrinsic[2, 2] = 1.0;

                            // Right Distortion Coefficients either 4,5 or 8 elements [k1, k2, p1, p2, k3, k4, k5, k6]
                            cameraCalibrationData.Distortion = new Emgu.CV.Matrix<double>(1/*rows*/, distortionRowCount/*cols*/);
                            cameraCalibrationData.Distortion[0, 0] = k1 ?? 0.0;
                            cameraCalibrationData.Distortion[0, 1] = k2 ?? 0.0;
                            cameraCalibrationData.Distortion[0, 2] = p1 ?? 0.0;
                            cameraCalibrationData.Distortion[0, 3] = p2 ?? 0.0;
                            if (distortionRowCount > 4)
                            {
                                cameraCalibrationData.Distortion[0, 4] = k3 ?? 0.0;
                            }
                            if (distortionRowCount > 5)
                            {
                                cameraCalibrationData.Distortion[0, 5] = k4 ?? 0.0;
                                cameraCalibrationData.Distortion[0, 6] = k5 ?? 0.0;
                                cameraCalibrationData.Distortion[0, 7] = k6 ?? 0.0;
                            }


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
        public int SaveToJson(out string json, bool indented = false)
        {
            int ret = 0;

            // Reset
            json = "";

            // Add a unique Guid to identify this calibration set of data id missing
            this.CalibrationID ??= Guid.NewGuid();

            try
            {
                JsonSerializerSettings settings;

                if (indented)
                {
                    settings = new()
                    {
                        Formatting = Formatting.Indented, // For pretty-printing the JSON
                        Converters = new List<JsonConverter> { new MatrixJsonConverter(), new VectorJsonConverter() }
                    };
                }
                else
                {
                    settings = new()
                    {
                        Formatting = Formatting.None,
                        Converters = new List<JsonConverter> { new MatrixJsonConverter(), new VectorJsonConverter() }
                    };
                }

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
            Description = string.Empty;            
            LeftCameraCalibration.Clear();
            RightCameraCalibration.Clear();
            StereoCameraCalibration.Clear();
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(/*CalibrationID,*/
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
            if (ReferenceEquals(this, obj)) return true;
            if (ReferenceEquals(obj, null)) return false;

            if (obj is CalibrationData other)
            {
                // Only compare content
                return Compare(other);
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

        // ** End of CalibrationData
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

