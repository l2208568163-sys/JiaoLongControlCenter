using System;
using System.Management;
using GamingControlCenter;
using RyzenSmu;
using Universal_x86_Tuning_Utility.Scripts.AMD_Backend;

namespace Universal_x86_Tuning_Utility.Scripts;

public class Family
{
	public enum RyzenFamily
	{
		Unknown = -1,
		SummitRidge,
		PinnacleRidge,
		RavenRidge,
		Dali,
		Pollock,
		Picasso,
		FireFlight,
		Matisse,
		Renoir,
		Lucienne,
		VanGogh,
		Mendocino,
		Vermeer,
		Cezanne_Barcelo,
		Rembrandt,
		Raphael,
		DragonRange,
		PhoenixPoint,
		PhoenixPoint2,
		HawkPoint,
		SonomaValley,
		GraniteRidge,
		FireRange,
		StrixHalo,
		StrixPoint,
		KrackanPoint
	}

	public enum ProcessorType
	{
		Unknown = -1,
		Amd_Apu,
		Amd_Desktop_Cpu,
		Amd_Laptop_Cpu,
		Intel
	}

	public static RyzenFamily FAM = RyzenFamily.Unknown;

	public static ProcessorType TYPE = ProcessorType.Unknown;

	public static string CPUName = "";

	public static int CPUFamily = 0;

	public static int CPUModel = 0;

	public static int CPUStepping = 0;

	public static async void setCpuFamily()
	{
		try
		{
			string[] array = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER").Split(' ');
			int num = Array.IndexOf(array, "Family") + 1;
			int num2 = Array.IndexOf(array, "Model") + 1;
			int num3 = Array.IndexOf(array, "Stepping") + 1;
			CPUFamily = int.Parse(array[num]);
			CPUModel = int.Parse(array[num2]);
			CPUStepping = int.Parse(array[num3].TrimEnd(','));
			foreach (ManagementObject item in new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_Processor").Get())
			{
				CPUName = item["Name"].ToString();
			}
		}
		catch (ManagementException)
		{
		}
		if (CPUName.Contains("Intel"))
		{
			return;
		}
		try
		{
			App.memTimings = Mem_Timings.RetrieveTimings();
		}
		catch
		{
		}
		if (CPUFamily == 23)
		{
			if (CPUModel == 1)
			{
				FAM = RyzenFamily.SummitRidge;
			}
			if (CPUModel == 8)
			{
				FAM = RyzenFamily.PinnacleRidge;
			}
			if (CPUModel == 17 || CPUModel == 18)
			{
				FAM = RyzenFamily.RavenRidge;
			}
			if (CPUModel == 24)
			{
				FAM = RyzenFamily.Picasso;
			}
			if ((CPUModel == 32 && CPUName.Contains("15e")) || (CPUModel == 32 && CPUName.Contains("15Ce")) || (CPUModel == 32 && CPUName.Contains("20e")))
			{
				FAM = RyzenFamily.Pollock;
			}
			else if (CPUModel == 32)
			{
				FAM = RyzenFamily.Dali;
			}
			if (CPUModel == 80)
			{
				FAM = RyzenFamily.FireFlight;
			}
			if (CPUModel == 96)
			{
				FAM = RyzenFamily.Renoir;
			}
			if (CPUModel == 104)
			{
				FAM = RyzenFamily.Lucienne;
			}
			if (CPUModel == 113)
			{
				FAM = RyzenFamily.Matisse;
			}
			if (CPUModel == 144 || CPUModel == 145)
			{
				FAM = RyzenFamily.VanGogh;
			}
			if (CPUModel == 160)
			{
				FAM = RyzenFamily.Mendocino;
			}
		}
		if (CPUFamily == 25)
		{
			if (CPUModel == 33)
			{
				FAM = RyzenFamily.Vermeer;
			}
			if (CPUModel == 63 || CPUModel == 68)
			{
				FAM = RyzenFamily.Rembrandt;
			}
			if (CPUModel == 80)
			{
				FAM = RyzenFamily.Cezanne_Barcelo;
			}
			if (CPUModel == 97 && CPUName.Contains("HX"))
			{
				FAM = RyzenFamily.DragonRange;
			}
			else if (CPUModel == 97)
			{
				FAM = RyzenFamily.Raphael;
			}
			if (CPUModel == 116)
			{
				FAM = RyzenFamily.PhoenixPoint;
			}
			if (CPUModel == 120)
			{
				FAM = RyzenFamily.PhoenixPoint2;
			}
			if (CPUModel == 117)
			{
				FAM = RyzenFamily.HawkPoint;
			}
		}
		if (CPUFamily == 26)
		{
			if (CPUModel == 68 && CPUName.Contains("HX"))
			{
				FAM = RyzenFamily.FireRange;
			}
			else if (CPUModel == 68)
			{
				FAM = RyzenFamily.GraniteRidge;
			}
			if (CPUModel == 96)
			{
				FAM = RyzenFamily.KrackanPoint;
			}
			if (CPUModel == 32 || CPUModel == 36)
			{
				FAM = RyzenFamily.StrixPoint;
			}
			if (CPUModel == 112)
			{
				FAM = RyzenFamily.StrixHalo;
			}
		}
		if (FAM == RyzenFamily.SummitRidge || FAM == RyzenFamily.PinnacleRidge || FAM == RyzenFamily.Matisse || FAM == RyzenFamily.Vermeer || FAM == RyzenFamily.Raphael || FAM == RyzenFamily.GraniteRidge)
		{
			TYPE = ProcessorType.Amd_Desktop_Cpu;
		}
		else
		{
			TYPE = ProcessorType.Amd_Apu;
		}
		Addresses.setAddresses();
	}
}
