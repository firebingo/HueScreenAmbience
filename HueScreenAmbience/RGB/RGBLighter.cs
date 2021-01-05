using RGB.NET.Core;
using RGB.NET.Brushes;
using RGB.NET.Groups;
using RGB.NET.Devices.Corsair;
using RGB.NET.Devices.Razer;
using RGB.NET.Devices.Logitech;
using RGB.NET.Devices.Asus;
using System;
using System.Threading.Tasks;
using ImageMagick;
using System.Linq;
using System.IO;
using BitmapZoneProcessor;

namespace HueScreenAmbience.RGB
{
	public class RGBLighter : IDisposable
	{
		private readonly RGBSurface _surface;
		private FileLogger _logger;
		private Config _config;
		private MemoryStream imageByteStream;
		private DateTime _lastChangeTime;
		private TimeSpan _frameTimeSpan;
		private bool _started = false;
		private byte _colorChangeThreshold = 5;

		public RGBLighter()
		{
			_surface = RGBSurface.Instance;
			_surface.Exception += Surface_Exception;
		}

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
		}

		public void Start()
		{
			try
			{
				if (_started)
					return;

				_frameTimeSpan = TimeSpan.FromMilliseconds(1000 / _config.Model.rgbDeviceSettings.updateFrameRate);
				_colorChangeThreshold = _config.Model.rgbDeviceSettings.colorChangeThreshold;
				imageByteStream = new MemoryStream();
				LoadDevices();
				_started = true;
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}
		}

		public void Stop()
		{
			try
			{
				if (!_started)
					return;

				//This is the best I can do because rgb.net doesn't let me fully release the surface and let it be able to be created again.
				if (_surface?.Devices != null)
				{
					foreach (var device in _surface.Devices)
					{
						var group = new ListLedGroup(device)
						{
							Brush = new SolidColorBrush(RGBConsts.White)
						};
						_surface.Update();
						group.Detach();
					}
				}
				_started = false;
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}
		}

		private void LoadDevices()
		{
			RGBDeviceType deviceMask = RGBDeviceType.None;
			if (_config.Model.rgbDeviceSettings.useKeyboards)
				deviceMask |= RGBDeviceType.Keyboard;
			if (_config.Model.rgbDeviceSettings.useMice)
				deviceMask |= RGBDeviceType.Mouse;
			if(_config.Model.rgbDeviceSettings.useMotherboard)
				deviceMask |= RGBDeviceType.Mainboard;
			Console.WriteLine("Loading rgb devices...");
			_surface.LoadDevices(CorsairDeviceProvider.Instance, deviceMask, throwExceptions: true);
			_surface.LoadDevices(RazerDeviceProvider.Instance, deviceMask, throwExceptions: true);
			_surface.LoadDevices(LogitechDeviceProvider.Instance, deviceMask, throwExceptions: true);
			_surface.LoadDevices(AsusDeviceProvider.Instance, deviceMask, throwExceptions: true);
			_surface.AlignDevices();

			foreach (var device in _surface.Devices)
			{
				var group = new ListLedGroup(device)
				{
					Brush = new SolidColorBrush(RGBConsts.Black)
				};
				_surface.Update();
				group.Detach();
			}
		}

