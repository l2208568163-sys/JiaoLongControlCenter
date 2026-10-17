param([int]$x, [int]$y)
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
public struct POINT { public int X; public int Y; }
'@ -Name Win -Namespace Native

[Native.Win]::SetCursorPos($x, $y) | Out-Null
Start-Sleep -Milliseconds 300
# 三连击：三对 down/up，间隔 ~180ms，总窗口 ~540ms < 0.9s
1..3 | ForEach-Object {
    [Native.Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Native.Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
}
$p = New-Object Native.POINT
[Native.Win]::GetCursorPos([ref]$p) | Out-Null
Write-Host "triple-clicked at $x,$y (cursor now $($p.X),$($p.Y))"
