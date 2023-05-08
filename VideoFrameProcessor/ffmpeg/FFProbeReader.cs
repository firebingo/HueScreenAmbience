using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace VideoFrameProcessor.ffmpeg
{
	public static class FFProbeReader
	{
		public static async Task<FFProbeModel?> ReadFFprobeInfo(string filePath)
		{
			var p = new Process();
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.FileName = "ffprobe.exe";
			p.StartInfo.Arguments = $@"-select_streams v:0 -print_format json -show_entries stream -i {filePath}";
			p.Start();
			var stream = p.StandardOutput.BaseStream;
			p.WaitForExit();
			return await JsonSerializer.DeserializeAsync<FFProbeModel>(stream);
		}
	}
}
