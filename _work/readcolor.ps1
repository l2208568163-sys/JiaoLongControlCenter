$mo = New-Object System.Management.ManagementObject('root\WMI', "MICommonInterface.InstanceName='ACPI\PNP0C14\MIFS_0'", $null)
$in = New-Object byte[] 32; $in[1] = 250; $in[3] = 17
$mp = $mo.GetMethodParameters('MiInterface'); $mp['InData'] = [byte[]]$in
$o = $mo.InvokeMethod('MiInterface', $mp, $null)['OutData']
Write-Host ("fn17 color: {0:X2} {1:X2} {2:X2}" -f $o[4], $o[5], $o[6])
