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
				DXGI.CreateDXGIFactory2<IDXGIFactory7>(false, out var factory);
				for (var i = 0; factory.EnumAdapterByGpuPreference(i, GpuPreference.Unspecified, out IDXGIAdapter1 adapter).Success; ++i)
				{
					if (adapter == null)
						continue;

					displays.Add(new DxEnumeratedAdapter()
					{
						AdapterId = i,
						Name = adapter.Description1.Description
					});
					adapter.Dispose();
				}
				factory.Dispose();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
			return displays;
		}

		public static IDXGIAdapter1 GetAdapter1(int adapterId, IDXGIFactory7 factory)
		{
			if (factory.EnumAdapterByGpuPreference(adapterId, GpuPreference.Unspecified, out IDXGIAdapter1 adapter).Success && adapter is not null)
				return adapter;

			return null;
		}

		public static IDXGIOutput GetOutput(int outputId, IDXGIAdapter1 adapter)
		{
			if (adapter.EnumOutputs(outputId, out IDXGIOutput output).Success && output is not null)
				return output;

			return null;
		}

		public static List<DxEnumeratedDisplay> GetMonitors(int adapterId)
		{
			var displays = new List<DxEnumeratedDisplay>();
			try
			{
				DXGI.CreateDXGIFactory2<IDXGIFactory7>(false, out var factory);
				using var adapter = GetAdapter1(adapterId, factory);
				D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, new FeatureLevel[] { FeatureLevel.Level_11_1 }, out var device);

				for (var i = 0; adapter.EnumOutputs(i, out IDXGIOutput output).Success; ++i)
				{
					if (output == null)
						continue;

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
					output.Dispose();
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
				DXGI.CreateDXGIFactory2<IDXGIFactory7>(false, out var factory);
				using var adapter = GetAdapter1(adapterId, factory);
				D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, new FeatureLevel[] { FeatureLevel.Level_11_1 }, out var device);
				using var output = GetOutput(id, adapter);
				if (output == null)
					return null;
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
	}
}
