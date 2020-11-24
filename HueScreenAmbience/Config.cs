using ImageMagick;
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
			_config.hueSettings.colorMultiplier = Math.Clamp(_config.hueSettings.colorMultiplier, 0.0f, 2.0f);
			_config.hueSettings.blendLastColorAmount = Math.Clamp(_config.hueSettings.blendLastColorAmount, 0.0f, 1.0f);
			_config.hueSettings.updateFrameRate = Math.Clamp(_config.hueSettings.updateFrameRate, 1, 24);
			_config.rgbDeviceSettings.colorMultiplier = Math.Clamp(_config.rgbDeviceSettings.colorMultiplier, 0.0f, 2.0f);
			_config.zoneColumns = Math.Max(1, _config.zoneColumns);
			_config.zoneRows = Math.Max(1, _config.zoneRows);
			_config.readResolutionReduce = Math.Clamp(_config.readResolutionReduce, 1.0f, 10.0f);
			_config.screenReadFrameRate = Math.Max(1, _config.screenReadFrameRate);
		}
	}

	public enum HueType
	{
		Basic = 0,
		Entertainment = 1
	}

	[Serializable]
	public class ConfigModel
	{
		public HueSettings hueSettings = new HueSettings();
		public ZoneProcessSettings zoneProcessSettings = new ZoneProcessSettings();
		public RGBDeviceSettings rgbDeviceSettings = new RGBDeviceSettings();
		public int adapterId = 0;
		public int monitorId = 0;
		public int zoneColumns = 1;
		public int zoneRows = 1;
		public int screenReadFrameRate = 24;
		public int pixelCount = 0;
		public bool dumpPngs = false;
		public string imageDumpLocation = "Images";
		public bool intrinsicsEnabled;
		public float readResolutionReduce = 2.0f;
		public System.Drawing.Rectangle? bitmapRect = null;
	}

	public class HueSettings
	{
		public bool useHue = true;
		public string appKey;
		public string entertainmentKey;
		public string ip;
		public string roomId;
		public HueType hueType = HueType.Basic;
		public int updateFrameRate = 8;
		public bool turnLightOnIfOff = true;
		public bool shutLightOffOnStop = true;
		public byte maxColorValue = 255;
		public byte minColorValue = 0;
		public float colorMultiplier = 1.0f;
		public byte colorChangeThreshold = 15;
		public float blendLastColorAmount = 0.4f;
	}

	public class ZoneProcessSettings
	{
		public float resizeScale = 16.0f;
		public float resizeSigma = 0.45f;
		public FilterType resizeFilter = FilterType.Gaussian;
	}

	public class RGBDeviceSettings
	{
		public bool useKeyboards = false;
		public bool useMice = false;
		public float colorMultiplier = 1.0f;
		public int keyboardResReduce = 4;
	}
}
