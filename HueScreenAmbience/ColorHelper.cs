﻿using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;

namespace HueScreenAmbience
{
	public static class ColorHelper
	{
		public static readonly ColorSpaceConverter _colorConverter;

		static ColorHelper()
		{
			_colorConverter = new ColorSpaceConverter();
		}

		public static Color SaturateColor(Color color, float saturate)
		{
			var hsv = _colorConverter.ToHsv(color.ToPixel<Rgb24>());
			var newhsv = new Hsv(hsv.H, Math.Min(1.0f, hsv.S * saturate), hsv.V);
			var newrgb = _colorConverter.ToRgb(newhsv);
			var r = (byte)Math.Clamp(newrgb.R * 255, 0, 255);
			var g = (byte)Math.Clamp(newrgb.G * 255, 0, 255);
			var b = (byte)Math.Clamp(newrgb.B * 255, 0, 255);
			return Color.FromRgb(r, g, b);
		}

		public static string ColorToHex(Color c)
		{
			return c.ToHex()[0..6];
		}
	}
}
