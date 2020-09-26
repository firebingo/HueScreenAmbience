using System;
using System.Drawing;

namespace HueScreenAmbience
{
	public class PixelZone
	{
		public readonly Point TopLeft;
		public readonly Point BottomRight;
		public readonly int Row;
		public readonly int Column;
		public int Count;
		public readonly long[] Totals;
		public byte AvgR
		{
			get
			{
				if(Count > 0)
					return (byte)(Totals[0] / Count);
				return 0;
			}
		}
		public byte AvgG
		{
			get
			{
				if (Count > 0)
					return (byte)(Totals[1] / Count);
				return 0;
			}
		}
		public byte AvgB
		{
			get
			{
				if (Count > 0)
					return (byte)(Totals[2] / Count);
				return 0;
			}
		}

		public PixelZone(Point topLeft, Point bottomRight, int row, int column, int count, long[] totals)
		{
			TopLeft = topLeft;
			BottomRight = bottomRight;
			Row = row;
			Column = column;
			Count = count;
			Totals = totals;
		}

		public PixelZone(int row, int column, int xMin, int xMax, int yMin, int yMax)
		{
			Row = row;
			Column = column;
			TopLeft = new Point(xMin, yMin);
			BottomRight = new Point(xMax, yMax);
			Totals = new long[] { 0, 0, 0 };
			Count = 0;
		}

		public bool IsPointInZone(Point p)
		{
			if(p.X >= TopLeft.X && p.X < BottomRight.X && p.Y >= TopLeft.Y && p.Y < BottomRight.Y)
				return true;
			return false;
		}

		public static PixelZone Clone(PixelZone source)
		{
			var topLeft = new Point(source.TopLeft.X, source.TopLeft.Y);
			var bottomRight = new Point(source.BottomRight.X, source.BottomRight.Y);
			var totals = new long[] { source.Totals[0], source.Totals[1], source.Totals[2] };
			return new PixelZone(topLeft, bottomRight, source.Row, source.Column, source.Count, totals);
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
			if(obj is ReadPixel p)
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
