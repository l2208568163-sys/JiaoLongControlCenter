using System;
using System.IO;
using System.Windows;
using LibreHardwareMonitor.Hardware;

namespace GamingControlCenter;

public class SystemUsage : IEquatable<SystemUsage>
{
	private Computer computer = new Computer
	{
		IsCpuEnabled = true,
		IsGpuEnabled = true,
		IsMemoryEnabled = true,
		IsMotherboardEnabled = false,
		IsControllerEnabled = false,
		IsNetworkEnabled = false,
		IsStorageEnabled = false
	};

	public int gpu_temp { get; set; }

	public int cpu_temp { get; set; }

	public float cpu_usage_rate { get; set; }

	public float cpu_freq { get; set; }

	public int gpu_speed { get; set; }

	public float gpu_fan_rate { get; set; }

	public float gpu_freq { get; set; }

	public double gpu_memory_total { get; set; }

	public int gpu_power_usage { get; set; }

	public double gpu_usage_rate { get; set; }

	public double ram_usage { get; set; }

	public SystemUsage()
	{
		computer.Open();
	}

	public void UpInfo(SystemUsage systemUsage)
	{
		foreach (IHardware item in computer.Hardware)
		{
			if (item.HardwareType != HardwareType.Cpu && item.HardwareType != HardwareType.GpuNvidia && item.HardwareType != HardwareType.Memory)
			{
				continue;
			}
			item.Update();
			ISensor[] sensors = item.Sensors;
			foreach (ISensor sensor in sensors)
			{
				sensor.ValuesTimeWindow = TimeSpan.Zero;
				try
				{
					if (item.HardwareType == HardwareType.Memory && sensor.Name == "Memory" && sensor.SensorType == SensorType.Load)
					{
						systemUsage.ram_usage = sensor.Value.Value;
					}
					if (item.HardwareType == HardwareType.Cpu)
					{
						if (sensor.Name == "Core (Tctl/Tdie)" && sensor.SensorType == SensorType.Temperature)
						{
							systemUsage.cpu_temp = (int)sensor.Value.Value;
						}
						if (sensor.Name == "CPU Total" && sensor.SensorType == SensorType.Load)
						{
							systemUsage.cpu_usage_rate = sensor.Value.Value;
						}
						if (sensor.Name == "Core #1" && sensor.SensorType == SensorType.Clock)
						{
							systemUsage.cpu_freq = sensor.Value.Value / 1000f;
						}
					}
					if (item.HardwareType == HardwareType.GpuNvidia)
					{
						if (sensor.Name == "GPU Core" && sensor.SensorType == SensorType.Load)
						{
							systemUsage.gpu_usage_rate = sensor.Value.Value;
						}
						if (sensor.Name == "GPU Core" && sensor.SensorType == SensorType.Temperature)
						{
							systemUsage.gpu_temp = (int)sensor.Value.Value;
						}
						if (sensor.Name == "GPU Core" && sensor.SensorType == SensorType.Clock)
						{
							systemUsage.gpu_freq = sensor.Value.Value / 1000f;
						}
						if (sensor.Name == "GPU Fan" && sensor.SensorType == SensorType.Fan)
						{
							systemUsage.gpu_speed = (int)sensor.Value.Value;
						}
						if (sensor.Name == "GPU Fan" && sensor.SensorType == SensorType.Control)
						{
							systemUsage.gpu_fan_rate = (int)sensor.Value.Value;
						}
						if (sensor.Name == "GPU Memory" && sensor.SensorType == SensorType.Clock)
						{
							systemUsage.gpu_memory_total = sensor.Max.Value / 1024f;
						}
						if (sensor.SensorType == SensorType.Power && sensor.Value.HasValue)
						{
							gpu_power_usage = (int)sensor.Value.Value;
						}
					}
				}
				catch (InvalidOperationException ex)
				{
					MessageBox.Show("发生了其他异常：" + ex.Message);
					throw;
				}
			}
		}
	}

	public void computerClose()
	{
		computer.Hardware.Clear();
		computer.Close();
	}

	public int getCurrentDiskusage(out int freesize, out int totalsize)
	{
		long num = 0L;
		long num2 = 0L;
		DriveInfo[] drives = DriveInfo.GetDrives();
		foreach (DriveInfo driveInfo in drives)
		{
			if (driveInfo.IsReady && driveInfo.DriveType != DriveType.Network && driveInfo.DriveType != DriveType.NoRootDirectory)
			{
				num += driveInfo.TotalSize;
				num2 += driveInfo.TotalFreeSpace;
			}
		}
		totalsize = (int)((double)num / 1073741824.0);
		freesize = (int)((double)num2 / 1073741824.0);
		return 0;
	}

	public bool Equals(SystemUsage other)
	{
		throw new NotImplementedException();
	}
}
