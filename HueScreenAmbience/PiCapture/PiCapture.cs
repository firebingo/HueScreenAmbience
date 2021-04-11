using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HueScreenAmbience.PiCapture
{
	public class PiCapture : IDisposable
	{
		private readonly int _width = 0;
		private readonly int _height = 0;
		private readonly int _frameRate = 0;
		private Process _ffmpegProcess;
		private Stream _ffmpegStream;
		private bool _isRunning = false;
		private readonly int _frameLength = 0;
		private readonly byte[] _frameBuffer;
		private readonly byte[] _frameReadBuffer;
		private readonly SemaphoreSlim _readingSemaphore;

		private Thread _ffmpegThread;
		private Bitmap _bitmap;


		private readonly FileLogger _logger;


		public PiCapture(int width, int height, int frameRate, FileLogger logger)
		{
			_logger = logger;
			_width = width;
			_height = height;
			_frameRate = frameRate;
			_frameLength = _width * _height * 3;
			_frameBuffer = new byte[_frameLength];
			_frameReadBuffer = new byte[_frameLength];
			_readingSemaphore = new SemaphoreSlim(1, 1);
			_bitmap = new Bitmap(_width, _height, PixelFormat.Format24bppRgb);
		}

		//gpu_mem=256 /boot/config.txt
		public void Start()
		{
			try
			{
				if (_ffmpegProcess != null)
					return;
				_ffmpegProcess = new Process();

				_ffmpegProcess.StartInfo.UseShellExecute = false;
				_ffmpegProcess.StartInfo.RedirectStandardOutput = true;
				_ffmpegProcess.StartInfo.FileName = "ffmpeg";
				_ffmpegProcess.StartInfo.Arguments = $"-f v4l2 -input_format yuv420p -video_size {_width}x{_height} -i /dev/video0 -c:v rawvideo -pix_fmt bgr24 -r {_frameRate} -f rawvideo pipe:1";

				_isRunning = true;
				_ffmpegThread = new Thread(new ThreadStart(ReadLoop));
				_ffmpegThread.Name = "FFMPEG Read Thread";

				_ffmpegProcess.Start();
				_ffmpegStream = _ffmpegProcess.StandardOutput.BaseStream;
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
		}

		public void ReadLoop()
		{
			unsafe
			{
				var buffer = new byte[_width * 3];
				do
				{
					_readingSemaphore.Wait();
					fixed (byte* framePtr = &_frameBuffer[0])
					{
						var frameBytesRead = 0;
						var bytesRead = 0;

						if (frameBytesRead + _width * 3 > _frameLength)
						{
							if (!_isRunning)
							{
								_readingSemaphore.Release();
								break;
							}

							bytesRead = _ffmpegStream.Read(buffer, 0, _frameLength - frameBytesRead);
							fixed (byte* bufferPtr = &buffer[0])
							{
								for (var i = 0; i < bytesRead; ++i)
								{
									framePtr[frameBytesRead + i] = bufferPtr[i];
								}
							}

							frameBytesRead = 0;
						}
						else
						{
							if (!_isRunning)
							{
								_readingSemaphore.Release();
								break;
							}

							bytesRead = _ffmpegStream.Read(buffer, 0, buffer.Length);
							if (bytesRead != 0)
							{
								fixed (byte* bufferPtr = &buffer[0])
								{
									for (var i = 0; i < bytesRead; ++i)
									{
										framePtr[frameBytesRead + i] = bufferPtr[i];
									}
								}
								frameBytesRead += bytesRead;
							}
						}
					}
					_readingSemaphore.Release();
				}
				while (_isRunning);
			}
		}

		public async Task<Bitmap> GetFrame()
		{
			await _readingSemaphore.WaitAsync();
			try
			{
				unsafe
				{
					Buffer.BlockCopy(_frameBuffer, 0, _frameReadBuffer, 0, _frameLength);
					var boundsRect = new Rectangle(0, 0, _width, _height);
					var mapDest = _bitmap.LockBits(boundsRect, ImageLockMode.WriteOnly, _bitmap.PixelFormat);
					var destPtr = mapDest.Scan0;
					Marshal.Copy(_frameReadBuffer, 0, destPtr, _frameLength);
					_bitmap.UnlockBits(mapDest);
				}

				return _bitmap;
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
			finally
			{
				_readingSemaphore.Release();
			}

			return null;
		}

		public void Stop()
		{
			_isRunning = false;
			if (_ffmpegProcess != null)
				_ffmpegProcess.StandardOutput.Close();
		}

		public void Dispose()
		{
			if (_ffmpegProcess != null)
				_ffmpegStream?.Dispose();

			_bitmap?.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
