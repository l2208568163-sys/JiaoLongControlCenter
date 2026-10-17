namespace BLD.WMIOperation;

public enum WMICPUPower : byte
{
	CloseState = 0,
	OpenState = 1,
	SPLState = 2,
	SPPTState = 3,
	CPUTempWallState = 4,
	Unknow = byte.MaxValue
}
