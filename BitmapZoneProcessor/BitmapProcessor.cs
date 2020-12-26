using ImageMagick;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BitmapZoneProcessor
{
	public static class BitmapProcessor
	{
		public static void ReadBitmap(int screenWidth, int screenHeight, float readResolutionReduce, int zoneRows, int zoneColumns, int pixelCount, ReadPixel[] readPixels, ref Bitmap bmp, ref PixelZone[] zones, Rectangle? bitmapRect = null)
		{
			//Reset zones
			foreach (var zone in zones)
			{
				zone.Count = 0;
				zone.Totals[0] = 0;
				zone.Totals[1] = 0;
				zone.Totals[2] = 0;
				zone.ResetAverages();
			}

			if (bitmapRect.HasValue)
			{
				var oldBmp = bmp;
				bmp = ImageHandler.CropBitmapRect(oldBmp, bitmapRect.Value);
				oldBmp.Dispose();
			}

			var t = DateTime.UtcNow;
			//Reducing the resolution of the desktop capture takes time but it saves a lot of time on reading the image
			if (readResolutionReduce > 1.0f)
			{
				var oldBmp = bmp;
				bmp = ImageHandler.ResizeBitmapImage(oldBmp, screenWidth, screenHeight);
				oldBmp.Dispose();
			}
			//Console.WriteLine($"Resize Time:        {(DateTime.UtcNow - t).TotalMilliseconds}");

			//using (var fi = System.IO.File.OpenWrite($"Images/i{_frame.ToString().PadLeft(5, '0')}.png"))
			//	bmp.Save(fi, ImageFormat.Png);

			t = DateTime.UtcNow;
			BitmapData srcData = bmp.LockBits(
			new Rectangle(0, 0, bmp.Width, bmp.Height),
			ImageLockMode.ReadOnly,
			PixelFormat.Format32bppArgb);

			unsafe
			{
				//var t1 = DateTime.UtcNow;
				//Console.WriteLine($"Build Bitmap Time: {(t1 - start).TotalMilliseconds}");

				var totalSize = readPixels.Length;

				byte* p = (byte*)(void*)srcData.Scan0;
				//Colors in are bitmap format are 32bpp so 4 bytes for each color in RGBA format
				//If we are set to 0 just read the full screen
				//This is reccomended if reducing the resolution as it is fast enough and produces better results
				if (pixelCount == 0 || readPixels.Length == 0)
				{
					totalSize = srcData.Width * srcData.Height;
					bool oneZone = !(zones.Length > 1);
					int currentZone = 0;
					var zone = zones[currentZone];
					int zoneRow = 0;
					int xIter = 0;
					int yIter = 0;
					for (var i = 0; i < totalSize * 4; i += 4)
					{
						//index is our power of 4 padded index in the bitmap.
						zone.Totals[2] += p[i]; //b
						zone.Totals[1] += p[i + 1]; //g
						zone.Totals[0] += p[i + 2]; //r
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
				else
				{
					int pixIndex = 0;
					for (var i = 0; i < totalSize; ++i)
					{
						//y * stride gives us the offset for the scanline we are in on the bitmap (ex. line 352 * 1080 = 380160 bits)
						//x * 4 gives us our power of 4 for column
						//ex. total offset for coord 960x540 on a 1080p image is is (540 * 1080) + 960 * 4 = 587040 bits
						pixIndex = (readPixels[i].Pixel.Y * srcData.Stride) + readPixels[i].Pixel.X * 4;
						readPixels[i].Zone.Totals[0] += p[pixIndex + 2];
						readPixels[i].Zone.Totals[1] += p[pixIndex + 1];
						readPixels[i].Zone.Totals[2] += p[pixIndex];
						readPixels[i].Zone.Count++;
					}
				}

				//t2 = DateTime.UtcNow;
				//Console.WriteLine($"Read Bitmap Time:  {(t2 - t1).TotalMilliseconds}");
			}
			bmp.UnlockBits(srcData);
		}

		public static (MagickImage image, MagickImage blurImage) PreparePostBitmap(PixelZone[] zones, int columns, int rows, float resizeScale, FilterType resizeFilter, float resizeSigma, MemoryStream smallImageMemStream)
		{
			var image = ImageHandler.CreateSmallImageFromZones(zones, columns, rows, smallImageMemStream);
			var blurImage = ImageHandler.ResizeImage(image,
				(int)Math.Floor(columns * resizeScale),
				(int)Math.Floor(rows * resizeScale),
				resizeFilter,
				resizeSigma);

			return (image, blurImage);
		}
	}
}
