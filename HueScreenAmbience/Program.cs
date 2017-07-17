using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace HueScreenAmbience
{
	class HueScreenAmbience
	{
		InputHandler input = null;
		Core core = null;
		Config config = null;
		ScreenReader screen = null;
		IServiceProvider map = null;

		static void Main(string[] args)
		{
			new HueScreenAmbience().run().GetAwaiter().GetResult();
		}

		public async Task run()
		{
			try
			{
				map = ConfigureServices();
				
				config.loadConfig();

				input.installServices(map);
				Thread inputThread = new Thread(new ThreadStart(input.handleInput));
				inputThread.Name = "Input Thread";
				inputThread.Start();
				
				core.installServices(map);
				Thread coreThread = new Thread(new ThreadStart(core.start));
				coreThread.Name = "Core Thread";
				coreThread.Start();

				screen.installServices(map);
				Thread screenThread = new Thread(new ThreadStart(screen.start));
				screenThread.Name = "Screen Reader Thread";
				screenThread.Start();

				//Delay until application quit
				await Task.Delay(-1);
			}
			catch (Exception e)
			{
				return;
			}
		}

		private IServiceProvider ConfigureServices()
		{
			//setup and add command service.
			config = new Config();
			input = new InputHandler();
			core = new Core();
			screen = new ScreenReader();
		
			var services = new ServiceCollection()
				.AddSingleton(config)
				.AddSingleton(input)
				.AddSingleton(core)
				.AddSingleton(screen);
			var provider = new DefaultServiceProviderFactory().CreateServiceProvider(services);
			return provider;
		}
	}
}