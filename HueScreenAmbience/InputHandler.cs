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

		public void installServices(IServiceProvider _map)
		{
			core = _map.GetService(typeof(Core)) as Core;
			config = _map.GetService(typeof(Config)) as Config;
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
						Console.WriteLine("-s - Asks for ip then starts game listener.");
						Console.WriteLine("-c - Search for bridges to connect to.");
						Console.WriteLine("-quit - Exits the application.");
						Console.WriteLine("--------------------------------");
						Console.WriteLine("-r - Select room for effect");
						break;
					//Connect to hue bridge
					case "-connect":
					case "-c":
						Console.WriteLine("Searching");
						await core.connectToBridge();
						break;
					//quit the application.
					case "-quit":
					case "-q":
						Console.WriteLine("Closing");
						Thread.Sleep(2000);
						Environment.Exit(0);
						isRunning = false;
						break;
					case "-room":
					case "-r":
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
			Console.WriteLine("Use -h for help");
		}
	}
}
