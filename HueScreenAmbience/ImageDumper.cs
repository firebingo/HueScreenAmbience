using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HueScreenAmbience
{
	public class ImageDumper
	{
		private Config _config;

		public ImageDumper(Config config)
		{
			_config = config;
		}

		public void DumpZonesToImage(PixelZone[] zones, int width, int height, string imageName)
		{
			try
			{
				using var memStream = new MemoryStream();
				using var binaryWriter = new BinaryWriter(memStream);

				var currentZone = 0;
				var maxColumn = zones.OrderByDescending(x => x.Column).First().Column + 1;
				var zoneRow = 0;
				for (var y = 0; y < height; ++y)
				{
					if (y > zones[currentZone].BottomRight.Y)
					{
						currentZone = ++zoneRow * maxColumn;
					}
					else
						currentZone = zoneRow * maxColumn;
					for (var x = 0; x < width; ++x)
					{
						if (x > zones[currentZone].BottomRight.X)
							currentZone++;

						binaryWriter.Write(zones[currentZone].AvgR);
						binaryWriter.Write(zones[currentZone].AvgG);
						binaryWriter.Write(zones[currentZone].AvgB);
					}
				}

				memStream.Seek(0, SeekOrigin.Begin);

				var settings = new MagickReadSettings
				{
					Width = width,
					Height = height,
					Depth = 8,
					Format = MagickFormat.Rgb,
					Compression = CompressionMethod.NoCompression
				};

				using var newimage = new MagickImage(memStream, settings)
				{
					Format = MagickFormat.Png
				};

				newimage.Write(Path.Combine(_config.Model.imageDumpLocation, imageName));
			}
			catch (Exception ex)
			{

			}
		}
	}
}
