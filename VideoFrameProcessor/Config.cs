using BitmapZoneProcessor;
using System;
using System.IO;
using System.Text.Json;

namespace VideoFrameProcessor
{
	public class Config
	{
		private ConfigModel _config = new ConfigModel();
		public ConfigModel Model { get => _config; private set => _config = value; }

		private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};

		public void LoadConfig()
		{
			try
			{
				if (File.Exists("Data/Config.json"))
				{
					var readJson = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText("Data/Config.json"), _jsonOptions);
					if (readJson == null)
						throw new Exception("Failed to read config");
					_config = readJson;
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
				File.WriteAllText("Data/Config.json", JsonSerializer.Serialize(_config, _jsonOptions));
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		private void ValidateConfig()
		{
			_config.ZoneColumns = Math.Max(1, _config.ZoneColumns);
			_config.ZoneRows = Math.Max(1, _config.ZoneRows);
			_config.ReadResolutionReduce = Math.Clamp(_config.ReadResolutionReduce, 1.0f, 10.0f);
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
