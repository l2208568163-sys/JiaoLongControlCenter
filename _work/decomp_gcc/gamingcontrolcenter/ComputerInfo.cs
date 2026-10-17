using System;
using System.Collections.Generic;
using System.Text.Json;
using BLD.WMIOperation;

namespace GamingControlCenter;

public class ComputerInfo
{
	private static WmiService wmiQuery = new WmiService();

	private int brisk_pc_cpu_setBits()
	{
		return 0;
	}

	private int brisk_pc_cpu_decode()
	{
		return 0;
	}

	public static int brisk_pc_cpu_info(out CPU_INTERNAL_INFO cpuInfo)
	{
		string wmiClassName = "Win32_Processor";
		string text = "Name";
		string text2 = "NumberOfCores";
		string text3 = "NumberOfLogicalProcessors";
		string text4 = "MaxClockSpeed";
		string itemId = text + "," + text2 + "," + text3 + "," + text4;
		string resultInfo = "";
		wmiQuery.GetValueByKey(wmiClassName, itemId, out resultInfo);
		JsonElement property = JsonDocument.Parse(resultInfo).RootElement.GetProperty("data");
		cpuInfo.name = property[0].GetProperty(text).ToString();
		if (!string.IsNullOrEmpty(property[0].GetProperty(text2).ToString()))
		{
			cpuInfo.physics_cores = int.Parse(property[0].GetProperty(text2).ToString());
		}
		else
		{
			cpuInfo.physics_cores = 0;
		}
		if (!string.IsNullOrEmpty(property[0].GetProperty(text3).ToString()))
		{
			cpuInfo.logic_cores = int.Parse(property[0].GetProperty(text3).ToString());
		}
		else
		{
			cpuInfo.logic_cores = 0;
		}
		if (!string.IsNullOrEmpty(property[0].GetProperty(text4).ToString()))
		{
			cpuInfo.base_speed = int.Parse(property[0].GetProperty(text4).ToString());
		}
		else
		{
			cpuInfo.base_speed = 0;
		}
		return 0;
	}

	public static int brisk_pc_mem_info(List<MEM_INTERNAL_INFO> memInfo)
	{
		string wmiClassName = "Win32_PhysicalMemory";
		string text = "BankLabel";
		string text2 = "ConfiguredClockSpeed";
		string text3 = "Capacity";
		string itemId = text + "," + text2 + "," + text3;
		string resultInfo = "";
		wmiQuery.GetValueByKey(wmiClassName, itemId, out resultInfo);
		JsonElement rootElement = JsonDocument.Parse(resultInfo).RootElement;
		int num = int.Parse(rootElement.GetProperty("count").ToString());
		JsonElement property = rootElement.GetProperty("data");
		MEM_INTERNAL_INFO[] array = new MEM_INTERNAL_INFO[num];
		for (int i = 0; i < num; i++)
		{
			array[i].count = num;
			array[i].index = property[i].GetProperty(text).ToString();
			if (!string.IsNullOrEmpty(property[i].GetProperty(text2).ToString()))
			{
				array[i].speed = int.Parse(property[i].GetProperty(text2).ToString());
			}
			else
			{
				array[i].speed = 0;
			}
			if (!string.IsNullOrEmpty(property[i].GetProperty(text3).ToString()))
			{
				array[i].size = ulong.Parse(property[i].GetProperty(text3).ToString()) >> 30;
			}
			else
			{
				array[i].size = 0uL;
			}
			memInfo.Add(array[i]);
		}
		return 0;
	}

	public static int brisk_pc_disk_info(List<DISK_INTERNAL_INFO> diskInfo)
	{
		string wmiClassName = "Win32_DiskDrive";
		string text = "Index";
		string text2 = "Model";
		string text3 = "Size";
		string itemId = text + "," + text2 + "," + text3;
		string resultInfo = "";
		wmiQuery.GetValueByKey(wmiClassName, itemId, out resultInfo);
		JsonElement rootElement = JsonDocument.Parse(resultInfo).RootElement;
		int num = int.Parse(rootElement.GetProperty("count").ToString());
		JsonElement property = rootElement.GetProperty("data");
		DISK_INTERNAL_INFO[] array = new DISK_INTERNAL_INFO[num];
		for (int i = 0; i < num; i++)
		{
			array[i].count = num;
			if (!string.IsNullOrEmpty(property[i].GetProperty(text).ToString()))
			{
				array[i].index = int.Parse(property[i].GetProperty(text).ToString());
			}
			else
			{
				array[i].index = 0;
			}
			array[i].name = property[i].GetProperty(text2).ToString();
			if (!string.IsNullOrEmpty(property[i].GetProperty(text3).ToString()))
			{
				array[i].size = ulong.Parse(property[i].GetProperty(text3).ToString()) / 1000 / 1000 / 1000;
			}
			else
			{
				array[i].size = 0uL;
			}
			diskInfo.Add(array[i]);
		}
		return 0;
	}

