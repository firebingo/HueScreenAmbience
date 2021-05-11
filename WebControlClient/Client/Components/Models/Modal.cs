using System;

namespace WebControlClient.Client.Components
{
	public enum ModalType
	{
		None = 0,
		Info = 1,
		Warning = 2,
		Error = 3
	}

	public class ModalButton
	{
		public string Text { get; set; }
		public Action OnClick { get; set; }
	}
}
