using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using HueScreenAmbience.Hue;
using HueScreenAmbience.DXGICaptureScreen;

namespace HueScreenAmbience
{
	public class Core
	{
		private bool _isScreenStarted = false;

		private Config _config;
		private InputHandler _input;
		private ScreenReader _screen;
		private HueCore _hueClient;

		public async Task Start(bool waitHueStart)
		{
			if (waitHueStart)
				await _hueClient.Start();
			else
				_ = _hueClient.Start();
		}

		public void InstallServices(IServiceProvider map)
		{
			_config = map.GetService(typeof(Config)) as Config;
			_input = map.GetService(typeof(InputHandler)) as InputHandler;
			_screen = map.GetService(typeof(ScreenReader)) as ScreenReader;
			_hueClient = map.GetService(typeof(HueCore)) as HueCore;
		}

		public async Task<bool> ConnectToBridge()
		{
			try
			{
				var bridgeIPs = await HueCore.GetBridges();
				if (!bridgeIPs?.Any() ?? false)
					return false;

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
					if (int.TryParse(read, out readInt))
					{
						if (readInt > bridges.Count || readInt < 1)
							continue;
						validInput = true;
						_hueClient.SetBridge(bridges[readInt - 1]);
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
								if (_config.Model.hueSettings.hueType == HueType.Basic)
									await _hueClient.RegisterBridge(deviceName);
								else
									await _hueClient.RegisterBridgeEntertainment(deviceName);
							}
							catch (Exception ex)
							{
								Console.WriteLine(ex.Message);
								Console.ReadLine();
							}
						}
						while (!nameValid);
					}
				}
				while (!validInput);

