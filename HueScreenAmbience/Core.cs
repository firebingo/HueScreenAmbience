using Q42.HueApi;
using Q42.HueApi.Interfaces;
using Q42.HueApi.Models.Bridge;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StringExtensions;

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
		private const string appName = "HUEScreenAmbience";

		public bool isRunning { get; private set; } = false;
		public bool isConnectedToBridge { get; private set; } = false;
		private IServiceProvider map;
		private Config config;
		private InputHandler input;

		public void start()
		{
			isRunning = true;
			autoConnectAttempt();
		}

		public void installServices(IServiceProvider _map)
		{
			config = _map.GetService(typeof(Config)) as Config;
			input = _map.GetService(typeof(InputHandler)) as InputHandler;
			map = _map;
		}

		private async Task<bool> autoConnectAttempt()
		{
			if(config.config.appKey.isNullOrEmpty() || config.config.ip.isNullOrEmpty())
				return Task.FromResult<bool>(false).Result;

			Client = new LocalHueClient(config.config.ip);
			Client.Initialize(config.config.appKey);
			isConnectedToBridge = true;

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
							catch(Exception e)
							{
								Console.WriteLine(e.Message);
								Console.ReadLine();
							}
						}
						while(!nameValid);
					}
				}
				while (!validInput);

				input.resetConsole();

				return Task.FromResult<bool>(true).Result;
			}
			catch (Exception e)
			{
				return Task.FromResult<bool>(true).Result;
			}
		}
	}
}
