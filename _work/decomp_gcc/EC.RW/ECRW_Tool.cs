using RyzenSmu;

namespace EC.RW;

internal class ECRW_Tool
{
	public static void Fan_Init()
	{
		SMUCommands.RyzenAccess.Write_EC_Init();
	}

	public static void Fan_Speed(byte speed)
	{
		SMUCommands.RyzenAccess.Write_EC_EX(51260, speed);
		SMUCommands.RyzenAccess.Write_EC_EX(51261, speed);
		byte b = SMUCommands.RyzenAccess.Read_EC_EX(2848);
		b |= 2;
		SMUCommands.RyzenAccess.Write_EC_EX(2848, b);
	}

	public static void Fan_Clear()
	{
		SMUCommands.RyzenAccess.Write_EC_EX(51260, 0);
		SMUCommands.RyzenAccess.Write_EC_EX(51261, 0);
		SMUCommands.RyzenAccess.Write_EC_EX(2848, 0);
	}
}
