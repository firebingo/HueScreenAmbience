using BitmapZoneProcessor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HueScreenAmbience
{
	public class Config
	{
		private ConfigModel _config;
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
				_config = null;
				if (File.Exists("Data/Config.json"))
				{
					_config = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText("Data/Config.json"), _jsonOptions);
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
				File.WriteAllText("Data/Config.json", JsonSerializer.Serialize(_config, _jsonOptions));
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		private void ValidateConfig()
		{
			_config.HueSettings.ColorMultiplier = Math.Clamp(_config.HueSettings.ColorMultiplier, 0.0f, 2.0f);
			_config.HueSettings.BlendLastColorAmount = Math.Clamp(_config.HueSettings.BlendLastColorAmount, 0.0f, 1.0f);
			_config.HueSettings.UpdateFrameRate = Math.Clamp(_config.HueSettings.UpdateFrameRate, 1, 24);
			_config.RgbDeviceSettings.ColorMultiplier = Math.Clamp(_config.RgbDeviceSettings.ColorMultiplier, 0.0f, 2.0f);
			_config.LightStripSettings.RemotePort = Math.Clamp(_config.LightStripSettings.RemotePort, 1, 65535);
			_config.LightStripSettings.BlendLastColorAmount = Math.Clamp(_config.LightStripSettings.BlendLastColorAmount, 0.0f, 1.0f);
			_config.LightStripSettings.SaturateColors = Math.Clamp(_config.LightStripSettings.SaturateColors, 0.0f, 5.0f);
			_config.ZoneColumns = Math.Max(1, _config.ZoneColumns);
			_config.ZoneRows = Math.Max(1, _config.ZoneRows);
			_config.ReadSkipPixels = (_config.ReadSkipPixels == 1 || _config.ReadSkipPixels == 2 || _config.ReadSkipPixels == 4) ? _config.ReadSkipPixels : 2;
			_config.ScreenReadFrameRate = Math.Max(1, _config.ScreenReadFrameRate);
			_config.SocketSettings.ListenPort = Math.Clamp(_config.SocketSettings.ListenPort, 1, 65535);
			_config.FfmpegCaptureSettings.BufferMultiplier = Math.Clamp(_config.FfmpegCaptureSettings.BufferMultiplier, 4, 32);
			_config.FfmpegCaptureSettings.ThreadQueueSize = Math.Clamp(_config.FfmpegCaptureSettings.ThreadQueueSize, 1, 4096);

			if (!IPAddress.TryParse(_config.LightStripSettings.RemoteAddress, out _))
			{
				_config.LightStripSettings.RemoteAddress = "127.0.0.1";
			}
			_ = _config.LightStripSettings.RemoteAddressIp;

			if (!IPAddress.TryParse(_config.SocketSettings.ListenAddress, out _))
			{
				_config.SocketSettings.ListenAddress = IPAddress.Any.ToString();
			}
			_ = _config.SocketSettings.ListenIp;

			for (var i = 0; i < _config.LightStripSettings.Lights.Count; ++i)
			{
				var x = Math.Clamp(_config.LightStripSettings.Lights[i].X, 0.0f, 1.0f);
				var y = Math.Clamp(_config.LightStripSettings.Lights[i].Y, 0.0f, 1.0f);
				_config.LightStripSettings.Lights[i] = new SimplePointF(x, y);
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
		public HueSettings HueSettings { get; set; } = new HueSettings();
		public ZoneProcessSettings ZoneProcessSettings { get; set; } = new ZoneProcessSettings();
		public RGBDeviceSettings RgbDeviceSettings { get; set; } = new RGBDeviceSettings();
		public FFMpegCaptureSettings FfmpegCaptureSettings { get; set; } = new FFMpegCaptureSettings();
		public SocketSettings SocketSettings { get; set; } = new SocketSettings();
		public int AdapterId { get; set; } = 0;
		public int MonitorId { get; set; } = 0;
		public int ZoneColumns { get; set; } = 1;
		public int ZoneRows { get; set; } = 1;
		public int ScreenReadFrameRate { get; set; } = 24;
		public bool DumpPngs { get; set; } = false;
		public string ImageDumpLocation { get; set; } = "Images";
		public bool IntrinsicsEnabled { get; set; } = false;
		public int ReadSkipPixels { get; set; } = 2;
		public bool DebugTimings { get; set; } = false;
		public LightStripSettings LightStripSettings { get; set; } = new LightStripSettings();
	}

	public class HueSettings
	{
		public bool UseHue { get; set; } = true;
		public string AppKey { get; set; } = string.Empty;
		public string EntertainmentKey { get; set; } = string.Empty;
		public string Ip { get; set; } = string.Empty;
		public Guid RoomId { get; set; } = Guid.Empty;
		public HueType HueType { get; set; } = HueType.Basic;
		public int UpdateFrameRate { get; set; } = 8;
		public bool TurnLightOnIfOff { get; set; } = true;
		public bool ShutLightOffOnStop { get; set; } = true;
		public byte MaxColorValue { get; set; } = 255;
		public byte MinColorValue { get; set; } = 0;
		public byte MinRoundColor { get; set; } = 4;
		public float ColorMultiplier { get; set; } = 1.0f;
		public byte ColorChangeThreshold { get; set; } = 15;
		public float BlendLastColorAmount { get; set; } = 0.4f;
	}

	public class ZoneProcessSettings
	{
		public float ResizeScale { get; set; } = 4.0f;
		public float ResizeSigma { get; set; } = 0.75f;
		public ImageFilter ResizeFilter { get; set; } = ImageFilter.Gaussian;
	}

	public class RGBDeviceSettings
	{
		[JsonIgnore]
		public bool UseDevices
		{
			get => UseKeyboards || UseMice || UseMotherboard || UseLightstrip;
		}
		public bool UseKeyboards { get; set; } = false;
		public bool UseMice { get; set; } = false;
		public bool UseMotherboard { get; set; } = false;
		public bool UseLightstrip { get; set; } = false;
		public float ColorMultiplier { get; set; } = 1.0f;
		public byte ColorChangeThreshold { get; set; } = 5;
		public int KeyboardResReduce { get; set; } = 4;
		public int UpdateFrameRate { get; set; } = 60;
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
		public bool UseLightStrip { get; set; } = false;
		public string RemoteAddress { get; set; } = "127.0.0.1";
		private IPAddress _remoteAddressIp;
		[JsonIgnore]
		public IPAddress RemoteAddressIp
		{
			get
			{
				_remoteAddressIp ??= IPAddress.Parse(RemoteAddress);
				return _remoteAddressIp;
			}
		}
		public int RemotePort { get; set; } = 9250;
		public float ColorMultiplier { get; set; } = 1.0f;
		public float BlendLastColorAmount { get; set; } = 0.4f;
		public float SaturateColors { get; set; } = 1.0f;
		public int UpdateFrameRate { get; set; } = 24;
		public List<SimplePointF> Lights { get; set; } = new List<SimplePointF>();
	}

	public class FFMpegCaptureSettings
	{
		public bool UseFFMpeg { get; set; } = false;
		public bool LightsLocal { get; set; } = false;
		public int Width { get; set; } = 1280;
		public int Height { get; set; } = 720;
		public int FrameRate { get; set; } = 18;
		public int SkipFrames { get; set; } = 100;
		public int InputWidth { get; set; } = 1280;
		public int InputHeight { get; set; } = 720;
		public int InputFrameRate { get; set; } = 60;
		public string InputSource { get; set; } = "/dev/video0";
		public string InputFormat { get; set; } = "v4l2";
		public string InputPixelFormatType { get; set; } = "input_format";
		public string InputPixelFormat { get; set; } = "yuv420p";
		public int BufferMultiplier { get; set; } = 5;
		public int ThreadQueueSize { get; set; } = 128;
		public bool FfmpegStdError { get; set; } = false;
		public bool UseGpu { get; set; } = false;
	}

	public class SocketSettings
	{
		public bool EnableHubSocket { get; set; } = false;
		public bool AspnetConsoleLog { get; set; } = false;
		public int ListenPort { get; set; } = 34780;
		public string ListenAddress { get; set; } = IPAddress.Any.ToString();
		private IPAddress _listenIp;
		[JsonIgnore]
		public IPAddress ListenIp
		{
			get
			{
				_listenIp ??= IPAddress.Parse(ListenAddress);
				return _listenIp;
			}
		}
		public string SslCertLocation { get; set; } = string.Empty;
		public string SslCertPassword { get; set; } = string.Empty;
		public SslProtocols SslProtocol { get; set; } = SslProtocols.Tls12 | SslProtocols.Tls13;
	}
}
