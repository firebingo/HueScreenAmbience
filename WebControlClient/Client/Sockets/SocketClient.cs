﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebControlClient.Client.Shared;
using WebControlClient.Shared;

namespace WebControlClient.Client.Sockets
{
	public class SocketClientResponseEventArgs : EventArgs
	{
		public ClientResponse Response { get; set; }
	}

	public class SocketClient
	{
		private readonly Uri _url;
		private ClientWebSocket _socket;
		private CancellationToken _cancelToken;
		public bool IsClosed { get; private set; } = true;

		public delegate Task OnClientResponseEventHandler(object sender, SocketClientResponseEventArgs e);
		public event OnClientResponseEventHandler OnClientResponse;

		public SocketClient(string ip, int port, CancellationToken cancelToken)
		{
			_url = new Uri($"ws://{ip}:{port}");
			_cancelToken = cancelToken;
			_socket = new ClientWebSocket();
		}

		public async Task Connect()
		{
			await _socket.ConnectAsync(_url, _cancelToken);
			IsClosed = false;
			_ = Task.Run(() => ReceiveLoop());
		}

		public async Task Send(ClientMessage message)
		{
			var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, DefaultJsonOptions.JsonOptions));
			await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _cancelToken);
		}

		public async Task ReceiveLoop()
		{
			using var bufferStream = new MemoryStream(1024);
			using var readStream = new StreamReader(bufferStream, Encoding.UTF8);
			var buffer = new byte[1024];
			var data = string.Empty;
			do
			{
				if (_socket.State == WebSocketState.Closed)
				{
					try
					{
						_socket.Dispose();
					}
					catch { }
					IsClosed = true;
					break;
				}

				WebSocketReceiveResult receiveResult = await _socket.ReceiveAsync(buffer, _cancelToken);

				if (receiveResult.MessageType == WebSocketMessageType.Close)
				{
					await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", _cancelToken);
					try
					{
						_socket.Dispose();
					}
					catch { }
					IsClosed = true;
					break;
				}

				bufferStream.Seek(0, SeekOrigin.Begin);
				var totalRead = 0;
				if (!receiveResult.EndOfMessage)
				{
					do
					{
						receiveResult = await _socket.ReceiveAsync(buffer, _cancelToken);
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
					await _socket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "", _cancelToken);
					try
					{
						_socket.Dispose();
					}
					catch { }
					IsClosed = true;
					break;
				}

				var invokeList = new List<Task>();
				var model = JsonSerializer.Deserialize<ClientResponse>(data, DefaultJsonOptions.JsonOptions);
				switch (model.Type)
				{
					case ClientResponseType.SAData:
						{
							var eventArgs = new SocketClientResponseEventArgs()
							{
								Response = JsonSerializer.Deserialize<ClientResponse<ScreenAmbienceStatus>>(data, DefaultJsonOptions.JsonOptions)
							};
							foreach (var i in OnClientResponse.GetInvocationList())
							{
								invokeList.Add(((OnClientResponseEventHandler)i)(this, eventArgs));
							}
							break;
						}
					case ClientResponseType.Pong:
						break;
					default:
						{
							var eventArgs = new SocketClientResponseEventArgs()
							{
								Response = model
							};
							foreach (var i in OnClientResponse.GetInvocationList())
							{
								invokeList.Add(((OnClientResponseEventHandler)i)(this, eventArgs));
							}
							break;
						}
				}
				await Task.WhenAll(invokeList);
			} while (!_cancelToken.IsCancellationRequested);
		}

		public async Task Close()
		{
			await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, _cancelToken);
		}
	}
}
