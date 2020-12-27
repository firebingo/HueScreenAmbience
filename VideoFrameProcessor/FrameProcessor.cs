using BitmapZoneProcessor;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace VideoFrameProcessor
{
	public static class FrameProcessor
	{
		public static async Task ProcessFrame(Config config, MemoryStream stream, int frame, int frameLength, int width, int height, string outPath)
		{
			Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
			try
			{
				stream.Seek(0, SeekOrigin.Begin);
				var boundsRect = new Rectangle(0, 0, width, height);
				var bmpDest = bmp.LockBits(boundsRect, ImageLockMode.WriteOnly, bmp.PixelFormat);
				unsafe
				{
					byte* bp = (byte*)(void*)bmpDest.Scan0;
					stream.Seek(0, SeekOrigin.Begin);
					for (var i = 0; i < frameLength; i += 3)
					{
						bp[i] = (byte)stream.ReadByte();
						bp[i + 1] = (byte)stream.ReadByte();
						bp[i + 2] = (byte)stream.ReadByte();
					}
				}
				bmp.UnlockBits(bmpDest);
				var zones = SetupPixelZones(width, height, config.Model.ZoneRows, config.Model.ZoneColumns);
				BitmapProcessor.ReadBitmap(width, height, config.Model.ReadResolutionReduce, config.Model.ZoneRows, config.Model.ZoneColumns, 0, Array.Empty<ReadPixel>(), ref bmp, ref zones);

				foreach (var zone in zones)
				{
					zone.CalculateAverages();
				}

				var (image, blurImage) = BitmapProcessor.PreparePostBitmap(zones, config.Model.ZoneColumns, config.Model.ZoneRows, config.Model.ResizeScale, config.Model.ResizeFilter, config.Model.ResizeSigma, null);
				using var fileStream = File.Open(Path.Combine(outPath, $"out{frame.ToString().PadLeft(6, '0')}.png"), FileMode.OpenOrCreate, FileAccess.Write);
				await blurImage.WriteAsync(fileStream, ImageMagick.MagickFormat.Png);
				blurImage.Dispose();
				image.Dispose();
			}
			catch(Exception ex)
			{
				Console.WriteLine(ex);
			}
			finally
			{
				if(bmp!= null)
					bmp.Dispose();
				await stream.DisposeAsync();
			}
		}

		private static PixelZone[] SetupPixelZones(int width, int height, int rows, int columns)
		{
			var zones = new PixelZone[columns * rows];
			if (zones.Length == 0)
				throw new Exception("0 Light zones created");
			var row = 0;
			for (var i = 0; i < zones.Length; ++i)
			{
				var col = i % columns;
				var xMin = (width / (double)columns) * col;
				//If we are in the last column just set the bottom right to screen width so we dont get weird rounding where edge is not included
				var xMax = col == columns - 1
					? width
					: (width / (double)columns) * (col + 1);
				var yMin = (height / (double)rows) * row;
				//If we are in the last row just set the bottom right to screen height so we dont get weird rounding where edge is not included
				var yMax = row == rows - 1
					? height
					: (height / (double)rows) * (row + 1);
				zones[i] = new PixelZone(row, col, (int)Math.Ceiling(xMin), (int)Math.Ceiling(xMax), (int)Math.Ceiling(yMin), (int)Math.Ceiling(yMax));
				if (col == columns - 1)
					row += 1;
			}

			return zones;
		}
	}
}
