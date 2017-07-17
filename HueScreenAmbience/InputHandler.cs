using System;
using System.Threading;

namespace HueScreenAmbience
{
	class InputHandler
	{
		public bool isRunning = false;
		private IServiceProvider map;
		private Core core;
		private Config config;
		private ScreenReader screen;

		public void installServices(IServiceProvider _map)
		{
			core = _map.GetService(typeof(Core)) as Core;
			config = _map.GetService(typeof(Config)) as Config;
			screen = _map.GetService(typeof(ScreenReader)) as ScreenReader;
			map = _map;
		}

		/// <summary>
		/// Handles input to the console.
		/// </summary>
		public async void handleInput()
		{
			isRunning = true;
			string input = "";

			resetConsole();
			//while the input thread is set to run
			do
			{
				//Read the input line and act based on it.
				input = Console.ReadLine();
				switch (input.ToLower())
				{
					//help
					case "-h":
						Console.WriteLine("-c - Search for bridges to connect to.");
						Console.WriteLine("-quit - Exits the application.");
						Console.WriteLine("--------------------------------");
						Console.WriteLine("-r - Select room for effect.");
						Console.WriteLine("-p - Change pixel sample count.");
						Console.WriteLine("-s - Starts the lighting.");
						Console.WriteLine("-e - Stops the lighting.");
						break;
					//Connect to hue bridge
					case "-connect":
					case "-c":
						Console.WriteLine("Searching");
						await core.connectToBridge();
						resetConsole();
						break;
					case "-start":
					case "-s":
						core.startScreenReading();
						resetConsole();
						break;
					case "-stop":
					case "-end":
					case "-e":
						core.stopScreenReading();
						resetConsole();
						break;
					//Quit the application.
					case "-quit":
					case "-q":
						Console.WriteLine("Closing");
						Thread.Sleep(2000);
						Environment.Exit(0);
						isRunning = false;
						break;
					//Select a room
					case "-room":
					case "-r":
						await core.selectRoom();
						resetConsole();
						break;
					//Change pixel count
					case "-pixels":
					case "-p":
						core.changePixelCount();
						resetConsole();
						break;
					default:
						Console.WriteLine("Invalid Input Given");
						break;
				}
			}
			while (isRunning);
		}

		public void resetConsole()
		{
			Console.Clear();
			Console.WriteLine("HUE Screen Based Lighting");
			if (core.isConnectedToBridge)
				Console.WriteLine("Connected to Bridge");
			if (core.useRoom != null)
				Console.WriteLine($"Using room: {core.useRoom.Name}");
			Console.WriteLine("Use -h for help");
			if (screen.isRunning)
				Console.WriteLine($"Lighting is running");
		}
	}
}
