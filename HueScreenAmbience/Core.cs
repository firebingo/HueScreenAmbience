using Q42.HueApi;
using Q42.HueApi.Interfaces;
using Q42.HueApi.Models.Bridge;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Q42.HueApi.Models.Groups;
using System.Threading;
using System.Drawing;
using Q42.HueApi.ColorConverters.HSB;
using Q42.HueApi.ColorConverters;

namespace HueScreenAmbience
{
	public class Core
	{
		private ILocalHueClient _client = null;
		private LocatedBridge _useBridge = null;
		public Group UseRoom { get; private set; } = null;
		private const string _appName = "HUEScreenAmbience";

		public bool IsConnectedToBridge { get; private set; } = false;
		private IServiceProvider _map;
		private Config _config;
		private InputHandler _input;
		private ScreenReader _screen;
		private Thread _screenLoopThread;
		private bool _sendingCommand;

		public void Start()
		{
			Task.Run(() => AutoConnectAttempt());
		}

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_input = _map.GetService(typeof(InputHandler)) as InputHandler;
			_screen = _map.GetService(typeof(ScreenReader)) as ScreenReader;
			this._map = _map;
		}

		private async Task<bool> AutoConnectAttempt()
		{
			if (string.IsNullOrWhiteSpace(_config.Model.appKey) || string.IsNullOrWhiteSpace(_config.Model.ip))
				return Task.FromResult<bool>(false).Result;

			Console.WriteLine("Attempting auto-connect");
			_client = new LocalHueClient(_config.Model.ip);
			_client.Initialize(_config.Model.appKey);
			IsConnectedToBridge = true;

			if (!string.IsNullOrWhiteSpace(_config.Model.roomId))
			{
				var Groups = await _client.GetGroupsAsync();
				if (Groups != null && Groups.Count != 0)
					UseRoom = Groups.FirstOrDefault(x => x.Id == _config.Model.roomId);
			}

			_input.ResetConsole();
			return Task.FromResult<bool>(true).Result;
		}

		public async Task<bool> ConnectToBridge()
		{
			try
			{
				IBridgeLocator locator = new HttpBridgeLocator();
				IEnumerable<LocatedBridge> bridgeIPs = await locator.LocateBridgesAsync(TimeSpan.FromSeconds(5));
				if (bridgeIPs == null || bridgeIPs.Count() == 0)
					return Task.FromResult<bool>(false).Result;

				var bridges = bridgeIPs.ToList();

				bool validInput = false;
				do
				{
					Console.Clear();
					Console.WriteLine("Found Bridges:");
					var i = 1;
					foreach (var b in bridges)
					{
						Console.WriteLine($"{i++}: ip:{b.IpAddress} id:{b.BridgeId}");
					}


					Console.WriteLine("Input a bridge(#) to connect to: ");
					var read = Console.ReadLine();
					var readInt = -1;
					if (Int32.TryParse(read, out readInt))
					{
						if (readInt > bridges.Count || readInt < 1)
							continue;
						validInput = true;
						_useBridge = bridges[readInt - 1];
						bool nameValid = false;
						do
						{
							try
							{
								Console.Clear();
								Console.WriteLine("Input Device Name and press Link Button on HUE Bridge before hitting enter: ");
								var deviceName = Console.ReadLine().Trim();
								if (deviceName == string.Empty || deviceName.Length > 19 || deviceName.Contains(" "))
									continue;
								nameValid = true;
								_client = new LocalHueClient(_useBridge.IpAddress);
								var appKey = await _client.RegisterAsync(_appName, deviceName);
								_client.Initialize(appKey);
								IsConnectedToBridge = true;
								_config.Model.ip = _useBridge.IpAddress;
								_config.Model.appKey = appKey;
								_config.SaveConfig();
							}
							catch (Exception e)
							{
								Console.WriteLine(e.Message);
								Console.ReadLine();
							}
						}
						while (!nameValid);
					}
				}
				while (!validInput);

				return Task.FromResult<bool>(true).Result;
			}
			catch
			{
				return Task.FromResult<bool>(true).Result;
			}
		}

