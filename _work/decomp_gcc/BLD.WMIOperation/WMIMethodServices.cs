using System;
using System.Management;

namespace BLD.WMIOperation;

public static class WMIMethodServices
{
	private static void PrintByteArray(byte[] data)
	{
		for (int i = 0; i < 8; i++)
		{
			Console.WriteLine("{0:X}", data[i]);
		}
	}

	private static byte[] _MakeMethodPrams(WMIMethodType wMIMethodType, WMIMethodName wMIMethodName)
	{
		byte[] array = new byte[32];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = 0;
		}
		array[1] = (byte)wMIMethodType;
		array[3] = (byte)wMIMethodName;
		return array;
	}

	public static T GetValue<T>(WMIMethodName wMIMethodName)
	{
		Tuple<bool, byte[]> tuple = ExcMethod(_MakeMethodPrams(WMIMethodType.Get, wMIMethodName));
		if (!tuple.Item1)
		{
			if (typeof(T) == typeof(Tuple<int, int>))
			{
				return (T)(object)new Tuple<int, int>(-1, -1);
			}
			if (typeof(T) == typeof(Tuple<int, int, int>))
			{
				return (T)(object)new Tuple<int, int, int>(-1, -1, -1);
			}
			return (T)(object)byte.MaxValue;
		}
		PrintByteArray(tuple.Item2);
		if (typeof(T) == typeof(Tuple<int, int>))
		{
			int item = (tuple.Item2[5] << 8) + tuple.Item2[4];
			int item2 = (tuple.Item2[7] << 8) + tuple.Item2[6];
			return (T)(object)new Tuple<int, int>(item, item2);
		}
		if (typeof(T) == typeof(Tuple<int, int, int>))
		{
			byte item3 = tuple.Item2[4];
			int item4 = tuple.Item2[5];
			int item5 = tuple.Item2[6];
			return (T)(object)new Tuple<int, int, int>(item3, item4, item5);
		}
		return (T)(object)tuple.Item2[4];
	}

	public static bool SetValue(WMIMethodName wMIMethodName, object setvalue)
	{
		bool result = true;
		byte[] array = _MakeMethodPrams(WMIMethodType.Set, wMIMethodName);
		array[4] = (byte)setvalue;
		Console.WriteLine("SetMethod inparms:");
		PrintByteArray(array);
		if (!ExcMethod(array).Item1)
		{
			result = false;
		}
		return result;
	}

	public static bool SetValue(WMIMethodName wMIMethodName, byte[] setvalue)
	{
		bool result = true;
		byte[] array = _MakeMethodPrams(WMIMethodType.Set, wMIMethodName);
		for (int i = 0; i < setvalue.Length; i++)
		{
			array[4 + i] = setvalue[i];
		}
		Console.WriteLine("SetMethod inparms:");
		PrintByteArray(array);
		if (!ExcMethod(array).Item1)
		{
			result = false;
		}
		return result;
	}

	public static Tuple<bool, byte[]> ExcMethod(byte[] inData)
	{
		if (inData == null)
		{
			return new Tuple<bool, byte[]>(item1: false, null);
		}
		if (inData.Length != 32)
		{
			return new Tuple<bool, byte[]>(item1: false, null);
		}
		PrintByteArray(inData);
		try
		{
			ManagementObject managementObject = new ManagementObject("root\\WMI", "MICommonInterface.InstanceName='ACPI\\PNP0C14\\MIFS_0'", null);
			ManagementBaseObject methodParameters = managementObject.GetMethodParameters("MiInterface");
			methodParameters["InData"] = inData;
			byte[] item = managementObject.InvokeMethod("MiInterface", methodParameters, null)["OutData"] as byte[];
			return new Tuple<bool, byte[]>(item1: true, item);
		}
		catch (ManagementException ex)
		{
			Console.WriteLine("An error occurred while trying to execute the WMI method: " + ex.Message);
			return new Tuple<bool, byte[]>(item1: false, null);
		}
	}
}
