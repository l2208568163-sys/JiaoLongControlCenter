param([Parameter(Mandatory=$true)][string]$Root)
# ============================================================
# 桥接服务安装（由 Inno Setup 安装器在 [Run] 阶段调用）
# 文件已由安装器复制到 {app}\bridge，本脚本只负责服务注册与启动
# 注意：不使用 sc.exe（部分安全沙箱会拦截），删除服务走 CIM
# ============================================================
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$SvcName   = "JiaoLongCCUBridge"
$ExePath   = Join-Path $Root "bridge\JiaoLongCCUBridge.exe"
$LegacyDir = "C:\Program Files\JiaoLongCCUBridge"

try {
    if (-not (Test-Path $ExePath)) { throw "bridge exe missing: $ExePath" }

    # 0. 兜底停进程（PrepareToInstall 已停过服务，这里幂等重复）
    Get-Process JiaoLongCCUBridge -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    # 1. 服务已存在则删除（旧版可能指向 C:\Program Files\JiaoLongCCUBridge，binPath 变更必须重建）
    $svcObj = Get-CimInstance Win32_Service -Filter "Name='$SvcName'" -ErrorAction SilentlyContinue
    if ($svcObj) {
        $svcObj | Remove-CimInstance
        $waited = 0
        while ((Get-CimInstance Win32_Service -Filter "Name='$SvcName'" -ErrorAction SilentlyContinue) -and ($waited -lt 10)) {
            Start-Sleep -Seconds 1; $waited++
        }
    }

    # 2. 注册服务（binPath 带引号，防止路径含空格）
    New-Service -Name $SvcName -BinaryPathName ('"' + $ExePath + '"') -StartupType Automatic -DisplayName "JiaoLongCCUBridge" | Out-Null

    # 3. 失败恢复动作：reset=86400s，三次动作均为重启，延迟 5s/5s/10s（sc.exe failure 等价注册表写法）
    $fa = [byte[]]@(0x80,0x51,0x01,0x00, 0,0,0,0, 0,0,0,0, 0x03,0,0,0,
                    1,0,0,0, 0x88,0x13,0,0,  1,0,0,0, 0x88,0x13,0,0,  1,0,0,0, 0x10,0x27,0,0)
    try {
        Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$SvcName" -Name "FailureActions" -Value $fa -Type Binary -ErrorAction Stop
    } catch { }

    # 4. 启动
    Start-Service $SvcName
    Start-Sleep -Seconds 2
    $st = (Get-Service $SvcName -ErrorAction Stop).Status

    # 5. 清理历史安装目录（老版本 PS1 安装位置；文件被占用则留给卸载流程清理）
    if (Test-Path $LegacyDir) {
        try { Remove-Item $LegacyDir -Recurse -Force -ErrorAction Stop } catch { }
    }

    if ($st -ne "Running") { throw "service status: $st" }
    Write-Host "SERVICE_OK $st"
    exit 0
} catch {
    Write-Host "SERVICE_FAIL $($_.Exception.Message)"
    exit 1
}
