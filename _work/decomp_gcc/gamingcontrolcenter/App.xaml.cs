using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Universal_x86_Tuning_Utility.Scripts;
using Universal_x86_Tuning_Utility.Scripts.AMD_Backend;

namespace GamingControlCenter;

public partial class App : Application
{
	private static Mutex mutex = new Mutex(initiallyOwned: true, "JiaoLongPlus2.0");

	public static Mem_Timings memTimings = new Mem_Timings();

	public static void StopDoubleOpen()
	{
		if (!mutex.WaitOne(TimeSpan.Zero, exitContext: true))
		{
			MessageBox.Show("后台已有该程序,请查看任务栏");
			Application.Current.Shutdown();
		}
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		StopDoubleOpen();
		Family.setCpuFamily();
		if (Enumerable.Contains(e.Args, "--minimize"))
		{
			MainWindow mainWindow = new MainWindow();
			mainWindow.Show();
			mainWindow.WindowState = WindowState.Minimized;
			mainWindow.ShowInTaskbar = false;
			mainWindow.Hide();
		}
		else
		{
			new MainWindow().Show();
		}
	}
}
