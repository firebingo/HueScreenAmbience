using System;
using System.Collections.Generic;
using System.Text;

namespace ImageMagickProcessor
{
	public enum FilterType
	{
		Undefined = 0,
		Point = 1,
		Box = 2,
		Triangle = 3,
		Hermite = 4,
		Hann = 5,
		Hamming = 6,
		Blackman = 7,
		Gaussian = 8,
		Quadratic = 9,
		Cubic = 10,
		Catrom = 11,
		Mitchell = 12,
		Jinc = 13,
		Sinc = 14,
		SincFast = 15,
		Kaiser = 16,
		Welch = 17,
		Parzen = 18,
		Bohman = 19,
		Bartlett = 20,
		Lagrange = 21,
		Lanczos = 22,
		LanczosSharp = 23,
		Lanczos2 = 24,
		Lanczos2Sharp = 25,
		Robidoux = 26,
		RobidouxSharp = 27,
		Cosine = 28,
		Spline = 29,
		LanczosRadius = 30,
		CubicSpline = 31
	}
}
