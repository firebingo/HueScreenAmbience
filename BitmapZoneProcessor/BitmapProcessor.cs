//using ImageMagick;
using ImageMagickProcessor;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace BitmapZoneProcessor
{
	public static class BitmapProcessor
	{
		public static bool ReadBitmap(int width, int height, float readResolutionReduce, int zoneRows, int zoneColumns, int pixelCount, ReadPixel[] readPixels, ref Bitmap bmp, ref PixelZone[] zones, bool disposeBitmapOnChange = true, Rectangle? bitmapRect = null)
		{
			bool bitmapChanged = false;
			//Reset zones
			foreach (var zone in zones)
			{
				zone.Count = 0;
				zone.TotalR = 0;
				zone.TotalG = 0;
				zone.TotalB = 0;
				zone.ResetAverages();
			}

			if (bmp.PixelFormat != PixelFormat.Format32bppArgb && bmp.PixelFormat != PixelFormat.Format24bppRgb)
				throw new Exception("Bitmap must be either 32bppArgb or 24bppRgb");

			if (bitmapRect.HasValue)
			{
				if (disposeBitmapOnChange)
				{
					var oldBmp = bmp;
					bmp = ImageHandler.CropBitmapRect(oldBmp, bitmapRect.Value);
					oldBmp.Dispose();
				}
				else
				{
					bmp = ImageHandler.CropBitmapRect(bmp, bitmapRect.Value);
				}
				bitmapChanged = true;
			}

			var t = DateTime.UtcNow;
			//Reducing the resolution of the desktop capture takes time but it saves a lot of time on reading the image
			if (readResolutionReduce > 1.0f)
			{
				if (disposeBitmapOnChange)
				{
					var oldBmp = bmp;
					bmp = ImageHandler.CropBitmapRect(oldBmp, bitmapRect.Value);
					oldBmp.Dispose();
				}
				else
				{
					bmp = ImageHandler.ResizeBitmapImage(bmp, width, height);
				}
				bitmapChanged = true;
			}
			//Console.WriteLine($"Resize Time:        {(DateTime.UtcNow - t).TotalMilliseconds}");

			//using (var fi = System.IO.File.OpenWrite($"Images/i{_frame.ToString().PadLeft(5, '0')}.png"))
			//	bmp.Save(fi, ImageFormat.Png);

			t = DateTime.UtcNow;
			BitmapData srcData = bmp.LockBits(
			new Rectangle(0, 0, bmp.Width, bmp.Height),
			ImageLockMode.ReadOnly,
			bmp.PixelFormat);

			unsafe
			{
				//var t1 = DateTime.UtcNow;
				//Console.WriteLine($"Build Bitmap Time: {(t1 - start).TotalMilliseconds}");

				var totalSize = readPixels.Length;
				var pixLength = bmp.PixelFormat == PixelFormat.Format32bppArgb ? 4 : 3;

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
					for (var i = 0; i < totalSize * pixLength; i += pixLength)
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
				else
				{
					int pixIndex = 0;
					for (var i = 0; i < totalSize; ++i)
					{
						//y * stride gives us the offset for the scanline we are in on the bitmap (ex. line 352 * 1080 = 380160 bits)
						//x * 4 gives us our power of 4 for column
						//ex. total offset for coord 960x540 on a 1080p image is is (540 * 1080) + 960 * 4 = 587040 bits
						pixIndex = (readPixels[i].Pixel.Y * srcData.Stride) + readPixels[i].Pixel.X * pixLength;
						readPixels[i].Zone.TotalR += p[pixIndex + 2];
						readPixels[i].Zone.TotalG += p[pixIndex + 1];
						readPixels[i].Zone.TotalB += p[pixIndex];
						readPixels[i].Zone.Count++;
					}
				}

				//t2 = DateTime.UtcNow;
				//Console.WriteLine($"Read Bitmap Time:  {(t2 - t1).TotalMilliseconds}");
			}
			bmp.UnlockBits(srcData);
			return bitmapChanged;
		}

		public static async Task<(MemoryStream image, MemoryStream blurImage)> PreparePostBitmap(PixelZone[] zones, int columns, int rows, int newWidth, int newHeight, FilterType resizeFilter, float resizeSigma, MemoryStream smallImageMemStream, MemoryStream blurImageMemStream)
		{
			try
			{
				//var newWidth = (int)Math.Floor(columns * resizeScale);
				//var newHeight = (int)Math.Floor(rows * resizeScale);

				var image = ImageHandler.CreateSmallImageFromZones(zones, columns, rows, smallImageMemStream);

				var blurImage = await ImageHandler.ResizeImage(image,
					columns,
					rows,
					blurImageMemStream,
					newWidth,
					newHeight,
					resizeFilter,
					resizeSigma);

				return (image, blurImage);
			}
			catch
			{
				throw;
			}
		}
	}
}
