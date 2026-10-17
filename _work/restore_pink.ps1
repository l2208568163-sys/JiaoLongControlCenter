$mo = New-Object System.Management.ManagementObject('root\WMI', "MICommonInterface.InstanceName='ACPI\PNP0C14\MIFS_0'", $null)
function Set-Fn2($fn, $vals) {
    $in = New-Object byte[] 32; $in[1] = 251; $in[3] = $fn
    for ($i = 0; $i -lt $vals.Count; $i++) { $in[4 + $i] = $vals[$i] }
    $mp = $mo.GetMethodParameters('MiInterface'); $mp['InData'] = [byte[]]$in
    $mo.InvokeMethod('MiInterface', $mp, $null) | Out-Null
}
Set-Fn2 16 @(2)
Start-Sleep -Milliseconds 600
Set-Fn2 17 @([byte]255, [byte]0, [byte]114)
Set-Fn2 18 @(3)
Start-Sleep -Milliseconds 500
$in = New-Object byte[] 32; $in[1] = 250; $in[3] = 17
$mp = $mo.GetMethodParameters('MiInterface'); $mp['InData'] = [byte[]]$in
$o = $mo.InvokeMethod('MiInterface', $mp, $null)['OutData']
Write-Host ("restored color: {0:X2} {1:X2} {2:X2} mode={3} bright={4}" -f $o[4], $o[5], $o[6], (Get-Fn16), 0)
function Get-Fn16 { $in2 = New-Object byte[] 32; $in2[1] = 250; $in2[3] = 16; $mp2 = $mo.GetMethodParameters('MiInterface'); $mp2['InData'] = [byte[]]$in2; return ($mo.InvokeMethod('MiInterface', $mp2, $null)['OutData'])[4] }
Write-Host ("verify mode: " + (Get-Fn16))