		public void UpdateFromImage(System.Drawing.Color averageColor, MagickImage image)
		{
			try
			{
				if ((DateTime.UtcNow - _lastChangeTime).TotalMilliseconds < _frameTimeSpan.TotalMilliseconds)
					return;

				var start = DateTime.UtcNow;
				var red = _config.Model.rgbDeviceSettings.keyboardResReduce;
				unsafe
				{
					int* colors = stackalloc int[] { 0, 0, 0 };
					//byte[] bytes;
					foreach (var device in _surface.Devices.Where(x => x.DeviceInfo.DeviceType == RGBDeviceType.Keyboard))
					{
						imageByteStream.Seek(0, SeekOrigin.Begin);
						//I am sampling the image by half the given dimensions because the rgb.net layouts width/height are not physical key dimensions and I dont need the extra accuracy here.
						// It is better to reduce the footprint created by doing this to try and help the gc.
						using var resizeImage = ImageHandler.ResizeImage(image, (int)device.DeviceRectangle.Size.Width / red, (int)device.DeviceRectangle.Size.Height / red);
						resizeImage.Write(imageByteStream);

						int count = 0;
						var sampleX = 0;
						var sampleY = 0;
						var pixIndex = 0;
						var stride = resizeImage.Width * 3;
						var r = 0;
						var g = 0;
						var b = 0;
						//Get the colors for the whole size of the key so we can average the whole key instead of just sampling its center.
						foreach (var key in device)
						{
							colors[0] = 0;
							colors[1] = 0;
							colors[2] = 0;
							count = 0;
							for (var y = 0; y < key.LedRectangle.Size.Height / red; ++y)
							{
								for (var x = 0; x < key.LedRectangle.Size.Width / red; ++x)
								{
									sampleX = (int)Math.Floor(key.LedRectangle.Location.X / red + x);
									sampleY = (int)Math.Floor(key.LedRectangle.Location.Y / red + y);
									pixIndex = (sampleY * stride) + sampleX * 3;
									imageByteStream.Seek(pixIndex, SeekOrigin.Begin);
									colors[0] += imageByteStream.ReadByte();
									colors[1] += imageByteStream.ReadByte();
									colors[2] += imageByteStream.ReadByte();
									count++;
								}
							}

							r = (int)Math.Floor((colors[0] / count) * _config.Model.rgbDeviceSettings.colorMultiplier);
							g = (int)Math.Floor((colors[1] / count) * _config.Model.rgbDeviceSettings.colorMultiplier);
							b = (int)Math.Floor((colors[2] / count) * _config.Model.rgbDeviceSettings.colorMultiplier);
							var lastColorR = key.Color.R * 255;
							var lastColorG = key.Color.G * 255;
							var lastColorB = key.Color.B * 255;
							//Only set the key color if it has changed enough.
							//This is to hopefully slow down the amount of allocations needed for the RGB.Net Color.
							if (!(lastColorR >= r - _colorChangeThreshold && lastColorR <= r + _colorChangeThreshold &&
								lastColorG >= g - _colorChangeThreshold && lastColorG <= g + _colorChangeThreshold &&
								lastColorB >= b - _colorChangeThreshold && lastColorB <= b + _colorChangeThreshold))
							{
								key.Color = new Color(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
							}
						}
					}
				}
				foreach (var device in _surface.Devices.Where(x => x.DeviceInfo.DeviceType == RGBDeviceType.Mouse || x.DeviceInfo.DeviceType == RGBDeviceType.Mainboard))
				{
					var color = new Color(averageColor.R, averageColor.G, averageColor.B);
					foreach (var led in device)
					{
						led.Color = color;
					}
				}
				//Console.WriteLine($"Keyboard calc time: {(DateTime.UtcNow - start).TotalMilliseconds}");

				_surface.Update();
				_lastChangeTime = DateTime.UtcNow;
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}
		}

		//private void SetupKeyboardDevices()
		//{
		//	try
		//	{
		//		foreach (var device in _surface.Devices.Where(x => x.DeviceInfo.DeviceType == RGBDeviceType.Keyboard))
		//		{
		//			var listLedGroup = new ListLedGroup(device)
		//			{
		//				Brush = new SolidColorBrush(RGBConsts.Black)
		//			};
		//			listLedGroup.Brush.BrushCalculationMode = BrushCalculationMode.Relative;
		//		}
		//	}
		//	catch (Exception ex)
		//	{
		//		_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
		//	}
		//}

		private void Surface_Exception(ExceptionEventArgs args)
		{
			_ = Task.Run(() => _logger?.WriteLog(args.Exception?.ToString()));
		}

		public void Dispose()
		{
			foreach (var device in _surface?.Devices)
			{
				try
				{
					device.Dispose();
				}
				catch (Exception ex)
				{
					_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
				}
			}

			imageByteStream?.Dispose();
			imageByteStream = null;
			_surface.Exception -= Surface_Exception;
			_surface?.Dispose();
			_started = false;
			GC.SuppressFinalize(this);
		}
	}
}
