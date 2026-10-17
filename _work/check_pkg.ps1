$pkgs = Get-AppxPackage | Where-Object { $_.Name -like '*CCU*' }
$pkgs | Format-List Name, PackageFullName, Publisher, InstallLocation, Version
