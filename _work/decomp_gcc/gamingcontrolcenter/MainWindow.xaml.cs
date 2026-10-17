using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BLD.WMIOperation;
using EC.RW;
using GamingControlCenter.Dialogs;
using GamingControlCenter.Provider;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using NVIDIA;
using RyzenSmu;
using Universal_x86_Tuning_Utility.Scripts;

namespace GamingControlCenter;

public partial class MainWindow : Window, IComponentConnector
{
	public delegate int keyboardHookProc(int code, nint wParam, nint lParam);

	public enum HookType
	{
		WH_JOURNALRECORD,
		WH_JOURNALPLAYBACK,
		WH_KEYBOARD,
		WH_GETMESSAGE,
		WH_CALLWNDPROC,
		WH_CBT,
		WH_SYSMSGFILTER,
		WH_MOUSE,
		WH_HARDWARE,
		WH_DEBUG,
		WH_SHELL,
		WH_FOREGROUNDIDLE,
		WH_CALLWNDPROCRET,
		WH_KEYBOARD_LL,
		WH_MOUSE_LL
	}

	public delegate int HookProc(int nCode, nint wParam, nint lParam);

	[Serializable]
	public struct MSG
	{
		public nint hwnd;

		public nint lParam;

		public int message;

		public int pt_x;

		public int pt_y;

		public int time;

		public nint wParam;
	}

	private enum ACLineStatus : byte
	{
		Offline = 0,
		Online = 1,
		Unknown = byte.MaxValue
	}

	private enum BatteryFlag : byte
	{
		High = 1,
		Low = 2,
		Critical = 4,
		Charging = 8,
		NoSystemBattery = 128,
		Unknown = byte.MaxValue
	}

	private struct SystemPowerStatus
	{
		public ACLineStatus LineStatus;

		public BatteryFlag flgBattery;

		public byte BatteryLifePercent;

		public byte Reserved1;

		public int BatteryLifeTime;

		public int BatteryFullLifeTime;
	}

	[Serializable]
	private class AppSettings
	{
		public string Version { get; set; } = speedfileversion;

		public byte FastestModeFanSpeedSaveValue { get; set; } = 53;

		public byte PlayingModeFanSpeedSaveValue { get; set; } = 43;

		public byte WorkModeFanSpeedSaveValue { get; set; } = 27;

		public byte IsInCustomMode { get; set; }

		public byte CpuTempWallData { get; set; } = 99;

		public byte SplData { get; set; } = 101;

		public byte SpptData { get; set; } = 105;

		public int CoreClockOffsetData { get; set; } = 100;

		public string CpuFrequency { get; set; } = "3000";

		public string GpuFrequency { get; set; } = "2000";

		public bool ExeStart { get; set; }

		public bool CpuButtonStatus { get; set; }

		public bool GpuButtonStatus { get; set; }

		public List<int> TcListLocal { get; set; } = new List<int> { 2200, 3000, 3500, 4000, 4500, 5000, 5800 };

		public bool RainbowGradientButtonStatus { get; set; }

		public string CpuTemperatureWall { get; set; } = "99";

		public string CpuVoltage { get; set; } = "0";

		public string GpuFreqOC { get; set; } = "0";

		public string GpuMemoryOC { get; set; } = "0";

		public byte CPUPowerConsumptionValue { get; set; } = 45;

		public byte PLNumValue { get; set; } = 45;

		public bool TemperatureCurveAutoStart { get; set; }

		public bool DisableTurboBoost { get; set; }

		public bool TempWall { get; set; }

		public bool VoltageStatus { get; set; }

		public bool PLLStatus { get; set; }

		public bool SLLStatus { get; set; }

		public bool AutoModeStatus { get; set; }
	}

	public SystemUsage systemUsage = new SystemUsage();

	public static MainWindow mainWindowHandle;

	private CancellationTokenSource source = new CancellationTokenSource();

	private CancellationToken token;

	private CancellationTokenSource fan_source;

	private CancellationTokenSource led_source;

	private CancellationTokenSource auto_source;

	private UserControl1_Para CpuPoly_Para = new UserControl1_Para();

	private CollectPoint CpuPoly_collectPoint = new CollectPoint();

	private double CpuPoly_x;

	private UserControl1_Para Gpu1Poly_Para = new UserControl1_Para();

	private CollectPoint Gpu1Poly_collectPoint = new CollectPoint();

	private double Gpu1Poly_x;

	private UserControl1_Para Gpu2Poly_Para = new UserControl1_Para();

	private CollectPoint Gpu2Poly_collectPoint = new CollectPoint();

	private double Gpu2Poly_x;

	private static nint hHook;

	private readonly byte fastestMode_FanSpeed_MaxValue = 58;

	private readonly byte fastestMode_FanSpeed_MinValue = 50;

	private readonly byte playingMode_FanSpeed_MaxValue = 50;

	private readonly byte playingMode_FanSpeed_MinValue = 35;

	private readonly byte workMode_FanSpeed_MaxValue = 35;

	private readonly byte workMode_FanSpeed_MinValue = 22;

	private byte fastestMode_FanSpeed_SaveValue = 53;

	private byte playingMode_FanSpeed_SaveValue = 43;

	private byte workMode_FanSpeed_SaveValue = 27;

	private byte isInCustomMode;

	private byte cpuTempWallData = 99;

	private byte splData = 101;

	private byte spptData = 105;

	private string cpuFrequency = "3000";

	private string gpuFrequency = "2000";

	private string cpuTemperatureWall = "99";

	private string cpuVoltage = "0";

	private string gpuFreqOC = "0";

	private string gpuMemoryOC = "0";

	private bool exeStart;

	private bool cpuButtonStatus;

	private bool gpuButtonStatus;

	private bool rainbowGradientButtonStatus;

	private bool disableTurboBoostStatus;

	private bool tempWallStatus;

	private bool voltageStatus;

	private bool pLLStatus;

	private bool sLLStatus;

	private bool temperatureCurveAutoStartStatus;

	private bool autoModeStatus;

	private List<int> tcListLocal = new List<int> { 2200, 3000, 3500, 4000, 4500, 5000, 5800 };

	private int coreClockOffsetData = 100;

	private int cpu_r = 9;

	public static bool isFirstSetMode = true;

	private static string speedfileversion = "0.3.10.0";

	public static UTF8Encoding _encode = new UTF8Encoding();

	private AppSettings _appSettings = new AppSettings();

	private static keyboardHookProc callbackDelegate;

	private const int WM_KEYDOWN = 256;

	private const int WM_KEYUP = 257;

	private const int VK_CAPITAL = 20;

	private const int VK_NUMLOCK = 144;

	private int SetKeyboardLightBrightnessToZeroStep;

	private bool NumLockToggleButton_Set;

	private bool CapsLockToggleButton_Set;

	private bool PCAmbientlight_Set;

	private bool FnLock_Set;

	private bool TPLockButton_Set;

	private bool IndependentConnection_Set;

	private const int BreathDurationSeconds = 5;

	private const int UpdateIntervalMilliseconds = 50;

	private WMISystemPerMode SystemPerMode = WMISystemPerMode.Unknow;

	private int original_FanSpeed;

	private int gpu_power_usage;

	private bool grb_flag;

	private VariableMonitor monitor;

	private WMISystemPerMode autoMode = WMISystemPerMode.QuietMode;

