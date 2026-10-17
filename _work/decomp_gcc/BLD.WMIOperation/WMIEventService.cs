using System;
using System.Diagnostics;
using System.Management;
using System.Timers;

namespace BLD.WMIOperation;

public class WMIEventService
{
	public delegate void WMIEventArrivedEventHandler(WMIEventType wMIEventType, WMIEventName wMIEventName, object eVENTvalue);

	private ManagementEventWatcher _watcher;

	private byte times;

	public event WMIEventArrivedEventHandler _WMIEventArrived;

	public bool InitAndStart(WMIEventArrivedEventHandler wMIEventArrived)
	{
		try
		{
			string query = "SELECT * FROM HID_EVENT20";
			_watcher = new ManagementEventWatcher("root\\WMI", query);
			Console.WriteLine("Waiting for an event...");
			_watcher.EventArrived += Watcher_EventArrived;
			_WMIEventArrived += wMIEventArrived;
			_watcher.Start();
			return true;
		}
		catch (ManagementException ex)
		{
			Console.WriteLine("An error occurred while trying to receive an event: " + ex.Message);
			return false;
		}
	}

	protected void viewTimer(object sender, ElapsedEventArgs e)
	{
		byte[] array = new byte[8] { 1, 15, times, 0, 0, 0, 0, 0 };
		WMIEventType wMIEventType = (WMIEventType)array[0];
		WMIEventName wMIEventName = (WMIEventName)array[1];
		object obj = null;
		obj = ((wMIEventName != WMIEventName.CPUFanSpeed && wMIEventName != WMIEventName.GPUFanSpeed) ? ((object)array[2]) : ((object)((array[2] << 8) + array[3])));
		Console.WriteLine("outdata:===============");
		for (int i = 0; i < array.Length; i++)
		{
			Console.WriteLine(array[i]);
		}
		if (_WMIEventArrived != null)
		{
			_WMIEventArrived(wMIEventType, wMIEventName, obj);
		}
		times++;
		if (times == 3)
		{
			times = 0;
		}
	}

	public void Stop()
	{
		if (_watcher != null)
		{
			_watcher.Stop();
			_watcher.Dispose();
		}
		_watcher.EventArrived -= Watcher_EventArrived;
		_watcher.Dispose();
	}

	private void Watcher_EventArrived(object sender, EventArrivedEventArgs e)
	{
		Console.WriteLine(e.NewEvent);
		byte[] array = e.NewEvent["EventDetail"] as byte[];
		WMIEventType wMIEventType = (WMIEventType)array[0];
		WMIEventName wMIEventName = (WMIEventName)array[1];
		object obj = null;
		obj = ((wMIEventName != WMIEventName.CPUFanSpeed && wMIEventName != WMIEventName.GPUFanSpeed) ? ((object)array[2]) : ((object)((array[2] << 8) + array[3])));
		if (_WMIEventArrived != null)
		{
			_WMIEventArrived(wMIEventType, wMIEventName, obj);
		}
		EventLogHelper._EventLog.WriteEntry(BitConverter.ToString(array), EventLogEntryType.Information);
	}
}
