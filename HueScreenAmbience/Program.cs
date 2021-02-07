using System;
using System.Threading;
using System.Threading.Tasks;
using HueScreenAmbience.Hue;
using HueScreenAmbience.LightStrip;
using HueScreenAmbience.RGB;
using Microsoft.Extensions.DependencyInjection;

namespace HueScreenAmbience
{
	class HueScreenAmbience
	{
		private InputHandler _input = null;
		private Core _core = null;
		private Config _config = null;
		private ScreenReader _screen = null;
		private ZoneProcessor _zoneProcesser = null;
		private HueCore _hueClient = null;
		private RGBLighter _rgbLighter = null;
		private StripLighter _stripLighter = null;
		private IServiceProvider _map = null;

		static void Main()
		{
			new HueScreenAmbience().Run().GetAwaiter().GetResult();
		}

		public async Task Run()
		{
			try
			{
				_config = new Config();
				_config.LoadConfig();

				_map = ConfigureServices();

				_input.InstallServices(_map);
				Thread inputThread = new Thread(new ThreadStart(_input.HandleInput));
				inputThread.Name = "Input Thread";

				_core.InstallServices(_map);
				Thread coreThread = new Thread(new ThreadStart(_core.Start));
				coreThread.Name = "Core Thread";

				_screen.InstallServices(_map);
				Thread screenThread = new Thread(new ThreadStart(_screen.Start));
				screenThread.Name = "Screen Reader Thread";
				_hueClient.InstallServices(_map);
				_zoneProcesser.InstallServices(_map);
				_rgbLighter.InstallServices(_map);
				_stripLighter.InstallServices(_map);

				inputThread.Start();
				coreThread.Start();
				screenThread.Start();

				AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

				Console.CancelKeyPress += Console_CancelKeyPress;

				//Delay until application quit
				await Task.Delay(-1);
			}
			catch
			{
				return;
			}
		}

		//Try to gracefully shut down a little so hue and light strips will be stopped immediatly instead
		// of just timing out on their sides.
		private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
		{
			Console.WriteLine("closing");
			var task = _core.StopScreenReading();
			do
			{
				Thread.Sleep(0);
			}
			while (!task.IsCompleted);
		}

		private void CurrentDomain_ProcessExit(object sender, EventArgs e)
		{
			Console.WriteLine("closing");
			var task = _core.StopScreenReading();
			do
			{
				Thread.Sleep(0);
			}
			while (!task.IsCompleted);
		}

		private IServiceProvider ConfigureServices()
		{
			//setup and add command service.
			_input = new InputHandler();
			_core = new Core();
			_screen = new ScreenReader();
			_zoneProcesser = new ZoneProcessor();
			_hueClient = new HueCore();
			_rgbLighter = new RGBLighter();
			_stripLighter = new StripLighter();

			var services = new ServiceCollection();
			if (_config.Model.piCameraSettings.isPi)
			{
				services
				.AddScoped<FileLogger>()
				.AddSingleton(_config)
				.AddSingleton(_input)
				.AddSingleton(_core)
				.AddSingleton(_hueClient)
				.AddSingleton(_screen)
				.AddSingleton(_zoneProcesser)
				.AddSingleton(_stripLighter);
			}
			else
			{
				services
				.AddScoped<FileLogger>()
				.AddSingleton(_config)
				.AddSingleton(_input)
				.AddSingleton(_core)
				.AddSingleton(_hueClient)
				.AddSingleton(_screen)
				.AddSingleton(_zoneProcesser)
				.AddSingleton(_rgbLighter)
				.AddSingleton(_stripLighter);
			}
			var provider = new DefaultServiceProviderFactory().CreateServiceProvider(services);
			return provider;
		}
	}
}