using System.Diagnostics;

namespace BLD.WMIOperation;

public static class EventLogHelper
{
	private static EventLog log = new EventLog();

	public static EventLog _EventLog
	{
		get
		{
			log.Source = "OSDEvents";
			return log;
		}
	}
}
