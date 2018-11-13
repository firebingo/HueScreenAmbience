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

		public void InstallServices(IServiceProvider _map)
		{
			core = _map.GetService(typeof(Core)) as Core;
			config = _map.GetService(typeof(Config)) as Config;
			screen = _map.GetService(typeof(ScreenReader)) as ScreenReader;
			map = _map;
		}

		/// <summary>
		/// Handles input to the console.
		/// </summary>
		public async void HandleInput()
		{
			isRunning = true;
			string input = "";

			ResetConsole();
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
						await core.ConnectToBridge();
						ResetConsole();
						break;
					case "-start":
					case "-s":
						core.StartScreenReading();
						ResetConsole();
						break;
					case "-stop":
					case "-end":
					case "-e":
						core.StopScreenReading();
						ResetConsole();
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
						await core.SelectRoom();
						ResetConsole();
						break;
					//Change pixel count
					case "-pixels":
					case "-p":
						core.ChangePixelCount();
						ResetConsole();
						break;
					default:
						Console.WriteLine("Invalid Input Given");
						break;
				}
			}
			while (isRunning);
		}

		public void ResetConsole()
		{
			Console.Clear();
			Console.WriteLine("HUE Screen Based Lighting");
			if (core.IsConnectedToBridge)
				Console.WriteLine("Connected to Bridge");
			if (core.UseRoom != null)
				Console.WriteLine($"Using room: {core.UseRoom.Name}");
			Console.WriteLine("Use -h for help");
			if (screen.IsRunning)
				Console.WriteLine($"Lighting is running");
		}
	}
}
