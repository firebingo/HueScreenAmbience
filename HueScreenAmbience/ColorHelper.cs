using System;
using System.Drawing;

namespace HueScreenAmbience
{
	public struct HSVColor
	{
		public float H { get; }
		public float S { get; }
		public float V { get; }

		public HSVColor(float h, float s, float v)
		{
			H = h;
			S = s;
			V = v;
		}

		//https://stackoverflow.com/questions/359612/how-to-change-rgb-color-to-hsv
		public static HSVColor ColorToHSV(Color color)
		{
			int max = Math.Max(color.R, Math.Max(color.G, color.B));
			int min = Math.Min(color.R, Math.Min(color.G, color.B));

			float hue = color.GetHue();
			float saturation = (max == 0) ? 0 : 1.0f - (1.0f * min / max);
			float value = max / 255.0f;
			return new HSVColor(hue, saturation, value);
		}

		public static Color ColorFromHSV(float hue, float saturation, float value)
		{
			int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
			float f = hue / 60 - (float)Math.Floor(hue / 60);

			value *= 255;
			int v = Convert.ToInt32(value);
			int p = Convert.ToInt32(value * (1 - saturation));
			int q = Convert.ToInt32(value * (1 - f * saturation));
			int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

			return hi switch
			{
				0 => Color.FromArgb(255, v, t, p),
				1 => Color.FromArgb(255, q, v, p),
				2 => Color.FromArgb(255, p, v, t),
				3 => Color.FromArgb(255, p, q, v),
				4 => Color.FromArgb(255, t, p, v),
				_ => Color.FromArgb(255, v, p, q)
			};
		}
	}

	public static class ColorHelper
	{
		public static Color SaturateColor(Color color, float saturate)
		{
			var hsv = HSVColor.ColorToHSV(color);
			var newSat = Math.Min(1.0f, hsv.S * saturate);
			return HSVColor.ColorFromHSV(hsv.H, newSat, hsv.V);
		}

		public static string ColorToHex(Color c)
		{
			return $"{c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2")}";
		}
	}
}
