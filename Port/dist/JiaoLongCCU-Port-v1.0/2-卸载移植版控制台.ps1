#Requires -RunAsAdministrator
# 蛟龙16 Pro 2023 移植版控制台卸载脚本
$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Step($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }

Step "移除 CCU.WinUI 应用"
Get-AppxPackage -Name "CCU.WinUI" -AllUsers | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
Get-AppxPackage -Name "CCU.WinUI" | Remove-AppxPackage -ErrorAction SilentlyContinue

Step "停止并删除桥接服务"
Stop-Service JiaoLongCCUBridge -Force -ErrorAction SilentlyContinue
sc.exe delete JiaoLongCCUBridge | Out-Null
Get-Process JiaoLongCCUBridge -ErrorAction SilentlyContinue | Stop-Process -Force

Step "清理注册表"
Remove-Item "HKLM:\SOFTWARE\OEM\GamingCenter2" -Recurse -Force -ErrorAction SilentlyContinue

Step "删除程序目录"
Remove-Item "C:\Program Files\JiaoLongCCUBridge" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:PUBLIC\Desktop\机械革命控制中心.lnk" -Force -ErrorAction SilentlyContinue

Step "清理数据（可选保留）"
Remove-Item "C:\ProgramData\JiaoLongCCUBridge" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n卸载完成。注意：不会动官方 UWACPIDriver/GCUBridge（如果你曾装过官方 GX 版，请用它的卸载器清理）。" -ForegroundColor Green
