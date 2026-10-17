param([string]$Phase)  # "locate" or "click"
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

if ($Phase -eq "locate") {
    # 找 ~218x250 的 Image（Logo）和“主页”导航项
    foreach ($e in $desc) {
        $c = $e.Current; $r = $c.BoundingRectangle
        if ($c.ControlType.ProgrammaticName -eq "ControlType.Image" -and $r.Width -gt 150 -and $r.Width -lt 320 -and $r.Height -gt 150 -and $r.Height -lt 320) {
            Write-Host "LOGO-IMAGE rect=$([int]$r.X),$([int]$r.Y) $([int]$r.Width)x$([int]$r.Height)"
        }
        if ($c.Name -eq "主页") {
            Write-Host "NAV-HOME rect=$([int]$r.X),$([int]$r.Y) $([int]$r.Width)x$([int]$r.Height)"
        }
    }
    exit 0
}

if ($Phase -eq "click") {
    $lx = [int]$args[0]; $ly = [int]$args[1]
    [Native.Win]::SetCursorPos($lx, $ly) | Out-Null
    Start-Sleep -Milliseconds 300
    1..3 | ForEach-Object {
        [Native.Win]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60
        [Native.Win]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 120
    }
    Write-Host "TRIPLE-CLICKED $lx,$ly"
    exit 0
}
