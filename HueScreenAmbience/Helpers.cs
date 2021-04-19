namespace HueScreenAmbience
{
	public static class Helpers
	{
		public static int GetImageCoordinate(int stride, int x, int y, int bitDepth = 3)
		{
			return (y * stride) + x * bitDepth;
		}
	}
}