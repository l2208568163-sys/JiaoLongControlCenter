using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace GamingControlCenter;

public class PCInfoFormat
{
	private static CPU_INTERNAL_INFO cpu_info = default;

	private static List<MEM_INTERNAL_INFO> mem_list = new List<MEM_INTERNAL_INFO>();

	private static List<DISK_INTERNAL_INFO> disk_list = new List<DISK_INTERNAL_INFO>();

	private static List<GPU_INTERNAL_INFO> gpu_list = new List<GPU_INTERNAL_INFO>();

	private static List<NET_INTERNAL_INFO> net_list = new List<NET_INTERNAL_INFO>();

	private static OS_INTERNAL_INFO os_info = default;

	private static List<DISK_INSTANCE_INFO> disk_inst = new List<DISK_INSTANCE_INFO>();

	private static bool init = false;

	private static ManagementObjectSearcher searchercpucore = new ManagementObjectSearcher("Select * from Win32_Processor");

	public static bool GetInit()
	{
		return init;
	}

	public static int InitComputerInformation()
	{
		if (!init)
		{
			ComputerInfo.brisk_pc_cpu_info(out cpu_info);
			ComputerInfo.brisk_pc_mem_info(mem_list);
			ComputerInfo.brisk_pc_disk_info(disk_list);
			ComputerInfo.brisk_pc_gpu_info(gpu_list);
			ComputerInfo.brisk_pc_net_info(net_list);
			ComputerInfo.brisk_pc_os_info(out os_info);
			ComputerInfo.brisk_pc_disk_instance(disk_inst);
			init = true;
		}
		return 0;
	}

	public static int ComputerInformation(out string totalinfo_show)
	{
		totalinfo_show = "";
		totalinfo_show += "[CPU]\n";
		totalinfo_show += "--------------------------------------------------\n";
		totalinfo_show = totalinfo_show + cpu_info.name + " for ";
		totalinfo_show = totalinfo_show + cpu_info.physics_cores + " (P)/";
		totalinfo_show = totalinfo_show + cpu_info.logic_cores + " (T).\n";
		totalinfo_show += "\n[Memory]\n";
		totalinfo_show += "--------------------------------------------------\n";
		foreach (MEM_INTERNAL_INFO item in mem_list)
		{
			totalinfo_show += item.size;
			if (mem_list.Count > 1)
			{
				totalinfo_show += " + ";
			}
		}
		totalinfo_show += " (MiB).\n";
		totalinfo_show += "\n[Disk]\n";
		totalinfo_show += "--------------------------------------------------\n";
		foreach (DISK_INTERNAL_INFO item2 in disk_list)
		{
			totalinfo_show = totalinfo_show + item2.name + "\t" + item2.size + " (GB).\n";
		}
		totalinfo_show += "\n[GPU]\n";
		totalinfo_show += "--------------------------------------------------\n";
		foreach (GPU_INTERNAL_INFO item3 in gpu_list)
		{
			totalinfo_show = totalinfo_show + item3.name + "\t" + item3.ramSize + " (MiB).\n";
		}
		totalinfo_show += "\n[Network Adapter]\n";
		totalinfo_show += "--------------------------------------------------\n";
		foreach (NET_INTERNAL_INFO item4 in net_list)
		{
			totalinfo_show = totalinfo_show + item4.index + "\t" + item4.name + "\n";
		}
		totalinfo_show += "\n[OS]\n";
		totalinfo_show += "--------------------------------------------------\n";
		totalinfo_show = totalinfo_show + os_info.name + ", " + os_info.version + "\n";
		return 0;
	}

	public static int GetCPUName(out string pcinformations)
	{
		pcinformations = cpu_info.name;
		return 0;
	}

	public static int GetCPUPhysicsCores(out int PhysicsCores)
	{
		PhysicsCores = cpu_info.physics_cores;
		return 0;
	}

	public static int GetCPUFreq(out int pcinformations)
	{
		pcinformations = cpu_info.base_speed;
		return 0;
	}

	public static int GetMemoryInformation(out string pcinformations)
	{
		pcinformations = "[Memory]\n\n";
		foreach (MEM_INTERNAL_INFO item in mem_list)
		{
			pcinformations += "Index:\t";
			pcinformations += item.index;
			pcinformations += "\nSpeed:\t";
			pcinformations += item.speed;
			pcinformations += " (MHz)\n";
			pcinformations += "Size:\t";
			pcinformations += item.size;
			pcinformations += " (MiB)\n\n";
		}
		return 0;
	}

