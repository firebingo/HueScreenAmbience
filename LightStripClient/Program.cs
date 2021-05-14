using LightsShared;
using LightStripClient.Sockets;
using System;
using System.Threading.Tasks;

namespace LightStripClient
{
	class Program
	{
		private static LightStripLighter? _lighter;
		private static SocketHandler? _socketHandler;

		static async Task Main()
		{
			Config config = new Config();
			config.LoadConfig();
			FileLogger logger = new FileLogger();

			_lighter = new LightStripLighter(config, logger);
			_lighter.Start();
			_socketHandler = new SocketHandler(config, logger, _lighter);
			await _socketHandler.Start();

			AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

			Console.CancelKeyPress += Console_CancelKeyPress;
		}

		private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
		{
			_lighter?.Stop();
			_lighter?.Dispose();
			_socketHandler?.Stop();
		}

		private static void CurrentDomain_ProcessExit(object? sender, EventArgs e)
		{
			_lighter?.Stop();
			_lighter?.Dispose();
			_socketHandler?.Stop();
		}
	}
}
