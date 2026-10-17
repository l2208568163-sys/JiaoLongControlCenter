using System;

namespace GamingControlCenter;

public class ColorConversionHelper
{
	public static float[] HSB2RGB(float[] hsb)
	{
		if (hsb[0] == 360f)
		{
			hsb[0] = 0f;
		}
		_ = new float[3];
		float num = 0f;
		float num2 = 0f;
		float num3 = 0f;
		if (hsb[1] == 0f)
		{
			num = (num2 = (num3 = hsb[2]));
		}
		else
		{
			float num4 = hsb[0] / 60f;
			int num5 = (int)Math.Floor(num4);
			float num6 = num4 - (float)num5;
			float num7 = hsb[2] * (1f - hsb[1]);
			float num8 = hsb[2] * (1f - hsb[1] * num6);
			float num9 = hsb[2] * (1f - hsb[1] * (1f - num6));
			switch (num5)
			{
			case 0:
				num = hsb[2];
				num2 = num9;
				num3 = num7;
				break;
			case 1:
				num = num8;
				num2 = hsb[2];
				num3 = num7;
				break;
			case 2:
				num = num7;
				num2 = hsb[2];
				num3 = num9;
				break;
			case 3:
				num = num7;
				num2 = num8;
				num3 = hsb[2];
				break;
			case 4:
				num = num9;
				num2 = num7;
				num3 = hsb[2];
				break;
			case 5:
				num = hsb[2];
				num2 = num7;
				num3 = num8;
				break;
			}
		}
		return new float[3]
		{
			num * 255f,
			num2 * 255f,
			num3 * 255f
		};
	}

	public static float[] RGB2HSB(float[] rgb)
	{
		float[] array = new float[3];
		float num = rgb[0];
		float num2 = rgb[1];
		float num3 = rgb[2];
		float num4 = Math.Max(num, Math.Max(num2, num3));
		if (num4 <= 0f)
		{
			return array;
		}
		float num5 = Math.Min(num, Math.Min(num2, num3));
		float num6 = num4 - num5;
		if (num4 > num5)
		{
			if (num2 == num4)
			{
				array[0] = (num3 - num) / num6 * 60f + 120f;
			}
			else if (num3 == num4)
			{
				array[0] = (num - num2) / num6 * 60f + 240f;
			}
			else if (num3 > num2)
			{
				array[0] = (num2 - num3) / num6 * 60f + 360f;
			}
			else
			{
				array[0] = (num2 - num3) / num6 * 60f;
			}
			if (array[0] < 0f)
			{
				array[0] += 360f;
			}
		}
		else
		{
			array[0] = 0f;
		}
		array[1] = num6 / num4;
		array[2] = num4 / 255f;
		return array;
	}
}
