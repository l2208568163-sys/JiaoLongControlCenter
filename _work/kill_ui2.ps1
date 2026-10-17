$log = "F:\JiaoLongControlCenter\_work\kill_result2.txt"
"start $(Get-Date -Format HH:mm:ss)" | Out-File $log -Encoding utf8

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Priv {
  [DllImport("advapi32.dll", SetLastError=true)]
  static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr tok);
  [DllImport("advapi32.dll", SetLastError=true)]
  static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
  [DllImport("advapi32.dll", SetLastError=true)]
  static extern bool AdjustTokenPrivileges(IntPtr tok, bool disableAll, ref TOKENPRIVILEGES newp, int len, IntPtr prev, IntPtr ret);
  [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
  [StructLayout(LayoutKind.Sequential)]
  public struct TOKENPRIVILEGES { public int PrivilegeCount; public long Luid; public int Attributes; }
  public static bool EnableDebugPriv() {
    IntPtr tok;
    if (!OpenProcessToken(GetCurrentProcess(), 0x28, out tok)) return false;
    long luid;
    if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out luid)) return false;
    var tp = new TOKENPRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 2 };
    return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
  }
}
'@
$ok = [Priv]::EnableDebugPriv()
"SeDebugPrivilege enabled: $ok" | Out-File $log -Append -Encoding utf8

foreach ($p in Get-Process CCUWinUI -ErrorAction SilentlyContinue) {
    try {
        $p.Kill()
        $p.WaitForExit(3000) | Out-Null
        "killed $($p.Id)" | Out-File $log -Append -Encoding utf8
    } catch {
        "FAILED $($p.Id): $($_.Exception.InnerException.Message ?? $_.Exception.Message)" | Out-File $log -Append -Encoding utf8
    }
}
Start-Sleep -Seconds 2
$left = (Get-Process CCUWinUI -ErrorAction SilentlyContinue | Measure-Object).Count
"remaining: $left" | Out-File $log -Append -Encoding utf8
