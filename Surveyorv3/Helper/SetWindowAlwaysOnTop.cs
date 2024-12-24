using System;
using System.Runtime.InteropServices;


namespace Surveyor.Helper
{
	public static class WindowInteropHelper
	{
		private const int HWND_TOPMOST = -1;
		private const int HWND_NOTOPMOST = -2;
		private const int SWP_NOMOVE = 0x0002;
		private const int SWP_NOSIZE = 0x0001;
		private const int SWP_SHOWWINDOW = 0x0040;

		[DllImport("user32.dll", SetLastError = true)]
		public static extern bool SetWindowPos(
			IntPtr hWnd,
			IntPtr hWndInsertAfter,
			int X,
			int Y,
			int cx,
			int cy,
			uint uFlags
		);

		public static void SetWindowAlwaysOnTop(IntPtr windowHandle, bool topmost)
		{
			SetWindowPos(
				windowHandle,
				new IntPtr(topmost ? HWND_TOPMOST : HWND_NOTOPMOST),
				0,
				0,
				0,
				0,
				SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW
			);
		}
	}
}