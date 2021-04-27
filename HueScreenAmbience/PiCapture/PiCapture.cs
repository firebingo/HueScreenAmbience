using System;
using System.Diagnostics;
using System.IO;
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
		private bool _readLock;

		private Thread _ffmpegThread;

		private readonly FileLogger _logger;


		public PiCapture(int width, int height, int frameRate, FileLogger logger)
		{
			_logger = logger;
			_width = width;
			_height = height;
			_frameRate = frameRate;
			_frameLength = _width * _height * 4;
			_frameBuffer = new byte[_frameLength];
			_readLock = false;
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
				_ffmpegProcess.StartInfo.RedirectStandardError = true;
				_ffmpegProcess.StartInfo.RedirectStandardOutput = true;
				_ffmpegProcess.StartInfo.FileName = "ffmpeg";
				//rgb32 and bgr32 are flipped for some reason??
				_ffmpegProcess.StartInfo.Arguments = $"-f v4l2 -input_format yuv422p -framerate 60 -video_size {_width}x{_height} -i /dev/video0 -c:v rawvideo -pix_fmt rgb32 -r {_frameRate} -f rawvideo pipe:1";

				_isRunning = true;
				_ffmpegThread = new Thread(new ThreadStart(ReadLoop));
				_ffmpegThread.Name = "FFMPEG Read Thread";

				_ffmpegProcess.Start();
				_ffmpegStream = _ffmpegProcess.StandardOutput.BaseStream;
				_ffmpegThread.Start();
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
				var rowSize = _width * 4;
				var buffer = new byte[rowSize];
				var frameBytesRead = 0;
				var bytesRead = 0;
				fixed (byte* framePtr = &_frameBuffer[0])
				{
					do
					{
						//Console.WriteLine("FFMPEG Read Wait");
						//Console.WriteLine("Begin FFMPEG Read");

						do
						{
							Thread.Sleep(0);
						}
						while (_readLock);

						_readLock = true;
						if (frameBytesRead + rowSize > _frameLength)
						{
							if (!_isRunning)
							{
								_readLock = false;
								break;
							}

							bytesRead = _ffmpegStream.Read(buffer, 0, _frameLength - frameBytesRead);
							//Console.WriteLine($"Read Bytes {bytesRead}");
							fixed (byte* bufferPtr = &buffer[0])
							{
								var j = frameBytesRead;
								for (var i = 0; i < bytesRead; ++i)
								{
									framePtr[j++] = bufferPtr[i];
								}
							}

							frameBytesRead = 0;
						}
						else
						{
							if (!_isRunning)
							{
								_readLock = false;
								break;
							}

							bytesRead = _ffmpegStream.Read(buffer, 0, buffer.Length);
							if (bytesRead != 0)
							{
								fixed (byte* bufferPtr = &buffer[0])
								{
									var j = frameBytesRead;
									for (var i = 0; i < bytesRead; ++i)
									{
										framePtr[j++] = bufferPtr[i];
									}
								}
								frameBytesRead += bytesRead;
							}
						}
						_readLock = false;
						//Console.WriteLine("End FFMPEG Read");
					} while (_isRunning);
				}
			}
		}

		public bool GetFrame(MemoryStream frameStream)
		{
			try
			{
				var start = DateTime.UtcNow;
				var frameData = frameStream.GetBuffer();
				do
				{
					Thread.Sleep(0);
				}
				while (_readLock);
				_readLock = true;
				unsafe
				{
					var t = DateTime.UtcNow;
					Buffer.BlockCopy(_frameBuffer, 0, frameData, 0, _frameLength);
					//Console.WriteLine($"Buffer.BlockCopy: {(DateTime.UtcNow - t).TotalMilliseconds}");
				}
				_readLock = false;
				//Console.WriteLine($"GetFrame: {(DateTime.UtcNow - start).TotalMilliseconds}");
				return true;
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}

			return false;
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

			GC.SuppressFinalize(this);
		}
	}
}
