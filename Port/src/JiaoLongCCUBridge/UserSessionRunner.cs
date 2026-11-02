using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace JiaoLongCCUBridge;

/// <summary>
/// 在用户会话中运行进程（服务 Session 0 的 GDI 显示 API 不可用，
/// 刷新率查询/切换必须借用户令牌执行）。返回进程 stdout。
/// </summary>
public static class UserSessionRunner
{
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumProcesses([Out] uint[] processIds, uint arraySizeBytes, out uint bytesCopied);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attrs, int impLevel, int tokenType, out IntPtr newToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint pid, out uint sessionId);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUserW(IntPtr token, string? appName, string cmdLine, IntPtr procAttrs, IntPtr threadAttrs, bool inheritHandles,
        uint creationFlags, IntPtr env, string? workingDir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);

    [DllImport("userenv.dll")]
    private static extern bool DestroyEnvironmentBlock(IntPtr env);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, IntPtr attrs, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr hFile, [Out] byte[] buffer, uint toRead, out uint read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekNamedPipe(IntPtr h, IntPtr buf, uint bufSize, IntPtr read, out uint avail, IntPtr left);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr h, uint ms);

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x400;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint STILL_ACTIVE = 259;
    private const uint STARTF_USESTDHANDLES = 0x100;

    public static bool RunInUserSession(string exe, string args, out string output, int timeoutMs = 15000)
    {
        output = "";
        IntPtr token = IntPtr.Zero, dup = IntPtr.Zero, env = IntPtr.Zero;
        IntPtr hProcess = IntPtr.Zero, hThread = IntPtr.Zero, readEnd = IntPtr.Zero, writeEnd = IntPtr.Zero, errRead = IntPtr.Zero, errWrite = IntPtr.Zero;
        try
        {
            uint sessionId = WTSGetActiveConsoleSessionId();
            Log.Info($"user session id: {sessionId}");

            dup = TryGetUserToken(sessionId);
            if (dup == IntPtr.Zero)
            {
                Log.Error("UserSessionRunner: 未找到活跃用户令牌");
                return false;
            }
            token = dup;
            dup = IntPtr.Zero;

            IntPtr sa = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_ATTRIBUTES_RAW>());
            Marshal.StructureToPtr(new SECURITY_ATTRIBUTES_RAW { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES_RAW>(), bInheritHandle = true }, sa, false);
            CreatePipe(out readEnd, out writeEnd, sa, 0);
            CreatePipe(out errRead, out errWrite, sa, 0);

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            si.dwFlags = (int)STARTF_USESTDHANDLES;
            si.hStdOutput = writeEnd;
            si.hStdError = errWrite;

            CreateEnvironmentBlock(out env, token, false);

            if (!CreateProcessAsUserW(token, null, $"\"{exe}\" {args}", IntPtr.Zero, IntPtr.Zero, true,
                    CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT, env, null, ref si, out PROCESS_INFORMATION pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUserW");
            hProcess = pi.hProcess;
            hThread = pi.hThread;
            CloseHandle(writeEnd);
            CloseHandle(errWrite);
            writeEnd = IntPtr.Zero;
            errWrite = IntPtr.Zero;

            var sb = new StringBuilder();
            var sbErr = new StringBuilder();
            var buf = new byte[8192];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool any = false;
                if (PeekNamedPipe(readEnd, IntPtr.Zero, 0, IntPtr.Zero, out uint avail, IntPtr.Zero) && avail > 0)
                {
                    if (ReadFile(readEnd, buf, Math.Min(avail, (uint)buf.Length), out uint read, IntPtr.Zero) && read > 0)
                    { sb.Append(Encoding.UTF8.GetString(buf, 0, (int)read)); any = true; }
                }
                if (PeekNamedPipe(errRead, IntPtr.Zero, 0, IntPtr.Zero, out uint availE, IntPtr.Zero) && availE > 0)
                {
                    if (ReadFile(errRead, buf, Math.Min(availE, (uint)buf.Length), out uint readE, IntPtr.Zero) && readE > 0)
                    { sbErr.Append(Encoding.UTF8.GetString(buf, 0, (int)readE)); any = true; }
                }
                if (!any)
                {
                    GetExitCodeProcess(hProcess, out uint code);
                    if (code != STILL_ACTIVE) break;
                    Thread.Sleep(60);
                }
            }
            GetExitCodeProcess(hProcess, out uint exitCode);
            output = sb.ToString();
            if (sbErr.Length > 0) Log.Error($"UserSessionRunner stderr: {sbErr}");
            Log.Info($"UserSessionRunner exit={exitCode} stdoutLen={output.Length}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("UserSessionRunner: " + ex.Message);
            return false;
        }
        finally
        {
            foreach (var h in new[] { token, dup, env, hProcess, hThread, readEnd, writeEnd, errRead, errWrite })
                if (h != IntPtr.Zero) CloseHandle(h);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr h, out uint code);

    /// <summary>取活跃控制台会话里的用户主令牌（已 Duplicate 为 primary）。用完需 CloseHandle。</summary>
    private static IntPtr TryGetUserToken(uint sessionId)
    {
        // 优先 explorer.exe：它才是真正的交互式用户进程。
        // 直接遍历进程会先撞上 winlogon/csrss 这类 Session 1 里的 SYSTEM 进程，
        // 用它们的令牌启动子进程会得到一个"没有用户配置环境"的 SYSTEM 进程。
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (ProcessIdToSessionId((uint)p.Id, out uint sess) && sess == sessionId)
                {
                    IntPtr t = TokenFromPid((uint)p.Id);
                    if (t != IntPtr.Zero) return t;
                }
            }
            catch { }
            finally { p.Dispose(); }
        }

        // 兜底：同会话任意可打开令牌的进程
        uint[] pids = new uint[4096];
        if (!EnumProcesses(pids, (uint)(pids.Length * 4), out _)) return IntPtr.Zero;
        foreach (uint pid in pids)
        {
            if (pid == 0) continue;
            if (!ProcessIdToSessionId(pid, out uint sess) || sess != sessionId) continue;
            IntPtr t = TokenFromPid(pid);
            if (t != IntPtr.Zero) return t;
        }
        return IntPtr.Zero;
    }

    private static IntPtr TokenFromPid(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(h, TOKEN_DUPLICATE | TOKEN_QUERY, out IntPtr tok)) return IntPtr.Zero;
            try
            {
                return DuplicateTokenEx(tok, MAXIMUM_ALLOWED | TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY,
                    IntPtr.Zero, SecurityImpersonation, TokenPrimary, out IntPtr dup)
                    ? dup
                    : IntPtr.Zero;
            }
            finally { CloseHandle(tok); }
        }
        finally { CloseHandle(h); }
    }

    /// <summary>
    /// 以当前登录用户身份启动一个**常驻**进程（不等待退出）。
    /// 与 RunInUserSession 的区别：指定 winsta0\default 桌面、不建管道、立即返回 PID。
    /// 用于键盘钩子这类必须活在用户桌面会话里的助手。
    /// </summary>
    public static bool StartInUserSession(string exe, string args, out int pid)
    {
        pid = 0;
        IntPtr token = IntPtr.Zero, env = IntPtr.Zero;
        try
        {
            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF) { Log.Error("StartInUserSession: 无活动控制台会话"); return false; }

            token = TryGetUserToken(sessionId);
            if (token == IntPtr.Zero) { Log.Error("StartInUserSession: 未找到用户令牌"); return false; }

            bool envOk = CreateEnvironmentBlock(out env, token, false);
            Log.Info($"StartInUserSession: token=ok envBlock={envOk} envNull={env == IntPtr.Zero}");
            // lpDesktop 必须留空：显式指定 "winsta0\default" 时，服务（Session 0 的
            // Service-0x0-xxx$ 窗口站）到用户桌面建进程会失败/秒退；留空则由令牌的
            // 会话决定桌面，实测与 RunInUserSession 行为一致。
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };

            if (!CreateProcessAsUserW(token, null, $"\"{exe}\" {args}", IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT, env, null, ref si, out PROCESS_INFORMATION pi))
            {
                int err = Marshal.GetLastWin32Error();
                Log.Error($"StartInUserSession CreateProcessAsUserW failed err={err}");
                return false;
            }
            pid = (int)pi.dwProcessId;
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);

            // 诊断：子进程若在托管入口前就死掉（apphost 解压失败/被拦截），
            // 只有退出码能说明问题，所以这里短暂回查一次。
            Thread.Sleep(600);
            try
            {
                using var child = Process.GetProcessById(pid);
                if (child.HasExited)
                    Log.Error($"StartInUserSession: 子进程已退出 exit=0x{child.ExitCode:X8}");
                else
                    Log.Info($"StartInUserSession: {Path.GetFileName(exe)} {args} -> pid={pid} 存活");
            }
            catch (ArgumentException)
            {
                Log.Error("StartInUserSession: 子进程已退出（无法读取退出码）");
            }
            catch (Exception ex)
            {
                Log.Info($"StartInUserSession: {Path.GetFileName(exe)} {args} -> pid={pid} ({ex.Message})");
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("StartInUserSession: " + ex.Message);
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES_RAW
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }
}
