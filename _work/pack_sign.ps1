$ErrorActionPreference = "Stop"
$kit = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
$work = "F:\JiaoLongControlCenter\_work\msix_patch"
$pfxPwd = "JiaoLongCCU-Port-2026"   # 留存: 签名证书 PFX 密码

# ---------- 1. manifest Publisher -> CN=JiaoLongCCU-Port ----------
$mf = "$work\app2\AppxManifest.xml"
$txt = [IO.File]::ReadAllText($mf)
$oldPub = 'Publisher="CN=AISTONE GLOBAL (SUZHOU) LIMITED, O=AISTONE GLOBAL (SUZHOU) LIMITED, L=Suzhou, S=Jiangsu, C=CN, OID.1.3.6.1.4.1.311.60.2.1.1=Suzhou, OID.1.3.6.1.4.1.311.60.2.1.2=Jiangsu, OID.1.3.6.1.4.1.311.60.2.1.3=CN, SERIALNUMBER=91320594321285131E, OID.2.5.4.15=Private Organization"'
if ($txt.Contains($oldPub)) {
    $txt = $txt.Replace($oldPub, 'Publisher="CN=JiaoLongCCU-Port"')
    [IO.File]::WriteAllText($mf, $txt)
    Write-Host "==> manifest Publisher -> CN=JiaoLongCCU-Port"
} else {
    Write-Host "==> manifest Publisher already JiaoLongCCU-Port (or pattern not found)"
}

# ---------- 2. 重签自签证书（旧 pfx 密码丢失） ----------
$subj = "CN=JiaoLongCCU-Port"
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subj -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    Write-Host "==> creating new self-signed cert $subj"
    $cert = New-SelfSignedCertificate -Type Custom -Subject $subj `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
        -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 `
        -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(10)
}
Write-Host "    cert: $($cert.Thumbprint)  NotAfter=$($cert.NotAfter)"

$sec = ConvertTo-SecureString -String $pfxPwd -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "$work\JiaoLongCCU-Port.pfx" -Password $sec -Force | Out-Null
Export-Certificate -Cert $cert -FilePath "$work\JiaoLongCCU-Port.cer" -Force | Out-Null
Import-Certificate -FilePath "$work\JiaoLongCCU-Port.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Import-Certificate -FilePath "$work\JiaoLongCCU-Port.cer" -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
Write-Host "==> cert exported + imported to TrustedPeople"

# ---------- 3. makeappx pack ----------
Remove-Item "$work\CCU.WinUI.ported.msix" -Force -ErrorAction SilentlyContinue
Write-Host "==> makeappx pack"
& "$kit\makeappx.exe" pack /d "$work\app2" /p "$work\CCU.WinUI.ported.msix" /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed: $LASTEXITCODE" }
Write-Host "    packed OK"

# ---------- 4. signtool sign ----------
Write-Host "==> signtool sign"
& "$kit\signtool.exe" sign /fd SHA256 /f "$work\JiaoLongCCU-Port.pfx" /p $pfxPwd "$work\CCU.WinUI.ported.msix"
if ($LASTEXITCODE -ne 0) { throw "signtool failed: $LASTEXITCODE" }

& "$kit\signtool.exe" verify /pa "$work\CCU.WinUI.ported.msix"
Get-Item "$work\CCU.WinUI.ported.msix" | Format-List Name, Length, LastWriteTime