		public async Task<bool> SelectRoom()
		{
			if (!IsConnectedToBridge)
				return Task.FromResult<bool>(false).Result;

			var Groups = await _client.GetGroupsAsync();

			if (Groups?.Count == 0)
			{
				Console.WriteLine("No rooms defined on bridge. Please use another HUE app to define a room.");
				Console.ReadLine();
				return Task.FromResult<bool>(false).Result;
			}

			bool validInput = false;
			do
			{
				Console.Clear();
				Console.WriteLine($"Found Rooms:");
				var i = 1;
				foreach (var r in Groups)
				{
					Console.WriteLine($"{i++}: Name: {r.Name}");
				}
				Console.WriteLine("Input a room(#) to connect to: ");
				var read = Console.ReadLine();
				var readInt = -1;
				if (Int32.TryParse(read, out readInt))
				{
					if (readInt > Groups.Count || readInt < 1)
						continue;
					validInput = true;
					UseRoom = Groups.ElementAt(readInt-1);
					_config.Model.roomId = UseRoom.Id;
					_config.SaveConfig();
				}
			} while (!validInput);

			return Task.FromResult<bool>(true).Result;
		}

		public void ChangePixelCount()
		{
			var valid = false;
			var screenPixelCount = _screen.ScreenInfo.height * _screen.ScreenInfo.width;
			do
			{
				Console.Clear();
				Console.WriteLine($"Current Count: {_config.Model.pixelCount}");
				Console.WriteLine("Large pixel counts may take too long to calculate and will lag behind the screen. Recommended to leave at default.");
				Console.WriteLine("Entering 0 will read the whole screen.");
				Console.WriteLine($"Max: {Math.Min(screenPixelCount, 10000000)}");
				Console.WriteLine("Input new pixel count:");
				var read = Console.ReadLine();
				var readInt = -1;
				if (Int32.TryParse(read, out readInt))
				{
					if (readInt > screenPixelCount || readInt > 10000000)
						continue;
					valid = true;
					_config.Model.pixelCount = readInt;
					_config.SaveConfig();
					_screen.PreparePixelsToGet();
				}
			}
			while (!valid);
		}

		public void SelectMonitor()
		{
			var valid = false;
			do
			{

			}
			while (!valid);
		}

		public async void StartScreenReading()
		{
			if (!IsConnectedToBridge || UseRoom == null)
			{
				Console.Clear();
				Console.WriteLine("Either not connected to a bridge or room has not been selected");
				Console.ReadLine();
				_input.ResetConsole();
				return;
			}
			var command = new LightCommand
			{
				On = true,
				TransitionTime = new TimeSpan(0, 0, 0, 0, (int)_screen.AverageDt)
			};
			command.TurnOn();
			await _client.SendCommandAsync(command, UseRoom.Lights);
			_screenLoopThread = new Thread(new ThreadStart(_screen.ReadScreenLoop));
			_screenLoopThread.Name = "Screen Loop Thread";
			_screenLoopThread.Start();
			_input.ResetConsole();
		}
		
		public void StopScreenReading()
		{
			if (_screenLoopThread != null && _screenLoopThread.IsAlive)
			{
				_screen.StopScreenLoop();
				_screenLoopThread.Abort();
			}
			_input.ResetConsole();
		}

		public async Task ChangeLightColor(Color c)
		{
			if (_sendingCommand)
				return;
			var command = new LightCommand
			{
				TransitionTime = new TimeSpan(0, 0, 0, 0, (int)_screen.AverageDt)
			};
			command.SetColor(new RGBColor(Helpers.ColorToHex(c)));
			_sendingCommand = true;
			try
			{
				await _client.SendCommandAsync(command, UseRoom.Lights);
			}
			catch
			{

			}
			_sendingCommand = false;
		}
	}
}
