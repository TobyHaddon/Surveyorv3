using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Surveyor.CalibProject.DataClass.CalibrationResultClass;

namespace Surveyor
{
    public record struct IterationResult(double MovementMinThreshold,
                                         double BlurMinThreshold,
                                         int MonoCornersMinThreshold,
                                         int BestFramesCount,
                                         CalibrationParameters CalibrationParameters,
                                         double ReprojectionRMS, double MaxError, double P95Error,
                                         MonoCalibrationQuality? MonoCalibrationQuality,
                                         StereoCalibrationQuality? StereoCalibrationQuality,
                                         int BestFramesListHash);

    public class IterationResultList
    {
        public List<IterationResult> Results { get; } = [];


        /// <summary>
        /// find the best result from the iteration results list.
        /// The best result is the one with the lowest reprojection RMS and max error
        /// </summary>
        /// <returns></returns>
        public IterationResult GetBestResult()
        {
            IterationResult iterationResult;

            // Guard
            if (Results.Count == 0)
                return new IterationResult(0, 0, 0, 0, CalibrationParameters.K1K2P1P2, double.MaxValue, double.MaxValue, double.MaxValue, MonoCalibrationQuality.Terrible, StereoCalibrationQuality.Terrible, 0);

            // LINQ query to find the best result with the lowest reprojection RMS and max error
            iterationResult = Results
                .OrderBy(r => r.MonoCalibrationQuality ?? MonoCalibrationQuality.Unknown)
                .ThenBy(r => r.ReprojectionRMS)
                .ThenBy(r => r.P95Error)
                .FirstOrDefault();

            return iterationResult;
        }


        /// <summary>
        /// Method to determine if a result is strictly worse than the best-so-far 
        /// </summary>
        /// <param name="candidate"></param>
        /// <param name="reference"></param>
        /// <returns></returns>
        public static bool IsWorseThan(IterationResult candidate, IterationResult reference)
        {
            MonoCalibrationQuality candidateQuality = candidate.MonoCalibrationQuality ?? MonoCalibrationQuality.Unknown;
            MonoCalibrationQuality referenceQuality = reference.MonoCalibrationQuality ?? MonoCalibrationQuality.Unknown;

            if (candidateQuality > referenceQuality)
                return true;

            if (candidateQuality < referenceQuality)
                return false;

            // Same quality bucket – compare reprojection and P95
            if (candidate.ReprojectionRMS > reference.ReprojectionRMS)
                return true;

            if (candidate.ReprojectionRMS < reference.ReprojectionRMS)
                return false;

            return candidate.P95Error > reference.P95Error;
        }


        /// <summary>
        /// Determine if the results are trending worse. We can look at the last few results
        /// and if they are trending worse then we can stop iterating. This is to prevent us
        /// from iterating for a long time and not finding a good result. We can define what
        /// we mean by trending worse as we go along but it could be something like if the
        /// last 10 results are worse than the best result found so far then we can stop iterating.
        /// </summary>
        /// <param name="iterationResultList"></param>
        /// <returns></returns>
        public bool AreResultingTrendingWorse()
        {
            // Guard – need a reasonable history before we start applying this heuristic
            const int minHistory = 24;   // require more history before we trust trend
            const int windowSize = 15;   // look at the last 12 results
            const int minWorseCount = 10; // at least 10 of those must be worse

            if (Results.Count < minHistory)
                return false;

            // Find the best result so far using the same ordering as GetBestResult
            IterationResult bestSoFar = GetBestResult();

            int count = Results.Count;
            int take = Math.Min(windowSize, count);
            IReadOnlyList<IterationResult> recent = [.. Results
                .Skip(count - take)
                .Take(take)];

            int worseCount = 0;
            int betterCount = 0;

            foreach (var r in recent)
            {
                if (IsWorseThan(r, bestSoFar))
                    worseCount++;
                else if (IsWorseThan(bestSoFar, r))
                    betterCount++;
                // else treated as "equal" – neither worse nor better
            }

            // Trending worse if:
            //  - we have no improvements in the recent window
            //  - AND the majority of recent results are worse than the best-so-far
            return betterCount == 0 && worseCount >= minWorseCount;
        }
    }
}
