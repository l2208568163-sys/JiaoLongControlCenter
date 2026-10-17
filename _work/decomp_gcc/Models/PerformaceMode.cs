using BLD.WMIOperation;

namespace Models;

public class PerformaceMode
{
	public static void SetPerformaceMode(WMISystemPerMode mode)
	{
		WMIMethodServices.SetValue(WMIMethodName.SystemPerMode, mode);
	}

	public static void CLISetPerformaceMode(byte b)
	{
		switch (b)
		{
		case 0:
			SetPerformaceMode(WMISystemPerMode.BalanceMode);
			break;
		case 1:
			SetPerformaceMode(WMISystemPerMode.PerformanceMode);
			break;
		case 2:
			SetPerformaceMode(WMISystemPerMode.QuietMode);
			break;
		default:
			SetPerformaceMode(WMISystemPerMode.Unknow);
			break;
		}
	}
}
