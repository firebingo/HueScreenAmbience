using HueScreenAmbience.Hue;
using HueScreenAmbience.Imaging;
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
		private ImageHandler _imageHandler;
		private FileLogger _logger;

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_hueClient = _map.GetService(typeof(HueCore)) as HueCore;
			_imageHandler = _map.GetService(typeof(ImageHandler)) as ImageHandler;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
		}

		public void PostRead(RGBLighter rgbLighter, PixelZone[] zones, int width, int height, long frame)
		{
			if (_processingFrame)
				return;
			_processingFrame = true;

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
				//using (var smallImage = _imageHandler.CreateSmallImageFromZones(zones))
				//{
				//	var path = Path.Combine(_config.Model.imageDumpLocation, $"{frame.ToString().PadLeft(6, '0')}_small.png");
				//	using var writeStream = File.OpenWrite(path);
				//	smallImage.Write(writeStream, MagickFormat.Png);
				//}

				//time = DateTime.UtcNow;
				//using var image = _imageHandler.CreateImageFromZones(zones, width, height, _imageMemStream);
				//Console.WriteLine($"PostRead CreateImageFromZones Time: {(DateTime.UtcNow - time).TotalMilliseconds}");
				time = DateTime.UtcNow;
				using var image = _imageHandler.CreateSmallImageFromZones(zones, columns, rows, _smallImageMemStream);
				//Console.WriteLine($"PostRead CreateSmallImageFromZones Time: {(DateTime.UtcNow - time).TotalMilliseconds}");
				time = DateTime.UtcNow;
				using var blurimage = _imageHandler.ResizeImage(image,
					(int)Math.Floor(columns * _config.Model.zoneProcessSettings.resizeScale),
					(int)Math.Floor(rows * _config.Model.zoneProcessSettings.resizeScale),
					_config.Model.zoneProcessSettings.resizeFilter,
					_config.Model.zoneProcessSettings.resizeSigma);
				//Console.WriteLine($"PostRead ResizeImage Time: {(DateTime.UtcNow - time).TotalMilliseconds}");

				if (image == null)
				{
					Console.WriteLine($"f:{frame} Image is null. Check log");
					return;
				}

				int avgR = zones.Sum(x => x.AvgR) / zones.Length;
				int avgG = zones.Sum(x => x.AvgG) / zones.Length;
				int avgB = zones.Sum(x => x.AvgB) / zones.Length;
				var avgColor = Color.FromArgb(255, avgR, avgG, avgB);

				time = DateTime.UtcNow;
				if (_config.Model.hueSettings.hueType == HueType.Basic)
				{
					
					Task.Run(() => _hueClient.ChangeLightColorBasic(avgColor));
				}
				else if (_config.Model.hueSettings.hueType == HueType.Entertainment)
				{
					var hueImage = new MagickImage(blurimage);
					Task.Run(() =>
					{
						_hueClient.UpdateEntertainmentGroupFromImage(hueImage);
						hueImage.Dispose();
					});
				}
				//Console.WriteLine($"PostRead ChangeLightColor Time: {(DateTime.UtcNow - time).TotalMilliseconds}");

				if (_config.Model.rgbDeviceSettings.useKeyboards)
				{
					var rgbImage = new MagickImage(blurimage);
					Task.Run(() =>
					{
						rgbLighter.UpdateFromImage(avgColor, rgbImage, frame);
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
						using var resizeImage = _imageHandler.ResizeImage(image, width, height);
						blurimage.Write(writeStream, MagickFormat.Png);
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
			}
		}
	}
}
