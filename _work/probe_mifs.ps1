$c = Get-CimInstance -Namespace root/WMI -ClassName MICommonInterface
function Invoke-MI([byte[]]$in) {
    $r = Invoke-CimMethod -InputObject $c -MethodName MiInterface -Arguments @{ InData = [byte[]]$in }
    return $r.OutData
}
# 只读探测 fn 0..40（GET=0xFA），记录响应
foreach ($fn in 0..40) {
    $in = New-Object byte[] 32
    $in[1] = 250
    $in[3] = $fn
    try {
        $out = Invoke-MI $in
        $hex = ($out | Select-Object -First 10 | ForEach-Object { $_.ToString('X2') }) -join ' '
        $val = $out[4]
        Write-Host ("fn={0,2} => {1}  (b4={2})" -f $fn, $hex, $val)
    } catch {
        Write-Host ("fn={0,2} => ERROR: {1}" -f $fn, $_.Exception.Message)
    }
}
