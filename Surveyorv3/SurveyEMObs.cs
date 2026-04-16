// Surveyor Load EventMeasure .EMObs file into the Survey class
// 
// Version 1.0
// Created
// Version 1.1  27 Mar 2026
// Added more error checking and reporting around the media file loading and frame rate and duration extraction

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using EMObsReaderNameSpace;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Events;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using static Surveyor.User_Controls.SurveyorTesting;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace Surveyor
{
    public partial class Survey
    {
        // Allow tolerance when comparing frame rates as some video formats can
        // have frame rates that are not exactly the same but are close enough
        // to be considered the same for synchronization purposes. For example,
        // a video might have a frame rate of 29.97 fps instead of 30 fps,
        // which is common for NTSC video. In such cases, a small tolerance
        // can help avoid false positives when checking for consistent frame
        // rates across media files. 
        private const double fpsTolerance = 0.001; // ~1e-3 fps is typically enough

        private class MediaItemInfo
        {
            private string _filename = string.Empty;
            private double _fps = 0.0;
            private TimeSpan _duration = TimeSpan.Zero;
            private TimeSpan _durationPriorMP4s = TimeSpan.Zero;

            public MediaItemInfo()
            {
                Filename = "";
                Fps = 0.0;
                Duration = TimeSpan.Zero;
                TotalFrames = 0;
                DurationPriorMP4s = TimeSpan.Zero;
                TotalFramesPriorMP4s = 0;
            }

            public MediaItemInfo(string filename, double fps, TimeSpan duration, TimeSpan durationPriorMP4s)
            {
                Filename = filename;
                Fps = fps;
                Duration = duration;
                DurationPriorMP4s = durationPriorMP4s;
                TotalFramesPriorMP4s = (long)Math.Round(Fps * DurationPriorMP4s.TotalSeconds, MidpointRounding.AwayFromZero);
            }

            public string Filename
            {
                get => _filename;
                set => _filename = value ?? string.Empty;
            }

            public double Fps
            {
                get => _fps;
                set
                {
                    _fps = value;
                    RecalculateTotalFrames();
                }
            }

            public TimeSpan Duration
            {
                get => _duration;
                set
                {
                    _duration = value < TimeSpan.Zero ? TimeSpan.Zero : value;
                    RecalculateTotalFrames();
                }
            }

            public long TotalFrames { get; private set; }

            public TimeSpan DurationPriorMP4s
            {
                get => _durationPriorMP4s;
                set => _durationPriorMP4s = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            }

            public long TotalFramesPriorMP4s { get; set; }

            private void RecalculateTotalFrames()
            {
                if (_fps > 0.0 && _duration > TimeSpan.Zero)
                    TotalFrames = (long)Math.Round(_fps * _duration.TotalSeconds, MidpointRounding.AwayFromZero);
                else
                    TotalFrames = 0;
            }
        }

        /// <summary>
        /// Called from Survey.SurveyLoad() for reading an EMObs file.
        /// </summary>
        /// <param name="surveyFileSpec"></param>
        /// <returns></returns>
        public (int result, string errorMessage) SurveyLoadEMObs(string surveyFileSpec)
        {
            int ret = 0;

            // Reset
            string errorMessages = "";

            // Create an instance of the managed wrapper class
            EMObsReaderCLR obj = new(surveyFileSpec);

            // Call DoSomething and get the list of OutputRow
            List<OutputRow> outputRows = obj.Process();
            Report?.Info("", $"EMObs file processed, {outputRows.Count} rows extracted from the .EMObs file");

            // Get the period information
            List<PeriodRow> periodRows = obj.GetPeriodRows();
            Report?.Info("", $"EMObs, {periodRows.Count} period rows");

            // Get the media information
            List<MediaInfoRow> mediaInfoRows = obj.GetMediaInfoRows();
            Report?.Info("", $"EMObs, {mediaInfoRows.Count} media info rows");

            // Get the calibration information
            List<CalibrationRow> calibrationRows = obj.GetCalibrationRows();
            Report?.Info("", $"EMObs, {calibrationRows.Count} calibration rows");

            // Get the frame rate and check for consistency across the media files. 
            // Note. Error reporting done inside the GetAndCheckFrameRate method
            double mediafps = GetAndCheckFrameRate(mediaInfoRows);

            // Get the synchronization offset and check for consistency 
            // Note. Error reporting done inside the GetAndCheckmediaOffsetFrames method
            long mediaOffsetFrames = GetAndCheckMediaFrameOffset(outputRows, mediaInfoRows);

            if (mediafps > 0 && mediaOffsetFrames != -1)
            {
                // Build the Survey.InfoClass
                Data.Info.SurveyType = SurveyType.StereoFish;
                Data.Info.SurveyDepth = string.Empty;
                Data.Info.SurveyFileName = System.IO.Path.GetFileName(surveyFileSpec);
                Data.Info.SurveyPath = System.IO.Path.GetDirectoryName(surveyFileSpec);
                Data.Info.SurveyCode = System.IO.Path.GetFileNameWithoutExtension(surveyFileSpec);

                // Build the MediaInfoItems
                if (outputRows.Count > 0)
                    Data.Media.MediaPath = Path.GetDirectoryName(outputRows[0].Path);
                Data.Media.LeftMediaFileNames = MakeMediaItemInfo(mediaInfoRows, trueLeftFalseRight: true);
                Data.Media.RightMediaFileNames = MakeMediaItemInfo(mediaInfoRows, trueLeftFalseRight: false);

                // Build the synchronization info
                Data.Sync.IsSynchronized = true;
                Data.Sync.TimeSpanOffset = TimeSpan.FromMilliseconds(1000.0 * mediaOffsetFrames / mediafps);

                // Build the calibration info. 
                CalibrationData calibrationData = MakeCalibrationData(surveyFileSpec, calibrationRows);
                Data.Calibration.AllowMultipleCalibrationData = false;
                Data.Calibration.PreferredCalibrationDataIndex = 0;
                Data.Calibration.CalibrationDataList.Add(calibrationData);
                

                // EVENTS Section

                // Add the Survey Start/Stop info
                ret = AddSurveyStartAndEndInfo(periodRows, mediaInfoRows, mediafps, mediaOffsetFrames);

                // Add the measurement, 3D point and 2D point info
                if (ret == 0)
                    ret = AddSurveyMeasurement3DAnd2DInfo(outputRows, mediaInfoRows, mediafps, mediaOffsetFrames, Data.Calibration.CalibrationDataList[0].CalibrationID);

            }
            else
            {
                if (!(mediafps > 0))
                {
                    if (errorMessages != "")
                        errorMessages += "\n";
                    errorMessages += $"Media frame has not been established. This is normal found in the media section of the EMObs {surveyFileSpec} in CMS>MSI";
                }
                if (!(mediaOffsetFrames != -1))
                {
                    if (errorMessages != "")
                        errorMessages += "\n";
                    errorMessages += $"Media synchronization offset has not been established. This is normal found by looking at the frame offsets of measurement or 3D points in the media section of the EMObs {surveyFileSpec}";
                }

                ret = -1;
            }

            return (ret, errorMessages);
        }


        /// <summary>
        /// Parse media info rows and check the frame rates are consistent across the media files. 
        /// Return the established frame rate if consistent or -1 if not consistent. 
        /// </summary>
        /// <param name="mediaInfoRows"></param>
        /// <returns></returns>
        private double GetAndCheckFrameRate(List<MediaInfoRow> mediaInfoRows)
        {
            double frameRate = -1.0;
            bool? mediafpsConsistent = null;

            // Get a distinct of frame rates
            var distinctFrameRates = mediaInfoRows
                    .Select(x => x.FrameRate)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

            if (distinctFrameRates.Count == 0)
            {
                Report?.Warning("", $"No media info rows found in the .EMObs file, can't determine the media frame rate");
            }
            else if (distinctFrameRates.Count > 1)
            {
                string mediafpsFirstFile = string.Empty;

                foreach (MediaInfoRow mediaInfoRow in mediaInfoRows)
                {
                    if (mediafpsConsistent is null)
                    {
                        frameRate = mediaInfoRow.FrameRate;
                        mediafpsFirstFile = mediaInfoRow.MediaFile;
                        mediafpsConsistent = true;
                    }
                    else if (Math.Abs(mediaInfoRow.FrameRate - frameRate) > fpsTolerance)
                    {
                        Report?.Warning("", $"Media fps differ, {mediaInfoRow.MediaFile} has {mediaInfoRow.FrameRate:F3} which is different to {mediafpsFirstFile} with {frameRate:F3}");
                        mediafpsConsistent = false;
                    }
                }
            }
            else if (distinctFrameRates.Count == 1)
            {
                mediafpsConsistent = true;
                frameRate = mediaInfoRows[0].FrameRate;
            }

            if (mediafpsConsistent is not null && !(bool)mediafpsConsistent)
                frameRate = -1.0;

            return frameRate;
        }


        /// <summary>
        /// Extract a list of media file names for either the left or 
        /// right camera from the media info rows.
        /// </summary>
        /// <param name="mediaInfoRows"></param>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns></returns>
        private ObservableCollection<string> MakeMediaItemInfo(List<MediaInfoRow> mediaInfoRows, bool trueLeftFalseRight)
        {
            ObservableCollection<string> MediaFileList = [];

            foreach (MediaInfoRow mediaInfoRow in mediaInfoRows)
            {
                if (mediaInfoRow.TrueLeftFalseRightCamera == trueLeftFalseRight)
                {
                    MediaFileList.Add(Path.GetFileName(mediaInfoRow.MediaFile));
                }
            }

            return MediaFileList;
        }


        /// <summary>
        /// Parse the stereo rows (Measurements and 3D) and get the media
        /// frame offset and check for consistency
        /// </summary>
        /// <param name="outputRows"></param>
        /// <returns></returns>
        private long GetAndCheckMediaFrameOffset(List <OutputRow> outputRows, List<MediaInfoRow> mediaInfoRows)
        {
            long mediaFrameOffset = -1;
            bool? mediaOffsetConsistent = null;
            int mediaOffsetFirstFoundRow = -1;
            int mediaOffsetFirstFoundFrameL;
            int mediaOffsetFirstFoundFrameR;
            string mediaOffsetFirstFoundFileL = string.Empty;
            string mediaOffsetFirstFoundFileR = string.Empty;

            // To allow a absolute frame offset to be calculated wen need to create
            // a left and right array holding the cumulative frames counts.
            int[] totalFramesPriorMP4s = new int[mediaInfoRows.Count];
            for (int i = 0; i < mediaInfoRows.Count; i++)
            {
                totalFramesPriorMP4s[i] = mediaInfoRows
                                .Where(m => m.TrueLeftFalseRightCamera == mediaInfoRows[i].TrueLeftFalseRightCamera
                                         && m.row < mediaInfoRows[i].row)
                                .Sum(m => m.FrameCount);
            }

            foreach (OutputRow item in outputRows)
            {
                int row = item.row;
                RowTypeManaged rowType = item.rowType;

                if (rowType == RowTypeManaged.RowTypeMeasurementPoint3D ||
                    rowType == RowTypeManaged.RowTypePoint3D)
                {
                    MediaInfoRow? mediaInfoRowL = mediaInfoRows.Find(i =>
                        string.Equals(i.MediaFile, item.FileL, StringComparison.OrdinalIgnoreCase) &&
                        i.TrueLeftFalseRightCamera == true);

                    MediaInfoRow? mediaInfoRowR = mediaInfoRows.Find(i =>
                        string.Equals(i.MediaFile, item.FileR, StringComparison.OrdinalIgnoreCase) &&
                        i.TrueLeftFalseRightCamera == false);

                    if (mediaInfoRowL is not null && mediaInfoRowR is not null)
                    {
                        // Calculate the absolute frame offset
                        long absFrameL = totalFramesPriorMP4s[mediaInfoRowL.row] + item.FrameL;
                        long absFrameR = totalFramesPriorMP4s[mediaInfoRowR.row] + item.FrameR;
                        long absFrameOffset = absFrameR - absFrameL;

                        if (mediaOffsetConsistent is null)
                        {
                            mediaOffsetFirstFoundRow = item.row;
                            
                            mediaFrameOffset = absFrameOffset;
                            mediaOffsetFirstFoundFrameL = item.FrameL;
                            mediaOffsetFirstFoundFrameR = item.FrameR;

                            mediaOffsetFirstFoundFileL = item.FileL;
                            mediaOffsetFirstFoundFileR = item.FileR;
                            mediaOffsetConsistent = true;
                        }
                        else
                        {
                            // Is the frame offset consistent with the first found frame offset?
                            if (mediaFrameOffset != absFrameOffset)
                            {
                                Report?.Warning("", $"Media offsets differ, files {item.FileL} & {item.FileR} offset={absFrameOffset} are different to {mediaOffsetFirstFoundFileL} & {mediaOffsetFirstFoundFileR} where the offset = {mediaFrameOffset}");
                                mediaOffsetConsistent = false;
                                break;
                            }
                        }
                    }
                }
            }

            if (mediaOffsetConsistent is not null && !(bool)mediaOffsetConsistent)
                mediaFrameOffset = -1;


            return mediaFrameOffset;
        }


        /// <summary>
        /// Try to build a CalibrationData instance from the calibration rows. 
        /// Note. This is a bit of a work in progress as I need to understand 
        /// better the calibration information that is in the .EMObs file and 
        /// how this maps to the CalibrationData class.
        /// </summary>
        /// <param name="surveyFileSpec"></param>
        /// <param name="calibrationRows"></param>
        /// <returns></returns>
        private CalibrationData MakeCalibrationData(string surveyFileSpec, List <CalibrationRow> calibrationRows)
        {
            CalibrationData calibrationData = new();

            if (calibrationRows.Count == 2)
            {
                // Element zero is always left and element one is always right
                CalibrationRow calibrationRowLeft = calibrationRows[0];
                CalibrationRow calibrationRowRight = calibrationRows[1];

                calibrationData.Description = $"Extracted from EMObs {surveyFileSpec}";
                calibrationData.CalibrationID = Guid.NewGuid();

                calibrationData.LeftCameraCalibration = BuildCameraCalibration(calibrationRowLeft, "LeftFromEMObs");
                calibrationData.RightCameraCalibration = BuildCameraCalibration(calibrationRowRight, "RightFromEMObs");
                calibrationData.StereoCameraCalibration = BuildStereoCalibration(calibrationRowLeft, calibrationRowRight);
            }
            else
            {
                Report?.Warning("", $"The EMObs file {surveyFileSpec} contains {calibrationRows.Count} calibration records, two records are required one for left and one for the right camera.");
            }

            return calibrationData;
        }

        // !!!This is untested code
        private static CalibrationCameraData BuildCameraCalibration(CalibrationRow row, string cameraId)
        {
            CalibrationCameraData cam = new()
            {
                CameraID = cameraId,
                RMS = 0.0,
                ProjectionRMS = 0.0,
                MaxError = 0.0,
                ImageTotal = 0,
                ImagesUsed = 0,
                // Image size [width,height] in your project
                ImageSize = new Emgu.CV.Matrix<int>(1, 2)
            };
            cam.ImageSize[0, 0] = row.FrameWidth;
            cam.ImageSize[0, 1] = row.FrameHeight;

            // Intrinsic matrix K (pixels)
            // Assumption: PPOffset is in mm from image center.
            double fx = row.FocalLength / row.XPixelSize;
            double fy = row.FocalLength / row.YPixelSize;
            double cx = (row.FrameWidth * 0.5) + (row.XPPOffset / row.XPixelSize);
            double cy = (row.FrameHeight * 0.5) + (row.YPPOffset / row.YPixelSize);

            cam.Intrinsic = new Emgu.CV.Matrix<double>(3, 3);
            cam.Intrinsic[0, 0] = fx; cam.Intrinsic[0, 1] = 0.0; cam.Intrinsic[0, 2] = cx;
            cam.Intrinsic[1, 0] = 0.0; cam.Intrinsic[1, 1] = fy; cam.Intrinsic[1, 2] = cy;
            cam.Intrinsic[2, 0] = 0.0; cam.Intrinsic[2, 1] = 0.0; cam.Intrinsic[2, 2] = 1.0;

            // Distortion vector D in OpenCV order [k1,k2,p1,p2,k3]
            // Best-effort mapping from EMObs terms (k3,k5,k7,p1,p2).
            double f = row.FocalLength; // mm

            double k1 = row.K3RadialDistortion * f * f;
            double k2 = row.K5RadialDistortion * Math.Pow(f, 4);
            double k3 = row.K7RadialDistortion * Math.Pow(f, 6);
            double p1 = row.P1DecenteringDistortion * f;
            double p2 = row.P2DecenteringDistortion * f;

            cam.Distortion = new Matrix<double>(1, 5);
            cam.Distortion[0, 0] = k1;
            cam.Distortion[0, 1] = k2;
            cam.Distortion[0, 2] = p1;
            cam.Distortion[0, 3] = p2;
            cam.Distortion[0, 4] = k3;

            return cam;
        }

        // !!!This is untested code
        private static CalibrationStereoCameraData BuildStereoCalibration(CalibrationRow left, CalibrationRow right)
        {
            const double mmToM = 0.001;

            CalibrationStereoCameraData stereo = new()
            {
                RMS = 0.0,
                ProjectionRMS = 0.0,
                MaxError = 0.0,
                ImageTotal = 0,
                ImagesUsed = 0
            };

            // Rotation matrices from omega/phi/kappa (degrees)
            Emgu.CV.Matrix<double> rLeft = BuildRotationFromOmegaPhiKappa(left.Omega, left.Phi, left.Kappa);
            Emgu.CV.Matrix<double> rRight = BuildRotationFromOmegaPhiKappa(right.Omega, right.Phi, right.Kappa);

            // Relative rotation Right <- Left
            Emgu.CV.Matrix<double> rRel = rRight * rLeft.Transpose();
            stereo.Rotation = rRel;

            // Camera centers (convert mm -> m)
            Matrix<double> cLeft = new(3, 1);
            cLeft[0, 0] = left.CameraX * mmToM;
            cLeft[1, 0] = left.CameraY * mmToM;
            cLeft[2, 0] = left.CameraZ * mmToM;

            Matrix<double> cRight = new(3, 1);
            cRight[0, 0] = right.CameraX * mmToM;
            cRight[1, 0] = right.CameraY * mmToM;
            cRight[2, 0] = right.CameraZ * mmToM;

            // Relative translation Right <- Left: T = R_right * (C_left - C_right)
            Emgu.CV.Matrix<double> dC = new(3, 1);
            dC[0, 0] = cLeft[0, 0] - cRight[0, 0];
            dC[1, 0] = cLeft[1, 0] - cRight[1, 0];
            dC[2, 0] = cLeft[2, 0] - cRight[2, 0];

            stereo.Translation = rRight * dC;

            return stereo;
        }

        // !!!This is untested code
        private static Emgu.CV.Matrix<double> BuildRotationFromOmegaPhiKappa(double omegaDeg, double phiDeg, double kappaDeg)
        {
            double o = omegaDeg * Math.PI / 180.0;
            double p = phiDeg * Math.PI / 180.0;
            double k = kappaDeg * Math.PI / 180.0;

            // Rx(omega), Ry(phi), Rz(kappa)
            Emgu.CV.Matrix<double> rx = new(3, 3);
            rx[0, 0] = 1; rx[0, 1] = 0; rx[0, 2] = 0;
            rx[1, 0] = 0; rx[1, 1] = Math.Cos(o); rx[1, 2] = -Math.Sin(o);
            rx[2, 0] = 0; rx[2, 1] = Math.Sin(o); rx[2, 2] = Math.Cos(o);

            Emgu.CV.Matrix<double> ry = new(3, 3);
            ry[0, 0] = Math.Cos(p); ry[0, 1] = 0; ry[0, 2] = Math.Sin(p);
            ry[1, 0] = 0; ry[1, 1] = 1; ry[1, 2] = 0;
            ry[2, 0] = -Math.Sin(p); ry[2, 1] = 0; ry[2, 2] = Math.Cos(p);

            Emgu.CV.Matrix<double> rz = new(3, 3);
            rz[0, 0] = Math.Cos(k); rz[0, 1] = -Math.Sin(k); rz[0, 2] = 0;
            rz[1, 0] = Math.Sin(k); rz[1, 1] = Math.Cos(k); rz[1, 2] = 0;
            rz[2, 0] = 0; rz[2, 1] = 0; rz[2, 2] = 1;

            // Conventional OPK order
            return rz * ry * rx;
        }


        /// <summary>
        /// Convert the EMObs period info into the Survey class SurveyStart and SurveyEnd events. 
        /// </summary>
        /// <param name="outputRows"></param>
        /// <param name="mediafps"></param>
        /// <param name="mediaOffsetFrames"></param>
        /// <returns></returns>
        private int AddSurveyStartAndEndInfo(List <PeriodRow> periodRows, List<MediaInfoRow> mediaInfoRows, double mediafps, long mediaOffsetFrames)
        {
            int ret = 0;

            foreach(PeriodRow periodRow in periodRows)
            {
                Event? eventItemStart = new (SurveyDataType.SurveyStart);
                Event? eventItemEnd = new (SurveyDataType.SurveyEnd);

                // Build the SurveyStart
                TransectMarker transectMarkerStart = new ();
                eventItemStart.EventData = transectMarkerStart;
                transectMarkerStart.MarkerName = periodRow.PeriodName;

                // Period always appear to use the left side
                LoadEventPosition(eventItemStart, trueLeftFalseRight: true, periodRow.MediaFile, periodRow.StartFrame, mediaInfoRows, mediafps, mediaOffsetFrames);

                // Build the SurveyEnd
                TransectMarker transectMarkerEnd = new ();
                eventItemEnd.EventData = transectMarkerEnd;
                transectMarkerEnd.MarkerName = periodRow.PeriodName;

                // Period always appear to use the left side
                LoadEventPosition(eventItemEnd, trueLeftFalseRight: true, periodRow.MediaFile, periodRow.EndFrame, mediaInfoRows, mediafps, mediaOffsetFrames);

                if (eventItemStart.TimeSpanTimelineController != TimeSpan.Zero)
                    this.Data.Events.EventList.Add(eventItemStart);
                else
                {
                    Report?.Warning("", $"Survey start event at row {periodRow.row} has a zero TimeSpanTimelineController, media file {periodRow.MediaFile}, frame {periodRow.StartFrame}");
                    ret = 1;
                }

                if (eventItemEnd.TimeSpanTimelineController != TimeSpan.Zero)
                    this.Data.Events.EventList.Add(eventItemEnd);
                else
                {
                    Report?.Warning("", $"Survey end event at row {periodRow.row} has a zero TimeSpanTimelineController, media file {periodRow.MediaFile}, frame {periodRow.EndFrame}");
                    ret = 1;
                }
            }
            return ret;
        }


        /// <summary>
        /// Convert the EMObs measurement, 3D point and 2D point info into the 
        /// Survey class SurveyMeasurementPoints, SurveyStereoPoints and SurveyPoints events.
        /// </summary>
        /// <param name="outputRows"></param>
        /// <param name="mediafps"></param>
        /// <param name="mediaOffsetFrames"></param>
        /// <returns></returns>
        private int AddSurveyMeasurement3DAnd2DInfo(List<OutputRow> outputRows, List<MediaInfoRow> mediaInfoRows, double mediafps, long mediaOffsetFrames, Guid? calibrationID)
        {
            int ret = 0;

            Event? eventItem;

            foreach (var item in outputRows)
            {
                eventItem = null;

                switch (item.rowType)
                {
                    case RowTypeManaged.RowTypeMeasurementPoint3D:
                        eventItem = new Event(SurveyDataType.SurveyMeasurementPoints);
                        eventItem.SetData(SurveyDataType.SurveyMeasurementPoints);
                        SurveyMeasurement surveyMeasurement = (SurveyMeasurement)eventItem.EventData!;
                        surveyMeasurement.CalibrationID = calibrationID;
                        surveyMeasurement.Measurement/*fish length*/ = item.Length;
                        surveyMeasurement.LeftXA = item.PointLX1;
                        surveyMeasurement.LeftYA = item.PointLY1;
                        surveyMeasurement.LeftXB = item.PointLX2;
                        surveyMeasurement.LeftYB = item.PointLY2;
                        surveyMeasurement.RightXA = item.PointRX1;
                        surveyMeasurement.RightYA = item.PointRY1;
                        surveyMeasurement.RightXB = item.PointRX2;
                        surveyMeasurement.RightYB = item.PointRY2;
                        LoadSpeciesInfo(item, surveyMeasurement.SpeciesInfo);
                        break;

                    case RowTypeManaged.RowTypePoint3D:
                        eventItem = new Event();
                        eventItem.SetData(SurveyDataType.SurveyStereoPoint);
                        SurveyStereoPoint surveyStereoPoint = (SurveyStereoPoint)eventItem.EventData!;
                        surveyStereoPoint.CalibrationID = calibrationID;
                        surveyStereoPoint.LeftX = item.PointLX1;
                        surveyStereoPoint.LeftY = item.PointLY1;
                        surveyStereoPoint.RightX = item.PointRX1;
                        surveyStereoPoint.RightY = item.PointRY1;
                        LoadSpeciesInfo(item, surveyStereoPoint.SpeciesInfo);
                        break;

                    case RowTypeManaged.RowTypePoint2DLeftCamera:
                        eventItem = new Event();
                        {
                            eventItem.SetData(SurveyDataType.SurveyPoint);
                            SurveyPoint surveyPoint = (SurveyPoint)eventItem.EventData!;
                            surveyPoint.TrueLeftFalseRight = true;/*left camera*/
                            surveyPoint.X = item.PointLX1;
                            surveyPoint.Y = item.PointLY1;
                            LoadSpeciesInfo(item, surveyPoint.SpeciesInfo);

                            item.FrameR = item.FrameL + (int)mediaOffsetFrames;
                        }
                        break;

                    case RowTypeManaged.RowTypePoint2DRightCamera:
                        eventItem = new Event();
                        {
                            eventItem.SetData(SurveyDataType.SurveyPoint);
                            SurveyPoint surveyPoint = (SurveyPoint)eventItem.EventData!;
                            surveyPoint.TrueLeftFalseRight = false;/*right camera*/
                            surveyPoint.X = item.PointRX1;
                            surveyPoint.Y = item.PointRY1;
                            LoadSpeciesInfo(item, surveyPoint.SpeciesInfo);

                            item.FrameL = item.FrameR - (int)mediaOffsetFrames;
                        }
                        break;
                }

                if (eventItem != null)
                {
                    LoadEventPosition(eventItem, item.FileL, item.FileR, item.FrameL, item.FrameR, mediaInfoRows, mediafps, mediaOffsetFrames);
                   
                    if (eventItem.TimeSpanTimelineController != TimeSpan.Zero)
                        this.Data.Events.EventList.Add(eventItem);
                    else
                    {
                        Report?.Warning("", $"Event at row {item.row} has a zero TimeSpanTimelineController, media files {item.FileL} & {item.FileR}, frames {item.FrameL} & {item.FrameR}");
                        ret = 1;
                    }
                }
            }

            return ret;
        }


        /// <summary>
        /// Convert the media file name to a media index that can be used to link the 
        /// Survey class events to the media files.
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="mediaInfoRows"></param>
        /// <param name="mediaFileName"></param>
        /// <returns></returns>
        private int GetMediaIndexFromFileName(bool trueLeftFalseRight, string mediaFileName)
        {
            int rowIndex;

            if (trueLeftFalseRight)
            {
                rowIndex = Data.Media.LeftMediaFileNames
                                .Select((value, i) => new { value, i })
                                .FirstOrDefault(x => string.Equals(x.value, mediaFileName, StringComparison.OrdinalIgnoreCase))
                                ?.i ?? -1;
            }
            else
            {
                rowIndex = Data.Media.RightMediaFileNames
                                .Select((value, i) => new { value, i })
                                .FirstOrDefault(x => string.Equals(x.value, mediaFileName, StringComparison.OrdinalIgnoreCase))
                                ?.i ?? -1;
            }

            return rowIndex;
        }


        /// <summary>
        /// Using the media file name and frame index complete the Event position
        /// information
        /// </summary>
        /// <param name="eventItem"></param>
        /// <param name="mediaFileNameLeft"></param>
        /// <param name="mediaFileNameRight"></param>
        /// <param name="frameLeft"></param>
        /// <param name="frameRight"></param>
        /// <param name="mediaInfoRows"></param>
        /// <param name="mediafps"></param>
        /// <param name="mediaOffsetFrames"></param>
        /// <returns></returns>
        private int LoadEventPosition(Event eventItem, string mediaFileNameLeft, string mediaFileNameRight, int frameLeft, int frameRight, List<MediaInfoRow> mediaInfoRows, double mediafps, long mediaOffsetFrames)
        {
            int ret = 0;

            if (eventItem is not null)
            {
                eventItem.DateTimeCreate = DateTime.Now;

                // Left Side
                if (!string.IsNullOrEmpty(mediaFileNameLeft))
                    eventItem.MediaLeftIndex = GetMediaIndexFromFileName(trueLeftFalseRight: true, mediaFileNameLeft);
                else
                    eventItem.MediaLeftIndex = -1;

                // Load left frame index
                eventItem.FrameIndexLeft = frameLeft;
                eventItem.FrameIndexRight = frameRight;

                // Right Side
                if (!string.IsNullOrEmpty(mediaFileNameRight))
                    eventItem.MediaRightIndex = GetMediaIndexFromFileName(trueLeftFalseRight: false, mediaFileNameRight);
                else
                    eventItem.MediaRightIndex = -1;

                // Load right frame index
                eventItem.FrameIndexRight = frameRight;
                eventItem.FrameIndexLeft = frameLeft;


                // Calculate time span frame positions
                eventItem.TimeSpanLeftFrame = TimeSpan.FromMicroseconds((double)((eventItem.FrameIndexLeft) * 1000000.0 / mediafps));
                eventItem.TimeSpanRightFrame = TimeSpan.FromMicroseconds((double)((eventItem.FrameIndexRight) * 1000000.0 / mediafps));

                if (mediaOffsetFrames > 0)
                    // Positive offset means right media started before left.
                    // This means the TimeLine controller will adopt the left
                    // side position
                    eventItem.TimeSpanTimelineController = eventItem.TimeSpanLeftFrame;
                else
                    // Minus offset means left media started before right.
                    // This means the TimeLine controller will adopt the right
                    // side position
                    eventItem.TimeSpanTimelineController = eventItem.TimeSpanRightFrame;
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="eventItem"></param>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="mediaFileName"></param>
        /// <param name="frame"></param>
        /// <param name="mediaInfoRows"></param>
        /// <param name="mediafps"></param>
        /// <param name="mediaOffsetFrames"></param>
        /// <returns></returns>
        private int LoadEventPosition(Event eventItem, bool trueLeftFalseRight, string mediaFileName, int frame, List<MediaInfoRow> mediaInfoRows, double mediafps, long mediaOffsetFrames)
        {
            string mediaFileNameLeft = string.Empty;
            string mediaFileNameRight = string.Empty;
            int frameLeft;
            int frameRight;

            if (trueLeftFalseRight)
            {
                mediaFileNameLeft = mediaFileName;
                frameLeft = frame;
                frameRight = frame + (int)mediaOffsetFrames;
            }
            else
            {
                mediaFileNameRight = mediaFileName;
                frameRight = frame;
                frameLeft = frame - (int)mediaOffsetFrames;
            }

            return LoadEventPosition(eventItem, mediaFileNameLeft, mediaFileNameRight, frameLeft, frameRight, mediaInfoRows, mediafps, mediaOffsetFrames);
        }


        /// <summary>
        /// Take the species information with the OutputRaw class and load it into the SpeciesInfo class.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        private static int LoadSpeciesInfo(OutputRow item, SpeciesInfo speciesInfo)
        {
            int ret = 0;

            speciesInfo.Family = item.Family;
            speciesInfo.Genus = item.Genus;
            speciesInfo.Species = item.Species;
            speciesInfo.Code = "";
            speciesInfo.Number = item.Count.ToString();
            speciesInfo.Stage = "";
            speciesInfo.Activity = "";
            speciesInfo.Comment = "";

            return ret;
        }     
    }
}
