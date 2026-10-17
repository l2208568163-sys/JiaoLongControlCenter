Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
'@ -Name Win2 -Namespace Native

$root = [System.Windows.Automation.AutomationElement]::RootElement
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$win = $null
foreach ($e in $all) { try { if ($e.Current.Name -like "*控制中心*") { $win = $e; break } } catch {} }
if (-not $win) { Write-Host "NO-WINDOW"; exit 1 }
$hwnd = [IntPtr]$win.Current.NativeWindowHandle

# 前台化
[Native.Win2]::ShowWindow($hwnd, 9) | Out-Null   # SW_RESTORE
[Native.Win2]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 800

# 找 Logo
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
Write-Host "logo center=($cx,$cy)"

[Native.Win2]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 400
1..3 | ForEach-Object {
    [Native.Win2]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 90
    [Native.Win2]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 170
}
Write-Host "TRIPLE-CLICKED after foregrounding"
