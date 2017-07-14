using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HueScreenAmbience
{
    public class Config
    {
		public ConfigModel config { get; private set; }

		public void loadConfig()
		{
			try
			{
				config = null;
				if (File.Exists("Data/Config.json"))
				{
					config = JsonConvert.DeserializeObject<ConfigModel>(File.ReadAllText("Data/Config.json"));
				}
				else
				{
					if (!Directory.Exists("Data"))
						Directory.CreateDirectory("Data");
					using (File.Create("Data/Config.json")) { }
					config = new ConfigModel();
					saveConfig();
				}
			}
			catch(Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		public void saveConfig()
		{
			try
			{
				File.WriteAllText("Data/Config.json", JsonConvert.SerializeObject(config));
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
	}
}
