using System;
using System.Collections.Generic;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

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
				DXGI.CreateDXGIFactory2<IDXGIFactory2>(false, out var factory);
				var adapterCount = factory.GetAdapterCount1();
				for (var i = 0; i < adapterCount; ++i)
				{
					using var adapter = factory.GetAdapter1(i);
					displays.Add(new DxEnumeratedAdapter()
					{
						AdapterId = i,
						Name = adapter.Description1.Description
					});
				}
				factory.Dispose();
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
				DXGI.CreateDXGIFactory2<IDXGIFactory2>(false, out var factory);
				using var adapter = factory.GetAdapter1(adapterId);
				D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, new FeatureLevel[] { FeatureLevel.Level_11_1 }, out var device);

				var displayCount = GetOutputCount(adapter);
				for (var i = 0; i < displayCount; ++i)
				{
					using var output = adapter.GetOutput(i);
					using var output6 = output.QueryInterface<IDXGIOutput6>();
					using var duplicatedOutput = output6.DuplicateOutput1(device, 1, new Format[] { Format.B8G8R8A8_UNorm });
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

				factory.Dispose();
				device.Dispose();
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
				DXGI.CreateDXGIFactory2<IDXGIFactory2>(false, out var factory);
				using var adapter = factory.GetAdapter1(adapterId);
				D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, new FeatureLevel[] { FeatureLevel.Level_11_1 }, out var device);
				if (id > GetOutputCount(adapter))
				{
					return null;
				}
				using var output = adapter.GetOutput(id);
				using var output6 = output.QueryInterface<IDXGIOutput6>();
				using var duplicatedOutput = output6.DuplicateOutput1(device, 1, new Format[] { Format.B8G8R8A8_UNorm });
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

				factory.Dispose();
				device.Dispose();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			return display;
		}

		private static int GetOutputCount(IDXGIAdapter1 adapter)
		{
			var nbOutputs = 0;
			do
			{
				try
				{
					var output = adapter.GetOutput(nbOutputs);
					if (output == null)
						break;
					output.Dispose();
					nbOutputs++;
				}
				catch
				{
					break;
				}
			} while (true);
			return nbOutputs;
		}
	}
}
