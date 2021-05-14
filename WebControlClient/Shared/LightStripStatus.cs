using System.Collections.Generic;

namespace WebControlClient.Shared
{
	public class LightStripStatus
	{
		public long? Frame { get; set; }
		public string BoundIp { get; set; }
		public string BoundPort { get; set; }
		public List<NetworkAddress> NetworkInterfaces { get; set; }

		public void CopyFrom(LightStripStatus from)
		{
			if (from.Frame.HasValue)
				Frame = from.Frame.Value;
			if (from.BoundIp != null)
				BoundIp = from.BoundIp;
			if (from.BoundPort != null)
				BoundPort = from.BoundPort;
			if (from.NetworkInterfaces != null)
				NetworkInterfaces = from.NetworkInterfaces;
		}
	}
}
