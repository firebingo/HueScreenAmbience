using LightsShared;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HueScreenAmbience.FFMpegCapture
{
	unsafe public class FFMpegCapture : IDisposable
	{
		private readonly int _width = 0;
		private readonly int _height = 0;
		private readonly int _frameRate = 0;
		private readonly int _inputWidth = 0;
		private readonly int _inputHeight = 0;
		private readonly string _inputSource = "";
		private readonly string _inputFormat = "";
		private readonly string _inputPixelFormatType = "";
		private readonly string _inputPixelFormat = "";
		private readonly int _inputFrameRate = 0;
		private readonly int _bufferMultiplier = 4;
		private readonly int _threadQueueSize = 128;
		private readonly bool _ffmpegOutput = false;
		private readonly bool _useGpu = false;
		private Process _ffmpegProcess;
		private Stream _ffmpegStream;
		private Stream _ffmpegStdErrorStream;
		private bool _isRunning = false;
		private readonly int _frameLength = 0;
		private byte[] _presentBuffer;
		private byte[] _readyBuffer;
		private byte[] _progressBuffer;
		private int _readLock;

		private Thread _ffmpegThread;
		private Thread _ffmpegStdErrorThread;

		private readonly FileLogger _logger;

		public FFMpegCapture(int width, int height, int inputWidth, int inputHeight, int frameRate, string inputSource, string inputFormat, string inputPixelFormat, string inputPixelFormatType,
			int inputFrameRate, int bufferMultiplier, int threadQueueSize, FileLogger logger, bool ffmpegOutput = false, bool useGpu = false)
		{
			_logger = logger;
			_width = width;
			_height = height;
			_inputWidth = inputWidth;
			_inputHeight = inputHeight;
			_frameRate = frameRate;
			_inputSource = inputSource;
			_inputFormat = inputFormat;
			_inputPixelFormat = inputPixelFormat;
			_inputPixelFormatType = inputPixelFormatType;
			_ffmpegOutput = ffmpegOutput;
			_useGpu = useGpu;
			_inputFrameRate = inputFrameRate;
			_bufferMultiplier = bufferMultiplier;
			_threadQueueSize = threadQueueSize;
			_frameLength = _width * _height * 4;
			_presentBuffer = new byte[_frameLength];
			_readyBuffer = new byte[_frameLength];
			_progressBuffer = new byte[_frameLength];
			_readLock = 1;
		}

		//gpu_mem=256 /boot/config.txt
		//The read/write loop for this is triple buffered.
		// The ffmpeg read process writes and exchanges between _readyBuffer and _progressBuffer.
		// The read frame process reads and exchanges between _presentBuffer and _readyBuffer.
		// _readlock is a int being treated as a bool and controlled through Interlocked.
		// When ffmpeg finishes reading a frame it sets it to 0.
		// When a frame is requested it reads _readLock and sets it to 1. If the value it read was 0 it means
		//  it is safe to read the frame from the current buffer then swap again.
		//https://codereview.stackexchange.com/questions/163810/lock-free-zero-copy-triple-buffer
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
				if (_useGpu)
					_ffmpegProcess.StartInfo.Arguments = $"-hwaccel cuda -hwaccel_output_format cuda -f {_inputFormat} -{_inputPixelFormatType} {_inputPixelFormat} -rtbufsize {_width * _height * _bufferMultiplier} -thread_queue_size {_threadQueueSize} -framerate {_inputFrameRate} -video_size {_inputWidth}x{_inputHeight} -i {_inputSource} -vf \"format=nv12,hwupload,scale_cuda=w={_width}:h={_height},hwdownload,format=nv12\" -c:v rawvideo -pix_fmt rgb32 -r {_frameRate} -f rawvideo pipe:1";
				else
					_ffmpegProcess.StartInfo.Arguments = $"-f {_inputFormat} -{_inputPixelFormatType} {_inputPixelFormat} -rtbufsize {_width * _height * _bufferMultiplier} -thread_queue_size {_threadQueueSize} -framerate {_inputFrameRate} -video_size {_inputWidth}x{_inputHeight} -i {_inputSource} -c:v rawvideo -pix_fmt rgb32 -r {_frameRate} -s {_width}x{_height} -f rawvideo pipe:1";

				_isRunning = true;
				_ffmpegThread = new Thread(new ThreadStart(ReadLoop));
				_ffmpegThread.Name = "FFMPEG Read Thread";

				_ffmpegProcess.Start();
				_ffmpegStream = _ffmpegProcess.StandardOutput.BaseStream;
				//need to read this until exit otherwise we will block
				if (!_ffmpegOutput)
				{
					_ffmpegStdErrorStream = _ffmpegProcess.StandardError.BaseStream;
					_ffmpegStdErrorThread = new Thread(new ThreadStart(ReadStdErrorLoop));
					_ffmpegStdErrorThread.Name = "FFMPEG StdError Thread";
					_ffmpegStdErrorThread.Start();
				}
				_ffmpegThread.Start();
			}
			catch (Exception ex)
			{
				Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
		}

		public void ReadLoop()
		{
			var rowSize = _width * 4;
			var buffer = new byte[rowSize];
			var frameBytesRead = 0;
			var bytesRead = 0;
			do
			{
				fixed (byte* framePtr = &_progressBuffer[0])
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

						Buffer.BlockCopy(_progressBuffer, 0, _readyBuffer, 0, _frameLength);
						Interlocked.Exchange(ref _progressBuffer, _readyBuffer);
						frameBytesRead = 0;
						Interlocked.Exchange(ref _readLock, 0);
					}
					else
					{
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
				}
			} while (_isRunning);
		}

		public void ReadStdErrorLoop()
		{
			var buffer = new byte[8192];
			do
			{
				_ffmpegStdErrorStream?.Read(buffer, 0, 8192);
			} while (_isRunning);
		}

		public bool GetFrame(MemoryStream frameStream)
		{
			try
			{
				var start = DateTime.UtcNow;
				var frameData = frameStream.GetBuffer();
				var t = DateTime.UtcNow;
				Buffer.BlockCopy(_presentBuffer, 0, frameData, 0, _frameLength);
				if (Interlocked.Exchange(ref _readLock, 1) == 1)
				{
					return false;
				}
				Interlocked.Exchange(ref _readyBuffer, _presentBuffer);
				//Console.WriteLine($"Buffer.BlockCopy: {(DateTime.UtcNow - t).TotalMilliseconds}");
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
			{
				_ffmpegProcess.StandardOutput.Close();
				if (!_ffmpegOutput)
					_ffmpegProcess.StandardError.Close();
			}
		}

		public void Dispose()
		{
			if (_ffmpegProcess != null)
			{
				_ffmpegStream?.Dispose();
				_ffmpegStdErrorStream?.Dispose();
			}

			Interlocked.Exchange(ref _readLock, 0);
			GC.SuppressFinalize(this);
		}
	}
}
