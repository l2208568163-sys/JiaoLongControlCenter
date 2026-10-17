Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);' -Name W3 -Namespace N
$root = [System.Windows.Automation.AutomationElement]::RootElement
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($e in $all) { try { if ($e.Current.Name -like "*控制中心*") { $h = [IntPtr]$e.Current.NativeWindowHandle; [N.W3]::ShowWindow($h, 9) | Out-Null; [N.W3]::SetForegroundWindow($h) | Out-Null; Write-Host "foregrounded"; break } } catch {} }
