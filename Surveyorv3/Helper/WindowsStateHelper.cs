using Microsoft.UI.Windowing;
using Windows.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Surveyor.Helper
{
    /// <summary>
    /// Sets and restore the window position and size and which monitor
    /// </summary>
    public static class WindowStateHelper
    {
        // P/Invoke to get monitor information
        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        // P/Invoke to get window monitor
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        // Save window position and size
        public static void SaveWindowState(IntPtr hWnd, AppWindow appWindow)
        {
            // Find the monitor associated with the window
            IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);

            // Retrieve DPI scaling factors
            uint dpiX, dpiY;
            DPIHelper.GetDpiForMonitor(monitor, DPIHelper.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);

            // Convert window coordinates to DIPs
            var position = appWindow.Position;
            var size = appWindow.Size;
            int dipX = (int)(position.X * 96.0 / dpiX);
            int dipY = (int)(position.Y * 96.0 / dpiY);

            // Save values in local settings
            ApplicationData.Current.LocalSettings.Values["WindowPosX"] = dipX;
            ApplicationData.Current.LocalSettings.Values["WindowPosY"] = dipY;
            ApplicationData.Current.LocalSettings.Values["WindowWidth"] = (int)(size.Width * 96.0 / dpiX);
            ApplicationData.Current.LocalSettings.Values["WindowHeight"] = (int)(size.Height * 96.0 / dpiY);
        }

        // Restore window position and size
        public static void RestoreWindowState(IntPtr hWnd, AppWindow appWindow)
        {
            // Retrieve values from local settings
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("WindowPosX", out object? posX) &&
                ApplicationData.Current.LocalSettings.Values.TryGetValue("WindowPosY", out object? posY) &&
                ApplicationData.Current.LocalSettings.Values.TryGetValue("WindowWidth", out object? width) &&
                ApplicationData.Current.LocalSettings.Values.TryGetValue("WindowHeight", out object? height))
            {
                // Find the monitor associated with the window
                IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);

                // Retrieve DPI scaling factors
                uint dpiX, dpiY;
                DPIHelper.GetDpiForMonitor(monitor, DPIHelper.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);

                // Convert DIPs back to physical pixels
                int pixelX = (int)(Convert.ToInt32(posX) * dpiX / 96.0);
                int pixelY = (int)(Convert.ToInt32(posY) * dpiY / 96.0);
                int pixelWidth = (int)(Convert.ToInt32(width) * dpiX / 96.0);
                int pixelHeight = (int)(Convert.ToInt32(height) * dpiY / 96.0);

                // Restore window size and position using Win32 API
                SetWindowPos(hWnd, IntPtr.Zero, pixelX, pixelY, pixelWidth, pixelHeight, SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }

        // P/Invoke to set window position
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }

    public class DPIHelper
    {
        // Monitor DPI retrieval function from Windows API
        [DllImport("Shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        // Constants for DPI scaling
        public const int MDT_EFFECTIVE_DPI = 0;
    }

}
