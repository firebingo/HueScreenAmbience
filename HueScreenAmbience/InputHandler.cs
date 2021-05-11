using HueScreenAmbience.Hue;
using Q42.HueApi.Interfaces;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HueScreenAmbience
{
	class InputHandler
	{
		private bool _isRunning = false;
		private readonly bool _isHeadless = false;
		private bool _isReady = false;
		private Core _core;
		private Config _config;
		private HueCore _hueClient;
		private ScreenReader _screen;
		private bool _headlessHasWrittenInfo;
		private StreamReader _stdinsr;

		public InputHandler(bool isHeadless)
		{
			_isHeadless = isHeadless;
		}

		public void InstallServices(IServiceProvider map)
		{
			_core = map.GetService(typeof(Core)) as Core;
			_config = map.GetService(typeof(Config)) as Config;
			_hueClient = map.GetService(typeof(HueCore)) as HueCore;
			_screen = map.GetService(typeof(ScreenReader)) as ScreenReader;
		}

		/// <summary>
		/// Handles input to the console.
		/// </summary>
		public async void HandleInput()
		{
			_stdinsr = new StreamReader(Console.OpenStandardInput());
			_isRunning = true;
			string input;

			ResetConsole();
			//while the input thread is set to run
			do
			{
				if (_screen.Ready && !_isReady)
				{
					_isReady = true;
					ResetConsole();
				}
				else if (!_isReady)
				{
					ResetConsole();
					Thread.Sleep(500);
					continue;
				}

				//Read the input line and act based on it.
				input = await _stdinsr.ReadLineAsync();
				if (string.IsNullOrWhiteSpace(input))
					continue;
				switch (input.ToLower())
				{
					//help
					case "-h":
						Console.WriteLine("-c - Search for bridges to connect to.");
						Console.WriteLine("-quit - Exits the application.");
						Console.WriteLine("--------------------------------");
						Console.WriteLine("-r - Select room for effect.");
						Console.WriteLine("-t - Change hue type.");
						Console.WriteLine("-a - Select graphics adapter.");
						Console.WriteLine("-m - Select monitor.");
						Console.WriteLine("-s - Starts the lighting.");
						Console.WriteLine("-e - Stops the lighting.");
						break;
					//Connect to hue bridge
					case "-connect":
					case "-c":
						if (CheckRunning())
							break;
						Console.WriteLine("Searching");
						await _core.ConnectToBridge();
						ResetConsole();
						break;
					case "-start":
					case "-s":
						_ = _core.StartScreenReading();
						ResetConsole();
						break;
					case "-stop":
					case "-end":
					case "-e":
						_ = _core.StopScreenReading();
						ResetConsole();
						break;
					//Quit the application.
					case "-quit":
					case "-q":
						Console.WriteLine("Closing");
						_ = _core.StopScreenReading();
						Thread.Sleep(2000);
						Environment.Exit(0);
						_isRunning = false;
						break;
					//Select a room
					case "-room":
					case "-r":
						if (CheckRunning())
							break;
						if (_config.Model.hueSettings.hueType == HueType.Basic)
							await _core.SelectRoom();
						else if (_config.Model.hueSettings.hueType == HueType.Entertainment)
							await _core.SelectEntertainmentGroup();
						ResetConsole();
						break;
					//Select hue type
					case "-hue":
					case "-t":
						if (CheckRunning())
							break;
						await _core.SelectHue();
						ResetConsole();
						break;
					//Change monitor
					case "-display":
					case "-monitor":
					case "-m":
						if (CheckRunning())
							break;
						_core.SelectMonitor();
						ResetConsole();
						break;
					case "-adapter":
					case "-a":
						if (CheckRunning())
							break;
						var oldAda = _config.Model.adapterId;
						_core.SelectAdapter();
						if (oldAda != _config.Model.adapterId)
							_core.SelectMonitor();
						ResetConsole();
						break;
					default:
						Console.WriteLine("Invalid Input Given");
						break;
				}
			}
			while (_isRunning);
		}

		private bool CheckRunning()
		{
			if (_screen.IsRunning)
			{
				Console.WriteLine("Stop running before changing settings");
				Console.ReadLine();
				ResetConsole();
				return true;
			}
			return false;
		}

		public void ResetConsole()
		{
			if (!_isHeadless)
			{
				Console.Clear();
				Console.WriteLine("HUE Screen Based Lighting");
				if (!_screen.Ready)
					Console.WriteLine("Loading...");
				else
				{
					if (_hueClient.IsConnectedToBridge)
						Console.WriteLine("Connected to Bridge");
					if (_hueClient.UseRoom != null)
						Console.WriteLine($"Using room: {_hueClient.UseRoom.Name}");
					if (_screen.Screen != null)
						Console.WriteLine($"Using monitor: {_screen.Screen.OutputId}: {_screen.Screen.Name} {_screen.Screen.Width}x{_screen.Screen.Height}x{_screen.Screen.RefreshRate:0.##}");
					if (_config.Model.piCameraSettings.isPi)
						Console.WriteLine($"Using pi camera input: {_config.Model.piCameraSettings.width}x{_config.Model.piCameraSettings.height}x{_config.Model.piCameraSettings.frameRate}");
					Console.WriteLine("Use -h for help");
					if (_screen.IsRunning)
						Console.WriteLine($"Lighting is running");
				}
			}
			else if (!_headlessHasWrittenInfo)
			{
				if (_hueClient.IsConnectedToBridge)
					Console.WriteLine("Connected to Bridge");
				if (_hueClient.UseRoom != null)
					Console.WriteLine($"Using room: {_hueClient.UseRoom.Name}");
				if (_screen.Screen != null)
					Console.WriteLine($"Using monitor: {_screen.Screen.OutputId}: {_screen.Screen.Name} {_screen.Screen.Width}x{_screen.Screen.Height}x{_screen.Screen.RefreshRate:0.##}");
				if (_config.Model.piCameraSettings.isPi)
					Console.WriteLine($"Using pi camera input: {_config.Model.piCameraSettings.width}x{_config.Model.piCameraSettings.height}x{_config.Model.piCameraSettings.frameRate}");
				if (_screen.IsRunning)
					Console.WriteLine($"Lighting is running");
				_headlessHasWrittenInfo = true;
			}
		}

		public void StopInput()
		{
			_isRunning = false;
			_stdinsr?.Dispose();
		}
	}
}
