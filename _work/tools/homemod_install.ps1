$ErrorActionPreference = "Continue"
$r = @()
# 1. 关掉运行中的 UI（否则 Remove-AppxPackage 失败）
$ui = Get-Process CCUWinUI -ErrorAction SilentlyContinue
if ($ui) { $r += "killing CCUWinUI pids=$($ui.Id -join ',')"; $ui | Stop-Process -Force; Start-Sleep -Seconds 2 }
# 2. 安装（先显式卸载：同版本号 Add-AppxPackage 可能被静默跳过，导致旧 DLL 继续运行）
$old = Get-AppxPackage -Name "CCU.WinUI" -ErrorAction SilentlyContinue
if ($old) { Remove-AppxPackage -Package $old.PackageFullName }
& "F:\JiaoLongControlCenter\_work\msix_patch\install_sandbox_safe.ps1"
# 3. 启动新 UI
$family = (Get-AppxPackage -Name "CCU.WinUI").PackageFamilyName
Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$family!App"
Start-Sleep -Seconds 14
$p = Get-Process CCUWinUI -ErrorAction SilentlyContinue
if ($p) { $r += "CCUWinUI ALIVE pids=$($p.Id -join ',') mem=$([math]::Round(($p | Measure-Object WorkingSet64 -Sum).Sum/1MB,1))MB title='$($p.MainWindowTitle)'" } else { $r += "CCUWinUI NOT RUNNING" }
$r += "Bridge service: $((Get-Service JiaoLongCCUBridge).Status)"
$r | Out-File "C:\Users\xingye\homemod_install.txt" -Encoding utf8
