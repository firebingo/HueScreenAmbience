using Q42.HueApi;
using Q42.HueApi.ColorConverters;
using Q42.HueApi.Interfaces;
using Q42.HueApi.Models.Bridge;
using Q42.HueApi.Models.Groups;
using Q42.HueApi.ColorConverters.HSB;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Q42.HueApi.Streaming;
using Q42.HueApi.Streaming.Models;
using System.Threading;
using Q42.HueApi.Streaming.Extensions;
using ImageMagick;
using Q42.HueApi.Streaming.Effects;

namespace HueScreenAmbience.Hue
{
	public class HueCore
	{
		private ILocalHueClient _client = null;
		private StreamingHueClient _streamClient = null;
		private LocatedBridge _useBridge = null;
		public Group UseRoom { get; private set; } = null;
		private const string _appName = "HUEScreenAmbience";
		public bool IsConnectedToBridge { get; private set; } = false;
		private DateTime _lastHueChangeTime;
		private bool _sendingCommand;
		private Color _lastColor;
		private byte _colorChangeThreshold = 15;
		private int _frameRate = 8;
		private TimeSpan _frameTimeSpan;
		private StreamingGroup _streamGroup;
		private EntertainmentLayer _streamBaseLayer;
		private CancellationTokenSource _cancelSource;
		private CancellationToken _cancelToken;

		private Config _config;
		private FileLogger _logger;

		public void InstallServices(IServiceProvider map)
		{
			_config = map.GetService(typeof(Config)) as Config;
			_logger = map.GetService(typeof(FileLogger)) as FileLogger;
		}

		public void Start()
		{
			_frameRate = _config.Model.hueSettings.updateFrameRate;
			_frameTimeSpan = TimeSpan.FromMilliseconds(1000 / _frameRate);
			_lastColor = Color.FromArgb(255, 255, 255);
			_colorChangeThreshold = _config.Model.hueSettings.colorChangeThreshold;
			Task.Run(() => AutoConnectAttempt());
		}

		public async Task<bool> AutoConnectAttempt()
		{
			if (string.IsNullOrWhiteSpace(_config.Model.hueSettings.appKey) || string.IsNullOrWhiteSpace(_config.Model.hueSettings.ip))
				return false;

			Console.WriteLine("Attempting auto-connect");
			await ConnectToBridge();
			return IsConnectedToBridge;
		}

		public async Task ConnectToBridge()
		{
			if (_config.Model.hueSettings.hueType == HueType.Basic)
			{
				_client = new LocalHueClient(_config.Model.hueSettings.ip);
				_client.Initialize(_config.Model.hueSettings.appKey);
				IsConnectedToBridge = true;
				if (!string.IsNullOrWhiteSpace(_config.Model.hueSettings.roomId))
				{
					var Groups = await _client.GetGroupsAsync();
					if (Groups != null && Groups.Count != 0)
						UseRoom = Groups.FirstOrDefault(x => x.Id == _config.Model.hueSettings.roomId);
				}
			}
			else if (_config.Model.hueSettings.hueType == HueType.Entertainment)
			{
				_streamClient = new StreamingHueClient(_config.Model.hueSettings.ip, _config.Model.hueSettings.appKey, _config.Model.hueSettings.entertainmentKey);
				IsConnectedToBridge = true;
				if (!string.IsNullOrWhiteSpace(_config.Model.hueSettings.groupId))
				{
					var Groups = await _streamClient.LocalHueClient.GetEntertainmentGroups();
					if (Groups != null && Groups.Count != 0)
						UseRoom = Groups.FirstOrDefault(x => x.Id == _config.Model.hueSettings.groupId);
				}
			}
		}

		public async Task<IEnumerable<LocatedBridge>> GetBridges()
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
			_client = new LocalHueClient(_useBridge.IpAddress);
			var appKey = await _client.RegisterAsync(_appName, name);
			_client.Initialize(appKey);
			IsConnectedToBridge = true;
			_config.Model.hueSettings.ip = _useBridge.IpAddress;
			_config.Model.hueSettings.appKey = appKey;
			_config.SaveConfig();
			await ConnectToBridge();
		}

		public async Task RegisterBridgeEntertainment(string name)
		{
			_client = new LocalHueClient(_useBridge.IpAddress);
			var registerResult = await _client.RegisterAsync(_appName, name, true);
			_streamClient = new StreamingHueClient(registerResult.Ip, registerResult.Username, registerResult.StreamingClientKey);
			IsConnectedToBridge = true;
			_config.Model.hueSettings.ip = _useBridge.IpAddress;
			_config.Model.hueSettings.appKey = registerResult.Username;
			_config.Model.hueSettings.entertainmentKey = registerResult.StreamingClientKey;
			_config.SaveConfig();
			await ConnectToBridge();
		}

		public async Task<IEnumerable<Group>> GetGroups()
		{
			if (_config.Model.hueSettings.hueType == HueType.Basic)
				return await _client.GetGroupsAsync();
			else
				return await _streamClient.LocalHueClient.GetGroupsAsync();
		}

		public async Task<IEnumerable<Group>> GetEntertainmentGroups()
		{
			if (_config.Model.hueSettings.hueType == HueType.Basic)
				return await _client.GetEntertainmentGroups();
			else
				return await _streamClient.LocalHueClient.GetEntertainmentGroups();
		}

		public void SetRoom(Group group)
		{
			UseRoom = group;
			_config.Model.hueSettings.roomId = UseRoom.Id;
			_config.SaveConfig();
		}

