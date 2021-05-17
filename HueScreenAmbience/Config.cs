using BitmapZoneProcessor;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

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
			catch (Exception e)
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
			catch (Exception e)
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
			_config.lightStripSettings.remotePort = Math.Clamp(_config.lightStripSettings.remotePort, 1, 65535);
			_config.lightStripSettings.blendLastColorAmount = Math.Clamp(_config.lightStripSettings.blendLastColorAmount, 0.0f, 1.0f);
			_config.lightStripSettings.saturateColors = Math.Clamp(_config.lightStripSettings.saturateColors, 0.0f, 5.0f);
			_config.zoneColumns = Math.Max(1, _config.zoneColumns);
			_config.zoneRows = Math.Max(1, _config.zoneRows);
			_config.readResolutionReduce = Math.Clamp(_config.readResolutionReduce, 1.0f, 10.0f);
			_config.screenReadFrameRate = Math.Max(1, _config.screenReadFrameRate);
			_config.socketSettings.listenPort = Math.Clamp(_config.socketSettings.listenPort, 1, 65535);

			if (_config.bitmapRect.HasValue)
				_config.imageRect = new SixLabors.ImageSharp.Rectangle(_config.bitmapRect.Value.top, _config.bitmapRect.Value.left, _config.bitmapRect.Value.width, _config.bitmapRect.Value.height);

			if (!IPAddress.TryParse(_config.lightStripSettings.remoteAddress, out _))
			{
				_config.lightStripSettings.remoteAddress = "127.0.0.1";
			}
			_ = _config.lightStripSettings.remoteAddressIp;

			if (!IPAddress.TryParse(_config.socketSettings.listenAddress, out _))
			{
				_config.socketSettings.listenAddress = IPAddress.Any.ToString();
			}
			_ = _config.socketSettings.listenIp;

			for (var i = 0; i < _config.lightStripSettings.lights.Count; ++i)
			{
				var x = Math.Clamp(_config.lightStripSettings.lights[i].X, 0.0f, 1.0f);
				var y = Math.Clamp(_config.lightStripSettings.lights[i].Y, 0.0f, 1.0f);
				_config.lightStripSettings.lights[i] = new SimplePointF(x, y);
			}
		}
	}

	public enum HueType
	{
		Basic = 0,
		Entertainment = 1
	}

	public struct JsonRect
	{
		public int top;
		public int left;
		public int width;
		public int height;
	}

	[Serializable]
	public class ConfigModel
	{
		public HueSettings hueSettings = new HueSettings();
		public ZoneProcessSettings zoneProcessSettings = new ZoneProcessSettings();
		public RGBDeviceSettings rgbDeviceSettings = new RGBDeviceSettings();
		public PiCameraSettings piCameraSettings = new PiCameraSettings();
		public SocketSettings socketSettings = new SocketSettings();
		public int adapterId = 0;
		public int monitorId = 0;
		public int zoneColumns = 1;
		public int zoneRows = 1;
		public int screenReadFrameRate = 24;
		public bool dumpPngs = false;
		public string imageDumpLocation = "Images";
		public bool intrinsicsEnabled;
		public float readResolutionReduce = 2.0f;
		public bool debugTimings = false;
		public JsonRect? bitmapRect = null;
		[JsonIgnore]
		public SixLabors.ImageSharp.Rectangle? imageRect = null;
		public LightStripSettings lightStripSettings = new LightStripSettings();
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
		public float resizeScale = 4.0f;
		public float resizeSigma = 0.75f;
		public ImageFilter resizeFilter = ImageFilter.Gaussian;
	}

	public class RGBDeviceSettings
	{
		public bool useKeyboards = false;
		public bool useMice = false;
		public bool useMotherboard = false;
		public float colorMultiplier = 1.0f;
		public byte colorChangeThreshold = 5;
		public int keyboardResReduce = 4;
		public int updateFrameRate = 60;
	}

	public struct SimplePointF
	{
		public float X { get; set; }
		public float Y { get; set; }

		public SimplePointF(float x, float y)
		{
			X = x;
			Y = y;
		}
	}

	public class LightStripSettings
	{
		public bool useLightStrip = false;
		public string remoteAddress = "127.0.0.1";
		private IPAddress _remoteAddressIp;
		[JsonIgnore]
		public IPAddress remoteAddressIp
		{
			get
			{
				if (_remoteAddressIp == null)
					_remoteAddressIp = IPAddress.Parse(remoteAddress);
				return _remoteAddressIp;
			}
		}
		public int remotePort = 9250;
		public float colorMultiplier = 1.0f;
		public float blendLastColorAmount = 0.4f;
		public float saturateColors = 1.0f;
		public int updateFrameRate = 24;
		public List<SimplePointF> lights = new List<SimplePointF>();
	}

	public class PiCameraSettings
	{
		public bool isPi = false;
		public bool lightsLocal = false;
		public int width = 1280;
		public int height = 720;
		public int frameRate = 18;
		public int skipFrames = 100;
		public int inputWidth = 1280;
		public int inputHeight = 720;
		public int inputFrameRate = 60;
		public string inputSource = "/dev/video0";
		public string inputFormat = "yuv420p";
		public bool ffmpegStdError = false;
	}

	public class SocketSettings
	{
		public bool enableHubSocket = false;
		public bool aspnetConsoleLog = false;
		public int listenPort = 34780;
		public string listenAddress = IPAddress.Any.ToString();
		private IPAddress _listenIp;
		[JsonIgnore]
		public IPAddress listenIp
		{
			get
			{
				if (_listenIp == null)
					_listenIp = IPAddress.Parse(listenAddress);
				return _listenIp;
			}
		}
	}
}
