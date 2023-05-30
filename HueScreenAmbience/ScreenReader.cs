using BitmapZoneProcessor;
using HueScreenAmbience.DXGICaptureScreen;
using HueScreenAmbience.LightStrip;
using HueScreenAmbience.RGB;
using LightsShared;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HueScreenAmbience
{
	public class ScreenReader : IDisposable
	{
		private bool disposed = false;

		private PixelZonesTotals _zoneTotals;
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
		private FFMpegCapture.FFMpegCapture _ffmpegCapture;
		private DateTime _lastPostReadTime;
		private long _frame;
		public long Frame { get => _frame; }
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

		public void Start()
		{
			_frameRate = _config.Model.ScreenReadFrameRate;
			if (_config.Model.FfmpegCaptureSettings.UseFFMpeg)
			{
				ScreenInfo = new ScreenDimensions()
				{
					Source = "Capture",
					Rate = _config.Model.FfmpegCaptureSettings.InputFrameRate,
					RealWidth = _config.Model.FfmpegCaptureSettings.Width,
					RealHeight = _config.Model.FfmpegCaptureSettings.Height,
					SizeReduction = _config.Model.ReadResolutionReduce
				};
			}
			else
				GetScreenInfo();
			SetupPixelZones();
		}

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_zoneProcesser = _map.GetService(typeof(ZoneProcessor)) as ZoneProcessor;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
			if (!_config.Model.FfmpegCaptureSettings.UseFFMpeg)
				_rgbLighter = _map.GetService(typeof(RGBLighter)) as RGBLighter;
			_stripLighter = _map.GetService(typeof(StripLighter)) as StripLighter;
		}

		public void GetScreenInfo()
		{
			try
			{
				var monitor = DxEnumerate.GetMonitor(_config.Model.AdapterId, _config.Model.MonitorId);
				if (monitor == null)
				{
					Console.WriteLine($"Monitor {_config.Model.MonitorId} not found, press enter to fallback to primary");
					Console.ReadLine();
					_config.Model.MonitorId = 0;
					_config.SaveConfig();
					monitor = DxEnumerate.GetMonitor(_config.Model.AdapterId, 0);
				}

				Screen = monitor ?? throw new Exception("No primary monitor");
				ScreenInfo = new ScreenDimensions
				{
					Source = $"[{monitor.OutputId}] {monitor.Name}",
					Rate = monitor.RefreshRate,
					RealWidth = monitor.Width,
					RealHeight = monitor.Height,
					SizeReduction = _config.Model.ReadResolutionReduce
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
		}

		unsafe public void SetupPixelZones()
		{
			_zones = new PixelZone[_config.Model.ZoneColumns * _config.Model.ZoneRows];
			_zoneTotals = new PixelZonesTotals(_zones.Length);
			if (_zones.Length == 0)
				throw new Exception("0 Light zones created");
			var newWidth = ScreenInfo.Width;
			var newHeight = ScreenInfo.Height;
			if (_config.Model.ImageRect.HasValue)
			{
				if (_config.Model.ReadResolutionReduce > 1.0f)
				{
					newWidth = (int)Math.Floor(_config.Model.ImageRect.Value.Width / _config.Model.ReadResolutionReduce);
					newHeight = (int)Math.Floor(_config.Model.ImageRect.Value.Height / _config.Model.ReadResolutionReduce);
				}
				else
				{
					newWidth = _config.Model.ImageRect.Value.Width;
					newHeight = _config.Model.ImageRect.Value.Height;
				}
			}
			var row = 0;
			for (var i = 0; i < _zones.Length; ++i)
			{
				var col = i % _config.Model.ZoneColumns;
				var xMin = (newWidth / (double)_config.Model.ZoneColumns) * col;
				//If we are in the last column just set the bottom right to screen width so we dont get weird rounding where edge is not included
				var xMax = col == _config.Model.ZoneColumns - 1
					? newWidth
					: (newWidth / (double)_config.Model.ZoneColumns) * (col + 1);
				var yMin = (newHeight / (double)_config.Model.ZoneRows) * row;
				//If we are in the last row just set the bottom right to screen height so we dont get weird rounding where edge is not included
				var yMax = row == _config.Model.ZoneRows - 1
					? newHeight
					: (newHeight / (double)_config.Model.ZoneRows) * (row + 1);
				_zones[i] = new PixelZone(row, col, (int)Math.Ceiling(xMin), (int)Math.Ceiling(xMax), (int)Math.Ceiling(yMin), (int)Math.Ceiling(yMax), newWidth * ScreenInfo.BitDepth, ScreenInfo.BitDepth, _zoneTotals, i);
				if (col == _config.Model.ZoneColumns - 1)
					row += 1;
			}
			Ready = true;
		}

		public void InitScreenLoop()
		{
			if (_config.Model.FfmpegCaptureSettings.UseFFMpeg)
			{
				_ffmpegCapture = new FFMpegCapture.FFMpegCapture(ScreenInfo.RealWidth, ScreenInfo.RealHeight, _config.Model.FfmpegCaptureSettings.InputWidth, _config.Model.FfmpegCaptureSettings.InputHeight,
					_config.Model.FfmpegCaptureSettings.FrameRate, _config.Model.FfmpegCaptureSettings.InputSource, _config.Model.FfmpegCaptureSettings.InputFormat, _config.Model.FfmpegCaptureSettings.InputPixelFormat,
					_config.Model.FfmpegCaptureSettings.InputPixelFormatType, _config.Model.FfmpegCaptureSettings.InputFrameRate, _config.Model.FfmpegCaptureSettings.BufferMultiplier,
					_config.Model.FfmpegCaptureSettings.ThreadQueueSize, _logger, _config.Model.FfmpegCaptureSettings.FfmpegStdError, _config.Model.FfmpegCaptureSettings.UseGpu);
				_ffmpegCapture.Start();
			}
			else
			{
				_dxCapture = new DxCapture(ScreenInfo.RealWidth, ScreenInfo.RealHeight, _config.Model.AdapterId, Screen.OutputId, _logger);
				if (_config.Model.RgbDeviceSettings.UseMotherboard || _config.Model.RgbDeviceSettings.UseKeyboards || _config.Model.RgbDeviceSettings.UseMice)
					_rgbLighter.Start();
			}

			if (_config.Model.LightStripSettings.UseLightStrip)
				_stripLighter.Start();
		}

		public async Task ReadScreenLoopDx()
		{
			IsRunning = true;
			var newWidth = ScreenInfo.Width;
			var newHeight = ScreenInfo.Height;
			var frameStream = new MemoryStream(1280 * 720 * ScreenInfo.BitDepth);
			MemoryStream cropFrameStream = null;
			if (_config.Model.ImageRect.HasValue)
			{
				cropFrameStream = new MemoryStream(_config.Model.ImageRect.Value.Width * _config.Model.ImageRect.Value.Height * ScreenInfo.BitDepth);
				if (_config.Model.ReadResolutionReduce > 1.0f)
				{
					newWidth = (int)Math.Floor(_config.Model.ImageRect.Value.Width / _config.Model.ReadResolutionReduce);
					newHeight = (int)Math.Floor(_config.Model.ImageRect.Value.Height / _config.Model.ReadResolutionReduce);
				}
				else
				{
					newWidth = _config.Model.ImageRect.Value.Width;
					newHeight = _config.Model.ImageRect.Value.Height;
				}
			}
			MemoryStream sizeFrameStream = null;
			if (_config.Model.ReadResolutionReduce > 1.0f)
				sizeFrameStream = new MemoryStream(newWidth * newHeight * ScreenInfo.BitDepth);

			do
			{
				var start = DateTime.UtcNow;
				try
				{
					var updatedFrame = false;
					var t = start;
					if (_config.Model.FfmpegCaptureSettings.UseFFMpeg)
						updatedFrame = _ffmpegCapture.GetFrame(frameStream);
					else
						updatedFrame = _dxCapture.GetFrame(frameStream);
					//Console.WriteLine($"Capture Time:        {(DateTime.UtcNow - start).TotalMilliseconds}");

					//If we are on pi and have skip frames set wait until we are past the frame number before we start trying to read.
					// This is done because initialization for the hdmi connection can take a bit before we get real frames back.
					if (_config.Model.FfmpegCaptureSettings.UseFFMpeg && _frame < _config.Model.FfmpegCaptureSettings.SkipFrames)
					{
						_lastPostReadTime = DateTime.UtcNow;
						await _zoneProcesser.PostRead(_zones, _zoneTotals, _frame);
						_frame++;
						continue;
					}

					if (!updatedFrame)
					{
						//If we havnt got a new frame in 2 seconds because the desktop hasnt updated send a update with the last zones anyways.
						// If this isint done hue will eventually disconnect us because we didnt send any updates.
						if ((DateTime.UtcNow - _lastPostReadTime).TotalMilliseconds > 2000)
						{
							_lastPostReadTime = DateTime.UtcNow;
							await _zoneProcesser.PostRead(_zones, _zoneTotals, _frame);
							_frame++;
						}
						continue;
					}

					var captureTime = (DateTime.UtcNow - t).TotalMilliseconds;
					t = DateTime.UtcNow;



					BitmapProcessor.ReadBitmap(frameStream, 1280, 720, newWidth, newHeight, _config.Model.ReadResolutionReduce,
						_config.Model.ZoneRows, _config.Model.ZoneColumns, _zones, _zoneTotals, ScreenInfo.BitDepth, sizeFrameStream, cropFrameStream, _config.Model.ImageRect);

					var readTime = (DateTime.UtcNow - t).TotalMilliseconds;

					t = DateTime.UtcNow;

					long f = _frame;
					_lastPostReadTime = DateTime.UtcNow;
					await _zoneProcesser.PostRead(_zones, _zoneTotals, f);

					var postReadTime = (DateTime.UtcNow - t).TotalMilliseconds;


					var dt = DateTime.UtcNow - start;
					if (++_averageIter >= _averageValues.Length)
						_averageIter = 0;
					_averageValues[_averageIter] = dt.TotalMilliseconds;
					//if (dt.TotalMilliseconds > 25)
					//{
					if (_config.Model.DebugTimings)
					{
						Console.WriteLine($"Capture Time:     {captureTime}");
						Console.WriteLine($"Read Time:        {readTime}");
						Console.WriteLine($"PostRead Time:    {postReadTime}");
						Console.WriteLine($"Total Time:       {dt.TotalMilliseconds}");
						Console.WriteLine($"AverageDt:        {AverageDt}");
						Console.WriteLine("---------------------------------------");
					}
					//}
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
					var dt = DateTime.UtcNow - start;
					if (dt.TotalMilliseconds < 1000 / _frameRate)
						Thread.Sleep((int)((1000 / _frameRate) - dt.TotalMilliseconds));
				}
			} while (IsRunning);
			await frameStream.DisposeAsync();
			if (cropFrameStream != null)
				await cropFrameStream.DisposeAsync();
			if (sizeFrameStream != null)
				await sizeFrameStream.DisposeAsync();
		}

		public void StopScreenLoop()
		{
			if (!IsRunning)
				return;

			IsRunning = false;
			if (_config.Model.FfmpegCaptureSettings.UseFFMpeg)
			{
				_ffmpegCapture.Stop();
				_ffmpegCapture.Dispose();
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
			public string Source;
			public double Rate;
			public int RealWidth;
			public int RealHeight;
			public float SizeReduction;
			public int BitDepth = 4;
			public int Width
			{
				get => SizeReduction == 0 ? RealWidth : (int)Math.Floor(RealWidth / SizeReduction);
			}
			public int Height
			{
				get => SizeReduction == 0 ? RealHeight : (int)Math.Floor(RealHeight / SizeReduction);
			}
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
				return;

			if (_zones != null)
			{
				foreach (var zone in _zones)
				{
					zone.Dispose();
				}
			}
			_zoneTotals?.Dispose();
			disposed = true;
		}
	}
}
