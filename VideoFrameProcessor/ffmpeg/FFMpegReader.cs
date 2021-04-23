using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace VideoFrameProcessor.ffmpeg
{
	public static class FFMpegReader
	{
		public static void ReadVideo(Config config, FFProbeStream streamInfo, string filePath, string outPath)
		{
			var p = new Process();
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.FileName = "ffmpeg.exe";
			p.StartInfo.Arguments = $"-i \"{filePath}\" -f rawvideo -c:v rawvideo -pix_fmt bgra -s {streamInfo.Width}x{streamInfo.Height} pipe:1";
			var f = 0;
			var frameLength = streamInfo.Width * streamInfo.Height * 4;
			var buffer = new byte[config.Model.BufferSize];
			p.Start();
			var stream = p.StandardOutput.BaseStream;
			using var frame = new MemoryStream(frameLength);
			var frameTasks = new List<Task>();
			var frameBytesRead = 0;
			var bytesRead = 0;
			do
			{
				while (frameTasks.Count >= config.Model.FrameConcurrency)
				{
					var removeTasks = new List<Task>();
					foreach (var task in frameTasks)
					{
						if (task.IsCompleted)
							removeTasks.Add(task);
					}
					foreach (var r in removeTasks)
					{
						frameTasks.Remove(r);
					}
				}

				if (frameBytesRead + config.Model.BufferSize > frameLength)
				{
					bytesRead = stream.Read(buffer, 0, frameLength - frameBytesRead);
					frame.Write(buffer);
					var frameProcessStream = new MemoryStream(frameLength);
					frame.Seek(0, SeekOrigin.Begin);
					frame.CopyTo(frameProcessStream);
					frameTasks.Add(FrameProcessor.ProcessFrame(config, frameProcessStream, f++, streamInfo.Width, streamInfo.Height, outPath));
					frame.Seek(0, SeekOrigin.Begin);
					frameBytesRead = 0;
				}
				else
				{
					bytesRead = stream.Read(buffer, 0, buffer.Length);
					if (bytesRead != 0)
					{
						frameBytesRead += bytesRead;
						frame.Write(buffer);
					}
				}
			} while (!p.HasExited);
			p.WaitForExit();
		}
	}
}
