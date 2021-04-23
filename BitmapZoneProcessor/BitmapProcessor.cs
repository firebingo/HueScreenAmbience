using System;
using System.IO;

namespace BitmapZoneProcessor
{
	public static class BitmapProcessor
	{
		public static void ReadBitmap(MemoryStream image, int width, int height, int newWidth, int newHeight, float resReduce, int zoneRows, int zoneColumns, ref PixelZone[] zones,
			MemoryStream resizeStream = null, MemoryStream cropStream = null, SixLabors.ImageSharp.Rectangle? bitmapRect = null)
		{
			var start = DateTime.UtcNow;
			//Reset zones
			foreach (var zone in zones)
			{
				zone.Count = 0;
				zone.TotalR = 0;
				zone.TotalG = 0;
				zone.TotalB = 0;
				zone.ResetAverages();
			}

			var useImage = image;
			//If this is passed it is expected that the zones passed are also based on this reduced resolution for now.
			// As the other comment below mentions maybe look into a better way of determining how reading the bitmap figures
			// out what zone to add values to.
			if (bitmapRect.HasValue)
			{
				useImage = ImageHandler.CropImageRect(useImage, width, height, cropStream, bitmapRect.Value);
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
				useImage = ImageHandler.ResizeImage(useImage, width, height, resizeStream, newWidth, newHeight, pixelFormat: PixelFormat.Rgba32);
			//Console.WriteLine($"Resize Time:        {(DateTime.UtcNow - t).TotalMilliseconds}");

			//using (var fi = File.OpenWrite($"Images/{DateTime.Now.Ticks.ToString().PadLeft(5, '0')}.jpeg"))
			//	bmp.Save(fi, ImageFormat.Jpeg);

			t = DateTime.UtcNow;

			unsafe
			{
				var t1 = DateTime.UtcNow;
				//Console.WriteLine($"Build Bitmap Time: {(t1 - start).TotalMilliseconds}");

				var pixLength = 4;

				fixed (byte* p = &(useImage.GetBuffer()[0]))
				{
					//Colors in are bitmap format are 32bpp so 4 bytes for each color in BGRA format
					var totalSize = newWidth * newHeight * pixLength;
					bool oneZone = !(zones.Length > 1);
					int currentZone = 0;
					var zone = zones[currentZone];
					int zoneRow = 0;
					int xIter = 0;
					int yIter = 0;
					for (var i = 0; i < totalSize; i += pixLength)
					{
						//index is our power of 4 padded index in the bitmap.
						zone.TotalB += p[i]; //b
						zone.TotalG += p[i + 1]; //g
						zone.TotalR += p[i + 2]; //r
						zone.Count++;

						//If we only have one row we do want to process this for the last zone still
						if (!oneZone && (zoneRows == 1 || currentZone != zones.Length - 1))
						{
							//If x is greater than zone width
							if (++xIter >= zone.Width)
							{
								xIter = 0;
								//If we are on the last column for this row
								if (zone.Column == zoneColumns - 1)
								{
									//If our y is greater than this rows height
									// reset y and advance us to the next row
									//Dont do this check if we only have one row
									if (zoneRows != 1 && ++yIter >= zone.Height)
									{
										yIter = 0;
										currentZone = ++zoneRow * zoneColumns;
										zone = zones[currentZone];
									}
									//Else reset us back to the start of the current row
									else
									{
										currentZone = zoneRow * zoneColumns;
										zone = zones[currentZone];
									}
								}
								//Else move to the next column
								else
									zone = zones[++currentZone];
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
