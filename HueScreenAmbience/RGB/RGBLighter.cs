﻿using RGB.NET.Core;
using RGB.NET.Brushes;
using RGB.NET.Groups;
using RGB.NET.Devices.Corsair;
using RGB.NET.Devices.Razer;
using RGB.NET.Devices.Logitech;
using RGB.NET.Devices.Asus;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using BitmapZoneProcessor;
using LightsShared;

namespace HueScreenAmbience.RGB
{
	public class RGBLighter : IDisposable
	{
		private readonly RGBSurface _surface;
		private FileLogger _logger;
		private Config _config;
		private MemoryStream _imageByteStream;
		private DateTime _lastChangeTime;
		private TimeSpan _frameTimeSpan;
		private bool _started = false;
		private byte _colorChangeThreshold = 5;
		private readonly int[] _colors;

		public RGBLighter()
		{
			_colors = new int[] { 0, 0, 0 };
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
			if (_config.Model.rgbDeviceSettings.useMotherboard)
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

		public void UpdateFromImage(SixLabors.ImageSharp.PixelFormats.Rgb24 averageColor, MemoryStream image, int width, int height)
		{
			try
			{
				if ((DateTime.UtcNow - _lastChangeTime).TotalMilliseconds < _frameTimeSpan.TotalMilliseconds)
					return;

				var start = DateTime.UtcNow;
				var red = _config.Model.rgbDeviceSettings.keyboardResReduce;

				foreach (var device in _surface.Devices.Where(x => x.DeviceInfo.DeviceType == RGBDeviceType.Keyboard))
				{

					//I am sampling the image by half the given dimensions because the rgb.net layouts width/height are not physical key dimensions and I dont need the extra accuracy here.
					// It is better to reduce the footprint created by doing this to try and help the gc.
					var newWidth = (int)Math.Floor(device.DeviceRectangle.Size.Width / red);
					var newHeight = (int)Math.Floor(device.DeviceRectangle.Size.Height / red);
					if (_imageByteStream == null)
						_imageByteStream = new MemoryStream(newWidth * newHeight * 3);

					_imageByteStream.Seek(0, SeekOrigin.Begin);
					var resizeImage = ImageHandler.ResizeImage(image, width, height, _imageByteStream, newWidth, newHeight, pixelFormat: PixelFormat.Rgb24);

					int count = 0;
					var sampleX = 0;
					var sampleY = 0;
					var pixIndex = 0;
					var stride = newWidth * 3;
					var r = 0;
					var g = 0;
					var b = 0;
					//Get the colors for the whole size of the key so we can average the whole key instead of just sampling its center.
					foreach (var key in device)
					{
						_colors[0] = 0;
						_colors[1] = 0;
						_colors[2] = 0;
						count = 0;
						for (var y = 0; y < key.LedRectangle.Size.Height / red; ++y)
						{
							for (var x = 0; x < key.LedRectangle.Size.Width / red; ++x)
							{
								sampleX = (int)Math.Floor(key.LedRectangle.Location.X / red + x);
								sampleY = (int)Math.Floor(key.LedRectangle.Location.Y / red + y);
								pixIndex = (sampleY * stride) + sampleX * 3;
								_imageByteStream.Seek(pixIndex, SeekOrigin.Begin);
								_colors[0] += _imageByteStream.ReadByte();
								_colors[1] += _imageByteStream.ReadByte();
								_colors[2] += _imageByteStream.ReadByte();
								count++;
							}
						}

						r = (int)Math.Floor((_colors[0] / count) * _config.Model.rgbDeviceSettings.colorMultiplier);
						g = (int)Math.Floor((_colors[1] / count) * _config.Model.rgbDeviceSettings.colorMultiplier);
						b = (int)Math.Floor((_colors[2] / count) * _config.Model.rgbDeviceSettings.colorMultiplier);
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

			_imageByteStream?.Dispose();
			_imageByteStream = null;
			_surface.Exception -= Surface_Exception;
			_surface?.Dispose();
			_started = false;
			GC.SuppressFinalize(this);
		}
	}
}
