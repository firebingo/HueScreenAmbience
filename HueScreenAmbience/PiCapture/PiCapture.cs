using MMALSharp;
using MMALSharp.Common;
using MMALSharp.Handlers;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;

namespace HueScreenAmbience.PiCapture
{
	public class PiCapture : IDisposable
	{
		private readonly int _width = 0;
		private readonly int _height = 0;
		private readonly MMALCamera _cam;
		private readonly MemoryStreamCaptureHandler _captureHandler;
		private readonly byte[] _readBuffer;
		private bool _firstRun = true;

		private readonly FileLogger _logger;
		private Bitmap _bitmap;

		public PiCapture(int width, int height, FileLogger logger)
		{
			_logger = logger;
			_width = width;
			_height = height;

			try
			{
				_cam = MMALCamera.Instance;

				MMALCameraConfig.StillEncoding = MMALEncoding.BGR32;
				MMALCameraConfig.StillSubFormat = MMALEncoding.BGR32;

				_captureHandler = new MemoryStreamCaptureHandler();

				_cam.ConfigureCameraSettings(_captureHandler);

				_readBuffer = new byte[_width * 4];
			}
			catch(Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex.ToString()));
				throw;
			}
		}

		public async Task<Bitmap> GetFrame()
		{
			try
			{
				if (_bitmap == null)
					_bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);

				await _cam.TakeRawPicture(_captureHandler);

				// Camera warm up time
				if (_firstRun)
				{
					_firstRun = false;
					await Task.Delay(2000);
				}

				unsafe
				{
					if (_captureHandler.CurrentStream.Length != 0)
					{
						var boundsRect = new Rectangle(0, 0, _width, _height);
						var mapDest = _bitmap.LockBits(boundsRect, ImageLockMode.WriteOnly, _bitmap.PixelFormat);
						var destPtr = mapDest.Scan0;
						var bytesRead = 0;
						var totalBytes = _width * _height * 4;
						//Read the bitmap from the camera memory stream line by line
						do
						{
							_captureHandler.CurrentStream.Read(_readBuffer, 0, _readBuffer.Length);
							byte* ptr = (byte*)destPtr;
							for (var i = 0; i < _readBuffer.Length; ++i)
								ptr[i] = _readBuffer[i];

							destPtr = IntPtr.Add(destPtr, mapDest.Stride);
							bytesRead += _width * 4;
						} while (bytesRead < totalBytes);
						_bitmap.UnlockBits(mapDest);
					}
					else
						return null;
				}

				return _bitmap;
			}
			catch(Exception ex)
			{
				_ = Task.Run(() => _logger.WriteLog(ex.ToString()));
			}

			return null;
		}

		public void Dispose()
		{
			_captureHandler.Dispose();
			_bitmap.Dispose();
			_cam.Cleanup();
			GC.SuppressFinalize(this);
		}
	}
}
