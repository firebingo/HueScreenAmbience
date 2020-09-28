using System;
using System.Drawing;

namespace HueScreenAmbience
{
	/// <summary>
	/// This class shall keep all the functionality for capturing 
	/// the desktop.
	/// http://www.codeguru.com/csharp/csharp/cs_graphics/screencaptures/article.php/c6139/Capturing-the-Screen-Image-Using-C.htm
	/// </summary>
	public class CaptureScreen
	{
		#region Public Class Functions
		public static Bitmap GetDesktopImage(int width, int height)
		{
			//In size variable we shall keep the size of the screen.
			//SIZE size;

			//Variable to keep the handle to bitmap.
			IntPtr hBitmap;

			//Get a handle to the desktop
			IntPtr dskHandle = PlatformInvokeUSER32.GetDesktopWindow();

			//Here we get the handle to the desktop device context.
			IntPtr hDC = PlatformInvokeUSER32.GetWindowDC(dskHandle);

			//Here we make a compatible device context in memory for screen device context.
			IntPtr hMemDC = PlatformInvokeGDI32.CreateCompatibleDC(hDC);

			//We pass SM_CXSCREEN constant to GetSystemMetrics to get the X coordinates of screen.
			//size.cx = PlatformInvokeUSER32.GetSystemMetrics(PlatformInvokeUSER32.SM_CXSCREEN);

			//We pass SM_CYSCREEN constant to GetSystemMetrics to get the Y coordinates of screen.
			//size.cy = PlatformInvokeUSER32.GetSystemMetrics(PlatformInvokeUSER32.SM_CYSCREEN);

			//We create a compatible bitmap of screen size using screen device context.
			hBitmap = PlatformInvokeGDI32.CreateCompatibleBitmap(hDC, width, height);

			//As hBitmap is IntPtr we can not check it against null. For this purspose IntPtr.Zero is used.
			if (hBitmap != IntPtr.Zero)
			{
				//Here we select the compatible bitmap in memeory device context and keeps the refrence to Old bitmap.
				IntPtr hOld = (IntPtr)PlatformInvokeGDI32.SelectObject(hMemDC, hBitmap);
				//We copy the Bitmap to the memory device context.
				PlatformInvokeGDI32.BitBlt(hMemDC, 0, 0, width, height, hDC, 0, 0, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
				//We select the old bitmap back to the memory device context.
				PlatformInvokeGDI32.SelectObject(hMemDC, hOld);
				//We delete the memory device context.
				PlatformInvokeGDI32.DeleteDC(hMemDC);
				//We release the screen device context.
				PlatformInvokeUSER32.ReleaseDC(dskHandle, hDC);
				//Image is created by Image bitmap handle and stored in local variable.
				Bitmap bmp = Image.FromHbitmap(hBitmap);
				if (bmp.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb && bmp.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppRgb)
					throw new Exception("Pixel format is not 32bppArgb or Format32bppRgb");
				//Release the memory for compatible bitmap.
				PlatformInvokeGDI32.DeleteObject(hBitmap);
				//Return the bitmap 
				return bmp;
			}

			//If hBitmap is null return null.
			return null;
		}

		public static Bitmap ResizeImage(Bitmap bmp, int width, int height)
		{
			Bitmap result = new Bitmap(width, height);
			using (Graphics g = Graphics.FromImage(result))
			{
				g.DrawImage(bmp, 0, 0, width, height);
			}

			return result;
		}
		#endregion
	}
}
