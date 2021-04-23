using System;
using SixLabors.ImageSharp;

namespace BitmapZoneProcessor
{
	public class PixelZone
	{
		public readonly Point TopLeft;
		public readonly Point BottomRight;
		public readonly int Width;
		public readonly int Height;
		public readonly int Row;
		public readonly int Column;
		public int Count;
		public long TotalR;
		public long TotalG;
		public long TotalB;
		//public readonly long[] Totals;
		public byte AvgR { get; private set; }
		public byte AvgG { get; private set; }
		public byte AvgB { get; private set; }

		public PixelZone(Point topLeft, Point bottomRight, int row, int column, int count, long totalR, long totalG, long totalB)
		{
			TopLeft = topLeft;
			BottomRight = bottomRight;
			Width = BottomRight.X - TopLeft.X;
			Height = BottomRight.Y - TopLeft.Y;
			Row = row;
			Column = column;
			Count = count;
			TotalR = totalR;
			TotalG = totalG;
			TotalB = totalB;
		}

		public PixelZone(int row, int column, int xMin, int xMax, int yMin, int yMax)
		{
			Row = row;
			Column = column;
			TopLeft = new Point(xMin, yMin);
			BottomRight = new Point(xMax, yMax);
			Width = BottomRight.X - TopLeft.X;
			Height = BottomRight.Y - TopLeft.Y;
			Count = 0;
		}

		public void ResetAverages()
		{
			AvgR = 0;
			AvgG = 0;
			AvgB = 0;
		}

		public void CalculateAverages()
		{
			if (Count > 0)
			{
				AvgR = (byte)(TotalR / Count);
				AvgG = (byte)(TotalG / Count);
				AvgB = (byte)(TotalB / Count);
			}
		}

		public bool IsCoordInZone(int x, int y)
		{
			if (x >= TopLeft.X && x < BottomRight.X && y >= TopLeft.Y && y < BottomRight.Y)
				return true;
			return false;
		}

		public bool IsPointInZone(Point p)
		{
			if (p.X >= TopLeft.X && p.X < BottomRight.X && p.Y >= TopLeft.Y && p.Y < BottomRight.Y)
				return true;
			return false;
		}

		public static PixelZone Clone(PixelZone source)
		{
			var topLeft = new Point(source.TopLeft.X, source.TopLeft.Y);
			var bottomRight = new Point(source.BottomRight.X, source.BottomRight.Y);
			return new PixelZone(topLeft, bottomRight, source.Row, source.Column, source.Count, source.TotalR, source.TotalG, source.TotalB);
		}
	}

	public readonly struct ReadPixel : IEquatable<ReadPixel>
	{
		//Point.Empty is 0,0 which is actually a valid point
		public static readonly ReadPixel Empty = new ReadPixel(null, new Point(-1, -1));

		public readonly PixelZone Zone;
		public readonly Point Pixel;

		public bool Equals(ReadPixel other)
		{
			return other.Pixel.Equals(this.Pixel);
		}

		public override bool Equals(object obj)
		{
			if (obj is ReadPixel p)
				return p.Pixel.Equals(this.Pixel);
			return false;
		}

		public override int GetHashCode()
		{
			return this.Pixel.GetHashCode();
		}

		public static bool operator ==(ReadPixel left, ReadPixel right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(ReadPixel left, ReadPixel right)
		{
			return !left.Equals(right);
		}

		public static bool operator ==(ReadPixel left, Point right)
		{
			return left.Pixel.Equals(right);
		}

		public static bool operator !=(ReadPixel left, Point right)
		{
			return !left.Pixel.Equals(right);
		}

		public ReadPixel(PixelZone zone, Point pixel)
		{
			Zone = zone;
			Pixel = pixel;
		}
	}
}
