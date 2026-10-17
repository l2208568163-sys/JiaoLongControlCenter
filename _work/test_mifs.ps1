$mo = New-Object System.Management.ManagementObject("root\WMI", "MICommonInterface.InstanceName='ACPI\PNP0C14\MIFS_0'", $null)
function Get-MI([byte]$type, [byte]$fn) {
    $in = New-Object byte[] 32
    $in[1] = $type; $in[3] = $fn
    $mp = $mo.GetMethodParameters("MiInterface")
    $mp["InData"] = [byte[]]$in
    $out = $mo.InvokeMethod("MiInterface", $mp, $null)
    $od = $out["OutData"]
    return ($od | Select-Object -First 12 | ForEach-Object { $_.ToString("X2") }) -join " "
}
# WMIMethodType: guess Get=1? try 0,1,2 for CPUThermometer(22)
foreach ($t in 0,1,2) {
    try { Write-Host "CPUThermometer(22) type=$t =>" (Get-MI $t 22) } catch { Write-Host "type=$t ERROR: $($_.Exception.Message)" }
}
