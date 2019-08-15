using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace HueScreenAmbience
{
	/// <summary>
	/// This class shall keep the GDI32 APIs being used in our program.
	/// </summary>
	public class PlatformInvokeGDI32
	{
		#region Class Functions
		[DllImport("gdi32.dll", EntryPoint = "DeleteDC")]
		public static extern IntPtr DeleteDC(IntPtr hDc);

		[DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
		public static extern IntPtr DeleteObject(IntPtr hDc);

		[DllImport("gdi32.dll", EntryPoint = "BitBlt")]
		public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSource, int xSrc, int ySrc, CopyPixelOperation dwRop);

		[DllImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")]
		public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

		[DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
		public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

		[DllImport("gdi32.dll", EntryPoint = "SelectObject")]
		public static extern IntPtr SelectObject(IntPtr hdc, IntPtr bmp);
		#endregion
	}
}
