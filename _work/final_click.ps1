Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
'@ -Name Win -Namespace Native

$root = [System.Windows.Automation.AutomationElement]::RootElement
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$win = $null
foreach ($e in $all) { try { if ($e.Current.Name -like "*控制中心*") { $win = $e; break } } catch {} }
if (-not $win) { Write-Host "NO-WINDOW"; exit 1 }
$desc = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# 1) Invoke 主页
$nav = $null
foreach ($e in $desc) { try { if ($e.Current.Name -eq "主页") { $nav = $e; break } } catch {} }
if (-not $nav) { Write-Host "NO-NAV"; exit 1 }
$inv = $null
try { $inv = $nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) } catch {}
if ($inv) { $inv.Invoke(); Write-Host "NAV-INVOKED" } else {
    $r = $nav.Current.BoundingRectangle
    $cx = [int]($r.X + $r.Width/2); $cy = [int]($r.Y + $r.Height/2)
    [Native.Win]::SetCursorPos($cx, $cy) | Out-Null; Start-Sleep -Milliseconds 200
    [Native.Win]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [Native.Win]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
    Write-Host "NAV-CLICKED $cx,$cy"
}
Start-Sleep -Milliseconds 2500

# 2) 找 Logo Image（~218x250）
$desc = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$logo = $null
foreach ($e in $desc) {
    try {
        $c = $e.Current; $r = $c.BoundingRectangle
        if ($c.ControlType.ProgrammaticName -eq "ControlType.Image" -and $r.Width -gt 150 -and $r.Width -lt 320 -and $r.Height -gt 150 -and $r.Height -lt 320) {
            $logo = $r; break
        }
    } catch {}
}
if (-not $logo) { Write-Host "NO-LOGO-IMAGE"; exit 2 }
$cx = [int]($logo.X + $logo.Width/2); $cy = [int]($logo.Y + $logo.Height/2)
Write-Host "LOGO at $([int]$logo.X),$([int]$logo.Y) $([int]$logo.Width)x$([int]$logo.Height) -> click $cx,$cy"

# 3) 三连击
[Native.Win]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 300
1..3 | ForEach-Object {
    [Native.Win]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [Native.Win]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 120
}
Write-Host "TRIPLE-CLICKED"
