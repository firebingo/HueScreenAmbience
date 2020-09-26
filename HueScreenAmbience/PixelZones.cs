using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Xml.Schema;

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
				return (byte)(Totals[0] / Count);
			}
		}
		public byte AvgG
		{
			get
			{
				return (byte)(Totals[1] / Count);
			}
		}
		public byte AvgB
		{
			get
			{
				return (byte)(Totals[2] / Count);
			}
		}

		public PixelZone(int row, int column, int xMin, int xMax, int yMin, int yMax)
		{
			Row = row;
			Column = column;
			TopLeft = new Point(xMin, yMin);
			BottomRight = new Point(xMax, yMax);
			Totals = new long[3] { 0, 0, 0 };
			Count = 0;
		}

		public bool IsPointInZone(Point p)
		{
			if(p.X >= TopLeft.X && p.X < BottomRight.X && p.Y >= TopLeft.Y && p.Y < BottomRight.Y)
				return true;
			return false;
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
