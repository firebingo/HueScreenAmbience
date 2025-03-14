﻿using Iot.Device.Ws28xx;
using LightsShared;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Device.Spi;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LightStripClient
{
	public class LightPacketHeader
	{
		public int LightServerVersion;
		public long FrameCount;
		public byte SequenceCount;
		public byte SequenceNumber;
		public int ColorByteCount;
	}

	public class LightStripLighter : IDisposable
	{
		private static readonly int _colorByteCount = 3;
		private static readonly int _packetMaxSize = 256;
		public static readonly int LightServerVersion = 0;

		private readonly Config? _config;
		private readonly FileLogger? _logger;
		private Socket? _lightClientSocket;
		private IPEndPoint? _lightServerEndpoint;
		private EndPoint? _lightServerEndpointRemote;
		private Thread? _processThread;
		private SpiDevice? _device;
		private Ws2812b? _lightStrip;
		private bool _running;
		private byte[] _buffer = [];
		private readonly MemoryStream _readStream;
		private long _currentFrame = -1;
		private byte _packetCount;
		private byte _frameReceivedPackets;
		private Dictionary<byte, List<Rgb24>>? _packets;

		public long Frame { get => _currentFrame; }
		public string BoundIp
		{
			get
			{
				if (_lightServerEndpoint != null)
					return _lightServerEndpoint.Address.ToString();
				return string.Empty;
			}
		}
		public string BoundPort
		{
			get
			{
				if (_lightServerEndpoint != null)
					return _lightServerEndpoint.Port.ToString();
				return string.Empty;
			}
		}

		public LightStripLighter(Config config, FileLogger logger)
		{
			_config = config;
			_logger = logger;
			_readStream = new MemoryStream(_packetMaxSize);
		}

		public void Start()
		{
			if (_running)
				return;

			if (_config == null)
				throw new Exception("Config is null");

			if (_lightClientSocket != null)
			{
				if (_lightClientSocket.IsBound)
					_lightClientSocket.Close();
				_lightClientSocket.Dispose();
				_lightClientSocket = null;
			}

			if (_config.Model.RemoteAddress == null || _config.Model.RemoteAddressIp == null)
			{
				_lightServerEndpoint = new IPEndPoint(IPAddress.Any, _config.Model.ReceivePort);
				_lightServerEndpointRemote = _lightServerEndpoint;
				_lightClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			}
			else
			{
				_lightServerEndpoint = new IPEndPoint(_config.Model.RemoteAddressIp, _config.Model.ReceivePort);
				_lightServerEndpointRemote = _lightServerEndpoint;
				_lightClientSocket = new Socket(_config.Model.RemoteAddressIp.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
			}

			_lightClientSocket.ReceiveTimeout = _config.Model.ReceiveTimeout;
			_lightClientSocket.Bind(_lightServerEndpointRemote);

			_buffer = new byte[_packetMaxSize];

			_packets = [];

			var settings = new SpiConnectionSettings(0, 0)
			{
				ClockFrequency = 2400000,
				Mode = SpiMode.Mode0,
				DataBitLength = 8
			};

			try
			{
				_device = SpiDevice.Create(settings);
				_lightStrip = new Ws2812b(_device, _config.Model.LightCount);
			}
			catch (Exception ex)
			{
				_device = null;
				_lightStrip = null;
				Console.WriteLine(ex.ToString());
				_ = Task.Run(() => _logger?.WriteLog(ex.ToString()));
			}

			_running = true;
			_processThread = new Thread(UpdateLightsLoop);
			_processThread.Name = "Light Client Update Thread";
			_processThread.Start();
		}

		public void UpdateLightsLoop()
		{
			do
			{
				try
				{
					if (!_running)
						break;

					if (_lightClientSocket == null || _lightServerEndpoint == null || _lightServerEndpointRemote == null || _packets == null)
					{
						_ = Task.Run(() => _logger?.WriteLog($"Socket or endpoints are null"));
						Stop();
						break;
					}

					_lightClientSocket.ReceiveFrom(_buffer, ref _lightServerEndpointRemote);
					_readStream.Seek(0, SeekOrigin.Begin);
					_readStream.Write(_buffer);
					_frameReceivedPackets++;

					if (!_running)
						break;

					var header = ReadPacketHeader();

					_packetCount = header.SequenceCount;
					//If we get a -1 reset lights.
					if (header.FrameCount == -1)
					{
						ResetLights();
						continue;
					}

					//If we get a packet for a later frame abandon the old one and reset our packet list.
					if (header.FrameCount > _currentFrame)
					{
						_currentFrame = header.FrameCount;
						_frameReceivedPackets = 1;
						_packets.Clear();
						for (byte i = 0; i < _packetCount; ++i)
						{
							_packets.Add(i, []);
						}
					}
					//If we get a previous frame just ignore it
					else if (header.FrameCount < _currentFrame)
					{
						continue;
					}

					ReadPacketColors(header.SequenceNumber, header.ColorByteCount);

					if (!_running)
						break;

					//Console.WriteLine($"Received and read packet {header.SequenceNumber} for frame {header.FrameCount} with {_packets[header.SequenceNumber].Count} colors");

					if (_packetCount == _frameReceivedPackets)
					{
						if (_config?.Model.LightCount != _packets.Sum(x => x.Value.Count))
						{
							Console.WriteLine($"Light count received does not match configured count ({_config?.Model.LightCount}), received ({_packets.Sum(x => x.Value.Count)})");
							continue;
						}

						Console.WriteLine($"All packets received for frame {header.FrameCount} with {_packets.Sum(x => x.Value.Count)} colors");
						var lightIter = 0;
						for (byte i = 0; i < _packets.Count; ++i)
						{
							for (var j = 0; j < _packets[i].Count; ++j)
							{
								//Console.WriteLine($"Frame {_currentFrame}: Setting light {lightIter.ToString().PadLeft(2, '0')} to color {_packets[i][j].R},{_packets[i][j].G},{_packets[i][j].B}");
								_lightStrip?.Image.SetPixel(lightIter++, 0, _packets[i][j]);
							}
						}
						_lightStrip?.Update();
					}
				}
				catch (SocketException tex)
				{
					if (tex.SocketErrorCode == SocketError.TimedOut)
					{
						Console.WriteLine($"No packets received in {_config?.Model.ReceiveTimeout}ms");
						ResetLights();
						//_ = Task.Run(() => _logger?.WriteLog($"Receive Timeout: {tex}"));
					}
					else
						_ = Task.Run(() => _logger?.WriteLog($"SocketException: {tex}"));
					continue;
				}
				catch (IOException ex)
				{
					Console.WriteLine($"IO Exception\n{ex}");
					_ = Task.Run(() => _logger?.WriteLog($"IO Exception\n{ex}"));
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					_ = Task.Run(() => _logger?.WriteLog(ex.ToString()));
					continue;
				}
			} while (_running);
		}

		public void ResetLights()
		{
			_currentFrame = -1;
			_lightStrip?.Image?.Clear();
			_lightStrip?.Update();
		}

		public void Stop()
		{
			ResetLights();
			if (_lightClientSocket?.IsBound ?? false)
				_lightClientSocket.Close();

			_device?.Dispose();
			_device = null;
			_lightStrip = null;

			_buffer = new byte[_packetMaxSize];
			_running = false;
		}

		private LightPacketHeader ReadPacketHeader()
		{
			var header = new LightPacketHeader();
			_readStream.Seek(0, SeekOrigin.Begin);

			unsafe
			{
				byte[] readBuffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
				_readStream.Read(readBuffer, 0, sizeof(int));
				header.LightServerVersion = BitConverter.ToInt32(readBuffer);
				ArrayPool<byte>.Shared.Return(readBuffer);

				readBuffer = ArrayPool<byte>.Shared.Rent(sizeof(long));
				_readStream.Read(readBuffer, 0, sizeof(long));
				header.FrameCount = BitConverter.ToInt64(readBuffer);
				ArrayPool<byte>.Shared.Return(readBuffer);

				header.SequenceCount = (byte)_readStream.ReadByte();

				header.SequenceNumber = (byte)_readStream.ReadByte();

				readBuffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
				_readStream.Read(readBuffer, 0, sizeof(int));
				header.ColorByteCount = BitConverter.ToInt32(readBuffer);
				ArrayPool<byte>.Shared.Return(readBuffer);
			}

			return header;
		}

		private void ReadPacketColors(byte sequence, int colorByteCount)
		{
			if (_packets == null)
				return;

			for (var i = 0; i < colorByteCount; i += _colorByteCount)
			{
				var r = (byte)_readStream.ReadByte();
				var g = (byte)_readStream.ReadByte();
				var b = (byte)_readStream.ReadByte();
				_packets[sequence].Add(new Rgb24(r, g, b));
			}
		}

		public void Dispose()
		{
			_running = false;
			_lightClientSocket?.Dispose();
			_readStream?.Dispose();
			if (_packets != null)
			{
				foreach (var packet in _packets)
				{
					packet.Value?.Clear();
				}
				_packets.Clear();
			}
			_packets = null;
			GC.SuppressFinalize(this);
		}
	}
}
