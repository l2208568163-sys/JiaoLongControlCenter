$log = "F:\JiaoLongControlCenter\_work\kill_result.txt"
"start $(Get-Date -Format HH:mm:ss)" | Out-File $log -Encoding utf8
foreach ($p in Get-Process CCUWinUI -ErrorAction SilentlyContinue) {
    try {
        $p.Kill()
        $p.WaitForExit(3000) | Out-Null
        "killed $($p.Id)" | Out-File $log -Append -Encoding utf8
    } catch {
        "FAILED $($p.Id): $($_.Exception.Message)" | Out-File $log -Append -Encoding utf8
    }
}
Start-Sleep -Seconds 2
$left = (Get-Process CCUWinUI -ErrorAction SilentlyContinue | Measure-Object).Count
"remaining: $left" | Out-File $log -Append -Encoding utf8
