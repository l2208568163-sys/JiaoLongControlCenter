using System;
using System.Runtime.InteropServices;

namespace OpenLibSys_Mem;

public class Ols : IDisposable
{
	public enum OlsStatus
	{
		NO_ERROR,
		DLL_NOT_FOUND,
		DLL_INCORRECT_VERSION,
		DLL_INITIALIZE_ERROR
	}

	public enum OlsDllStatus
	{
		OLS_DLL_NO_ERROR = 0,
		OLS_DLL_UNSUPPORTED_PLATFORM = 1,
		OLS_DLL_DRIVER_NOT_LOADED = 2,
		OLS_DLL_DRIVER_NOT_FOUND = 3,
		OLS_DLL_DRIVER_UNLOADED = 4,
		OLS_DLL_DRIVER_NOT_LOADED_ON_NETWORK = 5,
		OLS_DLL_UNKNOWN_ERROR = 9
	}

	public enum OlsDriverType
	{
		OLS_DRIVER_TYPE_UNKNOWN,
		OLS_DRIVER_TYPE_WIN_9X,
		OLS_DRIVER_TYPE_WIN_NT,
		OLS_DRIVER_TYPE_WIN_NT4,
		OLS_DRIVER_TYPE_WIN_NT_X64,
		OLS_DRIVER_TYPE_WIN_NT_IA64
	}

	public enum OlsErrorPci : uint
	{
		OLS_ERROR_PCI_BUS_NOT_EXIST = 3758096385u,
		OLS_ERROR_PCI_NO_DEVICE,
		OLS_ERROR_PCI_WRITE_CONFIG,
		OLS_ERROR_PCI_READ_CONFIG
	}

	public delegate uint _GetDllStatus();

	public delegate uint _GetDllVersion(ref byte major, ref byte minor, ref byte revision, ref byte release);

	public delegate uint _GetDriverVersion(ref byte major, ref byte minor, ref byte revision, ref byte release);

	public delegate uint _GetDriverType();

	public delegate int _InitializeOls();

	public delegate void _DeinitializeOls();

	public delegate int _IsCpuid();

	public delegate int _IsMsr();

	public delegate int _IsTsc();

	public delegate int _Hlt();

	public delegate int _HltTx(nuint threadAffinityMask);

	public delegate int _HltPx(nuint processAffinityMask);

	public delegate int _Rdmsr(uint index, ref uint eax, ref uint edx);

	public delegate int _RdmsrTx(uint index, ref uint eax, ref uint edx, nuint threadAffinityMask);

	public delegate int _RdmsrPx(uint index, ref uint eax, ref uint edx, nuint processAffinityMask);

	public delegate int _Wrmsr(uint index, uint eax, uint edx);

	public delegate int _WrmsrTx(uint index, uint eax, uint edx, nuint threadAffinityMask);

	public delegate int _WrmsrPx(uint index, uint eax, uint edx, nuint processAffinityMask);

	public delegate int _Rdpmc(uint index, ref uint eax, ref uint edx);

	public delegate int _RdpmcTx(uint index, ref uint eax, ref uint edx, nuint threadAffinityMask);

	public delegate int _RdpmcPx(uint index, ref uint eax, ref uint edx, nuint processAffinityMask);

	public delegate int _Cpuid(uint index, ref uint eax, ref uint ebx, ref uint ecx, ref uint edx);

	public delegate int _CpuidTx(uint index, ref uint eax, ref uint ebx, ref uint ecx, ref uint edx, nuint threadAffinityMask);

	public delegate int _CpuidPx(uint index, ref uint eax, ref uint ebx, ref uint ecx, ref uint edx, nuint processAffinityMask);

	public delegate int _Rdtsc(ref uint eax, ref uint edx);

	public delegate int _RdtscTx(ref uint eax, ref uint edx, nuint threadAffinityMask);

	public delegate int _RdtscPx(ref uint eax, ref uint edx, nuint processAffinityMask);

	public delegate byte _ReadIoPortByte(ushort port);

	public delegate ushort _ReadIoPortWord(ushort port);

	public delegate uint _ReadIoPortDword(ushort port);

	public delegate int _ReadIoPortByteEx(ushort port, ref byte value);

	public delegate int _ReadIoPortWordEx(ushort port, ref ushort value);

	public delegate int _ReadIoPortDwordEx(ushort port, ref uint value);

	public delegate void _WriteIoPortByte(ushort port, byte value);

	public delegate void _WriteIoPortWord(ushort port, ushort value);

	public delegate void _WriteIoPortDword(ushort port, uint value);

	public delegate int _WriteIoPortByteEx(ushort port, byte value);

	public delegate int _WriteIoPortWordEx(ushort port, ushort value);

	public delegate int _WriteIoPortDwordEx(ushort port, uint value);

	public delegate void _SetPciMaxBusIndex(byte max);

