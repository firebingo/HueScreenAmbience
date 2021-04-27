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

	public enum PixelFormat
	{
		Rgb24,
		Rgba32,
		Bgr32,
	}

	public static class ImageHandler
	{
		private static readonly ConcurrentDictionary<string, ResizeOptions> _resizeOptionCache;

		static ImageHandler()
		{
			_resizeOptionCache = new ConcurrentDictionary<string, ResizeOptions>();
		}

		public static MemoryStream CropImageRect(MemoryStream image, int width, int height, MemoryStream newImage, Rectangle rect, PixelFormat pixelFormat = PixelFormat.Rgba32)
		{
			image.Seek(0, SeekOrigin.Begin);
			Image simage = null;
			simage = pixelFormat switch
			{
				PixelFormat.Rgb24 => Image.LoadPixelData<Rgb24>(image.GetBuffer(), width, height),
				PixelFormat.Bgr32 => Image.LoadPixelData<Bgra32>(image.GetBuffer(), width, height),
				_ => Image.LoadPixelData<Rgba32>(image.GetBuffer(), width, height)
			};
			simage.Mutate(x => x.Crop(rect));
			switch (pixelFormat)
			{
				case PixelFormat.Rgb24:
					if (((Image<Rgb24>)simage).TryGetSinglePixelSpan(out var rgb24Span))
					{
						newImage.Seek(0, SeekOrigin.Begin);
						newImage.Write(MemoryMarshal.AsBytes(rgb24Span));
					}
					break;
				case PixelFormat.Bgr32:
					if (((Image<Bgra32>)simage).TryGetSinglePixelSpan(out var bgr32Span))
					{
						newImage.Seek(0, SeekOrigin.Begin);
						newImage.Write(MemoryMarshal.AsBytes(bgr32Span));
					}
					break;
				default:
					if (((Image<Rgba32>)simage).TryGetSinglePixelSpan(out var rba32Span))
					{
						newImage.Seek(0, SeekOrigin.Begin);
						newImage.Write(MemoryMarshal.AsBytes(rba32Span));
					}
					break;
			}

			simage.Dispose();
			return newImage;
		}

		public static MemoryStream CreateSmallImageFromZones(PixelZone[] zones, MemoryStream memStream)
		{
			for (var i = 0; i < zones.Length; ++i)
			{
				memStream.WriteByte(zones[i].AvgR);
				memStream.WriteByte(zones[i].AvgG);
				memStream.WriteByte(zones[i].AvgB);
			}

			memStream.Seek(0, SeekOrigin.Begin);

			return memStream;
		}

		public static MemoryStream ResizeImage(MemoryStream image, int width, int height, MemoryStream newImage, int newWidth, int newHeight, ImageFilter filter = ImageFilter.Point, double sigma = 0.5, PixelFormat pixelFormat = PixelFormat.Rgba32)
		{
			var resizeKey = $"{newWidth}{newHeight}{filter}{sigma}";
			if (!_resizeOptionCache.TryGetValue(resizeKey, out var options))
			{
				options = new ResizeOptions()
				{
					Size = new Size(newWidth, newHeight),
					Mode = ResizeMode.Manual,
					Sampler = ImageFilterToResampler(filter),
					TargetRectangle = new Rectangle(0, 0, newWidth, newHeight),
					Compand = false
				};
				_resizeOptionCache.TryAdd(resizeKey, options);
			}
			image.Seek(0, SeekOrigin.Begin);
			Image simage = null;
			simage = pixelFormat switch
			{
				PixelFormat.Rgb24 => Image.LoadPixelData<Rgb24>(image.GetBuffer(), width, height),
				PixelFormat.Bgr32 => Image.LoadPixelData<Bgra32>(image.GetBuffer(), width, height),
				_ => Image.LoadPixelData<Rgba32>(image.GetBuffer(), width, height)
			};
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
			switch (pixelFormat)
			{
				case PixelFormat.Rgb24:
					if (((Image<Rgb24>)simage).TryGetSinglePixelSpan(out var rgb24Span))
					{
						newImage.Seek(0, SeekOrigin.Begin);
						newImage.Write(MemoryMarshal.AsBytes(rgb24Span));
					}
					break;
				case PixelFormat.Bgr32:
					if (((Image<Bgra32>)simage).TryGetSinglePixelSpan(out var bgr32Span))
					{
						newImage.Seek(0, SeekOrigin.Begin);
						newImage.Write(MemoryMarshal.AsBytes(bgr32Span));
					}
					break;
				default:
					if (((Image<Rgba32>)simage).TryGetSinglePixelSpan(out var rba32Span))
					{
						newImage.Seek(0, SeekOrigin.Begin);
						newImage.Write(MemoryMarshal.AsBytes(rba32Span));
					}
					break;
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

		public static async Task WriteImageToFile(MemoryStream image, int width, int height, string path, int? resizeWidth = null, int? resizeHeight = null, PixelFormat pixelFormat = PixelFormat.Rgba32)
		{
			image.Seek(0, SeekOrigin.Begin);
			Image simage = null;
			simage = pixelFormat switch
			{
				PixelFormat.Rgb24 => Image.LoadPixelData<Rgb24>(image.GetBuffer(), width, height),
				PixelFormat.Bgr32 => Image.LoadPixelData<Bgra32>(image.GetBuffer(), width, height),
				_ => Image.LoadPixelData<Rgba32>(image.GetBuffer(), width, height)
			};
			if (resizeWidth.HasValue && resizeHeight.HasValue)
				simage.Mutate(x => x.Resize(resizeWidth.Value, resizeHeight.Value, KnownResamplers.NearestNeighbor));

			await simage.SaveAsPngAsync(path);
			simage.Dispose();
		}
	}
}
