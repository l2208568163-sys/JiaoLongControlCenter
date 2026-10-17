using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using RyzenSmu;

namespace Universal_x86_Tuning_Utility.Scripts;

internal class RyzenAdj_To_UXTU
{
	[DllImport("powrprof.dll")]
	public static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);

	public static async void Translate(string _ryzenAdjString, bool isAutoReapply = false)
	{
		try
		{
			_ryzenAdjString = _ryzenAdjString.Substring(0, _ryzenAdjString.Length - 1);
			string[] source = _ryzenAdjString.Split(' ');
			source = source.Distinct().ToArray();
			string[] array = source;
			foreach (string ryzenAdjCommand in array)
			{
				await Task.Run(() =>
				{
					try
					{
						string text = ryzenAdjCommand;
						if (!text.Contains("="))
						{
							text = ryzenAdjCommand + "=0";
						}
						string text2 = text.Split('=')[0].Replace("=", null).Replace("--", null);
						uint num = Convert.ToUInt32(text.Substring(ryzenAdjCommand.IndexOf('=') + 1));
						if (ryzenAdjCommand.Contains("skin"))
						{
							num *= 256;
						}
						if (num == 0 && !text2.Contains("co"))
						{
							SMUCommands.applySettings(text2, 0u);
						}
						else
						{
							SMUCommands.applySettings(text2, num);
						}
						Task.Delay(50);
					}
					catch (Exception)
					{
					}
				});
			}
		}
		catch (Exception)
		{
		}
	}
}
