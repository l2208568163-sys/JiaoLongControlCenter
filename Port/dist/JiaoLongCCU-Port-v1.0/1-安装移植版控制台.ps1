#Requires -RunAsAdministrator
# ============================================================
#  蛟龙16 Pro 2023 (MRID6) — 机械革命控制中心 5.56 移植版安装脚本
#  内容：桥接服务 JiaoLongCCUBridge + 官方 UI (CCU.WinUI.msix)
#  卸载：运行 2-卸载移植版控制台.ps1
# ============================================================
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$Payload  = Join-Path $Root "payload"
$Target   = "C:\Program Files\JiaoLongCCUBridge"
$SvcName  = "JiaoLongCCUBridge"

function Step($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }

# ---------- 0. 结束旧实例 ----------
# 先优雅停服务（不会触发 SCM 失败恢复重启），再清理残留进程
Step "停止已有的桥接进程/服务"
if (Get-Service $SvcName -ErrorAction SilentlyContinue) {
    Stop-Service $SvcName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}
Get-Process JiaoLongCCUBridge -ErrorAction SilentlyContinue | Stop-Process -Force

# ---------- 1. 部署桥接服务 ----------
Step "部署桥接服务到 $Target"
New-Item -ItemType Directory -Force -Path $Target | Out-Null
foreach ($f in @("JiaoLongCCUBridge.exe","JiaoLongCCUBridge.pdb","KaronOC.dll","WinRing0x64.dll","WinRing0x64.sys","display_helper.py")) {
    $src = Join-Path $Payload $f
    if (-not (Test-Path $src)) { continue }
    try {
        Copy-Item $src (Join-Path $Target $f) -Force
    } catch {
        # 目标被占用（服务/内核驱动在跑）时：内容一致就跳过，不一致才算失败
        $dst = Join-Path $Target $f
        if (-not (Test-Path $dst) -or (Get-FileHash $src).Hash -ne (Get-FileHash $dst).Hash) { throw }
        Write-Host "    $f 被占用且内容一致，跳过"
    }
}

