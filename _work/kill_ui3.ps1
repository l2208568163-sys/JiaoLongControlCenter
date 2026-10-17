Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Priv2 {
  [DllImport("advapi32.dll", SetLastError=true)]
  static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr tok);
  [DllImport("advapi32.dll", SetLastError=true)]
  static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
  [DllImport("advapi32.dll", SetLastError=true)]
  static extern bool AdjustTokenPrivileges(IntPtr tok, bool disableAll, ref TOKENPRIVILEGES newp, int len, IntPtr prev, IntPtr ret);
  [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll")] public static extern uint GetCurrentProcessId();
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
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Host ("shell elevated: " + $isAdmin)
Write-Host ("SeDebugPrivilege enabled: " + [Priv2]::EnableDebugPriv())
foreach ($p in Get-Process CCUWinUI -ErrorAction SilentlyContinue) {
    try {
        $p.Kill()
        $p.WaitForExit(3000) | Out-Null
        Write-Host ("killed " + $p.Id)
    } catch {
        Write-Host ("FAILED " + $p.Id + ": " + $_.Exception.Message)
    }
}
Start-Sleep -Seconds 2
Write-Host ("remaining: " + (Get-Process CCUWinUI -ErrorAction SilentlyContinue | Measure-Object).Count)
