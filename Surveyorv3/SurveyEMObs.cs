// Surveyor Load EventMeasure .EMObs file into the Survey class
// 
// Version 1.0
// Created
// Version 1.1  27 Mar 2026
// Added more error checking and reporting around the media file loading and frame rate and duration extraction

using EMObsReaderNameSpace;
using Surveyor.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using static Surveyor.User_Controls.SurveyorTesting;


namespace Surveyor
{
    public partial class Survey
    {
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
        public async Task<(int result, string errorMessage)> SurveyLoadEMObsAsync(string surveyFileSpec)
        {
            int ret = 0;

            // Allow tolerance when comparing frame rates as some video formats can
            // have frame rates that are not exactly the same but are close enough
            // to be considered the same for synchronization purposes. For example,
            // a video might have a frame rate of 29.97 fps instead of 30 fps,
            // which is common for NTSC video. In such cases, a small tolerance
            // can help avoid false positives when checking for consistent frame
            // rates across media files. 
            const double fpsTolerance = 0.001; // ~1e-3 fps is typically enough

            // Reset
            string errorMessages = "";


            // Create an instance of the managed wrapper class
            EMObsReaderCLR obj = new EMObsReaderCLR(surveyFileSpec);

            // Call DoSomething and get the list of OutputRow
            List<OutputRow> outputRows = obj.Process();

            // Iterate over the event data
            bool? singleMediaPath = null;
            string mediaPath = "";
            List<MediaItemInfo> leftMediaFiles = [];
            List<MediaItemInfo> rightMediaFiles = [];
            bool? mediaOffsetConsistent = null;
            int mediaOffsetFirstFoundRow = 0;
            TimeSpan mediaOffsetDuration = new(0);
            long mediaOffsetFrames = 0;
            string mediaOffsetFirstFoundFileL = "";
            string mediaOffsetFirstFoundFileR = "";
            long mediaOffsetFirstFoundFrameL = 0;
            long mediaOffsetFirstFoundFrameR = 0;
            bool? mediafpsConsistent = null;
            double mediafps = 0.0;
            string mediafpsFirstFile = "";
            TimeSpan durationPriorMP4sLeft = TimeSpan.Zero;
            TimeSpan durationPriorMP4sRight = TimeSpan.Zero;

            foreach (var item in outputRows)
            {
                // Check there is only one media path
                if (item.Path is not null)
                {
                    if (singleMediaPath is null)
                    {
                        mediaPath = item.Path;
                        singleMediaPath = true;
                    }
                    else if (mediaPath != item.Path)
                    {
                        if (errorMessages != "")
                            errorMessages += "\n";
                        errorMessages += $"Multiple media paths found {mediaPath} and {item.Path}";
                        ret = 1;
                        singleMediaPath = false;
                        break;
                    }
                }

                // Build a list of left and right media files
                if (!string.IsNullOrEmpty(item.FileL))
                {
                    if (!leftMediaFiles.Any(i => i.Filename == item.FileL))
                        leftMediaFiles.Add(new MediaItemInfo(item.FileL, -1.0, TimeSpan.Zero, TimeSpan.Zero));
                }
                if (!string.IsNullOrEmpty(item.FileR))
                {
                    if (!rightMediaFiles.Any(i => i.Filename == item.FileR))
                        rightMediaFiles.Add(new MediaItemInfo(item.FileR, -1.0, TimeSpan.Zero, TimeSpan.Zero));
                }
            }

            // Next check the media files are all present. If not prompt the user for a new media path and then re-check
            bool allMediaFound = CheckForMediaFiles(mediaPath, leftMediaFiles, rightMediaFiles, out string errorMessage);

            if (!allMediaFound)
            {
                // Next try look for the media in the survey file path
                // Note Post field trip the path from the survey file to the media files
                // is rarely correct
                string? surveyPath = Path.GetDirectoryName(surveyFileSpec);
                if (surveyPath is not null)
                {
                    mediaPath = (string)surveyPath;

                    allMediaFound = CheckForMediaFiles(mediaPath, leftMediaFiles, rightMediaFiles, out errorMessage);
                    if (!allMediaFound)
                    {
                        Report?.Warning("", $"EMObs media is missing, {errorMessage}");
                        ret = -2;
                    }
                }
                else
                {
                    Report?.Warning("", $"Can't extract the survey path and therefore can't look for missing media paths in the survey path");
                    ret = -1;
                }
            }

            // For each media file get the frame rate and the total frames
            if (ret == 0 && (singleMediaPath is not null && singleMediaPath == true))
            {
                // From the properties get the frame rate and the total frames. Note. Error reporting done inside
                ret = await PopulateFrameRateAndDurationAsync(mediaPath, leftMediaFiles);
                if (ret == 0)
                {
                    ret = await PopulateFrameRateAndDurationAsync(mediaPath, rightMediaFiles);
                }
            }

            // Check all the videos have the same fps rate
            if (ret == 0 && (singleMediaPath is not null && singleMediaPath == true))
            {
                foreach (MediaItemInfo mii in leftMediaFiles)
                {
                    if (mediafpsConsistent is null)
                    {
                        mediafps = mii.Fps;
                        mediafpsFirstFile = mii.Filename;
                        mediafpsConsistent = true;
                    }
                    // Do a tolerance check rather than exact equality as some video formats can have frame rates
                    // that are not exactly the same 
                    else if (Math.Abs(mii.Fps - mediafps) > fpsTolerance)
                    {
                        if (errorMessages != "")
                            errorMessages += "\n";
                        errorMessages += $"Left media fps differ, {mii.Filename} is different to {mediafpsFirstFile} in media directory {mediaPath}";
                        ret = 1;
                        mediafpsConsistent = false;
                    }
                }
                foreach (MediaItemInfo mii in rightMediaFiles)
                {
                    if (mediafpsConsistent is null)
                    {
                        mediafps = mii.Fps;
                        mediafpsFirstFile = mii.Filename;
                        mediafpsConsistent = true;
                    }
                    else if (mii.Fps != mediafps)
                    {
                        if (errorMessages != "")
                            errorMessages += "\n";
                        errorMessages += $"Right media fps differ, {mii.Filename} is different to {mediafpsFirstFile} in media directory {mediaPath}";
                        ret = 1;
                        mediafpsConsistent = false;
                    }
                }
            }

            // Loop through each object extracted from the .EMObs file
            foreach (OutputRow item in outputRows)
            {

                // Check the media frame offset is consistent
                if (ret == 0 && 
                    (singleMediaPath is not null && singleMediaPath == true) &&
                    (mediafpsConsistent is not null && mediafpsConsistent == true))
                {
                    int row = item.row;
                    RowTypeManaged rowType = item.rowType;

                    if (rowType == RowTypeManaged.RowTypeMeasurementPoint3D ||
                        rowType == RowTypeManaged.RowTypePoint3D)
                    {
                        MediaItemInfo? mediaItemInfoL = leftMediaFiles.Find(i => i.Filename == item.FileL);
                        MediaItemInfo? mediaItemInfoR = rightMediaFiles.Find(i => i.Filename == item.FileR);

                        if (mediaItemInfoL is not null && mediaItemInfoR is not null)
                        {
                            // Approach 1
                            TimeSpan timeSpanFullOffsetL = mediaItemInfoL.DurationPriorMP4s.Add(TimeSpan.FromMicroseconds(((double)item.FrameL * 1000000.0) / mediafps));
                            TimeSpan timeSpanFullOffsetR = mediaItemInfoR.DurationPriorMP4s.Add(TimeSpan.FromMicroseconds(((double)item.FrameR * 1000000.0) / mediafps));

                            TimeSpan timeOffsetFull = timeSpanFullOffsetR - timeSpanFullOffsetL;

                            // Approach 2
                            long absFrameL = mediaItemInfoL.TotalFramesPriorMP4s + item.FrameL;
                            long absFrameR = mediaItemInfoR.TotalFramesPriorMP4s + item.FrameR;
                            long absFrameOffset = absFrameR - absFrameL;
                            TimeSpan timeOffsetAbs = TimeSpan.FromMilliseconds(1000.0 * absFrameOffset / mediafps);

                            if (mediaOffsetConsistent is null)
                            {
                                mediaOffsetFirstFoundRow = item.row;
                                mediaOffsetDuration = timeOffsetFull;
                                mediaOffsetFrames = absFrameOffset;
                                mediaOffsetFirstFoundFrameL = item.FrameL;
                                mediaOffsetFirstFoundFrameR = item.FrameR;

                                mediaOffsetFirstFoundFileL = item.FileL;
                                mediaOffsetFirstFoundFileR = item.FileR;
                                mediaOffsetConsistent = true;
                            }
                            else 
                            {
                                TimeSpan difference = mediaOffsetDuration - timeOffsetFull;

                                if (/*Math.Abs(difference.TotalMilliseconds) > 1*/ mediaOffsetFrames != absFrameOffset)
                                {
                                    if (errorMessages != "")
                                        errorMessages += "\n";
                                    errorMessages += $"Media offsets differ, files {item.FileL} & {item.FileR} offset = {mediaOffsetDuration} are different to {mediaOffsetFirstFoundFileL} & {mediaOffsetFirstFoundFileR} where the offset = {timeOffsetFull}";
                                    ret = 1;
                                    mediaOffsetConsistent = false;
                                    break;
                                }
                            }
                        }
                    }
                }
            }



            if (ret == 0 &&
                (singleMediaPath is not null && singleMediaPath == true) &&
                (mediaOffsetConsistent is not null && mediaOffsetConsistent == true) &&
                (mediafpsConsistent is not null && mediafpsConsistent == true))
            {

                // Load the Survey class
                // Info instance
                Data.Info.SurveyType = SurveyType.StereoFish;
                Data.Info.SurveyFileName = System.IO.Path.GetFileName(surveyFileSpec);
                Data.Info.SurveyPath = System.IO.Path.GetDirectoryName(surveyFileSpec);
                Data.Media.MediaPath = mediaPath;
                Data.Media.LeftMediaFileNames = new ObservableCollection<string>(leftMediaFiles.Select(item => item.Filename));
                Data.Media.RightMediaFileNames = new ObservableCollection<string>(rightMediaFiles.Select(item => item.Filename));
                Data.Sync.TimeSpanOffset = mediaOffsetDuration;
                
                // Flag the left and right movie as synchronized
                if (mediaOffsetDuration != TimeSpan.Zero)
                    this.Data.Sync.IsSynchronized = true;

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

                                // Fix the right frame index as I will be 0
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

                                // Fix the left frame index as I will be 0
                                item.FrameL = item.FrameR - (int)mediaOffsetFrames;
                            }
                            break;
                    }

