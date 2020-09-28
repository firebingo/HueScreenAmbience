using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HueScreenAmbience
{
	public class FileLogger
	{
		public async Task WriteLog(string message)
		{
			await File.WriteAllTextAsync("Data/log.txt", $"[{DateTime.Now:yyMMdd HH:mm:ss}] {message}");
		}
	}
}
