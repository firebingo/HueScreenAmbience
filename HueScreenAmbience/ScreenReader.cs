using HueScreenAmbience.DXGICaptureScreen;
using Newtonsoft.Json;
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
using System.Xml.Serialization;

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
		private ZoneProcessor _zoneProcesser;
		private FileLogger _logger;
		private DxCapture _dxCapture;
		private long _frame;
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
		private int _frameRate = 24;

		private readonly object _lockPixelsObj = new object();

		public void Start()
		{
			_frameRate = _config.Model.screenReadFrameRate;
			GetScreenInfo();
			SetupPixelZones();
			_pixelsToRead = new ReadPixel[0];
			PreparePixelsToGet();
		}

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_core = _map.GetService(typeof(Core)) as Core;
			_zoneProcesser = _map.GetService(typeof(ZoneProcessor)) as ZoneProcessor;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
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
					Left = monitorInfo.Monitor.Left,
					Top = monitorInfo.Monitor.Top,
					RealWidth = monitorInfo.Monitor.Right - monitorInfo.Monitor.Left,
					RealHeight = monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top,
					SizeReduction = _config.Model.readResolutionReduce
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
				var xMin = (ScreenInfo.Width / (double)_config.Model.zoneColumns) * col;
				//If we are in the last column just set the bottom right to screen width so we dont get weird rounding where edge is not included
				var xMax = col == _config.Model.zoneColumns - 1
					? ScreenInfo.Width
					: (ScreenInfo.Width / (double)_config.Model.zoneColumns) * (col + 1);
				var yMin = (ScreenInfo.Height / (double)_config.Model.zoneRows) * row;
				//If we are in the last row just set the bottom right to screen height so we dont get weird rounding where edge is not included
				var yMax = row == _config.Model.zoneRows - 1
					? ScreenInfo.Height
					: (ScreenInfo.Height / (double)_config.Model.zoneRows) * (row + 1);
				_zones[i] = new PixelZone(row, col, (int)Math.Ceiling(xMin), (int)Math.Ceiling(xMax), (int)Math.Ceiling(yMin), (int)Math.Ceiling(yMax));
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
				if (_config.Model.pixelCount >= ScreenInfo.Width * ScreenInfo.Height)
					throw new Exception("More pixels to read than screen has");
				//Get a distinct list of points until we have the needed pixel count
				do
				{
					var newPixels = new List<ReadPixel>();
					for (var i = 0; i < _config.Model.pixelCount; ++i)
					{
						p = new Point(r.Next(0, ScreenInfo.Width), r.Next(0, ScreenInfo.Height));
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

		public void InitScreenLoop()
		{
			_dxCapture = new DxCapture(ScreenInfo.RealWidth, ScreenInfo.RealHeight, _logger);
		}

		public void ReadScreenLoopDx()
		{
			IsRunning = true;
			Bitmap bmp = null;
			do
			{
				try
				{
					var start = DateTime.UtcNow;
					//Reset zones
					foreach (var zone in _zones)
					{
						zone.Count = 0;
						zone.Totals[0] = 0;
						zone.Totals[1] = 0;
						zone.Totals[2] = 0;
						zone.ResetAverages();
					}

					bmp = _dxCapture.GetFrame();
					//If the bitmap is null that usually means the desktop has not been updated
					if (bmp == null)
						continue;

					//Reducing the resolution of the desktop capture takes time but it saves a lot of time on reading the image
					if (_config.Model.readResolutionReduce > 1.0f)
					{
						var oldBmp = bmp;
						bmp = CaptureScreen.ResizeImage(oldBmp, ScreenInfo.Width, ScreenInfo.Height);
						oldBmp.Dispose();
					}

					//using (var fi = System.IO.File.OpenWrite($"Images/i{_frame.ToString().PadLeft(5, '0')}.png"))
					//	bmp.Save(fi, ImageFormat.Png);

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
							//This is reccomended if reducing the resolution as it is fast enough and produces better results
							if (_config.Model.pixelCount == 0 || _pixelsToRead.Length == 0)
							{
								totalSize = srcData.Width * srcData.Height;
								bool oneZone = !(_zones.Length > 1);
								int currentZone = 0;
								var zone = _zones[currentZone];
								int zoneRow = 0;
								int xIter = 0;
								int yIter = 0;
								for (var i = 0; i < totalSize * 4; i += 4)
								{
									//index is our power of 4 padded index in the bitmap.
									zone.Totals[2] += p[i]; //b
									zone.Totals[1] += p[i + 1]; //g
									zone.Totals[0] += p[i + 2]; //r
									zone.Count++;

									if (!oneZone && currentZone != _zones.Length - 1)
									{
										//If x is greater than zone width
										if (++xIter >= zone.Width)
										{
											xIter = 0;
											//If we are on the last column for this row
											if (zone.Column == _config.Model.zoneColumns - 1)
											{
												//If our y is greater than this rows height
												// reset y and advance us to the next row
												if (++yIter >= zone.Height)
												{
													yIter = 0;
													currentZone = ++zoneRow * _config.Model.zoneColumns;
													zone = _zones[currentZone];
												}
												//Else reset us back to the start of the current row
												else
												{
													currentZone = zoneRow * _config.Model.zoneColumns;
													zone = _zones[currentZone];
												}
											}
											//Else move to the next column
											else
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
									//y * stride gives us the offset for the scanline we are in on the bitmap (ex. line 352 * 1080 = 380160 bits)
									//x * 4 gives us our power of 4 for column
									//ex. total offset for coord 960x540 on a 1080p image is is (540 * 1080) + 960 * 4 = 587040 bits
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
					}
					bmp.UnlockBits(srcData);
					long f = _frame;
					var tempZones = new PixelZone[_zones.Length];
					for (var i = 0; i < tempZones.Length; ++i)
					{
						tempZones[i] = PixelZone.Clone(_zones[i]);
					}
					Task.Run(() => _zoneProcesser.PostRead(tempZones, ScreenInfo.Width, ScreenInfo.Height, f));

					var dt = DateTime.UtcNow - start;
					if (++_averageIter >= _averageValues.Length)
						_averageIter = 0;
					_averageValues[_averageIter] = dt.TotalMilliseconds;
					//Console.WriteLine($"Total Time:        {dt.TotalMilliseconds}");
					//Console.WriteLine($"AverageDt:         {AverageDt}");
					//Console.WriteLine("---------------------------------------");
					_frame++;

					if (dt.TotalMilliseconds < 1000 / _frameRate)
						Thread.Sleep((int)((1000 / _frameRate) - dt.TotalMilliseconds));
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
					Task.Run(() => _logger.WriteLog(ex.ToString()));
					StopScreenLoop();
				}
				finally
				{
					bmp?.Dispose();
					bmp = null;
				}
			} while (IsRunning);
		}

		public void StopScreenLoop()
		{
			IsRunning = false;
			_dxCapture.Dispose();
		}

		public class ScreenDimensions
		{
			public int Left;
			public int Top;
			public int RealWidth;
			public int RealHeight;
			public float SizeReduction;
			public int Width
			{
				get => SizeReduction == 0 ? RealWidth : (int)Math.Floor(RealWidth / SizeReduction);
			}
			public int Height
			{
				get => SizeReduction == 0 ? RealHeight : (int)Math.Floor(RealHeight / SizeReduction);
			}
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
