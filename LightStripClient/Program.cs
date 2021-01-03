using System;

namespace LightStripClient
{
	class Program
	{
		private static LightStripLighter? _lighter;

		static void Main()
		{
			Config config = new Config();
			config.LoadConfig();
			FileLogger logger = new FileLogger();

			_lighter = new LightStripLighter(config, logger);
			_lighter.Start();

			AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
		}

		private static void CurrentDomain_ProcessExit(object? sender, EventArgs e)
		{
			_lighter?.Stop();
			_lighter?.Dispose();
		}
	}
}
