Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$win = $null
foreach ($e in $all) {
    try {
        $n = $e.Current.Name
        if ($n -like "*控制中心*") { $win = $e; break }
    } catch {}
}
if (-not $win) { Write-Host "window not found"; exit 1 }
Write-Host "window: '$($win.Current.Name)' rect=$($win.Current.BoundingRectangle) pid=$($win.Current.ProcessId)"

$desc = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "descendants: $($desc.Count)"
foreach ($e in $desc) {
    $c = $e.Current
    $r = $c.BoundingRectangle
    if ($r.Width -ge 150 -or $c.ControlType.ProgrammaticName -eq "ControlType.Image") {
        Write-Host "$($c.ControlType.ProgrammaticName) name='$($c.Name)' autoId='$($c.AutomationId)' rect=$($r.X),$($r.Y) $($r.Width)x$($r.Height)"
    }
}
