using System;
using System.IO;
using System.Threading.Tasks;

namespace LightStripClient
{
	public class FileLogger
	{
		public async Task WriteLog(string message)
		{
			await File.WriteAllTextAsync("Data/log.txt", $"[{DateTime.Now:yyMMdd HH:mm:ss}] {message}");
		}
	}
}
