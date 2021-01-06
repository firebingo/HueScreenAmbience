using System;
using System.IO;
using System.Net;
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
			_config.ReceivePort = Math.Clamp(_config.ReceivePort, 1, 65535);
			if(_config.RemoteAddress != null && !IPAddress.TryParse(_config.RemoteAddress, out _))
				_config.RemoteAddress = null;
			_ = _config.RemoteAddressIp;
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
				if (_remoteAddressIp == null)
					_remoteAddressIp = IPAddress.Parse(RemoteAddress);
				return _remoteAddressIp;
			}
		}
		public string? RemoteAddress = null;
		public int ReceivePort { get; set; } = 9250;
		public int ReceiveTimeout { get; set; } = 10000;
		public int LightCount { get; set; } = 0;
	}
}
