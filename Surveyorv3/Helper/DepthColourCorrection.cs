using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Surveyor.Helper
{
    public static class DepthColourCorrection
    {
        /// <summary>
        /// Returns the Red,Gree,Blue gain values to do colour correction at the indicated depth
        /// </summary>
        /// <param name="depthMeters"></param>
        /// <returns></returns>
        public static Matrix5x4 GetUnderwaterColorMatrix(uint depthMeters)
        {
            // Clamp between 5 and 18 meters
            depthMeters = Math.Clamp(depthMeters, 5, 18);

            // Red gain increases with depth, blue decreases
            float redGain = 1.2f + 0.08f * (depthMeters - 5);   // From 1.2 to ~2.24
            float greenGain = 1.0f - 0.004f * (depthMeters - 5); // From 1.0 to ~0.948
            float blueGain = 0.85f - 0.015f * (depthMeters - 5); // From 0.85 to ~0.655

            return new Matrix5x4
            {
                M11 = blueGain,
                M22 = greenGain,
                M33 = redGain,
                M44 = 1.0f // Alpha channel unchanged
            };
        }
    }

}
