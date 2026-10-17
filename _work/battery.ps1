Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
'@ -Name Win -Namespace Native
Add-Type -AssemblyName System.Windows.Forms

function Click3([int]$x, [int]$y) {
    [Native.Win]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 350
    1..3 | ForEach-Object {
        [Native.Win]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 90
        [Native.Win]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 160
    }
    $cp = [System.Windows.Forms.Cursor]::Position
    Write-Host "clicked3 $x,$y cursor=($($cp.X),$($cp.Y))"
}

Write-Host "=== A: Logo tab pill ==="
Click3 800 225
Start-Sleep -Milliseconds 800
Write-Host "=== B: page background below logo ==="
Click3 886 900
Start-Sleep -Milliseconds 800
Write-Host "=== C: logo center ==="
Click3 886 634
Start-Sleep -Milliseconds 500
Write-Host "=== done ==="
