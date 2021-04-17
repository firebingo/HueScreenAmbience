using BitmapZoneProcessor;
using HueScreenAmbience.Hue;
using HueScreenAmbience.LightStrip;
using HueScreenAmbience.RGB;
//using ImageMagick;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HueScreenAmbience
{
	public class ZoneProcessor
	{
		private bool _processingFrame;
		private MemoryStream _smallImageMemStream;
		private MemoryStream _blurImageMemStream;
		private MemoryStream _hueImageMemStream;
		private MemoryStream _lstripImageMemStream;
		private MemoryStream _rgbImageMemStream;
		private Config _config;
		private HueCore _hueClient;
		private FileLogger _logger;
		private RGBLighter _rgbLighter;
		private StripLighter _stripLighter;

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_hueClient = _map.GetService(typeof(HueCore)) as HueCore;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
			if (!_config.Model.piCameraSettings.isPi)
				_rgbLighter = _map.GetService(typeof(RGBLighter)) as RGBLighter;
			_stripLighter = _map.GetService(typeof(StripLighter)) as StripLighter;
		}

		public async Task PostRead(PixelZone[] zones, int width, int height, long frame)
		{
			if (_processingFrame)
				return;
			_processingFrame = true;

			(MemoryStream image, MemoryStream blurImage) images = (null, null);
			var columns = zones.OrderByDescending(x => x.Column).First().Column + 1;
			var rows = zones.OrderByDescending(x => x.Row).First().Row + 1;

			//Pre allocate the memory stream for images since it will be the same size every time
			if (_smallImageMemStream == null)
				_smallImageMemStream = new MemoryStream(columns * rows * 3);

			var newWidth = (int)Math.Floor(columns * _config.Model.zoneProcessSettings.resizeScale);
			var newHeight = (int)Math.Floor(rows * _config.Model.zoneProcessSettings.resizeScale);

			if (_blurImageMemStream == null)
				_blurImageMemStream = new MemoryStream(newWidth * newHeight * 3);

			try
			{
				var start = DateTime.UtcNow;
				var time = start;

				foreach (var zone in zones)
				{
					zone.CalculateAverages();
				}

				//This is for debug purpose so I can just dump out the zones as pixels
				//using (var smallImage = ImageHandler.CreateSmallImageFromZones(zones, columns, rows))
				//{
				//	var path = Path.Combine(_config.Model.imageDumpLocation, $"{frame.ToString().PadLeft(6, '0')}_small.png");
				//	using var writeStream = File.OpenWrite(path);
				//	smallImage.Write(writeStream, MagickFormat.Png);
				//}

				time = DateTime.UtcNow;

				images = await BitmapProcessor.PreparePostBitmap(zones, columns, rows, newWidth, newHeight, _config.Model.zoneProcessSettings.resizeFilter, _config.Model.zoneProcessSettings.resizeSigma, _smallImageMemStream, _blurImageMemStream);

				if (images.image == null)
				{
					Console.WriteLine($"f:{frame} Image is null. Check log");
					return;
				}

				int avgR = zones.Sum(x => x.AvgR) / zones.Length;
				int avgG = zones.Sum(x => x.AvgG) / zones.Length;
				int avgB = zones.Sum(x => x.AvgB) / zones.Length;
				var avgColor = Color.FromArgb(255, avgR, avgG, avgB);

				time = DateTime.UtcNow;

				var processingTasks = new List<Task>();

				if (_config.Model.hueSettings.useHue)
				{
					if (_config.Model.hueSettings.hueType == HueType.Basic)
					{
						processingTasks.Add(Task.Run(() => _hueClient.ChangeLightColorBasic(avgColor)));
					}
					else if (_config.Model.hueSettings.hueType == HueType.Entertainment)
					{
						if (_hueImageMemStream == null)
							_hueImageMemStream = new MemoryStream(newWidth * newHeight * 3);
						_hueImageMemStream.Seek(0, SeekOrigin.Begin);
						_blurImageMemStream.Seek(0, SeekOrigin.Begin);
						await _blurImageMemStream.CopyToAsync(_hueImageMemStream);
						processingTasks.Add(Task.Run(() =>
						{
							_hueClient.UpdateEntertainmentGroupFromImage(_hueImageMemStream, newWidth, newHeight);
						}));
					}
				}
				if (_config.Model.lightStripSettings.useLightStrip)
				{
					if (_lstripImageMemStream == null)
						_lstripImageMemStream = new MemoryStream(newWidth * newHeight * 3);
					_lstripImageMemStream.Seek(0, SeekOrigin.Begin);
					_blurImageMemStream.Seek(0, SeekOrigin.Begin);
					await _blurImageMemStream.CopyToAsync(_lstripImageMemStream);
					processingTasks.Add(Task.Run(() =>
					{

						_stripLighter.UpdateFromImage(_lstripImageMemStream, newWidth, newHeight, frame);
					}));
				}

				//Console.WriteLine($"PostRead ChangeLightColor Time: {(DateTime.UtcNow - time).TotalMilliseconds}");

				if (!_config.Model.piCameraSettings.isPi && (_config.Model.rgbDeviceSettings.useKeyboards || _config.Model.rgbDeviceSettings.useMice))
				{
					if (_rgbImageMemStream == null)
						_rgbImageMemStream = new MemoryStream(newWidth * newHeight * 3);
					_rgbImageMemStream.Seek(0, SeekOrigin.Begin);
					_blurImageMemStream.Seek(0, SeekOrigin.Begin);
					await _blurImageMemStream.CopyToAsync(_rgbImageMemStream);
					processingTasks.Add(Task.Run(() =>
					{
						_rgbLighter.UpdateFromImage(avgColor, _rgbImageMemStream, newWidth, newHeight);
					}));
				}

				if (_config.Model.dumpPngs)
				{
					try
					{
						time = DateTime.UtcNow;
						var path = Path.Combine(_config.Model.imageDumpLocation, $"{frame.ToString().PadLeft(6, '0')}.png");
						using var writeStream = File.OpenWrite(path);
						using var resizeImage = ImageHandler.ResizeImage(images.image, width, height);
						images.blurImage.Write(writeStream, MagickFormat.Png);
						//Console.WriteLine($"PostRead writeStream Time: {(DateTime.UtcNow - time).TotalMilliseconds}");
					}
					catch (Exception ex)
					{
						_ = Task.Run(() => _logger.WriteLog(ex.ToString()));
					}
				}

				await Task.WhenAll(processingTasks);

				//Console.WriteLine($"PostRead Total Time: {(DateTime.UtcNow - start).TotalMilliseconds}");
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
			finally
			{
				_blurImageMemStream.Seek(0, SeekOrigin.Begin);
				_smallImageMemStream.Seek(0, SeekOrigin.Begin);
				_hueImageMemStream.Seek(0, SeekOrigin.Begin);
				_rgbImageMemStream.Seek(0, SeekOrigin.Begin);
				_lstripImageMemStream.Seek(0, SeekOrigin.Begin);
				_processingFrame = false;
			}
		}
	}
}