	public static int GetMemName(out string pcinformations)
	{
		pcinformations = "";
		ulong num = 0uL;
		foreach (MEM_INTERNAL_INFO item in mem_list)
		{
			num += item.size;
		}
		pcinformations += num;
		return 0;
	}

	public static int getCpuCoreCount()
	{
		int num = 0;
		foreach (ManagementObject item in searchercpucore.Get())
		{
			num = int.Parse(item["NumberOfCores"].ToString());
		}
		Console.WriteLine("Number Of Cores: {0}", num);
		return num;
	}

	public static int GetMemFreq(out string pcinformations)
	{
		pcinformations = "";
		if (mem_list.Count > 0)
		{
			pcinformations += mem_list[0].speed;
			pcinformations += " MHz";
		}
		return 0;
	}

	public static int GetDiskInformation(out string pcinformations)
	{
		pcinformations = "[Disk]\n\n";
		foreach (DISK_INTERNAL_INFO item in disk_list)
		{
			pcinformations += "Index:\t";
			pcinformations += item.index;
			pcinformations += "\tName:\t";
			pcinformations += item.name;
			pcinformations += "\nSize:\t";
			pcinformations += item.size;
			pcinformations += " (GB)\n\n";
		}
		return 0;
	}

	public static int GetDiskName(out string pcinformations)
	{
		pcinformations = "";
		foreach (DISK_INTERNAL_INFO item in disk_list)
		{
			if (item.index == 0)
			{
				pcinformations += item.name;
				break;
			}
		}
		return 0;
	}

	public static int GetDiskSize(out string pcinformations)
	{
		pcinformations = "";
		foreach (DISK_INTERNAL_INFO item in disk_list)
		{
			if (item.index == 0)
			{
				pcinformations += item.size;
				break;
			}
		}
		return 0;
	}

	public static int GetGPUInformation(out string pcinformations)
	{
		pcinformations = "[GPU]\n\n";
		foreach (GPU_INTERNAL_INFO item in gpu_list)
		{
			pcinformations += "Index:\t";
			pcinformations += item.index;
			pcinformations += "\nName:\t";
			pcinformations += item.name;
			pcinformations += "\nRAM Size:\t";
			pcinformations += item.ramSize;
			pcinformations += " (MiB)\n\n";
		}
		return 0;
	}

	public static int GetMainGPUName(out string pcinformations)
	{
		pcinformations = "";
		foreach (GPU_INTERNAL_INFO item in gpu_list)
		{
			if (item.name.StartsWith("NVIDIA"))
			{
				pcinformations += item.name;
				return 0;
			}
		}
		return 0;
	}

	public static int GetMainGPU_MemoryTotal(out double pcinformations)
	{
		pcinformations = 0.0;
		foreach (GPU_INTERNAL_INFO item in gpu_list)
		{
			if (item.index.StartsWith("VideoController1"))
			{
				pcinformations += (double)item.ramSize / 1024.0;
				return 0;
			}
		}
		return 0;
	}

	public static int GetSecondGPUName(out string pcinformations)
	{
		pcinformations = "";
		if (gpu_list.Count > 1)
		{
			pcinformations += gpu_list[1].name;
		}
		else if (gpu_list.Count == 1)
		{
			pcinformations += gpu_list[0].name;
		}
		return 0;
	}

	public static int GetNetworkInformation(out string pcinformations)
	{
		pcinformations = "[Network Adapter]\n\n";
		foreach (NET_INTERNAL_INFO item in net_list)
		{
			pcinformations += "Index:\t";
			pcinformations += item.index;
			pcinformations += "\nName:\t";
			pcinformations += item.name;
			pcinformations += "\nMac Address:\t";
			pcinformations += item.macAddr;
			pcinformations += "\n\n";
		}
		return 0;
	}

	public static int GetOSInformation(out string pcinformations)
	{
		pcinformations = "[OS]\n\n";
		pcinformations += "Name:\t";
		pcinformations += os_info.name;
		pcinformations += "\nVersion:\t";
		pcinformations += os_info.version;
		return 0;
	}

