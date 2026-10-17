Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
'@ -Name Win -Namespace Native

$ErrorActionPreference = "Continue"
$x = [int]$args[0]; $y = [int]$args[1]

[Native.Win]::SetCursorPos($x, $y) | Out-Null
Start-Sleep -Milliseconds 400
[Native.Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # down
Start-Sleep -Milliseconds 80
[Native.Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # up
Write-Host "clicked at $x,$y"
