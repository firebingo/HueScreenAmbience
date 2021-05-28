using System;
using System.Threading.Tasks;
using HueScreenAmbience.Hue;
using WebControlClient.Client.Shared;
using WebControlClient.Shared;
using LightsShared.Sockets;
using LightsShared;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Security.Cryptography.X509Certificates;

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
			if (!_config.Model.socketSettings.enableHubSocket || _isRunning)
				return;

			try
			{
				_socketServer = new SocketServer(_logger);
				X509Certificate2 cert = null;
				if (!string.IsNullOrWhiteSpace(_config.Model.socketSettings.sslCertLocation) && File.Exists(_config.Model.socketSettings.sslCertLocation))
				{
					if (!string.IsNullOrWhiteSpace(_config.Model.socketSettings.sslCertPassword))
						cert = new X509Certificate2(_config.Model.socketSettings.sslCertLocation, _config.Model.socketSettings.sslCertPassword);
					else
						cert = new X509Certificate2(_config.Model.socketSettings.sslCertLocation);
				}
				await _socketServer.Start(_config.Model.socketSettings.listenIp.ToString(), _config.Model.socketSettings.listenPort, _config.Model.socketSettings.aspnetConsoleLog, cert, _config.Model.socketSettings.sslProtocol);
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
								UsingHue = _config.Model.hueSettings.useHue,
								UsingRgb = _config.Model.rgbDeviceSettings.useKeyboards || _config.Model.rgbDeviceSettings.useMice || _config.Model.rgbDeviceSettings.useMotherboard,
								UsingLightStrip = _config.Model.lightStripSettings.useLightStrip,
								ScreenInfo = new ScreenInfo()
								{
									Id = _screen.ScreenInfo.Source,
									RealHeight = _screen.ScreenInfo.RealHeight,
									RealWidth = _screen.ScreenInfo.RealWidth,
									Height = _screen.ScreenInfo.Height,
									Width = _screen.ScreenInfo.Width,
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