		public async Task OnStartReading()
		{
			try
			{
				if (_config.Model.hueSettings.hueType == HueType.Basic)
				{
					if (_config.Model.hueSettings.turnLightOnIfOff)
					{
						var command = new LightCommand
						{
							On = true,
							TransitionTime = _frameTimeSpan
						};
						command.TurnOn();
						await _client.SendCommandAsync(command, UseRoom.Lights);
					}
				}
				else if (_config.Model.hueSettings.hueType == HueType.Entertainment)
				{
					_cancelSource = new CancellationTokenSource();
					_cancelToken = _cancelSource.Token;
					_streamGroup = new StreamingGroup(UseRoom.Locations);
					await _streamClient.Connect(UseRoom.Id);
					_streamBaseLayer = _streamGroup.GetNewLayer(true);
					foreach (var light in _streamBaseLayer)
					{
						if (_config.Model.hueSettings.turnLightOnIfOff)
							light.SetState(_cancelToken, new RGBColor(1.0, 1.0, 1.0), 0.5);
					}

					_streamClient.ManualUpdate(_streamGroup);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				_ = Task.Run(() => _logger?.WriteLog(ex.ToString()));
			}
		}

		public async Task OnStopReading()
		{
			if (_config.Model.hueSettings.hueType == HueType.Basic)
			{
				if (_config.Model.hueSettings.shutLightOffOnStop)
				{
					var command = new LightCommand
					{
						On = false,
						TransitionTime = _frameTimeSpan
					};
					command.TurnOff();
					await _client.SendCommandAsync(command, UseRoom.Lights);
				}
			}
			else if (_config.Model.hueSettings.hueType == HueType.Entertainment)
			{
				if (_config.Model.hueSettings.shutLightOffOnStop)
				{
					foreach (var light in _streamBaseLayer)
					{
						light.SetState(_cancelToken, brightness: 0.0, timeSpan: _frameTimeSpan);
					}
				}

				_streamClient.ManualUpdate(_streamGroup);

				_cancelSource.Cancel();
				_cancelSource.Dispose();
				_streamGroup = null;
				_streamBaseLayer = null;
				//Closing disposes the client so we need to reconnect if we want to reuse it.
				_streamClient.Close();
				_streamClient = null;
				await ConnectToBridge();
			}
		}

		public async Task ChangeLightColorBasic(Color c)
		{
			if (_sendingCommand)
				return;
			var dt = DateTime.UtcNow - _lastHueChangeTime;
			//Hue bridge can only take so many updates at a time (7-10 a second) so this needs to be throttled
			if (dt.TotalMilliseconds < _frameTimeSpan.TotalMilliseconds)
				return;

			//If the last colors set are close enough to the current color keep the current color.
			//This is to prevent a lot of color jittering that can happen otherwise.
			var r = Math.Floor(c.R * _config.Model.hueSettings.colorMultiplier);
			var g = Math.Floor(c.G * _config.Model.hueSettings.colorMultiplier);
			var b = Math.Floor(c.B * _config.Model.hueSettings.colorMultiplier);
			r = Math.Clamp(r, 0, 255);
			g = Math.Clamp(g, 0, 255);
			b = Math.Clamp(b, 0, 255);
			if (_lastColor.R >= c.R - _colorChangeThreshold && _lastColor.R <= c.R + _colorChangeThreshold)
				r = _lastColor.R;
			if (_lastColor.G >= c.G - _colorChangeThreshold && _lastColor.G <= c.G + _colorChangeThreshold)
				g = _lastColor.G;
			if (_lastColor.B >= c.B - _colorChangeThreshold && _lastColor.B <= c.B + _colorChangeThreshold)
				b = _lastColor.B;
			c = Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
			if (c == _lastColor)
				return;
			_lastColor = c;

			var command = new LightCommand
			{
				TransitionTime = _frameTimeSpan
			};
			command.SetColor(new RGBColor(Helpers.ColorToHex(c)));
			_sendingCommand = true;
			try
			{
				await _client.SendCommandAsync(command, UseRoom.Lights);
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex.ToString()));
			}
			_lastHueChangeTime = DateTime.UtcNow;
			_sendingCommand = false;
		}

		public void UpdateEntertainmentGroupFromImage(MagickImage image)
		{
			try
			{
				if (_sendingCommand || _cancelToken.IsCancellationRequested)
					return;
				var dt = DateTime.UtcNow - _lastHueChangeTime;
				if (dt.TotalMilliseconds < _frameTimeSpan.TotalMilliseconds)
					return;

				var start = DateTime.UtcNow;
				using var pixels = image.GetPixelsUnsafe();
				foreach (var light in _streamBaseLayer)
				{
					var (x, y) = MapLightLocationToImage(light.LightLocation, image.Width, image.Height);
					var color = pixels[x, y].ToColor();
					var r = Math.Floor(color.R * _config.Model.hueSettings.colorMultiplier);
					var g = Math.Floor(color.G * _config.Model.hueSettings.colorMultiplier);
					var b = Math.Floor(color.B * _config.Model.hueSettings.colorMultiplier);
					var c = Color.FromArgb(255, (byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
					light.SetState(_cancelToken, new RGBColor(Helpers.ColorToHex(c)), 1.0);
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

		private (int x, int y) MapLightLocationToImage(LightLocation location, int width, int height)
		{
			var x = (int)Math.Floor((location.X - -1.0) / 2 * (width));
			var y = (int)Math.Floor((location.Y - -1.0) / 2 * (height));
			return (x, y);
		}
	}
}
