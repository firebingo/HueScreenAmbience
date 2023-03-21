using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LightStripCalculator
{
	class Program
	{
		static async Task Main(string[] args)
		{
			try
			{
				var builder = new ConfigurationBuilder();
				builder.AddCommandLine(args);
				var commandLine = builder.Build();

				//How many lights are on the strip.
				var lightCountS = commandLine["light_count"];
				if (string.IsNullOrWhiteSpace(lightCountS) || !int.TryParse(lightCountS, out var lightCount) || lightCount < 0)
				{
					Console.WriteLine("invalid light count");
					Console.ReadLine();
					return;
				}

				//The light in sequence that is at the top left of the rectangle.
				var lightStartS = commandLine["light_start"];
				if (string.IsNullOrWhiteSpace(lightStartS) || !int.TryParse(lightStartS, out var lightStart) || lightStart < 0 || lightStart > lightCount)
				{
					Console.WriteLine("invalid light start");
					Console.ReadLine();
					return;
				}

				//The bottom right coordinate of the lights.
				//This is to get the width and height so we can circle around the light strip to get all the lights coordinates.
				//(29,16.31) is 16:9 for 90 lights
				var bottomRightS = commandLine["bottom_right"];
				if (string.IsNullOrWhiteSpace(bottomRightS) || !LightCoord.TryParse(bottomRightS, out var bottomRight) || bottomRight == null)
				{
					Console.WriteLine("invalid bottom right coordinate");
					Console.ReadLine();
					return;
				}

				//Pushes in the coordinates of the lights by an amount so they are not sampling the very edge of the screen.
				var borderDecrease = 1.0f;
				var borderInS = commandLine["border_decrease"];
				if (!string.IsNullOrWhiteSpace(borderInS) && (!float.TryParse(borderInS, out borderDecrease) || borderDecrease <= 0.0f || borderDecrease > 1.0f))
				{
					Console.WriteLine("invalid border decrease");
					Console.ReadLine();
					return;
				}

				//Depending on which way the light strip is run the x coordinates may need to be flipped.
				var flipX = false;
				var flipXInS = commandLine["flipx"];
				if (!string.IsNullOrWhiteSpace(flipXInS) && (!bool.TryParse(flipXInS, out flipX)))
				{
					Console.WriteLine("invalid flip x");
					Console.ReadLine();
					return;
				}

				//Depending on which way the light strip is run the y coordinates may need to be flipped.
				var flipY = false;
				var flipYInS = commandLine["flipy"];
				if (!string.IsNullOrWhiteSpace(flipYInS) && (!bool.TryParse(flipYInS, out flipY)))
				{
					Console.WriteLine("invalid flip y");
					Console.ReadLine();
					return;
				}

				borderDecrease = (1.0f - borderDecrease) + 1.0f;

				var rect = Rectangle.FromLTRB(0, 0, bottomRight.Value.X, bottomRight.Value.Y);
				var bigRect = Rectangle.FromLTRB(0, 0, (int)Math.Floor(bottomRight.Value.X * borderDecrease), (int)Math.Floor(bottomRight.Value.Y * borderDecrease));
				var dictInt = new Dictionary<int, LightCoord>();
				var dictFloat = new Dictionary<int, LightCoordF>();

				//This circles around the edge of the rectangle counting the start light as 0,0 then filling the coordinates for each light until we hit the light count.
				// Then it goes back to light 0 and keeps filling.
				var lightIter = lightStart;
				//Top
				for (var x = 0; x < rect.Width; x++)
				{
					dictInt.Add(lightIter, new LightCoord(x, 0));
					//doSomethingWith(x, 0)
					if (++lightIter > lightCount - 1)
						lightIter = 0;
				}
				//Right
				for (var y = 0; y < rect.Height; y++)
				{
					dictInt.Add(lightIter, new LightCoord(rect.Width, y));
					//doSomethingWith(0, y)
					if (++lightIter > lightCount - 1)
						lightIter = 0;
				}
				//Bottom
				for (var x = rect.Width; x > 0; x--)
				{
					dictInt.Add(lightIter, new LightCoord(x, rect.Height));
					//doSomethingWith(x, smallRect.height)
					if (++lightIter > lightCount - 1)
						lightIter = 0;
				}
				//Left
				for (var y = rect.Height; y > 0; y--)
				{
					dictInt.Add(lightIter, new LightCoord(0, y));
					//doSomethingWith(smallRect.width, y)
					if (++lightIter > lightCount - 1)
						lightIter = 0;
				}

				//We need to center the reduced size rect in the full rect.
				var xDif = (bigRect.Width - rect.Width) / 2.0f;
				var yDif = (bigRect.Height - rect.Height) / 2.0f;
				for (var i = 0; i < lightCount; ++i)
				{
					float x = dictInt[i].X + xDif;
					float y = dictInt[i].Y + yDif;
					x /= bigRect.Width;
					y /= bigRect.Height;
					if (flipX)
						x = 1.0f - x;
					if (flipY)
						y = 1.0f - y;
					dictFloat.Add(i, new LightCoordF(x, y));
				}

				if (File.Exists("lightlist.json"))
					File.Delete("lightlist.json");
				using var fileOut = File.OpenWrite("lightlist.json");
				var list = dictFloat.OrderBy(x => x.Key).Select(x => x.Value).ToList();
				await JsonSerializer.SerializeAsync(fileOut, list, new JsonSerializerOptions()
				{
					WriteIndented = true
				});
				fileOut.Close();
				Console.WriteLine($"List written to {Path.GetFullPath("lightlist.json")}");
				Console.ReadLine();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				Console.ReadLine();
			}
		}
	}

	public readonly struct LightCoord
	{
		public readonly int X { get; }
		public readonly int Y { get; }

		public LightCoord(int x, int y)
		{
			X = x;
			Y = y;
		}

		public static bool TryParse(string ins, out LightCoord? coord)
		{
			coord = null;
			if (ins == null)
				return false;
			var nums = ins.Trim().TrimStart('(').TrimEnd(')').Split(',');
			if (nums == null || nums?.Length != 2)
				return false;

			if (!int.TryParse(nums[0], out var x) || !int.TryParse(nums[1], out var y))
				return false;

			coord = new LightCoord(x, y);

			return true;
		}
	}

	public readonly struct LightCoordF
	{
		public readonly float X { get; }
		public readonly float Y { get; }

		public LightCoordF(float x, float y)
		{
			X = x;
			Y = y;
		}
	}
}
