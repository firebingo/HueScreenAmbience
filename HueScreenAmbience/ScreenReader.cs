using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HueScreenAmbience
{
	class ScreenReader
	{
		private const Int32 MONITOR_DEFAULTTOPRIMERTY = 0x00000001;
		private const Int32 MONITOR_DEFAULTTONEAREST = 0x00000002;
		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr GetDesktopWindow();
		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern uint GetPixel(IntPtr dc, int x, int y);
		[DllImport("user32.dll")]
		private static extern IntPtr MonitorFromWindow(IntPtr handle, Int32 flags);
		[DllImport("user32.dll")]
		private static extern Boolean GetMonitorInfo(IntPtr hMonitor, MonitorInfo lpmi);

		private Point[] _pixelsToRead = null;
		private MonitorInfo _screen = null;
		public ScreenDimensions ScreenInfo { get; private set; }
		private Config _onfig;
		private Core _core;
		public bool IsRunning { get; private set; } = false;
		public double AverageDt = 0;
		private readonly int _thres = 15;

		private readonly object _lockPixelsObj = new object();
		private readonly int _frameRate = 7;

		public void Start()
		{
			GetScreenInfo();
			_pixelsToRead = new Point[0];
			PreparePixelsToGet();
		}

		public void InstallServices(IServiceProvider _map)
		{
			_onfig = _map.GetService(typeof(Config)) as Config;
			_core = _map.GetService(typeof(Core)) as Core;
		}

		public void GetScreenInfo()
		{
			var desk = GetDesktopWindow();
			var monitor = MonitorFromWindow(desk, MONITOR_DEFAULTTONEAREST);
			if (monitor != IntPtr.Zero)
			{
				var monitorInfo = new MonitorInfo();
				GetMonitorInfo(monitor, monitorInfo);
				_screen = monitorInfo;
				ScreenInfo = new ScreenDimensions();
				ScreenInfo.left = monitorInfo.Monitor.Left;
				ScreenInfo.top = monitorInfo.Monitor.Top;
				ScreenInfo.width = (monitorInfo.Monitor.Right - monitorInfo.Monitor.Left);
				ScreenInfo.height = (monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top);
			}
		}

		public void PreparePixelsToGet()
		{
			lock (_lockPixelsObj)
			{
				var pixelsToGet = new ConcurrentBag<Point>();
				GC.Collect();
				var r = new Random();

				Parallel.For(0, _onfig.Model.pixelCount, index =>
				{
					var p = new Point(r.Next(0, ScreenInfo.width), r.Next(0, ScreenInfo.height));
					pixelsToGet.Add(p);
				});
				//looping over an array is noticeably faster than a list at this scale
				_pixelsToRead = pixelsToGet.Distinct().ToArray();
			}
		}

		public void ReadScreenLoop()
		{
			IsRunning = true;
			Color lastColor = Color.FromArgb(255, 255, 255);
			do
			{
				var start = DateTime.UtcNow;
				Color avg = new Color();
				using (var bmp = CaptureScreen.GetDesktopImage(ScreenInfo.width, ScreenInfo.height))
				{
					BitmapData srcData = bmp.LockBits(
					new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
					ImageLockMode.ReadOnly,
					PixelFormat.Format32bppArgb);

					int stride = srcData.Stride;

					IntPtr Scan0 = srcData.Scan0;

					long[] totals = new long[] { 0, 0, 0 };

					var t1 = DateTime.UtcNow;
					Console.WriteLine($"Build Bitmap Time: {(t1 - start).TotalMilliseconds}");

					var totalSize = _pixelsToRead.Length;
					unsafe
					{
						byte* p = (byte*)(void*)Scan0;
						//Colors in are bitmap format are 32bpp so 4 bytes for each color in RGBA format
						lock (_lockPixelsObj)
						{
							//If we are set to 0 just read the full screen
							//At above 1080p this will be slower than using an actual number
							if (_onfig.Model.pixelCount == 0 || _pixelsToRead.Length == 0)
							{
								totalSize = srcData.Width * srcData.Height;
								for (var i = 0; i < totalSize * 4; i += 4)
								{
									//index is our power of 4 padded index in the bitmap.
									totals[0] += p[i]; //r
									totals[1] += p[i + 1]; //g
									totals[2] += p[i + 2]; //b
								}
							}
							else
							{
								foreach (var pix in _pixelsToRead)
								{
									//y * stride gives us the offset for the scanline we are in on the bitmap (ex. line 352 * 1080 = 380160 bits)
									//x * 4 gives us our power of 4 for column
									//ex. total offset for coord 960x540 on a 1080p image is is (540 * 1080) + 960 * 4 = 587040 bits
									int index = (pix.Y * stride) + pix.X * 4;
									//Then each r, g, b index is added to this offset
									totals[0] += p[index];
									totals[1] += p[index + 1];
									totals[2] += p[index + 2];
								}
							}
						}
					}

					var t2 = DateTime.UtcNow;
					Console.WriteLine($"Read Bitmap Time:  {(t2 - t1).TotalMilliseconds}");
					//Total colors are averaged
					int avgB = (int)(totals[0] / (totalSize));
					int avgG = (int)(totals[1] / (totalSize));
					int avgR = (int)(totals[2] / (totalSize));
					//If the last colors set are close enough to the current color keep the current color.
					//This is to prevent a lot of color jittering that can happen otherwise.
					if (lastColor.R >= avgR - _thres && lastColor.R <= avgR + _thres)
						avgR = lastColor.R;
					if (lastColor.G >= avgG - _thres && lastColor.G <= avgG + _thres)
						avgG = lastColor.G;
					if (lastColor.B >= avgB - _thres && lastColor.B <= avgB + _thres)
						avgB = lastColor.B;
					avg = Color.FromArgb(0, avgR, avgG, avgB);
					if (avg != lastColor)
						Task.Run(() => _core.ChangeLightColor(avg));
					lastColor = avg;
					bmp.UnlockBits(srcData);
					var t3 = DateTime.UtcNow;
					Console.WriteLine($"Average Calc Time: {(t3 - t2).TotalMilliseconds}");
					GC.Collect();
					Console.WriteLine($"GC Time:           {(DateTime.UtcNow - t3).TotalMilliseconds}");
				}
				

				var dt = DateTime.UtcNow - start;
				AverageDt = (AverageDt + dt.TotalMilliseconds) / 2;
				Console.WriteLine($"Total Time:        {dt.TotalMilliseconds}");
				Console.WriteLine("---------------------------------------");
				//Hue bridge can only take so many updates at a time (7-10 a second) so this needs to be throttled
				if (dt.TotalMilliseconds < 1000 / _frameRate)
					Thread.Sleep((int)((1000 / _frameRate) - dt.TotalMilliseconds));
			} while (IsRunning);
		}

		public void StopScreenLoop()
		{
			IsRunning = false;
		}

		public class ScreenDimensions
		{
			public int left;
			public int top;
			public int width;
			public int height;
		}

		[Serializable, StructLayout(LayoutKind.Sequential)]
		private struct Rectangle
		{
			public Int32 Left;
			public Int32 Top;
			public Int32 Right;
			public Int32 Bottom;


			public Rectangle(Int32 left, Int32 top, Int32 right, Int32 bottom)
			{
				this.Left = left;
				this.Top = top;
				this.Right = right;
				this.Bottom = bottom;
			}
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		private sealed class MonitorInfo
		{
			public Int32 Size = Marshal.SizeOf(typeof(MonitorInfo));
			public Rectangle Monitor;
			public Rectangle Work;
			public Int32 Flags;
		}
	}
}
