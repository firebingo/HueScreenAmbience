using BitmapZoneProcessor;
using System;
using System.IO;
using System.Threading.Tasks;

namespace VideoFrameProcessor
{
	public static class FrameProcessor
	{
		public static async Task ProcessFrame(Config config, MemoryStream stream, int frame, int width, int height, string outPath)
		{
			MemoryStream? resizeMemStream = null;
			try
			{
				stream.Seek(0, SeekOrigin.Begin);
				var (zones, zoneTotals) = SetupPixelZones(width, height, config.Model.ZoneRows, config.Model.ZoneColumns, config.Model.ReadSkipPixels);
				BitmapProcessor.ReadBitmap(stream, config.Model.ReadSkipPixels, config.Model.ZoneRows, config.Model.ZoneColumns, zones, zoneTotals, 4);

				zoneTotals.CalculateAverages();

				width = (int)Math.Floor(config.Model.ZoneColumns * config.Model.ResizeScale);
				height = (int)Math.Floor(config.Model.ZoneRows * config.Model.ResizeScale);

				var image = new MemoryStream(config.Model.ZoneColumns * config.Model.ZoneRows * 3);
				var blurImage = new MemoryStream(width * height * 3);

				(image, blurImage) = BitmapProcessor.PreparePostBitmap(zones, config.Model.ZoneColumns, config.Model.ZoneRows, width, height, config.Model.ResizeFilter, config.Model.ResizeSigma, image, blurImage);
				await ImageHandler.WriteImageToFile(blurImage, width, height, Path.Combine(outPath, $"out{frame.ToString().PadLeft(6, '0')}.png"), pixelFormat: PixelFormat.Rgb24);
				await blurImage.DisposeAsync();
				await image.DisposeAsync();
				foreach (var zone in zones)
				{
					zone.Dispose();
				}
				zoneTotals.Dispose();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			finally
			{
				if (resizeMemStream != null)
					await resizeMemStream.DisposeAsync();
				await stream.DisposeAsync();
			}
		}

		private static (PixelZone[] zones, PixelZonesTotals zoneTotals) SetupPixelZones(int width, int height, int rows, int columns, int skipPixels)
		{
			var zoneTotals = new PixelZonesTotals(columns * rows);
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
				zones[i] = new PixelZone(row, col, (int)Math.Ceiling(xMin), (int)Math.Ceiling(xMax), (int)Math.Ceiling(yMin), (int)Math.Ceiling(yMax), width * 4, 4, skipPixels, zoneTotals, i);
				if (col == columns - 1)
					row += 1;
			}

			return (zones, zoneTotals);
		}
	}
}
