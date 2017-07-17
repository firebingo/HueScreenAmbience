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

namespace StringExtensions
{
	public static class StringExtensionsClass
	{
		public static bool isNullOrEmpty(this string s1)
		{
			if (s1 == null || s1 == string.Empty)
				return true;
			return false;
		}
	}
}