				return true;
			}
			catch
			{
				return true;
			}
		}

		public async Task<bool> SelectHue()
		{
			var oldSetting = _config.Model.hueSettings.hueType;
			bool validInput = false;
			do
			{
				Console.Clear();
				Console.WriteLine($"Options:");
				Console.WriteLine($"0 - Basic");
				Console.WriteLine($"1 - Entertainment");
				var read = Console.ReadLine();
				if (int.TryParse(read, out var readInt))
				{
					if (readInt != 0 && readInt != 1)
						continue;
					validInput = true;
					_config.Model.hueSettings.hueType = (HueType)readInt;
					_config.SaveConfig();
				}
			} while (!validInput);

			if (_hueClient.IsConnectedToBridge && oldSetting != _config.Model.hueSettings.hueType)
				await ConnectToBridge();

			return true;
		}

		public async Task<bool> SelectRoom()
		{
			if (!_hueClient.IsConnectedToBridge)
				return false;

			var groups = await _hueClient.GetGroups();

			if (!groups?.Any() ?? true)
			{
				Console.WriteLine("No rooms defined on bridge. Please use another HUE app to define a room.");
				Console.ReadLine();
				return false;
			}

			bool validInput = false;
			do
			{
				Console.Clear();
				Console.WriteLine($"Found Rooms:");
				var i = 1;
				foreach (var r in groups)
				{
					Console.WriteLine($"{i++}: Name: {r.Name}");
				}
				Console.WriteLine("Input a room(#) to connect to: ");
				var read = Console.ReadLine();
				if (Int32.TryParse(read, out var readInt))
				{
					if (readInt > groups.Count() || readInt < 1)
						continue;
					validInput = true;
					_hueClient.SetRoom(groups.ElementAt(readInt - 1));
				}
			} while (!validInput);

			return true;
		}

		public async Task<bool> SelectEntertainmentGroup()
		{
			if (!_hueClient.IsConnectedToBridge)
				return false;

			var groups = await _hueClient.GetEntertainmentGroups();

			if (!groups?.Any() ?? true)
			{
				Console.WriteLine("No entertainment groups defined on bridge. Please use another HUE app to define a group.");
				Console.ReadLine();
				return false;
			}

			bool validInput = false;
			do
			{
				Console.Clear();
				Console.WriteLine($"Found Entertainment Groups:");
				var i = 1;
				foreach (var r in groups)
				{
					Console.WriteLine($"{i++}: Name: {r.Name}");
				}
				Console.WriteLine("Input a group(#) to connect to: ");
				var read = Console.ReadLine();
				if (int.TryParse(read, out var readInt))
				{
					if (readInt > groups.Count() || readInt < 1)
						continue;
					validInput = true;
					_hueClient.SetRoom(groups.ElementAt(readInt - 1));
				}
			} while (!validInput);
			return true;
		}

		public void SelectAdapter()
		{
			var validInput = false;
			var adapters = DxEnumerate.GetAdapters();
			if (!adapters?.Any() ?? true)
			{
				Console.WriteLine("No adapters found.");
				Console.ReadLine();
				return;
			}

			do
			{
				Console.Clear();
				Console.WriteLine($"Found adapters:");
				foreach (var adapter in adapters)
				{
					Console.WriteLine($"{adapter.AdapterId}: Name: {adapter.Name}");
				}
				Console.WriteLine("Input a adapter(#) to connect to: ");
				var read = Console.ReadLine();
				if (int.TryParse(read, out var readInt))
				{
					if (readInt > adapters.Count || readInt < 0)
						continue;

					var adapter = adapters.FirstOrDefault(x => x.AdapterId == readInt);
					if (adapter == null)
						continue;
					validInput = true;
					_config.Model.adapterId = adapter.AdapterId;
					_config.SaveConfig();
					_screen.Start();
				}
			}
			while (!validInput);
		}

		public void SelectMonitor()
		{
			var validInput = false;
			var displays = DxEnumerate.GetMonitors(_config.Model.adapterId);

			if (!displays?.Any() ?? true)
			{
				Console.WriteLine("No monitors found.");
				Console.ReadLine();
				return;
			}

			do
			{
				Console.Clear();
				Console.WriteLine($"Found monitors:");
				foreach (var display in displays)
				{
					Console.WriteLine($"{display.OutputId}: Name: {display.Name}, Info: {display.Width}x{display.Height}x{display.RefreshRate:0.##} Format: {display.Format}");
				}
				Console.WriteLine("Input a display(#) to connect to: ");
				var read = Console.ReadLine();
				if (int.TryParse(read, out var readInt))
				{
					if (readInt > displays.Count || readInt < 0)
						continue;

					var display = displays.FirstOrDefault(x => x.OutputId == readInt);
					if (display == null)
						continue;
					validInput = true;
					_config.Model.monitorId = display.OutputId;
					_config.SaveConfig();
					_screen.Start();
				}
			}
			while (!validInput);
		}

		public async Task StartScreenReading()
		{
			if (_isScreenStarted)
				return;

			if (_config.Model.hueSettings.useHue)
			{
				if (!_hueClient.IsConnectedToBridge || _hueClient.UseRoom == null)
				{
					Console.Clear();
					Console.WriteLine("Either not connected to a bridge or room has not been selected");
					Console.ReadLine();
					_input.ResetConsole();
					return;
				}
			}

			//Zones use int for total as it is faster and reasonable since we cut the screen resolution by a lot anyways.
			// Check that a full white screen will not go over the int max at our resolution.
			long maxValue = (long)_screen.ScreenInfo.Width * _screen.ScreenInfo.Height * byte.MaxValue;
			if (maxValue >= int.MaxValue)
				throw new Exception("Process screen size is too large, increase readResolutionReduce value");

#if ANYCPU
#else
			//This should prevent windows from going to sleep as entering idle or sleep state seems to break several things.
			var res = WindowsApi.SetThreadExecutionState(WindowsApi.EXECUTION_STATE.ES_CONTINUOUS | WindowsApi.EXECUTION_STATE.ES_DISPLAY_REQUIRED);
#endif

			_screen.InitScreenLoop();
			if (_config.Model.hueSettings.useHue)
				await _hueClient.OnStartReading();

			_isScreenStarted = true;
			_ = Task.Run(() => _screen.ReadScreenLoopDx());
			_input.ResetConsole();
		}

		public async Task StopScreenReading()
		{
#if ANYCPU
#else
			//Allow windows to sleep again
			var res = WindowsApi.SetThreadExecutionState(WindowsApi.EXECUTION_STATE.ES_CONTINUOUS);
#endif
			_screen.StopScreenLoop();

			await _hueClient.OnStopReading();
			_isScreenStarted = false;
			_input.ResetConsole();
		}
	}
}
