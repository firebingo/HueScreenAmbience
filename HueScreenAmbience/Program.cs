using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace HueScreenAmbience
{
	class HueScreenAmbience
	{
		private InputHandler _input = null;
		private Core _core = null;
		private Config _config = null;
		private ScreenReader _screen = null;
		private IServiceProvider _map = null;

		static void Main(string[] args)
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
				inputThread.Start();
				
				_core.InstallServices(_map);
				Thread coreThread = new Thread(new ThreadStart(_core.Start));
				coreThread.Name = "Core Thread";
				coreThread.Start();

				_screen.InstallServices(_map);
				Thread screenThread = new Thread(new ThreadStart(_screen.Start));
				screenThread.Name = "Screen Reader Thread";
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
		
			var services = new ServiceCollection()
				.AddSingleton(_config)
				.AddSingleton(_input)
				.AddSingleton(_core)
				.AddSingleton(_screen);
			var provider = new DefaultServiceProviderFactory().CreateServiceProvider(services);
			return provider;
		}
	}
}