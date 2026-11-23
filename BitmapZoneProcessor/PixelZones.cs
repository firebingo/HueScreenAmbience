using System;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;

namespace BitmapZoneProcessor
{
	public unsafe class PixelZonesTotals : IDisposable
	{
		private readonly int _length;
		public int* AvgB;
		public int* AvgG;
		public int* AvgR;
		public int* Count;
		public int* TotalB;
		public int* TotalG;

		public int* TotalR;
		private bool disposed;

		public PixelZonesTotals(int length)
		{
			_length = length;
			var bytesLength = (nuint)(sizeof(int) * length);
			TotalR = (int*)NativeMemory.Alloc(bytesLength);
			TotalG = (int*)NativeMemory.Alloc(bytesLength);
			TotalB = (int*)NativeMemory.Alloc(bytesLength);
			Count = (int*)NativeMemory.Alloc(bytesLength);
			AvgR = (int*)NativeMemory.Alloc(bytesLength);
			AvgG = (int*)NativeMemory.Alloc(bytesLength);
			AvgB = (int*)NativeMemory.Alloc(bytesLength);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public void ResetAverages()
		{
			for (var i = 0; i < _length; ++i)
			{
				TotalR[i] = 0;
				TotalG[i] = 0;
				TotalB[i] = 0;
				AvgR[i] = 0;
				AvgG[i] = 0;
				AvgB[i] = 0;
			}
		}

		public void CalculateAverages()
		{
			for (var i = 0; i < _length; ++i)
			{
				AvgR[i] = TotalR[i] / Count[i];
				AvgG[i] = TotalG[i] / Count[i];
				AvgB[i] = TotalB[i] / Count[i];
			}
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
				return;

			NativeMemory.Free(TotalR);
			NativeMemory.Free(TotalG);
			NativeMemory.Free(TotalB);
			NativeMemory.Free(Count);
			NativeMemory.Free(AvgR);
			NativeMemory.Free(AvgG);
			NativeMemory.Free(AvgB);
			disposed = true;
		}
	}

	public unsafe struct PixelZone : IDisposable
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
		public ZonePixelRange* PixelRanges;

		public PixelZone(int row, int column, int xMin, int xMax, int yMin, int yMax, int stride, int bitDepth,
			PixelZonesTotals totals, int totalsIndex)
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
			*Count = Width * Height;

			//The zones cover a rectangular area of the image, but the images are stored in a sequential array, so we need to have a range
			// for each y coordinate that the rectangle covers.
			var bytesLength = (nuint)(sizeof(ZonePixelRange) * Height);
			PixelRanges = (ZonePixelRange*)NativeMemory.Alloc(bytesLength);
			for (int y = TopLeft.Y, i = 0; y < BottomRight.Y; ++y, ++i)
			{
				var start = GetImageCoordinate(stride, TopLeft.X, y, bitDepth);
				var length = GetImageCoordinate(stride, BottomRight.X, y, bitDepth) - start;
				PixelRanges[i] = new ZonePixelRange(start, length);
			}
		}

		private static long GetImageCoordinate(int stride, int x, int y, int bitDepth = 3)
		{
			return y * stride + x * bitDepth;
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

		public void Dispose()
		{
			NativeMemory.Free(PixelRanges);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public readonly struct ZonePixelRange
	{
		public readonly long Start;
		public readonly long Length;

		public ZonePixelRange(long start, long length)
		{
			Start = start;
			Length = length;
		}
	}
}