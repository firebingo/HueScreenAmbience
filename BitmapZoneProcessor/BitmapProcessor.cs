using System;
using System.IO;
using System.Runtime.Intrinsics;

namespace BitmapZoneProcessor
{
	public static class BitmapProcessor
	{
		public static void ReadBitmap(MemoryStream image, int width, int height, int newWidth, int newHeight, float resReduce, int zoneRows, int zoneColumns, PixelZone[] zones,
			PixelZonesTotals zoneTotals, int bitDepth = 4, MemoryStream resizeStream = null, MemoryStream cropStream = null, SixLabors.ImageSharp.Rectangle? bitmapRect = null,
			bool useIntrinsics = false)
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

				fixed (byte* p = useImage.GetBuffer())
				{
					long totB = 0;
					long totG = 0;
					long totR = 0;
					byte* rowPtr;
					byte* rowEnd;

					for (var i = 0; i < zones.Length; ++i)
					{
						var zone = zones[i];
						var ranges = zone.PixelRanges;
						var zoneHeight = zone.Height;
						totB = totG = totR = 0;

						if (bitDepth == 4 && Vector128.IsHardwareAccelerated && useIntrinsics)
						{
							for (var j = 0; j < zoneHeight; ++j)
							{
								var range = ranges[j];
								rowPtr = p + range.Start;
								rowEnd = rowPtr + range.Length;

								//SIMD accumulator vector containing 4x uint32 channels: [TotalB, TotalG, TotalR, TotalA].
								var vTotals = Vector128<uint>.Zero;

								//Process 4 pixels (16 contiguous bytes: [B0, G0, R0, A0, B1, G1, R1, A1, B2, G2, R2, A2, B3, G3, R3, A3]) per iteration.
								while (rowPtr + 16 <= rowEnd)
								{
									//Load 16 bytes into a 128-bit SIMD register.
									var vBytes = Vector128.Load(rowPtr);

									//Widen 8-bit bytes to 16-bit ushorts to prevent overflow during addition.
									var (low16, high16) = Vector128.Widen(vBytes);
									//Further widen 16-bit ushorts to 32-bit uints for accumulation.
									var (low32_0, low32_1) = Vector128.Widen(low16);
									var (high32_0, high32_1) = Vector128.Widen(high16);

									//Sum all 4 pixels into the SIMD channel accumulator.
									vTotals += (low32_0 + low32_1) + (high32_0 + high32_1);
									rowPtr += 16;
								}

								//Extract accumulated Blue, Green, and Red totals (elements 0, 1, 2) from SIMD register into scalar registers.
								totB += vTotals.GetElement(0);
								totG += vTotals.GetElement(1);
								totR += vTotals.GetElement(2);

								//Process any leftover pixels (fewer than 4 pixels / 16 bytes) at the end of the row.
								while (rowPtr < rowEnd)
								{
									totB += rowPtr[0];
									totG += rowPtr[1];
									totR += rowPtr[2];
									rowPtr += 4;
								}
							}
						}
						else
						{
							for (var j = 0; j < zoneHeight; ++j)
							{
								var range = ranges[j];
								rowPtr = p + range.Start;
								rowEnd = rowPtr + range.Length;
								while (rowPtr < rowEnd)
								{
									totB += rowPtr[0];
									totG += rowPtr[1];
									totR += rowPtr[2];
									rowPtr += bitDepth;
								}
							}
						}
						
						*zone.TotalB += (int)totB;
						*zone.TotalG += (int)totG;
						*zone.TotalR += (int)totR;
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
