// Surveyor Load EventMeasure .EMObs file into the Project class
// 
// Version 1.0
// Created

using Surveyor.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using EMObsReaderNameSpace;



namespace Surveyor
{
    public partial class Project
    {
        private class MediaItemInfo
        {
            public MediaItemInfo()
            {
                Filename = "";
                Fps = 0.0;
                Duration = TimeSpan.Zero;
                TotalFrames = 0;
                DurationPriorMP4s = TimeSpan.Zero;
                TotalFramesPriorMP4s = 0;
            }
            public MediaItemInfo(string _filename, double _fps, TimeSpan _duration, TimeSpan _durationPriorMP4s)
            {
                Filename = _filename;
                Fps = _fps;
                Duration = _duration;                
                TotalFrames = (long)((_fps * _duration.TotalMilliseconds) / 1000.0);
                DurationPriorMP4s = _durationPriorMP4s;
                TotalFramesPriorMP4s = (long)((_fps * _durationPriorMP4s.TotalMilliseconds) / 1000.0);
            }

            public string Filename { get; set; }  // Make this public to access it from outside
            
            public double Fps { get; set; }
            public TimeSpan Duration { get; set; }
            public long TotalFrames { get; }
            public TimeSpan DurationPriorMP4s { get; set; }
            public long TotalFramesPriorMP4s { get; }

        }

        /// <summary>
        /// Called from Project.ProjectLoad() for reading an EMObs file.
        /// </summary>
        /// <param name="projectFileSpec"></param>
        /// <returns></returns>
        public async Task<(int result, string errorMessage)> ProjectLoadEMObs(string projectFileSpec)
        {
            int ret = 0;

            // Reset
            string errorMessages = "";


            // Create an instance of the managed wrapper class
            EMObsReaderCLR obj = new EMObsReaderCLR(projectFileSpec);

            // Call DoSomething and get the list of OutputRow
            List<OutputRow> outputRows = obj.Process();

            // Iterate over the event data
            bool? singleMediaPath = null;
            string mediaPath = "";
            List<MediaItemInfo> leftMediaFiles = new();
            List<MediaItemInfo> rightMediaFiles = new();
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
                    {
                        var (fps, duration) = await GetVideoFpsAndDurationAsync(mediaPath, item.FileL);
                        long totalFrames = (long)((fps * duration.TotalMilliseconds) / 1000.0);
                        leftMediaFiles.Add(new MediaItemInfo(item.FileL, fps, duration, durationPriorMP4sLeft));
                        durationPriorMP4sLeft += duration;
                    }
                }
                if (!string.IsNullOrEmpty(item.FileR))
                {
                    if (!rightMediaFiles.Any(i => i.Filename == item.FileR))
                    {
                        var (fps, duration) = await GetVideoFpsAndDurationAsync(mediaPath, item.FileR);
                        long totalFrames = (long)((fps * duration.TotalMilliseconds) / 1000.0);
                        rightMediaFiles.Add(new MediaItemInfo(item.FileR, fps, duration, durationPriorMP4sRight));
                        durationPriorMP4sRight += duration;
                    }
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
                    else if (mii.Fps != mediafps)
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

                // Load the Project class
                // Info instance
                this.Data.Info.ProjectFileName = System.IO.Path.GetFileName(projectFileSpec);
                this.Data.Info.ProjectPath = System.IO.Path.GetDirectoryName(projectFileSpec);
                this.Data.Media.MediaPath = mediaPath;
                this.Data.Media.LeftMediaFileNames = new ObservableCollection<string>(leftMediaFiles.Select(item => item.Filename));
                this.Data.Media.RightMediaFileNames = new ObservableCollection<string>(rightMediaFiles.Select(item => item.Filename));
                this.Data.Sync.TimeSpanOffset = mediaOffsetDuration;

                // Flag the left and right movie as synchronzied
                if (mediaOffsetDuration != TimeSpan.Zero)
                    this.Data.Sync.IsSynchronized = true;

                Event? eventItem;

                foreach (var item in outputRows)
                {
                    eventItem = null;

                    switch (item.rowType)
                    {
                        case RowTypeManaged.RowTypeMeasurementPoint3D:
                            eventItem = new Event(DataType.SurveyMeasurementPoints);
                            eventItem.SetData(DataType.SurveyMeasurementPoints);
                            SurveyMeasurement surveyMeasurement = (SurveyMeasurement)eventItem.EventData!;                            
                            surveyMeasurement.Distance/*fish length*/ = item.Length;
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
                            eventItem.SetData(DataType.SurveyStereoPoint);
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
                                eventItem.SetData(DataType.SurveyPoint);
                                SurveyPoint surveyPoint = (SurveyPoint)eventItem.EventData!;
                                surveyPoint.trueLeftfalseRight = true;/*left camera*/
                                surveyPoint.X = item.PointLX1;
                                surveyPoint.Y = item.PointLY1;
                                LoadSpeciesInfo(item, surveyPoint.SpeciesInfo);
                            }
                            break;
                        case RowTypeManaged.RowTypePoint2DRightCamera:
                            eventItem = new Event();
                            {
                                eventItem.SetData(DataType.SurveyPoint);
                                SurveyPoint surveyPoint = (SurveyPoint)eventItem.EventData!;
                                surveyPoint.trueLeftfalseRight = false;/*right camera*/
                                surveyPoint.X = item.PointRX1;
                                surveyPoint.Y = item.PointRY1;
                                LoadSpeciesInfo(item, surveyPoint.SpeciesInfo);
                            }
                            break;
                    }

                    if (eventItem != null)
                    {
                        long? absFrameL = null;
                        long? absFrameR = null;

                        MediaItemInfo ? mediaItemInfoL = leftMediaFiles.Find(i => i.Filename == item.FileL);
                        MediaItemInfo? mediaItemInfoR = rightMediaFiles.Find(i => i.Filename == item.FileR);

                        if (mediaItemInfoL is not null)
                            absFrameL = mediaItemInfoL.TotalFramesPriorMP4s + item.FrameL;
                                                
                        if (mediaItemInfoR is not null)
                            absFrameR = mediaItemInfoR.TotalFramesPriorMP4s + item.FrameR;
 
                        if (absFrameL is null && absFrameR is not null)
                            absFrameL = absFrameR - mediaOffsetFrames;

                        if (absFrameR is null && absFrameL is not null)
                            absFrameR = absFrameL + mediaOffsetFrames;


                        if (absFrameL is not null && absFrameR is not null)
                        {
                            eventItem.TimeSpanLeftFrame = TimeSpan.FromMicroseconds((double)((absFrameL) * 1000000.0 / mediafps));
                            eventItem.TimeSpanRightFrame = TimeSpan.FromMicroseconds((double)((absFrameR) * 1000000.0 / mediafps));
                            if (mediaOffsetFrames > 0)
                                eventItem.TimeSpanTimelineController = eventItem.TimeSpanRightFrame;
                            else
                                eventItem.TimeSpanTimelineController = eventItem.TimeSpanLeftFrame;
                        }

                        this.Data.Events.EventList.Add(eventItem);
                    }
                }
            }

            return (ret, errorMessages);
        }


        /// <summary>
        /// Take the species information with the OutputRaw class and load it into the SpeciesInfo class.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        static int LoadSpeciesInfo(OutputRow item, SpeciesInfo speciesInfo)
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
