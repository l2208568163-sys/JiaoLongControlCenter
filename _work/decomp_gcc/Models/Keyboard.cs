using System;
using BLD.WMIOperation;
using GamingControlCenter;

namespace Models;

public class Keyboard
{
	public static float[] GetRGBKeyboardColor()
	{
		Tuple<int, int, int> value = WMIMethodServices.GetValue<Tuple<int, int, int>>(WMIMethodName.RGBKeyboardColor);
		return ColorConversionHelper.RGB2HSB(new float[3] { value.Item1, value.Item2, value.Item3 });
	}

	public static void SetRGBKeyboardColor(byte red, byte green, byte blue)
	{
		WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardMode, WMIRGBKeyboardMode.Mode_RGBFixedMode);
		WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardColor, new byte[3] { red, green, blue });
	}

	public static void SetkeyboardLightBrightness(byte b)
	{
		if (b > 4)
		{
			WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardBrightness, 4);
		}
		else if (b < 1)
		{
			WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardBrightness, 0);
		}
		else
		{
			WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardBrightness, b);
		}
	}
}
