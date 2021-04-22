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

		static async Task Main(string[] args)
		{
			await new HueScreenAmbience().Run(args);
		}

		public async Task Run(string[] args)
		{
			//If we are on windows and trying to read screen we have to set dpi awareness for DuplicateOutput1 to work.
			// DuplicateOutput1 is required for duplicate output to be hdr aware.
#if ANYCPU
#else
			try
			{
				if (Environment.OSVersion.Version >= new Version(6, 3, 0)) // win 8.1 added support for per monitor dpi
				{
					if (Environment.OSVersion.Version >= new Version(10, 0, 15063)) // win 10 creators update added support for per monitor v2
					{
						WindowsApi.SetProcessDpiAwarenessContext((int)WindowsApi.DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
					}
					else
						WindowsApi.SetProcessDpiAwareness(WindowsApi.PROCESS_DPI_AWARENESS.Process_Per_Monitor_DPI_Aware);
				}
				else
				{
					Console.WriteLine($"{Environment.OSVersion.Version} is not supported. min req: {new Version(6, 3, 0)} (win 8.1)");
					return;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
#endif

			try
			{
				var isHeadless = false;
				foreach (var value in args)
				{
					if (!string.IsNullOrWhiteSpace(value) && value.Equals("--headless", StringComparison.OrdinalIgnoreCase))
					{
						isHeadless = true;
						break;
					}
				}

				_config = new Config();
				_config.LoadConfig();

				_map = ConfigureServices(isHeadless);

				_input.InstallServices(_map);
				Thread inputThread = null;
				if (!isHeadless)
				{
					inputThread = new Thread(new ThreadStart(_input.HandleInput));
					inputThread.Name = "Input Thread";
				}

				_core.InstallServices(_map);

				_screen.InstallServices(_map);
				_hueClient.InstallServices(_map);
				_zoneProcesser.InstallServices(_map);
				_rgbLighter.InstallServices(_map);
				_stripLighter.InstallServices(_map);

				inputThread?.Start();
				_core.Start();
				_screen.Start();

				AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

				Console.CancelKeyPress += Console_CancelKeyPress;

				if (isHeadless)
					await _core.StartScreenReading();

				//Delay until application quit
				await Task.Delay(-1);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
		}

		//Try to gracefully shut down a little so hue and light strips will be stopped immediatly instead
		// of just timing out on their sides.
		private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
		{
			OnProcessStop();
		}

		private void CurrentDomain_ProcessExit(object sender, EventArgs e)
		{
			OnProcessStop();
		}

		private void OnProcessStop()
		{
			Console.WriteLine("closing");
			_input.StopInput();
			var task = _core.StopScreenReading();
			do
			{
				Thread.Sleep(0);
			}
			while (!task.IsCompleted);
		}

		private IServiceProvider ConfigureServices(bool isHeadless)
		{
			//setup and add command service.
			_input = new InputHandler(isHeadless);
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