	public static int GetHardwareCount(string name)
	{
		int result = 0;
		if ("Memory" == name)
		{
			result = mem_list.Count;
		}
		else if ("Disk" == name)
		{
			result = disk_list.Count;
		}
		else if ("GPU" == name)
		{
			result = gpu_list.Count;
		}
		else if ("Network" == name)
		{
			result = net_list.Count;
		}
		return result;
	}

	public static int GetInstanceName(string name, int index, out string instanceName)
	{
		if ("Disk" == name)
		{
			instanceName = index.ToString();
			for (int i = 0; i < disk_inst.Count; i++)
			{
				if (disk_inst[i].index == index)
				{
					instanceName = instanceName + " " + disk_inst[i].Drive;
				}
			}
			if (index.ToString() == instanceName)
			{
				instanceName = "";
			}
		}
		else if ("Net" == name)
		{
			string name2 = net_list[index].name;
			name2 = name2.Replace("#", "_");
			name2 = name2.Replace("(", "[");
			name2 = name2.Replace(")", "]");
			instanceName = name2;
		}
		else
		{
			instanceName = "";
		}
		return 0;
	}

	public static int SaveComputerInfomation()
	{
		string text = "";
		string text2 = "";
		string text3 = "";
		string text4 = "";
		string text5 = "";
		string text6 = "";
		StreamWriter streamWriter = new StreamWriter(new FileStream(string.Concat(string.Concat(string.Concat(string.Concat(string.Concat(string.Concat("pc_info-" + DateTime.Now.Year + "-", DateTime.Now.Month.ToString(), "-"), DateTime.Now.Day.ToString(), "-"), DateTime.Now.Hour.ToString(), "_"), DateTime.Now.Minute.ToString(), "_"), DateTime.Now.Second.ToString()), ".csv"), FileMode.Create, FileAccess.Write));
		text += "CPU Informations\n";
		text += "Name,";
		text += cpu_info.name;
		text += "\nPhysical Cores,";
		text += cpu_info.physics_cores;
		text += "\nLogic Cores,";
		text += cpu_info.logic_cores;
		text += "\nClock Speed,";
		text += cpu_info.base_speed;
		text += ",(MHz)\n\n\n";
		text2 += "Memory Informations\n";
		foreach (MEM_INTERNAL_INFO item in mem_list)
		{
			text2 += "Index,";
			text2 += item.index;
			text2 += "\nSpeed,";
			text2 += item.speed;
			text2 += ",(MHz)\n";
			text2 += "Size,";
			text2 += item.size;
			text2 += ",(MiB)\n";
		}
		text2 += "\n\n";
		text3 += "Disk Informations\n";
		foreach (DISK_INTERNAL_INFO item2 in disk_list)
		{
			text3 += "Index,";
			text3 += item2.index;
			text3 += "\nName,";
			text3 += item2.name;
			text3 += "\nSize,";
			text3 += item2.size;
			text3 += ",(GB)\n";
		}
		text3 += "\n\n";
		text4 += "GPU Informations\n";
		foreach (GPU_INTERNAL_INFO item3 in gpu_list)
		{
			text4 += "Index,";
			text4 += item3.index;
			text4 += "\nName,";
			text4 += item3.name;
			text4 += "\nRAM Size,";
			text4 += item3.ramSize;
			text4 += ",(MiB)\n";
		}
		text4 += "\n\n";
		text5 += "Network Adapter Informations\n";
		foreach (NET_INTERNAL_INFO item4 in net_list)
		{
			text5 += "Index,";
			text5 += item4.index;
			text5 += "\nName,";
			text5 += item4.name;
			text5 += "\nMac Address,";
			text5 += item4.macAddr;
			text5 += "\n";
		}
		text5 += "\n\n";
		text6 += "OS Informations\n";
		text6 += "Name,";
		text6 += os_info.name;
		text6 += "\nVersion,";
		text6 += os_info.version;
		text6 += "\n";
		streamWriter.Write(text);
		streamWriter.Write(text2);
		streamWriter.Write(text3);
		streamWriter.Write(text4);
		streamWriter.Write(text5);
		streamWriter.Write(text6);
		streamWriter.Dispose();
		return 0;
	}
}
