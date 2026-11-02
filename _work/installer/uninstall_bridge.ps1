# 卸载桥接服务（由 Inno Setup 卸载器在删除文件前调用）
$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Stop-Service JiaoLongCCUBridge -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Service -Filter "Name='JiaoLongCCUBridge'" -ErrorAction SilentlyContinue | Remove-CimInstance
Get-Process JiaoLongCCUBridge -ErrorAction SilentlyContinue | Stop-Process -Force

# 程序目录与日志（{app} 由卸载器清理，这里清历史安装目录与运行数据）
Remove-Item "C:\Program Files\JiaoLongCCUBridge" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "C:\ProgramData\JiaoLongCCUBridge" -Recurse -Force -ErrorAction SilentlyContinue
exit 0
