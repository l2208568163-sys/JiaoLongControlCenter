Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
'@ -Name Win -Namespace Native

function Find-Window {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) { try { if ($e.Current.Name -like "*控制中心*") { return $e } } catch {} }
    return $null
}

# 1) 等窗口出现
$win = $null
foreach ($i in 1..10) { $win = Find-Window; if ($win) { break }; Start-Sleep -Seconds 2 }
if (-not $win) { Write-Host "NO-WINDOW"; exit 1 }

# 2) 等窗口矩形稳定（连续 4 次 × 1.5s 不变才算安静）
$last = ""; $stable = 0
foreach ($i in 1..20) {
    $r = $win.Current.BoundingRectangle
    $cur = "$([int]$r.X),$([int]$r.Y),$([int]$r.Width),$([int]$r.Height)"
    if ($cur -eq $last) { $stable++ } else { $stable = 0 }
    $last = $cur
    if ($stable -ge 4) { break }
    Start-Sleep -Milliseconds 1500
    $win = Find-Window
    if (-not $win) { Write-Host "WINDOW-GONE"; exit 1 }
}
Write-Host "stable rect: $last (stable=$stable)"

# 3) 找 Logo
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
Write-Host "logo rect=$([int]$logo.X),$([int]$logo.Y) $([int]$logo.Width)x$([int]$logo.Height) center=($cx,$cy)"

# 4) 三连击
[Native.Win]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 400
1..3 | ForEach-Object {
    [Native.Win]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 90
    [Native.Win]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 170
}
Write-Host "TRIPLE-CLICKED at ($cx,$cy)"
