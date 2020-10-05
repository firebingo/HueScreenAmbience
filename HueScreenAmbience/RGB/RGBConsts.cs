using RGB.NET.Core;

namespace HueScreenAmbience.RGB
{
	class RGBConsts
	{
		public static Color White { get; set; }
		public static Color Black { get; set; }
		public static Color Transparent { get; set; }

		static RGBConsts()
		{
			White = new Color(1.0, 1.0, 1.0, 1.0);
			Black = new Color(1.0, 0.0, 0.0, 0.0);
			Transparent = new Color(0.0, 0.0, 0.0, 0.0);
		}
	}
}
