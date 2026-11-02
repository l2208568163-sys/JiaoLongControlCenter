param([Parameter(Mandatory=$true)][string]$Root)
# ============================================================
# 控制中心 UI（MSIX）安装（由 Inno Setup 安装器在 [Run] 阶段调用）
# payload 已由安装器复制到 {app}\ui
# ============================================================
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$UI = Join-Path $Root "ui"

try {
    # 1. 杀运行中的 UI（不杀则 Remove-AppxPackage 失败）
    Get-Process CCUWinUI -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    # 2. 导入 UI 签名证书（补丁包由自签证书 CN=JiaoLongCCU-Port 重签名）
    Import-Certificate -FilePath (Join-Path $UI "JiaoLongCCU-Port.cer") -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

    # 3. 运行时依赖（已装则跳过）
    $warr = Get-AppxPackage -Name "Microsoft.WindowsAppRuntime.1.7" -ErrorAction SilentlyContinue
    if (-not $warr) {
        Add-AppxProvisionedPackage -Online -PackagePath (Join-Path $UI "Microsoft.WindowsAppRuntime.1.7.msix") -SkipLicense | Out-Null
    }

    # 4. UI 包（必须先显式卸载：同版本号 Add-AppxPackage 会被静默跳过，导致旧 DLL 继续运行）
    $old = Get-AppxPackage -Name "CCU.WinUI" -ErrorAction SilentlyContinue
    if ($old) {
        Remove-AppxPackage -Package $old.PackageFullName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    Add-AppxPackage -Path (Join-Path $UI "CCU.WinUI.msix")
    $app = Get-AppxPackage -Name "CCU.WinUI" -ErrorAction SilentlyContinue
    if (-not $app) { throw "CCU.WinUI not registered after Add-AppxPackage" }

    # 5. 本地回环豁免（尽力而为）
    try { CheckNetIsolation LoopbackExempt -a -n="$($app.PackageFamilyName)" 2>$null | Out-Null } catch { }

    # 6. 桌面快捷方式（PackageFamilyName 动态获取）
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut("$env:PUBLIC\Desktop\机械革命控制中心.lnk")
    $lnk.TargetPath = "explorer.exe"
    $lnk.Arguments  = "shell:AppsFolder\$($app.PackageFamilyName)!App"
    $lnk.IconLocation = (Join-Path $UI "gamingcenter_STD.ico")
    $lnk.Save()

    Write-Host "UI_OK $($app.PackageFullName)"
    exit 0
} catch {
    Write-Host "UI_FAIL $($_.Exception.Message)"
    exit 1
}
