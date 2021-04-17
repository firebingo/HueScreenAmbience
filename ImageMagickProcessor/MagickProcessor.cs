using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace ImageMagickProcessor
{
	public static class MagickProcessor
	{
		public static async Task<MemoryStream> ResizeImage(MemoryStream image, int width, int height, MemoryStream newImage, int newWidth, int newHeight, FilterType filter = FilterType.Point, double sigma = 0.5)
		{
			var bufferLength = width * height * 3;
			var buffer = new byte[bufferLength];

			Process magickProcess = new Process();

			magickProcess.StartInfo.UseShellExecute = false;
			magickProcess.StartInfo.RedirectStandardOutput = true;
			magickProcess.StartInfo.RedirectStandardError = true;
			magickProcess.StartInfo.FileName = "magick";
			magickProcess.StartInfo.Arguments = $"rgb:- -size {width}x{height} -depth 8 -filter {filter} -define filter:sigma={sigma} -resize {newWidth}x{newHeight}! rgb:-";

			magickProcess.Start();

			image.WriteTo(magickProcess.StandardInput.BaseStream);

			do
			{
				await magickProcess.StandardOutput.BaseStream.ReadAsync(buffer, 0, bufferLength);
				newImage.Write(buffer, 0, bufferLength);
			}
			while (!magickProcess.HasExited);

			return newImage;
		}
	}
}
