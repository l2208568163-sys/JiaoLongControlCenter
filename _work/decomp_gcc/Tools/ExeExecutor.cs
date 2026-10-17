using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Tools;

public static class ExeExecutor
{
	[Flags]
	private enum ThreadAccess : uint
	{
		SUSPEND_RESUME = 2u
	}

	private const int PROCESS_ALL_ACCESS = 2035711;

	private static StringBuilder _output = new StringBuilder();

	private static readonly object _outputLock = new object();

	[DllImport("kernel32.dll")]
	private static extern nint OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(nint hObject);

	[DllImport("kernel32.dll")]
	private static extern uint SuspendThread(nint hThread);

	[DllImport("kernel32.dll")]
	private static extern uint ResumeThread(nint hThread);

	[DllImport("kernel32.dll")]
	private static extern nint OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

	public static void StopAce()
	{
		List<string> list = new List<string> { "SGuardSvc64" };
		Process[] processes = Process.GetProcesses();
		foreach (Process process in processes)
		{
			if (!list.Contains(process.ProcessName))
			{
				continue;
			}
			try
			{
				MessageBox.Show($"正在挂起进程: {process.ProcessName} (PID: {process.Id})");
				nint num = OpenProcess(2035711, bInheritHandle: false, process.Id);
				if (num == IntPtr.Zero)
				{
					Console.WriteLine($"无法打开进程 {process.Id}。请确保有管理员权限。");
					continue;
				}
				foreach (ProcessThread thread in process.Threads)
				{
					uint id = (uint)thread.Id;
					nint num2 = OpenThread(ThreadAccess.SUSPEND_RESUME, bInheritHandle: false, id);
					if (num2 != IntPtr.Zero)
					{
						SuspendThread(num2);
						CloseHandle(num2);
						continue;
					}
					MessageBox.Show($"  无法打开线程 {id}。");
				}
				CloseHandle(num);
			}
			catch (Exception ex)
			{
				MessageBox.Show("错误: " + ex.Message);
			}
		}
	}

	public static async Task<string> ExecuteExeAsync(string filePath, string arguments)
	{
		if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
		{
			throw new ArgumentException("指定的文件不存在。", "filePath");
		}
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = filePath,
			Arguments = arguments,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		};
		Process process = new Process();
		try
		{
			process.StartInfo = startInfo;
			process.OutputDataReceived += OnOutputDataReceived;
			process.ErrorDataReceived += OnErrorDataReceived;
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			await Task.Run(() =>
			{
				process.WaitForExit();
			});
			return _output.ToString();
		}
		finally
		{
			if (process != null)
			{
				((IDisposable)process).Dispose();
			}
		}
	}

	private static void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
	{
		if (!string.IsNullOrEmpty(e.Data))
		{
			lock (_outputLock)
			{
				_output.Append(e.Data).Append(Environment.NewLine);
			}
		}
	}

	private static void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
	{
		if (!string.IsNullOrEmpty(e.Data))
		{
			lock (_outputLock)
			{
				StringBuilder output = _output;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, output);
				handler.AppendLiteral("错误: ");
				handler.AppendFormatted(e.Data);
				output.Append(ref handler).Append(Environment.NewLine);
			}
		}
	}
}
