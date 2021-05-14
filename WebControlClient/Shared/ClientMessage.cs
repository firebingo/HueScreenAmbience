namespace WebControlClient.Client.Shared
{
	public enum ClientMessageType
	{
		Ping = 0,
		StartReader = 1,
		StopReader = 2,
		GetSAState = 3,
		GetLSCState = 4
	}

	public class ClientMessage
	{
		public ClientMessageType ActionType { get; set; }
	}
}
