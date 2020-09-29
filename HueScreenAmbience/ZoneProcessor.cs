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
		private MemoryStream _imageMemStream;
		private Config _config;
		private Core _core;
		private ImageHandler _imageHandler;
		private FileLogger _logger;

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_core = _map.GetService(typeof(Core)) as Core;
			_imageHandler = _map.GetService(typeof(ImageHandler)) as ImageHandler;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
		}

		public void PostRead(PixelZone[] zones, int width, int height, long frame)
		{
			if (_processingFrame)
				return;
			_processingFrame = true;

			//Pre allocate the memory stream for images since it will be the same size every time
			if (_imageMemStream == null)
				_imageMemStream = new MemoryStream(width * height * 3);

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

				using var image = _imageHandler.CreateImageFromZones(zones, width, height, _imageMemStream);
				//Console.WriteLine($"PostRead CreateImageFromZones Time: {(DateTime.UtcNow - time).TotalMilliseconds}");

				if (image == null)
				{
					Console.WriteLine($"f:{frame} Image is null. Check log");
					return;
				}

				time = DateTime.UtcNow;
				int avgR = zones.Sum(x => x.AvgR) / zones.Length;
				int avgG = zones.Sum(x => x.AvgG) / zones.Length;
				int avgB = zones.Sum(x => x.AvgB) / zones.Length;
				var avg = Color.FromArgb(255, avgR, avgG, avgB);
				Task.Run(() => _core.ChangeLightColor(avg));
				//Console.WriteLine($"PostRead ChangeLightColor Time: {(DateTime.UtcNow - time).TotalMilliseconds}");

				if (_config.Model.dumpPngs)
				{
					try
					{
						time = DateTime.UtcNow;
						var path = Path.Combine(_config.Model.imageDumpLocation, $"{frame.ToString().PadLeft(6, '0')}.png");
						using var writeStream = File.OpenWrite(path);
						image.Write(writeStream, MagickFormat.Png);
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
				_imageMemStream.Seek(0, SeekOrigin.Begin);
				_processingFrame = false;
			}
		}
	}
}
