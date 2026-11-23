using RGB.NET.Core;

namespace HueScreenAmbience.RGB
{
	internal class RGBConsts
	{
		static RGBConsts()
		{
			White = new Color(1.0f, 1.0f, 1.0f, 1.0f);
			Black = new Color(1.0f, 0.0f, 0.0f, 0.0f);
			Transparent = new Color(0.0f, 0.0f, 0.0f, 0.0f);
		}

		public static Color White { get; set; }
		public static Color Black { get; set; }
		public static Color Transparent { get; set; }
	}
}