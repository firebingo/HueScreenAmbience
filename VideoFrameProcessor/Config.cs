using System;
using System.IO;
using System.Text.Json;
using BitmapZoneProcessor;

namespace VideoFrameProcessor
{
	public class Config
	{
		private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};

		public ConfigModel Model { get; private set; } = new ConfigModel();

		public void LoadConfig()
		{
			try
			{
				if (File.Exists("Data/Config.json"))
				{
					var readJson =
						JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText("Data/Config.json"), _jsonOptions) ??
						throw new Exception("Failed to read config");
					Model = readJson;
					ValidateConfig();
					SaveConfig();
				}
				else
				{
					if (!Directory.Exists("Data"))
						Directory.CreateDirectory("Data");
					SaveConfig();
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		public void SaveConfig()
		{
			try
			{
				File.WriteAllText("Data/Config.json", JsonSerializer.Serialize(Model, _jsonOptions));
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		private void ValidateConfig()
		{
			Model.ZoneColumns = Math.Max(1, Model.ZoneColumns);
			Model.ZoneRows = Math.Max(1, Model.ZoneRows);
			Model.ReadResolutionReduce = Math.Clamp(Model.ReadResolutionReduce, 1.0f, 10.0f);
		}
	}

	public class ConfigModel
	{
		public int FrameConcurrency { get; set; } = 16;
		public int BufferSize { get; set; } = 32768;
		public int ZoneColumns { get; set; } = 1;
		public int ZoneRows { get; set; } = 1;
		public float ResizeScale { get; set; } = 12.0f;
		public float ResizeSigma { get; set; } = 0.8f;
		public ImageFilter ResizeFilter { get; set; } = ImageFilter.Gaussian;
		public float ReadResolutionReduce { get; set; } = 2.0f;
	}
}