using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LightsShared
{
	public class FileLogger
	{
		public async Task WriteLog(string message, string location = "Data/log.txt")
		{
			using var file = File.Open(location, FileMode.Append, FileAccess.Write);
			var bytes = Encoding.UTF8.GetBytes($"[{DateTime.Now:yyMMdd HH:mm:ss}] {message}");
			await file.WriteAsync(bytes, 0, bytes.Length);
		}
	}
}
