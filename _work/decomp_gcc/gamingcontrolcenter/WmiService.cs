using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GamingControlCenter;

public class WmiService
{
	public int GetValueByKey(string wmiClassName, string itemId, out string resultInfo)
	{
		string text = "SELECT ";
		MemoryStream memoryStream = new MemoryStream();
		Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter((Stream)memoryStream, default(JsonWriterOptions));
		text += itemId;
		text += " FROM ";
		text += wmiClassName;
		try
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				ManagementObjectCollection managementObjectCollection = new ManagementObjectSearcher(new ObjectQuery(text)).Get();
				utf8JsonWriter.WriteStartObject();
				utf8JsonWriter.WriteNumber("count", managementObjectCollection.Count);
				utf8JsonWriter.WriteStartArray("data");
				foreach (ManagementObject item in managementObjectCollection)
				{
					utf8JsonWriter.WriteStartObject();
					string[] array = itemId.Split(new char[1] { ',' });
					foreach (string text2 in array)
					{
						if (!string.IsNullOrEmpty(text2))
						{
							if (object.Equals(item[text2], null))
							{
								utf8JsonWriter.WriteString(text2, "");
							}
							else
							{
								utf8JsonWriter.WriteString(text2, item[text2].ToString());
							}
						}
					}
					utf8JsonWriter.WriteEndObject();
				}
				utf8JsonWriter.WriteEndArray();
				utf8JsonWriter.WriteEndObject();
				utf8JsonWriter.Flush();
			}
			resultInfo = Encoding.UTF8.GetString(memoryStream.ToArray());
		}
		catch (Exception ex)
		{
			ex.ToString();
			resultInfo = "";
			return -1;
		}
		return 0;
	}
}
