using System;
using System.IO;

namespace BitmapZoneProcessor
{
	public static class BitmapProcessor
	{
		public static void ReadBitmap(MemoryStream image, int skipPixels, int zoneRows, int zoneColumns, PixelZone[] zones, PixelZonesTotals zoneTotals, int bitDepth = 4)
		{
			var start = DateTime.UtcNow;
			//Reset zones
			zoneTotals.ResetAverages();

			var useImage = image;

			//System.Threading.Tasks.Task.Run(() => ImageHandler.WriteImageToFile(useImage, newWidth, newHeight, $"Images/{DateTime.Now.Ticks.ToString().PadLeft(5, '0')}.png", pixelFormat: PixelFormat.Bgr32));

			var t = DateTime.UtcNow;

			unsafe
			{
				var t1 = DateTime.UtcNow;

				fixed (byte* p = &(useImage.GetBuffer()[0]))
				{
					long pos = 0;
					byte* posP;
					int iter = bitDepth * skipPixels;
					fixed (PixelZone* z = &zones[0])
					{
						for (var i = 0; i < zones.Length; ++i)
						{
							for (var j = 0; j < z[i].Height; ++j)
							{	
								for (var k = 0; k < z[i].PixelRanges[j].Length; k += iter)
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
