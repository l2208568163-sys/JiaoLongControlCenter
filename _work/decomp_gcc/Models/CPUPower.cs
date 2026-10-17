using BLD.WMIOperation;

namespace Models;

public class CPUPower
{
	public static void SetCpuShortPower(byte LongPower)
	{
		WMIMethodServices.SetValue(WMIMethodName.CPUPower, new byte[2] { 2, LongPower });
	}

	public static void SetCpuLongPower(byte ShortPower)
	{
		WMIMethodServices.SetValue(WMIMethodName.CPUPower, new byte[2] { 3, ShortPower });
	}
}
