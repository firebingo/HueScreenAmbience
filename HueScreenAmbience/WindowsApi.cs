#if ANYCPU
#else
using System.Runtime.InteropServices;

namespace HueScreenAmbience
{
	public static class WindowsApi
	{

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

		public enum EXECUTION_STATE : uint
		{
			ES_AWAYMODE_REQUIRED = 0x00000040,
			ES_CONTINUOUS = 0x80000000,
			ES_DISPLAY_REQUIRED = 0x00000002,
			ES_SYSTEM_REQUIRED = 0x00000001,
			ES_USER_PRESENT = 0x00000004
		}

		[DllImport("user32.dll", SetLastError = true)]
		internal static extern bool SetProcessDpiAwarenessContext(int dpiFlag);
		[DllImport("SHCore.dll", SetLastError = true)]
		internal static extern bool SetProcessDpiAwareness(PROCESS_DPI_AWARENESS awareness);

		internal enum PROCESS_DPI_AWARENESS
		{
			Process_DPI_Unaware = 0,
			Process_System_DPI_Aware = 1,
			Process_Per_Monitor_DPI_Aware = 2
		}

		internal enum DPI_AWARENESS_CONTEXT
		{
			DPI_AWARENESS_CONTEXT_UNAWARE = 16,
			DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = 17,
			DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = 18,
			DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = 34
		}
	}
}
#endif
