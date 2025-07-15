using HueScreenAmbience.Hue;
using LightsShared;
using LightsShared.Sockets;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using WebControlClient.Client.Shared;
using WebControlClient.Shared;

namespace HueScreenAmbience.Sockets
{
	public class SocketHandler
	{
		private bool _isRunning = false;

		private Core _core;
		private ScreenReader _screen;
		private HueCore _hueCore;
		private Config _config;
		private FileLogger _logger;

		private SocketServer _socketServer;

		public void InstallServices(IServiceProvider map)
		{
			_core = map.GetService(typeof(Core)) as Core;
			_hueCore = map.GetService(typeof(HueCore)) as HueCore;
			_screen = map.GetService(typeof(ScreenReader)) as ScreenReader;
			_config = map.GetService(typeof(Config)) as Config;
			_logger = map.GetService(typeof(FileLogger)) as FileLogger;
		}

		public async Task Start()
		{
			if (!_config.Model.SocketSettings.EnableHubSocket || _isRunning)
				return;

			try
			{
				_socketServer = new SocketServer(_logger);
				X509Certificate2 cert = null;
				if (!string.IsNullOrWhiteSpace(_config.Model.SocketSettings.PfxCertLocation) && File.Exists(_config.Model.SocketSettings.PfxCertLocation))
				{
					if (!string.IsNullOrWhiteSpace(_config.Model.SocketSettings.PfxCertPassword))
						cert = X509CertificateLoader.LoadPkcs12FromFile(_config.Model.SocketSettings.PfxCertLocation, _config.Model.SocketSettings.PfxCertPassword);
					else
						cert = X509CertificateLoader.LoadPkcs12FromFile(_config.Model.SocketSettings.PfxCertLocation, string.Empty);
				}
				else if (!string.IsNullOrWhiteSpace(_config.Model.SocketSettings.PemCertLocation) &&
						 File.Exists(_config.Model.SocketSettings.PemCertLocation) &&
						 !string.IsNullOrWhiteSpace(_config.Model.SocketSettings.PemCertPrivateKeyLocation) &&
						 File.Exists(_config.Model.SocketSettings.PemCertPrivateKeyLocation))
				{
					if (!string.IsNullOrWhiteSpace(_config.Model.SocketSettings.PemCertPassword))
						cert = X509Certificate2.CreateFromEncryptedPemFile(_config.Model.SocketSettings.PemCertLocation, _config.Model.SocketSettings.PemCertPassword, _config.Model.SocketSettings.PemCertPrivateKeyLocation);
					else
						cert = X509Certificate2.CreateFromPemFile(_config.Model.SocketSettings.PemCertLocation, _config.Model.SocketSettings.PemCertPrivateKeyLocation);

					if (_config.Model.SocketSettings.PkcsCertHack)
						cert = X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pkcs12), null);
				}

				await _socketServer.Start(_config.Model.SocketSettings.ListenIp.ToString(), _config.Model.SocketSettings.ListenPort, _config.Model.SocketSettings.AspnetConsoleLog, cert, _config.Model.SocketSettings.SslProtocol);
				_socketServer.OnClientMessage += HandleClientCommand;
				_isRunning = true;
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
			}
		}

		public async Task Stop()
		{
			if (!_isRunning)
				return;

			_socketServer.OnClientMessage -= HandleClientCommand;
			await _socketServer.Stop();
			_isRunning = false;
		}

		public async Task HandleClientCommand(object sender, SocketMessageEventArgs model)
		{
			var response = new ClientResponse();
			try
			{
				switch (model.Message.ActionType)
				{
					case ClientMessageType.Ping:
						response.Type = ClientResponseType.Pong;
						break;
					case ClientMessageType.StartReader:
						await _core.StartScreenReading();
						break;
					case ClientMessageType.StopReader:
						await _core.StopScreenReading();
						break;
					case ClientMessageType.GetSAState:
						response = new ClientResponse<ScreenAmbienceStatus>()
						{
							Success = true,
							Message = string.Empty,
							Type = ClientResponseType.SAData,
							Data = new ScreenAmbienceStatus()
							{
								IsStarted = _screen.IsRunning,
								Frame = _screen.Frame,
								AverageDeltaTime = _screen.AverageDt,
								IsHueConnected = _hueCore.IsConnectedToBridge,
								UsingHue = _config.Model.HueSettings.UseHue,
								UsingRgb = _config.Model.RgbDeviceSettings.UseKeyboards || _config.Model.RgbDeviceSettings.UseMice || _config.Model.RgbDeviceSettings.UseMotherboard,
								UsingLightStrip = _config.Model.LightStripSettings.UseLightStrip,
								ScreenInfo = new ScreenInfo()
								{
									Id = _screen.ScreenInfo.Source,
									RealHeight = (int)_screen.ScreenInfo.RealHeight,
									RealWidth = (int)_screen.ScreenInfo.RealWidth,
									Height = (int)_screen.ScreenInfo.Height,
									Width = (int)_screen.ScreenInfo.Width,
									Rate = _screen.ScreenInfo.Rate
								}
							}
						};
						await SendResponse<ScreenAmbienceStatus>(model.ClientId, response);
						break;
				}
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
				response.Success = false;
				response.Message = "Exception";
				response.Type = ClientResponseType.None;
				await SendResponse(model.ClientId, response);
			}
		}

		private async Task SendResponse(Guid clientId, ClientResponse res)
		{
			try
			{
				var responseJson = JsonSerializer.Serialize(res, DefaultJsonOptions.JsonOptions);
				await _socketServer.SendResponse(clientId, responseJson);
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
			}
		}

		private async Task SendResponse<T>(Guid clientId, ClientResponse res)
		{
			try
			{
				var responseJson = JsonSerializer.Serialize(res as ClientResponse<T>, DefaultJsonOptions.JsonOptions);
				await _socketServer.SendResponse(clientId, responseJson);
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
			}
		}
	}
}
