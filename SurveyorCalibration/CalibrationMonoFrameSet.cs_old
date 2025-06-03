using Emgu.CV;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Surveyor.Calibration
{
    public class CalibrationMonoFrameSet : CalibrationStereoFrameSet
    {
        public bool SetupMedia(VideoCapture _Capture)
        {
            return base.SetupMedia(_Capture, null);
        }

        public override void SetupLockFrameIndexes(int left, int right)
        {
            throw new NotSupportedException("Mono calibration does not support lock frame indexes.");
        }

        public override (int startFrameLeft, int startFrameRight) GetStartIndexes()
        {
            throw new NotSupportedException("Use GetStartIndex() in mono calibration.");
        }

        public int GetStartIndex()
        {
            (int startFrameLeft, _) = base.GetStartIndexes();

            return startFrameLeft; 
        }

        public override (int frameLeft, int frameRight) GetIndexes(int targetIndex)
        {
            throw new NotSupportedException("Use GetIndex(int targetIndex) in mono calibration.");
        }

        public int GetIndex(int targetIndex)
        {
            (int frameLeft, _) = GetIndexes(targetIndex);

            return frameLeft;
        }

        public override void AddFrame(int stereoFrameIndex, FrameCalibrationTarget frame, FrameCalibrationTarget? frameRight)
        {
            base.AddFrame(stereoFrameIndex, frame, null);
        }

        public void ReportOnLargeValues(bool suppressValues)
        {
            base.ReportOnLargeValues(true/*trueLeftrightFalse*/, suppressValues);
        }

        public Dictionary<(int gx, int gy, int binx, int biny), int> GetBinCounts(int gx, int gy)
        {
            return base.GetBinCounts(true/*trueLeftFalseRight*/, gx, gy);
        }


    }
}
