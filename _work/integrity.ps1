Add-Type @'
using System;
using System.Runtime.InteropServices;
public class IL2 {
    [DllImport("advapi32.dll")] public static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr tok);
    [DllImport("advapi32.dll")] public static extern bool GetTokenInformation(IntPtr tok, int cls, IntPtr info, uint len, out uint ret);
    [DllImport("advapi32.dll")] public static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr str);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr h);
    public static string GetIL(int pid) {
        IntPtr h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return "open-failed:" + Marshal.GetLastWin32Error();
        IntPtr tok;
        if (!OpenProcessToken(h, 0x0008, out tok)) return "tok-failed:" + Marshal.GetLastWin32Error();
        uint ret;
        GetTokenInformation(tok, 25, IntPtr.Zero, 0, out ret);
        if (ret == 0) return "size-0";
        IntPtr buf = Marshal.AllocHGlobal((int)ret);
        try {
            if (!GetTokenInformation(tok, 25, buf, ret, out ret)) return "query-failed:" + Marshal.GetLastWin32Error();
            IntPtr sid = Marshal.ReadIntPtr(buf);
            IntPtr str;
            if (!ConvertSidToStringSid(sid, out str)) return "sid-failed:" + Marshal.GetLastWin32Error();
            string s = Marshal.PtrToStringAuto(str);
            LocalFree(str);
            return s;
        } finally { Marshal.FreeHGlobal(buf); }
    }
}
'@
Write-Host ("PS     pid=$PID IL=" + [IL2]::GetIL($PID))
Get-Process CCUWinUI -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ("CCUWinUI pid=" + $_.Id + " IL=" + [IL2]::GetIL($_.Id))
}
Get-Process JiaoLongCCUBridge -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ("Bridge pid=" + $_.Id + " IL=" + [IL2]::GetIL($_.Id))
}
