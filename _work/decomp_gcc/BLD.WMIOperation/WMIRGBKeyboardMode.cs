namespace BLD.WMIOperation;

public enum WMIRGBKeyboardMode : byte
{
	Mode_Off = 0,
	Mode_RGBAutoCyclic = 1,
	Mode_RGBFixedMode = 2,
	Mode_CustomColors = 3,
	Unknow = byte.MaxValue
}
