using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RyzenSmu;

internal static class SMUCommands
{
	public static List<(string, bool, uint)> commands;

	public static Smu RyzenAccess = new Smu(EnableDebug: false);

	public static void applySettings(string commandName, uint value)
	{
		uint[] Args = new uint[6];
		Args[0] = value;
		IEnumerable<(string, bool, uint)> enumerable = commands.Where(((string, bool, uint) c) => c.Item1 == commandName);
		if (enumerable.Count() > 0)
		{
			List<Task> list = new List<Task>();
			foreach (var command in enumerable)
			{
				list.Add(Task.Run(async () =>
				{
					if (command.Item2)
					{
						RyzenAccess.SendMp1(command.Item3, ref Args);
					}
					else
					{
						RyzenAccess.SendRsmu(command.Item3, ref Args);
					}
				}));
			}
			Task.WaitAll(list.ToArray());
			return;
		}
		throw new ArgumentException("Command '" + commandName + "' not found");
	}
}
