using LightsShared;
using SharpGen.Runtime;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace HueScreenAmbience.DXGICaptureScreen
{
	[StructLayout(LayoutKind.Explicit, Size = 16)]
	struct PS_C_BUFFER
	{
		[FieldOffset(0)]
		public Int4 values;
	}

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
		private readonly ID3D11ShaderResourceView _screenResourceView;
		private readonly ID3D11Texture2D _scaleTexture;
		private readonly ID3D11RenderTargetView _scaleRenderView;
		private readonly ID3D11Texture2D _readTexture;
		private readonly ID3D11VertexShader _vertexShader;
		private readonly ID3D11PixelShader _scaleShader;
		private readonly ID3D11SamplerState _samplerState;
		private readonly ID3D11Buffer _pixelCBuffer;
		private readonly Viewport _viewport;
		private readonly FileLogger _logger;

		private bool _readingFrame = false;

		public DxCapture(int width, int height, int adapter, int monitor, FileLogger logger)
		{
			try
			{
				_logger = logger;
				_width = width;
				_height = height;

				DXGI.CreateDXGIFactory2<IDXGIFactory7>(false, out _factory);
				_adapter = DxEnumerate.GetAdapter1(adapter, _factory);
				D3D11.D3D11CreateDevice(_adapter, DriverType.Unknown, DeviceCreationFlags.None, new FeatureLevel[] { FeatureLevel.Level_11_1 }, out var device, out var context);
				_device = device;
				_deviceContext = context;

				_output = DxEnumerate.GetOutput(monitor, _adapter);
				_output6 = _output.QueryInterface<IDXGIOutput6>();
				_duplicatedOutput = _output6.DuplicateOutput1(_device, 1, new Format[] { Format.B8G8R8A8_UNorm });

				var textureDesc = new Texture2DDescription
				{
					CPUAccessFlags = CpuAccessFlags.None,
					BindFlags = BindFlags.ShaderResource,
					Format = Format.B8G8R8A8_UNorm,
					Width = _width,
					Height = _height,
					MiscFlags = ResourceOptionFlags.None,
					MipLevels = 1,
					ArraySize = 1,
					SampleDescription = { Count = 1, Quality = 0 },
					Usage = ResourceUsage.Default
				};
				var shrvDesc = new ShaderResourceViewDescription
				{
					Format = textureDesc.Format,
					ViewDimension = ShaderResourceViewDimension.Texture2D
				};
				shrvDesc.Texture2D.MostDetailedMip = 0;
				shrvDesc.Texture2D.MipLevels = 1;

				_screenTexture = _device.CreateTexture2D(textureDesc);
				_screenResourceView = _device.CreateShaderResourceView(_screenTexture, shrvDesc);

				textureDesc = new Texture2DDescription
				{
					CPUAccessFlags = CpuAccessFlags.None,
					BindFlags = BindFlags.RenderTarget,
					Format = Format.B8G8R8A8_UNorm,
					Width = 1280,
					Height = 720,
					MiscFlags = ResourceOptionFlags.None,
					MipLevels = 1,
					ArraySize = 1,
					SampleDescription = { Count = 1, Quality = 0 },
					Usage = ResourceUsage.Default
				};
				_scaleTexture = _device.CreateTexture2D(textureDesc);

				var renderDesc = new RenderTargetViewDescription
				{
					Format = textureDesc.Format,
					ViewDimension = RenderTargetViewDimension.Texture2D
				};
				renderDesc.Texture2D.MipSlice = 0;
				_scaleRenderView = _device.CreateRenderTargetView(_scaleTexture, renderDesc);

				textureDesc = new Texture2DDescription
				{
					CPUAccessFlags = CpuAccessFlags.Read | CpuAccessFlags.Write,
					BindFlags = BindFlags.None,
					Format = Format.B8G8R8A8_UNorm,
					Width = 1280,
					Height = 720,
					MiscFlags = ResourceOptionFlags.None,
					MipLevels = 1,
					ArraySize = 1,
					SampleDescription = { Count = 1, Quality = 0 },
					Usage = ResourceUsage.Staging
				};
				_readTexture = _device.CreateTexture2D(textureDesc);

				var bytes = File.ReadAllBytes("DXGICaptureScreen/Zones.cso");
				_scaleShader = _device.CreatePixelShader(bytes);
				bytes = File.ReadAllBytes("DXGICaptureScreen/VertexShader.cso");
				_vertexShader = _device.CreateVertexShader(bytes);
				
				var samplerDesc = new SamplerDescription();
				samplerDesc.Filter = Filter.MinMagMipLinear;
				samplerDesc.AddressU = TextureAddressMode.Clamp;
				samplerDesc.AddressV = TextureAddressMode.Clamp;
				samplerDesc.AddressW = TextureAddressMode.Clamp;
				samplerDesc.MipLODBias = 0.0f;
				samplerDesc.MaxAnisotropy = 1;
				samplerDesc.ComparisonFunc = ComparisonFunction.Always;
				samplerDesc.BorderColor = new Color4(0, 0, 0, 0);
				samplerDesc.MinLOD = 0;
				samplerDesc.MaxLOD = 1000.0f;
				_samplerState = _device.CreateSamplerState(samplerDesc);

				var bufDesc = new BufferDescription()
				{
					CPUAccessFlags = CpuAccessFlags.None,
					BindFlags = BindFlags.ConstantBuffer,
					ByteWidth = 16,
					Usage = ResourceUsage.Default
				};
				_pixelCBuffer = _device.CreateBuffer(bufDesc);

				_viewport = new Viewport(1280, 720);
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

					//_deviceContext.ClearRenderTargetView(_scaleRenderView, new Color4(1.0f, 1.0f, 1.0f, 1.0f));
					_deviceContext.OMSetRenderTargets(_scaleRenderView);
					_deviceContext.RSSetViewport(_viewport);
					_deviceContext.VSSetShader(_vertexShader);
					var shaderVal = new PS_C_BUFFER()
					{
						values = new Int4(1, 0, 0, 1)
					};
					_deviceContext.UpdateSubresource(shaderVal, _pixelCBuffer);
					_deviceContext.PSSetSamplers(0, 1, new ID3D11SamplerState[1] { _samplerState });
					_deviceContext.PSSetShaderResources(0, 1, new ID3D11ShaderResourceView[1] { _screenResourceView });
					_deviceContext.PSSetConstantBuffers(0, 1, new ID3D11Buffer[1] { _pixelCBuffer });
					_deviceContext.PSSetShader(_scaleShader);
					_deviceContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
					_deviceContext.Draw(4, 0);

					_deviceContext.Flush();

					_deviceContext.CopyResource(_readTexture, _scaleTexture);
					//_deviceContext.CopyResource(_readTexture, _screenTexture);

					var mapSource = _deviceContext.Map(_readTexture, 0, MapMode.Read);

					var sourcePtr = mapSource.DataPointer;
					unsafe
					{
						var frameData = frameStream.GetBuffer();
						fixed (byte* destBytePtr = &frameData[0])
						{
							var destPtr = (IntPtr)destBytePtr;
							for (int y = 0; y < 720; y++)
							{
								MemoryHelpers.CopyMemory(destPtr, sourcePtr, mapSource.RowPitch);
								sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
								destPtr = IntPtr.Add(destPtr, mapSource.RowPitch);
							}
						}
					}

					_deviceContext.Unmap(_readTexture, 0);
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
			_screenResourceView?.Dispose();
			_screenTexture?.Dispose();
			_scaleTexture?.Dispose();
			_scaleRenderView?.Dispose();
			_scaleShader?.Dispose();
			_output?.Dispose();
			_output6?.Dispose();
			_duplicatedOutput?.Dispose();
			_factory?.Dispose();
			_adapter?.Dispose();
			_device?.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
