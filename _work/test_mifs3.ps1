$c = Get-CimInstance -Namespace root/WMI -ClassName MICommonInterface
Write-Host ("Instance: " + $c.InstanceName + " Active=" + $c.Active)

function Invoke-MI([byte[]]$in) {
    $args_ = @{ InData = [byte[]]$in }
    $r = Invoke-CimMethod -InputObject $c -MethodName MiInterface -Arguments $args_
    return $r.OutData
}

foreach ($t in 0,1,2) {
    $in = New-Object byte[] 32
    $in[1] = $t
    $in[3] = 22   # CPUThermometer
    try {
        $out = Invoke-MI $in
        $hex = ($out | Select-Object -First 10 | ForEach-Object { $_.ToString('X2') }) -join ' '
        Write-Host ("type=$t fn=22 => " + $hex)
    } catch {
        Write-Host ("type=$t ERROR: " + $_.Exception.Message)
    }
}
