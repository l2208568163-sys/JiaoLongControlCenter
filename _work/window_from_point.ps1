Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WFP {
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    public struct POINT { public int X; public int Y; }
    public struct RECT { public int L, T, R, B; }
    public static string Probe(int x, int y) {
        var p = new POINT { X = x, Y = y };
        IntPtr h = WindowFromPoint(p);
        if (h == IntPtr.Zero) return "none";
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, 256);
        string title = sb.ToString();
        sb.Clear(); GetClassName(h, sb, 256);
        string cls = sb.ToString();
        uint pid; GetWindowThreadProcessId(h, out pid);
        RECT r; GetWindowRect(h, out r);
        IntPtr root = GetAncestor(h, 2); // GA_ROOT
        var sb2 = new StringBuilder(256);
        string rootTitle = "";
        uint rootPid = 0;
        if (root != IntPtr.Zero) {
            GetWindowText(root, sb2, 256); rootTitle = sb2.ToString();
            GetWindowThreadProcessId(root, out rootPid);
        }
        return string.Format("hwnd=0x{0:X} class='{1}' title='{2}' pid={3} rect=({4},{5})-({6},{7}) | ROOT hwnd=0x{8:X} class-title='{9}' pid={10}",
            h.ToInt64(), cls, title, pid, r.L, r.T, r.R, r.B, root.ToInt64(), rootTitle, rootPid);
    }
}
'@
Write-Host "point(886,634):" ([WFP]::Probe(886, 634))
Write-Host "point(624,488):" ([WFP]::Probe(624, 488))
Write-Host "point(300,700):" ([WFP]::Probe(300, 700))
