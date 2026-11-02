# 卸载控制中心 UI（由 Inno Setup 卸载器在删除文件前调用）
$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Get-Process CCUWinUI -ErrorAction SilentlyContinue | Stop-Process -Force
Get-AppxPackage -Name "CCU.WinUI" -AllUsers | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
Get-AppxPackage -Name "CCU.WinUI" | Remove-AppxPackage -ErrorAction SilentlyContinue
Remove-Item "$env:PUBLIC\Desktop\机械革命控制中心.lnk" -Force -ErrorAction SilentlyContinue
# 注意：Microsoft.WindowsAppRuntime.1.7 为共享运行时，不随本应用卸载
exit 0
