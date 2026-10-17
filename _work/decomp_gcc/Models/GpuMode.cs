using BLD.WMIOperation;

namespace Models;

public class GpuMode
{
	public static void SetGpuMode(WMIGPUMode mode)
	{
		WMIMethodServices.SetValue(WMIMethodName.GPUMode, mode);
	}

	public static void CLISetGpuMode(byte b)
	{
		switch (b)
		{
		case 1:
			WMIMethodServices.SetValue(WMIMethodName.GPUMode, WMIResultState.ON);
			break;
		case 0:
			WMIMethodServices.SetValue(WMIMethodName.GPUMode, WMIResultState.OFF);
			break;
		default:
			WMIMethodServices.SetValue(WMIMethodName.GPUMode, WMIResultState.Unknow);
			break;
		}
	}
}
