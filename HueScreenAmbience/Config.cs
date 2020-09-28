using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HueScreenAmbience
{
    public class Config
    {
		private ConfigModel _config;
		public ConfigModel Model { get => _config; private set => _config = value; }

		public void LoadConfig()
		{
			try
			{
				_config = null;
				if (File.Exists("Data/Config.json"))
				{
					_config = JsonConvert.DeserializeObject<ConfigModel>(File.ReadAllText("Data/Config.json"));
					ValidateConfig();
					SaveConfig();
				}
				else
				{
					if (!Directory.Exists("Data"))
						Directory.CreateDirectory("Data");
					using (File.Create("Data/Config.json")) { }
					_config = new ConfigModel();
					SaveConfig();
				}
			}
			catch(Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		public void SaveConfig()
		{
			try
			{
				File.WriteAllText("Data/Config.json", JsonConvert.SerializeObject(_config, Formatting.Indented));
			}
			catch(Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		private void ValidateConfig()
		{
			_config.hueSettings.colorMultiplier = Math.Clamp(_config.hueSettings.colorMultiplier, 0.0f, 1.0f);
			_config.zoneColumns = Math.Max(1, _config.zoneColumns);
			_config.zoneRows = Math.Max(1, _config.zoneRows);
			_config.readResolutionReduce = Math.Clamp(_config.readResolutionReduce, 1.0f, 10.0f);
		}
	}

	[Serializable]
	public class ConfigModel
	{
		public string appKey;
		public string ip;
		public string roomId;
		public HueSettings hueSettings = new HueSettings();
		public int zoneColumns = 1;
		public int zoneRows = 1;
		public int pixelCount = 150000;
		public bool dumpPngs = false;
		public string imageDumpLocation = "Images";
		public bool intrinsicsEnabled;
		public float readResolutionReduce = 2.0f;
	}

	public class HueSettings
	{
		public bool turnLightOnIfOff = true;
		public bool shutLightOffOnStop = true;
		public float colorMultiplier = 1.0f;
		public byte colorChangeThreshold = 15;
	}
}
