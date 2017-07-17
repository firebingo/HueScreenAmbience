using Q42.HueApi;
using Q42.HueApi.Interfaces;
using Q42.HueApi.Models.Bridge;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StringExtensions;
using Q42.HueApi.Models.Groups;
using System.Threading;
using System.Drawing;
using Q42.HueApi.ColorConverters.HSB;
using Q42.HueApi.ColorConverters;

namespace HueScreenAmbience
{
	public class Core
	{
		public ILocalHueClient _client
		{
			get { return Client; }
			private set { Client = value; }
		}
		private ILocalHueClient Client = null;
		private LocatedBridge useBridge = null;
		public Group useRoom { get; private set; } = null;
		private const string appName = "HUEScreenAmbience";

		public bool isConnectedToBridge { get; private set; } = false;
		private IServiceProvider map;
		private Config config;
		private InputHandler input;
		private ScreenReader screen;
		private Thread screenLoopThread;
		private bool sendingCommand;

		public void start()
		{
			autoConnectAttempt();
		}

		public void installServices(IServiceProvider _map)
		{
			config = _map.GetService(typeof(Config)) as Config;
			input = _map.GetService(typeof(InputHandler)) as InputHandler;
			screen = _map.GetService(typeof(ScreenReader)) as ScreenReader;
			map = _map;
		}

		private async Task<bool> autoConnectAttempt()
		{
			if (config.config.appKey.isNullOrEmpty() || config.config.ip.isNullOrEmpty())
				return Task.FromResult<bool>(false).Result;

			Console.WriteLine("Attempting auto-connect");
			Client = new LocalHueClient(config.config.ip);
			Client.Initialize(config.config.appKey);
			isConnectedToBridge = true;

			if (!config.config.roomId.isNullOrEmpty())
			{
				var Groups = await Client.GetGroupsAsync();
				if (Groups != null && Groups.Count != 0)
					useRoom = Groups.FirstOrDefault(x => x.Id == config.config.roomId);
			}

			input.resetConsole();
			return Task.FromResult<bool>(true).Result;
		}

		public async Task<bool> connectToBridge()
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
						useBridge = bridges[readInt - 1];
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
								Client = new LocalHueClient(useBridge.IpAddress);
								var appKey = await Client.RegisterAsync(appName, deviceName);
								Client.Initialize(appKey);
								isConnectedToBridge = true;
								config.config.ip = useBridge.IpAddress;
								config.config.appKey = appKey;
								config.saveConfig();
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
			catch (Exception e)
			{
				return Task.FromResult<bool>(true).Result;
			}
		}

		public async Task<bool> selectRoom()
		{
			if (!isConnectedToBridge)
				return Task.FromResult<bool>(false).Result;

			var Groups = await Client.GetGroupsAsync();

			if (Groups.Count == null || Groups.Count == 0)
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
					useRoom = Groups.ElementAt(readInt-1);
					config.config.roomId = useRoom.Id;
					config.saveConfig();
				}
			} while (!validInput);

			return Task.FromResult<bool>(true).Result;
		}

		public void changePixelCount()
		{
			var valid = false;
			var screenPixelCount = screen.screenInfo.height * screen.screenInfo.width;
			do
			{
				Console.Clear();
				Console.WriteLine($"Current Count: {config.config.pixelCount}");
				Console.WriteLine("Large pixel counts will take too long to calculate and will lag behind the screen. Recommended to leave at default");
				Console.WriteLine($"Max: {Math.Min(screenPixelCount, 10000000)}");
				Console.WriteLine("Input new pixel count:");
				var read = Console.ReadLine();
				var readInt = -1;
				if (Int32.TryParse(read, out readInt))
				{
					if (readInt > screenPixelCount || readInt > 10000000)
						continue;
					valid = true;
					config.config.pixelCount = readInt;
					config.saveConfig();
					screen.preparePixelsToGet();
				}
			}
			while (!valid);
		}

		public async void startScreenReading()
		{
			if (!isConnectedToBridge || useRoom == null)
			{
				Console.Clear();
				Console.WriteLine("Either not connected to a bridge or room has not been selected");
				Console.ReadLine();
				input.resetConsole();
				return;
			}
			var command = new LightCommand();
			command.On = true;
			command.TransitionTime = new TimeSpan(0, 0, 0, 0, (int)screen.averageDt);
			command.TurnOn();
			await Client.SendCommandAsync(command, useRoom.Lights);
			screenLoopThread = new Thread(new ThreadStart(screen.readScreenLoop));
			screenLoopThread.Name = "Screen Loop Thread";
			screenLoopThread.Start();
			input.resetConsole();
		}
		
		public void stopScreenReading()
		{
			if (screenLoopThread != null && screenLoopThread.IsAlive)
			{
				screen.stopScreenLoop();
				screenLoopThread.Abort();
			}
			input.resetConsole();
		}

		public async Task changeLightColor(Color c)
		{
			if (sendingCommand)
				return;
			var command = new LightCommand();
			command.TransitionTime = new TimeSpan(0, 0, 0, 0, (int)screen.averageDt);
			command.SetColor(new RGBColor(Helpers.ColorToHex(c)));
			sendingCommand = true;
			try
			{
				await Client.SendCommandAsync(command, useRoom.Lights);
			}
			catch (Exception e)
			{

			}
			sendingCommand = false;
		}
	}
}
