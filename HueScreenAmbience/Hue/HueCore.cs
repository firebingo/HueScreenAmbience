using HueApi;
using HueApi.BridgeLocator;
using HueApi.ColorConverters;
using HueApi.ColorConverters.Original.Extensions;
using HueApi.Entertainment;
using HueApi.Entertainment.Extensions;
using HueApi.Entertainment.Models;
using HueApi.Models;
using HueApi.Models.Requests;
using LightsShared;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HueScreenAmbience.Hue
{
	public class HueCore
	{
		private bool _isRunning = false;
		private LocalHueApi _client = null;
		private StreamingHueClient _streamClient = null;
		private LocatedBridge _useBridge = null;
		public Room UseRoom { get; private set; } = null;
		private Guid _useRoomGroupLight;
		public EntertainmentConfiguration UseEntertainment { get; private set; } = null;
		private const string _appName = "HUEScreenAmbience";
		public bool IsConnectedToBridge { get; private set; } = false;
		private DateTime _lastHueChangeTime;
		private bool _sendingCommand;
		private Rgb24 _lastColor;
		private byte _colorChangeThreshold = 15;
		private TimeSpan _frameTimeSpan;
		private StreamingGroup _streamGroup;
		private EntertainmentLayer _streamBaseLayer;
		private CancellationTokenSource _cancelSource;
		private CancellationToken _cancelToken;
		private Dictionary<Byte, Rgb24> _lastLightColors;

		private Config _config;
		private FileLogger _logger;

		public void InstallServices(IServiceProvider map)
		{
			_config = map.GetService(typeof(Config)) as Config;
			_logger = map.GetService(typeof(FileLogger)) as FileLogger;
		}

		public async Task Start()
		{
			_frameTimeSpan = TimeSpan.FromMilliseconds(1000 / _config.Model.HueSettings.UpdateFrameRate);
			_lastColor = new Rgb24(255, 255, 255);
			_colorChangeThreshold = _config.Model.HueSettings.ColorChangeThreshold;
			if (_lastLightColors != null)
				_lastLightColors.Clear();
			else
				_lastLightColors = new Dictionary<byte, Rgb24>();
			await AutoConnectAttempt();
		}

		public async Task<bool> AutoConnectAttempt()
		{
			if (string.IsNullOrWhiteSpace(_config.Model.HueSettings.AppKey) || string.IsNullOrWhiteSpace(_config.Model.HueSettings.Ip))
				return false;

			Console.WriteLine("Attempting auto-connect");
			await ConnectToBridge();
			return IsConnectedToBridge;
		}

		public async Task ConnectToBridge()
		{
			if (_config.Model.HueSettings.HueType == HueType.Basic)
			{
				_client = new LocalHueApi(_config.Model.HueSettings.Ip, _config.Model.HueSettings.AppKey);
				IsConnectedToBridge = true;
				if (_config.Model.HueSettings.RoomId != Guid.Empty)
				{
					var rooms = await _client.GetRoomsAsync();
					if (rooms != null && rooms.Data.Count != 0)
					{
						UseRoom = rooms.Data.FirstOrDefault(x => x.Id == _config.Model.HueSettings.RoomId);
						_useRoomGroupLight = UseRoom.Services.FirstOrDefault(x => x.Rtype == "grouped_light").Rid;
					}
				}
			}
			else if (_config.Model.HueSettings.HueType == HueType.Entertainment)
			{
				_streamClient = new StreamingHueClient(_config.Model.HueSettings.Ip, _config.Model.HueSettings.AppKey, _config.Model.HueSettings.EntertainmentKey);
				IsConnectedToBridge = true;
				if (_config.Model.HueSettings.RoomId != Guid.Empty)
				{
					var rooms = await _streamClient.LocalHueApi.GetEntertainmentGroups();
					if (rooms != null && rooms.Data.Count != 0)
						UseEntertainment = rooms.Data.FirstOrDefault(x => x.Id == _config.Model.HueSettings.RoomId);
				}
			}
		}

		public static async Task<IEnumerable<LocatedBridge>> GetBridges()
		{
			IBridgeLocator locator = new HttpBridgeLocator();
			return await locator.LocateBridgesAsync(TimeSpan.FromSeconds(5));
		}

		public void SetBridge(LocatedBridge bridge)
		{
			_useBridge = bridge;
		}

		public async Task RegisterBridge(string name)
		{
			var appKey = await LocalHueApi.RegisterAsync(_useBridge.IpAddress, _appName, name);
			_client = new LocalHueApi(_useBridge.IpAddress, appKey.Username);
			_config.Model.HueSettings.Ip = _useBridge.IpAddress;
			_config.Model.HueSettings.AppKey = appKey.Username;
			_config.SaveConfig();
			await ConnectToBridge();
		}

		public async Task RegisterBridgeEntertainment(string name)
		{
			var appKey = await LocalHueApi.RegisterAsync(_useBridge.IpAddress, _appName, name, true);
			_streamClient = new StreamingHueClient(_useBridge.IpAddress, appKey.Username, appKey.StreamingClientKey);
			_config.Model.HueSettings.Ip = _useBridge.IpAddress;
			_config.Model.HueSettings.AppKey = appKey.Username;
			_config.Model.HueSettings.EntertainmentKey = appKey.StreamingClientKey;
			_config.SaveConfig();
			await ConnectToBridge();
		}

		public async Task<IEnumerable<Room>> GetRooms()
		{
			if (_config.Model.HueSettings.HueType == HueType.Basic)
				return (await _client.GetRoomsAsync()).Data;
			else
				return (await _streamClient.LocalHueApi.GetRoomsAsync()).Data;
		}

		public async Task<IEnumerable<EntertainmentConfiguration>> GetEntertainmentGroups()
		{
			if (_config.Model.HueSettings.HueType == HueType.Basic)
				return (await _client.GetEntertainmentGroups()).Data;
			else
				return (await _streamClient.LocalHueApi.GetEntertainmentGroups()).Data;
		}

		public void SetRoom(Room room)
		{
			UseRoom = room;
			_useRoomGroupLight = UseRoom.Services.FirstOrDefault(x => x.Rtype == "grouped_light").Rid;
			UseEntertainment = null;
			_config.Model.HueSettings.RoomId = UseRoom.Id;
			_config.SaveConfig();
		}

		public void SetEntertainmentRoom(EntertainmentConfiguration room)
		{
			UseEntertainment = room;
			UseRoom = null;
			_useRoomGroupLight = Guid.Empty;
			_config.Model.HueSettings.RoomId = UseEntertainment.Id;
			_config.SaveConfig();
		}

		public async Task OnStartReading()
		{
			try
			{
				if (_config.Model.HueSettings.HueType == HueType.Basic)
				{
					if (_config.Model.HueSettings.TurnLightOnIfOff)
					{
						var command = new UpdateGroupedLight();
						command.TurnOn();
						await _client.UpdateGroupedLightAsync(_useRoomGroupLight, command);
					}
				}
				else if (_config.Model.HueSettings.HueType == HueType.Entertainment)
				{
					_cancelSource = new CancellationTokenSource();
					_cancelToken = _cancelSource.Token;
					_streamGroup = new StreamingGroup(UseEntertainment.Channels);
					await _streamClient.ConnectAsync(UseEntertainment.Id);
					_streamBaseLayer = _streamGroup.GetNewLayer(true);
					foreach (var light in _streamBaseLayer)
					{
						if (_config.Model.HueSettings.TurnLightOnIfOff)
							light.SetState(_cancelToken, new RGBColor(1.0, 1.0, 1.0), 0.5);
					}

					_streamClient.ManualUpdate(_streamGroup);
				}
				_isRunning = true;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				_ = Task.Run(() => _logger?.WriteLog(ex.ToString()));
			}
		}

		public async Task OnStopReading()
		{
			if (!_isRunning)
				return;

			if (_config.Model.HueSettings.HueType == HueType.Basic && _client != null)
			{
				if (_config.Model.HueSettings.TurnLightOnIfOff)
				{
					var command = new UpdateGroupedLight();
					command.TurnOff();
					await _client.UpdateGroupedLightAsync(_useRoomGroupLight, command);
				}
			}
			else if (_config.Model.HueSettings.HueType == HueType.Entertainment && _streamClient != null)
			{
				if (_config.Model.HueSettings.ShutLightOffOnStop && _streamBaseLayer != null)
				{
					foreach (var light in _streamBaseLayer)
					{
						light.SetState(_cancelToken, brightness: 0.0, timeSpan: _frameTimeSpan);
					}
				}

				if (_streamGroup != null)
					_streamClient?.ManualUpdate(_streamGroup);

				_cancelSource?.Cancel();
				_cancelSource?.Dispose();
				_streamGroup = null;
				_streamBaseLayer = null;
				//Closing disposes the client so we need to reconnect if we want to reuse it.
				_streamClient?.Close();
				_streamClient = null;
				await AutoConnectAttempt();
			}
			_isRunning = false;
		}

		public async Task ChangeLightColorBasic(Rgb24 c)
		{
			if (_sendingCommand)
				return;
			var dt = DateTime.UtcNow - _lastHueChangeTime;
			//Hue bridge can only take so many updates at a time (7-10 a second) so this needs to be throttled
			if (dt.TotalMilliseconds < _frameTimeSpan.TotalMilliseconds)
				return;

			//If the last colors set are close enough to the current color keep the current color.
			//This is to prevent a lot of color jittering that can happen otherwise.
			var roundMin = _config.Model.HueSettings.MinRoundColor;
			var min = _config.Model.HueSettings.MinColorValue;
			var max = _config.Model.HueSettings.MaxColorValue;
			var r = Math.Floor(c.R * _config.Model.HueSettings.ColorMultiplier);
			var g = Math.Floor(c.G * _config.Model.HueSettings.ColorMultiplier);
			var b = Math.Floor(c.B * _config.Model.HueSettings.ColorMultiplier);
			if (_lastColor.R >= c.R - _colorChangeThreshold && _lastColor.R <= c.R + _colorChangeThreshold)
				r = _lastColor.R;
			if (_lastColor.G >= c.G - _colorChangeThreshold && _lastColor.G <= c.G + _colorChangeThreshold)
				g = _lastColor.G;
			if (_lastColor.B >= c.B - _colorChangeThreshold && _lastColor.B <= c.B + _colorChangeThreshold)
				b = _lastColor.B;
			if (r + g + b <= roundMin)
				r = g = b = 0.0;
			c = new Rgb24((byte)Math.Clamp(r, min, max), (byte)Math.Clamp(g, min, max), (byte)Math.Clamp(b, min, max));
			if (c == _lastColor)
				return;
			_lastColor = c;

			var command = new UpdateGroupedLight();
			command.SetColor(new RGBColor(c.R, c.G, c.B)).SetBrightness(ColorHelper.GetBrightness(c));
			_sendingCommand = true;
			try
			{
				await _client.UpdateGroupedLightAsync(_useRoomGroupLight, command);
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex.ToString()));
			}
			_lastHueChangeTime = DateTime.UtcNow;
			_sendingCommand = false;
		}

		public void UpdateEntertainmentGroupFromImage(MemoryStream image, int width, int height)
		{
			try
			{
				if (_sendingCommand || _cancelToken.IsCancellationRequested)
					return;
				if ((DateTime.UtcNow - _lastHueChangeTime).TotalMilliseconds < _frameTimeSpan.TotalMilliseconds)
					return;

				_sendingCommand = true;
				var start = DateTime.UtcNow;
				var (x, y) = (0, 0);
				Rgb24 c;
				foreach (var light in _streamBaseLayer)
				{
					(x, y) = MapLightLocationToImage(light.LightLocation, width, height);
					image.Seek(Helpers.GetImageCoordinate(width * 3, x, y), SeekOrigin.Begin);
					var roundMin = _config.Model.HueSettings.MinRoundColor;
					var min = _config.Model.HueSettings.MinColorValue;
					var max = _config.Model.HueSettings.MaxColorValue;
					var r = Math.Floor(image.ReadByte() * _config.Model.HueSettings.ColorMultiplier);
					var g = Math.Floor(image.ReadByte() * _config.Model.HueSettings.ColorMultiplier);
					var b = Math.Floor(image.ReadByte() * _config.Model.HueSettings.ColorMultiplier);
					if (_lastLightColors.ContainsKey(light.Id))
					{
						var lastColor = _lastLightColors[light.Id];
						var blendAmount = 1.0f - _config.Model.HueSettings.BlendLastColorAmount;
						if (blendAmount != 0.0f)
						{
							r = Math.Sqrt((1 - blendAmount) * Math.Pow(lastColor.R, 2) + blendAmount * Math.Pow(r, 2));
							g = Math.Sqrt((1 - blendAmount) * Math.Pow(lastColor.G, 2) + blendAmount * Math.Pow(g, 2));
							b = Math.Sqrt((1 - blendAmount) * Math.Pow(lastColor.B, 2) + blendAmount * Math.Pow(b, 2));
						}
						if (lastColor.R >= r - _colorChangeThreshold && lastColor.R <= r + _colorChangeThreshold)
							r = lastColor.R;
						if (lastColor.G >= g - _colorChangeThreshold && lastColor.G <= g + _colorChangeThreshold)
							g = lastColor.G;
						if (lastColor.B >= b - _colorChangeThreshold && lastColor.B <= b + _colorChangeThreshold)
							b = lastColor.B;
						if (r + g + b <= roundMin)
							r = g = b = 0.0;
						c = new Rgb24((byte)Math.Clamp(r, min, max), (byte)Math.Clamp(g, min, max), (byte)Math.Clamp(b, min, max));
						_lastLightColors[light.Id] = c;
					}
					else
					{
						c = new Rgb24((byte)Math.Clamp(r, min, max), (byte)Math.Clamp(g, min, max), (byte)Math.Clamp(b, min, max));
						_lastLightColors.Add(light.Id, c);
					}

					light.SetState(_cancelToken, new RGBColor(c.R, c.G, c.B), 1.0);
				}

				_streamClient.ManualUpdate(_streamGroup);

				//Console.WriteLine($"UpdateEntertainmentGroupFromImage Time: {(DateTime.UtcNow - start).TotalMilliseconds}");
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex.ToString()));
			}
			_lastHueChangeTime = DateTime.UtcNow;
			_sendingCommand = false;
		}

		private static (int x, int y) MapLightLocationToImage(HuePosition location, int width, int height)
		{
			//Hue gives coordinates relative to center of room where -1 is far left and 1 is far right etc.
			// So we need to remap it to 0-1 range then get value relative to image.
			// Y also needs to be flipped as front of room is 1 which should correspond to 0 in the image
			var x = (int)Math.Floor((location.X - -1.0) / 2 * (width));
			var y = (int)Math.Floor((1.0 - (location.Y - -1.0) / 2) * (height));
			return (x, y);
		}
	}
}
