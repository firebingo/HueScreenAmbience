﻿using LightsShared;
using LightsShared.Sockets;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using WebControlClient.Client.Shared;
using WebControlClient.Shared;

namespace LightStripClient.Sockets
{
	public class SocketHandler
	{
		private bool _isRunning = false;

		private readonly Config _config;
		private readonly FileLogger _logger;
		private readonly LightStripLighter _lighter;
		private SocketServer? _socketServer;


		public SocketHandler(Config config, FileLogger logger, LightStripLighter lighter)
		{
			_config = config;
			_logger = logger;
			_lighter = lighter;
		}

		public async Task Start()
		{
			if (!_config.Model.SocketSettings.EnableHubSocket || _isRunning)
				return;

			try
			{
				_socketServer = new SocketServer(_logger);
				X509Certificate2? cert = null;
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
				//Console.WriteLine($"Loaded X509Certificate2 {cert}");
				await _socketServer.Start(_config.Model.SocketSettings.ListenIp.ToString(), _config.Model.SocketSettings.ListenPort, _config.Model.SocketSettings.AspnetConsoleLog, cert, _config.Model.SocketSettings.SslProtocol);
				_socketServer.OnClientMessage += HandleClientCommand;
				_isRunning = true;
			}
			catch (Exception ex)
			{
				//Console.WriteLine($"ex {ex}");
				_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
			}
		}

		public async Task Stop()
		{
			if (!_isRunning || _socketServer == null)
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
					case ClientMessageType.GetLSCState:
						response = new ClientResponse<LightStripStatus>()
						{
							Success = true,
							Message = string.Empty,
							Type = ClientResponseType.LSCData,
							Data = new LightStripStatus()
							{
								Frame = _lighter.Frame,
								BoundIp = _lighter.BoundIp,
								BoundPort = _lighter.BoundPort,
								NetworkInterfaces = NetworkAddresses.GetNetworkAddresses()
							}
						};
						await SendResponse<LightStripStatus>(model.ClientId, response);
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
			if (_socketServer == null)
				return;

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
			if (_socketServer == null)
				return;

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
