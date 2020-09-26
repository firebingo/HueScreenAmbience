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
	}

	[Serializable]
	public class ConfigModel
	{
		public string appKey;
		public string ip;
		public string roomId;
		public int zoneColumns = 1;
		public int zoneRows = 1;
		public int pixelCount = 150000;
		public bool intrinsicsEnabled;
	}
}
