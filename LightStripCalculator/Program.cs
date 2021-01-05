using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.IO;
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

				var rect = Rectangle.FromLTRB(0, 0, bottomRight.Value.X, bottomRight.Value.Y);
				Dictionary<int, LightCoordF> dict = new Dictionary<int, LightCoordF>();

				//This circles around the edge of the rectangle counting the start light as 0,0 then filling the coordinates for each light until we hit the light count.
				// Then it goes back to light 0 and keeps filling.
				var lightIter = lightStart;
				//Top
				for (var x = 0; x < rect.Width; x++)
				{
					dict.Add(lightIter, new LightCoordF(x / (float)rect.Width, 0.0f));
					//doSomethingWith(x, 0)
					if(++lightIter > lightCount)
						lightIter = 0;
				}
				//Right
				for (var y = 0; y < rect.Height; y++)
				{
					dict.Add(lightIter, new LightCoordF(1.0f, y / (float)rect.Height));
					//doSomethingWith(0, y)
					if (++lightIter > lightCount)
						lightIter = 0;
				}
				//Bottom
				for (var x = rect.Width; x > 0; x--)
				{
					dict.Add(lightIter, new LightCoordF(x / (float)rect.Width, 1.0f));
					//doSomethingWith(x, rect.height)
					if (++lightIter > lightCount)
						lightIter = 0;
				}
				//Left
				for (var y = rect.Height; y > 0; y--)
				{
					dict.Add(lightIter, new LightCoordF(0.0f, y / (float)rect.Height));
					//doSomethingWith(rect.width, y)
					if (++lightIter > lightCount)
						lightIter = 0;
				}

				using var fileOut = File.OpenWrite("lightlist.json");
				var list = dict.OrderBy(x => x.Key).Select(x => x.Value).ToList();
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

	public struct LightCoord
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

	public struct LightCoordF
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
