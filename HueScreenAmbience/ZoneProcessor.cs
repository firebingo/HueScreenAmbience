﻿using BitmapZoneProcessor;
using HueScreenAmbience.Hue;
using HueScreenAmbience.LightStrip;
using HueScreenAmbience.NanoLeaf;
using HueScreenAmbience.RGB;
using LightsShared;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HueScreenAmbience
{
	public class ZoneProcessor
	{
		private MemoryStream _smallImageMemStream;
		private MemoryStream _blurImageMemStream;
		private MemoryStream _hueImageMemStream;
		private MemoryStream _lstripImageMemStream;
		private MemoryStream _rgbImageMemStream;
		private MemoryStream _nanoLeafImageMemStream;
		private Config _config;
		private HueCore _hueClient;
		private FileLogger _logger;
		private RGBLighter _rgbLighter;
		private StripLighter _stripLighter;
		private NanoLeafClient _nanoLeafClient;

		public void InstallServices(IServiceProvider _map)
		{
			_config = _map.GetService(typeof(Config)) as Config;
			_hueClient = _map.GetService(typeof(HueCore)) as HueCore;
			_logger = _map.GetService(typeof(FileLogger)) as FileLogger;
			if (_config.Model.RgbDeviceSettings.UseDevices)
				_rgbLighter = _map.GetService(typeof(RGBLighter)) as RGBLighter;
			_stripLighter = _map.GetService(typeof(StripLighter)) as StripLighter;
			_nanoLeafClient = _map.GetService(typeof(NanoLeafClient)) as NanoLeafClient;
		}

		public async Task PostRead(PixelZone[] zones, PixelZonesTotals zoneTotals, long frame)
		{
			(MemoryStream image, MemoryStream blurImage) images = (null, null);
			var columns = zones.OrderByDescending(x => x.Column).First().Column + 1;
			var rows = zones.OrderByDescending(x => x.Row).First().Row + 1;

			//Pre allocate the memory stream for images since it will be the same size every time
			_smallImageMemStream ??= new MemoryStream(columns * rows * 3);

			var newWidth = (int)Math.Floor(columns * _config.Model.ZoneProcessSettings.ResizeScale);
			var newHeight = (int)Math.Floor(rows * _config.Model.ZoneProcessSettings.ResizeScale);

			_blurImageMemStream ??= new MemoryStream(newWidth * newHeight * 3);

			try
			{
				var start = DateTime.UtcNow;
				var time = start;

				zoneTotals.CalculateAverages();

				//This is for debug purpose so I can just dump out the zones as pixels
				//using (var smallImage = ImageHandler.CreateSmallImageFromZones(zones, columns, rows))
				//{
				//	var path = Path.Combine(_config.Model.imageDumpLocation, $"{frame.ToString().PadLeft(6, '0')}_small.png");
				//	using var writeStream = File.OpenWrite(path);
				//	smallImage.Write(writeStream, MagickFormat.Png);
				//}

				time = DateTime.UtcNow;

				images = BitmapProcessor.PreparePostBitmap(zones, columns, rows, newWidth, newHeight, _config.Model.ZoneProcessSettings.ResizeFilter, _config.Model.ZoneProcessSettings.ResizeSigma, _smallImageMemStream, _blurImageMemStream);

				//Console.WriteLine($"PreparePostBitmap Time: {(DateTime.UtcNow - time).TotalMilliseconds}");

				if (images.image == null)
				{
					Console.WriteLine($"f:{frame} Image is null. Check log");
					return;
				}

				Rgb24 avgColor;
				unsafe
				{
					int totalR = 0;
					int totalG = 0;
					int totalB = 0;
					for (var i = 0; i < zones.Length; ++i)
					{
						totalR += zoneTotals.AvgR[i];
						totalG += zoneTotals.AvgG[i];
						totalB += zoneTotals.AvgB[i];
					}
					avgColor = new Rgb24((byte)Math.Clamp(totalR / zones.Length, 0, 255), (byte)Math.Clamp(totalG / zones.Length, 0, 255), (byte)Math.Clamp(totalB / zones.Length, 0, 255));
				}

				time = DateTime.UtcNow;

				var processingTasks = new List<Task>();

				if (_config.Model.HueSettings.UseHue)
				{
					if (_config.Model.HueSettings.HueType == HueType.Basic)
					{
						processingTasks.Add(Task.Run(() => _hueClient.ChangeLightColorBasic(avgColor)));
					}
					else if (_config.Model.HueSettings.HueType == HueType.Entertainment)
					{
						_hueImageMemStream ??= new MemoryStream(newWidth * newHeight * 3);
						_hueImageMemStream.Seek(0, SeekOrigin.Begin);
						_blurImageMemStream.Seek(0, SeekOrigin.Begin);
						await _blurImageMemStream.CopyToAsync(_hueImageMemStream);
						processingTasks.Add(Task.Run(() =>
						{
							_hueClient.UpdateEntertainmentGroupFromImage(_hueImageMemStream, newWidth, newHeight);
						}));
					}
				}
				if (_config.Model.LightStripSettings.UseLightStrip)
				{
					_lstripImageMemStream ??= new MemoryStream(newWidth * newHeight * 3);
					_lstripImageMemStream.Seek(0, SeekOrigin.Begin);
					_blurImageMemStream.Seek(0, SeekOrigin.Begin);
					await _blurImageMemStream.CopyToAsync(_lstripImageMemStream);
					processingTasks.Add(Task.Run(() =>
					{
						_stripLighter.UpdateFromImage(_lstripImageMemStream, newWidth, newHeight, frame);
					}));
				}
				if (_config.Model.NanoLeafSettings.UseNanoLeaf)
				{
					_nanoLeafImageMemStream ??= new MemoryStream(newWidth * newHeight * 3);
					_nanoLeafImageMemStream.Seek(0, SeekOrigin.Begin);
					_blurImageMemStream.Seek(0, SeekOrigin.Begin);
					await _blurImageMemStream.CopyToAsync(_nanoLeafImageMemStream);
					processingTasks.Add(Task.Run(async () =>
					{
						await _nanoLeafClient.UpdateFromImage(_nanoLeafImageMemStream, newWidth, newHeight);
					}));
				}

				//Console.WriteLine($"PostRead ChangeLightColor Time: {(DateTime.UtcNow - time).TotalMilliseconds}");

				if (_config.Model.RgbDeviceSettings.UseDevices)
				{
					_rgbImageMemStream ??= new MemoryStream(newWidth * newHeight * 3);
					_rgbImageMemStream.Seek(0, SeekOrigin.Begin);
					_blurImageMemStream.Seek(0, SeekOrigin.Begin);
					await _blurImageMemStream.CopyToAsync(_rgbImageMemStream);
					processingTasks.Add(Task.Run(() =>
					{
						_rgbLighter.UpdateFromImage(avgColor, _rgbImageMemStream, newWidth, newHeight);
					}));
				}

				if (_config.Model.DumpPngs)
				{
					try
					{
						time = DateTime.UtcNow;
						var path = Path.Combine(_config.Model.ImageDumpLocation, $"{frame.ToString().PadLeft(6, '0')}.png");
						//_ = ImageHandler.WriteImageToFile(images.image, columns, rows, path, width, height);
						_ = ImageHandler.WriteImageToFile(images.blurImage, newWidth, newHeight, path, pixelFormat: PixelFormat.Rgb24);
						//Console.WriteLine($"PostRead writeStream Time: {(DateTime.UtcNow - time).TotalMilliseconds}");
					}
					catch (Exception ex)
					{
						_ = Task.Run(() => _logger.WriteLog(ex.ToString()));
					}
				}

				await Task.WhenAll(processingTasks);

				//Console.WriteLine($"PostRead Total Time: {(DateTime.UtcNow - start).TotalMilliseconds}");
			}
			catch (Exception ex)
			{
				_ = Task.Run(() => _logger.WriteLog(ex.ToString()));
			}
			finally
			{
				_blurImageMemStream?.Seek(0, SeekOrigin.Begin);
				_smallImageMemStream?.Seek(0, SeekOrigin.Begin);
				_hueImageMemStream?.Seek(0, SeekOrigin.Begin);
				_rgbImageMemStream?.Seek(0, SeekOrigin.Begin);
				_lstripImageMemStream?.Seek(0, SeekOrigin.Begin);
				_nanoLeafImageMemStream?.Seek(0, SeekOrigin.Begin);
			}
		}
	}
}
