using System;
using BLD.WMIOperation;

namespace Models;

public class CPUTempWall
{
	public static void SetCPUTempWall(byte inputCPUPower)
	{
		WMIMethodServices.SetValue(WMIMethodName.CPUPower, new byte[2] { 4, inputCPUPower });
	}

	public static float GetCPUTempWall()
	{
		return WMIMethodServices.GetValue<Tuple<int>>(WMIMethodName.SystemPerMode).Item1;
	}
}
