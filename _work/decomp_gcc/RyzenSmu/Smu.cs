using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenLibSys;

namespace RyzenSmu;

internal class Smu
{
	public enum Status
	{
		BAD = 0,
		OK = 1,
		FAILED = 255,
		UNKNOWN_CMD = 254,
		CMD_REJECTED_PREREQ = 253,
		CMD_REJECTED_BUSY = 252
	}

	private static readonly Dictionary<Status, string> status = new Dictionary<Status, string>
	{
		{
			Status.BAD,
			"BAD"
		},
		{
			Status.OK,
			"OK"
		},
		{
			Status.FAILED,
			"Failed"
		},
		{
			Status.UNKNOWN_CMD,
			"Unknown Command"
		},
		{
			Status.CMD_REJECTED_PREREQ,
			"CMD Rejected Prereq"
		},
		{
			Status.CMD_REJECTED_BUSY,
			"CMD Rejected Busy"
		}
	};

	private Ols RyzenNbAccesss = new Ols();

	private static Mutex amdSmuMutex;

	private const ushort SMU_TIMEOUT = 8192;

	private byte EC_OBF = 1;

	private byte EC_IBF = 2;

	private byte EC_DATA = 98;

	private byte EC_SC = 102;

	private byte RD_EC = 128;

	private byte WR_EC = 129;

	private byte EC_ADDR_PORT = 78;

	private byte EC_DATA_PORT = 79;

	private byte EC_CHIP_ID1;

	private byte EC_CHIP_ID2;

	private byte EC_CHIP_Ver;

	public static uint SMU_PCI_ADDR { get; set; }

	public static uint SMU_OFFSET_ADDR { get; set; }

	public static uint SMU_OFFSET_DATA { get; set; }

	public static uint MP1_ADDR_MSG { get; set; }

	public static uint MP1_ADDR_RSP { get; set; }

	public static uint MP1_ADDR_ARG { get; set; }

	public static uint PSMU_ADDR_MSG { get; set; }

	public static uint PSMU_ADDR_RSP { get; set; }

	public static uint PSMU_ADDR_ARG { get; set; }

	public static uint[] args { get; set; }

	public bool ShowDebug { get; set; }

	public Smu(bool EnableDebug)
	{
		ShowDebug = EnableDebug;
		switch (RyzenNbAccesss.GetDllStatus())
		{
		case 0u:
			_ = ShowDebug;
			break;
		case 2u:
			throw new ApplicationException("WinRing OLS_DRIVER_NOT_LOADED");
		case 1u:
			throw new ApplicationException("WinRing OLS_UNSUPPORTED_PLATFORM");
		case 3u:
			throw new ApplicationException("WinRing OLS_DLL_DRIVER_NOT_FOUND");
		case 4u:
			throw new ApplicationException("WinRing OLS_DLL_DRIVER_UNLOADED");
		case 5u:
			throw new ApplicationException("WinRing DRIVER_NOT_LOADED_ON_NETWORK");
		case 9u:
			throw new ApplicationException("WinRing OLS_DLL_UNKNOWN_ERROR");
		case 6u:
		case 7u:
		case 8u:
			break;
		}
	}

	public void Initialize()
	{
		amdSmuMutex = new Mutex();
		RyzenNbAccesss.InitializeOls();
		switch (RyzenNbAccesss.GetStatus())
		{
		case 0u:
			if (ShowDebug)
			{
				ShowDebug = false;
			}
			break;
		case 1u:
			throw new ApplicationException("WinRing DLL_NOT_FOUND");
		case 2u:
			throw new ApplicationException("WinRing DLL_INCORRECT_VERSION");
		case 3u:
			throw new ApplicationException("WinRing DLL_INITIALIZE_ERROR");
		}
	}

	public void Deinitialize()
	{
		RyzenNbAccesss.DeinitializeOls();
	}

	public Status SendMp1(uint message, ref uint[] arguments)
	{
		return SendMsg(MP1_ADDR_MSG, MP1_ADDR_RSP, MP1_ADDR_ARG, message, ref arguments);
	}

	public Status SendRsmu(uint message, ref uint[] arguments)
	{
		return SendMsg(PSMU_ADDR_MSG, PSMU_ADDR_RSP, PSMU_ADDR_ARG, message, ref arguments);
	}

	public bool SendSmuCommand(uint SMU_ADDR_MSG, uint SMU_ADDR_RSP, uint SMU_ADDR_ARG, uint msg, ref uint[] args)
	{
		return SendMsg(SMU_ADDR_MSG, SMU_ADDR_RSP, SMU_ADDR_ARG, msg, ref args) == Status.OK;
	}

