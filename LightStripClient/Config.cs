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
					var readJson = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText("Data/Config.json"), _jsonOptions) ?? throw new Exception("Failed to read config");
					_config = readJson;
					ValidateConfig();
					SaveConfig();
					Console.WriteLine($"Config loaded with {_config.LightCount} lights, receiving on port {_config.ReceivePort}");
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
			_config.ReceivePort = Math.Clamp(_config.ReceivePort, 1, 65535);
			if (_config.RemoteAddress != null && !IPAddress.TryParse(_config.RemoteAddress, out _))
				_config.RemoteAddress = null;
			_ = _config.RemoteAddressIp;

			_config.SocketSettings.ListenPort = Math.Clamp(_config.SocketSettings.ListenPort, 1, 65535);
			if (!IPAddress.TryParse(_config.SocketSettings.ListenAddress, out _))
				_config.SocketSettings.ListenAddress = IPAddress.Any.ToString();
			_ = _config.SocketSettings.ListenIp;
		}
	}

	public class ConfigModel
	{
		private IPAddress? _remoteAddressIp = null;
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
		public string? RemoteAddress = null;
		public int ReceivePort { get; set; } = 9250;
		public int ReceiveTimeout { get; set; } = 10000;
		public int LightCount { get; set; } = 0;
		public SocketSettings SocketSettings { get; set; } = new SocketSettings();
	}

	public class SocketSettings
	{
		public bool EnableHubSocket { get; set; } = false;
		public bool AspnetConsoleLog { get; set; } = false;
		public int ListenPort { get; set; } = 34780;
		public string ListenAddress { get; set; } = IPAddress.Any.ToString();
		private IPAddress? _listenIp;
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
