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
		private readonly int _inputWidth = 0;
		private readonly int _inputHeight = 0;
		private readonly string _inputSource = "";
		private readonly string _inputFormat = "";
		private readonly int _inputFrameRate = 0;
		private readonly bool _ffmpegOutput = false;
		private Process _ffmpegProcess;
		private Stream _ffmpegStream;
		private bool _isRunning = false;
		private readonly int _frameLength = 0;
		private readonly byte[] _frameBuffer;
		private readonly byte[] _frameBackBuffer;
		private bool _readLock;

		private Thread _ffmpegThread;

		private readonly FileLogger _logger;

		public PiCapture(int width, int height, int inputWidth, int inputHeight, int frameRate, string inputSource, string inputFormat, int inputFrameRate, FileLogger logger, bool ffmpegOutput = false)
		{
			_logger = logger;
			_width = width;
			_height = height;
			_inputWidth = inputWidth;
			_inputHeight = inputHeight;
			_frameRate = frameRate;
			_inputSource = inputSource;
			_inputFormat = inputFormat;
			_ffmpegOutput = ffmpegOutput;
			_inputFrameRate = inputFrameRate;
			_frameLength = _width * _height * 4;
			_frameBuffer = new byte[_frameLength];
			_frameBackBuffer = new byte[_frameLength];
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
				_ffmpegProcess.StartInfo.RedirectStandardError = !_ffmpegOutput;
				_ffmpegProcess.StartInfo.RedirectStandardOutput = true;
				_ffmpegProcess.StartInfo.FileName = "ffmpeg";
				//rgb32 and bgr32 are flipped for some reason??
				_ffmpegProcess.StartInfo.Arguments = $"-f v4l2 -input_format {_inputFormat} -framerate {_inputFrameRate} -video_size {_inputWidth}x{_inputHeight} -i {_inputSource} -c:v rawvideo -pix_fmt rgb32 -r {_frameRate} -s {_width}x{_height} -f rawvideo pipe:1";

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
						if (frameBytesRead + rowSize > _frameLength)
						{
							if (!_isRunning)
								break;

							bytesRead = _ffmpegStream.Read(buffer, 0, _frameLength - frameBytesRead);
							fixed (byte* bufferPtr = &buffer[0])
							{
								var j = frameBytesRead;
								for (var i = 0; i < bytesRead; ++i)
								{
									framePtr[j++] = bufferPtr[i];
								}
							}

							do
							{
								Thread.Sleep(0);
							}
							while (_readLock);
							_readLock = true;

							Buffer.BlockCopy(_frameBuffer, 0, _frameBackBuffer, 0, _frameLength);

							_readLock = false;
							frameBytesRead = 0;
						}
						else
						{
							if (!_isRunning)
								_readLock = false;

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
					Buffer.BlockCopy(_frameBackBuffer, 0, frameData, 0, _frameLength);
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
