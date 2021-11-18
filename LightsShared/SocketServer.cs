using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebControlClient.Client.Shared;
using WebControlClient.Shared;

namespace LightsShared.Sockets
{
	public class SocketMessageEventArgs
	{
		public Guid ClientId { get; set; }
		public ClientMessage Message { get; set; }
	}

	public class SocketServer
	{
		private IWebHost _webHost;
		private Thread _cleanThread;
		private ConcurrentDictionary<Guid, WebSocket> _connectedClients;
		private CancellationTokenSource _cancelSource;
		private CancellationToken _cancelToken;
		private readonly FileLogger _logger;

		public delegate Task OnClientMessageEventHandler(object sender, SocketMessageEventArgs e);
		public event OnClientMessageEventHandler OnClientMessage;

		public SocketServer(FileLogger logger)
		{
			_logger = logger;
		}

		public async Task Start(string listenIp, int listenPort, bool log = false, X509Certificate2 cert = null, SslProtocols sslProtocol = SslProtocols.Tls13 | SslProtocols.Tls12)
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

			_webHost = WebHost.CreateDefaultBuilder(Array.Empty<string>())
				.UseKestrel((hostingContext, options) =>
				{
					var localendpoint = IPEndPoint.Parse($"127.0.0.1:{listenPort}");
					var endpoint = IPEndPoint.Parse($"{listenIp}:{listenPort}");
					options.Listen(localendpoint);
					if (cert != null)
					{
						options.ConfigureHttpsDefaults(o =>
						{
							o.SslProtocols = sslProtocol;
						});
						options.Listen(endpoint, listenOptions =>
						{
							listenOptions.UseHttps(cert);
						});
					}
					else
						options.Listen(endpoint);
				})
				.UseEnvironment("Development")
				.Configure((app) =>
				{
					app.UseWebSockets(new WebSocketOptions()
					{
						KeepAliveInterval = TimeSpan.FromSeconds(30)
					});
					app.Use(OnHttpRequest);
				})
				.ConfigureLogging((context, logging) =>
				{
					logging.ClearProviders();
					if (log)
					{
						logging.AddConsole();
						logging.AddDebug();
					}
				})
				.Build();
			_cleanThread = new Thread(SocketCleanLoop)
			{
				Name = "Socket Hub Client Clean Thread",
				Priority = ThreadPriority.BelowNormal
			};
			await _webHost.StartAsync(_cancelToken);
			_cleanThread.Start();
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

					var model = JsonSerializer.Deserialize<ClientMessage>(data, DefaultJsonOptions.JsonOptions);
					var invokeList = new List<Task>();
					var eventArgs = new SocketMessageEventArgs()
					{
						ClientId = clientId,
						Message = model
					};
					foreach (var i in OnClientMessage.GetInvocationList())
					{
						invokeList.Add(((OnClientMessageEventHandler)i)(this, eventArgs));
					}
					await Task.WhenAll(invokeList);
				}
				catch (Exception ex)
				{
					_ = Task.Run(() => { _logger?.WriteLog(ex.ToString()); });
				}

				await Task.Delay(5);
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
		}
	}
}