	public Status SendMsg(uint SMU_ADDR_MSG, uint SMU_ADDR_RSP, uint SMU_ADDR_ARG, uint msg, ref uint[] args)
	{
		ushort num = 8192;
		uint[] array = new uint[6];
		int num2 = args.Length;
		uint data = 0u;
		if (num2 > array.Length)
		{
			num2 = array.Length;
		}
		for (int i = 0; i < num2; i++)
		{
			array[i] = args[i];
		}
		if (amdSmuMutex.WaitOne(5000))
		{
			while (!SmuWriteReg(SMU_ADDR_RSP, 0u) && --num > 0)
			{
			}
			if (num == 0)
			{
				amdSmuMutex.ReleaseMutex();
				SmuReadReg(SMU_ADDR_RSP, ref data);
				return (Status)data;
			}
			for (int j = 0; j < array.Length; j++)
			{
				SmuWriteReg(SMU_ADDR_ARG + (uint)(j * 4), array[j]);
			}
			SmuWriteReg(SMU_ADDR_MSG, msg);
			if (!SmuWaitDone(SMU_ADDR_RSP))
			{
				amdSmuMutex.ReleaseMutex();
				SmuReadReg(SMU_ADDR_RSP, ref data);
				return (Status)data;
			}
			for (int k = 0; k < args.Length; k++)
			{
				SmuReadReg(SMU_ADDR_ARG + (uint)(k * 4), ref args[k]);
			}
		}
		amdSmuMutex.ReleaseMutex();
		SmuReadReg(SMU_ADDR_RSP, ref data);
		return (Status)data;
	}

	public bool SmuWaitDone(uint SMU_ADDR_RSP)
	{
		ushort num = 8192;
		uint data = 0u;
		bool flag;
		do
		{
			flag = SmuReadReg(SMU_ADDR_RSP, ref data);
		}
		while ((!flag || data != 1) && --num > 0);
		if (num == 0 || data != 1)
		{
			flag = false;
		}
		return flag;
	}

	private bool SmuWriteReg(uint addr, uint data)
	{
		if (RyzenNbAccesss.WritePciConfigDwordEx(SMU_PCI_ADDR, SMU_OFFSET_ADDR, addr) == 1)
		{
			return RyzenNbAccesss.WritePciConfigDwordEx(SMU_PCI_ADDR, SMU_OFFSET_DATA, data) == 1;
		}
		return false;
	}

	private bool SmuReadReg(uint addr, ref uint data)
	{
		if (RyzenNbAccesss.WritePciConfigDwordEx(SMU_PCI_ADDR, SMU_OFFSET_ADDR, addr) == 1)
		{
			return RyzenNbAccesss.ReadPciConfigDwordEx(SMU_PCI_ADDR, SMU_OFFSET_DATA, ref data) == 1;
		}
		return false;
	}

	private bool ECStatus(byte flag)
	{
		bool flag2 = flag == EC_OBF;
		for (int i = 0; i < 100; i++)
		{
			byte value = 0;
			RyzenNbAccesss.ReadIoPortByteEx(EC_SC, ref value);
			if (((flag2 ? (~value) : value) & flag) == 0)
			{
				return true;
			}
		}
		return false;
	}

	public void Write_EC_Init()
	{
		EC_CHIP_ID1 = Read_EC_EX(8192);
		EC_CHIP_ID2 = Read_EC_EX(8193);
		if (85 == EC_CHIP_ID1)
		{
			EC_CHIP_Ver = Read_EC_EX(4192);
			EC_CHIP_Ver |= 128;
			Write_EC_EX(4192, EC_CHIP_Ver);
		}
	}

	public bool Write_EC_EX(ushort iIndex, byte data)
	{
		if (ECStatus(EC_IBF))
		{
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 46);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, 17);
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 47);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, (byte)(iIndex >> 8));
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 46);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, 16);
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 47);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, (byte)(iIndex & 0xFF));
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 46);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, 18);
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 47);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, data);
			return true;
		}
		return false;
	}

	public byte Read_EC_EX(ushort iIndex)
	{
		byte b = 0;
		if (ECStatus(EC_IBF))
		{
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 46);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, 17);
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 47);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, (byte)(iIndex >> 8));
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 46);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, 16);
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 47);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, (byte)(iIndex & 0xFF));
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 46);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, 18);
			RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT, 47);
			RyzenNbAccesss.WriteIoPortByte(EC_DATA_PORT, b);
			return b;
		}
		return b;
	}

	public bool Read_EC(byte addr, out byte ret_var)
	{
		ret_var = 0;
		if (ECStatus(EC_IBF))
		{
			RyzenNbAccesss.WriteIoPortByte(EC_SC, RD_EC);
			Task.Delay(100);
			if (ECStatus(EC_IBF))
			{
				RyzenNbAccesss.WriteIoPortByte(EC_DATA, addr);
				if (ECStatus(EC_OBF))
				{
					Task.Delay(100);
					ret_var = RyzenNbAccesss.ReadIoPortByte(EC_DATA);
					return true;
				}
			}
		}
		return false;
	}

	public bool Write_EC(byte addr, byte var)
	{
		if (ECStatus(EC_IBF))
		{
			RyzenNbAccesss.WriteIoPortByte(EC_SC, WR_EC);
			Task.Delay(100);
			if (ECStatus(EC_IBF))
			{
				Task.Delay(100);
				RyzenNbAccesss.WriteIoPortByte(EC_DATA, addr);
				Task.Delay(100);
				RyzenNbAccesss.WriteIoPortByte(EC_DATA, var);
				return true;
			}
		}
		return false;
	}
}
