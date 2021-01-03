﻿using System;
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
				_map = ConfigureServices();
				
				_config.LoadConfig();

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

				//Delay until application quit
				await Task.Delay(-1);
			}
			catch
			{
				return;
			}
		}

		private IServiceProvider ConfigureServices()
		{
			//setup and add command service.
			_config = new Config();
			_input = new InputHandler();
			_core = new Core();
			_screen = new ScreenReader();
			_zoneProcesser = new ZoneProcessor();
			_hueClient = new HueCore();
			_rgbLighter = new RGBLighter();
			_stripLighter = new StripLighter();

			var services = new ServiceCollection()
				.AddScoped<FileLogger>()
				.AddSingleton(_config)
				.AddSingleton(_input)
				.AddSingleton(_core)
				.AddSingleton(_hueClient)
				.AddSingleton(_screen)
				.AddSingleton(_zoneProcesser)
				.AddSingleton(_rgbLighter)
				.AddSingleton(_stripLighter);
			var provider = new DefaultServiceProviderFactory().CreateServiceProvider(services);
			return provider;
		}
	}
}