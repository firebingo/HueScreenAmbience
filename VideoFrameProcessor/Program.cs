using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VideoFrameProcessor.ffmpeg;

namespace VideoFrameProcessor
{
	internal class Program
	{
		private static async Task Main(string[] args)
		{
			var builder = new ConfigurationBuilder();
			builder.AddCommandLine(args);
			var commandLine = builder.Build();

			var filePath = commandLine["file_path"];
			if (string.IsNullOrWhiteSpace(filePath))
			{
				Console.WriteLine("no file path provided");
				Console.ReadLine();
				return;
			}

			var outPath = commandLine["out_path"];
			if (string.IsNullOrWhiteSpace(outPath))
			{
				Console.WriteLine("no output path provided");
				Console.ReadLine();
				return;
			}

			var config = new Config();
			config.LoadConfig();
			var info = await FFProbeReader.ReadFFprobeInfo(filePath);
			var streamInfo = info?.Streams.First();
			if (streamInfo == null)
			{
				Console.WriteLine("Failed to read video info");
				Console.ReadLine();
				return;
			}

			FFMpegReader.ReadVideo(config, streamInfo, filePath, outPath);
		}
	}
}