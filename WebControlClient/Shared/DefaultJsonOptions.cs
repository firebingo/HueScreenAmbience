using System.Text.Json;

namespace WebControlClient.Shared
{
	public static class DefaultJsonOptions
	{
		public static JsonSerializerOptions JsonOptions { get; }
		static DefaultJsonOptions()
		{
			JsonOptions = new JsonSerializerOptions()
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			};
		}
	}
}
