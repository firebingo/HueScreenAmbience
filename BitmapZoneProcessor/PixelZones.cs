using System;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;

namespace BitmapZoneProcessor
{
	unsafe public class PixelZonesTotals : IDisposable
	{
		private bool disposed = false;

		public int* TotalR;
		public int* TotalG;
		public int* TotalB;
		public int* Count;
		public int* AvgR;
		public int* AvgG;
		public int* AvgB;
		private int _length;

		public PixelZonesTotals(int length)
		{
			_length = length;
			var bytesLength = sizeof(int) * length;
			TotalR = (int*)Marshal.AllocHGlobal(bytesLength);
			TotalG = (int*)Marshal.AllocHGlobal(bytesLength);
			TotalB = (int*)Marshal.AllocHGlobal(bytesLength);
			Count = (int*)Marshal.AllocHGlobal(bytesLength);
			AvgR = (int*)Marshal.AllocHGlobal(bytesLength);
			AvgG = (int*)Marshal.AllocHGlobal(bytesLength);
			AvgB = (int*)Marshal.AllocHGlobal(bytesLength);
		}

		public void ResetAverages()
		{
			for (int i = 0; i < _length; ++i)
			{
				TotalR[i] = 0;
				TotalG[i] = 0;
				TotalB[i] = 0;
				Count[i] = 0;
				AvgR[i] = 0;
				AvgG[i] = 0;
				AvgB[i] = 0;
			}
		}

		unsafe public void CalculateAverages()
		{
			for (int i = 0; i < _length; ++i)
			{
				AvgR[i] = TotalR[i] / Count[i];
				AvgG[i] = TotalG[i] / Count[i];
				AvgB[i] = TotalB[i] / Count[i];
			}
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
				return;

			Marshal.FreeHGlobal((IntPtr)TotalR);
			Marshal.FreeHGlobal((IntPtr)TotalG);
			Marshal.FreeHGlobal((IntPtr)TotalB);
			Marshal.FreeHGlobal((IntPtr)Count);
			Marshal.FreeHGlobal((IntPtr)AvgR);
			Marshal.FreeHGlobal((IntPtr)AvgG);
			Marshal.FreeHGlobal((IntPtr)AvgB);
			disposed = true;
		}
	}

	unsafe public class PixelZone
	{
		public readonly Point TopLeft;
		public readonly Point BottomRight;
		public readonly int Width;
		public readonly int Height;
		public readonly int Row;
		public readonly int Column;
		public int* TotalR;
		public int* TotalG;
		public int* TotalB;
		public int* Count;
		public int* AvgR;
		public int* AvgG;
		public int* AvgB;

		public PixelZone(int row, int column, int xMin, int xMax, int yMin, int yMax, PixelZonesTotals totals, int totalsIndex)
		{
			Row = row;
			Column = column;
			TopLeft = new Point(xMin, yMin);
			BottomRight = new Point(xMax, yMax);
			Width = BottomRight.X - TopLeft.X;
			Height = BottomRight.Y - TopLeft.Y;
			TotalR = totals.TotalR + totalsIndex;
			TotalG = totals.TotalG + totalsIndex;
			TotalB = totals.TotalB + totalsIndex;
			Count = totals.Count + totalsIndex;
			AvgR = totals.AvgR + totalsIndex;
			AvgG = totals.AvgG + totalsIndex;
			AvgB = totals.AvgB + totalsIndex;
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