Step "注册 Windows 服务 ($SvcName)"
sc.exe create $SvcName binPath= "`"$Target\JiaoLongCCUBridge.exe`"" start= auto DisplayName= "JiaoLongCCUBridge" | Out-Null
sc.exe failure $SvcName reset= 86400 actions= restart/5000/restart/5000/restart/10000 | Out-Null
sc.exe start $SvcName | Out-Null
Start-Sleep -Seconds 2
$svc = Get-Service $SvcName
Write-Host "    服务状态: $($svc.Status)"

# ---------- 2. 注册表能力配置（服务启动时也会写，这里提前写好） ----------
Step "写入 GamingCenter2 注册表（能力开关/品牌）"
$def = "HKLM:\SOFTWARE\OEM\GamingCenter2"
$its = "$def\ItemSupport"
New-Item -Path $its -Force | Out-Null
Set-ItemProperty $def -Name "AppType"       -Value 1   -Type DWord
Set-ItemProperty $def -Name "APPTarget"     -Value "Customize" -Type String
Set-ItemProperty $def -Name "Language"      -Value "zh-cn" -Type String
Set-ItemProperty $def -Name "CustomizeTarget" -Value 56 -Type DWord
Set-ItemProperty $def -Name "AppName"       -Value "机械革命控制中心" -Type String
Set-ItemProperty $def -Name "LanguageList"  -Value "en-us,zh-cn" -Type String
Set-ItemProperty $its -Name "ServiceLoadingReady" -Value 1 -Type DWord
Set-ItemProperty $its -Name "BIOS_PROJECT_ID"     -Value "ID6" -Type String
Set-ItemProperty $its -Name "IsNvGpu"             -Value 1 -Type DWord
Set-ItemProperty $its -Name "RamFan1p5Support"    -Value 1 -Type DWord
Set-ItemProperty $its -Name "FanSettingsSupport"  -Value 1 -Type DWord
Set-ItemProperty $its -Name "TurboModeSupport"    -Value 1 -Type DWord
Set-ItemProperty $its -Name "OcSettingsSupport"   -Value 1 -Type DWord
Set-ItemProperty $its -Name "CPUPerformanceAndOverClockMenuSupport" -Value 1 -Type DWord
Set-ItemProperty $its -Name "KeyboardType"        -Value 1 -Type DWord
Set-ItemProperty $its -Name "DGpuDirectConnectionSupport" -Value 1 -Type DWord
Set-ItemProperty $its -Name "DispalyRefresh"      -Value 2 -Type DWord
Set-ItemProperty $its -Name "ColorCalibrationSupport" -Value 1 -Type DWord

# ---------- 3. 导入 UI 签名证书 ----------
# UI 包已打补丁（移除应用商店入口）并用自签证书 CN=JiaoLongCCU-Port 重签名
Step "导入 UI 签名证书 (JiaoLongCCU-Port)"
$cerPath = Join-Path $Payload "JiaoLongCCU-Port.cer"
Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Write-Host "    已导入 LocalMachine\TrustedPeople"

# ---------- 4. 安装运行时依赖（已装则跳过） ----------
Step "检查 WindowsAppRuntime 1.7 依赖"
$warr = Get-AppxPackage -Name "Microsoft.WindowsAppRuntime.1.7"
if (-not $warr) {
    Add-AppxProvisionedPackage -Online -PackagePath (Join-Path $Payload "Microsoft.WindowsAppRuntime.1.7.msix") -SkipLicense | Out-Null
    Write-Host "    已从 payload 安装 WindowsAppRuntime 1.7"
} else {
    Write-Host "    已安装: $($warr.Version)"
}

# ---------- 5. 安装官方 UI（CCU.WinUI.msix，已移除应用商店入口） ----------
Step "安装 机械革命控制中心 UI (CCU.WinUI 移植版)"
$old = Get-AppxPackage -Name "CCU.WinUI"
if ($old) {
    Write-Host "    检测到旧版本 $($old.Version)，先移除"
    Remove-AppxPackage -Package $old.PackageFullName -ErrorAction SilentlyContinue
}
Add-AppxPackage -Path (Join-Path $Payload "CCU.WinUI.msix")
$app = Get-AppxPackage -Name "CCU.WinUI"
Write-Host "    已安装: $($app.PackageFullName)"

# ---------- 6. 本地回环豁免（保险起见，全信任应用通常不需要） ----------
Step "添加 UWP 本地回环豁免"
$family = (Get-AppxPackage -Name "CCU.WinUI").PackageFamilyName
CheckNetIsolation LoopbackExempt -a -n="$family" 2>$null | Out-Null
Write-Host "    完成 ($family)"

# ---------- 7. 桌面快捷方式 ----------
Step "创建桌面快捷方式"
$appId = "$family!App"
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut("$env:PUBLIC\Desktop\机械革命控制中心.lnk")
$lnk.TargetPath = "explorer.exe"
$lnk.Arguments  = "shell:AppsFolder\$appId"
$lnk.IconLocation = (Join-Path $Payload "gamingcenter_STD.ico")
$lnk.Save()
Write-Host "    完成"

Write-Host "`n================ 安装完成 ================" -ForegroundColor Green
Write-Host "桌面快捷方式【机械革命控制中心】启动，或直接开始菜单搜索“机械革命控制中心”。"
Write-Host "如果首次启动卡在加载界面超过 60 秒："
Write-Host "  1) 重启服务: 以管理员运行  net stop JiaoLongCCUBridge && net start JiaoLongCCUBridge"
Write-Host "  2) 日志位置: C:\ProgramData\JiaoLongCCUBridge（服务） 与 C:\ProgramData\CCU_Gallery（UI）"
Write-Host "使用新版控制台时请先关闭旧的【第三方蛟龙游戏控制中心】。"
