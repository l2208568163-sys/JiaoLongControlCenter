Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class MSG {
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

$root = [System.Windows.Automation.AutomationElement]::RootElement
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$win = $null
foreach ($e in $all) { try { if ($e.Current.Name -like "*控制中心*") { $win = $e; break } } catch {} }
if (-not $win) { Write-Host "NO-WINDOW"; exit 1 }
$hwnd = [IntPtr]$win.Current.NativeWindowHandle
$wr = $win.Current.BoundingRectangle
Write-Host "hwnd=0x$($hwnd.ToString('X')) window=($([int]$wr.X),$([int]$wr.Y)) $([int]$wr.Width)x$([int]$wr.Height)"

# logo 中心（UIA 实时找）
$desc = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$logo = $null
foreach ($e in $desc) {
    try {
        $c = $e.Current; $r = $c.BoundingRectangle
        if ($c.ControlType.ProgrammaticName -eq "ControlType.Image" -and $r.Width -gt 150 -and $r.Width -lt 320 -and $r.Height -gt 150 -and $r.Height -lt 320) { $logo = $r; break }
    } catch {}
}
if (-not $logo) { Write-Host "NO-LOGO"; exit 2 }
$cx = [int]($logo.X + $logo.Width/2); $cy = [int]($logo.Y + $logo.Height/2)
Write-Host "logo center screen=($cx,$cy)"

# 窗口客户区坐标（LPARAM: y<<16 | x）
$lparam = [IntPtr](($cy - [int]$wr.Y) * 0x10000 + ($cx - [int]$wr.X))
$WM_LBUTTONDOWN = 0x0201; $WM_LBUTTONUP = 0x0202; $MK_LBUTTON = 0x0001

# 模拟三连击：DOWN/UP ×3
1..3 | ForEach-Object {
    [MSG]::PostMessage($hwnd, $WM_LBUTTONDOWN, [IntPtr]$MK_LBUTTON, $lparam) | Out-Null
    Start-Sleep -Milliseconds 90
    [MSG]::PostMessage($hwnd, $WM_LBUTTONUP, [IntPtr]::Zero, $lparam) | Out-Null
    Start-Sleep -Milliseconds 160
}
Write-Host "posted 3 clicks to hwnd"
