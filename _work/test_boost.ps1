$c = Get-CimInstance -Namespace root/WMI -ClassName MICommonInterface
function Invoke-MI([byte[]]$in) {
    $r = Invoke-CimMethod -InputObject $c -MethodName MiInterface -Arguments @{ InData = [byte[]]$in }
    return $r.OutData
}
function Get-Fn($fn) {
    $in = New-Object byte[] 32; $in[1] = 250; $in[3] = $fn
    return (Invoke-MI $in)[4]
}
function Set-Fn($fn, $v) {
    $in = New-Object byte[] 32; $in[1] = 251; $in[3] = $fn; $in[4] = $v
    Invoke-MI $in | Out-Null
}
function Get-Rpm {
    $in = New-Object byte[] 32; $in[1] = 250; $in[3] = 13
    $o = Invoke-MI $in
    return (( ($o[5] -shl 8) + $o[4] ), (($o[7] -shl 8) + $o[6]))
}

Write-Host ("fn20 switch = " + (Get-Fn 20) + "  fn21 value = " + (Get-Fn 21))
$r1, $r2 = Get-Rpm
Write-Host ("RPM before: CPU=" + $r1 + " GPU=" + $r2)

# 试验1：fn20=1 + fn21=100
Set-Fn 20 1
Start-Sleep -Milliseconds 300
Set-Fn 21 100
Start-Sleep -Seconds 3
$r1, $r2 = Get-Rpm
Write-Host ("fn21=100 -> RPM: CPU=" + $r1 + " GPU=" + $r2 + "   fn21 readback=" + (Get-Fn 21))
