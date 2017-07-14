
namespace HueScreenAmbience
{
    class Helpers
    {
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