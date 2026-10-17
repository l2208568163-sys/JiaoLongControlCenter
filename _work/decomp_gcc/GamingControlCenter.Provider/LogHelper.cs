using System;
using System.IO;
using System.Text;

namespace GamingControlCenter.Provider;

public class LogHelper
{
	public static void WriteLog(string sourceName, Exception ex)
	{
		WriteLog($"{sourceName}\r\n{ex.Message}\r\n{ex.Source}\r\n{ex.StackTrace}\r\n{ex.TargetSite}");
	}

	public static void WriteLog(string section, string message)
	{
		WriteLog(section + "\r\n" + message);
	}

	public static void WriteLog(string logInfo)
	{
		try
		{
			string text = Path.Combine(Environment.CurrentDirectory, "ErrorLog");
			if (!Directory.Exists(text))
			{
				Directory.CreateDirectory(text);
			}
			string path = DateTime.Now.ToString("yyyy-MM-dd_HH") + "_Log.log";
			File.AppendAllText(Path.Combine(text, path), DateTime.Now.ToString("yyyy-MM-dd:HH:mm:ss:ffff") + ":" + logInfo + "\r\n", Encoding.UTF8);
		}
		catch
		{
		}
	}
}
