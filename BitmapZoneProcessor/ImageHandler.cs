using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace BitmapZoneProcessor
{
	public enum ImageFilter
	{
		Point = 0,
		Bilinear = 1,
		Gaussian = 2
	}

	public static class ImageHandler
	{
		private static readonly ConcurrentDictionary<string, ResizeOptions> _resizeOptionCache;

		static ImageHandler()
		{
			_resizeOptionCache = new ConcurrentDictionary<string, ResizeOptions>();
		}

		public static Bitmap ResizeBitmapImage(Bitmap bmp, int width, int height)
		{
			Bitmap result = new Bitmap(width, height);
			using (Graphics g = Graphics.FromImage(result))
			{
				g.DrawImage(bmp, 0, 0, width, height);
			}

			return result;
		}

		public static Bitmap CropBitmapRect(Bitmap bmp, System.Drawing.Rectangle rect)
		{
			PixelFormat format = bmp.PixelFormat;
			return bmp.Clone(rect, format);
		}

		public static MemoryStream CreateSmallImageFromZones(PixelZone[] zones, MemoryStream memStream)
		{
			if (memStream == null)
				memStream = new MemoryStream();

			for (var i = 0; i < zones.Length; ++i)
			{
				memStream.WriteByte(zones[i].AvgR);
				memStream.WriteByte(zones[i].AvgG);
				memStream.WriteByte(zones[i].AvgB);
			}

			memStream.Seek(0, SeekOrigin.Begin);

			return memStream;
		}
		public static MemoryStream ResizeImage(MemoryStream image, int width, int height, MemoryStream newImage, int newWidth, int newHeight, ImageFilter filter = ImageFilter.Point, double sigma = 0.5)
		{
			var resizeKey = $"{newWidth}{newHeight}{filter}{sigma}";
			if (!_resizeOptionCache.TryGetValue(resizeKey, out var options))
			{
				options = new ResizeOptions()
				{
					Size = new SixLabors.ImageSharp.Size(newWidth, newHeight),
					Mode = ResizeMode.Manual,
					Sampler = ImageFilterToResampler(filter),
					TargetRectangle = new SixLabors.ImageSharp.Rectangle(0, 0, newWidth, newHeight),
					Compand = false
				};
				_resizeOptionCache.TryAdd(resizeKey, options);
			}
			image.Seek(0, SeekOrigin.Begin);
			var simage = SixLabors.ImageSharp.Image.LoadPixelData<Rgb24>(image.GetBuffer(), width, height);
			switch (filter)
			{
				case ImageFilter.Gaussian:
					simage.Mutate(x => x.Resize(options));
					simage.Mutate(x => x.GaussianBlur((float)sigma));
					break;
				default:
					simage.Mutate(x => x.Resize(options));
					break;
			}
			if (simage.TryGetSinglePixelSpan(out var pixelSpan))
			{
				newImage.Seek(0, SeekOrigin.Begin);
				newImage.Write(MemoryMarshal.AsBytes(pixelSpan));
			}

			simage.Dispose();
			return newImage;
		}

		public static IResampler ImageFilterToResampler(ImageFilter filter)
		{
			return filter switch
			{
				ImageFilter.Bilinear => KnownResamplers.Triangle,
				_ => KnownResamplers.NearestNeighbor
			};
		}

		public static async Task WriteImageToFile(MemoryStream image, int width, int height, string path, int? resizeWidth = null, int? resizeHeight = null)
		{
			image.Seek(0, SeekOrigin.Begin);
			var simage = SixLabors.ImageSharp.Image.LoadPixelData<Rgb24>(image.GetBuffer(), width, height);
			if (resizeWidth.HasValue && resizeHeight.HasValue)
				simage.Mutate(x => x.Resize(resizeWidth.Value, resizeHeight.Value, KnownResamplers.NearestNeighbor));

			await simage.SaveAsPngAsync(path);
			simage.Dispose();
		}
	}
}