                    if (eventItem != null)
                    {
                        MediaItemInfo? mediaItemInfoL = leftMediaFiles.Find(i => i.Filename == item.FileL);
                        MediaItemInfo? mediaItemInfoR = rightMediaFiles.Find(i => i.Filename == item.FileR);

                        eventItem.TimeSpanLeftFrame = TimeSpan.FromMicroseconds((double)((item.FrameL) * 1000000.0 / mediafps));
                        eventItem.TimeSpanRightFrame = TimeSpan.FromMicroseconds((double)((item.FrameR) * 1000000.0 / mediafps));
                         

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

                        if (eventItem.TimeSpanTimelineController != TimeSpan.Zero)
                            this.Data.Events.EventList.Add(eventItem);
                        else
                        {
                            if (errorMessages != "")
                                errorMessages += "\n";
                            errorMessages += $"Event at row {item.row} has a zero TimeSpanTimelineController, media files {item.FileL} & {item.FileR}, frames {item.FrameL} & {item.FrameR}, in media directory {mediaPath}";
                            ret = 1;
                        }
                    }
                }
            }

            return (ret, errorMessages);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="mediaPath">Path to all the media files</param>
        /// <param name="leftMediaFiles">List of left media files</param>
        /// <param name="rightMediaFiles">List of right media files</param>
        /// <param name="errorMessage"></param>
        /// <returns></returns>
        private static bool CheckForMediaFiles(string mediaPath, List<MediaItemInfo> leftMediaFiles, List<MediaItemInfo> rightMediaFiles, out string errorMessage)
        {
            // Reset
            errorMessage = string.Empty;

            List<string> errors = [];

            bool leftOk = _CheckFiles(mediaPath, leftMediaFiles, "Left", errors);
            bool rightOk = _CheckFiles(mediaPath, rightMediaFiles, "Right", errors);

            errorMessage = string.Join("\n", errors);
            return leftOk && rightOk;

            static bool _CheckFiles(string mediaPath, List<MediaItemInfo> mediaFiles, string side, List<string> errors)
            {
                bool allOk = true;

                if (string.IsNullOrWhiteSpace(mediaPath))
                {
                    errors.Add($"{side} media path is blank.");
                    return false;
                }

                if (!System.IO.Directory.Exists(mediaPath))
                {
                    errors.Add($"{side} media path does not exist: {mediaPath}");
                    return false;
                }

                foreach (MediaItemInfo mediaItem in mediaFiles)
                {
                    if (string.IsNullOrWhiteSpace(mediaItem.Filename))
                    {
                        errors.Add($"{side} media list contains a blank filename.");
                        allOk = false;
                        continue;
                    }

                    string fileSpec = System.IO.Path.Combine(mediaPath, mediaItem.Filename);

                    if (!System.IO.File.Exists(fileSpec))
                    {
                        errors.Add($"{side} media file missing: {fileSpec}");
                        allOk = false;
                        continue;
                    }

                    long length = 0;
                    try
                    {
                        length = new System.IO.FileInfo(fileSpec).Length;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{side} media file not readable: {fileSpec}, {ex.Message}");
                        allOk = false;
                        continue;
                    }

                    if (length <= 0)
                    {
                        errors.Add($"{side} media file is empty (0 bytes): {fileSpec}");
                        allOk = false;
                    }
                }

                return allOk;
            }
        }


        /// <summary>
        /// Asynchronously populates the frame rate and total frame count information for the specified media files.
        /// </summary>
        /// <param name="mediaPath">The file system path to the media file or directory containing media files to analyze. Cannot be null or
        /// empty.</param>
        /// <param name="mediaFiles">A list of MediaItemInfo objects representing the media files to update with frame rate and total frame
        /// information. Cannot be null.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the number of media files
        /// successfully updated.</returns>
        private async Task<int> PopulateFrameRateAndDurationAsync(string mediaPath, List<MediaItemInfo> mediaFiles)
        {
            int ret = 0;
            TimeSpan durationPriorMP4s = TimeSpan.Zero;

            foreach (MediaItemInfo mediaItemInfo in mediaFiles)
            {
                try
                {
                    (double fps, TimeSpan duration) = await GetVideoFpsAndDurationAsync(mediaPath, mediaItemInfo.Filename);

                    long totalFrames = (long)((fps * duration.TotalMilliseconds) / 1000.0);
                    
                    // Log the duration and frame rate
                    mediaItemInfo.Duration = duration;                    
                    mediaItemInfo.Fps = fps;
                    mediaItemInfo.DurationPriorMP4s = durationPriorMP4s;

                    // Calculate for the next media file
                    durationPriorMP4s += mediaItemInfo.Duration;
                }
                catch (Exception ex)
                {
                    Report?.Warning("", $"Failed to get frame rate and total frames for:{Path.Combine(mediaPath, mediaItemInfo.Filename)}, {ex.Message}");
                    // Continue on (don't break out) so we catch an other problem files
                    ret = -1;
                }
            }

            return ret;
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

      

        public static async Task<(double fps, TimeSpan duration)> GetVideoFpsAndDurationAsync(string path, string file)
        {
            double fps = 0.0;
            TimeSpan duration = TimeSpan.Zero;

            // Combine the path and file to get the full file path
            string fullFilePath = System.IO.Path.Combine(path, file);

            // Open the video file using Windows.Storage
            StorageFile videoFile = await StorageFile.GetFileFromPathAsync(fullFilePath);

            // Create a MediaClip from the video file
            MediaClip mediaClip = await MediaClip.CreateFromFileAsync(videoFile);

            // Get the video encoding properties
            VideoEncodingProperties properties = mediaClip.GetVideoEncodingProperties();

            // Get FPS
            if (properties != null)
            {
                uint frameRateNumerator = properties.FrameRate.Numerator;
                uint frameRateDenominator = properties.FrameRate.Denominator;

                if (frameRateDenominator != 0)
                {
                    fps = (double)frameRateNumerator / frameRateDenominator;
                }
            }

            // Get the duration
            duration = mediaClip.OriginalDuration;

            // Return both FPS and Duration as a tuple
            return (fps, duration);
        }
    }
}
