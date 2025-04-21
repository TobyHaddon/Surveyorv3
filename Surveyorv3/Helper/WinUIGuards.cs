using Microsoft.UI.Dispatching;
using System;

namespace Surveyor.Helper
{
    internal class WinUIGuards
    {
        /// <summary>
        /// Use at the top of the function if that function is intended for use only on the 
        /// UI Thread. This is to prevent the function being called from a non-UI thread.
        /// </summary>
        static internal void CheckIsUIThread()
        {
#if DEBUG
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue == null || !dispatcherQueue.HasThreadAccess)
                throw new InvalidOperationException("This function must be called from the UI thread");
#endif
        }
    }
}
