namespace BLD.WMIOperation;

public enum WMISystemPerMode : byte
{
	BalanceMode = 0,
	PerformanceMode = 1,
	QuietMode = 2,
	Unknow = byte.MaxValue
}
