using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HueScreenAmbience
{
	public class FileLogger
	{
		public async Task WriteLog(string message)
		{
			using var file = File.Open("Data/log.txt", FileMode.Append, FileAccess.Write);
			var bytes = Encoding.UTF8.GetBytes($"[{DateTime.Now:yyMMdd HH:mm:ss}] {message}");
			await file.WriteAsync(bytes, 0, bytes.Length);
		}
	}
}
