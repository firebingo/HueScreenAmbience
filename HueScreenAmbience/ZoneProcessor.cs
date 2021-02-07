using BitmapZoneProcessor;
using HueScreenAmbience.Hue;
using HueScreenAmbience.LightStrip;
using HueScreenAmbience.RGB;
using ImageMagick;
using System;
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
			if(!_config.Model.piCameraSettings.isPi)
				_rgbLighter = _map.GetService(typeof(RGBLighter)) as RGBLighter;
			_stripLighter = _map.GetService(typeof(StripLighter)) as StripLighter;
		}

		public void PostRead(PixelZone[] zones, int width, int height, long frame)
		{
			if (_processingFrame)
				return;
			_processingFrame = true;

			(MagickImage image, MagickImage blurImage) images = (null, null);
			var columns = zones.OrderByDescending(x => x.Column).First().Column + 1;
			var rows = zones.OrderByDescending(x => x.Row).First().Row + 1;

			//Pre allocate the memory stream for images since it will be the same size every time
			if (_smallImageMemStream == null)
				_smallImageMemStream = new MemoryStream(columns * rows);

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
				images = BitmapProcessor.PreparePostBitmap(zones, columns, rows, _config.Model.zoneProcessSettings.resizeScale, _config.Model.zoneProcessSettings.resizeFilter, _config.Model.zoneProcessSettings.resizeSigma, _smallImageMemStream);

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

				//This is a little spaghet but if hue and a light strip are enabled we can have them use the same
				// image since they dont modify it instead of creating 2 images and getting a byte array of it twice.
				if(_config.Model.hueSettings.useHue && _config.Model.lightStripSettings.useLightStrip)
				{
					var lightImage = new MagickImage(images.blurImage);

					if (_config.Model.hueSettings.hueType == HueType.Basic)
					{
						Task.Run(() => _hueClient.ChangeLightColorBasic(avgColor));

						Task.Run(() =>
						{
							using var pixels = lightImage.GetPixelsUnsafe();
							_stripLighter.UpdateFromImage(pixels, lightImage.Width, lightImage.Height, frame);
							lightImage.Dispose();
						});
					}
					else if(_config.Model.hueSettings.hueType == HueType.Entertainment)
					{
						Task.Run(() =>
						{
							using var pixels = lightImage.GetPixelsUnsafe();
							_stripLighter.UpdateFromImage(pixels, lightImage.Width, lightImage.Height, frame);
							_hueClient.UpdateEntertainmentGroupFromImage(pixels, lightImage.Width, lightImage.Height);
							lightImage.Dispose();
						});
					}
				}
				else if(_config.Model.hueSettings.useHue)
				{
					if (_config.Model.hueSettings.hueType == HueType.Basic)
					{
						Task.Run(() => _hueClient.ChangeLightColorBasic(avgColor));
					}
					else if (_config.Model.hueSettings.hueType == HueType.Entertainment)
					{
						var hueImage = new MagickImage(images.blurImage);
						Task.Run(() =>
						{
							using var pixels = hueImage.GetPixelsUnsafe();
							_hueClient.UpdateEntertainmentGroupFromImage(pixels, hueImage.Width, hueImage.Height);
							hueImage.Dispose();
						});
					}
				}
				else if(_config.Model.lightStripSettings.useLightStrip)
				{
					var stripImage = new MagickImage(images.blurImage);
					Task.Run(() =>
					{
						using var pixels = stripImage.GetPixelsUnsafe();
						_stripLighter.UpdateFromImage(pixels, stripImage.Width, stripImage.Height, frame);
						stripImage.Dispose();
					});
				}

				//Console.WriteLine($"PostRead ChangeLightColor Time: {(DateTime.UtcNow - time).TotalMilliseconds}");

				if (!_config.Model.piCameraSettings.isPi && (_config.Model.rgbDeviceSettings.useKeyboards || _config.Model.rgbDeviceSettings.useMice))
				{
					var rgbImage = new MagickImage(images.blurImage);
					Task.Run(() =>
					{
						_rgbLighter.UpdateFromImage(avgColor, rgbImage);
						rgbImage.Dispose();
					});
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
						Task.Run(() => _logger.WriteLog(ex.ToString()));
					}
				}

				//Console.WriteLine($"PostRead Total Time: {(DateTime.UtcNow - start).TotalMilliseconds}");
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
			finally
			{
				_smallImageMemStream.Seek(0, SeekOrigin.Begin);
				_processingFrame = false;
				if (images.image != null)
					images.image.Dispose();
				if (images.blurImage != null)
					images.blurImage.Dispose();
			}
		}
	}
}
