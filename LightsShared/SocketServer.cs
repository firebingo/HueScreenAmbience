using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebControlClient.Client.Shared;
using WebControlClient.Shared;

namespace LightsShared.Sockets
{
	public class SocketMessageEventArgs
	{
		public Guid ClientId { get; init; }
		public ClientMessage Message { get; init; }
	}

	public class SocketServer
	{
		public delegate Task OnClientMessageEventHandler(object sender, SocketMessageEventArgs e);

		private readonly FileLogger _logger;
		private CancellationTokenSource _cancelSource;
		private CancellationToken _cancelToken;
		private Thread _cleanThread;
		private ConcurrentDictionary<Guid, WebSocket> _connectedClients;
		private IHost _webHost;

		public SocketServer(FileLogger logger)
		{
			_logger = logger;
		}

		public event OnClientMessageEventHandler OnClientMessage;

		public async Task Start(string listenIp, int listenPort, bool log = false, X509Certificate2 cert = null,
			SslProtocols sslProtocol = SslProtocols.Tls13 | SslProtocols.Tls12)
		{
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

			var builder = Host.CreateDefaultBuilder()
				.ConfigureWebHostDefaults(builder =>
				{
					builder
						.UseKestrel((hostingContext, options) =>
						{
							var localendpoint = IPEndPoint.Parse($"127.0.0.1:{listenPort}");
							var endpoint = IPEndPoint.Parse($"{listenIp}:{listenPort}");
							options.Listen(localendpoint);
							if (cert != null)
							{
								options.ConfigureHttpsDefaults(o => { o.SslProtocols = sslProtocol; });
								options.Listen(endpoint, listenOptions => { listenOptions.UseHttps(cert); });
							}
							else
								options.Listen(endpoint);
						})
						.UseEnvironment("Development")
						.Configure(app =>
						{
							app.UseWebSockets(new WebSocketOptions
							{
								KeepAliveInterval = TimeSpan.FromSeconds(30)
							});
							app.Use(OnHttpRequest);
						});
				})
				.ConfigureLogging((context, logging) =>
				{
					logging.ClearProviders();
					if (!log) return;
					logging.AddConsole();
					logging.AddDebug();
				});
			_webHost = builder.Build();

			_cleanThread = new Thread(SocketCleanLoop)
			{
				Name = "Socket Hub Client Clean Thread",
				Priority = ThreadPriority.BelowNormal
			};
			await _webHost.StartAsync(_cancelToken);
			_cleanThread.Start();
		}

		private async Task OnHttpRequest(HttpContext context, Func<Task> next)
		{
			if (context.WebSockets.IsWebSocketRequest)
			{
				try
				{
					using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
					var guid = Guid.NewGuid();
					if (!_connectedClients.TryAdd(guid, webSocket))
					{
						await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, string.Empty,
							_cancelToken);
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
				if (next != null)
					await next.Invoke();
			}
		}

		private void SocketCleanLoop()
		{
			do
			{
				var removeClients = (from client in _connectedClients
					where client.Value.State is WebSocketState.Closed or WebSocketState.Aborted
					select client.Key).ToList();

				foreach (var r in removeClients)
				{
					_connectedClients.TryRemove(r, out _);
				}

				removeClients.Clear();

				Thread.Sleep(1000);
			} while (!_cancelToken.IsCancellationRequested);
		}

		private async Task SocketClientLoop(Guid clientId)
		{
			using var bufferStream = new MemoryStream(4096);
			using var readStream = new StreamReader(bufferStream, Encoding.UTF8);
			var buffer = new byte[4096];
			do
			{
				try
				{
					if (!_connectedClients.TryGetValue(clientId, out var socket))
						break;
					if (socket.State is WebSocketState.Closed or WebSocketState.Aborted)
					{
						_connectedClients.TryRemove(clientId, out _);
						break;
					}

					var receiveResult = await socket.ReceiveAsync(buffer, _cancelToken);

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
							await bufferStream.WriteAsync(buffer, 0, receiveResult.Count, _cancelToken);
						} while (receiveResult.EndOfMessage);
					}
					else
					{
						totalRead += receiveResult.Count;
						await bufferStream.WriteAsync(buffer, 0, receiveResult.Count, _cancelToken);
					}

					bufferStream.Seek(0, SeekOrigin.Begin);
					//Since the memory stream is reused trim it to the actual length of bytes read.
					bufferStream.SetLength(totalRead);
					var data = await readStream.ReadToEndAsync(_cancelToken);

					if (!data.StartsWith('{'))
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

					var model = JsonSerializer.Deserialize<ClientMessage>(data, DefaultJsonOptions.JsonOptions);
					var eventArgs = new SocketMessageEventArgs
					{
						ClientId = clientId,
						Message = model
					};
					var invocations = OnClientMessage?.GetInvocationList();
					await Task.WhenAll(
						invocations?.Select(i => ((OnClientMessageEventHandler)i)(this, eventArgs)) ?? []);
				}
				catch (Exception ex)
				{
					_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
				}

				await Task.Delay(5, _cancelToken);
			} while (!_cancelToken.IsCancellationRequested);
		}

		public async Task SendResponse(Guid clientId, string res)
		{
			if (!_connectedClients.TryGetValue(clientId, out var socket))
				throw new Exception($"Socket with id {clientId} not found.");
			var bytes = Encoding.UTF8.GetBytes(res);
			await socket.SendAsync(bytes, WebSocketMessageType.Text, true, _cancelToken);
		}

		public async Task DisconnectClients()
		{
			foreach (var client in _connectedClients)
			{
				await client.Value.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Server Stopping",
					_cancelToken);
			}

			_connectedClients.Clear();
		}

		public async Task Stop()
		{
			await DisconnectClients();
			await _cancelSource.CancelAsync();
			if (_webHost != null)
			{
				await _webHost.StopAsync();
				_webHost.Dispose();
			}
		}
	}
}