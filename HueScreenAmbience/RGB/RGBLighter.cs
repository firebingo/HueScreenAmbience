using RGB.NET.Core;
using RGB.NET.Brushes;
using RGB.NET.Devices.Corsair;
using System;
using System.Threading.Tasks;
using RGB.NET.Groups;
using ImageMagick;
using System.Linq;
using RGB.NET.Devices.Razer;
using RGB.NET.Devices.Logitech;
using HueScreenAmbience.Imaging;
using System.Buffers;
using System.Collections.Generic;

namespace HueScreenAmbience.RGB
{
	public class RGBLighter : IDisposable
	{
		private readonly RGBSurface _surface;
		private readonly FileLogger _logger;
		private readonly Config _config;
		private readonly ImageHandler _imageHandler;
		private bool _started = false;
		private List<byte[]> byteBuffer;

		public RGBLighter(FileLogger logger, Config config, ImageHandler imageHandler)
		{
			_logger = logger;
			_config = config;
			_imageHandler = imageHandler;
			_surface = RGBSurface.Instance;
			_surface.Exception += Surface_Exception;
		}

		public void Start()
		{
			try
			{
				if (!_started)
				{
					byteBuffer?.Clear();
					byteBuffer = new List<byte[]>();
					LoadDevices();
					_started = true;
				}
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger?.WriteLog(ex?.ToString()));
			}
		}

		public void Stop()
		{
			//This is the best I can do because rgb.net doesn't let me fully release the surface and let it be able to be created again.
			foreach (var device in _surface.Devices)
			{
				var group = new ListLedGroup(device)
				{
					Brush = new SolidColorBrush(RGBConsts.White)
				};
				_surface.Update();
				group.Detach();
			}
			_started = false;
		}

		private void LoadDevices()
		{
			RGBDeviceType deviceMask = RGBDeviceType.None;
			if(_config.Model.rgbDeviceSettings.useKeyboards)
				deviceMask |= RGBDeviceType.Keyboard;
			if (_config.Model.rgbDeviceSettings.useMice)
				deviceMask |= RGBDeviceType.Mouse;
			_surface.LoadDevices(CorsairDeviceProvider.Instance, deviceMask, throwExceptions: true);
			_surface.LoadDevices(RazerDeviceProvider.Instance, deviceMask, throwExceptions: true);
			_surface.LoadDevices(LogitechDeviceProvider.Instance, deviceMask, throwExceptions: true);
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

		public void UpdateFromImage(System.Drawing.Color averageColor, MagickImage image, long frame)
		{
			try
			{
				var start = DateTime.UtcNow;
				foreach (var device in _surface.Devices.Where(x => x.DeviceInfo.DeviceType == RGBDeviceType.Keyboard))
				{
					using var resizeImage = _imageHandler.ResizeImage(image, (int)device.DeviceRectangle.Size.Width, (int)device.DeviceRectangle.Size.Height);
					unsafe
					{
						//I hate how much this makes the GC work but its faster than getting/reading the pixels and there isint really an alternative.
						var bytes = resizeImage.ToByteArray(MagickFormat.Rgb);
						byteBuffer.Add(bytes);
						int* colors = stackalloc int[] { 0, 0, 0 };
						int count = 0;
						var sampleX = 0;
						var sampleY = 0;
						var pixIndex = 0;
						var stride = resizeImage.Width * 3;
						//Get the colors for the whole size of the key so we can average the whole key instead of just sampling its center.
						foreach (var key in device)
						{
							colors[0] = 0;
							colors[1] = 0;
							colors[2] = 0;
							count = 0;
							for (var y = 0; y < key.LedRectangle.Size.Height; ++y)
							{
								for (var x = 0; x < key.LedRectangle.Size.Width; ++x)
								{
									sampleX = (int)Math.Floor(key.LedRectangle.Location.X + x);
									sampleY = (int)Math.Floor(key.LedRectangle.Location.Y + y);
									pixIndex = (sampleY * stride) + sampleX * 3;
									colors[0] += bytes[pixIndex];
									colors[1] += bytes[pixIndex + 1];
									colors[2] += bytes[pixIndex + 2];
									count++;
								}
							}

							var r = (int)Math.Floor((colors[0] / count) * _config.Model.rgbDeviceSettings.colorMultiplier);
							var g = (int)Math.Floor((colors[1] / count) * _config.Model.rgbDeviceSettings.colorMultiplier);
							var b = (int)Math.Floor((colors[2] / count) * _config.Model.rgbDeviceSettings.colorMultiplier);
							key.Color = new Color(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
						}
					}
				}
				foreach(var device in _surface.Devices.Where(x => x.DeviceInfo.DeviceType == RGBDeviceType.Mouse))
				{
					var color = new Color(averageColor.R, averageColor.G, averageColor.B);
					foreach(var led in device)
					{
						led.Color = color;
					}
				}
				//Console.WriteLine($"Keyboard calc time: {(DateTime.UtcNow - start).TotalMilliseconds}");

				_surface.Update();

				//Im keeping a list of byte[] so that I can control the amount of gc that needs to happen from constantly converting images to byte buffers.
				// Id rather keep the little extra memory overhead than doing gen 2 gcs constantly.
				if (frame % 120 == 0)
				{
					byteBuffer.Clear();
					GC.Collect();
				}
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

			_surface.Exception -= Surface_Exception;
			_surface?.Dispose();
			byteBuffer?.Clear();
			byteBuffer = null;
			_started = false;
		}
	}
}
