using System.Runtime.InteropServices;

namespace JiaoLongCCUBridge;

/// <summary>
/// Win 键锁定（用户会话低级键盘钩子）。
///
/// 为什么不用注册表：实测 NoWinKeys 策略只屏蔽 Win 组合键（Win+E 等），
/// 单独按 Win 仍会弹出开始菜单，无法满足"锁定 Win 键"的语义。
/// 为什么必须跑在用户会话：WH_KEYBOARD_LL 只对安装它的桌面会话内的输入生效，
/// 服务的 Session 0 收不到任何用户按键。
///
/// 进程间约定（都放在 Public 交换目录，SYSTEM 与普通用户都可访问）：
///   winkey.lock       存在 = 需要拦截 Win 键
///   winkey.heartbeat  钩子每 5 秒刷新一次，桥接据此判断存活
///   winkey_hook.log   钩子自己的日志（不能写 SYSTEM 的 ProgramData 日志）
/// </summary>
public static class WinkeyHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    /// <summary>SYSTEM 与用户会话都能读写的交换目录。</summary>
    public static string ShareDir => Path.Combine(
        Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public", "JiaoLongCCUBridge");

    public static string StatePath => Path.Combine(ShareDir, "winkey.lock");
    public static string HeartbeatPath => Path.Combine(ShareDir, "winkey.heartbeat");
    private static string HookLogPath => Path.Combine(ShareDir, "winkey_hook.log");

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private static volatile bool _locked;
    private static long _blocked; // 累计吞掉的 Win 键次数（写入心跳，便于外部验证钩子确实在拦）
    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc;

    private static void HookLog(string msg)
    {
        try
        {
            Directory.CreateDirectory(ShareDir);
            File.AppendAllText(HookLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}| {msg}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>钩子进程入口（--winkey-hook）。</summary>
    public static int Run()
    {
        try
        {
            Directory.CreateDirectory(ShareDir);

            // 单实例：只在**本会话**去重即可（Local\ 不需要 SeCreateGlobalPrivilege，
            // 普通用户创建 Global\ 会直接抛 UnauthorizedAccessException —— 那是上一版秒退的原因）
            using var mutex = new Mutex(true, @"Local\JiaoLongWinkeyHook", out bool created);
            if (!created)
            {
                HookLog("已有实例在运行，退出");
                return 0;
            }

            try { _locked = File.Exists(StatePath); } catch { }
            HookLog($"启动 pid={Environment.ProcessId} locked={_locked}");

            new Thread(WatchStateLoop) { IsBackground = true, Name = "winkey-state" }.Start();
            new Thread(HeartbeatLoop) { IsBackground = true, Name = "winkey-heartbeat" }.Start();

            _proc = HookProc;
            _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, GetModuleHandleW(null), 0);
            if (_hook == IntPtr.Zero)
            {
                HookLog($"SetWindowsHookEx 失败 err={Marshal.GetLastWin32Error()}");
                return 2;
            }
            HookLog("钩子已安装");

            while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            UnhookWindowsHookEx(_hook);
            HookLog("退出");
            return 0;
        }
        catch (Exception ex)
        {
            HookLog("异常: " + ex);
            return 3;
        }
    }

    private static void HeartbeatLoop()
    {
        while (true)
        {
            try
            {
                File.WriteAllText(HeartbeatPath,
                    $"{Environment.ProcessId}|{DateTime.UtcNow:O}|locked={_locked}|blocked={Interlocked.Read(ref _blocked)}");
            }
            catch { }
            Thread.Sleep(5000);
        }
    }

    private static void WatchStateLoop()
    {
        while (true)
        {
            try
            {
                bool locked = File.Exists(StatePath);
                if (locked != _locked)
                {
                    _locked = locked;
                    HookLog($"锁定状态 -> {locked}");
                }
            }
            catch { }
            Thread.Sleep(400);
        }
    }

    private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _locked)
        {
            int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode 是首字段
            if (vk == VK_LWIN || vk == VK_RWIN)
            {
                Interlocked.Increment(ref _blocked);
                return (IntPtr)1; // 非零 = 阻止继续传递（down/up 都吞）
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

/// <summary>桥接侧：状态落地、存活判定与守护。</summary>
public static class WinkeyController
{
    private static readonly object Gate = new();
    private static bool _shareReady;

    /// <summary>确保交换目录存在且普通用户可写（心跳/状态文件由用户态钩子读写）。</summary>
    private static void EnsureShareDir()
    {
        if (_shareReady) return;
        lock (Gate)
        {
            if (_shareReady) return;
            try
            {
                Directory.CreateDirectory(WinkeyHook.ShareDir);
                // Users 组 SID：S-1-5-32-545。给目录及其内容 Modify 权限，
                // 否则用户会话里的钩子写不了心跳（SYSTEM 建的目录默认只给 SYSTEM 写）。
                var psi = new System.Diagnostics.ProcessStartInfo("icacls",
                    $"\"{WinkeyHook.ShareDir}\" /grant *S-1-5-32-545:(OI)(CI)M /T /C")
                { CreateNoWindow = true, UseShellExecute = false };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(5000);
                _shareReady = true;
                Log.Info($"winkey share dir ready: {WinkeyHook.ShareDir}");
            }
            catch (Exception ex)
            {
                Log.Error("winkey share dir: " + ex.Message);
            }
        }
    }

    /// <summary>钩子是否活着：心跳文件 20 秒内刷新过。</summary>
    public static bool IsHookRunning()
    {
        try
        {
            var fi = new FileInfo(WinkeyHook.HeartbeatPath);
            if (!fi.Exists) return false;
            return (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalSeconds < 20;
        }
        catch { return false; }
    }

    private static bool WriteState(bool locked)
    {
        try
        {
            EnsureShareDir();
            if (locked)
            {
                if (!File.Exists(WinkeyHook.StatePath))
                    File.WriteAllText(WinkeyHook.StatePath, "1");
            }
            else
            {
                if (File.Exists(WinkeyHook.StatePath))
                    File.Delete(WinkeyHook.StatePath);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"winkey state write failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>确保钩子进程在用户会话中运行。</summary>
    public static bool EnsureHookRunning()
    {
        if (IsHookRunning()) return true;
        lock (Gate)
        {
            if (IsHookRunning()) return true;
            EnsureShareDir();

            string exe = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Log.Error("winkey hook: 找不到自身可执行文件路径");
                return false;
            }

            // 通过 cmd 包一层并重定向输出：单文件 exe 若在 apphost 阶段失败（解压/加载），
            // 托管代码根本跑不到，只能靠这里的 stdout/stderr 留下线索。
            string cmdExe = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
            string bat = Path.Combine(WinkeyHook.ShareDir, "start_winkey_hook.cmd");
            string outLog = Path.Combine(WinkeyHook.ShareDir, "hook_stdout.txt");

            // 通道探针：先用同步执行验证"以用户身份跑命令"这条路本身通不通。
            // 结果会进主日志，便于区分是通道问题还是目标进程自身问题。
            try
            {
                if (UserSessionRunner.RunInUserSession(cmdExe, "/c echo probe-ok", out string probeOut, 6000))
                    Log.Info($"user-session probe: '{probeOut.Trim()}'");
                else
                    Log.Error("user-session probe: 通道不可用");
            }
            catch (Exception ex) { Log.Error("user-session probe: " + ex.Message); }
            try
            {
                // DOTNET_BUNDLE_EXTRACT_BASE_DIR：单文件 exe 默认解压到 %TEMP%，
                // 若子进程拿到的环境块不是用户环境（TEMP 指向 System 目录）就会静默失败，
                // 这里显式把解压目录钉到用户可写的交换目录，规避环境差异。
                string extractDir = Path.Combine(WinkeyHook.ShareDir, "dn");
                Directory.CreateDirectory(extractDir);
                File.WriteAllText(bat,
                    "@echo off\r\n" +
                    $"set DOTNET_BUNDLE_EXTRACT_BASE_DIR={extractDir}\r\n" +
                    $"\"{exe}\" --winkey-hook > \"{outLog}\" 2>&1\r\n",
                    System.Text.Encoding.ASCII);
            }
            catch (Exception ex)
            {
                Log.Error("winkey launcher script: " + ex.Message);
            }

            if (!UserSessionRunner.StartInUserSession(cmdExe, $"/c \"{bat}\"", out _))
                return false;

            for (int i = 0; i < 25 && !IsHookRunning(); i++) Thread.Sleep(200);
            bool ok = IsHookRunning();
            Log.Info($"winkey hook 启动结果: {ok}");
            return ok;
        }
    }

    /// <summary>应用锁定状态：写文件 + 拉起钩子。</summary>
    public static bool Apply(bool locked)
    {
        if (!WriteState(locked)) return false;
        return EnsureHookRunning();
    }

    public static bool IsLocked()
    {
        try { return File.Exists(WinkeyHook.StatePath); } catch { return false; }
    }

    /// <summary>启动时恢复，并**预热**钩子（空闲不拦截），保证后续切锁即时生效。</summary>
    public static void Restore(bool locked)
    {
        if (!WriteState(locked))
            Log.Error("winkey restore: 状态文件写入失败");
        bool ok = EnsureHookRunning();
        Log.Info($"restore winkey: locked={locked} hook_running={ok}");
    }
}
