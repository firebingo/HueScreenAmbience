using System;
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

		private List<Point> pixelsToGet = null;
		private MonitorInfo screen = null;
		public screenDimensions screenInfo { get; private set; }
		private Config config;
		private Core core;
		public bool isRunning { get; private set; } = false;
		public double averageDt = 0;
		private int thres = 15;

		object lockPixelsObj = new object();
		private int frameRate = 7;

		public void start()
		{
			getScreenInfo();
			pixelsToGet = new List<Point>();
			preparePixelsToGet();
		}

		public void installServices(IServiceProvider _map)
		{
			config = _map.GetService(typeof(Config)) as Config;
			core = _map.GetService(typeof(Core)) as Core;
		}

		public Color GetColorAt(Point p, IntPtr dc)
		{
			int a = (int)GetPixel(dc, p.X, p.Y);
			return Color.FromArgb(255, (a >> 0) & 0xff, (a >> 8) & 0xff, (a >> 16) & 0xff);
		}

		public void getScreenInfo()
		{
			var desk = GetDesktopWindow();
			var monitor = MonitorFromWindow(desk, MONITOR_DEFAULTTONEAREST);
			if (monitor != IntPtr.Zero)
			{
				var monitorInfo = new MonitorInfo();
				GetMonitorInfo(monitor, monitorInfo);
				screen = monitorInfo;
				screenInfo = new screenDimensions();
				screenInfo.left = monitorInfo.Monitor.Left;
				screenInfo.top = monitorInfo.Monitor.Top;
				screenInfo.width = (monitorInfo.Monitor.Right - monitorInfo.Monitor.Left);
				screenInfo.height = (monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top);
			}
		}

		public void preparePixelsToGet()
		{
			lock (lockPixelsObj)
			{
				pixelsToGet.Clear();
				var r = new Random();

				object tempLock = new object();
				Parallel.For(0, config.config.pixelCount, index =>
				{
					var p = new Point(r.Next(0, screenInfo.width), r.Next(0, screenInfo.height));
					lock (tempLock)
					{
						pixelsToGet.Add(p);
					}
				});
			}
		}

		public void readScreenLoop()
		{
			isRunning = true;
			Color lastColor = Color.FromArgb(255, 255, 255);
			do
			{
				var start = DateTime.Now;
				Color avg = new Color();
				using (var bmp = CaptureScreen.GetDesktopImage(screenInfo.width, screenInfo.height))
				{
					BitmapData srcData = bmp.LockBits(
					new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
					ImageLockMode.ReadOnly,
					PixelFormat.Format32bppArgb);

					int stride = srcData.Stride;

					IntPtr Scan0 = srcData.Scan0;

					long[] totals = new long[] { 0, 0, 0 };

					int width = bmp.Width;
					int height = bmp.Height;

					unsafe
					{
						byte* p = (byte*)(void*)Scan0;
						lock (lockPixelsObj)
						{
							foreach (var pix in pixelsToGet)
							{
								for (int color = 0; color < 3; color++)
								{
									int idx = (pix.Y * stride) + pix.X * 4 + color;
									totals[color] += p[idx];
								}
							}
						}
					}

					int avgB = (int)(totals[0] / (pixelsToGet.Count));
					int avgG = (int)(totals[1] / (pixelsToGet.Count));
					int avgR = (int)(totals[2] / (pixelsToGet.Count));
					if (lastColor.R >= avgR - thres && lastColor.R <= avgR + thres)
						avgR = lastColor.R;
					if (lastColor.G >= avgG - thres && lastColor.G <= avgG + thres)
						avgG = lastColor.G;
					if (lastColor.B >= avgB - thres && lastColor.B <= avgB + thres)
						avgB = lastColor.B;
					avg = Color.FromArgb(0, avgR, avgG, avgB);
					if (avg != lastColor)
						core.changeLightColor(avg).ConfigureAwait(false);
					lastColor = avg;
					bmp.UnlockBits(srcData);
					GC.Collect();
				}

				var end = DateTime.Now;
				var dt = end - start;
				averageDt = (averageDt + dt.TotalMilliseconds) / 2;
				if (dt.TotalMilliseconds < 1000 / frameRate)
					Thread.Sleep((int)((1000 / frameRate) - dt.TotalMilliseconds));
			} while (isRunning);
		}

		public void stopScreenLoop()
		{
			isRunning = false;
		}

		public class screenDimensions
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
