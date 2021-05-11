namespace WebControlClient.Shared
{
	public class ScreenAmbienceStatus
	{
		public bool? IsStarted { get; set; }
		public long? Frame { get; set; }
		public double? AverageDeltaTime { get; set; }
		public bool? IsHueConnected { get; set; }
		public bool? UsingHue { get; set; }
		public bool? UsingRgb { get; set; }
		public bool? UsingLightStrip { get; set; }
		public ScreenInfo ScreenInfo { get; set; }

		public void CopyFrom(ScreenAmbienceStatus from)
		{
			if (from.IsStarted.HasValue)
				IsStarted = from.IsStarted.Value;
			if (from.Frame.HasValue)
				Frame = from.Frame.Value;
			if (from.AverageDeltaTime.HasValue)
				AverageDeltaTime = from.AverageDeltaTime.Value;
			if (from.IsHueConnected.HasValue)
				IsHueConnected = from.IsHueConnected.Value;
			if (from.UsingHue.HasValue)
				UsingHue = from.UsingHue.Value;
			if (from.UsingRgb.HasValue)
				UsingRgb = from.UsingRgb.Value;
			if (from.UsingLightStrip.HasValue)
				UsingLightStrip = from.UsingLightStrip.Value;
			if (from.ScreenInfo != null)
				ScreenInfo = from.ScreenInfo;
		}
	}

	public class ScreenInfo
	{
		public string Id { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
		public int RealWidth { get; set; }
		public int RealHeight { get; set; }
		public double Rate { get; set; }
	}
}
