using System;
using System.Collections.Generic;
using System.Device.Spi;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Iot.Device.Ws28xx;
using LightsShared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HueScreenAmbience.LightStrip
{
	public class StripLighterLight
	{
		public PointF Location;
		public Rgb24 Color;
		public Rgb24? LastColor;
		//These are used to cache the lights x and y coordinates so I don't have to recalculate them every frame
		public int Width;
		public int Height;
		public Point? CacheLocation = null;
	}

	public class StripLighter : IDisposable
	{
		private static readonly int _colorByteCount = 3;
		private static readonly int _packetMaxSize = 256;
		private static readonly int _packetHeaderSize =
			Marshal.SizeOf((int)0) + //version int 
			Marshal.SizeOf((long)0) + //frame number
			1 + //number of sequences
			1 + //packet sequence number
			Marshal.SizeOf((int)0); //color byte count
		public static readonly int LightServerVersion = 0;

		private FileLogger _logger;
		private Config _config;
		private Socket _lightClientSocket;
		private IPEndPoint _lightClientEndpoint;
		private List<StripLighterLight> _lights;
		private Dictionary<byte, MemoryStream> _serializeStreams;
		private byte _currentStream;
		private byte _packetCount;
		private int _packetColorSize;
		private bool _started = false;
		private bool _updating = false;
		private readonly Rgb24 _resetColor = new Rgb24(0, 0, 0);
		private DateTime _lastChangeTime;
		private TimeSpan _frameTimeSpan;

		private SpiDevice _device;
		private Ws2812b _lightStrip;

		public StripLighter()
		{
		}

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
		}

		public void Start()
		{
			try
			{
				if (_started)
					return;

				if (_lights != null)
					_lights.Clear();
				else
					_lights = new List<StripLighterLight>();

				if (_config.Model.lightStripSettings.lights?.Any() ?? false)
				{
					foreach (var light in _config.Model.lightStripSettings.lights)
					{
						_lights.Add(new StripLighterLight()
						{
							Location = new PointF(light.X, light.Y),
							Color = Color.FromRgb(0, 0, 0),
							LastColor = null
						});
					}
				}

				if (_config.Model.piCameraSettings.isPi && _config.Model.piCameraSettings.lightsLocal)
				{
					try
					{
						var settings = new SpiConnectionSettings(0, 0)
						{
							ClockFrequency = 2400000,
							Mode = SpiMode.Mode0,
							DataBitLength = 8
						};

						_device = SpiDevice.Create(settings);
						_lightStrip = new Ws2812b(_device, _lights.Count);
					}
					catch (Exception ex)
					{
						_device = null;
						_lightStrip = null;
						Console.WriteLine(ex.ToString());
						_ = Task.Run(() => _logger?.WriteLog(ex.ToString()));
					}
				}
				else
				{
					if (_lightClientSocket != null)
						_lightClientSocket.Dispose();

					_lightClientSocket = new Socket(_config.Model.lightStripSettings.remoteAddressIp.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
					_lightClientEndpoint = new IPEndPoint(_config.Model.lightStripSettings.remoteAddressIp, _config.Model.lightStripSettings.remotePort);

					if (_serializeStreams != null)
					{
						foreach (var stream in _serializeStreams)
						{
							stream.Value.Dispose();
						}
						_serializeStreams.Clear();
					}
					_serializeStreams = new Dictionary<byte, MemoryStream>();
					//each light takes 3 bytes for its color
					_packetColorSize = _packetMaxSize - _packetHeaderSize;
					_packetCount = (byte)Math.Ceiling((_lights.Count * _colorByteCount) / (double)(_packetColorSize));
					for (byte i = 0; i < _packetCount; ++i)
					{
						_serializeStreams.Add(i, new MemoryStream(_packetMaxSize));
					}
				}
				_started = true;

				_frameTimeSpan = TimeSpan.FromMilliseconds(1000 / _config.Model.lightStripSettings.updateFrameRate);
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}
		}

		public void Stop()
		{
			try
			{
				if (!_started)
					return;

				//if were updating lights already spin waiting for it to finish before we try to clear the lights
				do
				{
					System.Threading.Thread.Sleep(5);
				} while (_updating);

				_updating = true;
				if (_config.Model.piCameraSettings.isPi && _config.Model.piCameraSettings.lightsLocal)
				{
					foreach (var light in _lights)
					{
						light.Color = Color.FromRgb(_resetColor.R, _resetColor.G, _resetColor.B);
						light.LastColor = light.Color;
					}
					_lightStrip?.Image?.Clear();
					_lightStrip?.Update();

					_device?.Dispose();
					_device = null;
					_lightStrip = null;
				}
				else
				{
					SerializeLightMetadata(-1);
					foreach (var light in _lights)
					{
						light.Color = Color.FromRgb(_resetColor.R, _resetColor.G, _resetColor.B);
						light.LastColor = light.Color;
						SerializeLightColor(light.Color);
					}

					SerializePad();

					//Send packets and then reset streams to start
					foreach (var stream in _serializeStreams)
					{
						_lightClientSocket.SendTo(stream.Value.ToArray(), _lightClientEndpoint);
						stream.Value.Seek(0, SeekOrigin.Begin);
					}
					_currentStream = 0;
				}
				_started = false;
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}
			_updating = false;
		}

		public void UpdateFromImage(MemoryStream image, int width, int height, long frame)
		{
			try
			{
				if ((DateTime.UtcNow - _lastChangeTime).TotalMilliseconds < _frameTimeSpan.TotalMilliseconds)
					return;

				var start = DateTime.UtcNow;

				if (!_lights?.Any() ?? true)
					return;
				if (_updating)
					return;

				_updating = true;
				if (_config.Model.piCameraSettings.isPi && _config.Model.piCameraSettings.lightsLocal)
					UpdateImagePi(image, width, height);
				else
					UpdateImageServer(image, width, height, frame);

				//Console.WriteLine($"StripLighter UpdateFromImage Time: {(_lastChangeTime - start).TotalMilliseconds}");
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}

			_updating = false;
		}

		public void UpdateImageServer(MemoryStream image, int width, int height, long frame)
		{
			SerializeLightMetadata(frame);
			foreach (var light in _lights)
			{
				UpdateLightColor(image, light, width, height);
				SerializeLightColor(light.Color);
			}

			SerializePad();

			//Send packets and then reset streams to start
			foreach (var stream in _serializeStreams)
			{
				_lightClientSocket.SendTo(stream.Value.ToArray(), _lightClientEndpoint);
				stream.Value.Seek(0, SeekOrigin.Begin);
			}
			_currentStream = 0;
			_lastChangeTime = DateTime.UtcNow;
		}

		public void UpdateImagePi(MemoryStream image, int width, int height)
		{
			for (var i = 0; i < _lights.Count; ++i)
			{
				UpdateLightColor(image, _lights[i], width, height);
				_lightStrip?.Image?.SetPixel(i, 0, _lights[i].Color);
			}
			_lightStrip?.Update();
		}

		public void UpdateLightColor(MemoryStream image, StripLighterLight light, int width, int height)
		{
			int x;
			int y;
			if (light.CacheLocation.HasValue && width == light.Width && height == light.Height)
			{
				(x, y) = (light.CacheLocation.Value.X, light.CacheLocation.Value.Y);
			}
			else
			{
				(x, y) = MapLightLocationToImage(light.Location, width, height);
				light.Width = width;
				light.Height = height;
				light.CacheLocation = new Point(x, y);
			}

			image.Seek(Helpers.GetImageCoordinate(width * 3, x, y), SeekOrigin.Begin);
			var r = Math.Floor(image.ReadByte() * _config.Model.lightStripSettings.colorMultiplier);
			var g = Math.Floor(image.ReadByte() * _config.Model.lightStripSettings.colorMultiplier);
			var b = Math.Floor(image.ReadByte() * _config.Model.lightStripSettings.colorMultiplier);
			if (light.LastColor.HasValue)
			{
				var blendAmount = 1.0f - _config.Model.lightStripSettings.blendLastColorAmount;
				if (blendAmount != 0.0f)
				{
					r = Math.Sqrt((1 - blendAmount) * Math.Pow(light.LastColor.Value.R, 2) + blendAmount * Math.Pow(r, 2));
					g = Math.Sqrt((1 - blendAmount) * Math.Pow(light.LastColor.Value.G, 2) + blendAmount * Math.Pow(g, 2));
					b = Math.Sqrt((1 - blendAmount) * Math.Pow(light.LastColor.Value.B, 2) + blendAmount * Math.Pow(b, 2));
				}
			}
			light.Color = Color.FromRgb((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
			if (_config.Model.lightStripSettings.saturateColors != 1.0f)
				light.Color = ColorHelper.SaturateColor(light.Color, _config.Model.lightStripSettings.saturateColors);
			light.LastColor = light.Color;
		}

		private static (int x, int y) MapLightLocationToImage(PointF location, int width, int height)
		{
			var x = (int)Math.Floor(location.X * width);
			var y = (int)Math.Floor(location.Y * height);
			return (x, y);
		}

		private void SerializeLightMetadata(long frame)
		{
			var lightByteTotal = _lights.Count * _colorByteCount;
			var lightBytesLeft = lightByteTotal;
			//Make sure we are splitting on 3 byte rgb boundry so we dont get bytes for the same color in different packets
			var packetMod = _packetColorSize - (_packetColorSize % _colorByteCount);
			foreach (var stream in _serializeStreams)
			{
				var colorBytes = 0;
				if (lightBytesLeft > packetMod)
					colorBytes = packetMod;
				else
					colorBytes = lightBytesLeft;
				stream.Value.Write(BitConverter.GetBytes(LightServerVersion));
				stream.Value.Write(BitConverter.GetBytes(frame));
				stream.Value.WriteByte(_packetCount);
				stream.Value.WriteByte(stream.Key);
				stream.Value.Write(BitConverter.GetBytes(colorBytes));
				lightBytesLeft -= packetMod;
			}
		}

		private void SerializeLightColor(Rgb24 color)
		{
			var stream = _serializeStreams[_currentStream];
			if ((stream.Position - _packetHeaderSize) + _colorByteCount > _packetColorSize)
			{
				_currentStream++;
				stream = _serializeStreams[_currentStream];
			}
			stream.WriteByte(color.R);
			stream.WriteByte(color.G);
			stream.WriteByte(color.B);
		}

		private void SerializePad()
		{
			foreach (var stream in _serializeStreams)
			{
				do
				{
					stream.Value.WriteByte(0);
				}
				while (stream.Value.Position < _packetMaxSize);
			}
		}

		public void Dispose()
		{
			_lights?.Clear();
			_lights = null;
			_lightClientSocket?.Dispose();
			_lightClientSocket = null;
			_lightClientEndpoint = null;
			if (_serializeStreams != null)
			{
				foreach (var stream in _serializeStreams)
				{
					stream.Value.Dispose();
				}
				_serializeStreams.Clear();
				_serializeStreams = null;
			}
			_device?.Dispose();
			_device = null;
			_lightStrip?.Image?.Clear();
			_lightStrip = null;
			GC.SuppressFinalize(this);
		}
	}
}
