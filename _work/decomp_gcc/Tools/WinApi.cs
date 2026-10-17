using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace Tools;

public class WinApi
{
	public delegate void WinEventDelegate(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

	public const int WM_HOTKEY = 786;

	public const int MOD_NOREPEAT = 16384;

	public const uint WINEVENT_OUTOFCONTEXT = 0u;

	public const uint EVENT_SYSTEM_FOREGROUND = 3u;

	public const int HWND_BROADCAST = 65535;

	public static readonly int WM_SHOW_LS = RegisterWindowMessage("WM_SHOW_LS");

	[DllImport("user32")]
	public static extern bool PostMessage(nint hwnd, int msg, nint wparam, nint lparam);

	[DllImport("user32")]
	public static extern int RegisterWindowMessage(string message);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool RegisterHotKey(nint hWnd, int id, ModifierKeys fsModifiers, int vk);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool UnregisterHotKey(nint hWnd, int id);

	[DllImport("user32.dll")]
	public static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

	[DllImport("user32.dll")]
	public static extern bool UnhookWinEvent(nint hWinEventHook);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern int GetWindowText(nint hwnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern int GetClassName(nint hwnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern int GetWindowTextLength(nint hwnd);

	public static string GetWindowTitle(nint hwnd)
	{
		int num = GetWindowTextLength(hwnd) + 1;
		StringBuilder stringBuilder = new StringBuilder(num);
		GetWindowText(hwnd, stringBuilder, num);
		return stringBuilder.ToString();
	}

	public static string GetWindowClass(nint hwnd)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		GetClassName(hwnd, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}
}