	public delegate byte _ReadPciConfigByte(uint pciAddress, byte regAddress);

	public delegate ushort _ReadPciConfigWord(uint pciAddress, byte regAddress);

	public delegate uint _ReadPciConfigDword(uint pciAddress, byte regAddress);

	public delegate int _ReadPciConfigByteEx(uint pciAddress, uint regAddress, ref byte value);

	public delegate int _ReadPciConfigWordEx(uint pciAddress, uint regAddress, ref ushort value);

	public delegate int _ReadPciConfigDwordEx(uint pciAddress, uint regAddress, ref uint value);

	public delegate void _WritePciConfigByte(uint pciAddress, byte regAddress, byte value);

	public delegate void _WritePciConfigWord(uint pciAddress, byte regAddress, ushort value);

	public delegate void _WritePciConfigDword(uint pciAddress, byte regAddress, uint value);

	public delegate int _WritePciConfigByteEx(uint pciAddress, uint regAddress, byte value);

	public delegate int _WritePciConfigWordEx(uint pciAddress, uint regAddress, ushort value);

	public delegate int _WritePciConfigDwordEx(uint pciAddress, uint regAddress, uint value);

	public delegate uint _FindPciDeviceById(ushort vendorId, ushort deviceId, byte index);

	public delegate uint _FindPciDeviceByClass(byte baseClass, byte subClass, byte programIf, byte index);

	public unsafe delegate uint _ReadDmiMemory(byte* buffer, uint count, uint unitSize);

	public unsafe delegate uint _ReadPhysicalMemory(nuint address, byte* buffer, uint count, uint unitSize);

	public unsafe delegate uint _WritePhysicalMemory(nuint address, byte* buffer, uint count, uint unitSize);

	private const string dllNameX64 = "WinRing0x64_Mem.dll";

	private const string dllName = "WinRing0_Mem.dll";

	private nint module = IntPtr.Zero;

	private OlsStatus status;

	public _GetDllStatus GetDllStatus;

	public _GetDllVersion GetDllVersion;

	public _GetDriverVersion GetDriverVersion;

	public _GetDriverType GetDriverType;

	public _InitializeOls InitializeOls;

	public _DeinitializeOls DeinitializeOls;

	public _IsCpuid IsCpuid;

	public _IsMsr IsMsr;

	public _IsTsc IsTsc;

	public _Hlt Hlt;

	public _HltTx HltTx;

	public _HltPx HltPx;

	public _Rdmsr Rdmsr;

	public _RdmsrTx RdmsrTx;

	public _RdmsrPx RdmsrPx;

	public _Wrmsr Wrmsr;

	public _WrmsrTx WrmsrTx;

	public _WrmsrPx WrmsrPx;

	public _Rdpmc Rdpmc;

	public _RdpmcTx RdpmcTx;

	public _RdpmcPx RdpmcPx;

	public _Cpuid Cpuid;

	public _CpuidTx CpuidTx;

	public _CpuidPx CpuidPx;

	public _Rdtsc Rdtsc;

	public _RdtscTx RdtscTx;

	public _RdtscPx RdtscPx;

	public _ReadIoPortByte ReadIoPortByte;

	public _ReadIoPortWord ReadIoPortWord;

	public _ReadIoPortDword ReadIoPortDword;

	public _ReadIoPortByteEx ReadIoPortByteEx;

	public _ReadIoPortWordEx ReadIoPortWordEx;

	public _ReadIoPortDwordEx ReadIoPortDwordEx;

	public _WriteIoPortByte WriteIoPortByte;

	public _WriteIoPortWord WriteIoPortWord;

	public _WriteIoPortDword WriteIoPortDword;

	public _WriteIoPortByteEx WriteIoPortByteEx;

	public _WriteIoPortWordEx WriteIoPortWordEx;

	public _WriteIoPortDwordEx WriteIoPortDwordEx;

	public _SetPciMaxBusIndex SetPciMaxBusIndex;

	public _ReadPciConfigByte ReadPciConfigByte;

	public _ReadPciConfigWord ReadPciConfigWord;

	public _ReadPciConfigDword ReadPciConfigDword;

	public _ReadPciConfigByteEx ReadPciConfigByteEx;

	public _ReadPciConfigWordEx ReadPciConfigWordEx;

	public _ReadPciConfigDwordEx ReadPciConfigDwordEx;

	public _WritePciConfigByte WritePciConfigByte;

	public _WritePciConfigWord WritePciConfigWord;

	public _WritePciConfigDword WritePciConfigDword;

	public _WritePciConfigByteEx WritePciConfigByteEx;

	public _WritePciConfigWordEx WritePciConfigWordEx;

	public _WritePciConfigDwordEx WritePciConfigDwordEx;

	public _FindPciDeviceById FindPciDeviceById;

