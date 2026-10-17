Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "top-level windows: $($all.Count)"
foreach ($e in $all) {
    $c = $e.Current
    $pid = 0
    try { $pid = $e.Current.ProcessId } catch {}
    Write-Host "pid=$pid name='$($c.Name)' type=$($c.ControlType.ProgrammaticName) rect=$($c.BoundingRectangle)"
}
