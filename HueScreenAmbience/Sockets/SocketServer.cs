using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using HueScreenAmbience.Hue;
using System.Net.WebSockets;
using WebControlClient.Client.Shared;
using WebControlClient.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;

namespace HueScreenAmbience.Sockets
{
	public class SocketServer
	{
		private bool _isRunning = false;
		private IWebHost _webHost;
		private Thread _cleanThread;
		private ConcurrentDictionary<Guid, WebSocket> _connectedClients;
		private CancellationTokenSource _cancelSource;
		private CancellationToken _cancelToken;
		private JsonSerializerOptions _jsonOptions;

		private Core _core;
		private ScreenReader _screen;
		private HueCore _hueCore;
		private Config _config;
		private FileLogger _logger;

		public SocketServer()
		{
			_jsonOptions = new JsonSerializerOptions()
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			};
		}

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

			if (_cancelSource != null)
			{
				try
				{
					_cancelSource.Dispose();
				}
				catch { }
			}
			_cancelSource = new CancellationTokenSource();
			_cancelToken = _cancelSource.Token;
			_connectedClients = new ConcurrentDictionary<Guid, WebSocket>();

			try
			{
				var localurl = $"http://127.0.0.1:{_config.Model.socketSettings.listenPort}/";
				var url = $"http://{_config.Model.socketSettings.listenIp}:{_config.Model.socketSettings.listenPort}/";
				_webHost = WebHost.CreateDefaultBuilder(Array.Empty<string>())
					.UseKestrel()
					.UseEnvironment("Development")
					.Configure((app) =>
					{
						app.UseWebSockets(new WebSocketOptions()
						{
							ReceiveBufferSize = 4096,
							KeepAliveInterval = TimeSpan.FromSeconds(30)
						});
						app.Use(OnHttpRequest);
					})
					.UseUrls(new string[] { url, localurl })
					.Build();
				_cleanThread = new Thread(SocketCleanLoop)
				{
					Name = "Socket Hub Client Clean Thread",
					Priority = ThreadPriority.BelowNormal
				};
				_isRunning = true;
				await _webHost.StartAsync(_cancelToken);
				_cleanThread.Start();
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
		}

		public async Task OnHttpRequest(HttpContext context, Func<Task> next)
		{
			if (context.WebSockets.IsWebSocketRequest)
			{
				try
				{
					using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
					var guid = Guid.NewGuid();
					if (!_connectedClients.TryAdd(guid, webSocket))
					{
						await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, string.Empty, _cancelToken);
						return;
					}

					await SocketClientLoop(guid);
				}
				catch (Exception ex)
				{
					_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
					context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
				}
			}
			else
			{
				await next?.Invoke();
			}
		}

		public void SocketCleanLoop()
		{
			do
			{
				var removeClients = new List<Guid>();
				foreach (var client in _connectedClients)
				{
					if (client.Value.State == WebSocketState.Closed || client.Value.State == WebSocketState.Aborted)
					{
						removeClients.Add(client.Key);
					}
				}
				foreach (var r in removeClients)
				{
					_connectedClients.TryRemove(r, out _);
				}

				removeClients.Clear();

				Thread.Sleep(1000);
			} while (!_cancelToken.IsCancellationRequested);
		}

		public async Task SocketClientLoop(Guid clientId)
		{
			using var bufferStream = new MemoryStream(4096);
			using var readStream = new StreamReader(bufferStream, Encoding.UTF8);
			var buffer = new byte[4096];
			var data = string.Empty;
			do
			{
				try
				{
					if (!_connectedClients.TryGetValue(clientId, out var socket))
						break;
					if (socket.State == WebSocketState.Closed || socket.State == WebSocketState.Aborted)
					{
						_connectedClients.TryRemove(clientId, out _);
						break;
					}

					WebSocketReceiveResult receiveResult = await socket.ReceiveAsync(buffer, _cancelToken);

					if (receiveResult.MessageType == WebSocketMessageType.Close)
					{
						await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", _cancelToken);
						_connectedClients.TryRemove(clientId, out _);
						break;
					}

					bufferStream.Seek(0, SeekOrigin.Begin);
					var totalRead = 0;
					if (!receiveResult.EndOfMessage)
					{
						do
						{
							receiveResult = await socket.ReceiveAsync(buffer, _cancelToken);
							totalRead += receiveResult.Count;
							await bufferStream.WriteAsync(buffer, 0, receiveResult.Count);
						}
						while (receiveResult.EndOfMessage);
					}
					else
					{
						totalRead += receiveResult.Count;
						await bufferStream.WriteAsync(buffer, 0, receiveResult.Count);
					}

					bufferStream.Seek(0, SeekOrigin.Begin);
					//Since the memory stream is reused trim it to the actual length of bytes read.
					bufferStream.SetLength(totalRead);
					data = await readStream.ReadToEndAsync();

					if (!data.StartsWith("{"))
					{
						await socket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "", _cancelToken);
						try
						{
							socket.Dispose();
						}
						catch { }
						_connectedClients.TryRemove(clientId, out _);
						break;
					}

					var model = JsonSerializer.Deserialize<ClientMessage>(data, _jsonOptions);
					var clientResponse = await HandleClientCommand(model);
					string responseJson;
					switch (clientResponse.Type)
					{
						case ClientResponseType.SAData:
							responseJson = JsonSerializer.Serialize(clientResponse as ClientResponse<ScreenAmbienceStatus>, _jsonOptions);
							break;
						default:
							responseJson = JsonSerializer.Serialize(clientResponse, _jsonOptions);
							break;
					}
					var bytes = Encoding.UTF8.GetBytes(responseJson);
					await socket.SendAsync(bytes, WebSocketMessageType.Text, true, _cancelToken);
				}
				catch (Exception ex)
				{
					_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
				}

				await Task.Delay(5);
			} while (!_cancelToken.IsCancellationRequested);
		}

		public async Task<ClientResponse> HandleClientCommand(ClientMessage model)
		{
			var response = new ClientResponse();
			try
			{
				switch (model.ActionType)
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
						break;
				}
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
				response.Success = false;
				response.Message = "Exception";
				response.Type = ClientResponseType.None;
				return response;
			}

			response.Success = true;
			return response;
		}

		public async Task DisconnectClients()
		{
			foreach (var client in _connectedClients)
			{
				await client.Value.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Server Stopping", _cancelToken);
			}
			_connectedClients.Clear();
		}

		public async Task Stop()
		{
			await DisconnectClients();
			_cancelSource.Cancel();
			if (_webHost != null)
			{
				await _webHost.StopAsync();
				_webHost.Dispose();
			}
			_isRunning = false;
		}
	}
}
