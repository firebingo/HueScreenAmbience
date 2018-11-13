using System.Drawing;

namespace HueScreenAmbience
{
    public static class Helpers
    {
		public static string ColorToHex(Color c)
		{
			return $"{c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2")}";
		}
	}
}