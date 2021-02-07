using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;

namespace HueScreenAmbience.DXGICaptureScreen
{
	public class DxCapture : IDisposable
	{
		private readonly int _width = 0;
		private readonly int _height = 0;
		private readonly Factory1 _factory;
		private readonly Adapter1 _adapter;
		private readonly SharpDX.Direct3D11.Device _device;
		private readonly Output _output;
		private readonly Output1 _output1;
		private readonly OutputDuplication _duplicatedOutput;
		private readonly Texture2D _screenTexture;
		private readonly FileLogger _logger;
		private Bitmap _bitmap;

		public DxCapture(int width, int height, int adapter, int monitor, FileLogger logger)
		{
			try
			{
				_logger = logger;
				_width = width;
				_height = height;

				_factory = new Factory1();
				_adapter = _factory.GetAdapter1(adapter);
				_device = new SharpDX.Direct3D11.Device(_adapter);

				_output = _adapter.GetOutput(monitor);
				_output1 = _output.QueryInterface<Output1>();
				_duplicatedOutput = _output1.DuplicateOutput(_device);

				var textureDesc = new Texture2DDescription
				{
					CpuAccessFlags = CpuAccessFlags.Read,
					BindFlags = BindFlags.None,
					Format = Format.B8G8R8A8_UNorm,
					Width = _width,
					Height = _height,
					OptionFlags = ResourceOptionFlags.None,
					MipLevels = 1,
					ArraySize = 1,
					SampleDescription = { Count = 1, Quality = 0 },
					Usage = ResourceUsage.Staging
				};
				_screenTexture = new Texture2D(_device, textureDesc);
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger?.WriteLog(ex.ToString()));
				throw;
			}
		}

		public Bitmap GetFrame()
		{
			try
			{
				var returnChange = false;
				var result = _duplicatedOutput.TryAcquireNextFrame(1000, out var duplicateFrameInformation, out var screenResource);
				if (result.Success && duplicateFrameInformation.LastPresentTime != 0)
				{
					using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
						_device.ImmediateContext.CopyResource(screenTexture2D, _screenTexture);

					_device.ImmediateContext.MapSubresource(_screenTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out var dataStream);

					var mapSource = _device.ImmediateContext.MapSubresource(_screenTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

					if(_bitmap == null)
						_bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);

					var boundsRect = new Rectangle(0, 0, _width, _height);

					var mapDest = _bitmap.LockBits(boundsRect, ImageLockMode.WriteOnly, _bitmap.PixelFormat);
					var sourcePtr = mapSource.DataPointer;
					var destPtr = mapDest.Scan0;
					for (int y = 0; y < _height; y++)
					{
						Utilities.CopyMemory(destPtr, sourcePtr, _width * 4);
						sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
						destPtr = IntPtr.Add(destPtr, mapDest.Stride);
					}
					_bitmap.UnlockBits(mapDest);

					_device.ImmediateContext.UnmapSubresource(_screenTexture, 0);
					returnChange = true;
				}

				screenResource?.Dispose();
				_duplicatedOutput.ReleaseFrame();

				//Only return the bitmap if it has actually been updated so we can still ignore it otherwise and save the processing.
				if (returnChange)
					return _bitmap;
				else
					return null;
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}

			return null;
		}

		public void Dispose()
		{
			_factory?.Dispose();
			_adapter?.Dispose();
			_device?.Dispose();
			_output?.Dispose();
			_output1?.Dispose();
			_duplicatedOutput?.Dispose();
			_screenTexture?.Dispose();
			_bitmap?.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
