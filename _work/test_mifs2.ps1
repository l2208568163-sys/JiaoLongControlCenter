$c = New-Object System.Management.ManagementClass('root\WMI','MICommonInterface',$null)
$m = $c.Methods['MiInterface']
foreach ($p in $m.InParametersObject.Properties) {
    Write-Host ("IN  name=" + $p.Name + " type=" + $p.Type)
}
foreach ($p in $m.OutParametersObject.Properties) {
    Write-Host ("OUT name=" + $p.Name + " type=" + $p.Type)
}
