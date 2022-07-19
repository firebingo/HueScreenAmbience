using LightsShared;
using SharpGen.Runtime;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace HueScreenAmbience.DXGICaptureScreen
{
	public class DxCapture : IDisposable
	{
		private readonly int _width = 0;
		private readonly int _height = 0;
		private readonly IDXGIFactory7 _factory;
		private readonly IDXGIAdapter1 _adapter;
		private readonly ID3D11Device _device;
		private readonly ID3D11DeviceContext _deviceContext;
		private readonly IDXGIOutput _output;
		private readonly IDXGIOutput6 _output6;
		private readonly IDXGIOutputDuplication _duplicatedOutput;
		private readonly ID3D11Texture2D _screenTexture;
		private readonly FileLogger _logger;

		private bool _readingFrame = false;

		public DxCapture(int width, int height, int adapter, int monitor, FileLogger logger)
		{
			try
			{
				_logger = logger;
				_width = width;
				_height = height;

				DXGI.CreateDXGIFactory2<IDXGIFactory7>(false, out var factory);
				_factory = factory;
				_adapter = _factory.GetAdapter1(adapter);
				D3D11.D3D11CreateDevice(_adapter, DriverType.Unknown, DeviceCreationFlags.None, new FeatureLevel[] { FeatureLevel.Level_11_1 }, out var device, out var context);
				_device = device;
				_deviceContext = context;

				_output = _adapter.GetOutput(monitor);
				_output6 = _output.QueryInterface<IDXGIOutput6>();
				_duplicatedOutput = _output6.DuplicateOutput1(_device, 1, new Format[] { Format.B8G8R8A8_UNorm });

				var textureDesc = new Texture2DDescription
				{
					CPUAccessFlags = CpuAccessFlags.Read | CpuAccessFlags.Write,
					BindFlags = BindFlags.None,
					Format = Format.B8G8R8A8_UNorm,
					Width = _width,
					Height = _height,
					MiscFlags = ResourceOptionFlags.None,
					MipLevels = 1,
					ArraySize = 1,
					SampleDescription = { Count = 1, Quality = 0 },
					Usage = ResourceUsage.Staging
				};

				_screenTexture = _device.CreateTexture2D(textureDesc);
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger?.WriteLog(ex.ToString()));
				throw;
			}
		}

		public bool GetFrame(MemoryStream frameStream)
		{
			try
			{
				_readingFrame = true;
				var returnChange = false;
				var result = _duplicatedOutput.AcquireNextFrame(1000, out var duplicateFrameInformation, out var screenResource);
				if (result.Success && duplicateFrameInformation.LastPresentTime != 0)
				{
					using (var screenTexture2D = screenResource.QueryInterface<ID3D11Texture2D>())
					{
						if (screenTexture2D.Description.Format == Format.B8G8R8A8_UNorm)
							_deviceContext.CopyResource(_screenTexture, screenTexture2D);
						else
						{
							screenResource?.Dispose();
							_duplicatedOutput?.ReleaseFrame();
							return false;
						}
					}

					var mapSource = _deviceContext.Map(_screenTexture, 0, MapMode.Read);

					var sourcePtr = mapSource.DataPointer;
					unsafe
					{
						var frameData = frameStream.GetBuffer();
						fixed (byte* destBytePtr = &frameData[0])
						{
							var destPtr = (IntPtr)destBytePtr;
							for (int y = 0; y < _height; y++)
							{
								MemoryHelpers.CopyMemory(destPtr, sourcePtr, mapSource.RowPitch);
								sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
								destPtr = IntPtr.Add(destPtr, mapSource.RowPitch);
							}
						}
					}

					_deviceContext.Unmap(_screenTexture, 0);
					returnChange = true;
				}

				screenResource?.Dispose();
				_duplicatedOutput?.ReleaseFrame();

				//Only return the bitmap if it has actually been updated so we can still ignore it otherwise and save the processing.
				if (returnChange)
					return true;
				else
					return false;
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
			finally
			{
				_readingFrame = false;
			}

			return false;
		}

		public void Dispose()
		{
			while (_readingFrame)
				Thread.Sleep(0);
			_factory?.Dispose();
			_adapter?.Dispose();
			_device?.Dispose();
			_output?.Dispose();
			_output6?.Dispose();
			_duplicatedOutput?.Dispose();
			_screenTexture?.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
