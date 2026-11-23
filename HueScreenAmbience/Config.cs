using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitmapZoneProcessor;
using SixLabors.ImageSharp;

namespace HueScreenAmbience
{
	public class Config
	{
		private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};

		public ConfigModel Model { get; private set; }

		public void LoadConfig()
		{
			try
			{
				Model = null;
				if (File.Exists("Data/Config.json"))
				{
					Model = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText("Data/Config.json"), _jsonOptions);
					ValidateConfig();
					SaveConfig();
				}
				else
				{
					if (!Directory.Exists("Data"))
						Directory.CreateDirectory("Data");
					using (File.Create("Data/Config.json")) { }

					Model = new ConfigModel();
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
			Model.HueSettings.ColorMultiplier = Math.Clamp(Model.HueSettings.ColorMultiplier, 0.0f, 2.0f);
			Model.HueSettings.BlendLastColorAmount = Math.Clamp(Model.HueSettings.BlendLastColorAmount, 0.0f, 1.0f);
			Model.HueSettings.UpdateFrameRate = Math.Clamp(Model.HueSettings.UpdateFrameRate, 1, 24);
			Model.RgbDeviceSettings.ColorMultiplier = Math.Clamp(Model.RgbDeviceSettings.ColorMultiplier, 0.0f, 2.0f);
			Model.LightStripSettings.RemotePort = Math.Clamp(Model.LightStripSettings.RemotePort, 1, 65535);
			Model.LightStripSettings.BlendLastColorAmount =
				Math.Clamp(Model.LightStripSettings.BlendLastColorAmount, 0.0f, 1.0f);
			Model.LightStripSettings.SaturateColors = Math.Clamp(Model.LightStripSettings.SaturateColors, 0.0f, 5.0f);
			Model.NanoLeafSettings.ColorMultiplier = Math.Clamp(Model.NanoLeafSettings.ColorMultiplier, 0.0f, 2.0f);
			Model.NanoLeafSettings.BlendLastColorAmount =
				Math.Clamp(Model.NanoLeafSettings.BlendLastColorAmount, 0.0f, 1.0f);
			Model.ZoneColumns = Math.Max(1, Model.ZoneColumns);
			Model.ZoneRows = Math.Max(1, Model.ZoneRows);
			Model.ReadResolutionReduce = Math.Clamp(Model.ReadResolutionReduce, 1.0f, 10.0f);
			Model.ScreenReadFrameRate = Math.Max(1, Model.ScreenReadFrameRate);
			Model.SocketSettings.ListenPort = Math.Clamp(Model.SocketSettings.ListenPort, 1, 65535);
			Model.FfmpegCaptureSettings.BufferMultiplier =
				Math.Clamp(Model.FfmpegCaptureSettings.BufferMultiplier, 4, 32);
			Model.FfmpegCaptureSettings.ThreadQueueSize =
				Math.Clamp(Model.FfmpegCaptureSettings.ThreadQueueSize, 1, 4096);

			if (Model.BitmapRect.HasValue)
				Model.ImageRect = new Rectangle(Model.BitmapRect.Value.top, Model.BitmapRect.Value.left,
					Model.BitmapRect.Value.width, Model.BitmapRect.Value.height);

			if (!IPAddress.TryParse(Model.LightStripSettings.RemoteAddress, out _))
				Model.LightStripSettings.RemoteAddress = "127.0.0.1";
			_ = Model.LightStripSettings.RemoteAddressIp;

			if (!IPAddress.TryParse(Model.SocketSettings.ListenAddress, out _))
				Model.SocketSettings.ListenAddress = IPAddress.Any.ToString();
			_ = Model.SocketSettings.ListenIp;

			if (!IPAddress.TryParse(Model.RgbDeviceSettings.OpenRGBAddress, out _))
				Model.RgbDeviceSettings.OpenRGBAddress = "127.0.0.1";
			_ = Model.RgbDeviceSettings.OpenRGBIp;

			for (var i = 0; i < Model.LightStripSettings.Lights.Count; ++i)
			{
				var x = Math.Clamp(Model.LightStripSettings.Lights[i].X, 0.0f, 1.0f);
				var y = Math.Clamp(Model.LightStripSettings.Lights[i].Y, 0.0f, 1.0f);
				Model.LightStripSettings.Lights[i] = new SimplePointF(x, y);
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
		[JsonIgnore]
		public Rectangle? ImageRect;

		public HueSettings HueSettings { get; set; } = new HueSettings();
		public ZoneProcessSettings ZoneProcessSettings { get; set; } = new ZoneProcessSettings();
		public RGBDeviceSettings RgbDeviceSettings { get; set; } = new RGBDeviceSettings();
		public FFMpegCaptureSettings FfmpegCaptureSettings { get; set; } = new FFMpegCaptureSettings();
		public SocketSettings SocketSettings { get; set; } = new SocketSettings();
		public uint AdapterId { get; set; }
		public uint MonitorId { get; set; }
		public int ZoneColumns { get; set; } = 1;
		public int ZoneRows { get; set; } = 1;
		public int ScreenReadFrameRate { get; set; } = 24;
		public bool DumpPngs { get; set; }
		public string ImageDumpLocation { get; set; } = "Images";
		public bool IntrinsicsEnabled { get; set; }
		public float ReadResolutionReduce { get; set; } = 2.0f;
		public bool DebugTimings { get; set; }
		public JsonRect? BitmapRect { get; set; }
		public NanoLeafSettings NanoLeafSettings { get; set; } = new NanoLeafSettings();
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
		public byte ColorChangeThreshold { get; set; } = 0;
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
		private IPAddress _openRGBIp;

		[JsonIgnore]
		public bool UseDevices => UseKeyboards || UseMice || UseMotherboard || UseLightstrip;

		public bool UseKeyboards { get; set; } = false;
		public bool UseMice { get; set; } = false;
		public bool UseMotherboard { get; set; } = false;
		public bool UseLightstrip { get; set; } = false;
		public float ColorMultiplier { get; set; } = 1.0f;
		public byte ColorChangeThreshold { get; set; } = 5;
		public int KeyboardResReduce { get; set; } = 4;
		public int UpdateFrameRate { get; set; } = 60;
		public bool UseOpenRGB { get; set; } = false;
		public short OpenRGBPort { get; set; } = 6742;
		public string OpenRGBAddress { get; set; } = "127.0.0.1";

		[JsonIgnore]
		public IPAddress OpenRGBIp
		{
			get
			{
				_openRGBIp ??= IPAddress.Parse(OpenRGBAddress);
				return _openRGBIp;
			}
		}
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
		private IPAddress _remoteAddressIp;
		public bool UseLightStrip { get; set; } = false;
		public string RemoteAddress { get; set; } = "127.0.0.1";

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
		public List<SimplePointF> Lights { get; set; } = [];
	}

	public class FFMpegCaptureSettings
	{
		public bool UseFFMpeg { get; set; } = false;
		public bool LightsLocal { get; set; } = false;
		public uint Width { get; set; } = 1280;
		public uint Height { get; set; } = 720;
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
		private IPAddress _listenIp;
		public bool EnableHubSocket { get; set; } = false;
		public bool AspnetConsoleLog { get; set; } = false;
		public int ListenPort { get; set; } = 34780;
		public string ListenAddress { get; set; } = IPAddress.Any.ToString();

		[JsonIgnore]
		public IPAddress ListenIp
		{
			get
			{
				_listenIp ??= IPAddress.Parse(ListenAddress);
				return _listenIp;
			}
		}

		public string PfxCertLocation { get; set; } = string.Empty;
		public string PfxCertPassword { get; set; } = string.Empty;
		public string PemCertLocation { get; set; } = string.Empty;
		public string PemCertPrivateKeyLocation { get; set; } = string.Empty;
		public string PemCertPassword { get; set; } = string.Empty;
		public bool PkcsCertHack { get; set; } = true;
		public SslProtocols SslProtocol { get; set; } = SslProtocols.Tls12 | SslProtocols.Tls13;
	}

	public class NanoLeafSettings
	{
		private IPAddress _remoteAddressIp;
		public bool UseNanoLeaf { get; set; } = false;
		public string RemoteAddress { get; set; } = "127.0.0.1";

		[JsonIgnore]
		public IPAddress RemoteAddressIp
		{
			get
			{
				_remoteAddressIp ??= IPAddress.Parse(RemoteAddress);
				return _remoteAddressIp;
			}
		}

		public ushort Port { get; set; } = 16021;
		public string AuthToken { get; set; }
		public int UpdateFrameRate { get; set; } = 60;
		public float ColorMultiplier { get; set; } = 1.0f;
		public byte ColorChangeThreshold { get; set; } = 0;
		public byte MinRoundColor { get; set; } = 4;
		public float BlendLastColorAmount { get; set; } = 0.4f;
		public int LayoutResReduce { get; set; } = 4;
		public bool FlipX { get; set; } = true;
		public double ExtraOrientationAdjustAngle { get; set; } = 0;
	}
}