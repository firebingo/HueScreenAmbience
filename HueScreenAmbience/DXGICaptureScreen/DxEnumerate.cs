using SharpDX.DXGI;
using System;
using System.Collections.Generic;

namespace HueScreenAmbience.DXGICaptureScreen
{
	public class DxEnumeratedAdapter
	{
		public int AdapterId { get; set; }
		public string Name { get; set; }
	}

	public class DxEnumeratedDisplay
	{
		public int OutputId { get; set; }
		public string Name { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
		public double RefreshRate { get; set; }
		public string Format { get; set; }
		public int Bpp { get; set; }
		public string ColorSpace { get; set; }
	}

	public static class DxEnumerate
	{
		public static List<DxEnumeratedAdapter> GetAdapters()
		{
			var displays = new List<DxEnumeratedAdapter>();
			try
			{
				using var factory = new Factory1();
				var adapterCount = factory.GetAdapterCount();
				for (var i = 0; i < adapterCount; ++i)
				{
					using var adapter = factory.GetAdapter1(i);
					displays.Add(new DxEnumeratedAdapter()
					{
						AdapterId = i,
						Name = adapter.Description1.Description
					});
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			return displays;
		}

		public static List<DxEnumeratedDisplay> GetMonitors(int adapterId)
		{
			var displays = new List<DxEnumeratedDisplay>();
			try
			{
				using var factory = new Factory1();
				using var adapter = factory.GetAdapter1(adapterId);
				using var device = new SharpDX.Direct3D11.Device(adapter);
				var displayCount = adapter.GetOutputCount();
				for (var i = 0; i < displayCount; ++i)
				{
					using var output = adapter.GetOutput(i);
					using var output6 = output.QueryInterface<Output6>();
					using var duplicatedOutput = output6.DuplicateOutput1(device, 0, 1, new Format[] { Format.B8G8R8A8_UNorm });
					displays.Add(new DxEnumeratedDisplay()
					{
						OutputId = i,
						Name = output.Description.DeviceName.ToString(),
						Width = duplicatedOutput.Description.ModeDescription.Width,
						Height = duplicatedOutput.Description.ModeDescription.Height,
						RefreshRate = duplicatedOutput.Description.ModeDescription.RefreshRate.Numerator / duplicatedOutput.Description.ModeDescription.RefreshRate.Denominator,
						Format = duplicatedOutput.Description.ModeDescription.Format.ToString(),
						Bpp = output6.Description1.BitsPerColor,
						ColorSpace = output6.Description1.ColorSpace.ToString()
					});
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			return displays;
		}

		public static DxEnumeratedDisplay GetMonitor(int adapterId, int id)
		{
			DxEnumeratedDisplay display = null;
			try
			{
				using var factory = new Factory1();
				using var adapter = factory.GetAdapter1(adapterId);
				using var device = new SharpDX.Direct3D11.Device(adapter);
				if (id > adapter.GetOutputCount())
				{
					return null;
				}
				using var output = adapter.GetOutput(id);
				using var output6 = output.QueryInterface<Output6>();
				using var duplicatedOutput = output6.DuplicateOutput1(device, 0, 1, new Format[] { Format.B8G8R8A8_UNorm });
				display = new DxEnumeratedDisplay()
				{
					OutputId = id,
					Name = output.Description.DeviceName.ToString(),
					Width = duplicatedOutput.Description.ModeDescription.Width,
					Height = duplicatedOutput.Description.ModeDescription.Height,
					RefreshRate = duplicatedOutput.Description.ModeDescription.RefreshRate.Numerator / duplicatedOutput.Description.ModeDescription.RefreshRate.Denominator,
					Format = duplicatedOutput.Description.ModeDescription.Format.ToString(),
					Bpp = output6.Description1.BitsPerColor,
					ColorSpace = output6.Description1.ColorSpace.ToString()
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			return display;
		}
	}
}
