using System;
using System.Runtime.InteropServices;

namespace Tools;

internal class LSCore
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void StatusListenerDelegate(int status, nint hwnd, int inputWidth, int inputHeight, int outputWidth, int outputHeight, [MarshalAs(UnmanagedType.I1)] bool resized, float scaleFactor, int errorCode);

	public enum ErrorCode
	{
		NO_CODE,
		RESIZE_FAILED,
		LARGE_OFFSET,
		CAPTURE_FAILED
	}

	internal static readonly char[] separator = new char[1] { ';' };

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern nint GetForegroundWindowEx();

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.BStr)]
	private static extern string GetAdapterNames();

	public static string[] GetAdapters()
	{
		return GetAdapterNames().Split(separator, StringSplitOptions.RemoveEmptyEntries);
	}

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.BStr)]
	private static extern string GetDisplayNames();

	public static string[] GetDisplays()
	{
		return GetDisplayNames().Split(separator, StringSplitOptions.RemoveEmptyEntries);
	}

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool SetDriverSettings();

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool IsWindowsBuildAtLeast(uint build);

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern float GetDwmRefreshRate();

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool SetWindowsSettings(int gpuPreference);

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool Init([MarshalAs(UnmanagedType.FunctionPtr)] StatusListenerDelegate callback);

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void UnInit();

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool Activate(nint hwnd);

	[DllImport("Lossless.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void ApplySettings(int scalingMode, int scalingFitMode, int scalingType, int scalingSubtype, float scaleFactor, [MarshalAs(UnmanagedType.I1)] bool resizeBeforeScale, int sharpness, [MarshalAs(UnmanagedType.I1)] bool VRS, int frameGenerationType, int frameGenerationSubtype, int frameGenerationMode, float frameGenerationMultiplier, float frameGenerationTarget, int frameGenerationFlowScale, [MarshalAs(UnmanagedType.I1)] bool clipCursor, [MarshalAs(UnmanagedType.I1)] bool cursorSensitivity, [MarshalAs(UnmanagedType.I1)] bool hideCursor, [MarshalAs(UnmanagedType.I1)] bool scaleCursor, int syncMode, int maxFrameLatency, [MarshalAs(UnmanagedType.I1)] bool gsyncSupport, [MarshalAs(UnmanagedType.I1)] bool hdrSupport, int captureApi, int queueTarget, [MarshalAs(UnmanagedType.I1)] bool drawFps, int gpuId, int displayId, int captureOffsetLeft, int captureOffsetTop, int captureOffsetRight, int captureOffsetBottom, [MarshalAs(UnmanagedType.I1)] bool multiDisplayMode, int setupPhase);
}
