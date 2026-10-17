using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using GamingControlCenter;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;

namespace NVIDIA;

internal class NvTuning
{
	public const int MinCoreOffset = -900;

	public const int MinMemoryOffset = -900;

	public const int MaxCoreOffset = 2000;

	public const int MaxMemoryOffset = 2000;

	public const int MinClockLimit = 400;

	public const int MaxClockLimit = 4000;

	internal static Guid activeSchemeGuid = Guid.Empty;

	[DllImport("powrprof.dll", SetLastError = true)]
	private static extern uint PowerReadACValueIndex(nint RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingGuid, ref Guid PowerSettingGuid, ref uint AcValueIndex);

	[DllImport("powrprof.dll", SetLastError = true)]
	private static extern uint PowerReadDCValueIndex(nint RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingGuid, ref Guid PowerSettingGuid, ref uint DcValueIndex);

	[DllImport("powrprof.dll", SetLastError = true)]
	private static extern uint PowerGetActiveScheme(nint RootPowerKey, ref nint SchemeGuid);

	[DllImport("kernel32.dll")]
	private static extern nint LocalFree(nint hMem);

	[DllImport("powrprof.dll", SetLastError = true)]
	public static extern uint PowerGetActualOverlayScheme(ref Guid ActualOverlayGuid);

	[DllImport("powrprof.dll", SetLastError = true)]
	private static extern uint PowerSetActiveScheme(nint RootPowerKey, ref Guid SchemeGuid);

	[DllImport("powrprof.dll", SetLastError = true)]
	public static extern uint PowerOpenUserPowerKey(ref nint phUserPowerKey, uint Access, bool OpenExisting);

