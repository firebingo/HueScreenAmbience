using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VideoFrameProcessor.ffmpeg
{
	public class FFProbeModel
	{
		[JsonPropertyName("streams")]
		public List<FFProbeStream> Streams { get; set; } = [];
	}

	public class FFProbeStream
	{
		[JsonPropertyName("width")]
		public int Width { get; set; }
		[JsonPropertyName("height")]
		public int Height { get; set; }
		[JsonPropertyName("r_frame_rate")]
		public string? RFrameRate { get; set; }
		private decimal? _frameRate;
		public decimal? FrameRate
		{
			get
			{
				if (!_frameRate.HasValue && RFrameRate != null)
				{
					var s = RFrameRate.Split("/");
					var d1 = decimal.Parse(s[0]);
					var d2 = decimal.Parse(s[1]);
					_frameRate = d1 / d2;
				}
				return _frameRate;
			}
		}
		[JsonPropertyName("time_base")]
		public string? TimeBase { get; set; }
		[JsonPropertyName("duration_ts")]
		public decimal DurationTs { get; set; }
		private decimal? _durationSeconds;
		public decimal? DurationSeconds
		{
			get
			{
				if (!_durationSeconds.HasValue && TimeBase != null)
				{
					var s = TimeBase.Split("/");
					var d1 = decimal.Parse(s[0]);
					var d2 = decimal.Parse(s[1]);
					_durationSeconds = (d1 / d2) * DurationTs;
				}
				return _durationSeconds;
			}
		}
	}
}
