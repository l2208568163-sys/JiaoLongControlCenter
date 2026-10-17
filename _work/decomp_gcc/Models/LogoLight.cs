using BLD.WMIOperation;

namespace Models;

public class LogoLight
{
	public static void SetLogoLight(WMIResultState m)
	{
		WMIMethodServices.SetValue(WMIMethodName.Ambientlight, m);
	}

	public static void CLISetLogoLight(byte b)
	{
		switch (b)
		{
		case 1:
			WMIMethodServices.SetValue(WMIMethodName.Ambientlight, WMIResultState.ON);
			break;
		case 0:
			WMIMethodServices.SetValue(WMIMethodName.Ambientlight, WMIResultState.OFF);
			break;
		default:
			WMIMethodServices.SetValue(WMIMethodName.Ambientlight, WMIResultState.Unknow);
			break;
		}
	}
}