	public static int SetClocks(int core, int memory, int voltage = 0)
	{
		if (core < -900 || core > 2000 || memory < -900 || memory > 2000)
		{
			return 0;
		}
		PhysicalGPU physicalGPU = PhysicalGPU.GetPhysicalGPUs().First();
		if (physicalGPU == null)
		{
			return -1;
		}
		try
		{
			PerformanceStates20ClockEntryV1 performanceStates20ClockEntryV = new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(core * 1000));
			PerformanceStates20ClockEntryV1 performanceStates20ClockEntryV2 = new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memory * 1000));
			PerformanceStates20ClockEntryV1[] clocks = new PerformanceStates20ClockEntryV1[2] { performanceStates20ClockEntryV, performanceStates20ClockEntryV2 };
			PerformanceStates20BaseVoltageEntryV1[] baseVoltages = new PerformanceStates20BaseVoltageEntryV1[0];
			IPerformanceStates20Info performanceStates20Info = new PerformanceStates20InfoV1(new PerformanceStates20InfoV1.PerformanceState20[1]
			{
				new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clocks, baseVoltages)
			}, 2u, 0u);
			GPUApi.SetPerformanceStates20(physicalGPU.Handle, performanceStates20Info);
		}
		catch (Exception)
		{
			return -1;
		}
		return 1;
	}

	public static int SetVoltage(int voltage)
	{
		return 1;
	}

	public static int RecVoltag(int voltage)
	{
		return 1;
	}

	public static int GetGPUPercentage()
	{
		return PhysicalGPU.GetPhysicalGPUs().First().UsageInformation.GPU.Percentage;
	}

	public static string GetShowCPUMHz()
	{
		nint SchemeGuid = IntPtr.Zero;
		if (PowerGetActiveScheme(IntPtr.Zero, ref SchemeGuid) != 0)
		{
			throw new Win32Exception();
		}
		activeSchemeGuid = (Guid)Marshal.PtrToStructure(SchemeGuid, typeof(Guid));
		LocalFree(SchemeGuid);
		Guid SchemeGuid2 = activeSchemeGuid;
		Guid SubGroupOfPowerSettingGuid = new Guid("54533251-82be-4824-96c1-47b60b740d00");
		Guid PowerSettingGuid = new Guid("75b0ae3f-bce0-45a7-8c89-c9611c25e100");
		uint DcValueIndex = 0u;
		if (PowerReadDCValueIndex(IntPtr.Zero, ref SchemeGuid2, ref SubGroupOfPowerSettingGuid, ref PowerSettingGuid, ref DcValueIndex) != 0)
		{
			throw new Win32Exception();
		}
		return DcValueIndex.ToString();
	}

	public static async void SetShowCPUMHz(int mhz, bool flag)
	{
		if (mhz < 1500 || mhz > 5400)
		{
			MessageBox.Show(mhz + "请设置1500mhz-5400mhz之间");
		}
		try
		{
			RunPowershellCommand("powercfg /overlaysetactive overlay_scheme_none");
			await Task.Delay(100);
			nint SchemeGuid = IntPtr.Zero;
			if (PowerGetActiveScheme(IntPtr.Zero, ref SchemeGuid) != 0)
			{
				throw new Win32Exception();
			}
			activeSchemeGuid = (Guid)Marshal.PtrToStructure(SchemeGuid, typeof(Guid));
			LocalFree(SchemeGuid);
			if (flag)
			{
				RunPowershellCommand($"powercfg /setacvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 75b0ae3f-bce0-45a7-8c89-c9611c25e100  {mhz}");
				await Task.Delay(100);
				RunPowershellCommand($"powercfg /setdcvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 75b0ae3f-bce0-45a7-8c89-c9611c25e100 {mhz}");
			}
			else
			{
				RunPowershellCommand("powercfg /setacvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 75b0ae3f-bce0-45a7-8c89-c9611c25e100 0");
				await Task.Delay(100);
				RunPowershellCommand("powercfg /setdcvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 75b0ae3f-bce0-45a7-8c89-c9611c25e100 0");
			}
			await Task.Delay(100);
			PowerSetActiveScheme(IntPtr.Zero, ref activeSchemeGuid);
		}
		catch
		{
		}
	}

	public static string GetPowermanagement()
	{
		nint phUserPowerKey = IntPtr.Zero;
		if (PowerOpenUserPowerKey(ref phUserPowerKey, 131097u, OpenExisting: true) == 0)
		{
			using RegistryKey registryKey = RegistryKey.FromHandle(new SafeRegistryHandle(phUserPowerKey, ownsHandle: true));
			object value = registryKey.GetValue("ActiveOverlayAcPowerScheme");
			if (value != null && !(value is string))
			{
				return "未知模式";
			}
			switch (value as string)
			{
			case "00000000-0000-0000-0000-000000000000":
				return "平衡";
			case "961cc777-2547-4f9d-8174-7d86181b8a7a":
				return "最佳能耗";
			case "ded574b5-45a0-4f42-8737-46345c09c238":
				return "最佳性能";
			}
		}
		return "未知模式";
	}

	public static async void SetCpuCore(int corePercent)
	{
		RunPowershellCommand("powercfg /overlaysetactive overlay_scheme_none");
		await Task.Delay(100);
		nint SchemeGuid = IntPtr.Zero;
		if (PowerGetActiveScheme(IntPtr.Zero, ref SchemeGuid) != 0)
		{
			throw new Win32Exception();
		}
		activeSchemeGuid = (Guid)Marshal.PtrToStructure(SchemeGuid, typeof(Guid));
		LocalFree(SchemeGuid);
		RunPowershellCommand($"powercfg /setacvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 8baa4a8a-14c6-4451-8e8b-14bdbd197537 {corePercent}");
		await Task.Delay(100);
		RunPowershellCommand($"powercfg /setdcvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 8baa4a8a-14c6-4451-8e8b-14bdbd197537 {corePercent}");
		await Task.Delay(100);
		RunPowershellCommand($"powercfg /setacvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 {corePercent}");
		await Task.Delay(100);
		RunPowershellCommand($"powercfg /setdcvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 {corePercent}");
		await Task.Delay(100);
		RunPowershellCommand($"powercfg /setacvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 ea062031-0e34-4ff1-9b6d-eb1059334028 {corePercent}");
		await Task.Delay(100);
		RunPowershellCommand($"powercfg /setdcvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 ea062031-0e34-4ff1-9b6d-eb1059334028 {corePercent}");
	}

	public static async void RecCpuCore()
	{
		RunPowershellCommand("powercfg /overlaysetactive overlay_scheme_none");
		await Task.Delay(100);
		nint SchemeGuid = IntPtr.Zero;
		if (PowerGetActiveScheme(IntPtr.Zero, ref SchemeGuid) != 0)
		{
			throw new Win32Exception();
		}
		activeSchemeGuid = (Guid)Marshal.PtrToStructure(SchemeGuid, typeof(Guid));
		LocalFree(SchemeGuid);
		RunPowershellCommand("powercfg /setacvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 100");
		await Task.Delay(100);
		RunPowershellCommand("powercfg /setdcvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 100");
		await Task.Delay(100);
		RunPowershellCommand("powercfg /setacvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 ea062031-0e34-4ff1-9b6d-eb1059334028 100");
		await Task.Delay(100);
		RunPowershellCommand("powercfg /setdcvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 ea062031-0e34-4ff1-9b6d-eb1059334028 100");
	}

	public static string GetCpuCore()
	{
		nint SchemeGuid = IntPtr.Zero;
		if (PowerGetActiveScheme(IntPtr.Zero, ref SchemeGuid) != 0)
		{
			throw new Win32Exception();
		}
		activeSchemeGuid = (Guid)Marshal.PtrToStructure(SchemeGuid, typeof(Guid));
		LocalFree(SchemeGuid);
		Guid SchemeGuid2 = activeSchemeGuid;
		Guid SubGroupOfPowerSettingGuid = new Guid("54533251-82be-4824-96c1-47b60b740d00");
		Guid PowerSettingGuid = new Guid("ea062031-0e34-4ff1-9b6d-eb1059334028");
		uint DcValueIndex = 0u;
		if (PowerReadDCValueIndex(IntPtr.Zero, ref SchemeGuid2, ref SubGroupOfPowerSettingGuid, ref PowerSettingGuid, ref DcValueIndex) != 0)
		{
			throw new Win32Exception();
		}
		int PhysicsCores = 0;
		PCInfoFormat.GetCPUPhysicsCores(out PhysicsCores);
		int num = Math.Abs(PhysicsCores - (int)Math.Round((float)(DcValueIndex + 100) / 100f * (float)PhysicsCores));
		if (num == PhysicsCores)
		{
			return "不限制";
		}
		return num.ToString();
	}

	public static async void SetCPUPerformanceStrategy(int power)
	{
		RunPowershellCommand("powercfg /overlaysetactive overlay_scheme_none");
		await Task.Delay(100);
		nint SchemeGuid = IntPtr.Zero;
		if (PowerGetActiveScheme(IntPtr.Zero, ref SchemeGuid) != 0)
		{
			throw new Win32Exception();
		}
		activeSchemeGuid = (Guid)Marshal.PtrToStructure(SchemeGuid, typeof(Guid));
		LocalFree(SchemeGuid);
		RunPowershellCommand($"powercfg /setacvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 36687f9e-e3a5-4dbf-b1dc-15eb381c6863 {power}");
		await Task.Delay(100);
		RunPowershellCommand($"powercfg /setdcvalueindex {activeSchemeGuid.ToString()} 54533251-82be-4824-96c1-47b60b740d00 36687f9e-e3a5-4dbf-b1dc-15eb381c6863 {power}");
	}

	public static void SetPowerManagement(string power)
	{
		nint SchemeGuid = IntPtr.Zero;
		if (PowerGetActiveScheme(IntPtr.Zero, ref SchemeGuid) != 0)
		{
			throw new Win32Exception();
		}
		activeSchemeGuid = (Guid)Marshal.PtrToStructure(SchemeGuid, typeof(Guid));
		LocalFree(SchemeGuid);
		if (power == "最佳效能")
		{
			RunPowershellCommand("powercfg /overlaysetactive OVERLAY_SCHEME_MIN");
		}
		if (power == "平衡")
		{
			RunPowershellCommand("powercfg /overlaysetactive overlay_scheme_none");
		}
		if (power == "最佳性能")
		{
			RunPowershellCommand("powercfg /overlaysetactive OVERLAY_SCHEME_MAX");
		}
	}

	public static async void SetTuro(bool flag)
	{
		_ = 4;
		try
		{
			if (flag)
			{
				nint SchemeGuid = IntPtr.Zero;
				if (PowerGetActiveScheme(IntPtr.Zero, ref SchemeGuid) != 0)
				{
					throw new Win32Exception();
				}
				activeSchemeGuid = (Guid)Marshal.PtrToStructure(SchemeGuid, typeof(Guid));
				LocalFree(SchemeGuid);
				RunPowershellCommand("powercfg /overlaysetactive overlay_scheme_none");
				await Task.Delay(100);
				RunPowershellCommand("powercfg /setacvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 be337238-0d82-4146-a960-4f3749d470c7 0");
				await Task.Delay(100);
				RunPowershellCommand("powercfg /setdcvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 be337238-0d82-4146-a960-4f3749d470c7 0");
				PowerSetActiveScheme(IntPtr.Zero, ref activeSchemeGuid);
			}
			else
			{
				nint SchemeGuid2 = IntPtr.Zero;
				if (PowerGetActiveScheme(IntPtr.Zero, ref SchemeGuid2) != 0)
				{
					throw new Win32Exception();
				}
				activeSchemeGuid = (Guid)Marshal.PtrToStructure(SchemeGuid2, typeof(Guid));
				LocalFree(SchemeGuid2);
				await Task.Delay(100);
				RunPowershellCommand("powercfg /setacvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 be337238-0d82-4146-a960-4f3749d470c7 2");
				await Task.Delay(100);
				RunPowershellCommand("powercfg /setdcvalueindex " + activeSchemeGuid.ToString() + " 54533251-82be-4824-96c1-47b60b740d00 be337238-0d82-4146-a960-4f3749d470c7 2");
				await Task.Delay(100);
				PowerSetActiveScheme(IntPtr.Zero, ref activeSchemeGuid);
			}
		}
		catch
		{
		}
	}

	public static int SetGPUClockLock(int clock)
	{
		if (clock < 400 || clock >= 2500)
		{
			clock = 0;
			MessageBox.Show("请输入大于400小于2500的值");
			return 0;
		}
		RunPowershellCommand($"nvidia-smi -lgc {clock},{clock}");
		return 1;
	}

	public static int SetMaxGPUClock(int clock)
	{
		if (clock < 400 || clock >= 2500)
		{
			clock = 0;
			MessageBox.Show("请输入大于400小于2500的值");
			return 0;
		}
		if (GetMaxGPUCLock() != clock)
		{
			if (clock > 0)
			{
				RunPowershellCommand($"nvidia-smi -lgc 0,{clock}");
			}
			else
			{
				RunPowershellCommand("nvidia-smi -rgc");
			}
			return 1;
		}
		return 0;
	}

	public static void RecMaxGPUClock()
	{
		RunPowershellCommand("nvidia-smi -rgc");
	}

	public static int GetMaxGPUCLock()
	{
		return (int)GPUApi.GetClockBoostLock(PhysicalGPU.GetPhysicalGPUs().First().Handle).ClockBoostLocks[0].VoltageInMicroV / 1000;
	}

	private static bool RunPowershellCommand(string script)
	{
		try
		{
			RunCMD("powershell", script);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static void RunCMD(string name, string args)
	{
		Process process = new Process();
		process.StartInfo.UseShellExecute = false;
		process.StartInfo.CreateNoWindow = true;
		process.StartInfo.RedirectStandardOutput = true;
		process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
		process.StartInfo.FileName = name;
		process.StartInfo.Arguments = args;
		process.Start();
		process.StandardOutput.ReadToEnd().Replace(Environment.NewLine, " ").Trim(' ');
		process.WaitForExit();
	}
}
