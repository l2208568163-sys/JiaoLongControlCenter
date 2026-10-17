$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$msix = "F:\JiaoLongControlCenter\Port\dist\JiaoLongCCU-Port-v1.0\payload\CCU.WinUI.msix"

function Kill-App {
    Get-Process CCUWinUI -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

function Get-Pkg { Get-AppxPackage -Name "CCU.WinUI" -ErrorAction SilentlyContinue }

Kill-App
$pkg = Get-Pkg
if ($pkg) {
    Write-Host "==> removing old package $($pkg.PackageFullName)"
    Remove-AppxPackage -Package $pkg.PackageFullName
    Start-Sleep -Seconds 2
    if (Get-Pkg) { Write-Host "!!! STILL INSTALLED after remove" } else { Write-Host "    removed OK" }
} else {
    Write-Host "==> no old package"
}

Write-Host "==> installing $msix"
Add-AppxPackage -Path $msix
$after = Get-Pkg
Write-Host "    installed: $($after.PackageFullName)"

Kill-App
Write-Host "==> done (app killed; launch manually)"
