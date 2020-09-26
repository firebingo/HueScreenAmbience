using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HueScreenAmbience
{
	class ScreenReader
	{
		private const Int32 MONITOR_DEFAULTTOPRIMERTY = 0x00000001;
		private const Int32 MONITOR_DEFAULTTONEAREST = 0x00000002;
		[DllImport("user32.dll")]
		private static extern IntPtr MonitorFromWindow(IntPtr handle, Int32 flags);
		[DllImport("user32.dll")]
		private static extern Boolean GetMonitorInfo(IntPtr hMonitor, MonitorInfo lpmi);

		private ReadPixel[] _pixelsToRead = null;
		private PixelZone[] _zones = null;
		private MonitorInfo _screen = null;
		public bool Ready { get; private set; }
		public ScreenDimensions ScreenInfo { get; private set; }
		private Config _config;
		private Core _core;
		public bool IsRunning { get; private set; } = false;
		public double AverageDt
		{
			get
			{
				double val = 0.0;
				for (var i = 0; i < _averageValues.Length; ++i)
					val += _averageValues[i];
				return val / _averageValues.Length;
			}
		}
		private readonly double[] _averageValues = new double[20];
		private int _averageIter = 0;
		private readonly int _thres = 15;
		private int _frameRate = 7;

		private readonly object _lockPixelsObj = new object();

		public void Start()
		{
			GetScreenInfo();
			SetupPixelZones();
			_pixelsToRead = new ReadPixel[0];
			PreparePixelsToGet();
		}

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_core = _map.GetService(typeof(Core)) as Core;
		}

		public void GetScreenInfo()
		{
			var desk = PlatformInvokeUSER32.GetDesktopWindow();
			var monitor = MonitorFromWindow(desk, MONITOR_DEFAULTTOPRIMERTY);
			if (monitor != IntPtr.Zero)
			{
				var monitorInfo = new MonitorInfo();
				GetMonitorInfo(monitor, monitorInfo);
				_screen = monitorInfo;
				ScreenInfo = new ScreenDimensions
				{
					left = monitorInfo.Monitor.Left,
					top = monitorInfo.Monitor.Top,
					width = (monitorInfo.Monitor.Right - monitorInfo.Monitor.Left),
					height = (monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top)
				};
			}
		}

		public void SetupPixelZones()
		{
			_zones = new PixelZone[_config.Model.zoneColumns * _config.Model.zoneRows];
			if (_zones.Length == 0)
				throw new Exception("0 Light zones created");
			var row = 0;
			for (var i = 0; i < _zones.Length; ++i)
			{
				var col = i % _config.Model.zoneColumns;
				var xMin = (ScreenInfo.width / _config.Model.zoneColumns) * col;
				//If we are in the last column just set the bottom right to screen width so we dont get weird rounding where edge is not included
				var xMax = col == _config.Model.zoneColumns - 1
					? ScreenInfo.width
					: (ScreenInfo.width / _config.Model.zoneColumns) * (col + 1);
				var yMin = (ScreenInfo.height / _config.Model.zoneRows) * row;
				//If we are in the last row just set the bottom right to screen height so we dont get weird rounding where edge is not included
				var yMax = row == _config.Model.zoneRows - 1
					? ScreenInfo.height
					: (ScreenInfo.height / _config.Model.zoneRows) * (row + 1);
				_zones[i] = new PixelZone(row, col, xMin, xMax, yMin, yMax);
				if (col == _config.Model.zoneColumns - 1)
					row += 1;
			}
		}

		public void PreparePixelsToGet()
		{
			Ready = false;
			lock (_lockPixelsObj)
			{
				var pixelsToGet = new List<ReadPixel>();
				var r = new Random();

				Point p = Point.Empty;
				if (_config.Model.pixelCount >= ScreenInfo.width * ScreenInfo.height)
					throw new Exception("More pixels to read than screen has");
				//Get a distinct list of points until we have the needed pixel count
				do
				{
					var newPixels = new List<ReadPixel>();
					for (var i = 0; i < _config.Model.pixelCount; ++i)
					{
						p = new Point(r.Next(0, ScreenInfo.width), r.Next(0, ScreenInfo.height));
						var zone = _zones.First(x => x.IsPointInZone(p));
						newPixels.Add(new ReadPixel(zone, p));
					}
					pixelsToGet.AddRange(newPixels.Distinct());
					pixelsToGet = pixelsToGet.Distinct().ToList();
				} while (pixelsToGet.Count < _config.Model.pixelCount);
				pixelsToGet = pixelsToGet.GetRange(0, _config.Model.pixelCount).ToList();

				//looping over an array is noticeably faster than a list at this scale
				_pixelsToRead = pixelsToGet.Distinct().ToArray();
			}
			Ready = true;
			GC.Collect();
		}

		public void InitScreenLoop(int frameRate)
		{
			_frameRate = frameRate;
		}

		public void ReadScreenLoop()
		{
			IsRunning = true;
			Color lastColor = Color.FromArgb(255, 255, 255);
			do
			{
				try
				{
					var start = DateTime.UtcNow;
					//var t2 = DateTime.UtcNow;
					Color avg = new Color();
					foreach (var zone in _zones)
					{
						zone.Count = 0;
						zone.Totals[0] = 0;
						zone.Totals[1] = 0;
						zone.Totals[2] = 0;
					}
					using var bmp = CaptureScreen.GetDesktopImage(ScreenInfo.width, ScreenInfo.height);

					if (bmp == null)
						continue;

					BitmapData srcData = bmp.LockBits(
					new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
					ImageLockMode.ReadOnly,
					PixelFormat.Format32bppArgb);

					unsafe
					{

						//var t1 = DateTime.UtcNow;
						//Console.WriteLine($"Build Bitmap Time: {(t1 - start).TotalMilliseconds}");

						var totalSize = _pixelsToRead.Length;

						byte* p = (byte*)(void*)srcData.Scan0;
						//Colors in are bitmap format are 32bpp so 4 bytes for each color in RGBA format
						lock (_lockPixelsObj)
						{
							//If we are set to 0 just read the full screen
							//At above 1080p this will be slower than using an actual number
							if (_config.Model.pixelCount == 0 || _pixelsToRead.Length == 0)
							{
								totalSize = srcData.Width * srcData.Height;
								bool oneZone = !(_zones.Length > 1);
								int currentZone = 0;
								var zone = _zones[currentZone];
								int zoneRow = 0;
								int x = 0;
								int y = 0;
								for (var i = 0; i < totalSize * 4; i += 4)
								{
									//index is our power of 4 padded index in the bitmap.
									zone.Totals[2] += p[i]; //b
									zone.Totals[1] += p[i + 1]; //g
									zone.Totals[0] += p[i + 2]; //r
									zone.Count++;
									if (!oneZone)
									{
										//If the new x is greater than the screen width
										// reset the x and advance the y to the next row
										if (++x > srcData.Width)
										{
											y++;
											x = 0;
										}
										//If we are on a new row check if our y is greater than our current zone boundry and if it is advance to the next zone row
										if (x == 0)
										{
											if (y > _zones[currentZone].BottomRight.Y)
											{
												currentZone = ++zoneRow * _config.Model.zoneColumns;
												zone = _zones[currentZone];
											}
										}
										//Else if the current x is greater than our current zone boundry advance to the next zone column
										else if (x > _zones[currentZone].BottomRight.X)
										{
											zone = _zones[++currentZone];
										}
									}
								}
							}
							else
							{
								int pixIndex = 0;
								for (var i = 0; i < totalSize; ++i)
								{
									pixIndex = (_pixelsToRead[i].Pixel.Y * srcData.Stride) + _pixelsToRead[i].Pixel.X * 4;
									_pixelsToRead[i].Zone.Totals[0] += p[pixIndex + 2];
									_pixelsToRead[i].Zone.Totals[1] += p[pixIndex + 1];
									_pixelsToRead[i].Zone.Totals[2] += p[pixIndex];
									_pixelsToRead[i].Zone.Count++;
								}
							}
						}

						//t2 = DateTime.UtcNow;
						//Console.WriteLine($"Read Bitmap Time:  {(t2 - t1).TotalMilliseconds}");

						//Total colors are averaged
						int avgR = _zones.Sum(x => x.AvgR) / _zones.Length;
						int avgG = _zones.Sum(x => x.AvgG) / _zones.Length;
						int avgB = _zones.Sum(x => x.AvgB) / _zones.Length;
						//If the last colors set are close enough to the current color keep the current color.
						//This is to prevent a lot of color jittering that can happen otherwise.
						if (lastColor.R >= avgR - _thres && lastColor.R <= avgR + _thres)
							avgR = lastColor.R;
						if (lastColor.G >= avgG - _thres && lastColor.G <= avgG + _thres)
							avgG = lastColor.G;
						if (lastColor.B >= avgB - _thres && lastColor.B <= avgB + _thres)
							avgB = lastColor.B;
						avg = Color.FromArgb(255, avgR, avgG, avgB);
					}
					if (avg != lastColor)
						Task.Run(() => _core.ChangeLightColor(avg));
					lastColor = avg;
					bmp.UnlockBits(srcData);
					//var t3 = DateTime.UtcNow;
					//Console.WriteLine($"Average Calc Time: {(t3 - t2).TotalMilliseconds}");
					//GC.Collect();
					//Console.WriteLine($"GC Time:           {(DateTime.UtcNow - t3).TotalMilliseconds}");
					var dt = DateTime.UtcNow - start;
					if (++_averageIter >= _averageValues.Length)
						_averageIter = 0;
					_averageValues[_averageIter] = dt.TotalMilliseconds;
					//Console.WriteLine($"Total Time:        {dt.TotalMilliseconds}");
					//Console.WriteLine($"AverageDt:         {AverageDt}");
					//Console.WriteLine("---------------------------------------");
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
					StopScreenLoop();
				}
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
