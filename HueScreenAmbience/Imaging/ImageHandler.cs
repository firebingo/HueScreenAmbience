using ImageMagick;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HueScreenAmbience.Imaging
{
	public class ImageHandler
	{
		private readonly Config _config;
		private readonly FileLogger _logger;

		public ImageHandler(Config config, FileLogger logger)
		{
			_config = config;
			_logger = logger;
		}

		public Bitmap ResizeBitmapImage(Bitmap bmp, int width, int height)
		{
			Bitmap result = new Bitmap(width, height);
			using (Graphics g = Graphics.FromImage(result))
			{
				g.DrawImage(bmp, 0, 0, width, height);
			}

			return result;
		}

		public MagickImage CreateSmallImageFromZones(PixelZone[] zones, int columns, int rows, MemoryStream memStream)
		{
			try
			{
				var memStreamExists = false;
				if (memStream == null)
					memStream = new MemoryStream();
				else
					memStreamExists = true;
				using var binaryWriter = new BinaryWriter(memStream, Encoding.Default, memStreamExists);

				for (var i = 0; i < zones.Length; ++i)
				{
					binaryWriter.Write(zones[i].AvgR);
					binaryWriter.Write(zones[i].AvgG);
					binaryWriter.Write(zones[i].AvgB);
				}

				memStream.Seek(0, SeekOrigin.Begin);

				var settings = new MagickReadSettings
				{
					Width = columns,
					Height = rows,
					Depth = 8,
					Format = MagickFormat.Rgb,
					Compression = CompressionMethod.NoCompression
				};
				var image = new MagickImage(memStream, settings);

				if (!memStreamExists)
					memStream.Dispose();

				return image;
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}

			return null;
		}

		public MagickImage CreateImageFromZones(PixelZone[] zones, int width, int height, MemoryStream memStream = null)
		{
			try
			{
				//var start = DateTime.UtcNow;
				var memStreamExists = false;
				if (memStream == null)
					memStream = new MemoryStream();
				else
					memStreamExists = true;
				using var binaryWriter = new BinaryWriter(memStream, Encoding.Default, memStreamExists);

				ConcurrentDictionary<int, byte[]> bytes = new ConcurrentDictionary<int, byte[]>();
				Parallel.For(0, height, y =>
				{
					var currentZone = 0;
					//Get the zone index for the current scanline
					for (var i = 0; i < zones.Length; ++i)
					{
						if (zones[i].IsCoordInZone(0, y))
						{
							currentZone = i;
							break;
						}
					}
					bytes.TryAdd(y, new byte[width * 3]);
					var scan = bytes[y];
					var iter = 0;
					for (var x = 0; x < width; ++x)
					{
						if (x > zones[currentZone].BottomRight.X)
							currentZone++;
						scan[iter] = zones[currentZone].AvgR;
						scan[iter + 1] = zones[currentZone].AvgG;
						scan[iter + 2] = zones[currentZone].AvgB;
						iter += 3;
					}
				});
				foreach (var b in bytes.OrderBy(x => x.Key))
				{
					binaryWriter.Write(b.Value);
				}

				//var t1 = DateTime.UtcNow;
				//Console.WriteLine($"Zone Loop Time: {(DateTime.UtcNow - start).TotalMilliseconds}");

				memStream.Seek(0, SeekOrigin.Begin);

				var settings = new MagickReadSettings
				{
					Width = width,
					Height = height,
					Depth = 8,
					Format = MagickFormat.Rgb,
					Compression = CompressionMethod.NoCompression
				};
				var image = new MagickImage(memStream, settings);

				//Console.WriteLine($"Image Build Time: {(DateTime.UtcNow - t1).TotalMilliseconds}");

				if (!memStreamExists)
					memStream.Dispose();

				return image;
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}

			return null;
		}

		public MagickImage ResizeImage(MagickImage image, int width, int height, FilterType filter = FilterType.Point, double sigma = 0.5)
		{
			var newImage = new MagickImage(image)
			{
				FilterType = filter
			};
			var geo = new MagickGeometry(width, height)
			{
				IgnoreAspectRatio = true,
				FillArea = true
			};
			newImage.SetArtifact("filter:sigma", sigma.ToString());
			newImage.Resize(geo);
			return newImage;
		}

		public void DumpZonesToImage(PixelZone[] zones, int width, int height, string imageName)
		{
			try
			{
				var image = CreateImageFromZones(zones, width, height);

				//var start = DateTime.UtcNow;
				image.Write(Path.Combine(_config.Model.imageDumpLocation, imageName), MagickFormat.Png);
				//Console.WriteLine($"Image Write Time: {(DateTime.UtcNow - start).TotalMilliseconds}");

				image.Dispose();
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
		}
	}
}
