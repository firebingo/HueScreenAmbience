using System.Text.Json;

namespace WebControlClient.Shared
{
	public static class DefaultJsonOptions
	{
		static DefaultJsonOptions()
		{
			JsonOptions = new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			};
		}

		public static JsonSerializerOptions JsonOptions { get; }
	}
}