	[DllImport("KaronOC.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern int ChangePstatesLevel0Settings(int grClockOffset, int memClockOffset);

	[DllImport("KaronOC.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern int GetPstatesLevel0Settings(ref int grClockOffset, ref int memClockOffset);

	public MainWindow()
	{
		InitializeComponent();
		mainWindowHandle = this;
		ECRW_Tool.Fan_Init();
		SystemEvents.SessionSwitch += OnSessionSwitch;
		// 桥接控制层：连接 JiaoLongCCUBridge，挂接 UI 开关事件（SPL/SPPT/睿频/降压等走桥接协议）
		JiaoLongBridge.Start();
		JiaoLongBridge.HomeStatusChanged += OnBridgeHomeStatus;
		HookBridgeControls();
		// 初始化期间 UI 回填会触发开关事件，先静默；启动完成（含 Init_SettingsAsync 回填）后解除
		_bridgeSyncing = true;
		Dispatcher.BeginInvoke(new System.Action(delegate
		{
			_bridgeSyncing = false;
		}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
	}

	private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
	{
		switch (e.Reason)
		{
		case SessionSwitchReason.SessionLock:
			original_FanSpeed = workMode_FanSpeed_SaveValue;
			SystemPerMode = WMIMethodServices.GetValue<WMISystemPerMode>(WMIMethodName.SystemPerMode);
			Set_FanSpeed(22);
			WMIMethodServices.SetValue(WMIMethodName.SystemPerMode, WMISystemPerMode.QuietMode);
			NvTuning.SetTuro(flag: true);
			break;
		case SessionSwitchReason.SessionUnlock:
			Set_FanSpeed((byte)original_FanSpeed);
			WMIMethodServices.SetValue(WMIMethodName.SystemPerMode, SystemPerMode);
			NvTuning.SetTuro(flag: false);
			break;
		}
	}

	private void NotifyIco_MouseDoubleClick(object sender, RoutedEventArgs e)
	{
		Show();
		WindowState = WindowState.Normal;
		ShowInTaskbar = true;
		Activate();
	}

	private void Show_Click(object sender, RoutedEventArgs e)
	{
		Show();
		WindowState = WindowState.Normal;
		ShowInTaskbar = true;
		Activate();
	}

	private void MinimizeButton_Click(object sender, RoutedEventArgs e)
	{
		WindowState = WindowState.Minimized;
		ShowInTaskbar = false;
		Hide();
	}

	public int LowLevelKeyboardProc(int nCode, nint wParam, nint lParam)
	{
		try
		{
			if (nCode >= 0 && wParam == 256)
			{
				int num = Marshal.ReadInt32(lParam);
				if (num == 20)
				{
					if (CapsLockStatus())
					{
						LogHelper.WriteLog("caps off");
						mainWindowHandle.Dispatcher.Invoke(() =>
						{
							if (mainWindowHandle.CapsLockToggleButton.IsChecked == true)
							{
								mainWindowHandle.CapsLockToggleButton_Set = true;
								mainWindowHandle.CapsLockToggleButton.IsChecked = false;
							}
						});
					}
					else
					{
						LogHelper.WriteLog("caps on");
						mainWindowHandle.Dispatcher.Invoke(() =>
						{
							if (mainWindowHandle.CapsLockToggleButton.IsChecked != true)
							{
								mainWindowHandle.CapsLockToggleButton_Set = true;
								mainWindowHandle.CapsLockToggleButton.IsChecked = true;
							}
						});
					}
				}
				if (num == 144)
				{
					if (NumLockStatus())
					{
						LogHelper.WriteLog("nums off");
						mainWindowHandle.Dispatcher.Invoke(() =>
						{
							if (mainWindowHandle.NumLockToggleButton.IsChecked == true)
							{
								mainWindowHandle.NumLockToggleButton_Set = true;
								mainWindowHandle.NumLockToggleButton.IsChecked = false;
							}
						});
					}
					else
					{
						LogHelper.WriteLog("nums on");
						mainWindowHandle.Dispatcher.Invoke(() =>
						{
							if (mainWindowHandle.NumLockToggleButton.IsChecked != true)
							{
								mainWindowHandle.NumLockToggleButton_Set = true;
								mainWindowHandle.NumLockToggleButton.IsChecked = true;
							}
						});
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("LowLevelKeyboardProc", ex);
		}
		return CallNextHookEx(hHook, nCode, wParam, lParam);
	}

	[DllImport("user32.dll")]
	private static extern nint SetWindowsHookEx(HookType hookType, keyboardHookProc callback, nint hInstance, uint threadId);

	[DllImport("user32.dll")]
	private static extern int CallNextHookEx(nint hHook, int nCode, nint wParam, nint lParam);

	[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
	public static extern short GetKeyState(int keyCode);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

	public static bool SetCapsLock()
	{
		keybd_event(20, 69, 1u, 0u);
		keybd_event(20, 69, 3u, 0u);
		return true;
	}

	public static bool SetNumLock()
	{
		keybd_event(144, 69, 1u, 0u);
		keybd_event(144, 69, 3u, 0u);
		return true;
	}

	public static bool CapsLockStatus()
	{
		return (GetKeyState(20) & 0xFFFF) != 0;
	}

	public static bool NumLockStatus()
	{
		return (GetKeyState(144) & 0xFFFF) != 0;
	}

	[DllImport("user32.dll")]
	public static extern bool GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	public static extern bool TranslateMessage([In] ref MSG lpMsg);

	[DllImport("user32.dll")]
	public static extern nint DispatchMessage([In] ref MSG lpmsg);

	public bool IsWindowHandleVaild()
	{
		return mainWindowHandle != null;
	}

	public bool SetAP_CPUInfos()
	{
		mainWindowHandle.Dispatcher.BeginInvoke((System.Action)(() =>
		{
			string pcinformations = "";
			int PhysicsCores = 0;
			PCInfoFormat.GetCPUName(out pcinformations);
			PCInfoFormat.GetCPUPhysicsCores(out PhysicsCores);
			CPUName.Text = pcinformations;
			CPUCorecount.Text = PhysicsCores.ToString();
		}));
		return true;
	}

	public bool SetAP_GPU1Infos()
	{
		if (!IsWindowHandleVaild())
		{
			return false;
		}
		mainWindowHandle.Dispatcher.BeginInvoke((System.Action)(() =>
		{
			string pcinformations = "";
			PCInfoFormat.GetMainGPUName(out pcinformations);
			if (pcinformations != "")
			{
				GPUName.Text = pcinformations;
			}
		}));
		return true;
	}

	public bool SetAP_CPUPoly_Init()
	{
		CpuPoly_Para.xGrap = 2;
		CpuPoly_Para.xGrapLable = 100;
		CpuPoly_Para.xGrapLablePoint = CpuPoly_Para.xGrapLable / CpuPoly_Para.xGrap;
		CpuPoly_Para.xWidth = CPUPOLY.Width - 50.0;
		CpuPoly_Para.yGrap = -10.0;
		CpuPoly_Para.yStartValue = 100.0;
		CpuPoly_Para.yGrapLable = 100;
		CpuPoly_Para.yLables = 0;
		CPUPOLY.myCanvasInit(CpuPoly_Para);
		if (!PCInfoFormat.GetInit())
		{
			PCInfoFormat.InitComputerInformation();
		}
		return true;
	}

	public bool SetAP_CPUPoly()
	{
		if (!IsWindowHandleVaild())
		{
			return false;
		}
		System.Threading.Tasks.Task.Run(async () =>
		{
			_ = 1;
			try
			{
				while (!token.IsCancellationRequested)
				{
					systemUsage.UpInfo(systemUsage);
					await mainWindowHandle.Dispatcher.BeginInvoke((System.Action)(() =>
					{
						SetCPUUsage((int)systemUsage.cpu_usage_rate);
						SetCPUCurrentFreq(systemUsage.cpu_freq);
						SetRamUsage(systemUsage.ram_usage);
						int aP_GPU1Poly = (int)Math.Round(systemUsage.gpu_usage_rate, MidpointRounding.AwayFromZero);
						SetAP_GPU1Poly(aP_GPU1Poly);
						gpu_power_usage = systemUsage.gpu_power_usage;
					}));
					await System.Threading.Tasks.Task.Delay(1000);
				}
			}
			catch (Exception ex)
			{
				LogHelper.WriteLog("SetAP_CPUPoly", ex);
			}
		}, token);
		return true;
	}

	public bool SetAP_MEMPoly_Init()
	{
		if (!PCInfoFormat.GetInit())
		{
			PCInfoFormat.InitComputerInformation();
		}
		return true;
	}

	public bool Set_CPUNUM_Init()
	{
		if (!PCInfoFormat.GetInit())
		{
			PCInfoFormat.InitComputerInformation();
			int cpuCoreCount = PCInfoFormat.getCpuCoreCount();
			SetCPUCoreCount(cpuCoreCount);
		}
		return true;
	}

	public bool SetAP_MEMInfos()
	{
		if (!IsWindowHandleVaild())
		{
			return false;
		}
		mainWindowHandle.Dispatcher.BeginInvoke((System.Action)(() =>
		{
			string pcinformations = "";
			PCInfoFormat.GetMemName(out pcinformations);
			if (pcinformations != "")
			{
				RAMName.Text = pcinformations;
			}
			string pcinformations2 = "";
			PCInfoFormat.GetMemFreq(out pcinformations2);
			if (pcinformations2 != "")
			{
				RAMFrequence.Text = pcinformations2;
			}
		}));
		return true;
	}

	public bool SetAP_DISKPoly_Init()
	{
		if (!PCInfoFormat.GetInit())
		{
			PCInfoFormat.InitComputerInformation();
		}
		return true;
	}

	public bool SetAP_DISKInfos()
	{
		if (!IsWindowHandleVaild())
		{
			return false;
		}
		mainWindowHandle.Dispatcher.BeginInvoke((System.Action)(() =>
		{
			int totalsize = 0;
			int freesize = 0;
			systemUsage.getCurrentDiskusage(out freesize, out totalsize);
			if (freesize != 0)
			{
				R0MTotalStorage.Text = totalsize.ToString();
			}
		}));
		return true;
	}

	public bool SetAP_CostomMode()
	{
		if (!IsWindowHandleVaild())
		{
			return false;
		}
		ManagementBaseObject managementBaseObject = new ManagementObjectSearcher("select * from Win32_Processor").Get().Cast<ManagementBaseObject>().FirstOrDefault();
		if (managementBaseObject != null)
		{
			string text = (string)managementBaseObject["Name"];
			LogHelper.WriteLog("Init_SettingsAsync", "Process name:" + text);
			if (text.Contains(" 7 "))
			{
				cpu_r = 7;
				fastestMode_FanSpeed_SaveValue = 53;
				if (splData > 85)
				{
					splData = 82;
				}
				mainWindowHandle.Dispatcher.BeginInvoke((System.Action)(() =>
				{
					mainWindowHandle.SPLSlider.Minimum = 75.0;
					mainWindowHandle.SPLSlider.Maximum = 85.0;
					mainWindowHandle.SPLSlider.Value = 82.0;
				}));
			}
			else if (text.Contains(" 9 "))
			{
				fastestMode_FanSpeed_SaveValue = 58;
			}
		}
		return true;
	}

	public bool SetAP_DISKPoly()
	{
		if (!IsWindowHandleVaild())
		{
			return false;
		}
		System.Threading.Tasks.Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				int freeSize = 0;
				int totalSize = 0;
				systemUsage.getCurrentDiskusage(out freeSize, out totalSize);
				await mainWindowHandle.Dispatcher.BeginInvoke((System.Action)(() =>
				{
					R0MCurrentStorage.Text = freeSize.ToString();
					int num = 100 - (int)((double)freeSize / (double)totalSize * 100.0);
					R0MProgressBar.Value = num;
					R0MTextBlock.Text = num + "%";
				}));
				await System.Threading.Tasks.Task.Delay(10000);
			}
		}, token);
		return true;
	}

	public bool SetAP_GPU1Poly_Init()
	{
		Gpu1Poly_Para.xGrap = 2;
		Gpu1Poly_Para.xGrapLable = 100;
		Gpu1Poly_Para.xGrapLablePoint = Gpu1Poly_Para.xGrapLable / Gpu1Poly_Para.xGrap;
		Gpu1Poly_Para.xWidth = GPU1POLY.Width - 50.0;
		Gpu1Poly_Para.yGrap = -10.0;
		Gpu1Poly_Para.yStartValue = 100.0;
		Gpu1Poly_Para.yGrapLable = 100;
		Gpu1Poly_Para.yLables = 0;
		GPU1POLY.myCanvasInit(Gpu1Poly_Para);
		Gpu2Poly_Para.xGrap = 2;
		Gpu2Poly_Para.xGrapLable = 100;
		Gpu2Poly_Para.xGrapLablePoint = Gpu2Poly_Para.xGrapLable / Gpu2Poly_Para.xGrap;
		Gpu2Poly_Para.xWidth = GPU2POLY.Width - 50.0;
		Gpu2Poly_Para.yGrap = -10.0;
		Gpu2Poly_Para.yStartValue = 100.0;
		Gpu2Poly_Para.yGrapLable = 100;
		Gpu2Poly_Para.yLables = 0;
		GPU2POLY.myCanvasInit(Gpu2Poly_Para);
		if (!PCInfoFormat.GetInit())
		{
			PCInfoFormat.InitComputerInformation();
		}
		return true;
	}

	public void Init_Keyboard_Event()
	{
		uint num = 0u;
		callbackDelegate = LowLevelKeyboardProc;
		num = 0u;
		while (true)
		{
			switch (num)
			{
			default:
				return;
			case 0u:
			case 1u:
			case 2u:
			case 3u:
			case 4u:
			case 5u:
			case 6u:
			case 7u:
			case 8u:
			case 9u:
				hHook = SetWindowsHookEx(HookType.WH_KEYBOARD_LL, callbackDelegate, IntPtr.Zero, 0u);
				if (hHook != IntPtr.Zero)
				{
					return;
				}
				break;
			case 10u:
				MessageBox.Show("Keyboard hook not set!");
				return;
			}
			Thread.Sleep(1000);
			num++;
		}
	}

	public void Init_Keyboard_State()
	{
		if (NumLockStatus())
		{
			LogHelper.WriteLog("Init_Keyboard_State", "nums on");
			if (NumLockToggleButton.IsChecked == false)
			{
				NumLockToggleButton.IsChecked = true;
			}
		}
		else
		{
			LogHelper.WriteLog("Init_Keyboard_State", "nums off");
			if (NumLockToggleButton.IsChecked == true)
			{
				NumLockToggleButton.IsChecked = false;
			}
		}
		if (CapsLockStatus())
		{
			LogHelper.WriteLog("Init_Keyboard_State", "caps on");
			if (CapsLockToggleButton.IsChecked == false)
			{
				CapsLockToggleButton.IsChecked = true;
			}
		}
		else
		{
			LogHelper.WriteLog("Init_Keyboard_State", "caps on");
			if (CapsLockToggleButton.IsChecked == true)
			{
				CapsLockToggleButton.IsChecked = false;
			}
		}
	}

	public async Task<bool> Init_SettingsAsync()
	{
		if (!IsWindowHandleVaild())
		{
			return false;
		}
		await System.Threading.Tasks.Task.Run(() =>
		{
			ReadFromFile_AppMachineInfo();
			if (cpuTempWallData < 44 || cpuTempWallData > 100)
			{
				cpuTempWallData = 45;
			}
			if (cpu_r == 9 && (splData < 95 || splData > 105))
			{
				splData = 101;
			}
			if (cpu_r == 7 && (splData < 75 || splData > 85))
			{
				splData = 82;
			}
			if (spptData < 105 || spptData > 120)
			{
				spptData = 105;
			}
			if (coreClockOffsetData < 0 || coreClockOffsetData > 200)
			{
				coreClockOffsetData = 100;
			}
			WMICPUPower CustomMode = WMIMethodServices.GetValue<WMICPUPower>(WMIMethodName.CPUPower);
			LogHelper.WriteLog("Init_SettingsAsync2", "CustomMode:" + CustomMode);
			mainWindowHandle.Dispatcher.BeginInvoke((System.Action)(() =>
			{
				if (CustomMode == WMICPUPower.OpenState)
				{
					isInCustomMode = 1;
					mainWindowHandle.PerfsettingPageHome.Visibility = Visibility.Hidden;
					mainWindowHandle.PerfsettingPage1.Visibility = Visibility.Visible;
				}
				mainWindowHandle.CPUTempWallSlider.Value = (int)cpuTempWallData;
				mainWindowHandle.SPLSlider.Value = (int)splData;
				mainWindowHandle.SPPTSlider.Value = (int)spptData;
				mainWindowHandle.GPUCoreClockSlider.Value = coreClockOffsetData;
				mainWindowHandle.CPUFrequency.Text = NvTuning.GetShowCPUMHz();
				mainWindowHandle.GPUFrequency.Text = gpuFrequency;
				mainWindowHandle.TemperatureWall.Text = cpuTemperatureWall;
				mainWindowHandle.CPUVoltage.Text = cpuVoltage;
				mainWindowHandle.GPUFreqOC.Text = gpuFreqOC;
				mainWindowHandle.GPUMemoryOC.Text = gpuMemoryOC;
				mainWindowHandle.CPU_Power_Consumption.Text = _appSettings.CPUPowerConsumptionValue.ToString();
				mainWindowHandle.PLNum.Text = _appSettings.PLNumValue.ToString();
				mainWindowHandle.RGB_Status.IsChecked = rainbowGradientButtonStatus;
				mainWindowHandle.StartUpButton.IsChecked = exeStart;
				mainWindowHandle.CPUFrequencyRegulation.IsChecked = cpuButtonStatus;
				mainWindowHandle.GPUFrequencyRegulation.IsChecked = gpuButtonStatus;
				mainWindowHandle.TemperatureCurveAutoStart.IsChecked = temperatureCurveAutoStartStatus;
				mainWindowHandle.AutoMode.IsChecked = autoModeStatus;
				mainWindowHandle.DisableTurboBoost.IsChecked = disableTurboBoostStatus;
				mainWindowHandle.TempWall.IsChecked = tempWallStatus;
				mainWindowHandle.VoltageStatus.IsChecked = voltageStatus;
				mainWindowHandle.PLLStatus.IsChecked = pLLStatus;
				mainWindowHandle.SLLStatus.IsChecked = sLLStatus;
				if (cpuButtonStatus)
				{
					NvTuning.SetShowCPUMHz(int.Parse(CPUFrequency.Text), flag: true);
				}
				if (gpuButtonStatus)
				{
					NvTuning.SetMaxGPUClock(int.Parse(GPUFrequency.Text));
				}
				if (rainbowGradientButtonStatus)
				{
					RainbowGradientRGB();
				}
				if (temperatureCurveAutoStartStatus)
				{
					RunTempCurve();
				}
				if (autoModeStatus)
				{
					Auto_Run();
				}
				mainWindowHandle.tc50.Value = tcListLocal[0];
				mainWindowHandle.tc60.Value = tcListLocal[1];
				mainWindowHandle.tc70.Value = tcListLocal[2];
				mainWindowHandle.tc80.Value = tcListLocal[3];
				mainWindowHandle.tc90.Value = tcListLocal[4];
				mainWindowHandle.tc100.Value = tcListLocal[5];
				mainWindowHandle.PowerMode.Text = NvTuning.GetPowermanagement();
				mainWindowHandle.LimitCore.Text = NvTuning.GetCpuCore();
			}));
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			WMIGPUMode GPUMode = WMIMethodServices.GetValue<WMIGPUMode>(WMIMethodName.GPUMode);
			LogHelper.WriteLog("Init_SettingsAsync", "GPUMode:" + GPUMode);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if (GPUMode == WMIGPUMode.DiscreteMode)
				{
					mainWindowHandle.IndependentConnection_Set = true;
					mainWindowHandle.IndependentConnection.IsChecked = true;
				}
				else
				{
					mainWindowHandle.IndependentConnection.IsChecked = false;
				}
			});
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			WMISystemPerMode SystemPerMode = WMIMethodServices.GetValue<WMISystemPerMode>(WMIMethodName.SystemPerMode);
			WMIMethodServices.GetValue<WMISystemPerMode>(WMIMethodName.MaxFanSpeed);
			LogHelper.WriteLog("Init_SettingsAsync", "SystemPerMode:" + SystemPerMode);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				SetAP_PerformaceMode(SystemPerMode);
			});
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			WMIResultState Ambientlight = WMIMethodServices.GetValue<WMIResultState>(WMIMethodName.Ambientlight);
			LogHelper.WriteLog("Init_SettingsAsync", "Ambientlight:" + Ambientlight);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if (Ambientlight == WMIResultState.ON)
				{
					PCAmbientlight_Set = true;
					PCAmbientlight.IsChecked = true;
				}
				else
				{
					PCAmbientlight.IsChecked = false;
				}
			});
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			bool _osdStateButton = false;
			if (new ServiceController("BLDHotKeyService").Status != ServiceControllerStatus.Running)
			{
				_osdStateButton = true;
			}
			LogHelper.WriteLog("Init_SettingsAsync", "osdStateButton:" + _osdStateButton);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if (_osdStateButton)
				{
					OSDIconButton.IsChecked = true;
				}
				else
				{
					OSDIconButton.IsChecked = false;
				}
			});
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			WMIResultState TPLock = WMIMethodServices.GetValue<WMIResultState>(WMIMethodName.TPLock);
			LogHelper.WriteLog("Init_SettingsAsync", "TPLock:" + TPLock);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if (TPLock == WMIResultState.ON)
				{
					TPLockButton_Set = true;
					TPLockButton.IsChecked = true;
				}
				else
				{
					TPLockButton.IsChecked = false;
				}
			});
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			WMIResultState _FnLock = WMIMethodServices.GetValue<WMIResultState>(WMIMethodName.FnLock);
			LogHelper.WriteLog("Init_SettingsAsync", "_FnLock:" + _FnLock);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if (_FnLock == WMIResultState.ON)
				{
					FnLock_Set = true;
					FnLock.IsChecked = true;
				}
				else
				{
					FnLock.IsChecked = false;
				}
			});
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			WMIRGBKeyboardBrightnessLevel _RGBKeyboardBrightness = WMIMethodServices.GetValue<WMIRGBKeyboardBrightnessLevel>(WMIMethodName.RGBKeyboardBrightness);
			LogHelper.WriteLog("Init_SettingsAsync", "_RGBKeyboardBrightness:" + _RGBKeyboardBrightness);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if (_RGBKeyboardBrightness == WMIRGBKeyboardBrightnessLevel.Level_0)
				{
					mainWindowHandle.KeyboardLightBrightness.Value = 0.0;
				}
				else if (_RGBKeyboardBrightness == WMIRGBKeyboardBrightnessLevel.Level_1)
				{
					mainWindowHandle.KeyboardLightBrightness.Value = 1.0;
				}
				else if (_RGBKeyboardBrightness == WMIRGBKeyboardBrightnessLevel.Level_2)
				{
					mainWindowHandle.KeyboardLightBrightness.Value = 2.0;
				}
				else
				{
					mainWindowHandle.KeyboardLightBrightness.Value = 3.0;
				}
			});
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			Tuple<int, int, int> value = WMIMethodServices.GetValue<Tuple<int, int, int>>(WMIMethodName.RGBKeyboardColor);
			float num = value.Item1;
			float num2 = value.Item2;
			float num3 = value.Item3;
			float[] hsb = ColorConversionHelper.RGB2HSB(new float[3] { num, num2, num3 });
			LogHelper.WriteLog("Init_SettingsAsync", "_RGBKeyboardColor_H: " + (int)hsb[0]);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				mainWindowHandle.KeyboardRGBColor.Value = hsb[0];
			});
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			WMIRGBKeyboardMode _RGBKeyboardMode = WMIMethodServices.GetValue<WMIRGBKeyboardMode>(WMIMethodName.RGBKeyboardMode);
			LogHelper.WriteLog("Init_SettingsAsync", "_RGBKeyboardMode:" + _RGBKeyboardMode);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if (_RGBKeyboardMode == WMIRGBKeyboardMode.Mode_RGBAutoCyclic)
				{
					mainWindowHandle.KeyboardRGBColor.IsEnabled = false;
					mainWindowHandle.KeyboardRGBColor.Opacity = 0.4;
				}
				else if (_RGBKeyboardMode == WMIRGBKeyboardMode.Mode_RGBFixedMode)
				{
					mainWindowHandle.KeyboardRGBColor.IsEnabled = true;
					mainWindowHandle.KeyboardRGBColor.Opacity = 1.0;
				}
			});
		});
		await System.Threading.Tasks.Task.Run(() =>
		{
			WMISystemPerMode value = WMIMethodServices.GetValue<WMISystemPerMode>(WMIMethodName.MaxFanSpeed);
			LogHelper.WriteLog("Init_SettingsAsync", "MaxFanSpeed:" + value);
			string _currentstring = value.ToString();
			byte _currentvalue = Convert.ToByte(value);
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if (!FastestMode.IsEnabled || mainWindowHandle.PerfsettingPage1.Visibility == Visibility)
				{
					if (_currentvalue <= fastestMode_FanSpeed_MinValue || _currentvalue > fastestMode_FanSpeed_MaxValue)
					{
						_currentstring = fastestMode_FanSpeed_SaveValue.ToString();
					}
				}
				else if (!PlayingMode.IsEnabled)
				{
					if (_currentvalue <= playingMode_FanSpeed_MinValue || _currentvalue >= playingMode_FanSpeed_MaxValue)
					{
						_currentstring = playingMode_FanSpeed_SaveValue.ToString();
					}
				}
				else if (!WorkMode.IsEnabled && (_currentvalue < workMode_FanSpeed_MinValue || _currentvalue >= workMode_FanSpeed_MaxValue))
				{
					_currentstring = workMode_FanSpeed_SaveValue.ToString();
				}
				MaxFanSpeed.Text = _currentstring;
				mainWindowHandle.MaxFanSpeedSlider.Value = (int)Set_MaxFanSpeedSlider();
			});
		});
		await System.Threading.Tasks.Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				Tuple<int, int> _CPUGPUFanSpeed = WMIMethodServices.GetValue<Tuple<int, int>>(WMIMethodName.CPUGPUFanSpeed);
				LogHelper.WriteLog("Init_SettingsAsync", "_CPUGPUFanSpeed:" + _CPUGPUFanSpeed.ToString());
				if (_CPUGPUFanSpeed.Item1 >= 0)
				{
					mainWindowHandle.Dispatcher.Invoke(() =>
					{
						FanSpeed.Text = _CPUGPUFanSpeed.Item1 + " RPM";
						float num = (float)_CPUGPUFanSpeed.Item1 / 60f;
						FANProgressBar.Value = (int)Math.Round(num);
						FANTextBlock.Text = num.ToString("F0") + "%";
					});
				}
				await System.Threading.Tasks.Task.Delay(3000);
			}
		}, token);
		return true;
	}

	private void ReadFromFile_AppMachineInfo()
	{
		try
		{
			string appSettingsFilePath = GetAppSettingsFilePath();
			if (File.Exists(appSettingsFilePath))
			{
				AppSettings appSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(appSettingsFilePath));
				if (appSettings != null)
				{
					_appSettings = appSettings;
					fastestMode_FanSpeed_SaveValue = appSettings.FastestModeFanSpeedSaveValue;
					playingMode_FanSpeed_SaveValue = appSettings.PlayingModeFanSpeedSaveValue;
					workMode_FanSpeed_SaveValue = appSettings.WorkModeFanSpeedSaveValue;
					isInCustomMode = appSettings.IsInCustomMode;
					cpuTempWallData = appSettings.CpuTempWallData;
					splData = appSettings.SplData;
					spptData = appSettings.SpptData;
					coreClockOffsetData = appSettings.CoreClockOffsetData;
					cpuFrequency = appSettings.CpuFrequency;
					gpuFrequency = appSettings.GpuFrequency;
					exeStart = appSettings.ExeStart;
					cpuButtonStatus = appSettings.CpuButtonStatus;
					gpuButtonStatus = appSettings.GpuButtonStatus;
					tcListLocal = new List<int>(appSettings.TcListLocal);
					rainbowGradientButtonStatus = appSettings.RainbowGradientButtonStatus;
					cpuTemperatureWall = appSettings.CpuTemperatureWall;
					cpuVoltage = appSettings.CpuVoltage;
					gpuFreqOC = appSettings.GpuFreqOC;
					gpuMemoryOC = appSettings.GpuMemoryOC;
					temperatureCurveAutoStartStatus = appSettings.TemperatureCurveAutoStart;
					autoModeStatus = appSettings.AutoModeStatus;
					disableTurboBoostStatus = appSettings.DisableTurboBoost;
					tempWallStatus = appSettings.TempWall;
					voltageStatus = appSettings.VoltageStatus;
					pLLStatus = appSettings.PLLStatus;
					sLLStatus = appSettings.SLLStatus;
				}
			}
			else
			{
				SaveAppSettings();
			}
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("ReadFromFile_AppMachineInfo", ex.Message);
			SaveAppSettings();
		}
	}

	private void SaveToFile_AppMachineInfo(byte savedValue, int rowNum)
	{
		try
		{
			switch (rowNum)
			{
			case 1:
				_appSettings.FastestModeFanSpeedSaveValue = savedValue;
				fastestMode_FanSpeed_SaveValue = savedValue;
				break;
			case 2:
				_appSettings.PlayingModeFanSpeedSaveValue = savedValue;
				playingMode_FanSpeed_SaveValue = savedValue;
				break;
			case 3:
				_appSettings.WorkModeFanSpeedSaveValue = savedValue;
				workMode_FanSpeed_SaveValue = savedValue;
				break;
			case 4:
				_appSettings.IsInCustomMode = savedValue;
				isInCustomMode = savedValue;
				break;
			case 5:
				_appSettings.CpuTempWallData = savedValue;
				cpuTempWallData = savedValue;
				break;
			case 6:
				_appSettings.SplData = savedValue;
				splData = savedValue;
				break;
			case 7:
				_appSettings.SpptData = savedValue;
				spptData = savedValue;
				break;
			}
			SaveAppSettings();
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("SaveToFile_AppMachineInfo", ex.Message);
		}
	}

	private void SaveToFile_AppMachineInfo(List<int> savedValue, int rowNum)
	{
		try
		{
			if (rowNum == 14)
			{
				_appSettings.TcListLocal = new List<int>(savedValue);
				tcListLocal = new List<int>(savedValue);
			}
			SaveAppSettings();
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("SaveToFile_AppMachineInfo", ex.Message);
		}
	}

	private void SaveToFile_AppMachineInfo(string savedValue, int rowNum)
	{
		try
		{
			switch (rowNum)
			{
			case 9:
				_appSettings.CpuFrequency = savedValue;
				cpuFrequency = savedValue;
				break;
			case 10:
				_appSettings.GpuFrequency = savedValue;
				gpuFrequency = savedValue;
				break;
			case 16:
				_appSettings.CpuTemperatureWall = savedValue;
				cpuTemperatureWall = savedValue;
				break;
			case 17:
				_appSettings.CpuVoltage = savedValue;
				cpuVoltage = savedValue;
				break;
			case 18:
				_appSettings.GpuFreqOC = savedValue;
				gpuFreqOC = savedValue;
				break;
			case 19:
				_appSettings.GpuMemoryOC = savedValue;
				gpuMemoryOC = savedValue;
				break;
			}
			SaveAppSettings();
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("SaveToFile_AppMachineInfo", ex.Message);
		}
	}

	private void SaveToFile_AppMachineInfo(byte[] savedValue, int rowNum)
	{
		try
		{
			for (int i = 0; i < savedValue.Length; i++)
			{
				SaveToFile_AppMachineInfo(savedValue[i], rowNum + i);
			}
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("SaveToFile_AppMachineInfo", ex.Message);
		}
	}

	private void SaveAppSettings()
	{
		try
		{
			string appSettingsFilePath = GetAppSettingsFilePath();
			_appSettings.Version = speedfileversion;
			_appSettings.FastestModeFanSpeedSaveValue = fastestMode_FanSpeed_SaveValue;
			_appSettings.PlayingModeFanSpeedSaveValue = playingMode_FanSpeed_SaveValue;
			_appSettings.WorkModeFanSpeedSaveValue = workMode_FanSpeed_SaveValue;
			_appSettings.IsInCustomMode = isInCustomMode;
			_appSettings.CpuTempWallData = cpuTempWallData;
			_appSettings.SplData = splData;
			_appSettings.SpptData = spptData;
			_appSettings.CoreClockOffsetData = coreClockOffsetData;
			_appSettings.CpuFrequency = cpuFrequency;
			_appSettings.GpuFrequency = gpuFrequency;
			_appSettings.ExeStart = exeStart;
			_appSettings.CpuButtonStatus = cpuButtonStatus;
			_appSettings.GpuButtonStatus = gpuButtonStatus;
			_appSettings.TcListLocal = new List<int>(tcListLocal);
			_appSettings.RainbowGradientButtonStatus = rainbowGradientButtonStatus;
			_appSettings.CpuTemperatureWall = cpuTemperatureWall;
			_appSettings.CpuVoltage = cpuVoltage;
			_appSettings.GpuFreqOC = gpuFreqOC;
			_appSettings.GpuMemoryOC = gpuMemoryOC;
			_appSettings.TemperatureCurveAutoStart = temperatureCurveAutoStartStatus;
			_appSettings.AutoModeStatus = autoModeStatus;
			_appSettings.DisableTurboBoost = disableTurboBoostStatus;
			_appSettings.TempWall = tempWallStatus;
			_appSettings.VoltageStatus = voltageStatus;
			_appSettings.PLLStatus = pLLStatus;
			_appSettings.SLLStatus = sLLStatus;
			if (mainWindowHandle != null && mainWindowHandle.CPU_Power_Consumption != null && !string.IsNullOrEmpty(mainWindowHandle.CPU_Power_Consumption.Text) && byte.TryParse(mainWindowHandle.CPU_Power_Consumption.Text, out var result))
			{
				_appSettings.CPUPowerConsumptionValue = result;
			}
			if (mainWindowHandle != null && mainWindowHandle.PLNum != null && !string.IsNullOrEmpty(mainWindowHandle.PLNum.Text) && byte.TryParse(mainWindowHandle.PLNum.Text, out var result2))
			{
				_appSettings.PLNumValue = result2;
			}
			if (File.Exists(appSettingsFilePath))
			{
				File.SetAttributes(appSettingsFilePath, File.GetAttributes(appSettingsFilePath) & ~FileAttributes.ReadOnly);
			}
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				WriteIndented = true
			};
			string contents = JsonSerializer.Serialize(_appSettings, options);
			File.WriteAllText(appSettingsFilePath, contents);
			File.SetAttributes(appSettingsFilePath, File.GetAttributes(appSettingsFilePath) | FileAttributes.ReadOnly);
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("SaveAppSettings", ex.Message);
		}
	}

	private string GetAppSettingsFilePath()
	{
		return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppSettings.json");
	}

	private string getFilePath_AppMachineInfo()
	{
		string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppMachineInfo");
		string appSettingsFilePath = GetAppSettingsFilePath();
		if (File.Exists(text) && !File.Exists(appSettingsFilePath))
		{
			try
			{
				MigrateFromOldFormat(text);
			}
			catch (Exception ex)
			{
				LogHelper.WriteLog("MigrationFailed", ex.Message);
			}
		}
		return appSettingsFilePath;
	}

	private void MigrateFromOldFormat(string oldPath)
	{
		if (!File.Exists(oldPath))
		{
			return;
		}
		try
		{
			string[] array = File.ReadAllLines(oldPath);
			File.SetAttributes(oldPath, File.GetAttributes(oldPath) & ~FileAttributes.ReadOnly);
			if (_encode.GetString(Convert.FromBase64String(array[0])) != speedfileversion)
			{
				SaveAppSettings();
				return;
			}
			fastestMode_FanSpeed_SaveValue = byte.Parse(_encode.GetString(Convert.FromBase64String(array[1])));
			playingMode_FanSpeed_SaveValue = byte.Parse(_encode.GetString(Convert.FromBase64String(array[2])));
			workMode_FanSpeed_SaveValue = byte.Parse(_encode.GetString(Convert.FromBase64String(array[3])));
			isInCustomMode = byte.Parse(_encode.GetString(Convert.FromBase64String(array[4])));
			cpuTempWallData = byte.Parse(_encode.GetString(Convert.FromBase64String(array[5])));
			splData = byte.Parse(_encode.GetString(Convert.FromBase64String(array[6])));
			spptData = byte.Parse(_encode.GetString(Convert.FromBase64String(array[7])));
			coreClockOffsetData = int.Parse(_encode.GetString(Convert.FromBase64String(array[8])));
			cpuFrequency = _encode.GetString(Convert.FromBase64String(array[9]));
			gpuFrequency = _encode.GetString(Convert.FromBase64String(array[10]));
			exeStart = bool.Parse(_encode.GetString(Convert.FromBase64String(array[11])));
			cpuButtonStatus = bool.Parse(_encode.GetString(Convert.FromBase64String(array[12])));
			gpuButtonStatus = bool.Parse(_encode.GetString(Convert.FromBase64String(array[13])));
			string text = _encode.GetString(Convert.FromBase64String(array[14]));
			tcListLocal = (from n in text.Split(',')
				select int.Parse(n)).ToList();
			rainbowGradientButtonStatus = bool.Parse(_encode.GetString(Convert.FromBase64String(array[15])));
			cpuTemperatureWall = _encode.GetString(Convert.FromBase64String(array[16]));
			cpuVoltage = _encode.GetString(Convert.FromBase64String(array[17]));
			gpuFreqOC = _encode.GetString(Convert.FromBase64String(array[18]));
			gpuMemoryOC = _encode.GetString(Convert.FromBase64String(array[19]));
			SaveAppSettings();
			string text2 = oldPath + ".bak";
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
			File.Move(oldPath, text2);
			LogHelper.WriteLog("Settings", "Successfully migrated settings from old format to new format");
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("MigrationFailedDetailed", ex.ToString());
			SaveAppSettings();
		}
	}

	public void SetAP_PerformaceMode(WMISystemPerMode m)
	{
		if (!isFirstSetMode)
		{
			updateFile__ModeToFanSpeed();
		}
		switch (m)
		{
		case WMISystemPerMode.BalanceMode:
			WorkMode.IsEnabled = true;
			FastestMode.IsEnabled = true;
			PlayingMode.IsEnabled = false;
			mainWindowHandle.ImagePerformanceMode.Source = new BitmapImage(new Uri("Assets/playing_194x194.png", UriKind.RelativeOrAbsolute));
			break;
		case WMISystemPerMode.PerformanceMode:
			WorkMode.IsEnabled = true;
			FastestMode.IsEnabled = false;
			PlayingMode.IsEnabled = true;
			mainWindowHandle.ImagePerformanceMode.Source = new BitmapImage(new Uri("Assets/fast_194x194.png", UriKind.RelativeOrAbsolute));
			break;
		case WMISystemPerMode.QuietMode:
			WorkMode.IsEnabled = false;
			FastestMode.IsEnabled = true;
			PlayingMode.IsEnabled = true;
			mainWindowHandle.ImagePerformanceMode.Source = new BitmapImage(new Uri("Assets/Working_194x194.png", UriKind.RelativeOrAbsolute));
			break;
		default:
			LogHelper.WriteLog("SetAP_PerformaceMode", "Error!!!Get Unknow PerformaceMode");
			break;
		}
		isFirstSetMode = false;
	}

	public void SetSP_PerformaceMode(WMISystemPerMode m)
	{
		updateUI_ModeToFanSpeed();
		LogHelper.WriteLog("SetSP_PerformaceMode", m.ToString());
		System.Threading.Tasks.Task.Run(() => WMIMethodServices.SetValue(WMIMethodName.SystemPerMode, m));
	}

	private void updateFile__ModeToFanSpeed()
	{
		byte b = Convert.ToByte(mainWindowHandle.MaxFanSpeed.Text);
		if (!FastestMode.IsEnabled || mainWindowHandle.PerfsettingPage1.Visibility == Visibility)
		{
			if (b >= 50 && b <= 58)
			{
				fastestMode_FanSpeed_SaveValue = b;
				SaveToFile_AppMachineInfo(b, 1);
			}
		}
		else if (!PlayingMode.IsEnabled)
		{
			if (b >= 35 && b <= 50)
			{
				playingMode_FanSpeed_SaveValue = b;
				SaveToFile_AppMachineInfo(b, 2);
			}
		}
		else if (!WorkMode.IsEnabled && b >= 22 && b <= 35)
		{
			workMode_FanSpeed_SaveValue = b;
			SaveToFile_AppMachineInfo(b, 3);
		}
	}

	private void updateUI_ModeToFanSpeed()
	{
		if (!FastestMode.IsEnabled)
		{
			Set_FanSpeed(fastestMode_FanSpeed_SaveValue);
		}
		else if (!PlayingMode.IsEnabled)
		{
			Set_FanSpeed(playingMode_FanSpeed_SaveValue);
		}
		else if (!WorkMode.IsEnabled)
		{
			Set_FanSpeed(workMode_FanSpeed_SaveValue);
		}
	}

	public void SetSP_TPMode(WMIResultState m)
	{
		LogHelper.WriteLog("SetSP_TPMode", m.ToString());
		System.Threading.Tasks.Task.Run(() => WMIMethodServices.SetValue(WMIMethodName.TPLock, m));
	}

	public void SetSP_Ambientlight(WMIResultState m)
	{
		LogHelper.WriteLog("SetSP_Ambientlight", m.ToString());
		System.Threading.Tasks.Task.Run(() => WMIMethodServices.SetValue(WMIMethodName.Ambientlight, m));
	}

	public void SetSP_FNLock(WMIResultState m)
	{
		LogHelper.WriteLog("SetSP_FNLock", m.ToString());
		System.Threading.Tasks.Task.Run(() => WMIMethodServices.SetValue(WMIMethodName.FnLock, m));
	}

	public void SetSP_GraphicsDirect(WMIGPUMode m)
	{
		LogHelper.WriteLog("SetSP_GraphicsDirect", m.ToString());
		System.Threading.Tasks.Task.Run(() => WMIMethodServices.SetValue(WMIMethodName.GPUMode, m));
	}

	private bool SetCPUCurrentFreq(double currentcpufreq)
	{
		CPUFrequence.Text = currentcpufreq.ToString("F2");
		return true;
	}

	private bool SetCPUUsage(int usage)
	{
		SetCPUPOLY(usage);
		SetCPUProgressbar(usage);
		SetCPUTextBlock(usage);
		return true;
	}

	private bool SetCPUCoreCount(int cpucorecount)
	{
		CPUCorecount.Text = cpucorecount.ToString();
		return true;
	}

	private bool SetRamUsage(double ramusage)
	{
		int num = (int)Math.Round(ramusage, MidpointRounding.AwayFromZero);
		RAMProgressBar.Value = num;
		RAMTextBlock.Text = num + "%";
		return true;
	}

	public bool SetAP_GPU1Poly(int gpuUsageFinal)
	{
		SetGPU1Usage(gpuUsageFinal);
		GPUThermometer.Text = systemUsage.gpu_temp.ToString();
		GPUFrequence.Text = systemUsage.gpu_freq.ToString("F2");
		CPUThermometer.Text = systemUsage.cpu_temp.ToString();
		GPUMemoryTotal.Text = Math.Round(systemUsage.gpu_memory_total).ToString("F1");
		return true;
	}

	private bool SetCPUPOLY(int usage)
	{
		CpuPoly_x++;
		CpuPoly_collectPoint.point = new Point(CpuPoly_x, usage);
		CpuPoly_collectPoint.curTime = DateTime.Now;
		CPUPOLY.AddPoint1(CpuPoly_collectPoint);
		return true;
	}

	private bool SetCPUProgressbar(int usage)
	{
		CPUProgressBar.Value = usage;
		return true;
	}

	private bool SetCPUTextBlock(int usage)
	{
		CPUTextBlock.Text = usage + "%";
		return true;
	}

	private bool SetGPU1Usage(int usage)
	{
		SetGPU1POLY(usage);
		SetGPU1Progressbar(usage);
		SetGPU1TextBlock(usage);
		return true;
	}

	private bool SetGPU1POLY(int usage)
	{
		Gpu1Poly_x++;
		Gpu1Poly_collectPoint.point = new Point(Gpu1Poly_x, usage);
		Gpu1Poly_collectPoint.curTime = DateTime.Now;
		GPU1POLY.AddPoint1(Gpu1Poly_collectPoint);
		return true;
	}

	private bool SetGPU1Progressbar(int usage)
	{
		GPU1ProgressBar.Value = usage;
		return true;
	}

	private bool SetGPU1TextBlock(int usage)
	{
		GPU1TextBlock.Text = usage + "%";
		return true;
	}

	private bool SetGPU2Usage(double usage)
	{
		SetGPU2POLY(usage);
		SetGPU2Progressbar(usage);
		SetGPU2TextBlock(usage);
		return true;
	}

	private bool SetGPU2POLY(double usage)
	{
		Gpu2Poly_x++;
		Gpu2Poly_collectPoint.point = new Point(Gpu2Poly_x, usage);
		Gpu2Poly_collectPoint.curTime = DateTime.Now;
		GPU2POLY.AddPoint1(Gpu2Poly_collectPoint);
		return true;
	}

	private bool SetGPU2Progressbar(double usage)
	{
		GPU2ProgressBar.Value = usage;
		return true;
	}

	private bool SetGPU2TextBlock(double usage)
	{
		GPU2TextBlock.Text = usage.ToString("F1") + "%";
		return true;
	}

	private bool SetRAMUsage(double usage)
	{
		SetRAMProgressbar(usage);
		SetRAMTextBlock(usage);
		return true;
	}

	private bool SetRAMProgressbar(double usage)
	{
		RAMProgressBar.Value = usage;
		return true;
	}

	private bool SetRAMTextBlock(double usage)
	{
		RAMTextBlock.Text = usage.ToString("F1") + "%";
		return true;
	}

	public void SetSP_KeyboardLightBrightness(byte keyboardLightBrightnessValue)
	{
		LogHelper.WriteLog("SetSP_KeyboardColor", "Level: " + keyboardLightBrightnessValue);
		System.Threading.Tasks.Task.Run(() =>
		{
			WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardBrightness, keyboardLightBrightnessValue);
		});
	}

	public void SetSP_KeyboardColorDefault()
	{
		mainWindowHandle.KeyboardRGBColor.IsEnabled = false;
		mainWindowHandle.KeyboardRGBColor.Opacity = 0.4;
		LogHelper.WriteLog("SetSP_KeyboardColor", "Setting Color toDefault");
		System.Threading.Tasks.Task.Run(() =>
		{
			WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardMode, WMIRGBKeyboardMode.Mode_RGBAutoCyclic);
		});
	}

	public void SetSP_KeyboardColor(byte red, byte green, byte blue)
	{
		System.Threading.Tasks.Task.Run(async () =>
		{
			WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardMode, WMIRGBKeyboardMode.Mode_RGBFixedMode);
			await System.Threading.Tasks.Task.Delay(500);
			WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardColor, new byte[3] { red, green, blue });
		});
		LogHelper.WriteLog("SetSP_KeyboardColor", "Setting Color to R:" + red + " G:" + green + " B:" + blue);
	}

	public void Set_KeyboardRgbColor()
	{
		float num = (float)Math.Round(mainWindowHandle.KeyboardRGBColor.Value);
		mainWindowHandle.KeyboardRGBColor.Value = num;
		float[] hsb = new float[3] { num, 1f, 1f };
		LogHelper.WriteLog("Set_KeyboardRgbColor", "Setting Color to H:" + num);
		float[] array = ColorConversionHelper.HSB2RGB(hsb);
		SetSP_KeyboardColor((byte)array[0], (byte)array[1], (byte)array[2]);
	}

	private string ReadSetting(string key)
	{
		try
		{
			return ConfigurationManager.AppSettings[key] ?? "";
		}
		catch (ConfigurationErrorsException)
		{
			return "";
		}
	}

	private void AddUpdateAppSettings(string key, string value)
	{
		try
		{
			Configuration configuration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
			KeyValueConfigurationCollection settings = configuration.AppSettings.Settings;
			if (settings[key] == null)
			{
				settings.Add(key, value);
			}
			else
			{
				settings[key].Value = value;
			}
			configuration.Save(ConfigurationSaveMode.Modified);
			ConfigurationManager.RefreshSection(configuration.AppSettings.SectionInformation.Name);
		}
		catch (ConfigurationErrorsException)
		{
			Console.WriteLine("Error writing app settings");
		}
	}

	private void Init_WMIEvent()
	{
		if (new WMIEventService().InitAndStart(wMIEventArrived))
		{
			LogHelper.WriteLog("WMI EVENT Listening...");
		}
	}

	private static void wMIEventArrived(WMIEventType wMIEventType, WMIEventName wMIEventName, object eVENTvalue)
	{
		LogHelper.WriteLog("wMIEventArrived", "WMIEventType: " + wMIEventType.ToString() + " wMIEventName: " + wMIEventName.ToString() + " eVENTvalue: " + eVENTvalue);
		switch (wMIEventName)
		{
		case WMIEventName.RGBKeyboardBrightness:
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if ((byte)eVENTvalue == 0)
				{
					mainWindowHandle.KeyboardLightBrightness.Value = 0.0;
				}
				else if ((byte)eVENTvalue == 1)
				{
					mainWindowHandle.KeyboardLightBrightness.Value = 1.0;
				}
				else if ((byte)eVENTvalue == 2)
				{
					mainWindowHandle.KeyboardLightBrightness.Value = 2.0;
				}
				else
				{
					mainWindowHandle.KeyboardLightBrightness.Value = 3.0;
				}
			});
			break;
		case WMIEventName.RGBKeyboardMode:
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if ((byte)eVENTvalue == 1)
				{
					mainWindowHandle.KeyboardRGBColor.IsEnabled = false;
					mainWindowHandle.KeyboardRGBColor.Opacity = 0.4;
				}
				else if ((byte)eVENTvalue == 2)
				{
					mainWindowHandle.KeyboardRGBColor.IsEnabled = true;
					mainWindowHandle.KeyboardRGBColor.Opacity = 1.0;
				}
			});
			break;
		case WMIEventName.AmbientlightState:
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if ((byte)eVENTvalue == 1)
				{
					if (!mainWindowHandle.PCAmbientlight.IsChecked.Value)
					{
						mainWindowHandle.PCAmbientlight_Set = true;
						mainWindowHandle.PCAmbientlight.IsChecked = true;
					}
				}
				else if (mainWindowHandle.PCAmbientlight.IsChecked.Value)
				{
					mainWindowHandle.PCAmbientlight_Set = true;
					mainWindowHandle.PCAmbientlight.IsChecked = false;
				}
			});
			break;
		case WMIEventName.TouchPadState:
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if ((byte)eVENTvalue == 1)
				{
					if (!mainWindowHandle.TPLockButton.IsChecked.Value)
					{
						mainWindowHandle.TPLockButton_Set = true;
						mainWindowHandle.TPLockButton.IsChecked = true;
					}
				}
				else if (mainWindowHandle.TPLockButton.IsChecked.Value)
				{
					mainWindowHandle.TPLockButton_Set = true;
					mainWindowHandle.TPLockButton.IsChecked = false;
				}
			});
			break;
		case WMIEventName.SystemPerMode:
			mainWindowHandle.Dispatcher.Invoke(() =>
			{
				if (mainWindowHandle.PerfsettingPage1.Visibility == Visibility.Visible)
				{
					mainWindowHandle.Set_CustomMode(WMICPUPower.CloseState);
				}
				mainWindowHandle.SetAP_PerformaceMode((WMISystemPerMode)(byte)eVENTvalue);
				mainWindowHandle.updateUI_ModeToFanSpeed();
			});
			break;
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetSystemPowerStatus(out SystemPowerStatus sps);

	private bool GetSP_IsAC()
	{
		SystemPowerStatus sps = default;
		GetSystemPowerStatus(out sps);
		if (sps.LineStatus == ACLineStatus.Online)
		{
			return true;
		}
		return false;
	}

	private bool GetSP_IsTypeC()
	{
		if (WMIMethodServices.GetValue<WMISystemACType>(WMIMethodName.SystemAcType) == WMISystemACType.TypeC)
		{
			return true;
		}
		return false;
	}

	private void SetSP_MaxFanSpeed(int type)
	{
		byte b = (byte)(int.Parse(MaxFanSpeed.Text) + type);
		if (!FastestMode.IsEnabled || mainWindowHandle.PerfsettingPage1.Visibility == Visibility)
		{
			if (b < fastestMode_FanSpeed_MinValue || b > fastestMode_FanSpeed_MaxValue)
			{
				MaxFanSpeed.Text = "50";
				return;
			}
		}
		else if (!PlayingMode.IsEnabled)
		{
			if (b < playingMode_FanSpeed_MinValue || b > playingMode_FanSpeed_MaxValue)
			{
				MaxFanSpeed.Text = "35";
				return;
			}
		}
		else if (!WorkMode.IsEnabled)
		{
			if (b < workMode_FanSpeed_MinValue || b > workMode_FanSpeed_MaxValue)
			{
				MaxFanSpeed.Text = "22";
				return;
			}
		}
		else if (WorkMode.IsEnabled || b < workMode_FanSpeed_MinValue || b > workMode_FanSpeed_MaxValue)
		{
			return;
		}
		SetSP_MaxFanSpeedButton(WMIResultState.ON);
		Set_FanSpeed(b);
	}

	private void Set_FanSpeed(byte speedValue)
	{
		MaxFanSpeed.Text = speedValue.ToString();
		MaxFanSpeedSlider.Value = (int)Set_MaxFanSpeedSlider();
		// 原为 MIFS MaxFanSpeed（本机固件忽略，报告 §14.6），改走桥接 no-op acknowledge，选项保留
		JiaoLongBridge.Send("MAX_FAN", ",\"Value\":" + speedValue);
		updateFile__ModeToFanSpeed();
	}

	private void SetSP_MaxFanSpeedButton(WMIResultState m)
	{
		LogHelper.WriteLog("SetSP_MaxFanSpeedButton", m.ToString());
		// 原为 MIFS MaxFanSpeedSwitch（无效通道），改走桥接（no-op）
		JiaoLongBridge.Send("MAX_FAN", ",\"Switch\":" + (m == WMIResultState.ON ? "true" : "false"));
	}

	public byte Set_MaxFanSpeedSlider()
	{
		int num = Convert.ToInt32(MaxFanSpeed.Text);
		float num2 = 0f;
		if ((!FastestMode.IsEnabled || mainWindowHandle.PerfsettingPage1.Visibility == Visibility) && fastestMode_FanSpeed_MinValue <= num && num <= fastestMode_FanSpeed_MaxValue)
		{
			num2 = (num - fastestMode_FanSpeed_MinValue) * 100 / (fastestMode_FanSpeed_MaxValue - fastestMode_FanSpeed_MinValue);
		}
		else if (!PlayingMode.IsEnabled && playingMode_FanSpeed_MinValue <= num && num <= playingMode_FanSpeed_MaxValue)
		{
			num2 = (num - playingMode_FanSpeed_MinValue) * 100 / (playingMode_FanSpeed_MaxValue - playingMode_FanSpeed_MinValue);
		}
		else if (!WorkMode.IsEnabled && workMode_FanSpeed_MinValue <= num && num <= workMode_FanSpeed_MaxValue)
		{
			num2 = (num - workMode_FanSpeed_MinValue) * 100 / (workMode_FanSpeed_MaxValue - workMode_FanSpeed_MinValue);
		}
		LogHelper.WriteLog("Set_MaxFanSpeedSlider", "Setting MaxFanSpeedSlider to :" + num2);
		return (byte)num2;
	}

	private void Set_OSDIconState(int state)
	{
		if (state == 0)
		{
			ServiceController serviceController = new ServiceController("BLDHotKeyService");
			if (serviceController.Status != ServiceControllerStatus.Stopped)
			{
				serviceController.Stop();
				serviceController.WaitForStatus(ServiceControllerStatus.Stopped, new TimeSpan(0, 0, 30));
			}
		}
		else
		{
			ServiceController serviceController2 = new ServiceController("BLDHotKeyService");
			if (serviceController2.Status != ServiceControllerStatus.Running)
			{
				serviceController2.Start();
				serviceController2.WaitForStatus(ServiceControllerStatus.Running, new TimeSpan(0, 0, 30));
			}
		}
	}

	public void Set_CPUTempWall()
	{
		cpuTempWallData = Convert.ToByte(mainWindowHandle.CPUTempWallSlider.Value);
		LogHelper.WriteLog("Set_CPUTempWall", "Value: " + cpuTempWallData);
		// 原为 MIFS CPUPower sub=4（本机固件忽略），改走桥接（桥接 no-op acknowledge，保留选项语义）
		JiaoLongBridge.Send("TEMP_WALL", ",\"Value\":" + cpuTempWallData);
		SaveToFile_AppMachineInfo(cpuTempWallData, 5);
	}

	public void Set_SPLSlider()
	{
		splData = Convert.ToByte(mainWindowHandle.SPLSlider.Value);
		LogHelper.WriteLog("Set_SPLSlider", "Value: " + splData);
		// 原为 MIFS 直连，改走桥接（与官方移植版/桥接服务统一控制权）
		JiaoLongBridge.Send("SPL", ",\"Watts\":" + splData);
		SaveToFile_AppMachineInfo(splData, 6);
	}

	public void Set_SPPTSlider()
	{
		spptData = Convert.ToByte(mainWindowHandle.SPPTSlider.Value);
		LogHelper.WriteLog("Set_SPLSlider", "Value: " + spptData);
		// 原为 MIFS 直连，改走桥接
		JiaoLongBridge.Send("SPPT", ",\"Watts\":" + spptData);
		SaveToFile_AppMachineInfo(spptData, 7);
	}

	public void Set_CustomMode(WMICPUPower m)
	{
		SetAP_CustomMode(m);
		SetSP_CustomMode(m);
		updateUI_ModeToFanSpeed();
		Set_GPUCoreClock((int)m);
	}

	public void SetAP_CustomMode(WMICPUPower m)
	{
		LogHelper.WriteLog("SetAP_CustomMode");
		mainWindowHandle.Dispatcher.Invoke(() =>
		{
			switch (m)
			{
			case WMICPUPower.CloseState:
				PerfsettingPageHome.Visibility = Visibility.Visible;
				PerfsettingPage1.Visibility = Visibility.Hidden;
				TemperatureCurvePage.Visibility = Visibility.Hidden;
				QuietPerformancePage.Visibility = Visibility.Hidden;
				break;
			case WMICPUPower.OpenState:
				PerfsettingPageHome.Visibility = Visibility.Hidden;
				PerfsettingPage1.Visibility = Visibility.Visible;
				TemperatureCurvePage.Visibility = Visibility.Hidden;
				QuietPerformancePage.Visibility = Visibility.Hidden;
				break;
			}
		});
	}

	public void SetSP_CustomMode(WMICPUPower m)
	{
		LogHelper.WriteLog("SetSP_CustomMode");
		System.Threading.Tasks.Task.Run(() =>
		{
			WMIMethodServices.SetValue(WMIMethodName.CPUPower, m);
			if (m == WMICPUPower.OpenState)
			{
				isInCustomMode = 1;
				// SPL/SPPT/温度墙改走桥接（统一控制权；温度墙在本机为 no-op）
				JiaoLongBridge.Send("TEMP_WALL", ",\"Value\":" + cpuTempWallData);
				JiaoLongBridge.Send("SPL", ",\"Watts\":" + splData);
				JiaoLongBridge.Send("SPPT", ",\"Watts\":" + spptData);
			}
			else
			{
				isInCustomMode = 0;
			}
			SaveToFile_AppMachineInfo(new byte[4] { isInCustomMode, cpuTempWallData, splData, spptData }, 4);
		});
	}

	public void Set_GPUCoreClock(int _clockflag)
	{
		int num = 100;
		if (_clockflag != 0)
		{
			num = Convert.ToInt32(GPUCoreClockSlider.Value);
			SaveToFile_AppMachineInfo((byte)num, 8);
		}
		int num2 = 3;
		int grClockOffset = 0;
		int memClockOffset = 0;
		while (num2 > 0 && (GetPstatesLevel0Settings(ref grClockOffset, ref memClockOffset) != 0 || ChangePstatesLevel0Settings(num, memClockOffset) != 0))
		{
			num2--;
		}
	}

	private async void Window_Loaded(object sender, RoutedEventArgs e)
	{
		mainWindowHandle = this;
		token = source.Token;
		Set_CPUNUM_Init();
		SetAP_CPUPoly_Init();
		SetAP_CPUInfos();
		SetAP_CPUPoly();
		SetAP_MEMPoly_Init();
		SetAP_MEMInfos();
		SetAP_GPU1Poly_Init();
		SetAP_GPU1Infos();
		SetAP_DISKPoly_Init();
		SetAP_DISKInfos();
		SetAP_DISKPoly();
		SetAP_CostomMode();
		Init_Keyboard_State();
		Init_Keyboard_Event();
		KeyBoardColorComBoxReset();
		KeyBoardColorComBoxReset2();
		KeyBoardColorComBoxReset3();
		MenuComBoxReset();
		Init_WMIEvent();
		InitializeComboBox();
		await Init_SettingsAsync();
	}

	private void KeyBoardColorComBoxReset()
	{
		KeyBoardColorComBox.SelectedItem = null;
	}

	private void KeyBoardColorComBoxReset2()
	{
		KeyBoardColorComBox2.SelectedItem = null;
	}

	private void KeyBoardColorComBoxReset3()
	{
		KeyBoardColorComBox3.SelectedItem = null;
	}

	private void MenuComBoxReset()
	{
		MenuComBox.SelectedItem = null;
	}

	private void ImageWorking_GotFocus(object sender, RoutedEventArgs e)
	{
	}

	private void ImagePlaying_GotFocus(object sender, RoutedEventArgs e)
	{
	}

	private void ImageFast_GotFocus(object sender, RoutedEventArgs e)
	{
	}

	private void ImagePlaying_LostFocus(object sender, RoutedEventArgs e)
	{
	}

	private void ImageWorking_LostFocus(object sender, RoutedEventArgs e)
	{
	}

	private void ImageFast_LostFocus(object sender, RoutedEventArgs e)
	{
	}

	private void Button_MouseEnter(object sender, MouseEventArgs e)
	{
		colorborder.Background = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 160, 0));
	}

	private void Button_MouseEnter2(object sender, MouseEventArgs e)
	{
		colorborderCore.Background = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 160, 0));
	}

	private void Button_MouseEnter3(object sender, MouseEventArgs e)
	{
		colorborderPowerManagement.Background = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 160, 0));
	}

	private void InitializeComboBox()
	{
		ComboBox keyBoardColorComBox = KeyBoardColorComBox2;
		PCInfoFormat.GetCPUPhysicsCores(out var PhysicsCores);
		List<string> list = new List<string>();
		list.Add("不限制");
		for (int i = 2; i <= PhysicsCores; i += 2)
		{
			list.Add(i.ToString());
		}
		list.RemoveAt(list.Count - 1);
		string[] array = list.ToArray();
		foreach (string content in array)
		{
			ComboBoxItem newItem = new ComboBoxItem
			{
				Content = content,
				FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./GamingControlCenter/Fonts/#庞门正道标题体"),
				FontSize = 12.0,
				Background = Brushes.Gray,
				Foreground = Brushes.LightGray
			};
			keyBoardColorComBox.Items.Add(newItem);
		}
	}

	private void ColorPicker_OnMouseDown(object sender, MouseButtonEventArgs e)
	{
		PickColor(sender as Image);
	}

	private void PickColor(Image colorPicker)
	{
		Point position = Mouse.GetPosition(colorPicker);
		BitmapSource bitmapSource = colorPicker.Source as BitmapSource;
		try
		{
			CroppedBitmap croppedBitmap = new CroppedBitmap(bitmapSource, new Int32Rect((int)((double)bitmapSource.PixelWidth * position.X / colorPicker.ActualWidth), (int)((double)bitmapSource.PixelHeight * position.Y / colorPicker.ActualHeight), 1, 1));
			byte[] array = new byte[bitmapSource.Format.BitsPerPixel / 8];
			croppedBitmap.CopyPixels(array, bitmapSource.Format.BitsPerPixel / 8, 0);
			byte red = array[2];
			byte green = array[1];
			byte blue = array[0];
			SetSP_KeyboardColor(red, green, blue);
		}
		catch (ArgumentException)
		{
		}
	}

	private void KeyboardLightBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		Console.WriteLine(e.NewValue);
		if (SetKeyboardLightBrightnessToZeroStep == 0)
		{
			SetKeyboardLightBrightnessToZeroStep = 1;
			KeyboardLightBrightness.Value = e.OldValue;
		}
		else
		{
			SetKeyboardLightBrightnessToZeroStep = 0;
		}
	}

	private void LightBrightness_ValueChanged2(object sender, MouseEventArgs e)
	{
		if (SetKeyboardLightBrightnessToZeroStep == 1)
		{
			SetKeyboardLightBrightnessToZeroStep = 2;
		}
	}

	private void TemperatureCurve_Click(object sender, RoutedEventArgs e)
	{
		PerfsettingPageHome.Visibility = Visibility.Hidden;
		TemperatureCurvePage.Visibility = Visibility.Visible;
	}

	private void Turo_ToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		disableTurboBoostStatus = true;
		SaveAppSettings();
		NvTuning.SetTuro(flag: true);
	}

	private void Turo_ToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		disableTurboBoostStatus = false;
		SaveAppSettings();
		NvTuning.SetTuro(flag: false);
	}

	private void ShutdownButton_Click(object sender, RoutedEventArgs e)
	{
		ECRW_Tool.Fan_Clear();
		StopTemperatureCurve();
		SMUCommands.RyzenAccess.Deinitialize();
		ShutdownButton.IsEnabled = false;
		source.Cancel();
		systemUsage.computerClose();
		SystemEvents.SessionSwitch -= OnSessionSwitch;
		Environment.Exit(0);
	}

	private void FastestMode_Click(object sender, RoutedEventArgs e)
	{
		bool sP_IsAC = GetSP_IsAC();
		if (GetSP_IsTypeC())
		{
			return;
		}
		if (!sP_IsAC)
		{
			PowerTips powerTips = new PowerTips();
			powerTips.Owner = Application.Current.MainWindow;
			powerTips.ShowDialog();
			return;
		}
		if (ReadSetting("NeverTips") == "True")
		{
			ImagePerformanceMode.Source = new BitmapImage(new Uri("Assets/fast_194x194.png", UriKind.RelativeOrAbsolute));
			SetAP_PerformaceMode(WMISystemPerMode.PerformanceMode);
			SetSP_PerformaceMode(WMISystemPerMode.PerformanceMode);
			return;
		}
		FastestTipsDialog fastestTipsDialog = new FastestTipsDialog();
		fastestTipsDialog.Owner = Application.Current.MainWindow;
		fastestTipsDialog.ShowDialog();
		if (FastestTipsDialog.IsUserChoiceSure && FastestTipsDialog.IsNeverTipsChecked)
		{
			AddUpdateAppSettings("NeverTips", "True");
		}
		if (FastestTipsDialog.IsUserChoiceSure)
		{
			SetAP_PerformaceMode(WMISystemPerMode.PerformanceMode);
			SetSP_PerformaceMode(WMISystemPerMode.PerformanceMode);
		}
	}

	public static void Set_Level()
	{
		int grClockOffset = 0;
		int memClockOffset = 0;
		int num = 3;
		while (num > 0 && (GetPstatesLevel0Settings(ref grClockOffset, ref memClockOffset) != 0 || ChangePstatesLevel0Settings(100, memClockOffset) != 0))
		{
			num--;
		}
	}

	private void Help_Click(object sender, EventArgs e)
	{
		MessageBox.Show("此模式会固定为狂飙模式,最低温度时返回最低转速,最高温度时返回最高转速,通过泄漏的EC通信协议实现,通过线性插值控制\n自启打开后,重启软件生效,如需停止,点击温度曲线的停止按钮");
	}

	private int DynamicsetTemperatureSpeedSwitch(List<int> tcList)
	{
		int[] array = new int[6] { 50, 60, 70, 80, 90, 100 };
		int num = 1800;
		int num2 = 5800;
		if (systemUsage.cpu_temp <= array[0])
		{
			return num;
		}
		if (systemUsage.cpu_temp >= array[^1])
		{
			return num2;
		}
		for (int i = 0; i < array.Length - 1; i++)
		{
			if (systemUsage.cpu_temp >= array[i] && systemUsage.cpu_temp < array[i + 1])
			{
				int num3 = array[i];
				int num4 = array[i + 1];
				int num5 = tcList[i];
				int num6 = tcList[i + 1];
				return (int)Math.Clamp((double)num5 + (double)(systemUsage.cpu_temp - num3) / (double)(num4 - num3) * (double)(num6 - num5), num, num2);
			}
		}
		return num;
	}

	private void QuietPerformanceMode_Click(object sender, EventArgs e)
	{
		WMIMethodServices.SetValue(WMIMethodName.SystemPerMode, WMISystemPerMode.PerformanceMode);
		MessageBox.Show("重要提示！！！\r\n关机系统和关闭软件,会自动关闭安静性能模式 v35以上bios才可用\r\n该功能未经充分测试.请充分注意温度变化,以免损坏硬件,造成的硬件损坏与本人无关\r\n该功能会关闭显卡功耗限制,当到达100度温度墙后,会强制降频.\r\n游戏时使用,推荐调低GPU GPU频率,合理设置游戏画质特效,保持温度99以下,推荐使用一些散热方法,即可达到理想效果,不被物理降频");
		PerfsettingPageHome.Visibility = Visibility.Hidden;
		QuietPerformancePage.Visibility = Visibility.Visible;
	}

	private void PRMSpeed_ToggleButton_Checked(object sender, EventArgs e)
	{
		int num = int.Parse(PRMSpeed.Text);
		if (num < 1800 || num > 5800)
		{
			MessageBox.Show("请输入1800-5800之间的数");
		}
		else
		{
			ECRW_Tool.Fan_Speed((byte)int.Parse(num.ToString().Substring(0, 2)));
		}
	}

	private void PRMSpeed_ToggleButton_UnChecked(object sender, EventArgs e)
	{
	}

	private void Consumption_TimerCallback(object state)
	{
		WMIMethodServices.SetValue(WMIMethodName.CPUPower, new byte[2]
		{
			2,
			Convert.ToByte(CPU_Power_Consumption.Text)
		});
	}

	private void CPU_Power_Consumption_Checked(object sender, EventArgs e)
	{
		int num = int.Parse(CPU_Power_Consumption.Text);
		if (num < 0 || num > 105)
		{
			MessageBox.Show("请输入20-105之间的数");
			return;
		}
		pLLStatus = true;
		SaveAppSettings();
		_appSettings.CPUPowerConsumptionValue = Convert.ToByte(num);
		string commandValues = $"--slow-limit={num * 1000} ";
		System.Threading.Tasks.Task.Run(() =>
		{
			RyzenAdj_To_UXTU.Translate(commandValues);
		});
	}

	private void CPU_Power_Consumption_UnChecked(object sender, EventArgs e)
	{
		pLLStatus = false;
		SaveAppSettings();
		_appSettings.CPUPowerConsumptionValue = 99;
		string commandValues = $"--slow-limit={120000} ";
		System.Threading.Tasks.Task.Run(() =>
		{
			RyzenAdj_To_UXTU.Translate(commandValues);
		});
	}

	private async void CPU_Power_PL_Consumption_Checked(object sender, EventArgs e)
	{
		int num = int.Parse(PLNum.Text);
		if (num < 0 || num > 120)
		{
			MessageBox.Show("请输入20-120之间的数");
			return;
		}
		sLLStatus = true;
		SaveAppSettings();
		_appSettings.PLNumValue = Convert.ToByte(num);
		string commandValues = $"--fast-limit={num * 1000} ";
		await System.Threading.Tasks.Task.Run(() =>
		{
			RyzenAdj_To_UXTU.Translate(commandValues);
		});
	}

	private async void CPU_Power_PL_Consumption_UnChecked(object sender, EventArgs e)
	{
		sLLStatus = false;
		SaveAppSettings();
		_appSettings.PLNumValue = 120;
		string commandValues = $"--fast-limit={120000} ";
		await System.Threading.Tasks.Task.Run(() =>
		{
			RyzenAdj_To_UXTU.Translate(commandValues);
		});
	}

	private void RGB_PowerOn_Checked(object sender, EventArgs e)
	{
		rainbowGradientButtonStatus = true;
		SaveAppSettings();
	}

	private void RGB_PowerOn_UnChecked(object sender, EventArgs e)
	{
		rainbowGradientButtonStatus = false;
		SaveAppSettings();
	}

	private async void CPUVoltage_ToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		int num = int.Parse(CPUVoltage.Text);
		if (num > 51 || num < 0)
		{
			MessageBox.Show("为了系统稳定安全,只允许设置降压 0~-50");
			return;
		}
		voltageStatus = true;
		cpuVoltage = CPUVoltage.Text;
		SaveAppSettings();
		string commandValues = $"--set-coall={Convert.ToUInt32((uint)(1048576 - -1 * -num))} ";
		await System.Threading.Tasks.Task.Run(() =>
		{
			RyzenAdj_To_UXTU.Translate(commandValues);
		});
	}

	private async void CPUVoltage_ToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		voltageStatus = false;
		SaveAppSettings();
		string commandValues = $"--set-coall={Convert.ToUInt32(1048576u)} ";
		await System.Threading.Tasks.Task.Run(() =>
		{
			RyzenAdj_To_UXTU.Translate(commandValues);
		});
	}

	private void Money_Click(object sender, EventArgs e)
	{
		new ImageViewWindow().ShowImage("Assets/wxplay.jpg", "Assets/zfbplay.jpg");
	}

	private void StopTemperatureCurve()
	{
		SaveTemperatureCurve.Content = "启动";
		if (fan_source != null)
		{
			fan_source.Cancel();
		}
		ECRW_Tool.Fan_Clear();
	}

	private void SaveTemperatureCurve_Click(object sender, RoutedEventArgs e)
	{
		RunTempCurve();
	}

	private async void RunTempCurve()
	{
		WMIMethodServices.SetValue(WMIMethodName.SystemPerMode, WMISystemPerMode.PerformanceMode);
		await System.Threading.Tasks.Task.Delay(300);
		if (SaveTemperatureCurve.Content.Equals("停止"))
		{
			StopTemperatureCurve();
			return;
		}
		fan_source = new CancellationTokenSource();
		SaveTemperatureCurve.Content = "停止";
		List<int> tcList = new List<int>
		{
			Convert.ToInt32(tc50.Value),
			Convert.ToInt32(tc60.Value),
			Convert.ToInt32(tc70.Value),
			Convert.ToInt32(tc80.Value),
			Convert.ToInt32(tc90.Value),
			Convert.ToInt32(tc100.Value)
		};
		SaveToFile_AppMachineInfo(tcList, 14);
		while (!fan_source.IsCancellationRequested)
		{
			await System.Threading.Tasks.Task.Delay(1000);
			ECRW_Tool.Fan_Speed((byte)(DynamicsetTemperatureSpeedSwitch(tcList) / 100));
		}
	}

	private void TrayIcon_DoubleClick(object sender, EventArgs e)
	{
		Show();
		WindowState = WindowState.Normal;
	}

	private void TempCurve_ToggleButton_Checked(object sender, EventArgs e)
	{
		temperatureCurveAutoStartStatus = true;
		SaveAppSettings();
	}

	private void TempCurve_ToggleButton_UnChecked(object sender, EventArgs e)
	{
		temperatureCurveAutoStartStatus = false;
		SaveAppSettings();
	}

	private void GridOfWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		Window.GetWindow(sender as Grid).DragMove();
	}

	private void KeyBoardColorComBox_SelectChange(object sender, SelectionChangedEventArgs e)
	{
		int selectedIndex = (sender as ComboBox).SelectedIndex;
		if (selectedIndex != -1)
		{
			LogHelper.WriteLog("KeyBoardColorComBox_SelectChange:", selectedIndex.ToString());
			KeyBoardColorComBoxReset();
			switch (selectedIndex)
			{
			case 0:
				SetSP_KeyboardColorDefault();
				break;
			case 1:
				KeyboardRGBColor.IsEnabled = true;
				KeyboardRGBColor.Opacity = 1.0;
				Set_KeyboardRgbColor();
				break;
			case 2:
				RainbowGradientRGB();
				break;
			}
		}
	}

	private void KeyBoardColorComBox_SelectChange2(object sender, SelectionChangedEventArgs e)
	{
		if ((sender as ComboBox).SelectedIndex != -1)
		{
			string text = ((sender as ComboBox).SelectedValue as ComboBoxItem).Content.ToString();
			if (text == "不限制")
			{
				NvTuning.RecCpuCore();
				mainWindowHandle.LimitCore.Text = "不限制";
			}
			else
			{
				int PhysicsCores = 0;
				PCInfoFormat.GetCPUPhysicsCores(out PhysicsCores);
				int num = (int)((float)(PhysicsCores - int.Parse(text)) / (float)PhysicsCores * 100f);
				NvTuning.SetCpuCore(100 - num);
				mainWindowHandle.LimitCore.Text = text;
			}
			KeyBoardColorComBoxReset2();
		}
	}

	private void KeyBoardColorComBox_SelectChange3(object sender, SelectionChangedEventArgs e)
	{
		if ((sender as ComboBox).SelectedIndex != -1)
		{
			NvTuning.SetPowerManagement(((sender as ComboBox).SelectedValue as ComboBoxItem).Content.ToString());
			mainWindowHandle.PowerMode.Text = NvTuning.GetPowermanagement();
			KeyBoardColorComBoxReset3();
		}
	}

	private async void RainbowGradientRGB()
	{
		if (led_source != null)
		{
			led_source.Cancel();
		}
		grb_flag = !grb_flag;
		if (grb_flag)
		{
			WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardMode, WMIRGBKeyboardMode.Mode_RGBFixedMode);
			SetSP_KeyboardLightBrightness(3);
			led_source = new CancellationTokenSource();
			try
			{
				while (!led_source.IsCancellationRequested)
				{
					for (float hue = 0f; hue < 360f; hue += 3f)
					{
						float[] array = ColorConversionHelper.HSB2RGB(new float[3] { hue, 1f, 1f });
						byte b = (byte)(array[0] + 0.1f);
						byte b2 = (byte)(array[1] + 0.1f);
						byte b3 = (byte)(array[2] + 0.1f);
						WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardColor, new byte[3] { b, b2, b3 });
						await System.Threading.Tasks.Task.Delay(50);
					}
				}
				return;
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
		if (led_source != null)
		{
			led_source.Cancel();
		}
		SetSP_KeyboardLightBrightness(0);
	}

	private void NumLockToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		if (NumLockToggleButton_Set)
		{
			NumLockToggleButton_Set = false;
			return;
		}
		System.Threading.Tasks.Task.Run(() =>
		{
			if (!NumLockStatus())
			{
				SetNumLock();
			}
		});
	}

	private void NumLockToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		if (NumLockToggleButton_Set)
		{
			NumLockToggleButton_Set = false;
			return;
		}
		System.Threading.Tasks.Task.Run(() =>
		{
			if (NumLockStatus())
			{
				SetNumLock();
			}
		});
	}

	private void CapsLockToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		if (CapsLockToggleButton_Set)
		{
			CapsLockToggleButton_Set = false;
			return;
		}
		System.Threading.Tasks.Task.Run(() =>
		{
			if (!CapsLockStatus())
			{
				SetCapsLock();
			}
		});
	}

	private void CapsLockToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		if (CapsLockToggleButton_Set)
		{
			CapsLockToggleButton_Set = false;
			return;
		}
		System.Threading.Tasks.Task.Run(() =>
		{
			if (CapsLockStatus())
			{
				SetCapsLock();
			}
		});
	}

	private void MenuComBox_SelectChange(object sender, SelectionChangedEventArgs e)
	{
		int selectedIndex = (sender as ComboBox).SelectedIndex;
		if (selectedIndex != -1)
		{
			MenuComBoxReset();
			if (selectedIndex == 0)
			{
				Process.Start(new ProcessStartInfo("https://www.mechrevo.com/service/")
				{
					UseShellExecute = true
				});
			}
		}
	}

	private void Window_Unloaded(object sender, RoutedEventArgs e)
	{
	}

	private void Button_MouseLeave(object sender, MouseEventArgs e)
	{
		colorborder.Background = new SolidColorBrush(Color.FromRgb(68, 75, 83));
	}

	private void Button_MouseLeave2(object sender, MouseEventArgs e)
	{
		colorborderCore.Background = new SolidColorBrush(Color.FromRgb(68, 75, 83));
	}

	private void Button_MouseLeave3(object sender, MouseEventArgs e)
	{
		colorborderPowerManagement.Background = new SolidColorBrush(Color.FromRgb(68, 75, 83));
	}

	private void PlayingMode_Click(object sender, RoutedEventArgs e)
	{
		if (!GetSP_IsTypeC())
		{
			ImagePerformanceMode.Source = new BitmapImage(new Uri("Assets/playing_194x194.png", UriKind.RelativeOrAbsolute));
			SetAP_PerformaceMode(WMISystemPerMode.BalanceMode);
			SetSP_PerformaceMode(WMISystemPerMode.BalanceMode);
		}
	}

	private void WorkMode_Click(object sender, RoutedEventArgs e)
	{
		if (!GetSP_IsTypeC())
		{
			ImagePerformanceMode.Source = new BitmapImage(new Uri("Assets/Working_194x194.png", UriKind.RelativeOrAbsolute));
			SetAP_PerformaceMode(WMISystemPerMode.QuietMode);
			SetSP_PerformaceMode(WMISystemPerMode.QuietMode);
		}
	}

	private void PCAmbientlight_Checked(object sender, RoutedEventArgs e)
	{
		if (PCAmbientlight_Set)
		{
			PCAmbientlight_Set = false;
		}
		else
		{
			SetSP_Ambientlight(WMIResultState.ON);
		}
	}

	private void PCAmbientlight_Unchecked(object sender, RoutedEventArgs e)
	{
		if (PCAmbientlight_Set)
		{
			PCAmbientlight_Set = false;
		}
		else
		{
			SetSP_Ambientlight(WMIResultState.OFF);
		}
	}

	private void FnLock_Checked(object sender, RoutedEventArgs e)
	{
		if (FnLock_Set)
		{
			FnLock_Set = false;
		}
		else
		{
			SetSP_FNLock(WMIResultState.ON);
		}
	}

	private void FnLock_Unchecked(object sender, RoutedEventArgs e)
	{
		if (FnLock_Set)
		{
			FnLock_Set = false;
		}
		else
		{
			SetSP_FNLock(WMIResultState.OFF);
		}
	}

	private void TPLockButton_Checked(object sender, RoutedEventArgs e)
	{
		if (TPLockButton_Set)
		{
			TPLockButton_Set = false;
		}
		else
		{
			SetSP_TPMode(WMIResultState.ON);
		}
	}

	private void TPLockButton_Unchecked(object sender, RoutedEventArgs e)
	{
		if (TPLockButton_Set)
		{
			TPLockButton_Set = false;
		}
		else
		{
			SetSP_TPMode(WMIResultState.OFF);
		}
	}

	private void IndependentConnection_Checked(object sender, RoutedEventArgs e)
	{
		if (IndependentConnection_Set)
		{
			IndependentConnection_Set = false;
			return;
		}
		GraphicsTipsDialog.IsUserChoiceSure = false;
		GraphicsTipsDialog graphicsTipsDialog = new GraphicsTipsDialog();
		graphicsTipsDialog.Owner = Application.Current.MainWindow;
		graphicsTipsDialog.ShowDialog();
		if (GraphicsTipsDialog.IsUserChoiceSure)
		{
			IndependentConnection.IsChecked = true;
			return;
		}
		IndependentConnection.Unchecked -= IndependentConnection_Unchecked;
		IndependentConnection.IsChecked = false;
		IndependentConnection.Unchecked += IndependentConnection_Unchecked;
	}

	private void IndependentConnection_Unchecked(object sender, RoutedEventArgs e)
	{
		if (IndependentConnection_Set)
		{
			IndependentConnection_Set = false;
			return;
		}
		GraphicsTipsDialog.IsUserChoiceSure = false;
		GraphicsTipsDialog graphicsTipsDialog = new GraphicsTipsDialog();
		graphicsTipsDialog.Owner = Application.Current.MainWindow;
		graphicsTipsDialog.ShowDialog();
		if (GraphicsTipsDialog.IsUserChoiceSure)
		{
			IndependentConnection.IsChecked = false;
			return;
		}
		IndependentConnection.Checked -= IndependentConnection_Checked;
		IndependentConnection.IsChecked = true;
		IndependentConnection.Checked += IndependentConnection_Checked;
	}

	private void TextBox1_PreviewTextInput(object sender, TextCompositionEventArgs e)
	{
		if (!int.TryParse(e.Text, out var result) || result < 0)
		{
			e.Handled = true;
		}
	}

	private void Temperature_Wall_ToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		tempWallStatus = true;
		cpuTemperatureWall = TemperatureWall.Text;
		SaveAppSettings();
		int value = int.Parse(TemperatureWall.Text);
		string commandValues = $"--tctl-temp={value} --cHTC-temp{value} --apu-skin-temp={value}";
		System.Threading.Tasks.Task.Run(() =>
		{
			RyzenAdj_To_UXTU.Translate(commandValues);
		});
	}

	private void Temperature_Wall_ToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		string commandValues = "--tctl-temp=99 --cHTC-temp=99 --apu-skin-temp=99";
		System.Threading.Tasks.Task.Run(() =>
		{
			RyzenAdj_To_UXTU.Translate(commandValues);
		});
		tempWallStatus = false;
		SaveAppSettings();
	}

	private void GPU_ToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		SaveToFile_AppMachineInfo(GPUFrequency.Text, 10);
		SaveToFile_AppMachineInfo("true", 13);
		NvTuning.SetMaxGPUClock(int.Parse(GPUFrequency.Text));
	}

	private void GPUFreqLock_ToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		NvTuning.SetGPUClockLock(int.Parse(GPUFreqLock.Text));
	}

	private void GPUFreqLock_ToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		NvTuning.RecMaxGPUClock();
	}

	private void CheckGPUFreqMemVer()
	{
		if (int.Parse(GPUFreqOC.Text) > 250 || int.Parse(GPUMemoryOC.Text) > 1000)
		{
			MessageBox.Show("只允许显卡超频范围0-250,显存超频范围0-1000");
		}
		else if (int.Parse(GPUFreqOC.Text) < 0 || int.Parse(GPUMemoryOC.Text) < 0)
		{
			MessageBox.Show("只允许显卡超频范围0-250,显存超频范围0-1000");
		}
	}

	private void GPUFreqOC_ToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		CheckGPUFreqMemVer();
		if (GPUMemOC_Checked.IsChecked == true)
		{
			NvTuning.SetClocks(int.Parse(GPUFreqOC.Text), int.Parse(GPUMemoryOC.Text));
		}
		else
		{
			NvTuning.SetClocks(int.Parse(GPUFreqOC.Text), 0);
		}
		SaveToFile_AppMachineInfo(GPUFreqOC.Text, 18);
	}

	private void GPUMemOC_ToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		CheckGPUFreqMemVer();
		if (GPUOC_Checked.IsChecked == true)
		{
			NvTuning.SetClocks(int.Parse(GPUFreqOC.Text), int.Parse(GPUMemoryOC.Text));
		}
		else
		{
			NvTuning.SetClocks(0, int.Parse(GPUMemoryOC.Text));
		}
		SaveToFile_AppMachineInfo(GPUMemoryOC.Text, 19);
	}

	private void GPUFreqOC_ToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		if (GPUMemOC_Checked.IsChecked == true)
		{
			NvTuning.SetClocks(0, int.Parse(GPUMemoryOC.Text));
		}
		else
		{
			NvTuning.SetClocks(0, 0);
		}
	}

	private void GPUMemOC_ToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		if (GPUOC_Checked.IsChecked == true)
		{
			NvTuning.SetClocks(int.Parse(GPUFreqOC.Text), 0);
		}
		else
		{
			NvTuning.SetClocks(0, 0);
		}
	}

	private void GPU_ToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		SaveToFile_AppMachineInfo("false", 13);
		NvTuning.RecMaxGPUClock();
	}

	private void CPU_ToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		cpuButtonStatus = true;
		SaveAppSettings();
		Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\GameBar", writable: true).SetValue("AutoGameModeEnabled", 0);
		SaveToFile_AppMachineInfo(CPUFrequency.Text, 9);
		NvTuning.SetShowCPUMHz(int.Parse(CPUFrequency.Text), flag: true);
	}

	private void CPU_ToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		cpuButtonStatus = false;
		SaveAppSettings();
		Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\GameBar", writable: true).SetValue("AutoGameModeEnabled", 1);
		NvTuning.SetShowCPUMHz(int.Parse(CPUFrequency.Text), flag: false);
	}

	private void StartUpButton_ToggleButton_Checked(object sender, RoutedEventArgs e)
	{
		exeStart = true;
		SaveAppSettings();
		string fileName = Process.GetCurrentProcess().MainModule.FileName;
		using TaskService taskService = new TaskService();
		string path = "JiaoLongPlus";
		TaskDefinition taskDefinition = taskService.NewTask();
		taskDefinition.Actions.Add(new ExecAction(fileName, "--minimize"));
		taskDefinition.Triggers.Add(new LogonTrigger());
		taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
		taskService.RootFolder.RegisterTaskDefinition(path, taskDefinition);
	}

	private void StartUpButton_ToggleButton_UnChecked(object sender, RoutedEventArgs e)
	{
		try
		{
			exeStart = false;
			SaveAppSettings();
			using TaskService taskService = new TaskService();
			string name = "JiaoLongPlus";
			taskService.RootFolder.DeleteTask(name);
		}
		catch
		{
			MessageBox.Show("删除失败");
		}
		finally
		{
			SaveToFile_AppMachineInfo("false", 11);
		}
	}

	private void IndependentConnection_Click(object sender, RoutedEventArgs e)
	{
		GraphicsTipsDialog.IsUserChoiceSure = false;
		GraphicsTipsDialog graphicsTipsDialog = new GraphicsTipsDialog();
		graphicsTipsDialog.Owner = Application.Current.MainWindow;
		graphicsTipsDialog.ShowDialog();
		if (GraphicsTipsDialog.IsUserChoiceSure)
		{
			if (IndependentConnection.IsChecked.Value)
			{
				SetSP_GraphicsDirect(WMIGPUMode.DiscreteMode);
			}
			else
			{
				SetSP_GraphicsDirect(WMIGPUMode.HybridMode);
			}
			System.Threading.Tasks.Task.Run(async () =>
			{
				Process.Start("shutdown.exe", "-r -t 2");
				await System.Threading.Tasks.Task.Delay(500);
				mainWindowHandle.Dispatcher.Invoke(() =>
				{
					ShutdownButton.IsEnabled = false;
					source.Cancel();
					Environment.Exit(0);
					Application.Current.Shutdown();
				});
			});
		}
		else
		{
			IndependentConnection.IsChecked = !IndependentConnection.IsChecked;
		}
	}

	private void KeyboardLightBrightness_Initialized(object sender, EventArgs e)
	{
		KeyboardLightBrightness.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(KeyboardLightBrightness_MouseLeftButtonUp), handledEventsToo: true);
	}

	private void KeyboardLightBrightness_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		int num = (int)Math.Round(KeyboardLightBrightness.Value);
		KeyboardLightBrightness.Value = num;
		SetSP_KeyboardLightBrightness((byte)num);
	}

	private void KeyboardRGBColor_Initialized(object sender, EventArgs e)
	{
		KeyboardRGBColor.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(KeyboardRGBColor_MouseLeftButtonUp), handledEventsToo: true);
	}

	private void KeyboardRGBColor_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		Set_KeyboardRgbColor();
	}

	private void MaxFanSpeedAdd_Click(object sender, RoutedEventArgs e)
	{
		SetSP_MaxFanSpeed(1);
	}

	private void MaxFanSpeedSub_Click(object sender, RoutedEventArgs e)
	{
		SetSP_MaxFanSpeed(-1);
	}

	private void MaxFanSpeedButton_Checked(object sender, RoutedEventArgs e)
	{
		SetSP_MaxFanSpeedButton(WMIResultState.ON);
	}

	private void MaxFanSpeedButton_Unchecked(object sender, RoutedEventArgs e)
	{
		SetSP_MaxFanSpeedButton(WMIResultState.OFF);
	}

	private void OSDIconButton_Checked(object sender, RoutedEventArgs e)
	{
		Set_OSDIconState(0);
	}

	private void OSDIconButton_Unchecked(object sender, RoutedEventArgs e)
	{
		Set_OSDIconState(1);
	}

	private void CPUTempWallSlider_Initialized(object sender, EventArgs e)
	{
		CPUTempWallSlider.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(CPUTempWallSlider_MouseLeftButtonUp), handledEventsToo: true);
	}

	private void CPUTempWallSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		Set_CPUTempWall();
	}

	private void SPLSlider_Initialized(object sender, EventArgs e)
	{
		SPLSlider.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(SPLSlider_MouseLeftButtonUp), handledEventsToo: true);
	}

	private void SPLSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		Set_SPLSlider();
	}

	private void SPPTSlider_Initialized(object sender, EventArgs e)
	{
		SPPTSlider.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(SPPTSlider_MouseLeftButtonUp), handledEventsToo: true);
	}

	private void SPPTSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		Set_SPPTSlider();
	}

	private void CustomMode_Click(object sender, RoutedEventArgs e)
	{
		bool sP_IsAC = GetSP_IsAC();
		if (!GetSP_IsTypeC())
		{
			if (!sP_IsAC)
			{
				PowerTips powerTips = new PowerTips("请在AC下打开自定义模式");
				powerTips.Owner = Application.Current.MainWindow;
				powerTips.ShowDialog();
			}
			else
			{
				Set_CustomMode(WMICPUPower.OpenState);
			}
		}
	}

	private void ModeReturnButton_Click(object sender, RoutedEventArgs e)
	{
		WorkMode.IsEnabled = true;
		FastestMode.IsEnabled = true;
		PlayingMode.IsEnabled = false;
		Set_CustomMode(WMICPUPower.CloseState);
	}

	private void FanCurveModeReturnButton_Click(object sender, RoutedEventArgs e)
	{
		WorkMode.IsEnabled = true;
		FastestMode.IsEnabled = false;
		PlayingMode.IsEnabled = true;
		Set_CustomMode(WMICPUPower.CloseState);
	}

	private void QuietModeReturnButton_Click(object sender, RoutedEventArgs e)
	{
		StopTemperatureCurve();
		FanToggleButton.IsChecked = false;
		WorkMode.IsEnabled = true;
		FastestMode.IsEnabled = false;
		PlayingMode.IsEnabled = true;
		Set_CustomMode(WMICPUPower.CloseState);
	}

	private void GPUCoreClockSlider_Initialized(object sender, EventArgs e)
	{
		GPUCoreClockSlider.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(GPUCoreClockSlider_MouseLeftButtonUp), handledEventsToo: true);
	}

	private void GPUCoreClockSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		Set_GPUCoreClock(1);
	}

	private void AutoMode_Button_Checked(object sender, RoutedEventArgs e)
	{
		autoModeStatus = true;
		SaveAppSettings();
		Auto_Run();
	}

	private async void Auto_Run()
	{
		monitor = new VariableMonitor();
		monitor.StartMonitoringAsync(() => gpu_power_usage);
		WMIMethodServices.SetValue(WMIMethodName.SystemPerMode, WMISystemPerMode.QuietMode);
		auto_source = new CancellationTokenSource();
		try
		{
			while (!auto_source.IsCancellationRequested)
			{
				await System.Threading.Tasks.Task.Delay(1000);
				AutoMode_Run();
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async void AutoMode_Button_UnChecked(object sender, RoutedEventArgs e)
	{
		autoModeStatus = false;
		SaveAppSettings();
		auto_source.Cancel();
		await monitor.StopMonitoringAsync();
	}

	private void AutoMode_Run()
	{
		if (monitor.IsConditionMet())
		{
			WMIMethodServices.SetValue(WMIMethodName.SystemPerMode, WMISystemPerMode.BalanceMode);
			autoMode = WMISystemPerMode.BalanceMode;
		}
		else if (autoMode != WMISystemPerMode.QuietMode)
		{
			WMIMethodServices.SetValue(WMIMethodName.SystemPerMode, WMISystemPerMode.QuietMode);
			autoMode = WMISystemPerMode.QuietMode;
		}
	}

	// ==================== 桥接控制层（JiaoLongCCUBridge，与官方移植版同后端） ====================

	/// <summary>true = 程序回填 UI / 初始化期间，开关事件不外发命令（防回环）。</summary>
	private bool _bridgeSyncing;

	/// <summary>给已有但无实现的开关挂接桥接协议（这些开关原版全是空壳：只存状态不发命令）。</summary>
	private void HookBridgeControls()
	{
		try
		{
			// 睿频开关：Checked=禁用睿频 → 桥接 TURBO On=false；Unchecked → TURBO On=true
			DisableTurboBoost.Checked += delegate
			{
				if (!_bridgeSyncing) JiaoLongBridge.Send("TURBO", ",\"On\":false");
			};
			DisableTurboBoost.Unchecked += delegate
			{
				if (!_bridgeSyncing) JiaoLongBridge.Send("TURBO", ",\"On\":true");
			};
			// 电压调节开关：开=应用降压步数（默认 20，桥接侧 0-50 钳制），关=0 步
			VoltageStatus.Checked += delegate
			{
				if (!_bridgeSyncing) JiaoLongBridge.Send("UNDERVOLT", ",\"Steps\":20");
			};
			VoltageStatus.Unchecked += delegate
			{
				if (!_bridgeSyncing) JiaoLongBridge.Send("UNDERVOLT", ",\"Steps\":0");
			};
			// 温度墙开关：本机固件忽略该 MIFS 子功能（报告 §14.6），桥接 no-op，仅保留选项与状态
			TempWall.Checked += delegate
			{
				if (!_bridgeSyncing) JiaoLongBridge.Send("TEMP_WALL", ",\"Value\":" + Convert.ToInt32(CPUTempWallSlider.Value));
			};
			TempWall.Unchecked += delegate
			{
				if (!_bridgeSyncing) JiaoLongBridge.Send("TEMP_WALL", ",\"Value\":0");
			};
			// CPU/GPU 限频开关：原版为空壳，桥接暂无对应有效通道，发 FREQ_LIMIT 由桥接 acknowledge
			CPUFrequencyRegulation.Click += delegate
			{
				if (!_bridgeSyncing) JiaoLongBridge.Send("FREQ_LIMIT", ",\"On\":" + (CPUFrequencyRegulation.IsChecked == true ? "true" : "false"));
			};
			GPUFrequencyRegulation.Click += delegate
			{
				if (!_bridgeSyncing) JiaoLongBridge.Send("FREQ_LIMIT", ",\"On\":" + (GPUFrequencyRegulation.IsChecked == true ? "true" : "false"));
			};
			LogHelper.WriteLog("HookBridgeControls", "bridge controls hooked");
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("HookBridgeControls", ex.Message);
		}
	}

	/// <summary>桥接 HomeStatus 回填：仅首次连接时同步 SPL/SPPT/睿频/降压 UI 值（此后以本 UI 操作为准）。</summary>
	private bool _bridgeStatusApplied;

	private void OnBridgeHomeStatus(JsonElement root)
	{
		try
		{
			if (_bridgeStatusApplied) return;
			_bridgeStatusApplied = true;
			_bridgeSyncing = true;
			Dispatcher.BeginInvoke(new System.Action(delegate
			{
				try
				{
					if (root.TryGetProperty("Spl", out var e) && e.ValueKind == JsonValueKind.Number)
						SPLSlider.Value = e.GetInt32();
					if (root.TryGetProperty("Sppt", out var e2) && e2.ValueKind == JsonValueKind.Number)
						SPPTSlider.Value = e2.GetInt32();
					if (root.TryGetProperty("TurboOff", out var e3))
					{
						if (e3.ValueKind == JsonValueKind.True) DisableTurboBoost.IsChecked = true;
						else if (e3.ValueKind == JsonValueKind.False) DisableTurboBoost.IsChecked = false;
					}
					if (root.TryGetProperty("UndervoltSteps", out var e4) && e4.ValueKind == JsonValueKind.Number)
						VoltageStatus.IsChecked = e4.GetInt32() > 0;
					LogHelper.WriteLog("OnBridgeHomeStatus", "UI synced from bridge status");
				}
				catch (Exception ex)
				{
					LogHelper.WriteLog("OnBridgeHomeStatus", "apply: " + ex.Message);
				}
				finally
				{
					_bridgeSyncing = false;
				}
			}));
		}
		catch (Exception ex2)
		{
			LogHelper.WriteLog("OnBridgeHomeStatus", ex2.Message);
		}
	}
}
