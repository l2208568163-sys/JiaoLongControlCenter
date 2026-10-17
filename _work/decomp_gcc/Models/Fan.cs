using BLD.WMIOperation;

namespace Models;

public class Fan
{
	public static void SwitchMaxFanSpeed(WMIResultState m)
	{
		WMIMethodServices.SetValue(WMIMethodName.MaxFanSpeedSwitch, m);
	}

	public static void SetFanSpeed(byte speed)
	{
		WMIMethodServices.SetValue(WMIMethodName.MaxFanSpeed, speed);
	}
}
