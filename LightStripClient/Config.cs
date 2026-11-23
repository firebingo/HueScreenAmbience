using System;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightStripClient
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
					Console.WriteLine(
						$"Config loaded with {Model.LightCount} lights, receiving on port {Model.ReceivePort}");
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
			Model.ReceivePort = Math.Clamp(Model.ReceivePort, 1, 65535);
			if (Model.RemoteAddress != null && !IPAddress.TryParse(Model.RemoteAddress, out _))
				Model.RemoteAddress = null;
			_ = Model.RemoteAddressIp;

			Model.SocketSettings.ListenPort = Math.Clamp(Model.SocketSettings.ListenPort, 1, 65535);
			if (!IPAddress.TryParse(Model.SocketSettings.ListenAddress, out _))
				Model.SocketSettings.ListenAddress = IPAddress.Any.ToString();
			_ = Model.SocketSettings.ListenIp;
		}
	}

	public class ConfigModel
	{
		public string? RemoteAddress;
		private IPAddress? _remoteAddressIp;

		[JsonIgnore]
		public IPAddress? RemoteAddressIp
		{
			get
			{
				if (RemoteAddress == null)
					return null;
				_remoteAddressIp ??= IPAddress.Parse(RemoteAddress);
				return _remoteAddressIp;
			}
		}

		public int ReceivePort { get; set; } = 9250;
		public int ReceiveTimeout { get; set; } = 10000;
		public int LightCount { get; set; }
		public SocketSettings SocketSettings { get; set; } = new SocketSettings();
	}

	public class SocketSettings
	{
		private IPAddress? _listenIp;
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
}