	public _FindPciDeviceByClass FindPciDeviceByClass;

	public _ReadDmiMemory ReadDmiMemory;

	public _ReadPhysicalMemory ReadPhysicalMemory;

	public _WritePhysicalMemory WritePhysicalMemory;

	public OlsStatus Status => status;

	public OlsDllStatus DllStatus => (OlsDllStatus)GetDllStatus();

	public uint PciBusDevFunc(uint bus, uint dev, uint func)
	{
		return ((bus & 0xFF) << 8) | ((dev & 0x1F) << 3) | (func & 7);
	}

	public uint PciGetBus(uint address)
	{
		return (address >> 8) & 0xFF;
	}

	public uint PciGetDev(uint address)
	{
		return (address >> 3) & 0x1F;
	}

	public uint PciGetFunc(uint address)
	{
		return address & 7;
	}

	[DllImport("kernel32")]
	public static extern nint LoadLibrary(string lpFileName);

	[DllImport("kernel32", SetLastError = true)]
	private static extern bool FreeLibrary(nint hModule);

	[DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
	private static extern nint GetProcAddress(nint hModule, [MarshalAs(UnmanagedType.LPStr)] string lpProcName);

	public Ols()
	{
		module = LoadLibrary((IntPtr.Size != 8) ? "WinRing0_Mem.dll" : "WinRing0x64_Mem.dll");
		if (module == IntPtr.Zero)
		{
			status = OlsStatus.DLL_NOT_FOUND;
			return;
		}
		GetDllStatus = GetDelegate<_GetDllStatus>("GetDllStatus");
		GetDllVersion = GetDelegate<_GetDllVersion>("GetDllVersion");
		GetDriverVersion = GetDelegate<_GetDriverVersion>("GetDriverVersion");
		GetDriverType = GetDelegate<_GetDriverType>("GetDriverType");
		InitializeOls = GetDelegate<_InitializeOls>("InitializeOls");
		DeinitializeOls = GetDelegate<_DeinitializeOls>("DeinitializeOls");
		IsCpuid = GetDelegate<_IsCpuid>("IsCpuid");
		IsMsr = GetDelegate<_IsMsr>("IsMsr");
		IsTsc = GetDelegate<_IsTsc>("IsTsc");
		Hlt = GetDelegate<_Hlt>("Hlt");
		HltTx = GetDelegate<_HltTx>("HltTx");
		HltPx = GetDelegate<_HltPx>("HltPx");
		Rdmsr = GetDelegate<_Rdmsr>("Rdmsr");
		RdmsrTx = GetDelegate<_RdmsrTx>("RdmsrTx");
		RdmsrPx = GetDelegate<_RdmsrPx>("RdmsrPx");
		Wrmsr = GetDelegate<_Wrmsr>("Wrmsr");
		WrmsrTx = GetDelegate<_WrmsrTx>("WrmsrTx");
		WrmsrPx = GetDelegate<_WrmsrPx>("WrmsrPx");
		Rdpmc = GetDelegate<_Rdpmc>("Rdpmc");
		RdpmcTx = GetDelegate<_RdpmcTx>("RdpmcTx");
		RdpmcPx = GetDelegate<_RdpmcPx>("RdpmcPx");
		Cpuid = GetDelegate<_Cpuid>("Cpuid");
		CpuidTx = GetDelegate<_CpuidTx>("CpuidTx");
		CpuidPx = GetDelegate<_CpuidPx>("CpuidPx");
		Rdtsc = GetDelegate<_Rdtsc>("Rdtsc");
		RdtscTx = GetDelegate<_RdtscTx>("RdtscTx");
		RdtscPx = GetDelegate<_RdtscPx>("RdtscPx");
		ReadIoPortByte = GetDelegate<_ReadIoPortByte>("ReadIoPortByte");
		ReadIoPortWord = GetDelegate<_ReadIoPortWord>("ReadIoPortWord");
		ReadIoPortDword = GetDelegate<_ReadIoPortDword>("ReadIoPortDword");
		ReadIoPortByteEx = GetDelegate<_ReadIoPortByteEx>("ReadIoPortByteEx");
		ReadIoPortWordEx = GetDelegate<_ReadIoPortWordEx>("ReadIoPortWordEx");
		ReadIoPortDwordEx = GetDelegate<_ReadIoPortDwordEx>("ReadIoPortDwordEx");
		WriteIoPortByte = GetDelegate<_WriteIoPortByte>("WriteIoPortByte");
		WriteIoPortWord = GetDelegate<_WriteIoPortWord>("WriteIoPortWord");
		WriteIoPortDword = GetDelegate<_WriteIoPortDword>("WriteIoPortDword");
		WriteIoPortByteEx = GetDelegate<_WriteIoPortByteEx>("WriteIoPortByteEx");
		WriteIoPortWordEx = GetDelegate<_WriteIoPortWordEx>("WriteIoPortWordEx");
		WriteIoPortDwordEx = GetDelegate<_WriteIoPortDwordEx>("WriteIoPortDwordEx");
		SetPciMaxBusIndex = GetDelegate<_SetPciMaxBusIndex>("SetPciMaxBusIndex");
		ReadPciConfigByte = GetDelegate<_ReadPciConfigByte>("ReadPciConfigByte");
		ReadPciConfigWord = GetDelegate<_ReadPciConfigWord>("ReadPciConfigWord");
		ReadPciConfigDword = GetDelegate<_ReadPciConfigDword>("ReadPciConfigDword");
		ReadPciConfigByteEx = GetDelegate<_ReadPciConfigByteEx>("ReadPciConfigByteEx");
		ReadPciConfigWordEx = GetDelegate<_ReadPciConfigWordEx>("ReadPciConfigWordEx");
		ReadPciConfigDwordEx = GetDelegate<_ReadPciConfigDwordEx>("ReadPciConfigDwordEx");
		WritePciConfigByte = GetDelegate<_WritePciConfigByte>("WritePciConfigByte");
		WritePciConfigWord = GetDelegate<_WritePciConfigWord>("WritePciConfigWord");
		WritePciConfigDword = GetDelegate<_WritePciConfigDword>("WritePciConfigDword");
		WritePciConfigByteEx = GetDelegate<_WritePciConfigByteEx>("WritePciConfigByteEx");
		WritePciConfigWordEx = GetDelegate<_WritePciConfigWordEx>("WritePciConfigWordEx");
		WritePciConfigDwordEx = GetDelegate<_WritePciConfigDwordEx>("WritePciConfigDwordEx");
		FindPciDeviceById = GetDelegate<_FindPciDeviceById>("FindPciDeviceById");
		FindPciDeviceByClass = GetDelegate<_FindPciDeviceByClass>("FindPciDeviceByClass");
		ReadDmiMemory = GetDelegate<_ReadDmiMemory>("ReadDmiMemory");
		ReadPhysicalMemory = GetDelegate<_ReadPhysicalMemory>("ReadPhysicalMemory");
		WritePhysicalMemory = GetDelegate<_WritePhysicalMemory>("WritePhysicalMemory");
		if (GetDllStatus == null || GetDllVersion == null || GetDriverVersion == null || GetDriverType == null || InitializeOls == null || DeinitializeOls == null || IsCpuid == null || IsMsr == null || IsTsc == null || Hlt == null || HltTx == null || HltPx == null || Rdmsr == null || RdmsrTx == null || RdmsrPx == null || Wrmsr == null || WrmsrTx == null || WrmsrPx == null || Rdpmc == null || RdpmcTx == null || RdpmcPx == null || Cpuid == null || CpuidTx == null || CpuidPx == null || Rdtsc == null || RdtscTx == null || RdtscPx == null || ReadIoPortByte == null || ReadIoPortWord == null || ReadIoPortDword == null || ReadIoPortByteEx == null || ReadIoPortWordEx == null || ReadIoPortDwordEx == null || WriteIoPortByte == null || WriteIoPortWord == null || WriteIoPortDword == null || WriteIoPortByteEx == null || WriteIoPortWordEx == null || WriteIoPortDwordEx == null || SetPciMaxBusIndex == null || ReadPciConfigByte == null || ReadPciConfigWord == null || ReadPciConfigDword == null || ReadPciConfigByteEx == null || ReadPciConfigWordEx == null || ReadPciConfigDwordEx == null || WritePciConfigByte == null || WritePciConfigWord == null || WritePciConfigDword == null || WritePciConfigByteEx == null || WritePciConfigWordEx == null || WritePciConfigDwordEx == null || FindPciDeviceById == null || FindPciDeviceByClass == null || ReadDmiMemory == null || ReadPhysicalMemory == null || WritePhysicalMemory == null)
		{
			status = OlsStatus.DLL_INCORRECT_VERSION;
		}
		if (InitializeOls() == 0)
		{
			status = OlsStatus.DLL_INITIALIZE_ERROR;
		}
	}

	public void Dispose()
	{
		if (module != IntPtr.Zero)
		{
			DeinitializeOls();
			FreeLibrary(module);
			module = IntPtr.Zero;
		}
	}

	public T GetDelegate<T>(string procName)
	{
		nint procAddress = GetProcAddress(module, procName);
		if (procAddress != IntPtr.Zero)
		{
			return Marshal.GetDelegateForFunctionPointer<T>(procAddress);
		}
		throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
	}

	public unsafe uint GetPhysLong(nuint address)
	{
		_ = 4u;
		uint result = 0u;
		ReadPhysicalMemory(address, (byte*)(&result), 1u, 4u);
		Marshal.GetLastWin32Error();
		return result;
	}
}
