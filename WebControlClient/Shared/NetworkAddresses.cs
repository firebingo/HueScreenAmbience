using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;

namespace WebControlClient.Shared
{
	[UnsupportedOSPlatform("browser")]
	public static class NetworkAddresses
	{
		private static List<NetworkAddress> _cache;

		public static List<NetworkAddress> GetNetworkAddresses()
		{
			if (_cache != null)
				return _cache;
			else
				_cache = new List<NetworkAddress>();

			foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
			{
				var add = new NetworkAddress();
				if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
				{
					add.Name = ni.Name;
					foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
					{
						add.Ip = ip.Address;
					}
				}
			}

			return _cache;
		}
	}

	public class NetworkAddress
	{
		public string Name { get; set; }
		public IPAddress Ip { get; set; }
	}
}
