using BitmapZoneProcessor;
using LightsShared;
using NanoLeafAPI;
using NanoLeafAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HueScreenAmbience.NanoLeaf
{
	public class NanoLeafClient : IAsyncDisposable
	{
		private NanoLeafController _controller { get; set; }
		private FileLogger _logger;
		private Config _config;

		private bool _started = false;
		private DateTime _lastChangeTime;
		private TimeSpan _frameTimeSpan;
		private byte _colorChangeThreshold = 5;
		private ExtControlFrame _frame;
		private PanelLayout _layout;
		private MemoryStream _imageByteStream;
		private int _redWidth;
		private int _redHeight;
		private List<PanelPosition> _usePanels;
		private int frame = 0;

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
		}

		public async Task Start()
		{
			try
			{
				if (_started)
					return;

				_frameTimeSpan = TimeSpan.FromMilliseconds(1000 / _config.Model.NanoLeafSettings.UpdateFrameRate);
				_colorChangeThreshold = _config.Model.NanoLeafSettings.ColorChangeThreshold;

				_controller = new NanoLeafController(_config.Model.NanoLeafSettings.RemoteAddressIp.ToString(),
				_config.Model.NanoLeafSettings.Port, _config.Model.NanoLeafSettings.AuthToken);

				var state = await _controller.GetCurrentState();
				_layout = state.PanelLayout;

				_frame = new ExtControlFrame()
				{
					Panels = new List<ExtControlPanel>()
				};
				float bigX = 0;
				float bigY = 0;
				int minX = 0;
				int minY = 0;
				_usePanels = _layout.Layout.Positions.Where(x => x.PanelShape != PanelShape.ShapeController && x.PanelShape != PanelShape.PowerConnector).ToList();
				//Rotate points by global orientation.
				// Im pretty sure this is still wrong and the adjustment angle values make no sense?? Im bad at math.
				var orient = _layout.GlobalOrientation.Value + _config.Model.NanoLeafSettings.ExtraOrientationAdjustAngle;
				foreach (var panel in _usePanels)
				{
					var x = panel.X * Math.Cos(orient) - panel.Y * Math.Sin(orient);
					var y = panel.X * Math.Sin(orient) + panel.Y * Math.Cos(orient);
					panel.X = (int)Math.Floor(x);
					panel.Y = (int)Math.Floor(y);
					if (panel.X < minX)
						minX = panel.X;
					if (panel.Y < minY)
						minY = panel.Y;
				}

				//Bring points back into positive space after rotation
				foreach (var panel in _usePanels)
				{
					panel.X += Math.Abs(minX);
					panel.Y += Math.Abs(minY);
				}

				//Flip points then bring them back into positive
				if(_config.Model.NanoLeafSettings.FlipX)
				{
					foreach (var panel in _usePanels)
					{
						panel.X = -panel.X;
						if (panel.X < minX)
							minX = panel.X;
					}

					foreach (var panel in _usePanels)
					{
						panel.X += Math.Abs(minX);
					}
				}

				//Find the max positions of the panels
				foreach (var panel in _usePanels)
				{
					//Console.WriteLine($"{panel.PanelID}: {panel.X},{panel.Y}");
					if (panel.X > bigX)
						bigX = panel.X;
					if (panel.Y > bigY)
						bigY = panel.Y;
					_frame.Panels.Add(new ExtControlPanel()
					{
						PanelID = panel.PanelID
					});
				}

				if (_config.Model.NanoLeafSettings.LayoutResReduce > 1)
				{
					_redWidth = (int)Math.Ceiling(bigX / _config.Model.NanoLeafSettings.LayoutResReduce);
					_redHeight = (int)Math.Ceiling(bigY / _config.Model.NanoLeafSettings.LayoutResReduce);
				}
				else
				{
					_redWidth = (int)Math.Ceiling(bigX);
					_redHeight = (int)Math.Ceiling(bigY);
				}
				_imageByteStream = new MemoryStream(_redWidth * _redHeight * 3);

				await _controller.StartExternalControl();
				_started = true;
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}
		}

		public async Task Stop()
		{
			try
			{
				if (!_started)
					return;

				await _controller.StopExternalControl();

				_frame = null;
				_layout = null;
				_imageByteStream?.Dispose();
				_imageByteStream = null;

				_controller?.Dispose();
				_controller = null;
				_started = false;
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}
		}

		public async Task UpdateFromImage(MemoryStream image, int width, int height)
		{
			try
			{
				if ((DateTime.UtcNow - _lastChangeTime).TotalMilliseconds < _frameTimeSpan.TotalMilliseconds)
					return;

				frame++;
				var red = _config.Model.RgbDeviceSettings.KeyboardResReduce;

				_imageByteStream.Seek(0, SeekOrigin.Begin);
				var resizeImage = ImageHandler.ResizeImage(image, width, height, _imageByteStream, _redWidth, _redHeight, pixelFormat: PixelFormat.Rgb24);
				//await ImageHandler.WriteImageToFile(resizeImage, _redWidth, _redHeight, Path.Combine(_config.Model.ImageDumpLocation, $"{frame}.png"), pixelFormat: PixelFormat.Rgb24);
				var sampleX = 0;
				var sampleY = 0;
				var pixIndex = 0;
				var stride = _redWidth * 3;
				byte r = 0;
				byte g = 0;
				byte b = 0;
				for (var i = 0; i < _usePanels.Count; ++i)
				{
					//TODO Maybe do something like rgblighter that we get the size of the panels and 
					// get an average of all the pixels the panel covers instead of just the center.
					sampleX = (int)Math.Floor((float)_usePanels[i].X / red);
					sampleY = (int)Math.Floor((float)_usePanels[i].Y / red);
					pixIndex = (sampleY * stride) + sampleX * 3;
					_imageByteStream.Seek(pixIndex, SeekOrigin.Begin);
					r = (byte)_imageByteStream.ReadByte();
					g = (byte)_imageByteStream.ReadByte();
					b = (byte)_imageByteStream.ReadByte();

					r = (byte)Math.Clamp((int)Math.Floor(r * _config.Model.NanoLeafSettings.ColorMultiplier), 0, 255);
					g = (byte)Math.Clamp((int)Math.Floor(g * _config.Model.NanoLeafSettings.ColorMultiplier), 0, 255);
					b = (byte)Math.Clamp((int)Math.Floor(b * _config.Model.NanoLeafSettings.ColorMultiplier), 0, 255);
					var lastColorR = _frame.Panels[i].Red;
					var lastColorG = _frame.Panels[i].Green;
					var lastColorB = _frame.Panels[i].Blue;
					var blendAmount = 1.0f - _config.Model.NanoLeafSettings.BlendLastColorAmount;
					if (blendAmount != 0.0f)
					{
						r = (byte)Math.Clamp(Math.Sqrt((1 - blendAmount) * Math.Pow(lastColorR, 2) + blendAmount * Math.Pow(r, 2)), 0, 255);
						g = (byte)Math.Clamp(Math.Sqrt((1 - blendAmount) * Math.Pow(lastColorG, 2) + blendAmount * Math.Pow(g, 2)), 0, 255);
						b = (byte)Math.Clamp(Math.Sqrt((1 - blendAmount) * Math.Pow(lastColorB, 2) + blendAmount * Math.Pow(b, 2)), 0, 255);
					}
					if (lastColorR >= r - _colorChangeThreshold && lastColorR <= r + _colorChangeThreshold)
						r = lastColorR;
					if (lastColorG >= g - _colorChangeThreshold && lastColorG <= g + _colorChangeThreshold)
						g = lastColorG;
					if (lastColorB >= b - _colorChangeThreshold && lastColorB <= b + _colorChangeThreshold)
						b = lastColorB;
					if (r + g + b <= _config.Model.HueSettings.MinRoundColor)
						r = g = b = 0;
					_frame.Panels[i].Red = r;
					_frame.Panels[i].Green = g;
					_frame.Panels[i].Blue = b;
				}

				await _controller.SendExternalControlFrame(_frame);

				_lastChangeTime = DateTime.UtcNow;
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}
		}

		public async ValueTask DisposeAsync()
		{
			try
			{
				if (_started)
				{
					await _controller.StopExternalControl();
					_started = false;
				}
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}

			_controller?.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
