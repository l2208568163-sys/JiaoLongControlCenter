$c = Get-CimInstance -Namespace root/WMI -ClassName MICommonInterface
function Invoke-MI([byte[]]$in) {
    $r = Invoke-CimMethod -InputObject $c -MethodName MiInterface -Arguments @{ InData = [byte[]]$in }
    return $r.OutData
}
function Get-Fn($fn) {
    $in = New-Object byte[] 32; $in[1] = 250; $in[3] = $fn
    return (Invoke-MI $in)
}
function Set-Fn($fn, $v) {
    $in = New-Object byte[] 32; $in[1] = 251; $in[3] = $fn; $in[4] = $v
    Invoke-MI $in | Out-Null
}

Write-Host ("当前 fn16 模式 = " + (Get-Fn 16)[4])
$o = Get-Fn 17
Write-Host ("当前颜色 = R:{0} G:{1} B:{2}" -f $o[4], $o[5], $o[6])
Write-Host ("当前 fn18 亮度 = " + (Get-Fn 18)[4])

# 写纯绿色固定色
Set-Fn 16 2
Start-Sleep -Milliseconds 600
$in = New-Object byte[] 32; $in[1] = 251; $in[3] = 17; $in[4] = 0; $in[5] = 255; $in[6] = 0
Invoke-MI $in | Out-Null
Start-Sleep -Milliseconds 500
$o2 = Get-Fn 17
Write-Host ("写入绿色后读回 = R:{0} G:{1} B:{2}   （如果还是旧颜色说明写入被拒）" -f $o2[4], $o2[5], $o2[6])

# 亮度降到 1 再恢复 3（肉眼可验证）
Set-Fn 18 1
Start-Sleep -Milliseconds 400
Write-Host ("亮度写1后读回 = " + (Get-Fn 18)[4] + "   （键盘应变暗）")
Set-Fn 18 3
Start-Sleep -Milliseconds 400
Write-Host ("亮度写3后读回 = " + (Get-Fn 18)[4] + "   （键盘应变亮）")
