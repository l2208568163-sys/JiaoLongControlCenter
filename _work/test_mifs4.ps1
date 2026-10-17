$c = Get-CimInstance -Namespace root/WMI -ClassName MICommonInterface

function Invoke-MI([byte[]]$in) {
    $r = Invoke-CimMethod -InputObject $c -MethodName MiInterface -Arguments @{ InData = [byte[]]$in }
    return $r.OutData
}

# WMIMethodType.Get = 250 (0xFA)
$in = New-Object byte[] 32
$in[1] = 250
$in[3] = 22   # CPUThermometer
try {
    $out = Invoke-MI $in
    $hex = ($out | Select-Object -First 10 | ForEach-Object { $_.ToString('X2') }) -join ' '
    Write-Host ("CPUThermometer GET => " + $hex)
} catch {
    Write-Host ("ERROR: " + $_.Exception.Message)
}

$in2 = New-Object byte[] 32
$in2[1] = 250
$in2[3] = 13   # CPUGPUFanSpeed
try {
    $out2 = Invoke-MI $in2
    $hex2 = ($out2 | Select-Object -First 10 | ForEach-Object { $_.ToString('X2') }) -join ' '
    Write-Host ("CPUGPUFanSpeed GET => " + $hex2)
} catch {
    Write-Host ("ERROR: " + $_.Exception.Message)
}