	public static int brisk_pc_gpu_info(List<GPU_INTERNAL_INFO> gpuInfo)
	{
		string wmiClassName = "Win32_VideoController";
		string text = "DeviceID";
		string text2 = "Name";
		string text3 = "AdapterRAM";
		string itemId = text + "," + text2 + "," + text3;
		string resultInfo = "";
		wmiQuery.GetValueByKey(wmiClassName, itemId, out resultInfo);
		JsonElement rootElement = JsonDocument.Parse(resultInfo).RootElement;
		int num = int.Parse(rootElement.GetProperty("count").ToString());
		JsonElement property = rootElement.GetProperty("data");
		GPU_INTERNAL_INFO[] array = new GPU_INTERNAL_INFO[num];
		for (int i = 0; i < num; i++)
		{
			array[i].count = num;
			array[i].index = property[i].GetProperty(text).ToString();
			array[i].name = property[i].GetProperty(text2).ToString();
			if (!string.IsNullOrEmpty(property[i].GetProperty(text3).ToString()))
			{
				array[i].ramSize = ulong.Parse(property[i].GetProperty(text3).ToString()) >> 20;
			}
			else
			{
				array[i].ramSize = 0uL;
			}
			gpuInfo.Add(array[i]);
		}
		return 0;
	}

	public static int brisk_pc_net_info(List<NET_INTERNAL_INFO> netInfo)
	{
		string wmiClassName = "Win32_NetworkAdapter WHERE PhysicalAdapter = True";
		string text = "DeviceID";
		string text2 = "Name";
		string text3 = "MACAddress";
		string itemId = text + "," + text2 + "," + text3;
		string resultInfo = "";
		wmiQuery.GetValueByKey(wmiClassName, itemId, out resultInfo);
		JsonElement rootElement = JsonDocument.Parse(resultInfo).RootElement;
		int num = int.Parse(rootElement.GetProperty("count").ToString());
		JsonElement property = rootElement.GetProperty("data");
		NET_INTERNAL_INFO[] array = new NET_INTERNAL_INFO[num];
		for (int i = 0; i < num; i++)
		{
			array[i].count = num;
			if (!string.IsNullOrEmpty(property[i].GetProperty(text).ToString()))
			{
				array[i].index = int.Parse(property[i].GetProperty(text).ToString());
			}
			else
			{
				array[i].index = 0;
			}
			array[i].name = property[i].GetProperty(text2).ToString();
			array[i].macAddr = property[i].GetProperty(text3).ToString();
			netInfo.Add(array[i]);
		}
		return 0;
	}

	public static int brisk_pc_os_info(out OS_INTERNAL_INFO osInfo)
	{
		string wmiClassName = "Win32_OperatingSystem";
		string text = "Caption";
		string text2 = "Version";
		string itemId = text + "," + text2;
		string resultInfo = "";
		wmiQuery.GetValueByKey(wmiClassName, itemId, out resultInfo);
		JsonElement property = JsonDocument.Parse(resultInfo).RootElement.GetProperty("data");
		osInfo.name = property[0].GetProperty(text).ToString();
		osInfo.version = property[0].GetProperty(text2).ToString();
		return 0;
	}

	public static int brisk_pc_disk_instance(List<DISK_INSTANCE_INFO> diskInstance)
	{
		string wmiClassName = "Win32_LogicalDiskToPartition";
		string text = "Antecedent";
		string text2 = "Dependent";
		string text3 = "";
		string itemId = text + "," + text2;
		string resultInfo = "";
		wmiQuery.GetValueByKey(wmiClassName, itemId, out resultInfo);
		JsonElement rootElement = JsonDocument.Parse(resultInfo).RootElement;
		int num = int.Parse(rootElement.GetProperty("count").ToString());
		JsonElement property = rootElement.GetProperty("data");
		DISK_INSTANCE_INFO[] array = new DISK_INSTANCE_INFO[num];
		for (int i = 0; i < num; i++)
		{
			array[i].count = num;
			text3 = property[i].GetProperty(text).ToString();
			text3 = text3.Split("=\"Disk #")[1];
			text3 = text3.Split(",")[0];
			array[i].index = int.Parse(text3);
			text3 = property[i].GetProperty(text2).ToString();
			text3 = text3.Split("=\"")[1];
			array[i].Drive = text3.Split("\"")[0];
			diskInstance.Add(array[i]);
		}
		return 0;
	}

	public static void WMI_METHOD_DEMO()
	{
	}

	private static void wMIEventArrived(WMIEventType wMIEventType, WMIEventName wMIEventName, object eVENTvalue)
	{
		Console.WriteLine("WMIEventType: " + wMIEventType.ToString() + " wMIEventName: " + wMIEventName.ToString() + " eVENTvalue: " + eVENTvalue);
	}
}
