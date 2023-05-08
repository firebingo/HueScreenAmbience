using System;
using System.IO;

namespace BitmapZoneProcessor
{
	public static class BitmapProcessor
	{
		public static void ReadBitmap(MemoryStream image, int width, int height, int newWidth, int newHeight, float resReduce, int zoneRows, int zoneColumns, PixelZone[] zones,
			PixelZonesTotals zoneTotals, int bitDepth = 4, MemoryStream resizeStream = null, MemoryStream cropStream = null, SixLabors.ImageSharp.Rectangle? bitmapRect = null)
		{
			var start = DateTime.UtcNow;
			//Reset zones
			zoneTotals.ResetAverages();

			var useImage = image;
			//If this is passed it is expected that the zones passed are also based on this reduced resolution for now.
			// As the other comment below mentions maybe look into a better way of determining how reading the bitmap figures
			// out what zone to add values to.
			if (bitmapRect.HasValue)
			{
				useImage = ImageHandler.CropImageRect(useImage, width, height, cropStream, bitmapRect.Value, PixelFormat.Bgr32);
				width = bitmapRect.Value.Width;
				height = bitmapRect.Value.Height;
			}

			var t = DateTime.UtcNow;
			//It is expected that if we have a crop rect that newWidth and newHeight will be the values that would be reduced from the crop.
			// and that resizeStream is the correct length for these values.
			//Reducing the resolution of the desktop capture takes time but it saves a lot of time on reading the image
			//TODO: Investigate just skipping this and reading the bitmap as a function of the reduce resolution value as it would
			// effectivly produce the same result as point sampling to a lower resolution first. This could save memory pressure from
			// rescaling the image. But it may make determining what zone to add values to more complicated.
			if (width != newWidth || height != newHeight)
				useImage = ImageHandler.ResizeImage(useImage, width, height, resizeStream, newWidth, newHeight, pixelFormat: PixelFormat.Bgr32);
			//Console.WriteLine($"Resize Time:        {(DateTime.UtcNow - t).TotalMilliseconds}");

			//System.Threading.Tasks.Task.Run(() => ImageHandler.WriteImageToFile(useImage, newWidth, newHeight, $"Images/{DateTime.Now.Ticks.ToString().PadLeft(5, '0')}.png", pixelFormat: PixelFormat.Bgr32));

			t = DateTime.UtcNow;

			unsafe
			{
				var t1 = DateTime.UtcNow;

				fixed (byte* p = &(useImage.GetBuffer()[0]))
				{
					long pos = 0;
					byte* posP;
					fixed (PixelZone* z = &zones[0])
					{
						for (var i = 0; i < zones.Length; ++i)
						{
							for (var j = 0; j < z[i].Height; ++j)
							{
								for (var k = 0; k < z[i].PixelRanges[j].Length; k += bitDepth)
								{
									//Colors are in BGRA32 format
									pos = z[i].PixelRanges[j].Start + k;
									posP = &p[pos];
									*z[i].TotalB += posP[0];
									*z[i].TotalG += posP[1];
									*z[i].TotalR += posP[2];
								}
							}
						}
					}
				}

				//Console.WriteLine($"Read Bitmap Time:  {(DateTime.UtcNow - t1).TotalMilliseconds}");
			}
		}

		public static (MemoryStream image, MemoryStream blurImage) PreparePostBitmap(PixelZone[] zones, int columns, int rows, int newWidth, int newHeight, ImageFilter resizeFilter, float resizeSigma, MemoryStream smallImageMemStream, MemoryStream blurImageMemStream)
		{
			try
			{
				var image = ImageHandler.CreateSmallImageFromZones(zones, smallImageMemStream);

				var blurImage = ImageHandler.ResizeImage(image,
					columns,
					rows,
					blurImageMemStream,
					newWidth,
					newHeight,
					resizeFilter,
					resizeSigma,
					PixelFormat.Rgb24);

				return (image, blurImage);
			}
			catch
			{
				throw;
			}
		}
	}
}
