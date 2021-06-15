namespace WebControlClient.Client.Shared
{
	public enum ClientResponseType
	{
		None = 0,
		Pong = 1,
		SAData = 2,
		LSCData = 3
	}

	public class ClientResponse
	{
		public bool Success { get; set; }
		public string Message { get; set; }
		public ClientResponseType Type { get; set; }
	}

	public class ClientResponse<T> : ClientResponse
	{
		public T Data { get; set; }
	}
}
