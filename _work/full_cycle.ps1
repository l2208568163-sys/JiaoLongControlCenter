# LogoMod 全流程：构建 → 拷贝 → 打包 → 签名 → 分发 → 重装 → 启动 → 读日志
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root  = "F:\JiaoLongControlCenter"
$lib   = "$root\_work\logomod\bin\Release\net8.0-windows10.0.22621.0\JiaoLongLogoMod.dll"
$app2  = "$root\_work\msix_patch\app2"
$kit   = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
$work  = "$root\_work\msix_patch"
$dist  = "$root\Port\dist\JiaoLongCCU-Port-v1.0\payload"
$pfxPwd = "JiaoLongCCU-Port-2026"

Write-Host "==> build"
& dotnet build "$root\_work\logomod\JiaoLongLogoMod.csproj" -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "==> stop app, copy dll"
Get-Process CCUWinUI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Copy-Item $lib "$app2\JiaoLongLogoMod.dll" -Force
$h1 = (Get-FileHash $lib -Algorithm SHA256).Hash
$h2 = (Get-FileHash "$app2\JiaoLongLogoMod.dll" -Algorithm SHA256).Hash
if ($h1 -ne $h2) { throw "copy mismatch" }
Write-Host "    dll hash: $($h1.Substring(0,16))"

Write-Host "==> manifest publisher check"
$mf = [IO.File]::ReadAllText("$app2\AppxManifest.xml")
if (-not $mf.Contains('Publisher="CN=JiaoLongCCU-Port"')) { throw "manifest publisher wrong" }

Write-Host "==> pack"
Remove-Item "$work\CCU.WinUI.ported.msix" -Force -ErrorAction SilentlyContinue
& "$kit\makeappx.exe" pack /d $app2 /p "$work\CCU.WinUI.ported.msix" /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

Write-Host "==> sign"
& "$kit\signtool.exe" sign /fd SHA256 /f "$work\JiaoLongCCU-Port.pfx" /p $pfxPwd "$work\CCU.WinUI.ported.msix" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }

Write-Host "==> distribute + verify package content"
Copy-Item "$work\CCU.WinUI.ported.msix" "$dist\CCU.WinUI.msix" -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead("$dist\CCU.WinUI.msix")
$entry = $zip.GetEntry("JiaoLongLogoMod.dll")
$sha = [System.Security.Cryptography.SHA256]::Create()
$inPkg = ([BitConverter]::ToString($sha.ComputeHash($entry.Open())).Replace("-","").Substring(0,16))
$zip.Dispose()
Write-Host "    dll in package: $inPkg"
if ($inPkg -ne $h1.Substring(0,16)) { throw "package contains stale dll!" }

Write-Host "==> remove + reinstall"
$pkg = Get-AppxPackage -Name "CCU.WinUI" -ErrorAction SilentlyContinue
if ($pkg) { Remove-AppxPackage -Package $pkg.PackageFullName; Start-Sleep -Seconds 2 }
Add-AppxPackage -Path "$dist\CCU.WinUI.msix"
Start-Sleep -Seconds 2
$inst = "$env:ProgramFiles\WindowsApps\CCU.WinUI_5.56.60.34_x64__fkb4sgs34zp4p\JiaoLongLogoMod.dll"
$h3 = (Get-FileHash $inst -Algorithm SHA256).Hash
Write-Host "    installed dll hash: $($h3.Substring(0,16))"
if ($h3 -ne $h1) { throw "installed dll is stale!" }

Write-Host "==> launch"
Remove-Item "C:\ProgramData\JiaoLongLogoMod\log.txt" -Force -ErrorAction SilentlyContinue
Start-Process "explorer.exe" "shell:AppsFolder\CCU.WinUI_fkb4sgs34zp4p!App"
Start-Sleep -Seconds 20
Write-Host "==> log summary"
Get-Content "C:\ProgramData\JiaoLongLogoMod\log.txt" -ErrorAction SilentlyContinue |
    Select-String -Pattern "init|poll|tree ready|chosen|HIT|hit-test|logo center|xamlroot|press|tapped|TRIGGER|picker|loaded" |
    ForEach-Object { $_.Line }
