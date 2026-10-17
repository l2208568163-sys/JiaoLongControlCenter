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

$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Radar")
$radar = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if (-not $radar) { Write-Host "NO-RADAR-TEXT"; exit 2 }
$r = $radar.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width/2); $cy = [int]($r.Y + $r.Height/2)
Write-Host "Radar pill rect=$([int]$r.X),$([int]$r.Y) $([int]$r.Width)x$([int]$r.Height) click=($cx,$cy)"

[Native.Win]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 300
[Native.Win]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 70
[Native.Win]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
Write-Host "single click sent"
