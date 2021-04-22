﻿using HueScreenAmbience.DXGICaptureScreen;
using HueScreenAmbience.PiCapture;
using HueScreenAmbience.RGB;
using BitmapZoneProcessor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HueScreenAmbience.LightStrip;

namespace HueScreenAmbience
{
	class ScreenReader
	{
		private ReadPixel[] _pixelsToRead = null;
		private PixelZone[] _zones = null;
		public bool Ready { get; private set; }
		public DxEnumeratedDisplay Screen { get; private set; }
		public ScreenDimensions ScreenInfo { get; private set; }
		private Config _config;
		private ZoneProcessor _zoneProcesser;
		private RGBLighter _rgbLighter;
		private StripLighter _stripLighter;
		private FileLogger _logger;
		private DxCapture _dxCapture;
		private PiCapture.PiCapture _piCapture;
		private DateTime _lastPostReadTime;
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
			if (_config.Model.piCameraSettings.isPi)
			{
				ScreenInfo = new ScreenDimensions()
				{
					RealWidth = _config.Model.piCameraSettings.width,
					RealHeight = _config.Model.piCameraSettings.height,
					SizeReduction = _config.Model.readResolutionReduce
				};
			}
			else
				GetScreenInfo();
			SetupPixelZones();
			_pixelsToRead = Array.Empty<ReadPixel>();
			PreparePixelsToGet();
		}

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_zoneProcesser = _map.GetService(typeof(ZoneProcessor)) as ZoneProcessor;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
			if (!_config.Model.piCameraSettings.isPi)
				_rgbLighter = _map.GetService(typeof(RGBLighter)) as RGBLighter;
			_stripLighter = _map.GetService(typeof(StripLighter)) as StripLighter;
		}

		public void GetScreenInfo()
		{
			try
			{
				var monitor = DxEnumerate.GetMonitor(_config.Model.adapterId, _config.Model.monitorId);
				if (monitor == null)
				{
					Console.WriteLine($"Monitor {_config.Model.monitorId} not found, press enter to fallback to primary");
					Console.ReadLine();
					_config.Model.monitorId = 0;
					_config.SaveConfig();
					monitor = DxEnumerate.GetMonitor(_config.Model.adapterId, 0);
				}

				Screen = monitor ?? throw new Exception("No primary monitor");
				ScreenInfo = new ScreenDimensions
				{
					RealWidth = monitor.Width,
					RealHeight = monitor.Height,
					SizeReduction = _config.Model.readResolutionReduce
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				Task.Run(() => _logger.WriteLog(ex.ToString()));
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
			if (_config.Model.piCameraSettings.isPi)
			{
				_piCapture = new PiCapture.PiCapture(ScreenInfo.RealWidth, ScreenInfo.RealHeight, _config.Model.piCameraSettings.frameRate, _logger);
				_piCapture.Start();
			}
			else
			{
				_dxCapture = new DxCapture(ScreenInfo.RealWidth, ScreenInfo.RealHeight, _config.Model.adapterId, Screen.OutputId, _logger);
				if (_config.Model.rgbDeviceSettings.useMotherboard || _config.Model.rgbDeviceSettings.useKeyboards || _config.Model.rgbDeviceSettings.useMice)
					_rgbLighter.Start();
			}

			if (_config.Model.lightStripSettings.useLightStrip)
				_stripLighter.Start();
		}

		public async Task ReadScreenLoopDx()
		{
			IsRunning = true;
			Bitmap bmp = null;
			do
			{
				bool bitmapChanged = false;
				var start = DateTime.UtcNow;
				try
				{
					var t = start;
					//Do not dispose this bitmap as DxCapture uses the same bitmap every loop to save allocation.
					if (_config.Model.piCameraSettings.isPi)
						bmp = await _piCapture.GetFrame();
					else
						bmp = _dxCapture.GetFrame();
					//Console.WriteLine($"Capture Time:        {(DateTime.UtcNow - start).TotalMilliseconds}");
					//If the bitmap is null that usually means the desktop has not been updated

					//If we are on pi and have skip frames set wait until we are past the frame number before we start trying to read.
					// This is done because initialization for the hdmi connection can take a bit before we get real frames back.
					if (_config.Model.piCameraSettings.isPi && _frame < _config.Model.piCameraSettings.skipFrames)
					{
						_lastPostReadTime = DateTime.UtcNow;
						await _zoneProcesser.PostRead(_zones, _frame);
						_frame++;
						continue;
					}

					if (bmp == null)
					{
						//If we havnt got a new frame in 2 seconds because the desktop hasnt updated send a update with the last zones anyways.
						// If this isint done hue will eventually disconnect us because we didnt send any updates.
						if ((DateTime.UtcNow - _lastPostReadTime).TotalMilliseconds > 2000)
						{
							_lastPostReadTime = DateTime.UtcNow;
							await _zoneProcesser.PostRead(_zones, _frame);
							_frame++;
						}
						continue;
					}

					//Console.WriteLine($"Capture Time:     {(DateTime.UtcNow - t).TotalMilliseconds}");
					t = DateTime.UtcNow;

					bitmapChanged = BitmapProcessor.ReadBitmap(ScreenInfo.Width, ScreenInfo.Height, ScreenInfo.SizeReduction, _config.Model.zoneRows, _config.Model.zoneColumns, _config.Model.pixelCount, _pixelsToRead, ref bmp, ref _zones, false, _config.Model.bitmapRect);

					//Console.WriteLine($"Read Time:        {(DateTime.UtcNow - t).TotalMilliseconds}");
					t = DateTime.UtcNow;

					long f = _frame;
					_lastPostReadTime = DateTime.UtcNow;
					await _zoneProcesser.PostRead(_zones, f);

					//Console.WriteLine($"PostRead Time:    {(DateTime.UtcNow - t).TotalMilliseconds}");

					var dt = DateTime.UtcNow - start;
					if (++_averageIter >= _averageValues.Length)
						_averageIter = 0;
					_averageValues[_averageIter] = dt.TotalMilliseconds;
					//Console.WriteLine($"Total Time:       {dt.TotalMilliseconds}");
					//Console.WriteLine($"AverageDt:        {AverageDt}");
					//Console.WriteLine("---------------------------------------");
					_frame++;
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
					_ = Task.Run(() => _logger.WriteLog(ex.ToString()));
					StopScreenLoop();
				}
				finally
				{
					//Only dispose the bitmap if ReadBitmap updated the reference to a new bitmap.
					// Otherwise we want to leave it as DxCapture will reuse the same bitmap.
					if (bitmapChanged)
					{
						bmp?.Dispose();
						bmp = null;
					}

					var dt = DateTime.UtcNow - start;
					if (dt.TotalMilliseconds < 1000 / _frameRate)
						Thread.Sleep((int)((1000 / _frameRate) - dt.TotalMilliseconds));
				}
			} while (IsRunning);
		}

		public void StopScreenLoop()
		{
			if (!IsRunning)
				return;

			IsRunning = false;
			if (_config.Model.piCameraSettings.isPi)
			{
				_piCapture.Stop();
				_piCapture.Dispose();
			}
			else
			{
				_dxCapture.Dispose();
				_rgbLighter?.Stop();
			}
			_stripLighter?.Stop();
		}

		public class ScreenDimensions
		{
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
	}
}
